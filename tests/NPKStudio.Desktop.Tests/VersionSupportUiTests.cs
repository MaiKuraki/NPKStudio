using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using NPKStudio.Core.Formats;
using NPKStudio.Desktop.Localization;
using NPKStudio.Desktop.ViewModels;
using NPKStudio.Desktop.Views;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     「格式支持等级」在界面上的落地：
///     <list type="bullet">
///         <item>v2（已验证）徽标只显示版本号，不能误报「实验性」；</item>
///         <item>v7（仅透传）帧区给可读原因，插入 / 删除 / 替换 / 裁剪 / GIF 全部置灰；</item>
///         <item>v5（实验性）在无头宿主下保存不弹窗、直接继续，且同一 IMG 只确认一次。</item>
///     </list>
///     <para>
///         ⚠️ v5 文件是<b>手工构造的内联 1555 帧</b>，v7 文件是<b>手工构造的头部</b>——
///         它们只用于验证界面策略，不代表真实样本。
///     </para>
/// </summary>
public sealed class VersionSupportUiTests
{
    private static TranslationCatalog Loc => TranslationCatalog.Instance;

    private static ImgFile ReadBack(string path) {
        using var stream = File.OpenRead(path);
        return ImgFile.Read(stream, Path.GetFileName(path), stream.Length);
    }

    /// <summary>1) 打开 v2：徽标形如「v2 · 线性 1555 · ZLIB」，不含「实验性」，编辑按钮可用。</summary>
    [AvaloniaFact]
    public async Task OpenV2_BadgeShowsVersionWithoutExperimentalMark() {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 2, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var viewModel = (MainWindowViewModel)window.DataContext!;
        await viewModel.OpenFolderAsync(folder.Path);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(ImgVersion.Ver2, viewModel.CurrentImgFile!.Version);
        Assert.True(viewModel.Inspector.HasSelection);
        Assert.False(viewModel.Inspector.IsVersionExperimental);
        Assert.False(viewModel.Inspector.IsVersionPassthrough);
        Assert.Null(viewModel.Inspector.VersionTip);

        Assert.StartsWith("v2", viewModel.Inspector.MetaText);
        Assert.DoesNotContain(Loc["version.experimental"], viewModel.Inspector.MetaText);
        Assert.Contains(Loc["bit.argb1555"], viewModel.Inspector.MetaText);

        // 已验证版本不应被置灰
        Assert.True(window.FindControl<Button>("InsertFrameButton")!.IsEnabled);
        Assert.True(window.FindControl<Button>("DeleteFrameButton")!.IsEnabled);
        Assert.True(window.FindControl<Button>("ReplaceFrameButton")!.IsEnabled);

        window.Close();
    }

    /// <summary>
    ///     1b) 打开构造的 v5（实验性）：徽标带「实验性」+ 版本号，tooltip 写明"无真实样本验证"。
    ///     <para>⚠️ 这个 v5 文件是手工构造的内联 1555 帧，<b>不是真实样本</b>——只验证标注文案。</para>
    /// </summary>
    [AvaloniaFact]
    public async Task OpenV5_BadgeIsMarkedExperimentalWithTooltip() {
        using var folder = TestFolder.Create();
        folder.WriteImg("effect.img", frameCount: 1, width: 8, height: 8, ImgVersion.Ver5);

        var window = new MainWindow();
        window.Show();

        var viewModel = (MainWindowViewModel)window.DataContext!;
        await viewModel.OpenFolderAsync(folder.Path);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(ImgVersion.Ver5, viewModel.CurrentImgFile!.Version);
        Assert.True(viewModel.IsExperimentalVersion);
        Assert.False(viewModel.IsPassthroughVersion);

        Assert.True(viewModel.Inspector.IsVersionExperimental);
        Assert.StartsWith("v5", viewModel.Inspector.MetaText);
        Assert.Contains(Loc["version.experimental"], viewModel.Inspector.MetaText);
        Assert.Equal(Loc["version.tip.experimental"], viewModel.Inspector.VersionTip);

        // 实验性版本仍然可编辑（写之前才拦一道）
        Assert.True(viewModel.CanEditFrames);
        Assert.True(viewModel.Inspector.CanInsert);
        Assert.True(window.FindControl<Button>("InsertFrameButton")!.IsEnabled);

        window.Close();
    }

    /// <summary>
    ///     2) 打开 v7（仅透传）：帧区空状态给出可读原因并带版本号；
    ///     插入 / 删除 / 替换 / 裁剪 / 导出 GIF 全部 <c>IsEnabled == false</c>。
    /// </summary>
    [AvaloniaFact]
    public async Task OpenPassthrough_ShowsReadableReasonAndDisablesEditing() {
        using var folder = TestFolder.Create();
        folder.WriteHeaderOnlyImg("mystery.img", ImgVersion.Ver7);

        var window = new MainWindow();
        window.Show();

        var viewModel = (MainWindowViewModel)window.DataContext!;
        await viewModel.OpenFolderAsync(folder.Path);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(ImgVersion.Ver7, viewModel.CurrentImgFile!.Version);
        Assert.True(viewModel.IsPassthroughVersion);
        Assert.False(viewModel.IsExperimentalVersion);
        Assert.False(viewModel.HasFrames);
        Assert.False(viewModel.CanEditFrames);

        // 帧区空状态说人话：带版本号 + "只能原样保存"
        Assert.Contains("v7", viewModel.FrameAreaEmptyText);
        Assert.Contains("原样保存", viewModel.FrameAreaEmptyText);
        Assert.DoesNotContain("左侧选择一个 IMG", viewModel.FrameAreaEmptyText);

        var emptyText = window.FindControl<TextBlock>("FrameEmptyText");
        Assert.NotNull(emptyText);
        Assert.True(emptyText!.IsVisible);
        Assert.Equal(viewModel.FrameAreaEmptyText, emptyText.Text);

        // 检查器与工具栏的编辑入口全部置灰（不替代 Core 的 NotSupportedException，只是不让点）
        Assert.False(viewModel.Inspector.CanInsert);
        Assert.False(viewModel.Inspector.CanDelete);
        Assert.False(viewModel.Inspector.CanResize);
        Assert.False(window.FindControl<Button>("InsertFrameButton")!.IsEnabled);
        Assert.False(window.FindControl<Button>("DeleteFrameButton")!.IsEnabled);
        Assert.False(window.FindControl<Button>("ReplaceFrameButton")!.IsEnabled);
        Assert.False(window.FindControl<Button>("TrimFrameButton")!.IsEnabled);
        Assert.False(window.FindControl<Button>("ExportGifButton")!.IsEnabled);

        window.Close();
    }

    /// <summary>3) 无头宿主（没有注入确认回调）保存 v5：不弹窗、直接继续，写回成功并清脏。</summary>
    [AvaloniaFact]
    public async Task ExperimentalSave_InHeadlessHost_ContinuesWithoutPrompt() {
        using var folder = TestFolder.Create();
        var path = folder.WriteImg("effect.img", frameCount: 1, width: 8, height: 8, ImgVersion.Ver5);

        var viewModel = new MainWindowViewModel();
        await viewModel.OpenFolderAsync(folder.Path);

        Assert.Equal(ImgVersion.Ver5, viewModel.CurrentImgFile!.Version);
        Assert.True(viewModel.IsExperimentalVersion);
        Assert.Null(viewModel.ConfirmExperimentalSave);     // 无头宿主：没人能点弹窗

        viewModel.Inspector.X = 5;
        Assert.True(viewModel.IsDirty);

        await viewModel.SaveAsync();

        Assert.False(viewModel.IsDirty);
        Assert.DoesNotContain(Loc["status.experimentalSaveCancelled"], viewModel.Status);

        // 磁盘上确实写出了 v5（版本与帧数都对）
        var reparsed = ReadBack(path);
        Assert.Equal(ImgVersion.Ver5, reparsed.Version);
        Assert.Single(reparsed.Frames);
        Assert.Equal(5, reparsed.Frames[0].X);
    }

    /// <summary>4) 同一 IMG 只确认一次：第一次保存问一次，编辑后再保存不再问。</summary>
    [AvaloniaFact]
    public async Task ExperimentalSave_AsksOnlyOncePerImage() {
        using var folder = TestFolder.Create();
        folder.WriteImg("effect.img", frameCount: 1, width: 8, height: 8, ImgVersion.Ver5);

        var viewModel = new MainWindowViewModel();
        var asked = 0;
        viewModel.ConfirmExperimentalSave = _ => {
            asked++;
            return Task.FromResult(true);
        };

        await viewModel.OpenFolderAsync(folder.Path);

        viewModel.Inspector.X = 5;
        await viewModel.SaveAsync();
        Assert.Equal(1, asked);

        // 再次编辑同一个 IMG 并保存：本次会话已确认过，不再问
        viewModel.Inspector.X = 9;
        Assert.True(viewModel.IsDirty);
        await viewModel.SaveAsync();
        Assert.Equal(1, asked);

        // 切到另一个 IMG 后重新问
        folder.WriteImg("other.img", frameCount: 1, width: 8, height: 8, ImgVersion.Ver5);
        await viewModel.OpenFolderAsync(folder.Path);
        viewModel.SelectedEntry = viewModel.Entries.First(entry => entry.Name == "other.img");
        await viewModel.PendingFrameLoad!;

        viewModel.Inspector.X = 3;
        await viewModel.SaveAsync();
        Assert.Equal(2, asked);
    }

    /// <summary>5) 用户点「取消」：不写回、保留脏标记、状态栏提示已取消。</summary>
    [AvaloniaFact]
    public async Task ExperimentalSave_Cancelled_KeepsDirtyAndDoesNotWrite() {
        using var folder = TestFolder.Create();
        var path = folder.WriteImg("effect.img", frameCount: 1, width: 8, height: 8, ImgVersion.Ver5);

        var viewModel = new MainWindowViewModel();
        viewModel.ConfirmExperimentalSave = _ => Task.FromResult(false);

        await viewModel.OpenFolderAsync(folder.Path);
        viewModel.Inspector.X = 5;

        await viewModel.SaveAsync();

        Assert.True(viewModel.IsDirty);
        Assert.Equal(Loc["status.experimentalSaveCancelled"], viewModel.Status);

        // 文件一个字节都没被改写：X 仍是原始值 0
        Assert.Equal(0, ReadBack(path).Frames[0].X);
    }

    /// <summary>6) 已验证版本（v2）保存时永远不问——即使宿主注入了确认回调。</summary>
    [AvaloniaFact]
    public async Task VerifiedSave_NeverAsksForConfirmation() {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 1, width: 8, height: 8);

        var viewModel = new MainWindowViewModel();
        var asked = 0;
        viewModel.ConfirmExperimentalSave = _ => {
            asked++;
            return Task.FromResult(true);
        };

        await viewModel.OpenFolderAsync(folder.Path);
        viewModel.Inspector.X = 5;
        await viewModel.SaveAsync();

        Assert.Equal(0, asked);
        Assert.False(viewModel.IsDirty);
    }
}
