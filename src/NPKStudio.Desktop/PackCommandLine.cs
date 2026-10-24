using System.Text;
using NPKStudio.Desktop.Services;

namespace NPKStudio.Desktop;

/// <summary>
///     无界面的批量打包命令：
///     <c>NPKStudio.exe --pack &lt;根目录&gt; --manifest &lt;清单.txt&gt; --out &lt;输出目录&gt;</c>
///     <para>用于把"按 NPK 分组的导出文件夹"一次性重打包成 NPK。</para>
/// </summary>
internal static class PackCommandLine {
    /// <summary>
    ///     <c>--scan-paths &lt;NPK目录&gt; --out &lt;清单.txt&gt;</c>：
    ///     扫描一个装满 NPK 的目录（客户端 <c>ImagePacks2</c>，或自己以前打好的包），
    ///     把每个包的条目表读出来写成 <c>IMG: sprite/...</c> 清单——这就是"清单从哪来"的答案，不需要 PVF。
    /// </summary>
    public static bool TryScan(string[] args) {
        var npkDirectory = GetValue(args, "--scan-paths");
        if (npkDirectory is null) return false;

        var output = GetValue(args, "--out") ?? "npk-paths.txt";
        try {
            var result = ResourceManifestWriter.Write(npkDirectory, output);
            Console.WriteLine($"已从 {result.NpkCount} 个 NPK 生成清单：{output}（{result.PathCount} 条路径）");
            return true;
        } catch (Exception ex) {
            Console.Error.WriteLine($"生成清单失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>若参数里含 <c>--pack</c> 则执行并返回 <c>true</c>。</summary>
    public static bool TryRun(string[] args) {
        var root = GetValue(args, "--pack");
        if (root is null) return false;

        var manifestPath = GetValue(args, "--manifest");
        var output = GetValue(args, "--out") ?? Path.Combine(root, "_packed");
        var baseDirectory = GetValue(args, "--base");

        var report = new StringBuilder();
        try {
            if (manifestPath is null || !File.Exists(manifestPath)) {
                report.AppendLine($"错误：--manifest 不存在：{manifestPath}");
            } else if (!Directory.Exists(root)) {
                report.AppendLine($"错误：--pack 目录不存在：{root}");
            } else {
                var manifest = ResourceManifest.Load(manifestPath);
                report.AppendLine($"清单路径数：{manifest.Count}");
                report.AppendLine($"源目录：{root}");
                report.AppendLine($"输出目录：{output}");
                report.AppendLine(baseDirectory is null
                    ? "合并基底：无（只输出缺失条目，覆盖同名 NPK 会丢失原有条目）"
                    : $"合并基底：{baseDirectory}（同名条目替换，其余保留）");
                report.AppendLine();

                var results = NpkPackService.PackAll(root, manifest, output, baseDirectory);
                var ok = results.Count(r => r.Success);
                foreach (var r in results) {
                    report.AppendLine(r.Success
                        ? $"OK   {r.NpkName,-60} {r.EntryCount,4} 条目  {r.TotalBytes,10:N0} B" +
                          (r.Replaced + r.Added > 0 ? $"  [替换 {r.Replaced} / 新增 {r.Added}]" : "") +
                          (r.Note is null ? "" : $"  （{r.Note}）")
                        : $"FAIL {r.Folder,-60} {r.Error}");
                }

                report.AppendLine();
                report.AppendLine($"完成：成功 {ok} / {results.Count}，条目合计 {results.Where(r => r.Success).Sum(r => r.EntryCount)}");
            }
        } catch (Exception ex) {
            report.AppendLine($"异常：{ex}");
        }

        var text = report.ToString();
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "pack-report.txt"), text, Encoding.UTF8);
        Console.WriteLine(text);
        return true;
    }

    private static string? GetValue(string[] args, string key) {
        for (var i = 0; i < args.Length - 1; i++) {
            if (args[i] == key) return args[i + 1];
        }

        return null;
    }
}
