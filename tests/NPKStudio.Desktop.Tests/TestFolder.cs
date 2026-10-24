using System.Buffers.Binary;

using NPKStudio.Core.Formats;
using NPKStudio.Core.Formats.Npk;
using NPKStudio.Core.Imaging;

namespace NPKStudio.Desktop.Tests;

/// <summary>
///     测试用素材：现场生成"帧数确定、尺寸确定"的合成 IMG，
///     不依赖 <c>X:/some/local/path</c> 之类的本机路径（换台机器也能跑）。
/// </summary>
internal sealed class TestFolder : IDisposable
{
    private readonly string _root;

    private TestFolder(string root)
    {
        _root = root;
        Directory.CreateDirectory(root);
    }

    /// <summary>目录路径。</summary>
    public string Path => _root;

    /// <summary>建一个临时目录。</summary>
    public static TestFolder Create()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "npkstudio-test-" + Guid.NewGuid().ToString("N"));
        return new TestFolder(root);
    }

    /// <summary>
    ///     在目录里写一个合成 IMG（默认 v2 + ARGB1555 + ZLIB）。
    /// </summary>
    /// <param name="fileName">文件名（同时作为相对路径）。</param>
    /// <param name="frameCount">帧数。</param>
    /// <param name="width">每帧宽度。</param>
    /// <param name="height">每帧高度。</param>
    /// <returns>写出的文件路径。</returns>
    public string WriteImg(string fileName, int frameCount, int width, int height) =>
        WriteImg(fileName, frameCount, width, height, ImgVersion.Ver2);

    /// <summary>
    ///     写一个指定版本的合成 IMG。
    ///     <para>
    ///         v5 也接受内联 ARGB1555 帧（handler 把 <c>length != 0</c> 的帧按内联帧处理），
    ///         因此不需要构造纹理页就能得到一个"能被解析、能被标记为实验性"的 v5 文件。
    ///     </para>
    /// </summary>
    /// <param name="fileName">文件名（同时作为相对路径）。</param>
    /// <param name="frameCount">帧数。</param>
    /// <param name="width">每帧宽度。</param>
    /// <param name="height">每帧高度。</param>
    /// <param name="version">目标版本（必须是已注册 handler 的版本，v7+ 请用 <see cref="WriteHeaderOnlyImg" />）。</param>
    /// <returns>写出的文件路径。</returns>
    public string WriteImg(string fileName, int frameCount, int width, int height, ImgVersion version)
    {
        var path = System.IO.Path.Combine(_root, fileName);
        var image = ImgFile.Create(version, fileName);

        for (var i = 0; i < frameCount; i++)
        {
            var frame = image.AddFrame();
            frame.Type = ColorBits.ARGB_1555;
            frame.Compress = CompressMode.ZLIB;
            frame.Width = width;
            frame.Height = height;
            frame.FrameWidth = width;
            frame.FrameHeight = height;

            // 每帧给一个不同的 X，让"联合包围盒"能真实反映帧数/尺寸变化
            frame.X = i;
            frame.Y = 0;

            var raw = new byte[width * height * 2];
            for (var k = 0; k < raw.Length; k++)
            {
                raw[k] = (byte)((i * 7) + k);
            }

            frame.RawData = ZlibCodec.Compress(raw);
            frame.Length = frame.RawData.Length;
        }

        using var stream = File.Create(path);
        image.Write(stream);
        return path;
    }

    /// <summary>
    ///     写一个"指定角色坐标"的单帧 IMG（用于坐标基准 / 标尺这类依赖帧 X/Y 的断言）。
    ///     <para>真实素材（如 30×40、X=235、Y=232 的 aura 帧）无法随仓库携带，这里按同样的几何现场合成。</para>
    /// </summary>
    /// <param name="fileName">文件名。</param>
    /// <param name="width">帧宽。</param>
    /// <param name="height">帧高。</param>
    /// <param name="x">帧在角色坐标系里的 X。</param>
    /// <param name="y">帧在角色坐标系里的 Y。</param>
    /// <returns>写出的文件路径。</returns>
    public string WriteImgAt(string fileName, int width, int height, int x, int y)
    {
        var path = System.IO.Path.Combine(_root, fileName);
        var image = ImgFile.Create(ImgVersion.Ver2, fileName);

        var frame = image.AddFrame();
        frame.Type = ColorBits.ARGB_1555;
        frame.Compress = CompressMode.ZLIB;
        frame.Width = width;
        frame.Height = height;
        frame.FrameWidth = width;
        frame.FrameHeight = height;
        frame.X = x;
        frame.Y = y;

        var raw = new byte[width * height * 2];
        for (var k = 0; k < raw.Length; k++)
        {
            raw[k] = (byte)((k * 13) + 7);
        }

        frame.RawData = ZlibCodec.Compress(raw);
        frame.Length = frame.RawData.Length;

        using var stream = File.Create(path);
        image.Write(stream);
        return path;
    }

    /// <summary>
    ///     写一个"只有合法公共头、0 帧"的 IMG，版本号可指定为 v7/v8/v9。
    ///     <para>
    ///         真实 v7 样本不存在，这里靠手工构造头来验证"仅透传"的空状态；
    ///         读侧会落到 <c>OtherHandler</c>（整段取样、不解析帧），与真机上的表现一致。
    ///     </para>
    /// </summary>
    /// <param name="fileName">文件名。</param>
    /// <param name="version">要写进头的版本号。</param>
    /// <returns>写出的文件路径。</returns>
    public string WriteHeaderOnlyImg(string fileName, ImgVersion version)
    {
        var path = System.IO.Path.Combine(_root, fileName);

        // 布局与 ImgFile.CommonHeaderSize 一致：魔数 16 + IndexLength 8 + version 4 + 帧数 4
        var header = new byte[ImgFile.CommonHeaderSize];
        NpkCipher.ImgFlagBytes.CopyTo(header, 0);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(16), 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), (int)version);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), 0);

        File.WriteAllBytes(path, header);
        return path;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不影响断言结果
        }
    }
}
