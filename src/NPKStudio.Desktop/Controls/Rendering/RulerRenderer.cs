using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace NPKStudio.Desktop.Controls.Rendering;

/// <summary>
///     标尺：刻度计算、刻度线与数字、顶部 / 左侧游标标记（含小三角与坐标标签）。
///     <para>
///         数字缓存跟着本类型走：坐标没变时复用同一份 <see cref="FormattedText" />，
///         鼠标移动的高频路径上不再分配。
///     </para>
/// </summary>
internal sealed class RulerRenderer {
    /// <summary>标尺厚度（屏幕像素）。与 <see cref="ImageCanvas.RulerSize" /> 同源。</summary>
    internal const double Thickness = ImageCanvas.RulerSize;

    /// <summary>相邻两个刻度数字的最小屏幕间距（保证 10px 数字不重叠）。</summary>
    private const double MinLabelSpacing = 48;

    /// <summary>刻度数字缓存上限（平移 / 缩放会不断产生新整数）。</summary>
    private const int TextCacheLimit = 256;

    /// <summary>自适应刻度候选档位（帧坐标像素）。</summary>
    private static readonly double[] StepCandidates = [1, 2, 5, 10, 20, 50, 100, 200, 500];

    /// <summary>标尺数字与参考线标签共用的字体（两处文字必须同宽同高）。</summary>
    internal static readonly Typeface TextTypeface = new(FontFamily.Default);

    /// <summary>刻度数字缓存：值 → <see cref="FormattedText" />。</summary>
    private readonly Dictionary<int, FormattedText> _textCache = new(64);

    /// <summary>
    ///     当前缩放下的刻度步进（帧坐标像素），取值来自 1/2/5/10/20/50/100/200/500。
    ///     <para>保证相邻数字的屏幕间距 ≥ 48px，缩放越大步进越小。</para>
    /// </summary>
    internal static double StepFor(double zoom) {
        foreach (var step in StepCandidates) {
            if (step * zoom >= MinLabelSpacing) return step;
        }

        return StepCandidates[^1];
    }

    /// <summary>
    ///     标尺上某个显示坐标对应的读数（当前基准）。绘制与测试共用这一处换算。
    /// </summary>
    internal static int LabelFor(double displayValue, bool horizontal, Point shift) =>
        (int)Math.Round(displayValue + (horizontal ? shift.X : shift.Y));

    /// <summary>主题切换 / 基准切换后旧 <see cref="FormattedText" /> 仍持着上一套画刷，必须整体失效。</summary>
    internal void InvalidateTextCache() => _textCache.Clear();

    internal FormattedText? GetText(int value, IBrush? brush) {
        if (brush is null) return null;
        if (_textCache.TryGetValue(value, out var cached)) return cached;

        if (_textCache.Count >= TextCacheLimit) _textCache.Clear();

        var text = new FormattedText(
            value.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            TextTypeface,
            10,
            brush);

        _textCache[value] = text;
        return text;
    }

    /// <summary>
    ///     标尺内侧的鼠标游标标签（顶部 X / 左侧 Y）。它们和参考线标签抢同一条边缘带，
    ///     布局时必须一起参与碰撞。
    /// </summary>
    internal void CollectCursorLabelRects(in CanvasRenderContext ctx, List<Rect> into) {
        if (!ctx.IsRulersVisible || ctx.PointerScreen is not { } pointer) return;
        if (ctx.RulerTextBrush is null) return;

        var width = ctx.Viewport.Width;
        var height = ctx.Viewport.Height;
        if (width < Thickness || height < Thickness) return;

        if (pointer.X >= Thickness && pointer.X <= width) {
            if (GetText((int)Math.Round(ctx.ScreenToDisplay(pointer).X), ctx.RulerTextBrush) is { } text) {
                var tagX = Math.Min(Math.Round(pointer.X) + 6, width - text.Width - 10);
                into.Add(new Rect(Math.Max(Thickness + 2, tagX), Thickness + 3, text.Width + 6, 15));
            }
        }

        if (pointer.Y >= Thickness && pointer.Y <= height) {
            if (GetText((int)Math.Round(ctx.ScreenToDisplay(pointer).Y), ctx.RulerTextBrush) is { } text) {
                var tagY = Math.Min(Math.Round(pointer.Y) + 6, height - 18);
                into.Add(new Rect(Thickness + 3, Math.Max(Thickness + 2, tagY), text.Width + 6, 15));
            }
        }
    }

    public void Draw(DrawingContext context, in CanvasRenderContext ctx) {
        var viewport = ctx.Viewport;
        if (!ctx.IsRulersVisible || viewport.Width < 4 || viewport.Height < 4) return;
        if (ctx.RulerBackground is not { } background) return;

        var width = viewport.Width;
        var height = viewport.Height;

        context.FillRectangle(background, new Rect(0, 0, width, Thickness));
        context.FillRectangle(background, new Rect(0, Thickness, Thickness, Math.Max(0, height - Thickness)));

        if (ctx.RulerBorderBrush is { } border) {
            context.FillRectangle(border, new Rect(0, Thickness - 1, width, 1));
            context.FillRectangle(border, new Rect(Thickness - 1, 0, 1, height));
        }

        var step = StepFor(ctx.Zoom);
        // 次刻度：主刻度屏幕间距够大时才细分，否则密到看不清
        var subdivisions = step * ctx.Zoom >= 40 ? 5 : 1;

        DrawAxis(context, in ctx, horizontal: true, width, height, step, subdivisions);
        DrawAxis(context, in ctx, horizontal: false, width, height, step, subdivisions);
        DrawMarker(context, in ctx, width, height);
    }

    private void DrawAxis(DrawingContext context, in CanvasRenderContext ctx, bool horizontal, double width,
        double height, double step, int subdivisions) {
        if (ctx.RulerTickBrush is not { } tickBrush) return;

        var zoom = ctx.Zoom;
        var offset = ctx.Offset;
        var strip = horizontal ? width : height;
        if (strip <= Thickness) return;

        // 刻度与数字都算在"显示坐标"里：屏幕映射始终 (显示值 * zoom + offset)，
        // 差别只在读数 —— Image 基准直接读显示值，Role 基准再加回画布原点。
        var displayAtStart = horizontal
            ? (Thickness - offset.X) / zoom
            : (Thickness - offset.Y) / zoom;

        var shift = ctx.OriginMode == RulerOriginMode.Role ? ctx.CanvasOrigin : default;

        var first = Math.Ceiling(displayAtStart / step) * step;
        for (var guard = 0; guard < 4096; guard++, first += step) {
            var screen = horizontal
                ? first * zoom + offset.X
                : first * zoom + offset.Y;

            if (screen > strip) break;
            if (screen >= Thickness) {
                DrawTick(context, horizontal, screen, tickBrush, major: true);

                var value = LabelFor(first, horizontal, shift);
                if (GetText(value, ctx.RulerTextBrush) is { } label) {
                    if (horizontal) {
                        if (screen + label.Width + 4 <= strip) {
                            context.DrawText(label, new Point(Math.Round(screen) + 3, 2));
                        }
                    } else if (screen + label.Width + 4 <= strip) {
                        // 左标尺数字旋转 90° 竖排，避免压在刻度线上
                        using (context.PushTransform(
                                   Matrix.CreateRotation(-Math.PI / 2)
                                   * Matrix.CreateTranslation(2, Math.Round(screen) + label.Width + 2))) {
                            context.DrawText(label, new Point(0, 0));
                        }
                    }
                }
            }

            if (subdivisions <= 1) continue;
            for (var k = 1; k < subdivisions; k++) {
                var minorValue = first + step * k / subdivisions;
                var minorScreen = horizontal
                    ? minorValue * zoom + offset.X
                    : minorValue * zoom + offset.Y;
                if (minorScreen > strip) break;
                if (minorScreen >= Thickness) {
                    DrawTick(context, horizontal, minorScreen, tickBrush, major: false);
                }
            }
        }
    }

    private static void DrawTick(DrawingContext context, bool horizontal, double screen, IBrush brush, bool major) {
        var length = major ? 7 : 4;
        var position = Math.Round(screen);
        if (horizontal) {
            context.FillRectangle(brush, new Rect(position, Thickness - length, 1, length));
        } else {
            context.FillRectangle(brush, new Rect(Thickness - length, position, length, 1));
        }
    }

    /// <summary>顶部 / 左侧的游标标记 + 当前坐标标签（画在标尺内侧边缘）。</summary>
    private void DrawMarker(DrawingContext context, in CanvasRenderContext ctx, double width, double height) {
        if (ctx.PointerScreen is not { } pointer) return;
        if (ctx.CrosshairBrush is not { } accent) return;

        var frame = ctx.ScreenToDisplay(pointer);

        // 顶部：竖线 + 向下小三角 + X 值
        if (pointer.X >= Thickness && pointer.X <= width) {
            var x = Math.Round(pointer.X);
            context.FillRectangle(accent, new Rect(x, 0, 1, Thickness - 1));
            DrawDownTriangle(context, accent, x, Thickness - 1);
            if (GetText((int)Math.Round(frame.X), ctx.RulerTextBrush) is { } text) {
                var tagX = Math.Min(x + 6, width - text.Width - 10);
                DrawLabelTag(context, in ctx, text, Math.Max(Thickness + 2, tagX), Thickness + 3);
            }
        }

        // 左侧：横线 + 向右小三角 + Y 值
        if (pointer.Y >= Thickness && pointer.Y <= height) {
            var y = Math.Round(pointer.Y);
            context.FillRectangle(accent, new Rect(0, y, Thickness - 1, 1));
            DrawRightTriangle(context, accent, y, Thickness - 1);
            if (GetText((int)Math.Round(frame.Y), ctx.RulerTextBrush) is { } text) {
                var tagY = Math.Min(y + 6, height - 18);
                DrawLabelTag(context, in ctx, text, Thickness + 3, Math.Max(Thickness + 2, tagY));
            }
        }
    }

    /// <summary>用 5 条 1px 横条拼一个小三角，避免每帧 new StreamGeometry。</summary>
    private static void DrawDownTriangle(DrawingContext context, IBrush brush, double centerX, double bottom) {
        for (var row = 0; row < 5; row++) {
            var half = 4 - row;
            context.FillRectangle(brush, new Rect(centerX - half, bottom - row, half * 2 + 1, 1));
        }
    }

    private static void DrawRightTriangle(DrawingContext context, IBrush brush, double centerY, double right) {
        for (var column = 0; column < 5; column++) {
            var half = 4 - column;
            context.FillRectangle(brush, new Rect(right - column, centerY - half, 1, half * 2 + 1));
        }
    }

    /// <summary>标尺游标 / 拖拽参考线共用的数值标签（底 + 下边框 + 文字）。</summary>
    internal void DrawLabelTag(DrawingContext context, in CanvasRenderContext ctx, FormattedText text, double x,
        double y) {
        if (ctx.RulerBackground is not { } background) {
            context.DrawText(text, new Point(x + 3, y + 1));
            return;
        }

        var rect = new Rect(x, y, text.Width + 6, 15);
        context.FillRectangle(background, rect);
        if (ctx.RulerBorderBrush is { } border) {
            context.FillRectangle(border, new Rect(x, y + 14, rect.Width, 1));
        }

        context.DrawText(text, new Point(x + 3, y + 1));
    }
}
