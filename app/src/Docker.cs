// REA de Rig's - la boite fermee : tout passe par docker run, toujours avec les memes protections.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReaRigs
{
    public static class Box
    {
        // protections imposees a CHAQUE lancement (jamais d'option pour les retirer)
        static readonly string[] Guard =
        {
            "--network", "none",                     // aucun acces reseau
            "--read-only",                           // systeme de la boite en lecture seule
            "--tmpfs", "/tmp:rw,size=4g",            // brouillon effacé a la fin
            "--cap-drop", "ALL",                     // aucun privilege Linux
            "--security-opt", "no-new-privileges",
            "--pids-limit", "512",
            "--memory", "8g",
            "-e", "HOME=/tmp",
        };

        public static string Q(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in a)
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); bs = 0; continue; }
                sb.Append('\\', bs); bs = 0; sb.Append(c);
            }
            sb.Append('\\', bs * 2); sb.Append('"');
            return sb.ToString();
        }

        public static string Join(IEnumerable<string> args) { return string.Join(" ", args.Select(Q)); }

        // arguments complets d'un lancement : entree en lecture seule, sortie ecrivable (facultative)
        public static List<string> RunArgs(string image, string inDir, string outDir, bool interactive, IEnumerable<string> rea)
        {
            var a = new List<string> { "run", "--rm" };
            if (interactive) a.Add("-i");
            a.AddRange(Guard);
            if (inDir != null) { a.Add("-v"); a.Add(Path.GetFullPath(inDir).TrimEnd('\\') + ":/in:ro"); }
            if (outDir != null) { a.Add("-v"); a.Add(Path.GetFullPath(outDir).TrimEnd('\\') + ":/out"); }
            a.Add(image);
            a.AddRange(rea);
            return a;
        }

        public class Result { public int Code; public string Err = ""; public long Bytes; }

        // lance docker ; stdout va directement dans un fichier (les resultats peuvent faire des centaines de Mo)
        public static Task<Result> Run(IEnumerable<string> args, string stdoutFile, CancellationToken ct, Action<string> line)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo("docker", Join(args))
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                var res = new Result();
                var err = new StringBuilder();
                using (var p = Process.Start(psi))
                {
                    p.ErrorDataReceived += (s, e) => { if (e.Data == null) return; lock (err) { if (err.Length < 20000) err.AppendLine(e.Data); } if (line != null) line(e.Data); };
                    p.BeginErrorReadLine();
                    using (ct.Register(() => { try { p.Kill(); } catch { } }))
                    {
                        if (stdoutFile != null)
                            using (var fs = new FileStream(stdoutFile, FileMode.Create, FileAccess.Write)) { p.StandardOutput.BaseStream.CopyTo(fs); res.Bytes = fs.Length; }
                        else p.StandardOutput.ReadToEnd();
                        p.WaitForExit();
                    }
                    res.Code = p.ExitCode;
                }
                res.Err = err.ToString();
                return res;
            });
        }

        // docker present et demarre ? (texte vide = ok)
        public static string Check()
        {
            try
            {
                var psi = new ProcessStartInfo("docker", "version --format {{.Server.Version}}") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } return "Docker ne répond pas."; }
                    return p.ExitCode == 0 && o.Trim().Length > 0 ? "" : "Docker Desktop n'est pas démarré.";
                }
            }
            catch { return "Docker Desktop n'est pas installé."; }
        }

        public static Task<Result> Pull(string imageRef, Action<string> line)
        {
            return Run(new[] { "pull", imageRef }, null, CancellationToken.None, line);
        }
    }
}
