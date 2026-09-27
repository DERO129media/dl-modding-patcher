// Updates ueber GitHub-Releases: neueste Version abfragen, .exe laden, Pruefsumme kontrollieren,
// laufende Datei umbenennen (erlaubt Windows) und die neue an ihre Stelle legen.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Dero
{
    static class Updater
    {
        public const string Repo = "DERO129media/dl-modding-patcher";
        public const string AssetName = "DERO-Patcher.exe";

        public class Release
        {
            public string Version, Notes, Url, ExeUrl, ShaUrl;
        }

        public static string ExePath
        {
            get { return Assembly.GetEntryAssembly().Location; }
        }

        static WebClient Client()
        {
            // .NET Framework spricht TLS 1.2 nicht immer von selbst
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var c = new WebClient { Encoding = Encoding.UTF8 };
            c.Headers[HttpRequestHeader.UserAgent] = "DERO-Patcher/" + App.Version;
            return c;
        }

        public static Release Latest()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
            req.UserAgent = "DERO-Patcher/" + App.Version;
            req.Accept = "application/vnd.github+json";
            req.Timeout = req.ReadWriteTimeout = 6000;
            string text;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                text = r.ReadToEnd();
            var o = Json.ParseObject(text);
            var rel = new Release
            {
                Version = Convert.ToString(o["tag_name"]).TrimStart('v', 'V'),
                Notes = o.ContainsKey("body") ? Convert.ToString(o["body"]) : "",
                Url = o.ContainsKey("html_url") ? Convert.ToString(o["html_url"]) : "https://github.com/" + Repo + "/releases",
            };
            foreach (object a in Json.Arr(o["assets"]) ?? new object[0])
            {
                var asset = Json.Obj(a);
                string name = Convert.ToString(asset["name"]), url = Convert.ToString(asset["browser_download_url"]);
                if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase)) rel.ExeUrl = url;
                if (string.Equals(name, AssetName + ".sha256", StringComparison.OrdinalIgnoreCase)) rel.ShaUrl = url;
            }
            return rel;
        }

        public static bool IsNewer(string remote, string local)
        {
            Version r, l;
            return Version.TryParse(remote, out r) && Version.TryParse(local, out l) && r > l;
        }

        public static void Install(Release rel)
        {
            if (rel.ExeUrl == null || rel.ShaUrl == null) throw new InvalidOperationException("Das Release enthält keine passende .exe mit Prüfsumme.");
            byte[] exe, sha;
            using (WebClient c = Client())
            {
                exe = c.DownloadData(rel.ExeUrl);
                sha = c.DownloadData(rel.ShaUrl);
            }
            string expected = Encoding.ASCII.GetString(sha).Trim().Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
            string actual;
            using (var h = SHA256.Create()) actual = BitConverter.ToString(h.ComputeHash(exe)).Replace("-", "").ToLowerInvariant();
            if (expected != actual) throw new InvalidDataException("Die Prüfsumme der heruntergeladenen Datei stimmt nicht. Update abgebrochen.");

            string path = ExePath, fresh = path + ".new", old = path + ".old";
            File.WriteAllBytes(fresh, exe);
            if (File.Exists(old)) File.Delete(old);
            File.Move(path, old);        // laufende .exe darf umbenannt werden
            try { File.Move(fresh, path); }
            catch { File.Move(old, path); throw; }
        }

        public static void CleanupOld()
        {
            try
            {
                string old = ExePath + ".old";
                if (File.Exists(old)) File.Delete(old);
            }
            catch { }
        }
    }
}
