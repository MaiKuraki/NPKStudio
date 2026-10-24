using System.Reflection;

namespace NPKStudio.Desktop;

/// <summary>
///     程序集版本的唯一读取点。
///     <para>
///         版本号的<b>唯一来源</b>是 <c>NPKStudio.Desktop.csproj</c> 里的 <c>&lt;Version&gt;</c>：
///         打包脚本也从同一个文件读版本命名 zip，所以界面、exe 属性、压缩包名不会再各说各话。
///     </para>
///     <para>
///         只读一次程序集元数据（冷路径，启动时静态初始化），不做类型反射。
///     </para>
/// </summary>
internal static class AppInfo {
    /// <summary>形如 <c>1.0.5</c> 的对外版本号（去掉程序集版本的第 4 位修订号）。</summary>
    internal static string Version { get; } = ReadVersion();

    private static string ReadVersion() {
        var version = typeof(AppInfo).Assembly.GetName().Version;
        if (version is null) return "0.0.0";
        return version.Revision > 0
            ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
