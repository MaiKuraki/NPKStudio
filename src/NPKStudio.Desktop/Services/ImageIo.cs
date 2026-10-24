using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NPKStudio.Core.Imaging;

namespace NPKStudio.Desktop.Services;

/// <summary>
///     PNG 与 <see cref="RgbaImage" /> 之间的互转。
///     <para>解码可在后台线程调用；<b>编码必须在 UI 线程</b>（内部会创建 <see cref="WriteableBitmap" />）。</para>
/// </summary>
public static class ImageIo {
    /// <summary>从 PNG/BMP/JPG 文件解码为 BGRA8888。</summary>
    public static RgbaImage Load(string path) {
        using var stream = File.OpenRead(path);
        using var bitmap = new Bitmap(stream);
        return FromBitmap(bitmap);
    }

    /// <summary>把任意 Avalonia 位图转成 <see cref="RgbaImage" />，必要时做 R/B 交换。</summary>
    public static RgbaImage FromBitmap(Bitmap bitmap) {
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        var buffer = new byte[width * height * 4];

        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(),
                buffer.Length, width * 4);
        } finally {
            handle.Free();
        }

        // Avalonia 的解码结果可能是 RGBA，统一换成 BGRA
        if (bitmap.Format == PixelFormat.Rgba8888) {
            for (var i = 0; i < buffer.Length; i += 4) {
                (buffer[i], buffer[i + 2]) = (buffer[i + 2], buffer[i]);
            }
        }

        return RgbaImage.FromBgra(buffer, width, height);
    }

    /// <summary>把 <see cref="RgbaImage" /> 保存为 PNG。必须在 UI 线程调用。</summary>
    public static void SavePng(RgbaImage image, string path) {
        using var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);

        BgraBitmapWriter.Write(image, bitmap);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }

        bitmap.Save(path);
    }
}
