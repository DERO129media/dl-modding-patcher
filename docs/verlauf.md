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

## Nächster Schritt

- v2 im echten Spiel testen: beide patchen, Mod-Code vergleichen, Koop beitreten.
- Die „ungetesteten“ Einstellungen nach und nach im Spiel prüfen, Ergebnisse in `docs/offene-fragen.md` eintragen.
