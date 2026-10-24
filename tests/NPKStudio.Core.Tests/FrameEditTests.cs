using System.Buffers.Binary;

using NPKStudio.Core.Formats;
using NPKStudio.Core.Formats.Npk;
using NPKStudio.Core.Imaging;
using NPKStudio.TestSupport;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     "换图 → 存包" 闭环测试（T-A / T-B / T-C）。
///     <para>
///         核心断言是<b>未修改条目的字节必须与源文件完全相同</b>：这是"只重编码改过的条目"的证明，
///         也是 PRD Q1（未改条目原样直拷）的落地验证。
///     </para>
///     <para>
///         基准包是仓库自带的合成样本 <see cref="SampleFiles.SyntheticSamplePath" />
///         （21 个几何 IMG、全 v2），不依赖任何真实客户端资源。
///     </para>
/// </summary>
[TestClass]
public sealed class FrameEditTests
{
    /// <summary>条目 0 的规格，来自 tools/make-sample：80×80、6 帧。</summary>
    private const int GradientSquareFrames = 6;

    private static string SamplePath => SampleFiles.SyntheticSamplePath;

    [TestMethod]
    public void ReplaceFrame_Save_OnlyModifiedEntryChanges()
    {
        if (!SampleFiles.SyntheticSampleAvailable)
        {
            return;
        }

        var temp = Path.Combine(Path.GetTempPath(), $"es_edit_{Guid.NewGuid():N}.NPK");
        try
        {
            byte[][] sourcePayloads;
            int imgLength;

            using (var archive = NpkArchive.Open(SamplePath))
            {
                sourcePayloads = new byte[archive.Entries.Count][];
                for (var i = 0; i < archive.Entries.Count; i++)
                {
                    sourcePayloads[i] = archive.ReadRaw(archive.Entries[i]);
                }

                var entry = archive.Entries[0];
                var image = archive.LoadImg(entry);
                Assert.AreEqual(80, image.Frames[0].Width);

                var red = CreateSolid(20, 20, new Color32(255, 0, 0, 255));
                image.ReplaceFrameImage(image.Frames[0], red, ColorBits.ARGB_1555);

                Assert.IsTrue(entry.IsModified, "ReplaceFrameImage 必须把所属条目标记为已修改，否则 Save 会原样直拷。");
                Assert.AreEqual(20, image.Frames[0].Width);
                Assert.AreEqual(20, image.Frames[0].Height);
                Assert.AreEqual(80, image.Frames[0].FrameWidth, "帧域只增不减：20 < 80，应保持 80。");
                Assert.AreEqual(CompressMode.ZLIB, image.Frames[0].Compress);
                Assert.AreEqual(image.Frames[0].RawData.Length, image.Frames[0].Length);

                // 条目表的 Length 是"整个 IMG 的字节数"，不是单帧数据长度——两者别混淆。
                imgLength = image.ToArray().Length;

                archive.Save(temp);
            }

            FileAssert.SavedFileIsValid(temp);

            using var saved = NpkArchive.Open(temp);
            Assert.AreEqual(sourcePayloads.Length, saved.Entries.Count);
            Assert.AreEqual("shapes/gradient_square.img", saved.Entries[0].Path);

            // 1) 被改的那条：几何变成 20x20，内容解出来是纯红
            var savedEntry = saved.Entries[0];
            Assert.AreEqual(imgLength, savedEntry.Length, "条目表里的 Length 必须更新为新 IMG 的字节数。");

            var savedImage = saved.LoadImg(savedEntry);
            Assert.AreEqual(GradientSquareFrames, savedImage.Frames.Count, "帧数不变。");
            Assert.AreEqual(20, savedImage.Frames[0].Width);
            Assert.AreEqual(20, savedImage.Frames[0].Height);

            var decoded = savedImage.Decode(savedImage.Frames[0]);
            Assert.IsNotNull(decoded);
            Assert.AreEqual(20, decoded.Width);
            Assert.AreEqual(20, decoded.Height);
            Assert.AreEqual(new Color32(255, 0, 0, 255), decoded.GetPixel(0, 0));
            Assert.AreEqual(new Color32(255, 0, 0, 255), decoded.GetPixel(19, 19));

            // 2) 其余条目：与源文件逐字节相同
            for (var i = 1; i < saved.Entries.Count; i++)
            {
                var after = saved.ReadRaw(saved.Entries[i]);
                CollectionAssert.AreEqual(
                    sourcePayloads[i],
                    after,
                    $"第 {i} 条 \"{saved.Entries[i].Path}\" 未被修改，字节必须与源文件完全一致。");
            }

            // 3) 偏移必须重算：第一条变长/变短后，后续条目的 offset 要跟着移
            var expectedOffset = NpkArchive.DataStartBase + (saved.Entries.Count * NpkArchive.EntrySize) + saved.Entries[0].Length;
            Assert.AreEqual(expectedOffset, saved.Entries[1].Offset, "后续条目偏移必须按新长度重算。");
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
    public void ReplaceFrame_Twice_SaveThenDecodeMatchesInput()
    {
        if (!SampleFiles.SyntheticSampleAvailable)
        {
            return;
        }

        var temp1 = Path.Combine(Path.GetTempPath(), $"es_edit1_{Guid.NewGuid():N}.NPK");
        var temp2 = Path.Combine(Path.GetTempPath(), $"es_edit2_{Guid.NewGuid():N}.NPK");
        try
        {
            // 用"1555 恰好可表示"的颜色造图，保证解码后能逐像素比对（1555 对任意颜色是有损的）
            var first = CreateFrom1555Words(17, 17, seed: 3);
            var second = CreateFrom1555Words(17, 17, seed: 11);

            using (var archive = NpkArchive.Open(SamplePath))
            {
                var image = archive.LoadImg(archive.Entries[0]);
                image.ReplaceFrameImage(image.Frames[0], first, ColorBits.ARGB_1555);
                archive.Save(temp1);
            }

            using (var archive = NpkArchive.Open(temp1))
            {
                var image = archive.LoadImg(archive.Entries[0]);
                AssertEqualPixels(first, image.Decode(image.Frames[0]), "第一次往返");

                // 第二次替换：换成另一张图，走同一条链路
                image.ReplaceFrameImage(image.Frames[0], second, ColorBits.ARGB_1555);
                archive.Save(temp2);
            }

            using (var archive = NpkArchive.Open(temp2))
            {
                var image = archive.LoadImg(archive.Entries[0]);
                AssertEqualPixels(second, image.Decode(image.Frames[0]), "第二次往返");
            }
        }
        finally
        {
            foreach (var path in new[] { temp1, temp2 })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public void ReplaceFrame_OnLinkFrame_BecomesNormalFrame()
    {
        if (!SampleFiles.SyntheticSampleAvailable)
        {
            return;
        }

        var temp = Path.Combine(Path.GetTempPath(), $"es_edit_link_{Guid.NewGuid():N}.NPK");
        try
        {
            int entryIndex;
            int frameCount;
            int linkIndex;

            using (var archive = NpkArchive.Open(SamplePath))
            {
                // 合成样本里 shapes/link_cycle.img 带 LINK 帧：按语义扫描定位，不写死条目下标。
                var found = -1;
                ImgFile? image = null;
                for (var i = 0; i < archive.Entries.Count && found < 0; i++)
                {
                    var candidate = archive.LoadImg(archive.Entries[i]);
                    if (candidate.Frames.Any(frame => frame.IsLink))
                    {
                        found = i;
                        image = candidate;
                    }
                }

                Assert.IsTrue(found >= 0, "合成样本里应当有一个含 LINK 帧的条目（shapes/link_cycle.img）。");
                Assert.IsNotNull(image);

                entryIndex = found;
                frameCount = image!.Frames.Count;
                var link = image.Frames.First(frame => frame.IsLink);
                linkIndex = link.Index;

                image.ReplaceFrameImage(link, CreateSolid(8, 8, new Color32(0, 255, 0, 255)), ColorBits.LINK);

                Assert.IsFalse(link.IsLink, "LINK 帧被换图后必须变成普通帧。");
                Assert.AreEqual(ColorBits.ARGB_1555, link.Type, "LINK 请求必须被强制成 ARGB_1555。");
                Assert.IsNull(link.LinkTarget);
                Assert.AreEqual(8, link.Width);

                archive.Save(temp);
            }

            using var saved = NpkArchive.Open(temp);
            var image2 = saved.LoadImg(saved.Entries[entryIndex]);
            Assert.AreEqual(frameCount, image2.Frames.Count);
            Assert.IsFalse(image2.Frames[linkIndex].IsLink);
            var decoded = image2.Decode(image2.Frames[linkIndex]);
            Assert.IsNotNull(decoded);
            Assert.AreEqual(new Color32(0, 255, 0, 255), decoded.GetPixel(0, 0));
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>
    ///     v5 条目"改帧 → 存包"闭环。
    ///     <para>
    ///         <b>语义变更说明</b>：本用例原名 <c>Save_V5ModifiedEntry_ThrowsNotSupported</c>，
    ///         锁定的是"v5 只读，标脏保存即抛 <see cref="NotSupportedException" />"的旧限制。
    ///         现在 v5 有了 <c>Ver5Handler</c>：帧像素写进新增的 ARGB1555 纹理页，
    ///         因此限制被拆掉，改为锁定新语义——<b>能保存，且重新打开后帧数/尺寸/像素不变</b>。
    ///         这正是"读写闭环"回归：从拒绝保存变成可往返，而不是删掉用例留下空白。
    ///     </para>
    /// </summary>
    [TestMethod]
    public void Save_V5ModifiedEntry_RepacksAndRoundTrips()
    {
        var npkPath = Path.Combine(Path.GetTempPath(), $"es_v5_{Guid.NewGuid():N}.NPK");
        var outPath = Path.Combine(Path.GetTempPath(), $"es_v5_out_{Guid.NewGuid():N}.NPK");
        try
        {
            File.WriteAllBytes(npkPath, BuildNpkWithSingleEntry(BuildV5Image()));

            int expectedLength;
            using (var archive = NpkArchive.Open(npkPath))
            {
                var entry = archive.Entries[0];
                var image = archive.LoadImg(entry);

                Assert.AreEqual(ImgVersion.Ver5, image.Version, "构造的样本应被识别为 v5（已注册 Ver5Handler）。");
                Assert.AreEqual(1, image.Frames.Count);
                Assert.AreEqual(1, image.Textures.Count, "纹理表应被解析出 1 张页。");

                var before = image.Decode(image.Frames[0]);
                Assert.IsNotNull(before);
                Assert.AreEqual(4, before!.Width);
                Assert.AreEqual(new Color32(255, 0, 0, 255), before.GetPixel(0, 0), "源图左上角应为红");

                // v5 换成纯蓝：像素进新纹理页，原页保留（旧限制在这里抛 NotSupportedException）。
                image.ReplaceFrameImage(image.Frames[0], CreateSolid(4, 4, new Color32(0, 0, 255, 255)), ColorBits.UNKNOWN);

                Assert.IsTrue(entry.IsModified);
                Assert.AreEqual(2, image.Textures.Count, "换图会新增一张纹理页，原页不就地改写。");
                Assert.AreEqual(ColorBits.ARGB_1555, image.Frames[0].Type, "v5 写回统一落成 1555（无 DXT 编码）。");

                expectedLength = image.ToArray().Length;
                archive.Save(outPath);
            }

            Assert.IsTrue(File.Exists(outPath), "v5 条目标脏后必须能正常保存。");
            Assert.IsFalse(File.Exists(outPath + ".saving"), "临时文件必须被清理。");

            using var saved = NpkArchive.Open(outPath);
            Assert.AreEqual(1, saved.Entries.Count);
            Assert.AreEqual(expectedLength, saved.Entries[0].Length, "条目长度必须更新为重编码后的 IMG 长度。");

            var reloaded = saved.LoadImg(saved.Entries[0]);
            Assert.AreEqual(ImgVersion.Ver5, reloaded.Version);
            Assert.AreEqual(1, reloaded.Frames.Count, "帧数不变。");

            var decoded = reloaded.Decode(reloaded.Frames[0]);
            Assert.IsNotNull(decoded);
            Assert.AreEqual(4, decoded!.Width, "UV 尺寸不变。");
            Assert.AreEqual(4, decoded.Height);
            Assert.AreEqual(new Color32(0, 0, 255, 255), decoded.GetPixel(0, 0), "保存后重开：像素应为替换后的蓝");
            Assert.AreEqual(new Color32(0, 0, 255, 255), decoded.GetPixel(3, 3));
        }
        finally
        {
            foreach (var path in new[] { npkPath, outPath })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [TestMethod]
    public void ReplaceFrameImage_RejectsPaletteIndexFormats()
    {
        if (!SampleFiles.SyntheticSampleAvailable)
        {
            return;
        }

        using var archive = NpkArchive.Open(SamplePath);
        var image = archive.LoadImg(archive.Entries[0]);

        try
        {
            image.ReplaceFrameImage(image.Frames[0], CreateSolid(4, 4, Color32.White), ColorBits.DXT_1);
            Assert.Fail("调色板索引/DXT 格式应被拒绝（需先量化）。");
        }
        catch (NotSupportedException)
        {
            // 期望路径
        }
    }

    /// <summary>
    ///     构造一个只含 1 个真实 v5 条目的最小 NPK（本机没有 v5 样本，只能手工拼）。
    ///     <para>
    ///         IMG 布局见 <see cref="TestImageBuilder.BuildVer5" />：1 张 4×4 ARGB1555 纹理页 +
    ///         1 个覆盖整页 UV 的纹理帧。
    ///     </para>
    /// </summary>
    private static byte[] BuildV5Image()
    {
        var page = CreateSolid(4, 4, new Color32(255, 0, 0, 255));
        var texture = new TestImageBuilder.TestTexture
        {
            Version = 1,
            Type = ColorBits.ARGB_1555,
            Width = 4,
            Height = 4,
            Raw = ColorCodec.Encode(page.Pixels, ColorBits.ARGB_1555)
        };

        var frame = new TestImageBuilder.TestFrame
        {
            Type = ColorBits.ARGB_1555,
            Compress = CompressMode.DDS_ZLIB,
            Width = 4,
            Height = 4,
            Length = 0,
            X = 3,
            Y = -2,
            FrameWidth = 8,
            FrameHeight = 8,
            Uv = new TestImageBuilder.TestUv
            {
                TextureIndex = 0,
                LeftUpX = 0,
                LeftUpY = 0,
                RightDownX = 4,
                RightDownY = 4
            }
        };

        return TestImageBuilder.BuildVer5([frame], [texture]);
    }

    /// <summary>把一段 IMG 字节包成单条目 NPK（条目表 + 32 字节空 hash + 数据区）。</summary>
    private static byte[] BuildNpkWithSingleEntry(byte[] payload)
    {
        const int count = 1;
        const string path = "sprite/test/v5.img";
        var dataStart = NpkArchive.DataStartBase + (count * NpkArchive.EntrySize);

        using var npk = new MemoryStream();
        npk.Write(NpkCipher.PackFlagBytes);
        WriteInt32(npk, count);
        WriteInt32(npk, dataStart);
        WriteInt32(npk, payload.Length);
        npk.Write(NpkCipher.EncodePath(path));
        npk.Write(new byte[NpkArchive.HashSize]); // 自检不校验 hash 内容，这里留空
        npk.Write(payload);
        return npk.ToArray();
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static RgbaImage CreateSolid(int width, int height, Color32 color)
    {
        var image = new RgbaImage(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.SetPixel(x, y, color);
            }
        }

        return image;
    }

    /// <summary>
    ///     构造一张"每个像素都由 ARGB1555 字解出"的图，保证 encode→decode 无损，便于逐像素比对。
    /// </summary>
    private static RgbaImage CreateFrom1555Words(int width, int height, byte seed)
    {
        var image = new RgbaImage(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var r = (x + seed) % 32;
                var g = (y * 3 + seed) % 32;
                var b = (x + y + seed) % 32;
                var word = (ushort)(0x8000 | (r << 10) | (g << 5) | b);
                image.SetPixel(x, y, ColorCodec.DecodeArgb1555(word));
            }
        }

        return image;
    }

    private static void AssertEqualPixels(RgbaImage expected, RgbaImage? actual, string stage)
    {
        Assert.IsNotNull(actual, $"{stage}：解码结果不应为 null。");
        Assert.AreEqual(expected.Width, actual.Width, $"{stage}：宽度");
        Assert.AreEqual(expected.Height, actual.Height, $"{stage}：高度");

        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                Assert.AreEqual(expected.GetPixel(x, y), actual.GetPixel(x, y), $"{stage}：像素 ({x},{y})");
            }
        }
    }

    /// <summary>
    ///     保存后文件的通用校验：能被重新打开、条目数正确、所有条目都能读出数据。
    /// </summary>
    private static class FileAssert
    {
        public static void SavedFileIsValid(string path)
        {
            Assert.IsTrue(File.Exists(path), "保存后目标文件应存在。");
            Assert.IsFalse(File.Exists(path + ".saving"), "临时文件必须被清理。");

            using var archive = NpkArchive.Open(path);
            Assert.IsTrue(archive.Entries.Count > 1, "保存后条目数应与源包一致。");
            foreach (var entry in archive.Entries)
            {
                Assert.AreEqual(entry.Length, archive.ReadRaw(entry).Length);
            }
        }
    }
}
