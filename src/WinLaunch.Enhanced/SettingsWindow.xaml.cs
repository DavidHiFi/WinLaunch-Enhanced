using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinLaunch.Enhanced.Services;
using WinLaunch.Enhanced.ViewModels;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace WinLaunch.Enhanced;

// Settings apply the moment they change, so the launcher behind this window
// previews every choice. Pages are built from a few row helpers to keep every
// row the same shape: title and hint on the left, the control on the right.
public partial class SettingsWindow : Window
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "WinLaunch Enhanced";

    private readonly MainWindow _main;
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _applyTimer;
    private bool _reloadPending;
    private bool _recording;
    private TextBlock _hotkeyText, _hotkeyStatus;
    private Border _hotkeyBox;
    private Action _updateChangedHandler;

    private static readonly (string Key, string Glyph, string Title, string Subtitle)[] Pages =
    {
        ("general", "", "General", "How the launcher opens and closes"),
        ("appearance", "", "Appearance", "Theme, background and text"),
        ("layout", "", "Layout", "Grid, icons and sorting"),
        ("search", "", "Search", "Ranking, calculator and usage history"),
        ("shortcut", "", "Shortcuts", "Keyboard shortcut and hot corners"),
        ("about", "", "About", "Version, data and backups"),
    };

    public SettingsWindow(MainWindow main)
    {
        _main = main;
        _vm = main.ViewModel;
        InitializeComponent();
        ResizeMode = ResizeMode.NoResize;
        _applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _applyTimer.Tick += (_, _) => ApplyNow();
        SidebarVersion.Text = $"Version {Version}";
        foreach (var p in Pages)
        {
            var nav = new RadioButton { Content = p.Title, Tag = p.Glyph, GroupName = "nav" };
            nav.SetResourceReference(StyleProperty, "NavButton");
            var key = p.Key;
            nav.Checked += (_, _) => ShowPage(key);
            Nav.Children.Add(nav);
        }
        ((RadioButton)Nav.Children[0]).IsChecked = true;
    }

    public void SelectPage(string key)
    {
        int i = Array.FindIndex(Pages, p => p.Key == key);
        if (i >= 0)
            ((RadioButton)Nav.Children[i]).IsChecked = true;
    }

    public static string[] PageKeys => Pages.Select(p => p.Key).ToArray();

    public static string Version =>
        typeof(SettingsWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "2.0.0";

    // ---- live apply ----

    private void Changed(bool reload = false)
    {
        _reloadPending |= reload;
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private void ApplyNow()
    {
        _applyTimer.Stop();
        try { _vm.SaveSettings(); } catch { }
        _vm.Prefs.Save();
        if (_reloadPending)
        {
            _reloadPending = false;
            _vm.Reload();
        }
        if (_vm.EnforceCapacity())
            _vm.RequestSave();
        _main.ApplyAll();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_updateChangedHandler != null)
        {
            if (_main.Updates != null)
                _main.Updates.Changed -= _updateChangedHandler;
            _updateChangedHandler = null;
        }
        if (_applyTimer.IsEnabled)
            ApplyNow();
        base.OnClosed(e);
    }

    // ---- chrome ----

    private void Drag_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.Primitives.ButtonBase)
            try { DragMove(); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_recording)
        {
            RecordKey(e);
            return;
        }
        if (e.Key == Key.Escape && Keyboard.FocusedElement is not TextBox)
        {
            Close();
            e.Handled = true;
        }
    }

    private void ShowPage(string key)
    {
        var page = Pages.First(p => p.Key == key);
        PageTitle.Text = page.Title;
        PageSubtitle.Text = page.Subtitle;
        _recording = false;
        if (_updateChangedHandler != null)
        {
            if (_main.Updates != null)
                _main.Updates.Changed -= _updateChangedHandler;
            _updateChangedHandler = null;
        }
        PageHost.Children.Clear();
        switch (key)
        {
            case "general": BuildGeneral(); break;
            case "appearance": BuildAppearance(); break;
            case "layout": BuildLayout(); break;
            case "search": BuildSearch(); break;
            case "shortcut": BuildShortcut(); break;
            case "about": BuildAbout(); break;
        }
        PageScroll.ScrollToTop();
    }

    // ---- pages ----

    private void BuildGeneral()
    {
        var s = _vm.Settings;
        var p = _vm.Prefs;
        var card = Section("Startup");
        Toggle(card, "Start with Windows", "Waits in the tray after you sign in, ready for your shortcut",
            IsAutostart(), SetAutostart);
        card = Section("Opening and closing");
        Toggle(card, "Close after opening an app", "Hide the launcher as soon as an app starts",
            p.CloseOnLaunch, v => { p.CloseOnLaunch = v; Changed(); });
        Toggle(card, "Click empty space to close", "Click between icons to hide the launcher, as in the original",
            p.HideOnEmptyClick, v => { p.HideOnEmptyClick = v; Changed(); });
        Toggle(card, "Zoom animation", "The board settles in when it opens",
            p.OpenAnimation, v => { p.OpenAnimation = v; Changed(); });
        card = Section("Screen");
        Toggle(card, "Open on the screen with the mouse", "Otherwise always use the screen below",
            s.OpenOnActiveDesktop, v => { s.OpenOnActiveDesktop = v; Changed(); });
        var screens = System.Windows.Forms.Screen.AllScreens;
        Choice(card, "Screen", "Used when the option above is off",
            screens.Select((sc, i) => $"{i + 1}{(sc.Primary ? " (main)" : "")}").ToArray(),
            Math.Clamp(s.ScreenIndex, 0, Math.Max(0, screens.Length - 1)),
            i => { s.ScreenIndex = i; Changed(); });
        Toggle(card, "Cover the taskbar", "Fill the whole screen instead of the work area",
            s.FillScreen, v => { s.FillScreen = v; Changed(); });
    }

    private void BuildAppearance()
    {
        var p = _vm.Prefs;
        var card = Section("Theme");
        card.Children.Add(ThemePicker());

        card = Section("Background");
        var modes = ThemeManager.Backdrops;
        var backdrop = ThemeManager.CurrentBackdrop;
        Choice(card, "Style", "Wallpaper and Blur are the original WinLaunch looks",
            modes, Array.IndexOf(modes, backdrop), i =>
            {
                ThemeManager.SaveBackdrop(_vm.Store, modes[i]);
                Changed();
                // Dim and Blur only apply to some styles, so rebuild the page in place.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var offset = PageScroll.VerticalOffset;
                    ShowPage("appearance");
                    PageScroll.ScrollToVerticalOffset(offset);
                }), DispatcherPriority.Background);
            });
        if (backdrop != "Solid")
            Slider(card, "Dim", "Darkens the background so names stay readable", 0, 0.8, p.Dim,
                100, "%", 0, 0.01, v => { p.Dim = Math.Round(v, 2); Changed(); });
        // Blur and Acrylic use the Windows blur, which apps cannot adjust.
        if (backdrop == "Wallpaper")
            Slider(card, "Blur", "How soft the wallpaper looks", 0, 60, p.Blur,
                1, "", 0, 1, v => { p.Blur = Math.Round(v); Changed(); });

        card = Section("Text");
        var fonts = ThemeManager.InstalledFonts();
        if (!fonts.Contains(p.FontFamily, StringComparer.OrdinalIgnoreCase))
            fonts.Insert(0, p.FontFamily);
        var preview = new TextBlock
        {
            Text = "Brave  Discord  Visual Studio Code  0123 -> != >=",
            FontSize = 15, Margin = new Thickness(0, 4, 0, 10), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        preview.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        Combo(card, "Font", "Used for every label, menu and this window", fonts,
            fonts.FindIndex(f => f.Equals(p.FontFamily, StringComparison.OrdinalIgnoreCase)),
            i => { p.FontFamily = fonts[i]; ThemeManager.ApplyPrefs(p); Changed(); },
            reset: () => EnhancedPrefs.DefaultFont);
        card.Children.Add(preview);
        Slider(card, "Text size", "Scales app names, search and folder titles", 0.8, 1.4, p.TextScale,
            100, "%", 0, 0.01, v => { p.TextScale = Math.Round(v, 2); Changed(); });
        Toggle(card, "Text shadow", "Keeps names readable on bright wallpapers",
            p.LabelShadow, v => { p.LabelShadow = v; Changed(); });
        Toggle(card, "Show app names", null, _vm.Settings.ShowItemIconText,
            v => { _vm.Settings.ShowItemIconText = v; Changed(); });
        Toggle(card, "Show folder names", null, _vm.Settings.ShowFolderIconText,
            v => { _vm.Settings.ShowFolderIconText = v; Changed(); });
    }

    private void BuildLayout()
    {
        var s = _vm.Settings;
        var card = Section("Grid");
        Slider(card, "Columns", "Apps per row", 4, 14, s.Columns, 1, "", 0, 1,
            v => { s.Columns = (int)Math.Round(v); Changed(); });
        Slider(card, "Rows", "Rows per page. A full page moves extra apps to the next page", 2, 8, s.Rows, 1, "", 0, 1,
            v => { s.Rows = (int)Math.Round(v); Changed(); });
        Slider(card, "Icon size", "Shrinks automatically if the grid gets tight", 0.8, 2.2, s.IconSize,
            100, "%", 0, 0.01, v => { s.IconSize = Math.Round(v, 2); Changed(); });
        Slider(card, "Folder columns", "Apps per row inside an open folder", 3, 12, s.FolderColumns, 1, "", 0, 1,
            v => { s.FolderColumns = (int)Math.Round(v); Changed(); });
        card = Section("Board");
        Toggle(card, "Search bar", null, s.SearchBarVisible, v => { s.SearchBarVisible = v; Changed(); });
        Toggle(card, "Page dots", "Shown when you have more than one page", s.PageIndicatorsVisible,
            v => { s.PageIndicatorsVisible = v; Changed(); });
        card = Section("Sorting");
        Toggle(card, "Sort alphabetically", "Off keeps the order you arranged", s.SortItemsAlphabetically,
            v => { s.SortItemsAlphabetically = v; Changed(reload: true); });
        Toggle(card, "Folders first", null, s.SortFoldersFirst,
            v => { s.SortFoldersFirst = v; Changed(reload: true); });
    }

    private void BuildSearch()
    {
        var p = _vm.Prefs;
        var card = Section("Results");
        Toggle(card, "Rank by most used", "Apps you open often come first", p.RankByUse,
            v => { p.RankByUse = v; Changed(); });
        Toggle(card, "Calculator", "Type 12*7 or (40+2)/3 and press Enter to copy the answer", p.Calculator,
            v => { p.Calculator = v; Changed(); });
        card = Section("Usage");
        int tracked = p.LaunchCounts.Count;
        ActionRow(card, "Reset usage history", $"{tracked} apps tracked, stored only on this PC", "Reset", () =>
        {
            p.LaunchCounts.Clear();
            p.Save();
            ShowPage("search");
        });
    }

    private void BuildShortcut()
    {
        var s = _vm.Settings;
        var card = Section("Keyboard shortcut");
        Toggle(card, "Global shortcut", "Opens and closes the launcher from any app", s.HotKeyEnabled,
            v => { s.HotKeyEnabled = v; _vm.SaveSettings(); _main.RegisterHotkey(); UpdateHotkeyStatus(); });
        var row = Row(card, "Shortcut", "Click, then press the keys you want");
        _hotkeyBox = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8), MinWidth = 200,
            BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand,
        };
        _hotkeyBox.SetResourceReference(Border.BackgroundProperty, "FieldBrush");
        _hotkeyBox.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        _hotkeyText = new TextBlock { Text = HotkeyFormat.Describe(s), HorizontalAlignment = HorizontalAlignment.Center };
        _hotkeyBox.Child = _hotkeyText;
        _hotkeyBox.MouseLeftButtonUp += (_, _) => StartRecording();
        row.Content = _hotkeyBox;
        _hotkeyStatus = new TextBlock { FontSize = 12, Margin = new Thickness(0, 2, 0, 8) };
        card.Children.Add(_hotkeyStatus);
        UpdateHotkeyStatus();
        Toggle(card, "Not over fullscreen apps", "The shortcut and hot corners do nothing while a game or video is fullscreen",
            s.BlockOnFullscreen, v => { s.BlockOnFullscreen = v; Changed(); });

        card = Section("Hot corners");
        Toggle(card, "Open by moving the mouse into a corner", null, s.HotCornersEnabled,
            v => { s.HotCornersEnabled = v; Changed(); });
        var corners = new[] { "Top left", "Top right", "Bottom left", "Bottom right" };
        int corner = s.HotTopLeft ? 0 : s.HotTopRight ? 1 : s.HotBottomLeft ? 2 : s.HotBottomRight ? 3 : 0;
        Choice(card, "Corner", null, corners, corner, i =>
        {
            s.HotTopLeft = i == 0; s.HotTopRight = i == 1; s.HotBottomLeft = i == 2; s.HotBottomRight = i == 3;
            Changed();
        });
        Slider(card, "Delay", "How long the pointer rests in the corner first, 0.0 s is instant", 0, 1.5, s.HotCornerDelay,
            1, " s", 1, 0.1, v => { s.HotCornerDelay = Math.Round(v, 1); Changed(); });

        card = Section("In the launcher");
        foreach (var (keys, what) in new[]
        {
            ("Type anything", "Search"), ("Enter", "Open the top result"),
            ("Ctrl + Shift + Enter", "Open the top result as administrator"),
            ("Esc", "Close the folder, search or launcher"), ("Right-click", "Options for an app, folder or the board"),
            ("PageUp / PageDown", "Change page"), ("Ctrl + E", "Edit layout"), ("Ctrl + N", "New folder"),
            ("F2 / Del", "Rename or remove the focused app"), ("Ctrl + Z", "Undo a removal"), ("Ctrl + ,", "Settings"),
        })
        {
            var r = Row(card, what, null);
            r.Content = Keycap(keys);
        }
    }

    // ---- updates ----

    // The Updates card: status, a manual check, download and install, and the
    // two update options. Status refreshes live while a check or download runs.
    private void BuildUpdates()
    {
        var updates = _main.Updates;
        if (updates == null)
            return;
        var p = _vm.Prefs;
        var card = Section("Updates");
        var statusHost = Row(card, "Status", null);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        statusHost.Content = status;
        var checkHost = Row(card, "Check for updates", "Compares this copy with the newest release on GitHub");
        var checkButton = new Button { Content = "Check now", MinWidth = 96 };
        checkButton.SetResourceReference(StyleProperty, "SecondaryButton");
        checkButton.Click += (_, _) => { _ = updates.CheckAsync(); };
        checkHost.Content = checkButton;
        var installHost = Row(card, "New version", null);
        var installButton = new Button { MinWidth = 96 };
        installButton.SetResourceReference(StyleProperty, "SecondaryButton");
        installButton.Click += (_, _) =>
        {
            if (updates.Status == UpdateStatus.Downloading)
            {
                updates.CancelDownload();
                return;
            }
            if (UpdateService.IsInstalled && updates.Available?.Installer != null)
                _ = updates.InstallAsync();
            else
                Open(updates.Available?.PageUrl ?? UpdateRules.ReleasesPage);
        };
        installHost.Content = installButton;
        Toggle(card, "Check for updates on startup", "Quietly asks GitHub for the newest release when WinLaunch starts",
            p.CheckForUpdates, v =>
            {
                p.CheckForUpdates = v;
                Changed();
                if (v)
                    _ = updates.CheckAsync();
            });
        Toggle(card, "Include preview releases", "Offer betas as well as finished releases",
            p.IncludePreReleases, v => { p.IncludePreReleases = v; Changed(); });

        string Checked() => updates.LastChecked is { } t
            ? $"Last checked {t.ToLocalTime():yyyy-MM-dd h:mm tt}."
            : "Never checked.";
        void Refresh()
        {
            checkButton.IsEnabled = !updates.Busy;
            checkButton.Content = updates.Status == UpdateStatus.Checking ? "Checking…" : "Check now";
            var release = updates.Available;
            switch (updates.Status)
            {
                case UpdateStatus.Checking:
                    status.Text = "Checking GitHub for the newest release…";
                    installHost.Content = Muted("Nothing to install yet.");
                    break;
                case UpdateStatus.Downloading when release != null:
                    status.Text = $"Downloading version {release.Version}… {(int)(updates.Progress * 100)}%";
                    installButton.Content = "Cancel";
                    installHost.Content = installButton;
                    break;
                case UpdateStatus.Available when release != null:
                    status.Text = $"Version {release.Version} is available (released {release.PublishedUtc:yyyy-MM-dd}). {Checked()}";
                    if (UpdateService.IsInstalled && release.Installer != null)
                    {
                        installButton.Content = "Download and install";
                    }
                    else
                    {
                        status.Text += UpdateService.IsInstalled
                            ? " This release has no checksum, so open it in the browser."
                            : " This copy is portable, so the update opens in the browser.";
                        installButton.Content = "View release…";
                    }
                    installHost.Content = installButton;
                    break;
                case UpdateStatus.Failed:
                    status.Text = updates.Message ?? "The check failed.";
                    installHost.Content = Muted("Nothing to install.");
                    break;
                case UpdateStatus.UpToDate:
                    status.Text = $"You have the newest version ({UpdateService.Current}). {Checked()}";
                    installHost.Content = Muted("Nothing to install.");
                    break;
                default:
                    if (release != null)
                    {
                        status.Text = $"Version {release.Version} is available.";
                        installButton.Content = UpdateService.IsInstalled && release.Installer != null
                            ? "Download and install" : "View release…";
                        installHost.Content = installButton;
                    }
                    else
                    {
                        status.Text = $"Version {UpdateService.Current}. {Checked()}";
                        installHost.Content = Muted("Nothing to install.");
                    }
                    break;
            }
        }
        _updateChangedHandler = () => Dispatcher.BeginInvoke(Refresh);
        updates.Changed += _updateChangedHandler;
        Refresh();
    }

    private void BuildAbout()
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 18) };
        head.Children.Add(new System.Windows.Controls.Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/WinLaunchEnhanced;component/Assets/logo-256.png")),
            Width = 72, Height = 72,
        });
        var text = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = "WinLaunch Enhanced", FontSize = 20, FontWeight = FontWeights.SemiBold });
        text.Children.Add(Muted($"Version {Version}. A rewrite of WinLaunch for Windows 11."));
        text.Children.Add(Muted("MIT license. Original WinLaunch (c) 2022 MrC0rrupted."));
        head.Children.Add(text);
        PageHost.Children.Add(head);

        BuildUpdates();

        var card = Section("Your data");
        ActionRow(card, "Data folder", _vm.Store.Directory, "Open", () => Open(_vm.Store.Directory));
        ActionRow(card, "Back up now", $"Keeps the last 10 copies in {System.IO.Path.GetFileName(_vm.Store.BackupDir)}", "Back up", () =>
        {
            _vm.SaveNow();
            Toast("Backup saved");
        });
        ActionRow(card, "Export backup…", "Saves your apps, folders, layout and look to one .WLbackup file", "Export…",
            ExportBackup);
        ActionRow(card, "Import backup…", "Replaces this setup with a .WLbackup file. Your current setup is saved first", "Import…",
            ImportBackup);
        ActionRow(card, "Reload apps", "Re-reads your apps after editing files by hand", "Reload", () =>
        {
            _vm.Reload();
            _main.ApplyAll();
            Toast("Reloaded");
        });
        ConfirmRow(card, "Clear all icons", "Removes every app and folder from the launcher. Your programs and files are not touched, and the old layout is kept in the backup folder",
            "Clear…", "Are you sure?", "Yes, clear", () =>
            {
                _vm.ClearAll();
                _main.ApplyAll();
                Toast("Cleared. The previous layout is in the backup folder");
            });
        card = Section("Project");
        ActionRow(card, "Source code and releases", "github.com/DavidHiFi/WinLaunch-Enhanced", "Open",
            () => Open("https://github.com/DavidHiFi/WinLaunch-Enhanced"));
        ActionRow(card, "Report a problem", null, "Open",
            () => Open("https://github.com/DavidHiFi/WinLaunch-Enhanced/issues"));
        card = Section("Quit");
        ActionRow(card, "Quit WinLaunch Enhanced", "The shortcut stops working until you open the app again", "Quit",
            () => System.Windows.Application.Current.MainWindow?.Close(), danger: true);
    }

    // ---- theme picker ----

    private UIElement ThemePicker()
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
        foreach (var t in Theme.Themes.All.Values)
        {
            var name = t.Name;
            var swatch = new Grid { Width = 128, Height = 64 };
            swatch.Children.Add(new Border { Background = new SolidColorBrush(Theme.Themes.C(t.Bg)), CornerRadius = new CornerRadius(8) });
            var inner = new StackPanel { Margin = new Thickness(10), VerticalAlignment = VerticalAlignment.Center };
            inner.Children.Add(new Border { Height = 8, Width = 70, HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Theme.Themes.C(t.Text)) });
            var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var c in new[] { t.Card, t.Field, t.Accent, t.Warn })
                dots.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
                    Margin = new Thickness(0, 0, 5, 0), Background = new SolidColorBrush(Theme.Themes.C(c)) });
            inner.Children.Add(dots);
            swatch.Children.Add(inner);
            var label = new TextBlock { Text = name, FontSize = 12, Margin = new Thickness(2, 7, 0, 0) };
            var content = new StackPanel();
            content.Children.Add(swatch);
            content.Children.Add(label);
            var rb = new RadioButton
            {
                GroupName = "theme", Content = content, Margin = new Thickness(0, 0, 10, 10),
                IsChecked = name == ThemeManager.CurrentTheme, Cursor = System.Windows.Input.Cursors.Hand,
                FocusVisualStyle = null, ToolTip = name,
            };
            rb.Template = SwatchTemplate();
            rb.Checked += (_, _) =>
            {
                ThemeManager.Apply(name, _vm.Store, _main.Compat);
                Changed();
            };
            wrap.Children.Add(rb);
        }
        return wrap;
    }

    private static ControlTemplate _swatchTemplate;

    // Ring around the swatch: accent when chosen, subtle on hover.
    private static ControlTemplate SwatchTemplate()
    {
        if (_swatchTemplate != null)
            return _swatchTemplate;
        const string xaml = @"<ControlTemplate TargetType='RadioButton'
            xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
            xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
          <Border x:Name='Ring' BorderThickness='2' BorderBrush='Transparent' CornerRadius='11' Padding='3'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Ring' Property='BorderBrush' Value='{DynamicResource CardBorderBrush}'/>
            </Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='Ring' Property='BorderBrush' Value='{DynamicResource MutedBrush}'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Ring' Property='BorderBrush' Value='{DynamicResource AccentBrush}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>";
        _swatchTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        return _swatchTemplate;
    }

    // ---- hotkey recorder ----

    private void StartRecording()
    {
        _recording = true;
        _hotkeyText.Text = "Press your shortcut…";
        _hotkeyBox.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        _hotkeyBox.Focusable = true;
        Keyboard.Focus(_hotkeyBox);
    }

    private void RecordKey(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }
        if (HotkeyFormat.IsModifier(key))
            return;
        var mods = Keyboard.Modifiers;
        bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        if (mods == ModifierKeys.None && !win)
        {
            _hotkeyText.Text = "Add Ctrl, Alt, Shift or Win";
            return;
        }
        var s = _vm.Settings;
        var old = (s.HotControl, s.HotAlt, s.HotShift, s.HotWin, s.HotKeyExtend);
        s.HotControl = mods.HasFlag(ModifierKeys.Control);
        s.HotAlt = mods.HasFlag(ModifierKeys.Alt);
        s.HotShift = mods.HasFlag(ModifierKeys.Shift);
        s.HotWin = win || mods.HasFlag(ModifierKeys.Windows);
        s.HotKeyExtend = key.ToString();
        s.HotKeyEnabled = true;
        _main.RegisterHotkey();
        if (!_main.HotkeyActive)
        {
            var why = _main.HotkeyProblem();
            (s.HotControl, s.HotAlt, s.HotShift, s.HotWin, s.HotKeyExtend) = old;
            _main.RegisterHotkey();
            StopRecording();
            _hotkeyStatus.Text = $"{why} Kept {HotkeyFormat.Describe(s)}.";
            _hotkeyStatus.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
            return;
        }
        _vm.SaveSettings();
        StopRecording();
    }

    private void StopRecording()
    {
        _recording = false;
        _hotkeyText.Text = HotkeyFormat.Describe(_vm.Settings);
        _hotkeyBox.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        UpdateHotkeyStatus();
    }

    private void UpdateHotkeyStatus()
    {
        if (_hotkeyStatus == null)
            return;
        if (!_vm.Settings.HotKeyEnabled)
        {
            _hotkeyStatus.Text = "Off. Use the tray icon or the Start menu shortcut to open the launcher.";
            _hotkeyStatus.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        }
        else if (!_main.HotkeyFailed)
        {
            _hotkeyStatus.Text = $"✓ {HotkeyFormat.Describe(_vm.Settings)} is ready";
            _hotkeyStatus.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        }
        else
        {
            _hotkeyStatus.Text = $"{_main.HotkeyProblem()} Pick a different one.";
            _hotkeyStatus.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
        }
    }

    // ---- autostart ----

    private static bool IsAutostart()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunValue) is string;
        }
        catch { return false; }
    }

    private static void SetAutostart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on)
                k.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --background");
            else
                k.DeleteValue(RunValue, false);
        }
        catch { }
    }

    // ---- row helpers ----

    private StackPanel Section(string title)
    {
        PageHost.Children.Add(new TextBlock
        {
            Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, PageHost.Children.Count == 0 ? 4 : 22, 0, 8),
        });
        var panel = new StackPanel();
        var card = new Border
        {
            Child = panel, Padding = new Thickness(18, 4, 18, 4),
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        PageHost.Children.Add(card);
        return panel;
    }

    private static TextBlock Muted(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return t;
    }

    private static void Divider(StackPanel card)
    {
        if (card.Children.Count == 0)
            return;
        var line = new Border { Height = 1, Margin = new Thickness(0, 2, 0, 2), Opacity = 0.7 };
        line.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
        card.Children.Add(line);
    }

    private static ContentControl Row(StackPanel card, string title, string hint)
    {
        Divider(card);
        var grid = new Grid { MinHeight = 52 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 20, 8) };
        left.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(hint))
            left.Children.Add(Muted(hint));
        grid.Children.Add(left);
        var host = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Focusable = false };
        Grid.SetColumn(host, 1);
        grid.Children.Add(host);
        card.Children.Add(grid);
        return host;
    }

    private static void Toggle(StackPanel card, string title, string hint, bool value, Action<bool> set)
    {
        Divider(card);
        var content = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        content.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(hint))
            content.Children.Add(Muted(hint));
        var cb = new CheckBox { Content = content, IsChecked = value, MinHeight = 52 };
        cb.SetResourceReference(StyleProperty, "ToggleRow");
        cb.Checked += (_, _) => set(true);
        cb.Unchecked += (_, _) => set(false);
        card.Children.Add(cb);
    }

    // Drag, use the arrow keys, or click the number and type one. Enter applies, Esc reverts.
    // scale turns the stored value into the number shown, so 0.75 reads as 75 with suffix %.
    // Values snap to step, so 1 % and 1 px are the smallest moves, never 2 %.
    private static void Slider(StackPanel card, string title, string hint, double min, double max, double value,
        double scale, string suffix, int decimals, double step, Action<double> set)
    {
        var host = Row(card, title, hint);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var slider = new System.Windows.Controls.Slider
        {
            Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 220,
            SmallChange = step, LargeChange = step * 5, VerticalAlignment = VerticalAlignment.Center,
        };
        slider.SetResourceReference(StyleProperty, "FlatSlider");
        string Format(double v) =>
            (v * scale).ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), CultureInfo.InvariantCulture) + suffix;
        var box = new TextBox
        {
            Text = Format(slider.Value), Width = 74, TextAlignment = TextAlignment.Right, MaxLength = 8,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(6, 3, 8, 3), VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.IBeam,
        };
        box.SetResourceReference(TextBox.ForegroundProperty, "TextBrush");
        box.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
        var field = new Border
        {
            CornerRadius = new CornerRadius(8), Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Child = box,
        };
        field.SetResourceReference(Border.BackgroundProperty, "FieldBrush");
        void Commit()
        {
            var text = box.Text.Trim().TrimEnd('%', 's', ' ').Replace(',', '.');
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var typed))
                slider.Value = Math.Clamp(typed / scale, min, max);
            box.Text = Format(slider.Value);
        }
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                box.Text = Format(slider.Value);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        };
        slider.ValueChanged += (_, e) =>
        {
            double v = Math.Round(e.NewValue / step, MidpointRounding.AwayFromZero) * step;
            if (Math.Abs(v - e.NewValue) > 1e-9)
            {
                slider.Value = v;
                return;
            }
            if (!box.IsKeyboardFocusWithin)
                box.Text = Format(v);
            set(v);
        };
        panel.Children.Add(slider);
        panel.Children.Add(field);
        host.Content = panel;
    }

    private static void Choice(StackPanel card, string title, string hint, string[] options, int selected, Action<int> set)
    {
        var host = Row(card, title, hint);
        var pill = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(2) };
        pill.SetResourceReference(Border.BackgroundProperty, "FieldBrush");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var group = Guid.NewGuid().ToString("N");
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var rb = new RadioButton { Content = options[i], GroupName = group, IsChecked = i == selected };
            rb.SetResourceReference(StyleProperty, "Segment");
            rb.Checked += (_, _) => set(index);
            panel.Children.Add(rb);
        }
        pill.Child = panel;
        host.Content = pill;
    }

    private static void Combo(StackPanel card, string title, string hint, List<string> items, int selected,
        Action<int> set, Func<string> reset = null)
    {
        var host = Row(card, title, hint);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var combo = new ComboBox { ItemsSource = items, SelectedIndex = selected, Width = 240 };
        combo.SetResourceReference(StyleProperty, "ThemeCombo");
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0)
                set(combo.SelectedIndex);
        };
        panel.Children.Add(combo);
        if (reset != null)
        {
            var b = new Button { Content = "", ToolTip = "Back to the default font", Margin = new Thickness(6, 0, 0, 0) };
            b.SetResourceReference(StyleProperty, "GlassIconButton");
            b.Click += (_, _) => combo.SelectedIndex = items.FindIndex(f => f.Equals(reset(), StringComparison.OrdinalIgnoreCase));
            panel.Children.Add(b);
        }
        host.Content = panel;
    }

    private void ActionRow(StackPanel card, string title, string hint, string action, Action click, bool danger = false)
    {
        var host = Row(card, title, hint);
        var b = new Button { Content = action, MinWidth = 96 };
        b.SetResourceReference(StyleProperty, "SecondaryButton");
        if (danger)
            b.SetResourceReference(ForegroundProperty, "WarnBrush");
        b.Click += (_, _) => click();
        host.Content = b;
    }

    private static UIElement Keycap(string keys)
    {
        var b = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 10, 4),
            BorderThickness = new Thickness(1, 1, 1, 2) };
        b.SetResourceReference(Border.BackgroundProperty, "FieldBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        b.Child = new TextBlock { Text = keys, FontSize = 12 };
        return b;
    }

    private void Toast(string text)
    {
        PageSubtitle.Text = "✓ " + text;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            var page = Pages.FirstOrDefault(p => p.Title == PageTitle.Text);
            PageSubtitle.Text = page.Subtitle;
        };
        t.Start();
    }

    // A destructive action: the first click asks, the second one does it.
    private static void ConfirmRow(StackPanel card, string title, string hint, string action, string question,
        string confirm, Action run)
    {
        var host = Row(card, title, hint);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var ask = new Button { Content = action, MinWidth = 96 };
        ask.SetResourceReference(StyleProperty, "SecondaryButton");
        ask.SetResourceReference(ForegroundProperty, "WarnBrush");
        var prompt = new TextBlock { Text = question, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var yes = new Button { Content = confirm, MinWidth = 120 };
        yes.SetResourceReference(StyleProperty, "SecondaryButton");
        yes.SetResourceReference(ForegroundProperty, "WarnBrush");
        var no = new Button { Content = "Cancel", MinWidth = 88, Margin = new Thickness(8, 0, 0, 0) };
        no.SetResourceReference(StyleProperty, "SecondaryButton");
        prompt.Visibility = yes.Visibility = no.Visibility = Visibility.Collapsed;
        panel.Children.Add(prompt);
        panel.Children.Add(yes);
        panel.Children.Add(no);
        panel.Children.Add(ask);
        host.Content = panel;
        void Reset()
        {
            ask.Visibility = Visibility.Visible;
            prompt.Visibility = yes.Visibility = no.Visibility = Visibility.Collapsed;
        }
        ask.Click += (_, _) =>
        {
            ask.Visibility = Visibility.Collapsed;
            prompt.Visibility = yes.Visibility = no.Visibility = Visibility.Visible;
        };
        no.Click += (_, _) => Reset();
        yes.Click += (_, _) =>
        {
            Reset();
            run();
        };
    }

    private void ExportBackup()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export WinLaunch backup",
            Filter = $"WinLaunch backup (*{BackupService.Extension})|*{BackupService.Extension}",
            FileName = $"WinLaunch-{DateTime.Now:yyyy-MM-dd}{BackupService.Extension}",
            AddExtension = true,
        };
        if (dlg.ShowDialog(this) != true)
            return;
        try
        {
            _vm.SaveNow();
            _vm.SaveSettings();
            _vm.Prefs.Save();
            BackupService.Export(_vm.Store.Directory, dlg.FileName);
            Toast($"Exported {System.IO.Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportBackup()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import WinLaunch backup",
            Filter = $"WinLaunch backup (*{BackupService.Extension})|*{BackupService.Extension}",
        };
        if (dlg.ShowDialog(this) != true)
            return;
        var name = System.IO.Path.GetFileName(dlg.FileName);
        if (MessageBox.Show(this,
                $"Replace your apps, folders and settings with {name}?\n\nYour current setup is saved to the backup folder first. WinLaunch restarts afterwards.",
                "Import backup", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            _applyTimer.Stop();
            _reloadPending = false;
            BackupService.Import(dlg.FileName, _vm.Store.Directory, _vm.Store.BackupDir);
            MainWindow.SkipSaveOnClose = true;
            RestartApp();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // A fresh copy starts once this one has exited, which releases the single-instance lock.
    private void RestartApp()
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 4 >nul & start \"\" \"{exe}\" --background")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        System.Windows.Application.Current.MainWindow?.Close();
    }

    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { }
    }
}
