using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace NPKStudio.Desktop.Controls.Rendering;

/// <summary>
///     一条参考线坐标标签的布局结果（绘制与测试读同一份数据）。
/// </summary>
internal readonly struct GuideLabelBox {
    public GuideLabelBox(bool vertical, int value, Rect rect, bool active) {
        Vertical = vertical;
        Value = value;
        Rect = rect;
        Active = active;
    }

    /// <summary><c>true</c> = 垂直参考线的标签（贴在顶部边缘）；<c>false</c> = 水平参考线（贴在左侧边缘）。</summary>
    public bool Vertical { get; }

    /// <summary>标签上的整数坐标（按 <see cref="ImageCanvas.OriginMode" /> 换算）。</summary>
    public int Value { get; }

    /// <summary>标签矩形（控件内屏幕坐标）。</summary>
    public Rect Rect { get; }

    /// <summary>是否正在被悬停/拖动（绘制时提亮 + 1px 描边）。</summary>
    public bool Active { get; }
}

/// <summary>
///     参考线的绘制：1px 线 + 坐标标签（布局 / 碰撞顺延 / 文字缓存）。
///     <para>
///         标签布局同时参与绘制与测试断言，所以这里持有唯一的布局结果；
///         游标标签矩形向 <see cref="RulerRenderer" /> 借，保证两条边缘带的碰撞一致。
///     </para>
/// </summary>
internal sealed class GuideRenderer {
    /// <summary>参考线坐标标签：文字左右内边距。</summary>
    private const double LabelPaddingX = 4;

    /// <summary>参考线坐标标签：文字上下内边距。</summary>
    private const double LabelPaddingY = 1;

    /// <summary>参考线坐标标签：距画布内边缘的留白（别贴着标尺分隔线）。</summary>
    private const double LabelInset = 2;

    /// <summary>参考线坐标标签：同一条边上两个标签之间的间隙。</summary>
    private const double LabelGap = 2;

    /// <summary>参考线坐标标签：常态整体不透明度（悬停/拖动时回到 1）。</summary>
    private const double LabelOpacity = 0.75;

    /// <summary>参考线坐标标签：圆角半径。</summary>
    private const double LabelCornerRadius = 3;

    /// <summary>参考线坐标标签：缓存上限（与标尺数字同一套策略）。</summary>
    private const int LabelCacheLimit = 256;

    /// <summary>参考线标签数字用等宽数字（OpenType <c>tnum</c>），宽度对齐、不抖。</summary>
    private static readonly FontFeatureCollection TabularFigures = [FontFeature.Parse("tnum")];

    /// <summary>排序委托只建一次，避免每次重绘产生新的 <see cref="Comparison{T}" />。</summary>
    private static readonly Comparison<GuideLabelSeed> SeedByScreen = static (a, b) => a.Screen.CompareTo(b.Screen);

    private readonly RulerRenderer _ruler;

    /// <summary>参考线坐标标签的文字缓存：整数值 → <see cref="FormattedText" />。</summary>
    private readonly Dictionary<int, FormattedText> _textCache = new(32);

    /// <summary>布局结果（绘制与测试读同一份数据）；每帧复用一个 List，避免堆分配。</summary>
    private readonly List<GuideLabelBox> _labelBoxes = new();

    /// <summary>标尺上鼠标游标标签的矩形（顶部 / 左侧最多各一个），参考线标签要整体避开它们。</summary>
    private readonly List<Rect> _cursorLabelRects = new();

    /// <summary>排布前的临时缓冲（值 / 屏幕位置 / 是否高亮），同样复用。</summary>
    private readonly List<GuideLabelSeed> _labelSeeds = new();

    private Pen? _labelPen;
    private IBrush? _labelPenOwner;

    public GuideRenderer(RulerRenderer ruler) => _ruler = ruler;

    /// <summary>最近一次布局出来的标签矩形（标签关闭时为 0 条）。</summary>
    public IReadOnlyList<GuideLabelBox> LabelBoxes => _labelBoxes;

    /// <summary>最近一次布局时标尺游标标签占用的矩形。</summary>
    public IReadOnlyList<Rect> CursorLabelBoxes => _cursorLabelRects;

    /// <summary>标签文字缓存未命中次数（坐标没变时这个数不该增长）。</summary>
    public int CacheMisses { get; private set; }

    /// <summary>主题 / 基准变化后旧文本仍持着上一套画刷，必须整体失效。</summary>
    public void InvalidateTextCache() => _textCache.Clear();

    /// <summary>标签数字格式（纯整数、无单位，负数带 <c>-</c>）。</summary>
    internal static string FormatGuideValue(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    ///     参考线 / 十字线唯一的描边几何：恒定 1 屏幕像素（垂直线的宽 = 1，水平线的高 = 1）。
    /// </summary>
    internal static Rect GuideStrokeRect(bool vertical, double position, double from, double to) =>
        vertical
            ? new Rect(position, from, 1, Math.Max(0, to - from))
            : new Rect(from, position, Math.Max(0, to - from), 1);

    /// <summary>
    ///     重算标签布局（测试与绘制共用）。
    ///     <para>
    ///         垂直参考线 → 顶部边缘、水平居中于线；水平参考线 → 左侧边缘、垂直居中于线。
    ///         同一条边上多个标签按屏幕坐标排序，与上一个重叠就顺延（顶部往下、左侧往右），保证都能读。
    ///     </para>
    /// </summary>
    public void UpdateLayout(in CanvasRenderContext ctx, GuideStore store) {
        _labelBoxes.Clear();
        _cursorLabelRects.Clear();
        if (!ctx.ShowGuideLabels || !ctx.IsGuidesVisible) return;

        var content = ctx.Content;
        if (content.Width <= 0 || content.Height <= 0) return;

        _ruler.CollectCursorLabelRects(in ctx, _cursorLabelRects);

        // 垂直参考线：标签贴顶部边缘（数值按当前基准换算，屏幕位置始终用角色坐标）
        _labelSeeds.Clear();
        for (var i = 0; i < store.Vertical.Count; i++) {
            var x = Math.Round(ctx.FrameXToScreen(store.Vertical[i]));
            if (x < content.Left || x > content.Right) continue;
            _labelSeeds.Add(new GuideLabelSeed(
                (int)Math.Round(store.Vertical[i] - ctx.DisplayOffset.X, MidpointRounding.AwayFromZero),
                x,
                ctx.IsGuideActive(GuideAxis.Vertical, i)));
        }

        if (_labelSeeds.Count > 0) {
            _labelSeeds.Sort(SeedByScreen);
            var baseTop = content.Top + LabelInset;
            for (var i = 0; i < _labelSeeds.Count; i++) {
                var seed = _labelSeeds[i];
                if (GetText(seed.Value, ctx.GuideLabelTextBrush) is not { } text) continue;
                var width = text.Width + LabelPaddingX * 2;
                var height = text.Height + LabelPaddingY * 2;
                if (width + LabelInset * 2 > content.Width) continue;

                var left = Math.Round(Math.Clamp(seed.Screen - width / 2,
                    content.Left + LabelInset, content.Right - width - LabelInset));

                // 与已有标签 / 标尺游标标签相交 → 往下顺延；间距至少 LabelGap（2px）。
                var top = PushDown(Math.Round(baseTop), left, width, height);
                if (top + height > content.Bottom) continue; // 竖直方向放不下：宁可不画，也不糊到别处
                var rect = new Rect(left, top, width, height);
                _labelBoxes.Add(new GuideLabelBox(true, seed.Value, rect, seed.Active));
            }
        }

        // 水平参考线：标签贴左侧边缘
        _labelSeeds.Clear();
        for (var i = 0; i < store.Horizontal.Count; i++) {
            var y = Math.Round(ctx.FrameYToScreen(store.Horizontal[i]));
            if (y < content.Top || y > content.Bottom) continue;
            _labelSeeds.Add(new GuideLabelSeed(
                (int)Math.Round(store.Horizontal[i] - ctx.DisplayOffset.Y, MidpointRounding.AwayFromZero),
                y,
                ctx.IsGuideActive(GuideAxis.Horizontal, i)));
        }

        if (_labelSeeds.Count > 0) {
            _labelSeeds.Sort(SeedByScreen);
            var baseLeft = content.Left + LabelInset;
            for (var i = 0; i < _labelSeeds.Count; i++) {
                var seed = _labelSeeds[i];
                if (GetText(seed.Value, ctx.GuideLabelTextBrush) is not { } text) continue;
                var width = text.Width + LabelPaddingX * 2;
                var height = text.Height + LabelPaddingY * 2;
                if (height + LabelInset * 2 > content.Height) continue;

                var top = Math.Round(Math.Clamp(seed.Screen - height / 2,
                    content.Top + LabelInset, content.Bottom - height - LabelInset));

                // 与已有标签（含顶部边缘带）/ 标尺游标标签相交 → 往右顺延；间距至少 2px。
                var left = PushRight(Math.Round(baseLeft), top, width, height);
                if (left + width > content.Right) continue; // 横向放不下：宁可不画，也不糊到别处
                var rect = new Rect(left, top, width, height);
                _labelBoxes.Add(new GuideLabelBox(false, seed.Value, rect, seed.Active));
            }
        }
    }

    /// <summary>画参考线本体（被标签矩形盖住的区段断开）+ 拖拽中的坐标标签。</summary>
    public void DrawLines(DrawingContext context, in CanvasRenderContext ctx, GuideStore store) {
        var content = ctx.Content;
        if (content.Width <= 0 || content.Height <= 0) return;

        using (context.PushClip(content)) {
            // 参考线 = 纯 1px 主题色（不再叠半透明黑影）。1px 的可见性交给主题色本身的对比度。
            var guideBrush = ctx.IsGuidesVisible ? ctx.GuideBrush : null;
            if (guideBrush is not null) {
                for (var i = 0; i < store.Horizontal.Count; i++) {
                    var y = Math.Round(ctx.FrameYToScreen(store.Horizontal[i]));
                    if (y < content.Top || y > content.Bottom) continue;
                    DrawHorizontalGuide(context, guideBrush, content, y);
                }

                for (var i = 0; i < store.Vertical.Count; i++) {
                    var x = Math.Round(ctx.FrameXToScreen(store.Vertical[i]));
                    if (x < content.Left || x > content.Right) continue;
                    DrawVerticalGuide(context, guideBrush, content, x);
                }

                // 拖拽中的参考线显示当前坐标值（文字缓存按整数值复用）
                if (ctx.DragAxis != GuideAxis.None && ctx.PointerScreen is { } dragging) {
                    var display = ctx.ScreenToDisplay(dragging);
                    var value = ctx.DragAxis == GuideAxis.Horizontal
                        ? (int)Math.Round(display.Y)
                        : (int)Math.Round(display.X);
                    if (_ruler.GetText(value, ctx.RulerTextBrush) is { } text) {
                        _ruler.DrawLabelTag(context, in ctx, text,
                            Math.Min(dragging.X + 10, content.Right - text.Width - 8),
                            Math.Min(dragging.Y + 10, content.Bottom - 18));
                    }
                }
            }
        }
    }

    /// <summary>标签最后画：十字线不能从数字中间穿过去。</summary>
    public void DrawLabels(DrawingContext context, in CanvasRenderContext ctx) {
        if (_labelBoxes.Count == 0) return;
        if (ctx.GuideLabelBackground is not { } background || ctx.GuideLabelTextBrush is null) return;

        using (context.PushClip(ctx.Content)) {
            // 常态整组半透明；被悬停/拖动的那一条单独画在最上面（不透明 + 1px 描边）
            using (context.PushOpacity(LabelOpacity)) {
                for (var i = 0; i < _labelBoxes.Count; i++) {
                    var box = _labelBoxes[i];
                    if (box.Active) continue;
                    DrawLabel(context, in ctx, box, background, null);
                }
            }

            for (var i = 0; i < _labelBoxes.Count; i++) {
                var box = _labelBoxes[i];
                if (!box.Active) continue;
                DrawLabel(context, in ctx, box, background, GetLabelPen(ctx.GuideBrush));
            }
        }
    }

    private void DrawLabel(DrawingContext context, in CanvasRenderContext ctx, in GuideLabelBox box, IBrush background,
        Pen? border) {
        context.DrawRectangle(background, border, new RoundedRect(box.Rect, LabelCornerRadius));
        if (GetText(box.Value, ctx.GuideLabelTextBrush) is { } text) {
            context.DrawText(text, new Point(box.Rect.X + LabelPaddingX, box.Rect.Y + LabelPaddingY));
        }
    }

    /// <summary>高亮标签的 1px 描边（复用参考线色）；Pen 缓存避免每帧 new。</summary>
    private Pen? GetLabelPen(IBrush? brush) {
        if (brush is null) return null;
        if (!ReferenceEquals(brush, _labelPenOwner) || _labelPen is null) {
            _labelPen = new Pen(brush, 1);
            _labelPenOwner = brush;
        }

        return _labelPen;
    }

    /// <summary>标签文字：按整数值缓存（与标尺刻度数字同一套策略）。</summary>
    private FormattedText? GetText(int value, IBrush? brush) {
        if (brush is null) return null;
        if (_textCache.TryGetValue(value, out var cached)) return cached;

        // 缓存有界：平移/缩放会不断产生新整数，不设上限会一直涨
        if (_textCache.Count >= LabelCacheLimit) _textCache.Clear();

        CacheMisses++;
        var text = new FormattedText(
            FormatGuideValue(value),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            RulerRenderer.TextTypeface,
            10,
            brush);
        text.SetFontFeatures(TabularFigures);
        _textCache[value] = text;
        return text;
    }

    /// <summary>画一条竖直参考线；被标签矩形盖住的区段断开。</summary>
    private void DrawVerticalGuide(DrawingContext context, IBrush brush, Rect content, double x) {
        var cursor = content.Top;
        for (var i = 0; i < _labelBoxes.Count; i++) {
            var box = _labelBoxes[i];
            if (!box.Vertical || x < box.Rect.Left - 1 || x > box.Rect.Right) continue;
            var top = Math.Max(content.Top, box.Rect.Top);
            var bottom = Math.Min(content.Bottom, box.Rect.Bottom + 1);
            if (top > cursor) FillVertical(context, brush, x, cursor, top);
            if (bottom > cursor) cursor = bottom;
        }

        if (cursor < content.Bottom) FillVertical(context, brush, x, cursor, content.Bottom);
    }

    /// <summary>画一条水平参考线；被标签矩形盖住的区段断开（理由同上）。</summary>
    private void DrawHorizontalGuide(DrawingContext context, IBrush brush, Rect content, double y) {
        var cursor = content.Left;
        for (var i = 0; i < _labelBoxes.Count; i++) {
            var box = _labelBoxes[i];
            if (box.Vertical || y < box.Rect.Top - 1 || y > box.Rect.Bottom) continue;
            var left = Math.Max(content.Left, box.Rect.Left);
            var right = Math.Min(content.Right, box.Rect.Right + 1);
            if (left > cursor) FillHorizontal(context, brush, y, cursor, left);
            if (right > cursor) cursor = right;
        }

        if (cursor < content.Right) FillHorizontal(context, brush, y, cursor, content.Right);
    }

    private static void FillVertical(DrawingContext context, IBrush brush, double x, double top, double bottom) {
        var height = bottom - top;
        if (height <= 0) return;
        context.FillRectangle(brush, GuideStrokeRect(true, x, top, bottom));
    }

    private static void FillHorizontal(DrawingContext context, IBrush brush, double y, double left, double right) {
        var width = right - left;
        if (width <= 0) return;
        context.FillRectangle(brush, GuideStrokeRect(false, y, left, right));
    }

    /// <summary>把标签沿 Y 向下推到不与任何已有标签 / 游标标签重叠（间距 ≥ <see cref="LabelGap" />）。</summary>
    private double PushDown(double top, double left, double width, double height) {
        for (var guard = 0; guard < 64; guard++) {
            var moved = false;

            foreach (var box in _labelBoxes) {
                if (!IntersectsHorizontally(left, width, box.Rect)) continue;
                if (!IntersectsVertically(top, height, box.Rect)) continue;
                top = box.Rect.Bottom + LabelGap;
                moved = true;
            }

            foreach (var cursor in _cursorLabelRects) {
                if (!IntersectsHorizontally(left, width, cursor)) continue;
                if (!IntersectsVertically(top, height, cursor)) continue;
                top = cursor.Bottom + LabelGap;
                moved = true;
            }

            if (!moved) break;
        }

        return top;
    }

    /// <summary>把标签沿 X 向右推到不与任何已有标签 / 游标标签重叠（间距 ≥ <see cref="LabelGap" />）。</summary>
    private double PushRight(double left, double top, double width, double height) {
        for (var guard = 0; guard < 64; guard++) {
            var moved = false;

            foreach (var box in _labelBoxes) {
                if (!IntersectsVertically(top, height, box.Rect)) continue;
                if (!IntersectsHorizontally(left, width, box.Rect)) continue;
                left = box.Rect.Right + LabelGap;
                moved = true;
            }

            foreach (var cursor in _cursorLabelRects) {
                if (!IntersectsVertically(top, height, cursor)) continue;
                if (!IntersectsHorizontally(left, width, cursor)) continue;
                left = cursor.Right + LabelGap;
                moved = true;
            }

            if (!moved) break;
        }

        return left;
    }

    private static bool IntersectsHorizontally(double left, double width, Rect other) =>
        left < other.Right && left + width > other.Left;

    private static bool IntersectsVertically(double top, double height, Rect other) =>
        top < other.Bottom && top + height > other.Top;

    /// <summary>排布前的临时项：整数值 / 屏幕位置 / 是否高亮。</summary>
    private readonly struct GuideLabelSeed {
        public GuideLabelSeed(int value, double screen, bool active) {
            Value = value;
            Screen = screen;
            Active = active;
        }

        public int Value { get; }

        public double Screen { get; }

        public bool Active { get; }
    }
}
