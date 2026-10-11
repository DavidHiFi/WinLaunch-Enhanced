using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WinLaunch.Enhanced.Services;
using WinLaunch.Enhanced.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using MessageBox = System.Windows.MessageBox;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDataFormats = System.Windows.DataFormats;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfHAlign = System.Windows.HorizontalAlignment;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Key = System.Windows.Input.Key;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;

namespace WinLaunch.Enhanced.Views;

public partial class SpringboardView : UserControl
{
    public MainViewModel VM { get; private set; }
    public ItemViewModel ActiveFolder { get; private set; }

    public event Action OverlayShouldHide;
    public event Action OpenSettings;

    private System.Windows.Point _dragStart;
    private ItemViewModel _dragItem;

    public SpringboardView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplySettings();
        PreviewKeyDown += OnKey;
    }

    public void Bind(MainViewModel vm)
    {
        VM = vm;
        DataContext = vm;
        ApplySettings();
        Refresh();
    }

    public void ApplySettings()
    {
        if (VM == null)
            return;
        var pageGrid = FindPanel<UniformGrid>(TileGrid);
        if (pageGrid != null)
            pageGrid.Columns = Math.Max(1, VM.Settings.Columns);
        var folderGrid = FindPanel<UniformGrid>(FolderGrid);
        if (folderGrid != null)
            folderGrid.Columns = Math.Max(1, VM.Settings.FolderColumns);
        SearchBox.Visibility = VM.Settings.SearchBarVisible ? Visibility.Visible : Visibility.Collapsed;
        RefreshDots();
    }

    public void FocusForOpen()
    {
        SearchBox.Text = "";
        CloseFolder();
        Refresh();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (SearchBox.Visibility == Visibility.Visible)
                SearchBox.Focus();
            else
                MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    public void RefreshPagePublic()
    {
        VM.RefreshPage();
        Refresh();
    }

    private void Refresh()
    {
        if (VM == null)
            return;
        bool searching = SearchBox.Text.Trim().Length > 0;
        TileGrid.ItemsSource = searching ? VM.SearchResults : VM.CurrentPageItems;
        LoadingState.Visibility = VM.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        ErrorState.Visibility = VM.LoadError != null ? Visibility.Visible : Visibility.Collapsed;
        ErrorDetail.Text = VM.LoadError ?? "";
        bool empty = !VM.IsLoading && VM.LoadError == null &&
            (searching ? VM.SearchResults.Count == 0 : VM.TopItems.Count == 0);
        EmptyState.Visibility = empty && !searching ? Visibility.Visible : Visibility.Collapsed;
        TileGrid.Visibility = empty || VM.IsLoading || VM.LoadError != null
            ? Visibility.Collapsed : Visibility.Visible;
        RefreshDots();
    }

    private void RefreshDots()
    {
        if (VM == null)
            return;
        PageDots.Children.Clear();
        var accent = (WpfBrush)FindResource("AccentBrush");
        var muted = (WpfBrush)FindResource("MutedBrush");
        for (int i = 0; i < VM.PageCount; i++)
        {
            int page = i;
            var dot = new Button
            {
                Content = i == VM.CurrentPage ? "●" : "○",
                Foreground = i == VM.CurrentPage ? accent : muted,
                Background = WpfBrushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 14,
                Padding = new Thickness(4, 0, 4, 0),
                Tag = page,
            };
            dot.Click += (_, _) => { VM.CurrentPage = page; Refresh(); };
            if (VM.Settings.PageIndicatorsVisible || VM.PageCount > 1)
                PageDots.Children.Add(dot);
        }
        EditButton.Content = VM.EditMode ? "Done" : "Edit";
    }

    // ---- launching ----

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not ItemViewModel item)
            return;
        if (item.IsFolder)
        {
            OpenFolder(item);
            return;
        }
        try
        {
            LauncherService.Launch(item.Model, VM.Store);
            OverlayShouldHide?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{ex.Message}", "Could not launch",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenFolder(ItemViewModel folder)
    {
        ActiveFolder = folder;
        FolderTitle.Text = folder.Name;
        FolderGrid.ItemsSource = folder.Children;
        FolderSheet.Visibility = Visibility.Visible;
    }

    private void FolderClose_Click(object sender, RoutedEventArgs e) => CloseFolder();
    private void CloseFolder()
    {
        ActiveFolder = null;
        FolderSheet.Visibility = Visibility.Collapsed;
    }

    // ---- search ----

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        VM.SearchText = SearchBox.Text;
        VM.RefreshSearch();
        Refresh();
    }

    // ---- keyboard (R-32: full keyboard operation) ----

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (FolderSheet.Visibility == Visibility.Visible) { CloseFolder(); e.Handled = true; }
            else if (SearchBox.Text.Length > 0) { SearchBox.Text = ""; e.Handled = true; }
            return;
        }
        if (e.Key == Key.Enter && SearchBox.IsKeyboardFocused && VM.SearchResults.Count > 0)
        {
            var top = VM.SearchResults[0];
            if (!top.IsFolder)
            {
                try { LauncherService.Launch(top.Model, VM.Store); OverlayShouldHide?.Invoke(); }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Could not launch",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else OpenFolder(top);
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E)
        {
            VM.EditMode = !VM.EditMode; RefreshDots(); e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            NewFolder_Click(sender, null); e.Handled = true;
        }
        else if (e.Key == Key.F2 && FocusManager.GetFocusedElement(this) is Button b && b.Tag is ItemViewModel item)
        {
            RenameItem(item); e.Handled = true;
        }
        else if (e.Key == Key.Delete && FocusManager.GetFocusedElement(this) is Button b2 && b2.Tag is ItemViewModel item2)
        {
            DeleteItem(item2); e.Handled = true;
        }
    }

    // ---- edit operations ----

    private void EditToggle_Click(object sender, RoutedEventArgs e)
    {
        VM.EditMode = !VM.EditMode;
        RefreshDots();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new WpfOpenFileDialog
        {
            Multiselect = true,
            Filter = "Apps and shortcuts (*.lnk;*.exe)|*.lnk;*.exe|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true)
            return;
        foreach (var path in dlg.FileNames)
        {
            try
            {
                var model = LinkImporter.Import(path, VM.Store);
                VM.AddModel(model, VM.CurrentPage);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not add {path}: {ex.Message}", "Add failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        VM.RequestSave();
        VM.RefreshPage();
        Refresh();
    }

    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptText("New folder", "Folder name:");
        if (name == null)
            return;
        var model = new WinLaunch.Legacy.ICItem
        {
            IsFolder = true,
            Name = name,
        };
        VM.AddModel(model, VM.CurrentPage);
        VM.RequestSave();
        VM.RefreshPage();
        Refresh();
    }

    private void Menu_Open(object sender, RoutedEventArgs e)
    {
        if (MenuItemOf(sender) is ItemViewModel item)
            Tile_Click(new Button { Tag = item }, null);
    }

    private void Menu_Rename(object sender, RoutedEventArgs e)
    {
        if (MenuItemOf(sender) is ItemViewModel item)
            RenameItem(item);
    }

    private void Menu_Delete(object sender, RoutedEventArgs e)
    {
        if (MenuItemOf(sender) is ItemViewModel item)
            DeleteItem(item);
    }

    private static ItemViewModel MenuItemOf(object sender)
    {
        // Context menus live outside the visual tree, so walk up to the menu
        // and resolve the tile via its placement target.
        if (sender is not MenuItem menuItem)
            return null;
        DependencyObject current = menuItem;
        while (current != null)
        {
            if (current is ContextMenu ctx)
                return (ctx.PlacementTarget as Button)?.Tag as ItemViewModel;
            current = (current as MenuItem)?.Parent as DependencyObject;
        }
        return null;
    }

    private void TileMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;
        if (menu.PlacementTarget is not Button targetBtn || targetBtn.Tag is not ItemViewModel item)
            return;
        // Rebuild the "Move to page" submenu each time so it reflects reality.
        for (int i = menu.Items.Count - 1; i >= 0; i--)
            if (menu.Items[i] is MenuItem mi && mi.Header is string h && h.StartsWith("Move to page"))
                menu.Items.RemoveAt(i);
        if (VM.PageCount > 1 || true)
        {
            var move = new MenuItem { Header = "Move to page" };
            for (int p = 0; p < Math.Max(VM.PageCount, item.Model.Page + 2); p++)
            {
                int page = p;
                var mi = new MenuItem { Header = $"Page {p + 1}", IsChecked = item.Model.Page == p };
                mi.Click += (_, _) =>
                {
                    VM.TopItems.Remove(item);
                    VM.AddModel(item.Model, page);
                    VM.RequestSave();
                    VM.RefreshPage();
                    Refresh();
                };
                move.Items.Add(mi);
            }
            menu.Items.Add(move);
        }
    }

    private void RenameItem(ItemViewModel item)
    {
        var name = PromptText("Rename", $"Name for \"{item.Name}\":", item.Name);
        if (name == null)
            return;
        item.Model.Name = name;
        item.NotifyRenamed();
        item.LoadIcon();
        VM.RequestSave();
    }

    private void DeleteItem(ItemViewModel item)
    {
        var where = ActiveFolder != null && ActiveFolder.Children.Contains(item)
            ? $" from \"{ActiveFolder.Name}\""
            : "";
        if (MessageBox.Show($"Remove \"{item.Name}\"{where}? The app itself is not uninstalled.",
                "Remove", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        if (ActiveFolder != null && ActiveFolder.Children.Contains(item))
        {
            ActiveFolder.Children.Remove(item);
            ActiveFolder.Model.Items.Remove(item.Model);
            ActiveFolder.LoadIcon();
            if (ActiveFolder.Children.Count == 0)
                CloseFolder();
        }
        else
        {
            VM.TopItems.Remove(item);
        }
        VM.RequestSave();
        VM.RefreshPage();
        Refresh();
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        VM.Reload();
        ApplySettings();
        Refresh();
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(VM.Store.Directory) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not open folder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings?.Invoke();

    // ---- drag and drop ----

    private void Tile_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button b && b.Tag is ItemViewModel item)
        {
            _dragStart = e.GetPosition(this);
            _dragItem = item;
        }
    }

    private void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem == null || e.LeftButton != MouseButtonState.Pressed || !VM.EditMode)
            return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        DragDrop.DoDragDrop((DependencyObject)sender, _dragItem, WpfDragDropEffects.Move);
        _dragItem = null;
    }

    private void Tile_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ItemViewModel)) ? WpfDragDropEffects.Move : WpfDragDropEffects.None;
        e.Handled = true;
    }

    private void Tile_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(ItemViewModel)))
            return;
        var source = (ItemViewModel)e.Data.GetData(typeof(ItemViewModel));
        if (sender is not Button b || b.Tag is not ItemViewModel target || source == target)
            return;
        if (target.IsFolder && source.Model.Page == target.Model.Page)
        {
            // Drop into folder.
            target.Model.Items.Add(source.Model);
            target.Children.Add(source);
            VM.TopItems.Remove(source);
            target.LoadIcon();
        }
        else
        {
            // Swap grid positions.
            (target.Model.GridIndex, source.Model.GridIndex) =
                (source.Model.GridIndex, target.Model.GridIndex);
            (target.Model.Page, source.Model.Page) =
                (source.Model.Page, target.Model.Page);
        }
        VM.RequestSave();
        VM.RefreshPage();
        Refresh();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
        {
            foreach (var path in (string[])e.Data.GetData(WpfDataFormats.FileDrop))
            {
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var model = LinkImporter.Import(path, VM.Store);
                        VM.AddModel(model, VM.CurrentPage);
                    }
                    catch { }
                }
            }
            VM.RequestSave();
            VM.RefreshPage();
            Refresh();
        }
    }

    private static T FindPanel<T>(ItemsControl control) where T : DependencyObject
    {
        if (control == null)
            return null;
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(control);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is T match)
                return match;
            int count = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
        }
        return null;
    }

    private static string PromptText(string title, string label, string initial = "")
    {
        var win = new Window
        {
            Title = title,
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) });
        var box = new TextBox { Text = initial };
        box.SelectAll();
        panel.Children.Add(box);
        var row = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHAlign.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        string result = null;
        ok.Click += (_, _) => { result = box.Text.Trim(); win.Close(); };
        row.Children.Add(ok);
        row.Children.Add(cancel);
        panel.Children.Add(row);
        win.Content = panel;
        win.ShowDialog();
        return string.IsNullOrEmpty(result) ? null : result;
    }
}
