using System.Runtime.InteropServices;

namespace NetPuls;

/// <summary>Kleines, randloses Fenster mit Live-Diagramm, standardmäßig direkt über der Taskleiste.</summary>
public sealed class MiniWindow : Form
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2;
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    public TrafficChart Chart { get; }
    public event Action? OpenMainRequested;
    public event Action<Point>? Moved;

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

        UpdateSize();
    }

    private void UpdateSize()
    {
        float s = DeviceDpi / 96f;
        Size = new Size((int)(190 * s), (int)(44 * s));
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateSize();
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

    private void OnChartMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (e.Clicks >= 2) { OpenMainRequested?.Invoke(); return; }
        // Fenster mit der Maus verschiebbar machen
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        Moved?.Invoke(Location);
    }

    /// <summary>Platziert das Fenster (gespeicherte Position oder unten rechts über der Taskleiste).</summary>
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
        int m = (int)(8 * DeviceDpi / 96f);
        Location = new Point(wa.Right - Width - m, wa.Bottom - Height - m);
    }

    /// <summary>Hält das Fenster über der Taskleiste, auch wenn diese angeklickt wurde.</summary>
    public void KeepOnTop()
    {
        if (Visible && IsHandleCreated)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
