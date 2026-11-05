using System.Net;
using System.Net.Sockets;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace NetPuls;

/// <summary>
/// Erkennt Heimnetz-Traffic: Über die Windows-Ereignisablaufverfolgung (ETW, Anbieter
/// "Microsoft-Windows-Kernel-Network") wird jede TCP/UDP-Übertragung mit Quell- und Zieladresse
/// gemeldet. Gezählt werden hier die Bytes, deren Gegenstelle im Heimnetz liegt (private,
/// Link-Local- und Multicast-Adressen). Der Sampler zieht sie vom Zähler der Netzwerkkarte ab.
/// So wird alles, was nicht eindeutig als Heimnetz erkannt wird, als Internet gezählt.
/// Benötigt Administratorrechte.
/// </summary>
public sealed class EtwInternetCounter : IDisposable
{
    private static readonly Guid KernelNetwork = new("7DD42A49-5329-4832-8DFD-43D979153A88");
    private const string SessionName = "NetPuls-Internet";

    // Ereignis-IDs des Anbieters: Daten gesendet/empfangen
    private static readonly HashSet<int> SendIds = new() { 10, 26, 42, 58 }; // TCPv4, TCPv6, UDPv4, UDPv6
    private static readonly HashSet<int> RecvIds = new() { 11, 27, 43, 59 };

    private TraceEventSession? _session;
    private Thread? _thread;
    private long _lanDown, _lanUp, _events;
    // Statistik für den Diagnosebericht (seit Start)
    private long _totalEvents, _netEvents, _unparsed, _lanBytes, _wanBytes, _loopBytes;
    private readonly Dictionary<int, long> _idCounts = new();
    private readonly List<string> _samples = new();

    public static bool IsAdmin()
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// <summary>Startet die Messung. Wirft eine Ausnahme, wenn das nicht möglich ist.</summary>
    public void Start()
    {
        if (!IsAdmin()) throw new UnauthorizedAccessException("Keine Administratorrechte.");

        _session = new TraceEventSession(SessionName) { StopOnDispose = true };
        _session.EnableProvider(KernelNetwork, TraceEventLevel.Informational, ulong.MaxValue);

        var parser = new RegisteredTraceEventParser(_session.Source);
        parser.All += OnEvent;

        _thread = new Thread(() =>
        {
            try { _session.Source.Process(); }
            catch { /* Sitzung wurde beendet */ }
        }) { IsBackground = true, Name = "NetPuls ETW" };
        _thread.Start();
    }

    /// <summary>Heimnetz-Bytes seit dem letzten Aufruf (und Anzahl Ereignisse), Zähler werden zurückgesetzt.</summary>
    public (long LanDown, long LanUp, long Events) Take() =>
        (Interlocked.Exchange(ref _lanDown, 0), Interlocked.Exchange(ref _lanUp, 0), Interlocked.Exchange(ref _events, 0));

    private void OnEvent(TraceEvent data)
    {
        Interlocked.Increment(ref _totalEvents);
        if (data.ProviderGuid != KernelNetwork) return;
        int id = (int)data.ID;
        lock (_idCounts) _idCounts[id] = _idCounts.GetValueOrDefault(id) + 1;
        bool send = SendIds.Contains(id);
        if (!send && !RecvIds.Contains(id)) return;

        Interlocked.Increment(ref _events);
        Interlocked.Increment(ref _netEvents);
        try
        {
            long size = Convert.ToInt64(data.PayloadByName("size"));
            // Gegenstelle: beim Senden das Ziel, beim Empfangen die Quelle
            object? raw = data.PayloadByName(send ? "daddr" : "saddr");
            var remote = ToAddress(raw);
            AddSample(id, raw, remote, size);

            if (remote == null) { Interlocked.Increment(ref _unparsed); Interlocked.Add(ref _wanBytes, size); return; }
            if (IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote))
            {
                // Loopback läuft nicht über die Netzwerkkarte -> weder abziehen noch zählen
                Interlocked.Add(ref _loopBytes, size);
                return;
            }
            if (!IsLocal(remote)) { Interlocked.Add(ref _wanBytes, size); return; }

            Interlocked.Add(ref _lanBytes, size);
            if (send) Interlocked.Add(ref _lanUp, size);
            else Interlocked.Add(ref _lanDown, size);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _unparsed);
            AddSample(id, "Fehler: " + ex.Message, null, 0);
        }
    }

    private void AddSample(int id, object? raw, IPAddress? parsed, long size)
    {
        lock (_samples)
        {
            if (_samples.Count >= 40) return;
            _samples.Add($"ID {id}: size={size}, adresse roh={raw ?? "null"} ({raw?.GetType().Name ?? "-"}), " +
                         $"erkannt={parsed?.ToString() ?? "NEIN"}, heimnetz={(parsed == null ? "-" : IsLocal(parsed) ? "ja" : "nein")}");
        }
    }

    /// <summary>Text für den Diagnosebericht.</summary>
    public string Diagnostics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ETW-Ereignisse gesamt: {Interlocked.Read(ref _totalEvents)}");
        sb.AppendLine($"Davon Daten gesendet/empfangen: {Interlocked.Read(ref _netEvents)}");
        sb.AppendLine($"Adresse nicht erkannt: {Interlocked.Read(ref _unparsed)}");
        sb.AppendLine($"Bytes Internet: {Interlocked.Read(ref _wanBytes):N0}");
        sb.AppendLine($"Bytes Heimnetz: {Interlocked.Read(ref _lanBytes):N0}");
        sb.AppendLine($"Bytes Loopback: {Interlocked.Read(ref _loopBytes):N0}");
        lock (_idCounts)
            sb.AppendLine("Ereignis-IDs: " + string.Join(", ", _idCounts.OrderBy(k => k.Key).Select(k => $"{k.Key}×{k.Value}")));
        sb.AppendLine("Beispiele:");
        lock (_samples) foreach (var line in _samples) sb.AppendLine("  " + line);
        return sb.ToString();
    }

    private static IPAddress? ToAddress(object? value) => value switch
    {
        IPAddress a => a,
        string s when IPAddress.TryParse(s, out var a) => a,
        byte[] b when b.Length is 4 or 16 => new IPAddress(b),
        int i => new IPAddress(BitConverter.GetBytes(i)),
        uint u => new IPAddress(BitConverter.GetBytes(u)),
        _ => null,
    };

    /// <summary>true, wenn die Adresse im Heimnetz bzw. nicht im Internet liegt.</summary>
    public static bool IsLocal(IPAddress a)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (IPAddress.IsLoopback(a)) return true;

        if (a.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = a.GetAddressBytes();
            return b[0] == 10                                   // 10.0.0.0/8
                || (b[0] == 172 && (b[1] & 0xF0) == 16)         // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16
                || (b[0] == 169 && b[1] == 254)                 // Link-Local
                || b[0] >= 224                                  // Multicast/Broadcast/reserviert
                || b[0] == 0;
        }

        if (a.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = a.GetAddressBytes();
            return a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC                        // fc00::/7 (ULA)
                || a.Equals(IPAddress.IPv6Any);
        }
        return false;
    }

    public void Dispose()
    {
        try { _session?.Dispose(); } catch { }
        _session = null;
    }
}
