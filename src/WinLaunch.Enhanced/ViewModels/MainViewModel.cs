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
    public Services.EnhancedPrefs Prefs { get; }

    public static string UseKey(ItemViewModel vm) => vm.Model.Application ?? vm.Name;
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
        Prefs = Services.EnhancedPrefs.Load(Store.Directory);
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
            foreach (var item in raw.OrderBy(i => i.Page).ThenBy(i => i.GridIndex))
            {
                var vm = new ItemViewModel(item, Store, IconCacheDir);
                vm.RefreshTarget();
                TopItems.Add(vm);
            }
            if (!ApplySort())
                Renumber();
            EnforceCapacity();
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

    // The original's sorting: with "Sort alphabetically" on, the board is laid
    // out by name (optionally folders first) and folder contents are sorted too.
    // Runs on load and before every save, so new and renamed tiles fall into place.
    public bool ApplySort()
    {
        var s = Settings;
        if (s == null || !s.SortItemsAlphabetically)
            return false;
        var byName = StringComparer.CurrentCultureIgnoreCase;
        if (!s.SortFolderContentsOnly)
        {
            var ordered = (s.SortFoldersFirst
                ? TopItems.OrderByDescending(i => i.IsFolder).ThenBy(i => i.Name, byName)
                : TopItems.OrderBy(i => i.Name, byName)).ToList();
            int cap = PageCapacity;
            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].Model.Page = i / cap;
                ordered[i].Model.GridIndex = i % cap;
            }
        }
        foreach (var folder in TopItems.Where(i => i.IsFolder))
        {
            var kids = folder.Children.OrderBy(c => c.Name, byName).ToList();
            for (int i = 0; i < kids.Count; i++)
            {
                int at = folder.Children.IndexOf(kids[i]);
                if (at != i)
                    folder.Children.Move(at, i);
                kids[i].Model.GridIndex = i;
            }
            folder.Model.Items = kids.Select(k => k.Model).ToList();
        }
        RecountPages();
        return true;
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
        var hits = new List<ItemViewModel>();
        foreach (var vm in TopItems)
        {
            if (Matches(vm, q))
                hits.Add(vm);
            if (vm.IsFolder)
                foreach (var child in vm.Children)
                    if (Matches(child, q))
                        hits.Add(child);
        }
        // Names that start with the query first, then word starts, then the
        // apps you open most (when ranking is on), then alphabetical.
        int Rank(ItemViewModel v) =>
            v.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 :
            v.Name.Contains(" " + q, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        foreach (var v in hits
                     .OrderBy(Rank)
                     .ThenByDescending(v => Prefs.RankByUse ? Prefs.UsesOf(UseKey(v)) : 0)
                     .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
            SearchResults.Add(v);
    }

    private static bool Matches(ItemViewModel vm, string q) =>
        vm.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        (vm.Model.Keywords ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);

    // A page holds Columns x Rows tiles. Adding to a full page lands on the next
    // page with room, as the original did. Stored indices of untouched items are
    // never rewritten.
    public int PageCapacity => Math.Max(1, Settings.Columns * Settings.Rows);

    private int CountOnPage(int page) => TopItems.Count(i => i.Model.Page == page);

    private int NextGridIndex(int page) =>
        TopItems.Where(i => i.Model.Page == page).Select(i => i.Model.GridIndex).DefaultIfEmpty(-1).Max() + 1;

    public ItemViewModel AddModel(ICItem model, int page)
    {
        int target = page;
        while (CountOnPage(target) >= PageCapacity)
            target++;
        model.Page = target;
        model.GridIndex = NextGridIndex(target);
        var vm = new ItemViewModel(model, Store, IconCacheDir);
        vm.RefreshTarget();
        vm.LoadIcon();
        TopItems.Add(vm);
        RecountPages();
        return vm;
    }

    // Moves tiles that no longer fit after the grid shrank onto the pages after them.
    public bool EnforceCapacity()
    {
        bool moved = false;
        int cap = PageCapacity;
        for (int page = 0; TopItems.Any() && page <= TopItems.Max(i => i.Model.Page); page++)
        {
            var onPage = TopItems.Where(i => i.Model.Page == page).OrderBy(i => i.Model.GridIndex).ToList();
            foreach (var extra in onPage.Skip(cap))
            {
                extra.Model.Page = page + 1;
                extra.Model.GridIndex = NextGridIndex(page + 1);
                moved = true;
            }
        }
        RecountPages();
        return moved;
    }

    // Removes every tile. SaveItems keeps the previous file in ICBackup first.
    public void ClearAll()
    {
        TopItems.Clear();
        _currentPage = 0;
        RecountPages();
        RefreshPage();
        RefreshSearch();
        OnPropertyChanged(nameof(IsEmpty));
        SaveNow();
    }

    public void RecountPages()
    {
        PageCount = Math.Max(1, TopItems.Any() ? TopItems.Max(i => i.Model.Page) + 1 : 1);
        if (_currentPage >= PageCount)
            _currentPage = PageCount - 1;
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
        ApplySort();
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
