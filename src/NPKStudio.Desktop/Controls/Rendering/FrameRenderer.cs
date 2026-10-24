using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NPKStudio.Core.Imaging;
using NPKStudio.Desktop.Services;

namespace NPKStudio.Desktop.Controls.Rendering;

/// <summary>
///     帧图像与帧边框。
///     <para>
///         同时持有位图缓存（<see cref="WriteableBitmap" />）与 1px 边框 Pen：
///         鼠标移动会高频触发重绘，这两者都不能每帧重建。
///     </para>
/// </summary>
internal sealed class FrameRenderer {
    private WriteableBitmap? _bitmap;
    private RgbaImage? _bitmapOwner;
    private Pen? _borderPen;
    private IBrush? _borderPenOwner;

    /// <summary>是否已有可绘制的位图（与 <see cref="ImageCanvas.Image" /> 解耦：重建期间为空）。</summary>
    public bool HasBitmap => _bitmap is not null;

    /// <summary>图像变化时重建 / 写入位图；同一个 <see cref="RgbaImage" /> 实例直接复用。</summary>
    public void Upload(RgbaImage? image) {
        if (image is null) {
            // 与重构前一致：只丢引用；原生位图由 GC / 下一次重建处理（拆卸时才显式 Dispose）
            _bitmap = null;
            _bitmapOwner = null;
            return;
        }

        if (ReferenceEquals(image, _bitmapOwner) && _bitmap is not null) return;

        if (_bitmap is null || _bitmap.PixelSize.Width != image.Width || _bitmap.PixelSize.Height != image.Height) {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        }

        BgraBitmapWriter.Write(image, _bitmap);
        _bitmapOwner = image;
    }

    /// <summary>
    ///     重新挂载到可视树时强制重建位图。
    ///     <para>离开可视树会释放位图，但 <c>Image</c> 没变 → 不会触发属性变更 → 必须在这里重建。</para>
    /// </summary>
    public void Reattach(RgbaImage? image) {
        _bitmapOwner = null;
        Upload(image);
    }

    /// <summary>离开可视树时释放原生位图。</summary>
    public void Release() {
        _bitmap?.Dispose();
        _bitmap = null;
        _bitmapOwner = null;
    }

    public void Draw(DrawingContext context, in CanvasRenderContext ctx) {
        if (ctx.Image is not { } image || _bitmap is null) return;

        // 最近邻只作用于画布主图：帧列表缩略图是另一个控件，保持自己的插值策略。
        using (context.PushClip(ctx.CanvasRect)) {
            using (context.PushRenderOptions(
                       new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None })) {
                using (context.PushTransform(
                           Matrix.CreateScale(ctx.Zoom, ctx.Zoom)
                           * Matrix.CreateTranslation(ctx.DrawOffset.X, ctx.DrawOffset.Y))) {
                    context.DrawImage(_bitmap, new Rect(ctx.FrameOffset.X, ctx.FrameOffset.Y, image.Width, image.Height));
                }
            }
        }

        // 1px 帧边框（屏幕空间，恒定粗细）
        if (ctx.FrameBorder is { } frameBorder) {
            if (!ReferenceEquals(frameBorder, _borderPenOwner) || _borderPen is null) {
                _borderPen = new Pen(frameBorder, 1);
                _borderPenOwner = frameBorder;
            }

            context.DrawRectangle(null, _borderPen, ctx.CanvasRect);
        }
    }
}
