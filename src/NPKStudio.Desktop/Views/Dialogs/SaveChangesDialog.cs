using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NPKStudio.Desktop.Localization;

namespace NPKStudio.Desktop.Views.Dialogs;

/// <summary>关闭前的三选一结果。</summary>
public enum SaveChangesChoice {
    /// <summary>取消关闭。</summary>
    Cancel,

    /// <summary>不保存，直接关闭。</summary>
    Discard,

    /// <summary>先保存，成功后再关闭。</summary>
    Save
}

/// <summary>
///     「有未保存的修改」确认框：保存 / 不保存 / 取消。
///     <para>
///         <b>为什么自己写而不是引包</b>：只为一个三选一拉一个对话框库不划算；
///         这里用 Avalonia 原生 <see cref="Window" /> + 主窗口同一套 <c>Button.primary</c> 样式，
///         浅深色主题都自动跟随。
///     </para>
///     <para>回车 = 保存，Esc = 取消（默认动作必须是最安全的那个）。</para>
/// </summary>
public sealed partial class SaveChangesDialog : Window {
    private SaveChangesChoice _choice = SaveChangesChoice.Cancel;

    public SaveChangesDialog() {
        InitializeComponent();
        KeyDown += OnDialogKeyDown;
    }

    /// <summary>弹出对话框并等待用户选择；关闭窗口（点 X）视为取消。</summary>
    /// <param name="owner">父窗口。</param>
    /// <param name="fileName">要显示的文件名。</param>
    /// <returns>用户的选择。</returns>
    public static async Task<SaveChangesChoice> AskAsync(Window owner, string fileName) {
        var dialog = new SaveChangesDialog();
        dialog.MessageText.Text = TranslationCatalog.Instance.F("dialog.close.message", fileName);

        await dialog.ShowDialog(owner);
        return dialog._choice;
    }

    private void OnSave(object? sender, RoutedEventArgs e) => CloseWith(SaveChangesChoice.Save);

    private void OnDiscard(object? sender, RoutedEventArgs e) => CloseWith(SaveChangesChoice.Discard);

    private void OnCancel(object? sender, RoutedEventArgs e) => CloseWith(SaveChangesChoice.Cancel);

    private void OnDialogKeyDown(object? sender, KeyEventArgs e) {
        switch (e.Key) {
            case Key.Escape:
                CloseWith(SaveChangesChoice.Cancel);
                e.Handled = true;
                break;
            case Key.Enter:
                CloseWith(SaveChangesChoice.Save);
                e.Handled = true;
                break;
        }
    }

    private void CloseWith(SaveChangesChoice choice) {
        _choice = choice;
        Close();
    }
}
