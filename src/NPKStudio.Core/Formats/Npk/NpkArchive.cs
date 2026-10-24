using System.Buffers.Binary;

using NPKStudio.Core.Diagnostics;
using NPKStudio.Core.Exceptions;

namespace NPKStudio.Core.Formats.Npk;

/// <summary>
///     NPK 包（<c>"NeoplePack_Bill"</c>）的只读视图 + 另存为。
///     <para>
///         布局（<c>docs/FORMAT.md</c> §1，真机样本逐字节校准）：
///         <code>
/// 0        : "NeoplePack_Bill" + 0x00        16 字节
/// 16       : int32 count
/// 20       : Entry[count]，每项 264 字节（int32 offset, int32 length, byte[256] 加密路径）
/// 20+264n  : byte[32] SHA256（对条目表区 len/17*17 字节）
/// 52+264n  : 数据区
/// </code>
///         所以 <b>数据区起点 = 52 + 264 × count</b>（52 = 16 + 4 + 32），不是 53。
///     </para>
///     <para>
///         线程模型：全部读取走 <c>RandomAccess.Read</c>（按偏移随机读，<b>不共享 Stream 游标</b>），
///         因此可并发调用；IMG 惰性解析结果放在 LRU(8) 里，锁只包字典/链表操作。
///     </para>
/// </summary>
public sealed class NpkArchive : IDisposable
{
    /// <summary>条目表每项字节数。</summary>
    public const int EntrySize = NpkEntryTable.EntrySize;

    /// <summary>条目表区之后的 SHA256 校验码字节数。</summary>
    public const int HashSize = NpkEntryTable.HashSize;

    /// <summary>条目表区（魔数 + 条目数 + 条目）相对数据区的固定前缀：16 + 4 + 32 = 52。</summary>
    public const int DataStartBase = NpkEntryTable.DataStartBase;

    /// <summary>已解析 IMG 的缓存容量。</summary>
    public const int ImgCacheCapacity = NpkImageCache.Capacity;

    private readonly List<NpkEntry> _entries = [];
    private readonly NpkImageCache _images = new();

    /// <summary>随机读取器（句柄、长度、分块拷贝都在里面）。</summary>
    private readonly NpkRawReader _reader;

    private bool _disposed;

    /// <summary>
    ///     是否正在"写回自身路径"。此时读句柄会被释放、条目表会被重建，
    ///     任何并发的按偏移读取都可能拿到已关闭的句柄或过期偏移，因此直接拒绝而不是加锁等待
    ///     （等待会把 UI 线程钉住，比报错更糟）。
    /// </summary>
    private volatile bool _saving;

    private NpkArchive(string filePath, NpkRawReader reader)
    {
        FilePath = filePath;
        _reader = reader;
    }

    /// <summary>
    ///     NPK 文件路径。
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    ///     文件总字节数（保存到自身路径后会刷新）。
    /// </summary>
    public long FileLength => _reader.Length;

    /// <summary>
    ///     条目列表（顺序与文件条目表一致）。
    ///     <para>⚠️ 保存到<b>同一路径</b>成功后，本列表会被重解析后的新实例整体替换（旧对象引用失效）。</para>
    /// </summary>
    public IReadOnlyList<NpkEntry> Entries => _entries;

    /// <summary>
    ///     打开一个 NPK 包（只解析条目表，不解析任何 IMG）。
    /// </summary>
    /// <param name="path">NPK 文件路径。</param>
    /// <returns>已解析条目表的归档对象。</returns>
    /// <exception cref="NpkFormatException">魔数不符、条目越界、文件截断。</exception>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    public static NpkArchive Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // FileShare.Delete 是"另存到自身路径"的前提：否则 File.Move(overwrite) 会因目标仍被本对象打开而失败。
        var reader = NpkRawReader.Open(path);
        try
        {
            var archive = new NpkArchive(path, reader);
            archive.ReadEntryTable();
            return archive;
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     把条目的原始字节<b>分块</b>拷贝到目标流，不产生整块 <c>byte[]</c>（64 KB 池化缓冲）。
    /// </summary>
    /// <param name="entry">源条目。</param>
    /// <param name="destination">目标流。</param>
    /// <exception cref="NpkFormatException">偏移/长度越界，或读到文件尾仍未拷满。</exception>
    public void CopyRawTo(NpkEntry entry, Stream destination)
    {
        ThrowIfDisposed();
        _reader.CopyRawTo(entry, destination);
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
        ThrowIfSaving();
        return _reader.ReadRaw(entry);
    }

    /// <summary>
    ///     惰性解析（或从 LRU 缓存取出）指定条目的 IMG。
    /// </summary>
    /// <param name="entry">目标条目。</param>
    /// <returns>解析后的 <see cref="ImgFile" />；同时回填 <see cref="NpkEntry.FrameCount" />。</returns>
    public ImgFile LoadImg(NpkEntry entry)
    {
        ThrowIfDisposed();
        ThrowIfSaving();
        return _images.LoadImg(entry, _reader);
    }

    /// <summary>
    ///     另存为新的 NPK 包：写临时文件 → 重新解析自检 → 原子替换。
    ///     <para><see cref="NpkEntry.IsModified" /> 为 <c>true</c> 的条目用内存中的 IMG 重新序列化，其余原样拷贝。</para>
    /// </summary>
    /// <param name="path">目标路径。</param>
    /// <param name="only">只写这些条目（<c>null</c> 表示全部，顺序始终按原包条目顺序）。</param>
    /// <exception cref="NpkFormatException">写出后自检失败（长度/条目表不符）。</exception>
    public void Save(string path, IReadOnlyCollection<NpkEntry>? only = null)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var targets = BuildTargetList(only);
        var sources = BuildSaveSources(targets);

        // 保存到自身路径时，Windows 下必须先释放自己持有的读句柄才能替换文件。
        // 释放时机交给 NpkWriter 的 beforeReplace 钩子：它在"写出 + 自检都通过、即将 Move"时才回调，
        // 所以写坏了（自检失败）不会把句柄提前释放掉。
        var selfSave = IsSameFile(path, FilePath);
        var handleReleased = false;

        // 写回自身期间禁止并发按偏移读取：句柄会被释放、条目表会被重建，
        // 并发的 LoadImg 要么撞上已关闭的句柄，要么读到过期偏移。
        _saving = selfSave;
        try
        {
            try
            {
                NpkWriter.WriteFileStreaming(path, sources, selfSave
                    ? () => {
                        _reader.ReleaseHandle();
                        handleReleased = true;
                    }
                    : null);
            }
            catch
            {
                if (handleReleased)
                {
                    // 替换失败也要让对象恢复可用状态。
                    _reader.Reopen();
                }

                throw;
            }

            if (selfSave)
            {
                // 重新打开 + 重解析条目表：文件被替换后偏移可能整体变化，必须刷新。
                ReloadAfterReplace();
            }
        }
        finally
        {
            _saving = false;
        }

        TraceLog.Instance.Info($"NPK 已保存：{path}（{targets.Count} 条目）。");
    }

    /// <summary>
    ///     把待写条目转成"延迟供应数据"的条目源。
    ///     <para>
    ///         已修改条目 → 内存中的 IMG 重新编码（只有这些会占内存）；
    ///         未修改条目 → <see cref="NpkEntrySource.FromArchive" />，写入时按偏移分块漂移，
    ///         <b>峰值内存与包大小无关</b>（旧实现把所有条目一次性读进 <c>byte[][]</c>，98MB 包 → 约 200MB 峰值）。
    ///     </para>
    /// </summary>
    /// <param name="targets">待写条目（顺序即写入顺序）。</param>
    /// <returns>与 <paramref name="targets" /> 一一对应的条目源。</returns>
    private NpkEntrySource[] BuildSaveSources(IReadOnlyList<NpkEntry> targets)
    {
        var sources = new NpkEntrySource[targets.Count];
        for (var i = 0; i < targets.Count; i++)
        {
            var entry = targets[i];
            sources[i] = TryGetModifiedPayload(entry, out var payload)
                ? NpkEntrySource.FromBytes(entry.Path, payload)
                : NpkEntrySource.FromArchive(this, entry);
        }

        return sources;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _images.Clear();
        _reader.Dispose();
    }

    private void ReadEntryTable()
    {
        _entries.Clear();

        var head = _reader.ReadAt(0, NpkEntryTable.EntryTableOffset);
        var count = NpkEntryTable.ReadCount(head, FileLength);
        if (count == 0)
        {
            return;
        }

        var tableBytes = _reader.ReadAt(NpkEntryTable.EntryTableOffset, count * NpkEntryTable.EntrySize);
        _entries.AddRange(NpkEntryTable.Parse(tableBytes, count, FileLength));
    }

    /// <summary>
    ///     保存到自身路径后的恢复：重开句柄、刷新长度、重解析条目表。
    ///     <para>文件被整体替换后，所有偏移都可能变化，因此条目表必须重读（<see cref="Entries" /> 会换成新实例）。</para>
    /// </summary>
    private void ReloadAfterReplace()
    {
        _images.Clear();
        _reader.Reopen();
        ReadEntryTable();
    }

    private static bool IsSameFile(string path, string other)
    {
        if (string.IsNullOrEmpty(other))
        {
            return false;
        }

        var left = Path.GetFullPath(path);
        var right = Path.GetFullPath(other);
        return string.Equals(left, right, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void TryDelete(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            TraceLog.Instance.Warn($"清理临时文件失败：{path}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            TraceLog.Instance.Warn($"清理临时文件失败（无权限）：{path}", ex);
        }
    }

    private List<NpkEntry> BuildTargetList(IReadOnlyCollection<NpkEntry>? only)
    {
        if (only is null)
        {
            return [.. _entries];
        }

        var set = new HashSet<NpkEntry>(only);
        return _entries.Where(set.Contains).ToList();
    }

    /// <summary>
    ///     取"已修改条目"在保存时要写入的字节。
    ///     <para>返回 <c>false</c> 表示"按原字节流式直拷即可"（未修改）。</para>
    ///     <para>
    ///         脏条目取不到对应的 <see cref="ImgFile" /> 说明内存状态已不一致：此时必须中止保存，
    ///         不能退回原字节直拷，否则会产出"内容缺失但保存成功"的文件，比直接失败危险得多。
    ///     </para>
    /// </summary>
    /// <param name="entry">目标条目。</param>
    /// <param name="payload">已修改条目的新字节；返回 <c>false</c> 时为空数组。</param>
    /// <returns>true 表示拿到了需要重新写入的字节。</returns>
    /// <exception cref="InvalidOperationException">条目已置脏但缓存中找不到对应的 <see cref="ImgFile" />。</exception>
    private bool TryGetModifiedPayload(NpkEntry entry, out byte[] payload)
    {
        payload = [];
        if (!entry.IsModified)
        {
            return false;
        }

        if (!_images.TryGet(entry, out var image) || image is null)
        {
            throw new InvalidOperationException(
                $"条目 \"{entry.Path}\" 已标记为已修改，但内存中的 IMG 已丢失，继续保存会丢弃这次编辑。" +
                "已中止保存以保护数据；请重新打开该 NPK 并重做修改（这是一个缺陷，请连同操作步骤一起反馈）。");
        }

        payload = image.ToArray();
        return true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>写回自身路径期间拒绝按偏移读取（见 <see cref="_saving" /> 的说明）。</summary>
    private void ThrowIfSaving()
    {
        if (_saving)
        {
            throw new InvalidOperationException("正在把该 NPK 写回自身路径，请等待保存完成后再切换条目。");
        }
    }

    /// <summary>
    ///     返回可读文本，便于日志定位。
    /// </summary>
    public override string ToString()
    {
        return $"NpkArchive({FilePath}, {_entries.Count} entries, {FileLength} bytes)";
    }
}
