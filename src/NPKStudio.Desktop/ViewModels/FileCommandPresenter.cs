using NPKStudio.Core.Imaging;
using NPKStudio.Desktop.Localization;
using NPKStudio.Desktop.Services;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>
///     文件级命令：另存、导出当前帧、打包成 NPK、资源清单的记住 / 生成。
///     <para>
///         <b>为什么单独成类</b>：这些命令都是"调 <see cref="FileWorkspace" /> → 把结果收敛成一句状态栏文案"，
///         与窗口的帧 / 画布状态无关。留在 VM 里只是一串重复的 try/最后赋 Status 的样板。
///     </para>
///     <para>
///         <b>不写 Status</b>：全部返回 <see cref="OperationResult" />（或文案字符串），由 VM 决定显示在哪里，
///         避免出现"同一条消息两个来源"的竞态。
///     </para>
/// </summary>
internal sealed class FileCommandPresenter
{
    private static TranslationCatalog Loc => TranslationCatalog.Instance;

    private readonly DocumentSession _session;
    private readonly FileWorkspace _workspace;
    private readonly PathLibrary _paths;

    public FileCommandPresenter(DocumentSession session, FileWorkspace workspace, PathLibrary paths)
    {
        _session = session;
        _workspace = workspace;
        _paths = paths;
    }

    /// <summary>已记住的资源清单路径；<c>null</c> 表示还没设置过。</summary>
    public string? ManifestPath => _paths.ManifestPath;

    /// <summary>导出当前帧为 PNG（<b>永远用原始解码结果</b>，不用叠加预览）。</summary>
    /// <param name="raw">原始解码像素。</param>
    /// <param name="current">画布当前图像（raw 为空时的兜底）。</param>
    /// <param name="path">输出路径。</param>
    /// <returns>成功与否 + 状态栏文案。</returns>
    public OperationResult ExportCurrentPng(RgbaImage? raw, RgbaImage? current, string path) =>
        _workspace.ExportCurrentPng(raw, current, path);

    /// <summary>保存当前文档：文件夹模式写回原 <c>.img</c>，NPK 模式另存到 <paramref name="path" />。</summary>
    /// <param name="path">输出路径（文件夹模式忽略）。</param>
    /// <param name="entry">当前条目（文件夹模式写回需要）。</param>
    /// <returns>成功与否 + 状态栏文案。</returns>
    public Task<OperationResult> SaveAsync(string? path, EntryItem? entry) =>
        _workspace.SaveAsync(path, _session.Archive, _session.Folder, _session.CurrentImg, entry);

    /// <summary>把当前包另存到指定路径（未修改条目按原始字节直拷）。</summary>
    /// <param name="path">输出路径。</param>
    /// <returns>成功与否 + 状态栏文案。</returns>
    public Task<OperationResult> SaveArchiveAsync(string path) =>
        _workspace.SaveAsync(path, _session.Archive, null, null, null);

    /// <summary>把"NPK 源目录 / 单个 img"打包成一个 NPK。</summary>
    /// <param name="folder">源目录。</param>
    /// <param name="manifestPath">清单路径；<c>null</c> 时按输出文件名推断包内路径。</param>
    /// <param name="outputPath">输出 NPK 路径。</param>
    /// <returns>成功与否 + 状态栏文案。</returns>
    public Task<OperationResult> ExportFolderToNpkAsync(FolderEntrySource folder, string? manifestPath, string outputPath) =>
        _workspace.ExportFolderToNpkAsync(folder, manifestPath, outputPath);

    /// <summary>记住（或清除）资源清单，立即落盘。</summary>
    /// <param name="path">清单文件路径；<c>null</c>/空白表示清除。</param>
    /// <returns>状态栏文案。</returns>
    public string SetManifest(string? path) {
        if (string.IsNullOrWhiteSpace(path)) {
            _paths.SetManifest(null);
            return Loc["status.manifestCleared"];
        }

        if (!File.Exists(path)) {
            return Loc.F("status.manifestMissing", path);
        }

        _paths.SetManifest(path);
        return Loc.F("status.manifestSet", Path.GetFileName(path), _paths.Current?.Count ?? 0);
    }

    /// <summary>扫描一个装满 NPK 的目录，反向生成资源清单（清单的标准来源）。</summary>
    /// <param name="npkDirectory">装有 NPK 的目录。</param>
    /// <param name="outputPath">清单输出路径。</param>
    /// <returns>成功与否 + 状态栏文案。</returns>
    public Task<OperationResult> GenerateManifestAsync(string npkDirectory, string outputPath) =>
        _workspace.GenerateManifestAsync(npkDirectory, outputPath);
}
