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
        Trace($"startup args=[{string.Join(' ', e.Args)}]");
        bool owned = TakeSingleInstanceLock();
        Trace($"single instance owned={owned}");
        if (!owned && !smoke)
        {
            Trace($"signalling {(quit ? "quit" : "show")} on an existing instance");
            Signal(quit ? QuitEventName : ShowEventName);
            Shutdown();
            return;
        }
        if (quit)
        {
            Trace("quit arg, exiting");
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
        Trace($"view model built, items={vm.TopItems.Count}");
        var window = new MainWindow(vm);
        Trace("window built");
        MainWindow = window;
        window.HideToTray();
        Trace("hidden to tray");
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

    // Startup and shutdown breadcrumb, written only when WL_TRACE=1. Answers
    // "the launcher will not start" without attaching a debugger.
    public static void Trace(string message)
    {
        if (Environment.GetEnvironmentVariable("WL_TRACE") != "1")
            return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinLaunch");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "enhanced-trace.log"), $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
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

    // Takes the single-instance mutex. A held mutex usually means another copy
    // is showing, but a leaked handle from a dead copy holds it too, and that
    // phantom used to brick every start (trace showed owned=False with no
    // instance alive). A real copy is always a WinLaunchEnhanced process, so a
    // held mutex with no such process is a phantom: claim a spare name and go.
    private bool TakeSingleInstanceLock()
    {
        _single = new Mutex(false, MutexName, out _);
        bool owned;
        try
        {
            owned = _single.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true; // the previous holder died; the lock is ours now
        }
        if (owned)
            return true;
        var others = System.Diagnostics.Process.GetProcessesByName("WinLaunchEnhanced");
        bool real = others.Any(p => p.Id != Environment.ProcessId);
        foreach (var p in others)
            p.Dispose();
        if (real)
            return false;
        Trace("phantom single-instance mutex, working around it");
        for (int i = 2; i <= 9; i++)
        {
            var spare = new Mutex(true, MutexName + "." + i, out bool got);
            if (got)
            {
                _single.Dispose();
                _single = spare;
                // Two copies racing for spares could both win one; the loser
                // stands down once the winner shows up as a process.
                var racers = System.Diagnostics.Process.GetProcessesByName("WinLaunchEnhanced");
                bool lost = racers.Any(p => p.Id != Environment.ProcessId);
                foreach (var p in racers)
                    p.Dispose();
                if (lost)
                    return false;
                return true;
            }
            spare.Dispose();
        }
        return true; // every name taken; the show/quit events still gate copies
    }

    private void Listen(MainWindow window)
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        // An installer's --quit with no copy running leaves the event set, and a
        // fresh copy would read that stale signal as its own quit and exit at once.
        while (_quitEvent.WaitOne(0)) { }
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
                    Trace("quit signal received");
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
