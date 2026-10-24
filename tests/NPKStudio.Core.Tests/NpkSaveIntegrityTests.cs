using NPKStudio.Core.Formats;
using NPKStudio.Core.Formats.Npk;
using NPKStudio.Core.Imaging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     保存完整性：已修改的条目不能被缓存淘汰丢弃。
///     <para>
///         <see cref="NpkImageCache" /> 容量为 8。若淘汰不区分是否已修改，被挤出缓存的编辑在保存时
///         会退回"按原字节直拷"，而 <c>Save</c> 仍返回成功，修改就此消失。
///         本文件锁定的契约是：脏条目永不淘汰；脏条目确实丢失时保存必须失败，不得产出内容缺失的文件。
///     </para>
///     <para>
///         触发条件：编辑第 1 张图，再浏览 8 张条目，然后保存。
///     </para>
/// </summary>
[TestClass]
public sealed class NpkSaveIntegrityTests
{
    /// <summary>条目数刻意大于 <see cref="NpkArchive.ImgCacheCapacity" />，保证"编辑 + 浏览"能把缓存灌满。</summary>
    private const int EntryCount = 12;

    /// <summary>编辑后打开足够多的其它条目 → 另存 → 重新打开，像素必须还是改后的值。</summary>
    [TestMethod]
    public void Save_KeepsEdit_WhenOtherEntriesFillTheCache()
    {
        using var scope = new TempPackage(EntryCount);
        var blue = Solid(0, 0, 255);

        using (var archive = NpkArchive.Open(scope.SourcePath))
        {
            var target = archive.Entries[0];
            var img = archive.LoadImg(target);
            Assert.AreEqual(255, img.Decode(img.Frames[0])!.GetPixel(0, 0).R, "初始应为红色。");

            img.ReplaceFrameImage(img.Frames[0], blue, ColorBits.UNKNOWN);
            Assert.IsTrue(target.IsModified, "ReplaceFrameImage 必须把条目标脏。");

            // 依次打开其余条目：超过容量 8 之后，缓存开始淘汰——但绝不该淘汰刚编辑过的那一条。
            for (var i = 1; i < archive.Entries.Count; i++)
            {
                archive.LoadImg(archive.Entries[i]);
            }

            archive.Save(scope.OutputPath);
        }

        using var saved = NpkArchive.Open(scope.OutputPath);
        var reread = saved.LoadImg(saved.Entries[0]);
        var pixel = reread.Decode(reread.Frames[0])!.GetPixel(0, 0);

        Assert.AreEqual(0, pixel.R, "红色分量应被改成 0（若为 255 说明编辑被静默丢弃）。");
        Assert.AreEqual(0, pixel.G);
        Assert.AreEqual(255, pixel.B, "蓝色分量应为 255。");
    }

    /// <summary>
    ///     脏条目在缓存里必须<b>保持同一个实例</b>：换实例就意味着重新解析过，
    ///     编辑内容已经丢了（这条断言直接盯住"淘汰"这个动作本身）。
    /// </summary>
    [TestMethod]
    public void Cache_NeverEvictsModifiedEntries()
    {
        using var scope = new TempPackage(EntryCount);
        using var archive = NpkArchive.Open(scope.SourcePath);

        var edited = archive.Entries[0];
        var original = archive.LoadImg(edited);
        original.ReplaceFrameImage(original.Frames[0], Solid(0, 0, 255), ColorBits.UNKNOWN);

        for (var i = 1; i < archive.Entries.Count; i++)
        {
            archive.LoadImg(archive.Entries[i]);
        }

        var again = archive.LoadImg(edited);
        Assert.AreSame(original, again, "已修改条目被淘汰后重新解析了，编辑必然丢失。");
        Assert.AreEqual(255, again.Decode(again.Frames[0])!.GetPixel(0, 0).B);
    }

    /// <summary>
    ///     多个条目都被编辑时同样都不能淘汰（缓存里可能全是脏条目，此时宁可不淘汰）。
    /// </summary>
    [TestMethod]
    public void Cache_KeepsEveryModifiedEntry_EvenWhenTheyExceedCapacity()
    {
        using var scope = new TempPackage(EntryCount);
        using var archive = NpkArchive.Open(scope.SourcePath);

        var capacity = NpkArchive.ImgCacheCapacity;
        var loaded = new List<(NpkEntry Entry, ImgFile Image)>();
        for (var i = 0; i <= capacity; i++)
        {
            var entry = archive.Entries[i];
            var img = archive.LoadImg(entry);
            img.ReplaceFrameImage(img.Frames[0], Solid(0, 0, 255), ColorBits.UNKNOWN);
            loaded.Add((entry, img));
        }

        // 再灌一批未修改的条目，制造淘汰压力
        for (var i = capacity + 1; i < archive.Entries.Count; i++)
        {
            archive.LoadImg(archive.Entries[i]);
        }

        foreach (var (entry, image) in loaded)
        {
            Assert.AreSame(image, archive.LoadImg(entry), $"条目 \"{entry.Path}\" 的编辑被淘汰了。");
        }
    }

    /// <summary>生成一张纯色 BGRA 图。</summary>
    private static RgbaImage Solid(byte r, byte g, byte b)
    {
        var image = new RgbaImage(4, 4);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                image.SetPixel(x, y, new Color32(r, g, b, 255));
            }
        }

        return image;
    }

    /// <summary>临时 NPK：<paramref name="entryCount" /> 个 4×4 纯红 v2 条目。</summary>
    private sealed class TempPackage : IDisposable
    {
        private readonly string _directory;

        public TempPackage(int entryCount)
        {
            _directory = Path.Combine(Path.GetTempPath(), "npkintegrity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            SourcePath = Path.Combine(_directory, "source.npk");
            OutputPath = Path.Combine(_directory, "saved.npk");

            var sources = new NpkEntrySource[entryCount];
            for (var i = 0; i < entryCount; i++)
            {
                sources[i] = NpkEntrySource.FromBytes($"sprite/test/e{i:D2}.img", BuildImg());
            }

            NpkWriter.WriteFileStreaming(SourcePath, sources);
        }

        public string SourcePath { get; }

        public string OutputPath { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响断言结果
            }
        }

        private static byte[] BuildImg()
        {
            var image = ImgFile.Create(ImgVersion.Ver2, "e.img");
            var frame = image.AddFrame();
            image.ReplaceFrameImage(frame, Solid(255, 0, 0), ColorBits.UNKNOWN);
            return image.ToArray();
        }
    }
}
