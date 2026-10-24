using System.Buffers.Binary;
using System.Security.Cryptography;

using NPKStudio.Core.Formats;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>
///     帧级变更标记的"按基线重算"器。
///     <para>
///         <b>为什么不是"编辑时置位"</b>：撤销 / 重做会把整个模型回滚，置位式标记回滚不掉
///         （撤销后仍然显示"已替换"）。这里只在两个时刻拍基线快照（加载完成、保存成功），
///         其余时候拿当前帧表与基线逐项比对，标记天然跟着模型走——撤销回去自然就没了。
///     </para>
///     <para>
///         <b>键用对象引用</b>：<see cref="ImgFrame" /> 是 IMG 生命周期内的稳定标识，
///         插入 / 删除只改帧表，不会重建已有的 <see cref="ImgFrame" /> 实例（撤销快照也存同一批引用）。
///         基线随 IMG 关闭（<see cref="Clear" />）/ 重新拍快照（<see cref="Capture" />）整体丢弃，
///         不会跨文档累积。
///     </para>
///     <para>
///         <b>哈希策略</b>：先比 <see cref="ImgFrame.RawData" /> 的<b>引用</b>，不同才走 SHA-256
///         （Core 的所有编辑路径都是"整段赋新数组"，从不原地改字节，见 <see cref="ImgEditHistory" /> 注释）。
///         因此常态刷新（移动 / 插入 / 调坐标）零哈希开销，只有真正被换过素材的那几帧才算一次。
///     </para>
/// </summary>
internal sealed class FrameChangeTracker
{
    private readonly Dictionary<ImgFrame, Baseline> _baseline = new(ReferenceEqualityComparer.Instance);

    /// <summary>数组引用 → 内容哈希。编辑只换数组不原地改字节，所以引用相同即内容相同。</summary>
    private readonly Dictionary<byte[], ulong> _hashes = new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<ImgFrame, FrameChangeKind> _marks = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ImgFrame, int> _positions = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    ///     插入帧的"落点快照"：新帧没有基线可比，但插入之后又被换素材 / 改尺寸时，
    ///     仍应同时给出 <c>Pixels</c> / <c>Geometry</c>（第一次见到它时记下当时的样子）。
    /// </summary>
    private readonly Dictionary<ImgFrame, Baseline> _inserted = new(ReferenceEqualityComparer.Instance);

    private readonly List<int> _deletedIndexes = [];

    /// <summary>当前有标记的帧数（卡片口径，与 <see cref="MainWindowViewModel.Frames" /> 一一对应）。</summary>
    public int ChangedCount { get; private set; }

    /// <summary>拍一次基线快照（加载完成 / 保存成功时调用），并清空既有标记。</summary>
    /// <param name="image">当前 IMG；<c>null</c> 表示没有文档。</param>
    public void Capture(ImgFile? image)
    {
        _baseline.Clear();
        _hashes.Clear();
        _marks.Clear();
        _inserted.Clear();
        ChangedCount = 0;

        if (image is null)
        {
            return;
        }

        var frames = image.Frames;
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            _baseline[frame] = new Baseline(frame.RawData, frame.X, frame.Y, frame.Width, frame.Height, i);
        }
    }

    /// <summary>丢弃基线（关闭 IMG / 切条目时调用）：标记全部归零，也不再持有任何帧引用。</summary>
    public void Clear()
    {
        _baseline.Clear();
        _hashes.Clear();
        _marks.Clear();
        _inserted.Clear();
        ChangedCount = 0;
    }

    /// <summary>
    ///     按当前帧表与基线重算每张卡片的 <see cref="FrameViewModel.ChangeKind" />。
    ///     <para>复杂度 O(帧数)，常态零哈希；500 帧规模实测在 1ms 以内。</para>
    /// </summary>
    /// <param name="image">当前 IMG；<c>null</c> 时把所有卡片清成无标记。</param>
    /// <param name="cards">右侧帧列表的卡片（与 IMG 帧表可能短暂不同步，按引用查找）。</param>
    public void Refresh(ImgFile? image, IReadOnlyList<FrameViewModel> cards)
    {
        _marks.Clear();
        ChangedCount = 0;

        var frames = image?.Frames;
        if (frames is null || _baseline.Count == 0)
        {
            foreach (var card in cards)
            {
                card.ChangeKind = FrameChangeKind.None;
            }

            return;
        }

        _positions.Clear();
        for (var i = 0; i < frames.Count; i++)
        {
            _positions[frames[i]] = i;
        }

        // 基线里存在、当前已不存在的帧：只参与"期望下标"的补偿，本身不产生标记（帧都没了）
        _deletedIndexes.Clear();
        foreach (var entry in _baseline)
        {
            if (!_positions.ContainsKey(entry.Key))
            {
                _deletedIndexes.Add(entry.Value.Index);
            }
        }

        _deletedIndexes.Sort();

        var insertedBefore = 0;
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            if (!_baseline.TryGetValue(frame, out var baseline))
            {
                // 插入的帧没有基线：第一次见到时记下"刚落下来"的样子，
                // 之后若又被换过素材 / 改过尺寸，标记里就能同时带上 Pixels / Geometry。
                if (!_inserted.TryGetValue(frame, out var origin))
                {
                    origin = new Baseline(frame.RawData, frame.X, frame.Y, frame.Width, frame.Height, i);
                    _inserted[frame] = origin;
                }

                var insertedKind = FrameChangeKind.Inserted;
                if (!SamePixels(origin.Data, frame.RawData))
                {
                    insertedKind |= FrameChangeKind.Pixels;
                }

                if (origin.X != frame.X || origin.Y != frame.Y ||
                    origin.Width != frame.Width || origin.Height != frame.Height)
                {
                    insertedKind |= FrameChangeKind.Geometry;
                }

                _marks[frame] = insertedKind;
                insertedBefore++;
                continue;
            }

            var kind = FrameChangeKind.None;
            if (!SamePixels(baseline.Data, frame.RawData))
            {
                kind |= FrameChangeKind.Pixels;
            }

            if (baseline.X != frame.X || baseline.Y != frame.Y ||
                baseline.Width != frame.Width || baseline.Height != frame.Height)
            {
                kind |= FrameChangeKind.Geometry;
            }

            // 期望下标要把"插入 / 删除造成的整体平移"扣掉：否则在列表头插一帧，
            // 后面每一帧的下标都变了，全都会被标成"已移动"——那就等于没标记。
            var expected = baseline.Index - CountDeletedBefore(baseline.Index) + insertedBefore;
            if (i != expected)
            {
                kind |= FrameChangeKind.Moved;
            }

            if (kind != FrameChangeKind.None)
            {
                _marks[frame] = kind;
            }
        }

        foreach (var card in cards)
        {
            var kind = _marks.TryGetValue(card.Frame, out var mark) ? mark : FrameChangeKind.None;
            card.ChangeKind = kind;
            if (kind != FrameChangeKind.None)
            {
                ChangedCount++;
            }
        }
    }

    /// <summary>两段原始字节是否同一份内容：先比引用（常态命中），再比长度，最后比 SHA-256。</summary>
    private bool SamePixels(byte[] left, byte[] right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left.Length == right.Length && HashOf(left) == HashOf(right);
    }

    /// <summary>内容哈希（取 SHA-256 前 8 字节，按引用缓存）。</summary>
    private ulong HashOf(byte[] data)
    {
        if (_hashes.TryGetValue(data, out var cached))
        {
            return cached;
        }

        var hash = BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(data));
        _hashes[data] = hash;
        return hash;
    }

    /// <summary>基线下标小于 <paramref name="baselineIndex" /> 的"已删除帧"数量（二分，已排序）。</summary>
    private int CountDeletedBefore(int baselineIndex)
    {
        var lo = 0;
        var hi = _deletedIndexes.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (_deletedIndexes[mid] < baselineIndex)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    /// <summary>基线里一帧的全部可比字段。</summary>
    /// <param name="Data">当时的原始字节（<b>引用</b>，不复制）。</param>
    /// <param name="X">当时的 X。</param>
    /// <param name="Y">当时的 Y。</param>
    /// <param name="Width">当时的宽。</param>
    /// <param name="Height">当时的高。</param>
    /// <param name="Index">当时在帧表里的下标。</param>
    private readonly record struct Baseline(byte[] Data, int X, int Y, int Width, int Height, int Index);
}
