namespace NPKStudio.Core.Formats;

/// <summary>
///     一个 IMG 的撤销 / 重做栈（Memento + 有界栈）。
///     <para>
///         <b>为什么用快照而不是命令对象</b>：本项目的所有编辑入口最终都只是改
///         <see cref="ImgFrame" /> 的十几个可变字段与帧表顺序，一条"命令"要还原它就得自己再存一遍这些字段；
///         直接存"编辑前的帧序列 + 每帧字段"反而更小、更少代码，也不会为每种操作新增一个类型。
///     </para>
///     <para>
///         <b>快照是 O(帧数) 的小对象</b>：<see cref="ImgFrame.RawData" /> 只存<b>引用</b>不复制——
///         本项目所有编辑路径都是"整段赋新数组"，没有原地改字节，所以共享引用安全。
///         唯一的例外是调用方绕过本类直接原地改 <c>RawData[i]</c>，那样撤销会看不到旧内容。
///     </para>
///     <para>
///         容量默认 50：超出后丢<b>最老</b>的一条（<see cref="Depth" /> 是"当前还可撤销的步数"）。
///     </para>
/// </summary>
public sealed class ImgEditHistory
{
    /// <summary>默认可撤销步数。</summary>
    public const int DefaultCapacity = 50;

    private readonly ImgFile _file;
    private readonly int _capacity;
    private readonly List<Snapshot> _undo = [];
    private readonly List<Snapshot> _redo = [];

    /// <summary>最近一次 <see cref="Record" /> / <see cref="Undo" /> / <see cref="Redo" /> 之后的模型状态。</summary>
    private Snapshot _current;

    /// <summary>
    ///     为一个 IMG 建立历史栈（构造时立刻取一份"当前状态"作为基线）。
    /// </summary>
    /// <param name="file">要记录编辑历史的 IMG。</param>
    /// <param name="capacity">最多保留多少步可撤销记录（&lt; 1 时按 1 处理）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="file" /> 为 <c>null</c>。</exception>
    public ImgEditHistory(ImgFile file, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(file);
        _file = file;
        _capacity = Math.Max(1, capacity);
        _current = Snapshot.Capture(file);
    }

    /// <summary>是否还有可撤销的编辑。</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>是否还有可重做的编辑。</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>当前可撤销的步数（上限为构造时给的容量）。</summary>
    public int Depth => _undo.Count;

    /// <summary>
    ///     一次编辑成功后调用：把"编辑前快照"压入撤销栈、清空重做栈，并重取当前快照。
    ///     <para>必须在模型改动<b>之后</b>调用；否则压进去的"编辑前"其实是编辑后的状态。</para>
    /// </summary>
    public void Record()
    {
        _undo.Add(_current);
        if (_undo.Count > _capacity)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        _current = Snapshot.Capture(_file);
    }

    /// <summary>
    ///     撤销一步：把当前状态压入重做栈并恢复上一份快照。
    /// </summary>
    /// <returns>是否真的撤销了（没有可撤销记录时返回 <c>false</c> 且不改动模型）。</returns>
    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(_current);
        _current = previous;
        Apply(_file, _current);
        return true;
    }

    /// <summary>
    ///     重做一步：把当前状态压回撤销栈并恢复下一份快照。
    /// </summary>
    /// <returns>是否真的重做了（没有可重做记录时返回 <c>false</c> 且不改动模型）。</returns>
    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(_current);

        // 重做也占用容量：连续重做把栈填满后同样丢最老的一条，Depth 才能如实反映可撤销步数。
        if (_undo.Count > _capacity)
        {
            _undo.RemoveAt(0);
        }

        _current = next;
        Apply(_file, _current);
        return true;
    }

    /// <summary>清空撤销 / 重做栈，并把当前状态作为新基线（打开新 IMG、切换条目时调用）。</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _current = Snapshot.Capture(_file);
    }

    /// <summary>
    ///     把一份快照写回模型：先按快照重排帧表，再逐帧写回字段，最后重排下标并标脏。
    /// </summary>
    private static void Apply(ImgFile file, Snapshot snapshot)
    {
        var frames = new ImgFrame[snapshot.States.Length];
        for (var i = 0; i < frames.Length; i++)
        {
            frames[i] = snapshot.States[i].Frame;
        }

        // 重建帧表会重排 Index；字段写回不碰 Index，所以顺序必须是"先重排表、再写字段"。
        file.RestoreFrameTable(frames);

        foreach (var state in snapshot.States)
        {
            var frame = state.Frame;
            frame.Type = state.Type;
            frame.Compress = state.Compress;
            frame.X = state.X;
            frame.Y = state.Y;
            frame.Width = state.Width;
            frame.Height = state.Height;
            frame.Length = state.Length;
            frame.FrameWidth = state.FrameWidth;
            frame.FrameHeight = state.FrameHeight;
            frame.RawData = state.RawData;
            frame.LinkTarget = state.LinkTarget;
            frame.LinkIndex = state.LinkIndex;
            frame.Texture = state.Texture;
        }

        file.MarkModified();
    }

    /// <summary>一帧的全部可变字段（<see cref="ImgFrame.RawData" /> 与 <c>LinkTarget/Texture</c> 存引用）。</summary>
    private readonly record struct FrameState(
        ImgFrame Frame,
        ColorBits Type,
        CompressMode Compress,
        int X,
        int Y,
        int Width,
        int Height,
        int Length,
        int FrameWidth,
        int FrameHeight,
        byte[] RawData,
        ImgFrame? LinkTarget,
        int LinkIndex,
        TextureRef? Texture);

    /// <summary>一次快照：帧的顺序就是数组顺序，每项带该帧当时的全部可变字段。</summary>
    private sealed class Snapshot
    {
        private Snapshot(FrameState[] states) => States = states;

        public FrameState[] States { get; }

        /// <summary>按当前帧表顺序取一份快照。</summary>
        public static Snapshot Capture(ImgFile file)
        {
            var frames = file.Frames;
            var states = new FrameState[frames.Count];
            for (var i = 0; i < states.Length; i++)
            {
                var frame = frames[i];
                states[i] = new FrameState(
                    frame,
                    frame.Type,
                    frame.Compress,
                    frame.X,
                    frame.Y,
                    frame.Width,
                    frame.Height,
                    frame.Length,
                    frame.FrameWidth,
                    frame.FrameHeight,
                    frame.RawData,
                    frame.LinkTarget,
                    frame.LinkIndex,
                    frame.Texture);
            }

            return new Snapshot(states);
        }
    }
}
