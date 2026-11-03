# NetPuls – Live-Anzeige des Internet-Traffics (Windows)

Kleines Tray-Programm, das nur eines macht: den aktuellen Download/Upload live anzeigen.

## Was es kann
- **Tray-Symbol als Mini-Diagramm**: obere Hälfte grün = Download, untere Hälfte orange = Upload.
  Mauszeiger drauf zeigt die aktuellen Werte. Linksklick öffnet das große Fenster.
- **Mini-Fenster an der Taskleiste**: kleines Live-Diagramm mit Zahlen, sitzt unten rechts über der Uhr,
  bleibt im Vordergrund, mit der Maus verschiebbar. Doppelklick öffnet das große Fenster.
- **Großes Fenster**: Live-Diagramm mit Achsen, aktuelle Werte, Spitzenwerte, Zeitraum 1–60 Minuten.
- Rechtsklick-Menü: Mini-Fenster an/aus, Netzwerkkarte wählen, Einheit (Mbit/s oder MB/s), Autostart, Beenden.
- Keine Statistik, keine Datenbank, keine Installation. Einstellungen liegen in `%APPDATA%\NetPuls\settings.json`.

## Starten
`NetPuls.exe` doppelklicken. Fertig. (Windows SmartScreen meldet evtl. „unbekannter Herausgeber“,
weil die Datei nicht signiert ist: „Weitere Informationen“ → „Trotzdem ausführen“.)

Tipp: Damit das Tray-Symbol immer sichtbar ist, in Windows 11 unter
*Einstellungen → Personalisierung → Taskleiste → Andere Symbole der Taskleiste* NetPuls einschalten
(in Windows 10 das Symbol aus dem ^-Überlauf in die Taskleiste ziehen).

## Was gemessen wird
„Automatisch (Internet)“ summiert alle aktiven Netzwerkkarten, die ein Standard-Gateway haben,
also normalerweise genau deine LAN- oder WLAN-Verbindung zum Router. Gemessen wird alles, was über diese
Karte läuft (also auch z. B. Kopien zu einem NAS im Heimnetz).
Bei VPN kann der Traffic doppelt gezählt werden – dann im Menü unter *Netzwerkkarte* gezielt eine Karte wählen.

## Selbst bauen
1. .NET 8 SDK installieren: `winget install Microsoft.DotNet.SDK.8`
2. `build.cmd` doppelklicken → `publish\NetPuls.exe`

Zum Ausprobieren geht auch im Ordner `src`: `dotnet run`.

## Dateien
- `TrafficSampler.cs` – liest jede Sekunde die Byte-Zähler der Netzwerkkarten
- `TrafficChart.cs` – zeichnet das Diagramm (groß und kompakt)
- `TrayIconRenderer.cs` – zeichnet das Tray-Symbol
- `MiniWindow.cs`, `MainWindow.cs`, `TrayApp.cs` – Fenster, Menü, Ablauf
