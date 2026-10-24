using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;

namespace NPKStudio.Desktop.ViewModels.Channels;

/// <summary>
///     一帧的显示像素：<paramref name="Raw" /> 是解码原样（导出 / 换图 / 另存必须用它），
///     <paramref name="Display" /> 是套过画布效果后的结果（画布只用它）。
/// </summary>
internal readonly record struct FramePixels(RgbaImage Raw, RgbaImage Display);

/// <summary>
///     帧位图的按帧缓存：把"每次切帧都重新解码"变成"每帧只解一次"。
///     <para>
///         一次解码要分配两份超过 LOH 阈值的缓冲：解压后的像素（<c>W×H×2</c>）与 BGRA 输出
///         （<c>W×H×4</c>）。播放每拍重解就是每拍一次大对象分配；缓存命中后，
///         从第二圈起不再解码。
///     </para>
///     <para>
///         失效依靠内容比对而不是显式通知：缓存项记录取图时的 <see cref="ImgFrame.RawData" /> 引用、
///         <see cref="ImgFrame.Texture" /> 引用与几何/格式字段，命中前逐项比对。Core 的编辑路径
///         一律整段替换数组或纹理引用，因此编辑后必然失效，调用方不必在每个编辑入口手工清缓存。
///     </para>
///     <para>
///         线程：解码在锁外进行（<see cref="ImgFile.Decode" /> 是纯函数，重复解同一帧无害），
///         字典操作在锁内；<see cref="Prefetch" /> 在后台线程预解，切换文档时依靠代次号停止。
///     </para>
/// </summary>
internal sealed class FrameImageCache
{
    /// <summary>
    ///     缓存字节上限。按"典型 DNF 帧 50~200KB"估算可容纳数百帧；
    ///     按最坏情况 609×554（每帧 1.29MB）也能容纳约 50 帧，覆盖绝大多数 ani 片段。
    /// </summary>
    private const long ByteBudget = 64L * 1024 * 1024;

    /// <summary>预解到"预算的 3/4"就停手，给后续交互解码留余量。</summary>
    private const long PrefetchHighWater = ByteBudget / 4 * 3;

    private readonly Func<RgbaImage, RgbaImage> _applyDisplayEffect;
    private readonly object _gate = new();
    private readonly Dictionary<ImgFrame, LinkedListNode<Entry>> _index = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<Entry> _lru = new();

    private long _bytes;
    private int _generation;

    /// <summary>构造。</summary>
    /// <param name="applyDisplayEffect">
    ///     画布效果的纯函数（输入原始像素、输出显示副本）。由调用方注入，避免缓存层知道"线性减淡"这种业务概念。
    /// </param>
    public FrameImageCache(Func<RgbaImage, RgbaImage> applyDisplayEffect)
    {
        _applyDisplayEffect = applyDisplayEffect;
    }

    /// <summary>当前文档代次；<see cref="Clear" /> 会自增，用于让在途的预解任务立刻退出。</summary>
    public int Generation
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    /// <summary>取一帧的像素：命中缓存直接返回，未命中则解码并放入缓存。</summary>
    /// <param name="file">帧所属 IMG。</param>
    /// <param name="frame">目标帧。</param>
    /// <param name="useDisplayEffect">是否要显示效果副本（<see cref="FramePixels.Display" />）。</param>
    /// <param name="generation">调用方持有的代次；与当前不一致时只解码不入缓存。</param>
    /// <returns>像素；解码失败或 LINK 帧返回 <c>null</c>。</returns>
    public FramePixels? GetOrDecode(ImgFile file, ImgFrame frame, bool useDisplayEffect, int generation)
    {
        lock (_gate)
        {
            if (_index.TryGetValue(frame, out var node) && node.Value.IsValidFor(frame))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return Materialize(node.Value, useDisplayEffect);
            }
        }

        RgbaImage? raw;
        try
        {
            raw = file.Decode(frame);
        }
        catch
        {
            // 解码失败不缓存：下一帧照样尝试，坏帧不会把缓存带脏。
            return null;
        }

        if (raw is null)
        {
            return null;
        }

        lock (_gate)
        {
            if (generation != _generation)
            {
                // 解码期间换了文档：结果只给本次调用，不污染新文档的缓存。
                return new FramePixels(raw, useDisplayEffect ? _applyDisplayEffect(raw) : raw);
            }

            var entry = new Entry(frame, raw);
            var added = _lru.AddFirst(entry);
            _index[frame] = added;
            _bytes += entry.Bytes;
            Trim();

            return Materialize(entry, useDisplayEffect);
        }
    }

    /// <summary>
    ///     后台预解一组帧（顺序即播放顺序），用于把首圈播放的解码开销提前摊掉。
    ///     <para>达到高水位或文档被切换时立即停止；不做重试、不抛异常。</para>
    /// </summary>
    /// <param name="file">帧所属 IMG。</param>
    /// <param name="frames">要预解的帧（按播放顺序）。</param>
    public void Prefetch(ImgFile file, IReadOnlyList<ImgFrame> frames)
    {
        if (frames.Count == 0)
        {
            return;
        }

        var generation = Generation;
        _ = Task.Run(() =>
        {
            for (var i = 0; i < frames.Count; i++)
            {
                if (Generation != generation || OverHighWater)
                {
                    return;
                }

                GetOrDecode(file, frames[i], useDisplayEffect: false, generation);
            }
        });
    }

    /// <summary>丢弃全部缓存并推进代次（切换 IMG / 关闭文档时调用）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _generation++;
            _index.Clear();
            _lru.Clear();
            _bytes = 0;
        }
    }

    private bool OverHighWater
    {
        get
        {
            lock (_gate)
            {
                return _bytes >= PrefetchHighWater;
            }
        }
    }

    /// <summary>取显示副本：效果副本按需生成并随缓存项一起失效。</summary>
    private FramePixels Materialize(Entry entry, bool useDisplayEffect)
    {
        if (!useDisplayEffect)
        {
            return new FramePixels(entry.Raw, entry.Raw);
        }

        // 双重检查：可能刚被别的线程补上。
        var display = entry.Display;
        if (display is not null)
        {
            return new FramePixels(entry.Raw, display);
        }

        display = _applyDisplayEffect(entry.Raw);
        entry.Display = display;
        _bytes += display.Pixels.Length;   // 效果副本同样占内存，必须计入预算
        return new FramePixels(entry.Raw, display);
    }

    /// <summary>按 LRU 淘汰到预算之内。</summary>
    private void Trim()
    {
        while (_bytes > ByteBudget && _lru.Last is { } last)
        {
            _lru.RemoveLast();
            _index.Remove(last.Value.Frame);
            _bytes -= last.Value.Bytes;
        }
    }

    /// <summary>一个缓存项：解码结果 + 取图那一刻的"内容指纹"。</summary>
    private sealed class Entry
    {
        public Entry(ImgFrame frame, RgbaImage raw)
        {
            Frame = frame;
            Raw = raw;
            RawData = frame.RawData;
            Texture = frame.Texture;
            Width = frame.Width;
            Height = frame.Height;
            Type = frame.Type;
            Compress = frame.Compress;
        }

        public ImgFrame Frame { get; }

        public RgbaImage Raw { get; }

        /// <summary>线性减淡后的显示副本；切换效果前为 <c>null</c>。</summary>
        public RgbaImage? Display { get; set; }

        public long Bytes => Raw.Pixels.Length + (Display?.Pixels.Length ?? 0);

        private byte[] RawData { get; }
        private TextureRef? Texture { get; }
        private int Width { get; }
        private int Height { get; }
        private ColorBits Type { get; }
        private CompressMode Compress { get; }

        /// <summary>帧内容是否仍是缓存时的样子（任何编辑都会换引用或改字段，从而失败）。</summary>
        public bool IsValidFor(ImgFrame frame) =>
            ReferenceEquals(RawData, frame.RawData)
            && ReferenceEquals(Texture, frame.Texture)
            && Width == frame.Width
            && Height == frame.Height
            && Type == frame.Type
            && Compress == frame.Compress;
    }
}
