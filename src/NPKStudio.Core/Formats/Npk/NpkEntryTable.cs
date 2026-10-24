using System.Buffers.Binary;

using NPKStudio.Core.Exceptions;

namespace NPKStudio.Core.Formats.Npk;

/// <summary>
///     NPK 条目表的字节布局与解析（纯函数，无状态、无 IO）。
///     <para>
///         布局（<c>FORMAT-VERIFIED.md</c> §1，真机样本逐字节校准）：
///         <code>
/// 0        : "NeoplePack_Bill" + 0x00        16 字节魔数
/// 16       : int32 count
/// 20       : Entry[count]，每项 264 字节（int32 offset, int32 length, byte[256] 加密路径）
/// 20+264n  : byte[32] SHA256
/// 52+264n  : 数据区
/// </code>
///     </para>
///     <para>
///         从 <c>NpkArchive</c> 抽出来的：那类原先既管 IO 又管解析还管缓存，
///         这里只保留"字节 → 条目"这一段，读字节的事交给调用方（<see cref="NpkRawReader" />）。
///     </para>
/// </summary>
internal static class NpkEntryTable
{
    /// <summary>条目表每项字节数。</summary>
    public const int EntrySize = 264;

    /// <summary>条目表区之后的 SHA256 校验码字节数。</summary>
    public const int HashSize = 32;

    /// <summary>
    ///     条目表区（魔数 + 条目数 + 条目）之后、数据区之前的固定前缀：16 + 4 + 32 = 52。
    ///     <para>所以数据区起点 = <c>52 + 264 × count</c>，不是 53。</para>
    /// </summary>
    public const int DataStartBase = 52;

    /// <summary>条目数字段相对文件起点的偏移。</summary>
    public const int CountOffset = 16;

    /// <summary>条目表相对文件起点的偏移（魔数 16 + 条目数 4）。</summary>
    public const int EntryTableOffset = 20;

    /// <summary>
    ///     校验魔数与条目数，返回条目数量。
    ///     </summary>
    /// <param name="head">文件开头至少 <see cref="EntryTableOffset" /> 字节。</param>
    /// <param name="fileLength">文件总长度（用于拦截"声明了不存在的条目"）。</param>
    /// <returns>条目数量（≥ 0）。</returns>
    /// <exception cref="NpkFormatException">魔数不符、条目数为负、或条目表超出文件长度。</exception>
    public static int ReadCount(ReadOnlySpan<byte> head, long fileLength)
    {
        if (fileLength < DataStartBase)
        {
            throw new NpkFormatException("文件不是有效的 NPK 包");
        }

        if (head.Length < EntryTableOffset
            || !head[..NpkCipher.PackFlagBytes.Length].SequenceEqual(NpkCipher.PackFlagBytes))
        {
            throw new NpkFormatException("文件不是有效的 NPK 包");
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(head[CountOffset..]);
        var minimum = (long)DataStartBase + ((long)count * EntrySize);
        if (count < 0 || minimum > fileLength)
        {
            throw new NpkFormatException("文件不是有效的 NPK 包");
        }

        return count;
    }

    /// <summary>
    ///     解析条目表字节（<paramref name="count" /> × 264 字节）。
    ///     </summary>
    /// <param name="tableBytes">条目表区字节。</param>
    /// <param name="count">条目数量（来自 <see cref="ReadCount" />）。</param>
    /// <param name="fileLength">文件总长度（用于逐条校验偏移/长度不越界）。</param>
    /// <returns>按文件顺序排列的条目。</returns>
    /// <exception cref="NpkFormatException">某个条目的 offset/length 越界。</exception>
    public static List<NpkEntry> Parse(ReadOnlySpan<byte> tableBytes, int count, long fileLength)
    {
        var result = new List<NpkEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var at = i * EntrySize;
            var offset = BinaryPrimitives.ReadInt32LittleEndian(tableBytes[at..]);
            var length = BinaryPrimitives.ReadInt32LittleEndian(tableBytes[(at + 4)..]);
            var path = NpkCipher.DecodePath(tableBytes.Slice(at + 8, NpkCipher.PathLength));

            if (offset < 0 || length < 0 || (long)offset + length > fileLength)
            {
                throw new NpkFormatException(
                    $"文件不是有效的 NPK 包：第 {i} 条 \"{path}\" 越界（offset={offset}, length={length}, 文件长度={fileLength}）。");
            }

            result.Add(new NpkEntry
            {
                Path = path,
                Offset = offset,
                Length = length
            });
        }

        return result;
    }
}
