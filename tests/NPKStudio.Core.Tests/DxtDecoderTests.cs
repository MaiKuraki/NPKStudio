using NPKStudio.Core.Imaging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     <see cref="DxtDecoder" /> 的已知向量测试：输入 8/16 字节的单块，硬编码 16 个像素的 BGRA 期望值。
///     <para>
///         共用色彩块：<c>c0 = 0xF800</c>（RGB565 → R=255,G=0,B=0），<c>c1 = 0x001F</c>（R=0,G=0,B=255），
///         索引字 <c>0xE4E4E4E4</c> → 每行 4 个像素的调色板下标依次为 0,1,2,3。
///         <c>c0 &gt; c1</c>，故 4 色模式：
///         color2 = (2*c0 + c1)/3 = (R 170, G 0, B 85)，color3 = (c0 + 2*c1)/3 = (R 85, G 0, B 170)。
///     </para>
/// </summary>
[TestClass]
public sealed class DxtDecoderTests
{
    [TestMethod]
    public void Dxt1_KnownVector_4x4Block()
    {
        // 块布局：c0(2) c1(2) index(4)
        byte[] block =
        [
            0x00, 0xF8, // c0 = 0xF800 → R=255,G=0,B=0
            0x1F, 0x00, // c1 = 0x001F → R=0,G=0,B=255
            0xE4, 0xE4, 0xE4, 0xE4 // 每行下标 0,1,2,3
        ];

        var pixels = DxtDecoder.DecodeToNewImage(block, 4, 4, DxtFormat.Dxt1);

        // 每行（4 像素 × BGRA）完全相同，共 4 行
        byte[] row =
        [
            0, 0, 255, 255, // x=0 → color0 (R255) → B=0,G=0,R=255,A=255
            255, 0, 0, 255, // x=1 → color1 (B255) → B=255,G=0,R=0,A=255
            85, 0, 170, 255, // x=2 → color2 (R170,B85)
            170, 0, 85, 255 // x=3 → color3 (R85,B170)
        ];

        var expected = new byte[64];
        for (var y = 0; y < 4; y++)
        {
            Array.Copy(row, 0, expected, y * 16, 16);
        }

        CollectionAssert.AreEqual(expected, pixels, "DXT1 单块解码结果与手工推算不一致。");
    }

    [TestMethod]
    public void Dxt1_ThreeColorMode_Index3IsTransparent()
    {
        // c0 = 0x001F < c1 = 0xF800 → 3 色模式：color2 为均值，color3 为全透明黑
        byte[] block =
        [
            0x1F, 0x00, // c0 = 0x001F → R=0,G=0,B=255
            0x00, 0xF8, // c1 = 0xF800 → R=255,G=0,B=0
            0xE4, 0xE4, 0xE4, 0xE4 // 每行下标 0,1,2,3
        ];

        var pixels = DxtDecoder.DecodeToNewImage(block, 4, 4, DxtFormat.Dxt1);

        // x=0 → color0 = (R0,B255)；x=1 → color1 = (R255,B0)
        CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, pixels[..4], "x=0 应为 color0");
        CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 255 }, pixels[4..8], "x=1 应为 color1");

        // x=2 → 均值 (127,0,127)
        CollectionAssert.AreEqual(new byte[] { 127, 0, 127, 255 }, pixels[8..12], "x=2 应为两色均值");

        // 三色模式下 index2 必须不透明（alpha 恒 0xFF）；只有 index3 才是透明黑。
        Assert.AreEqual<byte>(0xFF, pixels[11], "三色模式 index2 的 alpha 必须是 0xFF，不能跟着变透明。");

        // x=3 → 全透明黑
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0 }, pixels[12..16], "x=3 应为全透明");
    }

    [TestMethod]
    public void Dxt3_KnownVector_4x4Block()
    {
        // 块布局：alpha(8) + 色彩(8)；alpha 为 16 个 4 位值，像素 i 取第 i 个半字节。
        // 半字节序列 = 1,2,3,...,F,0 → alpha 字节按 (v<<4)|v 扩展为 0x11,0x22,...,0xFF,0x00
        byte[] block =
        [
            0x21, 0x43, // 像素 0..3 → 1,2,3,4
            0x65, 0x87, // 像素 4..7 → 5,6,7,8
            0xA9, 0xCB, // 像素 8..11 → 9,A,B,C
            0xED, 0x0F, // 像素 12..15 → D,E,F,0
            0x00, 0xF8, // c0 = 0xF800
            0x1F, 0x00, // c1 = 0x001F
            0xE4, 0xE4, 0xE4, 0xE4 // 每行下标 0,1,2,3
        ];

        byte[] expected =
        [
            // 行 0（alpha 17,34,51,68）
            0, 0, 255, 17,
            255, 0, 0, 34,
            85, 0, 170, 51,
            170, 0, 85, 68,
            // 行 1（alpha 85,102,119,136）
            0, 0, 255, 85,
            255, 0, 0, 102,
            85, 0, 170, 119,
            170, 0, 85, 136,
            // 行 2（alpha 153,170,187,204）
            0, 0, 255, 153,
            255, 0, 0, 170,
            85, 0, 170, 187,
            170, 0, 85, 204,
            // 行 3（alpha 221,238,255,0）
            0, 0, 255, 221,
            255, 0, 0, 238,
            85, 0, 170, 255,
            170, 0, 85, 0
        ];

        var pixels = DxtDecoder.DecodeToNewImage(block, 4, 4, DxtFormat.Dxt3);

        CollectionAssert.AreEqual(expected, pixels, "DXT3 单块解码结果与手工推算不一致（alpha 顺序/右移可能错）。");
    }

    [TestMethod]
    public void Dxt5_KnownVector_4x4Block()
    {
        // 块布局：alpha(8) + 色彩(8)。
        // a0 = 0xFF, a1 = 0x00，a0 > a1 → 8 值插值模式（分母 7，整除截断）：
        //   table = {255, 0, 218, 182, 145, 109, 72, 36}
        // 索引 48 位小端，像素 i 取 3 位，本向量为 i%8 → 字节 88 C6 FA 88 C6 FA
        byte[] block =
        [
            0xFF, 0x00, // a0, a1
            0x88, 0xC6, 0xFA, 0x88, 0xC6, 0xFA, // 16 × 3 位索引 = 0,1,2,3,4,5,6,7 循环
            0x00, 0xF8, // c0 = 0xF800
            0x1F, 0x00, // c1 = 0x001F
            0xE4, 0xE4, 0xE4, 0xE4 // 每行下标 0,1,2,3
        ];

        byte[] expected =
        [
            // 行 0（alpha 索引 0,1,2,3 → 255,0,218,182）
            0, 0, 255, 255,
            255, 0, 0, 0,
            85, 0, 170, 218,
            170, 0, 85, 182,
            // 行 1（alpha 索引 4,5,6,7 → 145,109,72,36）
            0, 0, 255, 145,
            255, 0, 0, 109,
            85, 0, 170, 72,
            170, 0, 85, 36,
            // 行 2（重新从索引 0 开始）
            0, 0, 255, 255,
            255, 0, 0, 0,
            85, 0, 170, 218,
            170, 0, 85, 182,
            // 行 3
            0, 0, 255, 145,
            255, 0, 0, 109,
            85, 0, 170, 72,
            170, 0, 85, 36
        ];

        var pixels = DxtDecoder.DecodeToNewImage(block, 4, 4, DxtFormat.Dxt5);

        CollectionAssert.AreEqual(expected, pixels, "DXT5 单块解码结果与手工推算不一致。");
    }

    [TestMethod]
    public void NonSquare_8x4_NotTransposed()
    {
        // 8x4：横向两个块。左块全 color0(R255)，右块全 color1(B255)。
        // 若实现把 height 当 stride 或交换 x/y，右侧块会写到错误位置。
        byte[] blockRed =
        [
            0x00, 0xF8, // c0 = 0xF800 → R=255
            0x1F, 0x00, // c1 = 0x001F
            0x00, 0x00, 0x00, 0x00 // 全部下标 0 → color0
        ];
        byte[] blockBlue =
        [
            0x1F, 0x00, // c0 = 0x001F
            0x00, 0xF8, // c1 = 0xF800
            0x00, 0x00, 0x00, 0x00 // 全部下标 0 → color0 = 0x001F → B=255
        ];

        var data = new byte[16];
        Array.Copy(blockRed, 0, data, 0, 8);
        Array.Copy(blockBlue, 0, data, 8, 8);

        var pixels = DxtDecoder.DecodeToNewImage(data, 8, 4, DxtFormat.Dxt1);

        // 左半（x<4）为红，右半（x>=4）为蓝
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var offset = (y * 8 + x) * 4;
                var expectedB = x < 4 ? (byte)0 : (byte)255;
                var expectedR = x < 4 ? (byte)255 : (byte)0;
                Assert.AreEqual(expectedB, pixels[offset], $"({x},{y}) B");
                Assert.AreEqual((byte)0, pixels[offset + 1], $"({x},{y}) G");
                Assert.AreEqual(expectedR, pixels[offset + 2], $"({x},{y}) R");
                Assert.AreEqual((byte)255, pixels[offset + 3], $"({x},{y}) A");
            }
        }
    }

    [TestMethod]
    public void NonSquare_4x8_BlockOrderIsRowMajor()
    {
        // 4x8：纵向两块（blocksX=1, blocksY=2）。数据流第 0 块必须落在**上**半，第 1 块落下半。
        // 若实现按列优先消耗块（旧实现的 x 外层循环），第 1 块会被放到并不存在的"右列"。
        byte[] blockRed =
        [
            0x00, 0xF8, // c0 = 0xF800 → R=255
            0x1F, 0x00, // c1 = 0x001F
            0x00, 0x00, 0x00, 0x00 // 全部下标 0 → color0 = 红
        ];
        byte[] blockBlue =
        [
            0x1F, 0x00, // c0 = 0x001F → B=255
            0x00, 0xF8, // c1 = 0xF800
            0x00, 0x00, 0x00, 0x00 // 全部下标 0 → color0 = 蓝
        ];

        var data = new byte[16];
        Array.Copy(blockRed, 0, data, 0, 8);
        Array.Copy(blockBlue, 0, data, 8, 8);

        var pixels = DxtDecoder.DecodeToNewImage(data, 4, 8, DxtFormat.Dxt1);

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var offset = ((y * 4) + x) * 4;
                var expectedB = y < 4 ? (byte)0 : (byte)255;
                Assert.AreEqual(expectedB, pixels[offset], $"({x},{y}) 应来自第 {y / 4} 块（行优先消耗块）。");
            }
        }
    }

    [TestMethod]
    public void Decode_Dxt5_TwoEndpointMode_UsesSixValuesPlusBlackAndWhite()
    {
        // a0 = 0x00 <= a1 = 0xFF → 6 值模式，索引 6 → 0x00、索引 7 → 0xFF
        byte[] alphaBlock =
        [
            0x00, 0xFF, // a0, a1
            0x88, 0xC6, 0xFA, 0x88, 0xC6, 0xFA // 索引 0..7 循环
        ];
        var alphas = new byte[16];
        DxtDecoder.DecodeDxt5Alpha(alphaBlock, alphas);

        // 6 值模式插值（分母 5）：a2=(4*0+255)/5=51, a3=(3*0+2*255)/5=102, a4=153, a5=204, a6=0, a7=255
        byte[] expected =
        [
            0x00, 0xFF, 51, 102, 153, 204, 0x00, 0xFF,
            0x00, 0xFF, 51, 102, 153, 204, 0x00, 0xFF
        ];
        CollectionAssert.AreEqual(expected, alphas);
    }

    [TestMethod]
    public void Decode_ThrowsWhenDataTooShort()
    {
        var shortData = new byte[8]; // 4x4 DXT3 需要 16 字节

        try
        {
            DxtDecoder.DecodeToNewImage(shortData, 4, 4, DxtFormat.Dxt3);
            Assert.Fail("数据不足时应抛出 ImgFormatException。");
        }
        catch (NPKStudio.Core.Exceptions.ImgFormatException)
        {
            // 期望路径
        }
    }

    [TestMethod]
    public void DecodeRgb565_MaxValuesExpandTo255()
    {
        Assert.AreEqual(new Color32(255, 255, 255, 255), DxtDecoder.DecodeRgb565(0xFFFF));
        Assert.AreEqual(new Color32(255, 0, 0, 255), DxtDecoder.DecodeRgb565(0xF800));
        Assert.AreEqual(new Color32(0, 255, 0, 255), DxtDecoder.DecodeRgb565(0x07E0));
        Assert.AreEqual(new Color32(0, 0, 255, 255), DxtDecoder.DecodeRgb565(0x001F));
    }

    /// <summary>
    ///     通道落点哨兵：直接断言 <b>BGRA 四字节</b>（[0]=B,[1]=G,[2]=R,[3]=A），
    ///     走完整链路 <c>DecodeRgb565 → SetPixel → Pixels</c>，而不是只比 <see cref="Color32" /> 字段。
    ///     <para>
    ///         <c>0xF800</c> 与 <c>0x001F</c> 互为字节倒序，R/B 若互换会同时变红；
    ///         <c>0xF81F</c>（紫）再补一道保险；<c>0x07E0</c> 单钉 6 位绿的扩展公式 <c>(v&lt;&lt;2)|(v&gt;&gt;4)</c>。
    ///     </para>
    /// </summary>
    [TestMethod]
    [DataRow((ushort)0xF800, (byte)0, (byte)0, (byte)255, (byte)255, DisplayName = "纯红：R 在 index 2")]
    [DataRow((ushort)0x001F, (byte)255, (byte)0, (byte)0, (byte)255, DisplayName = "纯蓝：B 在 index 0")]
    [DataRow((ushort)0xF81F, (byte)255, (byte)0, (byte)255, (byte)255, DisplayName = "紫：R/B 同时非零")]
    [DataRow((ushort)0x07E0, (byte)0, (byte)255, (byte)0, (byte)255, DisplayName = "纯绿：6 位通道")]
    public void DecodeRgb565_ChannelSlots_BgraSentinel(ushort value, byte expectedB, byte expectedG, byte expectedR, byte expectedA)
    {
        var color = DxtDecoder.DecodeRgb565(value);

        var image = new RgbaImage(1, 1);
        image.SetPixel(0, 0, color);

        Assert.AreEqual(expectedB, image.Pixels[0], "B（index 0）");
        Assert.AreEqual(expectedG, image.Pixels[1], "G（index 1）");
        Assert.AreEqual(expectedR, image.Pixels[2], "R（index 2）");
        Assert.AreEqual(expectedA, image.Pixels[3], "A（index 3）");
    }

    [TestMethod]
    public void Decode_IntoExistingRgbaImage_WritesThroughSetPixel()
    {
        // 架构约束：解码器只经 RgbaImage.SetPixel 落盘，不得自己算偏移。
        // 这里用非零初始值的目标图像验证"整块被正确覆盖 + 坐标对应正确"。
        var target = new RgbaImage(4, 4);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                target.SetPixel(x, y, new Color32(7, 7, 7, 7));
            }
        }

        byte[] block =
        [
            0x00, 0xF8, // c0 = 0xF800 → R=255
            0x1F, 0x00, // c1 = 0x001F → B=255
            0xE4, 0xE4, 0xE4, 0xE4 // 每行下标 0,1,2,3
        ];

        DxtDecoder.Decode(block, 4, 4, DxtFormat.Dxt1, target);

        Assert.AreEqual(new Color32(255, 0, 0, 255), target.GetPixel(0, 3), "左下应被 color0 覆盖");
        Assert.AreEqual(new Color32(0, 0, 255, 255), target.GetPixel(1, 0), "右上应被 color1 覆盖");
        Assert.AreEqual(new Color32(170, 0, 85, 255), target.GetPixel(2, 2), "(2,2) 应为 color2");
        Assert.AreEqual(new Color32(85, 0, 170, 255), target.GetPixel(3, 1), "(3,1) 应为 color3");
        Assert.AreEqual(64, target.Pixels.Length, "解码不得改变目标图像缓冲长度。");
    }

    [TestMethod]
    public void Decode_TargetSizeMismatch_Throws()
    {
        var target = new RgbaImage(8, 8);

        try
        {
            DxtDecoder.Decode(new byte[8], 4, 4, DxtFormat.Dxt1, target);
            Assert.Fail("目标尺寸与参数不一致时应抛出 ArgumentException。");
        }
        catch (ArgumentException)
        {
            // 期望路径
        }
    }
}
