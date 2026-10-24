using NPKStudio.Core.Formats;
using NPKStudio.Core.Formats.Npk;
using NPKStudio.Core.Imaging;

namespace NPKStudio.MakeSample;

/// <summary>
///     生成 <c>tests/samples/synthetic_shapes.NPK</c>：一个<b>完全由程序化几何图形</b>构成的 NPK 样本。
///     <para>
///         存在的理由：真实客户端 NPK 是别人的版权资产，不能随仓库分发；而测试与文档截图又需要一个
///         "有多条目、多帧、含 LINK、含不同像素格式"的包。这里的每一帧都是本程序用数学公式画出来的色块，
///         <b>不含任何游戏美术，也不临摹任何既有角色或表情</b>，因此可以随 MIT 一起提交。
///     </para>
///     <para>
///         包容器用项目自己的 <see cref="NpkWriter" />、IMG 用项目自己的 <see cref="ImgFile" /> 写出，
///         所以这个工具本身也是一次"从零造包"的端到端自测。
///     </para>
///     <para>
///         输出是<b>确定性</b>的：不写时间戳，伪随机只用固定种子的 LCG（每次重画消耗随机数的顺序一致），
///         同一份代码跑两次字节完全相同。
///     </para>
///     <para>
///         用法（在仓库根执行）：<c>dotnet run --project tools/make-sample</c>，
///         或显式指定输出路径：<c>dotnet run --project tools/make-sample -- out.NPK</c>。
///     </para>
/// </summary>
internal static class Program
{
    /// <summary>常规帧边长：80×80。</summary>
    private const int Size = 80;

    /// <summary>
    ///     特效贴图边长。比常规图形大一圈：火舌的外焰、光束的横向衰减、电弧的柔光晕
    ///     都需要"从亮到纯黑"的过渡半径，80 像素放不下层次，边缘会露出台阶。
    /// </summary>
    private const int EffectSize = 192;

    /// <summary>取样用的几何图形配色（纯色块，不取自任何游戏调色板）。</summary>
    private static readonly Color32[] Palette =
    [
        new(0xE6, 0x4A, 0x3F, 0xFF), // 砖红
        new(0xF2, 0xA6, 0x2B, 0xFF), // 琥珀
        new(0xF7, 0xD1, 0x54, 0xFF), // 米黄
        new(0x5C, 0xB8, 0x5C, 0xFF), // 草绿
        new(0x3D, 0x8B, 0xD4, 0xFF), // 天蓝
        new(0x7A, 0x5C, 0xD4, 0xFF), // 紫罗兰
        new(0xC0, 0x5C, 0xC0, 0xFF), // 品红
        new(0x44, 0x44, 0x4C, 0xFF), // 深灰
    ];

    private static int Main(string[] args)
    {
        var output = args.Length > 0 ? Path.GetFullPath(args[0]) : DefaultOutputPath();
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var entries = BuildEntries();
        NpkWriter.WriteFileStreaming(output, entries);

        var total = entries.Sum(entry => (long)entry.Length);
        Console.WriteLine($"已生成 {output}");
        Console.WriteLine($"{entries.Count} 个 IMG / {total} 字节");
        foreach (var entry in entries)
        {
            Console.WriteLine($"  {entry.Path,-36} {entry.Length,7} B");
        }

        return 0;
    }

    /// <summary>默认输出：仓库根的 <c>tests/samples/synthetic_shapes.NPK</c>（从可执行目录向上找 .sln）。</summary>
    private static string DefaultOutputPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NPKStudio.sln")))
        {
            dir = dir.Parent;
        }

        var root = dir?.FullName ?? Directory.GetCurrentDirectory();
        return Path.Combine(root, "tests", "samples", "synthetic_shapes.NPK");
    }

    private static List<NpkEntrySource> BuildEntries() =>
    [
        Img("shapes/gradient_square.img", 6, GradientSquare),
        Img("shapes/bouncing_ball.img", 8, BouncingBall),
        Img("shapes/checkerboard.img", 4, Checkerboard),
        Img("shapes/color_bars.img", 4, ColorBars),
        Img("shapes/rings.img", 6, Rings),
        Img("shapes/stripes.img", 5, Stripes, ColorBits.ARGB_4444),
        Img("shapes/triangles.img", 4, RotatingTriangle),
        Img("shapes/plasma.img", 8, Plasma),
        Img("shapes/dots_grid.img", 5, DotsGrid, ColorBits.ARGB_4444),
        Img("shapes/rounded_panel.img", 4, RoundedPanel),
        LinkCycle("shapes/link_cycle.img"),
        MultiSize("shapes/multi_size.img"),
        Img("shapes/sub/hbar_stack.img", 4, HorizontalBars),
        Img("shapes/alpha_fade.img", 6, AlphaFade),
        Img("shapes/spiral.img", 6, Spiral),
        Img("shapes/sweep.img", 6, Sweep),
        Img("shapes/sine_waves.img", 5, SineWaves),

        // 特效型贴图：不透明纯黑底 + 多层加色发光。存在的理由是"线性减淡"没有别的样本能演示——
        // 前面 17 个条目全是透明底实心图形，两种效果看起来几乎一样。
        // 三个新贴图都刻意保留"由亮到纯黑"的连续外缘：减淡后那一圈暗部会变成半透明，
        // 背景格子从暗部透出来，这是加色混合最直观的证据。
        Img("shapes/effect_flame_burst.img", 12, EffectFlameBurst, ColorBits.ARGB_8888, EffectSize),
        Img("shapes/effect_light_beams.img", 10, EffectLightBeams, ColorBits.ARGB_8888, EffectSize),
        Img("shapes/effect_plasma_arc.img", 10, EffectPlasmaArc, ColorBits.ARGB_8888, EffectSize),
        Img("shapes/effect_particle_burst.img", 10, EffectParticleBurst, ColorBits.ARGB_8888, EffectSize),
    ];

    /// <summary>把一个"帧序号 → 位图"的画笔包装成 NPK 条目源。</summary>
    private static NpkEntrySource Img(
        string path,
        int frameCount,
        Func<int, RgbaImage> paint,
        ColorBits type = ColorBits.ARGB_1555,
        int side = Size)
    {
        var image = ImgFile.Create(ImgVersion.Ver2, path);
        for (var i = 0; i < frameCount; i++)
        {
            var frame = image.AddFrame();
            image.ReplaceFrameImage(frame, paint(i), type);
            frame.X = 0;
            frame.Y = 0;
            frame.FrameWidth = side;
            frame.FrameHeight = side;
        }

        return NpkEntrySource.FromBytes(path, image.ToArray());
    }

    /// <summary>含 LINK 帧的条目：[方] [球] [LINK→0] [三角] [LINK→1]。</summary>
    private static NpkEntrySource LinkCycle(string path)
    {
        var image = ImgFile.Create(ImgVersion.Ver2, path);

        var first = image.AddFrame();
        image.ReplaceFrameImage(first, GradientSquare(0), ColorBits.ARGB_1555);

        var second = image.AddFrame();
        image.ReplaceFrameImage(second, BouncingBall(2), ColorBits.ARGB_1555);

        var linkToFirst = image.AddFrame();
        linkToFirst.Type = ColorBits.LINK;
        linkToFirst.LinkTarget = first;

        var third = image.AddFrame();
        image.ReplaceFrameImage(third, RotatingTriangle(1), ColorBits.ARGB_1555);

        var linkToSecond = image.AddFrame();
        linkToSecond.Type = ColorBits.LINK;
        linkToSecond.LinkTarget = second;

        foreach (var frame in image.Frames)
        {
            frame.X = 0;
            frame.Y = 0;
            frame.FrameWidth = Size;
            frame.FrameHeight = Size;
        }

        return NpkEntrySource.FromBytes(path, image.ToArray());
    }

    /// <summary>帧尺寸逐帧收缩（80 / 72 / 64 / 56），用来覆盖"帧域与位图尺寸不一致"的路径。</summary>
    private static NpkEntrySource MultiSize(string path)
    {
        int[] sizes = [80, 72, 64, 56];
        var image = ImgFile.Create(ImgVersion.Ver2, path);
        for (var i = 0; i < sizes.Length; i++)
        {
            var side = sizes[i];
            var bitmap = new RgbaImage(side, side);
            var color = Palette[i % Palette.Length];
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    // 由外向内一圈圈变暗的方环
                    var ring = Math.Min(Math.Min(x, y), Math.Min(side - 1 - x, side - 1 - y));
                    bitmap.SetPixel(x, y, Shade(color, 1f - (ring % 8) * 0.09f));
                }
            }

            var frame = image.AddFrame();
            image.ReplaceFrameImage(frame, bitmap, ColorBits.ARGB_1555);
            frame.X = (Size - side) / 2;
            frame.Y = (Size - side) / 2;
            frame.FrameWidth = Size;
            frame.FrameHeight = Size;
        }

        return NpkEntrySource.FromBytes(path, image.ToArray());
    }

    // ── 画笔 ────────────────────────────────────────────────────────────────
    // 全部是纯数学：渐变、圆、环、三角、棋盘、条纹、正弦噪声。没有任何"照着某张图描"的成分。

    /// <summary>渐变方块：对角线性渐变 + 3 px 深色描边，颜色按帧轮转。</summary>
    private static RgbaImage GradientSquare(int frame)
    {
        var image = new RgbaImage(Size, Size);
        var from = Palette[frame % Palette.Length];
        var to = Palette[(frame + 3) % Palette.Length];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var t = (x + y) / (2f * (Size - 1));
                var color = t < 0.5f
                    ? Lerp(from, to, t * 2f)
                    : Lerp(to, from, (t - 0.5f) * 2f);
                var border = x < 3 || y < 3 || x >= Size - 3 || y >= Size - 3;
                image.SetPixel(x, y, border ? Palette[7] : color);
            }
        }

        return image;
    }

    /// <summary>跳动的圆：透明底 + 上下位移的实心圆 + 底部基准线。</summary>
    private static RgbaImage BouncingBall(int frame)
    {
        var image = new RgbaImage(Size, Size);
        var centerY = Size / 2f + 24f * MathF.Sin(frame / 8f * MathF.Tau);
        var color = Palette[frame % Palette.Length];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var dx = x - Size / 2f + 0.5f;
                var dy = y - centerY + 0.5f;
                if ((dx * dx) + (dy * dy) <= 14f * 14f)
                {
                    image.SetPixel(x, y, color);
                }
                else if (y is >= 74 and <= 76)
                {
                    image.SetPixel(x, y, Palette[7]);
                }
            }
        }

        return image;
    }

    /// <summary>棋盘格：10 px 方格两色交替，奇数帧互换两色。</summary>
    private static RgbaImage Checkerboard(int frame)
    {
        var image = new RgbaImage(Size, Size);
        const int cell = 10;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var even = (x / cell + y / cell) % 2 == 0;
                image.SetPixel(x, y, (even ^ (frame % 2 == 1)) ? Palette[4] : Palette[2]);
            }
        }

        return image;
    }

    /// <summary>色条：8 条 10 px 竖条，色序按帧左移。</summary>
    private static RgbaImage ColorBars(int frame)
    {
        var image = new RgbaImage(Size, Size);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var bar = (x / 10 + frame) % Palette.Length;
                image.SetPixel(x, y, Palette[bar]);
            }
        }

        return image;
    }

    /// <summary>同心圆环：半径随帧增长，3 px 描边。</summary>
    private static RgbaImage Rings(int frame)
    {
        var image = new RgbaImage(Size, Size);
        var center = (Size - 1) / 2f;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = MathF.Sqrt((dx * dx) + (dy * dy));
                var band = (int)((distance - frame * 1.5f) / 9f);
                if (band < 0)
                {
                    continue;
                }

                if ((distance - frame * 1.5f) - band * 9f <= 3f)
                {
                    image.SetPixel(x, y, Palette[band % Palette.Length]);
                }
            }
        }

        return image;
    }

    /// <summary>斜条纹滚动：45° 方向的 8 px 条纹，相位随帧平移。</summary>
    private static RgbaImage Stripes(int frame)
    {
        var image = new RgbaImage(Size, Size);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var shifted = x + y + frame * 6;
                var band = (shifted / 8) % Palette.Length;
                image.SetPixel(x, y, Palette[band]);
            }
        }

        return image;
    }

    /// <summary>旋转三角：绕中心旋转的等边三角形，逐帧转 90°。</summary>
    private static RgbaImage RotatingTriangle(int frame)
    {
        var image = new RgbaImage(Size, Size);
        var center = (Size - 1) / 2f;
        var angle = frame / 4f * MathF.Tau;
        var color = Palette[(frame + 5) % Palette.Length];

        Span<float> xs = stackalloc float[3];
        Span<float> ys = stackalloc float[3];
        for (var i = 0; i < 3; i++)
        {
            var a = angle + i * MathF.Tau / 3f;
            xs[i] = center + 32f * MathF.Cos(a);
            ys[i] = center + 32f * MathF.Sin(a);
        }

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (InTriangle(x + 0.5f, y + 0.5f, xs, ys))
                {
                    image.SetPixel(x, y, color);
                }
            }
        }

        return image;
    }

    /// <summary>正弦渐变（plasma）：三通道各自沿不同方向、不同相位起伏。</summary>
    private static RgbaImage Plasma(int frame)
    {
        var image = new RgbaImage(Size, Size);
        var t = frame / 8f * MathF.Tau;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var r = 0.5f + 0.5f * MathF.Sin((x * 0.09f) + t);
                var g = 0.5f + 0.5f * MathF.Sin((y * 0.11f) - t);
                var b = 0.5f + 0.5f * MathF.Sin(((x + y) * 0.07f) + (t * 0.5f));
                image.SetPixel(x, y, new Color32(ToByte(r), ToByte(g), ToByte(b), 0xFF));
            }
        }

        return image;
    }

    /// <summary>点阵：8×8 个圆点，半径按帧脉动。</summary>
    private static RgbaImage DotsGrid(int frame)
    {
        var image = new RgbaImage(Size, Size);
        var radius = 2.5f + frame * 0.6f;
        for (var row = 0; row < 8; row++)
        {
            for (var column = 0; column < 8; column++)
            {
                var centerX = 6f + column * 10f;
                var centerY = 6f + row * 10f;
                for (var y = 0; y < Size; y++)
                {
                    for (var x = 0; x < Size; x++)
                    {
                        var dx = x - centerX;
                        var dy = y - centerY;
                        if ((dx * dx) + (dy * dy) <= radius * radius)
                        {
                            image.SetPixel(x, y, Palette[(row + column) % Palette.Length]);
                        }
                    }
                }
            }
        }

        return image;
    }

    /// <summary>圆角面板：圆角矩形 + 竖向渐变填充 + 2 px 描边，内部小方块逐帧移动。</summary>
    private static RgbaImage RoundedPanel(int frame)
    {
        var image = new RgbaImage(Size, Size);
        const int inset = 6;
        const int radius = 12;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (!InRoundedRect(x, y, inset, Size - 1 - inset, radius))
                {
                    continue;
                }

                var edge = !InRoundedRect(x, y, inset + 2, Size - 1 - inset - 2, radius - 2);
                image.SetPixel(x, y, edge
                    ? Palette[7]
                    : Lerp(Palette[4], Palette[6], y / (float)(Size - 1)));
            }
        }

        var blockOffset = frame * 6;
        for (var y = 30; y < 46; y++)
        {
            for (var x = 14 + blockOffset; x < 30 + blockOffset; x++)
            {
                image.SetPixel(x, y, Palette[2]);
            }
        }

        return image;
    }

    /// <summary>横向条带：上下排列的 10 px 横条，每帧整体下移一格。</summary>
    private static RgbaImage HorizontalBars(int frame)
    {
        var image = new RgbaImage(Size, Size);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var band = ((y / 10) + frame) % Palette.Length;
                image.SetPixel(x, y, Palette[band]);
            }
        }

        return image;
    }

    /// <summary>透明度渐变：实心方块，整体 alpha 按帧循环（覆盖"整帧半透明"的预览路径）。</summary>
    private static RgbaImage AlphaFade(int frame)
    {
        var image = new RgbaImage(Size, Size);
        int[] alphas = [0x30, 0x60, 0x90, 0xC0, 0xFF, 0xC0];
        var alpha = (byte)alphas[frame % alphas.Length];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var baseColor = Lerp(Palette[0], Palette[5], x / (float)(Size - 1));
                image.SetPixel(x, y, baseColor with { A = alpha });
            }
        }

        return image;
    }

    /// <summary>旋转螺线：阿基米德螺线（两圈，3 px 粗），整体逐帧转 60°。</summary>
    private static RgbaImage Spiral(int frame)
    {
        var image = new RgbaImage(Size, Size);
        var center = (Size - 1) / 2f;
        var rotation = frame / 6f * MathF.Tau; // 6 帧转一整圈
        var color = Palette[(frame + 1) % Palette.Length];
        for (var theta = 0f; theta <= 4f * MathF.PI; theta += 0.02f)
        {
            var r = 2.6f * theta;
            var x = center + (r * MathF.Cos(theta + rotation));
            var y = center + (r * MathF.Sin(theta + rotation));
            Stamp2(image, x, y, color);
        }

        return image;
    }

    /// <summary>扫描扇形：雷达式亮扇绕中心旋转（越靠扇尾越暗），6 帧转一整圈。</summary>
    private static RgbaImage Sweep(int frame)
    {
        var image = new RgbaImage(Size, Size);
        var center = (Size - 1) / 2f;
        var sweep = frame / 6f * MathF.Tau;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = MathF.Sqrt((dx * dx) + (dy * dy));
                if (distance >= 36f)
                {
                    continue;
                }

                // 扇尾拖出整圈亮度衰减：刚扫过的地方最亮
                var behind = sweep - MathF.Atan2(dy, dx);
                behind %= MathF.Tau;
                if (behind < 0f)
                {
                    behind += MathF.Tau;
                }

                var intensity = MathF.Pow(1f - (behind / MathF.Tau), 3f);
                if (intensity >= 0.02f)
                {
                    image.SetPixel(x, y, Shade(Palette[4], 0.22f + (0.78f * intensity)));
                }
            }
        }

        return image;
    }

    /// <summary>正弦波：4 条横向正弦曲线，相位逐帧平移。</summary>
    private static RgbaImage SineWaves(int frame)
    {
        var image = new RgbaImage(Size, Size);
        for (var line = 0; line < 4; line++)
        {
            var baseY = 14f + (line * 17f);
            var color = Palette[(line + 4) % Palette.Length];
            var phase = (frame * 1.2f) + (line * 0.9f);
            for (var x = 4; x < Size - 4; x++)
            {
                var y = baseY + (6f * MathF.Sin((x / 24f * MathF.Tau) + phase));
                Stamp2(image, x, y, color);
            }
        }

        return image;
    }

    /// <summary>在浮点坐标处点一个 2×2 的方点（螺线 / 波形这类"沿曲线走"的画笔用）。</summary>
    private static void Stamp2(RgbaImage image, float x, float y, Color32 color)
    {
        var ix = (int)MathF.Round(x);
        var iy = (int)MathF.Round(y);
        for (var dy = 0; dy < 2; dy++)
        {
            for (var dx = 0; dx < 2; dx++)
            {
                if (ix + dx is >= 0 and < Size && iy + dy is >= 0 and < Size)
                {
                    image.SetPixel(ix + dx, iy + dy, color);
                }
            }
        }
    }

    // ── 特效型画笔（黑底 + 多层加色发光）────────────────────────────────────
    // 这一类刻意不做"透明底"：DNF 的发光贴图就是一块不透明纯黑方块，光靠 RGB 亮度表达。
    // 「线性减淡」按 alpha = min(alpha, max(r,g,b)) 算，黑底 max=0 → 打穿成透明，只剩发光，
    // 这才是这个效果存在的意义。所以这里 alpha 一律写 255、黑底一律精确 0：
    // alpha 若已经是 0 就看不出差别，1~8 的"近似黑"又会让去黑底的判定产生分歧。
    //
    // 画法统一走 <see cref="LightCanvas" />：所有层往同一个浮点缓冲里"加光"，最后一次性 clamp。
    // 加色（而不是逐层覆盖）保证亮部叠亮部，在「线性减淡」下才有真实光效的层次。
    // 每个发光单元都用"连续场"表达（高斯横向衰减 × 纵向包络），外缘一定落到纯 0，
    // 这样减淡后才有"亮 → 半透明 → 完全透明"的过渡，背景格子能从暗部透出来。
    // 只有最内层的芯（火焰白热 / 电弧主芯 / 光束交叉点）允许接近纯白；外围一律用饱和色，
    // 否则整块贴图会糊成一片白，看不出加色叠加的层次。
    // 每个特效一个函数，参数只有帧号；颜色、层数、尺寸都在函数顶部以常量/局部变量给出，方便调整。

    /// <summary>
    ///     火焰喷发：5 条火舌（底部散开、顶端摆动着收窄）叠加成一片火墙，底部有一团余烬辉光。
    ///     <para>
    ///         每一层都是<b>连续标量场</b>而不是实心形状：横向高斯衰减 × 纵向幂次衰减 × 湍流扰动，
    ///         三层加色（暗红 → 深红/橙 → 金白芯）后，外缘自然会落到纯 0。
    ///         减淡模式下最外圈暗红变成低 alpha，背景格子从火边透出来——这正是要展示的东西。
    ///     </para>
    ///     <para>12 帧：火舌长度、摆动相位、湍流相位都随帧推进，但所有随机量来自固定相位，重跑一致。</para>
    /// </summary>
    private static RgbaImage EffectFlameBurst(int frame)
    {
        var ember = new Color32(0xB0, 0x1A, 0x06, 0xFF); // 余烬暗红
        var deepRed = new Color32(0xE8, 0x30, 0x0C, 0xFF); // 外焰深红
        var orange = new Color32(0xFF, 0x86, 0x1E, 0xFF); // 中焰橙
        var gold = new Color32(0xFF, 0xD8, 0x6A, 0xFF); // 内焰金
        var whiteHot = new Color32(0xFF, 0xF6, 0xD2, 0xFF); // 芯部白热

        var canvas = new LightCanvas(EffectSize, dither: true);
        var size = (float)EffectSize;
        var center = (size - 1f) * 0.5f;
        var t = frame / 12f * MathF.Tau;

        const int tongues = 5;
        for (var i = 0; i < tongues; i++)
        {
            // 每条火舌的"身份"是固定的（相位 seed、横向位置），只有时间项 t 随帧变
            var u = (i / (float)(tongues - 1)) - 0.5f; // -0.5 ~ 0.5
            var seed = 1.7f + (i * 2.31f);
            var baseX = center + (u * size * 0.44f);
            var baseY = size * 0.88f;
            var height = size * (0.54f + (0.10f * MathF.Sin(t + seed)) + (i % 2 == 0 ? 0.05f : 0f));
            var halfWidth = size * (0.080f + (0.018f * MathF.Sin((seed * 2.3f) + t)));
            var swayAmp = size * 0.045f * (0.7f + (0.3f * MathF.Sin(seed + (t * 0.5f))));

            var minY = Math.Max(0, (int)(baseY - height - 6f));
            var maxY = Math.Min(EffectSize - 1, (int)(baseY + 8f));
            var minX = Math.Max(0, (int)(baseX - (halfWidth * 2.6f) - swayAmp - 8f));
            var maxX = Math.Min(EffectSize - 1, (int)(baseX + (halfWidth * 2.6f) + swayAmp + 8f));

            for (var y = minY; y <= maxY; y++)
            {
                var h = (baseY - y) / height; // 0 = 根部，1 = 顶端
                if (h < -0.12f || h > 1.04f)
                {
                    continue;
                }

                var uy = Math.Clamp(h, 0f, 1f);
                var width = (halfWidth * MathF.Pow(1f - uy, 0.55f)) + 0.9f;
                var sway = swayAmp * MathF.Sin((uy * 2.4f) + (t * 1.1f) + seed);

                // 根部用一个很短的 smoothstep 收到 0：否则 y 越过基准线会突然截断，形成一条水平亮边
                var rootFade = SmoothStep(-0.12f, 0.05f, h);

                // 两个不同频率的正弦当作湍流：边缘不规则，但幅度受宽度约束，不会碎成噪点
                var swirl = (0.26f * MathF.Sin((uy * 9.3f) - (t * 2.7f) + seed)) +
                            (0.14f * MathF.Sin((uy * 21.7f) + (t * 4.1f) + (seed * 3.1f)));

                for (var x = minX; x <= maxX; x++)
                {
                    var nx = (((x - baseX) - sway) / width) + (swirl * 0.55f);
                    var lateral = MathF.Exp(-nx * nx * 1.5f);
                    var vertical = MathF.Pow(1f - uy, 0.85f) * rootFade;
                    var flicker = 0.78f + (0.22f * MathF.Sin((uy * 6.1f) - (t * 3.2f) + (seed * 1.7f)));
                    var heat = lateral * vertical * flicker;
                    if (heat <= 0.002f)
                    {
                        continue;
                    }

                    // 五层加色：幂次越高越窄，亮部叠加后自动变成白热芯，暗部只剩一点暗红
                    canvas.Add(x, y, ember, heat * 0.34f);
                    canvas.Add(x, y, deepRed, heat * 0.82f);
                    canvas.Add(x, y, orange, MathF.Pow(heat, 1.7f) * 1.05f);
                    canvas.Add(x, y, gold, MathF.Pow(heat, 3.0f) * 1.35f);
                    canvas.Add(x, y, whiteHot, MathF.Pow(heat, 5.0f) * 1.8f);
                }
            }
        }

        // 根部余烬：横向拉长的暗红辉光，补上"火焰从哪来"，同时把底边过渡做软。
        // 中心与 σ 都刻意收在画布内：椭圆一旦被下边界裁掉，就会留下一条生硬的水平亮边。
        for (var i = 0; i < 3; i++)
        {
            var offset = (i - 1) * size * 0.13f;
            var breathe = 0.9f + (0.1f * MathF.Sin((t * 1.3f) + i));
            canvas.SoftDisc(center + offset, size * 0.84f, size * 0.055f, deepRed, 0.16f * breathe);
            canvas.SoftDisc(center + offset, size * 0.86f, size * 0.035f, orange, 0.12f * breathe);
        }

        return canvas.Render();
    }

    /// <summary>
    ///     三色光束交叉：红 / 绿 / 蓝三束柔光从画面外射向中心，交叉点三通道相加成近白。
    ///     <para>
    ///         每束光 = 横向高斯衰减 × 纵向平滑包络（两端收到 0）× 轻微呼吸。
    ///         单束中心亮度刻意压在 0.5 以下，三束叠加才接近白——这样交叉点一定比任意单束亮，
    ///         直接演示"加色"；而两端包络到 0，减淡后光尾自然淡出、露出背景。
    ///     </para>
    ///     <para>10 帧：三束各自绕中心轻微摆动（相位错开），亮度整体呼吸。</para>
    /// </summary>
    private static RgbaImage EffectLightBeams(int frame)
    {
        var red = new Color32(0xFF, 0x3A, 0x2E, 0xFF);
        var green = new Color32(0x46, 0xFF, 0x6E, 0xFF);
        var blue = new Color32(0x3C, 0x86, 0xFF, 0xFF);

        var canvas = new LightCanvas(EffectSize, dither: true);
        var size = (float)EffectSize;
        var center = (size - 1f) * 0.5f;
        var t = frame / 10f * MathF.Tau;

        // 三条光束的端点都关于画面中心对称：三束几乎交于同一点，交叉处三通道相加成近白。
        // 端点落在画面外（起止各留一点），保证进入画面时已经有一段可见的横向衰减。
        (float X0, float Y0, float X1, float Y1, Color32 Color, float Phase)[] beams =
        [
            (-0.10f * size, 0.18f * size, 1.10f * size, 0.82f * size, red, 0f),
            (1.10f * size, -0.02f * size, -0.10f * size, 1.02f * size, green, 2.1f),
            (0.34f * size, 1.12f * size, 0.66f * size, -0.12f * size, blue, 4.2f),
        ];

        for (var b = 0; b < beams.Length; b++)
        {
            var beam = beams[b];
            // 绕中心旋转 ±2.6°，让三束光每帧轻微错动；端点一起转，方向才不会歪
            var angle = 0.045f * MathF.Sin(t + beam.Phase);
            var (sine, cosine) = MathF.SinCos(angle);
            var (x0, y0) = RotateAround(beam.X0, beam.Y0, center, center, sine, cosine);
            var (x1, y1) = RotateAround(beam.X1, beam.Y1, center, center, sine, cosine);
            var dx = x1 - x0;
            var dy = y1 - y0;
            var lengthSquared = (dx * dx) + (dy * dy);
            var breathe = 0.92f + (0.08f * MathF.Sin((t * 1.4f) + beam.Phase));

            for (var y = 0; y < EffectSize; y++)
            {
                for (var x = 0; x < EffectSize; x++)
                {
                    // 像素在光束上的投影参数 s（0 = 起点，1 = 终点）
                    var s = ((((x - x0) * dx) + ((y - y0) * dy)) / lengthSquared);
                    if (s is < -0.05f or > 1.05f)
                    {
                        continue;
                    }

                    var clamped = Math.Clamp(s, 0f, 1f);
                    var qx = x0 + (dx * clamped);
                    var qy = y0 + (dy * clamped);
                    var offsetX = x - qx;
                    var offsetY = y - qy;
                    var distanceSquared = (offsetX * offsetX) + (offsetY * offsetY);

                    // 横向：越靠中心光束越粗（视觉上光在汇聚）；两端用 smoothstep 收到 0
                    var sigmaCore = 2.6f + (2.2f * MathF.Sin(clamped * MathF.PI));
                    var sigmaHalo = (sigmaCore * 2.4f) + 2f;
                    var lateralCore = MathF.Exp(-distanceSquared / (2f * sigmaCore * sigmaCore));
                    var lateralHalo = MathF.Exp(-distanceSquared / (2f * sigmaHalo * sigmaHalo));
                    var envelope = SmoothStep(0f, 0.22f, s) * SmoothStep(1f, 0.78f, s);
                    var coreIntensity = lateralCore * envelope * breathe;
                    var haloIntensity = lateralHalo * envelope * breathe;
                    if (coreIntensity + haloIntensity <= 0.002f)
                    {
                        continue;
                    }

                    canvas.Add(x, y, beam.Color, coreIntensity * 1.05f);
                    // 外围一圈更宽更淡的同色光：暗部过渡带宽，减淡后能看到格子慢慢透出来
                    canvas.Add(x, y, beam.Color, haloIntensity * 0.22f);
                }
            }
        }

        return canvas.Render();
    }

    /// <summary>
    ///     电弧：中点位移算法生成 3 条锯齿状闪电 + 3 条短分叉，极亮细芯外套一层宽柔光晕。
    ///     <para>
    ///         逐像素取"到折线的最小距离"，按距离叠三层高斯光（宽蓝紫晕 / 中蓝辉光 / 白蓝细芯）。
    ///         芯部 1~1.5 px、光晕 10 px 以上，减淡后细芯保持不透明、光晕逐渐透出背景，
    ///         正是"细亮结构 + 宽柔光晕"在加色混合下的层次。
    ///     </para>
    ///     <para>10 帧：每帧换一组确定性种子重新生成折线，端点也轻微摆动，因此逐帧都在抖动。</para>
    /// </summary>
    private static RgbaImage EffectPlasmaArc(int frame)
    {
        var halo = new Color32(0x3A, 0x6C, 0xFF, 0xFF); // 蓝紫光晕
        var glow = new Color32(0x7E, 0xB4, 0xFF, 0xFF); // 中辉光
        var core = new Color32(0xE6, 0xF4, 0xFF, 0xFF); // 白蓝细芯

        var canvas = new LightCanvas(EffectSize, dither: true);
        var size = (float)EffectSize;
        var t = frame / 10f * MathF.Tau;

        // 端点小幅摆动，避免"只有锯齿在动、整体构图钉死"
        var wobble = 5f * MathF.Sin(t);
        (float Ax, float Ay, float Bx, float By, int Levels, float Roughness)[] arcs =
        [
            (0.05f * size, (0.20f * size) + wobble, 0.95f * size, (0.78f * size) - wobble, 5, 0.36f),
            (0.90f * size, (0.08f * size) - wobble, 0.16f * size, (0.88f * size) + wobble, 5, 0.32f),
            (0.10f * size, (0.62f * size) + wobble, 0.92f * size, (0.44f * size) - wobble, 4, 0.30f),
        ];

        var bolts = new List<List<(float X, float Y)>>(arcs.Length * 2);
        for (var i = 0; i < arcs.Length; i++)
        {
            var arc = arcs[i];
            // 种子按"帧号 + 弧序号"混合：同一帧重跑必然同一形状
            var seed = 0xA5F1_3C29u ^ ((uint)(frame + 1) * 2654435761u) ^ ((uint)i * 0x9E37_79B9u);
            bolts.Add(MidpointBolt(ref seed, arc.Ax, arc.Ay, arc.Bx, arc.By, arc.Levels, arc.Roughness));

            // 分叉：从主弧约 40% 处引出，更短更细（level 少一级、更粗糙）
            var parent = bolts[^1];
            var origin = parent[(int)(parent.Count * 0.40f)];
            var forkAngle = MathF.Atan2(arc.By - arc.Ay, arc.Bx - arc.Ax) + (i % 2 == 0 ? 0.9f : -0.9f);
            var forkLength = size * (0.16f + (0.05f * MathF.Sin((t * 2f) + i)));
            bolts.Add(MidpointBolt(
                ref seed,
                origin.X, origin.Y,
                origin.X + (MathF.Cos(forkAngle) * forkLength),
                origin.Y + (MathF.Sin(forkAngle) * forkLength),
                3, 0.34f));
        }

        for (var y = 0; y < EffectSize; y++)
        {
            for (var x = 0; x < EffectSize; x++)
            {
                var nearest = float.MaxValue;
                foreach (var bolt in bolts)
                {
                    for (var i = 0; i < bolt.Count - 1; i++)
                    {
                        var distance = PointSegmentDistance(x, y, bolt[i], bolt[i + 1]);
                        if (distance < nearest)
                        {
                            nearest = distance;
                        }
                    }
                }

                var d = nearest;
                var haloIntensity = MathF.Exp(-(d * d) / (2f * 9.5f * 9.5f)) * 0.52f;
                var glowIntensity = MathF.Exp(-(d * d) / (2f * 3.2f * 3.2f)) * 0.85f;
                var coreIntensity = MathF.Exp(-(d * d) / (2f * 1.05f * 1.05f)) * 1.55f;
                if (haloIntensity + glowIntensity + coreIntensity <= 0.002f)
                {
                    continue;
                }

                canvas.Add(x, y, halo, haloIntensity);
                canvas.Add(x, y, glow, glowIntensity);
                canvas.Add(x, y, core, coreIntensity);
            }
        }

        return canvas.Render();
    }

    /// <summary>
    ///     中点位移：反复在每段中点沿法线方向随机偏移，得到分形感的折线。
    ///     <para>偏移量与段长成正比、再按层衰减，所以每一级抖动幅度大致相同，不会越细越毛。</para>
    /// </summary>
    private static List<(float X, float Y)> MidpointBolt(
        ref uint seed, float ax, float ay, float bx, float by, int levels, float roughness)
    {
        var points = new List<(float X, float Y)>(1 << levels) { (ax, ay), (bx, by) };
        var amplitude = roughness;
        for (var level = 0; level < levels; level++)
        {
            var next = new List<(float X, float Y)>((points.Count * 2) - 1);
            for (var i = 0; i < points.Count - 1; i++)
            {
                var (x0, y0) = points[i];
                var (x1, y1) = points[i + 1];
                var dx = x1 - x0;
                var dy = y1 - y0;
                var length = MathF.Max(0.001f, MathF.Sqrt((dx * dx) + (dy * dy)));
                var offset = (NextFloat(ref seed) - 0.5f) * 2f * amplitude * length;

                next.Add((x0, y0));
                next.Add((
                    ((x0 + x1) * 0.5f) - (dy / length * offset),
                    ((y0 + y1) * 0.5f) + (dx / length * offset)));
            }

            next.Add(points[^1]);
            points = next;
            amplitude *= 0.72f;
        }

        return points;
    }

    /// <summary>点到线段的最短距离（电弧的距离场用它求"离最近一道闪电多远"）。</summary>
    private static float PointSegmentDistance(
        float px, float py, (float X, float Y) a, (float X, float Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        var t = lengthSquared <= 0.0001f
            ? 0f
            : Math.Clamp((((px - a.X) * dx) + ((py - a.Y) * dy)) / lengthSquared, 0f, 1f);
        var qx = a.X + (dx * t);
        var qy = a.Y + (dy * t);
        var ox = px - qx;
        var oy = py - qy;
        return MathF.Sqrt((ox * ox) + (oy * oy));
    }

    /// <summary>把点绕 (cx, cy) 旋转（sin / cos 由调用方预算，省去逐束三角函数）。</summary>
    private static (float X, float Y) RotateAround(
        float x, float y, float cx, float cy, float sine, float cosine)
    {
        var dx = x - cx;
        var dy = y - cy;
        return (cx + (dx * cosine) - (dy * sine), cy + (dx * sine) + (dy * cosine));
    }

    /// <summary>smoothstep：s 在 [edge0, edge1] 上平滑地从 0 到 1；edge1 &lt; edge0 时反向。</summary>
    private static float SmoothStep(float edge0, float edge1, float s)
    {
        var t = Math.Clamp((s - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    /// <summary>
    ///     粒子爆散：中心闪光 + 26 个粒子沿确定性方向飞出，每个粒子带短拖尾，整体逐帧衰减。
    ///     <para>每帧都从同一种子重新取随机数，所以第 i 个粒子在所有帧里都是同一条轨迹。</para>
    /// </summary>
    private static RgbaImage EffectParticleBurst(int frame)
    {
        var blue = new Color32(0x2E, 0x9B, 0xFF, 0xFF); // 电蓝
        var gold = new Color32(0xFF, 0xB8, 0x4D, 0xFF); // 暖金
        var paleGold = new Color32(0xFF, 0xE0, 0x8A, 0xFF); // 浅金（闪光芯）
        var magenta = new Color32(0xE8, 0x5C, 0xD0, 0xFF); // 品红

        const int particles = 26;
        var canvas = new LightCanvas(EffectSize);
        var center = (EffectSize - 1) / 2f;
        var p = frame / 9f; // 0 → 1
        var decay = MathF.Pow(1f - p, 0.85f); // 整体亮度衰减（末帧仍可见）
        var travel = 1f - MathF.Pow(1f - p, 2f); // ease-out：先快后慢

        uint seed = 0x5EED_C0DE; // 固定种子：粒子轨迹可复现
        for (var i = 0; i < particles; i++)
        {
            var angle = NextFloat(ref seed) * MathF.Tau;
            var speed = 34f + (NextFloat(ref seed) * 46f); // 34~80 px
            var sigma = 1.1f + (NextFloat(ref seed) * 1.6f); // 1.1~2.7 px
            var color = (NextRandom(ref seed) % 3u) switch
            {
                0 => blue,
                1 => gold,
                _ => magenta,
            };

            var distance = speed * travel;
            var (sin, cos) = MathF.SinCos(angle);
            var x = center + (distance * cos);
            var y = center + (distance * sin);

            // 拖尾：从粒子当前位置往回（朝中心）拉一条渐淡的线
            var tail = (speed * 0.16f * travel) + 2f;
            var backX = center + (MathF.Max(0f, distance - tail) * cos);
            var backY = center + (MathF.Max(0f, distance - tail) * sin);
            canvas.Line(x, y, backX, backY, sigma * 0.75f, color, 0.35f * decay);
            canvas.SoftDisc(x, y, sigma, color, 0.9f * decay);
        }

        // 中心闪光：只在头几帧亮，随后快速熄灭
        var flash = MathF.Pow(1f - p, 3f);
        canvas.SoftDisc(center, center, 10f * (1f - (p * 0.6f)), gold, 0.9f * flash);
        canvas.SoftDisc(center, center, 3.5f, paleGold, 0.8f * flash);

        return canvas.Render();
    }

    /// <summary>
    ///     加色光缓冲：所有发光层往同一个浮点 RGB 缓冲里累加，最后一次性 clamp 成字节。
    ///     <para>
    ///         黑底（RGB 0）+ 加色累积，天然就是"光叠光"；clamp 保证不溢出。
    ///         渲染时 alpha 一律写 255——黑底必须是不透明的，这是线性减淡的前提。
    ///     </para>
    ///     <para>
    ///         <c>dither</c> 打开时在量化前叠一个 8×8 Bayer 抖动（幅度 ±0.49 灰阶）。
    ///         浮点场本身是连续的，但写进 8 位通道时每级之间有 1/255 的台阶，宽而暗的光晕上会看到
    ///         同心色带；抖动把这些台阶打散成肉眼看不见的细噪声。偏移严格小于 0.5，所以 0 加抖动
    ///         四舍五入后仍是 0——纯黑底不会被"抖"出孤立亮点。
    ///     </para>
    /// </summary>
    private sealed class LightCanvas(int size, bool dither = false)
    {
        /// <summary>8×8 Bayer 阈值矩阵（值 0..63），抖动偏移由它换算成 [-0.5, 0.5)。</summary>
        private static readonly byte[] Bayer8 =
        [
            0, 32, 8, 40, 2, 34, 10, 42,
            48, 16, 56, 24, 50, 18, 58, 26,
            12, 44, 4, 36, 14, 46, 6, 38,
            60, 28, 52, 20, 62, 30, 54, 22,
            3, 35, 11, 43, 1, 33, 9, 41,
            51, 19, 59, 27, 49, 17, 57, 25,
            15, 47, 7, 39, 13, 45, 5, 37,
            63, 31, 55, 23, 61, 29, 53, 21,
        ];

        private readonly float[] _channels = new float[size * size * 3];

        public int Size { get; } = size;

        /// <summary>在 (x, y) 处叠加一份带强度的颜色（intensity 同时充当抗过曝的权重）。</summary>
        public void Add(int x, int y, Color32 color, float intensity)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size)
            {
                return;
            }

            var i = ((y * Size) + x) * 3;
            _channels[i] += color.R * intensity;
            _channels[i + 1] += color.G * intensity;
            _channels[i + 2] += color.B * intensity;
        }

        /// <summary>软圆盘：高斯衰减的加色光斑，是所有发光的基本单元。</summary>
        public void SoftDisc(float cx, float cy, float sigma, Color32 color, float intensity)
        {
            var reach = sigma * 3f;
            var minX = Math.Max(0, (int)(cx - reach));
            var minY = Math.Max(0, (int)(cy - reach));
            var maxX = Math.Min(Size - 1, (int)(cx + reach));
            var maxY = Math.Min(Size - 1, (int)(cy + reach));
            var denominator = 2f * sigma * sigma;
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var k = intensity * MathF.Exp(-((dx * dx) + (dy * dy)) / denominator);
                    if (k > 0.001f)
                    {
                        Add(x, y, color, k);
                    }
                }
            }
        }

        /// <summary>线段：沿路径按半σ步进盖软圆点（刻痕 / 拖尾 / 多边形边都用它）。</summary>
        public void Line(float x0, float y0, float x1, float y1, float sigma, Color32 color, float intensity)
        {
            var dx = x1 - x0;
            var dy = y1 - y0;
            var length = MathF.Sqrt((dx * dx) + (dy * dy));
            var steps = Math.Max(1, (int)(length / MathF.Max(0.5f, sigma * 0.5f)));
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                SoftDisc(x0 + (dx * t), y0 + (dy * t), sigma, color, intensity);
            }
        }

        /// <summary>同心圆环：高斯剖面的加色圆环（σ 越大环越"雾"）。</summary>
        public void Ring(float cx, float cy, float radius, float sigma, Color32 color, float intensity)
        {
            var outer = radius + (sigma * 3f);
            var minX = Math.Max(0, (int)(cx - outer));
            var minY = Math.Max(0, (int)(cy - outer));
            var maxX = Math.Min(Size - 1, (int)(cx + outer));
            var maxY = Math.Min(Size - 1, (int)(cy + outer));
            var denominator = 2f * sigma * sigma;
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var distance = MathF.Sqrt((dx * dx) + (dy * dy));
                    var k = intensity * MathF.Exp(-((distance - radius) * (distance - radius)) / denominator);
                    if (k > 0.001f)
                    {
                        Add(x, y, color, k);
                    }
                }
            }
        }

        /// <summary>clamp 成字节并写进不透明黑底画布（超出 255 的部分截断，即加法混合的上限）。</summary>
        public RgbaImage Render()
        {
            var image = new RgbaImage(Size, Size);
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var i = ((y * Size) + x) * 3;
                    var offset = dither
                        ? (((Bayer8[((y & 7) << 3) | (x & 7)] + 0.5f) / 64f) - 0.5f)
                        : 0f;
                    image.SetPixel(x, y, new Color32(
                        ToByte(_channels[i] + offset),
                        ToByte(_channels[i + 1] + offset),
                        ToByte(_channels[i + 2] + offset),
                        0xFF));
                }
            }

            return image;
        }
    }

    // ── 小工具 ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     确定性伪随机：LCG（Numerical Recipes 参数），固定种子、消耗顺序固定，
    ///     所以同一份代码跑两次、同一帧重画一万次，结果都逐字节相同。
    /// </summary>
    private static uint NextRandom(ref uint state) => state = (state * 1664525u) + 1013904223u;

    /// <summary>返回 [0, 1) 的确定性伪随机浮点。</summary>
    private static float NextFloat(ref uint state) => NextRandom(ref state) / 4294967296f;

    private static Color32 Lerp(Color32 a, Color32 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color32(
            ToByte(a.R + ((b.R - a.R) * t)),
            ToByte(a.G + ((b.G - a.G) * t)),
            ToByte(a.B + ((b.B - a.B) * t)),
            ToByte(a.A + ((b.A - a.A) * t)));
    }

    private static Color32 Shade(Color32 color, float factor) =>
        new(ToByte(color.R * factor), ToByte(color.G * factor), ToByte(color.B * factor), color.A);

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);

    private static bool InTriangle(float px, float py, ReadOnlySpan<float> xs, ReadOnlySpan<float> ys)
    {
        var d1 = Sign(px, py, xs[0], ys[0], xs[1], ys[1]);
        var d2 = Sign(px, py, xs[1], ys[1], xs[2], ys[2]);
        var d3 = Sign(px, py, xs[2], ys[2], xs[0], ys[0]);
        var hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPositive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNegative && hasPositive);
    }

    private static float Sign(float px, float py, float ax, float ay, float bx, float by) =>
        ((px - bx) * (ay - by)) - ((ax - bx) * (py - by));

    private static bool InRoundedRect(int x, int y, int min, int max, int radius)
    {
        if (x < min || y < min || x > max || y > max)
        {
            return false;
        }

        var left = Math.Min(x - min, max - x);
        var top = Math.Min(y - min, max - y);
        if (left >= radius || top >= radius)
        {
            return true;
        }

        var dx = radius - left - 0.5f;
        var dy = radius - top - 0.5f;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }
}
