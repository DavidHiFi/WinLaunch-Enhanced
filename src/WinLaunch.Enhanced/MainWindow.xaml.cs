using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using WinLaunch.Enhanced.Services;
using WinLaunch.Enhanced.ViewModels;
using WinLaunch.Legacy;
using Key = System.Windows.Input.Key;
using Brushes = System.Windows.Media.Brushes;

namespace WinLaunch.Enhanced;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ThemeCompat _compat;
    private readonly HotkeyService _hotkey = new();
    private readonly DispatcherTimer _cornerTimer;
    private WinForms.NotifyIcon _tray;
    private SettingsWindow _settings;
    private DateTime _cornerSince = DateTime.MaxValue;
    private bool _cornerArmed = true;

    public bool HotkeyActive { get; private set; }

    public MainWindow(MainViewModel vm, bool smoke = false)
    {
        _vm = vm;
        _compat = ThemeCompat.Load(Path.Combine(vm.Store.Directory, "CurrentTheme"));
        ThemeManager.Apply(ThemeManager.LoadThemeName(vm.Store), vm.Store, _compat);
        ThemeManager.ApplyPrefs(vm.Prefs);
        InitializeComponent();
        Board.Bind(vm);
        _cornerTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _cornerTimer.Tick += CornerTick;
        if (smoke)
            return;
        Board.OverlayShouldHide += () => Dispatcher.BeginInvoke(HideOverlay);
        Board.OpenSettings += OpenSettings;
        _hotkey.Pressed += () => Dispatcher.BeginInvoke(ToggleFromHotkey);
        PositionWindow();
        SetupTray();
        // A window that starts hidden (--background at sign-in) never gets an
        // HWND on its own, so SourceInitialized would never run and the hotkey
        // would never register. Create the handle now.
        new WindowInteropHelper(this).EnsureHandle();
    }

    public MainViewModel ViewModel => _vm;
    public ThemeCompat Compat => _compat;

    public void HideToTray()
    {
        Opacity = 0;
        Visibility = Visibility.Hidden;
    }

    private void PositionWindow()
    {
        try
        {
            var screens = WinForms.Screen.AllScreens;
            WinForms.Screen screen = screens.Length == 0 ? WinForms.Screen.PrimaryScreen :
                _vm.Settings.OpenOnActiveDesktop
                    ? WinForms.Screen.FromPoint(WinForms.Cursor.Position)
                    : screens[Math.Clamp(_vm.Settings.ScreenIndex, 0, screens.Length - 1)];
            var area = _vm.Settings.FillScreen ? screen.Bounds : screen.WorkingArea;
            // Screen coordinates are device pixels; WPF positions in DIPs.
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = area.Left / dpi.DpiScaleX;
            Top = area.Top / dpi.DpiScaleY;
            Width = area.Width / dpi.DpiScaleX;
            Height = area.Height / dpi.DpiScaleY;
        }
        catch { }
    }

    private void SetupTray()
    {
        _tray = new WinForms.NotifyIcon { Text = "WinLaunch Enhanced", Visible = true };
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(
                Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            if (icon != null)
                _tray.Icon = (System.Drawing.Icon)icon.Clone();
        }
        catch { _tray.Icon = System.Drawing.SystemIcons.Application; }
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                ShowOverlay();
            else if (e.Button == WinForms.MouseButtons.Right)
                ShowTrayMenu();
        };
    }

    // The tray menu uses the same themed WPF menu as the board instead of the
    // WinForms strip, so every menu in the app looks alike.
    private void ShowTrayMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
        };
        menu.Items.Add(MenuItem("Open WinLaunch", "", ShowOverlay, HotkeyText()));
        menu.Items.Add(MenuItem("Settings", "", OpenSettings));
        menu.Items.Add(MenuItem("Open data folder", "", () =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_vm.Store.Directory) { UseShellExecute = true }); }
            catch { }
        }));
        menu.Items.Add(new System.Windows.Controls.Separator());
        var quit = MenuItem("Quit", "", Close);
        quit.Tag = "danger";
        menu.Items.Add(quit);
        // A context menu needs a foreground window of ours to dismiss properly.
        var host = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, Width = 1, Height = 1, Topmost = true,
            Left = -10000, Top = -10000, ShowActivated = true,
        };
        host.Show();
        host.Activate();
        menu.Closed += (_, _) => host.Close();
        menu.IsOpen = true;
    }

    private static System.Windows.Controls.MenuItem MenuItem(string header, string glyph, Action click, string gesture = null)
    {
        var mi = new System.Windows.Controls.MenuItem { Header = header, Icon = glyph, InputGestureText = gesture };
        mi.Click += (_, _) => click();
        return mi;
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        ApplyComposition();
        RegisterHotkey();
        if (!HotkeyActive && _vm.Settings.HotKeyEnabled)
            _tray?.ShowBalloonTip(3000, "WinLaunch Enhanced",
                $"{HotkeyProblem()} Change it in Settings; the tray icon still works.",
                WinForms.ToolTipIcon.Warning);
        UpdateCorners();
    }

    public void RegisterHotkey()
    {
        _hotkey.Unregister();
        HotkeyActive = false;
        var s = _vm.Settings;
        var handle = new WindowInteropHelper(this).Handle;
        if (s.HotKeyEnabled)
            HotkeyActive = _hotkey.Register(handle, s.HotAlt, s.HotControl, s.HotShift, s.HotWin, s.HotKeyExtend);
    }

    // True only after Windows actually refused the shortcut; a window that
    // has not tried yet (offscreen previews) is not reported as a failure.
    public bool HotkeyFailed => !HotkeyActive && _hotkey.LastFailure != HotkeyFailure.None;

    public string HotkeyProblem() => _hotkey.LastFailure switch
    {
        HotkeyFailure.Taken => $"Another app already uses {HotkeyText()}.",
        HotkeyFailure.Invalid => $"{HotkeyText()} is not a valid shortcut.",
        _ => $"Windows refused {HotkeyText()}.",
    };

    public string HotkeyText() => HotkeyFormat.Describe(_vm.Settings);

    // Re-applies everything settings can change; called live by the settings window.
    public void ApplyAll()
    {
        ThemeManager.ApplyPrefs(_vm.Prefs);
        ApplyComposition();
        Board.ApplySettings();
        Board.RefreshPagePublic();
        if (Visibility == Visibility.Visible)
            PositionWindow();
        UpdateCorners();
    }

    private void ApplyComposition()
    {
        if (BackdropImage == null)
            return; // theme applied before InitializeComponent
        var mode = ThemeManager.CurrentBackdrop;
        var wall = mode == "Wallpaper" ? WallpaperBackdrop.GetBlurred(null, _vm.Prefs.Blur) : null;
        if (wall != null)
        {
            AcrylicHelper.ApplyComposition(this, "Solid", ThemeManager.IsDark());
            SetResourceReference(BackgroundProperty, "SolidBgBrush");
            BackdropImage.Source = wall;
            BackdropImage.Visibility = Visibility.Visible;
            BackdropScrim.Opacity = Math.Clamp(_vm.Prefs.Dim, 0, 0.9);
            BackdropScrim.Visibility = Visibility.Visible;
            return;
        }
        BackdropImage.Visibility = Visibility.Collapsed;
        BackdropScrim.Visibility = Visibility.Collapsed;
        AcrylicHelper.ApplyComposition(this, mode == "Wallpaper" ? "Solid" : mode, ThemeManager.IsDark());
        if (mode == "Solid" || mode == "Wallpaper")
            SetResourceReference(BackgroundProperty, "SolidBgBrush");
        else
            ThemeManager.ApplyScrim(this, _compat);
    }

    public void ReapplyComposition() => ApplyComposition();

    private void ToggleFromHotkey()
    {
        if (Visibility != Visibility.Visible && _vm.Settings.BlockOnFullscreen && FullscreenGuard.ForegroundIsFullscreen())
            return;
        ToggleOverlay();
    }

    public void ToggleOverlay()
    {
        if (Visibility == Visibility.Visible && Opacity > 0.5)
            HideOverlay();
        else
            ShowOverlay();
    }

    public void ShowOverlay()
    {
        PositionWindow();
        ApplyComposition();
        // Show(), not Visibility = Visible: the HWND exists from startup, and
        // WPF only treats the window as shown after Show().
        Show();
        try { Activate(); } catch (InvalidOperationException) { }
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140));
        BeginAnimation(OpacityProperty, fade);
        if (_vm.Prefs.OpenAnimation && SystemParameters.ClientAreaAnimation)
        {
            // Launchpad zoom: the board settles in from slightly larger.
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(220);
            BoardZoom.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(1.06, 1, dur) { EasingFunction = ease });
            BoardZoom.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(1.06, 1, dur) { EasingFunction = ease });
        }
        Board.FocusForOpen();
    }

    public void HideOverlay()
    {
        if (Visibility != Visibility.Visible)
            return;
        var anim = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(110));
        anim.Completed += (_, _) =>
        {
            if (Opacity < 0.05)
                Visibility = Visibility.Hidden;
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideOverlay();
            e.Handled = true;
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (Visibility == Visibility.Visible && Views.SpringboardView.ModalDepth == 0 && _settings == null)
            HideOverlay();
    }

    public void OpenSettings()
    {
        if (_settings != null)
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(this);
        if (Visibility == Visibility.Visible)
            _settings.Owner = this;
        else
            _settings.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _settings.Closed += (_, _) =>
        {
            _settings = null;
            if (Visibility == Visibility.Visible)
                Activate();
        };
        _settings.Show();
        _settings.Activate();
    }

    // ---- hot corners ----

    private void UpdateCorners()
    {
        var s = _vm.Settings;
        bool on = s.HotCornersEnabled && (s.HotTopLeft || s.HotTopRight || s.HotBottomLeft || s.HotBottomRight);
        if (on) _cornerTimer.Start(); else _cornerTimer.Stop();
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    private void CornerTick(object sender, EventArgs e)
    {
        if (!GetCursorPos(out var p))
            return;
        var s = _vm.Settings;
        bool inCorner = false;
        foreach (var scr in WinForms.Screen.AllScreens)
        {
            var b = scr.Bounds;
            bool l = p.X <= b.Left + 1, r = p.X >= b.Right - 2, t = p.Y <= b.Top + 1, bo = p.Y >= b.Bottom - 2;
            if ((s.HotTopLeft && l && t) || (s.HotTopRight && r && t) ||
                (s.HotBottomLeft && l && bo) || (s.HotBottomRight && r && bo))
            {
                inCorner = true;
                break;
            }
        }
        if (!inCorner)
        {
            _cornerArmed = true;
            _cornerSince = DateTime.MaxValue;
            return;
        }
        if (!_cornerArmed)
            return;
        if (_cornerSince == DateTime.MaxValue)
            _cornerSince = DateTime.UtcNow;
        if ((DateTime.UtcNow - _cornerSince).TotalSeconds >= Math.Max(0, s.HotCornerDelay))
        {
            _cornerArmed = false;
            if (Visibility != Visibility.Visible && !(s.BlockOnFullscreen && FullscreenGuard.ForegroundIsFullscreen()))
                ShowOverlay();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cornerTimer.Stop();
        try { _vm.SaveNow(); } catch { }
        _hotkey.Dispose();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnClosed(e);
        System.Windows.Application.Current.Shutdown();
    }
}
