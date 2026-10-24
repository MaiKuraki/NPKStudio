using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NPKStudio.Core.Imaging;
using NPKStudio.Desktop.Services;

namespace NPKStudio.Desktop.Controls;

/// <summary>
///     帧缩略图：把 <see cref="RgbaImage" /> 等比缩放到控件内并居中，底衬棋盘格。
///     <para>与 <see cref="ImageCanvas" /> 不同，本控件不接受交互，仅用于列表卡片。</para>
/// </summary>
public sealed class FrameThumbnail : Control {
    public static readonly StyledProperty<RgbaImage?> ImageProperty =
        AvaloniaProperty.Register<FrameThumbnail, RgbaImage?>(nameof(Image));

    public static readonly StyledProperty<IBrush?> CheckerBrushProperty =
        AvaloniaProperty.Register<FrameThumbnail, IBrush?>(nameof(CheckerBrush));

    private WriteableBitmap? _bitmap;
    private RgbaImage? _owner;

    public FrameThumbnail() {
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    }

    /// <summary>要显示的帧像素。</summary>
    public RgbaImage? Image {
        get => GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    /// <summary>棋盘格画刷。</summary>
    public IBrush? CheckerBrush {
        get => GetValue(CheckerBrushProperty);
        set => SetValue(CheckerBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
        base.OnPropertyChanged(change);
        if (change.Property == ImageProperty) {
            Upload();
            InvalidateVisual();
        }
    }

    /// <summary>
    ///     重新挂载时强制重建位图（列表虚拟化会"摘下来再挂回去"，不重建就只剩棋盘格）。
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        _owner = null;
        Upload();
        InvalidateVisual();
    }

    /// <summary>控件被虚拟化列表回收时释放原生位图，避免长时间滚动堆积 Skia 资源。</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnDetachedFromVisualTree(e);
        _bitmap?.Dispose();
        _bitmap = null;
        _owner = null;
    }

    private void Upload() {
        var image = Image;
        if (image is null) {
            _bitmap = null;
            _owner = null;
            return;
        }

        if (ReferenceEquals(image, _owner) && _bitmap is not null) return;

        if (_bitmap is null || _bitmap.PixelSize.Width != image.Width || _bitmap.PixelSize.Height != image.Height) {
            // 旧位图持有 Skia 原生资源，不显式释放要等 finalizer，列表滚动时会堆积
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        }

        BgraBitmapWriter.Write(image, _bitmap);
        _owner = image;
    }

    public override void Render(DrawingContext context) {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        if (CheckerBrush is not null) {
            context.FillRectangle(CheckerBrush, bounds);
        }

        var image = Image;
        if (image is null || _bitmap is null || bounds.Width <= 0 || bounds.Height <= 0) return;

        var scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
        var w = image.Width * scale;
        var h = image.Height * scale;
        var x = (bounds.Width - w) / 2;
        var y = (bounds.Height - h) / 2;
        context.DrawImage(_bitmap, new Rect(x, y, w, h));
    }
}
