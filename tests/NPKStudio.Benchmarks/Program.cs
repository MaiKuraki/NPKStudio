using System.Diagnostics;
using NPKStudio.Core.Formats.Npk;
using NPKStudio.TestSupport;

namespace NPKStudio.Benchmarks;

internal static class Program {
    private static readonly List<Row> Rows = [];

    private static void Main(string[] args) {
        // 优先自备的真实样本（不入库），没有就用仓库自带的合成样本。
        var sample = SampleFiles.RealSampleAvailable ? SampleFiles.RealSamplePath : SampleFiles.SyntheticSamplePath;
        if (!File.Exists(sample)) {
            Console.WriteLine($"找不到样本：{sample}（先运行 dotnet run --project tools/make-sample）");
            return;
        }

        Console.WriteLine($"样本：{Path.GetFileName(sample)}");

        if (args.Contains("--stress")) {
            Stress(sample);
            return;
        }

        if (args.Length >= 2 && args[0] == "--scan-all") {
            ScanAll(args[1]);
            return;
        }

        Console.WriteLine($"Runtime {Environment.Version} | GC {(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")}" +
                          $" | {(Environment.Is64BitProcess ? "x64" : "x86")}");
        Console.WriteLine(new string('-', 96));

        Run("Open NPK", 500, () => {
            using var a = NpkArchive.Open(sample);
        });

        using var archive = NpkArchive.Open(sample);
        var entry = archive.Entries[0];

        Run($"ReadRaw × {archive.Entries.Count} entries", 300, () => {
            foreach (var e in archive.Entries) {
                _ = archive.ReadRaw(e);
            }
        });

        var img = archive.LoadImg(entry);
        Run("LoadImg (decode index)", 300, () => {
            using var a = NpkArchive.Open(sample);
            _ = a.LoadImg(a.Entries[0]);
        });

        Run($"Decode frame0 ({img.Frames[0].Width}×{img.Frames[0].Height})", 2000, () => {
            _ = img.Decode(img.Frames[0]);
        });

        Run($"Decode all {img.Frames.Count} frames", 800, () => {
            foreach (var f in img.Frames) {
                _ = img.Decode(f);
            }
        });

        var payload = archive.Entries.Select(e => (Path: e.Path, Data: archive.ReadRaw(e))).ToArray();
        Run($"NpkWriter.Write ({payload.Length} entries)", 200, () => {
            using var ms = new MemoryStream();
            NpkWriter.WriteStreaming(ms, payload.Select(e => NpkEntrySource.FromBytes(e.Path, e.Data)).ToList());
        });

        // 闭包开销：批量打包时每个条目都会构造一个 NpkEntrySource
        var probePath = payload[0].Path;
        var probeData = payload[0].Data;
        Run("Create 1000 NpkEntrySource (closure)", 200, () => {
            var list = new NpkEntrySource[1000];
            for (var i = 0; i < list.Length; i++) {
                list[i] = NpkEntrySource.FromBytes(probePath, probeData);
            }
        });

        // 大包（真实客户端）——存在才测
        var big = args.FirstOrDefault(File.Exists);
        if (big is not null) {
            Console.WriteLine();
            Console.WriteLine($"大包样本：{Path.GetFileName(big)}  ({new FileInfo(big).Length / 1024.0 / 1024.0:F1} MB)");
            BenchmarkBig(big);
        }

        Console.WriteLine();
        Print();
    }

    private static void BenchmarkBig(string path) {
        using var archive = NpkArchive.Open(path);
        Console.WriteLine($"  条目数：{archive.Entries.Count}");

        Run("  Open only", 20, () => {
            using var a = NpkArchive.Open(path);
        });

        // 取最大的 20 个条目，逐个解析索引 + 解码首帧
        var biggest = archive.Entries.OrderByDescending(e => e.Length).Take(20).ToArray();
        Run("  LoadImg × 20 (biggest)", 10, () => {
            foreach (var e in biggest) {
                try {
                    _ = archive.LoadImg(e);
                } catch {
                    // 忽略不支持的版本
                }
            }
        });
    }

    /// <summary>打开一个真实大包并逐个解析全部条目，测"超大单包"场景的耗时与峰值内存。</summary>
    private static void ScanAll(string path) {
        var fileSize = new FileInfo(path).Length;
        Console.WriteLine($"样本 {Path.GetFileName(path)}  {fileSize / 1024.0 / 1024.0:F1} MB");

        var sw = Stopwatch.StartNew();
        using var archive = NpkArchive.Open(path);
        var openMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var ok = 0;
        var fail = 0;
        long bytes = 0;
        foreach (var entry in archive.Entries) {
            try {
                var img = archive.LoadImg(entry);
                bytes += entry.Length;
                ok++;
                _ = img.Frames.Count;
            } catch {
                fail++;
            }
        }

        sw.Stop();
        var proc = Process.GetCurrentProcess();
        proc.Refresh();

        Console.WriteLine($"条目 {archive.Entries.Count}（成功 {ok} / 失败 {fail}），解析数据 {bytes / 1024.0 / 1024.0:F1} MB");
        Console.WriteLine($"打开索引 {openMs:F2} ms，全量 LoadImg {sw.Elapsed.TotalMilliseconds:F0} ms");
        Console.WriteLine($"峰值工作集 {proc.PeakWorkingSet64 / 1024.0 / 1024.0:F0} MB，当前托管堆 {GC.GetTotalMemory(false) / 1024.0 / 1024.0:F1} MB");
        Console.WriteLine($"GC: Gen0 {GC.CollectionCount(0)} / Gen1 {GC.CollectionCount(1)} / Gen2 {GC.CollectionCount(2)}");
    }

    /// <summary>
    ///     长期运行稳定性：反复打开 → 解析 → 解码 → 释放，观察托管堆与工作集是否单调增长。
    ///     若每轮结束后托管堆回到同一水平，说明没有（托管侧）泄漏。
    /// </summary>
    private static void Stress(string sample) {
        Console.WriteLine("轮次  托管堆    工作集    句柄数    Gen0  Gen1  Gen2");
        Console.WriteLine(new string('-', 62));

        for (var round = 0; round < 20; round++) {
            for (var i = 0; i < 50; i++) {
                using var archive = NpkArchive.Open(sample);
                var img = archive.LoadImg(archive.Entries[0]);
                foreach (var frame in img.Frames) {
                    _ = img.Decode(frame);
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var proc = Process.GetCurrentProcess();
            proc.Refresh();
            Console.WriteLine(
                $"{round,4}  {GC.GetTotalMemory(false) / 1024,7} KB  {proc.WorkingSet64 / 1024 / 1024,6} MB  " +
                $"{proc.HandleCount,6}  {GC.CollectionCount(0),5} {GC.CollectionCount(1),5} {GC.CollectionCount(2),5}");
        }
    }

    private static void Run(string name, int iterations, Action action) {
        // 预热，让 JIT 完成分层编译
        for (var i = 0; i < Math.Min(5, iterations); i++) action();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++) action();

        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Rows.Add(new Row(
            name,
            iterations,
            sw.Elapsed.TotalMilliseconds / iterations,
            allocated / (double)iterations,
            GC.CollectionCount(0) - gen0,
            GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2));
    }

    private static void Print() {
        Console.WriteLine("{0,-34}{1,7}{2,12}{3,12}{4,7}{5,7}{6,7}",
            "操作", "迭代", "平均耗时", "每次分配", "Gen0", "Gen1", "Gen2");
        Console.WriteLine(new string('-', 96));
        foreach (var r in Rows) {
            Console.WriteLine($"{r.Name,-34}{r.Iterations,7}{r.MeanMs,10:F3} ms{r.AllocBytes,10:F0} B{r.Gen0,7}{r.Gen1,7}{r.Gen2,7}");
        }

        Console.WriteLine(new string('-', 96));
        Console.WriteLine($"进程峰值工作集：{Process.GetCurrentProcess().PeakWorkingSet64 / 1024.0 / 1024.0:F1} MB");
    }

    private sealed record Row(string Name, int Iterations, double MeanMs, double AllocBytes, int Gen0, int Gen1, int Gen2);
}
