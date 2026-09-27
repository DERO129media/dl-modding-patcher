// Spieldaten lesen: Spielordner finden, Skripte aus data0.pak laden, deutsche Namen,
// und daraus das Modell fuer die Oberflaeche samt Katalog der Einstellungen bauen.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Dero
{
    static class Txt
    {
        public static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static double D(string s) { return double.Parse(s, NumberStyles.Float, Inv); }
        public static int I(string s) { return int.Parse(s, Inv); }

        // Block ab dem Treffer von header bis zur passenden schliessenden Klammer
        public static bool FindBlock(string text, Regex header, int from, out int start, out int end)
        {
            start = end = -1;
            Match m = header.Match(text, from);
            if (!m.Success) return false;
            int open = text.IndexOf('{', m.Index + m.Length - 1);
            if (open < 0) return false;
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0) { start = m.Index; end = i + 1; return true; }
            }
            return false;
        }

        public static string Block(string text, Regex header)
        {
            int s, e;
            return FindBlock(text, header, 0, out s, out e) ? text.Substring(s, e - s) : null;
        }

        public static string EditBlock(string text, Regex header, Func<string, string> edit)
        {
            int s, e, s2, e2;
            if (!FindBlock(text, header, 0, out s, out e)) throw new InvalidDataException("Eintrag nicht gefunden: " + header);
            if (FindBlock(text, header, e, out s2, out e2)) throw new InvalidDataException("Eintrag mehrfach vorhanden: " + header);
            return text.Substring(0, s) + edit(text.Substring(s, e - s)) + text.Substring(e);
        }

        // Definitionen (nicht Verwendungen): Item("Name", ...) mit { in der naechsten Zeile
        public static Regex ItemHeader(string name)
        {
            return new Regex(@"(?<!\w)(?:Item|LootedObject)\(""" + Regex.Escape(name) + @"""[^\n]*\n\s*\{");
        }

        public static Regex SubHeader(string name)
        {
            return new Regex(@"(?<!\w)sub " + Regex.Escape(name) + @"\(");
        }

        public static Regex CallHeader(string func, string name)
        {
            return new Regex(@"(?<!\w)" + func + @"\(""" + Regex.Escape(name) + @"""\)");
        }

        public static IEnumerable<KeyValuePair<string, string>> Blocks(string text, string prefix)
        {
            var rx = new Regex(@"(?<!\w)(?:Item|LootedObject)\(""(" + Regex.Escape(prefix) + @"[^""]*)""[^\n]*\n\s*\{");
            foreach (Match m in rx.Matches(text))
            {
                int i = m.Index + m.Length, depth = 1;
                while (depth > 0 && i < text.Length)
                {
                    if (text[i] == '{') depth++;
                    else if (text[i] == '}') depth--;
                    i++;
                }
                yield return new KeyValuePair<string, string>(m.Groups[1].Value, text.Substring(m.Index + m.Length, i - 1 - (m.Index + m.Length)));
            }
        }

        // Zahl im Stil des Originals schreiben (Ganzzahl bleibt Ganzzahl, sonst bis 4 Nachkommastellen)
        public static string FormatLike(double v, string orig, bool keepInt = true)
        {
            if (v < 0) v = 0;
            int dot = orig.IndexOf('.');
            if (dot < 0 && (keepInt || Math.Abs(v - Math.Round(v)) < 1e-9)) return Math.Round(v).ToString(Inv);
            int dec = dot < 0 ? 1 : Math.Max(1, Math.Min(4, orig.Length - dot - 1));
            return Math.Round(v, 4).ToString("0." + new string('0', dec) + new string('#', 4 - dec), Inv);
        }
    }

    // ---------- Spielordner und Dateien ----------

    class GameFiles
    {
        public string GameDir, SourceDir;
        public Dictionary<string, string> Loc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Alles, was der Patcher lesen oder aendern kann
        public static readonly Regex Needed = new Regex(@"^scripts/(" +
            @"inventory/(inventory\w*|collectables_ft)\.scr|inventory/loot/(lootsets_ft|lootpools_ft)\.loot|" +
            @"(damagedefinitions|healingdefinitions)\.scr|trading/price_manager_params\w*\.scr|" +
            @"player/(player_variables\w*|player_fury_config|player_hunger_config)\.scr|player/perma_world/\w+\.scr|" +
            @"progression/(progressionactions|legendlevelconfig)\.scr)$", RegexOptions.IgnoreCase);

        public static string ResolveSourceDir(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string[] candidates = { path, Path.Combine(path, "ph_ft", "source"), Path.Combine(path, "source") };
            foreach (string c in candidates)
                if (File.Exists(Path.Combine(c, "data0.pak"))) return Path.GetFullPath(c);
            return null;
        }

        public static string AutoDetect()
        {
            var libs = new List<string>();
            try
            {
                string steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (steam == null)
                    steam = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
                if (steam != null)
                {
                    libs.Add(steam);
                    string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf))
                        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                            libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                }
            }
            catch { }
            libs.Add(@"C:\Program Files (x86)\Steam");
            foreach (string lib in libs)
            {
                string p = Path.Combine(lib.Replace('/', '\\'), "steamapps", "common", "Dying Light The Beast");
                if (ResolveSourceDir(p) != null) return p;
            }
            return null;
        }

        public static GameFiles Load(string gameDir)
        {
            var g = new GameFiles { GameDir = gameDir, SourceDir = ResolveSourceDir(gameDir) };
            if (g.SourceDir == null) throw new DirectoryNotFoundException("In diesem Ordner wurde Dying Light: The Beast nicht gefunden.");
            // data0 zuerst, data1 ergaenzt nur Fehlendes
            foreach (string pak in new[] { "data0.pak", "data1.pak" })
            {
                string p = Path.Combine(g.SourceDir, pak);
                if (!File.Exists(p)) continue;
                using (ZipArchive z = ZipFile.OpenRead(p))
                    foreach (ZipArchiveEntry e in z.Entries)
                    {
                        string name = e.FullName.Replace('\\', '/');
                        if (!Needed.IsMatch(name) || g.files.ContainsKey(name)) continue;
                        using (Stream s = e.Open())
                        using (var r = new StreamReader(s, Txt.Latin1, false))
                            g.files[name] = r.ReadToEnd();
                    }
            }
            g.LoadLoc();
            return g;
        }

        public string Get(string path)
        {
            string t;
            if (!files.TryGetValue(path, out t)) throw new InvalidDataException("Spieldatei fehlt: " + path);
            return t;
        }

        public bool Has(string path) { return files.ContainsKey(path); }

        // Pfad in der Schreibweise aus data0.pak (fuer die Eintraege in der Mod-Pak)
        public string Canon(string path)
        {
            foreach (string k in files.Keys)
                if (string.Equals(k, path, StringComparison.OrdinalIgnoreCase)) return k;
            throw new InvalidDataException("Spieldatei fehlt: " + path);
        }

        public IEnumerable<string> Paths(Regex rx)
        {
            return files.Keys.Where(k => rx.IsMatch(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        // pc_de_lang.binsloc: "m_keys" + 4 Byte + Anzahl + (uint16 Laenge + UTF-8)*n, ebenso "m_Values"
        void LoadLoc()
        {
            foreach (string lang in new[] { "de", "en" })
            {
                string pak = Path.Combine(SourceDir, "data_lang", "data" + lang + ".pak");
                if (!File.Exists(pak)) continue;
                try
                {
                    byte[] d;
                    using (ZipArchive z = ZipFile.OpenRead(pak))
                    {
                        ZipArchiveEntry e = z.Entries.FirstOrDefault(x => x.FullName.EndsWith("_" + lang + "_lang.binsloc", StringComparison.OrdinalIgnoreCase));
                        if (e == null) continue;
                        using (Stream s = e.Open())
                        using (var ms = new MemoryStream()) { s.CopyTo(ms); d = ms.ToArray(); }
                    }
                    List<string> keys = LocTable(d, IndexOf(d, "m_keys") + 10);
                    List<string> vals = LocTable(d, IndexOf(d, "m_Values") + 12);
                    for (int i = 0; i < keys.Count && i < vals.Count; i++) Loc[keys[i]] = vals[i];
                    return;
                }
                catch { Loc.Clear(); }
            }
        }

        static int IndexOf(byte[] d, string needle)
        {
            byte[] n = Encoding.ASCII.GetBytes(needle);
            for (int i = 0; i <= d.Length - n.Length; i++)
            {
                int j = 0;
                while (j < n.Length && d[i + j] == n[j]) j++;
                if (j == n.Length) return i;
            }
            throw new InvalidDataException(needle + " nicht gefunden");
        }

        static List<string> LocTable(byte[] d, int i)
        {
            int n = BitConverter.ToInt32(d, i);
            i += 4;
            var res = new List<string>(n);
            for (int k = 0; k < n; k++)
            {
                int l = BitConverter.ToUInt16(d, i);
                i += 2;
                res.Add(Encoding.UTF8.GetString(d, i, l));
                i += l;
            }
            return res;
        }
    }

    // ---------- Modell ----------

    class Tier
    {
        public int No, Out, Out2;
        public string Color, Blueprint, Item, Damage;
        public double? Head;
        public List<KeyValuePair<string, int>> Mats = new List<KeyValuePair<string, int>>();
    }

    class Family
    {
        public string Id, Cat, Name;
        public List<Tier> Tiers = new List<Tier>();
    }

    delegate void GApply(PatchCtx ctx, double value, double orig);

    class GItem
    {
        public string Id, Label, Sub = "", Ctl = "slider", Fmt = "x", Mode = "scale", Group, Color, Unit, Chip;
        public double Value, Min, Max, Step;
        public int Dec;
        public double[] Base;
        public Dictionary<string, object> Diff, Example;
        public string[] Files = { "player_variables*.scr" };
        public GApply Apply;

        public Dictionary<string, object> ToJson()
        {
            return Json.O("id", Id, "label", Label, "sub", Sub, "value", Value, "ctl", Ctl, "fmt", Fmt, "mode", Mode,
                "files", Files, "min", Min, "max", Max, "step", Step, "dec", Dec > 0 ? (object)Dec : null,
                "base", Base, "unit", Unit, "diff", Diff, "example", Example, "color", Color, "group", Group);
        }
    }

    class GameModel
    {
        public List<Family> Families = new List<Family>();
        public Dictionary<string, Family> FamById = new Dictionary<string, Family>();
        public List<string> LootSubs = new List<string>();                          // Loot + Geld
        public Dictionary<string, List<string>> DisSets = new Dictionary<string, List<string>>(); // Material -> Zerlegen-Sets
        public Dictionary<string, double> TradeOrig = new Dictionary<string, double>();
        public Dictionary<string, GItem> GItems = new Dictionary<string, GItem>();
        public Dictionary<string, object> Data;

        static readonly Dictionary<string, string> Colors = new Dictionary<string, string> {
            { "Color_White", "white" }, { "Color_Green", "green" }, { "Color_Blue", "blue" }, { "Color_Violet", "violet" }, { "Color_Orange", "orange" } };
        static readonly Dictionary<string, string> Rarity = new Dictionary<string, string> {
            { "Color_White", "Weiß" }, { "Color_Green", "Grün" }, { "Color_Blue", "Blau" }, { "Color_Violet", "Lila" }, { "Color_Orange", "Orange" } };

        GameFiles g;
        readonly Dictionary<string, string> materials = new Dictionary<string, string>();

        public static GameModel Build(GameFiles g)
        {
            var m = new GameModel { g = g };
            m.BuildAll();
            return m;
        }

        // ---------- Namen ----------

        string Loc(string key, int depth = 0)
        {
            string v;
            if (key == null || depth > 3 || !g.Loc.TryGetValue(key.Trim('&'), out v)) return null;
            Match m = Regex.Match(v.Trim(), "^&([^&]+)&$");
            return m.Success ? Loc(m.Groups[1].Value, depth + 1) : v;
        }

        string ItemName(string item)
        {
            foreach (string k in new[] { item + "_N", item + "_n", Regex.Replace(item, @"_T\d$", "") + "_N", Regex.Replace(item, @"_FT_T\d$", "_FT") + "_N" })
            {
                string v = Loc(k);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return null;
        }

        string MatName(string item)
        {
            string n;
            if (!materials.TryGetValue(item, out n))
                materials[item] = n = ItemName(item) ?? item.Replace("Craft_", "").Replace("_", " ");
            return n;
        }

        static string Category(string item)
        {
            if (item.Contains("ThrowingKnife") || item.StartsWith("Throwable_") && item.Contains("Knife")) return "wurf";
            if (item.StartsWith("Throwable_")) return new[] { "Star", "Coin", "Decoy" }.Any(item.Contains) ? "wurf" : "spreng";
            if (new[] { "Mine_", "Grenade", "PipeBomb", "Remote" }.Any(item.StartsWith)) return "spreng";
            if (new[] { "Medkit", "Potion", "Booster", "Improvised_" }.Any(item.StartsWith)) return "heil";
            if (item.StartsWith("Ammo_") || item.StartsWith("Bullet_") || item.Contains("Arrow") || item.Contains("Bolt")) return "muni";
            return "werkzeug";
        }

        // ---------- Aufbau ----------

        void BuildAll()
        {
            var crafting = BuildCrafting();
            List<object> loot, money;
            BuildLoot(out loot, out money);
            var dismantle = BuildDismantle();
            var trade = BuildTrade();
            var glob = BuildGlobal();
            Data = Json.O("crafting", crafting, "loot", loot, "money", money, "dismantle", dismantle,
                "trade", trade, "global", glob, "materials", materials);
        }

        List<object> BuildCrafting()
        {
            string col = g.Get("scripts/inventory/collectables_ft.scr");
            string inv = g.Get("scripts/inventory/inventory.scr");
            string dmg = g.Get("scripts/damagedefinitions.scr");
            var invBlocks = new Dictionary<string, string>();
            foreach (var kv in Txt.Blocks(inv, "")) if (!invBlocks.ContainsKey(kv.Key)) invBlocks[kv.Key] = kv.Value;
            var dmgRanges = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(dmg, @"Damage\(""([^""]+)""\)\s*\{\s*Ranged\(""([^""]+)""\)"))
                if (!dmgRanges.ContainsKey(m.Groups[1].Value)) dmgRanges[m.Groups[1].Value] = m.Groups[2].Value;

            foreach (var kv in Txt.Blocks(col, "Craftplan_"))
            {
                string name = kv.Key, b = kv.Value;
                if (!b.Contains("ItemType_CraftPlan") || name.StartsWith("Craftplan_dlc") || name.StartsWith("Craftplan_Mod_") || name.StartsWith("Craftplan_charm")) continue;
                Match ci = Regex.Match(b, @"CraftedItem\(""([^""]+)"",\s*(\d+),\s*(\d+)\)");
                if (!ci.Success || !Regex.IsMatch(b, @"RequiredItem\(""")) continue;
                string item = ci.Groups[1].Value;
                Match tm = Regex.Match(b, @"ItemLevel\((\d+),\s*(\d+)\)");
                Match cm = Regex.Match(b, @"Color\((Color_\w+)\)");
                string ib;
                invBlocks.TryGetValue(item, out ib);
                ib = ib ?? "";
                Match head = Regex.Match(ib, @"DamageHeadMult\(([\d.]+)\)");
                Match dn = Regex.Match(ib, @"DamageName\(""([^""]+)""\)");
                var t = new Tier
                {
                    No = tm.Success ? Txt.I(tm.Groups[1].Value) : 1,
                    Color = cm.Success && Colors.ContainsKey(cm.Groups[1].Value) ? Colors[cm.Groups[1].Value] : "white",
                    Blueprint = name, Item = item, Out = Txt.I(ci.Groups[2].Value), Out2 = Txt.I(ci.Groups[3].Value),
                    Head = head.Success ? (double?)Txt.D(head.Groups[1].Value) : null,
                };
                if (dn.Success && dmgRanges.ContainsKey(dn.Groups[1].Value)) t.Damage = dmgRanges[dn.Groups[1].Value];
                foreach (Match m in Regex.Matches(b, @"RequiredItem\(""([^""]+)"",\s*(\d+)\)"))
                    t.Mats.Add(new KeyValuePair<string, int>(m.Groups[1].Value, Txt.I(m.Groups[2].Value)));

                string famId = Regex.Replace(name, @"_T\d$", "");
                Family f;
                if (!FamById.TryGetValue(famId, out f))
                {
                    f = new Family { Id = famId, Cat = Category(item) };
                    FamById[famId] = f;
                    Families.Add(f);
                }
                if (f.Name == null)
                {
                    Match nm = Regex.Match(b, @"Name\(""([^""]+)""\)");
                    f.Name = ItemName(item) ?? (nm.Success ? Loc(nm.Groups[1].Value) : null) ?? famId;
                }
                f.Tiers.Add(t);
            }
            var res = new List<object>();
            foreach (Family f in Families.OrderBy(x => x.Cat, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.Ordinal))
            {
                f.Tiers.Sort((a, c) => a.No.CompareTo(c.No));
                var tiers = new List<object>();
                foreach (Tier t in f.Tiers)
                {
                    foreach (var mat in t.Mats) MatName(mat.Key);
                    tiers.Add(Json.O("tier", t.No, "color", t.Color, "blueprint", t.Blueprint, "item", t.Item,
                        "mats", t.Mats.Select(x => (object)new object[] { x.Key, x.Value }).ToList(),
                        "out", t.Out, "out2", t.Out2, "head", t.Head, "damage", t.Damage));
                }
                res.Add(Json.O("id", f.Id, "cat", f.Cat, "name", f.Name, "tiers", tiers));
            }
            return res;
        }

        // ItemCount-Eintraege einer sub in lootsets_ft.loot; normalOnly = nur vor PermaWorld()
        public static List<Tuple<string, int, int>> ItemCounts(string sets, string sub, bool normalOnly)
        {
            var res = new List<Tuple<string, int, int>>();
            string body = Txt.Block(sets, Txt.SubHeader(sub));
            if (body == null) return res;
            if (normalOnly)
            {
                int p = body.IndexOf("PermaWorld()", StringComparison.Ordinal);
                if (p >= 0) body = body.Substring(0, p);
            }
            foreach (Match m in Regex.Matches(body, @"ItemCount\(""([^""]+)"",\s*(\d+),\s*(\d+)"))
            {
                var t = Tuple.Create(m.Groups[1].Value, Txt.I(m.Groups[2].Value), Txt.I(m.Groups[3].Value));
                if (!res.Contains(t)) res.Add(t);  // doppelte Eintraege (mehrere Sets) nur einmal
            }
            return res;
        }

        static readonly Dictionary<string, string> Bundles = new Dictionary<string, string> {
            { "Mechanical", "Mechanik-Bündel" }, { "General", "Allgemeines Bündel" }, { "Chemical", "Chemie-Bündel" },
            { "Vanity", "Stoff-Bündel" }, { "Electrical", "Elektrik-Bündel" }, { "Apex_Rare", "Apex-Beute (selten)" },
            { "Apex_Uncommon", "Apex-Beute (ungewöhnlich)" } };

        void BuildLoot(out List<object> loot, out List<object> money)
        {
            string sets = g.Get("scripts/inventory/loot/lootsets_ft.loot");
            loot = new List<object>();
            foreach (Match sm in Regex.Matches(sets, @"sub (\w+_FT)\("))
            {
                string sub = sm.Groups[1].Value;
                var all = ItemCounts(sets, sub, false);
                if (all.Count == 0 || !all[0].Item1.StartsWith("Craft_")) continue;
                var parts = ItemCounts(sets, sub, true).Select(p => (object)Json.O("item", p.Item1, "name", MatName(p.Item1), "min", p.Item2, "max", p.Item3)).ToList();
                var e = Json.O("id", sub, "item", all[0].Item1, "name", MatName(all[0].Item1), "min", all[0].Item2, "max", all[0].Item3, "parts", parts);
                if (sub.Contains("_Craftparts_"))
                {
                    string key = sub.Replace("_Craftparts_FT", ""), label;
                    e["bundle"] = Bundles.TryGetValue(key, out label) ? label : key.Replace("_", " ");
                }
                loot.Add(e);
                LootSubs.Add(sub);
            }
            money = new List<object>();
            foreach (var kv in new[] { new[] { "Money_Low", "Kleiner Fund" }, new[] { "Money_Mid", "Mittlerer Fund" }, new[] { "Money_High", "Großer Fund" } })
            {
                var ic = ItemCounts(sets, kv[0], false);
                if (ic.Count == 0) continue;
                money.Add(Json.O("id", kv[0], "label", kv[1], "min", ic[0].Item2, "max", ic[0].Item3));
                LootSubs.Add(kv[0]);
            }
        }

        List<object> BuildDismantle()
        {
            string sets = g.Get("scripts/inventory/loot/lootsets_ft.loot");
            string pools = g.Get("scripts/inventory/loot/lootpools_ft.loot");
            var types = new Dictionary<string, string> { { "Slash", "Klingenwaffen" }, { "Blunt", "Stumpfe Waffen" }, { "Firearm", "Schusswaffen" }, { "Ranged", "Bögen & Armbrüste" } };
            var res = new List<object>();
            foreach (var kv in Txt.Blocks(pools, "Dismantle_T"))
            {
                Match m = Regex.Match(kv.Key, @"^Dismantle_T(\d)_(\w+)$");
                if (!m.Success) continue;
                var parts = new List<object>();
                foreach (Match u in Regex.Matches(kv.Value, @"use (\w+) \(weight = [\d.]+, min_amount = (\d+), max_amount = (\d+)\)"))
                {
                    var ic = ItemCounts(sets, u.Groups[1].Value, false);
                    if (ic.Count == 0) continue;
                    string item = ic[0].Item1;
                    parts.Add(Json.O("set", u.Groups[1].Value, "item", item, "name", MatName(item),
                        "min", Txt.I(u.Groups[2].Value) * ic[0].Item2, "max", Txt.I(u.Groups[3].Value) * ic[0].Item3));
                    List<string> l;
                    if (!DisSets.TryGetValue(item, out l)) DisSets[item] = l = new List<string>();
                    if (!l.Contains(u.Groups[1].Value)) l.Add(u.Groups[1].Value);
                }
                string type;
                res.Add(Json.O("id", kv.Key, "type", types.TryGetValue(m.Groups[2].Value, out type) ? type : m.Groups[2].Value,
                    "tier", Txt.I(m.Groups[1].Value), "parts", parts));
            }
            return res;
        }

        public static readonly string[][] SellParams = {
            new[] { "MELEE_WEAPON_SELL_PRICE_MOD", "Nahkampfwaffen verkaufen" }, new[] { "RANGED_WEAPON_SELL_PRICE_MOD", "Fernkampfwaffen verkaufen" },
            new[] { "COMMON_ITEM_SELL_PRICE_MOD", "Normale Gegenstände verkaufen" }, new[] { "COLOR_ITEM_SELL_PRICE_MOD", "Seltene Gegenstände verkaufen" },
            new[] { "VALUABLE_ITEM_SELL_PRICE_MOD", "Wertsachen verkaufen" } };

        Dictionary<string, object> BuildTrade()
        {
            string price = g.Get("scripts/trading/price_manager_params.scr");
            var sell = new List<object>();
            foreach (string[] p in SellParams)
            {
                double v = Txt.D(Regex.Match(price, @"Param\(""" + p[0] + @""",\s*([\d.]+)\)").Groups[1].Value);
                TradeOrig[p[0]] = v;
                sell.Add(Json.O("id", p[0], "label", p[1], "value", v));
            }
            TradeOrig["buy"] = PV("ItemBuyFactor");
            return Json.O("sell", sell, "buy", TradeOrig["buy"], "sellFactor", PV("ItemSellFactor"));
        }

        // ---------- Globale Einstellungen (Katalog) ----------

        public static readonly Regex PvFiles = new Regex(@"^scripts/player/(player_variables\w*|perma_world/\w+)\.scr$", RegexOptions.IgnoreCase);
        static readonly string[][] DiffFiles = { new[] { "Normal", "" }, new[] { "Leicht", "_easy" }, new[] { "Schwer", "_hard" }, new[] { "Albtraum", "_nightmare" } };

        static Regex ParamRx(string name)
        {
            return new Regex(@"(?m)^([ \t]*Param\(\s*""" + Regex.Escape(name) + @"""\s*,\s*"")(-?[\d.]+)(""\s*\))");
        }

        double PV(string name)
        {
            return Txt.D(ParamRx(name).Match(g.Get("scripts/player/player_variables.scr")).Groups[2].Value);
        }

        Dictionary<string, object> DiffNote(string name)
        {
            var d = new Dictionary<string, object>();
            foreach (string[] f in DiffFiles)
            {
                string path = "scripts/player/player_variables" + f[1] + ".scr";
                if (!g.Has(path)) continue;
                Match m = ParamRx(name).Match(g.Get(path));
                if (m.Success) d[f[0]] = Txt.D(m.Groups[2].Value);
            }
            return d.Count > 1 ? d : null;
        }

        // Aendert einen Param in allen player_variables-Dateien (inkl. Schwierigkeiten und Perma-World)
        static GApply ParamApply(string mode, params string[] names)
        {
            return delegate(PatchCtx ctx, double v, double o)
            {
                foreach (string path in ctx.Paths(PvFiles))
                    foreach (string name in names)
                        ctx.Replace(path, ParamRx(name), old => Transform(mode, old, v, o));
            };
        }

        public static double Transform(string mode, double old, double v, double o)
        {
            if (mode == "set") return v;
            if (mode == "add") return old + (v - o);
            return o != 0 ? old * v / o : v;
        }

        static GApply Regexes(string path, Regex rx, string mode, bool keepInt = true)
        {
            return delegate(PatchCtx ctx, double v, double o) { ctx.Replace(path, rx, old => Transform(mode, old, v, o), keepInt); };
        }

        static GApply BlockValues(string path, string func, string[] names, string mode)
        {
            return delegate(PatchCtx ctx, double v, double o)
            {
                foreach (string n in names)
                    ctx.EditBlock(path, Txt.CallHeader(func, n), b => PatchCtx.ReplaceIn(b, new Regex(@"(Health\("")([\d.]+)"), old => Transform(mode, old, v, o), true));
            };
        }

        List<object> BuildGlobal()
        {
            string prog = g.Get("scripts/progression/progressionactions.scr");
            string legend = g.Get("scripts/progression/legendlevelconfig.scr");
            string fury = g.Get("scripts/player/player_fury_config.scr");
            string heal = g.Get("scripts/healingdefinitions.scr");
            string special = g.Get("scripts/inventory/inventory_special.scr");
            string pvar = g.Get("scripts/player/player_variables.scr");

            Func<string, double> healHp = n => Txt.D(Regex.Match(heal, @"Healing\w*\(""" + n + @"""\)\s*\{\s*Health\(""([\d.]+)").Groups[1].Value);
            string[] medkitNames = Enumerable.Range(1, 4).Select(i => "Medkit_Small_FT_T" + i).ToArray();
            string[] potionNames = Enumerable.Range(1, 4).Select(i => "HealthRegenerationPotion_FT_T" + i).ToArray();
            var furyCosts = new Dictionary<string, double>();
            foreach (Match m in Regex.Matches(fury, @"ActionCost\(""(\w+)"",\s*([\d.]+)")) furyCosts[m.Groups[1].Value] = Txt.D(m.Groups[2].Value);
            var deathPct = Regex.Matches(pvar, @"Param\(""DeathPenaltyXpLossPercentageLevel\d+"",\s*""([\d.]+)""").Cast<Match>().Select(m => Txt.D(m.Groups[1].Value)).ToList();
            var actionCost = new Regex(@"(?m)^([ \t]*ActionCost\(\s*""\w+""\s*,\s*)([\d.]+)(\s*\))");

            var groups = new List<Dictionary<string, object>>();
            Action<string, string, string, string, GItem[]> group = delegate(string id, string chip, string title, string hint, GItem[] items)
            {
                foreach (GItem i in items) { i.Chip = chip; GItems[i.Id] = i; }
                groups.Add(Json.O("id", id, "chip", chip, "title", title, "hint", hint, "items", items.Select(i => (object)i.ToJson()).ToList()));
            };

            group("xp", "fort", "Erfahrung", "Wie schnell du aufsteigst.", new[] {
                new GItem { Id = "ActionsXPMultiplier", Label = "XP für Aktionen", Value = PV("ActionsXPMultiplier"), Sub = "Parkour, Kämpfe, Takedowns",
                    Min = 0.5, Max = 5, Step = 0.25, Apply = ParamApply("scale", "ActionsXPMultiplier") },
                new GItem { Id = "LegendBonus_Coop", Label = "Koop-Bonus", Value = Txt.D(Regex.Match(prog, @"LegendBonus_Coop\(2,\s*([\d.]+)\)").Groups[1].Value),
                    Sub = "Legendenpunkte, wenn ihr im Koop spielt", Min = 1, Max = 3, Step = 0.1, Files = new[] { "progressionactions.scr" },
                    Apply = Regexes("scripts/progression/progressionactions.scr", new Regex(@"(LegendBonus_Coop\(\s*\d+\s*,\s*)([\d.]+)(\s*\))"), "set") },
                new GItem { Id = "RespecCost", Label = "Umskillen kostet", Value = Txt.I(Regex.Match(legend, @"CostCash\((\d+)\)").Groups[1].Value),
                    Ctl = "step", Fmt = "n", Mode = "set", Sub = "Geld für das Zurücksetzen der Fähigkeiten", Min = 0, Max = 50000, Step = 500,
                    Files = new[] { "legendlevelconfig.scr" }, Apply = Regexes("scripts/progression/legendlevelconfig.scr", new Regex(@"(CostCash\()(\d+)(\))"), "set") },
            });
            group("death", "fort", "Todesstrafe", "XP, die du beim Sterben verlierst. Nachts ist der Verlust höher.", new[] {
                new GItem { Id = "DeathPenalty", Label = "XP-Verlust beim Tod", Value = 1, Fmt = "pct", Sub = "Anteil vom Original, gilt für Tag und Nacht",
                    Min = 0, Max = 1.5, Step = 0.05,
                    Example = Json.O("pct", deathPct.Count > 4 ? deathPct[4] : 0.015, "day", PV("DeathPenaltyXpLossMultiplierDay"), "night", PV("DeathPenaltyXpLossMultiplierNight")),
                    Apply = ParamApply("scale", "DeathPenaltyXpLossMultiplierDay", "DeathPenaltyXpLossMultiplierNight", "LLDeathPenaltyXpLossMultiplierDay", "LLDeathPenaltyXpLossMultiplierNight") },
            });
            group("stamina", "survive", "Ausdauer", "Ausdauer für Sprinten, Klettern und Kämpfen.", new[] {
                new GItem { Id = "MaxStaminaMultiplier", Label = "Maximale Ausdauer", Value = PV("MaxStaminaMultiplier"), Min = 0.5, Max = 3, Step = 0.1,
                    Apply = ParamApply("scale", "MaxStaminaMultiplier") },
                new GItem { Id = "StaminaRegenerationMul", Label = "Regeneration", Value = PV("StaminaRegenerationMul"), Min = 0.5, Max = 3, Step = 0.1,
                    Diff = DiffNote("StaminaRegenerationMul"), Apply = ParamApply("scale", "StaminaRegenerationMul") },
                new GItem { Id = "WeaponStaminaUsageMul", Label = "Kosten im Kampf", Value = PV("WeaponStaminaUsageMul"), Sub = "Schläge und Blocks",
                    Min = 0, Max = 2, Step = 0.05, Diff = DiffNote("WeaponStaminaUsageMul"), Apply = ParamApply("scale", "WeaponStaminaUsageMul") },
                new GItem { Id = "MoveStamina", Label = "Kosten beim Gleiten & Schwimmen", Value = 1, Min = 0, Max = 2, Step = 0.05,
                    Apply = ParamApply("scale", "GlideStaminaCost", "GlideStaminaCostAtNight", "SwimStaminaUsage") },
            });
            group("heal", "survive", "Heilung", "Medkits, Tränke und die automatische Heilung.", new[] {
                new GItem { Id = "MedkitHeal", Label = "Medkits", Value = 1, Sub = "Heilung pro Medkit, Stufe 1–4", Min = 0.5, Max = 5, Step = 0.25,
                    Base = new[] { healHp(medkitNames[0]), healHp(medkitNames[3]) }, Unit = "HP", Files = new[] { "healingdefinitions.scr" },
                    Apply = BlockValues("scripts/healingdefinitions.scr", "Healing", medkitNames, "scale") },
                new GItem { Id = "PotionHeal", Label = "Regenerationstränke", Value = 1, Sub = "Heilung pro Sekunde, Stufe 1–4", Min = 0.5, Max = 5, Step = 0.25,
                    Base = new[] { healHp(potionNames[0]), healHp(potionNames[3]) }, Unit = "% / s", Dec = 2, Files = new[] { "healingdefinitions.scr" },
                    Apply = BlockValues("scripts/healingdefinitions.scr", "HealingHps", potionNames, "scale") },
                new GItem { Id = "MaxAutoRegenHealthPercent", Label = "Heilt sich selbst bis", Value = PV("MaxAutoRegenHealthPercent"), Fmt = "pctv",
                    Sub = "Prozent der Lebensenergie ohne Medkit", Min = 10, Max = 100, Step = 1, Diff = DiffNote("MaxAutoRegenHealthPercent"),
                    Apply = ParamApply("scale", "MaxAutoRegenHealthPercent") },
                new GItem { Id = "HealthRegenerationDelay", Label = "Selbstheilung startet nach", Value = PV("HealthRegenerationDelay"), Fmt = "s",
                    Min = 0, Max = 20, Step = 0.5, Diff = DiffNote("HealthRegenerationDelay"), Apply = ParamApply("scale", "HealthRegenerationDelay") },
            });
            group("fury", "beast", "Beast-Modus", "Wut aufladen und die Fähigkeiten im Beast-Modus.", new[] {
                new GItem { Id = "FuryChargeMultiplier", Label = "Aufladetempo", Value = PV("FuryChargeMultiplier"), Sub = "Wie schnell sich die Wut füllt",
                    Min = 0.5, Max = 5, Step = 0.25, Apply = ParamApply("scale", "FuryChargeMultiplier") },
                new GItem { Id = "FuryActionCost", Label = "Kosten der Fähigkeiten", Value = 1,
                    Sub = string.Format(Txt.Inv, "z. B. Wurf {0} · Ansturm {1} Punkte", Get(furyCosts, "FuryThrowable"), Get(furyCosts, "FuryCharge")),
                    Min = 0, Max = 2, Step = 0.05, Files = new[] { "player_fury_config.scr" },
                    Apply = Regexes("scripts/player/player_fury_config.scr", actionCost, "scale", false) },
                new GItem { Id = "HungerActionCost", Label = "Hunger pro Aktion", Value = 1, Sub = "Verbrauch bei Angriffen, Sprüngen und Beast-Fähigkeiten",
                    Min = 0, Max = 2, Step = 0.05, Files = new[] { "player_hunger_config.scr" },
                    Apply = Regexes("scripts/player/player_hunger_config.scr", actionCost, "scale", false) },
            });
            // Common/Uncommon: welche Materialien gemeint sind, ist ungeklaert -> ein Regler fuer beide
            group("mat", "gear", "Material-Bonus", "Eingebauter Bonus auf gefundenes Crafting-Material. Im Original nur auf „Leicht“ aktiv (+25 %).", new[] {
                new GItem { Id = "CraftDropped", Label = "Mehr Material beim Plündern", Value = PV("CommonCraftDroppedMul"), Fmt = "plus", Mode = "add",
                    Sub = "Wirkt zusätzlich zu den Reglern unter „Loot“", Min = 0, Max = 2, Step = 0.25, Diff = DiffNote("CommonCraftDroppedMul"),
                    Apply = ParamApply("add", "CommonCraftDroppedMul", "UncommonCraftDroppedMul") },
            });

            var repair = new List<GItem> {
                new GItem { Id = "MeleeWpnDurabilityMulReduce", Label = "Verschleiß von Nahkampfwaffen", Value = 1, Fmt = "pct", Sub = "Anteil vom Original",
                    Min = 0, Max = 2, Step = 0.05, Diff = DiffNote("MeleeWpnDurabilityMulReduce"), Apply = ParamApply("scale", "MeleeWpnDurabilityMulReduce") },
                new GItem { Id = "PerfectRepairChance", Label = "Chance auf perfekte Reparatur", Value = PV("PerfectRepairChance"), Fmt = "pct", Mode = "set",
                    Min = 0, Max = 1, Step = 0.05, Apply = ParamApply("set", "PerfectRepairChance") },
            };
            foreach (Match m in Regex.Matches(special, @"MaxRepairCountByRarity\((Color_\w+),\s*(\d+)\)"))
            {
                string c = m.Groups[1].Value;
                if (!Rarity.ContainsKey(c) || GItems.ContainsKey("Repair_" + c)) continue;
                repair.Add(new GItem { Id = "Repair_" + c, Label = "Reparaturen: " + Rarity[c], Value = Txt.I(m.Groups[2].Value), Ctl = "step", Fmt = "n", Mode = "set",
                    Color = Colors[c], Min = 0, Max = 99, Step = 1, Files = new[] { "inventory_special.scr" },
                    Apply = Regexes("scripts/inventory/inventory_special.scr", new Regex(@"(MaxRepairCountByRarity\(\s*" + c + @"\s*,\s*)(\d+)(\s*\))"), "set") });
            }
            group("repair", "gear", "Reparatur & Haltbarkeit", "Wie schnell Waffen verschleißen und wie oft du sie reparieren kannst.", repair.ToArray());

            var inv = new List<GItem>();
            foreach (string[] s in new[] { new[] { "EquipmentSlotsCount", "Ausrüstungsplätze" }, new[] { "ConsumableSlotsCount", "Verbrauchsplätze" },
                new[] { "QuickSlotsCount", "Schnellzugriff" }, new[] { "AmmoSlotsCount", "Munitionsplätze" },
                new[] { "StorageEquipmentSlotsCount", "Lager: Ausrüstung" }, new[] { "StorageOtherSlotsCount", "Lager: Sonstiges" } })
                // "scale": Perma-World hat eigene (kleinere) Werte, die im gleichen Verhaeltnis mitwachsen
                inv.Add(new GItem { Id = s[0], Label = s[1], Value = PV(s[0]), Ctl = "step", Fmt = "n", Group = "Plätze",
                    Min = 1, Max = 999, Step = 1, Apply = ParamApply("scale", s[0]) });
            var stackCnt = StackCounts();
            foreach (var st in new[] {
                new[] { "stack_throw", "Wurfwaffen", "CategoryType_Throwable", "CategoryType_ThrowableLiquid" }, new[] { "stack_medkit", "Medkits", "CategoryType_Medkit" },
                new[] { "stack_powerup", "Tränke & Booster", "CategoryType_Powerup" }, new[] { "stack_ammo", "Munition", "CategoryType_Ammo" },
                new[] { "stack_lockpick", "Dietriche", "CategoryType_Lockpick" } })
            {
                string[] cats = st.Skip(2).ToArray();
                Dictionary<int, int> c0;
                if (!stackCnt.TryGetValue(cats[0], out c0)) continue;
                int baseVal = c0.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
                int n = cats.Sum(c => stackCnt.ContainsKey(c) && stackCnt[c].ContainsKey(baseVal) ? stackCnt[c][baseVal] : 0);
                inv.Add(new GItem { Id = st[0], Label = st[1], Value = baseVal, Ctl = "step", Fmt = "n", Mode = "set", Group = "Stapelgröße",
                    Sub = n == 1 ? "gilt für 1 Gegenstand" : "gilt für " + n + " Gegenstände", Min = 1, Max = 9999, Step = baseVal < 100 ? 1 : 10,
                    Files = new[] { "inventory*.scr" }, Apply = StackApply(cats, baseVal) });
            }
            group("inv", "gear", "Inventar", "Plätze im Rucksack und Lager, und wie viel auf einen Platz passt.", inv.ToArray());
            return groups.Cast<object>().ToList();
        }

        static double Get(Dictionary<string, double> d, string k) { double v; return d.TryGetValue(k, out v) ? v : 0; }

        public static readonly Regex InventoryFiles = new Regex(@"^scripts/inventory/inventory\w*\.scr$", RegexOptions.IgnoreCase);
        static readonly Regex ItemDef = new Regex(@"(?<!\w)Item\(""[^""]+"",\s*(CategoryType_\w+)\)\s*\{");
        static readonly Regex StackRx = new Regex(@"MaxStackCount\((\d+)\)");

        Dictionary<string, Dictionary<int, int>> StackCounts()
        {
            var res = new Dictionary<string, Dictionary<int, int>>();
            foreach (string path in g.Paths(InventoryFiles))
            {
                string t = g.Get(path);
                foreach (Match m in ItemDef.Matches(t))
                {
                    int s, e;
                    if (!Txt.FindBlock(t, ItemDef, m.Index, out s, out e)) continue;
                    Match sm = StackRx.Match(t.Substring(s, e - s));
                    if (!sm.Success) continue;
                    int v = Txt.I(sm.Groups[1].Value);
                    if (v <= 1) continue;
                    Dictionary<int, int> c;
                    if (!res.TryGetValue(m.Groups[1].Value, out c)) res[m.Groups[1].Value] = c = new Dictionary<int, int>();
                    c[v] = (c.ContainsKey(v) ? c[v] : 0) + 1;
                }
            }
            return res;
        }

        // Setzt MaxStackCount bei allen Items der Kategorien, die den Standardwert haben
        static GApply StackApply(string[] cats, int baseVal)
        {
            return delegate(PatchCtx ctx, double v, double o)
            {
                foreach (string path in ctx.Paths(InventoryFiles))
                {
                    string t = ctx.Get(path);
                    var sb = new StringBuilder();
                    int pos = 0;
                    bool changed = false;
                    foreach (Match m in ItemDef.Matches(t))
                    {
                        if (m.Index < pos || Array.IndexOf(cats, m.Groups[1].Value) < 0) continue;
                        int s, e;
                        if (!Txt.FindBlock(t, ItemDef, m.Index, out s, out e)) continue;
                        string b = t.Substring(s, e - s);
                        Match sm = StackRx.Match(b);
                        if (!sm.Success || Txt.I(sm.Groups[1].Value) != baseVal) continue;
                        sb.Append(t, pos, s - pos);
                        sb.Append(b.Substring(0, sm.Index) + "MaxStackCount(" + ((int)v).ToString(Txt.Inv) + ")" + b.Substring(sm.Index + sm.Length));
                        pos = e;
                        changed = true;
                    }
                    if (!changed) continue;
                    sb.Append(t, pos, t.Length - pos);
                    ctx.Set(path, sb.ToString());
                }
            };
        }

        // Originalzustand in der Form, die die Oberflaeche verwendet (fuer Pruefung und Vergleich)
        public Dictionary<string, object> OriginalState()
        {
            var glob = new Dictionary<string, object>();
            foreach (GItem i in GItems.Values) glob[i.Id] = i.Value;
            var craft = new Dictionary<string, object>();
            foreach (Family f in Families)
                craft[f.Id] = f.Tiers.Select(t => (object)Json.O("mats", t.Mats.Select(x => (object)x.Value).ToList(), "out", t.Out, "head", t.Head)).ToList();
            var loot = new Dictionary<string, object>();
            var money = new Dictionary<string, object>();
            foreach (string s in LootSubs) (s.StartsWith("Money_") ? money : loot)[s] = 1.0;
            var dis = new Dictionary<string, object>();
            foreach (string item in DisSets.Keys) dis[item] = 1.0;
            var trade = new Dictionary<string, object>();
            foreach (var kv in TradeOrig) trade[kv.Key] = kv.Value;
            return Json.O("glob", glob, "craft", craft, "loot", loot, "money", money, "dis", dis, "trade", trade);
        }
    }
}
