// Lokaler HTTP-Server fuer die Oberflaeche (nur 127.0.0.1). Liefert die HTML-Seite und eine kleine API.
// Schutz: zufaelliges Token pro Start (Header X-Dero-Token) und Host-Pruefung gegen DNS-Rebinding.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Dero
{
    // Gemeinsamer Zustand der laufenden Anwendung
    static class App
    {
        // Einzige Quelle der Versionsnummer: AssemblyVersion in Program.cs (muss zum Git-Tag vX.Y.Z passen)
        public static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        public static readonly object Lock = new object();
        public static readonly ManualResetEvent Exit = new ManualResetEvent(false);
        public static GameFiles Game;
        public static GameModel Model;
        public static string GameDir, LoadError;
        // Zaehlt jedes Laden der Spieldaten. Die Oberflaeche schickt ihren Stand mit; weicht er ab, muss sie neu laden.
        public static int DataVersion;
        public static Action RestartForUpdate;

        public static string DataDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DERO-Patcher");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        static string SettingsFile { get { return Path.Combine(DataDir, "settings.json"); } }

        public static string BackupDir { get { return Path.Combine(DataDir, "Sicherungen"); } }

        // Groesse + Zeitstempel der Quell-Paks beim Laden. Aendert Steam sie (Spiel-Update), wird neu geladen.
        static string loadedStamp;

        static string SourceStamp(string gameDir)
        {
            string src = GameFiles.ResolveSourceDir(gameDir);
            if (src == null) return null;
            var sb = new StringBuilder();
            foreach (string rel in new[] { "data0.pak", "data1.pak", @"data_lang\datade.pak", @"data_lang\dataen.pak" })
            {
                var fi = new FileInfo(Path.Combine(src, rel));
                sb.Append(rel).Append('=').Append(fi.Exists ? fi.Length + "@" + fi.LastWriteTimeUtc.Ticks : "-").Append(';');
            }
            return sb.ToString();
        }

        // Gescheitertes Laden (z. B. Datei kurz gesperrt) wird hoechstens alle RetrySeconds wiederholt
        static DateTime failedAt = DateTime.MinValue;
        const int RetrySeconds = 15;

        // Liest die Spieldaten neu, wenn sich die Quell-Paks seit dem Laden geaendert haben oder das Laden gescheitert war.
        // true = neu geladen
        public static bool Refresh()
        {
            lock (Lock)
            {
                if (GameDir == null) return false;
                string now;
                try { now = SourceStamp(GameDir); } catch { return false; }
                if (now == loadedStamp && (Model != null || (DateTime.Now - failedAt).TotalSeconds < RetrySeconds)) return false;
                LoadGame(GameDir, false);
                return true;
            }
        }

        // false = die Quell-Paks haben sich seit dem Laden geaendert (Spiel-Update laeuft oder lief)
        public static bool SourceUnchanged()
        {
            lock (Lock)
            {
                try { return GameDir != null && loadedStamp != null && SourceStamp(GameDir) == loadedStamp; }
                catch { return false; }
            }
        }

        public static void Init(string gameDirOverride)
        {
            Patcher.BackupDir = BackupDir;
            string dir = gameDirOverride;
            if (dir == null)
                try { dir = Convert.ToString(Json.ParseObject(File.ReadAllText(SettingsFile))["gameDir"]); } catch { }
            if (dir == null || GameFiles.ResolveSourceDir(dir) == null) dir = GameFiles.AutoDetect() ?? dir;
            LoadGame(dir, gameDirOverride == null);
        }

        public static void LoadGame(string dir, bool remember)
        {
            lock (Lock)
            {
                GameDir = dir;
                DataVersion++;
                Game = null;
                Model = null;
                LoadError = null;
                loadedStamp = null;
                if (dir == null) { LoadError = "Spielordner nicht gefunden."; return; }
                try
                {
                    // Stempel vor und nach dem Lesen: aendert Steam die Dateien waehrenddessen, ist der Stand nicht konsistent
                    loadedStamp = SourceStamp(dir);
                    Game = GameFiles.Load(dir);
                    Model = GameModel.Build(Game);
                    string after = SourceStamp(dir);
                    if (after != loadedStamp)
                    {
                        loadedStamp = after;   // erst nach der naechsten Aenderung oder nach RetrySeconds erneut versuchen
                        throw new IOException("Die Spieldateien haben sich während des Ladens geändert. Läuft gerade ein Steam-Update? Das Tool versucht es gleich noch einmal.");
                    }
                    if (remember) File.WriteAllText(SettingsFile, Json.Write(Json.O("gameDir", dir)));
                }
                catch (Exception ex)
                {
                    Game = null;
                    Model = null;
                    failedAt = DateTime.Now;
                    LoadError = ex is DirectoryNotFoundException ? ex.Message
                        : "Die Spieldaten konnten nicht gelesen werden (evtl. hat ein Spiel-Update die Dateien verändert): " + ex.Message;
                }
            }
        }
    }

    class Server
    {
        public int Port;
        public readonly string Token;
        public DateTime LastPing = DateTime.Now, ByeAt = DateTime.MinValue;
        public bool IgnoreRunning;   // nur Entwicklung (--dev --ignore-running): Tests gegen eine Spielkopie, waehrend das Spiel laeuft
        readonly string uiDir;
        TcpListener listener;
        Updater.Release lastRelease;
        DateTime lastCheck = DateTime.MinValue;
        string lastCheckError;

        public Server(int port, string token, string uiDir)
        {
            Port = port;
            Token = token;
            this.uiDir = uiDir;
        }

        public void Start()
        {
            listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var t = new Thread(delegate()
            {
                while (true)
                {
                    TcpClient c;
                    try { c = listener.AcceptTcpClient(); }
                    catch { return; }
                    ThreadPool.QueueUserWorkItem(Handle, c);
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        class Req
        {
            public string Method, Path, Body = "";
            public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        void Handle(object o)
        {
            var client = (TcpClient)o;
            try
            {
                using (client)
                using (NetworkStream ns = client.GetStream())
                {
                    ns.ReadTimeout = 15000;
                    Req r = Read(ns);
                    if (r == null) return;
                    int status;
                    string type;
                    byte[] body = Route(r, out status, out type);
                    string head = "HTTP/1.1 " + status + " " + (status == 200 ? "OK" : "Error") + "\r\nContent-Type: " + type
                        + "\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n\r\n";
                    byte[] hb = Encoding.ASCII.GetBytes(head);
                    ns.Write(hb, 0, hb.Length);
                    ns.Write(body, 0, body.Length);
                }
            }
            catch { }
        }

        static Req Read(NetworkStream ns)
        {
            var head = new MemoryStream();
            int b;
            while ((b = ns.ReadByte()) >= 0)
            {
                head.WriteByte((byte)b);
                long n = head.Length;
                byte[] a = head.GetBuffer();
                if (n >= 4 && a[n - 4] == '\r' && a[n - 3] == '\n' && a[n - 2] == '\r' && a[n - 1] == '\n') break;
                if (n > 65536) return null;
            }
            string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length < 2) return null;
            var r = new Req { Method = first[0], Path = first[1] };
            foreach (string l in lines.Skip(1))
            {
                int i = l.IndexOf(':');
                if (i > 0) r.Headers[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
            }
            string cl;
            int len;
            if (r.Headers.TryGetValue("Content-Length", out cl) && int.TryParse(cl, out len) && len > 0)
            {
                if (len > 16 * 1024 * 1024) return null;
                var buf = new byte[len];
                int read = 0;
                while (read < len)
                {
                    int n = ns.Read(buf, read, len - read);
                    if (n <= 0) break;
                    read += n;
                }
                r.Body = Encoding.UTF8.GetString(buf, 0, read);
            }
            return r;
        }

        static byte[] JsonBytes(object o) { return Encoding.UTF8.GetBytes(Json.Write(o)); }

        byte[] Route(Req r, out int status, out string type)
        {
            status = 200;
            type = "application/json; charset=utf-8";
            string host;
            r.Headers.TryGetValue("Host", out host);
            if (host != "127.0.0.1:" + Port && host != "localhost:" + Port) { status = 403; return JsonBytes(Json.O("error", "Host nicht erlaubt")); }

            string path = r.Path.Split('?')[0];
            if (!path.StartsWith("/api/"))
            {
                byte[] file = Static(path, out type);
                if (file == null) { status = 404; type = "text/plain"; return Encoding.UTF8.GetBytes("Nicht gefunden"); }
                return file;
            }
            string tok;
            if (!r.Headers.TryGetValue("X-Dero-Token", out tok) || tok != Token) { status = 403; return JsonBytes(Json.O("error", "Kein Zugriff")); }

            try
            {
                string key = r.Method + " " + path;
                switch (key)
                {
                    case "GET /api/state": return JsonBytes(State());
                    case "POST /api/preview": return JsonBytes(Preview(Json.ParseObject(r.Body)));
                    case "POST /api/patch": return JsonBytes(Patch(Json.ParseObject(r.Body), ref status));
                    case "POST /api/remove": return JsonBytes(Remove(ref status));
                    case "POST /api/browse": return JsonBytes(Browse());
                    case "POST /api/ping": LastPing = DateTime.Now; App.Refresh(); return JsonBytes(Json.O("ok", true, "dataVersion", App.DataVersion));
                    case "POST /api/bye": ByeAt = DateTime.Now; return JsonBytes(Json.O("ok", true));
                    case "GET /api/update": return JsonBytes(UpdateCheck(r.Path.Contains("force=1")));
                    case "POST /api/update": return JsonBytes(UpdateInstall(ref status));
                }
                status = 404;
                return JsonBytes(Json.O("error", "Unbekannte Anfrage"));
            }
            catch (Exception ex)
            {
                status = 500;
                return JsonBytes(Json.O("error", ex.Message));
            }
        }

        byte[] Static(string path, out string type)
        {
            if (path == "/") path = "/index.html";
            string ext = Path.GetExtension(path).ToLowerInvariant();
            type = ext == ".html" ? "text/html; charset=utf-8" : ext == ".js" ? "text/javascript; charset=utf-8" : ext == ".css" ? "text/css" : ext == ".png" ? "image/png" : "application/octet-stream";
            if (uiDir != null)
            {
                string full = Path.GetFullPath(Path.Combine(uiDir, path.TrimStart('/')));
                if (!full.StartsWith(Path.GetFullPath(uiDir), StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
                return File.ReadAllBytes(full);
            }
            if (path != "/index.html") return null;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("index.html"))
            using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
        }

        // ---------- API ----------

        bool Running() { return !IgnoreRunning && Patcher.GameRunning(); }

        static bool IsEmpty(Dictionary<string, object> diff)
        {
            return diff.Values.All(v => { var d = Json.Obj(v); return d == null || d.Count == 0; });
        }

        static Dictionary<string, object> DiffOf(Dictionary<string, object> body)
        {
            var d = Json.Obj(body.ContainsKey("diff") ? body["diff"] : null);
            if (d == null) throw new InvalidDataException("Einstellungen fehlen");
            // leere Bereiche weglassen, damit dieselben Werte immer denselben Text ergeben
            return d.Where(kv => { var x = Json.Obj(kv.Value); return x != null && x.Count > 0; }).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        // Die Oberflaeche arbeitet mit dem Datenstand, den sie beim Laden bekommen hat. Hat Steam das Spiel inzwischen
        // aktualisiert, darf weder Vorschau noch Patch mit alten Originalwerten weiterlaufen.
        const string StaleMsg = "Das Spiel wurde gerade aktualisiert. Die Oberfläche lädt die neuen Spieldaten, deine Einstellungen bleiben erhalten.";

        static bool Stale(Dictionary<string, object> body)
        {
            App.Refresh();
            object v;
            return body.TryGetValue("dataVersion", out v) && Convert.ToInt32(v) != App.DataVersion;
        }

        Dictionary<string, object> State()
        {
            App.Refresh();
            lock (App.Lock)
            {
                var res = Json.O("version", App.Version, "dev", uiDir != null, "running", Running(), "dataVersion", App.DataVersion,
                    "game", Json.O("dir", App.GameDir, "found", App.Model != null, "error", App.LoadError));
                if (App.Model == null) return res;
                res["data"] = App.Model.Data;
                Patcher.Installed inst = Patcher.FindInstalled(App.Game.SourceDir);
                if (inst != null && inst.Foreign != null)
                    res["installed"] = Json.O("pak", Path.GetFileName(inst.Path), "foreign", inst.Foreign);
                else if (inst != null)
                {
                    bool outdated = inst.Legacy || inst.Diff == null;
                    if (!outdated)
                        try { outdated = Patcher.ModCode(Patcher.BuildPak(Patcher.Apply(App.Model, App.Game, inst.Diff), inst.Diff)) != inst.Code; }
                        catch { outdated = true; }
                    res["installed"] = Json.O("pak", Path.GetFileName(inst.Path), "legacy", inst.Legacy, "diff", inst.Diff, "code", inst.Code, "outdated", outdated);
                }
                res["conflicts"] = Patcher.FindConflicts(App.Game.SourceDir, inst != null ? inst.Path : null);
                return res;
            }
        }

        Dictionary<string, object> Preview(Dictionary<string, object> body)
        {
            lock (App.Lock)
            {
                if (Stale(body)) return Json.O("error", StaleMsg, "stale", true);
                if (App.Model == null) return Json.O("error", "Spiel nicht geladen");
                var diff = DiffOf(body);
                if (IsEmpty(diff)) return Json.O("code", null, "files", new string[0]);
                try
                {
                    PatchCtx ctx = Patcher.Apply(App.Model, App.Game, diff);
                    return Json.O("code", Patcher.ModCode(Patcher.BuildPak(ctx, diff)), "files", ctx.Changed.Keys.Select(Path.GetFileName).ToList());
                }
                catch (InvalidDataException ex) { return Json.O("error", ex.Message); }
            }
        }

        Dictionary<string, object> Patch(Dictionary<string, object> body, ref int status)
        {
            lock (App.Lock)
            {
                if (Stale(body)) { status = 409; return Json.O("error", StaleMsg, "stale", true); }
                if (App.Model == null) { status = 409; return Json.O("error", "Spiel nicht geladen"); }
                if (Running()) { status = 409; return Json.O("error", "Das Spiel läuft noch. Bitte schließ es zuerst."); }
                var diff = DiffOf(body);
                if (IsEmpty(diff)) { status = 409; return Json.O("error", "Alles steht auf Original – es gibt nichts zu patchen."); }
                PatchCtx ctx;
                try { ctx = Patcher.Apply(App.Model, App.Game, diff); }
                catch (InvalidDataException ex) { status = 409; return Json.O("error", ex.Message); }
                byte[] pak = Patcher.BuildPak(ctx, diff);
                // Hat Steam waehrend des Erzeugens aktualisiert, stammt die Pak aus der alten Fassung: nicht installieren
                if (!App.SourceUnchanged()) { App.Refresh(); status = 409; return Json.O("error", StaleMsg, "stale", true); }
                Patcher.Installed inst = Patcher.FindInstalled(App.Game.SourceDir);
                if (inst != null && inst.Foreign != null) { status = 409; return Json.O("error", ForeignMsg(inst)); }
                string target = inst != null ? inst.Path : Patcher.FreeSlot(App.Game.SourceDir);
                if (target == null) { status = 409; return Json.O("error", "Alle Mod-Plätze (data2.pak bis data7.pak) sind schon belegt."); }
                try { Patcher.WriteFile(target, pak); }
                catch (UnauthorizedAccessException) { status = 409; return Json.O("error", AdminMsg); }
                catch (InvalidDataException ex) { status = 409; return Json.O("error", ex.Message); }
                catch (IOException ex) { status = 409; return Json.O("error", "Die Mod-Datei konnte nicht geschrieben werden: " + ex.Message + " Die bisherige Datei ist unverändert."); }
                return Json.O("ok", true, "pak", Path.GetFileName(target), "code", Patcher.ModCode(pak),
                    "files", ctx.Changed.Keys.Select(Path.GetFileName).ToList());
            }
        }

        Dictionary<string, object> Remove(ref int status)
        {
            lock (App.Lock)
            {
                if (App.Game == null) { status = 409; return Json.O("error", "Spiel nicht geladen"); }
                if (Running()) { status = 409; return Json.O("error", "Das Spiel läuft noch. Bitte schließ es zuerst."); }
                Patcher.Installed inst = Patcher.FindInstalled(App.Game.SourceDir);
                if (inst == null) return Json.O("ok", true);
                if (inst.Foreign != null) { status = 409; return Json.O("error", ForeignMsg(inst)); }
                try { Patcher.RemoveFile(inst.Path); }
                catch (UnauthorizedAccessException) { status = 409; return Json.O("error", AdminMsg); }
                catch (Exception ex) { status = 409; return Json.O("error", "Entfernen fehlgeschlagen: " + ex.Message); }
                return Json.O("ok", true, "pak", Path.GetFileName(inst.Path));
            }
        }

        const string AdminMsg = "Windows lässt das Tool nicht in den Spielordner schreiben. Schließ das Tool, klick mit rechts auf die .exe und wähle „Als Administrator ausführen“.";

        static string ForeignMsg(Patcher.Installed inst)
        {
            return Path.GetFileName(inst.Path) + " " + inst.Foreign + ". Das Tool fasst diese Datei deshalb nicht an. "
                + "Stammt sie von dir, verschieb sie aus dem Spielordner (ph_ft\\source) und patche danach neu.";
        }

        Dictionary<string, object> Browse()
        {
            string dir = Program.BrowseFolder(App.GameDir);
            if (dir == null) return Json.O("ok", false);
            App.LoadGame(dir, true);
            return Json.O("ok", true, "found", App.Model != null, "error", App.LoadError);
        }

        Dictionary<string, object> UpdateCheck(bool force)
        {
            if (force || (DateTime.Now - lastCheck).TotalMinutes > 10)
            {
                lastCheck = DateTime.Now;
                try { lastRelease = Updater.Latest(); lastCheckError = null; }
                catch (Exception ex) { lastRelease = null; lastCheckError = ex.Message; }
            }
            var res = Json.O("current", App.Version, "error", lastCheckError);
            if (lastRelease != null)
            {
                res["latest"] = lastRelease.Version;
                res["available"] = Updater.IsNewer(lastRelease.Version, App.Version) && lastRelease.ExeUrl != null;
                res["notes"] = lastRelease.Notes;
                res["url"] = lastRelease.Url;
            }
            return res;
        }

        Dictionary<string, object> UpdateInstall(ref int status)
        {
            if (lastRelease == null || !Updater.IsNewer(lastRelease.Version, App.Version)) { status = 409; return Json.O("error", "Kein Update verfügbar."); }
            try { Updater.Install(lastRelease); }
            catch (Exception ex)
            {
                status = 409;
                return Json.O("error", "Update fehlgeschlagen: " + ex.Message, "url", lastRelease.Url);
            }
            // Antwort erst senden, dann neu starten
            ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(600); if (App.RestartForUpdate != null) App.RestartForUpdate(); });
            return Json.O("ok", true, "version", lastRelease.Version);
        }
    }
}
