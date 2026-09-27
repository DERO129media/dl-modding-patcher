# Modding-Mechanik

## Wie das Spiel Mod-Paks lädt

- Die Engine sucht in `ph_ft\source\` nach `data{N}.pak`. Das Muster `{}/data{}.pak` steht in `engine_x64_rwdi.dll` (**verifiziert** per String-Suche).
- Laut Community (Nexus Mods) werden **`data2.pak` bis `data7.pak`** geladen. Bei gleichem Dateipfad gewinnt die **höhere Nummer**. Das ist nicht selbst geprüft, deckt sich aber damit, dass unser Mod wirkt. Es gibt einen Community-Mod „Data Pak Limit Bypass“, der diese Grenze aufhebt.
- Ein Mod-Pak enthält **nur die geänderten Dateien**, mit exakt gleichem Pfad wie in `data0.pak` und kleingeschrieben, z. B. `scripts/inventory/collectables_ft.scr`. Die Datei wird **komplett ersetzt**, nicht zusammengeführt.
- Beim Start zeigt das Spiel „Modified game data detected“. Daran erkennt man, dass ein Mod geladen wurde.

## Koop: byte-gleiche Paks nötig (verifiziert 2026-09-27)

- Wenn die Spieldaten von Host und Gast abweichen, erscheint: *„Beitritt zum Spiel nicht möglich – Sie können dem Spiel nicht beitreten, weil Ihre Spieldaten unterschiedlich sind.“*
- Ursache 1: Nur einer von beiden hat den Mod.
- Ursache 2: Beide hatten dieselben Werte, aber im ZIP stand die **aktuelle Uhrzeit** als Zeitstempel.
- **Lösung:** fester Zeitstempel (2020-01-01) für jeden ZIP-Eintrag. Damit erzeugen gleiche Werte eine byte-gleiche Pak. Der **Mod-Code** sind die ersten 8 Hex-Zeichen des MD5 der Pak-Datei (`XXXX-XXXX`) und wird im Tool angezeigt.
- Danach funktionierte Koop, als beide mit denselben Werten gepatcht hatten.
- Offen: ob das Spiel die ganze Pak-Datei hasht oder nur die Inhalte. Deterministische Paks decken beides ab.
- Folge für v2: **Jede Einstellung muss bei beiden identisch sein**, deshalb gibt es einen Einstellungs-Code zum Teilen.

## Konflikte mit anderen Mods

- Ändern zwei Mods dieselbe Datei, gewinnt die Pak mit der höheren Nummer komplett. Der Patcher warnt, wenn eine fremde `dataN.pak` dieselben Pfade enthält.
- `inventory.scr` (490 KB) und `player_variables.scr` sind große Dateien, die viele Mods anfassen. Deshalb nur Dateien mitpacken, die wirklich geändert wurden.

## Spiel-Updates

- Ein Update kann `data0.pak` ändern. Dann liefert eine alte Mod-Pak veraltete Skripte aus und überschreibt damit neue Inhalte.
- Deshalb liest der Patcher die Originale immer frisch. Er erkennt eine veraltete Pak, indem er die Werte der installierten Pak auf das aktuelle `data0.pak` erneut anwendet und das Ergebnis vergleicht. Weicht es ab, meldet er „veraltet“.
- Unsere Pak markiert sich mit zwei Kopfzeilen in jedem Skript: `// DERO-Patcher - automatisch erzeugt …` und `// DERO-Einstellungen: {JSON}`. Daran erkennt das Tool die eigene Pak und ihre Werte. v1 schrieb nur `// DERO-WurfPatcher v1.0 …`. Solche Paks gelten als „alte Version“.
- Durch den neuen Kopf ist der Mod-Code von v2 anders als bei v1, obwohl der Skript-Inhalt identisch ist. Beim Umstieg müssen beide Koop-Spieler einmal neu patchen.

## Rechte

Der Steam-Ordner unter `C:\Program Files (x86)` ist beim User beschreibbar. Für den Fall, dass nicht, fängt der Patcher `UnauthorizedAccessException` ab und rät zu „Als Administrator ausführen“.
