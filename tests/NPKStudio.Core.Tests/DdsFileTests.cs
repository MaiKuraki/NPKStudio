using System.Text;

using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Imaging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     <see cref="DdsFile" /> 头部解析测试（批次一未列入清单，但头部偏移量错一位就会全盘失效，故补测）。
/// </summary>
[TestClass]
public sealed class DdsFileTests
{
    [TestMethod]
    public void Parse_SingleMipDxt1_ReadsSizeFormatAndData()
    {
        // 4x4 DXT1 → 1 块 = 8 字节
        byte[] dxtBlock =
        [
            0x00, 0xF8,
            0x1F, 0x00,
            0xE4, 0xE4, 0xE4, 0xE4
        ];
        var data = BuildDds(4, 4, "DXT1", 1, dxtBlock);

        var dds = DdsFile.Parse(data);

        Assert.AreEqual(4, dds.Width);
        Assert.AreEqual(4, dds.Height);
        Assert.AreEqual(DxtFormat.Dxt1, dds.Format);
        Assert.AreEqual(1, dds.Mipmaps.Length);
        Assert.AreEqual(4, dds.PrimaryMipmap.Width);
        Assert.AreEqual(4, dds.PrimaryMipmap.Height);
        CollectionAssert.AreEqual(dxtBlock, dds.PrimaryMipmap.Data, "主层原始块字节应原样取出。");

        // 与解码器串起来验证一次端到端
        var pixels = DxtDecoder.DecodeToNewImage(dds.PrimaryMipmap.Data, dds.Width, dds.Height, dds.Format);
        Assert.AreEqual(64, pixels.Length);
    }

    [TestMethod]
    public void Parse_Dxt5_ReportsDxt5Format()
    {
        var data = BuildDds(8, 8, "DXT5", 1, new byte[16 * 4]);

        var dds = DdsFile.Parse(data);

        Assert.AreEqual(8, dds.Width);
        Assert.AreEqual(8, dds.Height);
        Assert.AreEqual(DxtFormat.Dxt5, dds.Format);
        Assert.AreEqual(64, dds.PrimaryMipmap.Data.Length, "8x8 DXT5 = 4 块 × 16 字节。");
    }

    [TestMethod]
    public void Parse_TooShort_ThrowsImgFormatException()
    {
        try
        {
            DdsFile.Parse(new byte[16]);
            Assert.Fail("长度不足 128 字节时应抛出 ImgFormatException。");
        }
        catch (ImgFormatException)
        {
            // 期望路径
        }
    }

    [TestMethod]
    public void Parse_BadMagic_ThrowsImgFormatException()
    {
        var data = BuildDds(4, 4, "DXT1", 1, new byte[8]);
        data[0] = (byte)'X';

        try
        {
            DdsFile.Parse(data);
            Assert.Fail("魔数错误时应抛出 ImgFormatException。");
        }
        catch (ImgFormatException)
        {
            // 期望路径
        }
    }

    [TestMethod]
    public void Parse_UnsupportedFourCc_ThrowsImgFormatException()
    {
        var data = BuildDds(4, 4, "DX10", 1, new byte[8]);

        try
        {
            DdsFile.Parse(data);
            Assert.Fail("不支持的 FourCC 应抛出 ImgFormatException。");
        }
        catch (ImgFormatException)
        {
            // 期望路径
        }
    }

    private static byte[] BuildDds(int width, int height, string fourCc, int mipCount, byte[] payload)
    {
        var header = new byte[128];
        Encoding.ASCII.GetBytes("DDS ").CopyTo(header, 0);
        WriteInt32(header, 4, 124); // dwSize
        WriteInt32(header, 8, 0x00081007); // dwFlags（仅占位）
        WriteInt32(header, 12, height);
        WriteInt32(header, 16, width);
        WriteInt32(header, 20, 0); // dwPitchOrLinearSize
        WriteInt32(header, 24, 0); // dwDepth
        WriteInt32(header, 28, mipCount);
        WriteInt32(header, 76, 32); // ddspf.dwSize
        WriteInt32(header, 80, 0x4); // ddspf.dwFlags = DDPF_FOURCC
        Encoding.ASCII.GetBytes(fourCc).CopyTo(header, 84);
        WriteInt32(header, 108, 0x1000); // dwCaps

        var result = new byte[header.Length + payload.Length];
        header.CopyTo(result, 0);
        payload.CopyTo(result, header.Length);
        return result;
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
