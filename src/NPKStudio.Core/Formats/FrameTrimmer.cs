using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Formats;

/// <summary>
///     帧裁剪（REQ-11）：裁掉四周全透明边，并把裁剪偏移<b>累加</b>到帧坐标上。
///     <para>
///         <b>绝对位置不变是唯一目的</b>：DNF 按每帧的 X/Y 把贴图贴到角色坐标系，
///         所以裁掉左上角 (bx, by) 个透明像素后必须 <c>X += bx</c>、<c>Y += by</c>，
///         否则贴图会整体向左上"跳"一段。
///     </para>
///     <para>
///         全透明帧不裁（否则会得到 0×0 的非法帧）；紧包围盒不裁（不做无谓的重编码与画质损失）。
///     </para>
/// </summary>
internal static class FrameTrimmer
{
    /// <summary>
    ///     裁剪一帧。调用方（<see cref="ImgFile.TrimFrame" />）已校验归属。
    /// </summary>
    /// <param name="file">帧所属的 IMG（用于解码与标脏）。</param>
    /// <param name="frame">目标帧。</param>
    /// <returns>是否真的裁了。</returns>
    /// <exception cref="InvalidOperationException">LINK 帧没有自己的像素，或解码失败。</exception>
    internal static bool Trim(ImgFile file, ImgFrame frame)
    {
        if (frame.IsLink)
        {
            throw new InvalidOperationException($"帧 #{frame.Index} 是 LINK 帧，没有自己的像素，无法裁剪。");
        }

        var image = file.Decode(frame)
            ?? throw new InvalidOperationException($"帧 #{frame.Index} 解码失败，无法裁剪（版本 {file.Version}）。");

        // alpha > 0 才算内容；全透明帧直接放弃（D-03：绝不产生 0 尺寸帧）。
        if (!image.ScanBounds(out var bounds))
        {
            return false;
        }

        // 已经是紧包围盒：裁了也不会变小，不做无谓重编码（会损失画质、改变压缩字节）。
        if (bounds.X == 0 && bounds.Y == 0 && bounds.Width == image.Width && bounds.Height == image.Height)
        {
            return false;
        }

        var cropped = image.Crop(bounds);

        // 统一出口：v1/v2/v4/v6 按原像素格式重编码（v4/v6 走色号量化），v5 落成新纹理页。
        FramePixelEncoder.WritePixels(file, frame, cropped);

        // 坐标是累加而非赋值：多次裁剪/原本就有负坐标时仍然保持绝对位置。
        frame.X += bounds.X;
        frame.Y += bounds.Y;
        frame.Width = cropped.Width;
        frame.Height = cropped.Height;

        // 与 ResizeFrame 同口径：帧域只增不减，避免裁完被画布更小的值裁掉。
        frame.FrameWidth = Math.Max(frame.FrameWidth, cropped.Width);
        frame.FrameHeight = Math.Max(frame.FrameHeight, cropped.Height);

        file.MarkModified();
        return true;
    }
}
