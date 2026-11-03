// REA de Rig's - reglages, mises a jour (latest.json du depot) et branchement a l'appli Claude.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ReaRigs
{
    using Dict = Dictionary<string, object>;

    public static class Cfg
    {
        public const string Latest = "https://raw.githubusercontent.com/rigmateus-dev/rea-rigs/main/latest.json";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 1000 };

        public static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "REA de Rigs"); } }
        public static string File_ { get { return Path.Combine(DataDir, "reglages.json"); } }
        public static string Analyses { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Analyse"); } }
        public static string Staging { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "REA de Rigs", "entree"); } }

        public static Dict Data = new Dict();

        public static void Load()
        {
            try { if (File.Exists(File_)) Data = Js.Deserialize<Dict>(File.ReadAllText(File_, Encoding.UTF8)) ?? new Dict(); } catch { Data = new Dict(); }
        }
        public static void Save()
        {
            Directory.CreateDirectory(DataDir);
            string tmp = File_ + ".tmp";
            File.WriteAllText(tmp, Js.Serialize(Data), new UTF8Encoding(false));
            if (File.Exists(File_)) File.Replace(tmp, File_, null); else File.Move(tmp, File_);
        }
        public static string Get(string k) { object v; return Data.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; }

        // image installee : toujours par empreinte (sha256), jamais par nom seul
        public static string ImageRef
        {
            get
            {
                string img = Get("image"), dg = Get("digest");
                return img.Length > 0 && Regex.IsMatch(dg, "^sha256:[0-9a-f]{64}$") ? img + "@" + dg : "";
            }
        }

        public class Release { public string Version, Image, Digest, Date; }

        public static Release FetchLatest()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var wc = new WebClient { Encoding = Encoding.UTF8 })
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "REA-de-Rigs";
                var d = Js.Deserialize<Dict>(wc.DownloadString(Latest + "?t=" + DateTime.UtcNow.Ticks));
                Func<string, string> s = k => { object v; return d.TryGetValue(k, out v) ? Convert.ToString(v) : ""; };
                var r = new Release { Version = s("version"), Image = s("image"), Digest = s("digest"), Date = s("date") };
                // garde-fous : seulement notre registre, seulement une empreinte valide
                if (!Regex.IsMatch(r.Image, "^ghcr\\.io/rigmateus-dev/rea-rigs$")) throw new Exception("latest.json : image inattendue (" + r.Image + ")");
                if (!Regex.IsMatch(r.Digest, "^sha256:[0-9a-f]{64}$")) throw new Exception("latest.json : empreinte invalide");
                return r;
            }
        }

        // ------------------------------------------------------------- appli Claude (claude_desktop_config.json)
        public static string ClaudeConfig { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json"); } }

        public static List<string> McpArgs()
        {
            Directory.CreateDirectory(Analyses);
            // Claude voit seulement Documents\Analyse, en lecture seule, sans reseau
            return Box.RunArgs(ImageRef, Analyses, null, true, new[] { "mcp" });
        }

        public static bool ClaudeLinked()
        {
            try { return File.Exists(ClaudeConfig) && File.ReadAllText(ClaudeConfig).Contains("\"rea-de-rigs\""); } catch { return false; }
        }

        // ajoute (ou met a jour) le serveur « rea-de-rigs » sans toucher au reste ; copie de sauvegarde avant
        public static string LinkClaude()
        {
            if (ImageRef.Length == 0) throw new Exception("Installe d'abord REA (bouton Installer).");
            string f = ClaudeConfig;
            Directory.CreateDirectory(Path.GetDirectoryName(f));
            Dict root = new Dict();
            if (File.Exists(f))
            {
                string txt = File.ReadAllText(f, Encoding.UTF8);
                if (txt.Trim().Length > 0) root = Js.Deserialize<Dict>(txt) ?? new Dict();
                File.Copy(f, f + ".avant-rea-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            }
            object o; Dict servers = root.TryGetValue("mcpServers", out o) ? o as Dict : null;
            if (servers == null) { servers = new Dict(); root["mcpServers"] = servers; }
            servers["rea-de-rigs"] = new Dict { { "command", "docker" }, { "args", McpArgs().Cast<object>().ToArray() } };
            string tmp = f + ".tmp";
            File.WriteAllText(tmp, Pretty(Js.Serialize(root)), new UTF8Encoding(false));
            if (File.Exists(f)) File.Replace(tmp, f, null); else File.Move(tmp, f);
            return f;
        }

        public static void UnlinkClaude()
        {
            string f = ClaudeConfig; if (!File.Exists(f)) return;
            var root = Js.Deserialize<Dict>(File.ReadAllText(f, Encoding.UTF8)) ?? new Dict();
            object o; var servers = root.TryGetValue("mcpServers", out o) ? o as Dict : null;
            if (servers == null || !servers.Remove("rea-de-rigs")) return;
            File.Copy(f, f + ".avant-rea-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            File.WriteAllText(f, Pretty(Js.Serialize(root)), new UTF8Encoding(false));
        }

        // JSON lisible (le serialiseur .NET ecrit tout sur une ligne)
        public static string Pretty(string json)
        {
            var sb = new StringBuilder(); int ind = 0; bool str = false, esc = false;
            foreach (char c in json)
            {
                if (str) { sb.Append(c); if (esc) esc = false; else if (c == '\\') esc = true; else if (c == '"') str = false; continue; }
                switch (c)
                {
                    case '"': str = true; sb.Append(c); break;
                    case '{': case '[': sb.Append(c).Append('\n').Append(' ', (++ind) * 2); break;
                    case '}': case ']': sb.Append('\n').Append(' ', (--ind) * 2).Append(c); break;
                    case ',': sb.Append(c).Append('\n').Append(' ', ind * 2); break;
                    case ':': sb.Append(": "); break;
                    default: if (!char.IsWhiteSpace(c)) sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
