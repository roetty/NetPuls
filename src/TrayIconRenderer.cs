using System.Runtime.InteropServices;

namespace NetPuls;

/// <summary>
/// Erzeugt das Tray-Symbol als Mini-Diagramm:
/// obere Hälfte = Download (grün, wächst nach oben), untere Hälfte = Upload (orange, wächst nach unten).
/// </summary>
public sealed class TrayIconRenderer : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // Zwei Generationen halten: das gerade angezeigte und das vorherige Symbol.
    private (IntPtr Handle, Icon? Icon) _current, _previous;

    public Icon Render(TrafficHistory history, bool bits)
    {
        var size = SystemInformation.SmallIconSize;
        int w = Math.Max(16, size.Width), h = Math.Max(16, size.Height);

        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(255, 18, 20, 24));
            int cols = w - 2;
            var samples = history.Last(cols);
            double peak = samples.Length == 0 ? 0 : samples.Max(s => Math.Max(s.Down, s.Up));
            double max = Units.NiceMax(peak, bits);
            int mid = h / 2;
            int half = mid - 1;

            using var downPen = new Pen(TrafficChart.DownColor);
            using var upPen = new Pen(TrafficChart.UpColor);
            for (int i = 0; i < samples.Length; i++)
            {
                int x = 1 + cols - samples.Length + i;
                int dh = (int)Math.Ceiling(Math.Min(samples[i].Down / max, 1) * half);
                int uh = (int)Math.Ceiling(Math.Min(samples[i].Up / max, 1) * half);
                if (dh > 0) g.DrawLine(downPen, x, mid - 1, x, mid - dh);
                if (uh > 0) g.DrawLine(upPen, x, mid, x, mid + uh - 1);
            }
            using var midPen = new Pen(Color.FromArgb(70, 255, 255, 255));
            g.DrawLine(midPen, 0, mid, w - 1, mid);
            using var border = new Pen(Color.FromArgb(110, 140, 150, 160));
            g.DrawRectangle(border, 0, 0, w - 1, h - 1);
        }

        IntPtr handle = bmp.GetHicon();
        var icon = Icon.FromHandle(handle);
        Release(_previous);
        _previous = _current;
        _current = (handle, icon);
        return icon;
    }

    private static void Release((IntPtr Handle, Icon? Icon) entry)
    {
        entry.Icon?.Dispose();
        if (entry.Handle != IntPtr.Zero) DestroyIcon(entry.Handle);
    }

    public void Dispose()
    {
        Release(_previous);
        Release(_current);
        _previous = _current = default;
    }
}
