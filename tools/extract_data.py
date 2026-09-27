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

# ---- Inventar ----
slots = [{"id": k, "label": l, "value": int(PV(k))} for k, l in (
    ("EquipmentSlotsCount", "Ausrüstungsplätze"), ("ConsumableSlotsCount", "Verbrauchsplätze"),
    ("QuickSlotsCount", "Schnellzugriff"), ("AmmoSlotsCount", "Munitionsplätze"),
    ("StorageEquipmentSlotsCount", "Lager: Ausrüstung"), ("StorageOtherSlotsCount", "Lager: Sonstiges"))]
RAR = {"Color_White": "Weiß", "Color_Green": "Grün", "Color_Blue": "Blau", "Color_Violet": "Lila", "Color_Orange": "Orange"}
repairs = [{"id": c, "label": RAR[c], "color": COLORS[c], "value": int(n)}
           for c, n in re.findall(r"MaxRepairCountByRarity\((Color_\w+),\s*(\d+)\)", special) if c in RAR]

data = {"crafting": sorted(families.values(), key=lambda f: (f["cat"], f["name"])), "loot": loot, "money": money,
        "dismantle": dismantle, "trade": trade, "slots": slots, "repairs": repairs, "materials": MAT}
open(OUT, "w", encoding="utf-8").write("window.DATA = " + json.dumps(data, ensure_ascii=False) + ";\n")
print(f"{OUT}: {len(families)} Rezepte, {len(loot)} Loot, {len(dismantle)} Zerlegen, {len(repairs)} Reparaturen")
