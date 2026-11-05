# NetPuls – Live-Anzeige des Internet-Traffics (Windows)

Kleines Tray-Programm, das nur eines macht: den aktuellen Download/Upload live anzeigen.

## Was es kann
- **Tray-Symbol als Mini-Diagramm**: obere Hälfte grün = Download, untere Hälfte orange = Upload.
  Mauszeiger drauf zeigt die aktuellen Werte. Linksklick öffnet das große Fenster.
- **Kleine Grafik in der Taskleiste**: Live-Kurve mit Download/Upload, direkt in die Taskleiste eingesetzt
  (links neben der Uhr, passt sich heller/dunkler Taskleiste an). Alternativ als frei verschiebbares Fenster
  (Menü → „Kleine Grafik“). Doppelklick öffnet das große Fenster.
- **Großes Fenster**: Live-Diagramm mit Achsen, aktuelle Werte, Spitzenwerte, Zeitraum 1–60 Minuten.
- Rechtsklick-Menü: kleine Grafik (Taskleiste/frei/aus), Heimnetz mitzählen an/aus, Netzwerkkarte wählen, Einheit (Mbit/s oder MB/s), Autostart, Hilfe, Über NetPuls, Beenden.
- **Hilfe und Info**: eigenes Fenster mit Bedienungshilfe und Programm-Infos (Menü, „?“-Knopf oder F1 im großen Fenster).
- Keine Statistik, keine Datenbank, keine Installation. Einstellungen liegen in `%APPDATA%\NetPuls\settings.json`.

## Download
[**NetPuls.exe herunterladen**](https://github.com/roetty/NetPuls/raw/main/download/NetPuls.exe) (Windows 10/11, 64 Bit, ca. 8 MB)

Benötigt die **.NET 8 Desktop Runtime** von Microsoft. Fehlt sie, zeigt Windows beim ersten Start einen
Hinweis mit Download-Link. Alternativ einmalig in der Eingabeaufforderung:
`winget install Microsoft.DotNet.DesktopRuntime.8`

## Starten
`NetPuls.exe` doppelklicken. Fertig. (Windows SmartScreen meldet evtl. „unbekannter Herausgeber“,
weil die Datei nicht signiert ist: „Weitere Informationen“ → „Trotzdem ausführen“, oder dauerhaft:
Rechtsklick → Eigenschaften → „Zulassen“.)

Meldet der Browser oder Defender einen „Virus“, ist das ein Fehlalarm bei unsignierten Programmen,
die Adminrechte anfordern. Der komplette Quellcode liegt hier im Repository.

Tipp: Damit das Tray-Symbol immer sichtbar ist, in Windows 11 unter
*Einstellungen → Personalisierung → Taskleiste → Andere Symbole der Taskleiste* NetPuls einschalten
(in Windows 10 das Symbol aus dem ^-Überlauf in die Taskleiste ziehen).

## Was gemessen wird
NetPuls zählt standardmäßig **nur Internet-Traffic**. Übertragungen innerhalb des Heimnetzes
(z. B. Kopien zum NAS, Streaming vom Medienserver, Drucker) werden herausgefiltert.

Dafür misst NetPuls den gesamten Traffic der Netzwerkkarte und zieht davon den Heimnetz-Anteil ab.
Den erkennt es über die Windows-Ereignisablaufverfolgung (ETW): Windows meldet jede TCP/UDP-Übertragung
mit Quell- und Zieladresse. Liegt die Gegenstelle im Heimnetz (10.x, 172.16–31.x, 192.168.x, Link-Local,
IPv6 fc00::/7 und fe80::, Multicast), wird sie abgezogen. Was nicht eindeutig als Heimnetz erkannt wird,
zählt als Internet. Deshalb braucht NetPuls **Administratorrechte**
und fragt beim Start per UAC danach. Mit „Mit Windows starten“ legt NetPuls eine Aufgabe in der
Aufgabenplanung an, dann startet es bei der Anmeldung ohne Nachfrage.

Bei Problemen hilft der Diagnosebericht: Rechtsklick → „Über NetPuls“ → „Diagnosebericht speichern“.

Ob der Filter aktiv ist, steht im Tooltip des Tray-Symbols und unten links im großen Fenster.
Klappt er nicht (z. B. ohne Adminrechte), zählt NetPuls wie vorher den gesamten Traffic der Netzwerkkarte
und zeigt einen Hinweis. Im Menü lässt sich mit „Heimnetz-Traffic mitzählen“ auch bewusst umschalten.
Die Auswahl unter *Netzwerkkarte* gilt nur in diesem Modus.

## Selbst bauen
1. .NET 8 SDK installieren: `winget install Microsoft.DotNet.SDK.8`
2. `build.cmd` doppelklicken → `publish\NetPuls.exe`

Zum Ausprobieren geht auch im Ordner `src`: `dotnet run`.

## Dateien
- `TrafficSampler.cs` – sammelt jede Sekunde die Messwerte
- `EtwInternetCounter.cs` – erkennt Heimnetz-Traffic (ETW), der vom Kartenzähler abgezogen wird
- `TrafficChart.cs` – zeichnet das Diagramm (groß und kompakt)
- `TrayIconRenderer.cs` – zeichnet das Tray-Symbol
- `HelpWindow.cs` – Hilfe- und Info-Fenster
- `MiniWindow.cs`, `MainWindow.cs`, `TrayApp.cs` – Fenster, Menü, Ablauf

## Lizenz und Haftungsausschluss
NetPuls steht unter der [MIT-Lizenz](LICENSE).

Die Nutzung erfolgt auf eigene Gefahr. NetPuls wird kostenlos und ohne jede Gewährleistung
bereitgestellt. Der Autor übernimmt keine Haftung für Schäden, Datenverluste oder Fehlfunktionen,
die durch die Nutzung entstehen, soweit gesetzlich zulässig. Die angezeigten Werte sind Richtwerte
und nicht für Abrechnungszwecke geeignet.
