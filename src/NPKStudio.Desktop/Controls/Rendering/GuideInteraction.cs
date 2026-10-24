using Avalonia;
using Avalonia.Input;

namespace NPKStudio.Desktop.Controls.Rendering;

/// <summary>
///     参考线的指针交互：命中判定、悬停光标、拖拽状态机。
///     <para>只维护状态与几何判断，不直接碰控件：重绘 / 事件 / 指针捕获仍由 <see cref="ImageCanvas" /> 负责。</para>
/// </summary>
internal sealed class GuideInteraction {
    /// <summary>参考线的屏幕命中容差。</summary>
    private const double HitTolerance = 4;

    private Cursor? _cursorNorthSouth;
    private Cursor? _cursorWestEast;

    /// <summary>正在拖拽的轴（<see cref="GuideAxis.None" /> = 没在拖）。</summary>
    public GuideAxis DragAxis { get; private set; }

    /// <summary>正在拖拽的线下标（-1 = 无）。</summary>
    public int DragIndex { get; private set; } = -1;

    /// <summary>当前悬停的轴（决定标签高亮）。</summary>
    public GuideAxis HoverAxis { get; private set; }

    /// <summary>当前悬停的线下标（-1 = 无）。</summary>
    public int HoverIndex { get; private set; } = -1;

    /// <summary>当前是否正在拖拽一条参考线。</summary>
    public bool IsDragging => DragAxis != GuideAxis.None;

    /// <summary>最近一次交互后的光标形状（由控件赋给 <c>Cursor</c> 属性）。</summary>
    public Cursor Cursor { get; private set; } = Cursor.Default;

    private HoverKind _hoverKind;

    /// <summary>命中容差内最近的一条参考线（水平线优先，与绘制顺序一致）。</summary>
    public bool TryHit(GuideStore store, in CanvasRenderContext ctx, Point position,
        out GuideAxis axis, out int index) {
        axis = GuideAxis.None;
        index = -1;

        var best = HitTolerance;
        for (var i = 0; i < store.Horizontal.Count; i++) {
            var distance = Math.Abs(ctx.FrameYToScreen(store.Horizontal[i]) - position.Y);
            if (distance > best) continue;
            best = distance;
            axis = GuideAxis.Horizontal;
            index = i;
        }

        for (var i = 0; i < store.Vertical.Count; i++) {
            var distance = Math.Abs(ctx.FrameXToScreen(store.Vertical[i]) - position.X);
            if (distance > best) continue;
            best = distance;
            axis = GuideAxis.Vertical;
            index = i;
        }

        return axis != GuideAxis.None;
    }

    /// <summary>
    ///     悬停光标切换：标尺 / 参考线上给对应方向的调整光标。
    ///     <para>只有轴 / 下标变化才需要重绘（标签高亮换人），只有光标种类变化才需要重新赋值。</para>
    /// </summary>
    public Cursor UpdateHover(GuideStore store, in CanvasRenderContext ctx, Point position,
        out bool visualChanged, out bool cursorChanged) {
        var kind = HoverKind.None;
        var hoverAxis = GuideAxis.None;
        var hoverIndex = -1;

        if (ctx.IsRulersVisible && position.Y < RulerRenderer.Thickness && position.X >= RulerRenderer.Thickness) {
            kind = HoverKind.Horizontal;
        } else if (ctx.IsRulersVisible && position.X < RulerRenderer.Thickness && position.Y >= RulerRenderer.Thickness) {
            kind = HoverKind.Vertical;
        } else if (ctx.IsGuidesVisible && ctx.Content.Contains(position) &&
                   TryHit(store, in ctx, position, out var axis, out var index)) {
            kind = axis == GuideAxis.Horizontal ? HoverKind.Horizontal : HoverKind.Vertical;
            hoverAxis = axis;
            hoverIndex = index;
        }

        // 悬停到"另一条"参考线上时，虽然光标形状没变，标签高亮也要跟着换
        visualChanged = hoverAxis != HoverAxis || hoverIndex != HoverIndex;
        if (visualChanged) {
            HoverAxis = hoverAxis;
            HoverIndex = hoverIndex;
        }

        cursorChanged = kind != _hoverKind;
        if (cursorChanged) {
            _hoverKind = kind;
            Cursor = kind switch {
                HoverKind.Horizontal => CursorNorthSouth,
                HoverKind.Vertical => CursorWestEast,
                _ => Cursor.Default
            };
        }

        return Cursor;
    }

    /// <summary>
    ///     开始拖动参考线。新建时立刻入表（便于绘制跟随），拖回标尺松开再删掉。
    /// </summary>
    public Cursor BeginDrag(GuideStore store, in CanvasRenderContext ctx, GuideAxis axis, int index,
        Point position, out bool added) {
        var frame = ctx.ToFrame(position);
        // 像素艺术没有"半条参考线"：开启像素对齐时落点直接吸附到整像素
        double value = axis == GuideAxis.Horizontal ? frame.Y : frame.X;
        if (ctx.IsPixelAlignEnabled) {
            value = Math.Round(value);
        }

        added = index < 0;
        if (added) index = store.Add(axis, value);

        DragAxis = axis;
        DragIndex = index;
        _hoverKind = axis == GuideAxis.Horizontal ? HoverKind.Horizontal : HoverKind.Vertical;
        Cursor = axis == GuideAxis.Horizontal ? CursorNorthSouth : CursorWestEast;
        return Cursor;
    }

    /// <summary>拖拽中：把线吸附到指针所在位置（像素对齐时取整）。</summary>
    public void MoveDrag(GuideStore store, in CanvasRenderContext ctx, Point position) {
        var frame = ctx.ToFrame(position);
        var snap = ctx.IsPixelAlignEnabled;
        store.Set(DragAxis, DragIndex, DragAxis == GuideAxis.Horizontal
            ? (snap ? Math.Round(frame.Y) : frame.Y)
            : (snap ? Math.Round(frame.X) : frame.X));
    }

    /// <summary>结束拖拽；<paramref name="remove" /> = 拖回标尺 / 拖出画布，松手即删除。</summary>
    public bool EndDrag(GuideStore store, bool remove) {
        var removed = remove && DragIndex >= 0 && store.RemoveAt(DragAxis, DragIndex);

        DragAxis = GuideAxis.None;
        DragIndex = -1;
        _hoverKind = HoverKind.None;
        HoverAxis = GuideAxis.None;
        HoverIndex = -1;
        Cursor = Cursor.Default;
        return removed;
    }

    /// <summary>指针离开画布：只清悬停态（拖拽由指针捕获保证不会走到这里）。</summary>
    public void ResetHover() {
        _hoverKind = HoverKind.None;
        HoverAxis = GuideAxis.None;
        HoverIndex = -1;
        Cursor = Cursor.Default;
    }

    private Cursor CursorNorthSouth => _cursorNorthSouth ??= new Cursor(StandardCursorType.SizeNorthSouth);

    private Cursor CursorWestEast => _cursorWestEast ??= new Cursor(StandardCursorType.SizeWestEast);

    private enum HoverKind {
        None,
        Horizontal,
        Vertical
    }
}
