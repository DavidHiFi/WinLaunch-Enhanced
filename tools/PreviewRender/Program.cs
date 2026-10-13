using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinLaunch.Enhanced;
using WinLaunch.Enhanced.Services;
using WinLaunch.Enhanced.ViewModels;
using WinLaunch.Enhanced.Views;
using WinLaunch.Legacy;

// Offscreen renderer for the README preview. Uses the real SpringboardView,
// real themes, and real extracted system icons with generic sample entries,
// so the screenshot shows the actual UI without touching user data or opening
// any window.
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        WinLaunch.Enhanced.ThemeManager.EnsureStyles();
        Console.WriteLine($"styles merged: {app.Resources.MergedDictionaries.Count}, " +
            $"CardPanel: {app.Resources.Contains("CardPanel")}, " +
            $"TileButton: {app.Resources.Contains("TileButton")}, " +
            $"CardBrush: {app.Resources.Contains("CardBrush")}");
        Exception failure = null;

        // Optional: --data <dir> renders a real dataset (output goes to %TEMP%,
        // never into docs/, so private app names can't leak into the repo).
        // Default renders generic samples into docs/ for the README.
        string dataDir = null, outDir = null, folderName = null, themes = null;
        bool edit = false, settingsPages = true, routeTest = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--data" && i + 1 < args.Length) dataDir = args[++i];
            if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
            if (args[i] == "--folder" && i + 1 < args.Length) folderName = args[++i];
            if (args[i] == "--themes" && i + 1 < args.Length) themes = args[++i];
            if (args[i] == "--edit") edit = true;
            if (args[i] == "--no-settings") settingsPages = false;
            if (args[i] == "--route-test") routeTest = true;
        }
        SpringboardView.AnimationsOff = true;
        int perfAt = Array.IndexOf(args, "--perf");
        if (perfAt >= 0 && dataDir != null)
            return PerfTest.Run(app, dataDir, perfAt + 1 < args.Length && !args[perfAt + 1].StartsWith("--") ? args[perfAt + 1] : "Wallpaper");
        if (args.Contains("--drag-test") && dataDir != null)
            return DragTest.Run(app, dataDir);
        if (args.Contains("--update-test"))
            return UpdateTest.Run();

        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), "wle-preview");
            LegacyStore store;
            if (dataDir != null)
            {
                // Work on a copy: rendering writes theme.mode/IconCache next to
                // the dataset, and the source (e.g. a pristine backup) stays clean.
                var work = Path.Combine(Path.GetTempPath(), "wle-audit-data");
                if (Directory.Exists(work))
                    Directory.Delete(work, recursive: true);
                CopyDir(dataDir, work);
                store = new LegacyStore(work);
            }
            else
            {
                if (Directory.Exists(tmp))
                    Directory.Delete(tmp, recursive: true);
                store = new LegacyStore(tmp);
                var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var sys = Path.Combine(win, "System32");
                // Generic Windows tools only, so no personal app names reach the README.
                var apps = new (string Name, string Path)[]
                {
                    ("File Explorer", Path.Combine(win, "explorer.exe")),
                    ("Task Manager", Path.Combine(sys, "taskmgr.exe")),
                    ("Calculator", Path.Combine(sys, "calc.exe")),
                    ("Command Prompt", Path.Combine(sys, "cmd.exe")),
                    ("PowerShell", Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe")),
                    ("Control Panel", Path.Combine(sys, "control.exe")),
                    ("Remote Desktop", Path.Combine(sys, "mstsc.exe")),
                    ("Registry Editor", Path.Combine(win, "regedit.exe")),
                    ("Resource Monitor", Path.Combine(sys, "resmon.exe")),
                    ("Volume Mixer", Path.Combine(sys, "sndvol.exe")),
                    ("DirectX Diagnostics", Path.Combine(sys, "dxdiag.exe")),
                    ("System Information", Path.Combine(sys, "msinfo32.exe")),
                };
                var items = apps.Select((a, i) => NewApp(a.Name, a.Path, i)).ToList();
                ICItem Folder(string name, int index, params (string Name, string Path)[] children) =>
                    new ICItem { IsFolder = true, Name = name, Page = 0, GridIndex = index,
                        Items = children.Select((c, i) => NewApp(c.Name, c.Path, i)).ToList() };
                items.Add(Folder("System Tools", items.Count,
                    ("Event Viewer", Path.Combine(sys, "eventvwr.exe")),
                    ("Disk Cleanup", Path.Combine(sys, "cleanmgr.exe")),
                    ("Performance Monitor", Path.Combine(sys, "perfmon.exe")),
                    ("System Configuration", Path.Combine(sys, "msconfig.exe")),
                    ("Disk Management", Path.Combine(sys, "diskmgmt.msc")),
                    ("Services", Path.Combine(sys, "services.msc")),
                    ("Device Manager", Path.Combine(sys, "devmgmt.msc")),
                    ("Task Scheduler", Path.Combine(sys, "taskschd.msc")),
                    ("Computer Management", Path.Combine(sys, "compmgmt.msc"))));
                items.Add(Folder("Accessibility", items.Count,
                    ("Magnifier", Path.Combine(sys, "magnify.exe")),
                    ("On-Screen Keyboard", Path.Combine(sys, "osk.exe")),
                    ("Narrator", Path.Combine(sys, "narrator.exe")),
                    ("Character Map", Path.Combine(sys, "charmap.exe"))));
                store.SaveItems(items);
                store.SaveSettings(new LegacySettings { Columns = 10, Rows = 5 });
            }
            outDir ??= dataDir != null
                ? Path.Combine(Path.GetTempPath(), "wle-preview-out")
                : Path.Combine(AppContext.BaseDirectory,
                    "..", "..", "..", "..", "..", "docs");

            var compat = new ThemeCompat();

            foreach (var mode in (themes ?? "Dark,Light").Split(','))
            {
                // Fresh view-model per pass so async icon loads cannot cross-talk between themes.
                var vm = new MainViewModel(store.Directory);
                ThemeManager.Apply(mode, vm.Store, compat);
                ThemeManager.ApplyPrefs(vm.Prefs);
                int w = dataDir != null ? 1920 : 1280, h = dataDir != null ? 1080 : 800;
                app.Dispatcher.Invoke(() =>
                {
                    LastView = new SpringboardView();
                    LastView.Bind(vm);
                    // Same layering as MainWindow in Wallpaper mode. Docs use a
                    // neutral generated backdrop; --data audits use the real wallpaper.
                    var wall = dataDir != null ? WallpaperBackdrop.GetBlurred(null, vm.Prefs.Blur) : null;
                    LastRoot = new System.Windows.Controls.Grid
                    {
                        Background = wall != null ? (Brush)app.FindResource("SolidBgBrush") : SampleBackdrop(),
                    };
                    if (wall != null)
                        LastRoot.Children.Add(new System.Windows.Controls.Image { Source = wall, Stretch = Stretch.UniformToFill });
                    LastRoot.Children.Add(new System.Windows.Shapes.Rectangle { Fill = (Brush)app.FindResource("WallScrimBrush"), Opacity = vm.Prefs.Dim });
                    LastRoot.Children.Add(LastView);
                    LastRoot.Measure(new Size(w, h));
                    LastRoot.Arrange(new Rect(0, 0, w, h));
                    LastRoot.UpdateLayout();
                });
                PumpUntilIcons(app.Dispatcher, vm, 30000);
                app.Dispatcher.Invoke(() =>
                {
                    var view = LastRoot;
                    if (edit)
                        LastView.SetEditMode(true);
                    if (folderName != null)
                    {
                        var f = vm.TopItems.FirstOrDefault(i => i.IsFolder &&
                            i.Name.Contains(folderName, StringComparison.OrdinalIgnoreCase));
                        if (f != null)
                            LastView.OpenFolder(f);
                    }
                    view.UpdateLayout();
                    var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(view);
                    rtb.Freeze();
                    var outPath = Path.GetFullPath(Path.Combine(outDir,
                        $"preview-{mode.ToLowerInvariant().Replace(' ', '-')}{(folderName != null ? "-folder" : "")}{(edit ? "-edit" : "")}.png"));
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    using var fs = new FileStream(outPath, FileMode.Create);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    enc.Save(fs);
                    Console.WriteLine($"wrote {outPath}");
                    if (routeTest)
                        RouteTest(vm, outDir, mode, w, h);
                });
                if (!settingsPages)
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
                        // A ContextMenu cannot be parented, so lay it out on its
                        // own and paint it over the board background.
                        menu.Measure(new Size(420, 520));
                        var ms = menu.DesiredSize;
                        menu.Arrange(new Rect(0, 0, ms.Width, ms.Height));
                        menu.UpdateLayout();
                        var dv = new DrawingVisual();
                        using (var ctx = dv.RenderOpen())
                        {
                            ctx.DrawRectangle((Brush)app.FindResource("SolidBgBrush"), null, new Rect(0, 0, ms.Width + 40, ms.Height + 40));
                            ctx.DrawRectangle(new VisualBrush(menu), null, new Rect(20, 20, ms.Width, ms.Height));
                        }
                        Save(dv, (int)ms.Width + 40, (int)ms.Height + 40, Path.Combine(outDir, $"menu-{tag}-{name}.png"));
                    }
                });
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        app.Dispatcher.InvokeShutdown();
        if (failure != null)
        {
            Console.WriteLine("PREVIEW FAILED: " + failure);
            return 1;
        }
        return 0;
    }

    // Fires the same ContextMenuOpening event WPF raises on a right-click and
    // reports whether each surface ends up with a non-empty, unhandled menu.
    private static void RouteTest(MainViewModel vm, string outDir, string mode, int w, int h)
    {
        var view = LastView;
        int fails = 0;
        void Check(string label, FrameworkElement source, Func<System.Windows.Controls.ContextMenu> menuOf)
        {
            var ctor = typeof(System.Windows.Controls.ContextMenuEventArgs).GetConstructor(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
                new[] { typeof(object), typeof(bool), typeof(double), typeof(double) }, null);
            var args = (System.Windows.Controls.ContextMenuEventArgs)ctor.Invoke(new object[] { source, true, 1.0, 1.0 });
            source.RaiseEvent(args);
            var menu = menuOf();
            var headers = menu == null ? "" : string.Join(" | ", menu.Items.OfType<System.Windows.Controls.MenuItem>().Select(m => m.Header));
            bool ok = !args.Handled && menu != null && menu.Items.Count > 0;
            if (!ok) fails++;
            Console.WriteLine($"ROUTE {(ok ? "PASS" : "FAIL")} {label}: handled={args.Handled} items=[{headers}]");
        }
        T Find<T>(DependencyObject root, Func<T, bool> pred) where T : DependencyObject
        {
            var q = new Queue<DependencyObject>();
            q.Enqueue(root);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (cur is T t && pred(t)) return t;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(cur); i++)
                    q.Enqueue(VisualTreeHelper.GetChild(cur, i));
            }
            return null;
        }
        var root = (FrameworkElement)view.FindName("Root");
        var tileGrid = (FrameworkElement)view.FindName("TileGrid");
        var appTile = Find<System.Windows.Controls.Button>(tileGrid, b => b.Tag is ItemViewModel i && !i.IsFolder);
        var folderTile = Find<System.Windows.Controls.Button>(tileGrid, b => b.Tag is ItemViewModel i && i.IsFolder);
        Check("board empty space", root, () => root.ContextMenu);
        var gridPanel = Find<System.Windows.Controls.Primitives.UniformGrid>(tileGrid, _ => true);
        if (gridPanel != null) Check("board grid gap", gridPanel, () => root.ContextMenu);
        if (appTile != null) Check("app tile", appTile, () => appTile.ContextMenu);
        if (appTile != null)
        {
            var model = ((ItemViewModel)appTile.Tag).Model;
            bool was = model.RunAsAdmin;
            var tick = appTile.ContextMenu.Items.OfType<System.Windows.Controls.MenuItem>()
                .First(m => (string)m.Header == "Always run as administrator");
            tick.IsChecked = !tick.IsChecked;
            tick.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            bool flipped = model.RunAsAdmin == !was;
            if (!flipped) fails++;
            Console.WriteLine($"ROUTE {(flipped ? "PASS" : "FAIL")} always-run-as-admin tick: {was} -> {model.RunAsAdmin}");
        }
        if (folderTile != null)
        {
            Check("folder tile", folderTile, () => folderTile.ContextMenu);
            view.OpenFolder((ItemViewModel)folderTile.Tag);
            view.UpdateLayout();
            var band = (System.Windows.Controls.Border)view.FindName("FolderBand");
            var folderGrid = (FrameworkElement)view.FindName("FolderGrid");
            Check("open folder empty space", band, () => band.ContextMenu);
            Check("folder dim backdrop", (FrameworkElement)view.FindName("FolderDim"), () => root.ContextMenu);
            var inner = Find<System.Windows.Controls.Button>(folderGrid, b => b.Tag is ItemViewModel);
            if (inner != null) Check("tile inside folder", inner, () => inner.ContextMenu);
            // Edit sheet for the folder's first app.
            var target = inner?.Tag as ItemViewModel ?? (ItemViewModel)appTile.Tag;
            typeof(SpringboardView).GetMethod("EditItem", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(view, new object[] { target });
            LastRoot.UpdateLayout();
            var tag = mode.ToLowerInvariant().Replace(' ', '-');
            Save(LastRoot, w, h, Path.Combine(outDir, $"route-{tag}-editsheet.png"));
            typeof(SpringboardView).GetMethod("CloseEdit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(view, null);
            Save(LastRoot, w, h, Path.Combine(outDir, $"route-{tag}-folder.png"));
        }
        // Services behind the new menu entries, on the temp data copy only.
        void Expect(string label, bool ok, string detail) { if (!ok) fails++; Console.WriteLine($"SERVICE {(ok ? "PASS" : "FAIL")} {label}: {detail}"); }
        Expect("drive path is not a URL", !LegacyStore.IsUrl(@"C:\Windows") && LegacyStore.IsUrl("steam://run/1"), "C: path vs steam://");
        var web = LinkImporter.ImportUrl("https://www.github.com/DavidHiFi");
        Expect("web link import", web.Name == "github.com" && vm.Store.ResolveLinkTarget(web.Application) == web.Application, web.Name);
        var dirItem = LinkImporter.Import(Path.GetTempPath(), vm.Store);
        var dirTarget = vm.Store.ResolveLinkTarget(dirItem.Application);
        Expect("folder shortcut import", dirTarget != null && Directory.Exists(dirTarget), $"{dirItem.Name} -> {dirTarget}");
        var scan = InstalledApps.Scan(new HashSet<string>());
        Expect("start menu scan", scan.Files.Count + scan.Folders.Count > 0, $"{scan.Files.Count} files, {scan.Folders.Count} folders");
        var appVm = vm.TopItems.First(i => !i.IsFolder);
        var png = Path.Combine(Path.GetTempPath(), "wle-test-icon.png");
        Save(new System.Windows.Shapes.Rectangle { Width = 64, Height = 64, Fill = Brushes.Orange }, 64, 64, png);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(SpringboardView).GetMethod("EditItem", flags).Invoke(view, new object[] { appVm });
        typeof(SpringboardView).GetField("_editIconPath", flags).SetValue(view, IconService.SaveCustomIcon(png, vm.Store));
        typeof(SpringboardView).GetMethod("EditSave_Click", flags).Invoke(view, new object[] { null, null });
        var resolved = IconService.ResolveIconPath(appVm.Model, vm.Store);
        Expect("custom icon saved via edit sheet", resolved != null && File.Exists(resolved), resolved ?? "null");
        var icon = Task.Run(() => IconService.GetIconAsync(appVm.Model, vm.Store, vm.IconCacheDir, CancellationToken.None)).GetAwaiter().GetResult();
        Expect("custom icon decodes", icon != null && icon.PixelWidth > 0, $"{icon?.PixelWidth}px");
        Console.WriteLine(fails == 0 ? "ROUTE ALL PASS" : $"ROUTE FAILURES: {fails}");
    }

    private static SpringboardView LastView;

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
    }
    private static System.Windows.Controls.Grid LastRoot;

    private static Brush SampleBackdrop()
    {
        var b = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x2A, 0x2B, 0x4A), 0),
                new GradientStop(Color.FromRgb(0x5B, 0x4B, 0x8A), 0.45),
                new GradientStop(Color.FromRgb(0x1F, 0x4E, 0x6B), 1),
            },
        };
        b.Freeze();
        return b;
    }

    private static ICItem NewApp(string name, string path, int index) =>
        new ICItem { Name = name, Application = path, Page = 0, GridIndex = index };

    internal static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDir(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    internal static void PumpUntilIcons(Dispatcher d, MainViewModel vm, int budgetMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < budgetMs)
        {
            bool done = true;
            foreach (var item in vm.TopItems.Concat(vm.TopItems.SelectMany(t => t.Children)))
            {
                if (!item.IconLoadTask.IsCompleted) { done = false; break; }
            }
            if (done)
                break;
            Pump(d, 500);
        }
        // Flush the queued Dispatcher.Invoke callbacks that set the bitmaps.
        Pump(d, 500);
    }

    internal static void Pump(Dispatcher d, int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, d)
        {
            Interval = TimeSpan.FromMilliseconds(ms),
        };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
