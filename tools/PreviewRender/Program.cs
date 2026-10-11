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
        bool edit = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--data" && i + 1 < args.Length) dataDir = args[++i];
            if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
            if (args[i] == "--folder" && i + 1 < args.Length) folderName = args[++i];
            if (args[i] == "--themes" && i + 1 < args.Length) themes = args[++i];
            if (args[i] == "--edit") edit = true;
        }
        SpringboardView.AnimationsOff = true;

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
                store.SaveItems(new List<ICItem>
                {
                    NewApp("File Explorer", Path.Combine(win, "explorer.exe"), 0),
                    NewApp("Task Manager", Path.Combine(win, "System32", "taskmgr.exe"), 1),
                    NewApp("Calculator", Path.Combine(win, "System32", "calc.exe"), 2),
                    NewApp("Management Console", Path.Combine(win, "System32", "mmc.exe"), 3),
                    new ICItem { IsFolder = true, Name = "System Tools", Page = 0, GridIndex = 4,
                        Items = new List<ICItem>
                        {
                            NewApp("Registry Editor", Path.Combine(win, "regedit.exe"), 0),
                            NewApp("Management Console", Path.Combine(win, "System32", "mmc.exe"), 1),
                        } },
                });
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
                int w = dataDir != null ? 1920 : 1280, h = dataDir != null ? 1080 : 800;
                app.Dispatcher.Invoke(() =>
                {
                    LastView = new SpringboardView();
                    LastView.Bind(vm);
                    // Same layering as MainWindow in Wallpaper mode. Docs use a
                    // neutral generated backdrop; --data audits use the real wallpaper.
                    var wall = dataDir != null ? WallpaperBackdrop.GetBlurred() : null;
                    LastRoot = new System.Windows.Controls.Grid
                    {
                        Background = wall != null ? (Brush)app.FindResource("SolidBgBrush") : SampleBackdrop(),
                    };
                    if (wall != null)
                        LastRoot.Children.Add(new System.Windows.Controls.Image { Source = wall, Stretch = Stretch.UniformToFill });
                    LastRoot.Children.Add(new System.Windows.Shapes.Rectangle { Fill = (Brush)app.FindResource("WallScrimBrush") });
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
                });
                // Settings dialog render: proves the rewritten settings XAML
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

    private static SpringboardView LastView;
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

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDir(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    private static void PumpUntilIcons(Dispatcher d, MainViewModel vm, int budgetMs)
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

    private static void Pump(Dispatcher d, int ms)
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
