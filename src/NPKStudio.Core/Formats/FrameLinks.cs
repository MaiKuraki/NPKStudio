using NPKStudio.Core.Imaging;

namespace NPKStudio.Core.Formats;

/// <summary>
///     帧结构编辑时的 LINK 一致性维护。
///     <para>
///         IMG 的 LINK 帧在索引区里只存"目标帧的下标"，而 <see cref="ImgFrame.LinkTarget" /> 是<b>对象引用</b>。
///         所以增删/移动帧只需要：① 让每个引用者仍然指向一个<b>还活着</b>的帧；② 重排 <see cref="ImgFrame.Index" />。
///     </para>
///     <para>
///         本类只被 <see cref="ImgFile.RemoveFrame" /> / <see cref="ImgFile.InsertFrame" /> /
///         <see cref="ImgFile.MoveFrame" /> 调用，刻意保持 <c>internal static</c> + 无分配（除必须的新数组）。
///     </para>
/// </summary>
internal static class FrameLinks
{
    /// <summary>
    ///     1×1 全透明帧的 zlib 压缩字节（ARGB1555 的 <c>0x0000</c>，每像素 2 字节）。
    ///     <para>
    ///         静态只算一次：删除 LINK 目标、插入空帧都会用到，没必要每次重新压缩。
    ///         <b>共享同一数组是安全的</b>——所有写路径都是"整段赋值"而不是原地改字节。
    ///     </para>
    /// </summary>
    private static readonly byte[] BlankRaw = ZlibCodec.Compress(new byte[2]);

    /// <summary>
    ///     1×1 全透明帧的 zlib 压缩字节（v4/v6 调色板索引格式，每像素 1 字节色号）。
    /// </summary>
    private static readonly byte[] BlankRawPalette = ZlibCodec.Compress(new byte[1]);

    /// <summary>把 <paramref name="target" /> 变成 1×1 全透明普通帧。</summary>
    /// <param name="target">目标帧（通常是一个原本 LINK、但目标已不存在而无法再链接的帧）。</param>
    /// <param name="bytesPerPixel">
    ///     该版本每像素的未压缩字节数：v1/v2 为 2，v4/v6 的色号格式为 1。
    ///     传错会写出"1×1 但数据 2 字节"的帧，v4/v6 解码时必然长度不符。
    /// </param>
    internal static void DegradeToBlank(ImgFrame target, int bytesPerPixel = 2)
    {
        var raw = bytesPerPixel == 1 ? BlankRawPalette : BlankRaw;
        target.Type = ColorBits.ARGB_1555;
        target.Compress = CompressMode.ZLIB;
        target.Width = 1;
        target.Height = 1;
        target.FrameWidth = 1;
        target.FrameHeight = 1;
        target.X = 0;
        target.Y = 0;
        target.Length = raw.Length;
        target.RawData = raw;
        target.LinkTarget = null;
        target.LinkIndex = -1;
        target.Texture = null;
    }

    /// <summary>
    ///     让 <paramref name="target" />（原 LINK）<b>继承</b><paramref name="source" /> 的像素与几何，变成普通帧。
    ///     <para>
    ///         语义来自原版 <c>DeleteImage</c>：删掉一个被引用的帧时，链接位顶替它的内容，
    ///         这样"删掉一帧"不会让其它帧的渲染结果发生变化。
    ///     </para>
    ///     <para>像素字节按引用共享（零拷贝）；写盘时是整段输出，不存在互相污染。</para>
    /// </summary>
    internal static void Adopt(ImgFrame target, ImgFrame source)
    {
        target.Type = source.Type == ColorBits.LINK ? ColorBits.ARGB_1555 : source.Type;
        target.Compress = source.Compress;
        target.Width = source.Width;
        target.Height = source.Height;
        target.FrameWidth = source.FrameWidth;
        target.FrameHeight = source.FrameHeight;
        target.X = source.X;
        target.Y = source.Y;
        target.Length = source.Length;
        target.RawData = source.RawData;
        target.Texture = source.Texture;
        target.LinkTarget = null;
        target.LinkIndex = -1;
    }

    /// <summary>
    ///     删除 <paramref name="victim" /> 之前，把它身上的所有 LINK 引用搬到别处。
    /// </summary>
    /// <param name="frames">整张帧表（此时 victim 还在里面）。</param>
    /// <param name="victim">即将被删除的帧。</param>
    /// <param name="bytesPerPixel">该版本每像素的未压缩字节数（v4/v6 为 1）。</param>
    /// <returns>引用者的新锚点；<c>null</c> 表示没有引用者，可以安全删除。</returns>
    internal static ImgFrame? DetachReferrers(FrameTable frames, ImgFrame victim, int bytesPerPixel = 2)
    {
        ImgFrame? anchor = null;

        // 用具体类型 + 下标循环：不走 IReadOnlyList 的接口枚举，零装箱。
        for (var i = 0; i < frames.Count; i++)
        {
            var other = frames[i];
            if (ReferenceEquals(other, victim) || !ReferenceEquals(other.LinkTarget, victim))
            {
                continue;
            }

            if (anchor is not null)
            {
                other.LinkTarget = anchor;
                continue;
            }

            if (victim.IsLink)
            {
                // victim 自己也是 LINK：引用者直接改指向 victim 的目标。
                if (victim.LinkTarget is not null && !ReferenceEquals(victim.LinkTarget, victim))
                {
                    other.LinkTarget = victim.LinkTarget;
                    anchor = victim.LinkTarget;
                }
                else
                {
                    // 目标的链接本身已断：把这个引用者降级成空帧，作为新的锚点。
                    DegradeToBlank(other, bytesPerPixel);
                    anchor = other;
                }
            }
            else
            {
                Adopt(other, victim);
                anchor = other;
            }
        }

        return anchor;
    }
}
