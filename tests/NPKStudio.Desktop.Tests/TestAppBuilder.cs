using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

using NPKStudio.Desktop;

[assembly: AvaloniaTestApplication(typeof(NPKStudio.Desktop.Tests.TestAppBuilder))]

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     无头 Avalonia 宿主：让 <c>[AvaloniaTest]</c> 在真实的 Avalonia UI 线程上跑测试，
///     这样 <c>DispatcherTimer</c>（播放/防抖/落盘）与 <c>Dispatcher.UIThread.Post</c>（缩略图）都能工作。
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>
///     测试环境初始化：把配置目录指到临时目录，避免读写用户真实的 <c>%LOCALAPPDATA%\NPKStudio</c>。
/// </summary>
internal static class TestEnvironment
{
    /// <summary>本次测试运行使用的配置目录。</summary>
    public static string ConfigDirectory { get; private set; } = string.Empty;

    [ModuleInitializer]
    internal static void Init()
    {
        ConfigDirectory = Path.Combine(Path.GetTempPath(), "npkstudio-tests-config");
        try
        {
            Directory.Delete(ConfigDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 上次运行的残留删不掉不影响本次（下面的文件都是覆盖写）
        }

        Directory.CreateDirectory(ConfigDirectory);
        Environment.SetEnvironmentVariable("NPKSTUDIO_CONFIG_DIR", ConfigDirectory);
    }

    /// <summary>读某个配置文件的内容；文件不存在时返回 <c>null</c>。</summary>
    public static string? ReadConfig(string fileName)
    {
        var path = Path.Combine(ConfigDirectory, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
