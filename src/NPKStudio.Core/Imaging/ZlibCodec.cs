using System.IO.Compression;

using NPKStudio.Core.Exceptions;

namespace NPKStudio.Core.Imaging;

/// <summary>
///     zlib（RFC 1950）压缩/解压，用于取代旧实现依赖的原生 <c>zlib1.dll</c>。
///     <para>全部基于 BCL 的 <see cref="ZLibStream" />，零第三方依赖、零原生依赖。</para>
/// </summary>
public static class ZlibCodec
{
    /// <summary>
    ///     解压后允许的最大长度上限（64 MB），用于拦截被破坏的长度字段导致的巨量分配。
    ///     <para>与 <c>ImgHandlerBase.MaxRawBytesPerFrame</c> 保持一致；全库实测单帧最大 609×554（约 1.3 MB）。</para>
    /// </summary>
    public const int MaxExpectedLength = 64 * 1024 * 1024;

    /// <summary>
    ///     使用 <see cref="CompressionLevel.Optimal" /> 压缩为 zlib 流（首字节通常为 <c>0x78</c>）。
    /// </summary>
    /// <param name="data">原始数据，可为空。</param>
    /// <returns>zlib 流字节。</returns>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>
    ///     解压 zlib 流，并<b>保证读满</b> <paramref name="expectedLength" /> 字节。
    /// </summary>
    /// <param name="data">zlib 流字节。</param>
    /// <param name="expectedLength">期望的解压后长度（通常为 <c>width*height*每像素字节数</c>）。</param>
    /// <returns>长度恰为 <paramref name="expectedLength" /> 的数组。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="expectedLength" /> 为负数。</exception>
    /// <exception cref="ImgFormatException">
    ///     流损坏、长度不足 <paramref name="expectedLength" />，或 <paramref name="expectedLength" /> 超过
    ///     <see cref="MaxExpectedLength" />。
    /// </exception>
    public static byte[] Decompress(ReadOnlySpan<byte> data, int expectedLength)
    {
        // span 无法直接给 MemoryStream：这里只能复制一份（调用方若有 byte[] 请用下面的重载）。
        return Decompress(data.ToArray(), 0, data.Length, expectedLength);
    }

    /// <summary>
    ///     解压 zlib 流（<b>不复制输入</b>），并保证读满 <paramref name="expectedLength" /> 字节。
    ///     <para>
    ///         IMG 解码的热路径：<c>frame.RawData</c> 本来就是 <c>byte[]</c>，
    ///         旧实现先 <c>data.ToArray()</c> 再包 <see cref="MemoryStream" />，
    ///         每帧白复制一次压缩数据（609×554 帧约 230 KB）。
    ///     </para>
    /// </summary>
    /// <param name="data">包含 zlib 流的缓冲。</param>
    /// <param name="offset">流的起始下标。</param>
    /// <param name="length">流的字节数。</param>
    /// <param name="expectedLength">期望的解压后长度。</param>
    /// <returns>长度恰为 <paramref name="expectedLength" /> 的数组。</returns>
    public static byte[] Decompress(byte[] data, int offset, int length, int expectedLength)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (offset < 0 || offset > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, $"偏移必须在 [0, {data.Length}] 内。");
        }

        if (length < 0 || offset + length > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, $"长度非法（offset={offset}, length={length}, buffer={data.Length}）。");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(expectedLength);
        if (expectedLength > MaxExpectedLength)
        {
            throw new ImgFormatException($"期望解压长度 {expectedLength} 超过上限 {MaxExpectedLength} 字节。");
        }

        var result = new byte[expectedLength];
        if (expectedLength == 0)
        {
            return result;
        }

        using var input = new MemoryStream(data, offset, length, writable: false);
        try
        {
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            var read = 0;
            while (read < expectedLength)
            {
                var count = zlib.Read(result, read, expectedLength - read);
                if (count <= 0)
                {
                    // 短读：绝不用"读到一半"的数据继续解码，直接抛受控异常。
                    throw new ImgFormatException($"zlib 数据解压后长度不足：期望 {expectedLength} 字节，实际 {read} 字节。");
                }

                read += count;
            }
        }
        catch (InvalidDataException ex)
        {
            throw new ImgFormatException("zlib 数据损坏，无法解压。", ex);
        }

        return result;
    }
}
