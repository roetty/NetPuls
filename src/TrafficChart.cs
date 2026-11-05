using System.Drawing.Drawing2D;

namespace NetPuls;

/// <summary>Zeichnet Download (grün, Fläche) und Upload (orange, Linie) als Live-Diagramm.</summary>
public sealed class TrafficChart : Control
{
    public static readonly Color DownColor = Color.FromArgb(52, 211, 120);
    public static readonly Color UpColor = Color.FromArgb(255, 145, 60);
    private static readonly Color GridColor = Color.FromArgb(52, 58, 66);

    public TrafficHistory? History { get; set; }
    /// <summary>Angezeigter Zeitraum in Sekunden.</summary>
    public int Seconds { get; set; } = 60;
    public bool UseBits { get; set; }
    /// <summary>Kompakte Darstellung ohne Achsen (für das Mini-Fenster).</summary>
    public bool Compact { get; set; }
    /// <summary>Für die Taskleiste: Werte zweizeilig links, Kurve rechts daneben.</summary>
    public bool TaskbarLayout { get; set; }

    public TrafficChart()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(22, 25, 30);
        ForeColor = Color.FromArgb(200, 205, 212);
    }

    private float Px(float v) => v * DeviceDpi / 96f;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var samples = History?.Last(Seconds) ?? Array.Empty<Sample>();
        double peak = samples.Length == 0 ? 0 : samples.Max(s => Math.Max(s.Down, s.Up));
        double max = Units.NiceMax(peak, UseBits);

        float textWidth = Compact && TaskbarLayout ? Px(84) : 0;
        RectangleF plot = Compact
            ? new RectangleF(textWidth, Px(2), Width - 1 - textWidth, Height - Px(3))
            : new RectangleF(Px(78), Px(12), Width - Px(78) - Px(14), Height - Px(12) - Px(28));
        if (plot.Width < 10 || plot.Height < 10) return;

        using var font = new Font("Segoe UI", Compact ? 7.5f : 8.5f);
        using var textBrush = new SolidBrush(ForeColor);
        using var gridPen = new Pen(GridColor) { DashStyle = Compact ? DashStyle.Dot : DashStyle.Solid };

        // Gitterlinien + Y-Beschriftung
        const int rows = 4;
        for (int r = 0; r <= rows; r++)
        {
            float y = plot.Bottom - plot.Height * r / rows;
            if (!Compact || (r > 0 && r < rows))
                g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            if (!Compact)
            {
                string label = Units.Format(max * r / rows, UseBits);
                var size = g.MeasureString(label, font);
                g.DrawString(label, font, textBrush, plot.Left - size.Width - Px(6), y - size.Height / 2);
            }
        }

        // Zeitachse
        if (!Compact)
        {
            int step = Seconds <= 60 ? 10 : Seconds <= 300 ? 60 : Seconds <= 900 ? 180 : 600;
            for (int t = 0; t <= Seconds; t += step)
            {
                float x = plot.Right - plot.Width * t / Seconds;
                g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
                string label = t == 0 ? "jetzt" : t < 120 ? $"-{t} s" : $"-{t / 60} min";
                var size = g.MeasureString(label, font);
                g.DrawString(label, font, textBrush, x - size.Width / 2, plot.Bottom + Px(5));
            }
        }

        if (samples.Length >= 2)
        {
            float dx = plot.Width / Seconds;
            PointF Pt(int i, double value)
            {
                float x = plot.Right - (samples.Length - 1 - i) * dx;
                float y = plot.Bottom - (float)(Math.Min(value / max, 1.0) * plot.Height);
                return new PointF(x, y);
            }

            var down = new PointF[samples.Length];
            var up = new PointF[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                down[i] = Pt(i, samples[i].Down);
                up[i] = Pt(i, samples[i].Up);
            }

            g.SetClip(plot);
            FillArea(g, down, plot.Bottom, Color.FromArgb(90, DownColor));
            FillArea(g, up, plot.Bottom, Color.FromArgb(45, UpColor));
            using (var p = new Pen(DownColor, Px(1.5f)) { LineJoin = LineJoin.Round })
                g.DrawLines(p, down);
            using (var p = new Pen(UpColor, Px(1.5f)) { LineJoin = LineJoin.Round })
                g.DrawLines(p, up);
            g.ResetClip();
        }

        if (Compact && TaskbarLayout)
        {
            var last = History?.Latest;
            using var f = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            using var downBrush = new SolidBrush(DownColor);
            using var upBrush = new SolidBrush(UpColor);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            string d = "↓ " + (last is { } a ? Units.Format(a.Down, UseBits) : "–");
            string u = "↑ " + (last is { } b ? Units.Format(b.Up, UseBits) : "–");
            float lh = g.MeasureString("↓ 0", f).Height;
            float top = (Height - 2 * lh) / 2;
            g.DrawString(d, f, downBrush, Px(3), top);
            g.DrawString(u, f, upBrush, Px(3), top + lh);
        }
        else if (Compact)
        {
            var last = History?.Latest;
            string text = last is { } s
                ? $"↓ {Units.Format(s.Down, UseBits)}   ↑ {Units.Format(s.Up, UseBits)}"
                : "Messe …";
            using var shadow = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
            using var bold = new Font("Segoe UI", 8f, FontStyle.Bold);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.DrawString(text, bold, shadow, Px(5), Px(3));
            g.DrawString(text, bold, Brushes.White, Px(4), Px(2));
        }
        else
        {
            using var border = new Pen(GridColor);
            g.DrawRectangle(border, plot.X, plot.Y, plot.Width, plot.Height);
        }
    }

    private static void FillArea(Graphics g, PointF[] line, float bottom, Color color)
    {
        var poly = new PointF[line.Length + 2];
        line.CopyTo(poly, 0);
        poly[^2] = new PointF(line[^1].X, bottom);
        poly[^1] = new PointF(line[0].X, bottom);
        using var b = new SolidBrush(color);
        g.FillPolygon(b, poly);
    }
}
