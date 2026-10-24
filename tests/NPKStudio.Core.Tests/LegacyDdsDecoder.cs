namespace NPKStudio.Core.Tests;

/// <summary>
///     旧 <c>NPKStudio.Core.Coder.DdsDecoder</c> 的<b>原样搬运</b>（仅测试工程使用）。
///     <para>
///         存在意义：它是唯一可对标的"旧工具观感"基准，用来钉死 <b>R/B 通道槽位</b>与块内像素顺序。
///     </para>
///     <para>
///         <b>实测结论（校正了"旧输出是新输出的转置"这一说法）</b>：
///         旧像素定位是 <c>linear = height * (x + i/4) + (y + i%4)</c>，即
///         <c>row = x_b + i/4</c>、<c>col = y_b + i%4</c>。它把循环变量 <c>x</c> 当行号、<c>y</c> 当列号
///         （与外层 <c>x += 4</c> / 内层 <c>y += 4</c> 的遍历顺序恰好互换），两次互换相消，
///         块内布局与块放置<b>在方形图上完全正确</b>——所以：
///     </para>
///     <list type="bullet">
///         <item><b>width == height</b>：旧输出与新实现<b>逐字节相同</b>（这也是当年没人发现的原因）；</item>
///         <item>
///             <b>width != height</b>：行 stride 用了 <c>height</c> 而不是 <c>width</c>，
///             产生<b>重叠写 + 错位</b>，输出本身就是坏的，<b>不能作为参照</b>。
///             真正的缺陷是"stride 用错"，不是"转置"。
///         </item>
///     </list>
///     <para>
///         <b>刻意只搬 DXT1 与 DXT5</b>：这两种格式旧实现除 stride 外是对的。
///         <b>DXT3 不能这样对标</b>——D-16（alpha 块与色彩块顺序颠倒）、D-17（16 个像素只取到 4 个 alpha）
///         让旧 DXT3 输出连通道与布局都不可信，只能靠 4×4 已知向量硬编码期望值来验。
///     </para>
/// </summary>
[Obsolete("参照实现（旧 DdsDecoder 原样搬运）。仅用于 DXT1/DXT5 交叉校验，禁止在产品代码中调用。")]
internal static class LegacyDdsDecoder
{
    /// <summary>
    ///     旧 DXT1 解码：返回 BGRA 缓冲，但像素被"转置"写入（参照用途）。
    /// </summary>
    public static byte[] DecodeDxt1(byte[] data, int width, int height)
    {
        var buffer = new byte[width * height * 4];
        var position = 0;
        for (var x = 0; x < width; x += 4)
        {
            for (var y = 0; y < height; y += 4)
            {
                var color0 = ReadUInt16(data, ref position);
                var color1 = ReadUInt16(data, ref position);
                var colors = new byte[4][];
                colors[0] = DecodeRgb565(color0);
                colors[1] = DecodeRgb565(color1);
                colors[2] = new byte[4];
                colors[3] = new byte[4];

                if (color0 > color1)
                {
                    colors[2][0] = (byte)((colors[0][0] * 2 + colors[1][0]) / 3);
                    colors[2][1] = (byte)((colors[0][1] * 2 + colors[1][1]) / 3);
                    colors[2][2] = (byte)((colors[0][2] * 2 + colors[1][2]) / 3);
                    colors[2][3] = 0xFF;
                    colors[3][0] = (byte)((colors[0][0] + colors[1][0] * 2) / 3);
                    colors[3][1] = (byte)((colors[0][1] + colors[1][1] * 2) / 3);
                    colors[3][2] = (byte)((colors[0][2] + colors[1][2] * 2) / 3);
                    colors[3][3] = 0xFF;
                }
                else
                {
                    colors[2][0] = (byte)((colors[0][0] + colors[1][0]) / 2);
                    colors[2][1] = (byte)((colors[0][1] + colors[1][1]) / 2);
                    colors[2][2] = (byte)((colors[0][2] + colors[1][2]) / 2);
                    colors[2][3] = (byte)((colors[0][3] + colors[1][3]) / 2);
                }

                var index = ReadInt32(data, ref position);
                for (var i = 0; i < 16; i++, index >>= 2)
                {
                    var j = index & 0x3;
                    // 旧实现的缺陷：行 stride 用了 height（方形图上恰好等于 width 故无害，非方形图上重叠错位）。
                    var pos = 4 * (height * (x + (i / 4)) + y + (i % 4));
                    buffer[pos + 0] = colors[j][0];
                    buffer[pos + 1] = colors[j][1];
                    buffer[pos + 2] = colors[j][2];
                    buffer[pos + 3] = colors[j][3];
                }
            }
        }

        return buffer;
    }

    /// <summary>
    ///     旧 DXT5 解码：返回 BGRA 缓冲，但像素被"转置"写入（参照用途）。
    /// </summary>
    public static byte[] DecodeDxt5(byte[] data, int width, int height)
    {
        var buffer = new byte[width * height * 4];
        var position = 0;
        for (var x = 0; x < width; x += 4)
        {
            for (var y = 0; y < height; y += 4)
            {
                var alphas = new byte[8];
                alphas[0] = data[position++];
                alphas[1] = data[position++];
                if (alphas[0] > alphas[1])
                {
                    alphas[2] = (byte)((6 * alphas[0] + 1 * alphas[1]) / 7);
                    alphas[3] = (byte)((5 * alphas[0] + 2 * alphas[1]) / 7);
                    alphas[4] = (byte)((4 * alphas[0] + 3 * alphas[1]) / 7);
                    alphas[5] = (byte)((3 * alphas[0] + 4 * alphas[1]) / 7);
                    alphas[6] = (byte)((2 * alphas[0] + 5 * alphas[1]) / 7);
                    alphas[7] = (byte)((1 * alphas[0] + 6 * alphas[1]) / 7);
                }
                else
                {
                    alphas[2] = (byte)((4 * alphas[0] + 1 * alphas[1]) / 5);
                    alphas[3] = (byte)((3 * alphas[0] + 2 * alphas[1]) / 5);
                    alphas[4] = (byte)((2 * alphas[0] + 3 * alphas[1]) / 5);
                    alphas[5] = (byte)((1 * alphas[0] + 4 * alphas[1]) / 5);
                    alphas[6] = 0x00;
                    alphas[7] = 0xFF;
                }

                // alpha 通道索引：每个 3 bit，共 48 bit
                var bit = Read6Byte(data, ref position);

                var color0 = ReadUInt16(data, ref position);
                var color1 = ReadUInt16(data, ref position);

                var colors = new byte[4][];
                colors[0] = DecodeRgb565(color0);
                colors[1] = DecodeRgb565(color1);
                colors[2] = new byte[4];
                colors[3] = new byte[4];
                for (var i = 0; i < 3; i++)
                {
                    colors[2][i] = (byte)((colors[0][i] * 2 + colors[1][i]) / 3);
                    colors[3][i] = (byte)((colors[0][i] + colors[1][i] * 2) / 3);
                }

                var index = ReadInt32(data, ref position);
                for (var i = 0; i < 16; i++, index >>= 2, bit >>= 3)
                {
                    var j = index & 0x3;
                    var pos = 4 * (height * (x + (i / 4)) + y + (i % 4));
                    var a = alphas[bit & 0x7];
                    buffer[pos + 0] = colors[j][0];
                    buffer[pos + 1] = colors[j][1];
                    buffer[pos + 2] = colors[j][2];
                    buffer[pos + 3] = a;
                }
            }
        }

        return buffer;
    }

    /// <summary>
    ///     旧 RGB565 扩展。注意变量名与规范相反：<c>r</c> 取低 5 位（规范是 B）、<c>b</c> 取高 5 位（规范是 R）；
    ///     调用方随后把 <c>[0]</c> 写进 BGRA 的 B 槽、<c>[2]</c> 写进 R 槽，两次相反恰好抵消。
    /// </summary>
    public static byte[] DecodeRgb565(ushort color)
    {
        var r = (byte)(color & 0x1F);
        var g = (byte)((color >> 5) & 0x3F);
        var b = (byte)((color >> 11) & 0x1F);

        r = (byte)((r << 3) | (r >> 2));
        g = (byte)((g << 2) | (g >> 4));
        b = (byte)((b << 3) | (b >> 2));
        return [r, g, b, 0xFF];
    }

    private static ulong Read6Byte(byte[] data, ref int position)
    {
        var buffer = new byte[8];
        Array.Copy(data, position, buffer, 0, 6);
        position += 6;
        return BitConverter.ToUInt64(buffer, 0);
    }

    private static ushort ReadUInt16(byte[] data, ref int position)
    {
        var value = (ushort)(data[position] | (data[position + 1] << 8));
        position += 2;
        return value;
    }

    private static int ReadInt32(byte[] data, ref int position)
    {
        var value = data[position]
                    | (data[position + 1] << 8)
                    | (data[position + 2] << 16)
                    | (data[position + 3] << 24);
        position += 4;
        return value;
    }
}
