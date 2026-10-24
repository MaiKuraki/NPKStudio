using System.Buffers;

using Microsoft.Win32.SafeHandles;

using NPKStudio.Core.Exceptions;

namespace NPKStudio.Core.Formats.Npk;

/// <summary>
///     NPK 文件的随机读取器：持有读句柄，按偏移读字节（<c>RandomAccess.Read</c>，<b>不共享游标</b>）。
///     <para>
///         从 <c>NpkArchive</c> 抽出来的：那类原先既要解析条目、又要管句柄、还要写盘。
///         这里只管"从文件的某个偏移读多少字节"，以及句柄的打开/释放/替换后重开。
///     </para>
///     <para>
///         线程模型：全部走 <see cref="RandomAccess" />，因此可并发读；
///         句柄的替换（自身保存后重开）由调用方在单线程下完成。
///     </para>
/// </summary>
internal sealed class NpkRawReader : IDisposable
{
    private readonly string _path;

    /// <summary>文件句柄。<b>非 readonly</b>：保存到自身路径时需要先释放、替换后再重开。</summary>
    private SafeFileHandle _handle;

    private bool _disposed;

    private NpkRawReader(string path, SafeFileHandle handle)
    {
        _path = path;
        _handle = handle;
        Length = RandomAccess.GetLength(handle);
    }

    /// <summary>文件总字节数（重开后刷新）。</summary>
    public long Length { get; private set; }

    /// <summary>
    ///     打开一个文件用于随机读。
    ///     <para>
    ///         <c>FileShare.Delete</c> 是"另存到自身路径"的前提：否则 <c>File.Move(overwrite)</c>
    ///         会因目标仍被本对象打开而失败。
    ///     </para>
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <returns>读取器（调用方负责 <see cref="Dispose" />）。</returns>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    public static NpkRawReader Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        try
        {
            return new NpkRawReader(path, handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     读满指定长度（内部按需多次 <c>RandomAccess.Read</c>）。
    /// </summary>
    /// <param name="offset">起始偏移。</param>
    /// <param name="length">字节数。</param>
    /// <returns>长度恰为 <paramref name="length" /> 的数组。</returns>
    /// <exception cref="NpkFormatException">读到文件尾仍未读满。</exception>
    public byte[] ReadAt(long offset, int length)
    {
        ThrowIfDisposed();

        var buffer = new byte[length];
        if (length == 0)
        {
            return buffer;
        }

        var read = 0;
        while (read < length)
        {
            var count = RandomAccess.Read(_handle, buffer.AsSpan(read), offset + read);
            if (count <= 0)
            {
                throw new NpkFormatException($"NPK 读取越界：offset={offset}, length={length}。");
            }

            read += count;
        }

        return buffer;
    }

    /// <summary>
    ///     读取条目的原始字节（未修改条目保存时原样拷贝，见 PRD Q1）。
    /// </summary>
    /// <param name="entry">目标条目。</param>
    /// <returns>原始数据副本。</returns>
    /// <exception cref="NpkFormatException">偏移/长度越界。</exception>
    public byte[] ReadRaw(NpkEntry entry)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entry);
        ValidateRange(entry);
        return ReadAt(entry.Offset, entry.Length);
    }

    /// <summary>
    ///     把条目的原始字节<b>分块</b>拷贝到目标流，不产生整块 <c>byte[]</c>。
    ///     <para>
    ///         缓冲区从 <see cref="ArrayPool{T}.Shared" /> 借 64 KB，用完归还（<c>clearArray: false</c>）。
    ///         供"以已有 NPK 为基底合并"的打包链路使用：98 MB 的基底包不会再被整个读进内存。
    ///     </para>
    /// </summary>
    /// <param name="entry">源条目。</param>
    /// <param name="destination">目标流。</param>
    /// <exception cref="NpkFormatException">偏移/长度越界，或读到文件尾仍未拷满。</exception>
    public void CopyRawTo(NpkEntry entry, Stream destination)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(destination);

        ValidateRange(entry);
        if (entry.Length == 0)
        {
            return;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(NpkWriter.CopyBufferSize);
        try
        {
            var remaining = entry.Length;
            var offset = (long)entry.Offset;
            while (remaining > 0)
            {
                var chunk = Math.Min(remaining, buffer.Length);
                var read = RandomAccess.Read(_handle, buffer.AsSpan(0, chunk), offset);
                if (read <= 0)
                {
                    throw new NpkFormatException(
                        $"NPK 读取越界：条目 \"{entry.Path}\" 在 offset={offset} 处意外结束，还剩 {remaining} 字节未读。");
                }

                destination.Write(buffer, 0, read);
                offset += read;
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }

    /// <summary>
    ///     释放句柄但保持对象可用（自身保存流程：必须先放开文件才能替换它）。
    ///     <para>之后要么 <see cref="Reopen" />，要么整个对象 <see cref="Dispose" />。</para>
    /// </summary>
    public void ReleaseHandle()
    {
        if (_disposed)
        {
            return;
        }

        _handle.Dispose();
        _released = true;
    }

    private bool _released;

    /// <summary>
    ///     重新打开文件并刷新 <see cref="Length" />（替换失败恢复 / 替换成功后刷新都要用）。
    /// </summary>
    /// <exception cref="IOException">文件已被删除或无权访问。</exception>
    public void Reopen()
    {
        if (_disposed)
        {
            // 已整体释放：重建句柄会让 Dispose 语义变得模糊，直接拒绝。
            throw new ObjectDisposedException(nameof(NpkRawReader));
        }

        if (_released)
        {
            _handle.Dispose();
        }

        _handle = File.OpenHandle(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        _released = false;
        Length = RandomAccess.GetLength(_handle);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
    }

    private void ValidateRange(NpkEntry entry)
    {
        if (entry.Offset < 0 || entry.Length < 0 || (long)entry.Offset + entry.Length > Length)
        {
            throw new NpkFormatException(
                $"NPK 条目 \"{entry.Path}\" 越界：offset={entry.Offset}, length={entry.Length}, 文件长度={Length}。");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
