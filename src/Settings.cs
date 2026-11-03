using System.Text.Json;
using Microsoft.Win32;

namespace NetPuls;

public sealed class Settings
{
    public string? AdapterId { get; set; }
    public bool UseBits { get; set; } = true;
    public bool ShowMiniWindow { get; set; } = true;
    public int? MiniX { get; set; }
    public int? MiniY { get; set; }
    public int ChartSeconds { get; set; } = 120;
    public bool MainTopMost { get; set; }
    public int? MainX { get; set; }
    public int? MainY { get; set; }
    public int? MainWidth { get; set; }
    public int? MainHeight { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetPuls", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* defekte Datei ignorieren */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* nicht kritisch */ }
    }
}

/// <summary>Autostart über HKCU\...\Run (kein Admin nötig).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NetPuls";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } path)
            key.SetValue(ValueName, $"\"{path}\"");
        else
            key.DeleteValue(ValueName, false);
    }
}
