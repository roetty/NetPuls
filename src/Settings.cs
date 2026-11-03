using System.Text.Json;
using Microsoft.Win32;

namespace NetPuls;

public sealed class Settings
{
    public string? AdapterId { get; set; }
    public bool UseBits { get; set; } = true;
    /// <summary>Heimnetz-Traffic herausfiltern (nur Internet zählen).</summary>
    public bool InternetOnly { get; set; } = true;
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

/// <summary>
/// Autostart über die Windows-Aufgabenplanung mit höchsten Rechten. So startet NetPuls bei der
/// Anmeldung mit Administratorrechten, ohne jedes Mal die UAC-Abfrage zu zeigen.
/// </summary>
public static class Autostart
{
    private const string TaskName = "NetPuls";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled => RunHidden("schtasks.exe", $"/Query /TN \"{TaskName}\"") == 0;

    public static void Set(bool enabled)
    {
        // Eintrag aus Version 1.0 (Run-Schlüssel) entfernen, der würde ohne Adminrechte starten
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue("NetPuls", false);
        }
        catch { }

        if (!enabled)
        {
            RunHidden("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F");
            return;
        }

        string path = Environment.ProcessPath ?? throw new InvalidOperationException("Programmpfad unbekannt.");
        string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        string ps =
            $"$a = New-ScheduledTaskAction -Execute '{Esc(path)}'; " +
            $"$t = New-ScheduledTaskTrigger -AtLogOn -User '{Esc(user)}'; " +
            $"$p = New-ScheduledTaskPrincipal -UserId '{Esc(user)}' -LogonType Interactive -RunLevel Highest; " +
            "$s = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew; " +
            $"Register-ScheduledTask -TaskName '{TaskName}' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null";
        int code = RunHidden("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{ps}\"");
        if (code != 0) throw new InvalidOperationException($"Aufgabe konnte nicht angelegt werden (Code {code}).");
    }

    private static string Esc(string s) => s.Replace("'", "''");

    private static int RunHidden(string file, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(file, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(30_000);
        return p.HasExited ? p.ExitCode : -1;
    }
}
