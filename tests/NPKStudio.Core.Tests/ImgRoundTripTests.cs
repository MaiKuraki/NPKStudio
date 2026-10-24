using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     IMG v1 / v2 的读→写→读往返测试。
///     <para>v1 没有真实样本（三个客户端抽样 100% 是 v2），因此用 <see cref="TestImageBuilder" /> 逐字节构造。</para>
/// </summary>
[TestClass]
public sealed class ImgRoundTripTests
{
    [TestMethod]
    public void Ver2_Read_IndexLengthAndGeometry()
    {
        var data = TestImageBuilder.BuildVer2(
        [
            NormalFrame(17, 17, 13, 13, 43, 43, seed: 0),
            NormalFrame(17, 17, 14, 13, 43, 43, seed: 1),
            LinkFrame(0),
            LinkFrame(1),
            NormalFrame(17, 17, 13, 13, 43, 43, seed: 4)
        ]);

        var image = ImgFile.Read(new MemoryStream(data), "test.img", data.Length);

        Assert.AreEqual(ImgVersion.Ver2, image.Version);
        Assert.AreEqual(5, image.Frames.Count);
        Assert.AreEqual(124L, image.IndexLength, "3 个普通帧(36B) + 2 个 LINK(8B) = 124。");

        var frame0 = image.Frames[0];
        Assert.AreEqual(ColorBits.ARGB_1555, frame0.Type);
        Assert.AreEqual(CompressMode.NONE, frame0.Compress);
        Assert.AreEqual(17, frame0.Width);
        Assert.AreEqual(17, frame0.Height);
        Assert.AreEqual(13, frame0.X);
        Assert.AreEqual(13, frame0.Y);
        Assert.AreEqual(43, frame0.FrameWidth);
        Assert.AreEqual(43, frame0.FrameHeight);
        Assert.AreEqual(17 * 17 * 2, frame0.Length);
        Assert.AreEqual(17 * 17 * 2, frame0.RawData.Length);
    }

    [TestMethod]
    public void Ver2_LinkFrames_ResolveTargets()
    {
        var data = TestImageBuilder.BuildVer2(
        [
            NormalFrame(4, 4, 0, 0, 8, 8, seed: 0),
            NormalFrame(4, 4, 1, 1, 8, 8, seed: 1),
            LinkFrame(0),
            LinkFrame(1)
        ]);

        var image = ImgFile.Read(new MemoryStream(data), "link.img", data.Length);

        Assert.IsTrue(image.Frames[2].IsLink);
        Assert.IsTrue(image.Frames[3].IsLink);
        Assert.AreSame(image.Frames[0], image.Frames[2].LinkTarget, "帧 2 应指向帧 0。");
        Assert.AreSame(image.Frames[1], image.Frames[3].LinkTarget, "帧 3 应指向帧 1。");
        Assert.IsTrue(image.Frames[0].RawData.Length > 0);
        Assert.AreEqual(0, image.Frames[2].RawData.Length, "LINK 帧不占数据区。");
    }

    [TestMethod]
    public void Ver2_Write_ThenRead_IsByteIdentical()
    {
        var original = TestImageBuilder.BuildVer2(
        [
            NormalFrame(17, 17, 13, 13, 43, 43, seed: 0),
            NormalFrame(9, 7, -3, 5, 20, 20, seed: 1),
            LinkFrame(1),
            NormalFrame(4, 4, 0, 0, 4, 4, seed: 3)
        ]);

        var image = ImgFile.Read(new MemoryStream(original), "roundtrip.img", original.Length);
        var written = image.ToArray();

        CollectionAssert.AreEqual(original, written, "v2 读→写 必须逐字节一致。");

        var reread = ImgFile.Read(new MemoryStream(written), "roundtrip.img", written.Length);
        Assert.AreEqual(image.Frames.Count, reread.Frames.Count);
        for (var i = 0; i < image.Frames.Count; i++)
        {
            Assert.AreEqual(image.Frames[i].X, reread.Frames[i].X, $"帧 {i} X");
            Assert.AreEqual(image.Frames[i].Y, reread.Frames[i].Y, $"帧 {i} Y");
            Assert.AreEqual(image.Frames[i].Width, reread.Frames[i].Width, $"帧 {i} W");
            Assert.AreEqual(image.Frames[i].Height, reread.Frames[i].Height, $"帧 {i} H");
            CollectionAssert.AreEqual(image.Frames[i].RawData, reread.Frames[i].RawData, $"帧 {i} RawData");
        }
    }

    [TestMethod]
    public void Ver2_NoneCompress_LengthIsDerivedFromGeometry()
    {
        // 故意把索引里的 length 写错（1），NONE 模式下读侧必须用 w*h*2 覆盖它。
        var data = TestImageBuilder.BuildVer2(
        [
            new TestImageBuilder.TestFrame
            {
                Type = ColorBits.ARGB_1555,
                Compress = CompressMode.NONE,
                Width = 4,
                Height = 4,
                Length = 1,
                FrameWidth = 4,
                FrameHeight = 4,
                Data = TestImageBuilder.BuildArgb1555Pixels(4, 4, 0)
            }
        ]);

        var image = ImgFile.Read(new MemoryStream(data), "dirty.img", data.Length);

        Assert.AreEqual(32, image.Frames[0].Length, "NONE 时长度应由几何覆盖（4*4*2=32），防脏数据。");
        Assert.AreEqual(32, image.Frames[0].RawData.Length);
    }

    [TestMethod]
    public void Ver1_Read_FrameDataIsInline()
    {
        var data = TestImageBuilder.BuildVer1(
        [
            NormalFrame(5, 5, 2, -4, 9, 9, seed: 0),
            NormalFrame(3, 3, 0, 0, 9, 9, seed: 1),
            LinkFrame(0)
        ]);

        var image = ImgFile.Read(new MemoryStream(data), "v1.img", data.Length);

        Assert.AreEqual(ImgVersion.Ver1, image.Version);
        Assert.AreEqual(3, image.Frames.Count);
        Assert.AreEqual(0L, image.IndexLength, "v1 头部的 indexLength 字段恒为 0。");
        Assert.AreEqual(50, image.Frames[0].RawData.Length, "5*5*2 = 50 字节内联数据。");
        Assert.AreEqual(18, image.Frames[1].RawData.Length, "3*3*2 = 18 字节内联数据。");
        Assert.AreSame(image.Frames[0], image.Frames[2].LinkTarget);
    }

    [TestMethod]
    public void Ver1_Write_ThenRead_IsByteIdentical()
    {
        var original = TestImageBuilder.BuildVer1(
        [
            NormalFrame(5, 5, 2, -4, 9, 9, seed: 0),
            NormalFrame(3, 3, 0, 0, 9, 9, seed: 1),
            LinkFrame(0)
        ]);

        var image = ImgFile.Read(new MemoryStream(original), "v1.img", original.Length);
        var written = image.ToArray();

        CollectionAssert.AreEqual(original, written, "v1 读→写 必须逐字节一致（含恒 0 的 indexLength）。");

        // 头部第 18..21 字节是 indexLength，必须仍是 0（格式约定，不可写成真实长度）。
        for (var i = 18; i < 22; i++)
        {
            Assert.AreEqual((byte)0, written[i], $"v1 头部 offset {i} 必须为 0。");
        }

        var reread = ImgFile.Read(new MemoryStream(written), "v1.img", written.Length);
        CollectionAssert.AreEqual(image.Frames[0].RawData, reread.Frames[0].RawData);
        CollectionAssert.AreEqual(image.Frames[1].RawData, reread.Frames[1].RawData);
    }

    [TestMethod]
    public void Decode_Ver2_ZlibArgb1555_ReturnsImage()
    {
        // 4x4 全紫（ARGB1555：a:1 r:5 g:5 b:5，r 在 bit10-14、b 在 bit0-4）
        // a=1,r=31,g=0,b=31 → 0x8000 | (31 << 10) | 31 = 0xFC1F → 期望 BGRA(255,0,255,255)
        var raw = new byte[4 * 4 * 2];
        for (var i = 0; i < 16; i++)
        {
            raw[i * 2] = 0x1F;
            raw[(i * 2) + 1] = 0xFC;
        }

        var compressed = ZlibCodec.Compress(raw);
        var data = TestImageBuilder.BuildVer2(
        [
            new TestImageBuilder.TestFrame
            {
                Type = ColorBits.ARGB_1555,
                Compress = CompressMode.ZLIB,
                Width = 4,
                Height = 4,
                Length = compressed.Length,
                FrameWidth = 4,
                FrameHeight = 4,
                Data = compressed
            }
        ]);

        var image = ImgFile.Read(new MemoryStream(data), "zlib.img", data.Length);
        var decoded = image.Decode(image.Frames[0]);

        Assert.IsNotNull(decoded);
        Assert.AreEqual(4, decoded.Width);
        Assert.AreEqual(4, decoded.Height);
        Assert.AreEqual(4 * 4 * 4, decoded.Pixels.Length);
        Assert.AreEqual(new Color32(255, 0, 255, 255), decoded.GetPixel(0, 0));
        Assert.AreEqual(new Color32(255, 0, 255, 255), decoded.GetPixel(3, 3));
    }

    [TestMethod]
    public void Decode_LinkFrame_ReturnsNull()
    {
        var data = TestImageBuilder.BuildVer2(
        [
            NormalFrame(4, 4, 0, 0, 8, 8, seed: 0),
            LinkFrame(0)
        ]);

        var image = ImgFile.Read(new MemoryStream(data), "link.img", data.Length);

        Assert.IsNull(image.Decode(image.Frames[1]), "LINK 帧解码必须返回 null（D-04）。");
        Assert.IsNull(image.Encode(image.Frames[1]), "LINK 帧编码必须返回 null（D-04）。");
    }

    [TestMethod]
    public void Encode_NoneCompress_ReturnsUncompressedPixels()
    {
        var raw = TestImageBuilder.BuildArgb1555Pixels(3, 2, seed: 7);
        var data = TestImageBuilder.BuildVer2(
        [
            new TestImageBuilder.TestFrame
            {
                Type = ColorBits.ARGB_1555,
                Compress = CompressMode.NONE,
                Width = 3,
                Height = 2,
                Length = raw.Length,
                FrameWidth = 3,
                FrameHeight = 2,
                Data = raw
            }
        ]);

        var image = ImgFile.Read(new MemoryStream(data), "none.img", data.Length);
        var encoded = image.Encode(image.Frames[0]);

        Assert.IsNotNull(encoded);
        CollectionAssert.AreEqual(raw, encoded, "Encode 应返回未压缩原始像素字节。");
    }

    [TestMethod]
    public void Read_LegalHeaderButTruncatedPayload_Throws()
    {
        var data = TestImageBuilder.BuildVer2([NormalFrame(8, 8, 0, 0, 8, 8, seed: 0)]);
        var truncated = data[..^10]; // 砍掉 10 字节数据

        try
        {
            ImgFile.Read(new MemoryStream(truncated), "truncated.img", truncated.Length);
            Assert.Fail("数据区被截断时应抛出异常（EndOfStreamException 或 ImgFormatException）。");
        }
        catch (Exception ex) when (ex is EndOfStreamException or ImgFormatException)
        {
            // 期望路径
        }
    }

    [TestMethod]
    public void Registry_Get_ReturnsVer2AndFallsBackToOther()
    {
        Assert.IsInstanceOfType<Formats.Handlers.Ver2Handler>(ImgHandlerRegistry.Get(ImgVersion.Ver2));
        Assert.IsInstanceOfType<Formats.Handlers.Ver1Handler>(ImgHandlerRegistry.Get(ImgVersion.Ver1));

        // v5 已实现（DDS 纹理图集）→ 必须拿到 Ver5Handler，而不是整段透传的 OtherHandler。
        Assert.IsInstanceOfType<Formats.Handlers.Ver5Handler>(ImgHandlerRegistry.Get(ImgVersion.Ver5));

        // 仍未实现的版本（v8）→ 回落 OtherHandler（不抛异常，保证"能打开但不可解码"）
        Assert.IsInstanceOfType<Formats.Handlers.OtherHandler>(ImgHandlerRegistry.Get(ImgVersion.Ver8));
    }

    [TestMethod]
    public void Registry_Register_IsIdempotent()
    {
        var before = ImgHandlerRegistry.RegisteredVersions.Count;

        // 重复注册同一版本不应抛异常（D-08）
        ImgHandlerRegistry.Register(ImgVersion.Ver7, static () => new Formats.Handlers.OtherHandler());
        ImgHandlerRegistry.Register(ImgVersion.Ver7, static () => new Formats.Handlers.OtherHandler());

        Assert.AreEqual(before + 1, ImgHandlerRegistry.RegisteredVersions.Count, "重复注册不得产生第二份注册项。");
        Assert.IsInstanceOfType<Formats.Handlers.OtherHandler>(ImgHandlerRegistry.Get(ImgVersion.Ver7));
    }

    private static TestImageBuilder.TestFrame NormalFrame(
        int width,
        int height,
        int x,
        int y,
        int frameWidth,
        int frameHeight,
        byte seed)
    {
        var pixels = TestImageBuilder.BuildArgb1555Pixels(width, height, seed);
        return new TestImageBuilder.TestFrame
        {
            Type = ColorBits.ARGB_1555,
            Compress = CompressMode.NONE,
            Width = width,
            Height = height,
            Length = pixels.Length,
            X = x,
            Y = y,
            FrameWidth = frameWidth,
            FrameHeight = frameHeight,
            Data = pixels
        };
    }

    private static TestImageBuilder.TestFrame LinkFrame(int target)
    {
        return new TestImageBuilder.TestFrame
        {
            Type = ColorBits.LINK,
            LinkTargetIndex = target
        };
    }
}
