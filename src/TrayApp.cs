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
    private readonly ToolStripMenuItem _miniMenu, _miniTaskbar, _miniFree, _miniOff, _adapterMenu, _bitsItem, _bytesItem, _autostartItem, _lanItem;
    private readonly MiniWindow _mini;
    private readonly Icon _appIcon;
    private MainWindow? _main;
    private HelpWindow? _help;

    public TrayApp()
    {
        _appIcon = CreateAppIcon();
        _sampler.AdapterId = _settings.AdapterId;
        _sampler.InternetOnly = _settings.InternetOnly;

        _menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Großes Fenster öffnen", null, (_, _) => ShowMain()) { Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold) };
        _miniMenu = new ToolStripMenuItem("Kleine Grafik");
        _miniTaskbar = new ToolStripMenuItem("In der Taskleiste (neben der Uhr)", null, (_, _) => SetMiniMode(MiniMode.Taskbar));
        _miniFree = new ToolStripMenuItem("Frei verschiebbar", null, (_, _) => SetMiniMode(MiniMode.Free));
        _miniOff = new ToolStripMenuItem("Aus", null, (_, _) => SetMiniMode(MiniMode.Off));
        _miniMenu.DropDownItems.AddRange(new ToolStripItem[] { _miniTaskbar, _miniFree, _miniOff });
        _adapterMenu = new ToolStripMenuItem("Netzwerkkarte");
        _adapterMenu.DropDownItems.Add("(wird geladen)");
        _adapterMenu.DropDownOpening += (_, _) => BuildAdapterMenu();
        var unitMenu = new ToolStripMenuItem("Einheit");
        _bitsItem = new ToolStripMenuItem("Mbit/s (Bits)", null, (_, _) => SetUnit(true));
        _bytesItem = new ToolStripMenuItem("MB/s (Bytes)", null, (_, _) => SetUnit(false));
        unitMenu.DropDownItems.AddRange(new ToolStripItem[] { _bitsItem, _bytesItem });
        _lanItem = new ToolStripMenuItem("Heimnetz-Traffic mitzählen", null, (_, _) => ToggleLan());
        _autostartItem = new ToolStripMenuItem("Mit Windows starten", null, (_, _) => ToggleAutostart());
        var helpItem = new ToolStripMenuItem("Hilfe", null, (_, _) => ShowHelp(0));
        var infoItem = new ToolStripMenuItem("Über NetPuls", null, (_, _) => ShowHelp(1));
        var exit = new ToolStripMenuItem("Beenden", null, (_, _) => Exit());
        _menu.Items.AddRange(new ToolStripItem[]
        {
            open, _miniMenu, new ToolStripSeparator(), _lanItem, _adapterMenu, unitMenu, _autostartItem,
            new ToolStripSeparator(), helpItem, infoItem, new ToolStripSeparator(), exit,
        });
        _menu.Opening += (_, _) =>
        {
            _autostartItem.Checked = SafeAutostartState();
            _bitsItem.Checked = _settings.UseBits;
            _bytesItem.Checked = !_settings.UseBits;
            var mode = _settings.GetMiniMode();
            _miniTaskbar.Checked = mode == MiniMode.Taskbar;
            _miniFree.Checked = mode == MiniMode.Free;
            _miniOff.Checked = mode == MiniMode.Off;
            _lanItem.Checked = !_settings.InternetOnly;
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
        _mini.TaskbarOffset = _settings.TaskbarOffset;
        _mini.Moved += () =>
        {
            if (_mini.Mode == MiniMode.Taskbar) _settings.TaskbarOffset = _mini.TaskbarOffset;
            else { _settings.MiniX = _mini.Left; _settings.MiniY = _mini.Top; }
            _settings.Save();
        };
        _mini.SetMode(_settings.GetMiniMode(), _settings.MiniX, _settings.MiniY);

        _sampler.Sampled += OnSample;
        _sampler.Start();
    }

    private void OnSample(Sample s)
    {
        bool bits = _settings.UseBits;
        _tray.Icon = _renderer.Render(_sampler.History, bits);
        string head = _sampler.FilteringActive ? "NetPuls (nur Internet)" : "NetPuls (inkl. Heimnetz)";
        string tip = $"{head}\n↓ {Units.Format(s.Down, bits)}\n↑ {Units.Format(s.Up, bits)}";
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;

        _mini.OnTick();
        if (_mini.Visible)
        {
            _mini.Chart.UseBits = bits;
            _mini.Chart.Invalidate();
        }
        _main?.UpdateView(bits, AdapterInfo());
    }

    private string AdapterInfo()
    {
        if (_sampler.FilteringActive) return _sampler.ModeInfo;
        string prefix = _settings.InternetOnly ? _sampler.ModeInfo + "  ·  " : "";
        return prefix + CardInfo();
    }

    private string CardInfo()
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

    private void ToggleLan()
    {
        _settings.InternetOnly = !_settings.InternetOnly;
        _sampler.InternetOnly = _settings.InternetOnly;
        _settings.Save();
        RefreshViews();
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

    private void SetMiniMode(MiniMode mode)
    {
        _settings.MiniMode = mode.ToString();
        _settings.ShowMiniWindow = mode != MiniMode.Off;
        _mini.SetMode(mode, _settings.MiniX, _settings.MiniY);
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
            _main.HelpWanted += () => ShowHelp(0);
        }
        _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.Activate();
        _main.UpdateView(_settings.UseBits, AdapterInfo());
    }

    private void ShowHelp(int tab)
    {
        if (_help == null || _help.IsDisposed)
            _help = new HelpWindow(_appIcon, () => _sampler.ModeInfo, _sampler.Diagnostics);
        _help.ShowTab(tab);
    }

    private void Exit()
    {
        _help?.Dispose();
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
