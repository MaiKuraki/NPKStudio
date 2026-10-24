using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Formats;

namespace NPKStudio.Core.Imaging;

/// <summary>
///     ARGB1555 / ARGB4444 / ARGB8888 三种像素格式与内部 BGRA8888 之间的编解码。
///     <para>
///         编码严格按 <c>docs/FORMAT.md</c> §7 的公式实现，
///         与解码函数互为逆运算：<c>Encode(Decode(word)) == word</c> 对全部合法字成立。
///     </para>
///     <para>字节序：1555/4444 为小端 16 位字（低字节在前），8888 直接是 B,G,R,A。</para>
/// </summary>
public static class ColorCodec
{
    /// <summary>ARGB1555 的格式常量（0x0E）。</summary>
    public const int Argb1555 = 0x0E;

    /// <summary>ARGB4444 的格式常量（0x0F）。</summary>
    public const int Argb4444 = 0x0F;

    /// <summary>ARGB8888 的格式常量（0x10）。</summary>
    public const int Argb8888 = 0x10;

    /// <summary>
    ///     返回指定格式每个像素占用的字节数。
    /// </summary>
    /// <param name="bits">像素格式。</param>
    /// <returns>1555/4444 返回 2，8888 返回 4。</returns>
    /// <exception cref="ImgFormatException">格式不是这三种普通像素格式（如 LINK / DXT）。</exception>
    public static int BytesPerPixel(ColorBits bits)
    {
        return bits switch
        {
            ColorBits.ARGB_1555 => 2,
            ColorBits.ARGB_4444 => 2,
            ColorBits.ARGB_8888 => 4,
            _ => throw new ImgFormatException($"像素格式 {bits}（0x{(int)bits:X2}）不是可逐像素解码的普通格式。")
        };
    }

    /// <summary>
    ///     解码单个像素。
    /// </summary>
    /// <param name="source">源字节，长度至少为 <see cref="BytesPerPixel" />。</param>
    /// <param name="bits">像素格式。</param>
    /// <returns>解码后的颜色（直通 alpha）。</returns>
    /// <exception cref="ArgumentException"><paramref name="source" /> 长度不足。</exception>
    /// <exception cref="ImgFormatException">格式不支持。</exception>
    public static Color32 DecodeColor(ReadOnlySpan<byte> source, ColorBits bits)
    {
        var size = BytesPerPixel(bits);
        if (source.Length < size)
        {
            throw new ArgumentException($"源字节长度 {source.Length} 小于 {bits} 所需的 {size}。", nameof(source));
        }

        return bits switch
        {
            ColorBits.ARGB_1555 => DecodeArgb1555((ushort)(source[0] | (source[1] << 8))),
            ColorBits.ARGB_4444 => DecodeArgb4444((ushort)(source[0] | (source[1] << 8))),
            _ => Color32.FromBgra(source[0], source[1], source[2], source[3])
        };
    }

    /// <summary>
    ///     编码单个像素到目标跨度（只写 <see cref="BytesPerPixel" /> 个字节）。
    /// </summary>
    /// <param name="color">要编码的颜色。</param>
    /// <param name="bits">目标像素格式。</param>
    /// <param name="destination">目标跨度，长度至少为 <see cref="BytesPerPixel" />。</param>
    /// <exception cref="ArgumentException"><paramref name="destination" /> 长度不足。</exception>
    /// <exception cref="ImgFormatException">格式不支持。</exception>
    public static void EncodeColor(Color32 color, ColorBits bits, Span<byte> destination)
    {
        var size = BytesPerPixel(bits);
        if (destination.Length < size)
        {
            throw new ArgumentException($"目标跨度长度 {destination.Length} 小于 {bits} 所需的 {size}。", nameof(destination));
        }

        switch (bits)
        {
            case ColorBits.ARGB_1555:
            {
                var value = EncodeArgb1555(color);
                destination[0] = (byte)(value & 0xFF);
                destination[1] = (byte)(value >> 8);
                break;
            }

            case ColorBits.ARGB_4444:
            {
                var value = EncodeArgb4444(color);
                destination[0] = (byte)(value & 0xFF);
                destination[1] = (byte)(value >> 8);
                break;
            }

            default:
                destination[0] = color.B;
                destination[1] = color.G;
                destination[2] = color.R;
                destination[3] = color.A;
                break;
        }
    }

    /// <summary>
    ///     批量解码为 BGRA8888 缓冲。
    /// </summary>
    /// <param name="source">源字节（恰好 <c>pixelCount * BytesPerPixel</c> 字节，多余字节被忽略）。</param>
    /// <param name="bits">源像素格式。</param>
    /// <param name="pixelCount">像素数量。</param>
    /// <returns>长度为 <c>pixelCount * 4</c> 的 BGRA 数组。</returns>
    /// <exception cref="ArgumentException">源字节不足。</exception>
    /// <exception cref="ImgFormatException">格式不支持。</exception>
    public static byte[] Decode(ReadOnlySpan<byte> source, ColorBits bits, int pixelCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pixelCount);
        var size = BytesPerPixel(bits);
        var required = checked(size * pixelCount);
        if (source.Length < required)
        {
            throw new ArgumentException($"源字节长度 {source.Length} 小于 {pixelCount} 个像素所需的 {required}。", nameof(source));
        }

        var result = new byte[checked(pixelCount * 4)];
        switch (bits)
        {
            case ColorBits.ARGB_1555:
            case ColorBits.ARGB_4444:
                for (var i = 0; i < pixelCount; i++)
                {
                    var offset = i * 2;
                    var word = (ushort)(source[offset] | (source[offset + 1] << 8));
                    var color = bits == ColorBits.ARGB_1555 ? DecodeArgb1555(word) : DecodeArgb4444(word);
                    var target = i * 4;
                    result[target] = color.B;
                    result[target + 1] = color.G;
                    result[target + 2] = color.R;
                    result[target + 3] = color.A;
                }

                break;

            default:
                source.Slice(0, required).CopyTo(result);
                break;
        }

        return result;
    }

    /// <summary>
    ///     批量编码：把 BGRA8888 缓冲编码为目标格式。
    /// </summary>
    /// <param name="bgra">BGRA8888 源缓冲，长度必须是 4 的整数倍。</param>
    /// <param name="bits">目标像素格式。</param>
    /// <returns>长度为 <c>(bgra.Length / 4) * BytesPerPixel</c> 的编码结果。</returns>
    /// <exception cref="ArgumentException"><paramref name="bgra" /> 长度不是 4 的整数倍。</exception>
    /// <exception cref="ImgFormatException">格式不支持。</exception>
    public static byte[] Encode(ReadOnlySpan<byte> bgra, ColorBits bits)
    {
        if (bgra.Length % 4 != 0)
        {
            throw new ArgumentException($"BGRA 缓冲长度 {bgra.Length} 不是 4 的整数倍。", nameof(bgra));
        }

        var pixelCount = bgra.Length / 4;
        var size = BytesPerPixel(bits);
        var result = new byte[checked(pixelCount * size)];

        if (bits == ColorBits.ARGB_8888)
        {
            bgra.CopyTo(result);
            return result;
        }

        for (var i = 0; i < pixelCount; i++)
        {
            var source = i * 4;
            var color = Color32.FromBgra(bgra[source], bgra[source + 1], bgra[source + 2], bgra[source + 3]);
            var value = bits == ColorBits.ARGB_1555 ? EncodeArgb1555(color) : EncodeArgb4444(color);
            var target = i * 2;
            result[target] = (byte)(value & 0xFF);
            result[target + 1] = (byte)(value >> 8);
        }

        return result;
    }

    /// <summary>
    ///     解码一个 ARGB1555 字。
    ///     <para>
    ///         位布局（小端 16 位）：<c>a:1 r:5 g:5 b:5</c>，其中
    ///         <c>a = b1&gt;&gt;7</c>、<c>r = (b1&gt;&gt;2)&amp;0x1F</c>、
    ///         <c>g = ((b0&gt;&gt;5) | ((b1&amp;3)&lt;&lt;3)) &amp; 0x1F</c>、<c>b = b0&amp;0x1F</c>。
    ///         5 位通道按 <c>(v&lt;&lt;3)|(v&gt;&gt;2)</c> 扩展到 8 位（真机上实测正确，勿改）。
    ///     </para>
    /// </summary>
    /// <param name="value">16 位取值。</param>
    /// <returns>解码后的颜色。</returns>
    public static Color32 DecodeArgb1555(ushort value)
    {
        var low = (byte)(value & 0xFF);
        var high = (byte)(value >> 8);

        var a = (high >> 7) & 0x01;
        var r = (high >> 2) & 0x1F;
        var g = ((low >> 5) | ((high & 0x03) << 3)) & 0x1F;
        var b = low & 0x1F;

        return new Color32(
            (byte)((r << 3) | (r >> 2)),
            (byte)((g << 3) | (g >> 2)),
            (byte)((b << 3) | (b >> 2)),
            (byte)(a == 1 ? 0xFF : 0x00));
    }

    /// <summary>
    ///     编码为 ARGB1555 字（<c>docs/FORMAT.md</c> §7 公式，与 <see cref="DecodeArgb1555" /> 互逆）。
    ///     <code>
    /// b0 = ((g5 &amp; 0x07) &lt;&lt; 5) | (b &gt;&gt; 3)
    /// b1 = ((a &gt;&gt; 7) &lt;&lt; 7) | ((r &gt;&gt; 3) &lt;&lt; 2) | ((g5 &gt;&gt; 3) &amp; 0x03)   // g5 = (g &gt;&gt; 3) &amp; 0x1F
    /// </code>
    /// </summary>
    /// <param name="color">要编码的颜色。</param>
    /// <returns>16 位取值。</returns>
    public static ushort EncodeArgb1555(Color32 color)
    {
        var a = (color.A >> 7) & 0x01;
        var r5 = (color.R >> 3) & 0x1F;
        var g5 = (color.G >> 3) & 0x1F;
        var b5 = (color.B >> 3) & 0x1F;

        var low = ((g5 & 0x07) << 5) | b5;
        var high = (a << 7) | (r5 << 2) | ((g5 >> 3) & 0x03);
        return (ushort)(low | (high << 8));
    }

    /// <summary>
    ///     解码一个 ARGB4444 字：<c>a = b1&amp;0xF0</c>、<c>r = (b1&amp;0x0F)&lt;&lt;4</c>、
    ///     <c>g = b0&amp;0xF0</c>、<c>b = (b0&amp;0x0F)&lt;&lt;4</c>（4 位按 <c>(v&lt;&lt;4)</c> 扩展到 8 位）。
    /// </summary>
    /// <param name="value">16 位取值。</param>
    /// <returns>解码后的颜色。</returns>
    public static Color32 DecodeArgb4444(ushort value)
    {
        var low = (byte)(value & 0xFF);
        var high = (byte)(value >> 8);

        var a = (byte)(high & 0xF0);
        var r = (byte)((high & 0x0F) << 4);
        var g = (byte)(low & 0xF0);
        var b = (byte)((low & 0x0F) << 4);

        return new Color32(r, g, b, a);
    }

    /// <summary>
    ///     编码为 ARGB4444 字。修正了旧实现的缺陷（D-02）：旧代码先把 <c>a</c> 右移 7 位再拼进去，
    ///     且 <c>g</c>/<c>a</c> 缺少 <c>0xF0</c> 掩码，导致 alpha 被摧毁。
    ///     <code>
    /// b0 = (g &amp; 0xF0) | (b &gt;&gt; 4)
    /// b1 = (a &amp; 0xF0) | (r &gt;&gt; 4)
    /// </code>
    /// </summary>
    /// <param name="color">要编码的颜色。</param>
    /// <returns>16 位取值。</returns>
    public static ushort EncodeArgb4444(Color32 color)
    {
        var low = (color.G & 0xF0) | (color.B >> 4);
        var high = (color.A & 0xF0) | (color.R >> 4);
        return (ushort)(low | (high << 8));
    }
}
