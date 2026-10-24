using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     敌对/损坏输入的护栏测试。
///     <para>
///         背景（实测数据）：修复前一个 <b>80 字节</b>的 IMG 只要把帧头声明成 20000×20000，
///         就会让解析器去分配 <b>763 MB</b>，然后抛 <see cref="EndOfStreamException" />；
///         60000×60000 则因为 int 乘法溢出抛 <see cref="OverflowException" /> ——
///         这两种都不属于"格式错误"，会绕过上层的 <c>catch (ImgFormatException)</c>。
///     </para>
///     <para>
///         本文件锁定修复后的契约：<b>一律 ImgFormatException，且在分配之前失败</b>。
///     </para>
/// </summary>
[TestClass]
public sealed class HostileInputTests
{
    /// <summary>声明超大尺寸 + 未压缩（NONE）→ 必须在分配前拒绝（修复前会分配 763MB 再抛 EndOfStream）。</summary>
    [TestMethod]
    public void NoneCompress_HugeDimension_ThrowsImgFormat_BeforeAllocating()
    {
        var data = BuildSingleFrameImg(width: 20000, height: 20000, ColorBits.ARGB_1555, CompressMode.NONE, payload: [1, 2, 3, 4]);

        // 不允许分配超过 64KB：护栏是在 new byte[] 之前触发的
        var before = GC.GetAllocatedBytesForCurrentThread();
        var ex = Assert.ThrowsExactly<ImgFormatException>(() => ReadImg(data));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsTrue(allocated < 64 * 1024, $"分配了 {allocated} 字节（期望 < 64KB，说明护栏没在分配前生效）。");
        StringAssert.Contains(ex.Message, "尺寸非法");
    }

    /// <summary>声明超大尺寸 + zlib → 修复前会抛 <see cref="OverflowException" />（int 乘法溢出）。</summary>
    [TestMethod]
    public void Zlib_HugeDimension_ThrowsImgFormat_NotOverflow()
    {
        var payload = ZlibCodec.Compress(new byte[64]);
        var data = BuildSingleFrameImg(width: 60000, height: 60000, ColorBits.ARGB_1555, CompressMode.ZLIB, payload);

        // OverflowException 不是 ImgFormatException 的子类，所以这行同时也验证了"不再是 Overflow"
        Assert.ThrowsExactly<ImgFormatException>(() => ReadImg(data));
    }

    /// <summary>刚好超过维度上限（8193）→ 拒绝，且异常信息里带上限值。</summary>
    [TestMethod]
    public void WidthJustOverLimit_ThrowsImgFormat()
    {
        var data = BuildSingleFrameImg(width: 8193, height: 1, ColorBits.ARGB_1555, CompressMode.NONE, new byte[8193 * 2]);
        var ex = Assert.ThrowsExactly<ImgFormatException>(() => ReadImg(data));
        StringAssert.Contains(ex.Message, "8192");
    }

    /// <summary>刚好等于上限（8192×1）→ 正常解析（护栏不能误伤合法文件）。</summary>
    [TestMethod]
    public void WidthAtLimit_ParsesSuccessfully()
    {
        var data = BuildSingleFrameImg(width: 8192, height: 1, ColorBits.ARGB_1555, CompressMode.NONE, new byte[8192 * 2]);
        var image = ReadImg(data);
        Assert.AreEqual(1, image.Frames.Count);
        Assert.AreEqual(8192, image.Frames[0].Width);
    }

    /// <summary>数据被截断（声明 1000 字节，实际只有 10 字节）→ 必须在分配前抛 ImgFormatException。</summary>
    [TestMethod]
    public void TruncatedPayload_ThrowsImgFormat()
    {
        var data = BuildSingleFrameImg(width: 4, height: 4, ColorBits.ARGB_1555, CompressMode.ZLIB, new byte[10], declaredLength: 1000);
        Assert.ThrowsExactly<ImgFormatException>(() => ReadImg(data));
    }

    /// <summary>像素量超过 64MB 上限（但宽高都在 8192 内）→ 拒绝。</summary>
    [TestMethod]
    public void PixelBudgetOverLimit_ThrowsImgFormat()
    {
        // 8192×8192×4 = 256MB > 64MB，但宽高都合法 → 由"像素量"护栏拦下
        var data = BuildSingleFrameImg(width: 8192, height: 8192, ColorBits.ARGB_8888, CompressMode.ZLIB, ZlibCodec.Compress(new byte[64]));
        var ex = Assert.ThrowsExactly<ImgFormatException>(() => ReadImg(data));
        StringAssert.Contains(ex.Message, "像素量");
    }

    private static ImgFile ReadImg(byte[] data) => ImgFile.Read(new MemoryStream(data), "hostile.img", data.Length);

    /// <summary>
    ///     构造只有一帧的最小 v2 IMG。帧头字段按参数写死，载荷原样附加（不做任何合法性保证）。
    /// </summary>
    private static byte[] BuildSingleFrameImg(
        int width,
        int height,
        ColorBits type,
        CompressMode compress,
        byte[] payload,
        int? declaredLength = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(System.Text.Encoding.Latin1.GetBytes("Neople Img File\0")); // 16 字节魔数
        writer.Write(36L);                                                        // IndexLength
        writer.Write((int)ImgVersion.Ver2);                                       // version
        writer.Write(1);                                                          // frame count

        writer.Write((int)type);
        writer.Write((int)compress);
        writer.Write(width);
        writer.Write(height);
        writer.Write(declaredLength ?? payload.Length);
        writer.Write(0);      // x
        writer.Write(0);      // y
        writer.Write(width);  // frameWidth
        writer.Write(height); // frameHeight
        writer.Write(payload);

        writer.Flush();
        return stream.ToArray();
    }
}
