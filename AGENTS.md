# AGENTS.md

**DERO-Patcher** für **Dying Light: The Beast** (Steam AppID 3008130). Ändert Spielregeln (Rezepte, Loot, Zerlegen, Händler, globale Werte), indem geänderte Skripte in eine eigene `dataN.pak` geschrieben werden. Die Originaldateien bleiben unberührt. Öffentliches Repo: `DERO129media/dl-modding-patcher`, Releases mit Auto-Update.

Kommunikation mit dem User auf **Deutsch**. Alle UI-Texte sind auf Deutsch. Schreibweise immer **„DERO“** (nie „DeRo“), auch in UI, Doku und Kommentaren.

## Befehle

```bash
cmd //c build.bat     # (aus Git Bash; klappt das nicht: PowerShell `cmd /c "<voller Pfad>\build.bat"`) → dist/DERO-Patcher.exe
dist/DERO-Patcher.exe --selftest "<Spielordner-Kopie>" "<ziel>.pak" [dero|<diff.json>]   # patcht ohne UI, schreibt .log
dist/DERO-Patcher.exe --export-data ui/data.js [--game <Ordner>]                         # Daten für die Demo-Vorschau
dist/DERO-Patcher.exe --dev --port 8732 --game "<Spielordner-Kopie>" --ui ui [--ignore-running]
                                                                    # Dev-Server ohne Fenster, UI von Platte, URL: http://127.0.0.1:8732/?t=dev
```

- UI-Vorschau ohne `.exe`: Demo-Server `python -m http.server 8731 --bind 127.0.0.1 --directory ui` (Demo-Modus mit `ui/data.js`).
- Mit echter API: Dev-Server im Hintergrund starten und `http://127.0.0.1:8732/?t=dev` im Browser-Fenster öffnen. Vor dem nächsten Build den Prozess beenden (`taskkill //IM DERO-Patcher.exe //F`), sonst ist die `.exe` gesperrt.
- Test-Spielordner: Kopie von `data0.pak` (+ `data_lang/datade.pak`) unter `<scratchpad>/fakegame/ph_ft/source/`.

## Release

1. Version in `src/Program.cs` (`AssemblyVersion`) erhöhen. Das ist die einzige Stelle.
2. Abschnitt `## vX.Y.Z` in `CHANGELOG.md` schreiben. Er wird zum Release-Text und im Tool angezeigt.
3. Committen, `git tag vX.Y.Z`, `git push --follow-tags`. Die GitHub Action baut und veröffentlicht `DERO-Patcher.exe` + `.sha256` + `.zip`.
4. **Koop:** Ändert eine Version, wie Dateien geschrieben werden, ändert sich der Mod-Code. Das im CHANGELOG erwähnen („bitte beide updaten und neu patchen“).

## Harte Regeln

- **Niemals `data0.pak`/`data1.pak` oder andere Spieldateien verändern.** Nur der Patcher schreibt `ph_ft/source/data2.pak` … `data7.pak`, und auch das nur, wenn der User im Tool klickt. Tests laufen gegen eine **Kopie** im Scratchpad. Kein Edge-Fenster starten, während der User spielt (`running: true` in `/api/state`).
- **Paks müssen deterministisch sein:** gleiche Werte ergeben byte-gleiche Datei (fester ZIP-Zeitstempel, sortierte Einträge, Einstellungen als JSON mit sortierten Schlüsseln). Sonst scheitert Koop mit „Spieldaten unterschiedlich“. Siehe [docs/modding-mechanik.md](docs/modding-mechanik.md).
- Originalwerte immer **live aus `data0.pak`** lesen, nie Spieldateien im Repo mitliefern. `ui/data.js` ist in `.gitignore`.
- Skripte als Latin-1 lesen und schreiben (reines ASCII, CRLF) und nur die Zielzeilen ersetzen. Nach jeder Patch-Änderung per Diff prüfen, dass sonst nichts anders ist (Vorlage: Test „alle Einstellungen“ in [docs/architektur.md](docs/architektur.md)).
- `csc.exe` aus .NET Framework 4 kann nur **C# 5**: kein `$"..."`, kein `?.`, keine `=>`-Member, keine ` `-Zeichenliterale. Umlaute brauchen `/codepage:65001` (steht in build.bat).
- Behauptungen über das Spiel als **verifiziert** oder **angenommen** kennzeichnen. Im Tool steht bei Ungetestetem das Label „ungetestet“.

## Aufbau

```
src/Program.cs      Start, Edge-Fenster (--app), Lebensdauer, CLI (--selftest, --export-data, --dev, --update-now)
src/Server.cs       App-Zustand + lokaler HTTP-Server (Token + Host-Prüfung) + API
src/GameData.cs     Spielordner finden, Skripte/Lokalisierung laden, Modell + Katalog der globalen Einstellungen
src/Patcher.cs      Einstellungen anwenden, deterministische Pak bauen, installierte Mod/Konflikte erkennen
src/Updater.cs      GitHub-Releases prüfen, .exe laden, SHA256 prüfen, sich selbst ersetzen
src/Json.cs         JSON lesen (JavaScriptSerializer) und kanonisch schreiben
ui/index.html       gesamte Oberfläche (wird in die .exe eingebettet); ohne Server Demo-Modus mit ui/data.js
.github/workflows/  Release-Build bei Tag v*
```

## Doku

| Datei | Inhalt |
|---|---|
| [docs/spieldaten.md](docs/spieldaten.md) | Welche Werte in welcher Datei stehen, Skript-Syntax, Beispiele |
| [docs/modding-mechanik.md](docs/modding-mechanik.md) | Wie Paks geladen werden, Koop, Konflikte, Updates |
| [docs/formate.md](docs/formate.md) | Binärformate: Lokalisierung (`.binsloc`), Spielstand (`.sav`) |
| [docs/architektur.md](docs/architektur.md) | Aufbau v2, API, Katalog, UX-Entscheidungen, Updater |
| [docs/offene-fragen.md](docs/offene-fragen.md) | Was noch nicht geklärt oder getestet ist |
| [docs/verlauf.md](docs/verlauf.md) | Chronik: was wann gebaut und entschieden wurde |

Wenn du etwas Neues über das Spiel herausfindest oder eine offene Frage klärst, trag es in die passende Datei unter `docs/` ein.
