# dl-modding-patcher

Patcher für **Dying Light: The Beast**: Crafting, Loot, Zerlegen, Händler und Inventar anpassen, ohne die Originaldateien anzufassen.

## Wie der Mod funktioniert

- Die Spieldaten liegen in `ph_ft\source\data0.pak`, einem normalen ZIP mit lesbaren Skripten (`.scr`, `.loot`).
- Der Patcher liest die Originale bei jedem Start frisch aus `data0.pak`, ändert nur die nötigen Zeilen und schreibt sie in eine eigene `data2.pak` bis `data7.pak`. Das Spiel lädt diese Datei zusätzlich, und die höhere Nummer gewinnt.
- „Mod entfernen“ heißt: diese eine Datei löschen.
- **Koop:** Host und Gast brauchen byte-gleiche Paks. Deshalb schreibt der Patcher einen festen Zeitstempel ins ZIP und zeigt einen Mod-Code (MD5-Auszug) zum Vergleichen an.

Ausführliche Doku in [`docs/`](docs/) – Einstieg für KI-Assistenten: [`AGENTS.md`](AGENTS.md).

## Wichtige Spieldateien

| Bereich | Datei in `data0.pak` |
|---|---|
| Rezepte (Kosten, Output) | `scripts/inventory/collectables_ft.scr` |
| Item-Werte (Kopfschuss, Stapel, Preis) | `scripts/inventory/inventory.scr` |
| Schadenswerte | `scripts/damagedefinitions.scr` |
| Loot-Mengen pro Fund | `scripts/inventory/loot/lootsets_ft.loot` |
| Zerlegen, Loot-Container | `scripts/inventory/loot/lootpools_ft.loot` |
| Händler | `scripts/trading/price_manager_params*.scr` (je Schwierigkeit) |
| Spielerwerte, Slots | `scripts/player/player_variables*.scr` |
| Reparaturen | `scripts/inventory/inventory_special.scr` |
| Deutsche Namen | `data_lang\datade.pak` → `texts/pc/pc_de_lang.binsloc` |

## Aufbau

```
src/Patcher.cs          Aktueller DERO-WurfPatcher v1 (WinForms, nur Wurfmesser)
ui/index.html           Design-Prototyp der neuen Oberfläche
tools/extract_data.py   Liest Originalwerte + deutsche Namen → ui/data.js
build.bat               Baut dist\DERO-WurfPatcher.exe (csc aus .NET Framework 4, keine Installation nötig)
LIESMICH.txt            Anleitung für Mitspieler
```

## Entwickeln

```bash
python tools/extract_data.py      # ui/data.js aus dem installierten Spiel erzeugen
build.bat                         # WurfPatcher v1 bauen
```

UI-Vorschau: `python -m http.server 8731 --directory ui` und dann `http://localhost:8731` öffnen.

Test ohne Oberfläche (schreibt nicht ins Spiel):
`dist\DERO-WurfPatcher.exe --selftest "<Kopie des Spielordners>" "<Ziel>.pak"`

## Nächste Schritte

- [ ] UI-Prototyp mit dem Patcher verbinden (Tweak-Katalog statt fest verdrahteter Wurfmesser)
- [ ] Loot/Zerlegen: Mengen-Semantik im Spiel verifizieren
- [ ] Schwierigkeits-Varianten (`_easy`, `_hard`, `_nightmare`) berücksichtigen
- [ ] Einstellungs-Code zum Teilen im echten Tool
