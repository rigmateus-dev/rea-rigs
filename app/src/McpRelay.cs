// REA de Rig's - relais « outil de Claude » (stdin/stdout JSON-RPC, une ligne = un message).
// Economie : l'appli Claude lance ce relais a chaque demarrage ; on NE demarre PAS Docker pour ca.
// On repond a « initialize » et « tools/list » depuis un cache (rempli lors du premier vrai lancement),
// et la boite n'est demarree qu'au premier vrai appel d'outil. Apres 10 min sans appel : la boite s'arrete ;
// apres 30 min : Docker aussi (si plus aucune boite ne tourne). Le prochain appel redemarre tout.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ReaRigs
{
    using Dict = Dictionary<string, object>;

    public static class McpRelay
    {
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 1000 };
        static readonly object OutLock = new object();
        static Stream Out;
        static Process Box_;
        static string InitLine;                 // le « initialize » de Claude, rejoue a chaque demarrage de la boite
        static string BootId;
        static readonly ManualResetEventSlim Booted = new ManualResetEventSlim(false);
        static DateTime Last = DateTime.UtcNow;
        static Dict Cache;                      // { initialize: result, tools: result }
        static readonly Dictionary<string, string> Capture = new Dictionary<string, string>();   // id -> "initialize" / "tools"

        static string CacheFile { get { string dg = Cfg.Get("digest"); return Path.Combine(Cfg.DataDir, "mcp-cache-" + (dg.Length > 15 ? dg.Substring(7, 12) : "x") + ".json"); } }
        static string IdKey(object id) { return id == null ? "" : Js.Serialize(id); }

        static void Send(string line)
        {
            byte[] b = new UTF8Encoding(false).GetBytes(line + "\n");
            lock (OutLock) { Out.Write(b, 0, b.Length); Out.Flush(); }
        }
        static void Reply(object id, object result) { Send(Js.Serialize(new Dict { { "jsonrpc", "2.0" }, { "id", id }, { "result", result } })); }
        static void Error(object id, string msg) { Send(Js.Serialize(new Dict { { "jsonrpc", "2.0" }, { "id", id }, { "error", new Dict { { "code", -32603 }, { "message", msg } } } })); }

        public static int Run()
        {
            Out = Console.OpenStandardOutput();
            try { if (File.Exists(CacheFile)) Cache = Js.Deserialize<Dict>(File.ReadAllText(CacheFile, Encoding.UTF8)); } catch { Cache = null; }
            var lines = new BlockingCollection<string>();
            var reader = new Thread(() =>
            {
                try { using (var sr = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false))) { string l; while ((l = sr.ReadLine()) != null) if (l.Trim().Length > 0) lines.Add(l); } }
                catch { }
                lines.CompleteAdding();
            }) { IsBackground = true };
            reader.Start();
            var idle = new Thread(IdleLoop) { IsBackground = true }; idle.Start();
            foreach (string line in lines.GetConsumingEnumerable())
            {
                Last = DateTime.UtcNow;
                Dict msg; try { msg = Js.Deserialize<Dict>(line); } catch { continue; }
                object idObj; msg.TryGetValue("id", out idObj);
                string method = msg.ContainsKey("method") ? Convert.ToString(msg["method"]) : "";
                if (method == "initialize") InitLine = line;
                if (Running()) { Forward(line, method, idObj); continue; }
                // boite arretee : on repond nous-memes tant que c'est possible
                if (method == "initialize" && Cache != null && Cache.ContainsKey("initialize")) { Reply(idObj, Cache["initialize"]); continue; }
                if (method == "tools/list" && Cache != null && Cache.ContainsKey("tools")) { Reply(idObj, Cache["tools"]); continue; }
                if (method.StartsWith("notifications/")) continue;
                if (method == "ping") { Reply(idObj, new Dict()); continue; }
                if (method == "resources/list") { Reply(idObj, new Dict { { "resources", new object[0] } }); continue; }
                if (method == "prompts/list") { Reply(idObj, new Dict { { "prompts", new object[0] } }); continue; }
                string err = Boot(method != "initialize");     // si c'est l'initialisation elle-meme : pas de rejeu
                if (err != null) { if (idObj != null) Error(idObj, "REA : " + err); continue; }
                Forward(line, method, idObj);
            }
            Stop();
            return 0;
        }

        static bool Running() { try { return Box_ != null && !Box_.HasExited; } catch { return false; } }

        static void Forward(string line, string method, object id)
        {
            if (id != null && (method == "initialize" || method == "tools/list")) lock (Capture) Capture[IdKey(id)] = method == "initialize" ? "initialize" : "tools";
            try { var w = Box_.StandardInput.BaseStream; byte[] b = new UTF8Encoding(false).GetBytes(line + "\n"); w.Write(b, 0, b.Length); w.Flush(); }
            catch { if (id != null) Error(id, "REA : la boîte s'est arrêtée, réessaie."); }
        }

        // demarre Docker (si besoin) puis la boite, et rejoue l'initialisation de Claude
        static string Boot(bool replay)
        {
            if (Box.Check() != "") { string e = Box.StartDesktop(); if (e != "") return e; }
            if (Cfg.ImageRef.Length == 0) return "REA n'est pas installé (ouvre l'appli REA de Rig's).";
            var psi = new ProcessStartInfo("docker", Box.Join(Cfg.McpArgs()))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
            };
            Box_ = Process.Start(psi);
            var p = Box_;
            new Thread(() => { try { p.StandardError.ReadToEnd(); } catch { } }) { IsBackground = true }.Start();
            Booted.Reset();
            new Thread(() => Pump(p)) { IsBackground = true }.Start();
            if (replay && InitLine != null)
            {
                // on rejoue « initialize » avec un id a nous : sa reponse n'est pas renvoyee a Claude
                var init = Js.Deserialize<Dict>(InitLine);
                BootId = "rea-boot-" + Guid.NewGuid().ToString("N");
                init["id"] = BootId;
                Forward(Js.Serialize(init), "", null);
                if (!Booted.Wait(120000)) return "la boîte ne répond pas.";
                Forward("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", "", null);
            }
            return null;
        }

        static void Pump(Process p)
        {
            try
            {
                string l;
                while ((l = p.StandardOutput.ReadLine()) != null)
                {
                    Last = DateTime.UtcNow;
                    Dict msg = null; try { msg = Js.Deserialize<Dict>(l); } catch { }
                    object id = null; if (msg != null) msg.TryGetValue("id", out id);
                    if (id != null && BootId != null && Convert.ToString(id) == BootId)
                    {
                        object r; if (msg.TryGetValue("result", out r)) SaveCache("initialize", r);
                        Booted.Set(); continue;
                    }
                    if (id != null && msg.ContainsKey("result"))
                    {
                        string kind = null; lock (Capture) { string k = IdKey(id); if (Capture.TryGetValue(k, out kind)) Capture.Remove(k); }
                        if (kind != null) SaveCache(kind, msg["result"]);
                    }
                    Send(l);
                }
            }
            catch { }
        }

        static void SaveCache(string key, object result)
        {
            try
            {
                if (Cache == null) Cache = new Dict();
                Cache[key] = result;
                Directory.CreateDirectory(Cfg.DataDir);
                File.WriteAllText(CacheFile, Js.Serialize(Cache), new UTF8Encoding(false));
            }
            catch { }
        }

        static void IdleLoop()
        {
            bool dockerStopped = false;
            while (true)
            {
                Thread.Sleep(30000);
                double idle = (DateTime.UtcNow - Last).TotalMinutes;
                if (Running() && idle > 10) Stop();
                if (!Running() && idle > 30 && !dockerStopped)
                {
                    if (NoOtherBoxes()) { Box.StopAll(); dockerStopped = true; }
                }
                if (idle < 1) dockerStopped = false;
            }
        }

        static bool NoOtherBoxes()
        {
            try
            {
                var psi = new ProcessStartInfo("docker", "ps -q") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi)) { string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(15000); return p.ExitCode == 0 && o.Trim().Length == 0; }
            }
            catch { return false; }
        }

        static void Stop()
        {
            var p = Box_; Box_ = null;
            if (p == null) return;
            try { p.StandardInput.Close(); } catch { }
            try { if (!p.WaitForExit(5000)) p.Kill(); } catch { }
        }
    }
}
