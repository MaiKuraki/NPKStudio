using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Formats;
using NPKStudio.Core.Imaging;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     IMG v5（DDS 纹理图集）：纹理表解析、UV 裁剪、DXT1/3/5 解码、写回、编辑链路与容错。
///     <para>
///         <b>没有真实 v5 样本</b>：仓库自带的合成样本全是 v2，开发期接触过的包也都是 v2。
///         因此所有样本都用 <see cref="TestImageBuilder.BuildVer5" /> 逐字节构造，
///         字节布局依据旧实现 <c>ExtractorSharp.Core/Handle/FifthHandler.cs</c>（见 <c>Ver5Handler</c> 注释）。
///         <b>"能自洽往返"不等于"与真实文件字节一致"</b>，这一点在测试结论里必须如实说明。
///     </para>
/// </summary>
[TestClass]
public sealed class Ver5Tests
{
    private const int TextureEntrySize = 28;
    private const int FrameIndexSize = 36;
    private const int TextureUvSize = 28;
    private const int LinkIndexSize = 8;

    private static readonly Color32 Red = new(255, 0, 0, 255);

    [TestMethod]
    public void Ver5_Read_TextureTablePaletteAndFrameUv()
    {
        var page = Page(8, 8);
        var data = TestImageBuilder.BuildVer5(
        [
            TextureFrame(3, 2, x: -4, y: 9, frameWidth: 20, frameHeight: 20, uv: Uv(0, 2, 1, 5, 3))
        ],
        [TextureOf(page, 0)],
        [Red]);

        var image = ImgFile.Read(new MemoryStream(data), "v5.img", data.Length);

        Assert.AreEqual(ImgVersion.Ver5, image.Version);
        Assert.AreEqual(1, image.Frames.Count, "帧数");
        Assert.AreEqual((long)(FrameIndexSize + TextureUvSize), image.IndexLength,
            "IndexLength 只含帧索引区（36 + 28 的 UV 扩展），不含色表与纹理表。");

        Assert.AreEqual(1, image.Palette.Count, "v5 也有色表区");
        Assert.AreEqual(Red, image.Palette[0]);

        Assert.AreEqual(1, image.Textures.Count, "纹理表项数");
        var texture = image.Textures[0];
        Assert.AreEqual(8, texture.Width);
        Assert.AreEqual(8, texture.Height);
        Assert.AreEqual(ColorBits.ARGB_1555, texture.Type);
        Assert.AreEqual(page.Width * page.Height * 2, texture.FullLength, "解压长度 = 宽 × 高 × 2（1555）");
        Assert.AreEqual(texture.FullLength, ColorCodec.BytesPerPixel(texture.Type) * page.Width * page.Height);
        Assert.IsTrue(texture.Data.Length > 0, "纹理数据必须是文件里的 zlib 流");

        var frame = image.Frames[0];
        Assert.AreEqual(ColorBits.ARGB_1555, frame.Type);
        Assert.AreEqual(CompressMode.DDS_ZLIB, frame.Compress);
        Assert.AreEqual(3, frame.Width, "帧宽以 UV 子矩形为准");
        Assert.AreEqual(2, frame.Height);
        Assert.AreEqual(-4, frame.X, "负坐标必须原样保留");
        Assert.AreEqual(9, frame.Y);
        Assert.AreEqual(20, frame.FrameWidth);
        Assert.AreEqual(20, frame.FrameHeight);
        Assert.AreEqual(0, frame.Length, "纹理帧没有帧级数据长度");
        CollectionAssert.AreEqual(Array.Empty<byte>(), frame.RawData);

        Assert.IsNotNull(frame.Texture);
        Assert.AreEqual(0, frame.Texture!.TextureIndex);
        Assert.AreEqual(new PixelBounds(2, 1, 3, 2), frame.Texture.Rect);
        Assert.AreEqual(0, frame.Texture.Unknown);
        Assert.AreEqual(0, frame.Texture.Rotate);

        // 解码结果必须是"裁出来的子矩形"，尺寸与帧几何一致，内容来自纹理页的 (2,1)。
        var decoded = image.Decode(frame)!;
        Assert.AreEqual(3, decoded.Width, "解码宽 = UV 子矩形宽（不是整页宽）");
        Assert.AreEqual(2, decoded.Height);
        Assert.AreEqual(PageColor(2, 1), decoded.GetPixel(0, 0), "子矩形左上角对应纹理页的 (2,1)");
        Assert.AreEqual(PageColor(4, 2), decoded.GetPixel(2, 1), "子矩形右下角对应纹理页的 (4,2)");
    }

    [TestMethod]
    public void Ver5_Decode_CropsSubRectFromSharedTexture()
    {
        var page = Page(4, 4);
        var data = TestImageBuilder.BuildVer5(
        [
            TextureFrame(2, 2, uv: Uv(0, 0, 0, 2, 2)),
            TextureFrame(2, 2, uv: Uv(0, 2, 2, 4, 4))
        ],
        [TextureOf(page, 0)]);

        var image = ImgFile.Read(new MemoryStream(data), "v5-shared.img", data.Length);

        Assert.AreEqual(1, image.Textures.Count, "多帧共用同一张纹理页");
        Assert.AreEqual(image.Frames[0].Texture!.TextureIndex, image.Frames[1].Texture!.TextureIndex);

        var first = image.Decode(image.Frames[0])!;
        var second = image.Decode(image.Frames[1])!;

        Assert.AreEqual(2, first.Width);
        Assert.AreEqual(2, first.Height);
        Assert.AreEqual(2, second.Width);
        Assert.AreEqual(2, second.Height);

        // 子矩形 (0,0)-(2,2) 与 (2,2)-(4,4) 必须裁出各自的像素，不能串位。
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                Assert.AreEqual(PageColor(x, y), first.GetPixel(x, y), $"左上象限 ({x},{y})");
                Assert.AreEqual(PageColor(x + 2, y + 2), second.GetPixel(x, y), $"右下象限 ({x},{y})");
            }
        }

        Assert.AreNotEqual(first.GetPixel(0, 0), second.GetPixel(0, 0), "两个子矩形的内容必须不同，否则裁剪测试没有意义。");
    }

    [TestMethod]
    public void Ver5_Decode_Dxt1_KnownVector()
    {
        // 已知向量与 DxtDecoderTests 相同：c0 = 0xF800（红）、c1 = 0x001F（蓝），
        // 索引字 0xE4E4E4E4 → 每行下标 0,1,2,3；c0 > c1 故 4 色模式。
        byte[] block =
        [
            0x00, 0xF8,
            0x1F, 0x00,
            0xE4, 0xE4, 0xE4, 0xE4
        ];

        var page = DdsTexture(DxtFormat.Dxt1, 4, 4, block, ColorBits.DXT_1, version: 1);
        var image = ReadSingleTextureImage(page);

        var decoded = image.Decode(image.Frames[0])!;

        Assert.AreEqual(4, decoded.Width);
        Assert.AreEqual(4, decoded.Height);
        Assert.AreEqual(new Color32(255, 0, 0, 255), decoded.GetPixel(0, 0), "x=0 是 color0（红）");
        Assert.AreEqual(new Color32(0, 0, 255, 255), decoded.GetPixel(1, 0), "x=1 是 color1（蓝）");
        Assert.AreEqual(new Color32(170, 0, 85, 255), decoded.GetPixel(2, 0), "x=2 是 2/3 插值");
        Assert.AreEqual(new Color32(85, 0, 170, 255), decoded.GetPixel(3, 0), "x=3 是 1/3 插值");
        Assert.AreEqual(new Color32(255, 0, 0, 255), decoded.GetPixel(0, 3), "块内每行相同");
    }

    [TestMethod]
    public void Ver5_Decode_Dxt3_KnownVector()
    {
        // alpha 半字节序列 1,2,3,…,F,0；色彩块同上（DXT3 恒 4 色模式）。
        byte[] block =
        [
            0x21, 0x43,
            0x65, 0x87,
            0xA9, 0xCB,
            0xED, 0x0F,
            0x00, 0xF8,
            0x1F, 0x00,
            0xE4, 0xE4, 0xE4, 0xE4
        ];

        var page = DdsTexture(DxtFormat.Dxt3, 4, 4, block, ColorBits.DXT_3, version: 3);
        var image = ReadSingleTextureImage(page);

        var decoded = image.Decode(image.Frames[0])!;

        Assert.AreEqual(new Color32(255, 0, 0, 17), decoded.GetPixel(0, 0), "像素 0：alpha 0x11");
        Assert.AreEqual(new Color32(0, 0, 255, 34), decoded.GetPixel(1, 0), "像素 1：alpha 0x22");
        Assert.AreEqual(new Color32(85, 0, 170, 0), decoded.GetPixel(3, 3), "像素 15：alpha 半字节 0 → 全透明");
        Assert.AreEqual(51, (int)decoded.GetPixel(2, 0).A, "像素 2 的 alpha 必须是 0x33");
    }

    [TestMethod]
    public void Ver5_Decode_Dxt5_KnownVector()
    {
        // a0 = 0xFF, a1 = 0x00（8 值插值模式）→ table = {255,0,218,182,145,109,72,36}；
        // 3 位索引 0..7 循环，色彩块下标 0,1,2,3。
        byte[] block =
        [
            0xFF, 0x00,
            0x88, 0xC6, 0xFA, 0x88, 0xC6, 0xFA,
            0x00, 0xF8,
            0x1F, 0x00,
            0xE4, 0xE4, 0xE4, 0xE4
        ];

        var page = DdsTexture(DxtFormat.Dxt5, 4, 4, block, ColorBits.DXT_5, version: 5);
        var image = ReadSingleTextureImage(page);

        var decoded = image.Decode(image.Frames[0])!;

        Assert.AreEqual(new Color32(255, 0, 0, 255), decoded.GetPixel(0, 0), "alpha 索引 0 → 255");
        Assert.AreEqual(new Color32(0, 0, 255, 0), decoded.GetPixel(1, 0), "alpha 索引 1 → 0");
        Assert.AreEqual(new Color32(170, 0, 85, 218), decoded.GetPixel(2, 0), "alpha 索引 2 → 218");
        Assert.AreEqual(new Color32(85, 0, 170, 182), decoded.GetPixel(3, 0), "alpha 索引 3 → 182");
    }

    [TestMethod]
    public void Ver5_RotateFlag_IsPreservedButNotApplied()
    {
        // Rotate 是旧实现的 top 字段（旧代码非 0 时 Rotate90FlipXY）。规范语义下 DXT 块本身是正立的，
        // 旋转只是掩盖旧解码器行列颠倒的补丁（D-18），因此新实现只保留字段、不旋转像素。
        var page = Page(4, 4);
        var data = TestImageBuilder.BuildVer5(
        [
            TextureFrame(4, 4, uv: new TestImageBuilder.TestUv
            {
                Unknown = 5,
                TextureIndex = 0,
                LeftUpX = 0,
                LeftUpY = 0,
                RightDownX = 4,
                RightDownY = 4,
                Rotate = 7
            })
        ],
        [TextureOf(page, 0)]);

        var image = ImgFile.Read(new MemoryStream(data), "v5-rotate.img", data.Length);
        var frame = image.Frames[0];

        Assert.AreEqual(7, frame.Texture!.Rotate, "Rotate 字段必须原样保留（丢了索引区字节往返就崩）");
        Assert.AreEqual(5, frame.Texture.Unknown, "Unknown 字段同样原样保留");

        var decoded = image.Decode(frame)!;
        Assert.AreEqual(4, decoded.Width);
        Assert.AreEqual(4, decoded.Height);
        Assert.AreEqual(PageColor(0, 0), decoded.GetPixel(0, 0), "未旋转：左上角仍是原图 (0,0)");
        Assert.AreEqual(PageColor(3, 3), decoded.GetPixel(3, 3), "未旋转：右下角仍是原图 (3,3)");

        var reread = RoundTrip(image, "v5-rotate.img");
        Assert.AreEqual(7, reread.Frames[0].Texture!.Rotate, "写回后 Rotate 仍然保留");
        CollectionAssert.AreEqual(decoded.Pixels, reread.Decode(reread.Frames[0])!.Pixels, "写回前后像素一致");
    }

    [TestMethod]
    public void Ver5_WriteThenRead_PreservesAtlasLayoutAndPixels()
    {
        // 一张 DXT1 纹理 + 两个共用它的帧：读→写必须复用同一张纹理页，不能变成"每帧一张"。
        byte[] block = [0x00, 0xF8, 0x1F, 0x00, 0xE4, 0xE4, 0xE4, 0xE4];
        var data = TestImageBuilder.BuildVer5(
        [
            TextureFrame(2, 2, x: 1, y: -3, frameWidth: 9, frameHeight: 9, uv: Uv(0, 0, 0, 2, 2)),
            TextureFrame(2, 2, x: -7, y: 4, frameWidth: 9, frameHeight: 9, uv: Uv(0, 2, 2, 4, 4))
        ],
        [DdsTexture(DxtFormat.Dxt1, 4, 4, block, ColorBits.DXT_1, version: 1)]);

        var image = ImgFile.Read(new MemoryStream(data), "v5-roundtrip.img", data.Length);
        var written = image.ToArray();
        var reread = ImgFile.Read(new MemoryStream(written), "v5-roundtrip.img", written.Length);

        Assert.AreEqual(2, reread.Frames.Count, "帧数");
        Assert.AreEqual(1, reread.Textures.Count, "纹理页数（读→写必须复用原图集，不新增纹理）");

        for (var i = 0; i < 2; i++)
        {
            Assert.AreEqual(image.Frames[i].Texture!.TextureIndex, reread.Frames[i].Texture!.TextureIndex, $"帧 {i} 的纹理下标");
            Assert.AreEqual(image.Frames[i].Texture!.Rect, reread.Frames[i].Texture!.Rect, $"帧 {i} 的 UV 子矩形");
            Assert.AreEqual(image.Frames[i].X, reread.Frames[i].X, $"帧 {i} 的 X");
            Assert.AreEqual(image.Frames[i].Y, reread.Frames[i].Y, $"帧 {i} 的 Y");

            // 尺寸断言不能省：只比对两次解码结果的话，"两边都忘了裁剪"也会通过。
            var expected = image.Decode(image.Frames[i])!;
            Assert.AreEqual(2, expected.Width, $"帧 {i} 解码宽必须是 UV 子矩形宽");
            Assert.AreEqual(2, expected.Height, $"帧 {i} 解码高必须是 UV 子矩形高");
            CollectionAssert.AreEqual(
                expected.Pixels,
                reread.Decode(reread.Frames[i])!.Pixels,
                $"帧 {i} 的像素（DXT 解码是确定性的，必须逐字节相同）");
        }

        CollectionAssert.AreEqual(image.Textures[0].Data, reread.Textures[0].Data, "未修改纹理页的压缩字节必须原样回放");
    }

    [TestMethod]
    public void Ver5_Write_Unmodified_IsByteIdentical()
    {
        // 纹理帧 + LINK + 内联 NONE 帧混合，覆盖三种索引项形态；"打开再另存"必须不动任何字节
        // （包括那个语义不明的长度字段：不认识就不要自己编一个值）。
        byte[] block = [0x00, 0xF8, 0x1F, 0x00, 0xE4, 0xE4, 0xE4, 0xE4];
        var inline = new RgbaImage(2, 1);
        inline.SetPixel(0, 0, PageColor(0, 0));
        inline.SetPixel(1, 0, PageColor(1, 0));

        var data = TestImageBuilder.BuildVer5(
        [
            TextureFrame(2, 2, x: 4, y: 5, frameWidth: 7, frameHeight: 7, uv: Uv(0, 0, 0, 2, 2)),
            new TestImageBuilder.TestFrame { Type = ColorBits.LINK, LinkTargetIndex = 0 },
            new TestImageBuilder.TestFrame
            {
                Type = ColorBits.ARGB_1555,
                Compress = CompressMode.NONE,
                Width = 2,
                Height = 1,
                Length = 4,
                X = -1,
                Y = -2,
                FrameWidth = 7,
                FrameHeight = 7,
                Data = ColorCodec.Encode(inline.Pixels, ColorBits.ARGB_1555)
            }
        ],
        [DdsTexture(DxtFormat.Dxt1, 4, 4, block, ColorBits.DXT_1, version: 1)],
        [Red]);

        var image = ImgFile.Read(new MemoryStream(data), "v5-identical.img", data.Length);
        var written = image.ToArray();

        CollectionAssert.AreEqual(data, written, "v5 读→写 必须逐字节一致（含纹理表、UV、LINK、内联帧）。");
    }

    [TestMethod]
    public void Ver5_CreateFromScratch_WritesAndReadsBack()
    {
        var image = ImgFile.Create(ImgVersion.Ver5, "new.img");
        var frame = image.AddFrame();

        image.ReplaceFrameImage(frame, Solid(3, 3, Red), ColorBits.UNKNOWN);

        Assert.AreEqual(1, image.Textures.Count, "新建帧的像素必须落成纹理页");
        Assert.IsNotNull(frame.Texture);
        Assert.AreEqual(0, image.Textures[0].FullLength - (3 * 3 * 2), "1555 未压缩长度 = 3×3×2");

        var reread = RoundTrip(image, "new.img");
        Assert.AreEqual(ImgVersion.Ver5, reread.Version);
        Assert.AreEqual(1, reread.Frames.Count);
        Assert.AreEqual(3, reread.Frames[0].Width);
        Assert.AreEqual(Red, reread.Decode(reread.Frames[0])!.GetPixel(2, 2));
    }

    [TestMethod]
    public void Ver5_LinkFrame_ResolvesAndSurvivesRoundTrip()
    {
        var data = TestImageBuilder.BuildVer5(
        [
            TextureFrame(2, 2, uv: Uv(0, 0, 0, 2, 2)),
            new TestImageBuilder.TestFrame { Type = ColorBits.LINK, LinkTargetIndex = 0 }
        ],
        [TextureOf(Page(4, 4), 0)]);

        var image = ImgFile.Read(new MemoryStream(data), "v5-link.img", data.Length);

        Assert.AreEqual(2, image.Frames.Count);
        Assert.IsTrue(image.Frames[1].IsLink);
        Assert.AreSame(image.Frames[0], image.Frames[1].LinkTarget);
        Assert.IsNull(image.Frames[1].Texture, "LINK 帧没有自己的 UV");
        Assert.IsNull(image.Decode(image.Frames[1]), "LINK 帧解码返回 null（D-04）");
        Assert.IsNull(image.GetUncompressedPixels(image.Frames[1]), "LINK 帧编码返回 null（D-04）");
        Assert.AreEqual((long)(FrameIndexSize + TextureUvSize + LinkIndexSize), image.IndexLength, "LINK 只占 8 字节");

        var reread = RoundTrip(image, "v5-link.img");
        Assert.IsTrue(reread.Frames[1].IsLink);
        Assert.AreSame(reread.Frames[0], reread.Frames[1].LinkTarget);
        CollectionAssert.AreEqual(
            image.Decode(image.Frames[0])!.Pixels,
            reread.Decode(reread.Frames[0])!.Pixels,
            "写回后帧 0 像素一致");
    }

    [TestMethod]
    public void Ver5_DeclaredTextureLengthBeyondStream_ThrowsImgFormatException()
    {
        // 纹理表声明 100+ 字节，但文件里只写入了真实压缩数据 → 读到数据区时必须报可读异常，不能越界读。
        var page = Page(4, 4);
        var texture = new TestImageBuilder.TestTexture
        {
            Type = ColorBits.ARGB_1555,
            Width = 4,
            Height = 4,
            Raw = ColorCodec.Encode(page.Pixels, ColorBits.ARGB_1555),
            LengthOverride = 4096
        };

        var data = TestImageBuilder.BuildVer5([TextureFrame(4, 4, uv: Uv(0, 0, 0, 4, 4))], [texture]);

        var ex = Assert.ThrowsExactly<ImgFormatException>(
            () => ImgFile.Read(new MemoryStream(data), "v5-bad-length.img", data.Length));

        StringAssert.Contains(ex.Message, "需要 4096 字节");
    }

    [TestMethod]
    public void Ver5_TexturePixelsShorterThanDeclaredSize_ThrowsImgFormatException()
    {
        // 声明 4×4 的 1555 纹理（需要 32 字节），实际只给了 4 个像素 → 解码时报格式错，而不是越界读。
        var texture = new TestImageBuilder.TestTexture
        {
            Type = ColorBits.ARGB_1555,
            Width = 4,
            Height = 4,
            Raw = new byte[8]
        };

        var data = TestImageBuilder.BuildVer5([TextureFrame(4, 4, uv: Uv(0, 0, 0, 4, 4))], [texture]);
        var image = ImgFile.Read(new MemoryStream(data), "v5-short-pixels.img", data.Length);

        var ex = Assert.ThrowsExactly<ImgFormatException>(() => image.Decode(image.Frames[0]));

        StringAssert.Contains(ex.Message, "需要 32 字节");
    }

    [TestMethod]
    public void Ver5_InlineNoneFrame_LengthDerivedAndNegativeCoords()
    {
        // v5 允许混入 v2 式内联帧（旧实现 type < LINK && length != 0 的分支）。
        var image = new RgbaImage(3, 2);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                image.SetPixel(x, y, PageColor(x, y));
            }
        }

        var frame = new TestImageBuilder.TestFrame
        {
            Type = ColorBits.ARGB_1555,
            Compress = CompressMode.NONE,
            Width = 3,
            Height = 2,
            Length = 1, // 故意写错：NONE 时必须用几何推导（3×2×2 = 12）
            X = -5,
            Y = -7,
            FrameWidth = 8,
            FrameHeight = 8,
            Data = ColorCodec.Encode(image.Pixels, ColorBits.ARGB_1555)
        };

        var data = TestImageBuilder.BuildVer5([frame], []);
        var parsed = ImgFile.Read(new MemoryStream(data), "v5-inline.img", data.Length);

        Assert.AreEqual(0, parsed.Textures.Count);
        Assert.AreEqual(12, parsed.Frames[0].Length, "NONE 时长度由 宽 × 高 × 2 推导");
        Assert.AreEqual(-5, parsed.Frames[0].X);
        Assert.AreEqual(-7, parsed.Frames[0].Y);
        Assert.IsNull(parsed.Frames[0].Texture);
        CollectionAssert.AreEqual(image.Pixels, parsed.Decode(parsed.Frames[0])!.Pixels, "内联帧像素");

        var reread = RoundTrip(parsed, "v5-inline.img");
        Assert.AreEqual(-5, reread.Frames[0].X);
        CollectionAssert.AreEqual(image.Pixels, reread.Decode(reread.Frames[0])!.Pixels, "内联帧写回后像素一致");
    }

    [TestMethod]
    public void Ver5_ReplaceFrameImage_WritesNewTextureAndKeepsSharedPage()
    {
        var page = Page(4, 4);
        var data = TestImageBuilder.BuildVer5(
        [
            TextureFrame(2, 2, uv: Uv(0, 0, 0, 2, 2)),
            TextureFrame(2, 2, uv: Uv(0, 2, 2, 4, 4))
        ],
        [TextureOf(page, 0)]);

        var image = ImgFile.Read(new MemoryStream(data), "v5-replace.img", data.Length);
        var expectedSecond = image.Decode(image.Frames[1])!;

        image.ReplaceFrameImage(image.Frames[0], Solid(2, 2, Red), ColorBits.UNKNOWN);

        Assert.AreEqual(2, image.Textures.Count, "换图新增一张纹理页，原共享页保持不变");
        Assert.AreEqual(1, image.Frames[0].Texture!.TextureIndex, "被替换的帧指向新页");
        Assert.AreEqual(0, image.Frames[1].Texture!.TextureIndex, "另一帧仍指向原页");
        Assert.AreEqual(new PixelBounds(0, 0, 2, 2), image.Frames[0].Texture!.Rect);
        Assert.AreEqual(ColorBits.ARGB_1555, image.Frames[0].Type, "v5 写回统一落成 1555（本轮不做 DXT 编码）");
        Assert.AreEqual(CompressMode.DDS_ZLIB, image.Frames[0].Compress);
        Assert.AreEqual(0, image.Frames[0].Length);

        Assert.AreEqual(Red, image.Decode(image.Frames[0])!.GetPixel(1, 1), "替换后的像素");
        CollectionAssert.AreEqual(expectedSecond.Pixels, image.Decode(image.Frames[1])!.Pixels, "共享页的那一帧不受影响");

        var reread = RoundTrip(image, "v5-replace.img");
        Assert.AreEqual(2, reread.Textures.Count);
        Assert.AreEqual(Red, reread.Decode(reread.Frames[0])!.GetPixel(0, 0), "写回后替换结果仍在");
        CollectionAssert.AreEqual(expectedSecond.Pixels, reread.Decode(reread.Frames[1])!.Pixels, "写回后共享页仍正确");
    }

    [TestMethod]
    public void Ver5_InsertFrame_ProducesTransparentTextureFrame()
    {
        var data = TestImageBuilder.BuildVer5(
            [TextureFrame(2, 2, uv: Uv(0, 0, 0, 2, 2))],
            [TextureOf(Page(4, 4), 0)]);

        var image = ImgFile.Read(new MemoryStream(data), "v5-insert.img", data.Length);
        var inserted = image.InsertFrame(0);

        Assert.AreEqual(2, image.Frames.Count);
        Assert.IsNotNull(inserted.Texture, "v5 的插入帧必须落成纹理帧");
        Assert.AreEqual(1, inserted.Width);
        Assert.AreEqual(1, inserted.Height);
        Assert.AreEqual(0, image.Decode(inserted)!.GetPixel(0, 0).A, "插入帧必须是全透明");
        Assert.AreEqual(2, image.Textures.Count);

        var reread = RoundTrip(image, "v5-insert.img");
        Assert.AreEqual(2, reread.Frames.Count);
        Assert.AreEqual(0, reread.Decode(reread.Frames[0])!.GetPixel(0, 0).A, "写回后插入帧仍可解码且透明");
        Assert.AreEqual(2, reread.Frames[1].Width, "原帧后移一位");
    }

    [TestMethod]
    public void Ver5_TrimAndResizeFrame_GoThroughTexturePath()
    {
        // 4×4 页里只有中间 2×2 不透明，裁剪后应为 2×2，并把偏移累加进 X/Y。
        var page = new RgbaImage(4, 4);
        for (var y = 1; y <= 2; y++)
        {
            for (var x = 1; x <= 2; x++)
            {
                page.SetPixel(x, y, Red);
            }
        }

        var data = TestImageBuilder.BuildVer5(
            [TextureFrame(4, 4, x: 10, y: 20, frameWidth: 30, frameHeight: 30, uv: Uv(0, 0, 0, 4, 4))],
            [TextureOf(page, 0)]);

        var image = ImgFile.Read(new MemoryStream(data), "v5-trim.img", data.Length);

        Assert.IsTrue(image.TrimFrame(image.Frames[0]));
        Assert.AreEqual(11, image.Frames[0].X, "X 必须累加裁剪偏移");
        Assert.AreEqual(21, image.Frames[0].Y);
        Assert.AreEqual(2, image.Frames[0].Width);
        Assert.AreEqual(2, image.Frames[0].Height);
        Assert.AreEqual(2, image.Textures.Count, "裁剪后落成新纹理页");
        Assert.AreEqual(Red, image.Decode(image.Frames[0])!.GetPixel(0, 0));

        Assert.IsTrue(image.ResizeFrame(image.Frames[0], 4, 4));
        Assert.AreEqual(4, image.Frames[0].Width);
        Assert.AreEqual(4, image.Frames[0].Height);
        Assert.AreEqual(4, image.Decode(image.Frames[0])!.Width);

        var reread = RoundTrip(image, "v5-trim.img");
        Assert.AreEqual(4, reread.Frames[0].Width, "写回后尺寸一致");
        Assert.AreEqual(4, reread.Decode(reread.Frames[0])!.Width, "写回后可解码");
    }

    [TestMethod]
    public void Ver5_UvOutsideTexture_ThrowsImgFormatException()
    {
        var data = TestImageBuilder.BuildVer5(
            [TextureFrame(2, 2, uv: Uv(0, 3, 3, 5, 5))],
            [TextureOf(Page(4, 4), 0)]);

        var ex = Assert.ThrowsExactly<ImgFormatException>(
            () => ImgFile.Read(new MemoryStream(data), "v5-bad-uv.img", data.Length));

        StringAssert.Contains(ex.Message, "UV 子矩形");
    }

    [TestMethod]
    public void Ver5_TextureIndexOutOfRange_ThrowsImgFormatException()
    {
        var data = TestImageBuilder.BuildVer5(
            [TextureFrame(2, 2, uv: Uv(1, 0, 0, 2, 2))],
            [TextureOf(Page(4, 4), 0)]);

        var ex = Assert.ThrowsExactly<ImgFormatException>(
            () => ImgFile.Read(new MemoryStream(data), "v5-bad-index.img", data.Length));

        StringAssert.Contains(ex.Message, "纹理下标 1 越界");
    }

    // ---------- 构造辅助 ----------

    private static ImgFile ReadSingleTextureImage(TestImageBuilder.TestTexture texture)
    {
        var data = TestImageBuilder.BuildVer5(
            [TextureFrame(4, 4, uv: Uv(0, 0, 0, 4, 4))],
            [texture]);

        return ImgFile.Read(new MemoryStream(data), "v5-dxt.img", data.Length);
    }

    /// <summary>写盘再读回，用于验证"编辑后的模型能被自己的读取器原样理解"。</summary>
    private static ImgFile RoundTrip(ImgFile image, string path)
    {
        var bytes = image.ToArray();
        return ImgFile.Read(new MemoryStream(bytes), path, bytes.Length);
    }

    private static TestImageBuilder.TestFrame TextureFrame(
        int width,
        int height,
        int x = 0,
        int y = 0,
        int frameWidth = 0,
        int frameHeight = 0,
        TestImageBuilder.TestUv? uv = null)
    {
        return new TestImageBuilder.TestFrame
        {
            // 纹理帧的 type 保持原字段（DXT 或 1555 都可以）；length 恒 0 是判别条件。
            Type = ColorBits.ARGB_1555,
            Compress = CompressMode.DDS_ZLIB,
            Width = width,
            Height = height,
            Length = 0,
            X = x,
            Y = y,
            FrameWidth = frameWidth == 0 ? width : frameWidth,
            FrameHeight = frameHeight == 0 ? height : frameHeight,
            Uv = uv
        };
    }

    private static TestImageBuilder.TestUv Uv(int textureIndex, int left, int top, int right, int bottom)
    {
        return new TestImageBuilder.TestUv
        {
            TextureIndex = textureIndex,
            LeftUpX = left,
            LeftUpY = top,
            RightDownX = right,
            RightDownY = bottom
        };
    }

    private static TestImageBuilder.TestTexture TextureOf(RgbaImage page, int index)
    {
        return new TestImageBuilder.TestTexture
        {
            Version = 1,
            Type = ColorBits.ARGB_1555,
            Index = index,
            Width = page.Width,
            Height = page.Height,
            Raw = ColorCodec.Encode(page.Pixels, ColorBits.ARGB_1555)
        };
    }

    private static TestImageBuilder.TestTexture DdsTexture(
        DxtFormat format,
        int width,
        int height,
        byte[] blocks,
        ColorBits type,
        int version)
    {
        return new TestImageBuilder.TestTexture
        {
            Version = version,
            Type = type,
            Width = width,
            Height = height,
            Raw = TestImageBuilder.BuildDds(format, width, height, blocks)
        };
    }

    /// <summary>造一张每个像素都能被 ARGB1555 精确表示、且互不相同的纹理页。</summary>
    private static RgbaImage Page(int width, int height)
    {
        var page = new RgbaImage(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                page.SetPixel(x, y, PageColor(x, y));
            }
        }

        return page;
    }

    private static Color32 PageColor(int x, int y)
    {
        var r = (x * 3 + 1) & 0x1F;
        var g = (y * 5 + 2) & 0x1F;
        var b = (x + (y * 7) + 3) & 0x1F;
        return ColorCodec.DecodeArgb1555((ushort)(0x8000 | (r << 10) | (g << 5) | b));
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
}
