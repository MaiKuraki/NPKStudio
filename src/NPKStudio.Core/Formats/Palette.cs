using System.Collections;

using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Formats;

/// <summary>
///     不可变调色板：<see cref="IReadOnlyList{T}" /> 的轻量封装，元素为直通 alpha 的 <see cref="Color32" />，
///     并附带与文件字节之间的转换，供 v4/v5/v6 复用。
///     <para>
///         ⚠️ <b>文件中的调色板字节序是 R,G,B,A（不是 B,G,R,A），且未经真机验证。</b>
///         依据：旧实现 <c>Colors.ReadPalette</c> 是 <c>Color.FromArgb(data[3], data[0], data[1], data[2])</c>，
///         即 <c>byte0=R, byte1=G, byte2=B, byte3=A</c>；<c>WritePalette</c> 对称写 <c>{color.R, color.G, color.B, color.A}</c>。
///         本机没有任何 v4/v6 真实样本，<b>R/B 是否真如旧实现那样排列无法验证</b>。
///     </para>
///     <para>
///         因此本类的 <see cref="FromBgraBytes" />/<see cref="ToBgraBytes" /> 只服务"确实是 B,G,R,A 顺序"的场合
///         （如 <c>RgbaImage</c> 缓冲），<b>不要</b>拿它们直接读写 v4/v6 调色板。
///         v4/v6 的 R,G,B,A 读写已集中到 <see cref="PaletteIndexCodec" />（internal），
///         那里的注释记录了核对要点与"未经真实样本验证"的说明。
///     </para>
/// </summary>
public sealed class Palette : IReadOnlyList<Color32>
{
    private const int BytesPerColor = 4;

    private readonly Color32[] _colors;

    /// <summary>
    ///     空调色板（0 色）。v1/v2 无调色板时使用。
    /// </summary>
    public static Palette Empty { get; } = new(Array.Empty<Color32>());

    /// <summary>
    ///     由颜色序列构造调色板；序列会被复制到内部数组，构造后外部修改不影响本实例。
    /// </summary>
    /// <param name="colors">颜色序列，不能为 <c>null</c>。</param>
    public Palette(IEnumerable<Color32> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        _colors = colors.ToArray();
    }

    /// <summary>
    ///     调色板颜色数量。
    /// </summary>
    public int Count => _colors.Length;

    /// <summary>
    ///     按索引取颜色。
    /// </summary>
    /// <param name="index">颜色下标，必须在 <c>[0, Count)</c> 内。</param>
    public Color32 this[int index] => _colors[index];

    /// <summary>
    ///     从 BGRA 字节流解析调色板（每色 4 字节，顺序 B,G,R,A）。
    /// </summary>
    /// <param name="bgra">调色板字节，长度必须是 4 的整数倍。</param>
    /// <returns>解析得到的调色板。</returns>
    /// <exception cref="ImgFormatException">字节长度不是 4 的整数倍。</exception>
    public static Palette FromBgraBytes(ReadOnlySpan<byte> bgra)
    {
        if (bgra.Length % BytesPerColor != 0)
        {
            throw new ImgFormatException($"调色板字节长度 {bgra.Length} 不是 4 的整数倍。");
        }

        var count = bgra.Length / BytesPerColor;
        var colors = new Color32[count];
        for (var i = 0; i < count; i++)
        {
            var offset = i * BytesPerColor;
            colors[i] = Color32.FromBgra(bgra[offset], bgra[offset + 1], bgra[offset + 2], bgra[offset + 3]);
        }

        return new Palette(colors);
    }

    /// <summary>
    ///     导出为 BGRA 字节数组（每色 4 字节，顺序 B,G,R,A）。
    /// </summary>
    /// <returns>长度为 <c>Count * 4</c> 的新数组。</returns>
    public byte[] ToBgraBytes()
    {
        var result = new byte[_colors.Length * BytesPerColor];
        for (var i = 0; i < _colors.Length; i++)
        {
            var color = _colors[i];
            var offset = i * BytesPerColor;
            result[offset] = color.B;
            result[offset + 1] = color.G;
            result[offset + 2] = color.R;
            result[offset + 3] = color.A;
        }

        return result;
    }

    /// <summary>
    ///     导出为 <see cref="Color32" /> 数组副本。
    /// </summary>
    /// <returns>与内部数组内容一致的新数组。</returns>
    public Color32[] ToArray()
    {
        return (Color32[])_colors.Clone();
    }

    /// <summary>
    ///     按索引查找颜色，未命中返回 <c>false</c>（不抛异常，供脏数据容错）。
    /// </summary>
    /// <param name="index">颜色下标。</param>
    /// <param name="color">命中的颜色；未命中为 <see cref="Color32.Transparent" />。</param>
    /// <returns>下标合法则为 <c>true</c>。</returns>
    public bool TryGet(int index, out Color32 color)
    {
        if (index < 0 || index >= _colors.Length)
        {
            color = Color32.Transparent;
            return false;
        }

        color = _colors[index];
        return true;
    }

    /// <inheritdoc />
    public IEnumerator<Color32> GetEnumerator()
    {
        return ((IEnumerable<Color32>)_colors).GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    ///     返回可读文本，便于日志定位。
    /// </summary>
    public override string ToString()
    {
        return $"Palette(Count={_colors.Length})";
    }
}
