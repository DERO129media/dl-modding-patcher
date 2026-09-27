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
- [x] **Stapelgrößen von Materialien**: Sie stehen doch in `inventory.scr` (`Item("Craft_Scrap", CategoryType_CraftComponent)`), alle mit `MaxStackCount(9999)`. Ein Regler lohnt sich nicht.
- [ ] **`CommonCraftDroppedMul`/`UncommonCraftDroppedMul`**: Welche Materialien sind „common“ und welche „uncommon“? Die Material-Items haben nur `Color` (Grün/Blau/Lila), und die Lokalisierung hat keinen Text dazu. Deshalb gibt es im Tool vorerst einen gemeinsamen Regler.
- [ ] **`MeleeWpnDurabilityMulReduce`**: Heißt ein höherer Wert mehr Verschleiß? Das ist nur aus Leicht 1,33 < Normal 2,0 abgeleitet.
- [ ] **Weniger Inventarplätze als belegt:** Was passiert mit Gegenständen in den überzähligen Plätzen? Ungeklärt, deshalb lassen sich Plätze und Stapelgrößen seit v2.1.0 nur vergrößern.
- [ ] **Munitionsplätze über 100:** Im Skript steht `limited max is 100` neben `AmmoSlotsCount`. Ob das Spiel mehr ignoriert oder abstürzt, ist nicht getestet. Das Tool begrenzt auf 100.
- [ ] **`LegendBonus_Coop`**: Gilt er nur für Legendenpunkte oder auch für normale XP?
