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

    public void Start()
    {
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

        if (seconds < 0.2) return; // erster Aufruf: nur Startwerte merken

        var sample = new Sample(DateTime.Now, dRx / seconds, dTx / seconds);
        History.Add(sample);
        Sampled?.Invoke(sample);
    }

    public void Dispose() => _timer.Dispose();
}
