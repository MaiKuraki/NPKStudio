using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Formats;
using NPKStudio.Core.Formats.Npk;
using NPKStudio.TestSupport;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     NPK 容器的集成测试。
///     <para>
///         <b>两类基准</b>：<see cref="SampleFiles.SyntheticSamplePath" />（仓库自带，21 个几何 IMG、全 v2，
///         随 MIT 提交）承担容器结构、读取、另存、截断等通用断言；
///         <see cref="SampleFiles.RealSamplePath" />（开发者自备，不入库）只用于"某份特定真实包的字节快照"——
///         条目表偏移、首帧几何、校验码位置这些数值只对那一份文件成立。
///     </para>
///     <para>
///         真实样本缺失时，快照用例在方法开头静默返回（xUnit 2 无 <c>Assert.Skip</c>，
///         这里的 MSTest 同样没有等价物；返回前不写任何输出）。
///     </para>
/// </summary>
[TestClass]
public sealed class NpkRoundTripTests
{
    private static string SamplePath => SampleFiles.SyntheticSamplePath;

    private static string RealSamplePath => SampleFiles.RealSamplePath;

    [TestMethod]
    public void Sample_EntryTable()
    {
        if (!SampleFiles.RealSampleAvailable)
        {
            // 真实客户端样本不入库（版权原因）。缺失时标记为未执行，避免把"跳过"计入通过。
            Assert.Inconclusive($"缺少 {SampleFiles.RealSampleName}：真实客户端快照用例未执行。");
        }

        using var archive = NpkArchive.Open(RealSamplePath);

        Assert.AreEqual(42, archive.Entries.Count, "样本应为 42 个条目。");
        Assert.AreEqual("sprite/interface/emoticon/against.img", archive.Entries[0].Path);
        Assert.AreEqual(11140, archive.Entries[0].Offset, "数据区起点必须等于 52 + 264 × 42。");
        Assert.AreEqual(588, archive.Entries[0].Length);
        Assert.AreEqual("against.img", archive.Entries[0].Name);
        Assert.AreEqual(80742L, archive.FileLength);

        // 条目长度之和 + 数据区起点 == 文件长度（无空洞、无重叠）
        var total = archive.Entries.Sum(e => (long)e.Length);
        Assert.AreEqual(archive.FileLength, NpkArchive.DataStartBase + (archive.Entries.Count * NpkArchive.EntrySize) + total);
    }

    [TestMethod]
    public void Sample_FirstImageHeader()
    {
        if (!SampleFiles.RealSampleAvailable)
        {
            // 真实客户端样本不入库（版权原因）。缺失时标记为未执行，避免把"跳过"计入通过。
            Assert.Inconclusive($"缺少 {SampleFiles.RealSampleName}：真实客户端快照用例未执行。");
        }

        using var archive = NpkArchive.Open(RealSamplePath);
        var entry = archive.Entries[0];

        var image = archive.LoadImg(entry);

        Assert.AreEqual(ImgVersion.Ver2, image.Version);
        Assert.AreEqual(124L, image.IndexLength, "3 个普通帧(36B) + 2 个 LINK(8B) = 124。");
        Assert.AreEqual(5, image.Frames.Count);
        Assert.AreEqual(5, entry.FrameCount, "LoadImg 应回填条目帧数。");

        var frame0 = image.Frames[0];
        Assert.AreEqual(ColorBits.ARGB_1555, frame0.Type);
        Assert.AreEqual(CompressMode.ZLIB, frame0.Compress);
        Assert.AreEqual(17, frame0.Width);
        Assert.AreEqual(17, frame0.Height);
        Assert.AreEqual(145, frame0.Length);
        Assert.AreEqual(13, frame0.X);
        Assert.AreEqual(13, frame0.Y);
        Assert.AreEqual(43, frame0.FrameWidth);
        Assert.AreEqual(43, frame0.FrameHeight);
        Assert.AreEqual(145, frame0.RawData.Length);

        // 帧 2、3 是 LINK，分别指向帧 0、帧 1
        Assert.IsTrue(image.Frames[2].IsLink);
        Assert.IsTrue(image.Frames[3].IsLink);
        Assert.AreSame(image.Frames[0], image.Frames[2].LinkTarget);
        Assert.AreSame(image.Frames[1], image.Frames[3].LinkTarget);
    }

    [TestMethod]
    public void Sample_DecodeFirstFrame_Is17x17()
    {
        if (!SampleFiles.RealSampleAvailable)
        {
            // 真实客户端样本不入库（版权原因）。缺失时标记为未执行，避免把"跳过"计入通过。
            Assert.Inconclusive($"缺少 {SampleFiles.RealSampleName}：真实客户端快照用例未执行。");
        }

        using var archive = NpkArchive.Open(RealSamplePath);
        var image = archive.LoadImg(archive.Entries[0]);

        var decoded = image.Decode(image.Frames[0]);

        Assert.IsNotNull(decoded);
        Assert.AreEqual(17, decoded.Width);
        Assert.AreEqual(17, decoded.Height);
        Assert.AreEqual(17 * 17 * 4, decoded.Pixels.Length, "解压后应为 578 字节原始数据 → 1156 字节 BGRA。");

        // 帧里应有不透明像素，且边界不应超出图像
        Assert.IsTrue(decoded.ScanBounds(out var bounds));
        Assert.IsTrue(bounds.Width <= decoded.Width && bounds.Height <= decoded.Height);
        Assert.IsTrue(bounds.Width > 0 && bounds.Height > 0);
    }

    [TestMethod]
    public void Sample_ReadRaw_MatchesEntryLength()
    {
        if (!SampleFiles.SyntheticSampleAvailable)
        {
            return;
        }

        using var archive = NpkArchive.Open(SamplePath);
        var entry = archive.Entries[0];

        var raw = archive.ReadRaw(entry);

        Assert.AreEqual(entry.Length, raw.Length);
        Assert.AreEqual("Neople Img File\0", System.Text.Encoding.Latin1.GetString(raw, 0, 16));
    }

    [TestMethod]
    public void Sample_Save_ThenReopen_IsByteIdentical()
    {
        if (!SampleFiles.SyntheticSampleAvailable)
        {
            return;
        }

        var temp = Path.Combine(Path.GetTempPath(), $"es_next_{Guid.NewGuid():N}.NPK");
        try
        {
            int entryCount;
            using (var archive = NpkArchive.Open(SamplePath))
            {
                entryCount = archive.Entries.Count;
                archive.Save(temp);
            }

            var original = File.ReadAllBytes(SamplePath);
            var saved = File.ReadAllBytes(temp);

            Assert.AreEqual(original.Length, saved.Length, "另存后的文件长度必须一致。");
            CollectionAssert.AreEqual(
                original,
                saved,
                "未修改任何条目时，另存结果应与原包逐字节一致（含条目表、SHA256 与数据区）。");

            using var reopened = NpkArchive.Open(temp);
            Assert.AreEqual(entryCount, reopened.Entries.Count);
            for (var i = 0; i < reopened.Entries.Count; i++)
            {
                Assert.IsFalse(string.IsNullOrEmpty(reopened.Entries[i].Path));
            }
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
    public void Sample_SaveToExistingPath_ReplacesAtomically()
    {
        if (!SampleFiles.SyntheticSampleAvailable)
        {
            return;
        }

        var temp = Path.Combine(Path.GetTempPath(), $"es_next_{Guid.NewGuid():N}.NPK");
        try
        {
            File.Copy(SamplePath, temp);

            int entryCount;
            int firstOffset;
            long firstLength;
            using (var archive = NpkArchive.Open(temp))
            {
                entryCount = archive.Entries.Count;
                firstOffset = archive.Entries[0].Offset;
                firstLength = archive.Entries[0].Length;
                archive.Save(temp);
            }

            Assert.IsFalse(File.Exists(temp + ".saving"), "临时文件必须被清理（原子替换后不存在）。");

            using var reopened = NpkArchive.Open(temp);
            Assert.AreEqual(entryCount, reopened.Entries.Count);
            Assert.AreEqual(firstOffset, reopened.Entries[0].Offset, "未改条目时偏移不变。");
            Assert.AreEqual(firstLength, reopened.Entries[0].Length);
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
    public void Open_NonNpkFile_ThrowsWithReadableMessage()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"es_next_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(temp, new byte[512]);

            try
            {
                using var archive = NpkArchive.Open(temp);
                Assert.Fail("魔数不符时应抛出 NpkFormatException。");
            }
            catch (NpkFormatException ex)
            {
                Assert.AreEqual("文件不是有效的 NPK 包", ex.Message);
            }
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
    public void Open_TruncatedNpk_ThrowsNpkFormatException()
    {
        if (!SampleFiles.SyntheticSampleAvailable)
        {
            return;
        }

        var temp = Path.Combine(Path.GetTempPath(), $"es_next_{Guid.NewGuid():N}.NPK");
        try
        {
            var original = File.ReadAllBytes(SamplePath);
            File.WriteAllBytes(temp, original[..20000]); // 砍掉绝大部分数据区

            try
            {
                using var archive = NpkArchive.Open(temp);
                Assert.Fail("条目越界时应抛出 NpkFormatException。");
            }
            catch (NpkFormatException)
            {
                // 期望路径
            }
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
    public void Cipher_PathRoundTrip()
    {
        const string path = "shapes/gradient_square.img";

        var encoded = NpkCipher.EncodePath(path);
        Assert.AreEqual(NpkCipher.PathLength, encoded.Length);

        var decoded = NpkCipher.DecodePath(encoded);
        Assert.AreEqual(path, decoded);
    }

    [TestMethod]
    public void Cipher_HashMatchesSample()
    {
        if (!SampleFiles.SyntheticSampleAvailable)
        {
            return;
        }

        // 校验码只对 条目表区（头部 20 字节 + 264 × 条目数）做 SHA256（长度 len/17*17）。
        var original = File.ReadAllBytes(SamplePath);
        var count = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(16));
        var tableLength = 20 + (count * NpkArchive.EntrySize);
        var storedHash = original.AsSpan(tableLength, NpkArchive.HashSize).ToArray();

        var computed = NpkCipher.ComputeHash(original.AsSpan(0, tableLength));

        CollectionAssert.AreEqual(storedHash, computed, "SHA256 截断规则必须与写出的样本一致（len/17*17，不可改成完整长度）。");
    }

    [TestMethod]
    public void Cipher_KeyLayout()
    {
        // 密钥 = "puchikon@neople dungeon and fighter "(36 字节) + 循环 "DNF" 填充到 254 + 末位 0。
        // ⚠️ FORMAT-VERIFIED.md §1 的注释写"37 字节"，实测是 36（末尾空格 + 36 = 见下）；
        //    该文档的伪代码用的是 header.Length 驱动，所以逻辑不受影响，只是注释数字偏 1。
        var header = "puchikon@neople dungeon and fighter ";
        Assert.AreEqual(36, header.Length);

        var key = NpkCipher.Key;
        Assert.AreEqual(256, key.Length);
        Assert.AreEqual(header, System.Text.Encoding.UTF8.GetString(key[..header.Length]));

        // 填充从 header.Length 开始：i = 36 → "DNF"[36 % 3] = 'D'
        Assert.AreEqual((byte)'D', key[36]);
        Assert.AreEqual((byte)'N', key[37]);
        Assert.AreEqual((byte)'F', key[38]);
        Assert.AreEqual((byte)0, key[255]);
    }
}
