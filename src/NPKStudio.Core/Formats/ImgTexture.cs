using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Formats;

/// <summary>
///     v5 的一张纹理页（DDS 图集）：文件里纹理表项的全部字段 + 该页的原始字节。
///     <para>
///         纹理表项字节布局（<b>28 字节</b>，紧跟色表之后、帧索引区之前）：
///         <c>version, type, index, length, fullLength, width, height</c>（7 × int32）。
///         依据是旧实现 <c>ExtractorSharp.Core/Handle/FifthHandler.cs</c> 的
///         <c>CreateFromStream</c> / <c>AdjustData</c>。
///     </para>
///     <para>
///         <b>数据永远是 zlib 流</b>：旧实现 <c>Texture.Pictrue</c> 无条件 <c>Zlib.Decompress(Data, FullLength)</c>，
///         写侧 <c>Texture.CreateFromBitmap</c> 也无条件 <c>Zlib.Compress</c>，没有"未压缩纹理"的分支。
///     </para>
///     <para>
///         <see cref="Data" /> 保留文件里的压缩字节（读→写原样回放），解码时才解压；
///         这样"没改过的条目"重新序列化后纹理内容仍然逐字节相同。
///     </para>
/// </summary>
internal sealed class ImgTexture
{
    /// <summary>
    ///     纹理版本（旧实现 <c>TextureVersion</c>）：<c>1</c> = DXT1、<c>3</c> = DXT3、<c>5</c> = DXT5。
    ///     <para>
    ///         该字段只用于诊断：真正决定解码路径的是 <see cref="Type" />（未压缩格式直接读像素，
    ///         DXT 类型才解析 DDS 头）。旧实现写出 1555 纹理时也把 Version 留成默认值 1，
    ///         说明它本来就不参与解码。
    ///     </para>
    /// </summary>
    public int Version;

    /// <summary>
    ///     像素格式：<c>0x0E/0x0F/0x10</c>（未压缩 1555/4444/8888）或 <c>0x12/0x13/0x14</c>（DDS 块压缩）。
    ///     <para>DXT 纹理的解压结果是<b>完整 DDS 文件</b>（含 128 字节头），由 <see cref="DdsFile" /> 解析。</para>
    /// </summary>
    public ColorBits Type;

    /// <summary>纹理表项里的 <c>index</c> 字段（旧实现写"纹理在列表中的下标"，读侧不依赖它）。</summary>
    public int Index;

    /// <summary>该纹理在文件里的压缩数据字节数，即 <see cref="Data" /> 的长度（读侧用它定位数据区）。</summary>
    public int Length;

    /// <summary>解压后的字节数（DXT 纹理含 DDS 头），解码时作为 zlib 的期望长度。</summary>
    public int FullLength;

    /// <summary>纹理页宽度（像素）。</summary>
    public int Width;

    /// <summary>纹理页高度（像素）。</summary>
    public int Height;

    /// <summary>文件里的原始字节（zlib 流，未解压）。</summary>
    public byte[] Data = [];

    /// <summary>
    ///     该纹理是否是 DDS 块压缩（DXT1/3/5）。只有这几种类型才走 <see cref="DdsFile" /> 解析。
    /// </summary>
    public bool IsBlockCompressed =>
        Type is ColorBits.DXT_1 or ColorBits.DXT_3 or ColorBits.DXT_5;

    /// <summary>返回可读文本，便于日志定位。</summary>
    public override string ToString()
    {
        return $"ImgTexture(#{Index}, {Type}, v{Version}, {Width}x{Height}, {Length}B→{FullLength}B)";
    }
}
