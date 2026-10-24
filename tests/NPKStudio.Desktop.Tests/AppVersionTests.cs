using System.Text.RegularExpressions;

using Avalonia.Headless.XUnit;

using NPKStudio.Desktop;
using NPKStudio.Desktop.ViewModels;
using NPKStudio.Desktop.Views;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     版本号单一来源。
///     <para>
///         用户遇到过"软件里的版本和发布包不一致"：csproj 的 <c>&lt;Version&gt;</c> 一直停在 0.8.1，
///         而 zip 名已经到 1.0.x，界面上又完全没有版本显示。这组测试锁住"界面标题里能读到版本"，
///         并且版本号来自程序集而不是某个写死的常量。
///     </para>
/// </summary>
public sealed class AppVersionTests {
    /// <summary>版本号是三段式数字，且不是占位值。</summary>
    [Fact]
    public void AppVersion_IsThreePartNumeric() {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), MainWindowViewModel.AppVersion);
        Assert.NotEqual("0.0.0", MainWindowViewModel.AppVersion);
    }

    /// <summary>没有打开文件时，标题是「NPKStudio 版本号」。</summary>
    [AvaloniaFact]
    public void WindowTitle_WithoutFile_ShowsVersion() {
        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        Assert.Contains(MainWindowViewModel.AppVersion, vm.WindowTitle);
        Assert.StartsWith("NPKStudio", vm.WindowTitle);

        window.Close();
    }

    /// <summary>打开文件夹后标题同时带文件名与版本号，且切换会刷新（属性变更通知没漏）。</summary>
    [AvaloniaFact]
    public async Task WindowTitle_WithFile_ShowsFileAndVersion() {
        using var folder = TestFolder.Create();
        folder.WriteImg("walk.img", frameCount: 2, width: 8, height: 8);

        var window = new MainWindow();
        window.Show();

        var vm = (MainWindowViewModel)window.DataContext!;
        var changed = 0;
        vm.PropertyChanged += (_, e) => {
            if (e.PropertyName == nameof(MainWindowViewModel.WindowTitle)) changed++;
        };

        await vm.OpenFolderAsync(folder.Path);

        Assert.Contains(MainWindowViewModel.AppVersion, vm.WindowTitle);
        Assert.Contains(vm.FileName, vm.WindowTitle);
        Assert.True(changed > 0, "打开文件夹后 WindowTitle 没有发出变更通知，标题栏不会刷新");

        window.Close();
    }

    /// <summary>程序集里写进去的版本就是 csproj 的值（防止只改文档不改程序集又漂移）。</summary>
    [Fact]
    public void AssemblyVersion_MatchesCsprojVersion() {
        var csproj = FindCsproj();
        if (csproj is null) return; // 找不到源码树（例如只跑已构建的测试程序集）就跳过

        var match = Regex.Match(csproj, @"<Version>([^<]+)</Version>");
        Assert.True(match.Success, "csproj 里没有 <Version> 节点，打包脚本与界面会再次失配");

        Assert.Equal(match.Groups[1].Value.Trim(), AppInfo.Version);
    }

    private static string? FindCsproj() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && directory is not null; i++) {
            var candidate = Path.Combine(directory.FullName, "src", "NPKStudio.Desktop", "NPKStudio.Desktop.csproj");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }

        return null;
    }
}
