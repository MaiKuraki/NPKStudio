using System.Buffers.Binary;
using System.Text;

using NPKStudio.Core.Exceptions;

namespace NPKStudio.Core.Formats;

/// <summary>
///     <see cref="Stream" /> 的小端定长读写扩展。
///     <para>
///         设计原则（D-12）：定长读一律走 <c>Stream.ReadExactly</c>（.NET 7+ 内置），
///         短读时抛 <see cref="EndOfStreamException" />，绝不返回"读到一半"的缓冲；
///         不再保留裸 <c>Read</c> 写法。全部方法为小端，语义与 <see cref="BinaryReader" />/<see cref="BinaryWriter" /> 一致。
///     </para>
/// </summary>
public static class StreamExtensions
{
    /// <summary>
    ///     从流中读满 <paramref name="destination" /> 指定的字节数。
    /// </summary>
    /// <param name="stream">源流。</param>
    /// <param name="destination">目标缓冲，会被完全填满。</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream" /> 为 <c>null</c>。</exception>
    /// <exception cref="EndOfStreamException">流中剩余字节不足。</exception>
    public static void ReadExactlyOrThrow(this Stream stream, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.ReadExactly(destination);
    }

    /// <summary>
    ///     从流中读满 <paramref name="length" /> 个字节并返回新数组。
    /// </summary>
    /// <param name="stream">源流。</param>
    /// <param name="length">要读取的字节数，必须为非负数。</param>
    /// <returns>长度为 <paramref name="length" /> 的新数组。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream" /> 为 <c>null</c>。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length" /> 为负数。</exception>
    /// <exception cref="EndOfStreamException">流中剩余字节不足。</exception>
    public static byte[] ReadBytesExact(this Stream stream, int length)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        // 先校验"流里到底有没有这么多字节"，再分配。
        // 否则一个 80 字节的损坏文件声明 W*H*2 = 763MB 就能让我们分配 763MB（实测过）。
        if (stream.CanSeek)
        {
            var remaining = stream.Length - stream.Position;
            if (remaining < length)
            {
                throw new ImgFormatException($"需要 {length} 字节，但流中只剩 {remaining} 字节。");
            }
        }

        var buffer = new byte[length];
        stream.ReadExactly(buffer, 0, length);
        return buffer;
    }

    /// <summary>
    ///     从流中读取一个 16 位无符号整数（小端）。
    /// </summary>
    /// <param name="stream">源流。</param>
    /// <returns>读取到的值。</returns>
    public static ushort ReadUInt16(this Stream stream)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactlyOrThrow(stream, buffer);
        return BinaryPrimitives.ReadUInt16LittleEndian(buffer);
    }

    /// <summary>
    ///     从流中读取一个 32 位有符号整数（小端）。
    /// </summary>
    /// <param name="stream">源流。</param>
    /// <returns>读取到的值。</returns>
    public static int ReadInt32(this Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactlyOrThrow(stream, buffer);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    /// <summary>
    ///     从流中读取一个 32 位无符号整数（小端）。
    /// </summary>
    /// <param name="stream">源流。</param>
    /// <returns>读取到的值。</returns>
    public static uint ReadUInt32(this Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactlyOrThrow(stream, buffer);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    /// <summary>
    ///     从流中读取一个 64 位有符号整数（小端）。
    /// </summary>
    /// <param name="stream">源流。</param>
    /// <returns>读取到的值。</returns>
    public static long ReadInt64(this Stream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactlyOrThrow(stream, buffer);
        return BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    /// <summary>
    ///     向流写入一个 16 位无符号整数（小端）。
    /// </summary>
    /// <param name="stream">目标流。</param>
    /// <param name="value">要写入的值。</param>
    public static void WriteUInt16(this Stream stream, ushort value)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>
    ///     向流写入一个 32 位有符号整数（小端）。
    /// </summary>
    /// <param name="stream">目标流。</param>
    /// <param name="value">要写入的值。</param>
    public static void WriteInt32(this Stream stream, int value)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>
    ///     向流写入一个 32 位无符号整数（小端）。
    /// </summary>
    /// <param name="stream">目标流。</param>
    /// <param name="value">要写入的值。</param>
    public static void WriteUInt32(this Stream stream, uint value)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>
    ///     向流写入一个 64 位有符号整数（小端）。
    /// </summary>
    /// <param name="stream">目标流。</param>
    /// <param name="value">要写入的值。</param>
    public static void WriteInt64(this Stream stream, long value)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>
    ///     读取以 0x00 结尾的字符串（不包含结尾的 0x00），遇到流末尾同样返回。
    /// </summary>
    /// <param name="stream">源流。</param>
    /// <param name="encoding">解码方式，通常为 <see cref="Encoding.Latin1" /> 或 <see cref="Encoding.UTF8" />。</param>
    /// <returns>解码后的字符串。</returns>
    /// <exception cref="ArgumentNullException">参数为 <c>null</c>。</exception>
    public static string ReadNullTerminatedString(this Stream stream, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(encoding);

        using var buffer = new MemoryStream();
        var value = 0;
        while ((value = stream.ReadByte()) > 0)
        {
            buffer.WriteByte((byte)value);
        }

        return encoding.GetString(buffer.ToArray());
    }

    /// <summary>
    ///     写入字符串并以 0x00 结尾。
    /// </summary>
    /// <param name="stream">目标流。</param>
    /// <param name="value">要写入的字符串。</param>
    /// <param name="encoding">编码方式。</param>
    /// <exception cref="ArgumentNullException">参数为 <c>null</c>。</exception>
    public static void WriteNullTerminatedString(this Stream stream, string value, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(encoding);

        var bytes = encoding.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
    }

    /// <summary>
    ///     重新定位流游标（相对当前位置），等价于旧实现 <c>Streams.Seek(this Stream, long)</c>。
    /// </summary>
    /// <param name="stream">目标流。</param>
    /// <param name="offset">相对偏移，可为负。</param>
    /// <returns>新的绝对位置。</returns>
    public static long SeekCurrent(this Stream stream, long offset)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return stream.Seek(offset, SeekOrigin.Current);
    }
}
