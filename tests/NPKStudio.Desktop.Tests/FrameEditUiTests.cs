using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

using NPKStudio.Desktop.ViewModels;
using NPKStudio.Desktop.Views;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     帧编辑闭环的"界面层"断言：真实 <see cref="ListBox" /> 的选区同步、Delete 快捷键。
///     <para>
///         <b>为什么必须有这一层</b>：<c>SelectedItems</c> 是 Avalonia 的可写 <c>IList</c>，
///         绑定后控件把它当作选区存储的本体。绑定写错（或换了集合实例）时单选仍能用、多选静默失效，
///         纯 ViewModel 测试看不出来；快捷键则受 <c>OnKeyDown</c> 里"输入框放行"那段逻辑影响。
///     </para>
/// </summary>
public sealed class FrameEditUiTests
{
    /// <summary>在列表控件里选中两帧 → VM 的 SelectedFrames 收到两帧，检查器切成多选。</summary>
    [AvaloniaFact]
    public async Task MultiSelectionInList_ReachesViewModel()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 4, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);

        var list = window.FindControl<ListBox>("FrameList");
        Assert.NotNull(list);
        Assert.NotNull(list!.SelectedItems);
        Assert.Equal(SelectionMode.Multiple, list.SelectionMode);

        list.Selection!.Clear();
        list.Selection.Select(1);
        list.Selection.Select(2);

        Assert.Equal(2, vm.SelectedFrames.Count);
        Assert.Contains(vm.Frames[1], vm.SelectedFrames);
        Assert.Contains(vm.Frames[2], vm.SelectedFrames);
        Assert.False(vm.Inspector.IsSingle);

        window.Close();
    }

    /// <summary>多选后删除：两帧一起消失，控件选区回到单帧。</summary>
    [AvaloniaFact]
    public async Task DeleteAfterMultiSelection_RemovesBothAndRestoresSingleSelection()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 5, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);

        var list = window.FindControl<ListBox>("FrameList")!;
        list.Selection!.Clear();
        list.Selection.Select(1);
        list.Selection.Select(2);

        vm.DeleteSelectedFrames();

        Assert.Equal(3, vm.Frames.Count);
        Assert.Equal(3, vm.CurrentImgFile!.Frames.Count);
        Assert.Single(list.SelectedItems!);
        Assert.Same(vm.SelectedFrame, list.SelectedItem);
        Assert.True(vm.Inspector.IsSingle);

        window.Close();
    }

    /// <summary>
    ///     Delete 键：焦点不在输入框时删帧；焦点在 <see cref="TextBox" /> 时放行（当作删字符），
    ///     否则在搜索框里按 Delete 会莫名其妙删掉一帧。
    /// </summary>
    [AvaloniaFact]
    public async Task DeleteKey_DeletesFrame_ButIsIgnoredFromTextBox()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 3, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);
        vm.SelectedFrame = vm.Frames[1];

        // 搜索框里按 Delete：事件从 TextBox 冒泡上来，Source 是 TextBox，必须放行
        var search = window.FindControl<TextBox>("SearchBox");
        Assert.NotNull(search);
        search!.RaiseEvent(new KeyEventArgs {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Delete
        });
        Assert.Equal(3, vm.Frames.Count);

        // 焦点不在输入框：删掉当前帧
        window.RaiseEvent(new KeyEventArgs {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Delete
        });
        Assert.Equal(2, vm.Frames.Count);

        window.Close();
    }

    /// <summary>
    ///     非法输入：帧字段不变、输入框挂上 <c>invalid</c> 类并且真的解析出红色边框
    ///     （<c>.invalid</c> 用的是 <c>/template/ ButtonSpinner#PART_Spinner</c> 选择器，写错会静默失效）。
    /// </summary>
    [AvaloniaFact]
    public async Task InvalidInput_MarksFieldRed_AndLeavesFrameUntouched()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 2, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);
        vm.SelectedFrame = vm.Frames[0];

        var frame = vm.SelectedFrame!.Frame;
        var before = frame.X;

        vm.Inspector.X = null;

        Assert.True(vm.Inspector.XInvalid);
        Assert.Equal(before, frame.X);
        Assert.Contains("整数", vm.Status);

        var boxes = window.GetVisualDescendants().OfType<NumericUpDown>().ToList();
        Assert.Equal(4, boxes.Count);
        Assert.Contains("invalid", boxes[0].Classes);
        Assert.DoesNotContain("invalid", boxes[3].Classes);

        // 边框真的变成了红系（.invalid → StateDanger），并且已经传进模板里的 ButtonSpinner
        var invalidBrush = Assert.IsAssignableFrom<ISolidColorBrush>(boxes[0].BorderBrush);
        var normalBrush = Assert.IsAssignableFrom<ISolidColorBrush>(boxes[3].BorderBrush);
        Assert.NotEqual(normalBrush.Color, invalidBrush.Color);
        Assert.True(invalidBrush.Color.R > invalidBrush.Color.G + 40);

        window.Close();
    }

    /// <summary>
    ///     播放 / 步进推帧时选区必须收敛成单帧：否则"播完再按 Delete"会把播放过程中
    ///     累积选中的帧全删掉（<c>SelectionModel.SelectedItem</c> 的 setter 本身就是"先清空再选中"，
    ///     这里用一个真实控件把这条语义钉住）。
    /// </summary>
    [AvaloniaFact]
    public async Task SteppingFrames_CollapsesSelectionToSingle()
    {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 8, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        await vm.OpenFolderAsync(folder.Path);

        var list = window.FindControl<ListBox>("FrameList")!;
        list.Selection!.Clear();
        list.Selection.Select(1);
        list.Selection.Select(2);
        Assert.Equal(2, vm.SelectedFrames.Count);

        vm.StepFrame(1);

        Assert.Single(vm.SelectedFrames);
        Assert.Same(vm.SelectedFrame, vm.SelectedFrames[0]);

        window.Close();
    }
}
