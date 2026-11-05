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
    /// <summary>"Free", "Taskbar" oder "Off". Leer = aus ShowMiniWindow übernehmen (alte Einstellungen).</summary>
    public string? MiniMode { get; set; }
    public int TaskbarOffset { get; set; }

    public MiniMode GetMiniMode() =>
        Enum.TryParse<MiniMode>(MiniMode, out var m) ? m : ShowMiniWindow ? NetPuls.MiniMode.Free : NetPuls.MiniMode.Off;
    public int? MiniX { get; set; }
    public int? MiniY { get; set; }
    public int ChartSeconds { get; set; } = 120;
    public bool MainTopMost { get; set; }
    public int? MainX { get; set; }
    public int? MainY { get; set; }
    public int? MainWidth { get; set; }
    public int? MainHeight { get; set; }

    public static string FolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetPuls");
    private static string FilePath => Path.Combine(FolderPath, "settings.json");

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
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>NetPuls: Live-Anzeige des Internet-Traffics</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{Xml(user)}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{Xml(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author"><Exec><Command>{Xml(path)}</Command></Exec></Actions>
            </Task>
            """;
        string file = Path.Combine(Path.GetTempPath(), "NetPuls-Autostart.xml");
        File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
        try
        {
            int code = RunHidden("schtasks.exe", $"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
            if (code != 0) throw new InvalidOperationException($"Aufgabe konnte nicht angelegt werden (Code {code}).");
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    private static string Xml(string s) => System.Security.SecurityElement.Escape(s) ?? s;

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
