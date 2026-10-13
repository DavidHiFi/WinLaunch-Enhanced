using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WinLaunch.Legacy;
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
using Panel = System.Windows.Controls.Panel;
using TextBox = System.Windows.Controls.TextBox;
using Point = System.Windows.Point;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfOpenFolderDialog = Microsoft.Win32.OpenFolderDialog;

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
    public event Action QuitRequested;

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
        // The page grid binds to these, so it follows the settings even before it is created.
        Resources["PageColumns"] = Math.Max(1, s.Columns);
        Resources["PageRows"] = Math.Max(1, s.Rows);
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
        CloseEdit();
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
        if (FolderLayer.Visibility == Visibility.Visible || AnyDialogOpen)
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
        if (_swiping || !IsEmptySurface(e.OriginalSource))
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

    private void OpenItem(ItemViewModel item)
    {
        if (item.IsFolder)
            OpenFolder(item);
        else
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
        FolderGrid.ItemsSource = folder.Children;
        FolderLayer.Visibility = Visibility.Visible;
        FolderLayer.UpdateLayout();
        SizeFolderGrid(folder);
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
        // Keyboard users land on the first app; a mouse open leaves no tile
        // focused, so the only plate is the one under the pointer.
        bool viaKeyboard = InputManager.Current.MostRecentInputDevice is KeyboardDevice;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (viaKeyboard)
                FolderGrid.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            else
                Focus();
        }), DispatcherPriority.Input);
    }

    private void SizeFolderGrid(ItemViewModel folder)
    {
        var s = VM.Settings;
        var grid = FindPanel<UniformGrid>(FolderGrid);
        int cols = Math.Clamp(folder.Children.Count, 1, Math.Max(1, Math.Min(s.FolderColumns, s.Columns)));
        if (grid != null)
            grid.Columns = cols;
        double cellH = (double)Resources["TileCellHeight"];
        FolderScroll.MaxHeight = Math.Max(1, Math.Min(s.FolderRows, 4)) * cellH;
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
        Refresh();
    }

    private void CalcChip_Click(object sender, MouseButtonEventArgs e) => CopyCalc();

    private void CopyCalc()
    {
        try { System.Windows.Clipboard.SetText(CalcText.Text); } catch { }
        if (VM.Prefs.CloseOnLaunch)
            OverlayShouldHide?.Invoke();
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
        if (Keyboard.FocusedElement is TextBoxBase || AnyDialogOpen)
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
        if (EditLayer.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { CloseEdit(); e.Handled = true; }
            else if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox) { EditSave_Click(null, null); e.Handled = true; }
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

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = BuildBoardMenu();
        menu.PlacementTarget = AddButton;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    // Each surface fills its own menu. The event bubbles tile -> folder band ->
    // board, so outer handlers step aside when an inner one owns the click.
    private void Root_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        if (FindTile(src) != null || IsInsideElement(src, FolderBand))
            return;
        if (AnyDialogOpen || IsInside<TextBoxBase>(src))
        {
            e.Handled = true;
            return;
        }
        Fill(Root.ContextMenu, FolderLayer.Visibility == Visibility.Visible && ActiveFolder != null
            ? FolderMenuItems(ActiveFolder)
            : BoardMenuItems());
    }

    private void FolderBand_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        if (FindTile(src) != null)
            return;
        if (AnyDialogOpen || ActiveFolder == null || IsInside<TextBoxBase>(src))
        {
            e.Handled = true;
            return;
        }
        Fill(FolderBand.ContextMenu, FolderMenuItems(ActiveFolder));
    }

    private void Tile_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Button b || b.Tag is not ItemViewModel item)
            return;
        if (AnyDialogOpen)
        {
            e.Handled = true;
            return;
        }
        _holdTimer.Stop();
        b.ContextMenu ??= new ContextMenu();
        Fill(b.ContextMenu, TileMenuItems(item));
    }

    private bool AnyDialogOpen =>
        PromptLayer.Visibility == Visibility.Visible || EditLayer.Visibility == Visibility.Visible;

    // Empty space: anything on the board that is not a control, the search
    // pill, a chip or an open sheet.
    private bool IsEmptySurface(object source)
    {
        if (source is not DependencyObject d || FolderLayer.Visibility == Visibility.Visible || AnyDialogOpen)
            return false;
        return !IsInside<ButtonBase>(d) && !IsInside<TextBoxBase>(d) &&
               !IsInsideElement(d, SearchPill) && !IsInsideElement(d, CalcChip) && !IsInsideElement(d, UndoToast);
    }

    private static DependencyObject ParentOf(DependencyObject d) =>
        d is Visual || d is System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(d)
            : LogicalTreeHelper.GetParent(d);

    // Walks up to the board root; the UserControl's own template is not part of the board.
    private bool IsInside<T>(DependencyObject d) where T : DependencyObject
    {
        for (var cur = d; cur != null && cur != Root && cur != this; cur = ParentOf(cur))
            if (cur is T)
                return true;
        return false;
    }

    private bool IsInsideElement(DependencyObject d, DependencyObject element)
    {
        for (var cur = d; cur != null && cur != this; cur = ParentOf(cur))
            if (cur == element)
                return true;
        return false;
    }

    private ItemViewModel FindTile(DependencyObject d)
    {
        for (var cur = d; cur != null && cur != Root && cur != this; cur = ParentOf(cur))
            if (cur is Button b && b.Tag is ItemViewModel item)
                return item;
        return null;
    }

    private static void Fill(ContextMenu menu, IEnumerable<object> items)
    {
        menu.Items.Clear();
        foreach (var item in items)
            menu.Items.Add(item);
    }

    public ContextMenu BuildBoardMenu()
    {
        var menu = new ContextMenu();
        Fill(menu, BoardMenuItems());
        return menu;
    }

    // The original's board menu (add file, folder, link, installed apps,
    // settings, quit) plus Enhanced's group folder and layout editing.
    private List<object> BoardMenuItems() => new()
    {
        Item("Add app or file…", () => AddFiles(null), null, ""),
        Item("Add folder shortcut…", () => AddFolderShortcut(null), null, ""),
        Item("Add web link…", () => AddWebLink(null), null, ""),
        Item("New folder", NewFolder, "Ctrl+N", ""),
        Item("Add installed apps…", AddInstalledApps, null, ""),
        new Separator(),
        Item(VM.EditMode ? "Done editing" : "Edit layout", () => SetEditMode(!VM.EditMode), "Ctrl+E",
            VM.EditMode ? "" : ""),
        Item("Settings", () => OpenSettings?.Invoke(), "Ctrl+,", ""),
        new Separator(),
        Item("Hide launcher", () => OverlayShouldHide?.Invoke(), "Esc", ""),
        Danger(Item("Quit WinLaunch", Quit, null, "")),
    };

    private List<object> FolderMenuItems(ItemViewModel folder) => new()
    {
        Item("Add app or file to folder…", () => AddFiles(folder), null, ""),
        Item("Add folder shortcut…", () => AddFolderShortcut(folder), null, ""),
        Item("Add web link…", () => AddWebLink(folder), null, ""),
        new Separator(),
        Item("Edit folder…", () => EditItem(folder), null, ""),
        Item("Rename folder", () => RenameItem(folder), "F2", ""),
        Item(VM.EditMode ? "Done editing" : "Edit layout", () => SetEditMode(!VM.EditMode), "Ctrl+E",
            VM.EditMode ? "" : ""),
        new Separator(),
        Item("Close folder", () => CloseFolder(), "Esc", ""),
    };

    private static MenuItem Item(string header, Action click, string gesture = null, string glyph = null)
    {
        var mi = new MenuItem { Header = header, InputGestureText = gesture, Icon = glyph };
        mi.Click += (_, _) => click();
        return mi;
    }

    private static MenuItem Danger(MenuItem mi)
    {
        mi.Tag = "danger";
        return mi;
    }

    public ContextMenu BuildTileMenu(ItemViewModel item)
    {
        var menu = new ContextMenu();
        Fill(menu, TileMenuItems(item));
        return menu;
    }

    // The original's item menu: Open, Run as administrator, Edit, Remove,
    // Open file location; plus rename and page moves.
    private List<object> TileMenuItems(ItemViewModel item)
    {
        var items = new List<object>();
        bool inFolder = ActiveFolder != null && ActiveFolder.Children.Contains(item);
        item.RefreshTarget();
        items.Add(Item("Open", () => OpenItem(item), "Enter", item.IsFolder ? "" : ""));
        if (!item.IsFolder)
        {
            items.Add(Item("Run as administrator", () => Launch(item, true), "Ctrl+Shift+Enter", ""));
            if (item.ResolvedTarget != null && !LegacyStore.IsUrl(item.ResolvedTarget))
                items.Add(Item("Open file location", () => RevealInExplorer(item.ResolvedTarget), null, ""));
        }
        items.Add(new Separator());
        items.Add(Item("Edit…", () => EditItem(item), null, ""));
        items.Add(Item("Rename", () => RenameItem(item), "F2", ""));
        if (inFolder)
        {
            items.Add(Item("Move out of folder", () => MoveOutOfFolder(item), null, ""));
        }
        else
        {
            var move = new MenuItem { Header = "Move to page", Icon = "" };
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
            items.Add(move);
        }
        items.Add(new Separator());
        items.Add(Danger(Item(item.IsFolder ? "Remove folder" : "Remove from launcher", () => RemoveItem(item), "Del", "")));
        return items;
    }

    private void Quit()
    {
        var answer = MessageBoxResult.No;
        ShowModal(() => answer = MessageBox.Show(Window.GetWindow(this),
            "Quit WinLaunch? The shortcut stops working until you start it again.", "Quit WinLaunch",
            MessageBoxButton.YesNo, MessageBoxImage.Question));
        if (answer == MessageBoxResult.Yes)
            QuitRequested?.Invoke();
    }

    // ---- adding ----

    // Adds new tiles to the current page, or to a folder when one is given.
    private void AddModels(IEnumerable<ICItem> models, ItemViewModel folder)
    {
        foreach (var model in models)
        {
            if (folder == null)
            {
                VM.AddModel(model, VM.CurrentPage);
                continue;
            }
            folder.Model.Items ??= new List<ICItem>();
            model.Page = 0;
            model.GridIndex = folder.Model.Items.Count;
            folder.Model.Items.Add(model);
            var vm = new ItemViewModel(model, VM.Store, VM.IconCacheDir, $"in {folder.Name}");
            vm.RefreshTarget();
            vm.LoadIcon();
            folder.Children.Add(vm);
        }
        if (folder != null)
        {
            folder.LoadIcon();
            if (ActiveFolder == folder)
                Dispatcher.BeginInvoke(new Action(() => SizeFolderGrid(folder)), DispatcherPriority.Loaded);
        }
        VM.RequestSave();
        VM.RefreshPage();
        Refresh();
    }

    private void ImportPaths(IEnumerable<string> paths, ItemViewModel folder)
    {
        var added = new List<ICItem>();
        var failed = new List<string>();
        foreach (var path in paths)
        {
            try { added.Add(LinkImporter.Import(path, VM.Store)); }
            catch (Exception ex) { failed.Add($"{Path.GetFileName(path)}: {ex.Message}"); }
        }
        AddModels(added, folder);
        if (failed.Count > 0)
            ShowModal(() => MessageBox.Show(Window.GetWindow(this), string.Join("\n", failed),
                "Some items were not added", MessageBoxButton.OK, MessageBoxImage.Warning));
    }

    private void AddFiles(ItemViewModel folder)
    {
        var dlg = new WpfOpenFileDialog
        {
            Title = folder == null ? "Add apps or files" : $"Add to \"{folder.Name}\"",
            Multiselect = true,
            DereferenceLinks = false,
            Filter = "Apps and shortcuts (*.lnk;*.exe;*.url;*.bat;*.cmd)|*.lnk;*.exe;*.url;*.bat;*.cmd|All files (*.*)|*.*",
        };
        bool picked = false;
        ShowModal(() => picked = dlg.ShowDialog(Window.GetWindow(this)) == true);
        if (picked)
            ImportPaths(dlg.FileNames, folder);
    }

    private void AddFolderShortcut(ItemViewModel folder)
    {
        var dlg = new WpfOpenFolderDialog { Title = "Add folder shortcuts", Multiselect = true };
        bool picked = false;
        ShowModal(() => picked = dlg.ShowDialog(Window.GetWindow(this)) == true);
        if (picked)
            ImportPaths(dlg.FolderNames, folder);
    }

    private void AddWebLink(ItemViewModel folder)
    {
        ShowPrompt("Add web link", "https://", "Add", text =>
        {
            var url = text.Contains("://") ? text : "https://" + text;
            if (!LegacyStore.IsUrl(url) || url.EndsWith("://"))
            {
                ShowModal(() => MessageBox.Show(Window.GetWindow(this), $"\"{text}\" is not a web address.",
                    "Add web link", MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }
            AddModels(new[] { LinkImporter.ImportUrl(url) }, folder);
        });
    }

    // The original's "Refresh installed apps": adds Start menu shortcuts that
    // are not pinned yet; Start menu folders with two or more new apps become folders.
    private void AddInstalledApps()
    {
        var answer = MessageBoxResult.No;
        ShowModal(() => answer = MessageBox.Show(Window.GetWindow(this),
            "Add every Start menu app that is not in WinLaunch yet?", "Add installed apps",
            MessageBoxButton.YesNo, MessageBoxImage.Question));
        if (answer != MessageBoxResult.Yes)
            return;
        CloseFolder(animate: false);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Collect(IEnumerable<ICItem> list)
        {
            foreach (var i in list)
            {
                names.Add(i.Name ?? "");
                Collect(i.Items ?? new List<ICItem>());
            }
        }
        Collect(VM.TopItems.Select(t => t.Model));
        var (files, folders) = InstalledApps.Scan(names);
        var models = new List<ICItem>();
        foreach (var file in files)
        {
            try { models.Add(LinkImporter.Import(file, VM.Store)); } catch { }
        }
        foreach (var (dir, dirFiles) in folders)
        {
            var group = new ICItem { IsFolder = true, Name = new DirectoryInfo(dir).Name };
            foreach (var file in dirFiles)
            {
                try
                {
                    var child = LinkImporter.Import(file, VM.Store);
                    child.Page = 0;
                    child.GridIndex = group.Items.Count;
                    group.Items.Add(child);
                }
                catch { }
            }
            if (group.Items.Count > 0)
                models.Add(group);
        }
        AddModels(models, null);
        int count = models.Sum(m => m.IsFolder ? m.Items.Count : 1);
        ShowModal(() => MessageBox.Show(Window.GetWindow(this),
            count == 0 ? "Every Start menu app is already in WinLaunch." : $"Added {count} apps.",
            "Add installed apps", MessageBoxButton.OK, MessageBoxImage.Information));
    }

    // ---- edit sheet ----

    private ItemViewModel _editItem;
    private string _editIconPath;
    private string _editOriginalPath;

    private void EditItem(ItemViewModel item)
    {
        _editItem = item;
        var m = item.Model;
        item.RefreshTarget();
        EditTitle.Text = item.IsFolder ? "Edit folder" : "Edit app";
        EditName.Text = m.Name ?? "";
        _editOriginalPath = item.ResolvedTarget ?? m.Application ?? "";
        EditPath.Text = _editOriginalPath;
        EditArgs.Text = m.Arguments ?? "";
        EditKeywords.Text = m.Keywords ?? "";
        EditNotes.Text = m.Notes ?? "";
        EditAdmin.IsChecked = m.RunAsAdmin;
        EditMiniatures.IsChecked = m.ShowMiniatures;
        EditAppFields.Visibility = item.IsFolder ? Visibility.Collapsed : Visibility.Visible;
        EditAdmin.Visibility = item.IsFolder ? Visibility.Collapsed : Visibility.Visible;
        EditMiniatures.Visibility = item.IsFolder ? Visibility.Visible : Visibility.Collapsed;
        _editIconPath = m.IconPath;
        EditIcon.Source = item.Icon;
        EditResetIcon.IsEnabled = !string.IsNullOrEmpty(_editIconPath);
        EditError.Visibility = Visibility.Collapsed;
        EditLayer.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            EditName.Focus();
            EditName.SelectAll();
        }), DispatcherPriority.Input);
    }

    private void CloseEdit()
    {
        _editItem = null;
        EditLayer.Visibility = Visibility.Collapsed;
    }

    private async void PreviewEditIcon()
    {
        var item = _editItem;
        if (item == null)
            return;
        var probe = new ICItem
        {
            Name = item.Model.Name, IsFolder = item.Model.IsFolder, Items = item.Model.Items,
            Application = item.Model.Application, IconPath = _editIconPath,
        };
        var bmp = await IconService.GetIconAsync(probe, VM.Store, VM.IconCacheDir, CancellationToken.None);
        if (_editItem == item)
            EditIcon.Source = bmp;
        EditResetIcon.IsEnabled = !string.IsNullOrEmpty(_editIconPath);
    }

    private void EditChooseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new WpfOpenFileDialog
        {
            Title = "Choose an icon",
            Filter = "Images and icons (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff;*.webp;*.exe;*.dll)|" +
                     "*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff;*.webp;*.exe;*.dll|All files (*.*)|*.*",
        };
        bool picked = false;
        ShowModal(() => picked = dlg.ShowDialog(Window.GetWindow(this)) == true);
        if (!picked)
            return;
        try
        {
            _editIconPath = IconService.SaveCustomIcon(dlg.FileName, VM.Store);
            PreviewEditIcon();
        }
        catch (Exception ex)
        {
            ShowEditError($"Could not use that icon: {ex.Message}");
        }
    }

    private void EditResetIcon_Click(object sender, RoutedEventArgs e)
    {
        _editIconPath = null;
        PreviewEditIcon();
    }

    private void EditBrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new WpfOpenFileDialog { Title = "Choose the app or file", DereferenceLinks = false, Filter = "All files (*.*)|*.*" };
        bool picked = false;
        ShowModal(() => picked = dlg.ShowDialog(Window.GetWindow(this)) == true);
        if (picked)
            EditPath.Text = dlg.FileName;
    }

    private void EditBrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new WpfOpenFolderDialog { Title = "Choose the folder" };
        bool picked = false;
        ShowModal(() => picked = dlg.ShowDialog(Window.GetWindow(this)) == true);
        if (picked)
            EditPath.Text = dlg.FolderName;
    }

    private void ShowEditError(string text)
    {
        EditError.Text = text;
        EditError.Visibility = Visibility.Visible;
    }

    private void EditSave_Click(object sender, RoutedEventArgs e)
    {
        var item = _editItem;
        if (item == null)
            return;
        var m = item.Model;
        var name = EditName.Text.Trim();
        if (name.Length == 0)
        {
            ShowEditError("Give it a name.");
            return;
        }
        if (!item.IsFolder)
        {
            var path = EditPath.Text.Trim().Trim('"');
            if (path != _editOriginalPath)
            {
                if (!LegacyStore.IsUrl(path) && !File.Exists(path) && !System.IO.Directory.Exists(path))
                {
                    ShowEditError("That path does not exist.");
                    return;
                }
                m.Application = path;
            }
            m.Arguments = EditArgs.Text.Trim();
            m.Keywords = EditKeywords.Text.Trim();
            m.RunAsAdmin = EditAdmin.IsChecked == true;
        }
        else
        {
            m.ShowMiniatures = EditMiniatures.IsChecked == true;
        }
        m.Name = name;
        m.Notes = EditNotes.Text.Trim();
        m.IconPath = string.IsNullOrEmpty(_editIconPath) ? null : _editIconPath;
        CloseEdit();
        item.NotifyEdited();
        if (ActiveFolder == item)
            FolderTitle.Text = name;
        VM.RequestSave();
        VM.RefreshSearch();
        Refresh();
    }

    private void EditCancel_Click(object sender, RoutedEventArgs e) => CloseEdit();

    private void RevealInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch { }
        OverlayShouldHide?.Invoke();
    }

    // ---- edit operations ----

    private void Add_Click(object sender, RoutedEventArgs e) => AddFiles(null);

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

    // Focus follows the pointer between tiles, so a keyboard-focused tile never
    // keeps a second plate next to the hovered one. The search box keeps focus.
    private void Tile_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Button b && !AnyDialogOpen && Keyboard.FocusedElement is Button f &&
            f != b && f.Tag is ItemViewModel)
            b.Focus();
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
