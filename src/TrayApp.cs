using System.Net.NetworkInformation;

namespace NetPuls;

/// <summary>Hauptlogik: Tray-Symbol, Menü, Mini-Fenster und großes Fenster.</summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly Settings _settings = Settings.Load();
    private readonly TrafficSampler _sampler = new();
    private readonly TrayIconRenderer _renderer = new();
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _miniItem, _adapterMenu, _bitsItem, _bytesItem, _autostartItem;
    private readonly MiniWindow _mini;
    private readonly Icon _appIcon;
    private MainWindow? _main;
    private int _tick;

    public TrayApp()
    {
        _appIcon = CreateAppIcon();
        _sampler.AdapterId = _settings.AdapterId;

        _menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Großes Fenster öffnen", null, (_, _) => ShowMain()) { Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold) };
        _miniItem = new ToolStripMenuItem("Mini-Fenster an der Taskleiste", null, (_, _) => ToggleMini()) { Checked = _settings.ShowMiniWindow };
        _adapterMenu = new ToolStripMenuItem("Netzwerkkarte");
        _adapterMenu.DropDownItems.Add("(wird geladen)");
        _adapterMenu.DropDownOpening += (_, _) => BuildAdapterMenu();
        var unitMenu = new ToolStripMenuItem("Einheit");
        _bitsItem = new ToolStripMenuItem("Mbit/s (Bits)", null, (_, _) => SetUnit(true));
        _bytesItem = new ToolStripMenuItem("MB/s (Bytes)", null, (_, _) => SetUnit(false));
        unitMenu.DropDownItems.AddRange(new ToolStripItem[] { _bitsItem, _bytesItem });
        _autostartItem = new ToolStripMenuItem("Mit Windows starten", null, (_, _) => ToggleAutostart());
        var exit = new ToolStripMenuItem("Beenden", null, (_, _) => Exit());
        _menu.Items.AddRange(new ToolStripItem[]
        {
            open, _miniItem, new ToolStripSeparator(), _adapterMenu, unitMenu, _autostartItem,
            new ToolStripSeparator(), exit,
        });
        _menu.Opening += (_, _) =>
        {
            _autostartItem.Checked = SafeAutostartState();
            _bitsItem.Checked = _settings.UseBits;
            _bytesItem.Checked = !_settings.UseBits;
            _miniItem.Checked = _settings.ShowMiniWindow;
        };

        _tray = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "NetPuls",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };

        _mini = new MiniWindow(_sampler.History, _menu);
        _mini.Chart.UseBits = _settings.UseBits;
        _mini.OpenMainRequested += ShowMain;
        _mini.Moved += p => { _settings.MiniX = p.X; _settings.MiniY = p.Y; _settings.Save(); };
        _mini.PlaceAt(_settings.MiniX, _settings.MiniY);
        if (_settings.ShowMiniWindow) _mini.Show();

        _sampler.Sampled += OnSample;
        _sampler.Start();
    }

    private void OnSample(Sample s)
    {
        bool bits = _settings.UseBits;
        _tray.Icon = _renderer.Render(_sampler.History, bits);
        string tip = $"NetPuls\n↓ {Units.Format(s.Down, bits)}\n↑ {Units.Format(s.Up, bits)}";
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;

        if (_mini.Visible)
        {
            _mini.Chart.UseBits = bits;
            _mini.Chart.Invalidate();
            if (++_tick % 3 == 0) _mini.KeepOnTop();
        }
        _main?.UpdateView(bits, AdapterInfo());
    }

    private string AdapterInfo()
    {
        if (_settings.AdapterId == null) return "Netzwerkkarte: automatisch (alle Verbindungen mit Internet-Gateway)";
        var nic = SafeInterfaces().FirstOrDefault(n => n.Id == _settings.AdapterId);
        return nic == null ? "Netzwerkkarte: ausgewählte Karte ist nicht verbunden" : $"Netzwerkkarte: {nic.Name} ({nic.Description})";
    }

    private static NetworkInterface[] SafeInterfaces()
    {
        try { return TrafficSampler.AllUsableInterfaces().ToArray(); }
        catch { return Array.Empty<NetworkInterface>(); }
    }

    private void BuildAdapterMenu()
    {
        _adapterMenu.DropDownItems.Clear();
        var auto = new ToolStripMenuItem("Automatisch (Internet)", null, (_, _) => SetAdapter(null))
        {
            Checked = _settings.AdapterId == null,
        };
        _adapterMenu.DropDownItems.Add(auto);
        _adapterMenu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var nic in SafeInterfaces())
        {
            string id = nic.Id;
            string label = $"{nic.Name} – {nic.Description}";
            if (TrafficSampler.HasGateway(nic)) label += "  [Internet]";
            _adapterMenu.DropDownItems.Add(new ToolStripMenuItem(label, null, (_, _) => SetAdapter(id))
            {
                Checked = _settings.AdapterId == id,
            });
        }
    }

    private void SetAdapter(string? id)
    {
        _settings.AdapterId = id;
        _sampler.AdapterId = id;
        _settings.Save();
    }

    private void SetUnit(bool bits)
    {
        _settings.UseBits = bits;
        _settings.Save();
        RefreshViews();
    }

    private void RefreshViews()
    {
        _mini.Chart.UseBits = _settings.UseBits;
        _mini.Chart.Invalidate();
        _main?.UpdateView(_settings.UseBits, AdapterInfo());
    }

    private void ToggleMini()
    {
        _settings.ShowMiniWindow = !_settings.ShowMiniWindow;
        if (_settings.ShowMiniWindow) { _mini.Show(); _mini.KeepOnTop(); }
        else _mini.Hide();
        _settings.Save();
    }

    private static bool SafeAutostartState()
    {
        try { return Autostart.IsEnabled; } catch { return false; }
    }

    private void ToggleAutostart()
    {
        try { Autostart.Set(!SafeAutostartState()); }
        catch (Exception ex)
        {
            MessageBox.Show("Autostart konnte nicht geändert werden:\n" + ex.Message, "NetPuls",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowMain()
    {
        if (_main == null || _main.IsDisposed)
        {
            _main = new MainWindow(_sampler.History, _settings, _appIcon);
            _main.UnitChanged += RefreshViews;
        }
        _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.Activate();
        _main.UpdateView(_settings.UseBits, AdapterInfo());
    }

    private void Exit()
    {
        if (_main != null)
        {
            _main.SaveBounds();
            _main.AllowClose = true;
            _main.Close();
        }
        _settings.Save();
        _sampler.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _mini.Close();
        _renderer.Dispose();
        ExitThread();
    }

    /// <summary>Programmsymbol (Fenster/Startzustand): stilisierte Pulskurve.</summary>
    private static Icon CreateAppIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(Color.FromArgb(22, 25, 30));
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var down = new Pen(TrafficChart.DownColor, 2.6f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            g.DrawLines(down, new PointF[] { new(4, 17), new(10, 17), new(13, 8), new(18, 25), new(21, 14), new(28, 14) });
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
