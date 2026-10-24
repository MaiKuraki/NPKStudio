using System.Buffers.Binary;

using NPKStudio.Core.Exceptions;
using NPKStudio.Core.Formats;
using NPKStudio.Core.Formats.Npk;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NPKStudio.Core.Tests;

/// <summary>
///     写出后自检（<see cref="NpkWriter.VerifySources" />）的测试。
///     <para>
///         背景：<c>NpkArchive.Save</c> 改成流式后，偏移只在 <c>NpkWriter.WriteStreaming</c> 里算一次，
///         自检如果只核"长度"不核"offset"，那么"偏移算错但长度对"的坏包会被判为成功。
///         本文件锁定 offset 校验存在且生效。
///     </para>
/// </summary>
[TestClass]
public sealed class NpkWriterVerifyTests
{
    /// <summary>路径字段在条目内的偏移（条目 = offset(4) + length(4) + path(256)）。</summary>
    private const int EntryTableOffset = 20;

    /// <summary>条目表每项字节数（与 <see cref="NpkArchive.EntrySize" /> 同源）。</summary>
    private const int EntrySize = 264;

    /// <summary>正常写出的包必须通过自检（防"加了校验就误报"）。</summary>
    [TestMethod]
    public void VerifySources_AcceptsFreshlyWrittenPackage()
    {
        using var scope = new TempPackage();
        var sources = scope.Sources;
        NpkWriter.WriteFileStreaming(scope.FilePath, sources);

        var length = new FileInfo(scope.FilePath).Length;
        NpkWriter.VerifySources(scope.FilePath, sources, length);   // 不抛即通过
    }

    /// <summary>篡改第 2 条的 offset（长度不变）→ 自检必须失败，且报 offset。</summary>
    [TestMethod]
    public void VerifySources_DetectsTamperedOffset()
    {
        using var scope = new TempPackage();
        var sources = scope.Sources;
        NpkWriter.WriteFileStreaming(scope.FilePath, sources);

        // 篡改：把第 2 条的 offset 整体后移 8 字节（长度字段不动 → 总长度仍对得上）
        var bytes = File.ReadAllBytes(scope.FilePath);
        var secondOffsetPos = EntryTableOffset + EntrySize;
        var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(secondOffsetPos));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(secondOffsetPos), original + 8);
        File.WriteAllBytes(scope.FilePath, bytes);

        var length = new FileInfo(scope.FilePath).Length;
        var ex = Assert.ThrowsExactly<NpkFormatException>(
            () => NpkWriter.VerifySources(scope.FilePath, sources, length));

        StringAssert.Contains(ex.Message, "offset");
    }

    /// <summary>篡改某条长度 → 自检仍然要报（长度校验不能被 offset 校验掩盖）。</summary>
    [TestMethod]
    public void VerifySources_DetectsTamperedLength()
    {
        using var scope = new TempPackage();
        var sources = scope.Sources;
        NpkWriter.WriteFileStreaming(scope.FilePath, sources);

        var bytes = File.ReadAllBytes(scope.FilePath);
        var secondLengthPos = EntryTableOffset + EntrySize + 4;
        var original = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(secondLengthPos));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(secondLengthPos), original + 4);
        File.WriteAllBytes(scope.FilePath, bytes);

        var length = new FileInfo(scope.FilePath).Length;
        var ex = Assert.ThrowsExactly<NpkFormatException>(
            () => NpkWriter.VerifySources(scope.FilePath, sources, length));

        StringAssert.Contains(ex.Message, "长度");
    }

    /// <summary>临时包：两个条目（16 / 32 字节），用于各种篡改测试。</summary>
    private sealed class TempPackage : IDisposable
    {
        private readonly string _directory;

        public TempPackage()
        {
            _directory = Path.Combine(Path.GetTempPath(), "npkverify-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            FilePath = System.IO.Path.Combine(_directory, "verify.npk");
            Sources =
            [
                NpkEntrySource.FromBytes("sprite/a.img", new byte[16]),
                NpkEntrySource.FromBytes("sprite/b.img", new byte[32])
            ];
        }

        public string FilePath { get; }

        public NpkEntrySource[] Sources { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // 清理失败不影响断言结果
            }
        }
    }
}
