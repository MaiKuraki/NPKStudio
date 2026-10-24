using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

using NPKStudio.Desktop.ViewModels;
using NPKStudio.Desktop.Views;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     帧检查器输入区（两行两列）的布局断言。
///     <para>
///         宽度相等由列定义保证、垂直居中由模板里的 TextBox 保证——两条都只有真控件能量。
///     </para>
/// </summary>
public sealed class InspectorLayoutUiTests
{
    private static async Task<(MainWindow Window, MainWindowViewModel Vm)> OpenAsync() {
        var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 2, width: 32, height: 32);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);
        Assert.NotNull(vm.SelectedFrame);

        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static NumericUpDown Input(MainWindow window, string name) {
        var input = window.FindControl<NumericUpDown>(name);
        Assert.NotNull(input);
        return input!;
    }

    /// <summary>
    ///     四个输入框两两等宽（允许 ≤1px 的布局取整差），并排成两行两列（X/Y 一行，宽/高 一行）。
    ///     <para>不追求绝对等宽：为消除 0.5px 差而关掉布局取整会让边框落在半像素上、抗锯齿发虚，
    ///     对像素艺术工具得不偿失。1px 差肉眼不可辨，边框锐利更重要。</para>
    /// </summary>
    [AvaloniaFact]
    public async Task Inputs_AreEqualWidthAndArrangedInTwoRows() {
        var (window, _) = await OpenAsync();

        var x = Input(window, "InspectorXInput");
        var y = Input(window, "InspectorYInput");
        var width = Input(window, "InspectorWidthInput");
        var height = Input(window, "InspectorHeightInput");

        Assert.True(x.Bounds.Width > 0, "检查器没有完成布局");

        Assert.True(Math.Abs(x.Bounds.Width - y.Bounds.Width) <= 1, $"X={x.Bounds.Width} 与 Y={y.Bounds.Width} 宽度差超过 1px");
        Assert.True(Math.Abs(width.Bounds.Width - height.Bounds.Width) <= 1, $"宽={width.Bounds.Width} 与 高={height.Bounds.Width} 宽度差超过 1px");
        Assert.True(Math.Abs(x.Bounds.Width - width.Bounds.Width) <= 1, $"X={x.Bounds.Width} 与 宽={width.Bounds.Width} 宽度差超过 1px");

        // 同一行：X 与 Y 的 Y 相同；宽 与 高 的 Y 相同；两行不同
        Assert.Equal(x.Bounds.Y, y.Bounds.Y);
        Assert.Equal(width.Bounds.Y, height.Bounds.Y);
        Assert.NotEqual(x.Bounds.Y, width.Bounds.Y);

        window.Close();
    }

    /// <summary>
    ///     输入框内文字的垂直中心与输入框中心误差 ≤1px。
    ///     <para>
    ///         模板里的 TextBox 撑满 30px 高、内容对齐为 Center，所以"TextBox 的几何中心"
    ///         就是文字排版区域的中心；再看它相对 NumericUpDown 是否偏移。
    ///     </para>
    /// </summary>
    [AvaloniaFact]
    public async Task Inputs_TextIsVerticallyCentered() {
        var (window, _) = await OpenAsync();

        foreach (var name in new[] {
                     "InspectorXInput", "InspectorYInput", "InspectorWidthInput", "InspectorHeightInput"
                 }) {
            var input = Input(window, name);
            var textBox = input.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            Assert.True(textBox is not null, $"{name} 的模板里找不到 PART_TextBox");

            Assert.Equal(VerticalAlignment.Center, textBox!.VerticalContentAlignment);
            Assert.Equal(0, textBox.Padding.Top);
            Assert.Equal(0, textBox.Padding.Bottom);

            var textCenter = textBox.TranslatePoint(new Point(0, textBox.Bounds.Height / 2), input);
            Assert.NotNull(textCenter);
            Assert.True(Math.Abs(textCenter!.Value.Y - input.Bounds.Height / 2) <= 1,
                $"{name} 文字垂直中心偏移 {(textCenter.Value.Y - input.Bounds.Height / 2):F1}px");
        }

        window.Close();
    }

    /// <summary>两行两列后检查器整块变矮：新版实测 186px；旧版多两行输入（每行 36px），约 258px。</summary>
    [AvaloniaFact]
    public async Task Inspector_IsShorterThanFourRowLayout() {
        var (window, _) = await OpenAsync();

        var panel = window.FindControl<Border>("InspectorPanel");
        Assert.NotNull(panel);
        Assert.True(panel!.Bounds.Height > 0, "检查器没有完成布局");

        var grid = window.FindControl<Grid>("InspectorGeometryGrid");
        Assert.NotNull(grid);
        // 两行输入 + 一行提示 = 3 行（旧版是 4 行输入 + 1 行提示 = 5 行）
        Assert.Equal(3, grid!.RowDefinitions.Count);

        // 旧版多两行输入，每行 30px + 6px 行距 = 72px；阈值取 230 确保"确实变矮"。
        Assert.True(panel.Bounds.Height < 230,
            $"检查器没有变矮：{panel.Bounds.Height:F1}px（新版实测 186px，旧版约 258px）");

        window.Close();
    }
}
