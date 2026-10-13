using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinLaunch.Enhanced.Services;
using WinLaunch.Legacy;

namespace WinLaunch.Enhanced.ViewModels;

public sealed class ItemViewModel : INotifyPropertyChanged
{
    public ICItem Model { get; }
    private readonly LegacyStore _store;
    private readonly string _cacheDir;
    private CancellationTokenSource _iconCts = new();

    public bool IsFolder => Model.IsFolder;
    public string Name => Model.Name;
    public string Subtitle { get; }
    public ObservableCollection<ItemViewModel> Children { get; } = new();

    private BitmapSource _icon;
    public BitmapSource Icon
    {
        get => _icon;
        private set { _icon = value; OnPropertyChanged(); }
    }

    private bool _broken;
    public bool Broken
    {
        get => _broken;
        private set { _broken = value; OnPropertyChanged(); }
    }

    public string ResolvedTarget { get; private set; }

    public ItemViewModel(ICItem model, LegacyStore store, string cacheDir, string subtitle = null)
    {
        Model = model;
        _store = store;
        _cacheDir = cacheDir;
        Subtitle = subtitle;
        Icon = IconService.GetInitialTile(model.Name);
        foreach (var child in model.Items ?? new List<ICItem>())
            Children.Add(new ItemViewModel(child, store, cacheDir, $"in {model.Name}"));
        Children.CollectionChanged += (_, _) => OnPropertyChanged(nameof(PreviewChildren));
    }

    // First nine children, drawn as the 3x3 miniature grid on a folder tile.
    public IReadOnlyList<ItemViewModel> PreviewChildren =>
        Model.ShowMiniatures ? Children.Take(9).ToList() : new List<ItemViewModel>();

    // Folders with a custom icon show it instead of the miniature grid.
    public bool HasCustomIcon => IsFolder && !string.IsNullOrEmpty(Model.IconPath);

    public string Tooltip => string.IsNullOrWhiteSpace(Model.Notes) ? Model.Name : Model.Name + "\n" + Model.Notes;

    public void RefreshTarget()
    {
        ResolvedTarget = IsFolder ? null : _store.ResolveLinkTarget(Model.Application);
        if (!IsFolder && LegacyStore.IsUrl(ResolvedTarget))
        {
            Broken = false;
            return;
        }
        Broken = !IsFolder && (ResolvedTarget == null ||
            (!File.Exists(ResolvedTarget) && !System.IO.Directory.Exists(ResolvedTarget)));
    }

    public void LoadIcon()
    {
        if (IsFolder)
            foreach (var child in Children)
                if (!child._iconRequested)
                {
                    child._iconRequested = true;
                    child.RefreshTarget();
                    child.LoadIcon();
                }
        _iconRequested = true;
        _iconCts.Cancel();
        _iconCts = new CancellationTokenSource();
        var ct = _iconCts.Token;
        IconLoadTask = Task.Run(async () =>
        {
            var bmp = await IconService.GetIconAsync(Model, _store, _cacheDir, ct);
            if (ct.IsCancellationRequested)
                return;
            App.Current?.Dispatcher.Invoke(() =>
            {
                if (!ct.IsCancellationRequested)
                    Icon = bmp;
            });
        }, ct);
    }

    public void NotifyRenamed()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Tooltip));
    }

    // Raised after an edit applies so the tile text, tooltip, custom-icon state
    // and preview grid all refresh together.
    public void NotifyEdited()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Tooltip));
        OnPropertyChanged(nameof(HasCustomIcon));
        OnPropertyChanged(nameof(PreviewChildren));
        RefreshTarget();
        LoadIcon();
    }

    private bool _iconRequested;

    public Task IconLoadTask { get; private set; } = Task.CompletedTask;

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string p = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
