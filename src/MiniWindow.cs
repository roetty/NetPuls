using System.Runtime.InteropServices;

namespace NetPuls;

public enum MiniMode { Off, Free, Taskbar }

/// <summary>
/// Kleines, randloses Fenster mit Live-Diagramm. Zwei Modi:
/// Free = frei verschiebbar, im Vordergrund.
/// Taskbar = wird als Kindfenster in die Windows-Taskleiste eingesetzt (links neben dem Infobereich).
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
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hWnd, ref POINT p);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    private const long WS_CHILD = 0x40000000L, WS_POPUP = 0x80000000L, WS_CLIPSIBLINGS = 0x04000000L;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const uint SWP_SHOWWINDOW = 0x40, SWP_FRAMECHANGED = 0x20, SWP_NOZORDER = 0x4;

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
    private IntPtr _embeddedIn; // Taskleiste, in die das Fenster eingesetzt ist

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
            if (_embeddedIn != IntPtr.Zero)
            {
                // Falls WinForms das Fenster neu erzeugt: gleich wieder als Teil der Taskleiste
                cp.Style = (int)((cp.Style & ~WS_POPUP) | WS_CHILD | WS_CLIPSIBLINGS);
                cp.ExStyle &= ~WS_EX_TOPMOST;
                cp.Parent = _embeddedIn;
            }
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
        _ = Handle; // Fenster-Handle sicher erzeugen

        if (mode != MiniMode.Taskbar) Unembed();

        if (mode == MiniMode.Off)
        {
            Hide();
            Unhook();
            return;
        }

        if (mode == MiniMode.Taskbar)
        {
            Unhook();
            ApplyTaskbarTheme();
            if (!Embed())
            {
                // Taskleiste nicht gefunden: vorerst frei anzeigen, OnTick versucht es erneut
                UpdateSize();
                PlaceAt(null, null);
                Show();
            }
            Chart.Invalidate();
            return;
        }

        Chart.ApplyDarkTheme();
        UpdateSize();
        PlaceAt(freeX, freeY);
        if (!_hiddenForFullscreen) Show();
        KeepOnTop();
        if (_hook == IntPtr.Zero)
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        Chart.Invalidate();
    }

    /// <summary>Setzt das Fenster als Kindfenster in die Taskleiste ein (wie Taskleisten-Erweiterungen).</summary>
    private bool Embed()
    {
        var bar = FindWindow("Shell_TrayWnd", null);
        if (bar == IntPtr.Zero) return false;
        if (_embeddedIn != bar)
        {
            long style = (long)GetWindowLongPtr(Handle, GWL_STYLE);
            style = (style & ~WS_POPUP) | WS_CHILD | WS_CLIPSIBLINGS;
            SetWindowLongPtr(Handle, GWL_STYLE, (IntPtr)style);
            long ex = (long)GetWindowLongPtr(Handle, GWL_EXSTYLE);
            SetWindowLongPtr(Handle, GWL_EXSTYLE, (IntPtr)(ex & ~(long)WS_EX_TOPMOST));
            SetParent(Handle, bar);
            _embeddedIn = bar;
        }
        Visible = true;
        Reposition();
        return true;
    }

    /// <summary>Holt das Fenster aus der Taskleiste zurück (wieder normales Fenster).</summary>
    private void Unembed()
    {
        if (_embeddedIn == IntPtr.Zero || !IsHandleCreated) { _embeddedIn = IntPtr.Zero; return; }
        Hide();
        SetParent(Handle, IntPtr.Zero);
        long style = (long)GetWindowLongPtr(Handle, GWL_STYLE);
        style = (style & ~WS_CHILD) | WS_POPUP;
        SetWindowLongPtr(Handle, GWL_STYLE, (IntPtr)style);
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        _embeddedIn = IntPtr.Zero;
    }

    /// <summary>Hintergrund passend zur hellen oder dunklen Taskleiste.</summary>
    private void ApplyTaskbarTheme()
    {
        bool light = false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch { }
        if (light) Chart.ApplyLightTheme(); else Chart.ApplyTaskbarDarkTheme();
        BackColor = Chart.BackColor;
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

        if (Mode == MiniMode.Taskbar && TrayNotifyLeft() is int trayLeft && GetWindowRect(Handle, out var w))
        {
            // Im Taskleisten-Modus nur waagerecht verschieben: Abstand zum Infobereich merken
            TaskbarOffset = Math.Max(0, trayLeft - w.Right - Dp(6));
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

    /// <summary>Positioniert das eingesetzte Fenster in der Taskleiste, links neben dem Infobereich.</summary>
    public void Reposition()
    {
        if (Mode != MiniMode.Taskbar || _dragging || _embeddedIn == IntPtr.Zero) return;
        var bar = TaskbarRect();
        var trayLeft = TrayNotifyLeft();
        if (bar is not { } r || trayLeft is not int tl) return;

        bool vertical = r.Height > r.Width;
        int w = vertical ? r.Width - Dp(4) : Dp(185);
        int h = vertical ? Dp(40) : r.Height - Dp(6);
        int screenX = vertical ? r.Left + Dp(2) : Math.Max(r.Left, tl - TaskbarOffset - w - Dp(6));
        int screenY = vertical ? r.Bottom - Dp(160) : r.Top + (r.Height - h) / 2;

        // Bildschirm- in Taskleisten-Koordinaten umrechnen
        var p = new POINT { X = screenX, Y = screenY };
        ScreenToClient(_embeddedIn, ref p);
        SetWindowPos(Handle, HWND_TOP, p.X, p.Y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>Jede Sekunde aufgerufen.</summary>
    public void OnTick()
    {
        if (Mode == MiniMode.Off) return;

        if (Mode == MiniMode.Taskbar)
        {
            // Explorer neu gestartet oder Taskleiste noch nicht da: erneut einsetzen
            var bar = FindWindow("Shell_TrayWnd", null);
            if (bar != IntPtr.Zero && (bar != _embeddedIn || !IsWindow(_embeddedIn) || GetParent(Handle) != bar))
            {
                _embeddedIn = IntPtr.Zero;
                Embed();
            }
            else Reposition();
            return;
        }

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
        KeepOnTop();
    }

    /// <summary>Hält das Fenster über der Taskleiste, auch wenn diese angeklickt wurde.</summary>
    public void KeepOnTop()
    {
        if (Mode == MiniMode.Free && Visible && IsHandleCreated && !_dragging)
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
        if (disposing) Unembed();
        base.Dispose(disposing);
    }
}
