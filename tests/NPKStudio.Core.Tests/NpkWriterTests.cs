using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Formats.Npk;
using NPKStudio.TestSupport;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     <see cref="NpkWriter" /> 的构建与自检测试。
///     <para>
///         最强的一条是 <see cref="RebuildSample_IsByteIdentical" />：把合成样本的全部条目
///         （路径 + 原始字节）用本类重建，断言产物与原文件<b>逐字节相同</b>。
///         这一条同时证明偏移累加、路径加密（含 0x00 填充参与异或）、SHA256 的 <c>len/17*17</c> 截断、
///         数据区顺序布局全部正确——任何一处偏一字节都会红。
///     </para>
/// </summary>
[TestClass]
public sealed class NpkWriterTests
{
    private static string SamplePath => SampleFiles.SyntheticSamplePath;

    /// <summary>把 (路径, 字节) 列表转成条目源（旧的 <c>NpkWriter.Write(Stream, (string, byte[])[])</c> 已删除）。</summary>
    private static NpkEntrySource[] Sources(IReadOnlyList<(string Path, byte[] Data)> entries)
    {
        var sources = new NpkEntrySource[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            sources[i] = NpkEntrySource.FromBytes(entries[i].Path, entries[i].Data);
        }

        return sources;
    }

    [TestMethod]
    public void RebuildSample_IsByteIdentical()
    {
        if (!SampleFiles.SyntheticSampleAvailable)
        {
            return;
        }

        var original = File.ReadAllBytes(SamplePath);

        var entries = new List<(string Path, byte[] Data)>();
        using (var archive = NpkArchive.Open(SamplePath))
        {
            foreach (var entry in archive.Entries)
            {
                entries.Add((entry.Path, archive.ReadRaw(entry)));
            }
        }

        Assert.IsTrue(entries.Count > 1, "样本应包含多个条目，否则偏移累加没有被覆盖。");

        using var stream = new MemoryStream();
        NpkWriter.WriteStreaming(stream, Sources(entries));
        var rebuilt = stream.ToArray();

        Assert.AreEqual(original.Length, rebuilt.Length, "重建产物长度必须等于原包长度。");
        CollectionAssert.AreEqual(original, rebuilt, "重建产物必须与原包逐字节完全一致。");
    }

    [TestMethod]
    public void SingleEntry_WriteThenRead_RoundTrips()
    {
        const string path = "sprite/interface/emoticon/test.img";
        var data = new byte[300];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 7);
        }

        var temp = Path.Combine(Path.GetTempPath(), $"es_single_{Guid.NewGuid():N}.NPK");
        try
        {
            using (var stream = new MemoryStream())
            {
                NpkWriter.WriteStreaming(stream, Sources([(path, data)]));
                var bytes = stream.ToArray();
                Assert.AreEqual(NpkArchive.DataStartBase + NpkArchive.EntrySize + data.Length, bytes.Length);
                File.WriteAllBytes(temp, bytes);
            }

            using var archive = NpkArchive.Open(temp);
            Assert.AreEqual(1, archive.Entries.Count);
            Assert.AreEqual(path, archive.Entries[0].Path);
            Assert.AreEqual(data.Length, archive.Entries[0].Length);
            Assert.AreEqual(NpkArchive.DataStartBase + NpkArchive.EntrySize, archive.Entries[0].Offset);
            CollectionAssert.AreEqual(data, archive.ReadRaw(archive.Entries[0]));
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    [TestMethod]
    public void WriteFile_ProducesReadablePackAndCleansTemp()
    {
        var target = Path.Combine(Path.GetTempPath(), $"es_writer_{Guid.NewGuid():N}.NPK");
        try
        {
            var entries = new List<(string Path, byte[] Data)>
            {
                ("a/one.img", new byte[] { 1, 2, 3 }),
                ("a/two.img", new byte[] { 4, 5, 6, 7 })
            };

            NpkWriter.WriteFileStreaming(target, Sources(entries));

            Assert.IsTrue(File.Exists(target));
            Assert.IsFalse(File.Exists(target + ".saving"), "临时文件必须被清理（自检通过后原子替换）。");

            // 52 + 2*264 + 3 + 4
            Assert.AreEqual(52 + (2 * 264) + 7, new FileInfo(target).Length);

            using var archive = NpkArchive.Open(target);
            Assert.AreEqual(2, archive.Entries.Count);
            Assert.AreEqual("a/one.img", archive.Entries[0].Path);
            CollectionAssert.AreEqual(new byte[] { 4, 5, 6, 7 }, archive.ReadRaw(archive.Entries[1]));
        }
        finally
        {
            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
    }

    [TestMethod]
    public void EmptyPack_IsReadableWithZeroEntries()
    {
        using var stream = new MemoryStream();
        NpkWriter.WriteStreaming(stream, []);
        var bytes = stream.ToArray();

        Assert.AreEqual((long)NpkArchive.DataStartBase, bytes.Length, "空包 = 条目表前缀 20 + 校验码 32 = 52 字节。");

        var temp = Path.Combine(Path.GetTempPath(), $"es_empty_{Guid.NewGuid():N}.NPK");
        try
        {
            File.WriteAllBytes(temp, bytes);
            using var archive = NpkArchive.Open(temp);
            Assert.AreEqual(0, archive.Entries.Count);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    [TestMethod]
    public void DuplicatePath_Throws()
    {
        var entries = new List<(string Path, byte[] Data)>
        {
            ("a/b.img", new byte[] { 1 }),
            ("a/b.img", new byte[] { 2 })
        };

        try
        {
            NpkWriter.WriteStreaming(new MemoryStream(), Sources(entries));
            Assert.Fail("重复路径应抛 ArgumentException。");
        }
        catch (ArgumentException ex)
        {
            StringAssert.Contains(ex.Message, "重复");
        }
    }

    [TestMethod]
    public void DuplicatePathIgnoringCase_Throws()
    {
        // Windows 上 "a/b.img" 与 "A/B.img" 会落到同一文件，等同重复
        var entries = new List<(string Path, byte[] Data)>
        {
            ("sprite/a.img", new byte[] { 1 }),
            ("SPRITE/A.IMG", new byte[] { 2 })
        };

        try
        {
            NpkWriter.WriteStreaming(new MemoryStream(), Sources(entries));
            Assert.Fail("仅大小写不同的路径应视为重复并抛 ArgumentException。");
        }
        catch (ArgumentException)
        {
            // 期望路径
        }
    }

    [TestMethod]
    public void EmptyPath_Throws()
    {
        try
        {
            NpkWriter.WriteStreaming(new MemoryStream(), Sources([("", new byte[] { 1 })]));
            Assert.Fail("空路径应抛 ArgumentException。");
        }
        catch (ArgumentException)
        {
            // 期望路径
        }
    }

    [TestMethod]
    public void TooLongPath_Throws()
    {
        var tooLong = new string('a', 256);
        try
        {
            NpkWriter.WriteStreaming(new MemoryStream(), Sources([(tooLong, new byte[] { 1 })]));
            Assert.Fail("路径超过 255 字节应抛 ArgumentException。");
        }
        catch (ArgumentException)
        {
            // 期望路径
        }
    }

    [TestMethod]
    public void NullOrEmptyData_Throws()
    {
        try
        {
            NpkWriter.WriteStreaming(new MemoryStream(), Sources([("a/b.img", null!)]));
            Assert.Fail("null 数据应抛 ArgumentException。");
        }
        catch (ArgumentException)
        {
            // 期望路径
        }

        try
        {
            NpkWriter.WriteStreaming(new MemoryStream(), Sources([("a/b.img", Array.Empty<byte>())]));
            Assert.Fail("空数据应抛 ArgumentException。");
        }
        catch (ArgumentException)
        {
            // 期望路径
        }
    }

    [TestMethod]
    public void EncodedPath_IsXorRoundTrip()
    {
        const string path = "sprite/interface/emoticon/against.img";

        var encrypted = NpkCipher.EncryptPath(path);
        Assert.AreEqual(NpkCipher.PathLength, encrypted.Length);
        Assert.AreEqual(path, NpkCipher.DecryptPath(encrypted));
        CollectionAssert.AreEqual(encrypted, NpkCipher.EncodePath(path), "EncryptPath 必须是 EncodePath 的别名。");
    }
}
