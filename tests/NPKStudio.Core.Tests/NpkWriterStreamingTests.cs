using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Formats;
using NPKStudio.Core.Formats.Npk;
using NPKStudio.TestSupport;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     <see cref="NpkWriter.WriteStreaming" /> / <see cref="NpkArchive.CopyRawTo" /> 的等价性测试。
///     <para>
///         核心不变式：<b>流式产物必须与"数据全在内存"的老路径逐字节一致</b>，
///         否则性能优化就成了行为变更。
///     </para>
///     <para>
///         基准包是仓库自带的合成样本（见 <see cref="SampleFiles.SyntheticSamplePath" />），不依赖真实客户端资源。
///     </para>
/// </summary>
[TestClass]
public sealed class NpkWriterStreamingTests
{
    private static string SamplePath => SampleFiles.SyntheticSamplePath;

    [TestMethod]
    public void WriteStreaming_Sample_IsByteIdenticalToOriginal()
    {
        if (!SampleFiles.SyntheticSampleAvailable)
        {
            return;
        }

        var original = File.ReadAllBytes(SamplePath);

        var sources = new List<NpkEntrySource>();
        byte[][] payloads;
        using (var archive = NpkArchive.Open(SamplePath))
        {
            payloads = new byte[archive.Entries.Count][];
            for (var i = 0; i < archive.Entries.Count; i++)
            {
                payloads[i] = archive.ReadRaw(archive.Entries[i]);
                // 用"从归档分块拷"这条最接近真实打包链路的路径
                sources.Add(NpkEntrySource.FromArchive(archive, archive.Entries[i]));
            }

            using var stream = new MemoryStream();
            NpkWriter.WriteStreaming(stream, sources);
            var rebuilt = stream.ToArray();

            Assert.AreEqual(original.Length, rebuilt.Length);
            CollectionAssert.AreEqual(original, rebuilt, "流式写出的产物必须与原包逐字节一致。");
        }

        // 再用内存字节做一次，验证两条供应路径产物相同
        var byteSources = new List<NpkEntrySource>();
        using (var archive = NpkArchive.Open(SamplePath))
        {
            for (var i = 0; i < archive.Entries.Count; i++)
            {
                byteSources.Add(NpkEntrySource.FromBytes(archive.Entries[i].Path, payloads[i]));
            }
        }

        using var stream2 = new MemoryStream();
        NpkWriter.WriteStreaming(stream2, byteSources);
        CollectionAssert.AreEqual(original, stream2.ToArray(), "FromBytes 与 FromArchive 的产物必须一致。");
    }

    [TestMethod]
    public void WriteStreaming_EqualsInMemoryWrite_ForMixedEntries()
    {
        var entries = new List<(string Path, byte[] Data)>
        {
            ("a/one.img", BuildBytes(100, 1)),
            ("a/two.img", BuildBytes(70000, 2)),
            ("b/three.img", BuildBytes(3, 3))
        };

        using var inMemory = new MemoryStream();
        NpkWriter.WriteStreaming(inMemory, entries.Select(e => NpkEntrySource.FromBytes(e.Path, e.Data)).ToList());

        var sources = entries.Select(e => NpkEntrySource.FromBytes(e.Path, e.Data)).ToList();
        using var streaming = new MemoryStream();
        NpkWriter.WriteStreaming(streaming, sources);

        CollectionAssert.AreEqual(inMemory.ToArray(), streaming.ToArray(), "流式与内存路径必须产出相同字节。");
    }

    [TestMethod]
    public void CopyRawTo_MatchesReadRaw_ForEveryEntry()
    {
        if (!SampleFiles.SyntheticSampleAvailable)
        {
            return;
        }

        using var archive = NpkArchive.Open(SamplePath);

        foreach (var entry in archive.Entries)
        {
            using var stream = new MemoryStream();
            archive.CopyRawTo(entry, stream);

            CollectionAssert.AreEqual(archive.ReadRaw(entry), stream.ToArray(), $"条目 \"{entry.Path}\" 分块拷贝结果不一致。");
        }
    }

    [TestMethod]
    public void WriteStreaming_DetectsWrongByteCount()
    {
        // 声明 100 字节，实际只写 50 字节 → 必须被检出（否则产出的包是坏的）
        var broken = new RawSource("a/broken.img", 100, new byte[50]);

        try
        {
            NpkWriter.WriteStreaming(new MemoryStream(), [broken]);
            Assert.Fail("实际写入字节数与声明长度不符时应抛 NpkFormatException。");
        }
        catch (NpkFormatException ex)
        {
            StringAssert.Contains(ex.Message, "a/broken.img");
        }
    }

    [TestMethod]
    public void WriteStreaming_ValidatesLikeInMemoryWrite()
    {
        // 空路径
        try
        {
            NpkWriter.WriteStreaming(new MemoryStream(), [Source("", 4)]);
            Assert.Fail("空路径应抛 ArgumentException。");
        }
        catch (ArgumentException)
        {
            // 期望
        }

        // 重复路径
        try
        {
            NpkWriter.WriteStreaming(new MemoryStream(), [Source("a/b.img", 4), Source("A/B.IMG", 4)]);
            Assert.Fail("重复路径应抛 ArgumentException。");
        }
        catch (ArgumentException)
        {
            // 期望
        }

        // 长度非法
        try
        {
            NpkWriter.WriteStreaming(new MemoryStream(), [Source("a/b.img", 0)]);
            Assert.Fail("长度 0 应抛 ArgumentException。");
        }
        catch (ArgumentException)
        {
            // 期望
        }
    }

    [TestMethod]
    public void WriteFileStreaming_ProducesReadablePack()
    {
        var target = Path.Combine(Path.GetTempPath(), $"es_stream_{Guid.NewGuid():N}.NPK");
        try
        {
            var workFile = Path.Combine(Path.GetTempPath(), $"es_stream_src_{Guid.NewGuid():N}.img");
            File.WriteAllBytes(workFile, BuildBytes(4096, 9));
            try
            {
                var sources = new List<NpkEntrySource>
                {
                    NpkEntrySource.FromFile("a/from-file.img", workFile),
                    NpkEntrySource.FromBytes("a/from-bytes.img", BuildBytes(10, 5))
                };

                NpkWriter.WriteFileStreaming(target, sources);

                Assert.IsTrue(File.Exists(target));
                Assert.IsFalse(File.Exists(target + ".saving"), "临时文件必须被清理。");

                using var archive = NpkArchive.Open(target);
                Assert.AreEqual(2, archive.Entries.Count);
                Assert.AreEqual(4096, archive.Entries[0].Length);
                Assert.AreEqual(10, archive.Entries[1].Length);
                CollectionAssert.AreEqual(BuildBytes(4096, 9), archive.ReadRaw(archive.Entries[0]));
            }
            finally
            {
                if (File.Exists(workFile))
                {
                    File.Delete(workFile);
                }
            }
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
    public void EncodePath_SpanOverload_MatchesArrayOverload()
    {
        const string path = "sprite/interface/emoticon/against.img";

        var expected = NpkCipher.EncodePath(path);
        var actual = new byte[NpkCipher.PathLength];
        NpkCipher.EncodePath(path, actual);

        CollectionAssert.AreEqual(expected, actual);

        // 目标跨度带前缀时只写指定区间，不越界
        var padded = new byte[NpkCipher.PathLength + 8];
        padded.AsSpan().Fill(0xEE);
        NpkCipher.EncodePath(path, padded.AsSpan(8));
        CollectionAssert.AreEqual(expected, padded[8..]);
        Assert.AreEqual((byte)0xEE, padded[0], "前缀区域不得被改写。");
    }

    [TestMethod]
    public void ComputeHash_SpanOverload_MatchesArrayOverload()
    {
        var table = BuildBytes(11108, 42);

        var expected = NpkCipher.ComputeHash(table);
        var actual = new byte[NpkCipher.HashSize];
        NpkCipher.ComputeHash(table, actual);

        CollectionAssert.AreEqual(expected, actual);
        Assert.AreEqual(NpkCipher.HashSize, expected.Length);
    }

    private static NpkEntrySource Source(string path, int length)
    {
        return new RawSource(path, length, new byte[length]);
    }

    /// <summary>
    ///     测试专用条目源：声明长度与实际写入内容可分别指定（用于构造"长度不符"的坏条目）。
    /// </summary>
    private sealed class RawSource(string path, int length, byte[] payload) : NpkEntrySource(path, length)
    {
        public override void WriteData(Stream destination)
        {
            destination.Write(payload, 0, payload.Length);
        }
    }

    private static byte[] BuildBytes(int length, byte seed)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)((i * 31 + seed) & 0xFF);
        }

        return data;
    }
}
