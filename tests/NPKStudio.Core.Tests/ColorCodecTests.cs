using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     <see cref="ColorCodec" /> 的编解码测试。
///     <para>重点覆盖 D-02：旧实现 ARGB4444 编码先 <c>a &gt;&gt;= 7</c> 且缺掩码，导致 alpha 被摧毁。</para>
/// </summary>
[TestClass]
public sealed class ColorCodecTests
{
    [TestMethod]
    public void Argb1555_Decode_KnownValues()
    {
        // 0xFFFF：a=1 r=31 g=31 b=31 → 全不透明白
        AssertColor(new Color32(255, 255, 255, 255), ColorCodec.DecodeArgb1555(0xFFFF));

        // 0x001F：低字节 = 0x1F(全蓝)，高字节 = 0x00(a=0 r=0 g低2位=0)
        AssertColor(new Color32(0, 0, 255, 0), ColorCodec.DecodeArgb1555(0x001F));

        // 0xFC00：高字节 0xFC → a=(0xFC>>7)&1=1，r=(0xFC>>2)&0x1F=31 → 不透明纯红
        AssertColor(new Color32(255, 0, 0, 255), ColorCodec.DecodeArgb1555(0xFC00));

        // 0x03E0：低字节 0xE0、高字节 0x03 → g = (0xE0>>5)|((0x03&3)<<3) = 7|24 = 31 → 纯绿（alpha 0）
        AssertColor(new Color32(0, 255, 0, 0), ColorCodec.DecodeArgb1555(0x03E0));
    }

    [TestMethod]
    public void Argb1555_Encode_IsInverseOfDecode_ForAllWords()
    {
        // 5 位通道扩展 (v<<3)|(v>>2) 可被 >>3 无损还原，故对全部 65536 个字都满足往返一致。
        for (var word = 0; word <= ushort.MaxValue; word++)
        {
            var color = ColorCodec.DecodeArgb1555((ushort)word);
            var encoded = ColorCodec.EncodeArgb1555(color);
            Assert.AreEqual((ushort)word, encoded, $"ARGB1555 往返失败：word=0x{word:X4}，decoded={color}");
        }
    }

    [TestMethod]
    public void Argb4444_Decode_KnownValues()
    {
        // 0xF00F：高字节 0xF0 → a=0xF0=240、r=0；低字节 0x0F → g=0、b=(0x0F<<4)=240
        AssertColor(new Color32(0, 0, 240, 240), ColorCodec.DecodeArgb4444(0xF00F));

        // 0x0FF0：高字节 0x0F → a=0、r=240；低字节 0xF0 → g=240、b=0
        AssertColor(new Color32(240, 240, 0, 0), ColorCodec.DecodeArgb4444(0x0FF0));
    }

    [TestMethod]
    public void Argb4444_Encode_PreservesAlpha()
    {
        // D-02 回归点：alpha=0xF0 必须被完整保留为 0xF0，不能被 >>7 变成 0x01。
        var color = ColorCodec.DecodeArgb4444(0xF00F);
        Assert.AreEqual<byte>(0xF0, color.A, "解码后的 alpha 应为 240，而不是 0/16。");
        Assert.AreEqual<byte>(0xF0, color.B);
        Assert.AreEqual<byte>(0x00, color.G);
        Assert.AreEqual<byte>(0x00, color.R);

        var encoded = ColorCodec.EncodeArgb4444(color);
        Assert.AreEqual<ushort>(0xF00F, encoded, "ARGB4444 编码未保留 alpha（D-02）。");
    }

    [TestMethod]
    public void Argb4444_Encode_IsInverseOfDecode_ForAllWords()
    {
        for (var word = 0; word <= ushort.MaxValue; word++)
        {
            var color = ColorCodec.DecodeArgb4444((ushort)word);
            var encoded = ColorCodec.EncodeArgb4444(color);
            Assert.AreEqual((ushort)word, encoded, $"ARGB4444 往返失败：word=0x{word:X4}，decoded={color}");
        }
    }

    [TestMethod]
    public void Argb8888_DecodeAndEncode_KeepBgraOrder()
    {
        // 文件中的 ARGB8888 就是 BGRA 字节序：B=0x10 G=0x20 R=0x30 A=0x40
        byte[] source = [0x10, 0x20, 0x30, 0x40];
        var color = ColorCodec.DecodeColor(source, ColorBits.ARGB_8888);
        AssertColor(new Color32(0x30, 0x20, 0x10, 0x40), color);

        var destination = new byte[4];
        ColorCodec.EncodeColor(color, ColorBits.ARGB_8888, destination);
        CollectionAssert.AreEqual(source, destination, "ARGB8888 编码未还原原始 BGRA 字节。");
    }

    [TestMethod]
    public void Decode_Bulk_ProducesBgraForEachFormat()
    {
        // 两个像素：不透明红 + 半透明蓝（4444 的 a=0x8 → 0x88）
        byte[] rgb1555 = [0x00, 0xFC, 0x1F, 0x80];
        var from1555 = ColorCodec.Decode(rgb1555, ColorBits.ARGB_1555, 2);
        Assert.AreEqual(8, from1555.Length);
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x00, 0xFF, 0xFF }, from1555[..4], "1555 第 0 个像素");
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0x00, 0x00, 0xFF }, from1555[4..], "1555 第 1 个像素");

        byte[] rgb4444 = [0x0F, 0xF0, 0xF0, 0x8F];
        var from4444 = ColorCodec.Decode(rgb4444, ColorBits.ARGB_4444, 2);
        CollectionAssert.AreEqual(new byte[] { 0xF0, 0x00, 0x00, 0xF0 }, from4444[..4], "4444 第 0 个像素");
        CollectionAssert.AreEqual(new byte[] { 0x00, 0xF0, 0xF0, 0x80 }, from4444[4..], "4444 第 1 个像素");

        var encoded1555 = ColorCodec.Encode(from1555, ColorBits.ARGB_1555);
        CollectionAssert.AreEqual(rgb1555, encoded1555, "1555 批量编码往返失败");
    }

    [TestMethod]
    public void BytesPerPixel_ReturnsExpectedSizes()
    {
        Assert.AreEqual(2, ColorCodec.BytesPerPixel(ColorBits.ARGB_1555));
        Assert.AreEqual(2, ColorCodec.BytesPerPixel(ColorBits.ARGB_4444));
        Assert.AreEqual(4, ColorCodec.BytesPerPixel(ColorBits.ARGB_8888));
    }

    private static void AssertColor(Color32 expected, Color32 actual, string? message = null)
    {
        Assert.AreEqual(expected.R, actual.R, $"{message} R");
        Assert.AreEqual(expected.G, actual.G, $"{message} G");
        Assert.AreEqual(expected.B, actual.B, $"{message} B");
        Assert.AreEqual(expected.A, actual.A, $"{message} A");
    }
}
