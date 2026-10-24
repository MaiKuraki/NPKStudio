using NPKStudio.Core.Formats.Npk;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>
///     左侧文件列表的一项。把两种来源统一成一个类型：
///     <list type="bullet">
///         <item>NPK 包内的条目（<see cref="Npk" /> 非空）；</item>
///         <item>"NPK 源目录"里的 <c>.img</c> 文件（<see cref="FilePath" /> 非空）。</item>
///     </list>
///     <para>
///         之所以抽这一层：列表原先直接绑 <see cref="NpkEntry" />，等于把 UI 绑死在 NPK 容器上，
///         加入文件夹模式后无法复用同一套"列表 → 选中 → 帧加载 → 画布"流程。
///     </para>
///     <para>
///         公开成员刻意与 <see cref="NpkEntry" /> 对齐（<see cref="Path" /> / <see cref="Name" /> /
///         <see cref="FrameCount" />），这样 XAML 的条目模板只需要换 <c>DataType</c>，绑定表达式不用动。
///     </para>
/// </summary>
public sealed class EntryItem : ViewModelBase {
    private int? _frameCount;

    private EntryItem(string path, string name, long length, NpkEntry? npk, string? filePath) {
        Path = path;
        Name = name;
        Length = length;
        Npk = npk;
        FilePath = filePath;
        _frameCount = npk?.FrameCount;
    }

    /// <summary>显示用路径：NPK 内完整路径，或相对源目录的路径（如 <c>icon.img</c>）。</summary>
    public string Path { get; }

    /// <summary>列表显示的名字（路径最后一段）。</summary>
    public string Name { get; }

    /// <summary>字节数：NPK 条目长度，或磁盘文件长度。</summary>
    public long Length { get; }

    /// <summary>
    ///     帧数；未解析时为 <c>null</c>，选中该条目解析后回填（供列表徽标）。
    ///     <para>带变更通知：回填后左侧徽标立即刷新，无需重开或重新选中。</para>
    /// </summary>
    public int? FrameCount {
        get => _frameCount;
        set => SetProperty(ref _frameCount, value);
    }

    /// <summary>是否来自文件夹（<c>true</c>）而不是 NPK 包内条目。</summary>
    public bool IsFileEntry => FilePath is not null;

    /// <summary>包内条目；文件夹来源时为 <c>null</c>。</summary>
    internal NpkEntry? Npk { get; }

    /// <summary>磁盘文件绝对路径；NPK 来源时为 <c>null</c>。</summary>
    internal string? FilePath { get; }

    /// <summary>由 NPK 条目构造。</summary>
    /// <param name="entry">包内条目。</param>
    /// <returns>列表项。</returns>
    public static EntryItem FromNpk(NpkEntry entry) {
        ArgumentNullException.ThrowIfNull(entry);
        return new EntryItem(entry.Path, entry.Name, entry.Length, entry, null);
    }

    /// <summary>由磁盘文件构造。</summary>
    /// <param name="relativePath">相对源目录的路径（仅用于显示，<b>不要</b>拿它去猜 NPK 内路径）。</param>
    /// <param name="fullPath">磁盘文件绝对路径。</param>
    /// <param name="length">文件字节数。</param>
    /// <returns>列表项。</returns>
    public static EntryItem FromFile(string relativePath, string fullPath, long length) {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        var name = System.IO.Path.GetFileName(relativePath.Replace('\\', '/'));
        return new EntryItem(relativePath, name, length, null, fullPath);
    }

    /// <summary>返回可读文本，便于日志定位。</summary>
    public override string ToString() => Path;
}
