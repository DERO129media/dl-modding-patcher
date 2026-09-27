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
- **Einstellungs-Code:** `DERO2.` + base64url(deflate-raw(JSON-Diff gegen die Originalwerte)), damit Koop-Partner identische Werte übernehmen. Bei „Großzügig“ sind das etwa 1.500 Zeichen und passen damit in eine Discord-Nachricht. `DERO1.` (unkomprimiert) wird weiterhin gelesen.
  - Beim Einfügen wird der Code sofort geprüft: bekannte Bereiche und IDs, Typen und Wertebereiche. Das Tool zeigt eine Zusammenfassung, erst dann wird „Übernehmen“ aktiv. Übernehmen lässt sich per Toast rückgängig machen.
- Der Mod-Code steht **live in der Aktionsleiste**, zusammen mit „✓ so installiert“ oder „noch nicht gepatcht“, damit beide vorher vergleichen können. Im Prototyp ist er ein Platzhalter (FNV-Hash). In v2 soll die `.exe` die Pak im Speicher bauen und ihren MD5 liefern, ohne etwas zu schreiben.

### UX-Entscheidungen (UX-Review 2026-09-27, umgesetzt)

- **Voreinstellung laden** fragt nach, wenn gerade eigene Werte eingestellt sind („Eigene“). Danach gibt es immer einen Toast mit „Rückgängig“. Dasselbe gilt für „Bereich zurücksetzen“ und „Code übernehmen“.
- **Zurücksetzen** ist auf drei Ebenen möglich: ↺ pro Regler-Zeile, Klick auf den durchgestrichenen Originalwert am Stepper und „Bereich zurücksetzen“ in der Toolbar. Ein Strich auf dem Regler markiert den Originalwert.
- **Wertanzeige:** Rechts steht der aktuelle Wert. In der Mitte steht die Wirkung (z. B. „3–4 → 6–8 Stück“), wo es eine gibt, sonst „Original“ bzw. „vorher X“. So wird nichts doppelt angezeigt.
- **Crafting-Stufen:** Alle 37 Rezepte kosten im Original auf T1–T4 gleich viel. Deshalb gilt eine Karte standardmäßig für alle Stufen. „Stufen einzeln anpassen“ blendet die Stufen-Knöpfe ein, geänderte Stufen tragen einen Punkt. Weichen die Stufen voneinander ab, erscheint „Alle Stufen wie Tx setzen“. Dazu gibt es den Filter „Nur geänderte“.
- **Ehrlichkeit:** Das Label „ungetestet“ steht an allem, was noch nicht im Spiel verifiziert ist. Verifiziert sind bisher nur Crafting-Kosten und -Output.
- **Patchen-Dialog** in drei Schritten:
  1. Zusammenfassung mit dem Hinweis „Spiel muss geschlossen sein“, den Originaldateien und „Mod entfernen“.
  2. Fortschritt.
  3. Ergebnis mit „Mod-Code kopieren“ und dem Koop-Hinweis samt „Teilen-Code kopieren“.

  Steht alles auf Original, wird nicht gepatcht, stattdessen bietet der Dialog „Mod entfernen“ an.
- Die Sidebar zeigt den echten Status: Mod aktiv mit Pak und Code, dazu „Mod entfernen“, oder „Kein Mod installiert“.
- **Barrierefreiheit:**
  - `aria-label` an allen Reglern und Steppern, `aria-pressed` an Chips und Stufen, `aria-current` in der Navigation.
  - Dialoge mit `role=dialog` und Fokusfalle, der Fokus kehrt danach zurück.
  - Nach jedem Neuzeichnen wird der Fokus wiederhergestellt.
  - Orange `:focus-visible`-Ringe, `--dim` auf #7d8795 aufgehellt, damit der Kontrast reicht.
- Unter 900 px wird die Seitenleiste zur Tab-Leiste oben, und die Regler-Zeilen brechen um.
- **Stepper-Eingaben** zeigen keinen Tausenderpunkt. `parseNum` versteht „5.000“, „5000“ und „2,5“ (Bug: 5.000 + 500 ergab 505).
- Händler-Werte gibt es pro Schwierigkeit in eigenen Dateien. Alle Varianten sollen mitgepatcht werden.

## Datenfluss im Prototyp

`tools/extract_data.py` → `ui/data.js` (`window.DATA = {crafting, loot, money, dismantle, trade, global, materials}`) → `ui/index.html`.
Der Zustand `S` enthält pro Bereich Werte bzw. Multiplikatoren. `ORIG` ist der Originalzustand, und Änderungen werden per Deep-Compare gezählt.
In v2 soll die `.exe` diese Daten zur Laufzeit liefern, portiert nach C#.

## Visuelles Testen

- UI: Demo-Server starten und einen Screenshot machen. `file://`-Seiten außerhalb des Projekts zeigt das Browser-Fenster nur als statisches Bild an.
- WinForms: Prozess starten, dann mit PowerShell `GetWindowRect` + `Graphics.CopyFromScreen` → PNG, dabei `SetProcessDPIAware` beachten.
