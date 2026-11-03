// REA de Rig's - une analyse : choisir la bonne commande REA, preparer l'entree, ranger le resultat.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReaRigs
{
    public class Mode
    {
        public string Id, Label, Hint;
        public override string ToString() { return Label; }
        public static readonly Mode[] All =
        {
            new Mode { Id = "auto", Label = "Automatique", Hint = "Choisit tout seul selon ce que tu glisses." },
            new Mode { Id = "managed", Label = "Programme .NET (C#)", Hint = "Identité, dépendances et liste des classes / méthodes d'un .exe ou .dll .NET." },
            new Mode { Id = "desktop", Label = "Application (dossier)", Hint = "Inventaire d'un dossier d'appli : moteur (Electron, .NET, Unity, Godot…), composants." },
            new Mode { Id = "js", Label = "Appli JavaScript / Electron", Hint = "Graphe des modules d'une appli Electron (dossier ou .asar)." },
            new Mode { Id = "artifact", Label = "Conteneur / installateur", Hint = "Ce qu'il y a à l'intérieur d'un fichier (paquet, archive, exe auto-extractible…)." },
        };
    }

    public class Step { public string Name, Out; public string[] Args; }

    public class Job
    {
        public string Source, Name, Dir, Kind;
        public List<Step> Steps = new List<Step>();
        public bool IsDir;

        static bool IsManaged(string file)
        {
            // PE avec un repertoire CLI non vide = .NET (on lit seulement l'en-tete, rien n'est execute)
            try
            {
                using (var fs = File.OpenRead(file))
                using (var br = new BinaryReader(fs))
                {
                    if (fs.Length < 0x200 || br.ReadUInt16() != 0x5A4D) return false;
                    fs.Position = 0x3C; int pe = br.ReadInt32(); if (pe <= 0 || pe > fs.Length - 0x100) return false;
                    fs.Position = pe; if (br.ReadUInt32() != 0x00004550) return false;
                    fs.Position = pe + 24; ushort magic = br.ReadUInt16();
                    int dd = pe + 24 + (magic == 0x20B ? 112 : 96);
                    fs.Position = dd + 14 * 8; uint rva = br.ReadUInt32(), size = br.ReadUInt32();
                    return rva != 0 && size != 0;
                }
            }
            catch { return false; }
        }

        public static Job Make(string source, string modeId)
        {
            source = Path.GetFullPath(source).TrimEnd('\\');
            bool dir = Directory.Exists(source);
            if (!dir && !File.Exists(source)) throw new Exception("Introuvable : " + source);
            var j = new Job { Source = source, IsDir = dir, Name = Path.GetFileName(source) };
            string ext = dir ? "" : Path.GetExtension(source).ToLowerInvariant();
            string kind = modeId;
            if (kind == "auto")
                kind = dir ? (Directory.GetFiles(source, "*.asar", SearchOption.AllDirectories).Any() ? "desktop" : "desktop")
                     : ext == ".asar" ? "js"
                     : (ext == ".exe" || ext == ".dll") && IsManaged(source) ? "managed"
                     : "artifact";
            j.Kind = kind;
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
            j.Dir = Path.Combine(Cfg.Analyses, stamp + "_" + Safe(j.Name));
            string t = dir ? "/in" : "/in/" + j.Name;
            switch (kind)
            {
                case "managed":
                    j.Steps.Add(new Step { Name = "Identité du programme", Out = "identite.json", Args = new[] { "inspect-managed-artifact", t, "--json" } });
                    j.Steps.Add(new Step { Name = "Classes et méthodes (résumé)", Out = "membres-resume.json", Args = new[] { "inspect-managed-members", t, "--detail", "summary", "--evidence-bundle", "/out/membres-complet.json", "--json" } });
                    break;
                case "desktop":
                    j.Steps.Add(new Step { Name = "Inventaire de l'application", Out = "application-resume.json", Args = new[] { "inspect-desktop-application", t, "--detail", "summary", "--evidence_output", "/out/application-complet.json", "--json" } });
                    break;
                case "js":
                    j.Steps.Add(new Step { Name = "Modules JavaScript", Out = "javascript-complet.json", Args = new[] { "analyze", t, "--json" } });
                    j.Steps.Add(new Step { Name = "Résumé", Out = "javascript-resume.json", Args = new[] { "inspect-analysis-view", "/out/vue-resume.json", "--json" } });
                    break;
                default:
                    j.Steps.Add(new Step { Name = "Contenu du fichier", Out = "contenu.json", Args = new[] { "inspect-artifact", t, "--json" } });
                    break;
            }
            return j;
        }

        static string Safe(string n)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n.Length > 60 ? n.Substring(0, 60) : n;
        }

        // une copie du fichier seul : la boite ne voit jamais ses voisins
        string StageInput()
        {
            if (IsDir) return Source;
            string d = Path.Combine(Cfg.Staging, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            File.Copy(Source, Path.Combine(d, Name));
            return d;
        }

        public async Task<string> Run(CancellationToken ct, Action<string> status)
        {
            string img = Cfg.ImageRef;
            if (img.Length == 0) throw new Exception("REA n'est pas installé : clique « Installer REA ».");
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, "source.txt"), Source + "\r\nmode : " + Kind + "\r\nimage : " + img + "\r\n", Encoding.UTF8);
            string input = StageInput();
            var log = new StringBuilder();
            try
            {
                foreach (var s in Steps)
                {
                    ct.ThrowIfCancellationRequested();
                    if (s.Out == "javascript-resume.json")
                        File.WriteAllText(Path.Combine(Dir, "vue-resume.json"), "{\"source\":{\"kind\":\"evidence-file\",\"path\":\"/out/javascript-complet.json\"},\"view\":{\"kind\":\"summary\"}}", new UTF8Encoding(false));
                    status(s.Name + "…");
                    var args = Box.RunArgs(img, input, Dir, false, s.Args);
                    var r = await Box.Run(args, Path.Combine(Dir, s.Out), ct, null);
                    log.AppendLine("$ rea " + string.Join(" ", s.Args)).AppendLine("code " + r.Code + ", " + r.Bytes + " octets").AppendLine(r.Err);
                    if (r.Code != 0)
                    {
                        File.WriteAllText(Path.Combine(Dir, "journal.txt"), log.ToString(), Encoding.UTF8);
                        string first = r.Err.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? ("code " + r.Code);
                        throw new Exception(s.Name + " : " + first);
                    }
                }
                File.WriteAllText(Path.Combine(Dir, "journal.txt"), log.ToString(), Encoding.UTF8);
                return Dir;
            }
            finally
            {
                if (!IsDir) try { Directory.Delete(input, true); } catch { }
            }
        }
    }
}
