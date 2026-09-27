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
            // NaN/Unendlich zuerst: an ihnen scheitert jeder Vergleich, sie wuerden sonst alle Grenzen umgehen
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new InvalidDataException("Ungültiger Wert bei " + id);
            if (v < min - 1e-9 || v > max + 1e-9) throw new InvalidDataException("Wert außerhalb des Bereichs bei " + id);
            return v;
        }

        // Ganze Zahl (Rezeptmengen): 2.5 wuerde sonst still zu 2, im Pak-Kopf stuende aber 2.5
        static int Int(object o, string id, int min, int max)
        {
            double v = Num(o, id, min, max);
            if (v != Math.Floor(v)) throw new InvalidDataException("Keine ganze Zahl bei " + id);
            return (int)v;
        }

        static double Factor(object o, string id) { return Num(o, id, 0.5, 5); }

        static void ApplyGlobal(PatchCtx ctx, GameModel m, string id, object val)
        {
            GItem i;
            if (!m.GItems.TryGetValue(id, out i)) throw new InvalidDataException("Unbekannte Einstellung: " + id);
            double v = Num(val, id, i.Min, i.Max);
            if (Math.Abs(v - i.Value) < 1e-12) return;
            i.Apply(ctx, v, i.Value);
            CheckBounds(ctx, i);
        }

        // Nach Skalierung und Rundung: jeder geschriebene Wert (alle Schwierigkeiten, Perma-World) muss in den Grenzen liegen
        static void CheckBounds(PatchCtx ctx, GItem i)
        {
            if (i.Params == null) return;
            foreach (string path in ctx.Paths(GameModel.PvFiles))
                foreach (string name in i.Params)
                    foreach (Match mm in GameModel.ParamRx(name).Matches(ctx.Get(path)))
                    {
                        double x = Txt.D(mm.Groups[2].Value);
                        if ((!double.IsNaN(i.Lo) && x < i.Lo - 1e-9) || (!double.IsNaN(i.Hi) && x > i.Hi + 1e-9))
                            throw new InvalidDataException(i.Label + ": ergibt in " + System.IO.Path.GetFileName(path) + " den Wert "
                                + x.ToString(Txt.Inv) + ", erlaubt ist " + i.Lo.ToString(Txt.Inv) + " bis " + i.Hi.ToString(Txt.Inv) + ".");
                    }
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
                var newMats = mats.Select((x, j) => Int(x, famId, 0, 999)).ToList();
                if (newMats.All(x => x == 0)) throw new InvalidDataException(f.Name + ": Mindestens ein Material muss mehr als 0 kosten.");
                int outN = Int(o.ContainsKey("out") ? o["out"] : null, famId, 1, 999);
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
            public string Foreign;   // gesetzt, wenn die Pak nicht vollstaendig vom DERO-Patcher stammt -> nie ersetzen oder loeschen
        }

        // Sicherungen ersetzter/entfernter Mod-Paks (null = keine, z. B. im Selbsttest)
        public static string BackupDir;
        const int KeepBackups = 20;
        static readonly Regex SlotName = new Regex(@"^data([2-7])\.pak$", RegexOptions.IgnoreCase);

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
                Installed res = Inspect(f);
                if (res != null) return res;
            }
            return null;
        }

        // null = gueltiges Archiv ohne DERO-Datei. Sonst muss JEDE Datei im Archiv vom DERO-Patcher stammen
        // (Kopfzeile, bekanntes Skript, alle mit denselben Einstellungen) - sonst ist Foreign gesetzt.
        // bytes: bereits gelesener Inhalt (Pruefung und spaeteres Ersetzen beziehen sich dann auf genau diese Bytes).
        public static Installed Inspect(string f, byte[] bytes = null)
        {
            try
            {
                var foreign = new List<string>();
                int own = 0;
                bool legacy = false, mixed = false;
                string settings = null;
                using (Stream st = bytes != null ? (Stream)new MemoryStream(bytes, false) : File.OpenRead(f))
                using (var z = new ZipArchive(st, ZipArchiveMode.Read))
                    foreach (ZipArchiveEntry e in z.Entries)
                    {
                        string name = e.FullName.Replace('\\', '/');
                        string l1 = null, l2 = null;
                        if (!name.EndsWith("/"))
                            using (var r = new StreamReader(e.Open(), Txt.Latin1))
                            {
                                l1 = r.ReadLine();
                                l2 = r.ReadLine();
                            }
                        if (l1 == null || !l1.StartsWith(Marker, StringComparison.Ordinal) || !GameFiles.Needed.IsMatch(name)) { foreign.Add(name); continue; }
                        bool leg = l1.StartsWith("// DERO-WurfPatcher", StringComparison.Ordinal);
                        if (own++ == 0) { legacy = leg; settings = l2; }
                        else if (leg != legacy || (!leg && l2 != settings)) mixed = true;
                    }
                if (own == 0) return null;
                var res = new Installed { Path = f, Legacy = legacy, Code = ModCode(bytes ?? File.ReadAllBytes(f)) };
                if (!legacy && settings != null && settings.StartsWith(SettingsPrefix, StringComparison.Ordinal))
                    try { res.Diff = Json.ParseObject(settings.Substring(SettingsPrefix.Length)); } catch { }
                if (foreign.Count > 0)
                    res.Foreign = "enthält auch fremde Dateien (" + string.Join(", ", foreign.Take(3).Select(x => System.IO.Path.GetFileName(x.TrimEnd('/')))) + (foreign.Count > 3 ? ", …" : "") + ")";
                else if (mixed) res.Foreign = "enthält Dateien aus verschiedenen Patches";
                else if (!legacy && res.Diff == null) res.Foreign = "die gespeicherten Einstellungen fehlen oder sind beschädigt";
                return res;
            }
            // Beschaedigt, gesperrt oder nicht lesbar: koennte unsere sein. Nicht einfach einen anderen Platz nehmen
            // (sonst doppelte Mods), sondern anhalten.
            catch (InvalidDataException) { return new Installed { Path = f, Foreign = "ist kein lesbares Archiv (beschädigt?)" }; }
            catch (Exception ex) { return new Installed { Path = f, Foreign = "konnte nicht gelesen werden (" + ex.Message.TrimEnd('.') + ")" }; }
        }

        // Namensregeln fuer jede Datei, die das Tool schreibt oder loescht. Windows schneidet Punkte/Leerzeichen am Ende ab
        // ("data0.pak." -> data0.pak) und liest ":" als Datenstrom, deshalb beides ablehnen statt normalisieren.
        public static void CheckName(string target)
        {
            string name = System.IO.Path.GetFileName(target) ?? "";
            if (name.Length == 0 || name != name.TrimEnd('.', ' ') || name.IndexOf(':') >= 0)
                throw new InvalidDataException("Ungültiger Dateiname: „" + name + "“.");
            if (name.StartsWith("data", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && !SlotName.IsMatch(name))
                throw new InvalidDataException(name + " ist eine Originaldatei des Spiels und wird nie verändert.");
        }

        // Liest eine vorhandene Zieldatei einmal und prueft, dass sie vollstaendig vom DERO-Patcher stammt.
        // null = Datei gibt es nicht. Nie Originaldateien, nie fremde Mods.
        static byte[] ReadOwned(string target)
        {
            CheckName(target);
            if (!File.Exists(target)) return null;
            byte[] b = File.ReadAllBytes(target);
            string name = System.IO.Path.GetFileName(target);
            Installed own = Inspect(target, b);
            if (own == null) throw new InvalidDataException(name + " stammt nicht vom DERO-Patcher und wird nicht angefasst.");
            if (own.Foreign != null) throw new InvalidDataException(name + " " + own.Foreign + " und wird deshalb nicht angefasst.");
            return b;
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

        // Schreibt die Mod-Pak. Ablauf bei einer vorhandenen Datei:
        //  1. einmal lesen und pruefen (ReadOwned), genau diese Bytes sichern
        //  2. neue Datei unter einem zufaelligen Namen exklusiv anlegen (nie eine fremde .tmp oder einen Hardlink beschreiben)
        //  3. File.Replace mit eigener Ruecksicherung neben dem Ziel; scheitert der Tausch, wird sie zurueckgelegt
        //  4. war die ersetzte Datei nicht mehr die gepruefte (anderes Programm dazwischen), wird sie zurueckgelegt
        public static void WriteFile(string target, byte[] data)
        {
            CheckName(target);   // vor GetFullPath: das schneidet Punkte/Leerzeichen am Ende ab
            target = System.IO.Path.GetFullPath(target);
            byte[] old = ReadOwned(target);
            if (old != null && old.SequenceEqual(data)) return;
            string name = System.IO.Path.GetFileName(target);
            string tmp = CreateNew(target, ".neu", data), bak = null;
            bool keepTmp = false;
            try
            {
                if (old == null) { File.Move(tmp, target); return; }   // scheitert, falls inzwischen eine Datei da ist
                Backup(name, old);
                bak = FreeName(target, ".alt");
                try { File.Replace(tmp, target, bak, true); }
                catch
                {
                    // ReplaceFile kann das Ziel schon umbenannt haben (Fehler 1177): zuruecklegen
                    if (!File.Exists(target) && File.Exists(bak)) try { File.Move(bak, target); } catch { }
                    if (!File.Exists(target)) keepTmp = true;   // Zustand unklar: nichts weiter loeschen
                    else bak = null;
                    throw;
                }
                if (!File.ReadAllBytes(bak).SequenceEqual(old))
                {
                    File.Replace(bak, target, null, true);
                    bak = null;
                    throw new InvalidDataException(name + " wurde während des Patchens von einem anderen Programm geändert. Das Tool hat sie zurückgelegt und nichts ersetzt.");
                }
            }
            finally
            {
                if (!keepTmp)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    try { if (bak != null && File.Exists(bak) && File.Exists(target)) File.Delete(bak); } catch { }
                }
            }
        }

        // Entfernt die eigene Mod-Pak (vorher gesichert). Erst umbenennen, dann pruefen, ob es noch die gepruefte ist.
        public static void RemoveFile(string target)
        {
            CheckName(target);   // vor GetFullPath: das schneidet Punkte/Leerzeichen am Ende ab
            target = System.IO.Path.GetFullPath(target);
            byte[] old = ReadOwned(target);
            if (old == null) return;
            string name = System.IO.Path.GetFileName(target);
            Backup(name, old);
            string q = FreeName(target, ".entfernt");
            File.Move(target, q);
            if (!File.ReadAllBytes(q).SequenceEqual(old))
            {
                File.Move(q, target);
                throw new InvalidDataException(name + " wurde während des Entfernens von einem anderen Programm geändert. Das Tool hat sie zurückgelegt.");
            }
            File.Delete(q);
        }

        // Freier Name neben dem Ziel, z. B. data2.pak.3f9a1c2b.alt (endet nicht auf .pak, wird also vom Spiel nicht geladen)
        static string FreeName(string target, string ext)
        {
            string p;
            do p = target + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ext;
            while (File.Exists(p));
            return p;
        }

        static string CreateNew(string target, string ext, byte[] data)
        {
            for (int i = 0; ; i++)
            {
                string p = FreeName(target, ext);
                try
                {
                    using (var fs = new FileStream(p, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                        fs.Write(data, 0, data.Length);
                    return p;
                }
                catch (IOException) { if (i >= 5 || !File.Exists(p)) throw; }   // Name war doch belegt: neuer Versuch
            }
        }

        static void Backup(string name, byte[] data)
        {
            if (BackupDir == null) return;
            Directory.CreateDirectory(BackupDir);
            // Millisekunden + Zaehler: zwei Sicherungen kurz hintereinander duerfen sich nicht ueberschreiben
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff", Txt.Inv);
            for (int n = 1; ; n++)
            {
                string dest = System.IO.Path.Combine(BackupDir, stamp + (n == 1 ? "" : "-" + n) + "_" + name);
                if (File.Exists(dest)) continue;
                using (var fs = new FileStream(dest, FileMode.CreateNew, FileAccess.Write)) fs.Write(data, 0, data.Length);
                break;
            }
            foreach (FileInfo f in new DirectoryInfo(BackupDir).GetFiles("*.pak").OrderByDescending(x => x.Name, StringComparer.Ordinal).Skip(KeepBackups))
                try { f.Delete(); } catch { }
        }

        public static bool GameRunning()
        {
            return Process.GetProcessesByName(GameProcess).Length > 0;
        }
    }
}
