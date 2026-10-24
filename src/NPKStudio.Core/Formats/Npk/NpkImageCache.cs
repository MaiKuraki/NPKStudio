using NPKStudio.Core.Exceptions;

namespace NPKStudio.Core.Formats.Npk;

/// <summary>
///     已解析 IMG 的 LRU 缓存（容量 8）。
///     <para>
///         从 <c>NpkArchive</c> 抽出来的：那类原先把"条目解析 + 随机读 + IMG 缓存 + 保存"混在一起，
///         这里只负责"按条目缓存已解析的 <see cref="ImgFile" />，超容量按最久未用淘汰"。
///     </para>
///     <para>
///         解析需要读原始字节，因此由调用方传入 <see cref="NpkRawReader" />（<b>直接方法调用，不走委托</b>）。
///     </para>
/// </summary>
/// <remarks>
///     当前 Desktop 侧全部是串行调用，这段并发设计零收益。
///     若将来确认单线程，可降级为普通 Dictionary + 单锁。
/// </remarks>
internal sealed class NpkImageCache
{
    /// <summary>缓存容量。</summary>
    public const int Capacity = 8;

    private readonly object _gate = new();
    private readonly Dictionary<NpkEntry, LinkedListNode<KeyValuePair<NpkEntry, ImgFile>>> _map = [];
    private readonly LinkedList<KeyValuePair<NpkEntry, ImgFile>> _order = [];

    /// <summary>
    ///     惰性解析（或从 LRU 取出）指定条目的 IMG。
    /// </summary>
    /// <param name="entry">目标条目。</param>
    /// <param name="reader">字节来源（缓存命中时不会被读取）。</param>
    /// <returns>解析后的 IMG；同时回填 <see cref="NpkEntry.FrameCount" />。</returns>
    /// <exception cref="ImgFormatException">条目不是合法的 IMG。</exception>
    public ImgFile LoadImg(NpkEntry entry, NpkRawReader reader)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(reader);

        lock (_gate)
        {
            if (_map.TryGetValue(entry, out var cached))
            {
                _order.Remove(cached);
                _order.AddFirst(cached);
                return cached.Value.Value;
            }
        }

        var data = reader.ReadRaw(entry);
        ImgFile image;
        using (var stream = new MemoryStream(data, writable: false))
        {
            image = ImgFile.Read(stream, entry.Path, data.Length);
        }

        entry.FrameCount = image.Frames.Count;

        // 打通"改帧 → 标记条目脏 → Save 重编码"链路：ImgFile.ReplaceFrameImage 通过它回写 IsModified。
        image.OwnerEntry = entry;

        lock (_gate)
        {
            // 双检：并发下可能已被别的线程放进去了。
            if (_map.TryGetValue(entry, out var existing))
            {
                return existing.Value.Value;
            }

            var node = _order.AddFirst(new KeyValuePair<NpkEntry, ImgFile>(entry, image));
            _map[entry] = node;
            Trim();
        }

        return image;
    }

    /// <summary>
    ///     只查缓存、不解析（用于"标记为已修改"的条目取内存中的 IMG）。
    /// </summary>
    /// <param name="entry">目标条目。</param>
    /// <param name="image">命中的 IMG；未命中为 <c>null</c>。</param>
    /// <returns>命中返回 <c>true</c>。</returns>
    public bool TryGet(NpkEntry entry, out ImgFile? image)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(entry, out var node))
            {
                image = node.Value.Value;
                return true;
            }
        }

        image = null;
        return false;
    }

    /// <summary>清空缓存（关闭归档、保存到自身后重解析时调用）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _order.Clear();
        }
    }

    private void Trim()
    {
        while (_map.Count > Capacity)
        {
            var last = _order.Last;
            if (last is null)
            {
                break;
            }

            _order.RemoveLast();
            _map.Remove(last.Value.Key);
        }
    }
}
