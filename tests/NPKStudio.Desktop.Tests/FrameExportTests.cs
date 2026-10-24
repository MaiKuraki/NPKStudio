using System.Text;

using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;
using NPKStudio.Desktop.Services;
using NPKStudio.Desktop.ViewModels;

using Avalonia.Headless.XUnit;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     批量 PNG / GIF 导出的验收：文件名口径、单帧失败不中断、GIF89a 头与合成后的画布尺寸。
///     <para>PNG 编码要在 UI 线程创建 <c>WriteableBitmap</c>，所以整类用 <see cref="AvaloniaFactAttribute" />。</para>
/// </summary>
public sealed class FrameExportTests
{
    /// <summary>批量 PNG：每帧一个目标文件，名字是 <c>{前缀}_{Index:D3}.png</c>，像素是原始解码结果。</summary>
    [AvaloniaFact]
    public void PngBatch_PlanNamesOneFilePerFrame()
    {
        using var folder = TestFolder.Create();
        var image = BuildImage(3, 4, 4, offsetStep: 1);
        var frames = ToViewModels(image);

        var plan = FrameExportService.PlanPngBatch(image, frames, folder.Path, "walk");

        Assert.Equal(0, plan.Failed);
        Assert.Null(plan.FirstError);
        Assert.Equal(3, plan.Items.Count);
        for (var i = 0; i < plan.Items.Count; i++)
        {
            var item = plan.Items[i];
            Assert.Equal(Path.Combine(folder.Path, $"walk_{i:D3}.png"), item.Path);
            Assert.Equal(4, item.Pixels.Width);
            Assert.Equal(4, item.Pixels.Height);
        }
    }

    /// <summary>批量 PNG：中间一帧解码失败不中断其余帧，计划里给出"2 成功 / 1 失败"。</summary>
    [AvaloniaFact]
    public void PngBatch_PlanContinuesAfterCorruptFrame()
    {
        using var folder = TestFolder.Create();
        var image = BuildImage(3, 4, 4, offsetStep: 1);
        image.Frames[1].RawData = [0x00]; // 非法 zlib 数据 → 解码必然失败
        var frames = ToViewModels(image);

        var plan = FrameExportService.PlanPngBatch(image, frames, folder.Path, "walk");

        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(1, plan.Failed);
        Assert.False(string.IsNullOrWhiteSpace(plan.FirstError));
        Assert.Equal("walk_000.png", Path.GetFileName(plan.Items[0].Path));
        Assert.Equal("walk_002.png", Path.GetFileName(plan.Items[1].Path));
    }

    /// <summary>GIF：写出 GIF89a，逻辑屏尺寸 = 各帧按 X/Y 合成后的联合包围盒。</summary>
    [AvaloniaFact]
    public async Task GifExport_CompositesFramesOntoUnionCanvas()
    {
        using var folder = TestFolder.Create();
        var image = BuildImage(2, 4, 4, offsetStep: 2);
        var frames = ToViewModels(image);
        var path = Path.Combine(folder.Path, "walk.gif");

        var result = await new FrameExportService().ExportGifAsync(image, frames, path, 100);

        Assert.True(result.Success, result.StatusMessage);
        Assert.True(File.Exists(path));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal("GIF89a", Encoding.ASCII.GetString(bytes, 0, 6));
        Assert.Equal(6, bytes[6] | (bytes[7] << 8));   // 4 + 2 偏移 = 6
        Assert.Equal(4, bytes[8] | (bytes[9] << 8));
        Assert.Equal(0x3B, bytes[^1]);
    }

    /// <summary>GIF：一帧都解不出来时如实失败，不写空文件。</summary>
    [AvaloniaFact]
    public async Task GifExport_AllLinkFrames_FailsWithoutFile()
    {
        using var folder = TestFolder.Create();
        var image = BuildImage(2, 4, 4, offsetStep: 1);
        foreach (var frame in image.Frames)
        {
            frame.Type = ColorBits.LINK;
            frame.RawData = [];
            frame.Length = 0;
        }

        var path = Path.Combine(folder.Path, "empty.gif");
        var result = await new FrameExportService().ExportGifAsync(image, ToViewModels(image), path, 100);

        Assert.False(result.Success);
        Assert.False(File.Exists(path));
    }

    private static List<FrameViewModel> ToViewModels(ImgFile image)
    {
        var list = new List<FrameViewModel>(image.Frames.Count);
        foreach (var frame in image.Frames)
        {
            list.Add(new FrameViewModel(frame));
        }

        return list;
    }

    /// <summary>构造 v2 + ARGB1555 + ZLIB 的合成 IMG（像素可解码，尺寸固定）。</summary>
    private static ImgFile BuildImage(int frameCount, int width, int height, int offsetStep)
    {
        var image = ImgFile.Create(ImgVersion.Ver2, "export.img");
        for (var i = 0; i < frameCount; i++)
        {
            var frame = image.AddFrame();
            frame.Type = ColorBits.ARGB_1555;
            frame.Compress = CompressMode.ZLIB;
            frame.Width = width;
            frame.Height = height;
            frame.FrameWidth = width;
            frame.FrameHeight = height;
            frame.X = i * offsetStep;
            frame.Y = 0;

            var raw = new byte[width * height * 2];
            for (var k = 0; k < raw.Length; k++)
            {
                raw[k] = (byte)((i * 23) + (k * 5));
            }

            frame.RawData = ZlibCodec.Compress(raw);
            frame.Length = frame.RawData.Length;
        }

        return image;
    }
}
