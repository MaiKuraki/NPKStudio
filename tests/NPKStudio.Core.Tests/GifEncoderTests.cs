using System.Diagnostics;
using System.Text;

using NPKStudio.Core.Imaging;
using NPKStudio.Core.Imaging.Gif;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     GIF89a 编码器的结构与像素正确性。
///     <para>
///         为了真正验证 LZW（而不是只检查文件头），本文件内含一个只服务测试的 GIF 解码器：
///         编码后立刻解回来逐像素比对。测试解码器不参与生产代码。
///     </para>
/// </summary>
[TestClass]
public sealed class GifEncoderTests
{
    [TestMethod]
    public void Encode_WritesGif89aHeaderLogicalScreenAndTrailer()
    {
        var small = Solid(4, 3, new Color32(10, 20, 30, 255));
        var wide = Solid(7, 2, new Color32(40, 50, 60, 255));

        var bytes = GifEncoder.Encode([small, wide], delayMilliseconds: 100, loopCount: 0);
        var gif = DecodeGif(bytes);

        Assert.AreEqual("GIF89a", Encoding.ASCII.GetString(bytes, 0, 6));
        Assert.AreEqual(7, gif.Width, "逻辑屏宽取所有帧的最大宽度。");
        Assert.AreEqual(3, gif.Height, "逻辑屏高取所有帧的最大高度。");
        Assert.AreEqual(2, gif.Frames.Count);
        Assert.AreEqual(4, gif.Frames[0].Width);
        Assert.AreEqual(3, gif.Frames[0].Height);
        Assert.AreEqual(7, gif.Frames[1].Width);
        Assert.AreEqual(2, gif.Frames[1].Height);
        Assert.AreEqual(0x3B, bytes[^1], "必须以 trailer 0x3B 结束。");
    }

    [TestMethod]
    public void Encode_SingleFrame_RoundTripsExactly()
    {
        var image = new RgbaImage(8, 8);
        var colors = new[]
        {
            new Color32(255, 0, 0, 255),
            new Color32(0, 255, 0, 255),
            new Color32(0, 0, 255, 255),
            new Color32(255, 255, 0, 255)
        };
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                image.SetPixel(x, y, colors[(x + y) & 3]);
            }
        }

        var gif = DecodeGif(GifEncoder.Encode([image], 100, 0));

        Assert.AreEqual(1, gif.Frames.Count);
        AssertExact(image, gif.Frames[0].Image, "单帧");
    }

    [TestMethod]
    public void Encode_MultiFrame_RoundTripsEachFrame()
    {
        var frames = new List<RgbaImage>();
        for (var i = 0; i < 3; i++)
        {
            var image = new RgbaImage(6, 6);
            var color = new Color32((byte)(60 * i), (byte)(255 - (50 * i)), (byte)(30 * i), 255);
            image.SetPixel(i, i, color);
            image.SetPixel(5 - i, i, color);
            frames.Add(image);
        }

        var gif = DecodeGif(GifEncoder.Encode(frames, 80, 2));

        Assert.AreEqual(3, gif.Frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            AssertExact(frames[i], gif.Frames[i].Image, $"帧 {i}");
        }
    }

    [TestMethod]
    public void Encode_TransparentPixels_SetGceFlagAndDecodeToAlphaZero()
    {
        var image = new RgbaImage(4, 4);
        var red = new Color32(200, 10, 10, 255);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                image.SetPixel(x, y, red);
            }
        }

        // 右半保持 (0,0,0,0) 全透明。
        var gif = DecodeGif(GifEncoder.Encode([image], 100, 0));
        var frame = gif.Frames[0];

        Assert.IsTrue(frame.HasTransparency, "存在 alpha<128 的像素时必须置透明标志。");
        Assert.IsTrue(frame.TransparentIndex >= 0 && frame.TransparentIndex < frame.TableSize);
        Assert.AreEqual(2, frame.Disposal, "处置方法应为 2（恢复背景）。");

        for (var y = 0; y < 4; y++)
        {
            Assert.AreEqual(0, frame.Image.GetPixel(3, y).A, "透明像素应解码为 alpha=0。");
            Assert.AreEqual(red, frame.Image.GetPixel(0, y));
        }
    }

    [TestMethod]
    public void Encode_DelayAndLoop_AreWrittenInGceAndNetscapeBlock()
    {
        var image = Solid(3, 3, new Color32(1, 2, 3, 255));

        var gif = DecodeGif(GifEncoder.Encode([image], 50, 3));

        Assert.AreEqual(3, gif.LoopCount, "NETSCAPE 子块里的循环次数。");
        Assert.AreEqual(5, gif.Frames[0].Delay, "50ms = 5 × 10ms。");

        // 越界值夹到 20..1000ms。
        var clampedLow = DecodeGif(GifEncoder.Encode([image], 1, 0));
        Assert.AreEqual(2, clampedLow.Frames[0].Delay);
        var clampedHigh = DecodeGif(GifEncoder.Encode([image], 5000, 0));
        Assert.AreEqual(100, clampedHigh.Frames[0].Delay);
    }

    [TestMethod]
    public void Encode_FullyTransparentFrame_ProducesValidTwoColorTable()
    {
        var image = new RgbaImage(3, 2); // 构造后即全透明

        var gif = DecodeGif(GifEncoder.Encode([image], 100, 0));
        var frame = gif.Frames[0];

        Assert.AreEqual(2, frame.TableSize, "全透明帧仍需要 2 项占位色表（6 字节）。");
        Assert.IsTrue(frame.HasTransparency);
        Assert.AreEqual(0, frame.TransparentIndex);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                Assert.AreEqual(0, frame.Image.GetPixel(x, y).A);
            }
        }
    }

    [TestMethod]
    public void Encode_TwoColorFrame_UsesMinimumCodeSizeTwo()
    {
        var image = new RgbaImage(2, 2);
        image.SetPixel(0, 0, new Color32(0, 0, 0, 255));
        image.SetPixel(1, 0, new Color32(255, 255, 255, 255));
        image.SetPixel(0, 1, new Color32(255, 255, 255, 255));
        image.SetPixel(1, 1, new Color32(0, 0, 0, 255));

        var gif = DecodeGif(GifEncoder.Encode([image], 100, 0));

        Assert.AreEqual(2, gif.Frames[0].TableSize, "2 色的色表长度应为 2。");
        Assert.AreEqual(2, gif.Frames[0].MinCodeSize, "GIF 规定最小码宽不得小于 2。");
        AssertExact(image, gif.Frames[0].Image, "2 色帧");
    }

    [TestMethod]
    public void Encode_Over256Colors_QuantizesWithinTolerance()
    {
        var image = new RgbaImage(64, 64);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                image.SetPixel(x, y, new Color32((byte)(x * 4), (byte)(y * 4), 128, 255));
            }
        }

        var gif = DecodeGif(GifEncoder.Encode([image], 100, 0));
        var frame = gif.Frames[0];

        Assert.AreEqual(256, frame.TableSize, "量化后色表应占满 256 槽。");
        var maxError = 0;
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var expected = image.GetPixel(x, y);
                var actual = frame.Image.GetPixel(x, y);
                maxError = Math.Max(maxError, Math.Abs(expected.R - actual.R));
                maxError = Math.Max(maxError, Math.Abs(expected.G - actual.G));
                maxError = Math.Max(maxError, Math.Abs(expected.B - actual.B));
            }
        }

        Assert.IsTrue(maxError <= 40, $"中位切分后单通道最大误差应 ≤ 40，实际 {maxError}。");
    }

    [TestMethod]
    public void Encode_NoisyFrame_FillsLzwDictionaryAndStillDecodes()
    {
        // 128×128 = 16384 个像素、256 种颜色且排列近似随机：LZW 字典必然会写满并触发 Clear Code。
        var image = new RgbaImage(128, 128);
        var palette = new Color32[256];
        for (var i = 0; i < palette.Length; i++)
        {
            palette[i] = new Color32((byte)i, (byte)((i * 7) & 0xFF), (byte)((i * 13) & 0xFF), 255);
        }

        var state = 123456789u;
        for (var y = 0; y < 128; y++)
        {
            for (var x = 0; x < 128; x++)
            {
                state = (state * 1664525u) + 1013904223u;
                image.SetPixel(x, y, palette[(state >> 16) & 0xFF]);
            }
        }

        var gif = DecodeGif(GifEncoder.Encode([image], 100, 0));

        Assert.AreEqual(256, gif.Frames[0].TableSize);
        AssertExact(image, gif.Frames[0].Image, "随机 256 色帧");
    }

    [TestMethod]
    public void Encode_500Frames_128x128_PrintsElapsedTime()
    {
        // 8 张图案轮换使用，避免为了测编码器而分配 500 × 64KB 的源图。
        var patterns = new List<RgbaImage>(8);
        for (var p = 0; p < 8; p++)
        {
            var image = new RgbaImage(128, 128);
            for (var y = 0; y < 128; y++)
            {
                for (var x = 0; x < 128; x++)
                {
                    var index = ((x + y + p) & 3) + (((x * y) & 7) << 2);
                    image.SetPixel(x, y, new Color32((byte)(index * 9), (byte)(255 - (index * 7)), (byte)(index * 3), 255));
                }
            }

            patterns.Add(image);
        }

        var frames = new List<RgbaImage>(500);
        for (var i = 0; i < 500; i++)
        {
            frames.Add(patterns[i & 7]);
        }

        var stopwatch = Stopwatch.StartNew();
        var bytes = GifEncoder.Encode(frames, 40, 0);
        stopwatch.Stop();

        Console.WriteLine($"[GIF] 500 帧 128×128 编码耗时 {stopwatch.ElapsedMilliseconds} ms，输出 {bytes.Length} 字节。");
        Assert.IsTrue(bytes.Length > 0);
        Assert.AreEqual(0x3B, bytes[^1]);
    }

    private static RgbaImage Solid(int width, int height, Color32 color)
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

    /// <summary>逐像素比对：RGB 必须一致；alpha 只有 0 / 255 两种可能（GIF 无半透明）。</summary>
    private static void AssertExact(RgbaImage expected, RgbaImage actual, string message)
    {
        Assert.AreEqual(expected.Width, actual.Width, $"{message}：宽");
        Assert.AreEqual(expected.Height, actual.Height, $"{message}：高");
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var e = expected.GetPixel(x, y);
                var a = actual.GetPixel(x, y);
                if (e.A < 128)
                {
                    Assert.AreEqual(0, a.A, $"{message} ({x},{y})：透明像素");
                    continue;
                }

                Assert.AreEqual(255, a.A, $"{message} ({x},{y})：不透明像素");
                Assert.AreEqual(e.R, a.R, $"{message} ({x},{y})：R");
                Assert.AreEqual(e.G, a.G, $"{message} ({x},{y})：G");
                Assert.AreEqual(e.B, a.B, $"{message} ({x},{y})：B");
            }
        }
    }

    private sealed class DecodedGif
    {
        internal int Width;
        internal int Height;
        internal int LoopCount;
        internal List<DecodedFrame> Frames { get; } = [];
    }

    private sealed class DecodedFrame
    {
        internal int Width;
        internal int Height;
        internal int Delay;
        internal int Disposal;
        internal bool HasTransparency;
        internal int TransparentIndex;
        internal int TableSize;
        internal int MinCodeSize;
        internal RgbaImage Image { get; set; } = null!;
    }

    /// <summary>
    ///     测试用 GIF 解析器：只实现本编码器会输出的块，遇到未知块直接失败（避免静默跳过错误结构）。
    /// </summary>
    private static DecodedGif DecodeGif(byte[] data)
    {
        Assert.IsTrue(data.Length > 13, "GIF 太短。");
        Assert.AreEqual("GIF89a", Encoding.ASCII.GetString(data, 0, 6));

        var pos = 6;
        var gif = new DecodedGif
        {
            Width = ReadUInt16(data, ref pos),
            Height = ReadUInt16(data, ref pos)
        };

        var screenPacked = data[pos++];
        pos += 2; // 背景色索引 + 像素宽高比
        if ((screenPacked & 0x80) != 0)
        {
            pos += 3 * (1 << ((screenPacked & 0x07) + 1));
        }

        var delay = 0;
        var disposal = 0;
        var transparentIndex = -1;
        var hasTransparency = false;

        while (pos < data.Length)
        {
            var marker = data[pos++];
            if (marker == 0x3B)
            {
                break;
            }

            if (marker == 0x21)
            {
                var label = data[pos++];
                if (label == 0xF9)
                {
                    var size = data[pos++];
                    var flags = data[pos++];
                    disposal = (flags >> 2) & 0x07;
                    hasTransparency = (flags & 0x01) != 0;
                    delay = ReadUInt16(data, ref pos);
                    transparentIndex = data[pos++];
                    pos += size - 4;
                    Assert.AreEqual(0, data[pos++], "GCE 块终止符");
                }
                else if (label == 0xFF)
                {
                    var size = data[pos++];
                    var name = Encoding.ASCII.GetString(data, pos, size);
                    pos += size;
                    if (name == "NETSCAPE2.0")
                    {
                        while (true)
                        {
                            var n = data[pos++];
                            if (n == 0)
                            {
                                break;
                            }

                            if (n >= 3 && data[pos] == 1)
                            {
                                gif.LoopCount = data[pos + 1] | (data[pos + 2] << 8);
                            }

                            pos += n;
                        }
                    }
                    else
                    {
                        SkipSubBlocks(data, ref pos);
                    }
                }
                else
                {
                    SkipSubBlocks(data, ref pos);
                }

                continue;
            }

            Assert.AreEqual(0x2C, marker, $"未知块标记 0x{marker:X2}");
            var left = ReadUInt16(data, ref pos);
            var top = ReadUInt16(data, ref pos);
            var width = ReadUInt16(data, ref pos);
            var height = ReadUInt16(data, ref pos);
            Assert.AreEqual(0, left);
            Assert.AreEqual(0, top);

            var imagePacked = data[pos++];
            Assert.AreEqual(1, (imagePacked >> 7) & 1, "每帧都应带本地色表。");
            var tableSize = 1 << ((imagePacked & 0x07) + 1);

            var palette = new byte[tableSize * 3];
            Array.Copy(data, pos, palette, 0, palette.Length);
            pos += palette.Length;

            var minCodeSize = data[pos++];
            using var compressed = new MemoryStream();
            while (true)
            {
                var n = data[pos++];
                if (n == 0)
                {
                    break;
                }

                compressed.Write(data, pos, n);
                pos += n;
            }

            var indices = LzwDecode(compressed.ToArray(), minCodeSize, width * height);

            var image = new RgbaImage(width, height);
            for (var i = 0; i < indices.Length; i++)
            {
                var index = indices[i];
                Assert.IsTrue(index < tableSize, $"色号 {index} 超出本地色表 {tableSize}。");
                var x = i % width;
                var y = i / width;
                if (hasTransparency && index == transparentIndex)
                {
                    continue; // RgbaImage 初始即全透明
                }

                var offset = index * 3;
                image.SetPixel(x, y, new Color32(palette[offset], palette[offset + 1], palette[offset + 2], 255));
            }

            gif.Frames.Add(new DecodedFrame
            {
                Width = width,
                Height = height,
                Delay = delay,
                Disposal = disposal,
                HasTransparency = hasTransparency,
                TransparentIndex = transparentIndex,
                TableSize = tableSize,
                MinCodeSize = minCodeSize,
                Image = image
            });

            delay = 0;
            disposal = 0;
            transparentIndex = -1;
            hasTransparency = false;
        }

        return gif;
    }

    private static void SkipSubBlocks(byte[] data, ref int pos)
    {
        while (true)
        {
            var n = data[pos++];
            if (n == 0)
            {
                return;
            }

            pos += n;
        }
    }

    private static int ReadUInt16(byte[] data, ref int pos)
    {
        var value = data[pos] | (data[pos + 1] << 8);
        pos += 2;
        return value;
    }

    /// <summary>
    ///     测试用 LZW 解码器（与生产编码器独立实现），用来证明编码结果真的可解。
    /// </summary>
    private static byte[] LzwDecode(byte[] data, int minCodeSize, int pixelCount)
    {
        var clearCode = 1 << minCodeSize;
        var endCode = clearCode + 1;
        var codeSize = minCodeSize + 1;
        var nextCode = endCode + 1;

        var prefix = new int[4096];
        var suffix = new byte[4096];
        var first = new byte[4096];
        for (var i = 0; i < clearCode; i++)
        {
            prefix[i] = -1;
            suffix[i] = (byte)i;
            first[i] = (byte)i;
        }

        var result = new byte[pixelCount];
        var outPos = 0;
        var stack = new byte[4096];
        var bitPos = 0;
        var old = -1;

        while (true)
        {
            var code = ReadCode(data, ref bitPos, codeSize);
            if (code < 0)
            {
                break;
            }

            if (code == clearCode)
            {
                codeSize = minCodeSize + 1;
                nextCode = endCode + 1;
                old = -1;
                continue;
            }

            if (code == endCode)
            {
                break;
            }

            if (old < 0)
            {
                Assert.IsTrue(code < clearCode, "Clear 之后的第一个码必须是字面量。");
                result[outPos++] = (byte)code;
                old = code;
                continue;
            }

            var stackPos = 0;
            var current = code;
            if (code >= nextCode)
            {
                Assert.AreEqual(nextCode, code, "码值不能超过 nextCode。");
                stack[stackPos++] = first[old];
                current = old;
            }

            while (current >= clearCode)
            {
                stack[stackPos++] = suffix[current];
                current = prefix[current];
            }

            var head = (byte)current;
            stack[stackPos++] = head;

            for (var i = stackPos - 1; i >= 0 && outPos < pixelCount; i--)
            {
                result[outPos++] = stack[i];
            }

            if (nextCode < 4096)
            {
                prefix[nextCode] = old;
                suffix[nextCode] = head;
                first[nextCode] = first[old];
                nextCode++;
                if (nextCode == (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                }
            }

            old = code;
        }

        Assert.AreEqual(pixelCount, outPos, "LZW 解出的像素数不符。");
        return result;
    }

    private static int ReadCode(byte[] data, ref int bitPos, int codeSize)
    {
        if (bitPos + codeSize > data.Length * 8)
        {
            return -1;
        }

        var value = 0;
        for (var i = 0; i < codeSize; i++)
        {
            var absolute = bitPos + i;
            if ((data[absolute >> 3] & (1 << (absolute & 7))) != 0)
            {
                value |= 1 << i;
            }
        }

        bitPos += codeSize;
        return value;
    }
}
