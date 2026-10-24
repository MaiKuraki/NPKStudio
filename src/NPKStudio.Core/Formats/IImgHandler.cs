using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Formats;

/// <summary>
///     单个 IMG 版本（v1/v2/v4/v5/v6）的读写策略。
///     <para>
///         分工：<see cref="ImgFile" /> 负责"魔数探测 + 公共头"，本接口负责"版本特有的头尾 + 索引区 + 数据区"。
///         v4/v5/v6 的调色板表、纹理表都在版本特有头里，所以它们也归 <see cref="ReadIndex" />。
///     </para>
/// </summary>
public interface IImgHandler
{
    /// <summary>
    ///     本处理器对应的版本。
    /// </summary>
    ImgVersion Version { get; }

    /// <summary>
    ///     读取索引区（含版本特有的调色板/纹理表）与数据区，把帧填进 <paramref name="file" />。
    ///     <para>v1 还要额外读取"魔数之后"的 14 字节版本头（IndexLength/保留区/version/count）。</para>
    /// </summary>
    /// <param name="stream">定位于索引区起始处的流。</param>
    /// <param name="file">目标文件对象。</param>
    void ReadIndex(Stream stream, ImgFile file);

    /// <summary>
    ///     解码一帧。
    /// </summary>
    /// <param name="file">帧所属文件（v5 需要访问纹理表）。</param>
    /// <param name="frame">要解码的帧。</param>
    /// <returns>解码结果；LINK 帧或无数据帧返回 <c>null</c>。</returns>
    RgbaImage? Decode(ImgFile file, ImgFrame frame);

    /// <summary>
    ///     取出一帧的<b>未压缩原始像素字节</b>（解压 + 去压缩头，<b>不做</b>像素格式转换）。
    ///     <para>
    ///         方向与写盘的 <c>FramePixelEncoder.Encode</c>（BGRA → 目标格式）相反：
    ///         本方法是"从文件字节还原出像素缓冲"，因此命名用"读"。
    ///     </para>
    /// </summary>
    /// <param name="file">帧所属文件。</param>
    /// <param name="frame">要读取的帧。</param>
    /// <returns>未压缩像素字节；LINK 帧返回 <c>null</c>（D-04：绝不把链接帧编码成独立纹理/数据）。</returns>
    byte[]? ReadUncompressedPixels(ImgFile file, ImgFrame frame);

    /// <summary>
    ///     写版本头 + 索引区（v1 因为是"索引与数据内联"，也在这里写完整帧数据）。
    /// </summary>
    /// <param name="writer">目标写入器。</param>
    /// <param name="file">源文件对象。</param>
    void WriteIndex(BinaryWriter writer, ImgFile file);

    /// <summary>
    ///     写数据区（v1 无独立数据区，实现为空）。
    /// </summary>
    /// <param name="writer">目标写入器。</param>
    /// <param name="file">源文件对象。</param>
    void WritePayload(BinaryWriter writer, ImgFile file);
}
