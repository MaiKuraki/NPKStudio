using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Formats.Handlers;

/// <summary>
///     "非 IMG / 未实现版本" 的兜底处理器：把整段字节原样收下、原样写出，不解析帧。
///     <para>
///         适用：<c>.ogg</c> 等非 IMG 条目，以及尚未实现的 v4/v5/v6（此时 <see cref="ImgFile" /> 会把
///         整段剩余字节交给本类，保证"读进来能原样写回去"，不会丢数据）。
///     </para>
/// </summary>
public sealed class OtherHandler : ImgHandlerBase
{
    /// <inheritdoc />
    public override ImgVersion Version => ImgVersion.Other;

    /// <inheritdoc />
    public override void ReadIndex(Stream stream, ImgFile file)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(file);

        var length = (int)Math.Clamp(file.IndexLength, 0, int.MaxValue);
        file.RawPayload = stream.ReadBytesExact(length);
    }

    /// <inheritdoc />
    public override void WriteIndex(BinaryWriter writer, ImgFile file)
    {
        // 无版本头：整段内容在 WritePayload 中写出。
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(file);
    }

    /// <inheritdoc />
    public override void WritePayload(BinaryWriter writer, ImgFile file)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(file);
        writer.Write(file.RawPayload ?? []);
    }

    /// <inheritdoc />
    public override RgbaImage? Decode(ImgFile file, ImgFrame frame)
    {
        return null;
    }

    /// <inheritdoc />
    public override byte[]? ReadUncompressedPixels(ImgFile file, ImgFrame frame)
    {
        return null;
    }
}
