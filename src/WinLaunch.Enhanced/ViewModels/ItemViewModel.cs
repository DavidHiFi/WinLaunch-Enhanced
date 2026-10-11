using System.Collections.ObjectModel;
using System.ComponentModel;
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
    }

    public void RefreshTarget()
    {
        ResolvedTarget = IsFolder ? null : _store.ResolveLinkTarget(Model.Application);
        Broken = !IsFolder && ResolvedTarget == null;
    }

    public void LoadIcon()
    {
        _iconCts.Cancel();
        _iconCts = new CancellationTokenSource();
        var ct = _iconCts.Token;
        Task.Run(async () =>
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
    }

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string p = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
