using Avalonia.Headless.XUnit;

using NPKStudio.Desktop.Views;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     新增工具栏「资源清单」按钮的加载冒烟：XAML 里的图标、Flyout、事件处理器都要能解析。
///     <para>StaticResource / Click 处理器名写错在编译期查不出来，只有真的构造一次窗口才会炸。</para>
/// </summary>
public sealed class MainWindowXamlSmokeTests
{
    [AvaloniaFact]
    public void MainWindow_LoadsToolbarWithManifestFlyout()
    {
        var window = new MainWindow();

        Assert.NotNull(window.DataContext);
        window.Close();
    }
}
