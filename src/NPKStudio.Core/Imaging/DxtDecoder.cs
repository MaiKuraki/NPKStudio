using NPKStudio.Core.Exceptions;

namespace NPKStudio.Core.Imaging;

/// <summary>
///     DDS 纹理使用的块压缩格式。
/// </summary>
public enum DxtFormat
{
    /// <summary>DXT1（BC1）：每块 8 字节，1 位 alpha，可选 3 色 + 透明模式。</summary>
    Dxt1,

    /// <summary>DXT3（BC2）：每块 16 字节，显式 4 位 alpha。</summary>
    Dxt3,

    /// <summary>DXT5（BC3）：每块 16 字节，插值 3 位 alpha。</summary>
    Dxt5
}

/// <summary>
///     DXT1/DXT3/DXT5 解码器，按 Khronos（S3TC / BCn）规范重写，输出 BGRA8888。
///     <para>
///         <b>块内像素顺序</b>：块内索引 <c>i</c>（0..15）对应 <c>x = i % 4</c>、<c>y = i / 4</c>。
///         绝对坐标由 <see cref="RgbaImage.SetPixel" /> 唯一负责寻址，
///         本类<b>不出现任何手写字节偏移</b>（D-18 的根因就是解码器自己算 <c>4*(height*..+..)</c>）。
///     </para>
///     <para>
///         <b>块字节顺序</b>：DXT3/DXT5 先 8 字节 alpha 块、后 8 字节色彩块（D-16：原实现颠倒）；
///         DXT1 只有 8 字节色彩块。DXT3/DXT5 的色彩块<b>恒</b>用 4 色插值（不比较 c0/c1），
///         只有 DXT1 才按 <c>c0 &gt; c1</c> 分流三色 + 透明模式。
///     </para>
///     <para>
///         <b>输出字节语义与旧 <c>DdsDecoder</c> 保持逐字节一致</b>（含 B/R 槽位），
///         由 <c>LegacyDdsDecoderTests</c> 用"旧实现是转置参照"的逐通道断言锁定：
///         若有人把 R/B 顺序"修正"成规范而旧数据其实是反的，该测试会立刻变红。
///     </para>
/// </summary>
public static class DxtDecoder
{
    /// <summary>
    ///     把 DXT 块数据解码到<b>已存在</b>的目标图像（尺寸必须与参数一致）。
    /// </summary>
    /// <param name="block">压缩块数据（按 4x4 块行优先排列）。</param>
    /// <param name="width">图像宽度（&gt; 0，不要求是 4 的倍数，右侧不足部分丢弃）。</param>
    /// <param name="height">图像高度（&gt; 0，不要求是 4 的倍数，底部不足部分丢弃）。</param>
    /// <param name="format">块压缩格式。</param>
    /// <param name="target">目标图像，尺寸必须恰为 <paramref name="width" /> x <paramref name="height" />。</param>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> 为 <c>null</c>。</exception>
    /// <exception cref="ArgumentOutOfRangeException">宽或高不为正。</exception>
    /// <exception cref="ArgumentException">目标图像尺寸与参数不一致。</exception>
    /// <exception cref="ImgFormatException">压缩数据长度不足。</exception>
    public static void Decode(ReadOnlySpan<byte> block, int width, int height, DxtFormat format, RgbaImage target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "宽度必须为正数。");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "高度必须为正数。");
        }

        if (target.Width != width || target.Height != height)
        {
            throw new ArgumentException(
                $"目标图像尺寸 {target.Width}x{target.Height} 与参数 {width}x{height} 不一致。", nameof(target));
        }

        var blockSize = format == DxtFormat.Dxt1 ? 8 : 16;
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        var required = checked(blocksX * blocksY * blockSize);
        if (block.Length < required)
        {
            throw new ImgFormatException($"{format} 数据长度不足：{width}x{height} 需要 {required} 字节，实际 {block.Length} 字节。");
        }

        Span<byte> alphas = stackalloc byte[16];
        Span<Color32> colors = stackalloc Color32[4];
        var cursor = 0;

        for (var blockY = 0; blockY < blocksY; blockY++)
        {
            for (var blockX = 0; blockX < blocksX; blockX++)
            {
                alphas.Fill(0xFF);

                // ---- 先读 alpha 块（DXT3 / DXT5）——D-16：规范是 alpha 在前、色彩在后 ----
                switch (format)
                {
                    case DxtFormat.Dxt3:
                        DecodeDxt3Alpha(block.Slice(cursor, 8), alphas);
                        cursor += 8;
                        break;
                    case DxtFormat.Dxt5:
                        DecodeDxt5Alpha(block.Slice(cursor, 8), alphas);
                        cursor += 8;
                        break;
                }

                // ---- 再读 8 字节色彩块 ----
                var c0 = (ushort)(block[cursor] | (block[cursor + 1] << 8));
                var c1 = (ushort)(block[cursor + 2] | (block[cursor + 3] << 8));
                var indices = (uint)(block[cursor + 4]
                                     | (block[cursor + 5] << 8)
                                     | (block[cursor + 6] << 16)
                                     | (block[cursor + 7] << 24));
                cursor += 8;

                BuildColorTable(c0, c1, format, colors);

                for (var i = 0; i < 16; i++)
                {
                    var x = blockX * 4 + (i % 4);
                    var y = blockY * 4 + (i / 4);
                    if (x >= width || y >= height)
                    {
                        continue;
                    }

                    var paletteIndex = (int)((indices >> (i * 2)) & 0x03);
                    var color = colors[paletteIndex];

                    // 像素落盘唯一入口：SetPixel 内部按 (y * Width + x) * 4 寻址。
                    target.SetPixel(x, y, format == DxtFormat.Dxt1
                        ? color
                        : new Color32(color.R, color.G, color.B, alphas[i]));
                }
            }
        }
    }

    /// <summary>
    ///     便捷重载：新建目标图像、解码后导出 BGRA 字节副本。
    ///     <para>实现完全委托给 <see cref="Decode(ReadOnlySpan{byte}, int, int, DxtFormat, RgbaImage)" />，
    ///     不存在第二套寻址。</para>
    ///     <para><b>名字里带 NewImage 是刻意的</b>：它会分配 <c>W*H*4</c> 的新缓冲，
    ///     热路径（LRU 帧 churn、缩略图滚动）应改用写入既有图像的重载，避免"分配即丢弃"。</para>
    /// </summary>
    /// <param name="block">压缩块数据。</param>
    /// <param name="width">图像宽度（&gt; 0）。</param>
    /// <param name="height">图像高度（&gt; 0）。</param>
    /// <param name="format">块压缩格式。</param>
    /// <returns>长度为 <c>width * height * 4</c> 的 BGRA 数组。</returns>
    public static byte[] DecodeToNewImage(ReadOnlySpan<byte> block, int width, int height, DxtFormat format)
    {
        var image = new RgbaImage(width, height);
        Decode(block, width, height, format, image);
        return image.ToBgra32();
    }

    /// <summary>
    ///     把 RGB565 值扩展为 8 位通道的 <see cref="Color32" />（alpha 恒为 255）。
    ///     <para>规范位布局：<c>r:5</c> 在<b>高位</b>（bit 11-15）、<c>g:6</c> 居中、<c>b:5</c> 在<b>低位</b>（bit 0-4）；
    ///     扩展公式为 <c>(v&lt;&lt;3)|(v&gt;&gt;2)</c>（5 位）与 <c>(v&lt;&lt;2)|(v&gt;&gt;4)</c>（6 位）。</para>
    ///     <para>
    ///         ⚠️ <b>通道顺序不可"顺手修正"</b>。旧 <c>DdsDecoder.DecodeRgb565</c> 的变量名与规范相反
    ///         （它把低 5 位命名为 <c>r</c>、高 5 位命名为 <c>b</c>），但随后 <c>buf[pos+0]=colors[j][0]</c>
    ///         把"低 5 位"写进了 BGRA 的第 0 号字节（B 槽）、把"高 5 位"写进第 2 号字节（R 槽），
    ///         <b>两次相反恰好抵消，最终字节输出与规范一致</b>。本方法返回规范 <see cref="Color32" />、
    ///         由 <see cref="RgbaImage.SetPixel" /> 按 B,G,R,A 落盘，因此字节结果与旧实现<b>逐字节相同</b>。
    ///         该等价关系由 <c>LegacyDdsDecoderTests</c> 的逐通道交叉断言锁定，勿凭直觉改动。
    ///     </para>
    /// </summary>
    /// <param name="value">RGB565 取值。</param>
    /// <returns>扩展后的颜色。</returns>
    public static Color32 DecodeRgb565(ushort value)
    {
        var r = (value >> 11) & 0x1F;
        var g = (value >> 5) & 0x3F;
        var b = value & 0x1F;

        return new Color32(
            (byte)((r << 3) | (r >> 2)),
            (byte)((g << 2) | (g >> 4)),
            (byte)((b << 3) | (b >> 2)),
            0xFF);
    }

    /// <summary>
    ///     解码 DXT3 的 8 字节 alpha 块：16 个 4 位显式 alpha。
    ///     <para>
    ///         像素 <c>i</c> 的 alpha 取第 <c>i</c> 个半字节 —— 等价于
    ///         <c>(alphas[i / 4] &gt;&gt; (4 * (i % 4))) &amp; 0xF</c>（D-17）：
    ///         旧实现只写 <c>alphas[i/4] &amp; 0xF</c>，16 个像素只用到 4 个 alpha。
    ///     </para>
    /// </summary>
    /// <param name="block">至少 8 字节的 alpha 块。</param>
    /// <param name="alphas">输出跨度，长度至少 16，按像素顺序写入。</param>
    public static void DecodeDxt3Alpha(ReadOnlySpan<byte> block, Span<byte> alphas)
    {
        if (block.Length < 8)
        {
            throw new ImgFormatException($"DXT3 alpha 块长度不足：需要 8 字节，实际 {block.Length} 字节。");
        }

        if (alphas.Length < 16)
        {
            throw new ArgumentException($"alpha 输出跨度长度 {alphas.Length} 小于 16。", nameof(alphas));
        }

        for (var i = 0; i < 16; i++)
        {
            // 每个 16 位字承载 4 个像素：i / 4 选字，i % 4 选半字节（低 nibble 在前）。
            var nibble = (block[i >> 1] >> ((i & 1) * 4)) & 0x0F;
            alphas[i] = (byte)((nibble << 4) | nibble);
        }
    }

    /// <summary>
    ///     解码 DXT5 的 8 字节 alpha 块：2 个端点 + 48 位（16 x 3 位）索引。
    ///     <para><c>a0 &gt; a1</c> 时用 8 值插值模式（分母 7），否则用 6 值 + 0/255 模式（分母 5）；
    ///     插值结果一律<b>整除截断</b>（与 Khronos 伪代码的整数算术一致）。</para>
    /// </summary>
    /// <param name="block">至少 8 字节的 alpha 块。</param>
    /// <param name="alphas">输出跨度，长度至少 16，按像素顺序写入。</param>
    /// <exception cref="ImgFormatException">块长度不足。</exception>
    /// <exception cref="ArgumentException">输出跨度不足。</exception>
    public static void DecodeDxt5Alpha(ReadOnlySpan<byte> block, Span<byte> alphas)
    {
        if (block.Length < 8)
        {
            throw new ImgFormatException($"DXT5 alpha 块长度不足：需要 8 字节，实际 {block.Length} 字节。");
        }

        if (alphas.Length < 16)
        {
            throw new ArgumentException($"alpha 输出跨度长度 {alphas.Length} 小于 16。", nameof(alphas));
        }

        int a0 = block[0];
        int a1 = block[1];
        Span<byte> table = stackalloc byte[8];
        table[0] = (byte)a0;
        table[1] = (byte)a1;

        if (a0 > a1)
        {
            table[2] = (byte)((6 * a0 + 1 * a1) / 7);
            table[3] = (byte)((5 * a0 + 2 * a1) / 7);
            table[4] = (byte)((4 * a0 + 3 * a1) / 7);
            table[5] = (byte)((3 * a0 + 4 * a1) / 7);
            table[6] = (byte)((2 * a0 + 5 * a1) / 7);
            table[7] = (byte)((1 * a0 + 6 * a1) / 7);
        }
        else
        {
            table[2] = (byte)((4 * a0 + 1 * a1) / 5);
            table[3] = (byte)((3 * a0 + 2 * a1) / 5);
            table[4] = (byte)((2 * a0 + 3 * a1) / 5);
            table[5] = (byte)((1 * a0 + 4 * a1) / 5);
            table[6] = 0x00;
            table[7] = 0xFF;
        }

        // 48 位索引，小端：块内第 2..7 字节，每像素 3 位。
        var bits = 0UL;
        for (var i = 0; i < 6; i++)
        {
            bits |= (ulong)block[2 + i] << (8 * i);
        }

        for (var i = 0; i < 16; i++)
        {
            alphas[i] = table[(int)((bits >> (i * 3)) & 0x07)];
        }
    }

    /// <summary>
    ///     构造 4 色查找表。
    ///     <para>DXT3/DXT5 恒为 4 色模式；DXT1 在 <c>c0 &gt; c1</c> 时为 4 色、否则 3 色 + 全透明。</para>
    /// </summary>
    private static void BuildColorTable(ushort c0, ushort c1, DxtFormat format, Span<Color32> colors)
    {
        colors[0] = DecodeRgb565(c0);
        colors[1] = DecodeRgb565(c1);

        var fourColor = format != DxtFormat.Dxt1 || c0 > c1;
        var color0 = colors[0];
        var color1 = colors[1];

        if (fourColor)
        {
            colors[2] = new Color32(
                (byte)((2 * color0.R + color1.R) / 3),
                (byte)((2 * color0.G + color1.G) / 3),
                (byte)((2 * color0.B + color1.B) / 3),
                0xFF);
            colors[3] = new Color32(
                (byte)((color0.R + 2 * color1.R) / 3),
                (byte)((color0.G + 2 * color1.G) / 3),
                (byte)((color0.B + 2 * color1.B) / 3),
                0xFF);
        }
        else
        {
            colors[2] = new Color32(
                (byte)((color0.R + color1.R) / 2),
                (byte)((color0.G + color1.G) / 2),
                (byte)((color0.B + color1.B) / 2),
                0xFF);

            // 3 色模式下的索引 3 表示"全透明黑"。
            colors[3] = new Color32(0, 0, 0, 0);
        }
    }
}
