# Architektur

## v1: DERO-WurfPatcher (`src/Patcher.cs`) – im Einsatz

- Eine `.exe` (~33 KB) ohne Installation, gebaut mit `csc.exe` aus .NET Framework 4 (in Windows enthalten). UI in WinForms.
- Ablauf: Den Spielordner automatisch finden (Registry `HKCU\Software\Valve\Steam\SteamPath` + `libraryfolders.vdf`) → `collectables_ft.scr` + `inventory.scr` aus `data0.pak` lesen → Werte parsen → per Regex **nur Zielzeilen** im jeweiligen `Item(...)`-Block ersetzen → Kopfzeile als Marker voranstellen → deterministisches ZIP nach `dataN.pak` schreiben.
- Slot-Wahl: eigene Pak wiederverwenden (erkannt am Marker), sonst den ersten freien Platz `data2` bis `data7`.
- Schützt vor Fehlern: Das Spiel darf nicht laufen (Prozess `DyingLightGame_TheBeast_x64_rwdi`). Der Patcher bricht ab, wenn ein Block nicht eindeutig gefunden wird. Materialwert 0 kommentiert die Zeile aus. Mindestens ein Material muss mehr als 0 kosten.
- `--selftest <spielordner> <ziel.pak>` patcht ohne Oberfläche mit den DeRo-Voreinstellungen und schreibt ein Log. Getestet wird gegen eine Kopie von `data0.pak` in einem Scratch-Ordner.
- DeRo-Voreinstellung: Wurfmesser T1–T4 kosten 5 Teile, 1 Draht und 1 Klinge, liefern 5 Stück und haben Kopfschuss ×3,0.

## v2: geplant (`ui/index.html` ist der Prototyp)

- **UI in HTML/CSS/JS**, dunkles Dying-Light-Design mit Orange `#ff6b1a` und Schrift Bahnschrift.
- **Host:** Die C#-`.exe` startet einen lokalen HTTP-Server (`TcpListener`/`HttpListener` auf localhost) und öffnet `msedge --app=http://localhost:<port>`, also ein Fenster ohne Browser-Leiste. Es bleibt eine einzige Datei ohne Installation. Die Alternative WPF wurde verworfen, weil sie aufwendiger zu gestalten ist.
- **Tweak-Katalog statt fest verdrahteter Logik:** Jede Einstellung wird als Eintrag beschrieben (Datei, Block, Feld, Regex), der Patcher bleibt generisch. Neue Einstellungen brauchen dann nur neue Katalog-Einträge.
- Bereiche: **Global**, Crafting, Loot (Multiplikator pro Material und Geld), Zerlegen (Multiplikator pro Material), Händler (Verkaufsanteile, Kauffaktor).
- **Global** (Wunsch des Users, 2026-09-27) bündelt spielweite Einstellungen: Erfahrung, Todesstrafe, Ausdauer, Heilung, Beast-Modus, Material-Bonus, Reparatur & Haltbarkeit und Inventar (Slots, Stapelgrößen). Der frühere Bereich „Inventar“ ist darin aufgegangen. Chips filtern nach Fortschritt, Überleben, Beast-Modus sowie Ausrüstung & Inventar.
  - Jede Einstellung ist in `extract_data.py` ein Katalog-Eintrag: `id, label, sub, value, ctl (slider|step), fmt, mode, files, params, min/max/step`, optional `base` + `unit` (Vorschau skalierter Werte) und `diff` (Werte der Schwierigkeitsgrade).
  - `mode` sagt dem Patcher, wie die Schwierigkeits-Varianten mitgezogen werden. `scale` = jedes Vorkommen im gleichen Verhältnis (Regler zeigt den Normal-Wert oder einen Faktor), `add` = Differenz auf alle Varianten addieren, `set` = überall derselbe Wert.
  - Sammel-Regler wie „Kosten der Fähigkeiten“ skalieren alle `ActionCost(...)` einer Datei, „XP-Verlust beim Tod“ alle vier `*DeathPenaltyXpLossMultiplier*`.
- Voreinstellungen: Original, DeRo (entspricht v1, damit Koop kompatibel bleibt), Großzügig.
- **Einstellungs-Code:** `DERO1.` + base64url(JSON-Diff gegen die Originalwerte), damit Koop-Partner identische Werte übernehmen. Im Prototyp funktioniert das schon.
- Der Mod-Code im Prototyp ist nur ein Platzhalter (FNV-Hash). Echt ist der MD5 der Pak.
- Händler-Werte gibt es pro Schwierigkeit in eigenen Dateien. Alle Varianten sollen mitgepatcht werden.

## Datenfluss im Prototyp

`tools/extract_data.py` → `ui/data.js` (`window.DATA = {crafting, loot, money, dismantle, trade, global, materials}`) → `ui/index.html`.
Der Zustand `S` enthält pro Bereich Werte bzw. Multiplikatoren. `ORIG` ist der Originalzustand, und Änderungen werden per Deep-Compare gezählt.
In v2 soll die `.exe` diese Daten zur Laufzeit liefern, portiert nach C#.

## Visuelles Testen

- UI: Demo-Server starten und einen Screenshot machen. `file://`-Seiten außerhalb des Projekts zeigt das Browser-Fenster nur als statisches Bild an.
- WinForms: Prozess starten, dann mit PowerShell `GetWindowRect` + `Graphics.CopyFromScreen` → PNG, dabei `SetProcessDPIAware` beachten.
