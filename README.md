# DERO-Patcher

Patcher für **Dying Light: The Beast**: Crafting-Rezepte, Loot, Zerlegen, Händlerpreise und globale Werte (Erfahrung, Todesstrafe, Ausdauer, Heilung, Beast-Modus, Reparatur, Inventar) anpassen, ohne die Originaldateien anzufassen.

**Download:** [neueste Version](https://github.com/DERO129media/dl-modding-patcher/releases/latest) → `DERO-Patcher.zip` entpacken und `DERO-Patcher.exe` starten. Anleitung in [`LIESMICH.txt`](LIESMICH.txt). Das Tool aktualisiert sich danach selbst.

## Wie der Mod funktioniert

- Die Spieldaten liegen in `ph_ft\source\data0.pak`, einem normalen ZIP mit lesbaren Skripten (`.scr`, `.loot`).
- Der Patcher liest die Originale bei jedem Start frisch, ändert nur die nötigen Zeilen und schreibt sie in eine eigene `data2.pak` bis `data7.pak`. Das Spiel lädt diese Datei zusätzlich.
- „Mod entfernen“ heißt: diese eine Datei löschen.
- **Koop:** Host und Gast brauchen byte-gleiche Paks. Gleiche Einstellungen ergeben immer dieselbe Datei. Der **Mod-Code** zeigt, ob beide gleich sind, und der **Teilen-Code** überträgt die Einstellungen.

Ausführliche Doku in [`docs/`](docs/), Einstieg für KI-Assistenten: [`AGENTS.md`](AGENTS.md).

## Wichtige Spieldateien

| Bereich | Datei in `data0.pak` |
|---|---|
| Rezepte (Kosten, Output) | `scripts/inventory/collectables_ft.scr` |
| Item-Werte (Kopfschuss, Stapel, Preis) | `scripts/inventory/inventory*.scr` |
| Loot-Mengen, Zerlegen | `scripts/inventory/loot/lootsets_ft.loot`, `lootpools_ft.loot` |
| Händler | `scripts/trading/price_manager_params*.scr` (je Schwierigkeit) |
| Spielerwerte, Slots | `scripts/player/player_variables*.scr` |
| Heilung | `scripts/healingdefinitions.scr` |
| Beast-Modus, Hunger | `scripts/player/player_fury_config.scr`, `player_hunger_config.scr` |
| Reparaturen | `scripts/inventory/inventory_special.scr` |
| Deutsche Namen | `data_lang\datade.pak` → `texts/pc/pc_de_lang.binsloc` |

## Entwickeln

Es wird nur Windows gebraucht: `build.bat` nutzt den C#-Compiler aus .NET Framework 4.

```bash
build.bat                                                        # → dist\DERO-Patcher.exe
dist\DERO-Patcher.exe --selftest "<Spielordner-Kopie>" test.pak   # patcht ohne Oberfläche, schreibt test.pak.log
dist\DERO-Patcher.exe --dev --game "<Spielordner-Kopie>" --ui ui  # Dev-Server: http://127.0.0.1:8732/?t=dev
```

**Release:** Version in `src/Program.cs` erhöhen, Abschnitt in `CHANGELOG.md` schreiben, `git tag vX.Y.Z` und pushen. Die GitHub Action baut und veröffentlicht die `.exe`.
