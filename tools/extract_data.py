"""Sammelt Originalwerte aus data0.pak + deutsche Namen fuer den Design-Prototyp.

Aufruf: python tools/extract_data.py [Spielordner]  ->  schreibt ui/data.js
"""
import json, os, re, struct, sys, zipfile

ROOT = sys.argv[1] if len(sys.argv) > 1 else r"C:\Program Files (x86)\Steam\steamapps\common\Dying Light The Beast"
GAME = os.path.join(ROOT, "ph_ft", "source")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "ui", "data.js")

z = zipfile.ZipFile(GAME + r"\data0.pak")
rd = lambda n: z.read(n).decode("latin1")
col = rd("scripts/inventory/collectables_ft.scr")
inv = rd("scripts/inventory/inventory.scr")
dmg = rd("scripts/damagedefinitions.scr")
sets = rd("scripts/inventory/loot/lootsets_ft.loot")
pools = rd("scripts/inventory/loot/lootpools_ft.loot")
price = rd("scripts/trading/price_manager_params.scr")
pvar = rd("scripts/player/player_variables.scr")
special = rd("scripts/inventory/inventory_special.scr")

# ---- Lokalisierung ----
d = zipfile.ZipFile(GAME + r"\data_lang\datade.pak").read("texts/pc/pc_de_lang.binsloc")
def table(i):
    n = struct.unpack_from("<I", d, i)[0]; i += 4; out = []
    for _ in range(n):
        l = struct.unpack_from("<H", d, i)[0]; i += 2
        out.append(d[i:i + l].decode("utf-8", "replace")); i += l
    return out
keys = table(d.find(b"m_keys") + 10)
vals = table(d.find(b"m_Values") + 12)
LOC = {k.lower(): v for k, v in zip(keys, vals)}

def loc(key, depth=0):
    v = LOC.get(key.strip("&").lower())
    if v is None or depth > 3: return None
    m = re.fullmatch(r"&([^&]+)&", v.strip())
    return loc(m.group(1), depth + 1) if m else v

def item_name(item):
    for k in (item + "_N", item + "_n", re.sub(r"_T\d$", "", item) + "_N", re.sub(r"_FT_T\d$", "_FT", item) + "_N"):
        v = loc(k)
        if v: return v
    return None

def blocks(text, prefix):
    for m in re.finditer(r'(?:Item|LootedObject)\("(' + prefix + r'[^"]*)"[^\n]*\n\s*\{', text):
        i, depth = m.end(), 1
        while depth:
            depth += (text[i] == "{") - (text[i] == "}"); i += 1
        yield m.group(1), text[m.end():i - 1]

MAT = {}
def mat_name(m):
    if m not in MAT: MAT[m] = item_name(m) or m.replace("Craft_", "").replace("_", " ")
    return MAT[m]

# ---- Crafting ----
inv_blocks = dict(blocks(inv, ""))
dmg_ranges = dict(re.findall(r'Damage\("([^"]+)"\)\s*\{\s*Ranged\("([^"]+)"\)', dmg))
COLORS = {"Color_White": "white", "Color_Green": "green", "Color_Blue": "blue", "Color_Violet": "violet", "Color_Orange": "orange"}

def category(item):
    if "ThrowingKnife" in item or item.startswith("Throwable_") and "Knife" in item: return "wurf"
    if item.startswith(("Throwable_",)): return "wurf" if any(s in item for s in ("Star", "Coin", "Decoy")) else "spreng"
    if item.startswith(("Mine_", "Grenade", "PipeBomb", "Remote")): return "spreng"
    if item.startswith(("Medkit", "Potion", "Booster", "Improvised_")): return "heil"
    if item.startswith(("Ammo_", "Bullet_")) or "Arrow" in item or "Bolt" in item: return "muni"
    return "werkzeug"

families = {}
for name, b in blocks(col, "Craftplan_"):
    if "ItemType_CraftPlan" not in b or name.startswith(("Craftplan_dlc", "Craftplan_Mod_", "Craftplan_charm")): continue
    ci = re.search(r'CraftedItem\("([^"]+)",\s*(\d+),\s*(\d+)\)', b)
    if not ci or not re.search(r'RequiredItem\("', b): continue
    item = ci.group(1)
    tier_m = re.search(r"ItemLevel\((\d+),\s*(\d+)\)", b)
    tier = int(tier_m.group(1)) if tier_m else 1
    fam = re.sub(r"_T\d$", "", name)
    ib = inv_blocks.get(item, "")
    head = re.search(r"DamageHeadMult\(([\d.]+)\)", ib)
    dn = re.search(r'DamageName\("([^"]+)"\)', ib)
    entry = {
        "tier": tier,
        "color": COLORS.get((re.search(r"Color\((Color_\w+)\)", b) or [None, ""])[1], "white"),
        "blueprint": name, "item": item,
        "mats": [[m, int(n)] for m, n in re.findall(r'RequiredItem\("([^"]+)",\s*(\d+)\)', b)],
        "out": int(ci.group(2)), "out2": int(ci.group(3)),
    }
    if head: entry["head"] = float(head.group(1))
    if dn and dn.group(1) in dmg_ranges: entry["damage"] = dmg_ranges[dn.group(1)]
    f = families.setdefault(fam, {"id": fam, "cat": category(item), "tiers": []})
    f["name"] = f.get("name") or item_name(item) or loc(re.search(r'Name\("([^"]+)"\)', b).group(1)) or fam
    f["tiers"].append(entry)
for f in families.values():
    f["tiers"].sort(key=lambda e: e["tier"])
    for e in f["tiers"]:
        for m in e["mats"]: mat_name(m[0])

# ---- Loot pro Fund ----
def first_itemcount(sub):
    m = re.search(r"sub " + re.escape(sub) + r"\(.*?\{(.*?)\n\s*\}\s*\n\s*\}", sets, re.S)
    if not m: return None
    ic = re.search(r'ItemCount\("([^"]+)",\s*(\d+),\s*(\d+)', m.group(1))
    return ic and (ic.group(1), int(ic.group(2)), int(ic.group(3)))

loot = []
for sub in re.findall(r"sub (\w+_FT)\(", sets):
    ic = first_itemcount(sub)
    if ic and ic[0].startswith("Craft_"):
        loot.append({"id": sub, "item": ic[0], "name": mat_name(ic[0]), "min": ic[1], "max": ic[2]})
money = []
for sub, label in (("Money_Low", "Kleiner Fund"), ("Money_Mid", "Mittlerer Fund"), ("Money_High", "Großer Fund")):
    ic = first_itemcount(sub)
    money.append({"id": sub, "label": label, "min": ic[1], "max": ic[2]})

# ---- Zerlegen ----
TYPES = {"Slash": "Klingenwaffen", "Blunt": "Stumpfe Waffen", "Firearm": "Schusswaffen", "Ranged": "Bögen & Armbrüste"}
dismantle = []
for name, b in blocks(pools, "Dismantle_T"):
    m = re.match(r"Dismantle_T(\d)_(\w+)", name)
    if not m: continue
    parts = []
    for s, mn, mx in re.findall(r"use (\w+) \(weight = [\d.]+, min_amount = (\d+), max_amount = (\d+)\)", b):
        ic = first_itemcount(s)
        if ic: parts.append({"set": s, "item": ic[0], "name": mat_name(ic[0]), "min": int(mn) * ic[1], "max": int(mx) * ic[2]})
    dismantle.append({"id": name, "type": TYPES.get(m.group(2), m.group(2)), "tier": int(m.group(1)), "parts": parts})

# ---- Handel ----
P = lambda n: float(re.search(r'Param\("' + n + r'",\s*([\d.]+)\)', price).group(1))
PV = lambda n: re.search(r'Param\("' + n + r'",\s*"([^"]*)"\)', pvar).group(1)
trade = {
    "sell": [
        {"id": "MELEE_WEAPON_SELL_PRICE_MOD", "label": "Nahkampfwaffen verkaufen", "value": P("MELEE_WEAPON_SELL_PRICE_MOD")},
        {"id": "RANGED_WEAPON_SELL_PRICE_MOD", "label": "Fernkampfwaffen verkaufen", "value": P("RANGED_WEAPON_SELL_PRICE_MOD")},
        {"id": "COMMON_ITEM_SELL_PRICE_MOD", "label": "Normale Gegenstände verkaufen", "value": P("COMMON_ITEM_SELL_PRICE_MOD")},
        {"id": "COLOR_ITEM_SELL_PRICE_MOD", "label": "Seltene Gegenstände verkaufen", "value": P("COLOR_ITEM_SELL_PRICE_MOD")},
        {"id": "VALUABLE_ITEM_SELL_PRICE_MOD", "label": "Wertsachen verkaufen", "value": P("VALUABLE_ITEM_SELL_PRICE_MOD")},
    ],
    "buy": float(PV("ItemBuyFactor")), "sellFactor": float(PV("ItemSellFactor")),
}

# ---- Global: Spieler, Fortschritt, Ausrüstung ----
# Jede Einstellung: ctl "slider" oder "step", fmt fuer die Anzeige, mode sagt dem Patcher, wie Schwierigkeits-
# Varianten mitgezogen werden: "scale" = alle Vorkommen im gleichen Verhaeltnis, "set" = alle auf denselben Wert.
PV_FILES = {"Normal": pvar}
for k, f in (("Leicht", "easy"), ("Schwer", "hard"), ("Albtraum", "nightmare")):
    PV_FILES[k] = rd(f"scripts/player/player_variables_{f}.scr")

def pvv(name):
    """Wert eines Params in Normal + allen Schwierigkeits-Varianten, die ihn ueberschreiben."""
    out = {}
    for k, t in PV_FILES.items():
        m = re.search(r'^[ \t]*Param\("' + name + r'",\s*"([-\d.]+)"\)', t, re.M)
        if m: out[k] = float(m.group(1))
    return out

prog = rd("scripts/progression/progressionactions.scr")
legend = rd("scripts/progression/legendlevelconfig.scr")
fury = rd("scripts/player/player_fury_config.scr")
hunger = rd("scripts/player/player_hunger_config.scr")
heal = rd("scripts/healingdefinitions.scr")
PVF = "player_variables*.scr"

def G(id, label, value, ctl="slider", fmt="x", mode="scale", files=(PVF,), sub="", **kw):
    e = {"id": id, "label": label, "sub": sub, "value": value, "ctl": ctl, "fmt": fmt, "mode": mode, "files": list(files)}
    e.update(kw)
    return e

def diff_note(name):
    v = pvv(name)
    return v if len(v) > 1 else None

heal_hp = lambda n: float(re.search(r'Healing\w*\("' + n + r'"\)\s*\{\s*Health\("([\d.]+)', heal).group(1))
medkits = [heal_hp(f"Medkit_Small_FT_T{i}") for i in range(1, 5)]
potions = [heal_hp(f"HealthRegenerationPotion_FT_T{i}") for i in range(1, 5)]
fury_costs = {k: float(v) for k, v in re.findall(r'ActionCost\("(\w+)",\s*([\d.]+)', fury)}
hunger_costs = [float(v) for v in re.findall(r'ActionCost\("\w+",\s*([\d.]+)', hunger)]
death_pct = [float(v) for v in re.findall(r'Param\("DeathPenaltyXpLossPercentageLevel\d+",\s*"([\d.]+)"', pvar)]

# Stapelgroessen je Kategorie (haeufigster Wert), ueber alle Inventar-Dateien
stack_cnt = {}
for n in z.namelist():
    if not re.match(r"scripts/inventory/inventory\w*\.scr$", n): continue
    for m in re.finditer(r'Item\("[^"]+",\s*(CategoryType_\w+)\)\s*\{(.*?)\n\s*\}', rd(n), re.S):
        s = re.search(r"MaxStackCount\((\d+)\)", m.group(2))
        if s and int(s.group(1)) > 1:
            c = stack_cnt.setdefault(m.group(1), {})
            c[int(s.group(1))] = c.get(int(s.group(1)), 0) + 1
stack = lambda cat: max(stack_cnt[cat].items(), key=lambda kv: kv[1])
STACKS = (("stack_throw", "Wurfwaffen", ("CategoryType_Throwable", "CategoryType_ThrowableLiquid")),
          ("stack_medkit", "Medkits", ("CategoryType_Medkit",)),
          ("stack_powerup", "Tränke & Booster", ("CategoryType_Powerup",)),
          ("stack_ammo", "Munition", ("CategoryType_Ammo",)),
          ("stack_lockpick", "Dietriche", ("CategoryType_Lockpick",)))

RAR = {"Color_White": "Weiß", "Color_Green": "Grün", "Color_Blue": "Blau", "Color_Violet": "Lila", "Color_Orange": "Orange"}
SLOTS = (("EquipmentSlotsCount", "Ausrüstungsplätze"), ("ConsumableSlotsCount", "Verbrauchsplätze"),
         ("QuickSlotsCount", "Schnellzugriff"), ("AmmoSlotsCount", "Munitionsplätze"),
         ("StorageEquipmentSlotsCount", "Lager: Ausrüstung"), ("StorageOtherSlotsCount", "Lager: Sonstiges"))

glob = [
    {"id": "xp", "chip": "fort", "title": "Erfahrung", "hint": "Wie schnell du aufsteigst.", "items": [
        G("ActionsXPMultiplier", "XP für Aktionen", pvv("ActionsXPMultiplier")["Normal"], sub="Parkour, Kämpfe, Takedowns",
          min=0.5, max=5, step=0.25, params=["ActionsXPMultiplier"]),
        G("LegendBonus_Coop", "Koop-Bonus", float(re.search(r"LegendBonus_Coop\(2,\s*([\d.]+)\)", prog).group(1)),
          sub="Legendenpunkte, wenn ihr zu zweit spielt", min=1, max=3, step=0.1, files=["progressionactions.scr"]),
        G("RespecCost", "Umskillen kostet", int(re.search(r"CostCash\((\d+)\)", legend).group(1)), ctl="step", fmt="n",
          mode="set", sub="Geld für das Zurücksetzen der Fähigkeiten", min=0, max=50000, step=500, files=["legendlevelconfig.scr"]),
    ]},
    {"id": "death", "chip": "fort", "title": "Todesstrafe", "hint": "XP, die du beim Sterben verlierst. Nachts ist der Verlust höher.", "items": [
        G("DeathPenalty", "XP-Verlust beim Tod", 1.0, fmt="pct", sub="Anteil vom Original, gilt für Tag und Nacht",
          min=0, max=1.5, step=0.05, example={"pct": death_pct[4], "day": pvv("DeathPenaltyXpLossMultiplierDay")["Normal"],
                                             "night": pvv("DeathPenaltyXpLossMultiplierNight")["Normal"]},
          params=["DeathPenaltyXpLossMultiplierDay", "DeathPenaltyXpLossMultiplierNight",
                  "LLDeathPenaltyXpLossMultiplierDay", "LLDeathPenaltyXpLossMultiplierNight"]),
    ]},
    {"id": "stamina", "chip": "survive", "title": "Ausdauer", "hint": "Ausdauer für Sprinten, Klettern und Kämpfen.", "items": [
        G("MaxStaminaMultiplier", "Maximale Ausdauer", pvv("MaxStaminaMultiplier")["Normal"], min=0.5, max=3, step=0.1),
        G("StaminaRegenerationMul", "Regeneration", pvv("StaminaRegenerationMul")["Normal"], min=0.5, max=3, step=0.1,
          diff=diff_note("StaminaRegenerationMul")),
        G("WeaponStaminaUsageMul", "Kosten im Kampf", pvv("WeaponStaminaUsageMul")["Normal"], sub="Schläge und Blocks",
          min=0, max=2, step=0.05, diff=diff_note("WeaponStaminaUsageMul")),
        G("MoveStamina", "Kosten beim Gleiten & Schwimmen", 1.0, min=0, max=2, step=0.05,
          params=["GlideStaminaCost", "GlideStaminaCostAtNight", "SwimStaminaUsage"]),
    ]},
    {"id": "heal", "chip": "survive", "title": "Heilung", "hint": "Medkits, Tränke und die automatische Heilung.", "items": [
        G("MedkitHeal", "Medkits", 1.0, sub="Heilung pro Medkit, Stufe 1–4", min=0.5, max=5, step=0.25,
          base=[medkits[0], medkits[-1]], unit="HP", files=["healingdefinitions.scr"]),
        G("PotionHeal", "Regenerationstränke", 1.0, sub="Heilung pro Sekunde, Stufe 1–4", min=0.5, max=5, step=0.25,
          base=[potions[0], potions[-1]], unit="% / s", dec=2, files=["healingdefinitions.scr"]),
        G("MaxAutoRegenHealthPercent", "Heilt sich selbst bis", pvv("MaxAutoRegenHealthPercent")["Normal"], fmt="pctv",
          sub="Prozent der Lebensenergie ohne Medkit", min=10, max=100, step=1, diff=diff_note("MaxAutoRegenHealthPercent")),
        G("HealthRegenerationDelay", "Selbstheilung startet nach", pvv("HealthRegenerationDelay")["Normal"], fmt="s",
          min=0, max=20, step=0.5, diff=diff_note("HealthRegenerationDelay")),
    ]},
    {"id": "fury", "chip": "beast", "title": "Beast-Modus", "hint": "Wut aufladen und die Fähigkeiten im Beast-Modus.", "items": [
        G("FuryChargeMultiplier", "Aufladetempo", pvv("FuryChargeMultiplier")["Normal"], sub="Wie schnell sich die Wut füllt",
          min=0.5, max=5, step=0.25),
        G("FuryActionCost", "Kosten der Fähigkeiten", 1.0,
          sub="z. B. Wurf {:g} · Ansturm {:g} Punkte".format(fury_costs["FuryThrowable"], fury_costs["FuryCharge"]),
          min=0, max=2, step=0.05, files=["player_fury_config.scr"]),
        G("HungerActionCost", "Hunger pro Aktion", 1.0, sub="Verbrauch bei Angriffen, Sprüngen und Beast-Fähigkeiten",
          min=0, max=2, step=0.05, files=["player_hunger_config.scr"]),
    ]},
    {"id": "mat", "chip": "gear", "title": "Material-Bonus", "hint": "Eingebauter Bonus auf gefundenes Crafting-Material. Im Original nur auf „Leicht“ aktiv (+25 %).", "items": [
        # Common/Uncommon: welche Materialien gemeint sind, ist ungeklaert -> ein Regler fuer beide
        G("CraftDropped", "Mehr Material beim Plündern", pvv("CommonCraftDroppedMul")["Normal"], fmt="plus", mode="add",
          sub="Zusätzlich zu den Loot-Reglern", min=0, max=2, step=0.25, diff=diff_note("CommonCraftDroppedMul"),
          params=["CommonCraftDroppedMul", "UncommonCraftDroppedMul"]),
    ]},
    {"id": "repair", "chip": "gear", "title": "Reparatur & Haltbarkeit", "hint": "Wie schnell Waffen verschleißen und wie oft du sie reparieren kannst.", "items": [
        G("MeleeWpnDurabilityMulReduce", "Verschleiß von Nahkampfwaffen", 1.0, fmt="pct", sub="Anteil vom Original",
          min=0, max=2, step=0.05, diff=diff_note("MeleeWpnDurabilityMulReduce"), params=["MeleeWpnDurabilityMulReduce"]),
        G("PerfectRepairChance", "Chance auf perfekte Reparatur", pvv("PerfectRepairChance")["Normal"], fmt="pct", mode="set",
          min=0, max=1, step=0.05),
    ] + [G("Repair_" + c, "Reparaturen: " + RAR[c], int(n), ctl="step", fmt="n", mode="set", color=COLORS[c],
           min=0, max=99, step=1, files=["inventory_special.scr"])
         for c, n in re.findall(r"MaxRepairCountByRarity\((Color_\w+),\s*(\d+)\)", special) if c in RAR]},
    {"id": "inv", "chip": "gear", "title": "Inventar", "hint": "Plätze im Rucksack und Lager, und wie viel auf einen Platz passt.", "items":
        [G(k, l, int(PV(k)), ctl="step", fmt="n", mode="set", group="Plätze", min=1, max=999, step=1) for k, l in SLOTS] +
        [G(i, l, stack(cats[0])[0], ctl="step", fmt="n", mode="set", group="Stapelgröße",
           sub="%d Items" % sum(stack_cnt.get(c, {}).get(stack(cats[0])[0], 0) for c in cats),
           min=1, max=9999, step=1 if stack(cats[0])[0] < 100 else 10, files=["inventory*.scr"], cats=list(cats))
         for i, l, cats in STACKS]},
]

data = {"crafting": sorted(families.values(), key=lambda f: (f["cat"], f["name"])), "loot": loot, "money": money,
        "dismantle": dismantle, "trade": trade, "global": glob, "materials": MAT}
open(OUT, "w", encoding="utf-8").write("window.DATA = " + json.dumps(data, ensure_ascii=False) + ";\n")
print(f"{OUT}: {len(families)} Rezepte, {len(loot)} Loot, {len(dismantle)} Zerlegen, "
      f"{sum(len(g['items']) for g in glob)} globale Einstellungen")
