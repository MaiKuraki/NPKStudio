namespace NPKStudio.Core.Imaging;

/// <summary>
///     与渲染无关的 32 位真彩色值：直通（非预乘）alpha，通道顺序为 R/G/B/A。
///     <para>
///         内部像素缓冲恒为 BGRA8888 字节序；本类型只用于"跨模块传一个颜色"的场合，
///         与字节缓冲之间的转换统一走 <see cref="FromBgra" /> 与 <see cref="ToBgra32" />。
///     </para>
/// </summary>
/// <param name="R">红色通道。</param>
/// <param name="G">绿色通道。</param>
/// <param name="B">蓝色通道。</param>
/// <param name="A">alpha 通道（0 全透明，255 全不透明）。</param>
public readonly record struct Color32(byte R, byte G, byte B, byte A)
{
    /// <summary>
    ///     完全透明（0,0,0,0）。新建图像时的初始像素值。
    /// </summary>
    public static Color32 Transparent => default;

    /// <summary>
    ///     不透明黑色（0,0,0,255）。
    /// </summary>
    public static Color32 Black => new(0, 0, 0, 255);

    /// <summary>
    ///     不透明白色（255,255,255,255）。
    /// </summary>
    public static Color32 White => new(255, 255, 255, 255);

    /// <summary>
    ///     按 BGRA 字节顺序构造一个颜色。
    /// </summary>
    /// <param name="b">蓝色通道。</param>
    /// <param name="g">绿色通道。</param>
    /// <param name="r">红色通道。</param>
    /// <param name="a">alpha 通道。</param>
    /// <returns>对应的 <see cref="Color32" />。</returns>
    public static Color32 FromBgra(byte b, byte g, byte r, byte a)
    {
        return new Color32(r, g, b, a);
    }

    /// <summary>
    ///     按 BGRA 字节顺序解包一个 32 位无符号整数（小端语义：低字节为 B）。
    /// </summary>
    /// <param name="bgra">打包值，形如 <c>0xAARRGGBB</c> 的通道布局，但字节排列为 B,G,R,A。</param>
    /// <returns>对应的 <see cref="Color32" />。</returns>
    public static Color32 FromBgra32(uint bgra)
    {
        return new Color32(
            (byte)((bgra >> 16) & 0xFF),
            (byte)((bgra >> 8) & 0xFF),
            (byte)(bgra & 0xFF),
            (byte)((bgra >> 24) & 0xFF));
    }

    /// <summary>
    ///     打包为 32 位无符号整数，语义为"小端写出 4 字节 B,G,R,A"：
    ///     <c>B | G&lt;&lt;8 | R&lt;&lt;16 | A&lt;&lt;24</c>。
    /// </summary>
    /// <returns>打包后的 32 位值。</returns>
    public uint ToBgra32()
    {
        return (uint)(B | (G << 8) | (R << 16) | (A << 24));
    }

    /// <summary>
    ///     返回可读文本，便于日志与断言失败时定位。
    /// </summary>
    public override string ToString()
    {
        return $"Color32(R={R}, G={G}, B={B}, A={A})";
    }
}
