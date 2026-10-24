using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     帧裁剪（REQ-11）：裁透明边 + 坐标回补。
///     <para>
///         核心不变量：裁剪后把新帧按新的 X/Y 贴回原画布，必须与原图<b>逐像素完全相同</b>——
///         这是"游戏里位置不跳"的唯一证明。
///     </para>
/// </summary>
[TestClass]
public sealed class FrameTrimTests
{
    [TestMethod]
    public void Trim_CropsTransparentBorder_AndKeepsAbsolutePosition()
    {
        const int frameX = 100;
        const int frameY = 50;
        var canvas = new RgbaImage(40, 40);
        var color = new Color32(200, 30, 40, 255);
        for (var y = 11; y < 21; y++)
        {
            for (var x = 7; x < 17; x++)
            {
                canvas.SetPixel(x, y, color);
            }
        }

        var file = BuildV2(canvas, frameX, frameY, 40, 40);
        var frame = file.Frames[0];
        var original = file.Decode(frame)!;

        Assert.IsTrue(file.TrimFrame(frame), "有透明边，应该裁。");
        Assert.AreEqual(10, frame.Width);
        Assert.AreEqual(10, frame.Height);
        Assert.AreEqual(frameX + 7, frame.X, "X 必须累加透明边宽度。");
        Assert.AreEqual(frameY + 11, frame.Y, "Y 必须累加透明边高度。");
        Assert.AreEqual(40, frame.FrameWidth, "帧域只增不减。");
        Assert.AreEqual(40, frame.FrameHeight);

        // 把裁剪结果按新的 X/Y 贴回 40×40 画布，逐像素与原图比较。
        var trimmed = file.Decode(frame)!;
        Assert.AreEqual(10, trimmed.Width);
        Assert.AreEqual(10, trimmed.Height);

        var reassembled = new RgbaImage(40, 40);
        var offsetX = frame.X - frameX;
        var offsetY = frame.Y - frameY;
        for (var y = 0; y < trimmed.Height; y++)
        {
            for (var x = 0; x < trimmed.Width; x++)
            {
                reassembled.SetPixel(offsetX + x, offsetY + y, trimmed.GetPixel(x, y));
            }
        }

        AssertSamePixels(original, reassembled, "裁剪后贴回原位");
    }

    [TestMethod]
    public void Trim_AllTransparent_ReturnsFalse_AndLeavesFrameUntouched()
    {
        var file = BuildV2(new RgbaImage(10, 10), 3, 4, 10, 10);
        var frame = file.Frames[0];
        var raw = frame.RawData;

        Assert.IsFalse(file.TrimFrame(frame), "全透明帧不能裁（会得到 0×0 非法帧）。");

        Assert.AreEqual(10, frame.Width);
        Assert.AreEqual(10, frame.Height);
        Assert.AreEqual(3, frame.X);
        Assert.AreEqual(4, frame.Y);
        Assert.AreSame(raw, frame.RawData, "返回 false 时不得重编码。");
    }

    [TestMethod]
    public void Trim_AlreadyTight_ReturnsFalse_WithoutReencoding()
    {
        var image = new RgbaImage(5, 5);
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                image.SetPixel(x, y, new Color32(1, 2, 3, 255));
            }
        }

        var file = BuildV2(image, -2, -2, 5, 5);
        var frame = file.Frames[0];
        var raw = frame.RawData;

        Assert.IsFalse(file.TrimFrame(frame), "已经是紧包围盒，不应有收益。");
        Assert.AreSame(raw, frame.RawData, "无收益时不得重编码（避免无谓画质损失）。");
        Assert.AreEqual(-2, frame.X);
        Assert.AreEqual(-2, frame.Y);
    }

    [TestMethod]
    public void Trim_LinkFrame_Throws()
    {
        var data = TestImageBuilder.BuildVer2(
        [
            new TestImageBuilder.TestFrame
            {
                Type = ColorBits.ARGB_1555,
                Compress = CompressMode.NONE,
                Width = 2,
                Height = 2,
                Length = 2 * 2 * 2,
                FrameWidth = 2,
                FrameHeight = 2,
                Data = TestImageBuilder.BuildArgb1555Pixels(2, 2, 0)
            },
            new TestImageBuilder.TestFrame { Type = ColorBits.LINK, LinkTargetIndex = 0 }
        ]);
        var file = ImgFile.Read(new MemoryStream(data), "trim-link.img", data.Length);

        Assert.ThrowsExactly<InvalidOperationException>(() => file.TrimFrame(file.Frames[1]));
    }

    [TestMethod]
    public void Trim_AccumulatesNegativeCoordinates()
    {
        var canvas = new RgbaImage(8, 8);
        for (var y = 3; y < 7; y++)
        {
            for (var x = 2; x < 5; x++)
            {
                canvas.SetPixel(x, y, new Color32(9, 8, 7, 255));
            }
        }

        var file = BuildV2(canvas, -5, -8, 8, 8);
        var frame = file.Frames[0];

        Assert.IsTrue(file.TrimFrame(frame));
        Assert.AreEqual(-5 + 2, frame.X, "负坐标同样按累加处理。");
        Assert.AreEqual(-8 + 3, frame.Y);
        Assert.AreEqual(3, frame.Width);
        Assert.AreEqual(4, frame.Height);
    }

    [TestMethod]
    public void Trim_PaletteFrame_ReencodesAsIndices()
    {
        var clear = new Color32(0, 0, 0, 0);
        var red = new Color32(255, 0, 0, 255);

        // 8×8 全是色号 0（透明），(3,2) 起 4×5 是色号 1（红）。
        var indices = new byte[8 * 8];
        for (var y = 2; y < 7; y++)
        {
            for (var x = 3; x < 7; x++)
            {
                indices[(y * 8) + x] = 1;
            }
        }

        var payload = ZlibCodec.Compress(indices);
        var data = TestImageBuilder.BuildVer4(
        [
            new TestImageBuilder.TestFrame
            {
                Type = ColorBits.ARGB_1555,
                Compress = CompressMode.ZLIB,
                Width = 8,
                Height = 8,
                Length = payload.Length,
                X = 20,
                Y = 30,
                FrameWidth = 20,
                FrameHeight = 20,
                Data = payload
            }
        ], [clear, red]);

        var file = ImgFile.Read(new MemoryStream(data), "trim-v4.img", data.Length);
        var frame = file.Frames[0];
        var original = file.Decode(frame)!;

        Assert.IsTrue(file.TrimFrame(frame));

        Assert.AreEqual(4, frame.Width);
        Assert.AreEqual(5, frame.Height);
        Assert.AreEqual(23, frame.X);
        Assert.AreEqual(32, frame.Y);
        Assert.AreEqual(CompressMode.ZLIB, frame.Compress);
        Assert.AreEqual(2, file.Palette.Count, "裁剪不得改动色表。");

        var expected = original.Crop(new PixelBounds(3, 2, 4, 5));
        AssertSamePixels(expected, file.Decode(frame)!, "v4 裁剪后像素");

        // 写回再读：色号仍是 1 字节/像素，不能被误写成 1555。
        var written = file.ToArray();
        var reread = ImgFile.Read(new MemoryStream(written), "trim-v4.img", written.Length);
        AssertSamePixels(expected, reread.Decode(reread.Frames[0])!, "v4 裁剪写回");
    }

    private static ImgFile BuildV2(RgbaImage image, int x, int y, int frameWidth, int frameHeight)
    {
        var data = TestImageBuilder.BuildVer2(
        [
            new TestImageBuilder.TestFrame
            {
                Type = ColorBits.ARGB_1555,
                Compress = CompressMode.NONE,
                Width = image.Width,
                Height = image.Height,
                Length = image.Width * image.Height * 2,
                X = x,
                Y = y,
                FrameWidth = frameWidth,
                FrameHeight = frameHeight,
                Data = ColorCodec.Encode(image.Pixels, ColorBits.ARGB_1555)
            }
        ]);
        return ImgFile.Read(new MemoryStream(data), "trim.img", data.Length);
    }

    private static void AssertSamePixels(RgbaImage expected, RgbaImage actual, string message)
    {
        Assert.AreEqual(expected.Width, actual.Width, $"{message}：宽度");
        Assert.AreEqual(expected.Height, actual.Height, $"{message}：高度");
        CollectionAssert.AreEqual(expected.Pixels, actual.Pixels, $"{message}：像素");
    }
}
