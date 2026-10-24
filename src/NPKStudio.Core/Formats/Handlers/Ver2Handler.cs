using NPKStudio.Core.Exceptions;

namespace NPKStudio.Core.Formats.Handlers;

/// <summary>
///     v2（<c>"Neople Img File"</c>）处理器，也是 v4/v6 的骨架基础。
///     <para>
///         布局（<c>FORMAT-VERIFIED.md</c> §2）：公共头 32 字节 → 索引区（<c>IndexLength</c> 字节）→ 数据区。
///         数据区按帧顺序<b>只</b>存放非 LINK 帧的 <c>Length</c> 字节，LINK 帧不占数据区。
///     </para>
/// </summary>
public sealed class Ver2Handler : ImgHandlerBase
{
    /// <inheritdoc />
    public override ImgVersion Version => ImgVersion.Ver2;

    /// <inheritdoc />
    public override void ReadIndex(Stream stream, ImgFile file)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(file);

        ReadFrameIndex(stream, file, file.DeclaredFrameCount);
        ReadPayloadSequential(stream, file);
        ResolveLinks(file);
    }

    /// <inheritdoc />
    public override void WriteIndex(BinaryWriter writer, ImgFile file)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(file);

        file.IndexLength = ComputeIndexLength(file);
        WriteCommonHeader(writer, file);
        WriteFrameIndex(writer, file);

        // 一致性自检：写出的索引区长度必须与头部声明相符，否则文件必然不可读。
        if (file.IndexLength != ComputeIndexLength(file))
        {
            throw new ImgFormatException($"v2 索引区长度自检失败：声明 {file.IndexLength}，实际 {ComputeIndexLength(file)}。");
        }
    }
}
