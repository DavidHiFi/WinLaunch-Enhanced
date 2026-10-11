using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using WinForms = System.Windows.Forms;
using WinLaunch.Enhanced.Services;
using WinLaunch.Enhanced.ViewModels;
using WinLaunch.Legacy;
using Key = System.Windows.Input.Key;

namespace WinLaunch.Enhanced;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ThemeCompat _compat;
    private readonly HotkeyService _hotkey = new();
    private WinForms.NotifyIcon _tray;
    private bool _hotkeyActive;

    public MainWindow(MainViewModel vm, bool smoke = false)
    {
        _vm = vm;
        _compat = ThemeCompat.Load(Path.Combine(vm.Store.Directory, "CurrentTheme"));
        ThemeManager.Apply(ThemeManager.LoadMode(vm.Store), vm.Store, _compat);
        InitializeComponent();
        Board.Bind(vm);
        if (smoke)
            return;
        Board.OverlayShouldHide += () => Dispatcher.BeginInvoke(HideOverlay);
        Board.OpenSettings += OpenSettings;
        PositionWindow();
        SetupTray();
    }

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
            Left = area.Left;
            Top = area.Top;
            Width = area.Width;
            Height = area.Height;
        }
        catch { }
    }

    private void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Text = "WinLaunch Enhanced",
            Visible = true,
        };
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(
                Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            if (icon != null)
                _tray.Icon = (System.Drawing.Icon)icon.Clone();
        }
        catch { _tray.Icon = System.Drawing.SystemIcons.Application; }
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowOverlay());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("Open data folder", null, (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_vm.Store.Directory) { UseShellExecute = true }); }
            catch { }
        });
        menu.Items.Add("Exit", null, (_, _) => Close());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowOverlay();
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        if (_compat.WantBlur)
            AcrylicHelper.EnableAcrylic(this);
        ThemeManager.ApplyScrim(this, _compat);
        var s = _vm.Settings;
        if (s.HotKeyEnabled)
        {
            var handle = new WindowInteropHelper(this).Handle;
            _hotkeyActive = _hotkey.Register(handle, s.HotAlt, s.HotControl, s.HotShift, s.HotWin, s.HotKeyExtend);
            _hotkey.Pressed += () => Dispatcher.BeginInvoke(ToggleOverlay);
        }
        if (!_hotkeyActive && s.HotKeyEnabled)
            _tray?.ShowBalloonTip(3000, "WinLaunch Enhanced",
                $"Hotkey {DescribeHotkey()} could not be registered. The tray icon still works.",
                WinForms.ToolTipIcon.Warning);
    }

    private string DescribeHotkey()
    {
        var s = _vm.Settings;
        var parts = new List<string>();
        if (s.HotControl) parts.Add("Ctrl");
        if (s.HotAlt) parts.Add("Alt");
        if (s.HotShift) parts.Add("Shift");
        if (s.HotWin) parts.Add("Win");
        parts.Add(s.HotKeyExtend);
        return string.Join("+", parts);
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
        Visibility = Visibility.Visible;
        Activate();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
            new Duration(TimeSpan.FromMilliseconds(120))) { EasingFunction = null });
        Board.FocusForOpen();
    }

    public void HideOverlay()
    {
        var anim = new DoubleAnimation(Opacity, 0, new Duration(TimeSpan.FromMilliseconds(100)));
        anim.Completed += (_, _) => Visibility = Visibility.Hidden;
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
        if (Visibility == Visibility.Visible)
            HideOverlay();
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow(_vm, _compat) { Owner = this };
        win.ShowDialog();
        Board.ApplySettings();
        Board.RefreshPagePublic();
        PositionWindow();
        _hotkey.Unregister();
        _hotkeyActive = false;
        var s = _vm.Settings;
        if (s.HotKeyEnabled)
        {
            _hotkeyActive = _hotkey.Register(new WindowInteropHelper(this).Handle,
                s.HotAlt, s.HotControl, s.HotShift, s.HotWin, s.HotKeyExtend);
            _hotkey.Pressed += () => Dispatcher.BeginInvoke(ToggleOverlay);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try { _vm.SaveNow(); } catch { }
        _hotkey.Dispose();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnClosed(e);
    }
}
