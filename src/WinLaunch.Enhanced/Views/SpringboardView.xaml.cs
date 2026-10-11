using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinLaunch.Enhanced.Services;
using WinLaunch.Enhanced.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDataFormats = System.Windows.DataFormats;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Key = System.Windows.Input.Key;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Point = System.Windows.Point;

namespace WinLaunch.Enhanced.Views;

public sealed class TileTemplateSelector : DataTemplateSelector
{
    public DataTemplate AppTemplate { get; set; }
    public DataTemplate FolderTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        if (item is ItemViewModel vm && vm.IsFolder)
            return FolderTemplate;
        return AppTemplate;
    }
}

// The Launchpad board: one page of tiles at a time, folders open as a band
// over a dimmed board, edit mode wiggles tiles and shows remove badges.
public partial class SpringboardView : UserControl
{
    public MainViewModel VM { get; private set; }
    public ItemViewModel ActiveFolder { get; private set; }

    public event Action OverlayShouldHide;
    public event Action OpenSettings;

    // Counts dialogs this view opened, so the window does not auto-hide when a
    // file picker or message box takes focus.
    public static int ModalDepth { get; set; }

    // Offscreen preview renders have no composition clock, so animations
    // would freeze at their first frame there.
    public static bool AnimationsOff { get; set; }

    private static bool Animate => !AnimationsOff && SystemParameters.ClientAreaAnimation;

    private readonly List<FrameworkElement> _wiggleHosts = new();
    private readonly Random _rng = new();
    private readonly DispatcherTimer _holdTimer;
    private readonly DispatcherTimer _undoTimer;

    private Point _dragStart;
    private ItemViewModel _dragItem;
    private bool _suppressClick;
    private Point _swipeStart;
    private bool _swiping;
    private DateTime _lastWheel = DateTime.MinValue;
    private Action<string> _promptAccept;
    private (ItemViewModel item, ItemViewModel folder, int index, int page, int grid)? _lastRemoved;

    public SpringboardView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplySettings();
        PreviewKeyDown += OnKey;
        PreviewTextInput += OnTextInput;
        _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer.Stop();
            if (VM != null && !VM.EditMode && _dragItem != null)
            {
                _suppressClick = true;
                SetEditMode(true);
            }
        };
        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _undoTimer.Tick += (_, _) => HideUndo();
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
        var s = VM.Settings;
        var pageGrid = FindPanel<UniformGrid>(TileGrid);
        if (pageGrid != null)
        {
            pageGrid.Columns = Math.Max(1, s.Columns);
            pageGrid.Rows = Math.Max(1, s.Rows);
        }
        SearchPill.Visibility = s.SearchBarVisible ? Visibility.Visible : Visibility.Hidden;
        Resources["ItemLabelVisibility"] = s.ShowItemIconText ? Visibility.Visible : Visibility.Collapsed;
        Resources["FolderLabelVisibility"] = s.ShowFolderIconText ? Visibility.Visible : Visibility.Collapsed;
        UpdateTileMetrics();
        RefreshDots();
    }

    // Cell size comes from the page area and the user's grid; the icon follows
    // the original formula (70 px x IconSize) but never outgrows its cell.
    private void UpdateTileMetrics()
    {
        if (VM == null || PageHost.ActualWidth <= 0 || PageHost.ActualHeight <= 0)
            return;
        var s = VM.Settings;
        int cols = Math.Max(1, s.Columns), rows = Math.Max(1, s.Rows);
        double cellW = Math.Floor(PageHost.ActualWidth / cols);
        double cellH = Math.Floor(PageHost.ActualHeight / rows);
        double wanted = Math.Round(70 * (s.IconSize <= 0 ? 1.4 : s.IconSize));
        double icon = Math.Floor(Math.Min(wanted, Math.Min(cellW * 0.74, cellH * 0.64)));
        icon = Math.Max(40, icon);
        Resources["TileCellWidth"] = cellW;
        Resources["TileCellHeight"] = cellH;
        Resources["TileIconSize"] = icon;
        Resources["TileLabelWidth"] = Math.Max(40, cellW - 14);
        Resources["TileFontSize"] = Math.Round(Math.Clamp(11.5 + icon / 40, 12, 15) * ThemeManager.TextScale, 1);
    }

    private void PageHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTileMetrics();

    public void FocusForOpen()
    {
        SearchBox.Text = "";
        CloseFolder(animate: false);
        ClosePrompt();
        SetEditMode(false);
        Refresh();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (SearchPill.Visibility == Visibility.Visible)
                SearchBox.Focus();
            else
                Focus();
        }), DispatcherPriority.Background);
    }

    public void RefreshPagePublic()
    {
        VM.RefreshPage();
        Refresh();
    }

    private bool Searching => SearchBox.Text.Trim().Length > 0;

    private void Refresh()
    {
        if (VM == null)
            return;
        bool searching = Searching;
        if (searching)
        {
            int cap = Math.Max(1, VM.Settings.Columns * VM.Settings.Rows);
            TileGrid.ItemsSource = VM.SearchResults.Take(cap).ToList();
        }
        else
        {
            TileGrid.ItemsSource = VM.CurrentPageItems;
        }
        LoadingState.Visibility = VM.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        ErrorState.Visibility = VM.LoadError != null ? Visibility.Visible : Visibility.Collapsed;
        ErrorDetail.Text = VM.LoadError ?? "";
        bool ok = !VM.IsLoading && VM.LoadError == null;
        EmptyState.Visibility = ok && !searching && VM.TopItems.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        NoResults.Visibility = ok && searching && VM.SearchResults.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        TileGrid.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        RefreshDots();
    }

    private void RefreshDots()
    {
        if (VM == null)
            return;
        PageDots.Children.Clear();
        bool show = VM.Settings.PageIndicatorsVisible && !Searching && VM.PageCount > 1;
        if (show)
        {
            for (int i = 0; i < VM.PageCount; i++)
            {
                int page = i;
                var dot = new Button { Tag = i == VM.CurrentPage ? "True" : "False", ToolTip = $"Page {i + 1}" };
                dot.SetResourceReference(StyleProperty, "PageDot");
                dot.Click += (_, _) => GoToPage(page);
                PageDots.Children.Add(dot);
            }
        }
        DoneButton.Visibility = VM.EditMode ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- paging ----

    private void GoToPage(int page)
    {
        if (VM == null || Searching)
            return;
        page = Math.Clamp(page, 0, Math.Max(0, VM.PageCount - 1));
        if (page == VM.CurrentPage)
            return;
        int dir = page > VM.CurrentPage ? 1 : -1;
        VM.CurrentPage = page;
        Refresh();
        if (!Animate)
            return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageShift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(dir * 90, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        TileGrid.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0.15, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
    }

    private void Root_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FolderLayer.Visibility == Visibility.Visible || PromptLayer.Visibility == Visibility.Visible)
            return;
        if ((DateTime.UtcNow - _lastWheel).TotalMilliseconds < 280)
            return;
        _lastWheel = DateTime.UtcNow;
        GoToPage(VM.CurrentPage + (e.Delta < 0 ? 1 : -1));
        e.Handled = true;
    }

    private void Root_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _swipeStart = e.GetPosition(this);
        _swiping = false;
    }

    private void Root_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (VM == null || VM.EditMode || FolderLayer.Visibility == Visibility.Visible)
            return;
        double dx = e.GetPosition(this).X - _swipeStart.X;
        if (Math.Abs(dx) > 120)
        {
            _swiping = true;
            _suppressClick = true;
            GoToPage(VM.CurrentPage + (dx < 0 ? 1 : -1));
        }
    }

    // A plain click on empty space closes the overlay, as in the original;
    // in edit mode it ends editing instead.
    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_swiping || !ReferenceEquals(e.OriginalSource, Root))
        {
            _swiping = false;
            return;
        }
        if (VM.EditMode)
            SetEditMode(false);
        else if (FolderLayer.Visibility != Visibility.Visible && VM.Prefs.HideOnEmptyClick)
            OverlayShouldHide?.Invoke();
    }

    // ---- launching ----

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressClick)
        {
            _suppressClick = false;
            return;
        }
        if (sender is not Button b || b.Tag is not ItemViewModel item)
            return;
        if (item.IsFolder)
        {
            OpenFolder(item);
            return;
        }
        if (VM.EditMode)
            return;
        Launch(item, false);
    }

    private void Launch(ItemViewModel item, bool asAdmin)
    {
        try
        {
            LauncherService.Launch(item.Model, VM.Store, asAdmin);
            VM.Prefs.CountLaunch(MainViewModel.UseKey(item));
            if (VM.Prefs.CloseOnLaunch)
                OverlayShouldHide?.Invoke();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // UAC prompt cancelled: nothing to report.
        }
        catch (Exception ex)
        {
            ShowModal(() => MessageBox.Show(Window.GetWindow(this), ex.Message, "Could not open",
                MessageBoxButton.OK, MessageBoxImage.Warning));
        }
    }

    // ---- folders ----

    public void OpenFolder(ItemViewModel folder)
    {
        ActiveFolder = folder;
        folder.LoadIcon();
        FolderTitle.Text = folder.Name;
        var s = VM.Settings;
        FolderGrid.ItemsSource = folder.Children;
        FolderLayer.Visibility = Visibility.Visible;
        FolderLayer.UpdateLayout();
        var grid = FindPanel<UniformGrid>(FolderGrid);
        int cols = Math.Clamp(folder.Children.Count, 1, Math.Max(1, Math.Min(s.FolderColumns, s.Columns)));
        if (grid != null)
            grid.Columns = cols;
        double cellH = (double)Resources["TileCellHeight"];
        FolderScroll.MaxHeight = Math.Max(1, Math.Min(s.FolderRows, 4)) * cellH;
        FolderScroll.ScrollToTop();
        // Launchpad pushes the board out of focus behind an open folder.
        PageHost.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 16 };
        if (Animate)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(200);
            FolderScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, dur) { EasingFunction = ease });
            FolderScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, dur) { EasingFunction = ease });
            FolderLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }
        Dispatcher.BeginInvoke(new Action(() =>
            FolderGrid.MoveFocus(new TraversalRequest(FocusNavigationDirection.First))),
            DispatcherPriority.Input);
    }

    private void FolderDim_MouseUp(object sender, MouseButtonEventArgs e)
    {
        CloseFolder();
        e.Handled = true;
    }

    private void CloseFolder(bool animate = true)
    {
        if (FolderLayer.Visibility != Visibility.Visible)
        {
            ActiveFolder = null;
            return;
        }
        CommitFolderTitle();
        ActiveFolder = null;
        PageHost.Effect = null;
        if (!animate || !Animate)
        {
            FolderLayer.BeginAnimation(OpacityProperty, null);
            FolderLayer.Visibility = Visibility.Collapsed;
            return;
        }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(130));
        fade.Completed += (_, _) =>
        {
            if (ActiveFolder == null)
                FolderLayer.Visibility = Visibility.Collapsed;
        };
        FolderScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.94, TimeSpan.FromMilliseconds(130)));
        FolderScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.94, TimeSpan.FromMilliseconds(130)));
        FolderLayer.BeginAnimation(OpacityProperty, fade);
    }

    private void FolderTitle_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitFolderTitle();
            FolderGrid.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            e.Handled = true;
        }
    }

    private void FolderTitle_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitFolderTitle();

    private void CommitFolderTitle()
    {
        var folder = ActiveFolder;
        var name = FolderTitle.Text.Trim();
        if (folder == null || name.Length == 0 || name == folder.Name)
            return;
        folder.Model.Name = name;
        folder.NotifyRenamed();
        VM.RequestSave();
    }

    // ---- search ----

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchChrome();
        VM.SearchText = SearchBox.Text;
        VM.RefreshSearch();
        var calc = VM.Prefs.Calculator ? Calculator.TryEvaluate(SearchBox.Text) : null;
        CalcText.Text = calc ?? "";
        CalcChip.Visibility = calc != null ? Visibility.Visible : Visibility.Collapsed;
        WebSearchText.Text = $"Search the web for \u201C{SearchBox.Text.Trim()}\u201D";
        WebSearchButton.Visibility = VM.Prefs.WebSearch ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    private void CalcChip_Click(object sender, MouseButtonEventArgs e) => CopyCalc();

    private void CopyCalc()
    {
        try { System.Windows.Clipboard.SetText(CalcText.Text); } catch { }
        if (VM.Prefs.CloseOnLaunch)
            OverlayShouldHide?.Invoke();
    }

    private void WebSearch_Click(object sender, RoutedEventArgs e) => WebSearch();

    private void WebSearch()
    {
        var q = SearchBox.Text.Trim();
        if (q.Length == 0)
            return;
        try
        {
            var url = string.Format(VM.Prefs.WebSearchUrl, Uri.EscapeDataString(q));
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            OverlayShouldHide?.Invoke();
        }
        catch { }
    }

    private void SearchBox_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) => UpdateSearchChrome();

    private void UpdateSearchChrome()
    {
        bool empty = SearchBox.Text.Length == 0;
        SearchHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        SearchGlyph.Visibility = SearchHint.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    // Typing anywhere on the board starts a search.
    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (SearchBox.IsKeyboardFocused || FolderTitle.IsKeyboardFocused || PromptBox.IsKeyboardFocused)
            return;
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]) || SearchPill.Visibility != Visibility.Visible)
            return;
        if (FolderLayer.Visibility == Visibility.Visible)
            CloseFolder(animate: false);
        SearchBox.Focus();
        SearchBox.Text += e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }

    // ---- keyboard ----

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (PromptLayer.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { ClosePrompt(); e.Handled = true; }
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (FolderLayer.Visibility == Visibility.Visible) { CloseFolder(); e.Handled = true; }
            else if (VM.EditMode) { SetEditMode(false); e.Handled = true; }
            else if (SearchBox.Text.Length > 0) { SearchBox.Text = ""; e.Handled = true; }
            return;
        }
        if (FolderTitle.IsKeyboardFocused)
            return;
        bool inSearch = SearchBox.IsKeyboardFocused;
        if (e.Key == Key.Enter && inSearch && CalcChip.Visibility == Visibility.Visible)
        {
            CopyCalc();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && inSearch && SearchBox.Text.Trim().Length > 0 &&
            VM.SearchResults.Count == 0 && VM.Prefs.WebSearch)
        {
            WebSearch();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && inSearch && VM.SearchResults.Count > 0)
        {
            var top = VM.SearchResults[0];
            if (top.IsFolder) OpenFolder(top);
            else Launch(top, Keyboard.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Shift));
            e.Handled = true;
            return;
        }
        if (inSearch && e.Key == Key.Down)
        {
            TileGrid.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            e.Handled = true;
            return;
        }
        bool emptySearch = SearchBox.Text.Length == 0;
        if (FolderLayer.Visibility != Visibility.Visible && emptySearch)
        {
            if (e.Key == Key.PageDown || (inSearch && e.Key == Key.Right) ||
                (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Right))
            { GoToPage(VM.CurrentPage + 1); e.Handled = true; return; }
            if (e.Key == Key.PageUp || (inSearch && e.Key == Key.Left) ||
                (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Left))
            { GoToPage(VM.CurrentPage - 1); e.Handled = true; return; }
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E)
        {
            SetEditMode(!VM.EditMode); e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            NewFolder(); e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z && _lastRemoved != null)
        {
            Undo_Click(null, null); e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma)
        {
            OpenSettings?.Invoke(); e.Handled = true;
        }
        else if (e.Key == Key.F2 && FocusedItem() is ItemViewModel item)
        {
            RenameItem(item); e.Handled = true;
        }
        else if (e.Key == Key.Delete && FocusedItem() is ItemViewModel item2)
        {
            RemoveItem(item2); e.Handled = true;
        }
    }

    private ItemViewModel FocusedItem() =>
        Keyboard.FocusedElement is Button b && b.Tag is ItemViewModel item ? item : null;

    // ---- edit mode ----

    public void SetEditMode(bool on)
    {
        if (VM == null)
            return;
        if (VM.EditMode != on)
            VM.EditMode = on;
        foreach (var host in _wiggleHosts.ToList())
            ApplyWiggle(host, on);
        RefreshDots();
    }

    private void Done_Click(object sender, RoutedEventArgs e) => SetEditMode(false);

    private void WiggleHost_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement host)
            return;
        if (!_wiggleHosts.Contains(host))
            _wiggleHosts.Add(host);
        ApplyWiggle(host, VM?.EditMode == true);
    }

    private void WiggleHost_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement host)
            _wiggleHosts.Remove(host);
    }

    // Small alternating rotation with a random phase per tile, so the grid
    // shivers instead of swinging in lockstep. Off when Windows animations are off.
    private void ApplyWiggle(FrameworkElement host, bool on)
    {
        if (host.RenderTransform is not RotateTransform rot)
            return;
        if (rot.IsFrozen)
        {
            // Transforms declared in a DataTemplate are frozen and shared.
            if (!on || !Animate)
                return;
            rot = new RotateTransform();
            host.RenderTransform = rot;
        }
        if (!on || !Animate)
        {
            rot.BeginAnimation(RotateTransform.AngleProperty, null);
            rot.Angle = 0;
            return;
        }
        double amp = 1.4 + _rng.NextDouble() * 0.5;
        var anim = new DoubleAnimation(-amp, amp, TimeSpan.FromMilliseconds(120 + _rng.Next(40)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromMilliseconds(_rng.Next(160)),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        rot.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    private void Badge_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is ItemViewModel item)
            RemoveItem(item);
        e.Handled = true;
    }

    // ---- menus ----

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = BuildBoardMenu();
        menu.PlacementTarget = MenuButton;
        menu.Placement = PlacementMode.Bottom;
        menu.HorizontalOffset = -190;
        menu.IsOpen = true;
    }

    private void Root_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Root) && e.OriginalSource is not UniformGrid)
        {
            e.Handled = true;
            return;
        }
        Root.ContextMenu = BuildBoardMenu();
    }

    public ContextMenu BuildBoardMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Add apps…", () => Add_Click(null, null), null, "\uE710"));
        menu.Items.Add(Item("New folder", NewFolder, "Ctrl+N", "\uE8F4"));
        menu.Items.Add(Item(VM.EditMode ? "Done editing" : "Edit layout", () => SetEditMode(!VM.EditMode), "Ctrl+E",
            VM.EditMode ? "\uE73E" : "\uE70F"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Settings", () => OpenSettings?.Invoke(), "Ctrl+,", "\uE713"));
        menu.Items.Add(Item("Hide launcher", () => OverlayShouldHide?.Invoke(), "Esc", "\uE711"));
        return menu;
    }

    private static MenuItem Item(string header, Action click, string gesture = null, string glyph = null)
    {
        var mi = new MenuItem { Header = header, InputGestureText = gesture, Icon = glyph };
        mi.Click += (_, _) => click();
        return mi;
    }

    private void Tile_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Button b || b.Tag is not ItemViewModel item)
            return;
        b.ContextMenu = BuildTileMenu(item);
    }

    public ContextMenu BuildTileMenu(ItemViewModel item)
    {
        var menu = new ContextMenu();
        bool inFolder = ActiveFolder != null && ActiveFolder.Children.Contains(item);
        menu.Items.Add(Item("Open", () => Tile_Click(new Button { Tag = item }, null), "Enter",
            item.IsFolder ? "\uE838" : "\uE768"));
        if (!item.IsFolder)
        {
            menu.Items.Add(Item("Run as administrator", () => Launch(item, true), null, "\uE7EF"));
            if (item.ResolvedTarget != null)
                menu.Items.Add(Item("Open file location", () => RevealInExplorer(item.ResolvedTarget), null, "\uED25"));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Rename", () => RenameItem(item), "F2", "\uE8AC"));
        if (inFolder)
        {
            menu.Items.Add(Item("Move out of folder", () => MoveOutOfFolder(item), null, "\uE8DE"));
        }
        else
        {
            var move = new MenuItem { Header = "Move to page", Icon = "\uE8AB" };
            int pages = Math.Max(VM.PageCount, item.Model.Page + 1) + 1;
            for (int p = 0; p < pages; p++)
            {
                int page = p;
                var mi = new MenuItem
                {
                    Header = p == pages - 1 && p >= VM.PageCount ? $"New page {p + 1}" : $"Page {p + 1}",
                    IsChecked = item.Model.Page == p,
                };
                mi.Click += (_, _) => MoveToPage(item, page);
                move.Items.Add(mi);
            }
            menu.Items.Add(move);
        }
        menu.Items.Add(new Separator());
        var remove = Item(item.IsFolder ? "Remove folder" : "Remove from launcher", () => RemoveItem(item), "Del", "\uE74D");
        remove.Tag = "danger";
        menu.Items.Add(remove);
        return menu;
    }

    private void RevealInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch { }
        OverlayShouldHide?.Invoke();
    }

    // ---- edit operations ----

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new WpfOpenFileDialog
        {
            Title = "Add apps",
            Multiselect = true,
            Filter = "Apps and shortcuts (*.lnk;*.exe;*.url)|*.lnk;*.exe;*.url|All files (*.*)|*.*",
        };
        bool picked = false;
        ShowModal(() => picked = dlg.ShowDialog(Window.GetWindow(this)) == true);
        if (!picked)
            return;
        var failed = new List<string>();
        foreach (var path in dlg.FileNames)
        {
            try
            {
                var model = LinkImporter.Import(path, VM.Store);
                VM.AddModel(model, VM.CurrentPage);
            }
            catch (Exception ex)
            {
                failed.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        VM.RequestSave();
        VM.RefreshPage();
        Refresh();
        if (failed.Count > 0)
            ShowModal(() => MessageBox.Show(Window.GetWindow(this), string.Join("\n", failed),
                "Some apps were not added", MessageBoxButton.OK, MessageBoxImage.Warning));
    }

    private void NewFolder()
    {
        ShowPrompt("New folder", "", "Create", name =>
        {
            var model = new WinLaunch.Legacy.ICItem { IsFolder = true, Name = name };
            VM.AddModel(model, VM.CurrentPage);
            VM.RequestSave();
            VM.RefreshPage();
            Refresh();
        });
    }

    private void RenameItem(ItemViewModel item)
    {
        ShowPrompt($"Rename \"{item.Name}\"", item.Name, "Save", name =>
        {
            item.Model.Name = name;
            item.NotifyRenamed();
            if (ActiveFolder == item)
                FolderTitle.Text = name;
            VM.RequestSave();
        });
    }

    private void MoveToPage(ItemViewModel item, int page)
    {
        VM.TopItems.Remove(item);
        VM.AddModel(item.Model, page);
        VM.RequestSave();
        VM.RecountPages();
        VM.RefreshPage();
        Refresh();
    }

    private void MoveOutOfFolder(ItemViewModel item)
    {
        var folder = ActiveFolder;
        if (folder == null)
            return;
        folder.Children.Remove(item);
        folder.Model.Items.Remove(item.Model);
        VM.AddModel(item.Model, VM.CurrentPage);
        if (folder.Children.Count == 0)
            CloseFolder();
        VM.RequestSave();
        VM.RefreshPage();
        Refresh();
    }

    // Removing a tile never uninstalls anything; it is undoable for a few
    // seconds (button or Ctrl+Z), and every save keeps a backup besides.
    private void RemoveItem(ItemViewModel item)
    {
        var folder = ActiveFolder != null && ActiveFolder.Children.Contains(item) ? ActiveFolder : null;
        if (folder != null)
        {
            int index = folder.Children.IndexOf(item);
            folder.Children.Remove(item);
            folder.Model.Items.Remove(item.Model);
            _lastRemoved = (item, folder, index, item.Model.Page, item.Model.GridIndex);
            if (folder.Children.Count == 0)
                CloseFolder();
        }
        else
        {
            _lastRemoved = (item, null, VM.TopItems.IndexOf(item), item.Model.Page, item.Model.GridIndex);
            VM.TopItems.Remove(item);
        }
        VM.RequestSave();
        VM.RefreshPage();
        Refresh();
        UndoText.Text = $"Removed \"{item.Name}\"";
        PageDots.Visibility = Visibility.Collapsed;
        UndoToast.Visibility = Visibility.Visible;
        _undoTimer.Stop();
        _undoTimer.Start();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_lastRemoved is not { } r)
            return;
        if (r.folder != null)
        {
            int at = Math.Clamp(r.index, 0, r.folder.Children.Count);
            r.folder.Children.Insert(at, r.item);
            r.folder.Model.Items.Insert(Math.Clamp(r.index, 0, r.folder.Model.Items.Count), r.item.Model);
            if (!VM.TopItems.Contains(r.folder))
                VM.TopItems.Add(r.folder);
        }
        else
        {
            r.item.Model.Page = r.page;
            r.item.Model.GridIndex = r.grid;
            VM.TopItems.Insert(Math.Clamp(r.index, 0, VM.TopItems.Count), r.item);
        }
        _lastRemoved = null;
        VM.RequestSave();
        VM.RefreshPage();
        Refresh();
        HideUndo();
    }

    private void HideUndo()
    {
        _undoTimer.Stop();
        UndoToast.Visibility = Visibility.Collapsed;
        PageDots.Visibility = Visibility.Visible;
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
        catch { }
    }

    // ---- prompt ----

    private void ShowPrompt(string title, string initial, string okText, Action<string> accept)
    {
        _promptAccept = accept;
        PromptTitle.Text = title;
        PromptOk.Content = okText;
        PromptBox.Text = initial;
        PromptLayer.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            PromptBox.Focus();
            PromptBox.SelectAll();
        }), DispatcherPriority.Input);
    }

    private void PromptBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { PromptOk_Click(null, null); e.Handled = true; }
    }

    private void PromptOk_Click(object sender, RoutedEventArgs e)
    {
        var text = PromptBox.Text.Trim();
        var accept = _promptAccept;
        ClosePrompt();
        if (text.Length > 0)
            accept?.Invoke(text);
    }

    private void PromptCancel_Click(object sender, RoutedEventArgs e) => ClosePrompt();

    private void ClosePrompt()
    {
        _promptAccept = null;
        PromptLayer.Visibility = Visibility.Collapsed;
    }

    private static void ShowModal(Action show)
    {
        ModalDepth++;
        try { show(); }
        finally { ModalDepth--; }
    }

    // ---- drag and drop ----

    private void Tile_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button b && b.Tag is ItemViewModel item)
        {
            _dragStart = e.GetPosition(this);
            _dragItem = item;
            if (!VM.EditMode)
                _holdTimer.Start();
        }
    }

    private void Tile_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _holdTimer.Stop();
    }

    private void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem == null || e.LeftButton != MouseButtonState.Pressed)
            return;
        var pos = e.GetPosition(this);
        bool moved = Math.Abs(pos.X - _dragStart.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                     Math.Abs(pos.Y - _dragStart.Y) >= SystemParameters.MinimumVerticalDragDistance;
        if (!moved)
            return;
        _holdTimer.Stop();
        if (!VM.EditMode)
            return;
        var item = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop((DependencyObject)sender, item, WpfDragDropEffects.Move);
        _suppressClick = false;
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
        e.Handled = true;
        bool sourceTop = VM.TopItems.Contains(source);
        bool targetTop = VM.TopItems.Contains(target);
        if (target.IsFolder && targetTop && sourceTop && !source.IsFolder)
        {
            // Drop onto a folder: move the app inside it.
            VM.TopItems.Remove(source);
            target.Model.Items ??= new List<WinLaunch.Legacy.ICItem>();
            target.Model.Items.Add(source.Model);
            target.Children.Add(source);
        }
        else if (sourceTop && targetTop)
        {
            (target.Model.GridIndex, source.Model.GridIndex) = (source.Model.GridIndex, target.Model.GridIndex);
            (target.Model.Page, source.Model.Page) = (source.Model.Page, target.Model.Page);
        }
        else if (ActiveFolder != null && ActiveFolder.Children.Contains(source) &&
                 ActiveFolder.Children.Contains(target))
        {
            // Reorder inside the open folder.
            var kids = ActiveFolder.Children;
            int to = kids.IndexOf(target);
            kids.Move(kids.IndexOf(source), to);
            ActiveFolder.Model.Items.Remove(source.Model);
            ActiveFolder.Model.Items.Insert(to, source.Model);
        }
        else
        {
            return;
        }
        VM.RequestSave();
        VM.RefreshPage();
        Refresh();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop))
            return;
        foreach (var path in (string[])e.Data.GetData(WpfDataFormats.FileDrop))
        {
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            {
                try { VM.AddModel(LinkImporter.Import(path, VM.Store), VM.CurrentPage); }
                catch { }
            }
        }
        VM.RequestSave();
        VM.RefreshPage();
        Refresh();
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
}
