using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WinLaunch.Legacy;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinLaunch.Enhanced.Services;
using WinLaunch.Enhanced.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
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

    private Point _pressStart;
    private ItemViewModel _pressItem;
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
            if (VM != null && !VM.EditMode && _pressItem != null)
            {
                _suppressClick = true;
                SetEditMode(true);
            }
        };
        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _undoTimer.Tick += (_, _) => HideUndo();
        MouseMove += Drag_MouseMove;
        MouseLeftButtonUp += Drag_MouseUp;
        LostMouseCapture += Drag_LostCapture;
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
        AddButton.Visibility = VM.Prefs.ShowAddButton ? Visibility.Visible : Visibility.Collapsed;
        MenuButton.Visibility = VM.Prefs.ShowSettingsButton ? Visibility.Visible : Visibility.Collapsed;
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
        double font = Math.Round(Math.Clamp(11.5 + icon / 40, 12, 15) * ThemeManager.TextScale, 1);
        // Leave room for the tile padding, a little air and two lines of name,
        // so labels are never clipped mid-line.
        double line = Math.Ceiling(font * 1.36);
        bool labels = s.ShowItemIconText || s.ShowFolderIconText;
        double room = cellH - 20 - (labels ? line * 2 + 9 : 0);
        icon = Math.Max(40, Math.Floor(Math.Min(icon, room)));
        Resources["TileCellWidth"] = cellW;
        Resources["TileCellHeight"] = cellH;
        Resources["TileIconSize"] = icon;
        Resources["TileLabelWidth"] = Math.Max(40, cellW - 6);
        Resources["TileLabelMaxHeight"] = line * 2;
        Resources["TileFontSize"] = font;
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
        if (ok && VM.RecoveryNote != null)
        {
            var note = VM.RecoveryNote;
            VM.RecoveryNote = null;
            ShowNotice("Your apps were restored from a backup", note);
        }
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

    private void GoToPage(int page, bool allowNewPage = false)
    {
        if (VM == null || Searching)
            return;
        page = Math.Clamp(page, 0, Math.Max(0, VM.PageCount - (allowNewPage ? 0 : 1)));
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
            ShowNotice($"Could not open \"{item.Name}\"", ex.Message);
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
        // It is cached and blurred behind the folder. Only the blur radius
        // and the two layers' own opacities animate, so no frame re-renders the
        // board or composites a full-screen layer offscreen.
        var blur = new System.Windows.Media.Effects.BlurEffect
        {
            Radius = Animate ? 0 : 16, RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
        };
        // Cached around the blur at half resolution: the blur hides the lost
        // detail and costs a quarter as much while the radius animates.
        BoardLayer.CacheMode = new BitmapCache { RenderAtScale = 0.5 };
        PageHost.Effect = blur;
        FolderLayer.Opacity = 1;
        // Clear what an animated close left behind (opacity 0, scale 0.94).
        FolderPanel.BeginAnimation(OpacityProperty, null);
        FolderDim.BeginAnimation(OpacityProperty, null);
        FolderScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        FolderScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (Animate)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(200);
            // The folder scales in as one cached bitmap, then renders live again.
            FolderPanel.CacheMode = new BitmapCache { SnapsToDevicePixels = true };
            var grow = new DoubleAnimation(0.9, 1, dur) { EasingFunction = ease };
            grow.Completed += (_, _) => FolderPanel.CacheMode = null;
            FolderScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, dur) { EasingFunction = ease });
            FolderScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            FolderPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            FolderDim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, new DoubleAnimation(0, 16, dur) { EasingFunction = ease });
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
        void Finish()
        {
            FolderLayer.Visibility = Visibility.Collapsed;
            FolderPanel.CacheMode = null;
            PageHost.Effect = null;
            BoardLayer.CacheMode = null;
        }
        if (!animate || !Animate)
        {
            FolderPanel.BeginAnimation(OpacityProperty, null);
            FolderDim.BeginAnimation(OpacityProperty, null);
            Finish();
            return;
        }
        var dur = TimeSpan.FromMilliseconds(140);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, dur) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            if (ActiveFolder == null)
                Finish();
        };
        FolderPanel.CacheMode = new BitmapCache { SnapsToDevicePixels = true };
        FolderScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, dur) { EasingFunction = ease });
        FolderScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, dur) { EasingFunction = ease });
        FolderPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
        FolderDim.BeginAnimation(OpacityProperty, fade);
        if (PageHost.Effect is System.Windows.Media.Effects.BlurEffect blur)
            blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
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
        if (_drag != null)
        {
            if (e.Key == Key.Escape)
                CancelDrag();
            e.Handled = true;
            return;
        }
        if (PromptLayer.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { ClosePrompt(); e.Handled = true; }
            else if (e.Key == Key.Enter && PromptBox.Visibility != Visibility.Visible) { PromptOk_Click(null, null); e.Handled = true; }
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
        Danger(Item("Quit WinLaunch Enhanced", Quit, null, "")),
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
            // The original's per-app checkbox: when ticked, a normal click runs it elevated too.
            var always = new MenuItem
            {
                Header = "Always run as administrator", IsCheckable = true, IsChecked = item.Model.RunAsAdmin,
                Icon = "", StaysOpenOnClick = false,
            };
            always.Click += (_, _) =>
            {
                item.Model.RunAsAdmin = always.IsChecked;
                VM.RequestSave();
            };
            items.Add(always);
            if (item.ResolvedTarget != null && !LegacyStore.IsUrl(item.ResolvedTarget))
            {
                // Packaged apps resolve empty and MSI advertised shortcuts
                // point at an Installer-cache stub: reveal their cached .lnk,
                // the file that actually launches them, instead of a dead path.
                var reveal = VM.Store.IsShellLaunchLink(item.Model.Application, item.ResolvedTarget)
                    ? VM.Store.CachedLinkPath(item.Model.Application)
                    : item.ResolvedTarget;
                items.Add(Item("Open file location", () => RevealInExplorer(reveal), null, ""));
            }
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
        ShowNotice("Quit WinLaunch Enhanced?", "The shortcut stops working until you start WinLaunch again.",
            "Quit", () => QuitRequested?.Invoke());
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
            ShowNotice("Some items were not added", string.Join("\n", failed));
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
                ShowNotice("Add web link", $"\"{text}\" is not a web address.");
                return;
            }
            AddModels(new[] { LinkImporter.ImportUrl(url) }, folder);
        });
    }

    // The original's "Refresh installed apps": adds Start menu shortcuts that
    // are not pinned yet; Start menu folders with two or more new apps become folders.
    private void AddInstalledApps() =>
        ShowNotice("Add installed apps?", "Adds every Start menu app that is not in WinLaunch yet. " +
            "Start menu folders with two or more new apps become folders here.", "Add", AddInstalledAppsNow);

    private void AddInstalledAppsNow()
    {
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
        ShowNotice("Add installed apps",
            count == 0 ? "Every Start menu app is already in WinLaunch." : $"Added {count} apps.");
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
        // Packaged apps resolve to an empty string and MSI advertised
        // shortcuts to an Installer-cache stub: show the cached .lnk they
        // run, so the location is never a blank field or a dead stub path
        // (the original fell back to the stored reference the same way).
        if (!string.IsNullOrEmpty(item.ResolvedTarget) &&
            !VM.Store.IsShellLaunchLink(m.Application, item.ResolvedTarget))
            _editOriginalPath = item.ResolvedTarget;
        else if (m.Application != null && (Path.IsPathRooted(m.Application) || LegacyStore.IsUrl(m.Application)))
            _editOriginalPath = m.Application;
        else
            _editOriginalPath = VM.Store.CachedLinkPath(m.Application) ?? m.Application ?? "";
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
        VM.RefreshPage();
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
            VM.RefreshPage();
            Refresh();
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
        PromptMessage.Visibility = Visibility.Collapsed;
        PromptBox.Visibility = Visibility.Visible;
        PromptCancel.Visibility = Visibility.Visible;
        PromptOk.Content = okText;
        PromptBox.Text = initial;
        PromptLayer.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            PromptBox.Focus();
            PromptBox.SelectAll();
        }), DispatcherPriority.Input);
    }

    // Themed stand-in for a message box: a message with OK, or with a
    // confirm button and Cancel when onOk is given.
    private void ShowNotice(string title, string message, string okText = "OK", Action onOk = null)
    {
        _promptAccept = onOk == null ? null : _ => onOk();
        PromptTitle.Text = title;
        PromptMessage.Text = message;
        PromptMessage.Visibility = Visibility.Visible;
        PromptBox.Visibility = Visibility.Collapsed;
        PromptBox.Text = "-";
        PromptCancel.Visibility = onOk == null ? Visibility.Collapsed : Visibility.Visible;
        PromptOk.Content = okText;
        PromptLayer.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() => PromptOk.Focus()), DispatcherPriority.Input);
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
    //
    // Press a tile and move: it lifts at once, and the other tiles glide out of
    // the way as the pointer moves, like the original launcher. Releasing settles
    // it into the cell under the pointer. Dropping on the middle of an app makes a
    // folder of the two, on the middle of a folder moves the app inside. Dragging
    // out of an open folder takes the app out, and resting at the left or right
    // edge of the board turns the page.

    private const double DragThreshold = 8;
    private const double DragPad = 16;

    private sealed class DragState
    {
        public ItemViewModel Item;
        public Button Source;
        public ItemViewModel FromFolder;
        public bool HomeInFolder;
        public int HomeIndex;
        public int HomePage;
        public System.Windows.Controls.Image Ghost;
        public Point Grab;
        public Point Pointer;
        public int EdgeDir;
        public bool Settling;
        public bool InFolder;
        public int InsertAt;
        public ItemViewModel Merge;
        public Button MergeButton;
        public ItemsControl Host;
        public readonly Dictionary<ItemViewModel, (double X, double Y, double Opacity)> Glides = new();
    }

    private DragState _drag;
    private DispatcherTimer _edgeTimer;

    private void Tile_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button b && b.Tag is ItemViewModel item)
        {
            _pressStart = e.GetPosition(this);
            _pressItem = item;
            if (!VM.EditMode)
                _holdTimer.Start();
        }
    }

    private void Tile_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _holdTimer.Stop();
        _pressItem = null;
    }

    // Focus follows the pointer between tiles, so a keyboard-focused tile never
    // keeps a second plate next to the hovered one. The search box keeps focus.
    private void Tile_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_drag == null && sender is Button b && !AnyDialogOpen && Keyboard.FocusedElement is Button f &&
            f != b && f.Tag is ItemViewModel)
            b.Focus();
    }

    private void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drag != null || _pressItem == null || e.LeftButton != MouseButtonState.Pressed || sender is not Button b)
            return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _pressStart.X) < DragThreshold && Math.Abs(pos.Y - _pressStart.Y) < DragThreshold)
            return;
        if (!Searching)
            BeginDrag(b, _pressItem, e);
    }

    private void BeginDrag(Button source, ItemViewModel item, MouseEventArgs e)
    {
        _pressItem = null;
        _holdTimer.Stop();
        var fromFolder = ActiveFolder != null && ActiveFolder.Children.Contains(item) ? ActiveFolder : null;
        // Folders do not nest, so a folder can only be dragged on the board.
        if (fromFolder == null && !VM.TopItems.Contains(item))
            return;
        if (source.Content is not FrameworkElement content || content.ActualWidth <= 0)
            return;
        // Pause the wiggle so the snapshot is upright.
        ApplyWiggle(content, false);
        double pad = DragPad, w = content.ActualWidth, h = content.ActualHeight;
        double W = w + pad * 2, H = h + pad * 2;
        // Rendered at device pixels. Without the DPI scale the icon is stretched.
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(W * dpi.DpiScaleX), (int)Math.Ceiling(H * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // The remove badge stays off the lifted icon; its opacity is restored after the render.
        var badge = FindPanel<Button>(content);
        double badgeOpacity = badge?.Opacity ?? 1;
        if (badge != null)
            badge.Opacity = 0;
        var art = new DrawingVisual();
        using (var dc = art.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(dpi.DpiScaleX, dpi.DpiScaleY));
            dc.DrawRectangle(new VisualBrush(content)
            {
                Viewbox = new Rect(-pad, -pad, W, H), ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, W, H), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill,
            }, null, new Rect(0, 0, W, H));
            dc.Pop();
        }
        bitmap.Render(art);
        if (badge != null)
            badge.Opacity = badgeOpacity;
        bitmap.Freeze();
        var grab = e.GetPosition(content);
        // Starts at the tile's current hover scale, so the lift has no visible jump.
        double scale = (source.RenderTransform as ScaleTransform)?.ScaleX ?? 1;
        var ghost = new System.Windows.Controls.Image
        {
            Source = bitmap, Width = W, Height = H, IsHitTestVisible = false, Opacity = source.Opacity,
            RenderTransformOrigin = new Point((grab.X + pad) / W, (grab.Y + pad) / H),
            RenderTransform = new ScaleTransform(scale, scale),
        };
        RenderOptions.SetBitmapScalingMode(ghost, BitmapScalingMode.HighQuality);
        var layer = e.GetPosition(DragLayer);
        Canvas.SetLeft(ghost, layer.X - grab.X - pad);
        Canvas.SetTop(ghost, layer.Y - grab.Y - pad);
        DragLayer.Children.Add(ghost);
        var list = fromFolder != null ? fromFolder.Children : VM.CurrentPageItems;
        _drag = new DragState
        {
            Item = item, Source = source, FromFolder = fromFolder, Ghost = ghost,
            Grab = new Point(grab.X + pad, grab.Y + pad), Pointer = e.GetPosition(this),
            HomeInFolder = fromFolder != null, HomeIndex = list.IndexOf(item), HomePage = VM.CurrentPage,
        };
        source.Opacity = 0;
        source.IsHitTestVisible = false;
        _suppressClick = true;
        Mouse.Capture(this, CaptureMode.Element);
        Lift(ghost);
        Preview();
    }

    // The lifted tile grows and turns translucent, as in the original.
    private static void Lift(System.Windows.Controls.Image ghost)
    {
        var st = (ScaleTransform)ghost.RenderTransform;
        if (!Animate)
        {
            st.ScaleX = st.ScaleY = 1.2;
            ghost.Opacity = 0.7;
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(140);
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.2, dur) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.2, dur) { EasingFunction = ease });
        ghost.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.7, dur) { EasingFunction = ease });
    }

    private void Drag_MouseMove(object sender, MouseEventArgs e)
    {
        var d = _drag;
        if (d == null)
            return;
        e.Handled = true;
        if (d.Settling)
            return;
        var layer = e.GetPosition(DragLayer);
        Canvas.SetLeft(d.Ghost, layer.X - d.Grab.X);
        Canvas.SetTop(d.Ghost, layer.Y - d.Grab.Y);
        d.Pointer = e.GetPosition(this);
        // Leaving the open folder takes the app out of it onto the board.
        if (d.FromFolder != null && FolderLayer.Visibility == Visibility.Visible && !OverFolderBand(e))
            CloseFolder(animate: false);
        UpdateEdgePaging();
        Preview();
    }

    private void Drag_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var d = _drag;
        if (d == null)
            return;
        e.Handled = true;
        if (d.Settling)
            return;
        d.Pointer = e.GetPosition(this);
        Preview();
        Settle();
    }

    // The board lost the mouse mid-drag (focus or capture went elsewhere): put the tile back.
    private void Drag_LostCapture(object sender, MouseEventArgs e)
    {
        if (_drag != null && !_drag.Settling && e.OriginalSource == this)
            CancelDrag();
    }

    private bool OverFolderBand(MouseEventArgs e)
    {
        var p = e.GetPosition(FolderBand);
        const double slack = 24;
        return p.X >= -slack && p.Y >= -slack - 60 && p.X <= FolderBand.ActualWidth + slack &&
               p.Y <= FolderBand.ActualHeight + slack;
    }

    // Release: the tile lands in the cell it was previewing, or on the tile it merges with.
    private void Settle()
    {
        var d = _drag;
        d.Settling = true;
        if (Mouse.Captured == this)
            Mouse.Capture(null);
        bool moved = d.Merge != null || d.InFolder != d.HomeInFolder || d.InsertAt != d.HomeIndex ||
                     (!d.InFolder && VM.CurrentPage != d.HomePage);
        if (!moved)
        {
            Land(d, HomeCenter(d), 1, 1, EndDrag);
            return;
        }
        if (d.Merge != null)
        {
            int at = VM.CurrentPageItems.IndexOf(d.Merge);
            Land(d, at < 0 ? GhostCenter(d) : CellCenter(TileGrid, at), 0.6, 0, () => Finish(d));
            return;
        }
        Land(d, CellCenter(d.InFolder ? FolderGrid : TileGrid, d.InsertAt), 1, 1, () => Finish(d));
    }

    // The model changes only after the tile has landed, so nothing jumps.
    private void Finish(DragState d)
    {
        try { DropTile(d); }
        catch (Exception ex) { App.Trace("drop failed: " + ex.Message); }
        EndDrag();
    }

    private void CancelDrag()
    {
        var d = _drag;
        if (d == null || d.Settling)
            return;
        d.Settling = true;
        if (Mouse.Captured == this)
            Mouse.Capture(null);
        var host = CurrentHost;
        var items = CurrentItems;
        bool home = d.Source is { IsLoaded: true, IsVisible: true };
        ApplyLayout(host, d, items, items.ToList(), ColumnsFor(host),
            host == FolderGrid ? int.MaxValue : VM.PageCapacity, CellWidth, CellHeight);
        Land(d, home ? HomeCenter(d) : GhostCenter(d), 1, home ? 1 : 0, EndDrag);
    }

    private void EndDrag()
    {
        var d = _drag;
        _drag = null;
        _edgeTimer?.Stop();
        if (d == null)
            return;
        d.MergeButton?.ClearValue(BackgroundProperty);
        DragLayer.Children.Remove(d.Ghost);
        ClearGlides(TileGrid);
        ClearGlides(FolderGrid);
        if (d.Source != null)
        {
            d.Source.Opacity = 1;
            d.Source.IsHitTestVisible = true;
            if (d.Source.Content is FrameworkElement content)
                ApplyWiggle(content, VM.EditMode);
        }
        if (Mouse.Captured == this)
            Mouse.Capture(null);
        Dispatcher.BeginInvoke(new Action(() => _suppressClick = false), DispatcherPriority.Input);
    }

    // Works out where the tile would land and glides the others out of its way.
    private void Preview()
    {
        var d = _drag;
        if (d == null || d.Settling)
            return;
        var host = CurrentHost;
        var items = CurrentItems;
        if (d.Host != host)
        {
            if (d.Host != null)
                ClearGlides(d.Host);
            d.Glides.Clear();
            d.Host = host;
        }
        bool inFolder = host == FolderGrid;
        int cols = ColumnsFor(host);
        double cw = CellWidth, ch = CellHeight;
        var p = TranslatePoint(d.Pointer, host);
        int col = Math.Clamp((int)Math.Floor(p.X / cw), 0, cols - 1);
        int row = Math.Max(0, (int)Math.Floor(p.Y / ch));
        int cell = row * cols + col;
        var merge = !inFolder && !d.Item.IsFolder && cell < items.Count && items[cell] != d.Item &&
                    OnIconCenter(p, cw, ch, col, row) ? items[cell] : null;
        int others = items.Contains(d.Item) ? items.Count - 1 : items.Count;
        d.InFolder = inFolder;
        d.InsertAt = Math.Min(cell, others);
        if (merge != d.Merge)
            SetMergeTarget(d, merge, host);
        var layout = merge == null ? WithDropped(items, d.Item, d.InsertAt) : items.ToList();
        ApplyLayout(host, d, items, layout, cols, inFolder ? int.MaxValue : VM.PageCapacity, cw, ch);
    }

    // Each tile glides from its cell to the cell it would take in the layout.
    private void ApplyLayout(ItemsControl host, DragState d, IList<ItemViewModel> items, IList<ItemViewModel> layout,
        int cols, int cap, double cw, double ch)
    {
        for (int i = 0; i < layout.Count; i++)
        {
            var item = layout[i];
            int was = items.IndexOf(item);
            if (item == d.Item || was < 0)
                continue;
            Glide(host, d, item, (i % cols - was % cols) * cw, (i / cols - was / cols) * ch, i < cap ? 1 : 0);
        }
    }

    // Glides a tile by an offset from its cell. Unchanged targets are skipped.
    private void Glide(ItemsControl host, DragState d, ItemViewModel item, double x, double y, double opacity)
    {
        if (host.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement c)
            return;
        if (d.Glides.TryGetValue(item, out var was) && was == (x, y, opacity))
            return;
        d.Glides[item] = (x, y, opacity);
        var t = c.RenderTransform as TranslateTransform;
        if (t == null)
        {
            t = new TranslateTransform();
            c.RenderTransform = t;
        }
        if (!Animate)
        {
            t.X = x;
            t.Y = y;
            c.Opacity = opacity;
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(170);
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, dur) { EasingFunction = ease });
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, dur) { EasingFunction = ease });
        c.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(opacity, dur) { EasingFunction = ease });
    }

    private static void ClearGlides(ItemsControl host)
    {
        for (int i = 0; i < host.Items.Count; i++)
        {
            if (host.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c)
                continue;
            c.BeginAnimation(UIElement.OpacityProperty, null);
            c.Opacity = 1;
            if (c.RenderTransform is TranslateTransform t)
            {
                t.BeginAnimation(TranslateTransform.XProperty, null);
                t.BeginAnimation(TranslateTransform.YProperty, null);
                t.X = 0;
                t.Y = 0;
            }
        }
    }

    private static List<ItemViewModel> WithDropped(IList<ItemViewModel> items, ItemViewModel dropped, int at)
    {
        var list = items.Where(i => i != dropped).ToList();
        list.Insert(Math.Clamp(at, 0, list.Count), dropped);
        return list;
    }

    private void SetMergeTarget(DragState d, ItemViewModel tile, ItemsControl host)
    {
        d.MergeButton?.ClearValue(BackgroundProperty);
        d.MergeButton = null;
        d.Merge = tile;
        if (tile != null && host.ItemContainerGenerator.ContainerFromItem(tile) is DependencyObject container)
        {
            d.MergeButton = FindPanel<Button>(container);
            d.MergeButton?.SetResourceReference(BackgroundProperty, "GlassHoverBrush");
        }
    }

    // Whether the pointer is over the middle of a tile's icon (merge), not its edge (move).
    private bool OnIconCenter(Point p, double cw, double ch, int col, int row)
    {
        double icon = (double)Resources["TileIconSize"];
        double x = p.X - col * cw, y = p.Y - row * ch;
        return Math.Abs(x - cw / 2) < icon * 0.42 && Math.Abs(y - ((ch - icon) / 2 + icon * 0.42)) < icon * 0.5;
    }

    // Moves the lifted tile to a point while it scales to the given size, then runs done.
    private void Land(DragState d, Point center, double scale, double opacity, Action done)
    {
        var ghost = d.Ghost;
        double left = center.X - ghost.Width / 2, top = center.Y - ghost.Height / 2;
        var st = (ScaleTransform)ghost.RenderTransform;
        if (!Animate)
        {
            Canvas.SetLeft(ghost, left);
            Canvas.SetTop(ghost, top);
            st.ScaleX = st.ScaleY = scale;
            ghost.Opacity = opacity;
            done();
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(170);
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, dur) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, dur) { EasingFunction = ease });
        ghost.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(opacity, dur) { EasingFunction = ease });
        ghost.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(left, dur) { EasingFunction = ease });
        var landing = new DoubleAnimation(top, dur) { EasingFunction = ease };
        landing.Completed += (_, _) => done();
        ghost.BeginAnimation(Canvas.TopProperty, landing);
    }

    private Point HomeCenter(DragState d)
    {
        var s = d.Source;
        if (s is { IsLoaded: true, IsVisible: true })
            return s.TranslatePoint(new Point(s.ActualWidth / 2, s.ActualHeight / 2), DragLayer);
        return GhostCenter(d);
    }

    private static Point GhostCenter(DragState d) =>
        new(Canvas.GetLeft(d.Ghost) + d.Ghost.Width / 2, Canvas.GetTop(d.Ghost) + d.Ghost.Height / 2);

    private Point CellCenter(ItemsControl host, int index)
    {
        int cols = ColumnsFor(host);
        return host.TranslatePoint(new Point((index % cols + 0.5) * CellWidth, (index / cols + 0.5) * CellHeight), DragLayer);
    }

    private bool ShowingFolder => FolderLayer.Visibility == Visibility.Visible && ActiveFolder != null;
    private ItemsControl CurrentHost => ShowingFolder ? FolderGrid : TileGrid;
    private IList<ItemViewModel> CurrentItems => ShowingFolder ? ActiveFolder.Children : VM.CurrentPageItems;
    private double CellWidth => (double)Resources["TileCellWidth"];
    private double CellHeight => (double)Resources["TileCellHeight"];

    private int ColumnsFor(ItemsControl host) =>
        host == FolderGrid ? Math.Max(1, FindPanel<UniformGrid>(FolderGrid)?.Columns ?? 5) : Math.Max(1, VM.Settings.Columns);

    private void UpdateEdgePaging()
    {
        var d = _drag;
        int dir = 0;
        if (FolderLayer.Visibility != Visibility.Visible)
        {
            double x = d.Pointer.X;
            if (x < 36) dir = -1;
            else if (x > ActualWidth - 36) dir = 1;
        }
        if (dir == d.EdgeDir)
            return;
        d.EdgeDir = dir;
        _edgeTimer ??= CreateEdgeTimer();
        _edgeTimer.Stop();
        if (dir != 0)
            _edgeTimer.Start();
    }

    private DispatcherTimer CreateEdgeTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        t.Tick += (_, _) =>
        {
            if (_drag == null || _drag.EdgeDir == 0)
            {
                t.Stop();
                return;
            }
            // One empty page past the last lets a tile start a new page.
            int last = VM.PageCount - (VM.TopItems.Any(i => i.Model.Page == VM.PageCount - 1 && i != _drag.Item) ? 0 : 1);
            int page = Math.Clamp(VM.CurrentPage + _drag.EdgeDir, 0, Math.Max(0, last));
            if (page == VM.CurrentPage)
                return;
            GoToPage(page, allowNewPage: true);
            TileGrid.UpdateLayout();
            _drag.Glides.Clear();
            Preview();
        };
        return t;
    }

    private void DropTile(DragState drag)
    {
        var item = drag.Item;
        if (drag.FromFolder != null && !drag.InFolder)
        {
            // Out of a folder: it has closed, so the app lands on the board.
            var folder = drag.FromFolder;
            folder.Children.Remove(item);
            folder.Model.Items.Remove(item.Model);
            if (folder.Children.Count == 0)
                VM.TopItems.Remove(folder);
            else
                folder.LoadIcon();
            item.Model.Page = VM.CurrentPage;
            var vm = new ItemViewModel(item.Model, VM.Store, VM.IconCacheDir);
            vm.RefreshTarget();
            vm.LoadIcon();
            VM.TopItems.Add(vm);
            if (drag.Merge != null && VM.TopItems.Contains(drag.Merge))
            {
                MergeInto(vm, drag.Merge);
                return;
            }
            PlaceOnPage(vm, drag.InsertAt);
            return;
        }
        if (drag.FromFolder != null)
        {
            // Reorder inside the open folder.
            var kids = drag.FromFolder.Children;
            int from = kids.IndexOf(item);
            if (from < 0 || from == drag.InsertAt)
                return;
            kids.Move(from, drag.InsertAt);
            drag.FromFolder.Model.Items = kids.Select(k => k.Model).ToList();
            for (int i = 0; i < kids.Count; i++)
                kids[i].Model.GridIndex = i;
            drag.FromFolder.NotifyEdited();
            Commit();
            return;
        }
        if (drag.Merge != null)
        {
            MergeInto(item, drag.Merge);
            return;
        }
        PlaceOnPage(item, drag.InsertAt);
    }

    // App onto app makes a new folder in the target's place; app onto a
    // folder moves it inside.
    private void MergeInto(ItemViewModel item, ItemViewModel target)
    {
        if (target.IsFolder)
        {
            VM.TopItems.Remove(item);
            target.Model.Items ??= new List<ICItem>();
            item.Model.Page = 0;
            item.Model.GridIndex = target.Model.Items.Count;
            target.Model.Items.Add(item.Model);
            var child = new ItemViewModel(item.Model, VM.Store, VM.IconCacheDir, $"in {target.Name}");
            child.RefreshTarget();
            child.LoadIcon();
            target.Children.Add(child);
            target.LoadIcon();
            Commit();
            return;
        }
        var folderModel = new ICItem
        {
            IsFolder = true, Name = "New folder", Page = target.Model.Page, GridIndex = target.Model.GridIndex,
            Items = new List<ICItem> { target.Model, item.Model },
        };
        target.Model.Page = 0;
        target.Model.GridIndex = 0;
        item.Model.Page = 0;
        item.Model.GridIndex = 1;
        VM.TopItems.Remove(target);
        VM.TopItems.Remove(item);
        var folder = new ItemViewModel(folderModel, VM.Store, VM.IconCacheDir);
        folder.LoadIcon();
        VM.TopItems.Add(folder);
        Commit();
        // Open it with the name selected, as Launchpad does, so typing renames it.
        OpenFolder(folder);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            FolderTitle.Focus();
            FolderTitle.SelectAll();
        }), DispatcherPriority.Input);
    }

    // Moves a board tile to a cell on the current page. Other tiles on that
    // page shift to make room; a full page pushes its last tile onward.
    private void PlaceOnPage(ItemViewModel item, int slot)
    {
        int page = VM.CurrentPage;
        var onPage = VM.TopItems.Where(i => i.Model.Page == page && i != item)
            .OrderBy(i => i.Model.GridIndex).ToList();
        onPage.Insert(Math.Clamp(slot, 0, onPage.Count), item);
        for (int i = 0; i < onPage.Count; i++)
        {
            onPage[i].Model.Page = page;
            onPage[i].Model.GridIndex = i;
        }
        VM.EnforceCapacity();
        Commit();
    }

    private void Commit()
    {
        VM.RequestSave();
        VM.RecountPages();
        VM.RefreshPage();
        VM.RefreshSearch();
        Refresh();
    }

    // Files dropped from Explorer become tiles; internal tile drags never use OLE.
    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effects = e.Data.GetDataPresent(WpfDataFormats.FileDrop) ? WpfDragDropEffects.Copy : WpfDragDropEffects.None;
        e.Handled = true;
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
                catch (Exception ex)
                {
                    // A failed pin must never look like a successful no-op.
                    ShowNotice($"Could not add {Path.GetFileName(path)}", ex.Message);
                }
            }
        }
        VM.RequestSave();
        VM.RefreshPage();
        Refresh();
    }

    private static T FindPanel<T>(DependencyObject control) where T : DependencyObject
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
