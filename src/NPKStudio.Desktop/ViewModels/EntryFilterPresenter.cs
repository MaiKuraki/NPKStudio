using System.Collections.ObjectModel;

using Avalonia.Threading;

using NPKStudio.Desktop.Localization;

namespace NPKStudio.Desktop.ViewModels;

/// <summary>
///     左侧条目列表的搜索 / 过滤：关键字防抖、过滤视图重建、条目计数文案。
///     <para>
///         <b>为什么单独成类</b>：1108 个条目下"边打字边过滤"要处理防抖、集合实例替换、
///         以及"重建把 <c>SelectedItem</c> 冲成 null"的误触发；这些和窗口的其它状态无关，
///         留在 VM 里只会让它变成 GodClass。
///     </para>
///     <para>
///         <b>选中项不在这里</b>：<c>SelectedItem</c> 是控件双向绑定的本体，只能由 VM 写。
///         过滤完成后通过 <see cref="FilterSettled" /> 通知 VM，由 VM 决定"保留 / 清空"选中项。
///     </para>
/// </summary>
internal sealed class EntryFilterPresenter : ViewModelBase
{
    private static TranslationCatalog Loc => TranslationCatalog.Instance;

    /// <summary>搜索防抖窗口：连续输入只重建一次过滤视图。</summary>
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(150);

    private readonly ObservableCollection<EntryItem> _entries;
    private string _filter = string.Empty;
    private ObservableCollection<EntryItem> _filtered = new();
    private DispatcherTimer? _debounce;

    public EntryFilterPresenter(ObservableCollection<EntryItem> entries) {
        _entries = entries;
        _entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CountText));
    }

    /// <summary>过滤完成（防抖到期 / 关键字被清空）：VM 据此重新对齐选中项。</summary>
    public event EventHandler? FilterSettled;

    /// <summary>过滤视图；每次重建都换新实例，让 <c>ListBox</c> 只做一次视图刷新。</summary>
    public ObservableCollection<EntryItem> Filtered {
        get => _filtered;
        private set {
            _filtered = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CountText));
        }
    }

    /// <summary>
    ///     搜索关键字：按条目路径做不区分大小写的包含匹配；空串表示全量。
    ///     <para>清空要立刻生效（打开 / 重置 / 点清空按钮），逐字符输入才走 150ms 防抖。</para>
    /// </summary>
    public string Filter {
        get => _filter;
        set {
            var next = value ?? string.Empty;
            if (_filter == next) return;

            _filter = next;
            OnPropertyChanged();

            _debounce?.Stop();
            _debounce = null;

            if (next.Length == 0) {
                FilterSettled?.Invoke(this, EventArgs.Empty);
                return;
            }

            _debounce = new DispatcherTimer { Interval = DebounceInterval };
            _debounce.Tick += (_, _) => {
                _debounce?.Stop();
                _debounce = null;
                FilterSettled?.Invoke(this, EventArgs.Empty);
            };
            _debounce.Start();
        }
    }

    /// <summary>状态栏的条目计数文案；有筛选时同时给出总量与命中数。</summary>
    public string CountText => _filter.Trim().Length == 0
        ? Loc.F("status.imageCount", _entries.Count)
        : Loc.F("status.imageCountFiltered", _entries.Count, _filtered.Count);

    /// <summary>重建期间屏蔽"选中项被冲成 null"引发的重新解析。</summary>
    public bool SuppressSelectionReload { get; set; }

    /// <summary>停止防抖（关窗 / 重置时调用，避免定时器在退出后还触发）。</summary>
    public void StopDebounce() {
        _debounce?.Stop();
        _debounce = null;
    }

    /// <summary>
    ///     按当前关键字重建过滤视图。
    /// </summary>
    /// <param name="keep">原来选中的条目（可能为 <c>null</c>）。</param>
    /// <returns><c>true</c> 表示 <paramref name="keep" /> 仍在视图里；<c>false</c> 表示应清空选中。</returns>
    public bool Apply(EntryItem? keep) {
        var filter = _filter.Trim();

        List<EntryItem> items;
        if (filter.Length == 0) {
            items = [.. _entries];
        } else {
            items = new List<EntryItem>();
            foreach (var entry in _entries) {
                if (entry.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)) {
                    items.Add(entry);
                }
            }
        }

        SuppressSelectionReload = true;
        try {
            Filtered = new ObservableCollection<EntryItem>(items);
        } finally {
            SuppressSelectionReload = false;
        }

        return keep is not null && items.Contains(keep);
    }
}
