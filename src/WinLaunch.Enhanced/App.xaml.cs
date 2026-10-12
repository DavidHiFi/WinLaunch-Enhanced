using System.IO;
using System.Threading;
using System.Windows;
using WinLaunch.Enhanced.ViewModels;

namespace WinLaunch.Enhanced;

// One instance per user. Starting the app again (Start menu, desktop
// shortcut, taskbar pin) asks the running copy to show the launcher, so the
// shortcut behaves like opening the app. --background starts hidden in the
// tray (sign-in), --quit asks a running copy to exit (installer, uninstaller).
public partial class App : System.Windows.Application
{
    private const string MutexName = "WinLaunch.Enhanced.SingleInstance";
    private const string ShowEventName = "WinLaunch.Enhanced.Show";
    private const string QuitEventName = "WinLaunch.Enhanced.Quit";

    private static Mutex _single;
    private EventWaitHandle _showEvent, _quitEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // A tray app that dies takes the hotkey with it. Log UI-thread errors
        // and keep running instead of vanishing.
        DispatcherUnhandledException += (_, ex) =>
        {
            LogError(ex.Exception);
            ex.Handled = true;
        };
        ThemeManager.EnsureStyles();

        bool quit = e.Args.Contains("--quit");
        bool smoke = e.Args.Contains("--smoke");
        _single = new Mutex(true, MutexName, out bool owned);
        if (!owned && !smoke)
        {
            Signal(quit ? QuitEventName : ShowEventName);
            Shutdown();
            return;
        }
        if (quit)
        {
            Shutdown();
            return;
        }

        var dataDir = e.Args.FirstOrDefault(a => !a.StartsWith("--") && Directory.Exists(a));
        if (smoke)
        {
            RunSmoke(dataDir);
            return;
        }
        var vm = new MainViewModel(dataDir);
        var window = new MainWindow(vm);
        MainWindow = window;
        window.HideToTray();
        Listen(window);
        if (!e.Args.Contains("--background"))
            window.ShowOverlay();
    }

    private static void LogError(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinLaunch");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "enhanced-errors.log"), $"[{DateTime.Now:yyyy-MM-dd h:mm:ss tt}] {ex}\n\n");
        }
        catch { }
    }

    private static void Signal(string name)
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(name);
            ev.Set();
        }
        catch { }
    }

    private void Listen(MainWindow window)
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        var thread = new Thread(() =>
        {
            var handles = new WaitHandle[] { _showEvent, _quitEvent };
            while (true)
            {
                int which = WaitHandle.WaitAny(handles);
                if (which == 0)
                    Dispatcher.BeginInvoke(window.ShowOverlay);
                else
                {
                    Dispatcher.BeginInvoke(() => window.Close());
                    return;
                }
            }
        }) { IsBackground = true, Name = "InstanceSignals" };
        thread.Start();
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
                    $"nested={nested} broken={broken} icons={icons} font={ThemeManager.CurrentFont} " +
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
