using System.Text;

using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     <see cref="ImgEditHistory" />（撤销 / 重做）的验收测试。
///     <para>
///         断言一律落在<b>可观察的模型状态</b>上：帧顺序、每帧字段、以及解码后的像素字节。
///         其中"连续 20 次编辑 → 撤销 → 重做"那条按 REQ-16 的要求做<b>逐字节</b>比对，
///         不是"设计上保证"。
///     </para>
/// </summary>
[TestClass]
public sealed class ImgEditHistoryTests
{
    /// <summary>删除后撤销：帧数恢复，且每帧像素与坐标逐字节还原。</summary>
    [TestMethod]
    public void RemoveFrame_Undo_RestoresCountAndPixels()
    {
        var image = CreateImage(3);
        var history = new ImgEditHistory(image);
        var before = Fingerprint(image);

        Assert.IsTrue(image.RemoveFrame(image.Frames[1]));
        history.Record();
        Assert.AreEqual(2, image.Frames.Count);
        Assert.IsTrue(history.CanUndo);
        Assert.AreEqual(1, history.Depth);

        Assert.IsTrue(history.Undo());
        Assert.AreEqual(3, image.Frames.Count);
        Assert.AreEqual(before, Fingerprint(image), "撤销后帧顺序、坐标与像素必须与删除前完全一致。");
        Assert.IsFalse(history.CanUndo);
        Assert.IsTrue(history.CanRedo);
    }

    /// <summary>插入后撤销：帧数回到原值，顺序不变。</summary>
    [TestMethod]
    public void InsertFrame_Undo_RestoresFrameCount()
    {
        var image = CreateImage(3);
        var history = new ImgEditHistory(image);
        var before = Fingerprint(image);

        image.InsertFrame(1);
        history.Record();
        Assert.AreEqual(4, image.Frames.Count);
        Assert.AreEqual(1, image.Frames[1].Width, "插入的应是 1×1 透明帧。");

        Assert.IsTrue(history.Undo());
        Assert.AreEqual(3, image.Frames.Count);
        Assert.AreEqual(before, Fingerprint(image), "撤销插入后像素与坐标必须完全还原。");
    }

    /// <summary>移动后撤销：顺序恢复（比较每帧的对象引用顺序，而不是像素）。</summary>
    [TestMethod]
    public void MoveFrame_Undo_RestoresOrder()
    {
        var image = CreateImage(4);
        var history = new ImgEditHistory(image);
        var original = image.Frames.ToArray();

        Assert.IsTrue(image.MoveFrame(0, 3));
        history.Record();
        CollectionAssert.AreEqual(new[] { original[1], original[2], original[3], original[0] }, image.Frames.ToArray());

        Assert.IsTrue(history.Undo());
        CollectionAssert.AreEqual(original, image.Frames.ToArray(), "撤销移动后帧的对象顺序必须回到初始。");
        for (var i = 0; i < original.Length; i++)
        {
            Assert.AreEqual(i, image.Frames[i].Index, "撤销后每帧的 Index 必须等于列表下标。");
        }
    }

    /// <summary>
    ///     REQ-16：连续 20 次「编辑 → 撤销 → 重做」之后，像素与坐标必须与初始状态<b>逐字节一致</b>。
    ///     <para>
    ///         每一轮都在四种编辑里轮换（移动 / 改坐标 / 缩放 / 换图），覆盖快照里全部的可变字段；
    ///         每轮断言三件事：撤销后 == 初始、重做后 == 编辑后、再撤销后 == 初始。
    ///     </para>
    /// </summary>
    [TestMethod]
    public void TwentyEditUndoRedoCycles_KeepPixelsAndGeometryByteIdentical()
    {
        var image = CreateImage(3);
        var history = new ImgEditHistory(image);
        var initial = Fingerprint(image);

        for (var i = 0; i < 20; i++)
        {
            ApplyEdit(image, i);
            history.Record();

            var edited = Fingerprint(image);
            Assert.AreNotEqual(initial, edited, $"第 {i + 1} 轮编辑应真的改变模型。");

            Assert.IsTrue(history.Undo(), $"第 {i + 1} 轮撤销应成功。");
            Assert.AreEqual(initial, Fingerprint(image), $"第 {i + 1} 轮撤销后必须与初始逐字节一致。");

            Assert.IsTrue(history.Redo(), $"第 {i + 1} 轮重做应成功。");
            Assert.AreEqual(edited, Fingerprint(image), $"第 {i + 1} 轮重做后必须与编辑后逐字节一致。");

            Assert.IsTrue(history.Undo(), $"第 {i + 1} 轮回退应成功。");
            Assert.AreEqual(initial, Fingerprint(image), $"第 {i + 1} 轮回退后必须与初始逐字节一致。");
        }

        Assert.AreEqual(0, history.Depth, "每轮都以撤销收尾，可撤销步数应回到 0。");
        Assert.IsFalse(history.CanUndo);
        Assert.IsTrue(history.CanRedo);
    }

    /// <summary>超过容量后丢最老的记录：容量 3 + 5 次编辑 → 只能撤销 3 步。</summary>
    [TestMethod]
    public void Capacity_DropsOldestEntries()
    {
        var image = CreateImage(3);
        var history = new ImgEditHistory(image, capacity: 3);

        for (var i = 0; i < 5; i++)
        {
            image.InsertFrame(0);
            history.Record();
        }

        Assert.AreEqual(8, image.Frames.Count);
        Assert.AreEqual(3, history.Depth, "容量 3 时深度不得超过 3。");

        Assert.IsTrue(history.Undo());
        Assert.IsTrue(history.Undo());
        Assert.IsTrue(history.Undo());
        Assert.IsFalse(history.CanUndo);
        Assert.IsFalse(history.Undo(), "超过容量的记录应已被丢弃。");

        // 5 次插入里最老的 2 步被丢弃 → 只能退回"第 2 次插入之后"的 5 帧状态。
        Assert.AreEqual(5, image.Frames.Count, "撤销到底后应停在被丢弃点之后的状态，而不是初始 3 帧。");
    }

    /// <summary><see cref="ImgEditHistory.Clear" /> 之后不能再撤销 / 重做。</summary>
    [TestMethod]
    public void Clear_ResetsBothStacks()
    {
        var image = CreateImage(3);
        var history = new ImgEditHistory(image);

        image.InsertFrame(0);
        history.Record();
        Assert.IsTrue(history.CanUndo);

        Assert.IsTrue(history.Undo());
        Assert.IsTrue(history.CanRedo);

        history.Clear();
        Assert.IsFalse(history.CanUndo);
        Assert.IsFalse(history.CanRedo);
        Assert.AreEqual(0, history.Depth);
        Assert.IsFalse(history.Undo());
        Assert.IsFalse(history.Redo());

        // Clear 之后的新基线是"当前状态"，再编辑一次仍然可以撤销回它。
        var baseline = Fingerprint(image);
        image.InsertFrame(0);
        history.Record();
        Assert.IsTrue(history.Undo());
        Assert.AreEqual(baseline, Fingerprint(image));
    }

    /// <summary>删除被 LINK 引用的帧后撤销：引用者必须恢复成 LINK，且目标仍是原来那一帧。</summary>
    [TestMethod]
    public void RemoveReferencedFrame_Undo_RestoresLinkTargets()
    {
        var image = CreateImage(3);
        var target = image.Frames[0];
        var referrer = image.Frames[2];
        referrer.Type = ColorBits.LINK;
        referrer.Width = 0;
        referrer.Height = 0;
        referrer.FrameWidth = 0;
        referrer.FrameHeight = 0;
        referrer.RawData = [];
        referrer.Length = 0;
        referrer.LinkTarget = target;

        var history = new ImgEditHistory(image);

        Assert.IsTrue(image.RemoveFrame(target));
        history.Record();
        Assert.IsFalse(referrer.IsLink, "删除被引用帧时，第一个引用者会顶替它的像素。");

        Assert.IsTrue(history.Undo());
        Assert.AreSame(target, image.Frames[0], "被删的帧对象必须回到原位。");
        Assert.AreSame(referrer, image.Frames[2]);
        Assert.IsTrue(referrer.IsLink, "撤销后引用者必须恢复成 LINK 帧。");
        Assert.AreSame(target, referrer.LinkTarget, "LINK 目标必须指回原帧对象。");
    }

    /// <summary>四种编辑轮换：覆盖快照里的顺序 / 坐标 / 尺寸 / 像素四类字段。</summary>
    private static void ApplyEdit(ImgFile image, int round)
    {
        switch (round % 4)
        {
            case 0:
                image.MoveFrame(0, image.Frames.Count - 1);
                break;
            case 1:
                var frame = image.Frames[1];
                image.SetFramePosition(frame, frame.X + 5 + round, frame.Y - 3 - round);
                break;
            case 2:
                image.ResizeFrame(image.Frames[0], 3, 5);
                break;
            default:
                image.ReplaceFrameImage(image.Frames[2], Solid(3, 3, new Color32(255, (byte)(round * 8), 0, 255)), ColorBits.ARGB_1555);
                break;
        }
    }

    /// <summary>
    ///     可比较的模型指纹：帧顺序 + 全部可变字段 + 解码后的像素字节。
    ///     <para><c>RawData</c> 用十六进制展开，保证是逐字节比较而不是"看起来一样"。</para>
    /// </summary>
    private static string Fingerprint(ImgFile image)
    {
        var builder = new StringBuilder();
        foreach (var frame in image.Frames)
        {
            builder.Append(frame.Index).Append(':')
                .Append((int)frame.Type).Append(':')
                .Append((int)frame.Compress).Append(':')
                .Append(frame.X).Append(',').Append(frame.Y).Append(',')
                .Append(frame.Width).Append(',').Append(frame.Height).Append(',')
                .Append(frame.Length).Append(',')
                .Append(frame.FrameWidth).Append(',').Append(frame.FrameHeight).Append(':')
                .Append(Convert.ToHexString(frame.RawData)).Append(':')
                .Append(frame.LinkTarget?.Index.ToString() ?? "-").Append(':');

            var decoded = image.Decode(frame);
            builder.Append(decoded is null
                ? "-"
                : $"{decoded.Width}x{decoded.Height}:{Convert.ToHexString(decoded.Pixels)}");

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>构造 v2 + ARGB1555 + ZLIB 的合成 IMG，每帧像素互不相同。</summary>
    private static ImgFile CreateImage(int frameCount, int width = 4, int height = 4)
    {
        var image = ImgFile.Create(ImgVersion.Ver2, "history.img");
        for (var i = 0; i < frameCount; i++)
        {
            var frame = image.AddFrame();
            frame.Type = ColorBits.ARGB_1555;
            frame.Compress = CompressMode.ZLIB;
            frame.Width = width;
            frame.Height = height;
            frame.FrameWidth = width;
            frame.FrameHeight = height;
            frame.X = i * 3;
            frame.Y = -i;

            var raw = new byte[width * height * 2];
            for (var k = 0; k < raw.Length; k++)
            {
                raw[k] = (byte)((i * 31) + (k * 7));
            }

            frame.RawData = ZlibCodec.Compress(raw);
            frame.Length = frame.RawData.Length;
        }

        return image;
    }

    private static RgbaImage Solid(int width, int height, Color32 color)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = color.A;
        }

        return RgbaImage.FromBgra(pixels, width, height);
    }
}
