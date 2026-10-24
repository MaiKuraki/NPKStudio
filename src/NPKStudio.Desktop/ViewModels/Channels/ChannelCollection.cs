using NPKStudio.Desktop.Services;

namespace NPKStudio.Desktop.ViewModels.Channels;

/// <summary>
///     窗口持有的通道集合：顺序表 + "当前正在看的那个"。
///     <para>
///         现在应用只开一个通道（构造时就建好），但集合本身不假设数量：
///         <see cref="Add" /> 可以继续追加，<see cref="SetActive" /> 只切换"哪一个是活动通道"，
///         不改任何通道内部的选中 / 播放 / 过滤 / 隐藏 / 变更基线——切回去还是原样。
///     </para>
///     <para>
///         <see cref="ActiveChannel" /> <b>永不为 <c>null</c></b>：外壳的所有转发都直接落在它身上，
///         没有"还没打开文件"这种特例分支。
///     </para>
/// </summary>
internal sealed class ChannelCollection
{
    private readonly AppSettingsStore _settings;
    private readonly FileWorkspace _workspace;
    private readonly PathLibrary _paths;
    private readonly List<FrameChannel> _channels = [];

    /// <summary>测试 / 独立使用的默认上下文（设置目录由 <c>NPKSTUDIO_CONFIG_DIR</c> 等外部条件决定）。</summary>
    public ChannelCollection() : this(CreateContext()) {
    }

    private ChannelCollection((AppSettingsStore Settings, FileWorkspace Workspace, PathLibrary Paths) context)
        : this(context.Settings, context.Workspace, context.Paths) {
    }

    /// <summary>用外壳已有的设置 / 工作区 / 路径库构造（与窗口共用同一份落盘配置）。</summary>
    public ChannelCollection(AppSettingsStore settings, FileWorkspace workspace, PathLibrary paths) {
        _settings = settings;
        _workspace = workspace;
        _paths = paths;

        // 首个通道随窗口一起建好：界面永远有一个活动通道可绑定
        ActiveChannel = Add();
    }

    /// <summary>按加入顺序排列的通道（只读视图，外部不能增删）。</summary>
    public IReadOnlyList<FrameChannel> Channels => _channels;

    /// <summary>当前正在看的通道。</summary>
    public FrameChannel ActiveChannel { get; private set; }

    /// <summary>通道数量。</summary>
    public int Count => _channels.Count;

    /// <summary>活动通道在集合里的下标。</summary>
    public int ActiveIndex => _channels.IndexOf(ActiveChannel);

    /// <summary>活动通道变了（外壳据此重新转发所有属性通知）。</summary>
    public event EventHandler? ActiveChannelChanged;

    /// <summary>追加一个空通道（不改变当前活动通道）。</summary>
    public FrameChannel Add() {
        var channel = new FrameChannel(_settings, _workspace, _paths);
        _channels.Add(channel);
        return channel;
    }

    /// <summary>按序号切换活动通道；越界或切换到自己都直接返回。</summary>
    public void SetActive(int index) {
        if (index < 0 || index >= _channels.Count) return;
        SetActive(_channels[index]);
    }

    /// <summary>把某个已在集合里的通道设为活动通道。</summary>
    public void SetActive(FrameChannel channel) {
        if (ReferenceEquals(ActiveChannel, channel)) return;
        if (!_channels.Contains(channel)) return;

        ActiveChannel = channel;
        ActiveChannelChanged?.Invoke(this, EventArgs.Empty);
    }

    private static (AppSettingsStore Settings, FileWorkspace Workspace, PathLibrary Paths) CreateContext() {
        var settings = AppSettingsStore.Load();
        return (settings, new FileWorkspace(), new PathLibrary(settings));
    }
}
