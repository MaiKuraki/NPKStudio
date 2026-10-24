using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     IMG v4（单色表）/ v6（多色表）的读写、解码、编辑与容错。
///     <para>
///         <b>没有真实样本</b>：仓库自带的合成样本全是 v2，公开渠道也拿不到可随仓库分发的 v4/v6 包，
///         所以这些样本全部按 <see cref="TestImageBuilder" /> 逐字节构造，布局依据见
///         <c>FORMAT</c> 说明与 <c>Ver4Handler</c>/<c>Ver6Handler</c> 的注释。
///     </para>
/// </summary>
[TestClass]
public sealed class Ver4Ver6Tests
{
    private static readonly Color32 Red = new(255, 0, 0, 255);
    private static readonly Color32 Green = new(0, 255, 0, 255);
    private static readonly Color32 Blue = new(0, 0, 255, 255);
    private static readonly Color32 Clear = new(0, 0, 0, 0);

    [TestMethod]
    public void Ver4_Read_IndexLengthPaletteAndGeometry()
    {
        var frame = PaletteFrame(2, 2, [0, 1, 2, 3], zlib: true, x: -3, y: 7, frameWidth: 10, frameHeight: 11);
        var data = TestImageBuilder.BuildVer4([frame], [Red, Green, Blue, Clear]);

        var image = ImgFile.Read(new MemoryStream(data), "v4.img", data.Length);

        Assert.AreEqual(ImgVersion.Ver4, image.Version);
        Assert.AreEqual(1, image.Frames.Count);
        Assert.AreEqual(36L, image.IndexLength, "IndexLength 只含帧索引区，不含色表。");
        Assert.AreEqual(4, image.Palette.Count);
        Assert.AreEqual(Red, image.Palette[0]);
        Assert.AreEqual(Green, image.Palette[1]);
        Assert.AreEqual(Blue, image.Palette[2]);
        Assert.AreEqual(Clear, image.Palette[3]);

        var read = image.Frames[0];
        Assert.AreEqual(ColorBits.ARGB_1555, read.Type, "v4 索引区里的 type 仍沿用 0x0E（旧实现约定）。");
        Assert.AreEqual(CompressMode.ZLIB, read.Compress);
        Assert.AreEqual(2, read.Width);
        Assert.AreEqual(2, read.Height);
        Assert.AreEqual(-3, read.X);
        Assert.AreEqual(7, read.Y);
        Assert.AreEqual(10, read.FrameWidth);
        Assert.AreEqual(11, read.FrameHeight);
    }

    [TestMethod]
    public void Ver4_Decode_MapsIndicesToBgra()
    {
        var frame = PaletteFrame(2, 2, [0, 1, 2, 3], zlib: true);
        var data = TestImageBuilder.BuildVer4([frame], [Red, Green, Blue, Clear]);
        var image = ImgFile.Read(new MemoryStream(data), "v4-decode.img", data.Length);

        var decoded = image.Decode(image.Frames[0])!;

        Assert.AreEqual(2, decoded.Width);
        Assert.AreEqual(2, decoded.Height);
        Assert.AreEqual(Red, decoded.GetPixel(0, 0));
        Assert.AreEqual(Green, decoded.GetPixel(1, 0));
        Assert.AreEqual(Blue, decoded.GetPixel(0, 1));
        Assert.AreEqual(Clear, decoded.GetPixel(1, 1));
    }

    [TestMethod]
    public void Ver4_Write_ThenRead_IsByteIdentical_WithLink()
    {
        var data = TestImageBuilder.BuildVer4(
        [
            PaletteFrame(2, 2, [0, 1, 2, 3], zlib: true, x: 5, y: -6, frameWidth: 12, frameHeight: 12),
            new TestImageBuilder.TestFrame { Type = ColorBits.LINK, LinkTargetIndex = 0 },
            PaletteFrame(1, 1, [3], zlib: false, x: 0, y: 0, frameWidth: 12, frameHeight: 12)
        ],
        [Red, Green, Blue, Clear]);

        var image = ImgFile.Read(new MemoryStream(data), "v4-write.img", data.Length);
        var written = image.ToArray();

        CollectionAssert.AreEqual(data, written, "v4 读→写 必须逐字节一致（含色表、LINK、NONE 帧）。");

        var reread = ImgFile.Read(new MemoryStream(written), "v4-write.img", written.Length);
        Assert.AreEqual(3, reread.Frames.Count);
        Assert.IsTrue(reread.Frames[1].IsLink);
        Assert.AreSame(reread.Frames[0], reread.Frames[1].LinkTarget);
        AssertSamePixels(image.Decode(image.Frames[0])!, reread.Decode(reread.Frames[0])!, "帧 0");
        AssertSamePixels(image.Decode(image.Frames[2])!, reread.Decode(reread.Frames[2])!, "帧 2");
    }

    [TestMethod]
    public void Ver4_NoneCompress_LengthIsDerivedFromGeometry()
    {
        // 索引里的 length 故意写错（1）：NONE 时必须用 Width × Height × 1 覆盖。
        var frame = new TestImageBuilder.TestFrame
        {
            Type = ColorBits.ARGB_1555,
            Compress = CompressMode.NONE,
            Width = 3,
            Height = 2,
            Length = 1,
            FrameWidth = 3,
            FrameHeight = 2,
            Data = [0, 1, 0, 1, 0, 1]
        };
        var data = TestImageBuilder.BuildVer4([frame], [Red, Green]);
        var image = ImgFile.Read(new MemoryStream(data), "v4-none.img", data.Length);

        Assert.AreEqual(6, image.Frames[0].Length, "NONE 时长度应为 3×2×1 = 6，而不是 v2 的 ×2。");
        Assert.AreEqual(6, image.Frames[0].RawData.Length);
        Assert.AreEqual(Red, image.Decode(image.Frames[0])!.GetPixel(0, 0));
    }

    [TestMethod]
    public void Ver4_IndexBeyondPalette_ThrowsImgFormatException()
    {
        var data = TestImageBuilder.BuildVer4([PaletteFrame(2, 1, [0, 7], zlib: true)], [Red, Green]);
        var image = ImgFile.Read(new MemoryStream(data), "v4-bad-index.img", data.Length);

        var ex = Assert.ThrowsExactly<ImgFormatException>(() => image.Decode(image.Frames[0]));

        StringAssert.Contains(ex.Message, "色号 7");
        StringAssert.Contains(ex.Message, "超出色表长度 2");
    }

    [TestMethod]
    public void Ver4_ReplaceFrameImage_QuantizesToNearestColor()
    {
        var data = TestImageBuilder.BuildVer4([PaletteFrame(2, 2, [0, 0, 0, 0], zlib: true)], [Red, Green]);
        var image = ImgFile.Read(new MemoryStream(data), "v4-replace.img", data.Length);
        var frame = image.Frames[0];

        var replacement = new RgbaImage(2, 2);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                // (250,10,10) 离红最近，但色表里没有完全相同的颜色 → 必须走就近色。
                replacement.SetPixel(x, y, new Color32(250, 10, 10, 255));
            }
        }

        image.ReplaceFrameImage(frame, replacement, ColorBits.UNKNOWN);

        Assert.AreEqual(CompressMode.ZLIB, frame.Compress);
        Assert.AreEqual(2, frame.Width);
        var decoded = image.Decode(frame)!;
        Assert.AreEqual(Red, decoded.GetPixel(0, 0), "近似色应量化到色表里的红。");
        Assert.AreEqual(Red, decoded.GetPixel(1, 1));

        // 写回后仍能读：色号是 1 字节/像素，替换链路没有写出 1555 的两字节数据。
        var written = image.ToArray();
        var reread = ImgFile.Read(new MemoryStream(written), "v4-replace.img", written.Length);
        Assert.AreEqual(2, reread.Frames[0].Width);
        Assert.AreEqual(Red, reread.Decode(reread.Frames[0])!.GetPixel(0, 0));
    }

    [TestMethod]
    public void Ver4_ReplaceLinkFrame_TurnsIntoNormalFrame()
    {
        var data = TestImageBuilder.BuildVer4(
        [
            PaletteFrame(1, 1, [0], zlib: true),
            new TestImageBuilder.TestFrame { Type = ColorBits.LINK, LinkTargetIndex = 0 }
        ],
        [Red, Green]);

        var image = ImgFile.Read(new MemoryStream(data), "v4-link-replace.img", data.Length);
        var link = image.Frames[1];
        Assert.IsTrue(link.IsLink);

        var replacement = new RgbaImage(2, 2);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                replacement.SetPixel(x, y, new Color32(0, 250, 10, 255));
            }
        }

        image.ReplaceFrameImage(link, replacement, ColorBits.UNKNOWN);

        Assert.IsFalse(link.IsLink, "被替换的 LINK 帧必须变成普通帧，否则写盘会找不到链接目标。");
        Assert.AreEqual(ColorBits.ARGB_1555, link.Type);
        Assert.AreEqual(Green, image.Decode(link)!.GetPixel(0, 0));

        var written = image.ToArray();
        var reread = ImgFile.Read(new MemoryStream(written), "v4-link-replace.img", written.Length);
        Assert.IsFalse(reread.Frames[1].IsLink);
        Assert.AreEqual(Green, reread.Decode(reread.Frames[1])!.GetPixel(1, 1));
    }

    [TestMethod]
    public void Ver4_InsertFrame_ProducesDecodableTransparentFrame()
    {
        // 色表第 1 项是透明的：空帧应选中它，而不是无脑写色号 0。
        var data = TestImageBuilder.BuildVer4([PaletteFrame(1, 1, [2], zlib: true)], [Red, Clear, Green]);
        var image = ImgFile.Read(new MemoryStream(data), "v4-insert.img", data.Length);

        var inserted = image.InsertFrame(0);

        Assert.AreEqual(1, inserted.Width);
        Assert.AreEqual(1, inserted.Height);
        var blank = image.Decode(inserted)!;
        Assert.AreEqual(1, blank.Pixels.Length / 4, "v4 的 1×1 空帧数据必须是 1 字节色号。");
        Assert.AreEqual(0, blank.GetPixel(0, 0).A, "空帧必须是全透明（选中色表里 A=0 的项）。");

        var written = image.ToArray();
        var reread = ImgFile.Read(new MemoryStream(written), "v4-insert.img", written.Length);
        Assert.AreEqual(2, reread.Frames.Count);
        Assert.AreEqual(0, reread.Decode(reread.Frames[0])!.GetPixel(0, 0).A);
        Assert.AreEqual(Green, reread.Decode(reread.Frames[1])!.GetPixel(0, 0), "原帧后移一位且内容不变。");
    }

    [TestMethod]
    public void Ver2_To_Ver4_To_Ver2_PixelsSurvive()
    {
        // 1) v2 源图：4×4，只有两个不透明像素 + 若干全透明像素。
        var source = new RgbaImage(4, 4);
        source.SetPixel(1, 1, new Color32(200, 30, 40, 255));
        source.SetPixel(2, 2, new Color32(10, 90, 220, 255));
        var v2Data = TestImageBuilder.BuildVer2(
        [
            new TestImageBuilder.TestFrame
            {
                Type = ColorBits.ARGB_1555,
                Compress = CompressMode.NONE,
                Width = 4,
                Height = 4,
                Length = 4 * 4 * 2,
                X = 9,
                Y = -4,
                FrameWidth = 20,
                FrameHeight = 20,
                Data = ColorCodec.Encode(source.Pixels, ColorBits.ARGB_1555)
            }
        ]);

        var v2 = ImgFile.Read(new MemoryStream(v2Data), "v2.img", v2Data.Length);
        var expected = v2.Decode(v2.Frames[0])!;

        // 2) 按解码结果构造 v4（测试侧量化：颜色都来自 v2 解码结果，必然精确命中）。
        var palette = new List<Color32>();
        var lookup = new Dictionary<uint, byte>();
        var indices = new byte[expected.Width * expected.Height];
        var pixels = expected.Pixels;
        for (var i = 0; i < indices.Length; i++)
        {
            var offset = i * 4;
            var key = (uint)(pixels[offset] | (pixels[offset + 1] << 8) | (pixels[offset + 2] << 16) | (pixels[offset + 3] << 24));
            if (!lookup.TryGetValue(key, out var index))
            {
                index = (byte)palette.Count;
                lookup.Add(key, index);
                palette.Add(Color32.FromBgra(pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]));
            }

            indices[i] = index;
        }

        var v4Data = TestImageBuilder.BuildVer4(
        [
            new TestImageBuilder.TestFrame
            {
                Type = ColorBits.ARGB_1555,
                Compress = CompressMode.ZLIB,
                Width = 4,
                Height = 4,
                Length = ZlibCodec.Compress(indices).Length,
                X = 9,
                Y = -4,
                FrameWidth = 20,
                FrameHeight = 20,
                Data = ZlibCodec.Compress(indices)
            }
        ], palette);

        var v4 = ImgFile.Read(new MemoryStream(v4Data), "v4.img", v4Data.Length);
        var fromV4 = v4.Decode(v4.Frames[0])!;
        AssertSamePixels(expected, fromV4, "v2 → v4");

        // 3) v4 写回再读（真实写盘路径），像素仍一致。
        var v4Written = v4.ToArray();
        var v4Reread = ImgFile.Read(new MemoryStream(v4Written), "v4.img", v4Written.Length);
        AssertSamePixels(expected, v4Reread.Decode(v4Reread.Frames[0])!, "v4 写→读");

        // 4) v4 解码结果再按 v2 结构编回去，几何与像素都不变。
        var backData = TestImageBuilder.BuildVer2(
        [
            new TestImageBuilder.TestFrame
            {
                Type = ColorBits.ARGB_1555,
                Compress = CompressMode.NONE,
                Width = 4,
                Height = 4,
                Length = 4 * 4 * 2,
                X = v4Reread.Frames[0].X,
                Y = v4Reread.Frames[0].Y,
                FrameWidth = v4Reread.Frames[0].FrameWidth,
                FrameHeight = v4Reread.Frames[0].FrameHeight,
                Data = ColorCodec.Encode(v4Reread.Decode(v4Reread.Frames[0])!.Pixels, ColorBits.ARGB_1555)
            }
        ]);

        var back = ImgFile.Read(new MemoryStream(backData), "v2-back.img", backData.Length);
        AssertSamePixels(expected, back.Decode(back.Frames[0])!, "v4 → v2");
        Assert.AreEqual(9, back.Frames[0].X);
        Assert.AreEqual(-4, back.Frames[0].Y);
    }

    [TestMethod]
    public void Ver4_Layout_PaletteSitsBetweenCommonHeaderAndFrameIndex()
    {
        var data = TestImageBuilder.BuildVer4([PaletteFrame(1, 1, [0], zlib: false)], [Red, Green]);

        // 0..31 公共头；32..35 色表项数；36..43 两色（R,G,B,A × 2）；44 起是帧索引区。
        Assert.AreEqual(2, BitConverter.ToInt32(data, 32), "色表项数必须紧跟 32 字节公共头。");
        Assert.AreEqual(255, data[36], "第一色 byte0 = R（旧实现 Colors.ReadPalette 的口径）。");
        Assert.AreEqual(0, data[37]);
        Assert.AreEqual(0, data[38]);
        Assert.AreEqual(255, data[39]);
        Assert.AreEqual(0x0E, BitConverter.ToInt32(data, 44), "色表之后才是帧索引区（type=0x0E）。");
        Assert.AreEqual(36L, BitConverter.ToInt64(data, 16), "IndexLength 只含 36 字节索引项，不含色表。");
    }

    [TestMethod]
    public void Ver6_Layout_TableCountThenEachTable()
    {
        var data = TestImageBuilder.BuildVer6(
            [PaletteFrame(1, 1, [0], zlib: false)],
            [new[] { Red }, new[] { Green, Blue }]);

        Assert.AreEqual(2, BitConverter.ToInt32(data, 32), "表数量紧跟公共头。");
        Assert.AreEqual(1, BitConverter.ToInt32(data, 36), "第 0 张表的项数。");
        Assert.AreEqual(255, data[40], "第 0 张表第一色 byte0 = R。");
        Assert.AreEqual(2, BitConverter.ToInt32(data, 44), "第 1 张表的项数。");
        // 32 + 4 + (4 + 4) + (4 + 8) = 56 → 帧索引区。
        Assert.AreEqual(0x0E, BitConverter.ToInt32(data, 56));
    }

    [TestMethod]
    public void Ver6_Read_MultiplePalettes_DecodesWithFirstTable()
    {
        var data = TestImageBuilder.BuildVer6(
            [PaletteFrame(2, 1, [0, 1], zlib: true)],
            [new[] { Red, Green }, new[] { Blue }]);

        var image = ImgFile.Read(new MemoryStream(data), "v6.img", data.Length);

        Assert.AreEqual(ImgVersion.Ver6, image.Version);
        Assert.AreEqual(2, image.Palettes.Count);
        Assert.AreEqual(2, image.Palettes[0].Count);
        Assert.AreEqual(1, image.Palettes[1].Count);
        Assert.AreEqual(2, image.Palette.Count, "主调色板出口应指向第 0 张表。");
        Assert.AreEqual(Blue, image.Palettes[1][0]);

        var decoded = image.Decode(image.Frames[0])!;
        Assert.AreEqual(Red, decoded.GetPixel(0, 0));
        Assert.AreEqual(Green, decoded.GetPixel(1, 0));
    }

    [TestMethod]
    public void Ver6_Write_ThenRead_IsByteIdentical_WithLink()
    {
        var data = TestImageBuilder.BuildVer6(
        [
            PaletteFrame(2, 2, [0, 1, 1, 0], zlib: true, x: 2, y: 3, frameWidth: 8, frameHeight: 8),
            new TestImageBuilder.TestFrame { Type = ColorBits.LINK, LinkTargetIndex = 0 },
            PaletteFrame(1, 1, [0], zlib: false, x: -1, y: -2, frameWidth: 8, frameHeight: 8)
        ],
        [new[] { Red, Green }, new[] { Blue, Clear }]);

        var image = ImgFile.Read(new MemoryStream(data), "v6-write.img", data.Length);
        var written = image.ToArray();

        CollectionAssert.AreEqual(data, written, "v6 读→写 必须逐字节一致（多色表、LINK、NONE）。");

        var reread = ImgFile.Read(new MemoryStream(written), "v6-write.img", written.Length);
        Assert.AreEqual(2, reread.Palettes.Count);
        Assert.AreSame(reread.Frames[0], reread.Frames[1].LinkTarget);
        AssertSamePixels(image.Decode(image.Frames[2])!, reread.Decode(reread.Frames[2])!, "帧 2");
    }

    [TestMethod]
    public void Ver6_IndexBeyondTable_ThrowsImgFormatException()
    {
        var data = TestImageBuilder.BuildVer6(
            [PaletteFrame(2, 1, [0, 3], zlib: true)],
            [new[] { Red }, new[] { Green, Blue }]);
        var image = ImgFile.Read(new MemoryStream(data), "v6-bad-index.img", data.Length);

        var ex = Assert.ThrowsExactly<ImgFormatException>(() => image.Decode(image.Frames[0]));

        StringAssert.Contains(ex.Message, "色号 3");
        StringAssert.Contains(ex.Message, "超出色表长度 1");
    }

    [TestMethod]
    public void Ver6_ReplaceFrameImage_UsesFirstTable()
    {
        var data = TestImageBuilder.BuildVer6(
            [PaletteFrame(2, 2, [0, 0, 0, 0], zlib: true)],
            [new[] { Green }, new[] { Red }]);
        var image = ImgFile.Read(new MemoryStream(data), "v6-replace.img", data.Length);

        var replacement = new RgbaImage(2, 2);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                replacement.SetPixel(x, y, new Color32(0, 250, 10, 255));
            }
        }

        image.ReplaceFrameImage(image.Frames[0], replacement, ColorBits.UNKNOWN);

        Assert.AreEqual(Green, image.Decode(image.Frames[0])!.GetPixel(0, 0), "v6 编辑按第 0 张表量化。");
    }

    private static TestImageBuilder.TestFrame PaletteFrame(
        int width,
        int height,
        byte[] indices,
        bool zlib,
        int x = 0,
        int y = 0,
        int frameWidth = 0,
        int frameHeight = 0)
    {
        var payload = zlib ? ZlibCodec.Compress(indices) : indices;
        return new TestImageBuilder.TestFrame
        {
            Type = ColorBits.ARGB_1555,
            Compress = zlib ? CompressMode.ZLIB : CompressMode.NONE,
            Width = width,
            Height = height,
            Length = payload.Length,
            X = x,
            Y = y,
            FrameWidth = frameWidth == 0 ? width : frameWidth,
            FrameHeight = frameHeight == 0 ? height : frameHeight,
            Data = payload
        };
    }

    private static void AssertSamePixels(RgbaImage expected, RgbaImage actual, string message)
    {
        Assert.AreEqual(expected.Width, actual.Width, $"{message}：宽度");
        Assert.AreEqual(expected.Height, actual.Height, $"{message}：高度");
        CollectionAssert.AreEqual(expected.Pixels, actual.Pixels, $"{message}：像素");
    }
}
