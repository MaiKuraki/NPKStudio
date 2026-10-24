namespace NPKStudio.Desktop.Controls.Rendering;

/// <summary>参考线方向。</summary>
internal enum GuideAxis {
    None,
    Horizontal,
    Vertical
}

/// <summary>
///     参考线的存储与增删（只有数据，没有任何绘制）。
///     <para>
///         水平参考线的值 = 角色坐标 Y，垂直参考线的值 = 角色坐标 X。
///         用 <see cref="List{T}" /> 保证绘制时零装箱。
///     </para>
/// </summary>
internal sealed class GuideStore {
    private readonly List<double> _horizontal = new();
    private readonly List<double> _vertical = new();

    /// <summary>水平参考线（角色坐标 Y）。</summary>
    public IReadOnlyList<double> Horizontal => _horizontal;

    /// <summary>垂直参考线（角色坐标 X）。</summary>
    public IReadOnlyList<double> Vertical => _vertical;

    /// <summary>参考线总数（水平 + 垂直）。</summary>
    public int Count => _horizontal.Count + _vertical.Count;

    public bool IsEmpty => _horizontal.Count == 0 && _vertical.Count == 0;

    /// <summary>追加一条参考线，返回它的下标。</summary>
    public int Add(GuideAxis axis, double value) {
        var list = axis == GuideAxis.Horizontal ? _horizontal : _vertical;
        list.Add(value);
        return list.Count - 1;
    }

    /// <summary>改写指定参考线的值（拖拽路径）；下标越界时忽略。</summary>
    public void Set(GuideAxis axis, int index, double value) {
        var list = axis == GuideAxis.Horizontal ? _horizontal : _vertical;
        if (index < 0 || index >= list.Count) return;
        list[index] = value;
    }

    /// <summary>删除指定参考线，返回是否真的删掉了一条。</summary>
    public bool RemoveAt(GuideAxis axis, int index) {
        var list = axis == GuideAxis.Horizontal ? _horizontal : _vertical;
        if (index < 0 || index >= list.Count) return false;
        list.RemoveAt(index);
        return true;
    }

    public void Clear() {
        _horizontal.Clear();
        _vertical.Clear();
    }
}
