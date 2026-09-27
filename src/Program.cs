// DERO-Patcher fuer Dying Light: The Beast
// Startet einen lokalen Server und oeffnet die Oberflaeche in einem eigenen Edge-Fenster (App-Modus).
// Die Originaldateien des Spiels werden nie veraendert; der Mod ist eine eigene dataN.pak.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("DERO-Patcher")]
[assembly: System.Reflection.AssemblyProduct("DERO-Patcher")]
[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]

namespace Dero
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        static Process browser;

        [STAThread]
        static int Main(string[] args)
        {
            var a = new List<string>(args);
            Func<string, string> arg = name => { int i = a.IndexOf(name); return i >= 0 && i + 1 < a.Count ? a[i + 1] : null; };

            // Test ohne Oberflaeche: --selftest <Spielordner> <Ziel.pak> [dero|<diff.json>]
            if (a.Count >= 3 && a[0] == "--selftest") return SelfTest(a[1], a[2], a.Count > 3 ? a[3] : "dero");
            // Daten fuer die UI-Vorschau im Browser: --export-data <ui/data.js> [--game <Ordner>]
            if (a.Count >= 2 && a[0] == "--export-data") return ExportData(a[1], arg("--game"));

            // Update ohne Oberflaeche (Support/Test): --update-now -> Exit 0 = aktualisiert, 2 = schon aktuell, 1 = Fehler
            if (a.Contains("--update-now")) return UpdateNow();

            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Updater.CleanupOld();
            bool dev = a.Contains("--dev");

            Mutex mutex = null;
            if (!dev)
            {
                bool created;
                mutex = new Mutex(true, "DERO-Patcher-" + Environment.UserName, out created);
                if (!created)
                {
                    bool got = false;
                    if (a.Contains("--after-update"))
                        try { got = mutex.WaitOne(20000); } catch (AbandonedMutexException) { got = true; }
                    if (!got)
                    {
                        MessageBox.Show("Der DERO-Patcher ist schon geöffnet.", "DERO-Patcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return 0;
                    }
                }
            }

            try
            {
                App.Init(arg("--game"));
                string uiDir = dev ? (arg("--ui") ?? Path.Combine(Path.GetDirectoryName(Updater.ExePath), "..", "ui")) : null;
                var server = new Server(dev ? int.Parse(arg("--port") ?? "8732") : 0, dev ? "dev" : NewToken(), uiDir);
                server.IgnoreRunning = dev && a.Contains("--ignore-running");
                server.Start();
                string url = "http://127.0.0.1:" + server.Port + "/?t=" + server.Token;
                App.RestartForUpdate = delegate
                {
                    CloseBrowser();
                    Process.Start(Updater.ExePath, "--after-update");
                    App.Exit.Set();
                };

                if (dev)
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "dero-dev-url.txt"), url);
                    App.Exit.WaitOne();
                    return 0;
                }

                browser = OpenBrowser(url);
                DateTime launched = DateTime.Now;
                while (!App.Exit.WaitOne(1000))
                {
                    if (browser != null)
                    {
                        if (!browser.HasExited) continue;
                        // Sofort beendet = an ein laufendes Edge uebergeben -> auf Heartbeat umschalten
                        if ((DateTime.Now - launched).TotalSeconds < 8) { browser = null; server.LastPing = DateTime.Now; continue; }
                        break;
                    }
                    DateTime now = DateTime.Now;
                    if (server.ByeAt > server.LastPing && (now - server.ByeAt).TotalSeconds > 10) break;
                    if ((now - server.LastPing).TotalMinutes > 5) break;
                }
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Der DERO-Patcher konnte nicht starten:\n\n" + ex.Message, "DERO-Patcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            finally
            {
                if (mutex != null) try { mutex.ReleaseMutex(); } catch { }
            }
        }

        static string NewToken()
        {
            var b = new byte[16];
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
        }

        // ---------- Fenster ----------

        static string FindBrowser()
        {
            var candidates = new List<string>();
            foreach (string exe in new[] { "msedge.exe", "chrome.exe" })
                foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
                    try
                    {
                        using (RegistryKey k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                            if (k != null && k.GetValue(null) is string) candidates.Add(((string)k.GetValue(null)).Trim('"'));
                    }
                    catch { }
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            candidates.Add(Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"));
            candidates.Add(Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe"));
            candidates.Add(Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"));
            return candidates.FirstOrDefault(File.Exists);
        }

        static Process OpenBrowser(string url)
        {
            string exe = FindBrowser();
            if (exe != null)
            {
                string profile = Path.Combine(App.DataDir, "browser");
                string args = "--app=\"" + url + "\" --user-data-dir=\"" + profile + "\" --no-first-run --no-default-browser-check "
                    + "--disable-sync --disable-features=Translate --hide-crash-restore-bubble --window-size=1440,920";
                try { return Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false }); }
                catch { }
            }
            Process.Start(url);   // Standardbrowser, Lebensdauer dann ueber Heartbeat
            return null;
        }

        static void CloseBrowser()
        {
            try
            {
                if (browser == null || browser.HasExited) return;
                browser.CloseMainWindow();
                if (!browser.WaitForExit(3000)) browser.Kill();
            }
            catch { }
        }

        public static string BrowseFolder(string start)
        {
            string result = null;
            var t = new Thread(delegate()
            {
                using (var owner = new Form { TopMost = true, ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, Opacity = 0, Width = 1, Height = 1, StartPosition = FormStartPosition.CenterScreen })
                using (var dlg = new FolderBrowserDialog { Description = "Wähle den Ordner „Dying Light The Beast“ (in steamapps\\common).", ShowNewFolderButton = false })
                {
                    if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) dlg.SelectedPath = start;
                    owner.Show();
                    owner.Activate();
                    if (dlg.ShowDialog(owner) == DialogResult.OK) result = dlg.SelectedPath;
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            return result;
        }

        // ---------- Werkzeuge fuer Entwicklung und Tests ----------

        static int UpdateNow()
        {
            string log = Updater.ExePath + ".update.log";
            try
            {
                Updater.Release r = Updater.Latest();
                if (!Updater.IsNewer(r.Version, App.Version)) { File.WriteAllText(log, "aktuell: " + App.Version + " (neueste: " + r.Version + ")"); return 2; }
                Updater.Install(r);
                File.WriteAllText(log, "aktualisiert: " + App.Version + " -> " + r.Version);
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(log, "Fehler: " + ex.Message);
                return 1;
            }
        }

        static int ExportData(string outFile, string gameDir)
        {
            GameFiles g = GameFiles.Load(gameDir ?? GameFiles.AutoDetect());
            GameModel m = GameModel.Build(g);
            File.WriteAllText(outFile, "window.DATA = " + Json.Write(m.Data) + ";\n", new UTF8Encoding(false));
            return 0;
        }

        // DeRo-Voreinstellung: Wurfmesser 1 Draht, 5 Stueck, Kopfschuss x3 (wie in der Oberflaeche)
        public static Dictionary<string, object> DeroDiff(GameModel m)
        {
            Family f = m.FamById["Craftplan_ThrowingKnives_FT"];
            var tiers = f.Tiers.Select(t => (object)Json.O(
                "mats", t.Mats.Select(x => (object)(x.Key.IndexOf("wiring", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : x.Value)).ToList(),
                "out", 5, "head", 3)).ToList();
            return Json.O("craft", Json.O(f.Id, tiers));
        }

        static int SelfTest(string gameDir, string outPak, string settings)
        {
            var log = new StringBuilder();
            try
            {
                GameFiles g = GameFiles.Load(gameDir);
                GameModel m = GameModel.Build(g);
                log.AppendLine("source: " + g.SourceDir);
                log.AppendLine("families: " + m.Families.Count + ", global: " + m.GItems.Count + ", loot: " + m.LootSubs.Count + ", dis: " + m.DisSets.Count);
                Dictionary<string, object> diff = settings == "dero" ? DeroDiff(m)
                    : Json.ParseObject(File.ReadAllText(settings));
                if (diff.ContainsKey("d")) diff = Json.Obj(diff["d"]);
                PatchCtx ctx = Patcher.Apply(m, g, diff);
                byte[] pak = Patcher.BuildPak(ctx, diff);
                byte[] again = Patcher.BuildPak(Patcher.Apply(m, g, Json.ParseObject(Patcher.Canonical(diff))), diff);
                Patcher.WriteFile(outPak, pak);
                log.AppendLine("files: " + string.Join(", ", ctx.Changed.Keys));
                log.AppendLine("code: " + Patcher.ModCode(pak) + (pak.SequenceEqual(again) ? " (deterministisch)" : " (NICHT deterministisch!)"));
                Patcher.Installed inst = Patcher.FindInstalled(Path.GetDirectoryName(Path.GetFullPath(outPak)));
                log.AppendLine("installed: " + (inst == null ? "-" : inst.Path + " legacy=" + inst.Legacy + " settings=" + (inst.Diff != null)));
                File.WriteAllText(outPak + ".log", log.ToString());
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(outPak + ".log", log + "ERROR: " + ex);
                return 1;
            }
        }
    }
}
