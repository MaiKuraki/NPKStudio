using Avalonia;
using Avalonia.Media;
using NPKStudio.Core.Imaging;

namespace NPKStudio.Desktop.Controls.Rendering;

/// <summary>
///     一次绘制所需的全部只读参数（视口 / 内容矩形 / 视口变换 / 主题画刷 / 开关位）。
///     <para>
///         值类型 + <c>in</c> 传递：渲染路径上不产生额外堆分配。
///         这里只做"取值"，所有绘制状态仍归 <see cref="ImageCanvas" /> 所有。
///     </para>
/// </summary>
internal readonly struct CanvasRenderContext {
    /// <summary>整个控件的屏幕矩形。</summary>
    public required Rect Viewport { get; init; }

    /// <summary>去掉标尺边带后的可绘制区域（参考线 / 十字线 / 标签的裁剪范围）。</summary>
    public required Rect Content { get; init; }

    /// <summary>画布（联合包围盒）在屏幕上的矩形（像素对齐后的绘制偏移）。</summary>
    public required Rect CanvasRect { get; init; }

    /// <summary>当前缩放倍数。</summary>
    public required double Zoom { get; init; }

    /// <summary>未取整的视口偏移：所有"坐标 ↔ 屏幕"换算都用它。</summary>
    public required Point Offset { get; init; }

    /// <summary>像素对齐后的绘制偏移：只用于画布背景 / 图像 / 边框的落点。</summary>
    public required Point DrawOffset { get; init; }

    /// <summary>画布坐标原点对应的角色坐标。</summary>
    public required Point CanvasOrigin { get; init; }

    /// <summary>显示坐标 0 对应的角色坐标（Image 基准 = 画布左上角；Role 基准 = 角色原点）。</summary>
    public required Point DisplayOffset { get; init; }

    /// <summary>画布尺寸（<see cref="ImageCanvas.CanvasSize" /> 未设时退回当前帧尺寸）。</summary>
    public required Size CanvasExtent { get; init; }

    /// <summary>鼠标位置；<c>null</c> = 指针不在画布上。</summary>
    public required Point? PointerScreen { get; init; }

    /// <summary>当前帧左上角在画布坐标系中的位置。</summary>
    public required Point FrameOffset { get; init; }

    /// <summary>当前帧像素。</summary>
    public required RgbaImage? Image { get; init; }

    /// <summary>帧内背景模式。</summary>
    public required CanvasBackground Background { get; init; }

    /// <summary>坐标基准（只影响读数，不影响屏幕定位）。</summary>
    public required RulerOriginMode OriginMode { get; init; }

    public required IBrush? OuterBrush { get; init; }
    public required IBrush? CheckerBrush { get; init; }
    public required IBrush? CheckerLightBrush { get; init; }
    public required IBrush? CheckerDarkBrush { get; init; }
    public required IBrush? FrameBorder { get; init; }
    public required IBrush? RulerBackground { get; init; }
    public required IBrush? RulerBorderBrush { get; init; }
    public required IBrush? RulerTickBrush { get; init; }
    public required IBrush? RulerTextBrush { get; init; }
    public required IBrush? CrosshairBrush { get; init; }
    public required IBrush? GuideBrush { get; init; }
    public required IBrush? GuideLabelBackground { get; init; }
    public required IBrush? GuideLabelTextBrush { get; init; }

    public required bool IsRulersVisible { get; init; }
    public required bool IsGuidesVisible { get; init; }
    public required bool ShowGuideLabels { get; init; }
    public required bool IsPixelAlignEnabled { get; init; }

    /// <summary>正在拖动的参考线方向（None = 没有拖动）。</summary>
    public required GuideAxis DragAxis { get; init; }

    /// <summary>正在拖动的参考线下标（-1 = 无）。</summary>
    public required int DragIndex { get; init; }

    /// <summary>悬停的参考线方向（None = 没有悬停）。</summary>
    public required GuideAxis HoverAxis { get; init; }

    /// <summary>悬停的参考线下标（-1 = 无）。</summary>
    public required int HoverIndex { get; init; }

    /// <summary>角色坐标 X → 屏幕 X（未取整，与重构前完全一致）。</summary>
    public double FrameXToScreen(double frameX) => (frameX - CanvasOrigin.X) * Zoom + Offset.X;

    /// <summary>角色坐标 Y → 屏幕 Y（未取整）。</summary>
    public double FrameYToScreen(double frameY) => (frameY - CanvasOrigin.Y) * Zoom + Offset.Y;

    /// <summary>屏幕坐标 → 角色坐标。</summary>
    public Point ToFrame(Point screen) => new(
        (screen.X - Offset.X) / Zoom + CanvasOrigin.X,
        (screen.Y - Offset.Y) / Zoom + CanvasOrigin.Y);

    /// <summary>角色坐标 → 当前基准下的显示坐标（仅用于读数）。</summary>
    public Point ToDisplay(Point role) => new(role.X - DisplayOffset.X, role.Y - DisplayOffset.Y);

    /// <summary>屏幕坐标 → 当前基准下的显示坐标。</summary>
    public Point ScreenToDisplay(Point screen) => ToDisplay(ToFrame(screen));

    /// <summary>该条参考线是否正在被悬停或拖动（决定标签要不要提亮 + 描边）。</summary>
    public bool IsGuideActive(GuideAxis axis, int index) =>
        (DragAxis == axis && DragIndex == index) || (HoverAxis == axis && HoverIndex == index);
}
