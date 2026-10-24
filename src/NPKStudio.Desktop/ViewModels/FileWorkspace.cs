using NPKStudio.Core.Formats;
using NPKStudio.Core.Formats.Npk;
using NPKStudio.Core.Imaging;
using NPKStudio.Desktop.Localization;
using NPKStudio.Desktop.Services;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>一次文件操作的结果：成功与否 + 已经格式化好的状态栏文案。</summary>
internal readonly record struct OperationResult(bool Success, string StatusMessage);

/// <summary>换图操作的结果：除 <see cref="OperationResult" /> 外带回加载的位图与目标帧（供 VM 刷新缩略图）。</summary>
internal readonly record struct ReplaceResult(bool Success, string StatusMessage, RgbaImage? Image, FrameViewModel? Frame);

/// <summary>
///     文件工作区：保存、导出、换图、打包成 NPK。
///     <para>
///         <b>本类无状态</b>：所有输入（归档/目录/当前 IMG/选中项）以参数传入，返回结果对象，
///         由 VM 负责赋给 <see cref="MainWindowViewModel.Status" /> 与 raise——不反向持有 VM、不发通知。
///     </para>
/// </summary>
internal sealed class FileWorkspace
{
    private static TranslationCatalog Loc => TranslationCatalog.Instance;

    public OperationResult ExportCurrentPng(RgbaImage? rawImage, RgbaImage? currentImage, string path)
    {
        var source = rawImage ?? currentImage;
        if (source is null)
        {
            return new OperationResult(false, Loc["status.noFrame"]);
        }

        ImageIo.SavePng(source, path);
        return new OperationResult(true, Loc.F("status.exported", Path.GetFileName(path), source.Width, source.Height));
    }

    public async Task<OperationResult> SaveAsync(
        string? path, NpkArchive? archive, FolderEntrySource? folder, ImgFile? currentImg, EntryItem? selectedEntry)
    {
        if (folder is not null)
        {
            return await SaveToFolderAsync(folder, currentImg, selectedEntry);
        }

        if (archive is null || string.IsNullOrEmpty(path))
        {
            return new OperationResult(false, Loc["status.noArchive"]);
        }

        return await SaveArchiveAsync(path, archive);
    }

    private async Task<OperationResult> SaveArchiveAsync(string path, NpkArchive archive)
    {
        try
        {
            await Task.Run(() => archive.Save(path));
            return new OperationResult(true, Loc.F("status.saved", Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            return new OperationResult(false, Loc.F("status.saveFailed", ex.Message));
        }
    }

    private async Task<OperationResult> SaveToFolderAsync(FolderEntrySource folder, ImgFile? currentImg, EntryItem? selectedEntry)
    {
        if (currentImg is null || selectedEntry is null || !selectedEntry.IsFileEntry)
        {
            return new OperationResult(false, Loc["status.noFrame"]);
        }

        try
        {
            await Task.Run(() => folder.Save(selectedEntry, currentImg));
            return new OperationResult(true, Loc.F("status.folderSaved", selectedEntry.Name));
        }
        catch (Exception ex)
        {
            return new OperationResult(false, Loc.F("status.saveFailed", ex.Message));
        }
    }

    public async Task<ReplaceResult> ReplaceFrameAsync(ImgFile image, FrameViewModel frame, string imagePath)
    {
        try
        {
            var rgba = await Task.Run(() => ImageIo.Load(imagePath));
            image.ReplaceFrameImage(frame.Frame, rgba, ColorBits.UNKNOWN);
            return new ReplaceResult(true, Loc.F("status.replaced", frame.IndexText, rgba.Width, rgba.Height), rgba, frame);
        }
        catch (Exception ex)
        {
            return new ReplaceResult(false, Loc.F("status.replaceFailed", ex.Message), null, frame);
        }
    }

    /// <summary>
    ///     把"NPK 源目录 / 单个 img"导出成一个 NPK。
    ///     <para>
    ///         <paramref name="manifestPath" /> 为 <c>null</c>（或空白）时<b>不读清单</b>：
    ///         包内目录由输出文件名推断（<see cref="ResourceManifest.InferPath" />），
    ///         即"用户把输出名起成正确的 NPK 名 → 包内路径就对"。
    ///     </para>
    /// </summary>
    /// <param name="folder">源目录（含单 img 模式）。</param>
    /// <param name="manifestPath">清单路径；<c>null</c> 表示用推断。</param>
    /// <param name="outputPath">用户选定的输出 NPK 路径。</param>
    public async Task<OperationResult> ExportFolderToNpkAsync(FolderEntrySource folder, string? manifestPath, string outputPath)
    {
        try
        {
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                outputDirectory = Directory.GetCurrentDirectory();
            }

            // 推断用的名字一律取"用户最终选定的输出文件名"，而不是源文件夹名。
            var npkStem = Path.GetFileNameWithoutExtension(outputPath);
            var useManifest = !string.IsNullOrWhiteSpace(manifestPath);

            var outcome = await Task.Run(() =>
            {
                var manifest = useManifest ? ResourceManifest.Load(manifestPath!) : null;
                return NpkPackService.PackOne(folder.RootPath, manifest, outputDirectory, null, npkStem);
            });

            if (!outcome.Success)
            {
                return new OperationResult(false, outcome.EntryCount == 0
                    ? Loc.F("status.exportNpkNoMatch", outcome.Error)
                    : Loc.F("status.exportNpkFailed", outcome.Error));
            }

            var produced = Path.Combine(outputDirectory, outcome.NpkName);
            if (!File.Exists(produced))
            {
                return new OperationResult(false, Loc.F("status.exportNpkFailed", produced));
            }

            if (!string.Equals(Path.GetFullPath(produced), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Move(produced, outputPath, overwrite: true);
            }

            // 路径来源必须如实反映：推断出来的路径只有 ~48.6% 正确，不能静默当成成功。
            var name = Path.GetFileName(outputPath);
            var count = outcome.EntryCount;
            return new OperationResult(true, !useManifest
                ? Loc.F("status.exportNpkOkInferred", name, count)
                : outcome.InferredHits > 0
                    ? Loc.F("status.exportNpkWarn", name, count, outcome.InferredHits)
                    : outcome.NameHits > 0
                        ? Loc.F("status.exportNpkOkByName", name, count, outcome.NameHits)
                        : Loc.F("status.exportNpkOk", name, count));
        }
        catch (Exception ex)
        {
            return new OperationResult(false, Loc.F("status.exportNpkFailed", ex.Message));
        }
    }

    /// <summary>
    ///     从客户端已有的 NPK 目录反向生成资源清单——回答"清单从哪来"。
    ///     <para>扫描在后台线程；<see cref="ResourceManifestWriter" /> 逐个开包写路径，不会把整目录的包同时驻留。</para>
    /// </summary>
    /// <param name="npkDirectory">装有 NPK 的目录（典型是 <c>ImagePacks2</c>）。</param>
    /// <param name="outputPath">清单输出路径。</param>
    public async Task<OperationResult> GenerateManifestAsync(string npkDirectory, string outputPath)
    {
        try
        {
            var result = await Task.Run(() => ResourceManifestWriter.Write(npkDirectory, outputPath));
            return new OperationResult(
                true,
                Loc.F("status.manifestGenerated", result.NpkCount, Path.GetFileName(outputPath), result.PathCount));
        }
        catch (Exception ex)
        {
            return new OperationResult(false, Loc.F("status.manifestGenerateFailed", ex.Message));
        }
    }
}
