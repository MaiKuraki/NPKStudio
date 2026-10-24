using NPKStudio.Core.Imaging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     <see cref="RgbaImage" /> 的几何与扫描测试。
///     <para>重点覆盖 D-03：全透明图像的 <c>ScanBounds</c> 必须返回 <c>false</c>，且绝不产生非法矩形。</para>
/// </summary>
[TestClass]
public sealed class RgbaImageTests
{
    [TestMethod]
    public void Constructor_AllocatesTransparentBufferWithCorrectLength()
    {
        var image = new RgbaImage(7, 5);

        Assert.AreEqual(7, image.Width);
        Assert.AreEqual(5, image.Height);
        Assert.AreEqual(28, image.Stride);
        Assert.AreEqual(7 * 5 * 4, image.Pixels.Length);
        CollectionAssert.AreEqual(new byte[7 * 5 * 4], image.Pixels, "新建图像应全为 0（全透明）。");
    }

    [TestMethod]
    public void ScanBounds_FullyTransparent_ReturnsFalse()
    {
        var image = new RgbaImage(128, 128);

        var found = image.ScanBounds(out var bounds);

        Assert.IsFalse(found, "全透明图像不应被扫描出边界（D-03）。");
        Assert.AreEqual(default(PixelBounds), bounds, "全透明时 bounds 必须为 default。");
    }

    [TestMethod]
    public void ScanBounds_SingleTransparentPixel_ReturnsFalse()
    {
        var image = new RgbaImage(1, 1);

        var found = image.ScanBounds(out var bounds);

        Assert.IsFalse(found);
        Assert.AreEqual(4, image.Pixels.Length, "1x1 图像的像素缓冲应为 4 字节。");
        Assert.AreEqual(default(PixelBounds), bounds);
    }

    [TestMethod]
    public void ScanBounds_FullyTransparentWithThresholdZero_ReturnsFullRect()
    {
        // alphaThreshold = 0 表示"所有像素都算不透明"，用于需要整图矩形的场合。
        var image = new RgbaImage(4, 3);

        var found = image.ScanBounds(0, out var bounds);

        Assert.IsTrue(found);
        Assert.AreEqual(new PixelBounds(0, 0, 4, 3), bounds);
        Assert.IsTrue(bounds.Width <= image.Width && bounds.Height <= image.Height);
    }

    [TestMethod]
    public void ScanBounds_ReturnsTightRectangle()
    {
        var image = new RgbaImage(8, 8);
        image.SetPixel(2, 3, new Color32(10, 20, 30, 255));
        image.SetPixel(5, 6, new Color32(10, 20, 30, 255));

        var found = image.ScanBounds(out var bounds);

        Assert.IsTrue(found);
        Assert.AreEqual(new PixelBounds(2, 3, 4, 4), bounds);
        Assert.AreEqual(2, bounds.X);
        Assert.AreEqual(3, bounds.Y);
        Assert.AreEqual(4, bounds.Width);
        Assert.AreEqual(4, bounds.Height);
    }

    [TestMethod]
    public void ScanBounds_ThresholdFiltersLowAlphaPixels()
    {
        var image = new RgbaImage(4, 4);
        image.SetPixel(0, 0, new Color32(0, 0, 0, 1));
        image.SetPixel(3, 3, new Color32(0, 0, 0, 200));

        // 默认阈值 1：alpha>=1 即算不透明 → 左上与右下都命中
        Assert.IsTrue(image.ScanBounds(out var withDefault));
        Assert.AreEqual(new PixelBounds(0, 0, 4, 4), withDefault);

        // 阈值 100：只剩右下角
        Assert.IsTrue(image.ScanBounds(100, out var withThreshold));
        Assert.AreEqual(new PixelBounds(3, 3, 1, 1), withThreshold);
    }

    [TestMethod]
    public void Crop_BeyondEdges_ClampsToIntersection()
    {
        var image = new RgbaImage(4, 4);
        image.SetPixel(2, 2, new Color32(1, 2, 3, 255));
        image.SetPixel(3, 3, new Color32(4, 5, 6, 255));

        var cropped = image.Crop(new PixelBounds(2, 2, 10, 10));

        Assert.AreEqual(2, cropped.Width);
        Assert.AreEqual(2, cropped.Height);
        Assert.AreEqual(2 * 2 * 4, cropped.Pixels.Length);
        Assert.AreEqual(new Color32(1, 2, 3, 255), cropped.GetPixel(0, 0));
        Assert.AreEqual(new Color32(4, 5, 6, 255), cropped.GetPixel(1, 1));
    }

    [TestMethod]
    public void Crop_NegativeOrigin_ClampsToImage()
    {
        var image = new RgbaImage(4, 4);
        image.SetPixel(0, 0, new Color32(9, 9, 9, 255));

        var cropped = image.Crop(new PixelBounds(-2, -2, 4, 4));

        Assert.AreEqual(2, cropped.Width);
        Assert.AreEqual(2, cropped.Height);
        Assert.AreEqual(new Color32(9, 9, 9, 255), cropped.GetPixel(0, 0));
    }

    [TestMethod]
    public void Crop_NoIntersection_ReturnsSingleTransparentPixel()
    {
        var image = new RgbaImage(4, 4);

        var cropped = image.Crop(new PixelBounds(100, 100, 8, 8));

        Assert.AreEqual(1, cropped.Width, "空交集必须退化为 1x1，不能返回 0 或越界尺寸。");
        Assert.AreEqual(1, cropped.Height);
        Assert.AreEqual(4, cropped.Pixels.Length);
        Assert.AreEqual(Color32.Transparent, cropped.GetPixel(0, 0));
    }

    [TestMethod]
    public void Resize_UsesNearestNeighbour()
    {
        var image = new RgbaImage(2, 2);
        image.SetPixel(0, 0, new Color32(255, 0, 0, 255));
        image.SetPixel(1, 0, new Color32(0, 255, 0, 255));
        image.SetPixel(0, 1, new Color32(0, 0, 255, 255));
        image.SetPixel(1, 1, new Color32(255, 255, 255, 255));

        var resized = image.Resize(4, 4);

        Assert.AreEqual(4, resized.Width);
        Assert.AreEqual(4, resized.Height);
        Assert.AreEqual(4 * 4 * 4, resized.Pixels.Length);

        // 最近邻：源 (x/2, y/2)
        Assert.AreEqual(new Color32(255, 0, 0, 255), resized.GetPixel(0, 0));
        Assert.AreEqual(new Color32(255, 0, 0, 255), resized.GetPixel(1, 1));
        Assert.AreEqual(new Color32(0, 255, 0, 255), resized.GetPixel(2, 0));
        Assert.AreEqual(new Color32(0, 0, 255, 255), resized.GetPixel(0, 2));
        Assert.AreEqual(new Color32(255, 255, 255, 255), resized.GetPixel(3, 3));

        var downscaled = image.Resize(1, 1);
        Assert.AreEqual(new Color32(255, 0, 0, 255), downscaled.GetPixel(0, 0));
    }

    [TestMethod]
    public void FromColors_And_ToBgra32_RoundTrip()
    {
        Color32[] colors =
        [
            new Color32(1, 2, 3, 4),
            new Color32(5, 6, 7, 8),
            new Color32(9, 10, 11, 12),
            new Color32(13, 14, 15, 16)
        ];

        var image = RgbaImage.FromColors(colors, 2, 2);

        byte[] expected =
        [
            3, 2, 1, 4,
            7, 6, 5, 8,
            11, 10, 9, 12,
            15, 14, 13, 16
        ];
        CollectionAssert.AreEqual(expected, image.Pixels, "FromColors 应写出 BGRA 字节序。");
        CollectionAssert.AreEqual(expected, image.ToBgra32(), "ToBgra32 应返回 BGRA 副本。");
        Assert.AreEqual(new Color32(13, 14, 15, 16), image.GetPixel(1, 1));
    }

    [TestMethod]
    public void FromBgra_KeepsBufferAndLength()
    {
        var pixels = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var image = RgbaImage.FromBgra(pixels, 2, 1);

        Assert.AreSame(pixels, image.Pixels, "FromBgra 不应复制数组（零拷贝接管）。");
        Assert.AreEqual(8, image.Pixels.Length);
        Assert.AreEqual(new Color32(3, 2, 1, 4), image.GetPixel(0, 0));
        Assert.AreEqual(new Color32(7, 6, 5, 8), image.GetPixel(1, 0));
    }

    [TestMethod]
    public void CopyRowTo_CopiesSingleRowWithoutChangingSource()
    {
        var image = new RgbaImage(3, 2);
        image.SetPixel(0, 1, new Color32(1, 1, 1, 255));
        image.SetPixel(1, 1, new Color32(2, 2, 2, 255));
        image.SetPixel(2, 1, new Color32(3, 3, 3, 255));
        var lengthBefore = image.Pixels.Length;

        var row = new byte[12];
        image.CopyRowTo(1, row);

        byte[] expected =
        [
            1, 1, 1, 255,
            2, 2, 2, 255,
            3, 3, 3, 255
        ];
        CollectionAssert.AreEqual(expected, row);
        Assert.AreEqual(lengthBefore, image.Pixels.Length, "CopyRowTo 不得改变像素数组长度。");
    }

    [TestMethod]
    public void Clone_IsDeepCopy()
    {
        var image = new RgbaImage(2, 2);
        image.SetPixel(0, 0, new Color32(1, 2, 3, 255));

        var clone = image.Clone();
        clone.SetPixel(0, 0, new Color32(4, 5, 6, 255));

        Assert.AreEqual(new Color32(1, 2, 3, 255), image.GetPixel(0, 0), "Clone 必须与源实例互不影响。");
        Assert.AreEqual(new Color32(4, 5, 6, 255), clone.GetPixel(0, 0));
    }

    [TestMethod]
    public void GetPixel_OutOfRange_ReturnsTransparent()
    {
        var image = new RgbaImage(2, 2);

        Assert.AreEqual(Color32.Transparent, image.GetPixel(-1, 0));
        Assert.AreEqual(Color32.Transparent, image.GetPixel(0, 5));
    }
}
