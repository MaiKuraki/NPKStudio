using System.Text.Json;

namespace NPKStudio.Desktop.Services;

/// <summary>
///     全局界面设置（与具体素材无关的"观看习惯"）。
///     <para>
///         目前只存播放倍率：帧率是<b>观看习惯</b>而不是素材属性，所以全局一份，
///         不放进按 IMG 记忆的 <c>graphic-effect.json</c>。
///     </para>
///     <para>
///         存储：<c>%LOCALAPPDATA%\NPKStudio\settings.json</c>，键 <c>playback.rateIndex</c>。
///         读写失败一律回退默认值。
///     </para>
/// </summary>
public sealed class AppSettingsStore {
    /// <summary>默认倍率下标（3 = 1×，基准 8fps）。</summary>
    public const int DefaultRateIndex = 3;

    private const string FileName = "settings.json";
    private const string RateIndexKey = "playback.rateIndex";
    private const string ManifestPathKey = "pack.manifestPath";

    private readonly Dictionary<string, int> _numbers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;

    private AppSettingsStore(string path) {
        _path = path;
    }

    /// <summary>
    ///     配置目录。
    ///     <para>
    ///         默认 <c>%LOCALAPPDATA%\NPKStudio</c>；环境变量 <c>NPKSTUDIO_CONFIG_DIR</c> 可覆盖
    ///         （自动化测试用它把配置写到临时目录，也方便做成便携版）。
    ///     </para>
    /// </summary>
    public static string ConfigDirectory {
        get {
            var overridden = Environment.GetEnvironmentVariable("NPKSTUDIO_CONFIG_DIR");
            return string.IsNullOrWhiteSpace(overridden)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NPKStudio")
                : overridden;
        }
    }

    /// <summary>播放倍率下标（0~8）。</summary>
    public int RateIndex {
        get => _numbers.TryGetValue(RateIndexKey, out var value) ? value : DefaultRateIndex;
        set => _numbers[RateIndexKey] = value;
    }

    /// <summary>
    ///     记住的资源清单路径（<c>IMG: sprite/...</c> 的 txt）。
    ///     <para>设一次就一直生效，导出 NPK 时自动用它解析包内路径，不必每次都选文件。</para>
    /// </summary>
    public string? ManifestPath {
        get => _strings.TryGetValue(ManifestPathKey, out var value) && value.Length > 0 ? value : null;
        set {
            if (string.IsNullOrWhiteSpace(value)) {
                _strings.Remove(ManifestPathKey);
            } else {
                _strings[ManifestPathKey] = value;
            }
        }
    }

    /// <summary>读取配置；文件不存在或损坏时返回默认值。</summary>
    public static AppSettingsStore Load() {
        var store = new AppSettingsStore(Path.Combine(ConfigDirectory, FileName));
        try {
            if (!File.Exists(store._path)) return store;

            using var document = JsonDocument.Parse(File.ReadAllText(store._path));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return store;

            foreach (var property in document.RootElement.EnumerateObject()) {
                if (property.Value.TryGetInt32(out var number)) {
                    store._numbers[property.Name] = number;
                } else if (property.Value.ValueKind == JsonValueKind.String) {
                    store._strings[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        } catch {
            // 配置损坏不影响启动
        }

        return store;
    }

    /// <summary>落盘（调用方负责防抖/关窗 flush）。</summary>
    public void Save() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            // 手写 JSON：项目禁用了反射序列化（JsonSerializerIsReflectionEnabledByDefault=false）
            using var stream = File.Create(_path);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            foreach (var pair in _numbers) {
                writer.WriteNumber(pair.Key, pair.Value);
            }

            foreach (var pair in _strings) {
                writer.WriteString(pair.Key, pair.Value);
            }

            writer.WriteEndObject();
        } catch {
            // 写失败不该打断使用
        }
    }
}
