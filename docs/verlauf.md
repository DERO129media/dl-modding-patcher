# Verlauf

## 2026-09-27

- Spielordner untersucht: Die `.pak` sind ZIPs, das Wurfmesser-Rezept steht in `collectables_ft.scr`.
- **v1 gebaut** (zuerst in `Dokumente\`, später umbenannt in **DERO-WurfPatcher**): Wurfmesser kosten 1 statt 3 Drähte, liefern 5 statt 2 Stück, Kopfschuss ×3,0.
- Der User hat gepatcht, danach scheiterte Koop mit „Spieldaten unterschiedlich“. **Fix:** fester ZIP-Zeitstempel und Mod-Code. Danach lief Koop mit der Koop-Partnerin. Die Werte kommen im Spiel an (1 Draht, 5 Stück).
- Spielstand-Bearbeitung und Live-Trainer wurden besprochen und verworfen. Entscheidung: **Regeln ändern, nicht Bestände.**
- Recherche für v2: Loot, Zerlegen, Händler, Inventar sowie das Lokalisierungsformat geknackt.
- **UI-Prototyp** gebaut, der User findet ihn gut („sieht doch schon ordentlich aus“).
- Repo angelegt: `Desktop\code\dl-modding-patcher`, erster Commit.
- Alle 1.882 Skripte nach weiteren Stellschrauben durchsucht (Liste in spieldaten.md). Die Schwierigkeitsgrade überschreiben nur Einzelwerte.
- Wunsch des Users: spielweite Einstellungen in einem eigenen Bereich **Global** bündeln. Im Prototyp umgesetzt mit 34 Einstellungen. „Inventar“ ist darin aufgegangen.
- UX/UI-Review per Subagent durchgeführt. Alle Befunde umgesetzt, siehe architektur.md → „UX-Entscheidungen“. Außerdem liefert der Extraktor jetzt den vollständigen Inhalt der Material-Bündel mit deutschen Namen.

- **v2 gebaut:**
  - Die `.exe` hostet die HTML-Oberfläche in einem Edge-App-Fenster.
  - Parser und Katalog sind nach C# portiert, der Python-Extraktor ist entfallen.
  - Echtes Patchen mit Einstellungen in der Pak, Live-Mod-Code, Erkennung von v1 und veralteten Mods.
  - Auto-Updater über GitHub-Releases, GitHub Action für den Release-Build.
  - Der User hat entschieden: Repo öffentlich, E-Mail in den Commits bleibt.
- Repo veröffentlicht: https://github.com/DERO129media/dl-modding-patcher. **Release v2.0.0** per GitHub Action gebaut (20 s).
- Updater end-to-end getestet: Eine Kopie mit Version 1.9.9 hat sich per `--update-now` von GitHub auf 2.0.0 aktualisiert (SHA256 geprüft), der zweite Lauf meldet „aktuell“. Der Neustart über die Oberfläche (Fenster schließen und neu öffnen) ist noch nicht live getestet.
- Externes Code-Review (Astra) mit fünf Befunden zur Dateisicherheit, alle bestätigt und in **v2.1.0** umgesetzt: Grenzen nach dem Umrechnen, strenge Eigentumserkennung, Schutz der Originaldateien auch im `--selftest`, Sicherungen und ausfallsicheres Ersetzen, Neuladen bei einem Spiel-Update. Details in architektur.md → „Dateisicherheit“. Die Texte zu „Mod entfernen“ sagen jetzt, dass der Spielstand nicht zurückgesetzt wird.
- Zweites Audit (Astra) mit 3 × P1 und 7 × P2 auf die v2.1.0-Fixes. Alle bestätigt und umgesetzt:
  - Temp-Datei mit Zufallsnamen und `CreateNew`.
  - Prüfung und Austausch auf dieselben Bytes gebunden, mit Rücksicherung und Wiederherstellung.
  - `--selftest` vergleicht die Verzeichniskennung statt des Pfads.
  - `NaN`/`Infinity` werden abgelehnt.
  - Stale-Prüfung nach dem Erzeugen der Pak.
  - Erneute Ladeversuche und eine Fehlerseite, die von selbst zurückkehrt.
  - Namen mit Punkt am Ende werden abgelehnt.
  - Beschädigte Paks werden gesperrt.
  - Alte Einstellungen werden migriert.
  - Selbst gefunden: Kommazahlen in Rezeptmengen wurden still abgeschnitten, jetzt abgelehnt.
- **Release v2.1.0** veröffentlicht.
- Repo für Fremde aufbereitet:
  - README neu: Spieler oben, Entwickler unten, Screenshot `docs/screenshot.png`, Haftung und Marke.
  - `LICENSE` (MIT, Entscheidung des Users), auch in der Release-Zip.
  - `LIESMICH.txt` korrigiert: Der Spielstand wird beim Entfernen nicht zurückgesetzt.
  - Das Tool sagt „Mitspieler“ statt „Partnerin“ (Wunsch des Users).
  - Dabei gefunden: Die Voreinstellung „DERO“ wurde nach dem Neustart als „Eigene“ angezeigt, weil gespeicherte Einstellungen sortierte Schlüssel haben. `eq` vergleicht jetzt unabhängig von der Reihenfolge.
- Git-Historie bereinigt (Wunsch des Users): keine Hinweise auf KI-Werkzeuge in Commits und Dateien, die Anleitung für Agenten heißt jetzt `AGENTS.md`. Commit-IDs und Tags wurden dabei neu geschrieben (Force-Push), die Releases blieben erhalten.
- **v2.2.0 vorbereitet** nach dem ersten Test des Users mit v2.1.1:
  - Die Voreinstellung „DERO“ ist jetzt die Einstellung des Users (22 Änderungen, Mod-Code `0217-2D9D`), gelesen aus der installierten `data2.pak` des Users.
  - Anzeigefehler gefunden: Das Aufladetempo stand auf 1,25, die Anzeige rundete auf ×1,3. `gfmt` zeigt jetzt zwei Nachkommastellen, wenn nötig.
  - Werte anderer Schwierigkeitsgrade als Badges, Banner für alte/veraltete Mod entfernt (steht im Status), Teilen-Dialog heißt „Teilen und übernehmen“ (Knopf bleibt „Teilen“, Wunsch des Users), Status unten links über die Trennlinie gezogen.
- **v2.2.0 veröffentlicht.** Danach (Wunsch des Users): Crafting höchstens 4 Spalten, „Eigene“ immer als vierte Voreinstellung sichtbar; ein Klick darauf stellt die eigenen Werte wieder her, die vor dem Laden einer Voreinstellung galten (v2.2.1).

## Nächster Schritt

- v2 im echten Spiel testen: beide patchen, Mod-Code vergleichen, Koop beitreten.
- Die Einstellungen im Spiel durchtesten, Fehler beheben, Ergebnisse in `docs/offene-fragen.md` eintragen.
