using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinLaunch.Enhanced;
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
    public static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Exception failure = null;

        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), "wle-preview");
            if (Directory.Exists(tmp))
                Directory.Delete(tmp, recursive: true);
            var store = new LegacyStore(tmp);
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

            var compat = new ThemeCompat();

            foreach (var mode in new[] { "Dark", "Light" })
            {
                // Fresh view-model per pass so async icon loads cannot cross-talk between themes.
                var vm = new MainViewModel(tmp);
                ThemeManager.Apply(mode, vm.Store, compat);
                app.Dispatcher.Invoke(() =>
                {
                    var view = new SpringboardView();
                    view.Bind(vm);
                    view.Background = (Brush)app.FindResource("ScrimBrush");
                    view.Measure(new Size(1280, 800));
                    view.Arrange(new Rect(0, 0, 1280, 800));
                    view.UpdateLayout();
                });
                Pump(app.Dispatcher, 2500);
                app.Dispatcher.Invoke(() =>
                {
                    var view = new SpringboardView();
                    view.Bind(vm);
                    view.Background = (Brush)app.FindResource("ScrimBrush");
                    view.Measure(new Size(1280, 800));
                    view.Arrange(new Rect(0, 0, 1280, 800));
                    view.UpdateLayout();
                    var rtb = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(view);
                    rtb.Freeze();
                    var outPath = Path.Combine(AppContext.BaseDirectory,
                        "..", "..", "..", "..", "..", "docs",
                        $"preview-{mode.ToLowerInvariant()}.png");
                    outPath = Path.GetFullPath(outPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
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

    private static ICItem NewApp(string name, string path, int index) =>
        new ICItem { Name = name, Application = path, Page = 0, GridIndex = index };

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
