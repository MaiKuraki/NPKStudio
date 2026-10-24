using System.Buffers.Binary;
using System.Text;

using NPKStudio.Core.Diagnostics;
using NPKStudio.Core.Exceptions;

namespace NPKStudio.Core.Formats.Npk;

/// <summary>
///     从"条目列表"构建 NPK 包（与 <see cref="NpkArchive" /> 的读取方向互补）。
///     <para>
///         字节布局（与 <see cref="NpkArchive" /> / <see cref="NpkCipher" /> 的常量严格对齐，真机样本逐字节验证过）：
///         <code>
/// 0        : "NeoplePack_Bill" + 0x00                     16 字节
/// 16       : int32 count
/// 20       : count × 264 字节 { int32 offset, int32 length, byte[256] 加密路径 }
/// 20+264n  : byte[32] SHA256（对条目表区 len/17*17 字节）
/// 52+264n  : 数据区，按条目顺序连续写入
/// </code>
///         其中 <c>offset</c> 从 <c>52 + 264 × count</c> 起按前序条目长度累加。
///     </para>
///     <para>
///         <b>流式</b>：偏移只依赖长度，因此可以先写完条目表再逐条拷数据，
///         峰值内存与条目总量无关（只与单个条目大小、以及一次拷贝的 64 KB 缓冲有关）。
///     </para>
/// </summary>
public static class NpkWriter
{
    /// <summary>流式拷贝缓冲区大小：64 KB。</summary>
    internal const int CopyBufferSize = 64 * 1024;

    /// <summary>条目表区的固定前缀长度：魔数 16 + 条目数 4 = 20（<see cref="NpkArchive.DataStartBase" /> = 20 + 32）。</summary>
    private const int TableHeaderSize = 20;

    /// <summary>
    ///     <b>流式</b>写出：先把整张条目表算完并写出，再逐条调用 <see cref="NpkEntrySource.WriteData" />。
    ///     <para>数据不会在内存中累积；拷贝使用调用方/实现方自己的缓冲。</para>
    /// </summary>
    /// <param name="destination">目标流（<b>必须可前进</b>；若可定位则额外校验每条实际写入字节数）。</param>
    /// <param name="entries">条目源列表，顺序即写入顺序。</param>
    /// <exception cref="ArgumentNullException">参数为 <c>null</c>。</exception>
    /// <exception cref="ArgumentException">
    ///     路径为空 / 重复 / 超长、长度 ≤ 0、总长度超过 int32 上限。
    /// </exception>
    /// <exception cref="NpkFormatException">
    ///     某个 <see cref="NpkEntrySource.WriteData" /> 实际写入的字节数与 <see cref="NpkEntrySource.Length" /> 不符。
    /// </exception>
    public static void WriteStreaming(Stream destination, IReadOnlyList<NpkEntrySource> entries)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(entries);
        Validate(entries);

        var count = entries.Count;
        var table = new byte[TableHeaderSize + (count * NpkArchive.EntrySize)];
        NpkCipher.PackFlagBytes.CopyTo(table, 0);
        BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(16), count);

        var position = (long)NpkArchive.DataStartBase + ((long)count * NpkArchive.EntrySize);
        for (var i = 0; i < count; i++)
        {
            var entry = entries[i];
            if (position > int.MaxValue)
            {
                throw new ArgumentException($"包总长度超过 {int.MaxValue} 字节，NPK 的 offset 字段是 int32，无法表示。", nameof(entries));
            }

            var at = TableHeaderSize + (i * NpkArchive.EntrySize);
            BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(at), (int)position);
            BinaryPrimitives.WriteInt32LittleEndian(table.AsSpan(at + 4), entry.Length);

            // 直接写进 table 的对应位置，避免每条路径一个临时 byte[256]。
            NpkCipher.EncodePath(entry.Path, table.AsSpan(at + 8, NpkCipher.PathLength));
            position += entry.Length;
        }

        destination.Write(table);

        Span<byte> hash = stackalloc byte[NpkArchive.HashSize];
        NpkCipher.ComputeHash(table, hash);
        destination.Write(hash);

        // 目标流不可定位时无法用 Position 差校验"实际写入 == 声明长度"。
        // 当前所有调用点都是 FileStream/MemoryStream（都可定位），但"跳过校验"必须留痕，
        // 否则将来有人接网络流/管道流会误以为长度校验一直在生效。
        var canVerify = destination.CanSeek;
        var warned = false;

        for (var i = 0; i < count; i++)
        {
            var entry = entries[i];
            var before = canVerify ? destination.Position : -1;
            entry.WriteData(destination);

            if (!canVerify)
            {
                if (!warned)
                {
                    TraceLog.Instance.Warn("目标流不可定位，已跳过条目长度校验（产出的包若长度不符将无法被检出）。");
                    warned = true;
                }

                continue;
            }

            var written = destination.Position - before;
            if (written != entry.Length)
            {
                throw new NpkFormatException(
                    $"条目 \"{entry.Path}\" 声明 {entry.Length} 字节，实际写入 {written} 字节，包已损坏。");
            }
        }

        if (count == 0)
        {
            // 空包合法（只有头 + 校验码，共 52 字节），但通常意味着上游筛选逻辑有问题，留个痕迹。
            TraceLog.Instance.Warn("NpkWriter 写出空包（count = 0），请确认上游条目筛选是否正确。");
        }
    }

    /// <summary>
    ///     <b>流式</b>写到文件：写临时文件（逐条流式拷贝）→ 重新解析自检 → 原子替换目标。
    /// </summary>
    /// <param name="path">目标路径。</param>
    /// <param name="entries">条目源列表，顺序即写入顺序。</param>
    /// <exception cref="ArgumentException">条目非法（见 <see cref="WriteStreaming" />）。</exception>
    /// <exception cref="NpkFormatException">写出后自检失败。</exception>
    /// <exception cref="IOException">目标文件被占用，无法替换。</exception>
    public static void WriteFileStreaming(string path, IReadOnlyList<NpkEntrySource> entries) =>
        WriteFileStreaming(path, entries, beforeReplace: null);

    /// <summary>
    ///     <b>流式</b>写到文件，并在"自检通过、即将替换目标"这一瞬间回调 <paramref name="beforeReplace" />。
    ///     <para>
    ///         用途：<see cref="NpkArchive.Save" /> 保存到自身路径时，必须先释放自己持有的读句柄，
    ///         否则 Windows 下 <c>File.Move</c> 替换会被拒。钩子放在自检之后，保证"写坏了不释放句柄"。
    ///     </para>
    /// </summary>
    /// <param name="path">目标路径。</param>
    /// <param name="entries">条目源列表。</param>
    /// <param name="beforeReplace">替换前回调（可为 <c>null</c>）；抛异常会中止替换并清理临时文件。</param>
    public static void WriteFileStreaming(string path, IReadOnlyList<NpkEntrySource> entries, Action? beforeReplace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(entries);

        var temp = path + ".saving";
        long expectedLength;
        try
        {
            using (var stream = new FileStream(
                       temp,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       CopyBufferSize,
                       FileOptions.SequentialScan))
            {
                WriteStreaming(stream, entries);
                expectedLength = stream.Length;
            }

            Verify(temp, entries, expectedLength);

            // 自检通过后才允许调用方释放资源（例如 NpkArchive 自己的读句柄）
            beforeReplace?.Invoke();

            try
            {
                File.Move(temp, path, overwrite: true);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException($"目标文件被占用，无法替换：{path}。请先关闭正在使用该文件的程序。", ex);
            }
            catch (IOException ex)
            {
                throw new IOException($"写入目标文件失败：{path}。{ex.Message}", ex);
            }
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        TraceLog.Instance.Info($"NPK 已写出：{path}（{entries.Count} 条目 / {expectedLength} 字节）。");
    }

    /// <summary>
    ///     流式写出的自检：核对总长度、条目数、路径、每条长度<b>与每条 offset</b>。
    ///     <para>
    ///         为什么要核 offset：<c>NpkArchive.Save</c> 改成流式后不再自己算偏移，
    ///         偏移只在 <see cref="WriteStreaming" /> 里算一次。只核长度的话，
    ///         "偏移算错但长度对"的损坏包会被判为成功。
    ///     </para>
    /// </summary>
    /// <param name="tempPath">刚写出的文件。</param>
    /// <param name="entries">期望的条目。</param>
    /// <param name="expectedLength">期望的总长度。</param>
    /// <exception cref="NpkFormatException">任一项不符。</exception>
    internal static void VerifySources(string tempPath, IReadOnlyList<NpkEntrySource> entries, long expectedLength)
    {
        var actualLength = new FileInfo(tempPath).Length;
        if (actualLength != expectedLength)
        {
            throw new NpkFormatException($"NPK 写出后自检失败：期望 {expectedLength} 字节，实际 {actualLength} 字节。");
        }

        using var archive = NpkArchive.Open(tempPath);
        if (archive.Entries.Count != entries.Count)
        {
            throw new NpkFormatException($"NPK 写出后自检失败：期望 {entries.Count} 个条目，实际 {archive.Entries.Count} 个。");
        }

        // 期望偏移与 WriteStreaming 的算法一致：数据区从 52 + 264*count 起，按条目顺序累加
        var expectedOffset = (long)NpkArchive.DataStartBase + ((long)entries.Count * NpkArchive.EntrySize);
        for (var i = 0; i < entries.Count; i++)
        {
            var actual = archive.Entries[i];
            if (!string.Equals(actual.Path, entries[i].Path, StringComparison.Ordinal))
            {
                throw new NpkFormatException(
                    $"NPK 写出后自检失败：第 {i} 条路径期望 \"{entries[i].Path}\"，实际 \"{actual.Path}\"（加密路径往返有误）。");
            }

            if (actual.Length != entries[i].Length)
            {
                throw new NpkFormatException(
                    $"NPK 写出后自检失败：第 {i} 条 \"{actual.Path}\" 长度期望 {entries[i].Length}，实际 {actual.Length}。");
            }

            if (actual.Offset != expectedOffset)
            {
                throw new NpkFormatException(
                    $"NPK 写出后自检失败：第 {i} 条 \"{actual.Path}\" offset 期望 {expectedOffset}，实际 {actual.Offset}。");
            }

            expectedOffset += entries[i].Length;
        }
    }

    /// <summary>
    ///     条目合法性校验。重复路径必须拒绝——客户端遇到重复路径只认第一条，会产出"看起来正常实则丢数据"的坏包。
    /// </summary>
    /// <param name="entries">待校验的条目源。</param>
    private static void Validate(IReadOnlyList<NpkEntrySource> entries)
    {
        // 大小写不敏感：DNF 运行在 Windows 上，"a/b.img" 与 "A/b.img" 落到同一文件，等同重复。
        var seen = new HashSet<string>(entries.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            ArgumentNullException.ThrowIfNull(entry, $"第 {i} 个条目为 null。");

            var path = entry.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException($"第 {i} 个条目的路径为空。", nameof(entries));
            }

            if (!seen.Add(path))
            {
                throw new ArgumentException($"第 {i} 个条目路径重复：\"{path}\"（重复路径会让客户端只认第一条）。", nameof(entries));
            }

            var byteCount = Encoding.UTF8.GetByteCount(path);
            if (byteCount >= NpkCipher.PathLength)
            {
                throw new ArgumentException(
                    $"第 {i} 个条目路径 \"{path}\" 的 UTF-8 长度 {byteCount} 超过上限 {NpkCipher.PathLength - 1} 字节（需留 1 字节给 0x00 结尾）。",
                    nameof(entries));
            }

            if (entry.Length <= 0)
            {
                throw new ArgumentException(
                    $"第 {i} 个条目 \"{path}\" 的长度为 {entry.Length}（必须 > 0，length=0 会被误判）。",
                    nameof(entries));
            }
        }
    }

    /// <summary>
    ///     写出后自检：重新解析临时文件，核对总长度、条目数、每条路径与长度。
    /// </summary>
    /// <param name="tempPath">刚写出的临时文件。</param>
    /// <param name="entries">期望的条目。</param>
    /// <param name="expectedLength">期望的总长度。</param>
    /// <exception cref="NpkFormatException">任一项不符。</exception>
    private static void Verify(string tempPath, IReadOnlyList<NpkEntrySource> entries, long expectedLength)
    {
        var actualLength = new FileInfo(tempPath).Length;
        if (actualLength != expectedLength)
        {
            throw new NpkFormatException($"NPK 写出后自检失败：期望 {expectedLength} 字节，实际 {actualLength} 字节。");
        }

        using var archive = NpkArchive.Open(tempPath);
        if (archive.Entries.Count != entries.Count)
        {
            throw new NpkFormatException($"NPK 写出后自检失败：期望 {entries.Count} 个条目，实际 {archive.Entries.Count} 个。");
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var actual = archive.Entries[i];
            if (!string.Equals(actual.Path, entries[i].Path, StringComparison.Ordinal))
            {
                throw new NpkFormatException(
                    $"NPK 写出后自检失败：第 {i} 条路径期望 \"{entries[i].Path}\"，实际 \"{actual.Path}\"（加密路径往返有误）。");
            }

            if (actual.Length != entries[i].Length)
            {
                throw new NpkFormatException(
                    $"NPK 写出后自检失败：第 {i} 条 \"{actual.Path}\" 长度期望 {entries[i].Length}，实际 {actual.Length}。");
            }
        }
    }

    private static void TryDelete(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            TraceLog.Instance.Warn($"清理临时文件失败：{path}", ex);
        }
    }
}
