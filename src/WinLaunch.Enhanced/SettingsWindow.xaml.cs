using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using WinLaunch.Enhanced.ViewModels;
using WinLaunch.Legacy;
using TextBox = System.Windows.Controls.TextBox;
using MessageBox = System.Windows.MessageBox;

namespace WinLaunch.Enhanced;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ThemeCompat _compat;

    public SettingsWindow(MainViewModel vm, ThemeCompat compat)
    {
        _vm = vm;
        _compat = compat;
        InitializeComponent();
        var s = vm.Settings;
        ColumnsBox.Text = s.Columns.ToString();
        RowsBox.Text = s.Rows.ToString();
        FolderColumnsBox.Text = s.FolderColumns.ToString();
        FolderRowsBox.Text = s.FolderRows.ToString();
        SearchBarBox.IsChecked = s.SearchBarVisible;
        PageDotsBox.IsChecked = s.PageIndicatorsVisible;
        SortAlphaBox.IsChecked = s.SortItemsAlphabetically;
        SortFoldersBox.IsChecked = s.SortFoldersFirst;
        FillScreenBox.IsChecked = s.FillScreen;
        ActiveDesktopBox.IsChecked = s.OpenOnActiveDesktop;
        ScreenBox.Text = s.ScreenIndex.ToString();
        HotkeyOnBox.IsChecked = s.HotKeyEnabled;
        CtrlBox.IsChecked = s.HotControl;
        AltBox.IsChecked = s.HotAlt;
        ShiftBox.IsChecked = s.HotShift;
        WinBox.IsChecked = s.HotWin;
        HotKeyBox.Text = s.HotKeyExtend;
        ThemeCombo.ItemsSource = Theme.Themes.All.Keys.ToList();
        ThemeCombo.SelectedItem = ThemeManager.CurrentTheme;
        BackdropCombo.ItemsSource = new List<string> { "Solid", "Acrylic", "Mica" };
        BackdropCombo.SelectedItem = ThemeManager.CurrentBackdrop;
        VersionText.Text = $"WinLaunch Enhanced 1.0.0. Data: {vm.Store.Directory}. " +
            $"Your original WinLaunch reads the same files.";
    }

    private static int Int(TextBox box, int fallback, int min, int max)
    {
        if (int.TryParse(box.Text, out int v))
            return Math.Clamp(v, min, max);
        return fallback;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _vm.Settings;
        s.Columns = Int(ColumnsBox, s.Columns, 1, 20);
        s.Rows = Int(RowsBox, s.Rows, 1, 20);
        s.FolderColumns = Int(FolderColumnsBox, s.FolderColumns, 1, 20);
        s.FolderRows = Int(FolderRowsBox, s.FolderRows, 1, 20);
        s.SearchBarVisible = SearchBarBox.IsChecked == true;
        s.PageIndicatorsVisible = PageDotsBox.IsChecked == true;
        s.SortItemsAlphabetically = SortAlphaBox.IsChecked == true;
        s.SortFoldersFirst = SortFoldersBox.IsChecked == true;
        s.FillScreen = FillScreenBox.IsChecked == true;
        s.OpenOnActiveDesktop = ActiveDesktopBox.IsChecked == true;
        s.ScreenIndex = Int(ScreenBox, s.ScreenIndex, 0, 8);
        s.HotKeyEnabled = HotkeyOnBox.IsChecked == true;
        s.HotControl = CtrlBox.IsChecked == true;
        s.HotAlt = AltBox.IsChecked == true;
        s.HotShift = ShiftBox.IsChecked == true;
        s.HotWin = WinBox.IsChecked == true;
        try
        {
            var key = (Key)new KeyConverter().ConvertFromString(HotKeyBox.Text.Trim());
            if (key == Key.None)
                throw new Exception();
            s.HotKeyExtend = key.ToString();
        }
        catch
        {
            MessageBox.Show($"\"{HotKeyBox.Text}\" is not a valid key. Keeping {s.HotKeyExtend}.",
                "Hotkey", MessageBoxButton.OK, MessageBoxImage.Warning);
            HotKeyBox.Text = s.HotKeyExtend;
        }
        _vm.SaveSettings();
        var newTheme = ThemeCombo.SelectedItem as string ?? ThemeManager.CurrentTheme;
        var newBackdrop = BackdropCombo.SelectedItem as string ?? ThemeManager.CurrentBackdrop;
        ThemeManager.Apply(newTheme, _vm.Store, _compat);
        ThemeManager.SaveBackdrop(_vm.Store, newBackdrop);
        (Owner as MainWindow)?.ReapplyComposition();
        _vm.Reload();
        Close();
    }

    private void Data_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_vm.Store.Directory) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not open folder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.SaveNow();
            MessageBox.Show($"Saved with backup to {_vm.Store.BackupDir}.",
                "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Backup failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }
}
