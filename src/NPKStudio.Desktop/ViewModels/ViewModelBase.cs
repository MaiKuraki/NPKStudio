using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>
///     最小可用的 MVVM 基类：属性变更通知 + 脏标记。刻意不引入 CommunityToolkit.Mvvm，
///     因为本项目只需要 <c>SetProperty</c> 与命令包装（约 60 行），引入源生成器会增加构建不确定性。
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
