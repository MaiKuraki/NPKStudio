using System.Security.Cryptography;
using System.Text;

using NPKStudio.Core.Exceptions;

namespace NPKStudio.Core.Formats.Npk;

/// <summary>
///     NPK 容器的常量、路径 XOR 密钥与校验码计算。
///     <para>
///         全部结论来自 <c>docs/refactor/FORMAT-VERIFIED.md</c> §1（真机样本逐字节校准）：
///         密钥 256 字节 = <c>"puchikon@neople dungeon and fighter "</c>（37 字节）+ 循环 <c>"DNF"</c> 填充到 254 + 末位 0。
///     </para>
/// </summary>
public static class NpkCipher
{
    /// <summary>NPK 魔数（含结尾 0x00，共 16 字节）。</summary>
    public const string PackFlag = "NeoplePack_Bill";

    /// <summary>v2/v4/v5/v6 的 IMG 魔数（含结尾 0x00，共 16 字节）。</summary>
    public const string ImgFlag = "Neople Img File";

    /// <summary>v1 的 IMG 魔数（含结尾 0x00，共 18 字节）。</summary>
    public const string ImageFlag = "Neople Image File";

    /// <summary>条目表中每条路径占用的固定字节数。</summary>
    public const int PathLength = 256;

    /// <summary>条目表区之后的 SHA256 校验码字节数。</summary>
    public const int HashSize = 32;

    /// <summary>路径异或密钥的明文头部。</summary>
    private const string KeyHeader = "puchikon@neople dungeon and fighter ";

    private static readonly byte[] KeyBytes = BuildKey();

    /// <summary>
    ///     NPK 魔数的 ASCII 字节（含结尾 0x00，16 字节）。
    /// </summary>
    public static byte[] PackFlagBytes { get; } = Encoding.Latin1.GetBytes(PackFlag + "\0");

    /// <summary>
    ///     v2 系 IMG 魔数的 ASCII 字节（含结尾 0x00，16 字节）。
    /// </summary>
    public static byte[] ImgFlagBytes { get; } = Encoding.Latin1.GetBytes(ImgFlag + "\0");

    /// <summary>
    ///     v1 IMG 魔数的 ASCII 字节（含结尾 0x00，18 字节）。
    /// </summary>
    public static byte[] ImageFlagBytes { get; } = Encoding.Latin1.GetBytes(ImageFlag + "\0");

    /// <summary>
    ///     256 字节路径异或密钥的只读视图。
    /// </summary>
    public static ReadOnlySpan<byte> Key => KeyBytes;

    private static byte[] BuildKey()
    {
        var key = new byte[PathLength];
        var header = Encoding.UTF8.GetBytes(KeyHeader);
        header.CopyTo(key, 0);

        var filler = Encoding.UTF8.GetBytes("DNF");
        for (var i = header.Length; i < PathLength - 1; i++)
        {
            key[i] = filler[i % 3];
        }

        key[PathLength - 1] = 0;
        return key;
    }

    /// <summary>
    ///     解密一条 256 字节的加密路径（遇到 0x00 截断）。
    /// </summary>
    /// <param name="raw">加密路径字节，通常为 256 字节；短于此长度时按可用长度处理。</param>
    /// <returns>解码后的路径（UTF-8）。</returns>
    public static string DecodePath(ReadOnlySpan<byte> raw)
    {
        if (raw.IsEmpty)
        {
            return string.Empty;
        }

        var length = Math.Min(raw.Length, PathLength);
        Span<byte> plain = stackalloc byte[PathLength];
        var count = 0;
        for (var i = 0; i < length; i++)
        {
            var value = (byte)(raw[i] ^ KeyBytes[i]);
            if (value == 0)
            {
                break;
            }

            plain[count++] = value;
        }

        return Encoding.UTF8.GetString(plain[..count]);
    }

    /// <summary>
    ///     加密一条路径为固定 256 字节（补零后整段异或）。
    ///     <para>注意：<b>填充的 0x00 也要参与异或</b>，否则读侧遇到非 0 字节会继续读下去。</para>
    /// </summary>
    /// <param name="path">相对路径，UTF-8 编码后不得达到 256 字节。</param>
    /// <returns>256 字节加密结果。</returns>
    /// <exception cref="NpkFormatException">路径编码后长度 &gt;= 256 字节。</exception>
    public static byte[] EncryptPath(string path) => EncodePath(path);

    /// <summary>
    ///     解密一条 256 字节的加密路径（遇到 0x00 截断）。
    /// </summary>
    /// <param name="raw">加密路径字节。</param>
    /// <returns>解码后的路径（UTF-8）。</returns>
    public static string DecryptPath(ReadOnlySpan<byte> raw) => DecodePath(raw);

    /// <summary>
    ///     加密一条路径为固定 256 字节（补零后整段异或）。
    ///     <para>注意：<b>填充的 0x00 也要参与异或</b>，否则读侧遇到非 0 字节会继续读下去。</para>
    /// </summary>
    /// <param name="path">相对路径，UTF-8 编码后不得达到 256 字节。</param>
    /// <returns>256 字节加密结果。</returns>
    /// <exception cref="NpkFormatException">路径编码后长度 &gt;= 256 字节。</exception>
    public static byte[] EncodePath(string path)
    {
        var result = new byte[PathLength];
        EncodePath(path, result);
        return result;
    }

    /// <summary>
    ///     加密一条路径，直接写进调用方提供的 256 字节目标（避免每条路径一个临时数组）。
    ///     <para>语义与 <see cref="EncodePath(string)" /> 完全一致：先补零，再整段异或。</para>
    /// </summary>
    /// <param name="path">相对路径，UTF-8 编码后不得达到 256 字节。</param>
    /// <param name="destination">目标跨度，长度至少 <see cref="PathLength" />。</param>
    /// <exception cref="ArgumentNullException"><paramref name="path" /> 为 <c>null</c>。</exception>
    /// <exception cref="ArgumentException"><paramref name="destination" /> 长度不足。</exception>
    /// <exception cref="NpkFormatException">路径编码后长度 &gt;= 256 字节。</exception>
    public static void EncodePath(string path, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (destination.Length < PathLength)
        {
            throw new ArgumentException($"目标跨度长度 {destination.Length} 小于 {PathLength}。", nameof(destination));
        }

        var target = destination[..PathLength];
        target.Clear();

        var written = Encoding.UTF8.GetBytes(path, target);
        if (written >= PathLength)
        {
            throw new NpkFormatException($"路径过长（UTF-8 编码后 {written} 字节），NPK 单条路径上限为 {PathLength - 1} 字节。");
        }

        for (var i = 0; i < PathLength; i++)
        {
            target[i] ^= KeyBytes[i];
        }
    }

    /// <summary>
    ///     计算条目表区的 SHA256 校验码（32 字节）。
    ///     <para>
    ///         ⚠️ <b>只对 <c>长度 / 17 * 17</c> 字节做 hash</b>——这个整除截断是格式约定，
    ///         不是笔误（<c>FORMAT-VERIFIED.md</c> §1）。"修正"成完整长度会让所有既有 NPK 校验失败。
    ///     </para>
    /// </summary>
    /// <param name="entryTable">条目表区字节（魔数 + 条目数 + 各条目 264 字节）。</param>
    /// <returns>32 字节哈希；输入为空时返回空数组。</returns>
    public static byte[] ComputeHash(ReadOnlySpan<byte> entryTable)
    {
        if (entryTable.IsEmpty)
        {
            return Array.Empty<byte>();
        }

        var result = new byte[HashSize];
        ComputeHash(entryTable, result);
        return result;
    }

    /// <summary>
    ///     计算条目表区的 SHA256，写进调用方提供的目标跨度（避免每次分配 32 字节数组）。
    ///     <para>截断规则同 <see cref="ComputeHash(ReadOnlySpan{byte})" />；输入为空时写入全 0。</para>
    /// </summary>
    /// <param name="entryTable">条目表区字节。</param>
    /// <param name="destination">目标跨度，长度至少 <see cref="HashSize" />。</param>
    /// <exception cref="ArgumentException"><paramref name="destination" /> 长度不足。</exception>
    public static void ComputeHash(ReadOnlySpan<byte> entryTable, Span<byte> destination)
    {
        if (destination.Length < HashSize)
        {
            throw new ArgumentException($"目标跨度长度 {destination.Length} 小于 {HashSize}。", nameof(destination));
        }

        var target = destination[..HashSize];
        if (entryTable.IsEmpty)
        {
            target.Clear();
            return;
        }

        var length = entryTable.Length / 17 * 17;
        SHA256.HashData(entryTable[..length], target);
    }
}
