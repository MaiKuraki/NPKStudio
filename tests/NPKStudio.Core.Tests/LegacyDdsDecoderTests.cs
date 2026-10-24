using NPKStudio.Core.Imaging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

#pragma warning disable CS0618 // 参照实现标记为 Obsolete，此处正是要调用它做交叉校验

namespace NPKStudio.Core.Tests;

/// <summary>
///     DXT1 / DXT5 与旧 <c>DdsDecoder</c> 的交叉校验。
///     <para>
///         <b>实测校正了"旧实现是新实现的转置"这个前提</b>（详细推导见
///         <see cref="LegacyDdsDecoder" /> 的类型注释）。真实关系是：
///     </para>
///     <list type="bullet">
///         <item>
///             <b>正方形图像（width == height）</b>：旧实现与新实现<b>逐字节完全相同</b>。
///             原因：旧循环把 <c>x</c> 当行号、<c>y</c> 当列号（与 `+=4` 的遍历顺序恰好互换），
///             两次互换相消；而 <c>linear = height * row + col</c> 在方形图上正好等于正确 stride。
///             所以这里可以做<b>逐字节相等</b>断言，钉死 R/B 通道槽位与块内像素顺序。
///         </item>
///         <item>
///             <b>非正方形图像</b>：旧实现的行 stride 用了 <c>height</c> 而非 <c>width</c>，
///             产生<b>重叠写 + 错位</b>，输出本身就是坏的，<b>不能作为参照</b>。
///             该结论由 <see cref="Legacy_NonSquare_IsBroken_SoNotReference" /> 固化，
///             防止后人把新实现"改回去对齐旧行为"。
///         </item>
///     </list>
///     <para>
///         <b>DXT3 不在此列</b>：D-16（alpha/色彩块顺序颠倒）与 D-17（16 像素只取到 4 个 alpha）
///         让旧 DXT3 输出连"通道和布局"都不可信，只能靠 4×4 已知向量硬编码验。
///     </para>
/// </summary>
[TestClass]
public sealed class LegacyDdsDecoderTests
{
    [TestMethod]
    public void Legacy_Dxt1_SquareImage_MatchesFreshByteForByte()
    {
        var blocks = new byte[4][];
        blocks[0] = Dxt1Block(0xF800, 0x001F, 0xE4E4E4E4); // 红 / 蓝，行内下标 0,1,2,3
        blocks[1] = Dxt1Block(0x07E0, 0x001F, 0x00000000); // 绿，全部下标 0
        blocks[2] = Dxt1Block(0xFFFF, 0x0000, 0x00000000); // 白，全部下标 0
        blocks[3] = Dxt1Block(0x001F, 0x0000, 0x00000000); // 蓝，全部下标 0
        var data = Concat(blocks);

        var legacy = LegacyDdsDecoder.DecodeDxt1(data, 8, 8);

        var fresh = new RgbaImage(8, 8);
        DxtDecoder.Decode(data, 8, 8, DxtFormat.Dxt1, fresh);

        // 反空断言：四个象限必须是不同颜色，否则"块放置正确"这件事根本没被验到。
        Assert.AreEqual(new Color32(255, 0, 0, 255), fresh.GetPixel(0, 0), "左上应为第 0 块（红）");
        Assert.AreEqual(new Color32(0, 255, 0, 255), fresh.GetPixel(4, 0), "右上应为第 1 块（绿）");
        Assert.AreEqual(new Color32(255, 255, 255, 255), fresh.GetPixel(0, 4), "左下应为第 2 块（白）");
        Assert.AreEqual(new Color32(0, 0, 255, 255), fresh.GetPixel(7, 7), "右下应为第 3 块（蓝）");

        // 核心断言：通道槽位（含 B/R）与块内像素顺序必须与旧工具一致。
        CollectionAssert.AreEqual(legacy, fresh.ToBgra32(), "方形图上 DXT1 输出应与旧 DdsDecoder 逐字节一致（含 R/B 槽位）。");
    }

    [TestMethod]
    public void Legacy_Dxt5_SquareImage_MatchesFreshByteForByte()
    {
        var blocks = new byte[4][];
        // b0：8 值 alpha 模式（a0 > a1）；b1：6 值模式（a0 <= a1）；b2/b3：端点相等/近满
        blocks[0] = Dxt5Block(0xFF, 0x00, [0x88, 0xC6, 0xFA, 0x88, 0xC6, 0xFA], 0xF800, 0x001F, 0xE4E4E4E4);
        blocks[1] = Dxt5Block(0x00, 0xFF, [0x88, 0xC6, 0xFA, 0x88, 0xC6, 0xFA], 0x07E0, 0x001F, 0x00000000);
        blocks[2] = Dxt5Block(0xFF, 0x80, [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC], 0xFFFF, 0x0000, 0x00000000);
        blocks[3] = Dxt5Block(0xF0, 0xF0, [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF], 0x001F, 0x0000, 0x00000000);
        var data = Concat(blocks);

        var legacy = LegacyDdsDecoder.DecodeDxt5(data, 8, 8);

        var fresh = new RgbaImage(8, 8);
        DxtDecoder.Decode(data, 8, 8, DxtFormat.Dxt5, fresh);

        // 反空断言：alpha 必须真的各不相同，否则 alpha 通道的对齐没被验到。
        Assert.AreEqual<byte>(255, fresh.GetPixel(0, 0).A, "第 0 块首像素 alpha 应为 255（8 值模式 a0）");
        Assert.AreEqual<byte>(0, fresh.GetPixel(4, 0).A, "第 1 块首像素 alpha 应为 0（6 值模式 a0=0x00）");

        CollectionAssert.AreEqual(legacy, fresh.ToBgra32(), "方形图上 DXT5 输出应与旧 DdsDecoder 逐字节一致（含 R/B 槽位与 alpha）。");
    }

    [TestMethod]
    public void Legacy_NonSquare_IsBroken_SoNotReference()
    {
        // 8x4：旧实现行 stride 用了 height(=4) 而不是 width(=8)，
        // 两块的数据会重叠写到同一段线性区间 → 输出不可用。
        // 本用例把这个事实固化：新实现的 8x4 输出必须**不等于**旧实现，
        // 否则说明有人为了"对齐旧行为"把 stride bug 搬了回来。
        var data = Concat([Dxt1Block(0xF800, 0x001F, 0xE4E4E4E4), Dxt1Block(0x07E0, 0x001F, 0x1B1B1B1B)]);

        var legacy = LegacyDdsDecoder.DecodeDxt1(data, 8, 4);

        var fresh = new RgbaImage(8, 4);
        DxtDecoder.Decode(data, 8, 4, DxtFormat.Dxt1, fresh);

        CollectionAssert.AreNotEqual(legacy, fresh.ToBgra32(),
            "旧实现在非正方形图上有 stride 缺陷，输出必须与新实现不同；相等说明新实现被改回了旧 bug。");

        // 新实现的正确结果：左半（x<4）第 0 块（红），右半第 1 块（绿）。
        Assert.AreEqual(new Color32(255, 0, 0, 255), fresh.GetPixel(0, 0));
        Assert.AreEqual(new Color32(0, 255, 0, 255), fresh.GetPixel(7, 3));
    }

    /// <summary>
    ///     构造 8 字节 DXT1 色彩块：c0、c1 与 4 像素索引字（小端）。
    /// </summary>
    private static byte[] Dxt1Block(ushort c0, ushort c1, uint indices)
    {
        return
        [
            (byte)(c0 & 0xFF), (byte)(c0 >> 8),
            (byte)(c1 & 0xFF), (byte)(c1 >> 8),
            (byte)(indices & 0xFF), (byte)((indices >> 8) & 0xFF),
            (byte)((indices >> 16) & 0xFF), (byte)((indices >> 24) & 0xFF)
        ];
    }

    /// <summary>
    ///     构造 16 字节 DXT5 块：8 字节 alpha（2 端点 + 48 位索引）+ 8 字节色彩块。
    /// </summary>
    private static byte[] Dxt5Block(byte a0, byte a1, byte[] alphaIndices, ushort c0, ushort c1, uint colorIndices)
    {
        var result = new byte[16];
        result[0] = a0;
        result[1] = a1;
        Array.Copy(alphaIndices, 0, result, 2, 6);
        var color = Dxt1Block(c0, c1, colorIndices);
        Array.Copy(color, 0, result, 8, 8);
        return result;
    }

    private static byte[] Concat(byte[][] blocks)
    {
        var total = 0;
        foreach (var block in blocks)
        {
            total += block.Length;
        }

        var result = new byte[total];
        var offset = 0;
        foreach (var block in blocks)
        {
            block.CopyTo(result, offset);
            offset += block.Length;
        }

        return result;
    }
}

#pragma warning restore CS0618
