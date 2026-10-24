using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using NPKStudio.Core.Formats;
using NPKStudio.Desktop.Localization;

namespace NPKStudio.Desktop.Views.Dialogs;

/// <summary>
///     「实验性格式写回」确认框：继续 / 取消。
///     <para>
///         v4 / v5 / v6 的写回逻辑是按旧版实现推导的，<b>没有真实样本验证过</b>，v5 还会把纹理降级成
///         ARGB1555 重编码。这里只负责问一次，真正的判定（该不该问、问过没有）在
///         <c>ExperimentalSaveConfirmation</c> 里，便于无头单测。
///     </para>
///     <para>回车 = 继续，Esc = 取消（默认动作是"继续"——它不销毁任何数据，只是写回）。</para>
/// </summary>
internal sealed partial class ExperimentalSaveDialog : Window {
    private bool _continue;

    public ExperimentalSaveDialog() {
        InitializeComponent();
        KeyDown += OnDialogKeyDown;
    }

    /// <summary>弹出对话框并等待用户选择；关闭窗口（点 X）视为取消。</summary>
    /// <param name="owner">父窗口。</param>
    /// <param name="version">当前 IMG 的版本（用于文案）。</param>
    /// <returns><c>true</c> = 继续写回，<c>false</c> = 取消。</returns>
    public static async Task<bool> AskAsync(Window owner, ImgVersion version) {
        var dialog = new ExperimentalSaveDialog();
        dialog.MessageText.Text = version == ImgVersion.Ver5
            ? TranslationCatalog.Instance["dialog.experimental.messageV5"]
            : TranslationCatalog.Instance.F("dialog.experimental.message", (int)version);

        await dialog.ShowDialog(owner);
        return dialog._continue;
    }

    private void OnContinue(object? sender, RoutedEventArgs e) => CloseWith(continueSave: true);

    private void OnCancel(object? sender, RoutedEventArgs e) => CloseWith(continueSave: false);

    private void OnDialogKeyDown(object? sender, KeyEventArgs e) {
        switch (e.Key) {
            case Key.Escape:
                CloseWith(continueSave: false);
                e.Handled = true;
                break;
            case Key.Enter:
                CloseWith(continueSave: true);
                e.Handled = true;
                break;
        }
    }

    private void CloseWith(bool continueSave) {
        _continue = continueSave;
        Close();
    }
}
