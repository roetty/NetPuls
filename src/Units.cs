namespace NetPuls;

public static class Units
{
    private static readonly string[] ByteUnits = { "B/s", "KB/s", "MB/s", "GB/s" };
    private static readonly string[] BitUnits = { "bit/s", "kbit/s", "Mbit/s", "Gbit/s" };

    /// <summary>Formatiert Bytes/s als z. B. "12,3 MB/s" oder "98,4 Mbit/s".</summary>
    public static string Format(double bytesPerSecond, bool bits)
    {
        double v = bits ? bytesPerSecond * 8 : bytesPerSecond;
        var units = bits ? BitUnits : ByteUnits;
        int i = 0;
        while (v >= 1000 && i < units.Length - 1) { v /= 1000; i++; }
        string num = i == 0 ? v.ToString("0") : v < 10 ? v.ToString("0.0") : v.ToString("0");
        return num + " " + units[i];
    }

    /// <summary>Rundet den Skalen-Höchstwert auf einen "schönen" Wert auf (in Bytes/s).</summary>
    public static double NiceMax(double bytesPerSecond, bool bits)
    {
        double factor = bits ? 8 : 1;
        double v = Math.Max(bytesPerSecond * factor, 1000 * factor);
        double mag = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var f in new[] { 1.0, 2, 4, 5, 8, 10 })
            if (f * mag >= v * 1.05 || f == 10)
                return f * mag / factor;
        return v / factor;
    }
}
