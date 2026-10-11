using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using WinLaunch.Legacy;

namespace WinLaunch.Enhanced.ViewModels;

// Display order is the stored order (Page, then GridIndex). Sorting from settings
// applies to the view; saves write the displayed order back, so both apps agree.
public sealed class MainViewModel : INotifyPropertyChanged
{
    public LegacyStore Store { get; }
    public LegacySettings Settings { get; private set; }
    public string IconCacheDir => Path.Combine(Store.Directory, "IconCache");

    public ObservableCollection<ItemViewModel> TopItems { get; } = new();
    public ObservableCollection<ItemViewModel> CurrentPageItems { get; } = new();
    public ObservableCollection<ItemViewModel> SearchResults { get; } = new();

    private int _currentPage;
    public int CurrentPage
    {
        get => _currentPage;
        set { _currentPage = value; OnPropertyChanged(); RefreshPage(); }
    }

    private int _pageCount = 1;
    public int PageCount
    {
        get => _pageCount;
        private set { _pageCount = value; OnPropertyChanged(); }
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); RefreshSearch(); }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    private bool _editMode;
    public bool EditMode
    {
        get => _editMode;
        set { _editMode = value; OnPropertyChanged(); }
    }

    private string _loadError;
    public string LoadError
    {
        get => _loadError;
        private set { _loadError = value; OnPropertyChanged(); }
    }

    public bool IsEmpty => !IsLoading && LoadError == null && TopItems.Count == 0;

    private CancellationTokenSource _saveCts = new();

    public MainViewModel(string dataDir = null)
    {
        Store = new LegacyStore(dataDir);
        Reload();
    }

    public void Reload()
    {
        IsLoading = true;
        LoadError = null;
        try
        {
            Settings = Store.LoadSettings();
            var raw = Store.LoadItems();
            TopItems.Clear();
            foreach (var item in ApplyTopSort(raw).OrderBy(i => i.Page).ThenBy(i => i.GridIndex))
            {
                var vm = new ItemViewModel(item, Store, IconCacheDir);
                vm.RefreshTarget();
                TopItems.Add(vm);
            }
            PageCount = Math.Max(1, TopItems.Any() ? TopItems.Max(i => i.Model.Page) + 1 : 1);
            Renumber();
            CurrentPage = 0;
            foreach (var vm in TopItems)
                vm.LoadIcon();
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex)
        {
            LoadError = $"Could not load {Store.ItemsPath}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private IEnumerable<ICItem> ApplyTopSort(List<ICItem> raw)
    {
        var s = Settings;
        var list = raw.ToList();
        if (s.SortItemsAlphabetically && !s.SortFolderContentsOnly)
        {
            list = s.SortFoldersFirst
                ? list.OrderByDescending(i => i.IsFolder).ThenBy(i => i.Name).ToList()
                : list.OrderBy(i => i.Name).ToList();
        }
        if (s.SortItemsAlphabetically)
        {
            foreach (var f in list.Where(i => i.IsFolder && i.Items != null))
                f.Items = f.Items.OrderBy(i => i.Name).ToList();
        }
        return list;
    }

    public void RefreshPage()
    {
        CurrentPageItems.Clear();
        foreach (var vm in TopItems.Where(i => i.Model.Page == _currentPage).OrderBy(i => i.Model.GridIndex))
            CurrentPageItems.Add(vm);
    }

    public void RefreshSearch()
    {
        SearchResults.Clear();
        var q = (_searchText ?? "").Trim();
        if (q.Length == 0)
            return;
        foreach (var vm in TopItems)
        {
            if (Matches(vm, q))
                SearchResults.Add(vm);
            if (vm.IsFolder)
                foreach (var child in vm.Children)
                    if (Matches(child, q))
                        SearchResults.Add(child);
        }
    }

    private static bool Matches(ItemViewModel vm, string q) =>
        vm.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        (vm.Model.Keywords ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);

    // Adds a new model on a page, appending it after existing tiles.
    // Stored indices of untouched items are never rewritten.
    public ItemViewModel AddModel(ICItem model, int page)
    {
        model.Page = page;
        model.GridIndex = TopItems.Where(i => i.Model.Page == page)
            .Select(i => i.Model.GridIndex).DefaultIfEmpty(-1).Max() + 1;
        var vm = new ItemViewModel(model, Store, IconCacheDir);
        vm.RefreshTarget();
        vm.LoadIcon();
        TopItems.Add(vm);
        return vm;
    }

    private void Renumber()
    {
        foreach (var group in TopItems.GroupBy(i => i.Model.Page))
        {
            int idx = 0;
            foreach (var vm in group.OrderBy(i => i.Model.GridIndex))
                vm.Model.GridIndex = idx++;
        }
    }

    // Rebuilds storage order from the view, then saves (debounced 1 s).
    public void RequestSave()
    {
        _saveCts.Cancel();
        _saveCts = new CancellationTokenSource();
        var ct = _saveCts.Token;
        Task.Delay(1000, ct).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            try { SaveNow(); }
            catch { }
        }, ct);
    }

    public void SaveNow()
    {
        // Writes models with the indices they carry. Untouched items keep the
        // exact values the original app wrote, so merely opening Enhanced
        // never reorders anyone's layout.
        Store.SaveItems(TopItems.OrderBy(i => i.Model.Page).ThenBy(i => i.Model.GridIndex)
            .Select(v => v.Model).ToList());
    }

    public void SaveSettings() => Store.SaveSettings(Settings);

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string p = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        if (p == nameof(CurrentPage))
            return;
    }
}
