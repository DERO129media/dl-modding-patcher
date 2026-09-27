// DERO-WurfPatcher fuer Dying Light: The Beast
// Liest die Originalwerte aus ph_ft\source\data0.pak und schreibt die geaenderten
// Skripte in eine eigene dataN.pak (N = 2..7). Die Originaldateien werden nie veraendert.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("DERO-WurfPatcher")]
[assembly: System.Reflection.AssemblyProduct("DERO-WurfPatcher")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace DeRo
{
    class TierValues
    {
        public int Scrap, Wiring, Blades, Output;
        public double HeadMult;
    }

    static class Mod
    {
        public const string Version = "1.0";
        public const string Marker = "// DERO-WurfPatcher";
        public const string CollectablesPath = "scripts/inventory/collectables_ft.scr";
        public const string InventoryPath = "scripts/inventory/inventory.scr";
        public const string GameProcess = "DyingLightGame_TheBeast_x64_rwdi";
        public static readonly string[] TierNames = { "T1 (Grün)", "T2 (Blau)", "T3 (Lila)", "T4 (Orange)" };
        static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        static readonly Regex CraftedRx = new Regex(@"CraftedItem\(\s*""(Throwable_ThrowingKnife_FT_T\d)""\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
        static readonly Regex HeadRx = new Regex(@"(?m)^([ \t]*)DamageHeadMult\(\s*([0-9.]+)\s*\)\s*;");

        public static TierValues[] DeroPreset()
        {
            var res = new TierValues[4];
            for (int i = 0; i < 4; i++)
                res[i] = new TierValues { Scrap = 5, Wiring = 1, Blades = 1, Output = 5, HeadMult = 3.0 };
            return res;
        }

        // ---------- Parsen ----------

        public static TierValues[] Parse(string collectables, string inventory)
        {
            var res = new TierValues[4];
            for (int t = 1; t <= 4; t++)
            {
                var v = new TierValues();
                string bp = GetBlock(collectables, BlueprintName(t));
                v.Scrap = ReadMaterial(bp, "Craft_Scrap");
                v.Wiring = ReadMaterial(bp, "Craft_wiring");
                v.Blades = ReadMaterial(bp, "Craft_Blades");
                Match m = CraftedRx.Match(bp);
                if (!m.Success) throw new InvalidDataException("CraftedItem fehlt bei Stufe T" + t);
                v.Output = int.Parse(m.Groups[2].Value);

                Match h = HeadRx.Match(GetBlock(inventory, KnifeName(t)));
                if (!h.Success) throw new InvalidDataException("DamageHeadMult fehlt bei Stufe T" + t);
                v.HeadMult = double.Parse(h.Groups[2].Value, CultureInfo.InvariantCulture);
                res[t - 1] = v;
            }
            return res;
        }

        static string BlueprintName(int t) { return "Craftplan_ThrowingKnives_FT_T" + t; }
        static string KnifeName(int t) { return "Throwable_ThrowingKnife_FT_T" + t; }

        static int ReadMaterial(string block, string material)
        {
            Match m = Regex.Match(block, @"(?mi)^[ \t]*RequiredItem\(\s*""" + material + @"""\s*,\s*(\d+)\s*\)");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        static void FindBlock(string text, string itemName, out int start, out int end)
        {
            string header = "Item(\"" + itemName + "\"";
            int h = text.IndexOf(header, StringComparison.Ordinal);
            if (h < 0) throw new InvalidDataException("Eintrag nicht gefunden: " + itemName);
            if (text.IndexOf(header, h + 1, StringComparison.Ordinal) >= 0)
                throw new InvalidDataException("Eintrag mehrfach vorhanden: " + itemName);
            int open = text.IndexOf('{', h);
            int depth = 0, i = open;
            for (; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) break;
            }
            if (open < 0 || i >= text.Length) throw new InvalidDataException("Block nicht lesbar: " + itemName);
            start = open;
            end = i + 1;
        }

        static string GetBlock(string text, string itemName)
        {
            int s, e;
            FindBlock(text, itemName, out s, out e);
            return text.Substring(s, e - s);
        }

        static string EditBlock(string text, string itemName, Func<string, string> edit)
        {
            int s, e;
            FindBlock(text, itemName, out s, out e);
            return text.Substring(0, s) + edit(text.Substring(s, e - s)) + text.Substring(e);
        }

        // ---------- Patchen ----------

        public static void Apply(string collectables, string inventory, TierValues[] values,
                                 out string newCollectables, out string newInventory)
        {
            for (int t = 1; t <= 4; t++)
            {
                TierValues v = values[t - 1];
                int tier = t;
                collectables = EditBlock(collectables, BlueprintName(t), delegate(string b)
                {
                    b = SetMaterial(b, "Craft_Scrap", v.Scrap);
                    b = SetMaterial(b, "Craft_wiring", v.Wiring);
                    b = SetMaterial(b, "Craft_Blades", v.Blades);
                    if (!CraftedRx.IsMatch(b)) throw new InvalidDataException("CraftedItem fehlt bei Stufe T" + tier);
                    // 2. Parameter ist unklar (evtl. Menge mit Skill) - wird nie kleiner als der Output
                    return CraftedRx.Replace(b, m => "CraftedItem(\"" + m.Groups[1].Value + "\", " + v.Output + ", "
                        + Math.Max(v.Output, int.Parse(m.Groups[3].Value)) + ")", 1);
                });
                inventory = EditBlock(inventory, KnifeName(t), delegate(string b)
                {
                    if (!HeadRx.IsMatch(b)) throw new InvalidDataException("DamageHeadMult fehlt bei Stufe T" + tier);
                    return HeadRx.Replace(b, m => m.Groups[1].Value + "DamageHeadMult("
                        + v.HeadMult.ToString("0.0##", CultureInfo.InvariantCulture) + ");", 1);
                });
            }
            string header = Marker + " v" + Version + " - automatisch erzeugt, bitte nicht von Hand bearbeiten\r\n";
            newCollectables = header + collectables;
            newInventory = header + inventory;
        }

        // Ersetzt RequiredItem(...) und AlternativePrice(...) fuer ein Material. 0 = Zeile auskommentieren.
        static string SetMaterial(string block, string material, int value)
        {
            var rx = new Regex(@"(?mi)^([ \t]*)(RequiredItem|AlternativePrice)\(\s*""(" + material + @")""\s*,\s*\d+\s*\)\s*;");
            bool hasRequired = false;
            string res = rx.Replace(block, m =>
            {
                if (m.Groups[2].Value == "RequiredItem") hasRequired = true;
                string call = m.Groups[2].Value + "(\"" + m.Groups[3].Value + "\", " + value + ");";
                return m.Groups[1].Value + (value > 0 ? call : "//" + call + " // DeRo: entfernt");
            });
            if (!hasRequired) throw new InvalidDataException("Material " + material + " nicht im Rezept gefunden");
            return res;
        }

        // ---------- Dateien ----------

        public static string ResolveSourceDir(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string[] candidates = { path, Path.Combine(path, "ph_ft", "source"), Path.Combine(path, "source") };
            foreach (string c in candidates)
                if (File.Exists(Path.Combine(c, "data0.pak"))) return Path.GetFullPath(c);
            return null;
        }

        public static string ReadEntry(string pak, string entry)
        {
            using (ZipArchive z = ZipFile.OpenRead(pak))
            {
                foreach (ZipArchiveEntry e in z.Entries)
                {
                    if (!string.Equals(e.FullName.Replace('\\', '/'), entry, StringComparison.OrdinalIgnoreCase)) continue;
                    using (Stream s = e.Open())
                    using (var r = new StreamReader(s, Latin1, false))
                        return r.ReadToEnd();
                }
            }
            return null;
        }

        public static void LoadOriginal(string sourceDir, out string collectables, out string inventory)
        {
            collectables = inventory = null;
            foreach (string name in new[] { "data0.pak", "data1.pak" })
            {
                string pak = Path.Combine(sourceDir, name);
                if (!File.Exists(pak)) continue;
                if (collectables == null) collectables = ReadEntry(pak, CollectablesPath);
                if (inventory == null) inventory = ReadEntry(pak, InventoryPath);
            }
            if (collectables == null || inventory == null)
                throw new InvalidDataException("Die Rezept-Dateien wurden in data0.pak nicht gefunden.");
        }

        static IEnumerable<string> ModPaks(string sourceDir)
        {
            foreach (string f in Directory.GetFiles(sourceDir, "data*.pak"))
            {
                int n;
                string num = Path.GetFileNameWithoutExtension(f).Substring(4);
                if (int.TryParse(num, out n) && n >= 2) yield return f;
            }
        }

        public static string FindDeroPak(string sourceDir)
        {
            foreach (string f in ModPaks(sourceDir))
            {
                try
                {
                    string c = ReadEntry(f, CollectablesPath);
                    if (c != null && c.StartsWith(Marker, StringComparison.Ordinal)) return f;
                }
                catch { }
            }
            return null;
        }

        public static List<string> FindConflicts(string sourceDir, string ownPak)
        {
            var res = new List<string>();
            foreach (string f in ModPaks(sourceDir))
            {
                if (ownPak != null && string.Equals(f, ownPak, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using (ZipArchive z = ZipFile.OpenRead(f))
                        foreach (ZipArchiveEntry e in z.Entries)
                        {
                            string n = e.FullName.Replace('\\', '/');
                            if (string.Equals(n, CollectablesPath, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(n, InventoryPath, StringComparison.OrdinalIgnoreCase))
                            {
                                res.Add(Path.GetFileName(f));
                                break;
                            }
                        }
                }
                catch { }
            }
            return res;
        }

        public static string FreeSlot(string sourceDir)
        {
            for (int n = 2; n <= 7; n++)
            {
                string p = Path.Combine(sourceDir, "data" + n + ".pak");
                if (!File.Exists(p)) return p;
            }
            return null;
        }

        public static void WritePak(string target, string collectables, string inventory)
        {
            string tmp = target + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (FileStream fs = File.Create(tmp))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteEntry(zip, CollectablesPath, collectables);
                WriteEntry(zip, InventoryPath, inventory);
            }
            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);
        }

        static void WriteEntry(ZipArchive zip, string name, string text)
        {
            byte[] data = Latin1.GetBytes(text);
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            // Fester Zeitstempel: gleiche Werte ergeben byte-gleiche Paks (wichtig fuer Koop)
            entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using (Stream s = entry.Open())
                s.Write(data, 0, data.Length);
        }

        // Kurzer Code zum Vergleichen im Koop: gleicher Code = gleiche Spieldaten
        public static string ModCode(string pak)
        {
            byte[] hash;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            using (FileStream fs = File.OpenRead(pak))
                hash = md5.ComputeHash(fs);
            string hex = BitConverter.ToString(hash).Replace("-", "");
            return hex.Substring(0, 4) + "-" + hex.Substring(4, 4);
        }

        public static bool GameRunning()
        {
            return Process.GetProcessesByName(GameProcess).Length > 0;
        }

        public static string AutoDetectGameDir()
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
    }

    class MainForm : Form
    {
        static readonly string[] Columns = { "Stufe", "Teile", "Drähte", "Klingen", "Output", "Kopfschuss ×" };
        static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
        static readonly Color Changed = Color.FromArgb(255, 241, 184);
        static readonly Color Accent = Color.FromArgb(196, 60, 40);
        static readonly Color Muted = Color.FromArgb(96, 96, 96);
        static readonly Color Warn = Color.FromArgb(176, 90, 0);

        TextBox txtPath;
        Button btnBrowse, btnDero, btnStd, btnPatch, btnRemove;
        DataGridView gridOrig, gridNew;
        Label lblInfo, lblWarn;
        string sourceDir, origCol, origInv, installedPak;
        TierValues[] original;
        float scale;
        int W;

        int S(int px) { return (int)Math.Round(px * scale); }

        public MainForm()
        {
            Text = "DERO-WurfPatcher – Dying Light: The Beast";
            Font = new Font("Segoe UI", 9f);
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            W = S(640);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            BackColor = Color.White;
            Padding = new Padding(S(18), S(14), S(18), S(14));

            var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

            root.Controls.Add(new Label { Text = "DERO-WurfPatcher", AutoSize = true, Font = new Font("Segoe UI", 15f, FontStyle.Bold) });
            root.Controls.Add(Wrap(new Label { Text = "Passt Crafting-Kosten, Output und Kopfschuss-Schaden der normalen Wurfmesser an. "
                + "Die Originaldateien des Spiels bleiben unverändert – der Mod landet als eigene dataN.pak daneben.", ForeColor = Muted }, S(12)));

            // Pfad
            var pathRow = new TableLayoutPanel { ColumnCount = 3, Width = W, Height = S(32), Margin = new Padding(0, 0, 0, S(12)) };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathRow.Controls.Add(new Label { Text = "Spielordner:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, S(6), 0) });
            txtPath = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, BackColor = Color.FromArgb(246, 246, 246), Margin = new Padding(0, S(4), S(6), 0) };
            pathRow.Controls.Add(txtPath);
            btnBrowse = MakeButton("Ändern…", false);
            btnBrowse.Margin = new Padding(0);
            btnBrowse.Click += OnBrowse;
            pathRow.Controls.Add(btnBrowse);
            root.Controls.Add(pathRow);

            // Tabellen
            root.Controls.Add(SectionLabel("Original im Spiel"));
            gridOrig = MakeGrid(false);
            root.Controls.Add(gridOrig);

            root.Controls.Add(SectionLabel("Deine Werte (Zellen anklicken zum Ändern – gelb = geändert)"));
            gridNew = MakeGrid(true);
            root.Controls.Add(gridNew);

            var presets = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, S(6), 0, S(10)), WrapContents = false };
            btnDero = MakeButton("DeRo-Settings laden", false);
            btnDero.Click += delegate { FillGrid(gridNew, Mod.DeroPreset()); };
            btnStd = MakeButton("Standardwerte laden", false);
            btnStd.Click += delegate { if (original != null) FillGrid(gridNew, original); };
            presets.Controls.Add(btnDero);
            presets.Controls.Add(btnStd);
            root.Controls.Add(presets);

            lblInfo = Wrap(new Label(), S(4));
            lblWarn = Wrap(new Label { ForeColor = Warn }, S(8));
            root.Controls.Add(lblInfo);
            root.Controls.Add(lblWarn);

            var actions = new FlowLayoutPanel { Width = W, Height = S(40), FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0) };
            btnPatch = MakeButton("Patchen", true);
            btnPatch.Click += OnPatch;
            btnRemove = MakeButton("Mod entfernen", false);
            btnRemove.Click += OnRemove;
            actions.Controls.Add(btnPatch);
            actions.Controls.Add(btnRemove);
            root.Controls.Add(actions);

            Controls.Add(root);
            SetEnabled(false);

            Shown += delegate
            {
                string dir = Mod.AutoDetectGameDir();
                ActiveControl = btnPatch;
                if (dir != null) LoadGame(dir);
                else ShowInfo("Spielordner nicht automatisch gefunden – klick auf „Ändern…“ und wähle den Ordner „Dying Light The Beast“.", true);
            };
        }

        Label Wrap(Label l, int bottom)
        {
            l.AutoSize = true;
            l.MaximumSize = new Size(W, 0);
            l.Margin = new Padding(0, 0, 0, bottom);
            return l;
        }

        Label SectionLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Margin = new Padding(0, S(4), 0, S(4)) };
        }

        Button MakeButton(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                Padding = new Padding(S(10), S(3), S(10), S(3)), Margin = new Padding(0, 0, S(8), 0),
                BackColor = primary ? Accent : Color.FromArgb(242, 242, 242),
                ForeColor = primary ? Color.White : Color.Black
            };
            if (primary) b.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(200, 200, 200);
            return b;
        }

        DataGridView MakeGrid(bool editable)
        {
            var g = new DataGridView
            {
                Width = W, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeColumns = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
                ScrollBars = ScrollBars.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect, EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = S(28), GridColor = Color.FromArgb(225, 225, 225),
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2, Margin = new Padding(0, 0, 0, S(8))
            };
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(244, 244, 244);
            g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            g.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            g.RowTemplate.Height = S(26);
            for (int i = 0; i < Columns.Length; i++)
            {
                var c = new DataGridViewTextBoxColumn { HeaderText = Columns[i], SortMode = DataGridViewColumnSortMode.NotSortable };
                if (i == 0)
                {
                    c.FillWeight = 140;
                    c.ReadOnly = true;
                    c.DefaultCellStyle.Padding = new Padding(S(6), 0, 0, 0);
                }
                else c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                g.Columns.Add(c);
            }
            g.Rows.Add(4);
            for (int r = 0; r < 4; r++) g.Rows[r].Cells[0].Value = Mod.TierNames[r];
            g.Height = S(28) + 4 * S(26) + 2;

            if (editable)
            {
                g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(205, 225, 250);
                g.DefaultCellStyle.SelectionForeColor = Color.Black;
                g.Columns[0].DefaultCellStyle.ForeColor = Muted;
                g.CellValidating += OnCellValidating;
                g.CellEndEdit += delegate(object s, DataGridViewCellEventArgs e) { g.Rows[e.RowIndex].ErrorText = ""; Highlight(); };
                g.CellClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.ColumnIndex > 0) g.BeginEdit(true); };
            }
            else
            {
                g.ReadOnly = true;
                g.DefaultCellStyle.ForeColor = Muted;
                g.DefaultCellStyle.SelectionBackColor = Color.White;
                g.DefaultCellStyle.SelectionForeColor = Muted;
            }
            return g;
        }

        // ---------- Werte <-> Tabelle ----------

        static string Fmt(double d) { return d.ToString("0.0#", De); }

        void FillGrid(DataGridView g, TierValues[] vals)
        {
            for (int r = 0; r < 4; r++)
            {
                DataGridViewCellCollection c = g.Rows[r].Cells;
                if (vals == null) { for (int i = 1; i < 6; i++) c[i].Value = ""; continue; }
                c[1].Value = vals[r].Scrap.ToString();
                c[2].Value = vals[r].Wiring.ToString();
                c[3].Value = vals[r].Blades.ToString();
                c[4].Value = vals[r].Output.ToString();
                c[5].Value = Fmt(vals[r].HeadMult);
            }
            if (g == gridNew) Highlight();
            g.ClearSelection();
        }

        static bool TryParseCell(int col, string s, out double value, out string error)
        {
            error = null;
            s = (s ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) { error = "Bitte eine Zahl eingeben."; return false; }
            if (col == 5)
            {
                if (value < 0.5 || value > 20) { error = "Kopfschuss-Multiplikator: 0,5 bis 20"; return false; }
                return true;
            }
            if (value != Math.Floor(value)) { error = "Bitte eine ganze Zahl eingeben."; return false; }
            int min = col == 4 ? 1 : 0;
            if (value < min || value > 99) { error = (col == 4 ? "Output: 1" : "Materialien: 0") + " bis 99"; return false; }
            return true;
        }

        void OnCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.ColumnIndex == 0) return;
            double v;
            string err;
            if (!TryParseCell(e.ColumnIndex, Convert.ToString(e.FormattedValue), out v, out err))
            {
                gridNew.Rows[e.RowIndex].ErrorText = err;
                e.Cancel = true;
            }
        }

        TierValues[] ReadGrid()
        {
            var res = new TierValues[4];
            for (int r = 0; r < 4; r++)
            {
                var d = new double[6];
                for (int c = 1; c < 6; c++)
                {
                    string err;
                    if (!TryParseCell(c, Convert.ToString(gridNew.Rows[r].Cells[c].Value), out d[c], out err))
                    {
                        MessageBox.Show(this, Mod.TierNames[r] + ", " + Columns[c] + ": " + err, "Ungültiger Wert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return null;
                    }
                }
                res[r] = new TierValues { Scrap = (int)d[1], Wiring = (int)d[2], Blades = (int)d[3], Output = (int)d[4], HeadMult = d[5] };
                if (res[r].Scrap + res[r].Wiring + res[r].Blades == 0)
                {
                    MessageBox.Show(this, Mod.TierNames[r] + ": Mindestens ein Material muss mehr als 0 kosten.", "Ungültiger Wert", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
            }
            return res;
        }

        void Highlight()
        {
            var bold = new Font(gridNew.Font, FontStyle.Bold);
            for (int r = 0; r < 4; r++)
                for (int c = 1; c < 6; c++)
                {
                    DataGridViewCell cell = gridNew.Rows[r].Cells[c];
                    double now, orig;
                    string err;
                    bool diff = original != null
                        && TryParseCell(c, Convert.ToString(cell.Value), out now, out err)
                        && TryParseCell(c, Convert.ToString(gridOrig.Rows[r].Cells[c].Value), out orig, out err)
                        && Math.Abs(now - orig) > 1e-9;
                    cell.Style.BackColor = diff ? Changed : Color.Empty;
                    cell.Style.Font = diff ? bold : null;
                }
        }

        // ---------- Ablauf ----------

        void SetEnabled(bool on)
        {
            gridNew.Enabled = btnDero.Enabled = btnStd.Enabled = btnPatch.Enabled = on;
            btnRemove.Enabled = on && installedPak != null;
            btnPatch.BackColor = on ? Accent : Color.FromArgb(215, 160, 150);
        }

        void ShowInfo(string text, bool warn)
        {
            lblInfo.Text = text;
            lblInfo.ForeColor = warn ? Warn : Color.Black;
        }

        void LoadGame(string path)
        {
            txtPath.Text = path;
            sourceDir = Mod.ResolveSourceDir(path);
            original = null;
            installedPak = null;
            lblWarn.Text = "";
            FillGrid(gridOrig, null);
            FillGrid(gridNew, null);

            if (sourceDir == null)
            {
                ShowInfo("In diesem Ordner wurde Dying Light: The Beast nicht gefunden. Wähle den Ordner „Dying Light The Beast“ aus.", true);
                SetEnabled(false);
                return;
            }
            try
            {
                Mod.LoadOriginal(sourceDir, out origCol, out origInv);
                original = Mod.Parse(origCol, origInv);
            }
            catch (Exception ex)
            {
                ShowInfo("Die Spieldaten konnten nicht gelesen werden (evtl. hat ein Spiel-Update die Dateien verändert): " + ex.Message, true);
                SetEnabled(false);
                return;
            }
            FillGrid(gridOrig, original);

            installedPak = Mod.FindDeroPak(sourceDir);
            TierValues[] current = null;
            bool outdated = false;
            if (installedPak != null)
            {
                try
                {
                    string c = Mod.ReadEntry(installedPak, Mod.CollectablesPath);
                    string i = Mod.ReadEntry(installedPak, Mod.InventoryPath);
                    current = Mod.Parse(c, i);
                    string expC, expI;
                    Mod.Apply(origCol, origInv, current, out expC, out expI);
                    outdated = expC != c || expI != i;
                }
                catch { outdated = true; }
            }
            FillGrid(gridNew, current ?? Mod.DeroPreset());

            if (installedPak == null)
                ShowInfo("Mod ist noch nicht installiert. Die DeRo-Settings sind vorausgewählt – einfach auf „Patchen“ klicken.", false);
            else if (outdated)
                ShowInfo("Das Spiel wurde seit dem letzten Patchen aktualisiert (" + Path.GetFileName(installedPak) + " ist veraltet). Bitte einmal neu auf „Patchen“ klicken.", true);
            else
                ShowInfo("✔ Mod ist installiert (" + Path.GetFileName(installedPak) + ").   Mod-Code: " + Mod.ModCode(installedPak)
                    + "\nFür Koop müssen beide Spieler denselben Mod-Code haben.", false);

            List<string> conflicts = Mod.FindConflicts(sourceDir, installedPak);
            if (conflicts.Count > 0)
                lblWarn.Text = "Achtung: " + string.Join(", ", conflicts) + " ändert dieselben Spieldateien. "
                    + "Die Mods vertragen sich nicht – einer überschreibt den anderen.";
            SetEnabled(true);
        }

        void OnBrowse(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Wähle den Ordner „Dying Light The Beast“ (in steamapps\\common).";
                dlg.ShowNewFolderButton = false;
                if (!string.IsNullOrEmpty(txtPath.Text) && Directory.Exists(txtPath.Text)) dlg.SelectedPath = txtPath.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadGame(dlg.SelectedPath);
            }
        }

        void OnPatch(object sender, EventArgs e)
        {
            if (!gridNew.EndEdit()) return;
            if (Mod.GameRunning())
            {
                MessageBox.Show(this, "Bitte schließ zuerst das Spiel.", "Spiel läuft noch", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            TierValues[] vals = ReadGrid();
            if (vals == null) return;
            string target = installedPak ?? Mod.FreeSlot(sourceDir);
            if (target == null)
            {
                MessageBox.Show(this, "Alle Mod-Plätze (data2.pak bis data7.pak) sind schon belegt.", "Kein Platz frei", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                string c, i;
                Mod.Apply(origCol, origInv, vals, out c, out i);
                Mod.WritePak(target, c, i);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Windows lässt das Tool nicht in den Spielordner schreiben.\n\nSchließ das Tool, klick mit rechts auf die .exe und wähle „Als Administrator ausführen“.",
                    "Keine Schreibrechte", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Patchen fehlgeschlagen:\n\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            MessageBox.Show(this, "Fertig! Der Mod wurde als " + Path.GetFileName(target) + " gespeichert.\n\n"
                + "Mod-Code: " + Mod.ModCode(target) + "\n"
                + "Für Koop müssen beide Spieler denselben Mod-Code haben (gleiche Werte patchen).\n\n"
                + "Beim Spielstart erscheint „Modified game data detected“ – das ist normal und zeigt, dass der Mod geladen wird.",
                "Gepatcht", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadGame(txtPath.Text);
        }

        void OnRemove(object sender, EventArgs e)
        {
            if (installedPak == null) return;
            if (MessageBox.Show(this, "Mod entfernen? Dabei wird " + Path.GetFileName(installedPak) + " gelöscht, danach gelten wieder die Originalwerte.",
                    "Mod entfernen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (Mod.GameRunning())
            {
                MessageBox.Show(this, "Bitte schließ zuerst das Spiel.", "Spiel läuft noch", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { File.Delete(installedPak); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Löschen fehlgeschlagen:\n\n" + ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            LoadGame(txtPath.Text);
        }
    }

    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            // Test ohne Oberflaeche: --selftest <Spielordner> <Ziel.pak>
            if (args.Length == 3 && args[0] == "--selftest") return SelfTest(args[1], args[2]);
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        static int SelfTest(string gameDir, string outPak)
        {
            var log = new StringBuilder();
            try
            {
                string src = Mod.ResolveSourceDir(gameDir);
                string c, i, nc, ni;
                Mod.LoadOriginal(src, out c, out i);
                log.AppendLine("source: " + src);
                Dump(log, "original", Mod.Parse(c, i));
                Mod.Apply(c, i, Mod.DeroPreset(), out nc, out ni);
                Mod.WritePak(outPak, nc, ni);
                Dump(log, "patched", Mod.Parse(Mod.ReadEntry(outPak, Mod.CollectablesPath), Mod.ReadEntry(outPak, Mod.InventoryPath)));
                log.AppendLine("installed: " + (Mod.FindDeroPak(src) ?? "-"));
                File.WriteAllText(outPak + ".log", log.ToString());
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(outPak + ".log", log + "ERROR: " + ex);
                return 1;
            }
        }

        static void Dump(StringBuilder log, string title, TierValues[] v)
        {
            log.AppendLine(title + ":");
            for (int t = 0; t < 4; t++)
                log.AppendLine(string.Format(CultureInfo.InvariantCulture, "  T{0}: scrap={1} wiring={2} blades={3} output={4} head={5}",
                    t + 1, v[t].Scrap, v[t].Wiring, v[t].Blades, v[t].Output, v[t].HeadMult));
        }
    }
}
