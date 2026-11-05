using System.Diagnostics;
using System.Net.NetworkInformation;

namespace NetPuls;

/// <summary>Ein Messpunkt: Download/Upload in Bytes pro Sekunde.</summary>
public readonly record struct Sample(DateTime Time, double Down, double Up);

/// <summary>Ringpuffer der letzten Messpunkte.</summary>
public sealed class TrafficHistory
{
    private readonly Sample[] _buffer;
    private int _start;
    public int Count { get; private set; }
    public int Capacity => _buffer.Length;

    public TrafficHistory(int capacity) => _buffer = new Sample[capacity];

    public void Add(Sample s)
    {
        if (Count < _buffer.Length)
        {
            _buffer[(_start + Count) % _buffer.Length] = s;
            Count++;
        }
        else
        {
            _buffer[_start] = s;
            _start = (_start + 1) % _buffer.Length;
        }
    }

    /// <summary>Die letzten n Messpunkte, älteste zuerst.</summary>
    public Sample[] Last(int n)
    {
        n = Math.Min(n, Count);
        var result = new Sample[n];
        int offset = Count - n;
        for (int i = 0; i < n; i++)
            result[i] = _buffer[(_start + offset + i) % _buffer.Length];
        return result;
    }

    public Sample? Latest => Count == 0 ? null : _buffer[(_start + Count - 1) % _buffer.Length];
}

/// <summary>Liest einmal pro Sekunde die Byte-Zähler der Netzwerkkarten aus.</summary>
public sealed class TrafficSampler : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly Stopwatch _clock = new();
    private readonly Dictionary<string, (long Rx, long Tx)> _last = new();
    private NetworkInterface[] _interfaces = Array.Empty<NetworkInterface>();
    private int _ticksSinceRefresh = int.MaxValue;
    private string? _adapterId;
    private EtwInternetCounter? _etw;
    private int _etwSilentSeconds;
    private bool _internetOnly;

    /// <summary>Kurzbeschreibung, was gerade gemessen wird (für die Anzeige).</summary>
    public string ModeInfo { get; private set; } = "";
    /// <summary>true, wenn tatsächlich nur Internet-Traffic gezählt wird.</summary>
    public bool FilteringActive => _etw != null && _etwSilentSeconds < 15;

    public TrafficHistory History { get; } = new(3600); // 1 Stunde bei 1 s
    public event Action<Sample>? Sampled;

    /// <summary>null = automatisch alle Karten mit Standard-Gateway (= Internet).</summary>
    public string? AdapterId
    {
        get => _adapterId;
        set { _adapterId = value; _ticksSinceRefresh = int.MaxValue; _last.Clear(); }
    }

    public TrafficSampler()
    {
        _timer.Tick += (_, _) => Tick();
        NetworkChange.NetworkAddressChanged += (_, _) => _ticksSinceRefresh = int.MaxValue;
    }

    /// <summary>true = Heimnetz-Traffic herausfiltern (benötigt Administratorrechte).</summary>
    public bool InternetOnly
    {
        get => _internetOnly;
        set { _internetOnly = value; UpdateMode(); }
    }

    private void UpdateMode()
    {
        _etw?.Dispose();
        _etw = null;
        _etwSilentSeconds = 0;
        if (_internetOnly)
        {
            try
            {
                var etw = new EtwInternetCounter();
                etw.Start();
                _etw = etw;
                ModeInfo = "Nur Internet-Traffic (Heimnetz wird abgezogen)";
                return;
            }
            catch (UnauthorizedAccessException)
            {
                ModeInfo = "Achtung: Heimnetz wird mitgezählt – NetPuls läuft ohne Administratorrechte";
            }
            catch (Exception ex)
            {
                ModeInfo = "Achtung: Heimnetz wird mitgezählt – Filter konnte nicht starten (" + ex.Message + ")";
            }
        }
        else
        {
            ModeInfo = "Gesamter Traffic der Netzwerkkarte (inkl. Heimnetz)";
        }
    }

    public void Start()
    {
        if (ModeInfo.Length == 0) UpdateMode();
        Tick();
        _timer.Start();
    }

    public static IEnumerable<NetworkInterface> AllUsableInterfaces() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                     && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                     && n.OperationalStatus == OperationalStatus.Up);

    public static bool HasGateway(NetworkInterface n)
    {
        try
        {
            return n.GetIPProperties().GatewayAddresses.Any(g =>
                !g.Address.Equals(System.Net.IPAddress.Any) &&
                !g.Address.Equals(System.Net.IPAddress.IPv6Any));
        }
        catch { return false; }
    }

    private void RefreshInterfaces()
    {
        try
        {
            var all = AllUsableInterfaces().ToArray();
            if (_adapterId != null)
                _interfaces = all.Where(n => n.Id == _adapterId).ToArray();
            else
            {
                var withGw = all.Where(HasGateway).ToArray();
                _interfaces = withGw.Length > 0 ? withGw : all;
            }
        }
        catch
        {
            _interfaces = Array.Empty<NetworkInterface>();
        }
        _ticksSinceRefresh = 0;
    }

    private void Tick()
    {
        if (_ticksSinceRefresh >= 10) RefreshInterfaces();
        _ticksSinceRefresh++;

        double seconds = _clock.IsRunning ? _clock.Elapsed.TotalSeconds : 0;
        _clock.Restart();

        long dRx = 0, dTx = 0;
        var seen = new HashSet<string>();
        foreach (var nic in _interfaces)
        {
            long rx, tx;
            try
            {
                var s = nic.GetIPStatistics();
                rx = s.BytesReceived;
                tx = s.BytesSent;
            }
            catch { continue; }

            seen.Add(nic.Id);
            if (_last.TryGetValue(nic.Id, out var prev))
            {
                if (rx >= prev.Rx) dRx += rx - prev.Rx;
                if (tx >= prev.Tx) dTx += tx - prev.Tx;
            }
            _last[nic.Id] = (rx, tx);
        }
        foreach (var gone in _last.Keys.Where(k => !seen.Contains(k)).ToList())
            _last.Remove(gone);

        if (_etw != null)
        {
            // Internet = alles auf der Netzwerkkarte minus erkannter Heimnetz-Traffic.
            // Was der Filter nicht eindeutig als Heimnetz erkennt, bleibt also drin.
            var (lanDown, lanUp, events) = _etw.Take();
            dRx = Math.Max(0, dRx - lanDown);
            dTx = Math.Max(0, dTx - lanUp);

            if (events == 0 && dRx + dTx > 50_000) _etwSilentSeconds++;
            else if (events > 0) _etwSilentSeconds = 0;
            ModeInfo = _etwSilentSeconds >= 15
                ? "Achtung: Heimnetz wird mitgezählt – Filter liefert keine Daten"
                : "Nur Internet-Traffic (Heimnetz wird abgezogen)";
        }

        if (seconds < 0.2) return; // erster Aufruf: nur Startwerte merken

        var sample = new Sample(DateTime.Now, dRx / seconds, dTx / seconds);
        History.Add(sample);
        Sampled?.Invoke(sample);
    }

    /// <summary>Diagnosebericht: Modus, Netzwerkkarten, Filterstatistik.</summary>
    public string Diagnostics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Modus: {ModeInfo}");
        sb.AppendLine($"Nur Internet eingestellt: {_internetOnly}, Filter aktiv: {FilteringActive}, Admin: {EtwInternetCounter.IsAdmin()}");
        sb.AppendLine($"Gewählte Karte: {(_adapterId ?? "automatisch")}");
        sb.AppendLine("Gemessene Karten:");
        foreach (var n in _interfaces) sb.AppendLine($"  {n.Name} | {n.Description} | {n.NetworkInterfaceType}");
        sb.AppendLine("Alle aktiven Karten:");
        try
        {
            foreach (var n in AllUsableInterfaces())
            {
                var st = n.GetIPStatistics();
                sb.AppendLine($"  {n.Name} | {n.Description} | {n.NetworkInterfaceType} | Gateway: {HasGateway(n)} | " +
                              $"empfangen {st.BytesReceived:N0} B, gesendet {st.BytesSent:N0} B");
            }
        }
        catch (Exception ex) { sb.AppendLine("  Fehler: " + ex.Message); }
        sb.AppendLine("Letzte 20 Messwerte (Bytes/s):");
        foreach (var x in History.Last(20)) sb.AppendLine($"  {x.Time:HH:mm:ss}  ↓ {x.Down:N0}  ↑ {x.Up:N0}");
        sb.AppendLine();
        sb.AppendLine("Filter:");
        sb.AppendLine(_etw?.Diagnostics() ?? "  (nicht aktiv)");
        return sb.ToString();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _etw?.Dispose();
        _etw = null;
    }
}
