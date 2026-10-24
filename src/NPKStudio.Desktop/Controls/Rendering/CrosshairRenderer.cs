using Avalonia;
using Avalonia.Media;

namespace NPKStudio.Desktop.Controls.Rendering;

/// <summary>
///     鼠标十字辅助线（1px 虚线，实 4 / 空 4）。
///     <para>分段缓冲复用同一个 List：鼠标移动是高频路径，不能每次渲染都分配。</para>
/// </summary>
internal sealed class CrosshairRenderer {
    /// <summary>虚线节长（实 4px / 空 4px）。</summary>
    private const double DashLength = 4;

    /// <summary>分段缓冲（复用，不每帧 new）。</summary>
    private readonly List<Rect> _segments = new();

    /// <summary>
    ///     生成十字线的虚线分段（与绘制共用同一份几何，测试可直接断言）。
    ///     <para>实 4px、空 4px，起点固定在 <paramref name="from" />，所以鼠标左右移动时节拍不会抖。</para>
    /// </summary>
    internal static void BuildCrosshairSegments(
        bool vertical, double position, double from, double to, List<Rect> into) {
        into.Clear();
        var span = to - from;
        if (span <= 0) return;

        var step = DashLength * 2;
        for (var offset = 0d; offset < span; offset += step) {
            var end = Math.Min(offset + DashLength, span);
            into.Add(GuideRenderer.GuideStrokeRect(vertical, position, from + offset, from + end));
        }
    }

    public void Draw(DrawingContext context, in CanvasRenderContext ctx) {
        if (ctx.CrosshairBrush is not { } brush || ctx.PointerScreen is not { } pointer ||
            !ctx.Content.Contains(pointer)) {
            return;
        }

        var content = ctx.Content;
        var x = Math.Round(pointer.X);
        var y = Math.Round(pointer.Y);

        using (context.PushClip(content)) {
            FillDashed(context, brush, vertical: true, x, content.Top, content.Bottom, _segments);
            FillDashed(context, brush, vertical: false, y, content.Left, content.Right, _segments);
        }
    }

    private static void FillDashed(DrawingContext context, IBrush brush, bool vertical, double position, double from,
        double to, List<Rect> buffer) {
        BuildCrosshairSegments(vertical, position, from, to, buffer);
        for (var i = 0; i < buffer.Count; i++) context.FillRectangle(brush, buffer[i]);
    }
}
