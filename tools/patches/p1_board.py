import os
os.chdir(os.path.join(os.path.dirname(__file__), "..", "..", "src", "WinLaunch.Enhanced"))

def patch(path, pairs):
    s = open(path, encoding="utf-8").read()
    for old, new in pairs:
        if old not in s:
            raise SystemExit(f"NOT FOUND in {path}: {old[:70]!r}")
        s = s.replace(old, new)
    open(path, "w", encoding="utf-8").write(s)

patch("Views/SpringboardView.xaml", [
('''      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center">''',
'''      <!-- Calculator: typing 12*7 shows the answer beside the search pill. -->
      <Border x:Name="CalcChip" Visibility="Collapsed" HorizontalAlignment="Center" Margin="470,0,0,0"
              Height="36" CornerRadius="18" Padding="16,0" Cursor="Hand"
              Background="{DynamicResource AccentBrush}" MouseLeftButtonUp="CalcChip_Click"
              ToolTip="Enter copies the result">
        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
          <TextBlock Text="=" FontSize="14" Foreground="{DynamicResource AccentInkBrush}" Opacity="0.7" Margin="0,0,8,0"/>
          <TextBlock x:Name="CalcText" FontSize="14" FontWeight="SemiBold" Foreground="{DynamicResource AccentInkBrush}"/>
        </StackPanel>
      </Border>
      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center">'''),
('''      <TextBlock x:Name="NoResults" Text="No results" FontSize="22"
                 Foreground="{DynamicResource LabelBrush}" Opacity="0.7"
                 HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>''',
'''      <StackPanel x:Name="NoResults" HorizontalAlignment="Center" VerticalAlignment="Center"
                  Visibility="Collapsed">
        <TextBlock Text="No apps match" FontSize="22" TextAlignment="Center" Opacity="0.75"
                   Foreground="{DynamicResource LabelBrush}"/>
        <Button x:Name="WebSearchButton" Style="{DynamicResource GlassPillButton}" Margin="0,18,0,0"
                HorizontalAlignment="Center" Click="WebSearch_Click">
          <StackPanel Orientation="Horizontal">
            <TextBlock Text="&#xE774;" FontFamily="Segoe Fluent Icons, Segoe MDL2 Assets" FontSize="14"
                       VerticalAlignment="Center" Margin="0,0,10,0"/>
            <TextBlock x:Name="WebSearchText" VerticalAlignment="Center"/>
            <TextBlock Text="Enter" FontSize="11" Opacity="0.6" VerticalAlignment="Center" Margin="14,0,0,0"/>
          </StackPanel>
        </Button>
      </StackPanel>'''),
])

patch("Views/SpringboardView.xaml.cs", [
('Resources["TileFontSize"] = Math.Clamp(Math.Round(11.5 + icon / 40, 1), 12, 15);',
 'Resources["TileFontSize"] = Math.Round(Math.Clamp(11.5 + icon / 40, 12, 15) * ThemeManager.TextScale, 1);'),
('''            LauncherService.Launch(item.Model, VM.Store, asAdmin);
            OverlayShouldHide?.Invoke();''',
'''            LauncherService.Launch(item.Model, VM.Store, asAdmin);
            VM.Prefs.CountLaunch(MainViewModel.UseKey(item));
            if (VM.Prefs.CloseOnLaunch)
                OverlayShouldHide?.Invoke();'''),
('''        if (VM.EditMode)
            SetEditMode(false);
        else
            OverlayShouldHide?.Invoke();''',
'''        if (VM.EditMode)
            SetEditMode(false);
        else if (FolderLayer.Visibility != Visibility.Visible && VM.Prefs.HideOnEmptyClick)
            OverlayShouldHide?.Invoke();'''),
('''    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchChrome();
        VM.SearchText = SearchBox.Text;
        VM.RefreshSearch();
        Refresh();
    }''',
'''    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchChrome();
        VM.SearchText = SearchBox.Text;
        VM.RefreshSearch();
        var calc = VM.Prefs.Calculator ? Calculator.TryEvaluate(SearchBox.Text) : null;
        CalcText.Text = calc ?? "";
        CalcChip.Visibility = calc != null ? Visibility.Visible : Visibility.Collapsed;
        WebSearchText.Text = $"Search the web for \\u201C{SearchBox.Text.Trim()}\\u201D";
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
    }'''),
('''        if (e.Key == Key.Enter && inSearch && VM.SearchResults.Count > 0)
        {''',
'''        if (e.Key == Key.Enter && inSearch && CalcChip.Visibility == Visibility.Visible)
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
        {'''),
('''        var menu = new ContextMenu();
        menu.Items.Add(Item("Add apps…", () => Add_Click(null, null)));
        menu.Items.Add(Item("New folder", NewFolder, "Ctrl+N"));
        menu.Items.Add(Item(VM.EditMode ? "Done editing" : "Edit layout", () => SetEditMode(!VM.EditMode), "Ctrl+E"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Settings", () => OpenSettings?.Invoke(), "Ctrl+,"));
        menu.Items.Add(Item("Hide", () => OverlayShouldHide?.Invoke(), "Esc"));
        return menu;''',
'''        var menu = new ContextMenu();
        menu.Items.Add(Item("Add apps…", () => Add_Click(null, null), null, "\\uE710"));
        menu.Items.Add(Item("New folder", NewFolder, "Ctrl+N", "\\uE8F4"));
        menu.Items.Add(Item(VM.EditMode ? "Done editing" : "Edit layout", () => SetEditMode(!VM.EditMode), "Ctrl+E",
            VM.EditMode ? "\\uE73E" : "\\uE70F"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Settings", () => OpenSettings?.Invoke(), "Ctrl+,", "\\uE713"));
        menu.Items.Add(Item("Hide launcher", () => OverlayShouldHide?.Invoke(), "Esc", "\\uE711"));
        return menu;'''),
('''    private static MenuItem Item(string header, Action click, string gesture = null)
    {
        var mi = new MenuItem { Header = header, InputGestureText = gesture };''',
'''    private static MenuItem Item(string header, Action click, string gesture = null, string glyph = null)
    {
        var mi = new MenuItem { Header = header, InputGestureText = gesture, Icon = glyph };'''),
('''        menu.Items.Add(Item("Open", () => Tile_Click(new Button { Tag = item }, null)));
        if (!item.IsFolder)
        {
            menu.Items.Add(Item("Run as administrator", () => Launch(item, true)));
            if (item.ResolvedTarget != null)
                menu.Items.Add(Item("Open file location", () => RevealInExplorer(item.ResolvedTarget)));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Rename", () => RenameItem(item), "F2"));
        if (inFolder)
        {
            menu.Items.Add(Item("Move out of folder", () => MoveOutOfFolder(item)));
        }
        else
        {
            var move = new MenuItem { Header = "Move to page" };''',
'''        menu.Items.Add(Item("Open", () => Tile_Click(new Button { Tag = item }, null), "Enter",
            item.IsFolder ? "\\uE838" : "\\uE768"));
        if (!item.IsFolder)
        {
            menu.Items.Add(Item("Run as administrator", () => Launch(item, true), null, "\\uE7EF"));
            if (item.ResolvedTarget != null)
                menu.Items.Add(Item("Open file location", () => RevealInExplorer(item.ResolvedTarget), null, "\\uED25"));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Rename", () => RenameItem(item), "F2", "\\uE8AC"));
        if (inFolder)
        {
            menu.Items.Add(Item("Move out of folder", () => MoveOutOfFolder(item), null, "\\uE8DE"));
        }
        else
        {
            var move = new MenuItem { Header = "Move to page", Icon = "\\uE8AB" };'''),
('''        menu.Items.Add(Item(item.IsFolder ? "Remove folder" : "Remove", () => RemoveItem(item), "Del"));''',
'''        var remove = Item(item.IsFolder ? "Remove folder" : "Remove from launcher", () => RemoveItem(item), "Del", "\\uE74D");
        remove.Tag = "danger";
        menu.Items.Add(remove);'''),
('''        menu.PlacementTarget = MenuButton;
        menu.Placement = PlacementMode.Bottom;''',
'''        menu.PlacementTarget = MenuButton;
        menu.Placement = PlacementMode.Bottom;
        menu.HorizontalOffset = -190;'''),
])

open("Services/Calculator.cs", "w", encoding="utf-8").write('''using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WinLaunch.Enhanced.Services;

// Quick arithmetic in the search box: digits, + - * / %, parentheses and
// decimals. Anything else is not treated as math, so app names never match.
public static class Calculator
{
    private static readonly Regex Allowed = new(@"^[\\d\\s\\.\\+\\-\\*/%\\(\\),x\\u00D7\\u00F7]+$", RegexOptions.Compiled);
    private static readonly Regex HasOperator = new(@"\\d\\s*[\\+\\-\\*/%x\\u00D7\\u00F7]\\s*[\\d\\(]", RegexOptions.Compiled);

    public static string TryEvaluate(string input)
    {
        var text = (input ?? "").Trim();
        if (text.Length < 3 || text.Length > 120 || !Allowed.IsMatch(text) || !HasOperator.IsMatch(text))
            return null;
        text = text.Replace('x', '*').Replace('\\u00D7', '*').Replace('\\u00F7', '/').Replace(",", "");
        try
        {
            var value = Convert.ToDouble(new DataTable().Compute(text, null), CultureInfo.InvariantCulture);
            if (double.IsNaN(value) || double.IsInfinity(value))
                return null;
            return Math.Abs(value) >= 1e15 || (value != 0 && Math.Abs(value) < 1e-6)
                ? value.ToString("G10", CultureInfo.InvariantCulture)
                : Math.Round(value, 10).ToString("#,0.##########", CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }
}
''')
print("ok")
