# Spieldaten: wo was steht

Stand: Spiel-Build `ph-ft_pc_tech_rel-ph-ft-patch07_202609102209` (Patch 07), geprüft am 2026-09-27.

## Installation

```
<Steam>\steamapps\common\Dying Light The Beast\
├─ ph_ft\source\data0.pak        ZIP, 18.078 Einträge: Skripte, GUI, Modelle …  ← hier liegt alles Relevante
├─ ph_ft\source\data1.pak        ZIP, nur maps/
├─ ph_ft\source\data_lang\datade.pak / dataen.pak   Lokalisierung (siehe formate.md)
├─ ph_ft\source\data\build.metadata                 Build-Name
└─ ph_ft\work\bin\x64\DyingLightGame_TheBeast_x64_rwdi.exe   Prozessname ohne .exe
```

`.pak` sind normale ZIPs (Deflate). Mit Python `zipfile` oder .NET `ZipFile` lesbar.

## Skript-Syntax (`.scr`, `.loot`, `.def`)

- Reines ASCII, **CRLF**. Viele Dateien beginnen mit `//This script is generated from Inventory.xlsm`.
- Aufbau: `sub main() { ... }`, `import "datei.scr"`, `use Funktion(...)`, Kommentare mit `//`.
- Items: `Item("Name", CategoryType_X) { Feld(args); ... }`. Namen sind pro Datei eindeutig.
- **Material-IDs haben uneinheitliche Schreibweise:** `Craft_wiring` und `Craft_Wiring` kommen beide vor. Deshalb immer ohne Beachtung von Groß- und Kleinschreibung matchen.

## Crafting-Rezepte: `scripts/inventory/collectables_ft.scr`

374 Blueprints (`ItemType(ItemType_CraftPlan)`). Ohne Waffen-Blueprints (`Craftplan_dlc*`), Mods (`Craftplan_Mod_*`), Charms und Freischaltungen bleiben **37 Rezeptfamilien**.

```
Item("Craftplan_ThrowingKnives_FT_T2", CategoryType_Collectable)
{
    RequiredItem("Craft_Scrap", 5);          // Kosten
    RequiredItem("Craft_wiring", 3);
    RequiredItem("Craft_Blades", 1);
    CraftedItem("Throwable_ThrowingKnife_FT_T2", 2, 5);   // Output
    AlternativePrice("DLC_FT_UpgradeComponent_T1", 1);    // nur T2–T4
    AlternativePrice("Craft_Scrap", 5);  ...               // dieselben Materialien noch mal
    ItemLevel(2, 4);  Color(Color_Blue);
    TokenPrice(EFaction_Exploration, 1);
    RequiredItemToShowInShop("Craftplan_ThrowingKnives_FT_T1");
    NextLevelBlueprintName("Craftplan_ThrowingKnives_FT_T3");
}
```

- `CraftedItem(item, a, b)`: **`a` = Menge pro Craft (verifiziert**: Original 2, auf 5 gepatcht, im Spiel kommen 5 heraus). `b` ist unklar, vielleicht die Menge mit einem Skill oder eine Obergrenze. Der Patcher setzt `b = max(b, a)`.
- Geänderte `RequiredItem`-Mengen wirken im Crafting-Menü (**verifiziert**: 3 → 1 Draht).
- Vorkommende Muster: `(1,1)` ×127, `(1,2)` ×66, `(2,5)` ×21, `(2,2)`, `(4,4)`, `(200,200)` (Flammöl), `(2,3)`.
- `AlternativePrice` ist vermutlich der Upgrade-Preis beim Craftmaster (**angenommen**). Der Patcher ändert Materialmengen dort mit.
- Stufen sind eigene Items `_T1`…`_T4` mit meist gleichen Kosten.

## Item-Werte: `scripts/inventory/inventory.scr`

```
Item("Throwable_ThrowingKnife_FT_T1", CategoryType_Throwable)
{
    MaxStackCount(99);  Price(15);
    DamageName("Throwable_ThrowingKnifeDamage");   → damagedefinitions.scr
    DamageHeadMult(2.5);
    StaminaDamage(4.5);
    IsAutomaticLegendLevelDamageScaling(true);     // Schaden skaliert mit Spielerlevel
}
```

Wurfmesser: T1 240–270, T2 360–400 (`_Upgrade2`), T3 570–630 (`_Upgrade5`), T4 870–960 (`_Upgrade8`). Kopfschuss ist überall ×2,5.

## Schaden: `scripts/damagedefinitions.scr`

`Damage("Name") { Ranged("240-270"); }`. Manche Einträge sind prozentual, z. B. `"8%-10% of MaxHealth"`.

## Loot

**`scripts/inventory/loot/lootsets_ft.loot`**: Menge pro Fund.

```
sub Blades_FT(float weight = 1.0, int min_amount = 1, int max_amount = 1, float prob = 1.0)
{
    Set(Other, weight, min_amount, max_amount, prob)
    {
        ItemCount("Craft_Blades", 1, 1, 1.0);      // normales Spiel
        PermaWorld();
        ItemCount("Craft_Blades", ...);            // Variante für Perma-World-Modus (angenommen)
    }
}
```

- Geld: `Money_Low` 10–40, `Money_Mid` 25–80, `Money_High` 40–120 (`Cash_Cash`).
- Klingen 1, Drähte 1–3, Teile 3–4 pro Fund. Bündel (`*_Craftparts_FT`) liefern mehr, `Apex_Uncommon_Craftparts_FT` gibt 5–10 Klingen.

**`scripts/inventory/loot/lootpools_ft.loot`**: Container und Zerlegen (623 `LootedObject`).

```
LootedObject("Dismantle_T2_Slash")
{
    ColorSet(ColorSet_Equal);
    LootAmount(3);
    use Dismantle_Scrap_T2 (weight = 1.0, min_amount = 1, max_amount = 2);
    use Dismantle_Blades   (weight = 1.0, min_amount = 1, max_amount = 2);
}
```

- **Wie `LootAmount` und `min_amount`/`max_amount` zusammenwirken, ist nicht verifiziert.** Der Prototyp rechnet `use.min × ItemCount.min` bis `use.max × ItemCount.max`.
- Zerlegen gibt es für `Slash`, `Blunt`, `Firearm`, `Ranged` in je T1–T3 sowie für Mod-Typen. Klingen kommen nur aus Klingenwaffen ab T2 (1–2 bzw. 2–3).
- `scripts/inventory/dismantleparams.scr` enthält nur Haltezeiten, keine Mengen.

## Händler

- `scripts/trading/price_manager_params.scr` mit den Varianten `_easy`, `_hard`, `_nightmare` und `_permadeath`:
  `MELEE_WEAPON_SELL_PRICE_MOD 0.05`, `RANGED_… 0.03`, `COMMON_ITEM_… 0.10`, `COLOR_ITEM_… 0.10`, `VALUABLE_… 1.00`,
  `ColorPriceMul(0..6, x)`, `ZoneLevelMul`, `InstalledModsSellPriceModifier(0.5)`.
- In `scripts/player/player_variables.scr`: `Param("ItemBuyFactor", "1.0")` und `ItemSellFactor`.

## Spieler und Inventar: `scripts/player/player_variables.scr`

- 2.555 `Param("Name", "Wert")`. Varianten: `player_variables_easy`, `_hard`, `_nightmare`, `_new_jumps` und `perma_world/perma_world_player_variables.scr`. **Welche davon wann gilt, ist ungeklärt.**
- Slots: Ausrüstung, Verbrauch und Schnellzugriff je 16, Munition 24, Lager Ausrüstung 100, Lager Sonstiges 250.
- `Inventory*MaxStackCount` gilt laut Kommentar nur bei `InventoryUpgradeEnabled=true`, und das steht auf false. Die Stapelgröße kommt daher aus `MaxStackCount` am Item.
- Beispiele: Ausdauerkosten beim Gleiten, Schwimmen und Bogen, Nacht-XP (`NightExp*`), Parkour in der Wut (`AdvancedParkourFury*`), Taschenlampe.

### Schwierigkeits-Varianten (aus dem Code gelesen)

`player_variables_easy.scr` usw. importieren `player_variables.scr` und überschreiben nur einzelne Werte:
`sub PlayerVariables() { use main(); use main_easy(); }`. Ein Wert, der in `main()` gepatcht wird, gilt also in allen Schwierigkeiten, **außer** eine Variante überschreibt ihn. Dann muss die Variante mitgepatcht werden.
`player_variables_new_jumps.scr` ist eine vollständige eigene Kopie mit 2.239 Params und wird von keinem Skript referenziert. Ob die Engine sie nutzt, ist unklar. Vorsichtshalber mitpatchen.

## Weitere Stellschrauben (Kandidaten, Stand 2026-09-27)

Alles einzelne Zahlen in klaren Zeilen, gleiches Muster wie die verifizierten Wurfmesser. Die Wirkung im Spiel ist **angenommen**, bis sie getestet ist.

| Bereich | Datei | Werte (Original) |
|---|---|---|
| Waffen aufwerten | `inventory/weaponenhancmentcosts.scr` | `BaseCost`/`PerRankCost` je Waffentyp × Seltenheit, z. B. Blunt Weiß 20 Teile + 5 Stoff, +2 Teile pro Rang |
| Todesstrafe | `player_variables.scr` | `DeathPenaltyXpLossPercentageLevel1..14` (0,5–1,5 %), `DeathPenaltyXpLossMultiplierDay/Night` 1,0/1,3, `LLDeathPenalty*`. Leicht setzt 0, Schwer 1,3/1,5 |
| Material-Drop-Bonus | `player_variables.scr` | `CommonCraftDroppedMul`/`UncommonCraftDroppedMul` 0,0, auf Leicht 0,25 („adds on top“) |
| XP | `player_variables.scr`, `progression/progressionactions.scr` | `ActionsXPMultiplier` 1,0, `NightXPBonus` 1,0, `NightExp*Reward`, `LegendBonus_Coop(2..4, 1.0)`, `LegendBonus_Difficulty` |
| Ausdauer | `player_variables.scr` | `MaxStaminaMultiplier` 1,0, `StaminaRegenerationMul` 1,0 (Schwer 0,85), Kosten für Gleiten 0,25/Nacht 0,3, Schwimmen 0,075, Bogen 0,375 |
| Wut (Fury) | `player_variables.scr`, `player/player_fury_config.scr` | `FuryMaxPoints` 100, `FuryChargeMultiplier` 1,0, `FuryChargedTime` 10, `ActionCost(...)` je Fury-Aktion |
| Hunger | `player/player_hunger_config.scr` | `ActionCost(...)` 0–1,0 je Aktion |
| Antizin | `player_variables.scr` | `AntizinNightDuration` 350, `AntizinDarkZoneDuration` 350, `PlayersAntizinCapacity` 0,8 |
| Heilung | `healingdefinitions.scr` | `HealingHps("MedkitRegen")`, `HealthRegenerationPotion_Upgrade*`, `MedkitInstant` … |
| Reparatur | `player_variables.scr` | `PerfectRepairChance` 0,0, `MeleeWpnDurabilityMulReduce` 2,0 (Leicht 1,33) |
| Umskillen | `progression/legendlevelconfig.scr` | `Respec { CostCash(5000); }` |
| Friendly Fire | `player_variables.scr` | `FriendlyFireMultiplier` 0,05 (Leicht 0, Schwer 0,1, Albtraum 0,2) |
| Stapelgrößen | `inventory/inventory*.scr` | `MaxStackCount(n)`: Wurfwaffen 99 (75 Items), Medkits 99, Tränke & Booster 99, Munition 999, Dietriche 99, Materialien 9999, Wertsachen 9999 |
| Heilwerte | `healingdefinitions.scr` | `Medkit_Small_FT_T1..4` 125/200/275/350 HP, `HealthRegenerationPotion_FT_T1..4` 0,5–1,25 % MaxHealth/s. Ältere Einträge ohne `_FT` stammen vermutlich aus Dying Light 2 |
| Auto-Heilung | `player_variables*.scr` | `MaxAutoRegenHealthPercent` 34 (Leicht 40, Schwer 25, Albtraum 20), `HealthRegenerationDelay` 7 s (5 / 10 / 12,5) |

**Bewusst nicht anfassen** (Risiko für Stabilität/Koop): Tageszeiten (`daytime.def`: laut Kommentar an KI, Sound und viele andere Dateien gekoppelt), Sprung- und Parkour-Physik, Gegner-Spawns und Dichte, `gameconfig/`, `savegame/`, `versioning/`, Quests und Story.

## Sonstiges

- Reparaturen: `scripts/inventory/inventory_special.scr` enthält `MaxRepairCountByRarity(Color_X, n)`: Weiß 2, Grün 2, Blau 4, Lila 5, Orange 7.
- Wut und Hunger: `scripts/player/player_fury_config.scr`, `player_hunger_config.scr`.
- Item-ID-Versionierung: `scripts/inventory/versioning/inventory_items_version_001.scr`, z. B. `Item(3245, "Throwable_ThrowingKnife_FT_T1")`.
