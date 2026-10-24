using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     帧结构编辑（增 / 删 / 移动）与几何编辑（位置 / 尺寸）。
///     <para>
///         断言重点不是"方法被调用过"，而是<b>三个不变量</b>：
///         ① <see cref="ImgFrame.Index" /> 必须恒等于列表下标（写 LINK 时用的就是它）；
///         ② LINK 引用永远指向一个还活着的帧；
///         ③ 编辑后写出再读回，帧数 / 顺序 / 坐标 / 尺寸全部一致。
///     </para>
/// </summary>
[TestClass]
public sealed class FrameTableTests
{
    [TestMethod]
    public void InsertFrame_InsertsBlankAndRenumbers()
    {
        var image = Build(3, out _);

        var inserted = image.InsertFrame(1);

        Assert.AreEqual(4, image.Frames.Count);
        Assert.AreSame(inserted, image.Frames[1]);
        Assert.IsTrue(inserted.Width == 1 && inserted.Height == 1, "插入的应是 1×1 全透明占位帧。");
        Assert.IsFalse(inserted.IsLink, "占位帧必须是普通帧，否则没有像素可写。");
        AssertIndexesAreListPositions(image, "插入后");
    }

    [TestMethod]
    public void InsertFrame_ThenWriteAndRead_RoundTrips()
    {
        var image = Build(3, out _);
        image.InsertFrame(0);

        var reloaded = RoundTrip(image);

        Assert.AreEqual(4, reloaded.Frames.Count);
        Assert.IsTrue(reloaded.Frames[0].Width == 1 && reloaded.Frames[0].Height == 1);
        AssertIndexesAreListPositions(reloaded, "读回后");
    }

    [TestMethod]
    public void RemoveFrame_NormalFrame_LinkTakesOverItsContent()
    {
        // [A(普通) , B(LINK→A)]，删掉 A —— 原版语义：B 顶替 A 的像素，渲染结果不变。
        var image = Build(1, out var first);
        var link = image.AddFrame();
        link.Type = ColorBits.LINK;
        link.LinkTarget = first;

        Assert.IsTrue(image.RemoveFrame(first));

        Assert.AreEqual(1, image.Frames.Count);
        var survivor = image.Frames[0];
        Assert.AreSame(link, survivor);
        Assert.IsFalse(survivor.IsLink, "顶替者必须变成普通帧，否则像素没地方存。");
        Assert.AreEqual(17, survivor.Width);
        Assert.AreEqual(17, survivor.Height);
        Assert.IsTrue(survivor.RawData.Length > 0);
        AssertIndexesAreListPositions(image, "删除普通帧后");
    }

    [TestMethod]
    public void RemoveFrame_LinkFrame_ReferrersPointToItsTarget()
    {
        // [A(普通) , B(LINK→A) , C(LINK→B)]，删掉 B —— C 应改指向 A。
        var image = Build(1, out var first);
        var middle = image.AddFrame();
        middle.Type = ColorBits.LINK;
        middle.LinkTarget = first;
        var outer = image.AddFrame();
        outer.Type = ColorBits.LINK;
        outer.LinkTarget = middle;

        Assert.IsTrue(image.RemoveFrame(middle));

        Assert.AreEqual(2, image.Frames.Count);
        Assert.AreSame(first, outer.LinkTarget, "删掉中间链接后，引用者要改指向它原本的目标。");
        AssertIndexesAreListPositions(image, "删除 LINK 帧后");
    }

    [TestMethod]
    public void RemoveFrame_Repeatedly_KeepsIndexesConsistent()
    {
        var image = Build(4, out _);
        while (image.Frames.Count > 0)
        {
            image.RemoveFrame(image.Frames[0]);
            AssertIndexesAreListPositions(image, "逐个删除后");
        }

        Assert.AreEqual(0, image.Frames.Count);
    }

    [TestMethod]
    public void MoveFrame_ReordersAndRenumbers()
    {
        var image = Build(3, out var first);
        var second = image.Frames[1];
        var third = image.Frames[2];

        Assert.IsTrue(image.MoveFrame(0, 2));

        Assert.AreSame(second, image.Frames[0]);
        Assert.AreSame(third, image.Frames[1]);
        Assert.AreSame(first, image.Frames[2]);
        AssertIndexesAreListPositions(image, "移动后");

        // 移动会改 Index，而写 LINK 用的就是 Index —— 必须写出再读回验证。
        var link = image.AddFrame();
        link.Type = ColorBits.LINK;
        link.LinkTarget = first;
        var reloaded = RoundTrip(image);
        Assert.AreSame(reloaded.Frames[2], reloaded.Frames[3].LinkTarget, "LINK 应仍然指向移动后的那一帧。");
    }

    [TestMethod]
    public void MoveFrame_SameIndex_ReturnsFalse()
    {
        var image = Build(3, out _);
        Assert.IsFalse(image.MoveFrame(1, 1));
    }

    [TestMethod]
    public void SetFramePosition_ChangesIndexTableOnly()
    {
        var image = Build(1, out var frame);

        Assert.IsTrue(image.SetFramePosition(frame, -12, 34));
        Assert.AreEqual(-12, frame.X, "X 允许为负（DNF 的帧常带负偏移）。");
        Assert.AreEqual(34, frame.Y);
        Assert.IsFalse(image.SetFramePosition(frame, -12, 34), "值没变时不应报告变化。");

        var reloaded = RoundTrip(image);
        Assert.AreEqual(-12, reloaded.Frames[0].X);
        Assert.AreEqual(34, reloaded.Frames[0].Y);
    }

    [TestMethod]
    public void ResizeFrame_ResamplesPixels()
    {
        var image = Build(1, out var frame);
        Assert.AreEqual(17, frame.Width);

        Assert.IsTrue(image.ResizeFrame(frame, 30, 20));
        Assert.AreEqual(30, frame.Width);
        Assert.AreEqual(20, frame.Height);
        Assert.AreEqual(CompressMode.ZLIB, frame.Compress);
        Assert.AreEqual(frame.RawData.Length, frame.Length);

        var reloaded = RoundTrip(image);
        var decoded = reloaded.Decode(reloaded.Frames[0]);
        Assert.IsNotNull(decoded);
        Assert.AreEqual(30, decoded!.Width);
        Assert.AreEqual(20, decoded.Height);
    }

    [TestMethod]
    public void ResizeFrame_LinkFrame_Throws()
    {
        var image = Build(1, out var first);
        var link = image.AddFrame();
        link.Type = ColorBits.LINK;
        link.LinkTarget = first;

        Assert.ThrowsExactly<InvalidOperationException>(() => image.ResizeFrame(link, 8, 8));
    }

    [TestMethod]
    public void InsertFrame_OnUnsupportedVersion_Throws()
    {
        // v5 现在已有 Ver5Handler（不再走 OtherHandler），所以这里改用仍未实现的 v8 代表"无 handler 的版本"：
        // 只有真正整段透传的版本才必须拒绝改写帧表，否则会写出结构损坏的文件。
        var v8 = ImgFile.Create(ImgVersion.Ver8, "a.img");
        Assert.ThrowsExactly<NotSupportedException>(() => v8.InsertFrame(0));
    }

    [TestMethod]
    public void RemoveFrame_ForeignFrame_ReturnsFalse()
    {
        var a = Build(2, out _);
        var b = Build(2, out _);

        Assert.IsFalse(a.RemoveFrame(b.Frames[0]), "别的 IMG 的帧不能被删。");
    }

    /// <summary>构造一个含 <paramref name="frameCount" /> 个 17×17 实心帧的 v2 IMG。</summary>
    private static ImgFile Build(int frameCount, out ImgFrame first)
    {
        var image = ImgFile.Create(ImgVersion.Ver2, "a.img");
        var solid = CreateSolid(17, 17, new Color32(0, 128, 255, 255));

        first = image.AddFrame();
        image.ReplaceFrameImage(first, solid, ColorBits.ARGB_1555);

        for (var i = 1; i < frameCount; i++)
        {
            image.ReplaceFrameImage(image.AddFrame(), solid, ColorBits.ARGB_1555);
        }

        return image;
    }

    private static ImgFile RoundTrip(ImgFile image)
    {
        var bytes = image.ToArray();
        using var stream = new MemoryStream(bytes, writable: false);
        return ImgFile.Read(stream, image.Path, bytes.Length);
    }

    private static void AssertIndexesAreListPositions(ImgFile image, string when)
    {
        for (var i = 0; i < image.Frames.Count; i++)
        {
            Assert.AreEqual(i, image.Frames[i].Index, $"{when}帧 #{i} 的 Index 与列表下标不符（写 LINK 会指错）。");
        }
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
}
