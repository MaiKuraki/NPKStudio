using Avalonia.Media;

namespace NPKStudio.Desktop.Controls.Rendering;

/// <summary>
///     画布背景：帧外底色 + 帧内背景（棋盘格 / 纯色 / 绿幕）。
///     <para>背景始终在控件这一层绘制（图像本身可能带透明），因此"换背景"只是换画刷。</para>
/// </summary>
internal sealed class CanvasBackgroundRenderer {
    private static readonly IBrush DarkSolid = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly IBrush LightSolid = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
    private static readonly IBrush GreenSolid = new SolidColorBrush(Color.FromRgb(0x00, 0xB1, 0x40));
    private static readonly IBrush BlackSolid = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00));

    /// <summary>帧外区域用独立底色，和帧内区分开，避免"图到哪里为止"看不清。</summary>
    public void DrawOuter(DrawingContext context, in CanvasRenderContext ctx) {
        if (ctx.OuterBrush is not null) {
            context.FillRectangle(ctx.OuterBrush, ctx.Viewport);
        }
    }

    /// <summary>帧内背景（棋盘格 / 纯色），表示透明区域。</summary>
    public void DrawContent(DrawingContext context, in CanvasRenderContext ctx) {
        var fill = ctx.Background switch {
            CanvasBackground.Dark => DarkSolid,
            CanvasBackground.Light => LightSolid,
            CanvasBackground.Green => GreenSolid,
            CanvasBackground.Black => BlackSolid,
            CanvasBackground.CheckerLight => ctx.CheckerLightBrush,
            CanvasBackground.CheckerDark => ctx.CheckerDarkBrush,
            _ => ctx.CheckerBrush
        };

        if (fill is not null && ctx.CanvasRect is { Width: > 0, Height: > 0 }) {
            using (context.PushClip(ctx.CanvasRect)) {
                context.FillRectangle(fill, ctx.CanvasRect);
            }
        }
    }
}
