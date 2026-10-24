using NPKStudio.Core.Formats.Npk;
using NPKStudio.Desktop.Services;
using NPKStudio.Desktop.ViewModels;

using Xunit;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     打包链路的端到端证据：
///     <list type="number">
///         <item>清单可以从客户端已有的 NPK 目录反向生成，行内容与包内条目路径逐条一致；</item>
///         <item>没有清单也能导出 NPK，包内路径按输出文件名推断，且重新读回后数据与源文件逐字节一致；</item>
///         <item>单个 <c>.img</c> 走文件夹模式，导出默认名即 img 名。</item>
///     </list>
///     <para>每个用例自建临时目录并自行清理，互不依赖执行顺序。</para>
/// </summary>
public sealed class NpkPackServiceTests
{
    /// <summary>造一个含若干条目的 NPK（数据用可辨识的字节，便于逐字节比对）。</summary>
    private static void WriteNpk(string path, params string[] entryPaths)
    {
        var sources = new List<NpkEntrySource>(entryPaths.Length);
        for (var i = 0; i < entryPaths.Length; i++)
        {
            var data = new byte[64 + i];
            for (var k = 0; k < data.Length; k++)
            {
                data[k] = (byte)((i * 37) + k);
            }

            sources.Add(NpkEntrySource.FromBytes(entryPaths[i], data));
        }

        NpkWriter.WriteFileStreaming(path, sources);
    }

    private static string CreateTempDirectory(string tag) =>
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "npkstudio-" + tag + "-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    ///     清单生成：扫一个装有 2 个 NPK 的目录，产出的 <c>IMG:</c> 行必须与包内条目路径完全一致
    ///     ——这是"清单从哪来"的答案，不需要 PVF。
    /// </summary>
    [Fact]
    public void GenerateManifest_FromNpkFolder_WritesEveryEntryPath()
    {
        var packs = CreateTempDirectory("packs");
        var outDir = CreateTempDirectory("out");
        try
        {
            var first = new[] { "sprite/character/priest/effect/a.img", "sprite/character/priest/effect/b.img" };
            var second = new[] { "sprite/character/swordman/effect/c.img" };
            WriteNpk(Path.Combine(packs, "sprite_character_priest_effect.NPK"), first);
            WriteNpk(Path.Combine(packs, "sprite_character_swordman_effect.NPK"), second);

            var manifestPath = Path.Combine(outDir, "missing_img_details.txt");
            var result = ResourceManifestWriter.Write(packs, manifestPath);

            Assert.Equal(2, result.NpkCount);
            Assert.Equal(3, result.PathCount);
            Assert.Equal(0, result.FailedCount);

            var lines = File.ReadAllLines(manifestPath)
                .Where(l => l.StartsWith("IMG: ", StringComparison.Ordinal))
                .Select(l => l[5..])
                .ToArray();

            Assert.Equal(first.Concat(second).OrderBy(p => p, StringComparer.Ordinal),
                lines.OrderBy(p => p, StringComparer.Ordinal));

            // 生成的清单必须能被 ResourceManifest 原样吃回去（写与读同一口径）
            var manifest = ResourceManifest.Load(manifestPath);
            Assert.Equal(3, manifest.Count);
            Assert.True(manifest.TryResolve("sprite_character_priest_effect", "a.img", out var resolved));
            Assert.Equal("sprite/character/priest/effect/a.img", resolved);
        }
        finally
        {
            Directory.Delete(packs, recursive: true);
            Directory.Delete(outDir, recursive: true);
        }
    }

    /// <summary>
    ///     无清单导出（端到端）：临时文件夹塞 3 个 <c>.img</c> → 导出成 <c>MyMod.NPK</c> →
    ///     重新 <see cref="NpkArchive.Open" /> 读回，断言条目数、路径、以及每个条目的字节与源文件一致。
    /// </summary>
    [Fact]
    public async Task ExportWithoutManifest_InfersPaths_AndPreservesBytes()
    {
        var source = CreateTempDirectory("src");
        var outDir = CreateTempDirectory("out");
        try
        {
            var names = new[] { "a.img", "b.img", "c.img" };
            var payloads = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < names.Length; i++)
            {
                var bytes = new byte[128 + (i * 11)];
                for (var k = 0; k < bytes.Length; k++)
                {
                    bytes[k] = (byte)((i * 53) + k);
                }

                File.WriteAllBytes(Path.Combine(source, names[i]), bytes);
                payloads[names[i]] = bytes;
            }

            var folder = FolderEntrySource.Open(source);
            var outputPath = Path.Combine(outDir, "MyMod.NPK");

            var result = await new FileWorkspace().ExportFolderToNpkAsync(folder, null, outputPath);

            Assert.True(result.Success, result.StatusMessage);

            using var archive = NpkArchive.Open(outputPath);
            Assert.Equal(names.Length, archive.Entries.Count);

            foreach (var entry in archive.Entries)
            {
                Assert.Equal("sprite/MyMod/" + entry.Name, entry.Path);
                Assert.True(payloads.TryGetValue(entry.Name, out var expected));
                Assert.Equal(expected!, archive.ReadRaw(entry));
            }
        }
        finally
        {
            Directory.Delete(source, recursive: true);
            Directory.Delete(outDir, recursive: true);
        }
    }

    /// <summary>
    ///     输出文件名符合 DNF 规则时（<c>sprite_character_priest_effect.NPK</c>），
    ///     推断出的包内目录就是 <c>sprite/character/priest/effect</c>。
    /// </summary>
    [Fact]
    public void PackOne_WithDnfStyleStem_ProducesNestedDirectory()
    {
        var source = CreateTempDirectory("src");
        var outDir = CreateTempDirectory("out");
        try
        {
            File.WriteAllBytes(Path.Combine(source, "a.img"), [1, 2, 3, 4]);
            File.WriteAllBytes(Path.Combine(source, "b.img"), [5, 6, 7]);

            var outcome = NpkPackService.PackOne(source, null, outDir, null, "sprite_character_priest_effect");

            Assert.True(outcome.Success, outcome.Error);
            Assert.Equal("sprite_character_priest_effect.NPK", outcome.NpkName);
            Assert.Equal(2, outcome.EntryCount);

            using var archive = NpkArchive.Open(Path.Combine(outDir, outcome.NpkName));
            Assert.Equal(
                ["sprite/character/priest/effect/a.img", "sprite/character/priest/effect/b.img"],
                archive.Entries.Select(e => e.Path).OrderBy(p => p, StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
            Directory.Delete(outDir, recursive: true);
        }
    }

    /// <summary>有清单时以清单为准，推断只用于清单里查不到的条目。</summary>
    [Fact]
    public void PackOne_WithManifest_PrefersManifestPaths()
    {
        var root = CreateTempDirectory("root");
        var outDir = CreateTempDirectory("out");
        var manifestFile = Path.Combine(outDir, "m.txt");
        try
        {
            // 文件夹名与清单里的目录对应（清单 key = 路径目录去 sprite/ 前缀后 _ 连接）
            var source = Directory.CreateDirectory(Path.Combine(root, "sprite_character_priest_effect")).FullName;
            File.WriteAllBytes(Path.Combine(source, "a.img"), [1, 2, 3, 4]);
            File.WriteAllBytes(Path.Combine(source, "b.img"), [5, 6, 7]);

            File.WriteAllLines(manifestFile,
                ["IMG: sprite/character/priest/effect/a.img", "IMG: sprite/character/priest/effect/b.img"]);

            // 即便推断用的名字是 atpriest（那个实测反例），有清单时也必须听清单的
            var outcome = NpkPackService.PackOne(
                source, ResourceManifest.Load(manifestFile), outDir, null, "sprite_character_atpriest_effect");

            Assert.True(outcome.Success, outcome.Error);
            Assert.Equal("sprite_character_priest_effect.NPK", outcome.NpkName);

            using var archive = NpkArchive.Open(Path.Combine(outDir, outcome.NpkName));
            Assert.Equal(
                ["sprite/character/priest/effect/a.img", "sprite/character/priest/effect/b.img"],
                archive.Entries.Select(e => e.Path).OrderBy(p => p, StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outDir, recursive: true);
        }
    }

    /// <summary>单个 <c>.img</c> 走文件夹模式：一个条目、名字即 img 主干（导出默认名就来自它）。</summary>
    [Fact]
    public void OpenSingleFile_TreatsOneImgAsFolderSource()
    {
        var source = CreateTempDirectory("src");
        try
        {
            var file = Path.Combine(source, "icon.img");
            File.WriteAllBytes(file, [9, 8, 7]);

            var folder = FolderEntrySource.OpenSingleFile(file);

            Assert.Equal(source, folder.RootPath);
            Assert.Equal("icon", folder.Name);
            Assert.Single(folder.Entries);
            Assert.Equal("icon.img", folder.Entries[0].Path);
            Assert.True(folder.Entries[0].IsFileEntry);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    /// <summary>打开单个 <c>.img</c> 不再落到 NPK 解析（此前会直接抛格式异常）。</summary>
    [Fact]
    public async Task DocumentSession_OpenSingleImg_UsesFolderMode()
    {
        var source = CreateTempDirectory("src");
        try
        {
            var file = Path.Combine(source, "icon.img");
            File.WriteAllBytes(file, [9, 8, 7]);

            using var session = new DocumentSession();
            var entries = await session.OpenAsync(file);

            Assert.Single(entries);
            Assert.Null(session.Archive);
            Assert.NotNull(session.Folder);
            Assert.Equal("icon", session.FileName);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }
}
