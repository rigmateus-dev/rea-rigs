// REA de Rig's - fenetre principale : glisser un fichier, lancer l'analyse dans la boite fermee, lire le resultat.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ReaRigs
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--debrancher") { try { Cfg.UnlinkClaude(); } catch { } return 0; }
            bool created;
            using (var m = new Mutex(true, "REA-de-Rigs-une-seule-fenetre", out created))
            {
                if (!created) return 0;
                Cfg.Load();
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += (s, e) => { MessageBox.Show(e.Exception.Message, "REA de Rig's", MessageBoxButton.OK, MessageBoxImage.Warning); e.Handled = true; };
                var w = new Main();
                if (args.Length > 0 && (File.Exists(args[0]) || Directory.Exists(args[0]))) w.Loaded += (s, e) => w.Pick(args[0]);
                return app.Run(w);
            }
        }
    }

    public class Main : Window
    {
        static Brush B(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex); b.Freeze(); return b; }
        static readonly Brush Bg = B("#0F1117"), Panel_ = B("#171A23"), Line = B("#2A2F3D"), Text = B("#E8EAF0"), Muted = B("#9AA1B2"), Accent = B("#7C9CFF"), Ok = B("#4ADE80"), Warn = B("#F5B83D"), Bad = B("#F87171");

        readonly TextBlock dockerState = new TextBlock(), versionState = new TextBlock(), status = new TextBlock(), hint = new TextBlock(), resTitle = new TextBlock(), resInfo = new TextBlock();
        readonly Button installBtn, claudeBtn, cancelBtn, openBtn, copyBtn;
        readonly ComboBox mode = new ComboBox(), files = new ComboBox();
        readonly ListBox history = new ListBox();
        readonly TreeView tree = new TreeView();
        readonly Border drop;
        CancellationTokenSource cts;
        string current;

        Button Btn(string text, Action a, bool primary = false)
        {
            var b = new Button { Content = text, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0), Foreground = primary ? Bg : Text, Background = primary ? Accent : Panel_, BorderBrush = Line, Cursor = Cursors.Hand };
            AutomationProperties.SetName(b, text);
            b.Click += (s, e) => { try { a(); } catch (Exception ex) { Say(ex.Message, Bad); } };
            return b;
        }
        static TextBlock T(string s, double size, Brush c, FontWeight? w = null) { return new TextBlock { Text = s, FontSize = size, Foreground = c, FontWeight = w ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap }; }

        public Main()
        {
            Title = "REA de Rig's"; Width = 1180; Height = 760; MinWidth = 820; MinHeight = 520;
            Background = Bg; Foreground = Text; FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location)) Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions()); } catch { }

            var root = new DockPanel { Margin = new Thickness(18) };
            // ---- barre du haut
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            var titles = new StackPanel();
            titles.Children.Add(T("REA de Rig's", 22, Text, FontWeights.SemiBold));
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            dockerState.Margin = new Thickness(0, 0, 16, 0); chips.Children.Add(dockerState); chips.Children.Add(versionState);
            titles.Children.Add(chips);
            installBtn = Btn("Installer REA", Update);
            claudeBtn = Btn("Brancher à Claude", ToggleClaude);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(installBtn); right.Children.Add(claudeBtn);
            DockPanel.SetDock(right, Dock.Right); top.Children.Add(right); top.Children.Add(titles);
            DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);

            // ---- historique
            var left = new DockPanel { Width = 270, Margin = new Thickness(0, 0, 16, 0) };
            var lh = T("ANALYSES", 11, Muted, FontWeights.SemiBold); lh.Margin = new Thickness(2, 0, 0, 8); DockPanel.SetDock(lh, Dock.Top); left.Children.Add(lh);
            history.Background = Panel_; history.Foreground = Text; history.BorderBrush = Line;
            AutomationProperties.SetName(history, "Analyses précédentes");
            history.SelectionChanged += (s, e) => { var it = history.SelectedItem as ListBoxItem; if (it != null) Show((string)it.Tag); };
            left.Children.Add(history);
            DockPanel.SetDock(left, Dock.Left); root.Children.Add(left);

            // ---- zone principale
            var main = new DockPanel();
            drop = new Border { BorderBrush = Accent, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(14), Background = Panel_, Padding = new Thickness(18), AllowDrop = true, Margin = new Thickness(0, 0, 0, 12) };
            var dp = new StackPanel();
            dp.Children.Add(T("Glisse ici un fichier ou un dossier à analyser", 16, Text, FontWeights.SemiBold));
            var sub = T("REA travaille dans une boîte fermée : sans réseau, sans accès au reste du PC, ton fichier n'est jamais modifié.", 12, Muted); sub.Margin = new Thickness(0, 4, 0, 12); dp.Children.Add(sub);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Btn("Choisir un fichier…", () => { var d = new Microsoft.Win32.OpenFileDialog { Title = "Fichier à analyser" }; if (d.ShowDialog(this) == true) Pick(d.FileName); }, true));
            row.Children.Add(Btn("Choisir un dossier…", () => { using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Dossier d'application à analyser" }) if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) Pick(d.SelectedPath); }));
            row.Children.Add(T("Type :", 13, Muted)); ((TextBlock)row.Children[row.Children.Count - 1]).VerticalAlignment = VerticalAlignment.Center; ((TextBlock)row.Children[row.Children.Count - 1]).Margin = new Thickness(8, 0, 8, 0);
            foreach (var m in Mode.All) mode.Items.Add(m);
            mode.SelectedIndex = 0; mode.MinWidth = 220; AutomationProperties.SetName(mode, "Type d'analyse");
            mode.SelectionChanged += (s, e) => hint.Text = ((Mode)mode.SelectedItem).Hint;
            row.Children.Add(mode);
            dp.Children.Add(row);
            hint.Foreground = Muted; hint.FontSize = 12; hint.Margin = new Thickness(0, 8, 0, 0); hint.Text = Mode.All[0].Hint; dp.Children.Add(hint);
            var srow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            cancelBtn = Btn("Annuler", () => { if (cts != null) cts.Cancel(); }); cancelBtn.Visibility = Visibility.Collapsed; DockPanel.SetDock(cancelBtn, Dock.Right); srow.Children.Add(cancelBtn);
            status.TextWrapping = TextWrapping.Wrap; status.VerticalAlignment = VerticalAlignment.Center; srow.Children.Add(status);
            dp.Children.Add(srow);
            drop.Child = dp;
            drop.DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            drop.Drop += (s, e) => { var f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null && f.Length > 0) Pick(f[0]); };
            DockPanel.SetDock(drop, Dock.Top); main.Children.Add(drop);

            // ---- resultat
            var res = new DockPanel();
            var rh = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            openBtn = Btn("Ouvrir le dossier", () => { if (current != null) Process.Start("explorer.exe", "\"" + current + "\""); });
            copyBtn = Btn("Copier pour Claude", () => { if (current != null) { Clipboard.SetText("Analyse REA à lire : " + current); Say("Chemin copié : colle-le dans Claude.", Ok); } });
            var rb = new StackPanel { Orientation = Orientation.Horizontal }; rb.Children.Add(openBtn); rb.Children.Add(copyBtn); DockPanel.SetDock(rb, Dock.Right); rh.Children.Add(rb);
            var rt = new StackPanel(); resTitle.FontSize = 15; resTitle.FontWeight = FontWeights.SemiBold; resTitle.Foreground = Text; rt.Children.Add(resTitle); resInfo.Foreground = Muted; resInfo.FontSize = 12; rt.Children.Add(resInfo); rh.Children.Add(rt);
            DockPanel.SetDock(rh, Dock.Top); res.Children.Add(rh);
            files.Margin = new Thickness(0, 0, 0, 8); AutomationProperties.SetName(files, "Fichier de résultat");
            files.SelectionChanged += (s, e) => { var f = files.SelectedItem as string; if (f != null && current != null) Fill(Path.Combine(current, f)); };
            DockPanel.SetDock(files, Dock.Top); res.Children.Add(files);
            tree.Background = Panel_; tree.Foreground = Text; tree.BorderBrush = Line; tree.FontFamily = new FontFamily("Cascadia Mono, Consolas"); tree.FontSize = 12.5;
            AutomationProperties.SetName(tree, "Résultat de l'analyse");
            res.Children.Add(tree);
            main.Children.Add(res);
            root.Children.Add(main);
            Content = root;

            ShowResult(false);
            Loaded += async (s, e) => { Refresh(); await CheckAll(); };
        }

        void Say(string text, Brush c) { status.Text = text; status.Foreground = c; }
        void ShowResult(bool on) { foreach (UIElement u in new UIElement[] { openBtn, copyBtn, files, tree }) u.Visibility = on ? Visibility.Visible : Visibility.Collapsed; resTitle.Text = on ? resTitle.Text : "Aucune analyse ouverte"; resInfo.Text = on ? resInfo.Text : "Glisse un fichier au-dessus, ou choisis une analyse à gauche."; }

        async Task CheckAll()
        {
            string d = await Task.Run(() => Box.Check());
            dockerState.Text = d.Length == 0 ? "● Boîte fermée prête (Docker)" : "● " + d;
            dockerState.Foreground = d.Length == 0 ? Ok : Bad;
            string v = Cfg.Get("version");
            versionState.Text = Cfg.ImageRef.Length > 0 ? "REA " + v + (Cfg.ClaudeLinked() ? " · branché à Claude" : "") : "REA pas encore installé";
            versionState.Foreground = Cfg.ImageRef.Length > 0 ? Muted : Warn;
            installBtn.Content = Cfg.ImageRef.Length > 0 ? "Vérifier les mises à jour" : "Installer REA";
            claudeBtn.Content = Cfg.ClaudeLinked() ? "Débrancher de Claude" : "Brancher à Claude";
            if (d.Length > 0) Say("Démarre Docker Desktop, puis relance l'appli.", Warn);
        }

        async void Update()
        {
            installBtn.IsEnabled = false;
            try
            {
                Say("Recherche de la dernière version validée…", Muted);
                var r = await Task.Run(() => Cfg.FetchLatest());
                string cur = Cfg.Get("digest");
                if (r.Digest == cur) { Say("REA est à jour (" + r.Version + ").", Ok); return; }
                if (cur.Length > 0 && MessageBox.Show(this, "Nouvelle version validée : REA " + r.Version + " (" + r.Date + ").\nL'installer ?", "REA de Rig's", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                Say("Téléchargement de la boîte REA " + r.Version + "…", Muted);
                var p = await Box.Pull(r.Image + "@" + r.Digest, null);
                if (p.Code != 0) throw new Exception("Téléchargement impossible : " + p.Err.Trim());
                Cfg.Data["image"] = r.Image; Cfg.Data["digest"] = r.Digest; Cfg.Data["version"] = r.Version; Cfg.Save();
                if (Cfg.ClaudeLinked()) Cfg.LinkClaude();          // Claude suit la nouvelle empreinte
                Say("REA " + r.Version + " installé.", Ok);
            }
            catch (Exception ex) { Say(ex.Message, Bad); }
            finally { installBtn.IsEnabled = true; await CheckAll(); }
        }

        async void ToggleClaude()
        {
            if (Cfg.ClaudeLinked())
            {
                Cfg.UnlinkClaude(); Say("Débranché. Redémarre l'appli Claude.", Ok);
            }
            else
            {
                if (MessageBox.Show(this, "REA sera ajouté comme outil dans l'appli Claude.\n\n• Claude verra seulement Documents\\Analyse, en lecture seule\n• sans réseau, dans la boîte fermée\n• une copie de ta configuration Claude est gardée\n\nContinuer ?", "Brancher à Claude", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                string f = Cfg.LinkClaude();
                Say("Branché (" + f + "). Redémarre l'appli Claude.", Ok);
            }
            await CheckAll();
        }

        public async void Pick(string path)
        {
            if (cts != null) { Say("Une analyse est déjà en cours.", Warn); return; }
            Job job;
            try { job = Job.Make(path, ((Mode)mode.SelectedItem).Id); }
            catch (Exception ex) { Say(ex.Message, Bad); return; }
            cts = new CancellationTokenSource();
            cancelBtn.Visibility = Visibility.Visible; drop.Opacity = 0.85;
            var sw = Stopwatch.StartNew();
            try
            {
                string dir = await job.Run(cts.Token, t => Dispatcher.BeginInvoke(new Action(() => Say(job.Name + " — " + t, Muted))));
                Say("Analyse terminée en " + (int)sw.Elapsed.TotalSeconds + " s.", Ok);
                Refresh(); Show(dir);
            }
            catch (OperationCanceledException) { Say("Analyse annulée.", Warn); }
            catch (Exception ex) { Say(ex.Message + (job.Dir != null ? "  (détails : journal.txt)" : ""), Bad); Refresh(); }
            finally { cts = null; cancelBtn.Visibility = Visibility.Collapsed; drop.Opacity = 1; }
        }

        void Refresh()
        {
            history.Items.Clear();
            if (!Directory.Exists(Cfg.Analyses)) return;
            foreach (var d in new DirectoryInfo(Cfg.Analyses).GetDirectories().OrderByDescending(x => x.Name).Take(200))
            {
                bool okRun = d.GetFiles("*.json").Any(f => f.Length > 2);
                var sp = new StackPanel { Margin = new Thickness(4, 4, 4, 4) };
                string nm = d.Name.Length > 16 ? d.Name.Substring(16) : d.Name;
                sp.Children.Add(T(nm, 13, Text, FontWeights.SemiBold));
                sp.Children.Add(T(d.Name.Length >= 15 ? d.Name.Substring(0, 15).Replace('_', ' ') + (okRun ? "" : " · échec") : "", 11, okRun ? Muted : Bad));
                var it = new ListBoxItem { Content = sp, Tag = d.FullName };
                AutomationProperties.SetName(it, nm);
                history.Items.Add(it);
            }
        }

        void Show(string dir)
        {
            current = dir;
            resTitle.Text = Path.GetFileName(dir);
            string src = File.Exists(Path.Combine(dir, "source.txt")) ? File.ReadAllText(Path.Combine(dir, "source.txt")).Split('\n')[0].Trim() : "";
            resInfo.Text = src;
            files.Items.Clear();
            foreach (var f in new DirectoryInfo(dir).GetFiles("*.json").OrderBy(f => f.Name.Contains("complet") ? 1 : 0).ThenBy(f => f.Name)) if (f.Name != "vue-resume.json") files.Items.Add(f.Name);
            ShowResult(true);
            if (files.Items.Count > 0) files.SelectedIndex = 0;
            else { tree.Items.Clear(); string j = Path.Combine(dir, "journal.txt"); tree.Items.Add(new TreeViewItem { Header = File.Exists(j) ? File.ReadAllText(j) : "Pas de résultat.", Foreground = Bad }); }
        }

        // ------------------------------------------------------------- arbre JSON (rempli a la demande)
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 1000 };

        void Fill(string file)
        {
            tree.Items.Clear();
            var fi = new FileInfo(file);
            if (fi.Length > 40L * 1024 * 1024)
            {
                tree.Items.Add(new TreeViewItem { Header = "Fichier trop gros pour l'afficher ici (" + (fi.Length / 1048576) + " Mo). Ouvre plutôt le résumé, ou demande à Claude de le lire.", Foreground = Warn });
                return;
            }
            object o;
            try { o = Js.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)); }
            catch (Exception ex) { tree.Items.Add(new TreeViewItem { Header = "JSON illisible : " + ex.Message, Foreground = Bad }); return; }
            foreach (var n in Children(o)) tree.Items.Add(n);
            foreach (TreeViewItem n in tree.Items) if (n.Items.Count > 0 && tree.Items.Count < 12) n.IsExpanded = true;
        }

        IEnumerable<TreeViewItem> Children(object o)
        {
            const int Max = 400;
            var d = o as IDictionary<string, object>;
            if (d != null) { int i = 0; foreach (var kv in d) { if (i++ == Max) { yield return More(d.Count - Max); yield break; } yield return Node(kv.Key, kv.Value); } yield break; }
            var l = o as IList;
            if (l != null) { for (int i = 0; i < l.Count; i++) { if (i == Max) { yield return More(l.Count - Max); yield break; } yield return Node("[" + i + "]", l[i]); } yield break; }
        }

        TreeViewItem More(int n) { return new TreeViewItem { Header = "… et " + n + " de plus (voir le fichier)", Foreground = Muted }; }

        TreeViewItem Node(string key, object v)
        {
            var it = new TreeViewItem { Foreground = Text };
            var tb = new TextBlock();
            tb.Inlines.Add(new System.Windows.Documents.Run(key) { Foreground = Accent });
            if (v is IDictionary<string, object>) tb.Inlines.Add(new System.Windows.Documents.Run("  { " + ((IDictionary<string, object>)v).Count + " }") { Foreground = Muted });
            else if (v is IList) tb.Inlines.Add(new System.Windows.Documents.Run("  [ " + ((IList)v).Count + " ]") { Foreground = Muted });
            else
            {
                string s = v == null ? "null" : v is string ? "\"" + v + "\"" : v is bool ? ((bool)v ? "true" : "false") : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
                if (s.Length > 300) s = s.Substring(0, 300) + "…";
                tb.Inlines.Add(new System.Windows.Documents.Run(" : " + s) { Foreground = v is string ? Ok : Text });
            }
            it.Header = tb;
            if (v is IDictionary<string, object> || v is IList)
            {
                it.Items.Add(new TreeViewItem { Header = "…" });       // place pour la fleche
                bool done = false;
                it.Expanded += (s, e) => { if (done || e.OriginalSource != it) return; done = true; it.Items.Clear(); foreach (var c in Children(v)) it.Items.Add(c); };
            }
            return it;
        }
    }
}
