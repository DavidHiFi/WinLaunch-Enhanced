using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinLaunch.Enhanced;
using WinLaunch.Enhanced.ViewModels;
using WinLaunch.Enhanced.Views;

// Frame-time measurement on the real fullscreen launcher window, over a copy
// of the data: opening, edit-mode wiggle, and a mouse drag. Prints the median
// and 95th-percentile frame interval and the share of frames slower than 20 ms.
// Usage: PreviewRender --data <dir> --perf [Wallpaper|Blur|Acrylic|Solid]
internal static class PerfTest
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

    public static int Run(Application app, string dataDir, string backdrop)
    {
        var d = app.Dispatcher;
        var work = Path.Combine(Path.GetTempPath(), "wle-perf-data");
        if (Directory.Exists(work))
            Directory.Delete(work, recursive: true);
        Program.CopyDir(dataDir, work);
        var vm = new MainViewModel(work);
        vm.Settings.SortItemsAlphabetically = false;
        vm.Reload();
        SpringboardView.AnimationsOff = false;
        if (Environment.GetEnvironmentVariable("WL_DIM0") == "1") vm.Prefs.Dim = 0;
        var win = new MainWindow(vm, smoke: true);
        ThemeManager.SaveBackdrop(vm.Store, backdrop);
        app.MainWindow = win;
        Program.PumpUntilIcons(d, vm, 20000);
        win.Prewarm();
        Program.Pump(d, 500);

        var frames = new List<double>();
        TimeSpan last = TimeSpan.Zero;
        bool recording = false;
        EventHandler onFrame = (_, e) =>
        {
            var t = ((RenderingEventArgs)e).RenderingTime;
            if (recording && last != TimeSpan.Zero && t != last)
                frames.Add((t - last).TotalMilliseconds);
            last = t;
        };
        CompositionTarget.Rendering += onFrame;

        void Phase(string name, Action act, int ms)
        {
            frames.Clear();
            last = TimeSpan.Zero;
            recording = true;
            act();
            Program.Pump(d, ms);
            recording = false;
            if (frames.Count == 0) { Console.WriteLine($"PERF {name}: no frames"); return; }
            var s = frames.OrderBy(x => x).ToList();
            double med = s[s.Count / 2], p95 = s[(int)(s.Count * 0.95)];
            double slow = 100.0 * s.Count(x => x > 20) / s.Count;
            Console.WriteLine($"PERF {backdrop} {name}: frames={s.Count} median={med:F1}ms p95={p95:F1}ms slow>20ms={slow:F0}%");
        }

        Phase("open (first)", () => win.ShowOverlay(), 600);
        win.HideOverlay();
        Program.Pump(d, 400);
        Phase("open (again)", () => win.ShowOverlay(), 600);
        Program.Pump(d, 300);
        CaptureScreen(win, Path.Combine(Path.GetTempPath(), $"wl-screen-{backdrop.ToLowerInvariant()}.png"));
        Phase("open anim only", () => { win.HideOverlay(); Program.Pump(d, 300); frames.Clear(); win.ShowOverlay(); }, 230);
        var view = (SpringboardView)win.FindName("Board");
        Phase("wiggle", () => view.SetEditMode(true), 2000);

        // Drag the first app around in a circle for two seconds.
        var tile = FirstAppButton(view);
        var c = tile.PointToScreen(new Point(tile.ActualWidth / 2, tile.ActualHeight / 2 - 12));
        SetCursorPos((int)c.X, (int)c.Y);
        Program.Pump(d, 100);
        mouse_event(0x02, 0, 0, 0, UIntPtr.Zero);
        Program.Pump(d, 120);
        Phase("drag", () => { }, 0);
        frames.Clear();
        recording = true;
        last = TimeSpan.Zero;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 2000)
        {
            double a = sw.ElapsedMilliseconds / 300.0;
            SetCursorPos((int)(c.X + 260 + Math.Cos(a) * 220), (int)(c.Y + 200 + Math.Sin(a) * 160));
            Program.Pump(d, 8);
        }
        recording = false;
        var s2 = frames.OrderBy(x => x).ToList();
        if (s2.Count > 0)
            Console.WriteLine($"PERF {backdrop} drag: frames={s2.Count} median={s2[s2.Count / 2]:F1}ms p95={s2[(int)(s2.Count * 0.95)]:F1}ms slow>20ms={100.0 * s2.Count(x => x > 20) / s2.Count:F0}%");
        SetCursorPos((int)c.X, (int)c.Y);
        Program.Pump(d, 50);
        // Esc cancels the drag so the test leaves the data unchanged.
        System.Windows.Input.Keyboard.Focus(view);
        mouse_event(0x04, 0, 0, 0, UIntPtr.Zero);
        Program.Pump(d, 200);

        // Page change: three rows per page spills the 20 tiles onto a second page.
        vm.Settings.Rows = 1;
        vm.EnforceCapacity();
        view.RefreshPagePublic();
        view.ApplySettings();
        Program.Pump(d, 400);
        var go = typeof(SpringboardView).GetMethod("GoToPage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Phase("page change", () => go.Invoke(view, new object[] { 1, false }), 260);
        vm.Settings.Rows = 5;
        vm.Reload();
        view.RefreshPagePublic();
        view.ApplySettings();
        Program.Pump(d, 400);
        var f = vm.TopItems.First(i => i.IsFolder);
        Phase("folder anim only", () => { view.SetEditMode(false); Program.Pump(d, 300); frames.Clear(); view.OpenFolder(f); }, 210);
        CompositionTarget.Rendering -= onFrame;
        win.HideOverlay();
        Program.Pump(d, 300);
        win.Close();
        return 0;
    }

    // Copies the composed desktop (DWM blur included) under the window.
    private static void CaptureScreen(Window win, string path)
    {
        var tl = win.PointToScreen(new Point(0, 0));
        var br = win.PointToScreen(new Point(win.ActualWidth, win.ActualHeight));
        int w = (int)(br.X - tl.X), h = (int)(br.Y - tl.Y);
        using var bmp = new System.Drawing.Bitmap(w, h);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen((int)tl.X, (int)tl.Y, 0, 0, new System.Drawing.Size(w, h));
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"captured {path}");
    }

    private static Button FirstAppButton(SpringboardView view)
    {
        var q = new Queue<DependencyObject>();
        q.Enqueue((DependencyObject)view.FindName("TileGrid"));
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur is Button b && b.Tag is ItemViewModel i && !i.IsFolder && b.ActualWidth > 30)
                return b;
            for (int n = 0; n < VisualTreeHelper.GetChildrenCount(cur); n++)
                q.Enqueue(VisualTreeHelper.GetChild(cur, n));
        }
        throw new InvalidOperationException("no app tile");
    }
}
