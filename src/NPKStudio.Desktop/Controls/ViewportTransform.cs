using Avalonia;

namespace NPKStudio.Desktop.Controls;

/// <summary>
///     画布视口变换（纯值逻辑，不依赖任何 Avalonia 控件，可单元测试）。
///     <para>坐标关系：<c>screen = image * Zoom + Offset</c>，原点为图像左上角。</para>
/// </summary>
public sealed class ViewportTransform {
    /// <summary>最小缩放。</summary>
    public const double MinZoom = 0.1;

    /// <summary>最大缩放。</summary>
    public const double MaxZoom = 16.0;

    /// <summary>当前缩放倍数（1.0 = 100%）。</summary>
    public double Zoom { get; private set; } = 1.0;

    /// <summary>图像原点在视口中的位置。</summary>
    public Point Offset { get; private set; }

    /// <summary>以 <paramref name="anchor" />（视口坐标）为锚点缩放，保证锚点下的图像像素不动。</summary>
    public void ZoomAt(Point anchor, double factor) {
        if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor)) return;
        var target = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(target - Zoom) < 1e-9) return;

        // imagePoint = (anchor - offset) / zoom；放大后要让同一 imagePoint 仍落在 anchor 上
        var imageX = (anchor.X - Offset.X) / Zoom;
        var imageY = (anchor.Y - Offset.Y) / Zoom;
        Zoom = target;
        Offset = new Point(anchor.X - imageX * target, anchor.Y - imageY * target);
    }

    /// <summary>平移到绝对缩放。</summary>
    public void SetZoom(double zoom, Point anchor) {
        var target = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (Math.Abs(target - Zoom) < 1e-9) return;
        ZoomAt(anchor, target / Zoom);
    }

    /// <summary>平移视口。</summary>
    public void Pan(Vector delta) => Offset += delta;

    /// <summary>
    ///     计算"完整可见"所需的原始缩放（未吸附、未夹到上下限）。
    ///     <para>像素对齐模式需要先拿到这个原始值再向下取档，所以计算与赋值拆开。</para>
    /// </summary>
    public double ComputeFit(Size image, Size viewport, double padding = 24) {
        if (image.Width <= 0 || image.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0) return 0;
        var availableW = Math.Max(1, viewport.Width - padding * 2);
        var availableH = Math.Max(1, viewport.Height - padding * 2);
        return Math.Min(availableW / image.Width, availableH / image.Height);
    }

    /// <summary>使图像完整可见并居中。</summary>
    public void Fit(Size image, Size viewport, double padding = 24) {
        var scale = ComputeFit(image, viewport, padding);
        if (scale <= 0) return;
        Zoom = Math.Clamp(scale, MinZoom, MaxZoom);
        Center(image, viewport);
    }

    /// <summary>在保持当前缩放的前提下令图像居中。</summary>
    public void Center(Size image, Size viewport) {
        Offset = new Point(
            (viewport.Width - image.Width * Zoom) / 2,
            (viewport.Height - image.Height * Zoom) / 2);
    }

    /// <summary>视口坐标 → 图像像素坐标。</summary>
    public Point ScreenToImage(Point screen) => new((screen.X - Offset.X) / Zoom, (screen.Y - Offset.Y) / Zoom);

    /// <summary>图像像素坐标 → 视口坐标。</summary>
    public Point ImageToScreen(Point image) => new(image.X * Zoom + Offset.X, image.Y * Zoom + Offset.Y);

    /// <summary>复位为 100% 并居中。</summary>
    public void Reset(Size image, Size viewport) {
        Zoom = 1.0;
        Center(image, viewport);
    }
}
