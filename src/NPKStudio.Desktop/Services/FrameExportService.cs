using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;
using NPKStudio.Core.Imaging.Gif;
using NPKStudio.Desktop.Localization;
using NPKStudio.Desktop.ViewModels;

namespace NPKStudio.Desktop.Services;

/// <summary>PNG 批量导出计划里的一项：目标路径 + 已解码的原始像素。</summary>
/// <param name="Path">完整输出路径。</param>
/// <param name="Pixels">该帧的原始解码结果（不含画布效果）。</param>
internal readonly record struct PngBatchItem(string Path, RgbaImage Pixels);

/// <summary>
///     PNG 批量导出的<b>计划</b>：解码成功待写的项，加上解码失败的统计。
///     <para>
///         把"解码 + 命名"和"真正写盘"分开，是为了让命名口径与"单帧失败不中断"能被单测覆盖——
///         Avalonia 的 <c>WriteableBitmap.Save</c> 在 headless 平台上是空实现，写盘本身无法在单测里断言。
///     </para>
/// </summary>
/// <param name="Items">解码成功、等待写盘的项（顺序与选中帧一致）。</param>
/// <param name="Failed">解码失败的帧数。</param>
/// <param name="FirstError">第一个解码失败的错误信息；没有失败时为 <c>null</c>。</param>
internal sealed record PngBatchPlan(IReadOnlyList<PngBatchItem> Items, int Failed, string? FirstError);

/// <summary>
///     帧序列的导出：批量 PNG 与动画 GIF。
///     <para>
///         <b>导出的永远是原始解码像素</b>（不经画布效果 / 叠加预览），与「导出当前帧」口径一致；
///         批量命名用帧自己的 <see cref="ImgFrame.Index" />，因此与界面上的 <c>#003</c> 一一对应。
///     </para>
///     <para>
///         PNG 走 <see cref="ImageIo.SavePng" />（内部创建 <c>WriteableBitmap</c>），<b>必须在 UI 线程调用</b>；
///         GIF 只做纯托管解码 + 自实现 LZW，可以整体放进后台线程。
///     </para>
/// </summary>
internal sealed class FrameExportService
{
    private static TranslationCatalog Loc => TranslationCatalog.Instance;

    /// <summary>
    ///     把多帧批量导出为独立 PNG（<c>{baseName}_{index:D3}.png</c>）。
    ///     <para>单帧失败<b>不中断</b>整体流程：结束时汇总成功 / 失败数量与首个错误。</para>
    /// </summary>
    /// <param name="image">帧所属 IMG（用于解码）。</param>
    /// <param name="frames">要导出的帧（顺序即写入顺序）。</param>
    /// <param name="directory">输出目录（不存在时自动创建）。</param>
    /// <param name="baseName">文件名前缀，通常是 IMG 名。</param>
    /// <returns>成功与否 + 状态栏文案。</returns>
    public OperationResult ExportPngBatch(
        ImgFile image, IReadOnlyList<FrameViewModel> frames, string directory, string baseName)
    {
        if (frames.Count == 0)
        {
            return new OperationResult(false, Loc["status.pngBatchNoTarget"]);
        }

        var plan = PlanPngBatch(image, frames, directory, baseName);
        var succeeded = 0;
        var failed = plan.Failed;
        var firstError = plan.FirstError;

        if (plan.Items.Count > 0)
        {
            Directory.CreateDirectory(directory);
        }

        foreach (var item in plan.Items)
        {
            try
            {
                ImageIo.SavePng(item.Pixels, item.Path);
                succeeded++;
            }
            catch (Exception ex)
            {
                failed++;
                firstError ??= ex.Message;
            }
        }

        return failed == 0
            ? new OperationResult(true, Loc.F("status.pngBatchOk", succeeded, directory))
            : new OperationResult(succeeded > 0, Loc.F("status.pngBatchPartial", succeeded, failed, firstError));
    }

    /// <summary>
    ///     只做"解码 + 计算目标文件名"，不碰磁盘（写盘由 <see cref="ExportPngBatch" /> 负责）。
    /// </summary>
    /// <param name="image">帧所属 IMG。</param>
    /// <param name="frames">要导出的帧。</param>
    /// <param name="directory">输出目录。</param>
    /// <param name="baseName">文件名前缀；空白时退化为 <c>frame</c>。</param>
    /// <returns>导出计划。</returns>
    internal static PngBatchPlan PlanPngBatch(
        ImgFile image, IReadOnlyList<FrameViewModel> frames, string directory, string baseName)
    {
        var prefix = string.IsNullOrWhiteSpace(baseName) ? "frame" : baseName;
        var items = new List<PngBatchItem>(frames.Count);
        var failed = 0;
        string? firstError = null;

        foreach (var item in frames)
        {
            try
            {
                var pixels = image.Decode(item.Frame)
                    ?? throw new InvalidOperationException(Loc.F("status.pngBatchLinkFrame", item.IndexText));

                items.Add(new PngBatchItem(Path.Combine(directory, $"{prefix}_{item.Frame.Index:D3}.png"), pixels));
            }
            catch (Exception ex)
            {
                failed++;
                firstError ??= ex.Message;
            }
        }

        return new PngBatchPlan(items, failed, firstError);
    }

    /// <summary>
    ///     把帧序列编码成动画 GIF（无限循环，每帧固定延时）。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>按帧坐标合成到同一画布</b>：DNF 的帧各自带 <c>X/Y</c>（可负），直接逐帧写在 <c>(0,0)</c>
    ///         会让动画整体漂移；这里先取所有可解码帧的联合包围盒，再把每帧贴到
    ///         <c>(X - minX, Y - minY)</c>。
    ///     </para>
    ///     <para>LINK 帧没有自己的像素，直接跳过；若一帧都解不出来则如实报错，不写出空 GIF。</para>
    /// </remarks>
    /// <param name="image">帧所属 IMG。</param>
    /// <param name="frames">要编码的帧（顺序即动画顺序）。</param>
    /// <param name="path">输出 GIF 路径。</param>
    /// <param name="delayMilliseconds">每帧延时（毫秒），由 GIF 编码器夹到 20..1000。</param>
    /// <returns>成功与否 + 状态栏文案。</returns>
    public Task<OperationResult> ExportGifAsync(
        ImgFile image, IReadOnlyList<FrameViewModel> frames, string path, int delayMilliseconds = 100)
    {
        // 解码 + LZW 都是纯托管计算，不碰 UI；帧数多时也不会卡住界面线程。
        return Task.Run(() => ExportGif(image, frames, path, delayMilliseconds));
    }

    private static OperationResult ExportGif(
        ImgFile image, IReadOnlyList<FrameViewModel> frames, string path, int delayMilliseconds)
    {
        if (frames.Count == 0)
        {
            return new OperationResult(false, Loc["status.pngBatchNoTarget"]);
        }

        try
        {
            var decoded = new List<(RgbaImage Pixels, int X, int Y)>(frames.Count);
            foreach (var item in frames)
            {
                if (item.Frame.IsLink)
                {
                    continue;
                }

                var pixels = image.Decode(item.Frame);
                if (pixels is not null)
                {
                    decoded.Add((pixels, item.Frame.X, item.Frame.Y));
                }
            }

            if (decoded.Count == 0)
            {
                return new OperationResult(false, Loc["status.gifNoPixels"]);
            }

            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;
            foreach (var (pixels, x, y) in decoded)
            {
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x + pixels.Width);
                maxY = Math.Max(maxY, y + pixels.Height);
            }

            var width = maxX - minX;
            var height = maxY - minY;

            // GIF 的逻辑屏是 16 位无符号：先挡掉，避免按 65535² 分配画布直接 OOM。
            if (width <= 0 || height <= 0 || width > ushort.MaxValue || height > ushort.MaxValue)
            {
                return new OperationResult(false, Loc.F("status.gifFailed", $"{width}x{height} > 65535"));
            }

            var canvases = new List<RgbaImage>(decoded.Count);
            foreach (var (pixels, x, y) in decoded)
            {
                canvases.Add(Composite(pixels, width, height, x - minX, y - minY));
            }

            var bytes = GifEncoder.Encode(canvases, delayMilliseconds, loopCount: 0);

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, bytes);
            return new OperationResult(true, Loc.F("status.gifExported", Path.GetFileName(path), canvases.Count, delayMilliseconds));
        }
        catch (Exception ex)
        {
            return new OperationResult(false, Loc.F("status.gifFailed", ex.Message));
        }
    }

    /// <summary>把一张图贴到全透明画布的指定位置（直接按字节拷贝，不做 alpha 混合）。</summary>
    private static RgbaImage Composite(RgbaImage source, int canvasWidth, int canvasHeight, int left, int top)
    {
        var canvas = new RgbaImage(canvasWidth, canvasHeight);
        var sourceStride = source.Stride;

        for (var row = 0; row < source.Height; row++)
        {
            var targetOffset = ((top + row) * canvasWidth + left) * 4;
            Buffer.BlockCopy(source.Pixels, row * sourceStride, canvas.Pixels, targetOffset, sourceStride);
        }

        return canvas;
    }
}
