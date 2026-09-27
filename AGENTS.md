# AGENTS.md

Patcher für **Dying Light: The Beast** (Steam AppID 3008130). Ändert Spielregeln (Rezepte, Loot, Zerlegen, Händler, Inventar), indem geänderte Skripte in eine eigene `dataN.pak` geschrieben werden. Die Originaldateien bleiben unberührt.

Kommunikation mit dem User auf **Deutsch**. Alle UI-Texte sind auf Deutsch.

## Befehle

```bash
python tools/extract_data.py      # Originalwerte + deutsche Namen aus dem Spiel → ui/data.js
cmd //c build.bat                 # (aus Git Bash) baut dist/DERO-WurfPatcher.exe
```

- UI-Vorschau: Demo-Server `python -m http.server 8731 --bind 127.0.0.1 --directory ui`.
- Headless-Test des Patchers: `dist/DERO-WurfPatcher.exe --selftest "<Spielordner-Kopie>" "<ziel>.pak"`. Er schreibt eine `.log` neben die Pak.

## Harte Regeln

- **Niemals `data0.pak`/`data1.pak` oder andere Spieldateien verändern.** Nur der Patcher schreibt `ph_ft/source/data2.pak` … `data7.pak`, und auch das nur, wenn der User im Tool klickt. Tests laufen gegen eine **Kopie** von `data0.pak` im Scratchpad.
- **Paks müssen deterministisch sein:** gleiche Werte ergeben byte-gleiche Datei (fester ZIP-Zeitstempel). Sonst scheitert Koop mit „Spieldaten unterschiedlich“. Siehe [docs/modding-mechanik.md](docs/modding-mechanik.md).
- Originalwerte immer **live aus `data0.pak`** lesen, nie Spieldateien im Repo mitliefern. `ui/data.js` und extrahierte Skripte sind in `.gitignore`, weil sie Eigentum des Entwicklers sind.
- Skripte als Latin-1 lesen und schreiben (reines ASCII, CRLF) und nur die Zielzeilen ersetzen. Nach jeder Patch-Änderung per Diff prüfen, dass sonst kein Byte anders ist.
- `csc.exe` aus .NET Framework 4 kann nur **C# 5**: kein `$"..."`, kein `?.`, keine `=>`-Member. Umlaute brauchen `/codepage:65001` (steht in build.bat).
- Behauptungen über das Spiel als **verifiziert** oder **angenommen** kennzeichnen. Die Doku trennt das bewusst.

## Aufbau

```
src/Patcher.cs          v1: DERO-WurfPatcher (WinForms), nur Wurfmesser – bei User + Koop-Partnerin im Einsatz
ui/index.html           Design-Prototyp v2 (HTML), noch nicht mit Patcher verbunden
tools/extract_data.py   Parser für Rezepte, Loot, Zerlegen, Handel, Slots + Lokalisierung
docs/                   Wissen für künftige Sessions (unten)
```

## Doku

| Datei | Inhalt |
|---|---|
| [docs/spieldaten.md](docs/spieldaten.md) | Welche Werte in welcher Datei stehen, Skript-Syntax, Beispiele |
| [docs/modding-mechanik.md](docs/modding-mechanik.md) | Wie Paks geladen werden, Koop, Konflikte, Updates |
| [docs/formate.md](docs/formate.md) | Binärformate: Lokalisierung (`.binsloc`), Spielstand (`.sav`) |
| [docs/architektur.md](docs/architektur.md) | v1-Aufbau, geplante v2 (HTML-UI + Katalog), Entscheidungen |
| [docs/offene-fragen.md](docs/offene-fragen.md) | Was noch nicht geklärt oder getestet ist |
| [docs/verlauf.md](docs/verlauf.md) | Chronik: was wann gebaut und entschieden wurde |

Wenn du etwas Neues über das Spiel herausfindest oder eine offene Frage klärst, trag es in die passende Datei unter `docs/` ein.
