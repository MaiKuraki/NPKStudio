using System.Collections.ObjectModel;

using Avalonia.Threading;

using NPKStudio.Core.Formats;
using NPKStudio.Core.Formats.Npk;
using NPKStudio.Core.Imaging;
using NPKStudio.Desktop.Services;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>一次 IMG 加载的结果：帧卡片列表、建议选中的首帧、以及解析出的 <see cref="ImgFile" />。</summary>
internal sealed record LoadResult(IReadOnlyList<FrameViewModel> Frames, FrameViewModel? Selected, ImgFile? Image);

/// <summary>
///     文档会话：当前打开的 NPK 包 / 源目录、条目表、当前 IMG，以及打开与加载帧的纯操作。
///     <para>不写 Status、不发 PropertyChanged、不反向持有 VM；结果全通过返回值交给 VM。</para>
/// </summary>
internal sealed class DocumentSession : IDisposable
{
    private CancellationTokenSource? _thumbnailCts;

    public NpkArchive? Archive { get; private set; }
    public FolderEntrySource? Folder { get; private set; }
    public ImgFile? CurrentImg { get; private set; }
    public ObservableCollection<EntryItem> Entries { get; } = new();
    public bool LastOpenSucceeded { get; private set; } = true;
    public string FileName { get; private set; } = "NPKStudio";

    public async Task<IReadOnlyList<EntryItem>> OpenAsync(string path)
    {
        Dispose();

        // 单个 .img 也走文件夹模式：NPK 的 magic 校验不认它，直接 Open 会抛格式异常。
        var isSingleImg = !Directory.Exists(path)
                          && File.Exists(path)
                          && path.EndsWith(".img", StringComparison.OrdinalIgnoreCase);

        if (Directory.Exists(path) || isSingleImg)
        {
            var folder = await Task.Run(() =>
                isSingleImg ? FolderEntrySource.OpenSingleFile(path) : FolderEntrySource.Open(path));
            Archive = null;
            Folder = folder;
            CurrentImg = null;
            FileName = folder.Name;
            LastOpenSucceeded = true;
            Entries.Clear();
            foreach (var e in folder.Entries) Entries.Add(e);
            return folder.Entries;
        }

        var archive = await Task.Run(() => NpkArchive.Open(path));
        Archive = archive;
        Folder = null;
        CurrentImg = null;
        FileName = Path.GetFileName(path);
        LastOpenSucceeded = true;
        var list = archive.Entries.Select(EntryItem.FromNpk).ToList();
        Entries.Clear();
        foreach (var e in list) Entries.Add(e);
        return list;
    }

    public void MarkOpenFailed() => LastOpenSucceeded = false;
    public void ClearImage() => CurrentImg = null;

    public async Task<LoadResult> LoadFramesAsync(EntryItem? item)
    {
        CancelPendingThumbnails();

        if (item is null || (Archive is null && Folder is null))
        {
            CurrentImg = null;
            return new LoadResult(Array.Empty<FrameViewModel>(), null, null);
        }

        var archive = Archive;
        var folder = Folder;
        var img = await Task.Run(() => folder is not null ? folder.Load(item) : archive!.LoadImg(item.Npk!));

        if (!ReferenceEquals(Archive, archive) || !ReferenceEquals(Folder, folder))
        {
            return new LoadResult(Array.Empty<FrameViewModel>(), null, null);
        }

        CurrentImg = img;
        item.FrameCount = img.Frames.Count;

        var frames = img.Frames.Select(f => new FrameViewModel(f)).ToList();
        var selected = PickInitialFrame(frames);

        const int thumbMaxWidth = 90;
        const int thumbMaxHeight = 86;
        var batch = frames.Take(80).ToArray();
        var cts = new CancellationTokenSource();
        _thumbnailCts = cts;

        await Task.Run(() =>
        {
            foreach (var frameItem in batch)
            {
                if (cts.IsCancellationRequested) return;

                RgbaImage? thumb = null;
                try
                {
                    thumb = CreateThumbnail(img.Decode(frameItem.Frame), thumbMaxWidth, thumbMaxHeight);
                }
                catch
                {
                }

                if (thumb is null) continue;
                if (cts.IsCancellationRequested) return;

                Dispatcher.UIThread.Post(() =>
                {
                    if (!cts.IsCancellationRequested) frameItem.Thumbnail = thumb;
                });
            }
        }, cts.Token);

        return new LoadResult(frames, selected, img);
    }

    /// <summary>
    ///     决定"打开 IMG 时默认选中哪一帧"：<b>固定第 0 帧</b>。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         用户明确要求<b>不要跳过首帧</b>。此前按尺寸跳过 1×1 帧的逻辑已回滚：
    ///         它会让"首帧本来有内容"的素材看起来被程序改动过。
    ///     </para>
    ///     <para>
    ///         首帧是 1×1 占位帧时，画布确实会显示一片空白——这是素材本身的真实内容，
    ///         不是加载失败；工具栏的状态栏与帧列表都如实标注了尺寸。
    ///     </para>
    /// </remarks>
    private static FrameViewModel? PickInitialFrame(IReadOnlyList<FrameViewModel> frames)
    {
        return frames.Count == 0 ? null : frames[0];
    }

    internal static RgbaImage? CreateThumbnail(RgbaImage? source, int maxWidth, int maxHeight)
    {
        if (source is null) return null;
        if (source.Width <= maxWidth && source.Height <= maxHeight) return source;

        var scale = Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return source.Resize(width, height);
    }

    public void CancelPendingThumbnails()
    {
        var cts = _thumbnailCts;
        _thumbnailCts = null;
        cts?.Cancel();
        cts?.Dispose();
    }

    public void Dispose()
    {
        CancelPendingThumbnails();
        Archive?.Dispose();
        Archive = null;
        Folder = null;
        CurrentImg = null;
    }
}
