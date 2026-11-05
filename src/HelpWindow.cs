using System.Diagnostics;
using System.Reflection;

namespace NetPuls;

/// <summary>Fenster mit den Reitern "Hilfe" und "Info".</summary>
public sealed class HelpWindow : Form
{
    public const string RepoUrl = "https://github.com/roetty/NetPuls";

    private static readonly Color Bg = Color.FromArgb(30, 33, 38);
    private static readonly Color Fg = Color.FromArgb(225, 228, 232);
    private static readonly Color Dim = Color.FromArgb(150, 156, 165);

    private readonly TabControl _tabs;
    private readonly Label _status;
    private readonly Func<string> _modeInfo;
    private readonly Func<string> _diagnostics;

    private const string HelpText =
@"SYMBOL IN DER TASKLEISTE
Das kleine Diagramm im Infobereich zeigt die letzten Sekunden:
oben grün = Download, unten orange = Upload.
Maus darüber: aktuelle Werte. Linksklick: großes Fenster. Rechtsklick: Menü.

Tipp: Damit das Symbol immer sichtbar ist, in Windows 11 unter
Einstellungen → Personalisierung → Taskleiste → Andere Symbole der Taskleiste
NetPuls einschalten (Windows 10: Symbol aus dem ^-Überlauf in die Taskleiste ziehen).

KLEINE GRAFIK
Im Menü unter „Kleine Grafik“ wählbar:
• In der Taskleiste: sitzt direkt in der Taskleiste links neben Uhr und Infobereich.
  Mit der Maus waagerecht ziehen, um den Abstand zur Uhr zu ändern.
• Frei verschiebbar: schwebendes Fenster, das du überall hinziehen kannst.
• Aus.
Doppelklick öffnet das große Fenster. Bei Vollbild (Spiele, Videos, Präsentationen)
blendet sich die Grafik automatisch aus.

GROSSES FENSTER
Live-Diagramm mit aktuellen Werten und Spitzenwerten.
Oben rechts lassen sich Zeitraum (1–60 Minuten), Einheit und
„Immer im Vordergrund“ einstellen. Schließen blendet das Fenster nur aus,
NetPuls läuft im Infobereich weiter.

NUR INTERNET-TRAFFIC
NetPuls zählt nur Daten, die mit dem Internet ausgetauscht werden.
Übertragungen im Heimnetz (NAS, Drucker, Streaming im LAN) werden herausgefiltert.
Dafür sind Administratorrechte nötig, deshalb fragt Windows beim Start nach.
Gemessen wird der gesamte Traffic der Netzwerkkarte, davon wird alles abgezogen,
was eindeutig mit Geräten im Heimnetz ausgetauscht wurde.
Mit „Heimnetz-Traffic mitzählen“ im Menü wird stattdessen alles gezählt,
was über die Netzwerkkarte läuft. Die Auswahl unter „Netzwerkkarte“ gilt nur dann.

EINHEITEN
Mbit/s = Megabit pro Sekunde, so geben Internetanbieter ihre Geschwindigkeit an.
MB/s = Megabyte pro Sekunde, so zeigen Browser Downloads an. 1 MB/s = 8 Mbit/s.

MIT WINDOWS STARTEN
Legt eine Aufgabe in der Windows-Aufgabenplanung an. NetPuls startet dann
bei der Anmeldung automatisch und ohne Nachfrage.

BEENDEN
Rechtsklick auf das Symbol → Beenden.

TASTEN
F1 öffnet diese Hilfe im großen Fenster.

PROBLEME?
Unter „Info“ → „Diagnosebericht speichern“ entsteht eine Textdatei auf dem Desktop,
die zeigt, was NetPuls misst.";

    public HelpWindow(Icon icon, Func<string> modeInfo, Func<string> diagnostics)
    {
        _modeInfo = modeInfo;
        _diagnostics = diagnostics;
        Text = "NetPuls – Hilfe und Info";
        Icon = icon;
        BackColor = Bg;
        ForeColor = Fg;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        Size = new Size(LogicalToDeviceUnits(640), LogicalToDeviceUnits(560));
        MinimumSize = new Size(LogicalToDeviceUnits(420), LogicalToDeviceUnits(320));
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        _tabs = new TabControl { Dock = DockStyle.Fill };

        // Reiter "Hilfe"
        var help = new TabPage("Hilfe") { BackColor = Bg };
        var helpBox = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None, BackColor = Bg, ForeColor = Fg, TabStop = false,
            Text = HelpText.Replace("\r\n", "\n").Replace("\n", "\r\n"),
        };
        var helpHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 6, 8), BackColor = Bg };
        helpHost.Controls.Add(helpBox);
        help.Controls.Add(helpHost);

        // Reiter "Info"
        var info = new TabPage("Info") { BackColor = Bg };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(18, 16, 18, 12), AutoScroll = true, BackColor = Bg,
        };
        var title = new Label { AutoSize = true, Text = "NetPuls", Font = new Font("Segoe UI Semibold", 20f), ForeColor = TrafficChart.DownColor };
        var version = new Label { AutoSize = true, Text = "Version " + AppVersion, ForeColor = Dim, Margin = new Padding(3, 0, 3, 12) };
        var desc = new Label
        {
            AutoSize = true, MaximumSize = new Size(LogicalToDeviceUnits(560), 0),
            Text = "Zeigt live an, wie viel Daten dein PC gerade mit dem Internet austauscht. " +
                   "Ohne Statistik, ohne Installation, ohne Werbung.",
            Margin = new Padding(3, 0, 3, 14),
        };
        _status = new Label { AutoSize = true, MaximumSize = new Size(LogicalToDeviceUnits(560), 0), Margin = new Padding(3, 0, 3, 14) };
        var link = new LinkLabel
        {
            AutoSize = true, Text = RepoUrl, LinkColor = Color.FromArgb(110, 170, 255),
            ActiveLinkColor = Color.White, Margin = new Padding(3, 0, 3, 4),
        };
        link.LinkClicked += (_, _) => Open(RepoUrl);
        var settingsLink = new LinkLabel
        {
            AutoSize = true, Text = "Ordner mit den Einstellungen öffnen", LinkColor = Color.FromArgb(110, 170, 255),
            ActiveLinkColor = Color.White, Margin = new Padding(3, 0, 3, 14),
        };
        settingsLink.LinkClicked += (_, _) =>
        {
            Directory.CreateDirectory(Settings.FolderPath);
            Open(Settings.FolderPath);
        };
        var tech = new Label
        {
            AutoSize = true, ForeColor = Dim, MaximumSize = new Size(LogicalToDeviceUnits(560), 0),
            Text = $".NET {Environment.Version} · {Environment.OSVersion.VersionString}\r\n" +
                   "Messung über die Windows-Ereignisablaufverfolgung (ETW).",
        };
        var diag = new Button
        {
            Text = "Diagnosebericht speichern", FlatStyle = FlatStyle.Flat, AutoSize = true,
            BackColor = Color.FromArgb(45, 49, 56), ForeColor = Fg, Margin = new Padding(3, 14, 3, 0),
        };
        diag.FlatAppearance.BorderColor = Color.FromArgb(70, 76, 86);
        diag.Click += (_, _) => SaveDiagnostics();
        var diagHint = new Label
        {
            AutoSize = true, ForeColor = Dim, MaximumSize = new Size(LogicalToDeviceUnits(560), 0),
            Text = "Speichert eine Textdatei auf dem Desktop, die bei der Fehlersuche hilft. " +
                   "Am besten während etwas heruntergeladen wird.",
        };
        flow.Controls.AddRange(new Control[] { title, version, desc, _status, link, settingsLink, tech, diag, diagHint });
        info.Controls.Add(flow);

        _tabs.TabPages.Add(help);
        _tabs.TabPages.Add(info);

        var close = new Button
        {
            Text = "Schließen", DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(45, 49, 56), ForeColor = Fg, AutoSize = true, Anchor = AnchorStyles.Right,
        };
        close.Click += (_, _) => Close();
        CancelButton = close;
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8), BackColor = Bg,
        };
        bottom.Controls.Add(close);

        Controls.Add(_tabs);
        Controls.Add(bottom);
    }

    public static string AppVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>Öffnet das Fenster auf dem gewünschten Reiter (0 = Hilfe, 1 = Info).</summary>
    public void ShowTab(int index)
    {
        _tabs.SelectedIndex = index;
        _status.Text = "Status: " + _modeInfo() +
            (EtwInternetCounter.IsAdmin() ? "\r\nLäuft mit Administratorrechten." : "\r\nLäuft ohne Administratorrechte.");
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    private void SaveDiagnostics()
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NetPuls-Diagnose.txt");
            string text = $"NetPuls {AppVersion} – Diagnose vom {DateTime.Now:dd.MM.yyyy HH:mm:ss}\r\n" +
                          $"{Environment.OSVersion.VersionString}, .NET {Environment.Version}\r\n\r\n" +
                          _diagnostics().Replace("\r\n", "\n").Replace("\n", "\r\n");
            File.WriteAllText(path, text, System.Text.Encoding.UTF8);
            Open(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Diagnose konnte nicht gespeichert werden:\n" + ex.Message, "NetPuls",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { }
    }
}
