namespace NetPuls;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Nur eine Instanz zulassen
        using var mutex = new Mutex(true, "NetPuls.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("NetPuls läuft bereits (siehe Infobereich der Taskleiste).",
                "NetPuls", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApp());
    }
}
