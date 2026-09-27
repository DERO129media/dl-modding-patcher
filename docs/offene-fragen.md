# Offene Fragen

Bei Klärung hier abhaken und das Ergebnis in die passende Doku übernehmen.

- [ ] **Zweiter Parameter von `CraftedItem(item, a, b)`**: Menge mit Skill? Obergrenze? Test im Spiel: `a` und `b` verschieden setzen und craften.
- [x] **Wirken die Wurfmesser-Werte im Spiel?** Ja, vom User bestätigt am 2026-09-27: 1 Draht als Kosten, 5 Stück pro Craft.
- [ ] **`AlternativePrice`**: Ist das der Upgrade-Preis beim Craftmaster oder ein alternativer Craft-Preis?
- [ ] **Loot-Semantik**: Wie wirken `LootAmount(n)` und `use X (min_amount, max_amount)` zusammen? Ist die Vorschau im Prototyp (`use.min × ItemCount.min`) richtig?
- [ ] **`PermaWorld()`** in `lootsets_ft.loot`: Zu welchem Modus gehört der zweite `ItemCount`?
- [ ] **Schwierigkeits-Varianten**: Teilweise geklärt: `_easy`/`_hard`/`_nightmare` überschreiben nur Einzelwerte über `main()` (siehe spieldaten.md). Offen bleibt, wofür `_new_jumps` (vollständige Kopie, nirgends referenziert) und `perma_world/` genutzt werden.
- [ ] **Kandidaten aus „Weitere Stellschrauben“** (spieldaten.md) im Spiel testen, bevor sie ins Tool kommen: Wirkt z. B. `CommonCraftDroppedMul` auch außerhalb von Leicht?
- [ ] **Grenze bei den Pak-Nummern**: Werden wirklich nur `data2` bis `data7` geladen, und gewinnt die höhere Nummer? Bisher nur laut Community.
- [ ] **Was prüft der Koop-Abgleich genau?** Die ganze Pak-Datei oder den Inhalt?
- [ ] **Stapelgrößen von Materialien**: Wo stehen die `Item("Craft_Blades")`-Definitionen? Sie sind nicht in `inventory*.scr` unter diesem Namen zu finden.
