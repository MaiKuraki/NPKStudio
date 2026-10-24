namespace NPKStudio.Core.Formats.Npk;

/// <summary>
///     一个"延迟供应数据"的 NPK 条目：<b>长度先给出，内容留到轮到它时才写入</b>。
///     <para>
///         这是流式打包的基础：NPK 的偏移只依赖各条目的长度，所以可以先把整张条目表算完写完，
///         再逐条把数据流式拷进去，<b>全程不需要把所有条目同时放在内存里</b>。
///     </para>
///     <para>
///         <b>为什么是抽象类而不是 <c>Action&lt;Stream&gt;</c></b>：批量打包时条目数以千计，
///         每个 lambda 都会连带一个编译器生成的闭包对象（实测 136 B/条目）。
///         改成抽象方法后每条目只剩一个对象本身（约 48 B），且消除间接调用——
///         这是全链路上唯一"每条目一次"的委托分配点。
///     </para>
///     <para>
///         <see cref="WriteData" /> 的实现方自己负责打开/关闭底层资源（文件流、归档句柄等），
///         并且必须<b>恰好</b>写出 <see cref="Length" /> 字节——写多写少都会被
///         <see cref="NpkWriter.WriteStreaming" /> 检出并抛 <c>NpkFormatException</c>。
///     </para>
/// </summary>
public abstract class NpkEntrySource
{
    /// <summary>
    ///     供派生类初始化路径与长度。
    /// </summary>
    /// <param name="path">包内相对路径。</param>
    /// <param name="length">数据字节数（&gt; 0）。</param>
    protected NpkEntrySource(string path, int length)
    {
        Path = path;
        Length = length;
    }

    /// <summary>
    ///     包内相对路径（以 <c>/</c> 分隔，UTF-8 编码后不得超过 255 字节）。
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     数据字节数（&gt; 0）。<b>必须准确</b>：它直接决定条目表里的 offset 与 length。
    /// </summary>
    public int Length { get; }

    /// <summary>
    ///     把该条目的数据写入目标流。<b>每个实例只会被调用一次，不会并发调用。</b>
    /// </summary>
    /// <param name="destination">目标流；实现方不应关闭它。</param>
    public abstract void WriteData(Stream destination);

    /// <summary>
    ///     由内存字节构造条目源（小数据、或调用方本来就持有字节时使用）。
    /// </summary>
    /// <param name="path">包内相对路径。</param>
    /// <param name="data">条目数据，不能为 <c>null</c> 或空。</param>
    /// <returns>条目源。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data" /> 为 <c>null</c>。</exception>
    /// <exception cref="ArgumentException"><paramref name="data" /> 为空数组。</exception>
    public static NpkEntrySource FromBytes(string path, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
        {
            throw new ArgumentException("条目数据长度为 0。", nameof(data));
        }

        return new BytesSource(path, data);
    }

    /// <summary>
    ///     由磁盘文件构造条目源：写入时才打开文件并流式拷贝，不预先读进内存。
    ///     <para>
    ///         长度在<b>构造时</b>读取一次；打开参数为 <c>FileShare.Read</c> +
    ///         <c>FileOptions.SequentialScan</c> + 64 KB 缓冲，便于操作系统做顺序读预取。
    ///     </para>
    /// </summary>
    /// <param name="path">包内相对路径。</param>
    /// <param name="filePath">本地文件绝对路径。</param>
    /// <returns>条目源。</returns>
    /// <exception cref="ArgumentException">路径为空、文件为空或过大。</exception>
    public static NpkEntrySource FromFile(string path, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var length = new FileInfo(filePath).Length;
        if (length <= 0)
        {
            throw new ArgumentException($"文件为空：{filePath}（{length} 字节）。", nameof(filePath));
        }

        if (length > int.MaxValue)
        {
            throw new ArgumentException($"文件过大（{length} 字节），NPK 条目长度是 int32。", nameof(filePath));
        }

        return new FileSource(path, filePath, (int)length);
    }

    /// <summary>
    ///     由归档条目构造条目源：写入时才从原 NPK 按偏移分块拷出（<c>RandomAccess.Read</c>）。
    /// </summary>
    /// <param name="archive">源归档，<b>必须保持打开直到写入完成</b>。</param>
    /// <param name="entry">源条目。</param>
    /// <returns>条目源。</returns>
    /// <exception cref="ArgumentNullException">参数为 <c>null</c>。</exception>
    public static NpkEntrySource FromArchive(NpkArchive archive, NpkEntry entry)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(entry);
        return new ArchiveSource(archive, entry);
    }

    /// <summary>
    ///     返回可读文本，便于日志定位。
    /// </summary>
    public override string ToString()
    {
        return $"NpkEntrySource({Path}, {Length} B)";
    }

    /// <summary>内存字节供应：零拷贝持有调用方的数组。</summary>
    private sealed class BytesSource : NpkEntrySource
    {
        private readonly byte[] _data;

        public BytesSource(string path, byte[] data)
            : base(path, data.Length)
        {
            _data = data;
        }

        public override void WriteData(Stream destination)
        {
            destination.Write(_data, 0, _data.Length);
        }
    }

    /// <summary>磁盘文件供应：写入时才开流，64 KB + 顺序扫描。</summary>
    private sealed class FileSource : NpkEntrySource
    {
        private readonly string _filePath;

        public FileSource(string path, string filePath, int length)
            : base(path, length)
        {
            _filePath = filePath;
        }

        public override void WriteData(Stream destination)
        {
            using var source = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                NpkWriter.CopyBufferSize,
                FileOptions.SequentialScan);
            source.CopyTo(destination, NpkWriter.CopyBufferSize);
        }
    }

    /// <summary>归档条目供应：从原 NPK 按偏移分块拷出，不产生整块 byte[]。</summary>
    private sealed class ArchiveSource : NpkEntrySource
    {
        private readonly NpkArchive _archive;
        private readonly NpkEntry _entry;

        public ArchiveSource(NpkArchive archive, NpkEntry entry)
            : base(entry.Path, entry.Length)
        {
            _archive = archive;
            _entry = entry;
        }

        public override void WriteData(Stream destination)
        {
            _archive.CopyRawTo(_entry, destination);
        }
    }
}
