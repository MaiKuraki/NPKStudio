using System.Text.Json;
using NPKStudio.Desktop.Controls;

namespace NPKStudio.Desktop.Services;

/// <summary>
///     按 IMG 记住「效果」选择（对应 ani 的 <c>[GRAPHIC EFFECT]</c>）。
///     <para>
///         存储：<c>%LOCALAPPDATA%\NPKStudio\graphic-effect.json</c>，键规则：文件夹模式
///         <c>文件夹名/相对路径</c>，NPK 模式 <c>NPK 文件名/条目路径</c>。
///     </para>
///     <para>
///         兼容历史格式：布尔（<c>true</c>）与旧枚举名（<c>RealBlend</c> / <c>Dodge</c> /
///         <c>Dark</c> / <c>Xor</c> / <c>Monochrome</c>）一律映射为 <c>LinearDodge</c>，
///         未知值回退 <c>None</c>；<c>false</c> 映射为 <c>None</c>。读写失败一律回退默认值。
///     </para>
/// </summary>
public sealed class GraphicEffectStore {
    private const string FileName = "graphic-effect.json";

    private readonly Dictionary<string, CanvasEffect> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;

    private GraphicEffectStore(string path) {
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

    /// <summary>读取配置；文件不存在或损坏时返回空表。</summary>
    public static GraphicEffectStore Load() {
        var store = new GraphicEffectStore(Path.Combine(ConfigDirectory, FileName));
        try {
            if (!File.Exists(store._path)) return store;

            using var document = JsonDocument.Parse(File.ReadAllText(store._path));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return store;

            foreach (var property in document.RootElement.EnumerateObject()) {
                var effect = ReadEffect(property.Value);
                if (effect is { } value) {
                    store._map[property.Name] = value;
                }
            }
        } catch {
            // 配置损坏不影响启动
        }

        return store;
    }

    private static CanvasEffect? ReadEffect(JsonElement value) {
        switch (value.ValueKind) {
            case JsonValueKind.Object:
                // 曾经存过 { effect, color } 的结构：取 effect，颜色已废弃
                return value.TryGetProperty("effect", out var e) && e.ValueKind == JsonValueKind.String
                    ? ParseEffect(e.GetString())
                    : CanvasEffect.None;

            case JsonValueKind.String:
                return ParseEffect(value.GetString());

            case JsonValueKind.True:
                return CanvasEffect.LinearDodge;

            case JsonValueKind.False:
                return CanvasEffect.None;

            default:
                return null;
        }
    }

    /// <summary>
    ///     解析效果名：只认 <c>None</c> / <c>LinearDodge</c>；
    ///     <c>RealBlend</c>、<c>Dodge</c>、<c>Dark</c>、<c>Xor</c>、<c>Monochrome</c>（已下线的效果）
    ///     一律映射为 <c>LinearDodge</c>，避免丢弃用户既有配置。
    /// </summary>
    private static CanvasEffect ParseEffect(string? name) {
        if (string.IsNullOrWhiteSpace(name)) return CanvasEffect.None;
        if (string.Equals(name, nameof(CanvasEffect.None), StringComparison.OrdinalIgnoreCase)) return CanvasEffect.None;

        // LinearDodge 与所有已下线效果（RealBlend/Dodge/Dark/Xor/Monochrome）都归到线性减淡
        return CanvasEffect.LinearDodge;
    }

    /// <summary>取某个 IMG 的效果；没有记录时返回 <paramref name="fallback" />。</summary>
    public CanvasEffect Get(string? key, CanvasEffect fallback) =>
        key is not null && _map.TryGetValue(key, out var value) ? value : fallback;

    /// <summary>记录某个 IMG 的效果（只改内存）。</summary>
    public void Set(string? key, CanvasEffect value) {
        if (string.IsNullOrEmpty(key)) return;
        _map[key] = value;
    }

    /// <summary>落盘（调用方负责防抖）。</summary>
    public void Save() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            // 手写 JSON：本项目禁用了反射序列化（JsonSerializerIsReflectionEnabledByDefault=false）
            using var stream = File.Create(_path);
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            writer.WriteStartObject();
            foreach (var pair in _map) {
                writer.WriteString(pair.Key, pair.Value.ToString());
            }

            writer.WriteEndObject();
        } catch {
            // 写失败不该打断使用
        }
    }
}
