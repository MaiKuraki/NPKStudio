using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Imaging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     <see cref="ZlibCodec" /> 的压缩/解压往返与短读行为测试。
/// </summary>
[TestClass]
public sealed class ZlibCodecTests
{
    [TestMethod]
    public void CompressThenDecompress_RoundTrips()
    {
        var source = BuildPattern(4096);

        var compressed = ZlibCodec.Compress(source);
        Assert.IsTrue(compressed.Length > 0, "压缩结果不应为空。");
        Assert.AreEqual(0x78, compressed[0], "zlib 流首字节应为 0x78（Windows/zlib 默认窗口）。");

        var restored = ZlibCodec.Decompress(compressed, source.Length);

        Assert.AreEqual(source.Length, restored.Length);
        CollectionAssert.AreEqual(source, restored, "解压结果与原始数据不一致。");
    }

    [TestMethod]
    public void RoundTrip_SmallImageSizedBuffer_MatchesManualZlibCase()
    {
        // 模拟实测样本：17x17 ARGB1555 → 578 字节
        var source = BuildPattern(17 * 17 * 2);

        var compressed = ZlibCodec.Compress(source);
        var restored = ZlibCodec.Decompress(compressed, source.Length);

        Assert.AreEqual(578, restored.Length);
        CollectionAssert.AreEqual(source, restored);
    }

    [TestMethod]
    public void Decompress_WhenExpectedLengthTooLarge_ThrowsImgFormatException()
    {
        var source = BuildPattern(100);
        var compressed = ZlibCodec.Compress(source);

        try
        {
            ZlibCodec.Decompress(compressed, source.Length + 10);
            Assert.Fail("期望长度大于实际长度时应抛出 ImgFormatException。");
        }
        catch (ImgFormatException)
        {
            // 期望路径：绝不返回"读到一半"的数据
        }
    }

    [TestMethod]
    public void Decompress_CorruptData_ThrowsImgFormatException()
    {
        byte[] garbage = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07];

        try
        {
            ZlibCodec.Decompress(garbage, 16);
            Assert.Fail("垃圾数据应抛出 ImgFormatException。");
        }
        catch (ImgFormatException)
        {
            // 期望路径
        }
    }

    [TestMethod]
    public void Decompress_EmptyExpectation_ReturnsEmptyArray()
    {
        var compressed = ZlibCodec.Compress(Array.Empty<byte>());

        var restored = ZlibCodec.Decompress(compressed, 0);

        Assert.AreEqual(0, restored.Length);
    }

    [TestMethod]
    public void Compress_IsDeterministicForSameInput()
    {
        var source = BuildPattern(1024);

        var first = ZlibCodec.Compress(source);
        var second = ZlibCodec.Compress(source);

        CollectionAssert.AreEqual(first, second, "相同输入应产生相同 zlib 流（便于字节级对比测试）。");
    }

    private static byte[] BuildPattern(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)((i * 31 + 7) & 0xFF);
        }

        return data;
    }
}
