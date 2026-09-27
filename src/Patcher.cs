// Wendet Einstellungen (Diff gegen die Originalwerte) auf die Spielskripte an und baut daraus
// eine deterministische dataN.pak. Gleiche Einstellungen + gleiches Spiel = byte-gleiche Datei (Koop).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Dero
{
    class PatchCtx
    {
        readonly GameFiles g;
        public readonly SortedDictionary<string, string> Changed = new SortedDictionary<string, string>(StringComparer.Ordinal);

        public PatchCtx(GameFiles g) { this.g = g; }

        public string Get(string path)
        {
            string t;
            return Changed.TryGetValue(g.Canon(path), out t) ? t : g.Get(path);
        }

        public void Set(string path, string text)
        {
            path = g.Canon(path);
            if (text == g.Get(path)) Changed.Remove(path);
            else Changed[path] = text;
        }

        public IEnumerable<string> Paths(Regex rx) { return g.Paths(rx); }

        public void Replace(string path, Regex rx, Func<double, double> f, bool keepInt = true)
        {
            Set(path, ReplaceIn(Get(path), rx, f, keepInt));
        }

        public void EditBlock(string path, Regex header, Func<string, string> edit)
        {
            Set(path, Txt.EditBlock(Get(path), header, edit));
        }

        // Gruppe 1 = Text davor, Gruppe 2 = Zahl, optional Gruppe 3 = Text danach
        public static string ReplaceIn(string text, Regex rx, Func<double, double> f, bool keepInt)
        {
            return rx.Replace(text, m => m.Groups[1].Value + Txt.FormatLike(f(Txt.D(m.Groups[2].Value)), m.Groups[2].Value, keepInt)
                + (m.Groups.Count > 3 ? m.Groups[3].Value : ""));
        }
    }

    static class Patcher
    {
        public const string Marker = "// DERO-";
        public const string Header = "// DERO-Patcher - automatisch erzeugt, bitte nicht von Hand bearbeiten";
        public const string SettingsPrefix = "// DERO-Einstellungen: ";
        public const string GameProcess = "DyingLightGame_TheBeast_x64_rwdi";
        const string Collectables = "scripts/inventory/collectables_ft.scr";
        const string Inventory = "scripts/inventory/inventory.scr";
        const string LootSets = "scripts/inventory/loot/lootsets_ft.loot";
        static readonly Regex TradeFiles = new Regex(@"^scripts/trading/price_manager_params\w*\.scr$", RegexOptions.IgnoreCase);
        static readonly Regex HeadRx = new Regex(@"(?m)^([ \t]*)DamageHeadMult\(\s*([0-9.]+)\s*\)\s*;");
        static readonly Regex ItemCountRx = new Regex(@"(ItemCount\(\s*""([^""]+)""\s*,\s*)(\d+)(\s*,\s*)(\d+)");

        // ---------- Anwenden ----------

        public static PatchCtx Apply(GameModel m, GameFiles g, Dictionary<string, object> diff)
        {
            var ctx = new PatchCtx(g);
            foreach (var sec in diff)
            {
                var d = Json.Obj(sec.Value);
                if (d == null) throw new InvalidDataException("Ungültiger Bereich: " + sec.Key);
                foreach (var kv in d)
                {
                    switch (sec.Key)
                    {
                        case "glob": ApplyGlobal(ctx, m, kv.Key, kv.Value); break;
                        case "craft": ApplyCraft(ctx, m, kv.Key, kv.Value); break;
                        case "loot":
                        case "money": ApplyLoot(ctx, m, kv.Key, Factor(kv.Value, kv.Key)); break;
                        case "dis": ApplyDismantle(ctx, m, kv.Key, Factor(kv.Value, kv.Key)); break;
                        case "trade": ApplyTrade(ctx, m, kv.Key, kv.Value); break;
                        default: throw new InvalidDataException("Unbekannter Bereich: " + sec.Key);
                    }
                }
            }
            return ctx;
        }

        static double Num(object o, string id, double min, double max)
        {
            if (!Json.IsNumber(o)) throw new InvalidDataException("Ungültiger Wert bei " + id);
            double v = Json.D(o);
            if (v < min - 1e-9 || v > max + 1e-9) throw new InvalidDataException("Wert außerhalb des Bereichs bei " + id);
            return v;
        }

        static double Factor(object o, string id) { return Num(o, id, 0.5, 5); }

        static void ApplyGlobal(PatchCtx ctx, GameModel m, string id, object val)
        {
            GItem i;
            if (!m.GItems.TryGetValue(id, out i)) throw new InvalidDataException("Unbekannte Einstellung: " + id);
            double v = Num(val, id, i.Min, i.Max);
            if (Math.Abs(v - i.Value) > 1e-12) i.Apply(ctx, v, i.Value);
        }

        static void ApplyCraft(PatchCtx ctx, GameModel m, string famId, object val)
        {
            Family f;
            if (!m.FamById.TryGetValue(famId, out f)) throw new InvalidDataException("Unbekanntes Rezept: " + famId);
            object[] tiers = Json.Arr(val);
            if (tiers == null || tiers.Length != f.Tiers.Count) throw new InvalidDataException("Ungültige Stufen bei " + famId);
            for (int ti = 0; ti < tiers.Length; ti++)
            {
                Tier t = f.Tiers[ti];
                var o = Json.Obj(tiers[ti]);
                object[] mats = o == null ? null : Json.Arr(o.ContainsKey("mats") ? o["mats"] : null);
                if (mats == null || mats.Length != t.Mats.Count) throw new InvalidDataException("Ungültige Materialien bei " + famId);
                var newMats = mats.Select((x, j) => (int)Num(x, famId, 0, 999)).ToList();
                if (newMats.All(x => x == 0)) throw new InvalidDataException(f.Name + ": Mindestens ein Material muss mehr als 0 kosten.");
                int outN = (int)Num(o.ContainsKey("out") ? o["out"] : null, famId, 1, 999);
                object hv = o.ContainsKey("head") ? o["head"] : null;
                double? head = hv == null ? (double?)null : Num(hv, famId, 0.5, 20);

                bool matsChanged = newMats.Where((x, j) => x != t.Mats[j].Value).Any();
                if (matsChanged || outN != t.Out)
                {
                    Tier tt = t;
                    ctx.EditBlock(Collectables, Txt.ItemHeader(t.Blueprint), delegate(string b)
                    {
                        for (int j = 0; j < tt.Mats.Count; j++)
                            if (newMats[j] != tt.Mats[j].Value) b = SetMaterial(b, tt.Mats[j].Key, newMats[j]);
                        if (outN != tt.Out)
                        {
                            var rx = new Regex(@"CraftedItem\(\s*""" + Regex.Escape(tt.Item) + @"""\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
                            if (!rx.IsMatch(b)) throw new InvalidDataException("CraftedItem fehlt bei " + tt.Blueprint);
                            // 2. Parameter ist unklar (evtl. Menge mit Skill) - wird nie kleiner als der Output
                            b = rx.Replace(b, x => "CraftedItem(\"" + tt.Item + "\", " + outN + ", " + Math.Max(outN, Txt.I(x.Groups[2].Value)) + ")", 1);
                        }
                        return b;
                    });
                }
                if (head.HasValue && t.Head.HasValue && Math.Abs(head.Value - t.Head.Value) > 1e-9)
                {
                    double hvv = head.Value;
                    ctx.EditBlock(Inventory, Txt.ItemHeader(t.Item), delegate(string b)
                    {
                        if (!HeadRx.IsMatch(b)) throw new InvalidDataException("DamageHeadMult fehlt bei " + t.Item);
                        return HeadRx.Replace(b, x => x.Groups[1].Value + "DamageHeadMult(" + hvv.ToString("0.0##", Txt.Inv) + ");", 1);
                    });
                }
            }
        }

        // Ersetzt RequiredItem(...) und AlternativePrice(...) fuer ein Material. 0 = Zeile auskommentieren.
        static string SetMaterial(string block, string material, int value)
        {
            var rx = new Regex(@"(?mi)^([ \t]*)(RequiredItem|AlternativePrice)\(\s*""(" + Regex.Escape(material) + @")""\s*,\s*\d+\s*\)\s*;");
            bool hasRequired = false;
            string res = rx.Replace(block, m =>
            {
                if (m.Groups[2].Value == "RequiredItem") hasRequired = true;
                string call = m.Groups[2].Value + "(\"" + m.Groups[3].Value + "\", " + value + ");";
                return m.Groups[1].Value + (value > 0 ? call : "//" + call + " // DERO: entfernt");
            });
            if (!hasRequired) throw new InvalidDataException("Material " + material + " nicht im Rezept gefunden");
            return res;
        }

        // Mengen in einer Loot-sub skalieren (nur normales Spiel, Perma-World bleibt)
        static void ScaleSub(PatchCtx ctx, string sub, string onlyItem, double f)
        {
            ctx.EditBlock(LootSets, Txt.SubHeader(sub), delegate(string b)
            {
                int p = b.IndexOf("PermaWorld()", StringComparison.Ordinal);
                string head = p >= 0 ? b.Substring(0, p) : b, tail = p >= 0 ? b.Substring(p) : "";
                head = ItemCountRx.Replace(head, m =>
                {
                    if (onlyItem != null && m.Groups[2].Value != onlyItem) return m.Value;
                    int a = Txt.I(m.Groups[3].Value), c = Txt.I(m.Groups[5].Value);
                    int na = a > 0 ? Math.Max(1, (int)Math.Round(a * f, MidpointRounding.AwayFromZero)) : 0;
                    int nc = Math.Max(na, Math.Max(1, (int)Math.Round(c * f, MidpointRounding.AwayFromZero)));
                    return m.Groups[1].Value + na + m.Groups[4].Value + nc;
                });
                return head + tail;
            });
        }

        static void ApplyLoot(PatchCtx ctx, GameModel m, string sub, double f)
        {
            if (!m.LootSubs.Contains(sub)) throw new InvalidDataException("Unbekannter Loot-Eintrag: " + sub);
            if (Math.Abs(f - 1) > 1e-12) ScaleSub(ctx, sub, null, f);
        }

        static void ApplyDismantle(PatchCtx ctx, GameModel m, string item, double f)
        {
            List<string> subs;
            if (!m.DisSets.TryGetValue(item, out subs)) throw new InvalidDataException("Unbekanntes Material: " + item);
            if (Math.Abs(f - 1) < 1e-12) return;
            foreach (string s in subs) ScaleSub(ctx, s, item, f);
        }

        static void ApplyTrade(PatchCtx ctx, GameModel m, string id, object val)
        {
            double o;
            if (!m.TradeOrig.TryGetValue(id, out o)) throw new InvalidDataException("Unbekannter Händler-Wert: " + id);
            double v = id == "buy" ? Num(val, id, 0.25, 2) : Num(val, id, 0, 1);
            if (Math.Abs(v - o) < 1e-12) return;
            if (id == "buy")
            {
                var rx = new Regex(@"(?m)^([ \t]*Param\(\s*""ItemBuyFactor""\s*,\s*"")(-?[\d.]+)(""\s*\))");
                foreach (string p in ctx.Paths(GameModel.PvFiles)) ctx.Replace(p, rx, old => GameModel.Transform("scale", old, v, o));
                return;
            }
            // Gleiches Verhaeltnis in allen Schwierigkeiten
            var prx = new Regex(@"(?m)^([ \t]*Param\(\s*""" + Regex.Escape(id) + @"""\s*,\s*)([\d.]+)(\s*\))");
            foreach (string p in ctx.Paths(TradeFiles)) ctx.Replace(p, prx, old => o != 0 ? old * v / o : v, false);
        }

        // ---------- Pak ----------

        public static string Canonical(Dictionary<string, object> diff) { return Json.Write(diff, true); }

        public static byte[] BuildPak(PatchCtx ctx, Dictionary<string, object> diff)
        {
            string header = Header + "\r\n" + SettingsPrefix + Canonical(diff) + "\r\n";
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach (var kv in ctx.Changed)
                    {
                        byte[] data = Txt.Latin1.GetBytes(header + kv.Value);
                        ZipArchiveEntry e = zip.CreateEntry(kv.Key, CompressionLevel.Optimal);
                        // Fester Zeitstempel: gleiche Werte ergeben byte-gleiche Paks (wichtig fuer Koop)
                        e.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
                        using (Stream s = e.Open()) s.Write(data, 0, data.Length);
                    }
                }
                return ms.ToArray();
            }
        }

        public static string ModCode(byte[] data)
        {
            using (var md5 = MD5.Create())
            {
                string hex = BitConverter.ToString(md5.ComputeHash(data)).Replace("-", "");
                return hex.Substring(0, 4) + "-" + hex.Substring(4, 4);
            }
        }

        // ---------- Installierte Mod ----------

        public class Installed
        {
            public string Path, Code;
            public bool Legacy;
            public Dictionary<string, object> Diff;
        }

        static IEnumerable<string> ModPaks(string sourceDir)
        {
            foreach (string f in Directory.GetFiles(sourceDir, "data*.pak").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                int n;
                if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(f).Substring(4), out n) && n >= 2) yield return f;
            }
        }

        public static Installed FindInstalled(string sourceDir)
        {
            foreach (string f in ModPaks(sourceDir))
            {
                try
                {
                    string l1 = null, l2 = null;
                    using (ZipArchive z = ZipFile.OpenRead(f))
                    {
                        ZipArchiveEntry e = z.Entries.FirstOrDefault();
                        if (e == null) continue;
                        using (var r = new StreamReader(e.Open(), Txt.Latin1))
                        {
                            l1 = r.ReadLine();
                            l2 = r.ReadLine();
                        }
                    }
                    if (l1 == null || !l1.StartsWith(Marker, StringComparison.Ordinal)) continue;
                    var res = new Installed { Path = f, Legacy = l1.StartsWith("// DERO-WurfPatcher", StringComparison.Ordinal), Code = ModCode(File.ReadAllBytes(f)) };
                    if (l2 != null && l2.StartsWith(SettingsPrefix, StringComparison.Ordinal))
                        try { res.Diff = Json.ParseObject(l2.Substring(SettingsPrefix.Length)); } catch { }
                    return res;
                }
                catch { }
            }
            return null;
        }

        // Andere Mods, die dieselben Dateien aendern wie wir (einer ueberschreibt den anderen)
        public static List<string> FindConflicts(string sourceDir, string ownPak)
        {
            var res = new List<string>();
            foreach (string f in ModPaks(sourceDir))
            {
                if (ownPak != null && string.Equals(f, ownPak, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using (ZipArchive z = ZipFile.OpenRead(f))
                        if (z.Entries.Any(e => GameFiles.Needed.IsMatch(e.FullName.Replace('\\', '/'))))
                            res.Add(System.IO.Path.GetFileName(f));
                }
                catch { }
            }
            return res;
        }

        public static string FreeSlot(string sourceDir)
        {
            for (int n = 2; n <= 7; n++)
            {
                string p = System.IO.Path.Combine(sourceDir, "data" + n + ".pak");
                if (!File.Exists(p)) return p;
            }
            return null;
        }

        public static void WriteFile(string target, byte[] data)
        {
            string tmp = target + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);
        }

        public static bool GameRunning()
        {
            return Process.GetProcessesByName(GameProcess).Length > 0;
        }
    }
}
