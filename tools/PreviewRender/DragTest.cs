using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinLaunch.Enhanced;
using WinLaunch.Enhanced.ViewModels;
using WinLaunch.Enhanced.Views;

// Real-input drag test: shows the board in a window on a copy of the data and
// moves the actual mouse through press, hold, drag and drop, then checks the
// resulting layout. Usage: PreviewRender --data <dir> --drag-test
internal static class DragTest
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    private const uint LeftDown = 0x02, LeftUp = 0x04;

    private static Dispatcher D;
    private static int Fails;

    public static int Run(Application app, string dataDir)
    {
        D = app.Dispatcher;
        var work = Path.Combine(Path.GetTempPath(), "wle-drag-data");
        if (Directory.Exists(work))
            Directory.Delete(work, recursive: true);
        Program.CopyDir(dataDir, work);
        // Sorting would snap tiles back, so test the unsorted layout.
        var vm = new MainViewModel(work);
        vm.Settings.SortItemsAlphabetically = false;
        vm.Reload();
        ThemeManager.Apply(ThemeManager.LoadThemeName(vm.Store), vm.Store, null);
        var view = new SpringboardView();
        view.Bind(vm);
        var win = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true,
            Left = 40, Top = 40, Width = 1400, Height = 820, Content = view, ShowInTaskbar = false,
        };
        win.SetResourceReference(Window.BackgroundProperty, "SolidBgBrush");
        win.Show();
        win.Activate();
        Program.PumpUntilIcons(D, vm, 20000);
        Program.Pump(D, 400);

        ItemViewModel App(int n) => vm.CurrentPageItems.Where(i => !i.IsFolder).ElementAt(n);
        ItemViewModel Folder() => vm.CurrentPageItems.First(i => i.IsFolder);

        // 1. Edit mode on, drag an app onto a folder's icon.
        view.SetEditMode(true);
        Program.Pump(D, 300);
        var a = App(0);
        var folder = Folder();
        int before = folder.Children.Count;
        Drag(view, a, folder, hold: false);
        Check("app into folder", folder.Children.Any(c => c.Model == a.Model) && !vm.TopItems.Contains(a),
            $"{folder.Name}: {before} -> {folder.Children.Count}");

        // 2. Drag an app onto another app: a new folder holds both.
        var b = App(0);
        var c = App(1);
        Drag(view, b, c, hold: false);
        var made = vm.TopItems.FirstOrDefault(i => i.IsFolder && i.Children.Any(k => k.Model == b.Model) &&
                                                   i.Children.Any(k => k.Model == c.Model));
        Check("app onto app makes a folder", made != null, made?.Name ?? "none");
        D.Invoke(() => { view.FocusForOpen(); view.SetEditMode(true); });
        Program.Pump(D, 300);

        // 3. Drag an app to the empty last cell of the page.
        var d = App(0);
        int beforeIdx = vm.CurrentPageItems.IndexOf(d);
        DragToEmpty(view, d);
        int afterIdx = vm.CurrentPageItems.IndexOf(d);
        Check("app to an empty spot", afterIdx == vm.CurrentPageItems.Count - 1 && afterIdx != beforeIdx,
            $"position {beforeIdx} -> {afterIdx} of {vm.CurrentPageItems.Count}");

        // 4. Edit mode off: press and hold a tile, then drag it onto a folder.
        view.SetEditMode(false);
        Program.Pump(D, 300);
        var e = App(0);
        var f2 = vm.CurrentPageItems.Last(i => i.IsFolder);
        Drag(view, e, f2, hold: true);
        Check("hold then drag", vm.EditMode && f2.Children.Any(k => k.Model == e.Model), $"edit mode {vm.EditMode}");

        // 5. Drag an app out of an open folder onto the board.
        view.SetEditMode(true);
        D.Invoke(() => view.OpenFolder(f2));
        Program.Pump(D, 500);
        var inner = FindButton(view, e, inFolder: true);
        var start = Center(inner);
        Press(start, hold: false);
        MoveTo(start, new Point(start.X, 30 + win.Top + win.Height - 120));
        Release();
        Program.Pump(D, 500);
        Check("drag out of a folder", vm.TopItems.Any(t => t.Model == e.Model) && f2.Children.All(k => k.Model != e.Model),
            $"folder now has {f2.Children.Count}");

        // 6. Reorder inside an open folder: first app onto the last one's spot.
        var big = vm.TopItems.Where(i => i.IsFolder).OrderByDescending(i => i.Children.Count).First();
        D.Invoke(() => view.OpenFolder(big));
        Program.Pump(D, 500);
        var first = big.Children.First();
        var last = big.Children.Last();
        var p1 = Center(FindButton(view, first, inFolder: true));
        var p2 = Center(FindButton(view, last, inFolder: true));
        Press(p1, hold: false);
        MoveTo(p1, p2);
        // Mid-drag picture of the floating tile.
        Snap(win, Path.Combine(Path.GetTempPath(), "wl-drag-mid.png"));
        Release();
        Program.Pump(D, 400);
        Check("reorder inside a folder", big.Children.Last() == first && big.Model.Items.Last() == first.Model,
            $"{first.Name} now at {big.Children.IndexOf(first)} of {big.Children.Count}");
        D.Invoke(() => view.FocusForOpen());
        view.SetEditMode(true);
        Program.Pump(D, 300);

        // 7. Rest at the right edge to turn to a new page, then drop there.
        var mover = vm.CurrentPageItems.First(i => !i.IsFolder);
        int pageBefore = vm.CurrentPage;
        var m1 = Center(FindButton(view, mover));
        var edge = view.PointToScreen(new Point(view.ActualWidth - 10, view.ActualHeight / 2));
        Press(m1, hold: false);
        MoveTo(m1, edge);
        for (int i = 0; i < 8; i++) { SetCursorPos((int)edge.X + (i % 2), (int)edge.Y); Program.Pump(D, 120); }
        var mid = view.PointToScreen(new Point(view.ActualWidth / 2, view.ActualHeight / 3));
        MoveTo(edge, mid);
        Release();
        Program.Pump(D, 500);
        Check("edge paging to a new page", mover.Model.Page == pageBefore + 1 && vm.PageCount >= 2,
            $"page {pageBefore} -> {mover.Model.Page}, pages {vm.PageCount}");

        win.Close();
        Console.WriteLine(Fails == 0 ? "DRAG ALL PASS" : $"DRAG FAILURES: {Fails}");
        return Fails == 0 ? 0 : 1;
    }

    private static void Snap(Window win, string path)
    {
        var el = (FrameworkElement)win.Content;
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)el.ActualWidth, (int)el.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(el);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static void Check(string label, bool ok, string detail)
    {
        if (!ok) Fails++;
        Console.WriteLine($"DRAG {(ok ? "PASS" : "FAIL")} {label}: {detail}");
    }

    private static Button FindButton(SpringboardView view, ItemViewModel item, bool inFolder = false)
    {
        var root = (DependencyObject)view.FindName(inFolder ? "FolderGrid" : "TileGrid");
        var q = new Queue<DependencyObject>();
        q.Enqueue(root);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur is Button b && b.Tag is ItemViewModel i && i.Model == item.Model && b.ActualWidth > 30)
                return b;
            for (int n = 0; n < VisualTreeHelper.GetChildrenCount(cur); n++)
                q.Enqueue(VisualTreeHelper.GetChild(cur, n));
        }
        throw new InvalidOperationException("tile not found: " + item.Name);
    }

    private static Point Center(Button b)
    {
        var icon = (double)((FrameworkElement)b.Content).ActualHeight;
        return b.PointToScreen(new Point(b.ActualWidth / 2, b.ActualHeight / 2 - 12));
    }

    private static void Drag(SpringboardView view, ItemViewModel source, ItemViewModel target, bool hold)
    {
        var from = Center(FindButton(view, source));
        var to = Center(FindButton(view, target));
        Press(from, hold);
        MoveTo(from, to);
        Program.Pump(D, 250);
        Release();
        Program.Pump(D, 500);
    }

    private static void DragToEmpty(SpringboardView view, ItemViewModel source)
    {
        var grid = (FrameworkElement)view.FindName("TileGrid");
        var from = Center(FindButton(view, source));
        var to = grid.PointToScreen(new Point(grid.ActualWidth - 20, grid.ActualHeight - 20));
        Press(from, hold: false);
        MoveTo(from, to);
        Release();
        Program.Pump(D, 500);
    }

    private static void Press(Point p, bool hold)
    {
        SetCursorPos((int)p.X, (int)p.Y);
        Program.Pump(D, 120);
        mouse_event(LeftDown, 0, 0, 0, UIntPtr.Zero);
        Program.Pump(D, hold ? 900 : 150);
    }

    private static void MoveTo(Point from, Point to)
    {
        for (int i = 1; i <= 20; i++)
        {
            SetCursorPos((int)(from.X + (to.X - from.X) * i / 20), (int)(from.Y + (to.Y - from.Y) * i / 20));
            Program.Pump(D, 25);
        }
    }

    private static void Release()
    {
        mouse_event(LeftUp, 0, 0, 0, UIntPtr.Zero);
        Program.Pump(D, 150);
    }
}
