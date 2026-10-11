import os
ROOT = os.path.join(os.path.dirname(__file__), "..", "..")

def patch(path, pairs):
    path = os.path.join(ROOT, path)
    s = open(path, encoding="utf-8").read()
    for old, new in pairs:
        if old not in s:
            raise SystemExit(f"NOT FOUND in {path}: {old[:80]!r}")
        s = s.replace(old, new, 1)
    open(path, "w", encoding="utf-8").write(s)

patch("src/WinLaunch.Enhanced/Views/SpringboardView.xaml.cs", [
("    private ContextMenu BuildBoardMenu()", "    public ContextMenu BuildBoardMenu()"),
('''    private void Tile_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Button b || b.Tag is not ItemViewModel item)
            return;
        var menu = new ContextMenu();''',
'''    private void Tile_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Button b || b.Tag is not ItemViewModel item)
            return;
        b.ContextMenu = BuildTileMenu(item);
    }

    public ContextMenu BuildTileMenu(ItemViewModel item)
    {
        var menu = new ContextMenu();'''),
('''        remove.Tag = "danger";
        menu.Items.Add(remove);
        b.ContextMenu = menu;
    }''',
'''        remove.Tag = "danger";
        menu.Items.Add(remove);
        return menu;
    }'''),
])

patch("src/WinLaunch.Enhanced/SettingsWindow.xaml.cs", [
('''    public static string Version =>''',
'''    public void SelectPage(string key)
    {
        int i = Array.FindIndex(Pages, p => p.Key == key);
        if (i >= 0)
            ((RadioButton)Nav.Children[i]).IsChecked = true;
    }

    public static string[] PageKeys => Pages.Select(p => p.Key).ToArray();

    public static string Version =>'''),
])

patch("tools/PreviewRender/Program.cs", [
('''                // Settings dialog render: proves the rewritten settings XAML
                // loads and shows the active theme (not shown on screen).
                app.Dispatcher.Invoke(() =>
                {
                    // A Window only composes once shown, so render its content
                    // element (the real settings layout) instead of the frame.
                    var compat2 = new ThemeCompat();
                    var dlg = new SettingsWindow(vm, compat2);
                    var content = (FrameworkElement)dlg.Content;
                    content.Measure(new Size(484, double.PositiveInfinity));
                    var sz = content.DesiredSize;
                    content.Arrange(new Rect(0, 0, 484, sz.Height));
                    content.UpdateLayout();
                    int ph = (int)Math.Min(Math.Ceiling(sz.Height), 1600);
                    var rtb = new RenderTargetBitmap(484, ph, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(content);
                    rtb.Freeze();
                    var outPath = Path.GetFullPath(Path.Combine(outDir,
                        $"settings-{mode.ToLowerInvariant()}.png"));
                    using var fs = new FileStream(outPath, FileMode.Create);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    enc.Save(fs);
                    Console.WriteLine($"wrote {outPath}");
                });''',
'''                if (!settingsPages)
                    continue;
                // Settings pages and menus: a Window only composes once shown,
                // so render its content element (the real layout) instead.
                app.Dispatcher.Invoke(() =>
                {
                    var main = new MainWindow(vm, smoke: true);
                    var dlg = new SettingsWindow(main);
                    var content = (FrameworkElement)dlg.Content;
                    var tag = mode.ToLowerInvariant().Replace(' ', '-');
                    foreach (var key in SettingsWindow.PageKeys)
                    {
                        dlg.SelectPage(key);
                        content.Measure(new Size(dlg.Width, dlg.Height));
                        content.Arrange(new Rect(0, 0, dlg.Width, dlg.Height));
                        content.UpdateLayout();
                        Pump(app.Dispatcher, 200);
                        content.UpdateLayout();
                        Save(content, (int)dlg.Width, (int)dlg.Height, Path.Combine(outDir, $"settings-{tag}-{key}.png"));
                    }
                    var menus = new[]
                    {
                        ("board", LastView.BuildBoardMenu()),
                        ("tile", LastView.BuildTileMenu(vm.TopItems.First(i => !i.IsFolder))),
                    };
                    foreach (var (name, menu) in menus)
                    {
                        var host = new System.Windows.Controls.Grid { Background = (Brush)app.FindResource("SolidBgBrush") };
                        host.Children.Add(menu);
                        host.Measure(new Size(420, 520));
                        host.Arrange(new Rect(0, 0, 420, 520));
                        host.UpdateLayout();
                        Save(host, 420, 520, Path.Combine(outDir, $"menu-{tag}-{name}.png"));
                    }
                });'''),
('''        bool edit = false;''', '''        bool edit = false, settingsPages = true;'''),
('''            if (args[i] == "--edit") edit = true;''', '''            if (args[i] == "--edit") edit = true;
            if (args[i] == "--no-settings") settingsPages = false;'''),
('''    private static SpringboardView LastView;''',
'''    private static SpringboardView LastView;

    private static void Save(Visual v, int w, int h, string path)
    {
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(v);
        rtb.Freeze();
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var fs = new FileStream(path, FileMode.Create);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        enc.Save(fs);
        Console.WriteLine($"wrote {path}");
    }'''),
])
print("ok")
