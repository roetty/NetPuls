using System.Net;
using System.Net.Sockets;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace NetPuls;

/// <summary>
/// Zählt nur Internet-Traffic: Über die Windows-Ereignisablaufverfolgung (ETW, Anbieter
/// "Microsoft-Windows-Kernel-Network") wird jede TCP/UDP-Übertragung mit Quell- und Zieladresse
/// gemeldet. Übertragungen, deren Gegenstelle im Heimnetz liegt (private, Link-Local-,
/// Loopback- und Multicast-Adressen), werden ignoriert. Benötigt Administratorrechte.
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
    private long _down, _up, _events;

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

    /// <summary>Gibt die seit dem letzten Aufruf gezählten Bytes zurück und setzt die Zähler zurück.</summary>
    public (long Down, long Up, long Events) Take() =>
        (Interlocked.Exchange(ref _down, 0), Interlocked.Exchange(ref _up, 0), Interlocked.Exchange(ref _events, 0));

    private void OnEvent(TraceEvent data)
    {
        if (data.ProviderGuid != KernelNetwork) return;
        int id = (int)data.ID;
        bool send = SendIds.Contains(id);
        if (!send && !RecvIds.Contains(id)) return;

        try
        {
            long size = Convert.ToInt64(data.PayloadByName("size"));
            // Gegenstelle: beim Senden das Ziel, beim Empfangen die Quelle
            var remote = ToAddress(data.PayloadByName(send ? "daddr" : "saddr"));
            Interlocked.Increment(ref _events);
            if (remote == null || IsLocal(remote)) return;
            if (send) Interlocked.Add(ref _up, size);
            else Interlocked.Add(ref _down, size);
        }
        catch { /* unerwartetes Ereignisformat ignorieren */ }
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
