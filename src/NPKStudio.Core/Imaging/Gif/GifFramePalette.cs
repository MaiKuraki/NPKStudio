using System.Runtime.CompilerServices;

namespace NPKStudio.Core.Imaging.Gif;

/// <summary>
///     一帧的 GIF 调色板与色号表（含透明色槽）。
///     <para>
///         规则：alpha &lt; 128 的像素映射到透明色索引；不透明像素先用精确颜色表（unique ≤ 255 时），
///         超过 255 色才走中位切分（median cut）量化。色表长度向上取 2 的幂（GIF 要求）。
///     </para>
///     <para>
///         本类型只服务编码器，构造后不可变；像素遍历一律用下标循环，不产生逐像素分配。
///     </para>
/// </summary>
internal sealed class GifFramePalette
{
    private const int AlphaThreshold = 128;

    private GifFramePalette(byte[] rgb, byte[] indices, int tableSize, int transparentIndex)
    {
        Rgb = rgb;
        Indices = indices;
        TableSize = tableSize;
        TransparentIndex = transparentIndex;
    }

    /// <summary>本地色表（RGB 三元组，长度 <c>TableSize × 3</c>）。</summary>
    internal byte[] Rgb { get; }

    /// <summary>每像素 1 字节的色号（长度 <c>Width × Height</c>）。</summary>
    internal byte[] Indices { get; }

    /// <summary>色表长度（2 的幂，2..256）。</summary>
    internal int TableSize { get; }

    /// <summary>透明色索引；<c>-1</c> 表示该帧没有透明像素。</summary>
    internal int TransparentIndex { get; }

    /// <summary>色表所需的位宽（<c>log2(TableSize)</c>）。</summary>
    internal int Bits => System.Numerics.BitOperations.Log2((uint)TableSize);

    /// <summary>LZW 最小码宽；GIF 规定不得小于 2。</summary>
    internal int MinCodeSize => Math.Max(2, Bits);

    /// <summary>
    ///     为一张位图建立调色板与色号表。
    /// </summary>
    /// <param name="image">源位图（BGRA8888）。</param>
    /// <returns>编码所需的色表与色号。</returns>
    internal static GifFramePalette Build(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var pixels = image.Pixels;
        var pixelCount = image.Width * image.Height;

        // 1. 直方图：只统计不透明像素；keys/colors/counts 三者下标对齐，顺序即首次出现顺序（结果确定）。
        var keys = new List<uint>(Math.Min(pixelCount, 512));
        var colors = new List<Color32>(Math.Min(pixelCount, 512));
        var counts = new List<int>(Math.Min(pixelCount, 512));
        var lookup = new Dictionary<uint, int>(Math.Min(pixelCount, 512));
        var hasTransparent = false;

        for (var i = 0; i < pixelCount; i++)
        {
            var offset = i * 4;
            if (pixels[offset + 3] < AlphaThreshold)
            {
                hasTransparent = true;
                continue;
            }

            // 键只含 RGB：GIF 没有半透明，alpha 已经不参与调色板。
            var key = (uint)(pixels[offset] | (pixels[offset + 1] << 8) | (pixels[offset + 2] << 16));
            if (lookup.TryGetValue(key, out var index))
            {
                counts[index]++;
                continue;
            }

            lookup.Add(key, keys.Count);
            keys.Add(key);
            colors.Add(new Color32(pixels[offset + 2], pixels[offset + 1], pixels[offset], 255));
            counts.Add(1);
        }

        // 2. 色表：不透明色数 ≤ 255（有透明槽）或 ≤ 256（无透明）时精确映射，不降质。
        var maxOpaque = hasTransparent ? 255 : 256;
        Color32[] paletteColors;
        byte[]? uniqueToPalette;

        if (colors.Count == 0)
        {
            // 全透明帧：给一个占位色（表长 2 → 6 字节），避免 0 长度色表。
            var transparentOnly = new byte[6];
            var onlyIndices = new byte[pixelCount];
            return new GifFramePalette(transparentOnly, onlyIndices, 2, 0);
        }

        if (colors.Count <= maxOpaque)
        {
            paletteColors = colors.ToArray();
            uniqueToPalette = null;
        }
        else
        {
            // 3. 中位切分：把颜色空间切成 maxOpaque 个盒子，每盒取加权平均色。
            paletteColors = MedianCut(colors, counts, maxOpaque, out uniqueToPalette);
        }

        var transparentIndex = hasTransparent ? paletteColors.Length : -1;
        var paletteCount = paletteColors.Length + (hasTransparent ? 1 : 0);
        var tableSize = 2;
        while (tableSize < paletteCount)
        {
            tableSize <<= 1;
        }

        var rgb = new byte[tableSize * 3];
        for (var i = 0; i < paletteColors.Length; i++)
        {
            var color = paletteColors[i];
            var offset = i * 3;
            rgb[offset] = color.R;
            rgb[offset + 1] = color.G;
            rgb[offset + 2] = color.B;
        }

        if (hasTransparent)
        {
            // 透明槽的 RGB 无意义（查看器按 transparentIndex 忽略），置 0。
            var offset = transparentIndex * 3;
            rgb[offset] = 0;
            rgb[offset + 1] = 0;
            rgb[offset + 2] = 0;
        }

        // 4. 逐像素落色号：唯一色下标 →（精确时同下标 / 量化时盒下标）。
        var indices = new byte[pixelCount];
        for (var i = 0; i < pixelCount; i++)
        {
            var offset = i * 4;
            if (pixels[offset + 3] < AlphaThreshold)
            {
                indices[i] = (byte)transparentIndex;
                continue;
            }

            var key = (uint)(pixels[offset] | (pixels[offset + 1] << 8) | (pixels[offset + 2] << 16));
            var unique = lookup[key];
            indices[i] = uniqueToPalette is null ? (byte)unique : uniqueToPalette[unique];
        }

        return new GifFramePalette(rgb, indices, tableSize, transparentIndex);
    }

    /// <summary>
    ///     中位切分：按"像素数加权"的中位数切分盒子，返回每个唯一色对应的盒下标。
    /// </summary>
    /// <param name="colors">唯一色。</param>
    /// <param name="counts">每种颜色出现次数。</param>
    /// <param name="target">目标色数（≤255）。</param>
    /// <param name="uniqueToPalette">长度为 <c>colors.Count</c> 的唯一色 → 色表下标。</param>
    /// <returns>代表色表（每个盒子一项）。</returns>
    private static Color32[] MedianCut(
        List<Color32> colors,
        List<int> counts,
        int target,
        out byte[] uniqueToPalette)
    {
        var boxes = new List<Box>(target) { Box.CreateAll(colors.Count, colors, counts) };

        while (boxes.Count < target)
        {
            // 选"最长边 × 像素数"最大的盒子切：优先消除大范围的颜色误差。
            var pick = -1;
            var bestScore = -1L;
            for (var i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (box.Items.Count < 2)
                {
                    continue;
                }

                var score = (long)box.LongestRange() * box.PixelCount;
                if (score > bestScore)
                {
                    bestScore = score;
                    pick = i;
                }
            }

            if (pick < 0)
            {
                break;
            }

            var source = boxes[pick];
            var channel = source.LongestChannel();
            source.Items.Sort((a, b) => Channel(colors[a], channel).CompareTo(Channel(colors[b], channel)));

            // 按像素数找中位：保证两半的像素量尽量接近，而不是唯一色个数接近。
            var half = source.PixelCount / 2;
            long accumulated = 0;
            var split = 1;
            for (var i = 0; i < source.Items.Count - 1; i++)
            {
                accumulated += counts[source.Items[i]];
                split = i + 1;
                if (accumulated >= half)
                {
                    break;
                }
            }

            if (split >= source.Items.Count)
            {
                split = source.Items.Count - 1;
            }

            var rightItems = source.Items.GetRange(split, source.Items.Count - split);
            source.Items.RemoveRange(split, rightItems.Count);
            var right = new Box(rightItems);
            source.Measure(colors, counts);
            right.Measure(colors, counts);
            boxes.Add(right);
        }

        var result = new byte[colors.Count];
        for (var boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
        {
            var box = boxes[boxIndex];
            for (var i = 0; i < box.Items.Count; i++)
            {
                result[box.Items[i]] = (byte)boxIndex;
            }
        }

        // 代表色必须在改任何东西之前算（这里不修改入参）。
        var representatives = new Color32[boxes.Count];
        for (var boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
        {
            representatives[boxIndex] = boxes[boxIndex].AverageColor(colors, counts);
        }

        uniqueToPalette = result;
        return representatives;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Channel(Color32 color, int channel)
    {
        return channel switch
        {
            0 => color.R,
            1 => color.G,
            _ => color.B
        };
    }

    /// <summary>中位切分中的一个颜色盒。</summary>
    private sealed class Box
    {
        internal Box(List<int> items)
        {
            Items = items;
        }

        internal List<int> Items { get; }

        internal long PixelCount { get; private set; }

        private byte _rMin;
        private byte _rMax;
        private byte _gMin;
        private byte _gMax;
        private byte _bMin;
        private byte _bMax;

        internal static Box CreateAll(int count, List<Color32> colors, List<int> counts)
        {
            var items = new List<int>(count);
            for (var i = 0; i < count; i++)
            {
                items.Add(i);
            }

            var box = new Box(items);
            box.Measure(colors, counts);
            return box;
        }

        internal void Measure(List<Color32> colors, List<int> counts)
        {
            _rMin = _gMin = _bMin = 255;
            _rMax = _gMax = _bMax = 0;
            long total = 0;

            for (var i = 0; i < Items.Count; i++)
            {
                var index = Items[i];
                var color = colors[index];
                if (color.R < _rMin) { _rMin = color.R; }
                if (color.R > _rMax) { _rMax = color.R; }
                if (color.G < _gMin) { _gMin = color.G; }
                if (color.G > _gMax) { _gMax = color.G; }
                if (color.B < _bMin) { _bMin = color.B; }
                if (color.B > _bMax) { _bMax = color.B; }
                total += counts[index];
            }

            PixelCount = total;
        }

        internal int LongestChannel()
        {
            var dr = _rMax - _rMin;
            var dg = _gMax - _gMin;
            var db = _bMax - _bMin;
            if (dr >= dg && dr >= db)
            {
                return 0;
            }

            return dg >= db ? 1 : 2;
        }

        internal int LongestRange()
        {
            var dr = _rMax - _rMin;
            var dg = _gMax - _gMin;
            var db = _bMax - _bMin;
            return Math.Max(dr, Math.Max(dg, db));
        }

        internal Color32 AverageColor(List<Color32> colors, List<int> counts)
        {
            long r = 0;
            long g = 0;
            long b = 0;
            long total = 0;
            for (var i = 0; i < Items.Count; i++)
            {
                var index = Items[i];
                var color = colors[index];
                var count = counts[index];
                r += color.R * (long)count;
                g += color.G * (long)count;
                b += color.B * (long)count;
                total += count;
            }

            if (total == 0)
            {
                return Color32.Black;
            }

            return new Color32((byte)(r / total), (byte)(g / total), (byte)(b / total), 255);
        }
    }
}
