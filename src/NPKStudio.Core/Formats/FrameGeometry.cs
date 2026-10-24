using NPKStudio.Core.Formats.Handlers;
using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Formats;

/// <summary>
///     帧的几何编辑（位置 / 尺寸）。
///     <para>
///         与结构编辑（增删/移动，见 <see cref="FrameTable" />）分开：几何只改"这一帧自己"，不触碰帧表顺序；
///         结构编辑会牵动下标与 LINK 关系。两者混在一起最容易写出"改了位置却忘了重排下标"的 bug。
///     </para>
/// </summary>
internal static class FrameGeometry
{
    /// <summary>改帧在角色坐标系中的位置。<b>只改索引表字段，不碰像素</b>，因此 O(1) 且零分配。</summary>
    /// <param name="file">帧所属的 IMG（用于标记所属 NPK 条目已修改）。</param>
    /// <param name="frame">目标帧。</param>
    /// <param name="x">新的 X，允许为负（DNF 的帧常带负偏移）。</param>
    /// <param name="y">新的 Y，允许为负。</param>
    /// <returns>是否有变化。</returns>
    internal static bool SetPosition(ImgFile file, ImgFrame frame, int x, int y)
    {
        if (frame.X == x && frame.Y == y)
        {
            return false;
        }

        frame.X = x;
        frame.Y = y;
        file.MarkModified();
        return true;
    }

    /// <summary>
    ///     把一帧的像素缩放到指定尺寸。
    ///     <para>
    ///         <b>为什么必须重采样</b>：DNF 的帧宽高与解码后的字节数强绑定
    ///         （<c>Width × Height × 每像素字节</c>），只改索引表里的 W/H 会写出解不出来的文件。
    ///         解码→缩放→按原像素格式重编码，保证不变量成立。
    ///     </para>
    ///     <para>帧域（<see cref="ImgFrame.FrameWidth" />/Height）只增不减，避免放大后被画布裁掉。</para>
    /// </summary>
    /// <param name="file">帧所属的 IMG。</param>
    /// <param name="frame">目标帧，不能是 LINK 帧。</param>
    /// <param name="width">新宽度，<c>1..8192</c>。</param>
    /// <param name="height">新高度，<c>1..8192</c>。</param>
    /// <returns>是否有变化。</returns>
    /// <exception cref="ArgumentOutOfRangeException">尺寸越界。</exception>
    /// <exception cref="InvalidOperationException">LINK 帧没有自己的像素，或解码失败。</exception>
    internal static bool Resize(ImgFile file, ImgFrame frame, int width, int height)
    {
        if (width < 1 || width > ImgHandlerBase.MaxDimension ||
            height < 1 || height > ImgHandlerBase.MaxDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), $"{width}x{height}", $"尺寸必须在 1..{ImgHandlerBase.MaxDimension} 之间。");
        }

        if (frame.IsLink)
        {
            throw new InvalidOperationException($"帧 #{frame.Index} 是 LINK 帧，没有自己的像素，无法缩放。");
        }

        if (frame.Width == width && frame.Height == height)
        {
            return false;
        }

        var current = file.Decode(frame)
            ?? throw new InvalidOperationException($"帧 #{frame.Index} 解码失败，无法缩放（版本 {file.Version}）。");

        var resized = current.Resize(width, height);

        // 统一出口：v1/v2/v4/v6 写 RawData，v5 落成新纹理页（不能只改索引字段，否则解不出来）。
        FramePixelEncoder.WritePixels(file, frame, resized);
        frame.Width = width;
        frame.Height = height;
        frame.FrameWidth = Math.Max(frame.FrameWidth, width);
        frame.FrameHeight = Math.Max(frame.FrameHeight, height);
        file.MarkModified();
        return true;
    }
}
