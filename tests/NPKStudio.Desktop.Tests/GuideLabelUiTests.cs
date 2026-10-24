using Avalonia;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using NPKStudio.Desktop.Controls;
using NPKStudio.Desktop.Controls.Rendering;
using NPKStudio.Desktop.ViewModels;
using NPKStudio.Desktop.Views;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     参考线坐标标签的布局断言。
///     <para>
///         <b>为什么必须有这一层</b>：标签位置是"视口变换 + 标尺边带 + 碰撞顺延"三件事的合成结果，
///         纯 ViewModel 测试看不到；<see cref="ImageCanvas.UpdateGuideLabelLayout" /> 与 <c>Render</c>
///         读的是同一份 <see cref="ImageCanvas.GuideLabelBoxes" />，所以这里量到的就是画出来的。
///     </para>
/// </summary>
public sealed class GuideLabelUiTests
{
    private static ImageCanvas Canvas(MainWindow window) {
        var canvas = window.FindControl<ImageCanvas>("FrameCanvas");
        Assert.NotNull(canvas);
        return canvas!;
    }

    /// <summary>开一个 8×8 的小图：视口完成布局，坐标换算才可用（不需要真实像素内容）。</summary>
    private static async Task<(MainWindow Window, ImageCanvas Canvas, MainWindowViewModel Vm)> OpenAsync() {
        var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 2, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);

        var canvas = Canvas(window);
        Assert.True(canvas.Bounds.Width > ImageCanvas.RulerSize, "画布没有完成布局，坐标换算会失真");
        return (window, canvas, vm);
    }

    /// <summary>把"屏幕坐标"换成角色坐标，再固定一条线——这样标签落点可以和屏幕位置直接对照。</summary>
    private static double VerticalGuideAt(ImageCanvas canvas, double screenX) =>
        canvas.ScreenToFrame(new Point(screenX, 0)).X;

    private static double HorizontalGuideAt(ImageCanvas canvas, double screenY) =>
        canvas.ScreenToFrame(new Point(0, screenY)).Y;

    /// <summary>一条垂直 + 一条水平：标签分别落在顶部边缘带 / 左侧边缘带，且沿线的方向居中。</summary>
    [AvaloniaFact]
    public async Task Labels_SitOnTopEdgeAndLeftEdge_WithoutLeavingContent() {
        var (window, canvas, _) = await OpenAsync();

        // 100%：角色坐标与屏幕坐标 1:1，参考线吸附整像素后偏差不超过 0.5px
        canvas.SetZoomLevel(1.0);
        canvas.AddVerticalGuide(VerticalGuideAt(canvas, 300));
        canvas.AddHorizontalGuide(HorizontalGuideAt(canvas, 320));
        canvas.UpdateGuideLabelLayout();

        var boxes = canvas.GuideLabelBoxes;
        Assert.Equal(2, boxes.Count);

        var vertical = boxes.Single(box => box.Vertical);
        var horizontal = boxes.Single(box => !box.Vertical);
        var content = canvas.Bounds;
        var band = 24; // 标签高度约 15，留一点余量

        // 垂直线 → 顶部边缘带内（在标尺内侧、不越出画布顶）
        Assert.True(vertical.Rect.Top >= ImageCanvas.RulerSize,
            $"标签压到顶部标尺上了：{vertical.Rect}");
        Assert.True(vertical.Rect.Bottom <= ImageCanvas.RulerSize + band,
            $"标签没有贴住顶部边缘：{vertical.Rect}");
        Assert.InRange(vertical.Rect.Center.X, 298, 302); // 水平居中于线
        Assert.InRange(vertical.Rect.Left, ImageCanvas.RulerSize, content.Right - 1);

        // 水平线 → 左侧边缘带内
        Assert.True(horizontal.Rect.Left >= ImageCanvas.RulerSize,
            $"标签压到左侧标尺上了：{horizontal.Rect}");
        Assert.True(horizontal.Rect.Left <= ImageCanvas.RulerSize + band,
            $"标签没有贴住左侧边缘：{horizontal.Rect}");
        Assert.InRange(horizontal.Rect.Center.Y, 318, 322); // 垂直居中于线
        Assert.InRange(horizontal.Rect.Top, ImageCanvas.RulerSize, content.Bottom - 1);

        window.Close();
    }

    /// <summary>两条线靠得很近时：后一条标签顺延（顶部往下推 / 左侧往右推），矩形不重叠。</summary>
    [AvaloniaFact]
    public async Task CloseGuides_LabelsDoNotOverlap() {
        var (window, canvas, _) = await OpenAsync();

        canvas.AddVerticalGuide(VerticalGuideAt(canvas, 300));
        canvas.AddVerticalGuide(VerticalGuideAt(canvas, 304));
        canvas.AddHorizontalGuide(HorizontalGuideAt(canvas, 300));
        canvas.AddHorizontalGuide(HorizontalGuideAt(canvas, 304));
        canvas.UpdateGuideLabelLayout();

        var verticals = canvas.GuideLabelBoxes.Where(box => box.Vertical).ToList();
        var horizontals = canvas.GuideLabelBoxes.Where(box => !box.Vertical).ToList();
        Assert.Equal(2, verticals.Count);
        Assert.Equal(2, horizontals.Count);

        // 顶部边缘：y 区间不相交
        var firstDown = verticals.OrderBy(box => box.Rect.Top).ToList();
        Assert.True(firstDown[1].Rect.Top >= firstDown[0].Rect.Bottom,
            $"顶部标签重叠：{firstDown[0].Rect} / {firstDown[1].Rect}");

        // 左侧边缘：x 区间不相交
        var firstRight = horizontals.OrderBy(box => box.Rect.Left).ToList();
        Assert.True(firstRight[1].Rect.Left >= firstRight[0].Rect.Right,
            $"左侧标签重叠：{firstRight[0].Rect} / {firstRight[1].Rect}");

        window.Close();
    }

    /// <summary>左上角：竖线贴着左边缘时，它的顶部标签不能被左侧边缘的标签压住。</summary>
    [AvaloniaFact]
    public async Task CornerGuides_LabelsDoNotOverlapEachOther() {
        var (window, canvas, _) = await OpenAsync();
        canvas.SetZoomLevel(1.0);

        // 两条线都挤在左上角：两个标签都会被 clamp 到同一个角上
        canvas.AddVerticalGuide(VerticalGuideAt(canvas, ImageCanvas.RulerSize + 4));
        canvas.AddHorizontalGuide(HorizontalGuideAt(canvas, ImageCanvas.RulerSize + 4));
        canvas.UpdateGuideLabelLayout();

        var boxes = canvas.GuideLabelBoxes;
        Assert.Equal(2, boxes.Count);
        var top = boxes.Single(box => box.Vertical);
        var left = boxes.Single(box => !box.Vertical);
        Assert.False(left.Rect.Intersects(top.Rect),
            $"左上角的两个标签压在一起了：{top.Rect} / {left.Rect}");

        window.Close();
    }

    /// <summary>关掉「参考线坐标」后不再产生标签，但参考线本身还在。</summary>
    [AvaloniaFact]
    public async Task ShowGuideLabelsOff_ProducesNoLabels_ButKeepsGuides() {
        var (window, canvas, vm) = await OpenAsync();

        canvas.AddVerticalGuide(VerticalGuideAt(canvas, 300));
        canvas.AddHorizontalGuide(HorizontalGuideAt(canvas, 320));
        canvas.UpdateGuideLabelLayout();
        Assert.Equal(2, canvas.GuideLabelBoxes.Count);

        // VM → 控件（右键菜单绑定的是同一个属性）
        vm.IsGuideLabelsVisible = false;
        Assert.False(canvas.ShowGuideLabels);

        canvas.UpdateGuideLabelLayout();
        Assert.Empty(canvas.GuideLabelBoxes);
        Assert.Equal(2, canvas.GuideCount);

        vm.IsGuideLabelsVisible = true;
        canvas.UpdateGuideLabelLayout();
        Assert.Equal(2, canvas.GuideLabelBoxes.Count);

        window.Close();
    }

    /// <summary>负坐标（DNF 帧坐标可能为负）要按 <c>-12</c> 输出，不能变成带括号或空串。</summary>
    [AvaloniaFact]
    public async Task NegativeCoordinate_LabelTextIsPlainMinusNumber() {
        var (window, canvas, _) = await OpenAsync();

        // 100% + 以视口中心为锚点缩放 → 角色 0 落在画布中央附近，-12 一定还在可见区里
        canvas.SetZoomLevel(1.0);
        canvas.AddVerticalGuide(-12);
        canvas.UpdateGuideLabelLayout();

        var box = Assert.Single(canvas.GuideLabelBoxes);
        Assert.Equal(-12, box.Value);
        Assert.Equal("-12", GuideRenderer.FormatGuideValue(box.Value));

        window.Close();
    }

    /// <summary>坐标没变时标签文字必须命中缓存（不重建 FormattedText）。</summary>
    [AvaloniaFact]
    public async Task LabelText_IsCachedWhileCoordinatesStayTheSame() {
        var (window, canvas, _) = await OpenAsync();

        // 100%：角色坐标与屏幕坐标 1:1，后面加的线才不会跑到视野外
        canvas.SetZoomLevel(1.0);
        canvas.AddVerticalGuide(VerticalGuideAt(canvas, 300));
        canvas.AddHorizontalGuide(HorizontalGuideAt(canvas, 320));

        canvas.UpdateGuideLabelLayout();
        var afterFirst = canvas.GuideLabelTextCacheMisses;
        Assert.True(afterFirst > 0, "第一次布局应该产生缓存未命中");

        canvas.UpdateGuideLabelLayout();
        Assert.Equal(afterFirst, canvas.GuideLabelTextCacheMisses);

        // 加一条新坐标的线 → 只该多一次未命中（老的两个值仍命中）
        canvas.AddVerticalGuide(VerticalGuideAt(canvas, 500) + 1);
        canvas.UpdateGuideLabelLayout();
        Assert.Equal(afterFirst + 1, canvas.GuideLabelTextCacheMisses);

        window.Close();
    }

    /// <summary>悬停到某条线上时，只有那一条的标签被标记为高亮（绘制时提亮 + 描边）。</summary>
    [AvaloniaFact]
    public async Task HoveredGuide_IsTheOnlyActiveLabel() {
        var (window, canvas, _) = await OpenAsync();

        // 100%：吸附整像素后线仍在指针 ±1px 内，命中容差 4px 才成立
        canvas.SetZoomLevel(1.0);
        canvas.AddVerticalGuide(VerticalGuideAt(canvas, 300));
        canvas.AddVerticalGuide(VerticalGuideAt(canvas, 600));

        canvas.SimulatePointer(new Point(300, 200));
        canvas.UpdateGuideLabelLayout();
        var active = canvas.GuideLabelBoxes.Where(box => box.Active).ToList();
        var one = Assert.Single(active);
        Assert.True(one.Vertical);
        Assert.InRange(one.Rect.Center.X, 298, 302);

        // 移开后全部回到常态
        canvas.SimulatePointer(new Point(200, 500));
        canvas.UpdateGuideLabelLayout();
        Assert.DoesNotContain(canvas.GuideLabelBoxes, box => box.Active);

        window.Close();
    }

    /// <summary>
    ///     三条坐标只差 2px 的参考线：标签必须上下顺延，两两不重叠，且相邻间距 ≥2px
    ///     （贴太紧会糊成一块）。
    /// </summary>
    [AvaloniaFact]
    public async Task ThreeCloseGuides_LabelsKeepAtLeastTwoPixelGap() {
        var (window, canvas, _) = await OpenAsync();
        canvas.SetZoomLevel(1.0);

        canvas.AddVerticalGuide(VerticalGuideAt(canvas, 300));
        canvas.AddVerticalGuide(VerticalGuideAt(canvas, 301));
        canvas.AddVerticalGuide(VerticalGuideAt(canvas, 303));
        canvas.UpdateGuideLabelLayout();

        var boxes = canvas.GuideLabelBoxes.Where(box => box.Vertical)
            .OrderBy(box => box.Rect.Top)
            .ToList();
        Assert.Equal(3, boxes.Count);

        for (var i = 0; i < boxes.Count; i++) {
            for (var j = i + 1; j < boxes.Count; j++) {
                Assert.False(boxes[i].Rect.Intersects(boxes[j].Rect),
                    $"标签重叠：{boxes[i].Rect} / {boxes[j].Rect}");
            }
        }

        for (var i = 1; i < boxes.Count; i++) {
            var gap = boxes[i].Rect.Top - boxes[i - 1].Rect.Bottom;
            Assert.True(gap >= 2 - 1e-9,
                $"第 {i} 个标签与上一个只隔 {gap:F1}px：{boxes[i - 1].Rect} / {boxes[i].Rect}");
        }

        window.Close();
    }

    /// <summary>
    ///     标尺上的鼠标游标标签与参考线标签不能重叠：游标停在同一位置时，参考线标签整体下移。
    /// </summary>
    [AvaloniaFact]
    public async Task GuideLabel_AvoidsRulerCursorLabel() {
        var (window, canvas, _) = await OpenAsync();
        canvas.SetZoomLevel(1.0);

        canvas.AddVerticalGuide(VerticalGuideAt(canvas, 300));
        canvas.SimulatePointer(new Point(300, 200)); // 顶部游标标签落在 x≈306
        canvas.UpdateGuideLabelLayout();

        var box = Assert.Single(canvas.GuideLabelBoxes);
        Assert.True(box.Vertical);
        Assert.NotEmpty(canvas.CursorLabelBoxes);

        foreach (var cursor in canvas.CursorLabelBoxes) {
            Assert.False(box.Rect.Intersects(cursor), $"参考线标签压住了游标标签：{box.Rect} / {cursor}");
        }

        // 原本贴顶部（y≈22），被游标标签顶开后至少下移一个标签高度
        Assert.True(box.Rect.Top >= ImageCanvas.RulerSize + 3 + 15,
            $"标签没有为游标让位：{box.Rect}");

        window.Close();
    }
}
