using System.Diagnostics;
using System.Text;

namespace NPKStudio.Desktop;

/// <summary>启动期致命异常的最小可用落盘器（不依赖任何 UI 类型）。</summary>
internal static class CrashReporter {
    public static void Write(Exception exception) {
        try {
            var dir = Path.Combine(AppContext.BaseDirectory, "log");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:O}] {exception.GetType().FullName}: {exception.Message}");
            sb.AppendLine(exception.StackTrace);
            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            Trace.WriteLine(sb.ToString());
        } catch {
            // 崩溃报告自身失败时不得掩盖原始异常。
        }
    }
}
