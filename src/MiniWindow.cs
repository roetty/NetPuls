using System.Runtime.InteropServices;

namespace NetPuls;

public enum MiniMode { Off, Free, Taskbar }

/// <summary>
/// Kleines, randloses Fenster mit Live-Diagramm. Zwei Modi:
/// Free = frei verschiebbar, im Vordergrund. Taskbar = sitzt in der Taskleiste links neben dem Infobereich.
/// </summary>
public sealed class MiniWindow : Form
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);

    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObj, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2;
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x3, WINEVENT_OUTOFCONTEXT = 0;

    private readonly WinEventProc _foregroundProc;
    private IntPtr _hook;
    private bool _dragging;
    private bool _hiddenForFullscreen;

    public TrafficChart Chart { get; }
    public MiniMode Mode { get; private set; } = MiniMode.Free;
    /// <summary>Abstand (Pixel) zwischen rechter Fensterkante und Infobereich im Taskleisten-Modus.</summary>
    public int TaskbarOffset { get; set; }

    public event Action? OpenMainRequested;
    /// <summary>Nach dem Verschieben mit der Maus.</summary>
    public event Action? Moved;

    public MiniWindow(TrafficHistory history, ContextMenuStrip menu)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(70, 76, 86);
        Padding = new Padding(1);
        Opacity = 0.92;
        Text = "NetPuls Mini";
        ContextMenuStrip = menu;

        Chart = new TrafficChart { History = history, Compact = true, Seconds = 90, Dock = DockStyle.Fill, ContextMenuStrip = menu };
        Chart.MouseDown += OnChartMouseDown;
        Controls.Add(Chart);

        _foregroundProc = (_, _, _, _, _, _, _) => KeepOnTop();
        UpdateSize();
    }

    private int Dp(float v) => (int)(v * DeviceDpi / 96f);

    private void UpdateSize()
    {
        if (Mode == MiniMode.Taskbar)
        {
            var bar = TaskbarRect();
            int h = bar is { } r && r.Height < r.Width ? r.Height - Dp(8) : Dp(40);
            Size = new Size(Dp(185), Math.Max(Dp(28), h));
        }
        else
        {
            Size = new Size(Dp(190), Dp(44));
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateSize();
        if (Mode == MiniMode.Taskbar) Reposition();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST; // nicht in Alt+Tab
            return cp;
        }
    }

    /// <summary>Wechselt den Modus und zeigt/versteckt das Fenster entsprechend.</summary>
    public void SetMode(MiniMode mode, int? freeX, int? freeY)
    {
        Mode = mode;
        Chart.TaskbarLayout = mode == MiniMode.Taskbar;
        Padding = mode == MiniMode.Taskbar ? new Padding(0) : new Padding(1);
        Opacity = mode == MiniMode.Taskbar ? 1.0 : 0.92;

        if (mode == MiniMode.Off)
        {
            Hide();
            Unhook();
            return;
        }

        UpdateSize();
        if (mode == MiniMode.Taskbar) Reposition();
        else PlaceAt(freeX, freeY);

        if (!_hiddenForFullscreen) Show();
        KeepOnTop();
        if (_hook == IntPtr.Zero)
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        Chart.Invalidate();
    }

    private void OnChartMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (e.Clicks >= 2) { OpenMainRequested?.Invoke(); return; }
        // Fenster mit der Maus verschiebbar machen
        _dragging = true;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        _dragging = false;

        if (Mode == MiniMode.Taskbar && TrayNotifyLeft() is int trayLeft)
        {
            // Im Taskleisten-Modus nur waagerecht verschieben: Abstand zum Infobereich merken
            TaskbarOffset = Math.Max(0, trayLeft - Right);
            Reposition();
        }
        Moved?.Invoke();
    }

    /// <summary>Platziert das Fenster frei (gespeicherte Position oder unten rechts über der Taskleiste).</summary>
    public void PlaceAt(int? x, int? y)
    {
        if (x is int px && y is int py)
        {
            var rect = new Rectangle(px, py, Width, Height);
            if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(rect)))
            {
                Location = new Point(px, py);
                return;
            }
        }
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        int m = Dp(8);
        Location = new Point(wa.Right - Width - m, wa.Bottom - Height - m);
    }

    // ---- Taskleiste --------------------------------------------------------------------------

    private static Rectangle? TaskbarRect()
    {
        var bar = FindWindow("Shell_TrayWnd", null);
        if (bar == IntPtr.Zero || !GetWindowRect(bar, out var r)) return null;
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    private static int? TrayNotifyLeft()
    {
        var bar = FindWindow("Shell_TrayWnd", null);
        if (bar == IntPtr.Zero) return null;
        var notify = FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify != IntPtr.Zero && GetWindowRect(notify, out var n) && n.Right > n.Left) return n.Left;
        // Fallback: rechtes Viertel der Taskleiste freilassen
        return GetWindowRect(bar, out var r) ? r.Right - (r.Right - r.Left) / 6 : null;
    }

    /// <summary>Setzt das Fenster in die Taskleiste, links neben den Infobereich.</summary>
    public void Reposition()
    {
        if (Mode != MiniMode.Taskbar || _dragging) return;
        var bar = TaskbarRect();
        var trayLeft = TrayNotifyLeft();
        if (bar is not { } r || trayLeft is not int tl || r.Height >= r.Width)
        {
            // Taskleiste nicht gefunden oder senkrecht: unten rechts schweben
            PlaceAt(null, null);
            return;
        }
        int h = r.Height - Dp(8);
        if (Height != Math.Max(Dp(28), h)) UpdateSize();
        int x = tl - TaskbarOffset - Width - Dp(6);
        int y = r.Top + (r.Height - Height) / 2;
        x = Math.Max(r.Left, x);
        if (Location != new Point(x, y)) Location = new Point(x, y);
    }

    /// <summary>Jede Sekunde aufgerufen: Vollbild prüfen, Position und Vordergrund halten.</summary>
    public void OnTick()
    {
        if (Mode == MiniMode.Off) return;

        bool fullscreen = false;
        try
        {
            // 2 = beschäftigt (Vollbild-App), 3 = Direct3D-Vollbild, 4 = Präsentationsmodus
            if (SHQueryUserNotificationState(out int state) == 0) fullscreen = state is 2 or 3 or 4;
        }
        catch { }

        if (fullscreen && Visible) { _hiddenForFullscreen = true; Hide(); return; }
        if (!fullscreen && _hiddenForFullscreen) { _hiddenForFullscreen = false; Show(); }
        if (!Visible) return;

        if (Mode == MiniMode.Taskbar) Reposition();
        KeepOnTop();
    }

    /// <summary>Hält das Fenster über der Taskleiste, auch wenn diese angeklickt wurde.</summary>
    public void KeepOnTop()
    {
        if (Visible && IsHandleCreated && !_dragging)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void Unhook()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    protected override void Dispose(bool disposing)
    {
        Unhook();
        base.Dispose(disposing);
    }
}
