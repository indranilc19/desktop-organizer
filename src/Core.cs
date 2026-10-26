// Desktop Organizer - a full-screen home / launcher for a Windows PC.
// Built on the PC by build.ps1 (Windows PowerShell Add-Type, C# 5, .NET Framework 4.x).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopOrganizer
{
    // ------------------------------------------------------------------ model
    public class Item : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Sub { get; set; }
        public string Path { get; set; }
        public string Cat { get; set; }
        public string Color { get; set; }
        public string Tint { get; set; }
        public string Ext { get; set; }
        public string Kind { get; set; }      // app, folder, image, pdf, book, manga, video, doc, text, web, claude
        public string Mode { get; set; }      // calibre, apps, pinned, photos ...
        public bool IsDir { get; set; }
        public bool Offline { get; set; }
        public string Where { get; set; }
        public string Tags { get; set; }        // extra searchable words (director, cast, language ...)
        public string Person { get; set; }      // director for films and shows
        public string Thumbnail { get; set; }   // poster / cover image path
        internal string LTags = "";
        internal string BaseSub;
        public long Modified { get; set; }
        public int Score { get; set; }
        internal string LName;
        internal string LSub;

        ImageSource icon, thumb;
        public ImageSource Icon { get { return icon; } set { icon = value; Raise("Icon"); Raise("Visual"); } }
        public ImageSource Thumb { get { return thumb; } set { thumb = value; Raise("Thumb"); Raise("Visual"); Raise("HasThumb"); } }
        public ImageSource Visual { get { return thumb ?? icon; } }
        public Visibility HasThumb { get { return thumb != null ? Visibility.Visible : Visibility.Collapsed; } }
        public string Glyph
        {
            get
            {
                switch (Kind)
                {
                    case "claude": return "\uE945";
                    case "web": return "\uE774";
                    default: return "";
                }
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        void Raise(string n) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(n)); }
    }

    public class Answer
    {
        public string Verdict, Text, Detail = "", Topic, Query;
        public Item Best;
        public List<Item> Hits = new List<Item>();
        public List<string[]> Links = new List<string[]>();
    }

    public class Source
    {
        public string Root;
        public string Cat;
        public int Depth;
        public int DirLevels;
        public bool Files = true;
        public string[] Exts;
        public string[] Exclude;
        public string Mode = "";
    }

    // ------------------------------------------------------------------ engine
    public static class Engine
    {
        public static volatile List<Item> Items = new List<Item>();
        public static volatile bool Fresh;
        public static volatile bool Busy;
        public static volatile bool Pending;
        public static DateTime BuiltAt;
        static readonly object histLock = new object();
        static Dictionary<string, long[]> hist = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase);
        static string histFile;

        static readonly string[] SkipDirs = { "node_modules", ".git", "__pycache__", "$recycle.bin", "system volume information", ".venv", "venv", ".next", ".cache", ".idea", ".vs", "_to recycle" };
        static readonly string[] Palette = {
            "Apps", "#6EA8FE", "Books", "#F4B860", "Reference", "#E78FB3", "Travel", "#5ED3A7",
            "Magazines", "#FF8A65", "Manga", "#C792EA", "Comics", "#4DD0E1", "Animation", "#81C784", "Work", "#90A4AE", "Projects", "#4FC3F7",
            "Docs", "#AED581", "Movies", "#FF7A7A", "Folders", "#FFD54F", "Desktop", "#80CBC4",
            "Downloads", "#B0BEC5", "Photos", "#F48FB1", "TV", "#FFB74D", "Anime", "#BA68C8", "Web", "#9FA8DA", "Claude", "#D97757" };
        static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic" };
        static readonly string[] VideoExt = { ".mp4", ".mkv", ".avi", ".mov", ".m4v", ".wmv", ".webm" };
        static readonly string[] BookExt = { ".epub", ".mobi", ".azw3", ".azw", ".kfx", ".fb2", ".djvu" };
        static readonly string[] TextExt = { ".txt", ".md", ".csv", ".json", ".log", ".ini", ".xml", ".ps1", ".py", ".js", ".ts", ".tsx", ".css", ".yml", ".yaml", ".kml" };

        public static string ColorOf(string cat)
        {
            for (int i = 0; i < Palette.Length; i += 2) if (Palette[i] == cat) return Palette[i + 1];
            return "#B0BEC5";
        }

        public static string KindOf(string ext, bool isDir, string cat, string mode)
        {
            if (isDir) return cat == "Manga" || cat == "Comics" ? "manga" : "folder";
            if (ext == ".lnk" || ext == ".exe" || ext == ".url" || mode == "apps" || mode == "pinned") return "app";
            if (Array.IndexOf(ImageExt, ext) >= 0) return "image";
            if (ext == ".pdf") return cat == "Books" || cat == "Reference" || cat == "Travel" || cat == "Magazines" ? "book" : "pdf";
            if (ext == ".cbz" || ext == ".cbr") return "manga";
            if (Array.IndexOf(BookExt, ext) >= 0) return "book";
            if (Array.IndexOf(VideoExt, ext) >= 0) return "video";
            if (Array.IndexOf(TextExt, ext) >= 0) return "text";
            return "doc";
        }

        public static List<Item> Build(Source[] sources)
        {
            var list = new List<Item>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var appNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sources)
            {
                try { if (Directory.Exists(s.Root)) Walk(s, s.Root, 0, list, seen, appNames); }
                catch { }
            }
            return list;
        }

        static bool Excluded(Source s, string name)
        {
            if (s.Exclude == null) return false;
            string l = name.ToLowerInvariant();
            foreach (var x in s.Exclude) if (x.Length > 0 && l.Contains(x.ToLowerInvariant())) return true;
            return false;
        }

        static void Walk(Source s, string dir, int level, List<Item> list, HashSet<string> seen, HashSet<string> appNames)
        {
            if (s.Files && level <= s.Depth)
            {
                string[] files;
                try { files = Directory.GetFiles(dir); } catch { files = new string[0]; }
                foreach (var f in files)
                {
                    string fn = System.IO.Path.GetFileName(f);
                    if (fn.StartsWith(".") || fn.StartsWith("~$") || fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                    if (s.Exts != null && s.Exts.Length > 0 && Array.IndexOf(s.Exts, ext) < 0) continue;
                    if (Excluded(s, fn)) continue;
                    if (!seen.Add(f)) continue;
                    var it = Make(s, f, false, ext);
                    if (it.Kind == "app" && !appNames.Add(it.Name)) continue;
                    list.Add(it);
                }
            }
            if (level >= s.Depth && level >= s.DirLevels) return;
            string[] dirs;
            try { dirs = Directory.GetDirectories(dir); } catch { return; }
            foreach (var d in dirs)
            {
                string dn = System.IO.Path.GetFileName(d);
                if (dn.StartsWith(".") || Array.IndexOf(SkipDirs, dn.ToLowerInvariant()) >= 0) continue;
                try
                {
                    var a = File.GetAttributes(d);
                    if ((a & FileAttributes.Hidden) != 0 || (a & FileAttributes.System) != 0) continue;
                }
                catch { continue; }
                if (Excluded(s, dn)) continue;
                if (level < s.DirLevels && seen.Add(d)) list.Add(Make(s, d, true, ""));
                if (level < s.Depth || level + 1 < s.DirLevels) Walk(s, d, level + 1, list, seen, appNames);
            }
        }

        public static string Pretty(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            string home = Environment.GetEnvironmentVariable("USERPROFILE") ?? "";
            if (home.Length > 0 && dir.StartsWith(home, StringComparison.OrdinalIgnoreCase))
                dir = "~" + dir.Substring(home.Length);
            dir = dir.Replace("~\\OneDrive\\", "~\\");
            return dir.TrimEnd('\\').Replace("\\", "  \u203A  ");
        }

        static Item Make(Source s, string path, bool isDir, string ext)
        {
            var it = new Item();
            it.Path = path; it.IsDir = isDir; it.Ext = ext; it.Cat = s.Cat; it.Mode = s.Mode;
            try { it.Modified = (isDir ? Directory.GetLastWriteTime(path) : File.GetLastWriteTime(path)).Ticks; } catch { }
            string parent = System.IO.Path.GetDirectoryName(path) ?? "";
            if (isDir)
            {
                it.Name = System.IO.Path.GetFileName(path);
                it.Sub = (s.Cat == "Photos" ? "Album" : s.Cat == "Manga" || s.Cat == "Comics" ? "Series" : "Folder") + "  \u00B7  " + Pretty(parent);
                if (s.Cat == "Manga" || s.Cat == "Comics") { it.Tags = it.Name.ToLowerInvariant(); it.Name = SeriesTitle(it.Name); }
            }
            else
            {
                it.Name = System.IO.Path.GetFileNameWithoutExtension(path);
                string e = ext.Length > 1 ? ext.Substring(1).ToUpperInvariant() : "";
                if (s.Mode == "apps" || s.Mode == "pinned" || ext == ".lnk" || ext == ".url" || ext == ".exe")
                {
                    if (s.Mode == "apps" || s.Mode == "pinned" || s.Cat == "Desktop") it.Cat = "Apps";
                    if (s.Mode == "pinned") it.Name = Regex.Replace(it.Name, @"^\d+\s+", "");
                    it.Sub = ext == ".url" ? "Web shortcut" : "Application";
                }
                else if (s.Mode == "calibre")
                {
                    string author = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(parent) ?? "");
                    string title = System.IO.Path.GetFileName(parent);
                    int p = title.LastIndexOf(" (");
                    if (p > 0 && title.EndsWith(")")) title = title.Substring(0, p);
                    it.Name = title;
                    it.Sub = author + "  \u00B7  " + e;
                }
                else it.Sub = e + "  \u00B7  " + Pretty(parent);
                if ((s.Cat == "Manga" || s.Cat == "Comics") && Regex.IsMatch(ext, @"^\.(cbz|cbr|pdf|epub)$")) { it.Tags = it.Name.ToLowerInvariant(); it.Name = SeriesTitle(it.Name); }
            }
            Finish(it);
            return it;
        }

        // How a comic / manga folder or file is shown: the published title, not the scanner's file name.
        // "Batman-Year One 01-04 (1986)" -> "Batman: Year One (1986)"; "Batman-Superman-Wonder Woman-Trinity 01-03 (2003)" ->
        // "Batman/Superman/Wonder Woman: Trinity (2003)"; "Batman #1-10" -> "Batman #1\u201310"; "Berserk - Vol 01 (2003)" stays.
        static readonly string[] Hyphenated = { "spider-man", "spider-woman", "spider-gwen", "x-men", "x-force", "x-factor", "ant-man", "she-hulk",
            "man-thing", "ex-machina", "half-blood", "k-on", "re-zero", "sci-fi", "ultra-man", "a-force", "t-rex" };
        public static string SeriesTitle(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string s = raw.Replace('_', ' ');
            s = Regex.Replace(s, @"^\s*(\([^)]{1,20}\)|\[[^\]]{1,30}\])\s*", "");     // scanner / group tags: "(Frew) Phantom 0002"
            s = Regex.Replace(s, @"^\s*(DC|Marvel|Image|Dark Horse) Comics\s*(\(Graphic Novel\))?\s*", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\((DELUXE EDITION|Graphic Novel)\)", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\b(PDF|CBR|CBZ|Comic|ctc|TPB|plus some art|Stuff|unscannd reprints)\b", m => m.Value.Equals("TPB", StringComparison.OrdinalIgnoreCase) ? "TPB" : "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"-(Flattermann|Archive Editions|[A-Z][a-z]+ Editions)\b.*?(?=\(|$)", "");
            // issue ranges ("01-24", "0 1-09", "01-xx") and lone issue numbers glued to names ("000-")
            s = Regex.Replace(s, @"\s+0?\s?\d{1,3}\s*[-\u2013]\s*(\d{1,3}|xx)\b", "");
            s = Regex.Replace(s, @"(?<=\s)0{2,3}\d?(?=[-\s])", "");
            s = Regex.Replace(s, @"#(\d+)\s*[-\u2013]\s*(\d+)", "#$1\u2013$2");
            // name-name joins: keep real hyphenated names, earlier joins become "/", the last one becomes ": "
            foreach (var h in Hyphenated)
            {
                int i = s.IndexOf(h, StringComparison.OrdinalIgnoreCase);
                if (i >= 0) s = s.Substring(0, i) + s.Substring(i, h.Length).Replace('-', '\u2011') + s.Substring(i + h.Length);
            }
            s = Regex.Replace(s, @"^\d{1,3}\s*-\s+", "");                 // "01- Asterix the Gaul"
            s = Regex.Replace(s, @"\s+-(?=[A-Z])", ": ");                     // "Batwoman -Kate Kane" (after issue no. removal)
            var outside = new StringBuilder(); int depth = 0;                 // never touch "(2006-2007)"
            foreach (char ch in s) { if (ch == '(') depth++; if (ch == ')') depth = Math.Max(0, depth - 1); outside.Append(depth > 0 && ch == '-' ? '\u2011' : ch); }
            s = outside.ToString();
            var joins = Regex.Matches(s, @"(?<=[A-Za-z0-9\)])-(?=[A-Z0-9])");
            for (int j = joins.Count - 1; j >= 0; j--)
                s = s.Substring(0, joins[j].Index) + (j == joins.Count - 1 ? ": " : "/") + s.Substring(joins[j].Index + 1);
            s = s.Replace('\u2011', '-');
            s = Regex.Replace(s, @"\(\s*\)", "");
            s = Regex.Replace(s, @"\s*:\s*(?=\(|$)", " ");
            s = Regex.Replace(s, @"\s{2,}", " ").Trim(' ', '-', '+', ',', ':');
            s = Regex.Replace(s, @"\s+\+\s*$", "");
            return s.Length >= 2 ? s : raw;
        }

        public static void Refinish(Item it) { Finish(it); }

        static void Finish(Item it)
        {
            if (it.Kind == null) it.Kind = KindOf(it.Ext ?? "", it.IsDir, it.Cat, it.Mode);
            it.Color = ColorOf(it.Cat);
            it.Tint = "#2E" + it.Color.Substring(1);
            it.LName = (it.Name ?? "").ToLowerInvariant();
            it.LSub = ((it.Sub ?? "") + " " + it.Cat).ToLowerInvariant();
            it.LTags = (it.Tags ?? "").ToLowerInvariant();
        }

        public static void RebuildAsync(Source[] sources, string cacheFile)
        {
            if (Busy) { Pending = true; return; }
            Busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var list = Build(sources);
                    try { Catalog.Merge(list); MediaLib.Merge(list); } catch (Exception ex) { Log.Write("merge: " + ex.Message); }
                    Items = list;
                    BuiltAt = DateTime.Now;
                    SaveCache(cacheFile);
                    Fresh = true;
                }
                catch (Exception ex) { Log.Write("index: " + ex); }
                finally { Busy = false; if (Pending) { Pending = false; RebuildAsync(sources, cacheFile); } }
            });
        }

        public static void SaveCache(string file)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var it in Items)
                {
                    if (it.Mode == "catalog") continue;
                    sb.Append(it.Name).Append('\t').Append(it.BaseSub ?? it.Sub).Append('\t').Append(it.Path).Append('\t')
                      .Append(it.Cat).Append('\t').Append(it.IsDir ? "1" : "0").Append('\t').Append(it.Ext).Append('\t')
                      .Append(it.Mode).Append('\t').Append(it.Modified).Append('\n');
                }
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static int LoadCache(string file)
        {
            var list = new List<Item>();
            try
            {
                if (!File.Exists(file)) return 0;
                foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    var c = line.Split('\t');
                    if (c.Length < 8) continue;
                    var it = new Item();
                    it.Name = c[0]; it.Sub = c[1]; it.Path = c[2]; it.Cat = c[3]; it.IsDir = c[4] == "1"; it.Ext = c[5]; it.Mode = c[6];
                    long m; long.TryParse(c[7], out m); it.Modified = m;
                    Finish(it);
                    list.Add(it);
                }
                try { Catalog.Merge(list); MediaLib.Merge(list); } catch (Exception ex) { Log.Write("merge: " + ex.Message); }
                Items = list;
                BuiltAt = File.GetLastWriteTime(file);
            }
            catch { }
            return list.Count;
        }

        // ---- usage history
        public static void LoadHistory(string file)
        {
            histFile = file;
            try
            {
                if (!File.Exists(file)) return;
                foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    var c = line.Split('\t');
                    if (c.Length < 3) continue;
                    long n, t;
                    if (long.TryParse(c[1], out n) && long.TryParse(c[2], out t)) hist[c[0]] = new long[] { n, t };
                }
            }
            catch { }
        }

        public static void Record(string path)
        {
            lock (histLock)
            {
                long[] h;
                if (!hist.TryGetValue(path, out h)) { h = new long[2]; hist[path] = h; }
                h[0]++; h[1] = DateTime.Now.Ticks;
                try
                {
                    var sb = new StringBuilder();
                    foreach (var kv in hist) sb.Append(kv.Key).Append('\t').Append(kv.Value[0]).Append('\t').Append(kv.Value[1]).Append('\n');
                    if (histFile != null) File.WriteAllText(histFile, sb.ToString(), Encoding.UTF8);
                }
                catch { }
            }
        }

        static int HistBoost(Item it)
        {
            long[] h;
            if (!hist.TryGetValue(it.Path, out h)) return 0;
            double days = (DateTime.Now.Ticks - h[1]) / (double)TimeSpan.TicksPerDay;
            int recency = days < 1 ? 40 : days < 7 ? 25 : days < 30 ? 10 : 0;
            return (int)Math.Min(h[0] * 12, 90) + recency;
        }

        public static List<Item> Recent(int max)
        {
            var src = Items;
            var byPath = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in src) if (!byPath.ContainsKey(it.Path)) byPath[it.Path] = it;
            var pairs = new List<KeyValuePair<long, Item>>();
            lock (histLock)
            {
                foreach (var kv in hist)
                {
                    Item it;
                    if (byPath.TryGetValue(kv.Key, out it)) pairs.Add(new KeyValuePair<long, Item>(kv.Value[1], it));
                }
            }
            pairs.Sort(delegate(KeyValuePair<long, Item> a, KeyValuePair<long, Item> b) { return b.Key.CompareTo(a.Key); });
            var res = new List<Item>();
            foreach (var p in pairs) { res.Add(p.Value); if (res.Count >= max) break; }
            return res;
        }

        // ---- matching
        static int TokScore(string t, string n)
        {
            int i = n.IndexOf(t, StringComparison.Ordinal);
            if (i == 0) return 100;
            int best = 0;
            while (i > 0)
            {
                bool boundary = !char.IsLetterOrDigit(n[i - 1]);
                int s = boundary ? 80 - Math.Min(i, 40) / 4 : 45;
                if (s > best) best = s;
                if (boundary) break;
                i = n.IndexOf(t, i + 1, StringComparison.Ordinal);
            }
            if (best > 0) return best;
            if (t.Length < 2) return 0;
            int pos = -1, gaps = 0, start = -1;
            for (int k = 0; k < t.Length; k++)
            {
                int nx = n.IndexOf(t[k], pos + 1);
                if (nx < 0) return 0;
                if (k == 0) start = nx; else gaps += nx - pos - 1;
                pos = nx;
            }
            if (start > 0 && char.IsLetterOrDigit(n[start - 1])) return 0;
            int sc = 34 - gaps;
            return sc < 6 ? 0 : sc;
        }

        static int Match(string[] toks, string full, Item it)
        {
            int total = 0;
            foreach (var t in toks)
            {
                int s = TokScore(t, it.LName);
                if (s == 0 && (it.LSub.Contains(t) || it.LTags.Contains(t))) s = 14;
                if (s == 0) return 0;
                total += s;
            }
            if (it.LName == full) total += 80;
            else if (it.LName.StartsWith(full)) total += 40;
            else if (it.LName.EndsWith(" " + full)) total += 30;
            if (it.Path.IndexOf("\\Drafts\\", StringComparison.OrdinalIgnoreCase) >= 0) total -= 45;
            if (it.Path.IndexOf("\\_To Recycle\\", StringComparison.OrdinalIgnoreCase) >= 0) total -= 200;
            total -= Math.Min(it.LName.Length, 80) / 8;
            return total < 1 ? 1 : total;
        }

        static int CatBoost(Item it)
        {
            if (it.Cat == "Apps") return it.Mode == "pinned" ? 40 : 30;
            if (it.IsDir) return it.Cat == "Photos" || it.Cat == "Manga" ? 45 : 25;
            return 0;
        }

        public static bool InCat(Item it, string cat)
        {
            if (cat == "Trips") return (it.Cat == "Photos" && it.IsDir && Regex.IsMatch(it.Name ?? "", @"^\d{4}-\d{2}")) || (it.Mode == "homevideo" && (it.Sub ?? "").Contains(" trip"));
            if (cat == "TV Shows") return it.Cat == "TV";
            if (cat == "Movies") return it.Cat == "Movies" && (it.Mode == "title" || it.Mode == "catalog" || it.Kind == "video");
            return it.Cat == cat;
        }

        public static List<Item> Search(string query, string cat, int max)
        {
            var src = Items;
            var res = new List<Item>();
            string q = (query ?? "").Trim().ToLowerInvariant();
            string[] toks = q.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            bool all = string.IsNullOrEmpty(cat) || cat == "All";
            var seenPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in src)
            {
                if (!all && !InCat(it, cat)) continue;
                int sc;
                if (toks.Length == 0) { sc = HistBoost(it) + 1; }
                else
                {
                    sc = Match(toks, q, it);
                    if (sc <= 0) continue;
                    sc += HistBoost(it) + CatBoost(it);
                }
                if (!seenPath.Add(it.Path)) continue;
                it.Score = sc;
                res.Add(it);
            }
            if (toks.Length == 0)
                res.Sort(delegate(Item a, Item b) { int c = b.Score.CompareTo(a.Score); return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            else
                res.Sort(delegate(Item a, Item b) { int c = b.Score.CompareTo(a.Score); return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            if (res.Count > max) res.RemoveRange(max, res.Count - max);
            return res;
        }

        // ---- conversational answers ("do I have ... ?")
        static readonly string[] AskStop = {
            "do","does","did","i","we","you","have","has","had","got","get","own","is","are","was","there","any","anything","something",
            "a","an","the","this","that","these","those","my","our","me","in","on","at","of","from","for","to","with","and","or",
            "library","collection","computer","pc","laptop","drive","drives","folder","folders","disk","saved","somewhere","stored","copy","copies",
            "where","which","what","whats","show","find","list","search","look","looking","see","can","could","please","pls","tell","if","whether",
            "called","named","titled","book","books","novel","novels","ebook","ebooks","title","titles","file","files","written","wrote","author",
            "manga","comic","comics","anime","show","shows","series","episode","episodes","movie","movies","film","films","photo","photos","pictures",
            "picture","album","albums","pics","directed","director","directors","starring","actor","actress","actors","featuring","cast","acted","language","tv","made","films","watch","in","doc","docs","document","documents","pdf","app","apps","program","magazine","magazines","guide","guides","how","many","much","all","else","other","still","yet","by","one","ones","some" };
        static readonly string[] NumWords = { "vol","volume","volumes","v","ch","chapter","chapters","ep","episode","s","season","part","no","number" };

        public static bool IsQuestion(string q)
        {
            string l = q.Trim().ToLowerInvariant();
            if (l.EndsWith("?")) return true;
            return Regex.IsMatch(l, @"^(do|did|does|have|has|got|is|are|any|where|which|what|show me|find|list|how many|can you find|search for)\b") ||
                   Regex.IsMatch(l, @"\b(do i have|have i got|in my (library|collection|pc|computer)|do we have)\b");
        }

        static string[] CatHints(string l)
        {
            var c = new List<string>();
            if (Regex.IsMatch(l, @"\b(books?|novels?|ebooks?|read|author|wrote|written)\b")) { c.Add("Books"); c.Add("Reference"); c.Add("Travel"); c.Add("Magazines"); }
            if (Regex.IsMatch(l, @"\b(manga|volumes?|vol|chapters?|ch)\b")) c.Add("Manga");
            if (Regex.IsMatch(l, @"\b(comics?|graphic novels?|issues?|tpb|dc|marvel)\b")) { c.Add("Comics"); if (Regex.IsMatch(l, @"\bcomics?\b")) c.Add("Manga"); }
            if (Regex.IsMatch(l, @"\b(animation|animated|cartoons?|pixar|disney)\b")) c.Add("Animation");
            if (Regex.IsMatch(l, @"\b(movies?|films?|watch|director|directed|starring|actor|actress)\b")) c.Add("Movies");
            if (Regex.IsMatch(l, @"\b(shows?|episodes?|season|series|tv|watch|director|directed|starring|actor|actress)\b")) c.Add("TV");
            if (Regex.IsMatch(l, @"\b(anime|episodes?|season|series|watch|directed|director)\b")) { c.Add("Anime"); c.Add("Animation"); }
            if (Regex.IsMatch(l, @"\b(photos?|pictures?|pics|albums?|trip)\b")) { c.Add("Photos"); c.Add("Folders"); }
            if (Regex.IsMatch(l, @"\b(magazines?)\b")) c.Add("Magazines");
            if (Regex.IsMatch(l, @"\b(guides?|travel)\b")) c.Add("Travel");
            if (Regex.IsMatch(l, @"\b(apps?|programs?|software|installed)\b")) c.Add("Apps");
            if (Regex.IsMatch(l, @"\b(docs?|documents?|pdfs?|resume|cv|invoice|notes)\b")) { c.Add("Docs"); c.Add("Work"); c.Add("Projects"); }
            return c.Distinct().ToArray();
        }

        static int Lev(string a, string b)
        {
            if (Math.Abs(a.Length - b.Length) > 2) return 9;
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        static string[] Words(string s) { return Regex.Split(s ?? "", @"[^\p{L}\p{N}]+").Where(w => w.Length > 0).ToArray(); }

        public static string AuthorOf(Item it)
        {
            if (it.Mode == "title") return it.Person ?? "";
            if (it.Mode != "calibre" || it.Sub == null) return "";
            int p = it.Sub.IndexOf("  \u00B7  ");
            return p > 0 ? it.Sub.Substring(0, p) : "";
        }

        static string Hay(Item it)
        {
            string extra = AuthorOf(it);
            if (extra.Length == 0)
            {
                try { extra = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(it.Path) ?? "") ?? ""; } catch { }
            }
            return it.LName + " " + extra.ToLowerInvariant() + " " + it.LTags;
        }

        // 2 = exact/prefix hit, 1 = fuzzy (typo) hit, 0 = miss
        static int Hit(string tok, string[] words)
        {
            bool num = tok.All(char.IsDigit);
            int best = 0;
            foreach (var w in words)
            {
                if (num) { if (w.All(char.IsDigit) && w.TrimStart('0') == tok.TrimStart('0')) return 2; continue; }
                if (w.StartsWith(tok) || (tok.Length >= 5 && w.Contains(tok))) return 2;
                if (tok.Length >= 4 && w.Length >= 4)
                {
                    int lim = tok.Length >= 7 ? 2 : 1;
                    if (Lev(tok, w) <= lim) best = 1;
                }
            }
            return best;
        }

        public static Answer Ask(string query, string cat)
        {
            if (!IsQuestion(query)) return null;
            string l = query.ToLowerInvariant().Replace("'s", "").Replace("\u2019s", "");
            var hints = CatHints(l);
            var toks = new List<string>();
            foreach (var w in Words(l))
            {
                if (Array.IndexOf(AskStop, w) >= 0 || Array.IndexOf(NumWords, w) >= 0) continue;
                var m = Regex.Match(w, @"^(?:vol|v|ch|ep|s|e)(\d+)$");
                if (m.Success) { toks.Add(m.Groups[1].Value); continue; }
                toks.Add(w);
            }
            if (toks.Count == 0) return null;
            string topic = string.Join(" ", toks);
            bool all = string.IsNullOrEmpty(cat) || cat == "All";
            var strong = new List<Item>(); var partial = new List<Item>(); var series = new List<Item>();
            bool hasNum = toks.Any(t => t.All(char.IsDigit)) && toks.Any(t => !t.All(char.IsDigit));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tarr = toks.ToArray();
            foreach (var it in Items)
            {
                if (!all && !InCat(it, cat)) continue;
                if (all && hints.Length > 0 && Array.IndexOf(hints, it.Cat) < 0) continue;
                if (!seen.Add(it.Path)) continue;
                var words = Words(Hay(it));
                int exact = 0, fuzzy = 0, sig = 0, textExact = 0, textN = 0;
                foreach (var t in tarr)
                {
                    int h = Hit(t, words);
                    bool isNum = t.All(char.IsDigit);
                    if (!isNum) { textN++; if (h == 2) textExact++; }
                    if (h == 2) { exact++; if (t.Length >= 3 || isNum) sig++; }
                    else if (h == 1) fuzzy++;
                }
                if (hasNum && textN > 0 && textExact == textN && exact < tarr.Length) series.Add(it);
                int sc = exact * 100 + fuzzy * 60 + HistBoost(it) + CatBoost(it) - Math.Min(it.LName.Length, 80) / 8;
                if (exact == tarr.Length && sig > 0) { it.Score = sc + 1000; strong.Add(it); }
                else if (exact + fuzzy >= Math.Max(1, (tarr.Length + 1) / 2) && (sig > 0 || fuzzy > 0)) { it.Score = sc; partial.Add(it); }
            }
            Comparison<Item> by = delegate(Item a, Item b) { int c = b.Score.CompareTo(a.Score); return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); };
            strong.Sort(by); partial.Sort(by);

            var ans = new Answer(); ans.Topic = topic; ans.Query = query.Trim();
            string kindWord = hints.Contains("Comics") && !hints.Contains("Manga") ? "comics" : hints.Contains("Manga") ? "manga" : hints.Contains("Movies") ? "titles" : hints.Contains("Photos") ? "albums/photos" : hints.Contains("Books") ? "books" : "items";
            if (strong.Count > 0)
            {
                ans.Verdict = "Yes"; ans.Hits = strong;
                var best = strong[0];
                // all hits by one author that the question named?
                string author = AuthorOf(best);
                bool byAuthor = author.Length > 0 && tarr.All(t => Hit(t, Words(author.ToLowerInvariant())) == 2);
                if (byAuthor)
                {
                    var books = strong.Where(x => AuthorOf(x) == author).ToList();
                    var titles = books.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    string noun = best.Mode == "title" ? "title" : "book";
                    ans.Text = "Yes \u2014 you have " + titles.Count + " " + noun + (titles.Count == 1 ? "" : "s") + " by " + author + ".";
                    ans.Detail = string.Join("  \u00B7  ", titles.Take(12)) + (titles.Count > 12 ? "  \u00B7  \u2026" : "");
                }
                else
                {
                    int n = strong.Count;
                    string a = AuthorOf(best);
                    ans.Text = "Yes \u2014 " + best.Name + (a.Length > 0 ? " by " + a : "") + " is in " + best.Cat + ".";
                    ans.Detail = n > 1 ? n + " matches for \u201C" + topic + "\u201D \u2014 " + string.Join("  \u00B7  ", strong.Skip(1).Take(6).Select(x => x.Name)) + (n > 7 ? "  \u00B7  \u2026" : "") : best.Sub;
                }
                ans.Best = best;
            }
            else if (series.Count > 0)
            {
                ans.Verdict = "No"; ans.Hits = series.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                string num = toks.First(t => t.All(char.IsDigit));
                string what = string.Join(" ", toks.Where(t => !t.All(char.IsDigit)));
                Item top = null; int topN = -1;
                foreach (var x in series)
                {
                    var mm = Regex.Match(x.Name, @"(?:\bvol(?:ume)?|\bch(?:apter)?|e)\s*(\d+)", RegexOptions.IgnoreCase);
                    int v; if (mm.Success && int.TryParse(mm.Groups[1].Value, out v) && v > topN) { topN = v; top = x; }
                }
                if (top != null && top.Name.IndexOf(" - ") > 0) what = top.Name.Substring(0, top.Name.IndexOf(" - "));
                ans.Text = "No \u2014 you have " + what + ", but not " + num + ".";
                ans.Detail = (top != null ? "Latest you have: " + top.Name + ".  " : "") + series.Count + " related item" + (series.Count == 1 ? "" : "s") + " below.";
                ans.Best = top ?? series[0];
            }
            else if (partial.Count > 0)
            {
                ans.Verdict = "Not sure"; ans.Hits = partial;
                ans.Text = "Not sure \u2014 nothing matches \u201C" + topic + "\u201D exactly. Closest " + kindWord + " you have:";
                ans.Detail = string.Join("  \u00B7  ", partial.Take(6).Select(x => x.Name + (AuthorOf(x).Length > 0 ? " (" + AuthorOf(x) + ")" : "")));
                ans.Best = partial[0];
            }
            else
            {
                ans.Verdict = "No"; ans.Hits = new List<Item>();
                ans.Text = "No \u2014 I can't find \u201C" + topic + "\u201D in your " + (hints.Length > 0 ? kindWord : "library, photos or files") + ".";
                ans.Detail = "Checked " + Items.Count.ToString("N0") + " indexed items" + (hints.Length > 0 ? " in " + string.Join(", ", hints) : "") + ". Try the links below, or press F5 if you just added it.";
            }
            if (ans.Best != null && ans.Best.Offline && ans.Verdict != "No")
                ans.Text = ans.Text.TrimEnd('.') + " \u2014 on " + ans.Best.Where + ", which isn't connected right now.";
            string enc = Uri.EscapeDataString(topic);
            string bc = ans.Best != null ? ans.Best.Cat : "";
            if (bc == "Manga" || bc == "Movies") hints = hints.Concat(new[] { bc }).Distinct().ToArray();
            if (bc == "Books" || bc == "Reference" || (bc.Length == 0 && hints.Length == 0)) hints = hints.Concat(new[] { "Books" }).Distinct().ToArray();
            if (hints.Contains("Books"))
            {
                ans.Links.Add(new[] { "Goodreads", "https://www.goodreads.com/search?q=" + enc });
                ans.Links.Add(new[] { "Amazon.in", "https://www.amazon.in/s?k=" + enc + "&i=digital-text" });
            }
            if (hints.Contains("Manga") || hints.Contains("Anime"))
                ans.Links.Add(new[] { "MyAnimeList", "https://myanimelist.net/search/all?q=" + enc });
            if (hints.Contains("Comics") || bc == "Comics")
                ans.Links.Add(new[] { "Comic Vine", "https://comicvine.gamespot.com/search/?q=" + enc });
            ans.Links.Add(new[] { "Google", "https://www.google.com/search?q=" + enc });
            ans.Links.Add(new[] { "Ask Claude", "https://claude.ai/new?q=" + Uri.EscapeDataString(query.Trim()) });
            return ans;
        }

        public static Item Special(string kind, string query)
        {
            var w = new Item();
            w.Kind = kind; w.Ext = "";
            if (kind == "claude")
            {
                w.Name = "Ask Claude: \u201C" + query.Trim() + "\u201D";
                w.Sub = "Opens claude.ai in Chrome with your question  \u00B7  Ctrl+Enter";
                w.Cat = "Claude";
                w.Path = "https://claude.ai/new?q=" + Uri.EscapeDataString(query.Trim());
            }
            else
            {
                w.Name = "Search the web for \u201C" + query.Trim() + "\u201D";
                w.Sub = "Google in Chrome  \u00B7  Ctrl+G";
                w.Cat = "Web";
                w.Path = "https://www.google.com/search?q=" + Uri.EscapeDataString(query.Trim());
            }
            Finish(w);
            return w;
        }
    }

    // ------------------------------------------------------------------ logging
    public static class Log
    {
        public static string File;
        public static void Write(string s)
        {
            try { if (File != null) System.IO.File.AppendAllText(File, DateTime.Now.ToString("s") + "  " + s + Environment.NewLine); } catch { }
        }
    }

    // ------------------------------------------------------------------ thumbnails
    public static class Thumbs
    {
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx; public int cy; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        static Dispatcher ui;
        static Thread sta;
        static readonly Queue<Action> staQueue = new Queue<Action>();
        static readonly AutoResetEvent staSignal = new AutoResetEvent(false);
        static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly string[] Img = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff" };

        public static void Init(Dispatcher d)
        {
            ui = d;
            sta = new Thread(StaLoop);
            sta.IsBackground = true;
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
        }

        static void StaLoop()
        {
            while (true)
            {
                Action a = null;
                lock (staQueue) { if (staQueue.Count > 0) a = staQueue.Dequeue(); }
                if (a == null) { staSignal.WaitOne(); continue; }
                try { a(); } catch { }
            }
        }

        static void Deliver(string key, ImageSource img, Action<ImageSource> done)
        {
            if (img != null && img.CanFreeze && !img.IsFrozen) img.Freeze();
            lock (cache) { cache[key] = img; }
            ui.BeginInvoke(new Action(delegate { done(img); }), DispatcherPriority.Background);
        }

        public static bool IsImage(string path)
        {
            return Array.IndexOf(Img, (System.IO.Path.GetExtension(path) ?? "").ToLowerInvariant()) >= 0;
        }

        // Thumbnail for an item, delivered on the UI thread.
        public static void Request(Item it, int size, Action<ImageSource> done)
        {
            string key = it.Path + "|" + size;
            lock (cache) { ImageSource c; if (cache.TryGetValue(key, out c)) { done(c); return; } }
            ThreadPool.QueueUserWorkItem(delegate
            {
                ImageSource img = null;
                try
                {
                    string src = PickSource(it);
                    if (src != null && IsImage(src)) img = LoadFile(src, size);
                    else if (src != null && (src.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase))) img = LoadCbz(src, size);
                    else if (src != null && (src.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))) img = LoadEpub(src, size);
                    else if (src != null && src.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase))
                    {
                        string cov = PdfCovers.GetArchive(src);
                        if (cov != null) img = LoadFile(cov, size);
                    }
                    else if (src != null && src.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        // the real first page (magazine / guide cover), not the PDF app's icon
                        string png = PdfCovers.Get(src);
                        if (png != null) img = LoadFile(png, size);
                        else { ShellAsync(src, size, key, done); return; }
                    }
                    else if (src != null && it.Mode == "title") src = null;   // never a random video frame for a film / show
                    else if (src != null && it.Kind != "app") { ShellAsync(src, size, key, done); return; }
                }
                catch (Exception ex) { Log.Write("thumb " + it.Path + ": " + ex.Message); }
                if (img == null && it.Kind == "app") { ShellAsync(it.Path, size, key, done); return; }
                Deliver(key, img, done);
            });
        }

        // which file best represents the item visually
        static string PickSource(Item it)
        {
            string p = it.Path;
            if (it.Kind == "web" || it.Kind == "claude") return null;
            if (!string.IsNullOrEmpty(it.Thumbnail) && File.Exists(it.Thumbnail)) return it.Thumbnail;
            if (it.Mode == "title") return null;        // films & shows: real poster or nothing (see MediaLib / Posters)
            if (it.Mode == "calibre")
            {
                string cover = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(p) ?? "", "cover.jpg");
                if (File.Exists(cover)) return cover;
            }
            bool strict = Array.IndexOf(StrictCats, it.Cat ?? "") >= 0;
            if (it.IsDir)
            {
                string cover = FindCover(p, 0, strict);
                return cover ?? (strict ? null : p);
            }
            if (strict && it.Kind == "video") return null;
            return p;
        }

        // categories where the picture must be real cover art (book cover, poster, disc art) -- never an arbitrary image
        static readonly string[] StrictCats = { "Manga", "Books", "Comics", "Movies", "TV", "TV Shows", "Anime", "Animation" };

        public static string FindCover(string dir, int depth) { return FindCover(dir, depth, false); }
        public static string FindCover(string dir, int depth, bool strict)
        {
            try
            {
                foreach (var name in new[] { "cover.jpg", "folder.jpg", "cover.png", "poster.jpg", "folder.png", "poster.png" })
                {
                    string c = System.IO.Path.Combine(dir, name);
                    if (File.Exists(c)) return c;
                }
                var files = Directory.GetFiles(dir);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                var imgs = files.Where(f => IsImage(f)).ToList();
                var art = imgs.Select(f => new { f, r = MediaLib.PosterRank(System.IO.Path.GetFileNameWithoutExtension(f)) }).Where(x => x.r > 0).OrderByDescending(x => x.r).FirstOrDefault();
                if (art != null) return art.f;
                // first volume / first book: its own cover (skip "00 - introduction / guide" style extras when there are real issues)
                var books = files.Where(f => Regex.IsMatch(f, @"\.(cbz|cbr|epub|pdf)$", RegexOptions.IgnoreCase)).ToList();
                var main = books.Where(f => !Regex.IsMatch(System.IO.Path.GetFileName(f), @"^(00|0)\b|^00-|preview|guide|catalog|puzzle", RegexOptions.IgnoreCase)).ToList();
                var book = (main.Count > 0 ? main : books).FirstOrDefault();
                if (book != null) return book;
                if (!strict && imgs.Count > 0) return imgs[imgs.Count / 3];
                // a folder of scanned pages (one issue/volume): its first page is the cover
                if (strict && imgs.Count > 0 && Directory.GetDirectories(dir).Length == 0) return imgs[0];
                if (depth < 2)
                {
                    var dirs = Directory.GetDirectories(dir);
                    Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                    // volumes before chapters, so the series shows its volume-1 cover
                    foreach (var d in dirs.OrderBy(d => System.IO.Path.GetFileName(d).StartsWith("Vol", StringComparison.OrdinalIgnoreCase) ? 0 : 1).Take(6))
                    {
                        var c = FindCover(d, depth + 1, strict);
                        if (c != null) return c;
                    }
                }
            }
            catch { }
            return null;
        }

        // ePub cover: <meta name="cover">, a manifest item with properties="cover-image", or an image called cover*
        public static ImageSource LoadEpub(string path, int size)
        {
            using (var z = ZipFile.OpenRead(path))
            {
                ZipArchiveEntry pick = null;
                try
                {
                    var cont = z.GetEntry("META-INF/container.xml");
                    string opfPath = null;
                    if (cont != null) using (var r = new StreamReader(cont.Open())) { var m = Regex.Match(r.ReadToEnd(), "full-path=\"([^\"]+)\""); if (m.Success) opfPath = m.Groups[1].Value; }
                    var opf = opfPath != null ? z.GetEntry(opfPath) : null;
                    if (opf != null)
                    {
                        string x; using (var r = new StreamReader(opf.Open())) x = r.ReadToEnd();
                        string baseDir = opfPath.Contains("/") ? opfPath.Substring(0, opfPath.LastIndexOf('/') + 1) : "";
                        string href = null;
                        var mc = Regex.Match(x, "<meta[^>]*name=\"cover\"[^>]*content=\"([^\"]+)\"|<meta[^>]*content=\"([^\"]+)\"[^>]*name=\"cover\"", RegexOptions.IgnoreCase);
                        string id = mc.Success ? (mc.Groups[1].Success && mc.Groups[1].Length > 0 ? mc.Groups[1].Value : mc.Groups[2].Value) : null;
                        foreach (Match it in Regex.Matches(x, "<item\\b[^>]*>", RegexOptions.IgnoreCase))
                        {
                            string tag = it.Value;
                            var hm = Regex.Match(tag, "href=\"([^\"]+)\""); if (!hm.Success) continue;
                            bool isCover = tag.IndexOf("cover-image", StringComparison.OrdinalIgnoreCase) >= 0 || (id != null && Regex.IsMatch(tag, "id=\"" + Regex.Escape(id) + "\""));
                            if (isCover && IsImage(hm.Groups[1].Value)) { href = hm.Groups[1].Value; break; }
                        }
                        if (href != null) pick = z.GetEntry(baseDir + Uri.UnescapeDataString(href)) ?? z.Entries.FirstOrDefault(e => e.FullName.EndsWith(Uri.UnescapeDataString(href), StringComparison.OrdinalIgnoreCase));
                    }
                }
                catch { }
                if (pick == null) pick = z.Entries.Where(e => IsImage(e.FullName) && System.IO.Path.GetFileName(e.FullName).StartsWith("cover", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                if (pick == null) return null;
                var ms = new MemoryStream();
                using (var s = pick.Open()) s.CopyTo(ms);
                ms.Position = 0;
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = size; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
                return bi;
            }
        }

        public static ImageSource LoadFile(string path, int size)
        {
            var bi = new BitmapImage();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.DecodePixelWidth = size;
                bi.StreamSource = fs;
                bi.EndInit();
            }
            bi.Freeze();
            return bi;
        }

        public static ImageSource LoadCbz(string path, int size)
        {
            using (var z = ZipFile.OpenRead(path))
            {
                var e = z.Entries.Where(x => IsImage(x.FullName)).OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (e == null) return null;
                var ms = new MemoryStream();
                using (var s = e.Open()) s.CopyTo(ms);
                ms.Position = 0;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = size;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }

        static void ShellAsync(string path, int size, string key, Action<ImageSource> done)
        {
            lock (staQueue)
            {
                staQueue.Enqueue(delegate
                {
                    ImageSource img = null;
                    try { img = Shell(path, size, 0x8 /*THUMBNAILONLY*/); } catch { }
                    if (img == null)
                    {
                        try { img = Shell(path, size, 0x4 /*ICONONLY*/); } catch { }
                        if (img != null) lock (icons) icons.Add(img);
                    }
                    Deliver(key, img, done);
                });
            }
            staSignal.Set();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER { public int biSize; public int biWidth; public int biHeight; public short biPlanes; public short biBitCount; public int biCompression; public int biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter; public int biClrUsed; public int biClrImportant; }
        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP { public int bmType; public int bmWidth; public int bmHeight; public int bmWidthBytes; public short bmPlanes; public short bmBitsPixel; public IntPtr bmBits; }
        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int c, ref BITMAP bm);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

        // Windows shell thumbnail (PDFs, videos, Office docs, folders, app icons) with proper alpha
        static readonly HashSet<ImageSource> icons = new HashSet<ImageSource>();
        public static bool IsIcon(ImageSource img) { lock (icons) return img != null && icons.Contains(img); }

        public static ImageSource Shell(string path, int size, int flags)
        {
            IShellItemImageFactory f;
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
            if (f == null) return null;
            IntPtr hbm;
            var sz = new SIZE(); sz.cx = size; sz.cy = size;
            int hr = f.GetImage(sz, 0x1 | flags, out hbm);
            Marshal.ReleaseComObject(f);
            if (hr != 0 || hbm == IntPtr.Zero) return null;
            IntPtr dc = GetDC(IntPtr.Zero);
            try
            {
                var bm = new BITMAP();
                GetObject(hbm, Marshal.SizeOf(typeof(BITMAP)), ref bm);
                int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
                if (w <= 0 || h <= 0) return null;
                var bi = new BITMAPINFOHEADER();
                bi.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bi.biWidth = w; bi.biHeight = -h; bi.biPlanes = 1; bi.biBitCount = 32; bi.biCompression = 0;
                var px = new byte[w * h * 4];
                if (GetDIBits(dc, hbm, 0, (uint)h, px, ref bi, 0) == 0) return null;
                bool anyAlpha = false;
                for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { anyAlpha = true; break; }
                var fmt = anyAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32;
                var bs = BitmapSource.Create(w, h, 96, 96, fmt, null, px, w * 4);
                bs.Freeze();
                return bs;
            }
            finally { ReleaseDC(IntPtr.Zero, dc); DeleteObject(hbm); }
        }
    }

    // ------------------------------------------------------------------ PDF covers
    // First page of a PDF rendered by Windows' own PDF engine (Windows.Data.Pdf via tools\pdf-cover.ps1, kept running),
    // cached as PNG in Drive Catalog\covers.
    public static class PdfCovers
    {
        static readonly object gate = new object();
        static System.Diagnostics.Process proc;
        static bool broken;
        static string dir;

        static string CacheFor(string pdf)
        {
            if (dir == null)
            {
                string root = Catalog.Dir ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Drive Catalog");
                dir = System.IO.Path.Combine(root, "covers");
                Directory.CreateDirectory(dir);
            }
            var fi = new FileInfo(pdf);
            string id = pdf.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return System.IO.Path.Combine(dir, BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(id))).Replace("-", "").Substring(0, 20) + ".png");
        }

        public static string Get(string pdf)
        {
            try
            {
                string png = CacheFor(pdf);
                if (File.Exists(png)) return png;
                if (broken) return null;
                lock (gate)
                {
                    if (File.Exists(png)) return png;
                    if (proc == null || proc.HasExited)
                    {
                        string script = System.IO.Path.Combine(Program.AppDir, "tools", "pdf-cover.ps1");
                        if (!File.Exists(script)) { broken = true; return null; }
                        var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -Serve");
                        psi.UseShellExecute = false; psi.CreateNoWindow = true;
                        psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true;
                        proc = System.Diagnostics.Process.Start(psi);
                    }
                    proc.StandardInput.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(pdf)) + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(png)));
                    proc.StandardInput.Flush();
                    var t = proc.StandardOutput.ReadLineAsync();
                    if (!t.Wait(45000)) { try { proc.Kill(); } catch { } proc = null; return null; }
                    string r = t.Result ?? "";
                    if (r.StartsWith("OK") && File.Exists(png)) return png;
                    if (r.Length == 0) { broken = true; Log.Write("pdf covers: renderer exited"); }
                    else Log.Write("pdf cover " + pdf + ": " + r);
                }
            }
            catch (Exception ex) { Log.Write("pdf cover: " + ex.Message); }
            return null;
        }

        // .cbr (RAR) comics: Windows' built-in tar (libarchive) lists the archive and pipes out the first page
        public static string GetArchive(string path)
        {
            try
            {
                string jpg = System.IO.Path.ChangeExtension(CacheFor(path), ".jpg");
                if (File.Exists(jpg)) return jpg;
                string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string tar = new[] { System.IO.Path.Combine(win, "Sysnative", "tar.exe"), System.IO.Path.Combine(win, "System32", "tar.exe") }.FirstOrDefault(File.Exists);
                if (tar == null) { Log.Write("cbr cover: tar.exe not found"); return null; }
                string list = RunText(tar, "-tf \"" + path + "\"");
                if (list == null) { Log.Write("cbr cover: listing timed out " + path); return null; }
                var first = list.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                .Where(e => Thumbs.IsImage(e) && e.IndexOf("__MACOSX", StringComparison.OrdinalIgnoreCase) < 0)
                                .OrderBy(e => e, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (first == null) { Log.Write("cbr cover: no pages in " + path + " (" + list.Length + " chars listed)"); return null; }
                var psi = new System.Diagnostics.ProcessStartInfo(tar, "-xOf \"" + path + "\" \"" + first + "\"");
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true;
                using (var p = System.Diagnostics.Process.Start(psi))
                using (var ms = new MemoryStream())
                {
                    var copy = p.StandardOutput.BaseStream.CopyToAsync(ms);
                    if (!copy.Wait(30000)) { try { p.Kill(); } catch { } return null; }
                    p.WaitForExit(5000);
                    if (ms.Length < 1000) { Log.Write("cbr cover: empty page from " + path); return null; }
                    File.WriteAllBytes(jpg, ms.ToArray());
                }
                return jpg;
            }
            catch (Exception ex) { Log.Write("cbr cover: " + ex.Message); return null; }
        }

        static string RunText(string exe, string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                var t = p.StandardOutput.ReadToEndAsync();
                if (!t.Wait(30000)) { try { p.Kill(); } catch { } return null; }
                return t.Result;
            }
        }

        public static void Stop() { try { if (proc != null && !proc.HasExited) proc.Kill(); } catch { } }
    }

    // ------------------------------------------------------------------ hotkey
    public class HotKey : IDisposable
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        HwndSource src; IntPtr hwnd; Action onPress; public string Label = "";
        public HotKey(Window w, Action press)
        {
            onPress = press;
            hwnd = new WindowInteropHelper(w).EnsureHandle();
            src = HwndSource.FromHwnd(hwnd);
            src.AddHook(Hook);
            const uint ALT = 1, CTRL = 2, NOREPEAT = 0x4000;
            if (RegisterHotKey(hwnd, 0x4E48, ALT | NOREPEAT, 0x20)) Label = "Alt+Space";
            else if (RegisterHotKey(hwnd, 0x4E48, CTRL | ALT | NOREPEAT, 0x20)) Label = "Ctrl+Alt+Space";
        }
        IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x0312 && w.ToInt32() == 0x4E48) { onPress(); handled = true; }
            return IntPtr.Zero;
        }
        public void Dispose() { UnregisterHotKey(hwnd, 0x4E48); }
    }

    // ------------------------------------------------------------------ settings (settings.json next to the exe)
    public class ShortcutButton { public string Label = "", Path = "", Fallback = ""; }
    public static class Settings
    {
        public static string AppName = "Desktop Organizer", UserName = "";
        public static List<ShortcutButton> Buttons = new List<ShortcutButton>();
        public static List<string> BackgroundFolders = new List<string>();
        public static List<string> IgnoreFolders = new List<string>();
        public static string DriveLetterHint = "", MediaDrive = "", LibraryDrive = "";

        public static void Load(string dir)
        {
            string f = System.IO.Path.Combine(dir, "settings.json");
            try
            {
                if (!File.Exists(f)) return;
                var js = new System.Web.Script.Serialization.JavaScriptSerializer();
                var d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(f, Encoding.UTF8));
                object o;
                if (d.TryGetValue("appName", out o) && o is string && ((string)o).Length > 0) AppName = (string)o;
                if (d.TryGetValue("userName", out o) && o is string) UserName = (string)o;
                if (d.TryGetValue("driveLetterHint", out o) && o is string) DriveLetterHint = (string)o;
                if (d.TryGetValue("mediaDrive", out o) && o is string) MediaDrive = (string)o;
                if (d.TryGetValue("libraryDrive", out o) && o is string) LibraryDrive = (string)o;
                if (d.TryGetValue("buttons", out o) && o is System.Collections.ArrayList)
                    foreach (var b in (System.Collections.ArrayList)o)
                    {
                        var bd = b as Dictionary<string, object>; if (bd == null) continue;
                        var sb = new ShortcutButton();
                        object v;
                        if (bd.TryGetValue("label", out v)) sb.Label = Convert.ToString(v);
                        if (bd.TryGetValue("path", out v)) sb.Path = Environment.ExpandEnvironmentVariables(Convert.ToString(v));
                        if (bd.TryGetValue("fallback", out v)) sb.Fallback = Environment.ExpandEnvironmentVariables(Convert.ToString(v));
                        if (sb.Label.Length > 0) Buttons.Add(sb);
                    }
                if (d.TryGetValue("backgroundFolders", out o) && o is System.Collections.ArrayList)
                    foreach (var x in (System.Collections.ArrayList)o) BackgroundFolders.Add(Environment.ExpandEnvironmentVariables(Convert.ToString(x)));
                if (d.TryGetValue("ignoreFolders", out o) && o is System.Collections.ArrayList)
                    foreach (var x in (System.Collections.ArrayList)o) IgnoreFolders.Add(Convert.ToString(x).ToLowerInvariant());
            }
            catch (Exception ex) { Log.Write("settings: " + ex.Message); }
        }
    }

    // ------------------------------------------------------------------ app
    public class Program
    {
        public static string AppDir, DataDir;
        static Mutex mutex;
        static EventWaitHandle showSignal;

        [STAThread]
        public static void Main(string[] args)
        {
            AppDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            DataDir = System.IO.Path.Combine(AppDir, "data");
            Directory.CreateDirectory(DataDir);
            Log.File = System.IO.Path.Combine(DataDir, "error.log");
            Settings.Load(AppDir);

            bool created;
            mutex = new Mutex(true, "Local\\DesktopOrganizer.Instance", out created);
            bool wantSort = args.Any(a => a.Equals("--sort", StringComparison.OrdinalIgnoreCase));
            if (!created)
            {
                try { EventWaitHandle.OpenExisting(wantSort ? "Local\\DesktopOrganizer.Sort" : "Local\\DesktopOrganizer.Show").Set(); } catch { }
                return;
            }
            var sortSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DesktopOrganizer.Sort");
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DesktopOrganizer.Show");

            // anything that would kill the process silently gets written down first
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log.Write("CRASH: " + e.ExceptionObject);
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(DataDir, "last-crash.txt"), DateTime.Now.ToString("s") + Environment.NewLine + e.ExceptionObject); } catch { }
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += delegate(object s, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
            {
                Log.Write("task: " + e.Exception.GetBaseException().Message); e.SetObserved();
            };
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                Log.Write("UI: " + e.Exception);
                e.Handled = true;
            };
            bool startup = args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));
            bool hidden = args.Any(a => a.Equals("--hidden", StringComparison.OrdinalIgnoreCase));
            Home home = null;
            try { home = new Home(); }
            catch (Exception ex)
            {
                Log.Write("start: " + ex);
                MessageBox.Show(Settings.AppName + " couldn't start:\n\n" + ex.Message + "\n\nDetails: " + Log.File, Settings.AppName);
                return;
            }
            var t = new Thread(delegate()
            {
                while (true)
                {
                    showSignal.WaitOne();
                    app.Dispatcher.BeginInvoke(new Action(delegate { home.Summon(); }));
                }
            });
            t.IsBackground = true;
            t.Start();
            var t2 = new Thread(delegate()
            {
                while (true)
                {
                    sortSignal.WaitOne();
                    app.Dispatcher.BeginInvoke(new Action(delegate { Sorter.Show(); }));
                }
            });
            t2.IsBackground = true; t2.Start();
            if (wantSort) { home.Prepare(); app.Dispatcher.BeginInvoke(new Action(delegate { Sorter.Show(); })); }
            else if (!hidden) home.Summon(); else home.Prepare();
            app.Run();
            GC.KeepAlive(mutex);
        }
    }
}
