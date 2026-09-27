# Architektur

## v2: DERO-Patcher (seit 2026-09-27)

Eine `.exe` ohne Installation, gebaut mit `csc.exe` aus .NET Framework 4 (C# 5). Die Oberfläche ist `ui/index.html` und als Ressource eingebettet.

### Ablauf

1. **Start** (`Program.cs`):
   - Named Mutex, damit nur eine Instanz läuft. Nach einem Update wartet die neue Instanz auf die alte.
   - Spielordner aus `%LOCALAPPDATA%\DERO-Patcher\settings.json`, sonst automatisch gesucht (Registry `HKCU\Software\Valve\Steam\SteamPath` + `libraryfolders.vdf`).
2. **Laden** (`GameData.cs`): Alle Skripte, die der Patcher braucht (Regex `GameFiles.Needed`), werden einmal aus `data0.pak`/`data1.pak` gelesen, dazu `datade.pak` (sonst `dataen.pak`) für deutsche Namen. `GameModel.Build` erzeugt daraus das Modell für die UI und den Katalog der globalen Einstellungen.
3. **Server** (`Server.cs`):
   - `TcpListener` auf `127.0.0.1` mit zufälligem Port. Die API verlangt den Header `X-Dero-Token` (Zufallswert pro Start, steht in der URL `?t=`).
   - Der `Host`-Header muss `127.0.0.1:<port>` sein (Schutz gegen DNS-Rebinding). Fremde Webseiten kommen so nicht an die API.
4. **Fenster:**
   - `msedge --app=<url> --user-data-dir=%LOCALAPPDATA%\DERO-Patcher\browser`. Das eigene Profil sorgt für einen eigenen Prozess. Endet er, beendet sich das Tool.
   - Fallback Chrome oder der Standardbrowser. Dann läuft die Lebensdauer über einen Heartbeat (`/api/ping` alle 20 s, `/api/bye` bei `pagehide`, Timeout 5 min).
5. **UI-Start:**
   - Ein kleines Boot-Skript holt `/api/state` und startet dann das eigentliche App-Skript (`<script type="text/plain" id="app">`).
   - Ohne Token in der URL läuft die Seite im **Demo-Modus** mit `ui/data.js`, zum Gestalten ohne `.exe`.
   - Wird das Spiel nicht gefunden, erscheint eine eigene Seite mit „Spielordner wählen“ (FolderBrowserDialog über `/api/browse`).

### API

| Anfrage | Zweck |
|---|---|
| `GET /api/state` | Version, `dataVersion`, Spielordner, Daten, installierte Mod (`pak, code, diff, legacy, outdated` bzw. `pak, foreign`), Konflikte, `running` |
| `POST /api/preview {diff, dataVersion}` | baut die Pak im Speicher und liefert Mod-Code und Dateiliste, ohne etwas zu schreiben. Die UI ruft das 250 ms nach jeder Änderung auf |
| `POST /api/patch {diff, dataVersion}` | schreibt die Pak (Spiel darf nicht laufen). Nutzt den Platz der eigenen Pak, sonst `data2`–`data7` |
| `POST /api/remove` | sichert und löscht die eigene Pak (nicht, wenn sie Fremdes enthält) |
| `POST /api/browse` | Ordnerauswahl, lädt das Spiel neu |
| `GET /api/update[?force=1]`, `POST /api/update` | Update prüfen (10 min Cache) bzw. installieren und neu starten |
| `POST /api/ping`, `POST /api/bye` | Heartbeat. `ping` prüft außerdem auf ein Spiel-Update und liefert `dataVersion` |

### Einstellungen, Katalog, Patchen

- **Diff statt Vollzustand:** Die UI schickt nur Abweichungen vom Original: `{glob:{id:wert}, craft:{familie:[{mats,out,head}]}, loot:{sub:faktor}, money:{sub:faktor}, dis:{item:faktor}, trade:{id:wert}}`. Der Server prüft Bereiche, IDs, Typen und Wertebereiche.
- **Katalog** (`GameModel.BuildGlobal`): Jede globale Einstellung ist ein `GItem`. UI-Felder: `label, sub, value, ctl, fmt, min/max/step, base/unit, diff`. Dazu kommt ein `Apply`-Delegate, der die Zeilen ändert. `mode` gilt für die Schwierigkeits-Varianten:
  - `scale`: jedes Vorkommen im gleichen Verhältnis,
  - `add`: Differenz addieren,
  - `set`: überall derselbe Wert.
- **Wo was gepatcht wird:**
  - Params in *allen* `player_variables*.scr` und `perma_world/*.scr`.
  - Händler in allen `price_manager_params*.scr`.
  - Loot/Zerlegen: `ItemCount` in der jeweiligen `sub` von `lootsets_ft.loot`, nur vor `PermaWorld()`.
  - Stapelgrößen: `MaxStackCount` aller Items der Kategorie, die den Standardwert haben, in allen `inventory*.scr`.
  - Crafting: `RequiredItem`/`AlternativePrice`/`CraftedItem` im Blueprint und `DamageHeadMult` am Item.
  - Inventarplätze skalieren (Perma-World hat eigene, kleinere Werte), Reparaturen, Umskill-Kosten und Koop-Bonus setzen.
- **Pak-Kopf:** Jede geänderte Datei beginnt mit zwei Zeilen:
  ```
  // DERO-Patcher - automatisch erzeugt, bitte nicht von Hand bearbeiten
  // DERO-Einstellungen: {kanonisches JSON des Diffs}
  ```
  Daran erkennt das Tool die eigene Pak und stellt nach dem Start die installierten Werte wieder her (Prüfung siehe „Dateisicherheit“). Das Präfix `// DERO-` erkennt auch v1-Paks (`// DERO-WurfPatcher v1.0`). Diese gelten als „alte Version“ und werden beim ersten Patchen auf demselben Platz ersetzt.
- **Veraltet-Erkennung:** Das Tool wendet den gespeicherten Diff auf das aktuelle `data0.pak` an und vergleicht die MD5-Werte. Weichen sie ab, erscheint ein Hinweis „Mod ist veraltet – neu patchen“.

### Dateisicherheit (zwei externe Reviews 2026-09-27, umgesetzt in v2.1.0)

- **Grenzen nach Skalierung und Rundung:** Ein `GItem` kann `Lo`/`Hi` und `Params` haben. `ParamApply` klemmt jeden geschriebenen Wert (alle Schwierigkeiten, Perma-World) auf diese Grenzen, `Patcher.CheckBounds` prüft danach alle Zeilen und bricht sonst ab. Selbstheilung: 1–100 (Leicht hat 40 statt 34 % und wäre bei 100 % sonst 118 %). Munitionsplätze: bis 100 (Kommentar `limited max is 100` im Skript). Inventarplätze und Stapelgrößen: Minimum ist der Originalwert. `Patcher.Num` lehnt `NaN`/`Infinity` vor jedem Vergleich ab (der JSON-Parser akzeptiert sie, und an ihnen scheitert jeder Bereichsvergleich). Rezeptmengen müssen ganze Zahlen sein (`Patcher.Int`).
- **Eigentumserkennung (`Patcher.Inspect`):** Eine Pak gilt nur als eigene, wenn *jeder* Eintrag mit `// DERO-` beginnt, zu `GameFiles.Needed` passt und alle dieselben Einstellungen tragen. Sonst ist `Foreign` gesetzt, und Patchen/Entfernen werden abgelehnt. Eine gesperrte, unlesbare oder beschädigte Pak (kein gültiges ZIP) gilt ebenfalls als `Foreign`, damit das Tool nicht auf einen anderen Platz ausweicht und zwei DERO-Mods entstehen. `null` heißt nur noch: gültiges Archiv ohne DERO-Inhalt.
- **Namensregeln (`CheckName`)**, geprüft am Namen *vor* `GetFullPath` (das schneidet Punkte und Leerzeichen am Ende ab): kein `.`/Leerzeichen am Ende, kein `:` (Datenstrom), nie `data0.pak`, `data1.pak` oder andere `data*.pak` außerhalb von `data2`–`data7`. Gilt auch für `--selftest`.
- **Eigentum (`ReadOwned`):** Eine vorhandene Zieldatei wird *einmal* gelesen. Prüfung, Sicherung und der spätere Vergleich beziehen sich auf genau diese Bytes.
- **Ersetzen (`WriteFile`):**
  1. Neue Datei unter zufälligem Namen (`data2.pak.<zufall>.neu`) mit `FileMode.CreateNew` und `WriteThrough`. So wird nie eine vorhandene `.tmp` oder ein Hardlink beschrieben.
  2. Gelesene Bytes nach `%LOCALAPPDATA%\DERO-Patcher\Sicherungen` (Zeitstempel mit Millisekunden, die letzten 20 bleiben).
  3. `File.Replace` mit eigener Rücksicherung `data2.pak.<zufall>.alt` neben dem Ziel. Scheitert der Tausch und fehlt das Ziel danach (ReplaceFile-Fehler 1177), wird `.alt` zurückgelegt. Ist der Zustand danach unklar, bleiben `.neu` und `.alt` liegen, statt gelöscht zu werden.
  4. Ist `.alt` nicht mehr byte-gleich mit der geprüften Datei (ein anderes Programm hat sie zwischendurch ersetzt), wird sie zurückgelegt und der Patch abgebrochen.
  - Identischer Inhalt wird nicht neu geschrieben. Die Reste-Namen enden nicht auf `.pak`, das Spiel lädt sie also nicht.
- **Entfernen (`RemoveFile`):** sichern, in `data2.pak.<zufall>.entfernt` umbenennen und vergleichen. Stimmt der Inhalt nicht, wird die Datei zurückgelegt, sonst gelöscht.
- **`--selftest`** verweigert den Ordner des echten Spiels (Exit 3, es wird nichts geschrieben). Verglichen wird die Dateikennung von Windows (Volume-Seriennummer + Dateiindex über `GetFileInformationByHandle`), nicht der Pfad. So zählen auch Junctions, Symlinks und 8.3-Kurznamen. Ist sie nicht ermittelbar, gilt ebenfalls Exit 3.
- **Spiel-Update bei offenem Tool:** `App.Refresh` vergleicht Größe und Änderungszeit von `data0/1.pak` und `datade/en.pak` mit dem Stand beim Laden und lädt bei Abweichung neu (`DataVersion++`). Aufgerufen bei `state`, `ping`, `preview` und `patch`.
  - `LoadGame` nimmt den Stempel vor *und* nach dem Lesen. Weichen sie ab, gilt das Laden als gescheitert.
  - Ein gescheitertes Laden (z. B. Datei während eines Updates gesperrt) wird spätestens nach 15 s wiederholt.
  - `patch` prüft den Stempel nach dem Erzeugen der Pak erneut und antwortet bei einer Änderung mit `stale`, statt die alte Fassung zu installieren. Schickt die UI eine alte `dataVersion`, gibt es ebenfalls `stale`.
  - Bei `stale` legt die UI ihre Einstellungen in `sessionStorage` ab und lädt neu. Beim Ping passiert das nur, wenn kein Dialog offen ist.
  - Die Fehlerseite („Spieldaten gerade nicht lesbar“) fragt alle 5 s nach und öffnet die Oberfläche von selbst wieder. Die gemerkten Einstellungen bleiben bis dahin erhalten.
- **Migration alter Einstellungen:** `validate(d, fixes)` arbeitet für gespeicherte Einstellungen (installierte Mod, Wiederherstellung) nachsichtig. Globale Werte außerhalb der Grenzen werden auf die Grenze gesetzt, anderes Ungültiges bleibt original, alle gültigen Werte bleiben erhalten. Jede Anpassung steht in einem Hinweis oben („Gespeicherte Einstellungen angepasst … neu patchen“). Teilen-Codes werden weiterhin streng geprüft.
- **Spielstand:** „Mod entfernen“ löscht nur die Pak. Was mit dem Mod erspielt und gespeichert wurde, bleibt im Spielstand. Das Tool sagt das im Entfernen- und im Patchen-Dialog.

### Tests (2026-09-27)

- **DERO per `--selftest`:** Der Inhalt ist identisch mit der im Spiel verifizierten v1-Pak (nur die Kopfzeilen unterscheiden sich). Code `ECC4-CE33`, deterministisch.
- **Alle 34 globalen Einstellungen, 4 Rezepte, alle Loot-, Zerlege- und Händlerwerte gleichzeitig:**
  - 22 Dateien geändert.
  - Jede Datei hat dieselbe Zeilenzahl, CRLF bleibt erhalten.
  - Geändert sind nur die Zielzeilen (per Python-Diff geprüft).
- **C#-Datenexport:** identisch mit dem früheren Python-Extraktor.
- **v2.1.0 (Dateisicherheit), gegen die Spielkopie:**
  - Alle 34 globalen Einstellungen je auf Min und Max: kein Fehler. „Alles gleichzeitig“: 22 Dateien, nur Zahlen in den Zielzeilen geändert. DERO weiter `ECC4-CE33`.
  - Selbstheilung 100 %: Normal und Leicht 100, Schwer 74, Albtraum 59. Munition 999 und Inventar 1 werden abgelehnt, Munition 100 ergibt Perma-World 33.
  - Ziel `data0.pak`/`data1.pak`, eine fremde Pak oder eine gemischte Pak (DERO + fremde Datei): abgelehnt, Dateien byte-gleich.
  - v1-Pak wird als eigene erkannt und ersetzt. Gesperrte Ziel-Pak: `IOException`, alte Datei unverändert, keine `.tmp` übrig.
  - Dev-Server: Zeitstempel von `data0.pak` geändert → `dataVersion` 1 → 2, Patch/Vorschau mit 1 → `stale`. Die UI lädt nach dem nächsten Ping neu und übernimmt die Einstellungen. Gemischte Pak: Hinweis in der UI, Patchen/Entfernen per API abgelehnt. Entfernen legt eine Sicherung an.
- **v2.1.0, nach dem zweiten Review:**
  - Hardlink `dero.pak.tmp` → Kopie von `data0.pak`: Die Kopie bleibt nach Neuanlage und Ersetzen byte-gleich, es bleiben keine `.neu`/`.alt`/`.entfernt` übrig.
  - `data0.pak.`, `data1.pak `, `data8.pak.`, `dero.pak.` und `x.pak:strom`: abgelehnt, nichts angelegt.
  - Abgeschnittene DERO-Pak in `data2`: als „kein lesbares Archiv“ gesperrt, Patchen per API abgelehnt.
  - `NaN`, `Infinity` und Kommazahlen im Rezept: abgelehnt (per `--selftest` und per API).
  - Verzeichniskennung: echter `source`-Ordner, eine Junction darauf und der 8.3-Kurzpfad ergeben dieselbe Kennung, die Spielkopie eine andere. Getestet nur lesend, ohne Schreibversuch im echten Spielordner.
  - `data0.pak` geändert und gesperrt: Die UI wechselt auf die Fehlerseite und kommt nach der Freigabe innerhalb von etwa 20 s von selbst zurück, die geänderte Einstellung ist übernommen.
  - Pak mit v2.0.1-Einstellungen (Munition 150, XP ×2, DERO-Wurfmesser): XP und Wurfmesser bleiben, Munition wird 100, der Hinweis erscheint. Nach dem Patchen: „✓ so installiert“.
  - Regression unverändert: 34 × Min/Max ohne Fehler, „alles gleichzeitig“ `A6B4-A805`, DERO `ECC4-CE33`.
  - Nicht getestet: der Wettlauf mit einem anderen Programm während `File.Replace` und der Fehlerfall 1177 (beides nur im Code nachvollzogen), FAT32.
- **UI im Dev-Modus gegen die Spielkopie:**
  - v1-Mod erkannt.
  - Patchen ersetzt `data2.pak`.
  - Nach dem Neuladen steht „✓ so installiert“.
  - Eine Änderung ergibt einen neuen Code, zurück ergibt den alten.
  - „Mod entfernen“ funktioniert.
  - Ohne Token oder mit fremdem Host antwortet der Server mit 403.

### Updater

- Quelle: `https://api.github.com/repos/DERO129media/dl-modding-patcher/releases/latest`. Das Release braucht die Assets `DERO-Patcher.exe` und `DERO-Patcher.exe.sha256`.
- Ablauf:
  1. Beide Dateien laden und SHA256 vergleichen.
  2. `exe` → `exe.old` umbenennen (erlaubt Windows auch bei laufender Datei) und `exe.new` → `exe`.
  3. Edge-Fenster schließen, neue Instanz mit `--after-update` starten.
  4. Die neue Instanz löscht `.old` beim Start.
- Die UI prüft 1,2 s nach dem Start (Hinweis oben) und über „Nach Updates suchen“ unten links. Ohne Internet passiert nichts.
- `--update-now` aktualisiert ohne Oberfläche (für Tests und Support) und schreibt `<exe>.update.log`.
- **Teilen-Code** enthält die Tool-Version (`{v, d}`). Bei abweichender Version warnt die UI, weil der Mod-Code sich dann unterscheiden kann.
- Build und Release macht die GitHub Action `.github/workflows/release.yml` bei jedem Tag `v*`. Sie prüft, dass Tag und `AssemblyVersion` zusammenpassen.

### Sonstige Entscheidungen

- WPF wurde verworfen (aufwendiger zu gestalten), ebenso `HttpListener`: Der braucht für manche Präfixe Admin-Rechte bzw. URL-ACLs. `TcpListener` mit eigenem Mini-HTTP reicht.
- Der Python-Extraktor wurde durch `--export-data` ersetzt, damit es nur eine Implementierung gibt.
- Der Einstellungs-Code wird im Browser komprimiert (CompressionStream), der Mod-Code kommt vom Server. So bleibt die Pak-Erzeugung an genau einer Stelle.

## v1: DERO-WurfPatcher (bis 2026-09-27, nur noch in der Git-Historie)

WinForms, nur Wurfmesser (Kosten, Output, Kopfschuss) für T1–T4. Mechanik wie v2 (deterministische Pak, Mod-Code), Kopfzeile `// DERO-WurfPatcher v1.0`.

## Oberfläche

- **UI in HTML/CSS/JS**, dunkles Dying-Light-Design mit Orange `#ff6b1a` und Schrift Bahnschrift.
- Bereiche: **Global**, Crafting, Loot (Multiplikator pro Material und Geld), Zerlegen (Multiplikator pro Material), Händler (Verkaufsanteile, Kauffaktor).
- **Global** (Wunsch des Users, 2026-09-27) bündelt spielweite Einstellungen: Erfahrung, Todesstrafe, Ausdauer, Heilung, Beast-Modus, Material-Bonus, Reparatur & Haltbarkeit und Inventar (Slots, Stapelgrößen). Chips filtern nach Fortschritt, Überleben, Beast-Modus sowie Ausrüstung & Inventar.
- Voreinstellungen: Original, DERO (Wurfmesser wie v1), Großzügig.
- **Einstellungs-Code:** `DERO2.` + base64url(deflate-raw(`{"v": Version, "d": Diff}`)). Bei „Großzügig“ sind das etwa 1.500 Zeichen, passt in eine Discord-Nachricht. `DERO1.` (unkomprimiert, nur Diff) wird weiterhin gelesen. Beim Einfügen wird sofort geprüft und zusammengefasst, Übernehmen lässt sich rückgängig machen.
- Der **Mod-Code** steht live in der Aktionsleiste, zusammen mit „✓ so installiert“ oder „noch nicht gepatcht“.

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

## Visuelles Testen

- UI: Demo-Server starten und einen Screenshot machen. `file://`-Seiten außerhalb des Projekts zeigt das Browser-Fenster nur als statisches Bild an.
- Mit echter API: `DERO-Patcher.exe --dev --port 8732 --game <Spielkopie> --ui ui --ignore-running` im Hintergrund, dann `http://127.0.0.1:8732/?t=dev` öffnen. Kein echtes Edge-Fenster starten, solange der User spielt.
