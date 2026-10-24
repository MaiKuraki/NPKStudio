using Avalonia;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>
///     帧拖拽排序的"插入缝"计算——<b>纯函数</b>，输入各卡片矩形与指针位置，输出插入下标。
///     <para>
///         <b>为什么单独成类</b>：真实拖拽要经过指针事件、命中测试与自动滚动，断言起来很脆；
///         把唯一的决策逻辑（落在哪两个卡片之间）抽成纯函数后，测试直接给矩形就能逐条钉住。
///         与 <c>WrapPanel</c> 的换行无关：按"垂直有交集"把卡片分成行，行内再按左半 / 右半决定插在前面还是后面。
///     </para>
///     <para>
///         返回值语义：<c>0..cards.Count</c>，表示"插到第 N 个元素之前"（<c>Count</c> 即插到末尾），
///         与 <see cref="FrameEditOperations.MoveTo" /> 的 <c>insertIndex</c> 完全一致。
///     </para>
/// </summary>
internal sealed class FrameDropPlanner
{
    /// <summary>
    ///     计算插入缝下标。
    /// </summary>
    /// <param name="cards">各卡片在列表坐标系里的矩形，顺序必须与帧列表一致。</param>
    /// <param name="pointer">指针在同一个坐标系里的位置。</param>
    /// <returns>插入缝下标（<c>0..cards.Count</c>）。</returns>
    public static int InsertIndex(IReadOnlyList<Rect> cards, Point pointer) {
        if (cards.Count == 0) {
            return 0;
        }

        var (rowStart, rowEnd) = ResolveRow(cards, pointer.Y);

        // 行首左侧 → 插到这一行最前；行尾右侧 → 插到这一行最后
        if (pointer.X <= cards[rowStart].Left) {
            return rowStart;
        }

        if (pointer.X >= cards[rowEnd].Right) {
            return rowEnd + 1;
        }

        var best = rowStart;
        var bestDistance = double.MaxValue;
        for (var i = rowStart; i <= rowEnd; i++) {
            var card = cards[i];
            var distance = HorizontalDistance(card, pointer.X);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = i;
        }

        // 落在左半 → 插在它前面；右半 → 插在它后面
        return pointer.X < cards[best].Center.X ? best : best + 1;
    }

    /// <summary>
    ///     找出指针所在的那一行（垂直有交集的连续卡片算一行）；不在任何行里时取垂直距离最近的行。
    /// </summary>
    /// <param name="cards">卡片矩形。</param>
    /// <param name="y">指针 Y。</param>
    /// <returns>该行的首尾下标（闭区间）。</returns>
    private static (int Start, int End) ResolveRow(IReadOnlyList<Rect> cards, double y) {
        var bestStart = 0;
        var bestEnd = 0;
        var bestDistance = double.MaxValue;

        var start = 0;
        while (start < cards.Count) {
            var end = start;
            // 同一行的卡片垂直方向一定有交集；换行后 Top 会落到上一行 Bottom 之下
            // （0.5px 容差吃掉布局取整带来的 123.9 / 124.0 抖动，否则两行会被并成一行）
            while (end + 1 < cards.Count && cards[end + 1].Top < cards[end].Bottom - 0.5) {
                end++;
            }

            var top = cards[start].Top;
            var bottom = cards[start].Bottom;
            for (var i = start + 1; i <= end; i++) {
                top = Math.Min(top, cards[i].Top);
                bottom = Math.Max(bottom, cards[i].Bottom);
            }

            var distance = y < top ? top - y : y > bottom ? y - bottom : 0d;
            if (distance < bestDistance) {
                bestDistance = distance;
                bestStart = start;
                bestEnd = end;
            }

            if (distance <= 0d) {
                break;
            }

            start = end + 1;
        }

        return (bestStart, bestEnd);
    }

    /// <summary>指针到卡片水平范围的距离；落在卡片内部时为 0。</summary>
    private static double HorizontalDistance(Rect card, double x) =>
        x < card.Left ? card.Left - x : x > card.Right ? x - card.Right : 0d;
}
