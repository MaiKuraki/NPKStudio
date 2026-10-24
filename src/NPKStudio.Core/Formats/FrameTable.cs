using System.Collections;

namespace NPKStudio.Core.Formats;

/// <summary>
///     IMG 的帧表：只管"一组帧的顺序与下标"，不关心 NPK、不关心像素、不关心脏标记。
///     <para>
///         <b>为什么要单独成类</b>：增删/移动帧会让 <see cref="ImgFrame.Index" /> 与列表下标失配，
///         而写 LINK 帧时用的正是 <see cref="ImgFrame.Index" />。把"重排下标"这件事和集合放在一起，
///         调用方就不可能漏掉它。
///     </para>
///     <para>
///         实现 <see cref="IReadOnlyList{T}" /> 让 <c>ImgFile.Frames</c> 的对外契约保持不变；
///         内部调用一律走具体类型（<c>sealed</c> + 直接索引器），不走接口虚调用。
///     </para>
/// </summary>
internal sealed class FrameTable : IReadOnlyList<ImgFrame>
{
    private readonly List<ImgFrame> _items = [];

    /// <summary>帧数。</summary>
    public int Count => _items.Count;

    /// <summary>按下标取帧。</summary>
    /// <param name="index">下标。</param>
    public ImgFrame this[int index] => _items[index];

    /// <summary>追加一帧（下标自动分配）。</summary>
    /// <returns>新帧，字段为默认值。</returns>
    public ImgFrame Append()
    {
        var frame = new ImgFrame { Index = _items.Count };
        _items.Add(frame);
        return frame;
    }

    /// <summary>把已构造好的帧追加到末尾（handler 解析时使用，下标由本表统一分配）。</summary>
    /// <param name="frame">已构造的帧。</param>
    public void AppendExisting(ImgFrame frame)
    {
        frame.Index = _items.Count;
        _items.Add(frame);
    }

    /// <summary>在指定位置插入一帧（1×1 全透明）。</summary>
    /// <param name="index">插入位置，<c>[0, Count]</c>。</param>
    /// <param name="bytesPerPixel">该版本每像素的未压缩字节数（v4/v6 的色号格式为 1）。</param>
    /// <returns>新帧。</returns>
    /// <exception cref="ArgumentOutOfRangeException">下标越界。</exception>
    public ImgFrame Insert(int index, int bytesPerPixel = 2)
    {
        if (index < 0 || index > _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"插入下标必须在 0..{_items.Count} 之间。");
        }

        var frame = new ImgFrame();
        FrameLinks.DegradeToBlank(frame, bytesPerPixel);
        _items.Insert(index, frame);
        Renumber();
        return frame;
    }

    /// <summary>移除一帧并重排下标（<b>不处理 LINK</b>，那一步由调用方先做）。</summary>
    /// <param name="frame">要移除的帧。</param>
    /// <returns>是否找到并移除。</returns>
    public bool Remove(ImgFrame frame)
    {
        var index = _items.IndexOf(frame);
        if (index < 0)
        {
            return false;
        }

        _items.RemoveAt(index);
        Renumber();
        return true;
    }

    /// <summary>把一帧移到另一个位置并重排下标。</summary>
    /// <param name="fromIndex">源下标。</param>
    /// <param name="toIndex">目标下标。</param>
    /// <returns>是否发生了移动。</returns>
    /// <exception cref="ArgumentOutOfRangeException">下标越界。</exception>
    public bool Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(fromIndex), fromIndex, $"源下标必须在 0..{_items.Count - 1} 之间。");
        }

        if (toIndex < 0 || toIndex >= _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(toIndex), toIndex, $"目标下标必须在 0..{_items.Count - 1} 之间。");
        }

        if (fromIndex == toIndex)
        {
            return false;
        }

        var frame = _items[fromIndex];
        _items.RemoveAt(fromIndex);
        _items.Insert(toIndex, frame);
        Renumber();
        return true;
    }

    /// <summary>帧是否属于本表（用引用比较，不用 <c>Equals</c>）。</summary>
    /// <param name="frame">待查帧。</param>
    /// <returns>是否属于。</returns>
    public bool Contains(ImgFrame frame) => IndexOf(frame) >= 0;

    /// <summary>取帧的下标，找不到返回 <c>-1</c>。</summary>
    /// <param name="frame">待查帧。</param>
    /// <returns>下标或 <c>-1</c>。</returns>
    public int IndexOf(ImgFrame frame)
    {
        // 先 O(1) 试探：绝大多数情况下 frame.Index 就是它的位置。
        var fast = frame.Index;
        if ((uint)fast < (uint)_items.Count && ReferenceEquals(_items[fast], frame))
        {
            return fast;
        }

        return _items.IndexOf(frame);
    }

    /// <summary>
    ///     用给定的帧序列<b>整体替换</b>帧表（撤销 / 重做恢复顺序时使用）。
    ///     <para>
    ///         只接受已经属于本文件的帧对象（引用复用），不新建帧、不复制像素；
    ///         重建后立即重排 <see cref="ImgFrame.Index" />，避免写 LINK 时指错。
    ///     </para>
    /// </summary>
    /// <param name="frames">按目标顺序排列的帧。</param>
    public void Restore(IReadOnlyList<ImgFrame> frames)
    {
        _items.Clear();
        for (var i = 0; i < frames.Count; i++)
        {
            _items.Add(frames[i]);
        }

        Renumber();
    }

    /// <summary>重排每帧的 <see cref="ImgFrame.Index" /> 使其等于列表下标。</summary>
    public void Renumber()
    {
        for (var i = 0; i < _items.Count; i++)
        {
            _items[i].Index = i;
        }
    }

    /// <summary>结构体枚举器（<c>foreach</c> 走具体类型时不装箱）。</summary>
    /// <returns>枚举器。</returns>
    public List<ImgFrame>.Enumerator GetEnumerator() => _items.GetEnumerator();

    IEnumerator<ImgFrame> IEnumerable<ImgFrame>.GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
