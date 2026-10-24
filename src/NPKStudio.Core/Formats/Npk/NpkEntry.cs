namespace NPKStudio.Core.Formats.Npk;

/// <summary>NPK 内的一个条目（一个 .img 文件）。</summary>
public sealed class NpkEntry {
    /// <summary>条目在包内的相对路径，例如 <c>sprite/interface/emoticon/against.img</c>。</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>数据区起始偏移。</summary>
    public int Offset { get; init; }

    /// <summary>数据区字节长度。</summary>
    public int Length { get; init; }

    /// <summary>帧数（首次解析该 IMG 后回填，用于列表徽标；未解析时为 <c>null</c>）。</summary>
    public int? FrameCount { get; internal set; }

    /// <summary>是否被编辑过。未编辑的条目在保存时按原始字节直拷。</summary>
    public bool IsModified { get; internal set; }

    /// <summary>文件名（路径最后一段）。</summary>
    /// <remarks>
    ///     <see cref="Path" /> 是 <c>init</c>（构造后不可变），所以只算一次并缓存。
    ///     列表每帧重绘都会读它，原来每次访问都要 <c>LastIndexOf</c> + 切一个新字符串。
    /// </remarks>
    public string Name => _name ??= SplitName();

    /// <summary>所在目录（不含文件名，末尾不带斜杠）。</summary>
    public string Directory => _directory ??= SplitDirectory();

    private string? _name;
    private string? _directory;

    private string SplitName()
    {
        var index = Path.LastIndexOf('/');
        return index >= 0 ? Path[(index + 1)..] : Path;
    }

    private string SplitDirectory()
    {
        var index = Path.LastIndexOf('/');
        return index > 0 ? Path[..index] : string.Empty;
    }

    /// <inheritdoc />
    public override string ToString() => Path;
}
