namespace NetPuls;

/// <summary>Großes Fenster mit Live-Diagramm und aktuellen Werten.</summary>
public sealed class MainWindow : Form
{
    private static readonly (string Label, int Seconds)[] Ranges =
    {
        ("1 Minute", 60), ("2 Minuten", 120), ("5 Minuten", 300), ("15 Minuten", 900), ("60 Minuten", 3600),
    };

    private readonly Settings _settings;
    private readonly TrafficChart _chart;
    private readonly Label _down, _up, _peak, _adapter;
    private readonly ComboBox _range, _unit;
    private readonly CheckBox _topMost;

    public bool AllowClose { get; set; }
    public event Action? UnitChanged;

    public MainWindow(TrafficHistory history, Settings settings, Icon icon)
    {
        _settings = settings;
        Text = "NetPuls – Live-Traffic";
        Icon = icon;
        BackColor = Color.FromArgb(30, 33, 38);
        ForeColor = Color.FromArgb(225, 228, 232);
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        int Dp(int v) => LogicalToDeviceUnits(v);
        MinimumSize = new Size(Dp(520), Dp(300));

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 2,
            Padding = new Padding(12, 10, 12, 6), BackColor = BackColor,
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var big = new Font("Segoe UI Semibold", 15f);
        _down = new Label { AutoSize = true, Font = big, ForeColor = TrafficChart.DownColor, Text = "↓ –", Margin = new Padding(0, 0, 28, 0) };
        _up = new Label { AutoSize = true, Font = big, ForeColor = TrafficChart.UpColor, Text = "↑ –", Margin = new Padding(0, 0, 28, 0) };
        _peak = new Label { AutoSize = true, ForeColor = Color.FromArgb(150, 156, 165), Anchor = AnchorStyles.Left };
        _adapter = new Label { AutoSize = true, ForeColor = Color.FromArgb(120, 126, 135), Margin = new Padding(0, 4, 0, 0) };

        var controls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, BackColor = BackColor };
        _range = DarkCombo(Dp(110));
        foreach (var r in Ranges) _range.Items.Add(r.Label);
        int idx = Array.FindIndex(Ranges, r => r.Seconds == settings.ChartSeconds);
        _range.SelectedIndex = idx < 0 ? 1 : idx;
        _range.SelectedIndexChanged += (_, _) =>
        {
            _settings.ChartSeconds = Ranges[_range.SelectedIndex].Seconds;
            _chart!.Seconds = _settings.ChartSeconds;
            _chart.Invalidate();
            _settings.Save();
        };

        _unit = DarkCombo(Dp(120));
        _unit.Items.AddRange(new object[] { "Mbit/s (Bits)", "MB/s (Bytes)" });
        _unit.SelectedIndex = settings.UseBits ? 0 : 1;
        _unit.SelectedIndexChanged += (_, _) =>
        {
            _settings.UseBits = _unit.SelectedIndex == 0;
            _settings.Save();
            UnitChanged?.Invoke();
        };

        _topMost = new CheckBox { Text = "Immer im Vordergrund", AutoSize = true, Checked = settings.MainTopMost, Margin = new Padding(8, 5, 0, 0) };
        _topMost.CheckedChanged += (_, _) =>
        {
            TopMost = _settings.MainTopMost = _topMost.Checked;
            _settings.Save();
        };
        TopMost = settings.MainTopMost;

        controls.Controls.AddRange(new Control[] { _range, _unit, _topMost });

        top.Controls.Add(_down, 0, 0);
        top.Controls.Add(_up, 1, 0);
        top.Controls.Add(_peak, 2, 0);
        top.Controls.Add(controls, 3, 0);
        top.Controls.Add(_adapter, 0, 1);
        top.SetColumnSpan(_adapter, 4);

        _chart = new TrafficChart
        {
            History = history, Seconds = settings.ChartSeconds, UseBits = settings.UseBits,
            Dock = DockStyle.Fill, Margin = new Padding(0),
        };
        var chartHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 8), BackColor = BackColor };
        chartHost.Controls.Add(_chart);

        Controls.Add(chartHost);
        Controls.Add(top);

        // Fensterposition wiederherstellen
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        var bounds = new Rectangle(
            settings.MainX ?? wa.Left + (wa.Width - Dp(900)) / 2,
            settings.MainY ?? wa.Top + (wa.Height - Dp(460)) / 2,
            settings.MainWidth ?? Dp(900), settings.MainHeight ?? Dp(460));
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds)))
            bounds.Location = new Point(wa.Left + 40, wa.Top + 40);
        Bounds = bounds;
    }

    private static ComboBox DarkCombo(int width) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(45, 49, 56),
        ForeColor = Color.FromArgb(225, 228, 232),
        Width = width,
        Margin = new Padding(6, 2, 0, 0),
    };

    public void UpdateView(bool useBits, string adapterInfo)
    {
        if (!Visible || WindowState == FormWindowState.Minimized) return;
        _chart.UseBits = useBits;
        var history = _chart.History!;
        if (history.Latest is { } s)
        {
            _down.Text = "↓ " + Units.Format(s.Down, useBits);
            _up.Text = "↑ " + Units.Format(s.Up, useBits);
        }
        var window = history.Last(_chart.Seconds);
        if (window.Length > 0)
        {
            _peak.Text = $"Spitze ({_range.Text}):  ↓ {Units.Format(window.Max(x => x.Down), useBits)}   " +
                         $"↑ {Units.Format(window.Max(x => x.Up), useBits)}";
        }
        _adapter.Text = adapterInfo;
        if (_unit.SelectedIndex != (useBits ? 0 : 1)) _unit.SelectedIndex = useBits ? 0 : 1;
        _chart.Invalidate();
    }

    public void SaveBounds()
    {
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.MainX = b.X; _settings.MainY = b.Y;
        _settings.MainWidth = b.Width; _settings.MainHeight = b.Height;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveBounds();
        _settings.Save();
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
        {
            // Schließen = nur ausblenden, Programm läuft im Tray weiter
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
