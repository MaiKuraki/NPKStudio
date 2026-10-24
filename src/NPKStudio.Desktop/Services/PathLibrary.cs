namespace NPKStudio.Desktop.Services;

/// <summary>
///     包内路径的"权威来源"——记住的资源清单。
///     <para>
///         <c>.img</c> 自身<b>不存储</b>它在 NPK 里的路径，NPK 一解成文件夹这个信息就丢了。
///         所以导出 NPK 时必须有一个路径来源；清单（<c>IMG: sprite/...</c> 的 txt）就是唯一可靠的来源，
///         它可以用 <see cref="ResourceManifestWriter" /> 从任意装有 NPK 的目录反向生成（不需要 PVF）。
///     </para>
///     <para>
///         清单路径<b>持久化在设置里</b>（<c>settings.json</c> 的 <c>pack.manifestPath</c>），
///         设一次就一直生效——用户不必每次导出都重新选文件。
///     </para>
///     <para>解析结果按路径缓存；文件被删除/损坏时返回 <c>null</c>（调用方回退到推断，不会崩）。</para>
/// </summary>
internal sealed class PathLibrary {
    private readonly AppSettingsStore _settings;
    private ResourceManifest? _manifest;
    private string? _loadedFrom;

    public PathLibrary(AppSettingsStore settings) => _settings = settings;

    /// <summary>记住的清单文件路径；未设置时为 <c>null</c>。</summary>
    public string? ManifestPath => _settings.ManifestPath;

    /// <summary>解析好的清单；未设置或读不出来时为 <c>null</c>。</summary>
    public ResourceManifest? Current {
        get {
            var path = ManifestPath;
            if (path is null) {
                _manifest = null;
                _loadedFrom = null;
                return null;
            }

            if (_manifest is not null && string.Equals(_loadedFrom, path, StringComparison.OrdinalIgnoreCase)) {
                return _manifest;
            }

            try {
                _manifest = ResourceManifest.Load(path);
            } catch {
                // 清单被删或格式不对：当作"没有清单"，调用方会回退到推断并警告。
                _manifest = null;
            }

            _loadedFrom = path;
            return _manifest;
        }
    }

    /// <summary>记住（或清空）清单路径并立即落盘。</summary>
    /// <param name="path">清单文件路径；<c>null</c>/空白表示清除。</param>
    public void SetManifest(string? path) {
        _settings.ManifestPath = path;
        _manifest = null;
        _loadedFrom = null;
        _settings.Save();
    }
}
