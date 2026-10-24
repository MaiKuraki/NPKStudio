using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using NPKStudio.Desktop.Controls;
using NPKStudio.Desktop.Controls.Rendering;
using NPKStudio.Desktop.Localization;
using NPKStudio.Desktop.ViewModels;
using NPKStudio.Desktop.Views;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     坐标基准（<see cref="RulerOriginMode" />）与 1px 参考线的界面层断言。
///     <para>
///         <b>为什么必须有这一层</b>：基准只影响"读数"——标尺刻度、游标、参考线标签三者共用同一套换算，
///         而参考线的屏幕位置必须完全不受影响。这两件事只有在真控件上（视口 + 边带 + 布局）才量得准。
///     </para>
///     <para>
///         素材用 30×40、X=235、Y=232 的合成帧，和用户截图里那张 aura 帧几何一致。
///     </para>
/// </summary>
public sealed class RulerOriginUiTests
{
    private const int FrameX = 235;
    private const int FrameY = 232;
    private const int FrameWidth = 30;
    private const int FrameHeight = 40;

    private static TranslationCatalog Loc => TranslationCatalog.Instance;

    private static async Task<(MainWindow Window, ImageCanvas Canvas, MainWindowViewModel Vm)> OpenAsync(
        TestFolder folder) {
        folder.WriteImgAt("aura.img", FrameWidth, FrameHeight, FrameX, FrameY);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);
        Assert.NotNull(vm.SelectedFrame);

        var canvas = window.FindControl<ImageCanvas>("FrameCanvas");
        Assert.NotNull(canvas);
        Assert.True(canvas!.Bounds.Width > ImageCanvas.RulerSize, "画布没有完成布局，坐标换算会失真");
        Assert.Equal(new Point(FrameX, FrameY), canvas.CanvasOrigin);

        return (window, canvas, vm);
    }

    /// <summary>
    ///     默认是图像基准：画布左上角读数 0；切到角色基准后，同一屏幕位置读数变成 CanvasOrigin（235 / 232）。
    /// </summary>
    [AvaloniaFact]
    public async Task ImageOrigin_ReadsZeroAtCanvasCorner_RoleOriginReadsCanvasOrigin() {
        using var folder = TestFolder.Create();
        var (window, canvas, vm) = await OpenAsync(folder);

        canvas.SetZoomLevel(1.0);
        Assert.Equal(RulerOriginMode.Image, canvas.OriginMode);

        // 画布左上角 = 帧 X/Y 对应的屏幕点
        var left = canvas.FrameXToScreen(canvas.CanvasOrigin.X);
        var top = canvas.FrameYToScreen(canvas.CanvasOrigin.Y);
        var display = canvas.ScreenToDisplay(new Point(left, top));

        Assert.Equal(0, display.X, 6);
        Assert.Equal(0, display.Y, 6);
        Assert.Equal(0, canvas.RulerLabelFor(display.X, horizontal: true));
        Assert.Equal(0, canvas.RulerLabelFor(display.Y, horizontal: false));

        vm.OriginMode = RulerOriginMode.Role;
        Assert.Equal(RulerOriginMode.Role, canvas.OriginMode);

        // 屏幕位置一格都没动，只是读数换成角色坐标
        Assert.Equal(left, canvas.FrameXToScreen(canvas.CanvasOrigin.X), 6);
        Assert.Equal(top, canvas.FrameYToScreen(canvas.CanvasOrigin.Y), 6);
        Assert.Equal(FrameX, canvas.RulerLabelFor(display.X, horizontal: true));
        Assert.Equal(FrameY, canvas.RulerLabelFor(display.Y, horizontal: false));

        window.Close();
    }

    /// <summary>
    ///     参考线贴着画布左上边缘时：图像基准标签是 0 / 0，角色基准标签是 235 / 232；
    ///     两种基准下线在屏幕上**完全不动**（只有数字变）。
    /// </summary>
    [AvaloniaFact]
    public async Task GuideLabels_FollowOriginMode_WhileGuidesStayPut() {
        using var folder = TestFolder.Create();
        var (window, canvas, vm) = await OpenAsync(folder);

        canvas.SetZoomLevel(1.0);
        canvas.AddVerticalGuide(FrameX);
        canvas.AddHorizontalGuide(FrameY);
        canvas.UpdateGuideLabelLayout();

        var screenBeforeX = canvas.FrameXToScreen(canvas.VerticalGuides[0]);
        var screenBeforeY = canvas.FrameYToScreen(canvas.HorizontalGuides[0]);

        Assert.Equal(2, canvas.GuideLabelBoxes.Count);
        Assert.Contains(canvas.GuideLabelBoxes, box => box.Vertical && box.Value == 0);
        Assert.Contains(canvas.GuideLabelBoxes, box => !box.Vertical && box.Value == 0);

        vm.OriginMode = RulerOriginMode.Role;
        canvas.UpdateGuideLabelLayout();

        Assert.Contains(canvas.GuideLabelBoxes, box => box.Vertical && box.Value == FrameX);
        Assert.Contains(canvas.GuideLabelBoxes, box => !box.Vertical && box.Value == FrameY);

        // 关键：参考线仍按角色坐标存储、按角色坐标定位 —— 切换基准不能让线移动
        Assert.Equal(screenBeforeX, canvas.FrameXToScreen(canvas.VerticalGuides[0]), 9);
        Assert.Equal(screenBeforeY, canvas.FrameYToScreen(canvas.HorizontalGuides[0]), 9);
        Assert.Equal(FrameX, canvas.VerticalGuides[0], 9);
        Assert.Equal(FrameY, canvas.HorizontalGuides[0], 9);

        window.Close();
    }

    /// <summary>检查器的 X/Y/宽/高 是要写进 IMG 的格式字段：切基准必须一个数都不变。</summary>
    [AvaloniaFact]
    public async Task InspectorGeometry_KeepsRoleCoordinates_WhenOriginModeChanges() {
        using var folder = TestFolder.Create();
        var (window, _, vm) = await OpenAsync(folder);
        Dispatcher.UIThread.RunJobs();

        var expected = Loc.F("status.selectionGeometry", FrameX, FrameY, FrameWidth, FrameHeight);
        Assert.Equal(expected, vm.SelectionGeometryText);

        // 状态栏文字与检查器输入框是同源（frame.Frame.X/Y），这里两处都验一遍
        var xInput = window.FindControl<NumericUpDown>("InspectorXInput");
        var yInput = window.FindControl<NumericUpDown>("InspectorYInput");
        Assert.NotNull(xInput);
        Assert.NotNull(yInput);
        Assert.Equal(FrameX, xInput!.Value);
        Assert.Equal(FrameY, yInput!.Value);

        vm.OriginMode = RulerOriginMode.Role;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(expected, vm.SelectionGeometryText);
        Assert.Equal(FrameX, xInput.Value);
        Assert.Equal(FrameY, yInput.Value);

        vm.OriginMode = RulerOriginMode.Image;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(expected, vm.SelectionGeometryText);
        Assert.Equal(FrameX, xInput.Value);
        Assert.Equal(FrameY, yInput.Value);

        window.Close();
    }

    /// <summary>状态栏的光标坐标跟着基准走，并且有一个极短的基准标识。</summary>
    [AvaloniaFact]
    public async Task StatusBarPointer_AndOriginBadge_FollowOriginMode() {
        using var folder = TestFolder.Create();
        var (window, canvas, vm) = await OpenAsync(folder);

        canvas.SetZoomLevel(1.0);
        var left = canvas.FrameXToScreen(FrameX);
        var top = canvas.FrameYToScreen(FrameY);

        canvas.SimulatePointer(new Point(left, top));
        Assert.Equal(Loc.F("status.pointer", 0, 0), vm.MousePositionText);
        Assert.Equal(Loc.F("status.origin", Loc["status.origin.image"]), vm.OriginModeText);
        Assert.Equal(Loc.F("canvas.origin.tip", Loc["canvas.origin.image"]), vm.OriginModeTip);

        vm.OriginMode = RulerOriginMode.Role;
        Assert.Equal(Loc.F("status.pointer", FrameX, FrameY), vm.MousePositionText);
        Assert.Equal(Loc.F("status.origin", Loc["status.origin.role"]), vm.OriginModeText);
        Assert.Equal(Loc.F("canvas.origin.tip", Loc["canvas.origin.role"]), vm.OriginModeTip);

        // 标尺左上角的交叉方块挂着同一个提示，用户不用翻菜单就能知道现在读的是哪套坐标
        var corner = window.FindControl<Border>("RulerOriginCorner");
        Assert.NotNull(corner);
        Assert.Equal(vm.OriginModeTip, ToolTip.GetTip(corner));

        canvas.SimulatePointer(null);
        Assert.Equal(string.Empty, vm.MousePositionText);

        window.Close();
    }

    /// <summary>右键菜单的「坐标基准」分组：默认勾图像，勾角色后 VM 与画布同步，且两项互斥。</summary>
    [AvaloniaFact]
    public async Task ContextMenuOriginGroup_TogglesBothViewModelAndCanvas() {
        using var folder = TestFolder.Create();
        var (window, canvas, vm) = await OpenAsync(folder);

        var group = canvas.ContextMenu!.Items.OfType<MenuItem>()
            .SingleOrDefault(item => string.Equals(item.Header as string, Loc["canvas.origin"], StringComparison.Ordinal));
        Assert.NotNull(group);

        var imageItem = group!.Items.OfType<MenuItem>()
            .Single(item => string.Equals(item.Header as string, Loc["canvas.origin.image"], StringComparison.Ordinal));
        var roleItem = group.Items.OfType<MenuItem>()
            .Single(item => string.Equals(item.Header as string, Loc["canvas.origin.role"], StringComparison.Ordinal));

        Assert.True(imageItem.IsChecked);
        Assert.False(roleItem.IsChecked);

        roleItem.IsChecked = true;
        Assert.Equal(RulerOriginMode.Role, vm.OriginMode);
        Assert.Equal(RulerOriginMode.Role, canvas.OriginMode);
        Assert.False(imageItem.IsChecked);

        // 再点一次已勾选项：CheckBox 会先把自己取消，Click 处理必须把它按回去
        roleItem.IsChecked = false;
        roleItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(RulerOriginMode.Role, vm.OriginMode);
        Assert.True(roleItem.IsChecked);

        window.Close();
    }

    /// <summary>
    ///     从标尺拖参考线时，无论当前基准是哪个，落库的都是<b>角色坐标</b>（基准只影响读数）。
    ///     <para>同一屏幕位置拖两次：图像基准与角色基准存下来的值必须完全相同。</para>
    /// </summary>
    [AvaloniaFact]
    public async Task DraggingGuideFromRuler_StoresRoleCoordinate_UnderBothOriginModes() {
        using var folder = TestFolder.Create();
        var (window, canvas, vm) = await OpenAsync(folder);

        canvas.SetZoomLevel(1.0);
        var ruler = canvas.TranslatePoint(new Point(200, ImageCanvas.RulerSize / 2), window);
        var content = canvas.TranslatePoint(new Point(200, 300), window);
        Assert.NotNull(ruler);
        Assert.NotNull(content);

        var expectedRole = canvas.ScreenToFrame(new Point(200, 300)).Y;

        window.MouseDown(ruler!.Value, MouseButton.Left);
        window.MouseMove(content!.Value, RawInputModifiers.LeftMouseButton);
        window.MouseUp(content.Value, MouseButton.Left);

        var imageModeValue = Assert.Single(canvas.HorizontalGuides);
        Assert.Equal(Math.Round(expectedRole), imageModeValue, 6);

        canvas.ClearGuides();
        vm.OriginMode = RulerOriginMode.Role;

        // 两次 MouseDown 离得太近会被无头平台算成双击（ClickCount=2 → 不走"从标尺新建参考线"分支）
        await Task.Delay(700);

        window.MouseDown(ruler.Value, MouseButton.Left);
        Assert.Single(canvas.HorizontalGuides); // 按下即入表，拖拽过程中就能看到
        window.MouseMove(content.Value, RawInputModifiers.LeftMouseButton);
        window.MouseUp(content.Value, MouseButton.Left);

        var roleModeValue = Assert.Single(canvas.HorizontalGuides);
        Assert.Equal(imageModeValue, roleModeValue, 6);

        // 标签读数在两种基准下不同，但线本身是同一条
        canvas.UpdateGuideLabelLayout();
        var label = Assert.Single(canvas.GuideLabelBoxes);
        Assert.Equal((int)Math.Round(expectedRole), label.Value);

        window.Close();
    }

    /// <summary>
    ///     参考线 / 十字线的描边几何恒定为 1 屏幕像素（去掉托底黑影后不能变成 1.5px 或 2px）。
    ///     <para>端到端的像素证明见 <c>artifacts/shots/ruler-origin/</c> 的采样报告。</para>
    /// </summary>
    [Fact]
    public void GuideStroke_IsExactlyOnePixelWide() {
        var vertical = GuideRenderer.GuideStrokeRect(true, 10.0, 20, 120);
        Assert.Equal(1, vertical.Width);
        Assert.Equal(100, vertical.Height);

        var horizontal = GuideRenderer.GuideStrokeRect(false, 30.0, 5, 55);
        Assert.Equal(1, horizontal.Height);
        Assert.Equal(50, horizontal.Width);
    }
}
