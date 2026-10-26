// Desktop Organizer - "Sort new files": looks in Downloads (and other inbox folders), works out what each new file is,
// proposes a proper name and home, moves it on confirmation and refreshes the catalogue.
// (appended after Core.cs at build time; shares its using block)
namespace DesktopOrganizer
{
    public class InboxOp { public string From, To; public bool IsDir; }

    public class InboxItem : INotifyPropertyChanged
    {
        bool sel; string status = "";
        public bool Selected { get { return sel; } set { sel = value; Raise("Selected"); } }
        public string Name { get; set; }
        public string SizeText { get; set; }
        public string Detected { get; set; }
        public string NewName { get; set; }
        public string DestFolder { get; set; }
        public string Status { get { return status; } set { status = value; Raise("Status"); } }
        public string Source { get; set; }
        public string Glyph { get; set; }
        public string Color { get; set; }
        public bool CanMove { get; set; }
        public long Size;
        public List<InboxOp> Ops = new List<InboxOp>();
        public event PropertyChangedEventHandler PropertyChanged;
        void Raise(string n) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(n)); }
    }

    public static class Inbox
    {
        static readonly string[] Partial = { ".!ut", ".!qb", ".part", ".crdownload", ".tmp", ".download", ".opdownload", ".partial", ".bc!", ".aria2" };
        static readonly string[] SkipNames = { "desktop.ini", "thumbs.db", ".ds_store" };
        static readonly string[] DocWords = { "invoice", "receipt", "statement", "bill", "ticket", "booking", "itinerary", "boarding", "resume", "cv", "offer",
            "letter", "form", "application", "aadhaar", "aadhar", "pan", "passport", "visa", "policy", "insurance", "salary", "payslip", "tax", "itr", "certificate",
            "report", "agreement", "contract", "quotation", "warranty", "manual", "challan", "order", "confirmation" };
        static readonly Dictionary<string, string> Publishers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "Penguin Random House", "PRH" }, { "Random House", "PRH" }, { "Penguin Books", "Penguin" }, { "Penguin", "Penguin" }, { "HarperCollins", "Harper" },
            { "Harper", "Harper" }, { "Simon & Schuster", "S&S" }, { "Simon and Schuster", "S&S" }, { "Hachette", "Hachette" }, { "Little, Brown", "Little Brown" },
            { "Pan Macmillan", "Macmillan" }, { "Macmillan", "Macmillan" }, { "Bloomsbury", "Bloomsbury" }, { "Scholastic", "Scholastic" },
            { "Dorling Kindersley", "DK" }, { "DK", "DK" }, { "O'Reilly", "OReilly" }, { "Wiley", "Wiley" }, { "Vintage", "Vintage" }, { "Bantam", "Bantam" },
            { "Tor", "Tor" }, { "Orbit", "Orbit" }, { "Gollancz", "Gollancz" }, { "Del Rey", "Del Rey" }, { "Ballantine", "Ballantine" }, { "Knopf", "Knopf" },
            { "Doubleday", "Doubleday" }, { "Faber", "Faber" }, { "Picador", "Picador" }, { "Rupa", "Rupa" }, { "Westland", "Westland" }, { "Viz", "Viz" },
            { "Kodansha", "Kodansha" }, { "Yen Press", "Yen" }, { "Seven Seas", "Seven Seas" }, { "Marvel", "Marvel" }, { "DC Comics", "DC" } };

        public static string ConfigFile { get { return System.IO.Path.Combine(Catalog.Dir ?? Program.DataDir, "inbox-folders.json"); } }
        public static string LogFile { get { return System.IO.Path.Combine(Catalog.Dir ?? Program.DataDir, "sorted-files-log.txt"); } }

        // ------------------------------------------------------------ where to look
        public static List<string> Folders()
        {
            var r = new List<string>();
            string home = Environment.GetEnvironmentVariable("USERPROFILE") ?? "";
            r.Add(System.IO.Path.Combine(home, "Downloads"));
            r.Add(System.IO.Path.Combine(home, "OneDrive", "Desktop"));
            r.Add(System.IO.Path.Combine(home, "Desktop"));
            string ut = TorrentDir(); if (ut != null) r.Add(ut);
            try
            {
                if (!File.Exists(ConfigFile))
                    File.WriteAllText(ConfigFile, "{\r\n  \"_help\": \"Extra folders where new files land (for example where big torrents finish). Downloads, Desktop and your uTorrent folder are always checked.\",\r\n  \"folders\": [],\r\n  \"sortWithoutAsking\": false\r\n}\r\n", new UTF8Encoding(false));
                var js = new System.Web.Script.Serialization.JavaScriptSerializer();
                var cfg = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigFile));
                object f; if (cfg.TryGetValue("folders", out f) && f is System.Collections.IEnumerable)
                    foreach (var x in (System.Collections.IEnumerable)f) r.Add(Environment.ExpandEnvironmentVariables(Convert.ToString(x)));
            }
            catch (Exception ex) { Log.Write("inbox config: " + ex.Message); }
            return r.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static bool SortWithoutAsking()
        {
            try { return Regex.IsMatch(File.ReadAllText(ConfigFile), "\"sortWithoutAsking\"\\s*:\\s*true"); } catch { return false; }
        }

        static string TorrentDir()
        {
            try
            {
                string f = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "uTorrent", "settings.dat");
                if (!File.Exists(f)) return null;
                string s = Encoding.GetEncoding("ISO-8859-1").GetString(File.ReadAllBytes(f));
                foreach (var key in new[] { "dir_completed_download", "dir_active_download" })
                {
                    int i = s.IndexOf(key, StringComparison.Ordinal); if (i < 0) continue;
                    var m = Regex.Match(s.Substring(i + key.Length), @"^(\d+):");
                    if (!m.Success) continue;
                    int len = int.Parse(m.Groups[1].Value);
                    string dir = Encoding.UTF8.GetString(Encoding.GetEncoding("ISO-8859-1").GetBytes(s.Substring(i + key.Length + m.Length, len)));
                    if (Directory.Exists(dir)) return dir;
                }
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------ drives
        // "media" drive (films, TV, anime, photos, videos) and "library" drive (books, comics, magazines, documents):
        // settings.json mediaDrive / libraryDrive (a letter or a volume label), else the largest and the second-largest external drive.
        static CatVolume Drive(bool big)
        {
            lock (Catalog.Sync)
            {
                var ext = Catalog.Vols.Values.Where(v => v.Online && !v.DriveType.StartsWith("System")).OrderByDescending(v => v.Total).ToList();
                string want = big ? Settings.MediaDrive : Settings.LibraryDrive;
                if (want.Length > 0)
                {
                    var m = ext.FirstOrDefault(v => v.Letter.Equals(want.TrimEnd(':', '\\'), StringComparison.OrdinalIgnoreCase) || v.Label.Equals(want, StringComparison.OrdinalIgnoreCase));
                    if (m != null) return m;
                }
                if (ext.Count == 0) return null;
                return big || ext.Count == 1 ? ext[0] : ext[1];
            }
        }
        internal static string BigName() { var v = Drive(true); return v != null ? v.Nick : "media drive"; }
        internal static string SmallName() { var v = Drive(false); return v != null ? v.Nick : "library drive"; }
        static string BigRoot() { var v = Drive(true); return v != null ? v.Root : null; }
        static string SmallRoot() { var v = Drive(false); return v != null ? v.Root : null; }

        // ------------------------------------------------------------ scan
        public static int QuickCount()
        {
            int n = 0;
            foreach (var f in Folders())
                try { foreach (var e in new DirectoryInfo(f).GetFileSystemInfos()) if (Candidate(e)) n++; } catch { }
            return n;
        }

        static bool Candidate(FileSystemInfo e)
        {
            string ln = e.Name.ToLowerInvariant();
            if (Array.IndexOf(SkipNames, ln) >= 0 || ln.StartsWith("~$") || ln.StartsWith(".")) return false;
            if ((e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return false;
            if (Partial.Any(p => ln.EndsWith(p))) return false;
            if (((int)e.Attributes & (0x400000 | 0x40000 | 0x1000)) != 0) return false;     // OneDrive cloud-only
            if ((DateTime.Now - e.LastWriteTime).TotalMinutes < 3) return false;              // still arriving
            if (e is DirectoryInfo && ((DirectoryInfo)e).GetFileSystemInfos().Length == 0) return false;
            if (e is FileInfo)
            {
                string t = Catalog.TypeOf(e.Extension.ToLowerInvariant());
                if (t == "Shortcut" || t == "System/meta") return false;
            }
            return true;
        }

        public static List<InboxItem> Scan()
        {
            var list = new List<InboxItem>();
            foreach (var folder in Folders())
            {
                FileSystemInfo[] es;
                try { es = new DirectoryInfo(folder).GetFileSystemInfos(); } catch { continue; }
                foreach (var e in es)
                {
                    if (!Candidate(e)) continue;
                    InboxItem it;
                    try { it = e is DirectoryInfo ? ClassifyFolder((DirectoryInfo)e) : ClassifyFile((FileInfo)e, null); }
                    catch (Exception ex) { it = Leave(e, "Couldn't read it: " + ex.Message); }
                    it.Source = folder;
                    list.Add(it);
                }
            }
            return list.OrderBy(i => i.CanMove ? 0 : 1).ThenBy(i => i.Detected).ThenBy(i => i.Name).ToList();
        }

        static InboxItem Base(FileSystemInfo e, string detected, string glyph, string color)
        {
            var it = new InboxItem { Name = e.Name, Detected = detected, Glyph = glyph, Color = color };
            it.Size = e is FileInfo ? ((FileInfo)e).Length : DirSize((DirectoryInfo)e);
            it.SizeText = Catalog.SizeText(it.Size);
            return it;
        }
        static long DirSize(DirectoryInfo d) { try { return d.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch { return 0; } }

        static InboxItem Leave(FileSystemInfo e, string why)
        {
            var it = Base(e, e is DirectoryInfo ? "Folder" : Catalog.TypeOf(e.Extension.ToLowerInvariant()), "\uE8B7", "#90A4AE");
            it.NewName = ""; it.DestFolder = "Stays where it is"; it.Status = why; it.CanMove = false; it.Selected = false;
            return it;
        }

        static InboxItem Ready(InboxItem it, string newName, string destFolder, string needs)
        {
            it.NewName = newName; it.DestFolder = destFolder;
            if (destFolder == null) { it.DestFolder = "Waiting for the " + needs + " drive"; it.Status = "Plug in the " + needs + " drive to move this"; it.CanMove = false; it.Selected = false; }
            else { it.Status = "Ready"; it.CanMove = true; it.Selected = true; }
            return it;
        }

        static string Clean(string s)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), c == ':' ? " -" : "");
            return Regex.Replace(s, @"\s+", " ").Trim(' ', '.');
        }
        static string TitleCase(string s)
        {
            if (s.Length == 0 || s.Any(char.IsLower) && s.Any(char.IsUpper)) return s;
            return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
        }

        // ------------------------------------------------------------ files
        public static InboxItem ClassifyFile(FileInfo f, string eventHint)
        {
            string ext = f.Extension.ToLowerInvariant(), type = Catalog.TypeOf(ext), stem = System.IO.Path.GetFileNameWithoutExtension(f.Name);
            switch (type)
            {
                case "Video": return ClassifyVideo(f, eventHint);
                case "Image": return ClassifyPhoto(f, eventHint, "Photo");
                case "eBook": return ClassifyBook(f);
                case "Comic": return ClassifyComic(f);
                case "PDF": return Regex.IsMatch(f.Name, @"^Personal\s*-", RegexOptions.IgnoreCase) ? ClassifyDoc(f, "Personal document") : ClassifyPdf(f);
                case "Document": case "Spreadsheet": case "Presentation": return ClassifyDoc(f, type);
                case "Audio":
                {
                    var it = Base(f, "Music / audio", "\uE8D6", "#4FC3F7");
                    string big = BigRoot();
                    it.Ops.Add(new InboxOp { From = f.FullName, To = big == null ? null : System.IO.Path.Combine(big, "Media Server", "Music", f.Name) });
                    return Ready(it, f.Name, big == null ? null : System.IO.Path.Combine(big, "Media Server", "Music"), BigName());
                }
                case "Subtitle": return Leave(f, "Subtitle \u2014 moves with its video when they're in a folder together");
                case "Installer": return Leave(f, "Installer \u2014 run or delete it yourself");
                case "Archive": return Leave(f, "Archive \u2014 extract it first, then sort the contents");
                default: return Leave(f, "Not a type I file away automatically");
            }
        }

        static InboxItem ClassifyVideo(FileInfo f, string eventHint)
        {
            string stem = System.IO.Path.GetFileNameWithoutExtension(f.Name);
            bool cam = Regex.IsMatch(f.Name, @"^(IMG|VID|MOV|PXL|DJI|GOPR|GH0|GX0|DSC|MVI|MAH|VID-\d{8}-WA|WhatsApp Video|Screen ?Recording|\d{8}_\d{6})", RegexOptions.IgnoreCase);
            var tv = Regex.Match(stem.Replace('.', ' '), @"^(.*?)[\s\-_]*\b[Ss](\d{1,2})[Ee](\d{1,3})|^(.*?)[\s\-_]*\b(\d{1,2})x(\d{2})\b");
            if (cam || eventHint != null || (!tv.Success && f.Length < 150L * 1048576)) return ClassifyPhoto(f, eventHint, "Home video");
            string big = BigRoot();
            if (tv.Success)
            {
                string show = tv.Groups[1].Success && tv.Groups[1].Value.Length > 0 ? tv.Groups[1].Value : tv.Groups[4].Value;
                int season = int.Parse(tv.Groups[2].Success && tv.Groups[2].Value.Length > 0 ? tv.Groups[2].Value : tv.Groups[5].Value);
                int ep = int.Parse(tv.Groups[3].Success && tv.Groups[3].Value.Length > 0 ? tv.Groups[3].Value : tv.Groups[6].Value);
                string name; int year; MediaLib.ParseName(show, out name, out year);
                name = TitleCase(Clean(Regex.Replace(name, @"^\[[^\]]*\]\s*", "")));
                name = Regex.Replace(name, @"\s+(US|UK)$", "", RegexOptions.IgnoreCase);
                bool anime = IsAnime(name, f.Name);
                var it = Base(f, anime ? "Anime episode" : "TV episode", anime ? "\uE7FC" : "\uE7F4", anime ? "#BA68C8" : "#FFB74D");
                // naming: "Show [Year]\Show - 1x01 - Episode Title.ext" (titles from TVmaze)
                string epTitle; int showYear;
                string showName = TvLookup(name, season, ep, out showYear, out epTitle);
                if (year > 0 && showYear == 0) showYear = year;
                string folder = big == null ? null : ExistingShowFolder(big, showName, anime);
                if (folder == null && big != null) folder = System.IO.Path.Combine(big, "Media Server", anime ? "Anime\\Series" : "TV", SafeName(showName + (showYear > 0 ? " [" + showYear + "]" : "")));
                string newName = SafeName(showName + " - " + (season == 0 ? "Special " + ep : season + "x" + ep.ToString("00")) + (epTitle.Length > 0 ? " - " + epTitle : "")) + f.Extension.ToLowerInvariant();
                string dest = folder;
                it.Ops.Add(new InboxOp { From = f.FullName, To = dest == null ? null : System.IO.Path.Combine(dest, newName) });
                AddSidecars(it, f, dest, System.IO.Path.GetFileNameWithoutExtension(newName));
                return Ready(it, newName, dest, BigName());
            }
            else
            {
                string name; int year; MediaLib.ParseName(stem, out name, out year);
                name = TitleCase(Clean(Regex.Replace(name, @"^\[[^\]]*\]\s*", "")));
                bool anime = IsAnime(name, f.Name);
                string label = name + (year > 0 ? " (" + year + ")" : "");
                var it = Base(f, anime ? "Anime film" : "Movie", anime ? "\uE7FC" : "\uE8B2", anime ? "#BA68C8" : "#FF7A7A");
                string dest = big == null ? null : System.IO.Path.Combine(big, "Media Server", anime ? "Anime\\Movies" : "Movies", label);
                string newName = label + f.Extension.ToLowerInvariant();
                it.Ops.Add(new InboxOp { From = f.FullName, To = dest == null ? null : System.IO.Path.Combine(dest, newName) });
                AddSidecars(it, f, dest, label);
                return Ready(it, newName, dest, BigName());
            }
        }

        // ------------------------------------------------------------ TV episode titles (TVmaze, cached per show)
        static readonly Dictionary<string, object[]> tvInfo = new Dictionary<string, object[]>(StringComparer.OrdinalIgnoreCase);
        static string TvLookup(string name, int season, int ep, out int year, out string title)
        {
            year = 0; title = "";
            object[] info;
            lock (tvInfo) tvInfo.TryGetValue(name, out info);
            if (info == null)
            {
                info = new object[] { name, 0, new Dictionary<string, string>() };
                try
                {
                    string s = MediaLib.Get("https://api.tvmaze.com/singlesearch/shows?embed=episodes&q=" + Uri.EscapeDataString(name));
                    if (!string.IsNullOrEmpty(s))
                    {
                        var d = MediaLib.Json(s);
                        string n = MediaLib.Str(d, "name"), prem = MediaLib.Str(d, "premiered");
                        if (MediaLib.Norm(n) == MediaLib.Norm(name) || MediaLib.Norm(n).StartsWith(MediaLib.Norm(name) + " ") || MediaLib.Norm(name).StartsWith(MediaLib.Norm(n) + " "))
                        {
                            var eps = (Dictionary<string, string>)info[2];
                            var arr = MediaLib.Arr(MediaLib.D(d, "_embedded"), "episodes");
                            if (arr != null) foreach (var o in arr) { var e = o as Dictionary<string, object>; if (e != null) eps[MediaLib.Str(e, "season") + "x" + MediaLib.Str(e, "number")] = MediaLib.Str(e, "name"); }
                            info[0] = n; info[1] = prem.Length >= 4 ? int.Parse(prem.Substring(0, 4)) : 0;
                        }
                    }
                }
                catch (Exception ex) { Log.Write("tvmaze " + name + ": " + ex.Message); }
                lock (tvInfo) tvInfo[name] = info;
            }
            year = (int)info[1];
            string t; if (((Dictionary<string, string>)info[2]).TryGetValue(season + "x" + ep, out t)) title = t ?? "";
            if (Regex.IsMatch(title, @"^Episode \d+$", RegexOptions.IgnoreCase)) title = "";
            return (string)info[0];
        }

        // a show that's already on the drive keeps its folder (whatever drive category it lives in)
        static string ExistingShowFolder(string big, string show, bool anime)
        {
            string key = MediaLib.Norm(show);
            foreach (var cat in anime ? new[] { "Anime\\Series", "TV", "Documentaries" } : new[] { "TV", "Documentaries", "Anime\\Series" })
            {
                string root = System.IO.Path.Combine(big, "Media Server", cat);
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var d in Directory.GetDirectories(root))
                    {
                        string n = Regex.Replace(System.IO.Path.GetFileName(d), @"\s*[\(\[]\d{4}[\)\]]\s*$", "");
                        if (MediaLib.Norm(n) == key) return d;
                    }
                }
                catch { }
            }
            return null;
        }

        static string SafeName(string s)
        {
            s = s.Replace(": ", " - ").Replace(":", "-");
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), "");
            return Regex.Replace(s, @"\s+", " ").Trim().TrimEnd('.');
        }

        static void AddSidecars(InboxItem it, FileInfo video, string dest, string newStem)
        {
            if (dest == null || video.Directory == null) return;
            string stem = System.IO.Path.GetFileNameWithoutExtension(video.Name);
            foreach (var s in video.Directory.GetFiles(stem + "*"))
            {
                string t = Catalog.TypeOf(s.Extension.ToLowerInvariant());
                if (t != "Subtitle" || s.FullName == video.FullName) continue;
                string suffix = s.Name.Substring(stem.Length);
                it.Ops.Add(new InboxOp { From = s.FullName, To = System.IO.Path.Combine(dest, newStem + suffix) });
            }
        }

        static bool IsAnime(string name, string raw)
        {
            if (Regex.IsMatch(raw, @"^\[(SubsPlease|Erai-raws|Judas|HorribleSubs|EMBER|ASW|Anime Time|DKB|Yameii|Breeze|Tsundere-Raws|ToonsHub)\]", RegexOptions.IgnoreCase)) return true;
            string n = MediaLib.Norm(name);
            lock (MediaLib.Sync) return MediaLib.Titles.Any(t => t.Kind == "Anime" && (MediaLib.Norm(t.Name) == n || n.StartsWith(MediaLib.Norm(t.Name) + " ")));
        }

        // photos and home videos: "YYYY-MM-DD - Event - ###" in Photos\YYYY\YYYY-MM Event
        static readonly Dictionary<string, int> numbering = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static InboxItem ClassifyPhoto(FileInfo f, string eventHint, string what)
        {
            var it = Base(f, what, what == "Photo" ? "\uE91B" : "\uE714", "#F48FB1");
            string big = BigRoot();
            DateTime d = TakenDate(f);
            bool screenshot = Regex.IsMatch(f.Name, @"^Screen ?shot|^Screenshot|^Capture", RegexOptions.IgnoreCase);
            bool whatsapp = Regex.IsMatch(f.Name, @"-WA\d{4}|^WhatsApp", RegexOptions.IgnoreCase);
            string ev = eventHint;
            if (ev == null)
            {
                TripInfo t; lock (MediaLib.Sync) t = MediaLib.Trips.FirstOrDefault(x => d.Date >= x.Start.AddDays(-1) && d.Date <= x.End.AddDays(1));
                ev = t != null ? t.Name : null;
            }
            string dest, newName;
            if (screenshot || whatsapp)
            {
                string sub = screenshot ? "Screenshots" : "WhatsApp";
                dest = big == null ? null : System.IO.Path.Combine(big, "Photos", sub, d.Year.ToString());
                newName = f.Name;
                it.Detected = screenshot ? "Screenshot" : "WhatsApp " + what.ToLowerInvariant();
            }
            else
            {
                string evName = Clean(ev ?? "Unsorted");
                string album = d.ToString("yyyy-MM") + " " + evName;
                dest = big == null ? null : System.IO.Path.Combine(big, "Photos", d.Year.ToString(), album);
                newName = dest == null ? f.Name : NextName(dest, d.ToString("yyyy-MM-dd") + " - " + evName, f.Extension.ToLowerInvariant());
                if (ev != null) it.Detected = what + " \u00B7 " + ev;
                var ppl = MediaLib.PeopleIn(f.FullName);
                if (ppl.Count > 0) it.Detected += " \u00B7 " + string.Join(", ", ppl);
            }
            it.Ops.Add(new InboxOp { From = f.FullName, To = dest == null ? null : System.IO.Path.Combine(dest, newName) });
            return Ready(it, newName, dest, BigName());
        }

        static string NextName(string dest, string prefix, string ext)
        {
            int n;
            lock (numbering)
            {
                if (!numbering.TryGetValue(dest + "|" + prefix, out n))
                {
                    n = 0;
                    try
                    {
                        if (Directory.Exists(dest))
                            foreach (var x in Directory.GetFiles(dest, prefix + " - *"))
                            {
                                var m = Regex.Match(System.IO.Path.GetFileNameWithoutExtension(x), @" - (\d{3,4})$");
                                if (m.Success) n = Math.Max(n, int.Parse(m.Groups[1].Value));
                            }
                    }
                    catch { }
                }
                n++;
                numbering[dest + "|" + prefix] = n;
            }
            return prefix + " - " + n.ToString("000") + ext;
        }

        static DateTime TakenDate(FileInfo f)
        {
            try
            {
                string t = Catalog.TypeOf(f.Extension.ToLowerInvariant());
                if (t == "Image" && (f.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)))
                {
                    using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var img = System.Drawing.Image.FromStream(fs, false, false))
                    {
                        foreach (int id in new[] { 0x9003, 0x9004, 0x0132 })
                        {
                            if (!img.PropertyIdList.Contains(id)) continue;
                            string s = Encoding.ASCII.GetString(img.GetPropertyItem(id).Value).Trim('\0', ' ');
                            DateTime d;
                            if (DateTime.TryParseExact(s, "yyyy:MM:dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d)) return d;
                        }
                    }
                }
                if (t == "Video")
                {
                    var cf = new CatFile();
                    MediaHeader.Probe(f.FullName, cf);
                    if (cf.Created.Year > 1995) return cf.Created;
                }
            }
            catch { }
            var byName = MediaLib.DateFromName(f.Name, 0);
            if (byName.Year > 1990) return byName;
            return f.LastWriteTime < f.CreationTime ? f.LastWriteTime : f.CreationTime;
        }

        static InboxItem ClassifyBook(FileInfo f)
        {
            var it = Base(f, "eBook", "\uE736", "#F4B860");
            string title = null, author = null, pub = null; int year = 0;
            if (f.Extension.Equals(".epub", StringComparison.OrdinalIgnoreCase)) EpubMeta(f.FullName, out title, out author, out pub, out year);
            if (string.IsNullOrEmpty(title))
            {
                string stem = System.IO.Path.GetFileNameWithoutExtension(f.Name).Replace('_', ' ');
                var m = Regex.Match(stem, @"^(.*?)\s+-\s+(.*)$");
                title = m.Success ? m.Groups[1].Value : stem;
                if (m.Success && author == null) author = m.Groups[2].Value;
            }
            if (year == 0) { var ym = Regex.Match(f.Name, @"\b(19|20)\d{2}\b"); if (ym.Success) year = int.Parse(ym.Value); }
            string pubShort = ShortPublisher(pub);
            string newName = string.IsNullOrWhiteSpace(pub) && year == 0 ? Clean(System.IO.Path.GetFileNameWithoutExtension(f.Name)) + f.Extension.ToLowerInvariant()
                             : Clean(pubShort + " - E" + (year > 0 ? year.ToString() : "0000") + " - " + title) + f.Extension.ToLowerInvariant();
            it.Detected = "eBook" + (author != null ? " \u00B7 " + author : "");
            string small = SmallRoot();
            string dest = small == null ? null : System.IO.Path.Combine(small, "Library", "Books Import");
            it.Ops.Add(new InboxOp { From = f.FullName, To = dest == null ? null : System.IO.Path.Combine(dest, newName) });
            Ready(it, newName, dest, SmallName());
            if (it.CanMove) it.Status = "Ready \u2014 add to Calibre from Books Import";
            return it;
        }

        static string ShortPublisher(string pub)
        {
            if (string.IsNullOrWhiteSpace(pub)) return "Unknown";
            foreach (var kv in Publishers) if (pub.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0) return kv.Value;
            var w = Regex.Replace(pub, @"\b(publishing|publishers|publisher|books|press|ltd|limited|inc|llc|group|pvt|private|india|uk|us)\b\.?", "", RegexOptions.IgnoreCase);
            w = Regex.Replace(w, @"[^\p{L}\p{N}& ]", " ").Trim();
            var parts = w.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? "Unknown" : parts.Length >= 3 ? string.Concat(parts.Select(p => char.ToUpper(p[0]))) : string.Join(" ", parts);
        }

        static void EpubMeta(string path, out string title, out string author, out string pub, out int year)
        {
            title = author = pub = null; year = 0;
            try
            {
                using (var z = ZipFile.OpenRead(path))
                {
                    var opf = z.Entries.FirstOrDefault(e => e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase));
                    if (opf == null) return;
                    string x; using (var r = new StreamReader(opf.Open())) x = r.ReadToEnd();
                    Func<string, string> tag = n => { var m = Regex.Match(x, "<dc:" + n + "[^>]*>(.*?)</dc:" + n + ">", RegexOptions.Singleline | RegexOptions.IgnoreCase); return m.Success ? System.Net.WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<[^>]+>", "")).Trim() : null; };
                    title = tag("title"); author = tag("creator"); pub = tag("publisher");
                    var d = tag("date"); if (d != null) { var ym = Regex.Match(d, @"(19|20)\d{2}"); if (ym.Success) year = int.Parse(ym.Value); }
                }
            }
            catch { }
        }

        static InboxItem ClassifyComic(FileInfo f)
        {
            string small = SmallRoot();
            string stem = System.IO.Path.GetFileNameWithoutExtension(f.Name);
            var y = Regex.Match(stem, @"\((\d{4})\)");
            var vm = Regex.Match(stem, @"^(.*?)\s+v(\d+(?:\.\d+)?)\b", RegexOptions.IgnoreCase);
            var cm = Regex.Match(stem, @"^(.*?)\s+(?:c|ch|chapter\s*)?(\d{1,4}(?:\.\d+)?)\s*(\(|$)", RegexOptions.IgnoreCase);
            string series = vm.Success ? vm.Groups[1].Value : cm.Success ? cm.Groups[1].Value : Regex.Replace(stem, @"\s*\(.*$", "");
            series = Clean(Regex.Replace(series, @"^\[[^\]]*\]\s*", "").Replace('_', ' '));
            string known = null;
            if (small != null)
            {
                try
                {
                    string mdir = System.IO.Path.Combine(small, "Library", "Manga");
                    if (Directory.Exists(mdir)) known = Directory.GetDirectories(mdir).Select(System.IO.Path.GetFileName).FirstOrDefault(n => MediaLib.Norm(n) == MediaLib.Norm(series));
                }
                catch { }
            }
            var it = Base(f, known != null ? "Manga \u00B7 " + known : "Comic / manga", "\uE736", "#C792EA");
            string seriesName = known ?? TitleCase(series);
            string dest, newName;
            if (vm.Success) { dest = "Volumes"; newName = seriesName + " - Vol " + Pad(vm.Groups[2].Value, 2); }
            else if (cm.Success) { dest = "Chapters"; newName = seriesName + " - Ch " + Pad(cm.Groups[2].Value, 3); }
            else { dest = ""; newName = stem; }
            if (y.Success) newName += " (" + y.Groups[1].Value + ")";
            newName += f.Extension.ToLowerInvariant();
            string folder = small == null ? null : System.IO.Path.Combine(small, "Library", known != null || vm.Success || cm.Success ? "Manga" : "Comics", seriesName, dest);
            it.Ops.Add(new InboxOp { From = f.FullName, To = folder == null ? null : System.IO.Path.Combine(folder, newName) });
            return Ready(it, newName, folder, SmallName());
        }
        static string Pad(string v, int w) { var p = v.Split('.'); return p[0].PadLeft(w, '0') + (p.Length > 1 ? "." + p[1] : ""); }

        static InboxItem ClassifyPdf(FileInfo f)
        {
            string small = SmallRoot();
            string stem = System.IO.Path.GetFileNameWithoutExtension(f.Name), l = stem.ToLowerInvariant();
            // magazine? match an existing magazine folder name
            if (small != null)
            {
                try
                {
                    string mdir = System.IO.Path.Combine(small, "Library", "Magazines");
                    if (Directory.Exists(mdir))
                        foreach (var mag in Directory.GetDirectories(mdir).Select(System.IO.Path.GetFileName))
                            if (MediaLib.Norm(stem).StartsWith(MediaLib.Norm(mag) + " ") || MediaLib.Norm(stem) == MediaLib.Norm(mag))
                            {
                                var ym = Regex.Match(stem, @"\b(19|20)\d{2}\b");
                                string year = ym.Success ? ym.Value : DateTime.Now.Year.ToString();
                                var it = Base(f, "Magazine \u00B7 " + mag, "\uE8F1", "#FF8A65");
                                string rest = Clean(stem.Substring(Math.Min(stem.Length, mag.Length)).Trim(' ', '-', '_'));
                                string newName = Clean(mag + " - E" + year + " - " + (rest.Length > 0 ? rest : stem)) + ".pdf";
                                string dest = System.IO.Path.Combine(mdir, mag, year);
                                it.Ops.Add(new InboxOp { From = f.FullName, To = System.IO.Path.Combine(dest, newName) });
                                return Ready(it, newName, dest, SmallName());
                            }
                }
                catch { }
            }
            bool doc = DocWords.Any(w => Regex.IsMatch(l, @"(?<![a-z])" + w + @"(?![a-z])")) || f.Length < 3L * 1048576;
            if (doc) return ClassifyDoc(f, "PDF document");
            var b = ClassifyBook(f);
            b.Detected = "Book (PDF)";
            return b;
        }

        static InboxItem ClassifyDoc(FileInfo f, string kind)
        {
            var it = Base(f, kind == "PDF document" ? "Document (PDF)" : kind, "\uE8A5", "#AED581");
            bool personal = Regex.IsMatch(f.Name, @"^Personal\s*-", RegexOptions.IgnoreCase);
            string small = SmallRoot();
            var d = f.LastWriteTime;
            string dest = small == null ? null : personal ? System.IO.Path.Combine(small, "Docs", "Personal") : System.IO.Path.Combine(small, "Docs", d.Year.ToString(), d.ToString("yyyy-MM"));
            it.Ops.Add(new InboxOp { From = f.FullName, To = dest == null ? null : System.IO.Path.Combine(dest, f.Name) });
            return Ready(it, f.Name, dest, SmallName());
        }

        // ------------------------------------------------------------ folders
        static InboxItem ClassifyFolder(DirectoryInfo d)
        {
            FileInfo[] files;
            try { files = d.GetFiles("*", SearchOption.AllDirectories); } catch { return Leave(d, "Couldn't read this folder"); }
            if (files.Length == 0) return Leave(d, "Empty");
            var byType = files.GroupBy(f => Catalog.TypeOf(f.Extension.ToLowerInvariant())).ToDictionary(g => g.Key, g => g.ToList());
            Func<string, List<FileInfo>> T = k => byType.ContainsKey(k) ? byType[k] : new List<FileInfo>();
            var vids = T("Video").Where(v => v.Length >= 150L * 1048576 && !Regex.IsMatch(v.Name, @"\bsample\b", RegexOptions.IgnoreCase)).ToList();
            bool camVids = T("Video").Any(v => Regex.IsMatch(v.Name, @"^(IMG|VID|MOV|PXL|DJI|GOPR|DSC|MVI)", RegexOptions.IgnoreCase));
            string big = BigRoot();
            string folderName; int fy; MediaLib.ParseName(d.Name, out folderName, out fy);
            if (vids.Count > 0 && !camVids)
            {
                bool tvFolder = vids.Any(v => Regex.IsMatch(v.Name, @"[Ss]\d{1,2}[Ee]\d{1,3}|\b\d{1,2}x\d{2}\b"));
                if (tvFolder)
                {
                    var it = Base(d, "TV season / episodes", "\uE7F4", "#FFB74D");
                    string firstDest = null, show = null;
                    foreach (var v in vids)
                    {
                        var sub = ClassifyVideo(v, null);
                        it.Ops.AddRange(sub.Ops);
                        if (firstDest == null) { firstDest = sub.DestFolder; show = sub.NewName; it.Detected = sub.Detected == "Anime episode" ? "Anime episodes" : "TV episodes"; it.Glyph = sub.Glyph; it.Color = sub.Color; }
                    }
                    if (big == null) return Ready(it, "", null, BigName());
                    Ready(it, vids.Count + " episode" + (vids.Count == 1 ? "" : "s") + " renamed like \u201C" + show + "\u201D", firstDest, BigName());
                    it.Status = "Ready \u2014 leftovers (nfo, samples) stay in Downloads";
                    return it;
                }
                // a film in its own folder: move the folder, rename the folder and the main video
                var main = vids.OrderByDescending(v => v.Length).First();
                string name; int year; MediaLib.ParseName(fy > 0 ? d.Name : System.IO.Path.GetFileNameWithoutExtension(main.Name), out name, out year);
                name = TitleCase(Clean(Regex.Replace(name, @"^\[[^\]]*\]\s*", "")));
                bool anime = IsAnime(name, d.Name);
                string label = name + (year > 0 ? " (" + year + ")" : "");
                var mi = Base(d, anime ? "Anime film" : "Movie", anime ? "\uE7FC" : "\uE8B2", anime ? "#BA68C8" : "#FF7A7A");
                string dest = big == null ? null : System.IO.Path.Combine(big, "Media Server", anime ? "Anime\\Movies" : "Movies", label);
                if (dest != null)
                {
                    foreach (var file in files)
                    {
                        string rel = file.FullName.Substring(d.FullName.Length).TrimStart('\\');
                        string to = System.IO.Path.Combine(dest, rel);
                        if (file.FullName == main.FullName) to = System.IO.Path.Combine(dest, label + main.Extension.ToLowerInvariant());
                        else if (Catalog.TypeOf(file.Extension.ToLowerInvariant()) == "Subtitle" && System.IO.Path.GetDirectoryName(file.FullName) == main.DirectoryName && file.Name.StartsWith(System.IO.Path.GetFileNameWithoutExtension(main.Name)))
                            to = System.IO.Path.Combine(dest, label + file.Name.Substring(System.IO.Path.GetFileNameWithoutExtension(main.Name).Length));
                        else if (Regex.IsMatch(file.Name, @"www\.|torrent|\.url$|\.nfo$|\.txt$|\.exe$|sample", RegexOptions.IgnoreCase)) continue;   // ads and junk stay behind
                        mi.Ops.Add(new InboxOp { From = file.FullName, To = to });
                    }
                }
                Ready(mi, label + main.Extension.ToLowerInvariant(), dest, BigName());
                if (mi.CanMove) mi.Status = "Ready \u2014 ads/nfo/sample files stay behind";
                return mi;
            }
            int imgs = T("Image").Count + T("Video").Count;
            if (imgs >= files.Length * 0.7)
            {
                string ev = Clean(Regex.Replace(d.Name, @"[_]+", " "));
                var folderPeople = MediaLib.PeopleIn(d.Name);
                foreach (var person in folderPeople) ev = Regex.Replace(ev, @"(?<![A-Za-z])" + Regex.Escape(person) + @"(?![A-Za-z])", "", RegexOptions.IgnoreCase);
                ev = Regex.Replace(ev, @"\s+", " ").Trim(' ', '-', ',', '&');
                if (ev.Length == 0) ev = Clean(d.Name);
                var it = Base(d, "Photo album \u00B7 " + ev, "\uE91B", "#F48FB1");
                string dest = null;
                foreach (var file in files.OrderBy(TakenDate))
                {
                    string ft = Catalog.TypeOf(file.Extension.ToLowerInvariant());
                    if (ft != "Image" && ft != "Video") continue;
                    var sub = ClassifyPhoto(file, ev, ft == "Image" ? "Photo" : "Home video");
                    it.Ops.AddRange(sub.Ops);
                    if (dest == null) dest = sub.DestFolder;
                }
                var ppl = MediaLib.PeopleIn(d.Name);
                if (ppl.Count > 0) it.Detected += " \u00B7 " + string.Join(", ", ppl);
                return Ready(it, imgs + " files renamed \u201CYYYY-MM-DD - " + ev + " - ###\u201D", big == null ? null : dest, BigName());
            }
            if (T("Comic").Count >= files.Length * 0.6)
            {
                var it = Base(d, "Manga / comics", "\uE736", "#C792EA");
                string dest = null;
                foreach (var file in T("Comic")) { var sub = ClassifyComic(file); it.Ops.AddRange(sub.Ops); if (dest == null) dest = sub.DestFolder; }
                return Ready(it, T("Comic").Count + " issues renamed", SmallRoot() == null ? null : dest, SmallName());
            }
            if (T("eBook").Count + T("PDF").Count >= files.Length * 0.6)
            {
                var it = Base(d, "Books", "\uE736", "#F4B860");
                string dest = null;
                foreach (var file in T("eBook").Concat(T("PDF"))) { var sub = file.Extension == ".pdf" ? ClassifyPdf(file) : ClassifyBook(file); it.Ops.AddRange(sub.Ops); if (dest == null) dest = sub.DestFolder; }
                return Ready(it, (T("eBook").Count + T("PDF").Count) + " books renamed", SmallRoot() == null ? null : dest, SmallName());
            }
            if (T("Audio").Count >= files.Length * 0.6)
            {
                var it = Base(d, "Music album", "\uE8D6", "#4FC3F7");
                string dest = big == null ? null : System.IO.Path.Combine(big, "Media Server", "Music", Clean(d.Name));
                if (dest != null) foreach (var file in files) it.Ops.Add(new InboxOp { From = file.FullName, To = System.IO.Path.Combine(dest, file.FullName.Substring(d.FullName.Length).TrimStart('\\')) });
                return Ready(it, Clean(d.Name), dest, BigName());
            }
            return Leave(d, "Mixed folder \u2014 open it and sort by hand, or drop the files straight into Downloads");
        }

        // ------------------------------------------------------------ move
        public static void Apply(List<InboxItem> items, Action<InboxItem> changed)
        {
            int moved = 0;
            foreach (var it in items)
            {
                if (!it.Selected || !it.CanMove) continue;
                it.Status = "Moving\u2026"; if (changed != null) changed(it);
                int ok = 0, fail = 0; string err = null;
                foreach (var op in it.Ops)
                {
                    if (op.To == null) continue;
                    try
                    {
                        if (!File.Exists(op.From)) continue;
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(op.To));
                        string to = op.To;
                        for (int n = 2; File.Exists(to); n++) to = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(op.To), System.IO.Path.GetFileNameWithoutExtension(op.To) + " (" + n + ")" + System.IO.Path.GetExtension(op.To));
                        File.Move(op.From, to);
                        File.AppendAllText(LogFile, DateTime.Now.ToString("s") + "\t" + op.From + "\t->\t" + to + "\r\n", Encoding.UTF8);
                        ok++;
                    }
                    catch (Exception ex) { fail++; err = ex.Message; Log.Write("sort " + op.From + ": " + ex.Message); }
                }
                // tidy: remove the source folder if nothing useful is left in it
                try
                {
                    string src = System.IO.Path.Combine(it.Source ?? "", it.Name);
                    if (Directory.Exists(src) && Directory.GetFiles(src, "*", SearchOption.AllDirectories).Length == 0) Directory.Delete(src, true);
                }
                catch { }
                it.Status = fail == 0 ? "Moved \u2713" : "Moved " + ok + ", failed " + fail + ": " + err;
                it.CanMove = false; it.Selected = false;
                moved += ok;
                if (changed != null) changed(it);
            }
            if (moved > 0) Catalog.RescanNow();
        }
    }

    // ---------------------------------------------------------------- window
    public class Sorter
    {
        static Sorter open;
        Window win; ListBox list; TextBlock sub, foot; Button go;
        List<InboxItem> items = new List<InboxItem>();

        const string Xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='Sort new files' Width='1180' Height='720' Background='#0B0E14' WindowStartupLocation='CenterScreen' FontFamily='Segoe UI'>
  <Window.Resources>
    <Style x:Key='Btn' TargetType='Button'>
      <Setter Property='Foreground' Value='#EEF1F6'/><Setter Property='Background' Value='#18202D'/><Setter Property='Padding' Value='14,7'/>
      <Setter Property='Margin' Value='8,0,0,0'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FontSize' Value='13'/>
      <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
        <Border Background='{TemplateBinding Background}' CornerRadius='9' BorderBrush='#2A3547' BorderThickness='1' Padding='{TemplateBinding Padding}'>
          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <DataTemplate x:Key='RowTpl'>
      <Border Padding='12,9' BorderBrush='#1D2532' BorderThickness='0,0,0,1'>
        <Grid>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width='30'/><ColumnDefinition Width='36'/><ColumnDefinition Width='3*'/><ColumnDefinition Width='2*'/><ColumnDefinition Width='3*'/><ColumnDefinition Width='2*'/>
          </Grid.ColumnDefinitions>
          <CheckBox IsChecked='{Binding Selected, Mode=TwoWay}' IsEnabled='{Binding CanMove}' VerticalAlignment='Center'/>
          <TextBlock Grid.Column='1' Text='{Binding Glyph}' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='18' Foreground='{Binding Color}' VerticalAlignment='Center'/>
          <StackPanel Grid.Column='2' Margin='0,0,12,0'>
            <TextBlock Text='{Binding Name}' Foreground='#EEF1F6' FontSize='13.5' TextTrimming='CharacterEllipsis' ToolTip='{Binding Name}'/>
            <TextBlock Foreground='#6B7486' FontSize='11.5'><Run Text='{Binding SizeText, Mode=OneWay}'/><Run Text='  &#183;  '/><Run Text='{Binding Source, Mode=OneWay}'/></TextBlock>
          </StackPanel>
          <TextBlock Grid.Column='3' Text='{Binding Detected}' Foreground='#C9D1E0' FontSize='12.5' TextWrapping='Wrap' VerticalAlignment='Center' Margin='0,0,12,0'/>
          <StackPanel Grid.Column='4' Margin='0,0,12,0'>
            <TextBlock Text='{Binding NewName}' Foreground='#9DB8FF' FontSize='13' TextTrimming='CharacterEllipsis' ToolTip='{Binding NewName}'/>
            <TextBlock Text='{Binding DestFolder}' Foreground='#6B7486' FontSize='11.5' TextTrimming='CharacterEllipsis' ToolTip='{Binding DestFolder}'/>
          </StackPanel>
          <TextBlock Grid.Column='5' Text='{Binding Status}' Foreground='#9AA4B6' FontSize='12' TextWrapping='Wrap' VerticalAlignment='Center'/>
        </Grid>
      </Border>
    </DataTemplate>
  </Window.Resources>
  <Grid Margin='26,22,26,20'>
    <Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='Auto'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/></Grid.RowDefinitions>
    <DockPanel>
      <StackPanel Orientation='Horizontal' DockPanel.Dock='Right' VerticalAlignment='Top'>
        <Button x:Name='BRescan' Style='{StaticResource Btn}' Content='Look again'/>
        <Button x:Name='BFolders' Style='{StaticResource Btn}' Content='Inbox folders&#8230;'/>
        <Button x:Name='BPeople' Style='{StaticResource Btn}' Content='People &amp; places&#8230;'/>
        <Button x:Name='BLog' Style='{StaticResource Btn}' Content='What moved'/>
      </StackPanel>
      <StackPanel>
        <TextBlock Text='Sort new files' FontSize='26' FontWeight='SemiBold' Foreground='#EEF1F6'/>
        <TextBlock x:Name='Sub' FontSize='13' Foreground='#9AA4B6' Margin='0,4,0,0' TextWrapping='Wrap'/>
      </StackPanel>
    </DockPanel>
    <Grid Grid.Row='1' Margin='12,18,12,6'>
      <Grid.ColumnDefinitions><ColumnDefinition Width='66'/><ColumnDefinition Width='3*'/><ColumnDefinition Width='2*'/><ColumnDefinition Width='3*'/><ColumnDefinition Width='2*'/></Grid.ColumnDefinitions>
      <TextBlock Grid.Column='1' Text='NEW FILE' Foreground='#6B7486' FontSize='11'/>
      <TextBlock Grid.Column='2' Text='LOOKS LIKE' Foreground='#6B7486' FontSize='11'/>
      <TextBlock Grid.Column='3' Text='NEW NAME  &#183;  GOES TO' Foreground='#6B7486' FontSize='11'/>
      <TextBlock Grid.Column='4' Text='STATUS' Foreground='#6B7486' FontSize='11'/>
    </Grid>
    <Border Grid.Row='2' Background='#121823' CornerRadius='14' BorderBrush='#243041' BorderThickness='1'>
      <ListBox x:Name='L' Background='Transparent' BorderThickness='0' ItemTemplate='{StaticResource RowTpl}' HorizontalContentAlignment='Stretch'
               ScrollViewer.HorizontalScrollBarVisibility='Disabled' VirtualizingPanel.ScrollUnit='Pixel'/>
    </Border>
    <DockPanel Grid.Row='3' Margin='0,16,0,0'>
      <Button x:Name='BGo' DockPanel.Dock='Right' Style='{StaticResource Btn}' Background='#5B8CFF' FontWeight='SemiBold' Padding='22,9' Content='Move selected'/>
      <TextBlock x:Name='Foot' Foreground='#9AA4B6' FontSize='12.5' VerticalAlignment='Center' TextWrapping='Wrap'/>
    </DockPanel>
  </Grid>
</Window>";

        public static void Show()
        {
            if (open != null) { open.win.Activate(); open.Refresh(); return; }
            open = new Sorter();
            open.win.Show(); open.win.Activate();
            open.Refresh();
        }

        Sorter()
        {
            win = (Window)XamlReader.Parse(Xaml);
            list = (ListBox)win.FindName("L"); sub = (TextBlock)win.FindName("Sub"); foot = (TextBlock)win.FindName("Foot"); go = (Button)win.FindName("BGo");
            ((Button)win.FindName("BRescan")).Click += delegate { Refresh(); };
            ((Button)win.FindName("BFolders")).Click += delegate { Inbox.Folders(); Open(Inbox.ConfigFile); };
            ((Button)win.FindName("BPeople")).Click += delegate { Open(System.IO.Path.Combine(Catalog.Dir, "people-and-places.json")); };
            ((Button)win.FindName("BLog")).Click += delegate { if (File.Exists(Inbox.LogFile)) Open(Inbox.LogFile); };
            go.Click += delegate { Go(); };
            win.Closed += delegate { open = null; };
        }

        static void Open(string p) { try { var psi = new ProcessStartInfo(p); psi.UseShellExecute = true; Process.Start(psi); } catch { } }

        void Refresh()
        {
            sub.Text = "Looking in " + string.Join(", ", Inbox.Folders().Select(f => Engine.Pretty(f))) + "\u2026";
            foot.Text = "Reading new files\u2026";
            go.IsEnabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<InboxItem> res;
                try { res = Inbox.Scan(); } catch (Exception ex) { Log.Write("inbox scan: " + ex); res = new List<InboxItem>(); }
                win.Dispatcher.BeginInvoke(new Action(delegate
                {
                    items = res;
                    list.ItemsSource = items;
                    sub.Text = "Looked in " + string.Join(", ", Inbox.Folders().Select(f => Engine.Pretty(f))) + ".  Big files go to the " + Inbox.BigName() + " drive (films, TV, anime, photos, videos); reading and documents go to the " + Inbox.SmallName() + " drive.";
                    UpdateFoot();
                }));
            });
        }

        void UpdateFoot()
        {
            int ready = items.Count(i => i.CanMove), sel = items.Count(i => i.Selected && i.CanMove);
            int waiting = items.Count(i => !i.CanMove && (i.Status ?? "").StartsWith("Plug in"));
            foot.Text = items.Count == 0 ? "Nothing new to sort. Files appear here a few minutes after they finish downloading." :
                ready + " ready to move" + (waiting > 0 ? "  \u00B7  " + waiting + " waiting for a drive" : "") + "  \u00B7  " + (items.Count - ready - waiting) + " left in place (installers, archives, unknown)";
            go.IsEnabled = sel > 0;
            go.Content = sel > 0 ? "Move " + sel + " item" + (sel == 1 ? "" : "s") : "Move selected";
            foreach (var i in items) { i.PropertyChanged -= OnItemChanged; i.PropertyChanged += OnItemChanged; }
        }
        void OnItemChanged(object s, PropertyChangedEventArgs e) { if (e.PropertyName == "Selected") UpdateFoot(); }

        void Go()
        {
            var todo = items.Where(i => i.Selected && i.CanMove).ToList();
            if (todo.Count == 0) return;
            go.IsEnabled = false; foot.Text = "Moving " + todo.Count + " item" + (todo.Count == 1 ? "" : "s") + "\u2026 big files take a while.";
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Inbox.Apply(todo, null); } catch (Exception ex) { Log.Write("inbox apply: " + ex); }
                win.Dispatcher.BeginInvoke(new Action(delegate
                {
                    list.Items.Refresh();
                    int done = todo.Count(i => (i.Status ?? "").StartsWith("Moved \u2713"));
                    foot.Text = "Done: " + done + " of " + todo.Count + " moved and added to the catalogue. Details in \u201CWhat moved\u201D.";
                    go.Content = "Move selected";
                }));
            });
        }

        // auto mode (sortWithoutAsking): moves everything that is clearly identified
        public static int AutoSort()
        {
            var items = Inbox.Scan().Where(i => i.CanMove && i.Selected).ToList();
            if (items.Count > 0) Inbox.Apply(items, null);
            return items.Count;
        }
    }
}
