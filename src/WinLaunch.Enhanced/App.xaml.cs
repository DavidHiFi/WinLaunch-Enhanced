using System.IO;
using System.Threading;
using System.Windows;
using WinLaunch.Enhanced.ViewModels;

namespace WinLaunch.Enhanced;

public partial class App : System.Windows.Application
{
    private static Mutex _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ThemeManager.EnsureStyles();
        _single = new Mutex(true, "WinLaunch.Enhanced.SingleInstance", out bool owned);
        if (!owned)
        {
            System.Windows.MessageBox.Show("WinLaunch Enhanced is already running.", "WinLaunch Enhanced",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var dataDir = e.Args.Length > 0 && Directory.Exists(e.Args[0]) ? e.Args[0] : null;
        if (e.Args.Any(a => a == "--smoke"))
        {
            RunSmoke(dataDir);
            return;
        }
        var vm = new MainViewModel(dataDir);
        var window = new MainWindow(vm);
        MainWindow = window;
        window.HideToTray();
        if (e.Args.Any(a => a == "--show"))
            window.ShowOverlay();
    }

    // Headless startup check: builds the real view-model and window offscreen,
    // pumps the dispatcher so async icons settle, prints counts, exits.
    // No window is shown, no hotkey registered, no tray icon, nothing saved.
    private void RunSmoke(string dataDir)
    {
        int exit = 0;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var vm = new MainViewModel(dataDir);
                var window = new MainWindow(vm, smoke: true);
                MainWindow = window;
                window.Measure(new System.Windows.Size(1920, 1080));
                window.Arrange(new Rect(0, 0, 1920, 1080));
                window.UpdateLayout();
                Pump(3000);
                int icons = vm.TopItems.Count(i => i.Icon != null);
                int folders = vm.TopItems.Count(i => i.IsFolder);
                int nested = vm.TopItems.Where(i => i.IsFolder).Sum(i => i.Children.Count);
                int broken = vm.TopItems.Count(i => i.Broken);
                Console.WriteLine($"smoke: items={vm.TopItems.Count} folders={folders} " +
                    $"nested={nested} broken={broken} icons={icons} " +
                    $"error={vm.LoadError ?? "none"} ms={sw.ElapsedMilliseconds}");
                if (vm.LoadError != null)
                    exit = 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("smoke FAILED: " + ex);
                exit = 1;
            }
            finally
            {
                Shutdown(exit);
            }
        }));
    }

    private static void Pump(int ms)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background,
            System.Windows.Threading.Dispatcher.CurrentDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(ms),
        };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
