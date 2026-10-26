// Desktop Organizer - drive catalog (appended after Core.cs at build time; shares its using block)
// Keeps a persistent record of every file on every drive, keyed by the volume's serial number,
// so files on an unplugged external drive are still known. Exports "Drive Catalog.xlsx".
namespace DesktopOrganizer
{
    public class CatFile
    {
        public string Rel, Ext, Type, Codec = "", Quality = "";
        public long Size, Mod, DurSec;
        public int W, H, Kbps;
        public bool Cloud, Probed;
        public DateTime Created;   // recording time from the video header (not stored)
    }

    public class CatVolume
    {
        public string Id, Label = "", Letter = "", DriveType = "";
        public long Total, Free;
        public DateTime Scanned = DateTime.MinValue;
        public int Ver;
        public bool Online;
        public List<CatFile> Files = new List<CatFile>();
        public string Root { get { return Letter + ":\\"; } }
        public string Display { get { return Nick + " (" + Letter + ":)"; } }
        // what the drive is called in the UI: by size ("5TB", "2TB"), or "Laptop" for the internal SSD partitions
        public string Nick
        {
            get
            {
                if (DriveType.StartsWith("System")) return "Laptop";
                if (Total <= 0) return Label.Length > 0 ? Label : "Drive";
                double tb = Total / 1e12;
                if (tb < 0.7) return "Laptop";
                return Math.Round(tb).ToString("0") + "TB";
            }
        }
    }

    public static class Catalog
    {
        public static string Dir;
        public static readonly object Sync = new object();
        public static Dictionary<string, CatVolume> Vols = new Dictionary<string, CatVolume>(StringComparer.OrdinalIgnoreCase);
        public static volatile bool Busy;
        public const int ScanVer = 2;
        public static volatile string StatusText = "";
        public static string XlsxPath { get { return System.IO.Path.Combine(Dir, "Drive Catalog.xlsx"); } }
        static Action onChanged;
        static readonly AutoResetEvent wake = new AutoResetEvent(false);
        static bool forceAll;

        static readonly string[] SkipDirs = { "$recycle.bin", "system volume information", "windows", "program files", "program files (x86)",
            "programdata", "appdata", "node_modules", ".git", "__pycache__", ".venv", "venv", ".next", ".cache", ".idea", ".vs", "_to recycle",
            "recovery", "$windows.~bt", "$windows.~ws", "msocache", "perflogs", "config.msi", "found.000", ".trash-1000", ".spotlight-v100", ".fseventsd" };
        static readonly string[] SkipFiles = { "desktop.ini", "thumbs.db", ".ds_store", "pagefile.sys", "hiberfil.sys", "swapfile.sys", "dumpstack.log.tmp" };
        static readonly string[] Videos = { ".mp4", ".mkv", ".avi", ".mov", ".m4v", ".wmv", ".webm", ".ts", ".m2ts", ".mpg", ".mpeg", ".flv", ".3gp", ".vob" };

        public static string TypeOf(string ext)
        {
            switch (ext)
            {
                case ".mp4": case ".mkv": case ".avi": case ".mov": case ".m4v": case ".wmv": case ".webm": case ".ts": case ".m2ts":
                case ".mpg": case ".mpeg": case ".flv": case ".3gp": case ".vob": return "Video";
                case ".jpg": case ".jpeg": case ".png": case ".gif": case ".bmp": case ".webp": case ".tif": case ".tiff": case ".heic":
                case ".arw": case ".cr2": case ".cr3": case ".nef": case ".dng": case ".raf": case ".orf": case ".rw2": case ".psd": return "Image";
                case ".mp3": case ".flac": case ".m4a": case ".aac": case ".wav": case ".ogg": case ".opus": case ".wma": case ".alac": return "Audio";
                case ".epub": case ".mobi": case ".azw": case ".azw3": case ".kfx": case ".fb2": case ".djvu": return "eBook";
                case ".cbz": case ".cbr": case ".cb7": return "Comic";
                case ".pdf": return "PDF";
                case ".doc": case ".docx": case ".odt": case ".rtf": case ".txt": case ".md": case ".pages": return "Document";
                case ".xls": case ".xlsx": case ".xlsm": case ".csv": case ".ods": case ".numbers": return "Spreadsheet";
                case ".ppt": case ".pptx": case ".odp": case ".key": return "Presentation";
                case ".zip": case ".rar": case ".7z": case ".tar": case ".gz": case ".bz2": case ".xz": case ".iso": case ".img": return "Archive";
                case ".exe": case ".msi": case ".msix": case ".appx": case ".dmg": case ".apk": case ".pkg": return "Installer";
                case ".srt": case ".ass": case ".ssa": case ".sub": case ".vtt": return "Subtitle";
                case ".py": case ".js": case ".ts_": case ".tsx": case ".jsx": case ".cs": case ".java": case ".cpp": case ".c": case ".h": case ".ipynb":
                case ".html": case ".css": case ".json": case ".xml": case ".yml": case ".yaml": case ".sql": case ".ps1": case ".sh": case ".m": return "Code";
                case ".lnk": case ".url": return "Shortcut";
                case ".nfo": case ".ini": case ".log": case ".tmp": case ".bak": case ".dat": case ".db": return "System/meta";
                default: return "Other";
            }
        }

        // ------------------------------------------------------------ persistence
        public static void Init(string dir)
        {
            Dir = dir;
            try { Directory.CreateDirectory(System.IO.Path.Combine(Dir, "volumes")); } catch { }
            var vols = new Dictionary<string, CatVolume>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var f in Directory.GetFiles(System.IO.Path.Combine(Dir, "volumes"), "*.tsv"))
                {
                    try { var v = Load(f); if (v != null) vols[v.Id] = v; } catch (Exception ex) { Log.Write("catalog load " + f + ": " + ex.Message); }
                }
            }
            catch { }
            lock (Sync) Vols = vols;
            MarkOnline();
        }

        static CatVolume Load(string file)
        {
            CatVolume v = null;
            using (var r = new StreamReader(file, Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    var c = line.Split('\t');
                    if (v == null)
                    {
                        if (c.Length < 9 || c[0] != "#vol") return null;
                        v = new CatVolume();
                        v.Id = c[1]; v.Label = c[2]; v.Letter = c[3]; v.DriveType = c[4];
                        long.TryParse(c[5], out v.Total); long.TryParse(c[6], out v.Free);
                        long t; if (long.TryParse(c[7], out t)) v.Scanned = new DateTime(t);
                        if (c.Length > 9) int.TryParse(c[9], out v.Ver);
                        continue;
                    }
                    if (c.Length < 12) continue;
                    var cf = new CatFile();
                    cf.Rel = c[0]; long.TryParse(c[1], out cf.Size); long.TryParse(c[2], out cf.Mod);
                    cf.Ext = c[3]; cf.Type = c[4]; cf.Cloud = c[5] == "1";
                    int.TryParse(c[6], out cf.W); int.TryParse(c[7], out cf.H); long.TryParse(c[8], out cf.DurSec);
                    int.TryParse(c[9], out cf.Kbps); cf.Codec = c[10]; cf.Quality = c[11];
                    cf.Probed = c.Length > 12 && c[12] == "1";
                    v.Files.Add(cf);
                }
            }
            return v;
        }

        static void Save(CatVolume v)
        {
            string file = System.IO.Path.Combine(Dir, "volumes", Safe(v.Id) + ".tsv");
            string tmp = file + ".tmp";
            using (var w = new StreamWriter(tmp, false, new UTF8Encoding(false)))
            {
                w.Write("#vol\t" + v.Id + "\t" + Clean(v.Label) + "\t" + v.Letter + "\t" + v.DriveType + "\t" + v.Total + "\t" + v.Free + "\t" + v.Scanned.Ticks + "\t" + v.Files.Count + "\t" + ScanVer + "\n");
                foreach (var f in v.Files)
                    w.Write(Clean(f.Rel) + "\t" + f.Size + "\t" + f.Mod + "\t" + f.Ext + "\t" + f.Type + "\t" + (f.Cloud ? "1" : "0") + "\t" + f.W + "\t" + f.H + "\t" +
                            f.DurSec + "\t" + f.Kbps + "\t" + Clean(f.Codec) + "\t" + Clean(f.Quality) + "\t" + (f.Probed ? "1" : "0") + "\n");
            }
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
        }

        static string Clean(string s) { return (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " "); }
        static string Safe(string s) { foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_'); return s; }

        // ------------------------------------------------------------ volume identity
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetVolumeInformation(string root, StringBuilder name, int nameSize, out uint serial, out uint maxComp, out uint flags, StringBuilder fs, int fsSize);

        public static string VolumeId(string root)
        {
            try
            {
                uint serial, mc, fl;
                if (GetVolumeInformation(root, null, 0, out serial, out mc, out fl, null, 0) && serial != 0) return serial.ToString("X8");
            }
            catch { }
            return "L-" + root.Substring(0, 1).ToUpperInvariant();
        }

        static List<DriveInfo> Ready()
        {
            var res = new List<DriveInfo>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady) continue;
                        if (d.DriveType != System.IO.DriveType.Fixed && d.DriveType != System.IO.DriveType.Removable) continue;
                        res.Add(d);
                    }
                    catch { }
                }
            }
            catch { }
            return res;
        }

        static bool IsSystem(DriveInfo d)
        {
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return sys.Length > 1 && char.ToUpperInvariant(sys[0]) == char.ToUpperInvariant(d.Name[0]);
        }

        // returns true when anything changed
        static bool MarkOnline()
        {
            bool changed = false;
            var ready = Ready();
            lock (Sync)
            {
                var online = new Dictionary<string, DriveInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in ready) online[VolumeId(d.Name)] = d;
                foreach (var v in Vols.Values)
                {
                    DriveInfo d;
                    bool on = online.TryGetValue(v.Id, out d);
                    if (on != v.Online) { v.Online = on; changed = true; }
                    if (on)
                    {
                        string letter = d.Name.Substring(0, 1).ToUpperInvariant();
                        if (letter != v.Letter) { v.Letter = letter; changed = true; }
                        try { v.Free = d.AvailableFreeSpace; v.Total = d.TotalSize; if (d.VolumeLabel.Length > 0) v.Label = d.VolumeLabel; } catch { }
                    }
                }
            }
            return changed;
        }

        // ------------------------------------------------------------ scanning
        public static void Start(Action changed)
        {
            onChanged = changed;
            var t = new Thread(Loop);
            t.IsBackground = true; t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }

        public static void RescanNow() { forceAll = true; wake.Set(); }

        static void Loop()
        {
            wake.WaitOne(TimeSpan.FromSeconds(25));
            while (true)
            {
                try { Cycle(); } catch (Exception ex) { Log.Write("catalog: " + ex); Busy = false; }
                wake.WaitOne(TimeSpan.FromSeconds(60));
            }
        }

        static void Cycle()
        {
            bool force = forceAll; forceAll = false;
            bool changed = MarkOnline();
            var todo = new List<DriveInfo>();
            foreach (var d in Ready())
            {
                string id = VolumeId(d.Name);
                CatVolume v; lock (Sync) Vols.TryGetValue(id, out v);
                if (force || v == null || (DateTime.Now - v.Scanned).TotalHours >= 6 || v.Scanned == DateTime.MinValue || v.Ver < ScanVer) todo.Add(d);
            }
            if (todo.Count > 0)
            {
                Busy = true;
                foreach (var d in todo)
                {
                    try
                    {
                        string id = VolumeId(d.Name);
                        CatVolume old; lock (Sync) Vols.TryGetValue(id, out old);
                        var v = Scan(d, id, old);
                        lock (Sync) Vols[id] = v;
                        Save(v);
                        changed = true;
                    }
                    catch (Exception ex) { Log.Write("catalog scan " + d.Name + ": " + ex.Message); }
                }
                Busy = false;
            }
            if (changed || !File.Exists(XlsxPath))
            {
                StatusText = "Writing catalogue\u2026";
                try { Export(XlsxPath); } catch (Exception ex) { Log.Write("catalog export: " + ex.Message); }
                try { MediaLib.AfterCatalog(); } catch (Exception ex) { Log.Write("media: " + ex.Message); }
                StatusText = "";
                var cb = onChanged; if (cb != null) cb();
            }
        }

        public static List<string> RootsFor(DriveInfo d)
        {
            var roots = new List<string>();
            if (!IsSystem(d)) { roots.Add(d.RootDirectory.FullName); return roots; }
            string home = Environment.GetEnvironmentVariable("USERPROFILE") ?? "";
            foreach (var sub in new[] { "Desktop", "Documents", "Downloads", "Pictures", "Videos", "Music",
                                        "OneDrive\\Desktop", "OneDrive\\Documents", "OneDrive\\Pictures", "OneDrive\\Videos", "OneDrive\\Music" })
            {
                string p = System.IO.Path.Combine(home, sub);
                if (Directory.Exists(p)) roots.Add(p);
            }
            string pub = Environment.GetEnvironmentVariable("PUBLIC") ?? "";
            if (pub.Length > 0) foreach (var sub in new[] { "Videos", "Music", "Pictures", "Documents" })
                { string p = System.IO.Path.Combine(pub, sub); if (Directory.Exists(p)) roots.Add(p); }
            return roots;
        }

        public static CatVolume Scan(DriveInfo d, string id, CatVolume old)
        {
            var v = new CatVolume();
            v.Id = id; v.Letter = d.Name.Substring(0, 1).ToUpperInvariant(); v.Online = true; v.Ver = ScanVer;
            try { v.Label = d.VolumeLabel; v.Total = d.TotalSize; v.Free = d.AvailableFreeSpace; v.DriveType = d.DriveType.ToString(); } catch { }
            if (IsSystem(d)) v.DriveType = "System (user folders)";
            var prev = new Dictionary<string, CatFile>(StringComparer.OrdinalIgnoreCase);
            if (old != null) foreach (var f in old.Files) prev[f.Rel] = f;
            string root = d.RootDirectory.FullName;
            bool sysDrive = IsSystem(d);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var r in RootsFor(d))
            {
                var stack = new Stack<string>();
                stack.Push(r);
                while (stack.Count > 0)
                {
                    string dir = stack.Pop();
                    DirectoryInfo di;
                    FileSystemInfo[] kids;
                    try { di = new DirectoryInfo(dir); kids = di.GetFileSystemInfos(); } catch { continue; }
                    foreach (var k in kids)
                    {
                        try
                        {
                            var a = k.Attributes;
                            if ((a & FileAttributes.Directory) != 0)
                            {
                                string dn = k.Name.ToLowerInvariant();
                                if (Array.IndexOf(SkipDirs, dn) >= 0 || dn.StartsWith("$")) continue;
                                bool hiddenSys = (a & FileAttributes.Hidden) != 0 && (a & FileAttributes.System) != 0;
                                bool reparse = (a & FileAttributes.ReparsePoint) != 0 && (a & (FileAttributes)0x400000) == 0 && (a & FileAttributes.Offline) == 0;
                                if (sysDrive && (hiddenSys || reparse)) continue;              // legacy junctions like "My Music" in Documents
                                if (!sysDrive && reparse && (a & FileAttributes.Hidden) != 0) continue;
                                if (k.FullName.Length > 400 || k.FullName.Count(ch => ch == '\\') > 40) continue;
                                stack.Push(k.FullName);
                                continue;
                            }
                            var fi = (FileInfo)k;
                            string ln = fi.Name.ToLowerInvariant();
                            if (Array.IndexOf(SkipFiles, ln) >= 0 || ln.StartsWith("~$") || ln.StartsWith(".device_info")) continue;
                            string full = fi.FullName;
                            string rel = full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
                            if (!seen.Add(rel)) continue;
                            var cf = new CatFile();
                            cf.Rel = rel; cf.Ext = fi.Extension.ToLowerInvariant(); cf.Type = TypeOf(cf.Ext);
                            cf.Size = fi.Length; cf.Mod = fi.LastWriteTime.Ticks;
                            cf.Cloud = ((int)a & (0x400000 | 0x40000 | 0x1000)) != 0;
                            if (cf.Type == "Video")
                            {
                                CatFile p;
                                if (prev.TryGetValue(rel, out p) && p.Probed && p.Size == cf.Size && p.Mod == cf.Mod && (p.W > 0 || p.DurSec > 0 && p.Kbps >= 0 && p.Codec.Length > 0))
                                { cf.W = p.W; cf.H = p.H; cf.DurSec = p.DurSec; cf.Kbps = p.Kbps; cf.Codec = p.Codec; cf.Probed = true; }
                                else if (!cf.Cloud)
                                {
                                    MediaHeader.Probe(full, cf);
                                    if (cf.W <= 0 || cf.DurSec <= 0)
                                    {
                                        var alt = new CatFile(); VideoProps.Probe(full, alt);
                                        if (cf.W <= 0 && alt.W > 0) { cf.W = alt.W; cf.H = alt.H; }
                                        if (cf.DurSec <= 0) cf.DurSec = alt.DurSec;
                                        if (cf.Codec.Length == 0) cf.Codec = alt.Codec;
                                        if (alt.Kbps > 0) cf.Kbps = alt.Kbps;
                                    }
                                    if (cf.Kbps <= 0 && cf.DurSec > 0) cf.Kbps = (int)(cf.Size * 8 / cf.DurSec / 1000);
                                    cf.Probed = true;
                                }
                                cf.Quality = QualityOf(cf, fi.Name);
                            }
                            v.Files.Add(cf);
                            if (++n % 500 == 0) { StatusText = "Cataloguing " + v.Display + "  \u00B7  " + n.ToString("N0") + " files"; Thread.Sleep(1); }
                        }
                        catch { }
                    }
                }
            }
            v.Files.Sort(delegate(CatFile x, CatFile y) { return string.Compare(x.Rel, y.Rel, StringComparison.OrdinalIgnoreCase); });
            v.Scanned = DateTime.Now;
            StatusText = "";
            return v;
        }

        // ------------------------------------------------------------ video quality
        public static string ResLabel(int w, int h)
        {
            if (w <= 0 && h <= 0) return "";
            if (w >= 3600 || h >= 2000) return "4K";
            if (w >= 2400 || h >= 1400) return "1440p";
            if (w >= 1800 || h >= 1000) return "1080p";
            if (w >= 1200 || h >= 700) return "720p";
            if (h >= 540 || w >= 960) return "SD 576p";
            return "SD " + (h > 0 ? h + "p" : "");
        }

        public static string QualityOf(CatFile cf, string name)
        {
            string n = name.ToLowerInvariant();
            var parts = new List<string>();
            string res = ResLabel(cf.W, cf.H);
            if (res.Length == 0)
            {
                var m = Regex.Match(n, @"\b(2160p|4k|1440p|1080p|720p|576p|480p|360p)\b");
                if (m.Success) res = m.Value == "2160p" ? "4K" : m.Value == "4k" ? "4K" : m.Value;
            }
            if (res.Length > 0) parts.Add(res);
            string codec = cf.Codec;
            if (codec.Length == 0)
            {
                if (Regex.IsMatch(n, @"x265|hevc|h\.?265")) codec = "HEVC";
                else if (Regex.IsMatch(n, @"x264|h\.?264|avc")) codec = "H.264";
                else if (Regex.IsMatch(n, @"\bav1\b")) codec = "AV1";
                else if (Regex.IsMatch(n, @"xvid|divx")) codec = "MPEG-4";
            }
            if (codec.Length > 0) parts.Add(codec);
            if (Regex.IsMatch(n, @"\b(hdr|hdr10|dv|dolby.?vision)\b")) parts.Add("HDR");
            if (Regex.IsMatch(n, @"10.?bit")) parts.Add("10-bit");
            if (Regex.IsMatch(n, @"\b(cam|hdcam|camrip|ts|telesync|hdts)\b")) parts.Add("CAM (low)");
            else if (Regex.IsMatch(n, @"blu.?ray|bdrip|brrip|\bbd\b|remux")) parts.Add("BluRay");
            else if (Regex.IsMatch(n, @"web.?dl|webrip|\bweb\b")) parts.Add("WEB");
            else if (Regex.IsMatch(n, @"hdtv")) parts.Add("HDTV");
            else if (Regex.IsMatch(n, @"dvd")) parts.Add("DVD");
            return string.Join(" \u00B7 ", parts);
        }

        public static string Dur(long s)
        {
            if (s <= 0) return "";
            long h = s / 3600, m = (s % 3600) / 60;
            return h > 0 ? h + "h " + m.ToString("00") + "m" : m + "m " + (s % 60).ToString("00") + "s";
        }

        public static string SizeText(long b)
        {
            if (b >= 1L << 30) return (b / 1073741824.0).ToString("0.0") + " GB";
            if (b >= 1L << 20) return (b / 1048576.0).ToString("0.0") + " MB";
            if (b >= 1024) return (b / 1024.0).ToString("0") + " KB";
            return b + " B";
        }

        // ------------------------------------------------------------ launcher integration
        public static void Merge(List<Item> list)
        {
            List<CatVolume> vols;
            lock (Sync) vols = Vols.Values.ToList();
            if (vols.Count == 0) return;
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in list) have.Add(it.Path);
            var vid = new Dictionary<string, CatFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in vols)
                if (v.Online) foreach (var f in v.Files) if (f.Type == "Video" && f.Quality.Length > 0) vid[v.Root + f.Rel] = f;
            foreach (var it in list)
            {
                CatFile f;
                if (it.Kind == "video" && vid.TryGetValue(it.Path, out f))
                {
                    if (it.BaseSub == null) it.BaseSub = it.Sub;
                    it.Sub = VideoTag(f) + "  \u00B7  " + it.BaseSub;
                    Engine.Refinish(it);
                }
            }
            foreach (var v in vols)
            {
                foreach (var f in v.Files)
                {
                    string cat;
                    switch (f.Type)
                    {
                        case "Video": cat = "Movies"; break;
                        case "eBook": cat = "Books"; break;
                        case "Comic": cat = f.Rel.IndexOf("\\Manga\\", StringComparison.OrdinalIgnoreCase) >= 0 ? "Manga" : "Comics"; break;
                        case "PDF": case "Document": case "Spreadsheet": case "Presentation": cat = "Docs"; break;
                        default: continue;
                    }
                    string path = v.Root + f.Rel;
                    if (!have.Add(path)) continue;
                    var it = new Item();
                    it.Path = path; it.Ext = f.Ext; it.Cat = cat; it.Mode = "catalog"; it.Modified = f.Mod;
                    it.Name = SStem(f.Rel);
                    if (cat == "Manga" || cat == "Comics") { it.Tags = it.Name.ToLowerInvariant(); it.Name = Engine.SeriesTitle(it.Name); }
                    it.Offline = !v.Online;
                    it.Where = v.Display;
                    string parent = SDir(path);
                    string tag = f.Type == "Video" ? VideoTag(f) : SizeText(f.Size);
                    it.Sub = tag + "  \u00B7  " + (it.Offline ? v.Display + " \u2014 not connected  \u00B7  " : "") + Engine.Pretty(parent);
                    Engine.Refinish(it);
                    list.Add(it);
                }
            }
        }

        public static string VideoTag(CatFile f)
        {
            var p = new List<string>();
            if (f.Quality.Length > 0) p.Add(f.Quality);
            if (f.DurSec > 0) p.Add(Dur(f.DurSec));
            p.Add(SizeText(f.Size));
            return string.Join("  \u00B7  ", p);
        }

        // A file that isn't where the index says: look for the same name (and size, when known) on the connected drives.
        public static string FindMoved(string oldPath, bool isDir)
        {
            if (string.IsNullOrEmpty(oldPath)) return null;
            string name = SFile(oldPath.TrimEnd('\\'));
            long size = -1;
            List<CatVolume> vols; lock (Sync) vols = Vols.Values.ToList();
            var ov = VolumeOfPath(oldPath);
            if (ov != null && oldPath.Length > 3)
            {
                string rel = oldPath.Substring(3);
                var f = ov.Files.FirstOrDefault(x => x.Rel.Equals(rel, StringComparison.OrdinalIgnoreCase));
                if (f != null) size = f.Size;
            }
            string best = null; int bestScore = -1;
            foreach (var v in vols.Where(x => x.Online))
            {
                foreach (var f in v.Files)
                {
                    string cand;
                    if (isDir)
                    {
                        string d = SDir(f.Rel);
                        if (!SFile(d).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                        cand = v.Root + d;
                    }
                    else
                    {
                        if (!SFile(f.Rel).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                        cand = v.Root + f.Rel;
                    }
                    if (cand.Equals(oldPath, StringComparison.OrdinalIgnoreCase)) continue;
                    int score = (size >= 0 && !isDir && f.Size == size ? 10 : 0) + (cand.Substring(1).Equals(oldPath.Substring(1), StringComparison.OrdinalIgnoreCase) ? 5 : 0);
                    if (score > bestScore && (isDir ? Directory.Exists(cand) : File.Exists(cand))) { best = cand; bestScore = score; }
                }
            }
            return best;
        }

        public static CatVolume VolumeOfPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 3) return null;
            string letter = path.Substring(0, 1);
            lock (Sync) return Vols.Values.FirstOrDefault(v => v.Letter.Equals(letter, StringComparison.OrdinalIgnoreCase) && v.Online)
                               ?? Vols.Values.FirstOrDefault(v => v.Letter.Equals(letter, StringComparison.OrdinalIgnoreCase));
        }

        public static string Summary()
        {
            lock (Sync)
            {
                if (Vols.Count == 0) return "";
                int on = Vols.Values.Count(v => v.Online);
                long files = Vols.Values.Sum(v => (long)v.Files.Count);
                return files.ToString("N0") + " files catalogued on " + Vols.Count + " drive" + (Vols.Count == 1 ? "" : "s") + (on < Vols.Count ? " (" + (Vols.Count - on) + " offline)" : "");
            }
        }

        // ------------------------------------------------------------ xlsx export
        public static void Export(string path)
        {
            List<CatVolume> vols;
            lock (Sync) vols = Vols.Values.OrderBy(v => v.Letter).ToList();
            string tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var fs = new FileStream(tmp, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var sheets = new List<string> { "Drives", "Summary", "Videos", "All files" };
                Put(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                    string.Join("", sheets.Select((s, i) => "<Override PartName=\"/xl/worksheets/sheet" + (i + 1) + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")) +
                    "</Types>");
                Put(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Put(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" +
                    string.Join("", sheets.Select((s, i) => "<sheet name=\"" + s + "\" sheetId=\"" + (i + 1) + "\" r:id=\"rId" + (i + 1) + "\"/>")) +
                    "</sheets>" +
                    "</workbook>");
                Put(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    string.Join("", sheets.Select((s, i) => "<Relationship Id=\"rId" + (i + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet" + (i + 1) + ".xml\"/>")) +
                    "<Relationship Id=\"rId" + (sheets.Count + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                Put(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.0\"/></numFmts>" +
                    "<fonts count=\"3\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font><font><sz val=\"11\"/><color rgb=\"FF9E9E9E\"/><name val=\"Calibri\"/></font></fonts>" +
                    "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F3A5F\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                    "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"4\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                    "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
                    "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                    "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                    "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");

                // 1. Drives
                var rows = new List<object[]>();
                foreach (var v in vols)
                {
                    long used = v.Files.Sum(f => f.Size);
                    rows.Add(new object[] { v.Nick + (v.Label.Length > 0 ? " (" + v.Label + ")" : ""), v.Letter + ":", v.Online ? "Connected" : "Not connected", v.DriveType, v.Id,
                        Gb(v.Total), Gb(v.Total - v.Free), Gb(v.Free), (double)v.Files.Count, Gb(used),
                        (double)v.Files.Count(f => f.Type == "Video"), (double)v.Files.Count(f => f.Type == "eBook" || f.Type == "Comic"),
                        (double)v.Files.Count(f => f.Type == "Image"), v.Scanned == DateTime.MinValue ? "" : v.Scanned.ToString("yyyy-MM-dd HH:mm") });
                }
                Sheet(zip, 1, new[] { "Drive", "Letter", "Status", "Type", "Volume serial", "Capacity (GB)", "Used (GB)", "Free (GB)", "Files catalogued", "Catalogued size (GB)", "Videos", "Books & comics", "Images", "Last scanned" },
                      new[] { 18, 8, 15, 20, 15, 14, 12, 12, 16, 20, 10, 15, 10, 18 }, rows);

                // 2. Summary by drive & type
                rows = new List<object[]>();
                foreach (var v in vols)
                    foreach (var g in v.Files.GroupBy(f => f.Type).OrderByDescending(g => g.Sum(f => f.Size)))
                        rows.Add(new object[] { v.Display, g.Key, (double)g.Count(), Gb(g.Sum(f => f.Size)), v.Online ? "Connected" : "Not connected" });
                Sheet(zip, 2, new[] { "Drive", "Type", "Files", "Size (GB)", "Status" }, new[] { 22, 16, 10, 12, 15 }, rows);

                // 3. Videos
                rows = new List<object[]>();
                foreach (var v in vols)
                    foreach (var f in v.Files.Where(f => f.Type == "Video"))
                        rows.Add(new object[] { SStem(f.Rel), f.Quality, f.W > 0 ? f.W + "\u00D7" + f.H : "",
                            f.Codec, Dur(f.DurSec), f.Kbps > 0 ? (double)Math.Round(f.Kbps / 1000.0, 1) : (object)"", Gb(f.Size), f.Ext.TrimStart('.').ToUpperInvariant(),
                            v.Display, v.Online ? "Connected" : "Not connected", Folder(v, f), new DateTime(f.Mod).ToString("yyyy-MM-dd") });
                Sheet(zip, 3, new[] { "Title / file", "Quality", "Resolution", "Codec", "Duration", "Bitrate (Mbps)", "Size (GB)", "Format", "Drive", "Status", "Folder", "Modified" },
                      new[] { 60, 26, 12, 9, 10, 13, 10, 8, 20, 14, 70, 11 }, rows);

                // 4. All files
                rows = new List<object[]>();
                foreach (var v in vols)
                    foreach (var f in v.Files)
                        rows.Add(new object[] { SFile(f.Rel), f.Type, f.Ext.TrimStart('.').ToUpperInvariant(), Mb(f.Size),
                            f.Type == "Video" ? f.Quality : "", v.Display, v.Online ? "Connected" : "Not connected", Folder(v, f),
                            new DateTime(f.Mod).ToString("yyyy-MM-dd"), f.Cloud ? "OneDrive cloud-only" : "" });
                Sheet(zip, 4, new[] { "Name", "Type", "Format", "Size (MB)", "Quality (video)", "Drive", "Status", "Folder", "Modified", "Note" },
                      new[] { 55, 13, 8, 11, 24, 20, 14, 80, 11, 18 }, rows);
            }
            if (File.Exists(path))
            {
                try { File.Delete(path); }
                catch { path = System.IO.Path.Combine(Dir, "Drive Catalog (latest - close Excel to update the main one).xlsx"); if (File.Exists(path)) File.Delete(path); }
            }
            File.Move(tmp, path);
        }

        static string Folder(CatVolume v, CatFile f) { return v.Root + SDir(f.Rel); }
        // long-path-safe helpers (System.IO.Path throws on paths over 260 chars in .NET Framework)
        public static string SFile(string p) { int i = p.LastIndexOf('\\'); return i >= 0 ? p.Substring(i + 1) : p; }
        public static string SDir(string p) { int i = p.LastIndexOf('\\'); return i > 0 ? p.Substring(0, i) : ""; }
        public static string SStem(string p) { string f = SFile(p); int i = f.LastIndexOf('.'); return i > 0 ? f.Substring(0, i) : f; }
        static double Gb(long b) { return Math.Round(b / 1073741824.0, 1); }
        static double Mb(long b) { return Math.Round(b / 1048576.0, 1); }

        static void Put(ZipArchive zip, string name, string text)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(text);
        }

        static string Col(int i) { string s = ""; i++; while (i > 0) { int m = (i - 1) % 26; s = (char)('A' + m) + s; i = (i - 1) / 26; } return s; }

        static string X(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '&') sb.Append("&amp;"); else if (c == '<') sb.Append("&lt;"); else if (c == '>') sb.Append("&gt;"); else if (c == '"') sb.Append("&quot;");
                else if (c < 0x20 && c != '\t') sb.Append(' ');
                else if (char.IsSurrogate(c)) sb.Append(c);
                else if (c == 0xFFFE || c == 0xFFFF) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static void Sheet(ZipArchive zip, int idx, string[] head, int[] widths, List<object[]> rows)
        {
            var e = zip.CreateEntry("xl/worksheets/sheet" + idx + ".xml", CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false)))
            {
                string last = Col(head.Length - 1) + (rows.Count + 1);
                w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
                w.Write("<dimension ref=\"A1:" + last + "\"/>");
                w.Write("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
                w.Write("<cols>");
                for (int i = 0; i < widths.Length; i++) w.Write("<col min=\"" + (i + 1) + "\" max=\"" + (i + 1) + "\" width=\"" + widths[i] + "\" customWidth=\"1\"/>");
                w.Write("</cols><sheetData>");
                w.Write("<row r=\"1\">");
                for (int i = 0; i < head.Length; i++) w.Write("<c r=\"" + Col(i) + "1\" s=\"1\" t=\"inlineStr\"><is><t>" + X(head[i]) + "</t></is></c>");
                w.Write("</row>");
                int r = 2;
                foreach (var row in rows)
                {
                    w.Write("<row r=\"" + r + "\">");
                    for (int i = 0; i < row.Length; i++)
                    {
                        var o = row[i];
                        string refc = Col(i) + r;
                        if (o is double)
                        {
                            double dv = (double)o;
                            bool frac = Math.Abs(dv - Math.Round(dv)) > 1e-9;
                            w.Write("<c r=\"" + refc + "\"" + (frac ? " s=\"2\"" : "") + "><v>" + dv.ToString(System.Globalization.CultureInfo.InvariantCulture) + "</v></c>");
                        }
                        else
                        {
                            string sv = o == null ? "" : o.ToString();
                            if (sv.Length == 0) continue;
                            bool dim = sv == "Not connected";
                            w.Write("<c r=\"" + refc + "\" t=\"inlineStr\"" + (dim ? " s=\"3\"" : "") + "><is><t xml:space=\"preserve\">" + X(sv) + "</t></is></c>");
                        }
                    }
                    w.Write("</row>");
                    r++;
                }
                w.Write("</sheetData>");
                w.Write("<autoFilter ref=\"A1:" + last + "\"/>");
                w.Write("</worksheet>");
            }
        }
    }

    // ---------------------------------------------------------------- container header parser (MKV / MP4 / MOV / AVI)
    public static class MediaHeader
    {
        public static bool Probe(string path, CatFile cf)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536))
                {
                    var head = new byte[16];
                    if (fs.Read(head, 0, 16) < 12) return false;
                    fs.Position = 0;
                    if (head[0] == 0x1A && head[1] == 0x45 && head[2] == 0xDF && head[3] == 0xA3) return Mkv(fs, cf);
                    if (head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' && head[8] == 'A' && head[9] == 'V' && head[10] == 'I') return Avi(fs, cf);
                    string t = Encoding.ASCII.GetString(head, 4, 4);
                    if (t == "ftyp" || t == "moov" || t == "mdat" || t == "wide" || t == "free" || t == "skip") return Mp4(fs, cf);
                }
            }
            catch { }
            return false;
        }

        static byte[] ReadSome(FileStream fs, long pos, int max)
        {
            fs.Position = pos;
            int n = (int)Math.Min(max, Math.Max(0, fs.Length - pos));
            var buf = new byte[n]; int got = 0;
            while (got < n) { int r = fs.Read(buf, got, n - got); if (r <= 0) break; got += r; }
            if (got < n) Array.Resize(ref buf, got);
            return buf;
        }

        // ---- Matroska
        static long VintId(byte[] b, ref int p)
        {
            if (p >= b.Length) return -1;
            int first = b[p], len = 1, mask = 0x80;
            while (len <= 4 && (first & mask) == 0) { mask >>= 1; len++; }
            if (len > 4 || p + len > b.Length) return -1;
            long v = 0; for (int i = 0; i < len; i++) v = (v << 8) | b[p + i];
            p += len; return v;
        }
        static long VintSize(byte[] b, ref int p)
        {
            if (p >= b.Length) return -1;
            int first = b[p], len = 1, mask = 0x80;
            while (len <= 8 && (first & mask) == 0) { mask >>= 1; len++; }
            if (len > 8 || p + len > b.Length) return -1;
            long v = first & (mask - 1); bool allOnes = v == mask - 1;
            for (int i = 1; i < len; i++) { v = (v << 8) | b[p + i]; if (b[p + i] != 0xFF) allOnes = false; }
            p += len; return allOnes ? long.MaxValue : v;
        }
        static ulong UInt(byte[] b, int p, long n) { ulong v = 0; for (int i = 0; i < n && p + i < b.Length; i++) v = (v << 8) | b[p + i]; return v; }
        static double Float(byte[] b, int p, long n)
        {
            var t = new byte[n]; Array.Copy(b, p, t, 0, (int)n); Array.Reverse(t);
            return n == 4 ? BitConverter.ToSingle(t, 0) : n == 8 ? BitConverter.ToDouble(t, 0) : 0;
        }

        class MkvState { public ulong Scale = 1000000; public double Dur; public int W, H; public string Codec = ""; public bool Done; public long TrackType; public string TrackCodec = ""; public int TW, TH; }

        static bool Mkv(FileStream fs, CatFile cf)
        {
            var b = ReadSome(fs, 0, 4 * 1024 * 1024);
            var st = new MkvState();
            Walk(b, 0, b.Length, st, 0);
            if (st.W <= 0 && st.Dur <= 0) return false;
            cf.W = st.W; cf.H = st.H;
            if (st.Dur > 0) cf.DurSec = (long)(st.Dur * st.Scale / 1e9);
            cf.Codec = MkvCodec(st.Codec);
            return true;
        }

        static void Walk(byte[] b, int start, int end, MkvState st, int depth)
        {
            int p = start;
            while (p < end && !st.Done && depth < 8)
            {
                int at = p;
                long id = VintId(b, ref p); if (id < 0) return;
                long size = VintSize(b, ref p); if (size < 0) return;
                int dataEnd = size == long.MaxValue || p + size > end ? end : (int)(p + size);
                bool fits = size != long.MaxValue && p + size <= b.Length;
                switch (id)
                {
                    case 0x18538067: case 0x1654AE6B: case 0x1549A966: case 0xE0:   // Segment, Tracks, Info, Video
                        Walk(b, p, dataEnd, st, depth + 1); break;
                    case 0xAE:                                                      // TrackEntry
                        st.TrackType = 0; st.TrackCodec = ""; st.TW = 0; st.TH = 0;
                        Walk(b, p, dataEnd, st, depth + 1);
                        if ((st.TrackType == 1 || st.TW > 0) && st.W == 0) { st.W = st.TW; st.H = st.TH; st.Codec = st.TrackCodec; }
                        break;
                    case 0x83: if (fits) st.TrackType = (long)UInt(b, p, size); break;
                    case 0x86: if (fits) st.TrackCodec = Encoding.ASCII.GetString(b, p, (int)size).TrimEnd('\0'); break;
                    case 0xB0: if (fits) st.TW = (int)UInt(b, p, size); break;
                    case 0xBA: if (fits) st.TH = (int)UInt(b, p, size); break;
                    case 0x2AD7B1: if (fits) st.Scale = UInt(b, p, size); break;
                    case 0x4489: if (fits) st.Dur = Float(b, p, size); break;
                    case 0x1F43B675: st.Done = true; return;                         // Cluster: headers are over
                }
                if (size == long.MaxValue) { if (id != 0x18538067) return; else return; }
                if (p + size > end) return;
                p = (int)(p + size);
                if (p <= at) return;
            }
        }

        static string MkvCodec(string id)
        {
            if (id.StartsWith("V_MPEGH/ISO/HEVC")) return "HEVC";
            if (id.StartsWith("V_MPEG4/ISO/AVC")) return "H.264";
            if (id.StartsWith("V_AV1")) return "AV1";
            if (id.StartsWith("V_VP9")) return "VP9";
            if (id.StartsWith("V_VP8")) return "VP8";
            if (id.StartsWith("V_MPEG4")) return "MPEG-4";
            if (id.StartsWith("V_MPEG2") || id.StartsWith("V_MPEG1")) return "MPEG-2";
            if (id.StartsWith("V_MS/VFW")) return "VfW";
            return id.Replace("V_", "");
        }

        // ---- MP4 / MOV
        static uint BE32(byte[] b, int p) { return (uint)(b[p] << 24 | b[p + 1] << 16 | b[p + 2] << 8 | b[p + 3]); }
        static ulong BE64(byte[] b, int p) { return ((ulong)BE32(b, p) << 32) | BE32(b, p + 4); }

        static bool Mp4(FileStream fs, CatFile cf)
        {
            long pos = 0, len = fs.Length;
            var h = new byte[16];
            for (int guard = 0; guard < 200 && pos + 8 <= len; guard++)
            {
                fs.Position = pos;
                if (fs.Read(h, 0, 16) < 8) return false;
                long size = BE32(h, 0); string type = Encoding.ASCII.GetString(h, 4, 4); int hdr = 8;
                if (size == 1) { size = (long)BE64(h, 8); hdr = 16; }
                else if (size == 0) size = len - pos;
                if (size < hdr) return false;
                if (type == "moov")
                {
                    var b = ReadSome(fs, pos + hdr, (int)Math.Min(size - hdr, 32L * 1024 * 1024));
                    var r = new Mp4Info();
                    Boxes(b, 0, b.Length, r, 0);
                    if (r.W <= 0 && r.Dur <= 0) return false;
                    cf.W = r.W; cf.H = r.H; cf.Codec = VideoProps.CodecName(r.Four);
                    if (r.Scale > 0) cf.DurSec = (long)(r.Dur / r.Scale);
                    cf.Created = r.Created;
                    return true;
                }
                pos += size;
            }
            return false;
        }

        class Mp4Info { public DateTime Created; public double Dur, Scale; public int W, H; public string Four = ""; public string Handler = ""; public int TW, TH; public string TFour = ""; }

        static void Boxes(byte[] b, int start, int end, Mp4Info r, int depth)
        {
            int p = start;
            while (p + 8 <= end && depth < 10)
            {
                long size = BE32(b, p); string type = Encoding.ASCII.GetString(b, p + 4, 4); int hdr = 8;
                if (size == 1 && p + 16 <= end) { size = (long)BE64(b, p + 8); hdr = 16; }
                else if (size == 0) size = end - p;
                if (size < hdr || p + size > end) return;
                int d = p + hdr, e = (int)(p + size);
                switch (type)
                {
                    case "trak":
                        r.Handler = ""; r.TW = 0; r.TH = 0; r.TFour = "";
                        Boxes(b, d, e, r, depth + 1);
                        if (r.Handler == "vide" && r.W == 0) { r.W = r.TW; r.H = r.TH; r.Four = r.TFour; }
                        break;
                    case "mdia": case "minf": case "stbl":
                        Boxes(b, d, e, r, depth + 1); break;
                    case "mvhd":
                        if (e - d >= 32)
                        {
                            ulong secs;
                            if (b[d] == 1) { r.Scale = BE32(b, d + 20); r.Dur = BE64(b, d + 24); secs = BE64(b, d + 4); }
                            else { r.Scale = BE32(b, d + 12); r.Dur = BE32(b, d + 16); secs = BE32(b, d + 4); }
                            if (secs > 2082844800UL && secs < 6000000000UL) r.Created = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(secs).ToLocalTime();
                        }
                        break;
                    case "tkhd":
                        if (e - d >= 84) { if (r.TW == 0) { r.TW = (int)(BE32(b, e - 8) >> 16); r.TH = (int)(BE32(b, e - 4) >> 16); } }
                        break;
                    case "hdlr":
                        if (e - d >= 12 && r.Handler.Length == 0) r.Handler = Encoding.ASCII.GetString(b, d + 8, 4);
                        break;
                    case "stsd":
                        if (e - d >= 16 + 36)
                        {
                            int en = d + 8;
                            r.TFour = Encoding.ASCII.GetString(b, en + 4, 4);
                            int w = (b[en + 32] << 8) | b[en + 33], hh = (b[en + 34] << 8) | b[en + 35];
                            if (w > 0 && hh > 0) { r.TW = w; r.TH = hh; }
                        }
                        break;
                }
                p = e;
            }
        }

        // ---- AVI
        static int Find(byte[] b, string s, int from)
        {
            var k = Encoding.ASCII.GetBytes(s);
            for (int i = from; i <= b.Length - k.Length; i++)
            {
                int j = 0; while (j < k.Length && b[i + j] == k[j]) j++;
                if (j == k.Length) return i;
            }
            return -1;
        }
        static int LE32(byte[] b, int p) { return b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24; }

        static bool Avi(FileStream fs, CatFile cf)
        {
            var b = ReadSome(fs, 0, 256 * 1024);
            int a = Find(b, "avih", 0);
            if (a < 0 || a + 8 + 40 > b.Length) return false;
            int d = a + 8;
            long usPerFrame = (uint)LE32(b, d), frames = (uint)LE32(b, d + 16);
            cf.W = LE32(b, d + 32); cf.H = LE32(b, d + 36);
            if (usPerFrame > 0 && frames > 0) cf.DurSec = (long)(usPerFrame * frames / 1000000.0);
            int s = Find(b, "vids", a);
            if (s >= 0 && s + 8 <= b.Length) cf.Codec = VideoProps.CodecName(Encoding.ASCII.GetString(b, s + 4, 4));
            int f = s >= 0 ? Find(b, "strf", s) : -1;
            if ((cf.Codec.Length == 0 || cf.Codec.Trim().Length < 3) && f >= 0 && f + 8 + 20 <= b.Length) cf.Codec = VideoProps.CodecName(Encoding.ASCII.GetString(b, f + 8 + 16, 4));
            return cf.W > 0;
        }
    }

    // ---------------------------------------------------------------- Windows property store (video resolution, duration, codec)
    public static class VideoProps
    {
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct PROPERTYKEY { public Guid fmtid; public uint pid; public PROPERTYKEY(Guid g, uint p) { fmtid = g; pid = p; } }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        struct PROPVARIANT
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr p;
            [FieldOffset(8)] public uint ui4;
            [FieldOffset(8)] public ulong ui8;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint c);
            [PreserveSig] int GetAt(uint i, out PROPERTYKEY key);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
            [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
            [PreserveSig] int Commit();
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr pbc, int flags, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pv);

        static readonly Guid VideoFmt = new Guid("64440491-4C8B-11D1-8B70-080036B11A03");
        static readonly Guid MediaFmt = new Guid("64440490-4C8B-11D1-8B70-080036B11A03");

        public static void Probe(string path, CatFile cf)
        {
            IPropertyStore store = null;
            try
            {
                Guid iid = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, ref iid, out store) != 0 || store == null) return;
                cf.W = (int)Num(store, VideoFmt, 3);
                cf.H = (int)Num(store, VideoFmt, 4);
                long br = Num(store, VideoFmt, 43); if (br <= 0) br = Num(store, VideoFmt, 8);
                cf.Kbps = (int)(br / 1000);
                long dur = Num(store, MediaFmt, 3);
                cf.DurSec = dur / 10000000;
                cf.Codec = CodecName(Str(store, VideoFmt, 10));
            }
            catch (Exception ex) { Log.Write("probe " + path + ": " + ex.Message); }
            finally { if (store != null) Marshal.ReleaseComObject(store); }
        }

        static long Num(IPropertyStore s, Guid fmt, uint pid)
        {
            var k = new PROPERTYKEY(fmt, pid); PROPVARIANT pv;
            if (s.GetValue(ref k, out pv) != 0) return 0;
            long r = 0;
            switch (pv.vt) { case 19: case 3: r = pv.ui4; break; case 21: case 20: r = (long)pv.ui8; break; case 18: case 2: r = pv.ui4 & 0xFFFF; break; }
            PropVariantClear(ref pv);
            return r;
        }

        static string Str(IPropertyStore s, Guid fmt, uint pid)
        {
            var k = new PROPERTYKEY(fmt, pid); PROPVARIANT pv;
            if (s.GetValue(ref k, out pv) != 0) return "";
            string r = pv.vt == 31 && pv.p != IntPtr.Zero ? Marshal.PtrToStringUni(pv.p) : "";
            PropVariantClear(ref pv);
            return r ?? "";
        }

        public static string CodecName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string four = raw;
            var m = Regex.Match(raw, @"^\{?([0-9A-Fa-f]{8})-");
            if (m.Success)
            {
                uint v = Convert.ToUInt32(m.Groups[1].Value, 16);
                var sb = new StringBuilder();
                for (int i = 0; i < 4; i++) { char c = (char)((v >> (8 * i)) & 0xFF); if (c >= 32 && c < 127) sb.Append(c); }
                four = sb.ToString();
            }
            switch (four.Trim().ToUpperInvariant())
            {
                case "H264": case "AVC1": case "AVC3": case "X264": return "H.264";
                case "HEVC": case "H265": case "HVC1": case "HEV1": case "X265": return "HEVC";
                case "VP90": case "VP9": case "VP09": return "VP9";
                case "AV01": return "AV1";
                case "MP4V": case "XVID": case "DIVX": case "DX50": case "FMP4": return "MPEG-4";
                case "WVC1": case "WMV3": return "VC-1";
                case "MPG2": case "MPEG": return "MPEG-2";
                case "MJPG": return "MJPEG";
                default: return four.Length == 4 ? four.ToUpperInvariant() : "";
            }
        }
    }
}
