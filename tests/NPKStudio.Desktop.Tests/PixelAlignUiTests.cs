using Avalonia;
using Avalonia.Controls;

using Avalonia.Headless.XUnit;

using NPKStudio.Desktop.Controls;
using NPKStudio.Desktop.ViewModels;
using NPKStudio.Desktop.Views;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     像素对齐的界面层断言。
///     <para>
///         缩放档位是"视口变换 + 吸附规则"的合成结果，纯 ViewModel 看不到；
///         这里直接驱动真控件，量到的 <see cref="ImageCanvas.Zoom" /> 就是气泡 / 状态栏显示的值。
///     </para>
/// </summary>
public sealed class PixelAlignUiTests
{
    private static async Task<(MainWindow Window, ImageCanvas Canvas, MainWindowViewModel Vm)> OpenAsync() {
        var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 2, width: 17, height: 17);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);

        var canvas = window.FindControl<ImageCanvas>("FrameCanvas");
        Assert.NotNull(canvas);
        Assert.True(canvas!.Bounds.Width > ImageCanvas.RulerSize, "画布没有完成布局，缩放换算会失真");
        Assert.NotNull(canvas.Image);
        return (window, canvas, vm);
    }

    private static Size Extent(ImageCanvas canvas) =>
        canvas.CanvasSize is { Width: > 0, Height: > 0 }
            ? canvas.CanvasSize
            : new Size(canvas.Image!.Width, canvas.Image.Height);

    /// <summary>开启时 3.7 倍吸附到最近的整数倍 4.0（每个源像素铺成 4×4 屏幕像素）。</summary>
    [AvaloniaFact]
    public async Task PixelAlignOn_SnapsIntegerZoomToNearestWholeMultiple() {
        var (window, canvas, vm) = await OpenAsync();

        Assert.True(vm.IsPixelAlignEnabled, "像素对齐必须默认开启");
        Assert.True(canvas.IsPixelAlignEnabled);

        canvas.SetZoomLevel(3.7);

        Assert.Equal(4.0, canvas.Zoom, 9);
        Assert.True(new PixelZoom().IsLevel(canvas.Zoom));

        window.Close();
    }

    /// <summary>开启时 0.37 倍吸附到 1/3（缩小时只允许整数分之一，避免半个像素）。</summary>
    [AvaloniaFact]
    public async Task PixelAlignOn_SnapsBelowOneToUnitFraction() {
        var (window, canvas, _) = await OpenAsync();

        canvas.SetZoomLevel(0.37);

        Assert.Equal(1.0 / 3, canvas.Zoom, 9);
        Assert.True(new PixelZoom().IsLevel(canvas.Zoom));

        window.Close();
    }

    /// <summary>关闭时保留原来的连续缩放：3.7 / 0.37 原样生效。</summary>
    [AvaloniaFact]
    public async Task PixelAlignOff_KeepsContinuousZoom() {
        var (window, canvas, vm) = await OpenAsync();

        vm.IsPixelAlignEnabled = false;
        Assert.False(canvas.IsPixelAlignEnabled);

        canvas.SetZoomLevel(3.7);
        Assert.Equal(3.7, canvas.Zoom, 9);

        canvas.SetZoomLevel(0.37);
        Assert.Equal(0.37, canvas.Zoom, 9);

        window.Close();
    }

    /// <summary>适配窗口时向下取档：结果必须是合法档位，且不大于原始适配值（宁可留边）。</summary>
    [AvaloniaFact]
    public async Task FitToView_SnapsDownToLegalPixelLevel() {
        var (window, canvas, _) = await OpenAsync();

        var raw = new ViewportTransform().ComputeFit(Extent(canvas), canvas.Bounds.Size);
        Assert.True(raw > 0);

        canvas.FitToView();

        Assert.True(new PixelZoom().IsLevel(canvas.Zoom), $"FitToView 得到了非整档缩放 {canvas.Zoom}");
        Assert.True(canvas.Zoom <= raw + 1e-9, $"像素对齐不应向上取整：raw={raw}, actual={canvas.Zoom}");
        Assert.True(canvas.Zoom > 0);

        window.Close();
    }

    /// <summary>参考线落点吸附到整像素：小数坐标入库前必须取整。</summary>
    [AvaloniaFact]
    public async Task Guides_SnapToWholePixels() {
        var (window, canvas, _) = await OpenAsync();

        canvas.AddVerticalGuide(12.4);
        canvas.AddHorizontalGuide(-3.6);

        Assert.Equal(12, canvas.VerticalGuides[0]);
        Assert.Equal(-4, canvas.HorizontalGuides[0]);

        window.Close();
    }

    /// <summary>右键菜单「像素对齐」勾选状态与 VM / 控件双向同步（默认勾上）。</summary>
    [AvaloniaFact]
    public async Task PixelAlignMenuItem_TogglesViewModelAndCanvas() {
        var (window, canvas, vm) = await OpenAsync();

        var menu = canvas.ContextMenu;
        Assert.NotNull(menu);
        var toggles = menu!.Items.OfType<MenuItem>()
            .Where(item => item.ToggleType == MenuItemToggleType.CheckBox)
            .ToList();
        Assert.Equal(4, toggles.Count);

        var pixelAlign = toggles[3];
        Assert.True(pixelAlign.IsChecked);

        pixelAlign.IsChecked = false;
        Assert.False(vm.IsPixelAlignEnabled);
        Assert.False(canvas.IsPixelAlignEnabled);

        pixelAlign.IsChecked = true;
        Assert.True(vm.IsPixelAlignEnabled);
        Assert.True(canvas.IsPixelAlignEnabled);

        window.Close();
    }
}
