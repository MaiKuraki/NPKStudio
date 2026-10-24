using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace NPKStudio.Desktop.Localization;

/// <summary>
///     XAML 取词标记扩展：<c>Text="{loc:Tr tip.open}"</c>。
///     <para>返回的是绑定而非静态值，因此切换语言时界面会实时刷新。</para>
/// </summary>
public sealed class TrExtension : MarkupExtension {
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    /// <summary>词表 key。</summary>
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) {
        return new Binding($"[{Key}]") {
            Source = TranslationCatalog.Instance,
            Mode = BindingMode.OneWay
        };
    }
}
