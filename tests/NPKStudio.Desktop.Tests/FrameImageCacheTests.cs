using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;
using NPKStudio.Desktop.ViewModels.Channels;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     帧位图缓存的两条硬约束：<b>命中不分配</b>、<b>内容变了必须失效</b>。
///     <para>
///         这两条是"播放不卡"的全部依据：只要命中还会分配，播放照样撞 LOH；
///         只要失效不可靠，画布就会显示编辑前的旧像素。
///     </para>
/// </summary>
public sealed class FrameImageCacheTests
{
    /// <summary>缓存命中路径必须接近零分配（复用同一张图，不重新解码）。</summary>
    [Fact]
    public void CacheHit_DoesNotAllocatePerFrame()
    {
        var image = BuildImage(64, 64);
        var frame = image.Frames[0];
        var cache = new FrameImageCache(static source => source);

        // 首次：真解码（分配 W×H×2 + W×H×4）
        var first = cache.GetOrDecode(image, frame, useDisplayEffect: false, cache.Generation);
        Assert.NotNull(first);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++)
        {
            var hit = cache.GetOrDecode(image, frame, useDisplayEffect: false, cache.Generation);
            Assert.Same(first!.Value.Raw, hit!.Value.Raw);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 4096,
            $"200 次缓存命中分配了 {allocated} 字节（期望 < 4096，即每次 < 20 字节）。");
    }

    /// <summary>换了素材（RawData 引用变化）之后必须重新解码，不能返回旧像素。</summary>
    [Fact]
    public void ReplacingPixels_InvalidatesCachedBitmap()
    {
        var image = BuildImage(32, 32, red: 255, blue: 0);
        var frame = image.Frames[0];
        var cache = new FrameImageCache(static source => source);

        var before = cache.GetOrDecode(image, frame, false, cache.Generation)!.Value.Raw;
        Assert.Equal(255, before.GetPixel(0, 0).R);

        image.ReplaceFrameImage(frame, Solid(32, 32, red: 0, blue: 255), ColorBits.UNKNOWN);

        var after = cache.GetOrDecode(image, frame, false, cache.Generation)!.Value.Raw;
        Assert.Equal(0, after.GetPixel(0, 0).R);
        Assert.Equal(255, after.GetPixel(0, 0).B);
    }

    /// <summary>几何变化（改宽高）同样必须失效，否则画布会按旧尺寸裁切。</summary>
    [Fact]
    public void ResizingFrame_InvalidatesCachedBitmap()
    {
        var image = BuildImage(16, 16);
        var frame = image.Frames[0];
        var cache = new FrameImageCache(static source => source);

        Assert.Equal(16, cache.GetOrDecode(image, frame, false, cache.Generation)!.Value.Raw.Width);

        image.ResizeFrame(frame, 24, 24);

        // ResizeFrame 内部会解码并重编码，缓存的指纹（宽高/RawData）随之失效
        var after = cache.GetOrDecode(image, frame, false, cache.Generation)!.Value.Raw;
        Assert.Equal(24, after.Width);
        Assert.Equal(24, after.Height);
    }

    private static ImgFile BuildImage(int width, int height, byte red = 255, byte blue = 0)
    {
        var image = ImgFile.Create(ImgVersion.Ver2, "cache.img");
        var frame = image.AddFrame();
        image.ReplaceFrameImage(frame, Solid(width, height, red, blue), ColorBits.UNKNOWN);
        return image;
    }

    private static RgbaImage Solid(int width, int height, byte red, byte blue)
    {
        var image = new RgbaImage(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.SetPixel(x, y, new Color32(red, 64, blue, 255));
            }
        }

        return image;
    }
}
