using System.Runtime.InteropServices;

using Avalonia.Media.Imaging;

using NPKStudio.Core.Imaging;

namespace NPKStudio.Desktop.Services;

/// <summary>
///     把 <see cref="RgbaImage" /> 的 BGRA 像素写进 <see cref="WriteableBitmap" />。
///     <para>
///         原先这段"逐行 <c>Marshal.Copy</c>"在 <see cref="Controls.ImageCanvas" />、
///         <see cref="Controls.FrameThumbnail" />、<see cref="ImageIo" /> 里各抄了一份。
///         抽出来顺带做了一件正事：<see cref="RgbaImage.Stride" /> 恒等于 <c>Width * 4</c>，
///         当位图的 <c>RowBytes</c> 与之相同（绝大多数情况）时<b>整块一次拷贝</b>即可，
///         不必按行发 554 次 P/Invoke（609×554 的帧、32fps 播放 = 每秒 1.7 万次）。
///     </para>
/// </summary>
internal static class BgraBitmapWriter
{
    /// <summary>
    ///     把 <paramref name="image" /> 的像素写入 <paramref name="bitmap" />（尺寸必须匹配）。
    /// </summary>
    /// <param name="image">源图像（BGRA8888，行优先）。</param>
    /// <param name="bitmap">目标位图，像素尺寸必须与 <paramref name="image" /> 一致。</param>
    /// <exception cref="ArgumentNullException">任一参数为 <c>null</c>。</exception>
    /// <exception cref="ArgumentException">位图尺寸与图像尺寸不一致。</exception>
    public static void Write(RgbaImage image, WriteableBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(bitmap);

        var size = bitmap.PixelSize;
        if (size.Width != image.Width || size.Height != image.Height)
        {
            throw new ArgumentException(
                $"位图尺寸 {size.Width}x{size.Height} 与图像 {image.Width}x{image.Height} 不一致。", nameof(bitmap));
        }

        using var fb = bitmap.Lock();
        if (fb.RowBytes == image.Stride)
        {
            // 最常见路径：stride 相同 → 一次整块拷贝
            Marshal.Copy(image.Pixels, 0, fb.Address, image.Pixels.Length);
            return;
        }

        // 兜底：位图行跨度带 padding 时逐行拷
        var rowBytes = image.Stride;
        for (var y = 0; y < image.Height; y++)
        {
            Marshal.Copy(image.Pixels, y * rowBytes, IntPtr.Add(fb.Address, y * fb.RowBytes), rowBytes);
        }
    }
}
