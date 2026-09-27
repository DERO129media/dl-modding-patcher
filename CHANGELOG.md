# Änderungen

Jeder Abschnitt `## vX.Y.Z` wird beim Release automatisch als Text übernommen und im Tool unter „Update verfügbar“ angezeigt.

## v2.1.0

Mehr Sicherheit für Spieldateien und fremde Mods (nach einem externen Code-Review).

- **Grenzen nach dem Umrechnen:** Werte für andere Schwierigkeitsgrade und Perma-World bleiben jetzt in den vorgesehenen Grenzen. Selbstheilung geht auf keinem Schwierigkeitsgrad über 100 %, Munitionsplätze höchstens bis 100 (so steht es im Spielskript).
- **Inventarplätze und Stapelgrößen lassen sich nur noch vergrößern.** Weniger Platz als belegt könnte Gegenstände kosten.
- **Fremde Mods bleiben unangetastet:** Enthält eine Mod-Datei neben DERO-Inhalten auch Fremdes, wird sie weder ersetzt noch gelöscht. Das Tool zeigt einen Hinweis.
- **Beschädigte Mod-Dateien** werden erkannt und nicht übergangen. Das Tool legt dann keine zweite Mod daneben an.
- **Sicherungen:** Vor jedem Ersetzen oder Entfernen wird die bisherige Mod-Datei gesichert (`%LOCALAPPDATA%\DERO-Patcher\Sicherungen`, die letzten 20). Das Ersetzen ist ausfallsicher: Klappt es nicht, wird die alte Datei wiederhergestellt. Ändert ein anderes Programm die Datei währenddessen, bricht das Tool ab und legt sie zurück.
- **Spiel-Update bei offenem Tool:** Aktualisiert Steam das Spiel, während das Tool offen ist, lädt es die neuen Spieldaten automatisch. Deine Einstellungen bleiben erhalten. Ist das Spiel gerade nicht lesbar, versucht es das Tool alle paar Sekunden erneut.
- **Einstellungen aus älteren Versionen** bleiben erhalten. Nur Werte außerhalb der neuen Grenzen werden angepasst, und das Tool zeigt an, welche.
- „Mod entfernen“ erklärt jetzt genauer: Die Spielregeln sind danach wieder original, dein Spielstand wird aber nicht zurückgesetzt.

**Koop:** Wer Selbstheilung über 85 % gestellt hat, bekommt einen anderen Mod-Code. Mehr als 100 Munitionsplätze und verkleinerte Inventarplätze werden nicht mehr angenommen. Bitte beide updaten und neu patchen.

## v2.0.1

- Schreibweise korrigiert: Die Voreinstellung heißt jetzt „DERO“.
- Der Mod-Code bleibt gleich, neu patchen ist nicht nötig.

## v2.0.0

Komplett neues Tool: aus dem WurfPatcher wird der **DERO-Patcher**.

- Neue Oberfläche im eigenen Fenster mit den Bereichen Global, Crafting, Loot, Zerlegen und Händler.
- **Global:** Erfahrung, Todesstrafe, Ausdauer, Heilung, Beast-Modus, Material-Bonus, Reparatur und Inventar (Plätze, Stapelgrößen).
- **Crafting:** alle 37 Rezepte (Kosten, Menge pro Craft, Kopfschuss bei Wurfwaffen).
- Voreinstellungen Original, DERO und Großzügig. Alles lässt sich pro Wert, pro Bereich oder komplett zurücksetzen.
- **Koop:** Der Mod-Code wird schon vor dem Patchen angezeigt. Der Teilen-Code ist komprimiert und passt in eine Discord-Nachricht.
- **Automatische Updates** über GitHub, jede neue Version wird per Prüfsumme kontrolliert.
- Erkennt eine installierte WurfPatcher-v1-Mod und ersetzt sie beim ersten Patchen.
- Erkennt, wenn ein Spiel-Update die Mod veralten lässt.

Hinweis: Alles außer Crafting-Kosten und -Menge ist noch nicht im Spiel getestet und im Tool als „ungetestet“ markiert.
