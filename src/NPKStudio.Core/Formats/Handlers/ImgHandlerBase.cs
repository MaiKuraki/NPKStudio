using NPKStudio.Core.Diagnostics;
using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Formats.Handlers;

/// <summary>
///     v2 系（v2/v4/v5/v6 共用的"索引区 + 数据区"骨架）处理器的基类。
///     <para>
///         字节布局见 <c>FORMAT-VERIFIED.md</c> §2：
///         <code>
/// int32 type                 // 0x0E/0x0F/0x10/0x11(LINK)/0x12~0x14(DXT)
/// if LINK: int32 targetIndex             → 共 8 字节
/// else  : int32 compress, w, h, length, x, y, frameWidth, frameHeight  → 共 36 字节
/// </code>
///     </para>
/// </summary>
public abstract class ImgHandlerBase : IImgHandler
{
    /// <summary>普通帧索引项字节数（type + 8 个 int32）。</summary>
    public const int FrameIndexSize = 36;

    /// <summary>LINK 帧索引项字节数（type + targetIndex）。</summary>
    public const int LinkIndexSize = 8;

    /// <summary>
    ///     单帧宽/高的上限。<b>护栏，不是格式限制</b>：全库实测最大 609×554，
    ///     留 13 倍余量；用于拦截"损坏文件声明 20000×20000"导致的巨量分配。
    /// </summary>
    public const int MaxDimension = 8192;

    /// <summary>单帧原始像素字节上限（64 MB），与 <see cref="ZlibCodec.MaxExpectedLength" /> 对齐。</summary>
    public const long MaxRawBytesPerFrame = 64L * 1024 * 1024;

    /// <inheritdoc />
    public abstract ImgVersion Version { get; }

    /// <inheritdoc />
    public abstract void ReadIndex(Stream stream, ImgFile file);

    /// <inheritdoc />
    public abstract void WriteIndex(BinaryWriter writer, ImgFile file);

    /// <inheritdoc />
    public virtual void WritePayload(BinaryWriter writer, ImgFile file)
    {
        WritePayloadSequential(writer, file);
    }

    /// <inheritdoc />
    public virtual RgbaImage? Decode(ImgFile file, ImgFrame frame)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.IsLink)
        {
            return null;
        }

        var pixels = Encode(file, frame) ?? throw new ImgFormatException($"帧 #{frame.Index} 无数据可解码。");
        var count = ComputePixelCount(frame, BytesPerPixel(frame.Type));

        if (frame.Type == ColorBits.ARGB_8888)
        {
            return RgbaImage.FromBgra(pixels, frame.Width, frame.Height);
        }

        return RgbaImage.FromBgra(ColorCodec.Decode(pixels, frame.Type, count), frame.Width, frame.Height);
    }

    /// <inheritdoc />
    public virtual byte[]? Encode(ImgFile file, ImgFrame frame)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.IsLink)
        {
            // D-04：LINK 帧永远不产出独立数据。
            return null;
        }

        var expected = ComputeRawByteCount(frame, BytesPerPixel(frame.Type));
        var raw = frame.RawData ?? [];
        return frame.Compress switch
        {
            CompressMode.NONE => raw.Length >= expected
                ? raw[..expected]
                : throw new ImgFormatException($"帧 #{frame.Index} 未压缩数据长度不足：期望 {expected} 字节，实际 {raw.Length} 字节。"),

            // 传 byte[] + 偏移，避免 ZlibCodec 内部再 data.ToArray() 复制一份压缩数据
            CompressMode.ZLIB => ZlibCodec.Decompress(raw, 0, raw.Length, expected),
            _ => throw new ImgFormatException($"帧 #{frame.Index} 的压缩方式 {frame.Compress}（0x{(int)frame.Compress:X2}）暂不支持。")
        };
    }

    /// <summary>
    ///     每个像素占用的字节数（仅普通像素格式；LINK/DXT 不适用）。
    /// </summary>
    /// <param name="type">像素格式。</param>
    /// <returns>ARGB8888 返回 4，其余返回 2。</returns>
    protected static int BytesPerPixel(ColorBits type)
    {
        return type == ColorBits.ARGB_8888 ? 4 : 2;
    }

    /// <summary>
    ///     帧几何护栏：宽高必须为正且不超过 <see cref="MaxDimension" />，像素量不超过
    ///     <see cref="MaxRawBytesPerFrame" />。
    ///     <para>
    ///         存在的意义：IMG 的宽高来自文件头，是<b>不可信输入</b>。
    ///         实测一个 80 字节的损坏文件声明 20000×20000 就能触发 763MB 分配。
    ///     </para>
    /// </summary>
    /// <param name="frame">待校验的帧。</param>
    /// <param name="index">帧下标（仅用于异常文案）。</param>
    /// <param name="bytesPerPixel">每像素字节数（v4/v6 的调色板索引格式为 1）。</param>
    /// <exception cref="ImgFormatException">宽高非法或像素量超限。</exception>
    private protected static void ValidateGeometry(ImgFrame frame, int index, int bytesPerPixel)
    {
        if (frame.Width <= 0 || frame.Height <= 0
            || frame.Width > MaxDimension || frame.Height > MaxDimension)
        {
            throw new ImgFormatException($"帧 #{index} 尺寸非法：{frame.Width}x{frame.Height}（上限 {MaxDimension}）。");
        }

        var bytes = (long)frame.Width * frame.Height * bytesPerPixel;
        if (bytes > MaxRawBytesPerFrame)
        {
            throw new ImgFormatException(
                $"帧 #{index} 像素量超过上限：{frame.Width}x{frame.Height} × {bytesPerPixel}B = {bytes} 字节（上限 {MaxRawBytesPerFrame}）。");
        }
    }

    /// <summary>用 long 计算像素数并校验上限，返回可安全转 int 的值。</summary>
    private protected static int ComputePixelCount(ImgFrame frame, int bytesPerPixel)
    {
        var pixels = (long)frame.Width * frame.Height;
        if (pixels <= 0 || pixels * bytesPerPixel > MaxRawBytesPerFrame)
        {
            throw new ImgFormatException($"帧 #{frame.Index} 尺寸非法：{frame.Width}x{frame.Height}。");
        }

        return (int)pixels;
    }

    /// <summary>用 long 计算"未压缩字节数"并校验上限（避免 int 乘法溢出）。</summary>
    private protected static int ComputeRawByteCount(ImgFrame frame, int bytesPerPixel)
    {
        var bytes = (long)frame.Width * frame.Height * bytesPerPixel;
        if (bytes <= 0 || bytes > MaxRawBytesPerFrame)
        {
            throw new ImgFormatException(
                $"帧 #{frame.Index} 像素量超过上限：{frame.Width}x{frame.Height}（上限 {MaxRawBytesPerFrame} 字节）。");
        }

        return (int)bytes;
    }

    /// <summary>
    ///     读取 v2 系索引区（不含版本特有头、不含数据区）。
    /// </summary>
    /// <param name="stream">源流。</param>
    /// <param name="file">目标文件对象，帧会被追加进去。</param>
    /// <param name="count">帧数。</param>
    /// <param name="bytesPerPixel">
    ///     每像素字节数覆盖值；<c>0</c> 表示按 <see cref="BytesPerPixel" /> 由 <see cref="ImgFrame.Type" /> 推导。
    ///     v4/v6 的像素是 1 字节色号，必须显式传 <c>1</c>，否则 NONE 模式会把长度算成两倍。
    /// </param>
    protected static void ReadFrameIndex(Stream stream, ImgFile file, int count, int bytesPerPixel = 0)
    {
        for (var i = 0; i < count; i++)
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

            var pixelBytes = bytesPerPixel > 0 ? bytesPerPixel : BytesPerPixel(frame.Type);

            // ⚠️ 护栏必须在下面的 NONE 长度推导之前：那里是 unchecked 乘法，
            //    先溢出成负数就被 Length<0 掩盖，先算出正数则会去分配几百 MB。
            ValidateGeometry(frame, i, pixelBytes);

            if (frame.Compress == CompressMode.NONE)
            {
                // FORMAT-VERIFIED §6：NONE 时用几何推导长度覆盖索引里的值，防脏数据。
                frame.Length = (int)((long)frame.Width * frame.Height * pixelBytes);
            }

            if (frame.Length < 0)
            {
                throw new ImgFormatException($"帧 #{i} 数据长度非法：{frame.Length}。");
            }
        }
    }

    /// <summary>
    ///     按帧顺序读取数据区（只读非 LINK 帧，每帧 <c>Length</c> 字节）。
    /// </summary>
    /// <param name="stream">源流，定位于数据区起点。</param>
    /// <param name="file">已填好索引的文件对象。</param>
    protected static void ReadPayloadSequential(Stream stream, ImgFile file)
    {
        foreach (var frame in file.Frames)
        {
            if (frame.IsLink)
            {
                continue;
            }

            frame.RawData = stream.ReadBytesExact(frame.Length);
        }
    }

    /// <summary>
    ///     解决 LINK 帧的目标引用。
    ///     <para>
    ///         容错策略：目标下标非法（越界/自指）时把 <see cref="ImgFrame.LinkTarget" /> 置空并记日志，
    ///         <b>不整表丢弃</b>——旧实现遇到坏链接直接清空整个帧列表，损失过大。
    ///     </para>
    /// </summary>
    /// <param name="file">待解析的目标文件。</param>
    protected static void ResolveLinks(ImgFile file)
    {
        foreach (var frame in file.Frames)
        {
            if (!frame.IsLink)
            {
                continue;
            }

            var index = frame.LinkIndex;
            if (index >= 0 && index < file.Frames.Count && index != frame.Index)
            {
                frame.LinkTarget = file.Frames[index];
                frame.LinkIndex = -1;
                continue;
            }

            frame.LinkTarget = null;
            TraceLog.Instance.Warn($"IMG {file.Path} 帧 #{frame.Index} 的 LINK 目标下标 {index} 非法，已置空。");
        }
    }

    /// <summary>
    ///     计算 v2 系索引区字节数（v2/v4/v6 的 <c>IndexLength</c> 语义）。
    /// </summary>
    /// <param name="file">源文件对象。</param>
    /// <returns>索引区长度。</returns>
    protected static long ComputeIndexLength(ImgFile file)
    {
        long length = 0;
        foreach (var frame in file.Frames)
        {
            length += frame.IsLink ? LinkIndexSize : FrameIndexSize;
        }

        return length;
    }

    /// <summary>
    ///     写 v2 系公共头：魔数 16 + IndexLength 8 + version 4 + count 4 = 32 字节。
    /// </summary>
    /// <param name="writer">目标写入器。</param>
    /// <param name="file">源文件对象。</param>
    protected static void WriteCommonHeader(BinaryWriter writer, ImgFile file)
    {
        writer.Write(Npk.NpkCipher.ImgFlagBytes);
        writer.Write(file.IndexLength);
        writer.Write((int)file.Version);
        writer.Write(file.Frames.Count);
    }

    /// <summary>
    ///     写 v2 系索引区。
    /// </summary>
    /// <param name="writer">目标写入器。</param>
    /// <param name="file">源文件对象。</param>
    protected static void WriteFrameIndex(BinaryWriter writer, ImgFile file)
    {
        foreach (var frame in file.Frames)
        {
            writer.Write((int)frame.Type);
            if (frame.IsLink)
            {
                var target = frame.LinkTarget ?? throw new ImgFormatException($"帧 #{frame.Index} 是 LINK 但没有目标帧。");
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
        }
    }

    /// <summary>
    ///     写 v2 系数据区（只写非 LINK 帧，顺序与索引一致）。
    /// </summary>
    /// <param name="writer">目标写入器。</param>
    /// <param name="file">源文件对象。</param>
    protected void WritePayloadSequential(BinaryWriter writer, ImgFile file)
    {
        foreach (var frame in file.Frames)
        {
            if (frame.IsLink)
            {
                continue;
            }

            var raw = frame.RawData ?? [];
            writer.Write(raw);
        }
    }
}
