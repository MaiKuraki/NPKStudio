using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Formats.Npk;

namespace NPKStudio.Core.Formats.Handlers;

/// <summary>
///     v1（<c>"Neople Image File"</c>）处理器。
///     <para>
///         与 v2 的根本区别（<c>docs/FORMAT.md</c> §3）：<b>帧数据内联在索引流里</b>，
///         没有独立数据区，因此读取是"读一帧索引 → 立刻读该帧 <c>Length</c> 字节数据"的循环，
///         <b>不能复用 v2 的"先索引后数据"</b>。
///     </para>
///     <para>
///         头部（32 字节）：魔数 18 + <c>int32 indexLength</c>(写入恒 0) + <c>int16 保留</c> + <c>int32 version</c> + <c>int32 count</c>。
///         魔数由 <see cref="ImgFile" /> 探测并消费，本类只处理其后 14 字节。
///     </para>
/// </summary>
public sealed class Ver1Handler : ImgHandlerBase
{
    /// <summary>v1 头部中魔数之后的字节数：indexLength(4) + 保留(2) + version(4) + count(4)。</summary>
    public const int HeaderTailSize = 14;

    /// <inheritdoc />
    public override ImgVersion Version => ImgVersion.Ver1;

    /// <inheritdoc />
    public override void ReadIndex(Stream stream, ImgFile file)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(file);

        // ⚠️ 格式约定：v1 的 indexLength 字段写入时恒为 0（头部先于流长度确定而写出），
        //    读侧不依赖它，这里只记录下来供诊断，不做任何长度推算。
        file.IndexLength = stream.ReadInt32();

        Span<byte> reserved = stackalloc byte[2];
        stream.ReadExactlyOrThrow(reserved);

        file.Version = (ImgVersion)stream.ReadInt32();
        file.DeclaredFrameCount = stream.ReadInt32();
        if (file.DeclaredFrameCount < 0)
        {
            throw new ImgFormatException($"v1 IMG 头部帧数非法：{file.DeclaredFrameCount}。");
        }

        for (var i = 0; i < file.DeclaredFrameCount; i++)
        {
            var frame = new ImgFrame
            {
                Index = i,
                Type = (ColorBits)stream.ReadInt32()
            };
            file.AppendFrame(frame);

            if (frame.Type == ColorBits.LINK)
            {
                frame.LinkIndex = stream.ReadInt32();
                continue;
            }

            frame.Compress = (CompressMode)stream.ReadInt32();
            frame.Width = stream.ReadInt32();
            frame.Height = stream.ReadInt32();
            frame.Length = stream.ReadInt32();
            frame.X = stream.ReadInt32();
            frame.Y = stream.ReadInt32();
            frame.FrameWidth = stream.ReadInt32();
            frame.FrameHeight = stream.ReadInt32();

            if (frame.Compress == CompressMode.NONE)
            {
                frame.Length = frame.Width * frame.Height * BytesPerPixel(frame.Type);
            }

            if (frame.Length < 0)
            {
                throw new ImgFormatException($"v1 帧 #{i} 数据长度非法：{frame.Length}。");
            }

            // 内联数据：紧跟在索引之后。
            frame.RawData = stream.ReadBytesExact(frame.Length);
        }

        ResolveLinks(file);
    }

    /// <summary>
    ///     v1 的"索引区"其实包含完整文件（头 + 内联帧），数据区为空。
    /// </summary>
    /// <param name="writer">目标写入器。</param>
    /// <param name="file">源文件对象。</param>
    public override void WriteIndex(BinaryWriter writer, ImgFile file)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(file);

        writer.Write(NpkCipher.ImageFlagBytes); // 18 字节（含结尾 0x00）
        writer.Write(0); // ⚠️ 格式约定：indexLength 恒 0，不可改成真实长度（会破坏兼容）
        writer.Write((short)0); // 2 字节保留区
        writer.Write((int)ImgVersion.Ver1);
        writer.Write(file.Frames.Count);

        foreach (var frame in file.Frames)
        {
            writer.Write((int)frame.Type);
            if (frame.IsLink)
            {
                var target = frame.LinkTarget ?? throw new ImgFormatException($"v1 帧 #{frame.Index} 是 LINK 但没有目标帧。");
                writer.Write(target.Index);
                continue;
            }

            frame.Length = frame.RawData?.Length ?? 0;
            writer.Write((int)frame.Compress);
            writer.Write(frame.Width);
            writer.Write(frame.Height);
            writer.Write(frame.Length);
            writer.Write(frame.X);
            writer.Write(frame.Y);
            writer.Write(frame.FrameWidth);
            writer.Write(frame.FrameHeight);
            writer.Write(frame.RawData ?? []);
        }

        file.IndexLength = 0;
    }

    /// <inheritdoc />
    public override void WritePayload(BinaryWriter writer, ImgFile file)
    {
        // v1 没有独立数据区：全部内容已在 WriteIndex 中写出。
    }
}
