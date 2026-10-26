// Desktop Organizer - movie / TV / anime knowledge (appended after Core.cs at build time; shares its using block)
// Groups catalogued video files into titles, enriches them from Wikidata (director, cast, language, genre ...)
// and writes the "Drive Catalog.html" dashboard.
namespace DesktopOrganizer
{
    public class MetaInfo
    {
        public string Q = "", Label = "", Desc = "", Imdb = "";
        public int Year, Minutes;
        public List<string> Directors = new List<string>(), Cast = new List<string>(), Langs = new List<string>(),
                            Countries = new List<string>(), Genres = new List<string>(), Types = new List<string>();
        public long Tried;
        public bool Ok;
    }

    public class Title
    {
        public string Key, Kind, Name, Path, Folder, Poster = "", Quality = "", Res = "";
        public int Year, Episodes;
        public long Size, DurSec, Added, MainSize;
        public bool Online, IsAnimeHint, Adult;
        public int PosterRank;
        public SortedSet<int> Seasons = new SortedSet<int>();
        public List<string> Drives = new List<string>();
        public HashSet<string> Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public MetaInfo Meta;
    }

    public class HomeVideo
    {
        public string Path, Name, Trip = "", Event = "", Activity = "", Drive = "", Quality = "";
        public List<string> People = new List<string>();
        public DateTime Date;
        public long Size, DurSec;
        public bool Online;
        public string Group { get { return Trip.Length > 0 ? Trip : Event.Length > 0 ? Event : Date.ToString("MMMM yyyy"); } }
    }

    public class TripInfo { public string Name; public DateTime Start = DateTime.MaxValue, End = DateTime.MinValue; public int Photos; }

    public static class MediaLib
    {
        public static readonly object Sync = new object();
        public static List<Title> Titles = new List<Title>();
        public static Dictionary<string, MetaInfo> Meta = new Dictionary<string, MetaInfo>(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<string, Title> ByFile = new Dictionary<string, Title>(StringComparer.OrdinalIgnoreCase);
        public static List<HomeVideo> HomeVideos = new List<HomeVideo>();
        public static List<TripInfo> Trips = new List<TripInfo>();
        static List<KeyValuePair<string, string[]>> people = new List<KeyValuePair<string, string[]>>();
        static Dictionary<string, string> placeAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly string[] PhotoRoots = { "photos", "pictures", "camera", "camera roll", "dcim", "icloud photos", "whatsapp", "whatsapp video",
            "google photos", "videos", "screen recordings", "my videos" };
        static readonly string[] PhotoGeneric = { "self", "mov", "google photos", "photo dump dslr", "dcim", "icloud photos", "photos", "pictures", "camera roll",
            "camera", "chosen", "lightroom cc", "archive (from 2tb)", "i5", "print i5", "whatsapp", "whatsapp video", "screenshots", "videos", "my videos",
            "misc", "phone", "profile photos", "wallpapers", "screen recordings", "new folder", "export", "edited", "raw", "jpg", "jpeg", "video", "videos" };
        static readonly string[] ActivityWords = { "farewell", "puja", "pujo", "wedding", "birthday", "bday", "photoshoot", "shoot", "quiz", "concert", "party",
            "graduation", "convocation", "festival", "diwali", "holi", "trek", "trekking", "hike", "beach", "match", "cricket", "football", "dance", "nach",
            "reunion", "anniversary", "biye", "shaadi", "sangeet", "engagement", "puja", "graduation", "fest", "workshop", "conference", "picnic", "dinner", "lunch", "drive", "ride", "marathon", "game" };
        public static volatile string StatusText = "";
        static string metaFile;
        static Action changed;
        static readonly AutoResetEvent wake = new AutoResetEvent(false);

        static readonly string[] Generic = { "", "movies", "movie", "films", "film", "sync", "4k", "collections", "nc17", "animation", "documentaries",
            "documentary", "media server", "anime", "series", "tv", "tv shows", "shows", "videos", "video", "downloads", "new folder", "misc", "hollywood",
            "bollywood", "english", "hindi", "kids", "cartoons", "torrents", "complete", "my videos" };
        static readonly string[] NonMedia = { "photos", "pictures", "camera", "camera roll", "dcim", "icloud photos", "whatsapp", "whatsapp video",
            "projects", "software", "screen recordings", "screenshots", "auto data backup" };
        static readonly string[] SkipSeg = { "season", "specials", "extras", "soundtrack", "featurettes", "subs", "subtitles", "sample", "samples", "disc", "cd1", "cd2", "ost" };

        // ------------------------------------------------------------ name parsing
        public static void ParseName(string raw, out string title, out int year)
        {
            string s = Regex.Replace(raw, @"^\s*www\.?\s*\S+?[\.\s](org|com|net|to|mx|lt|ag|in)\s*-*\s*", "", RegexOptions.IgnoreCase);
            int dots = s.Count(c => c == '.'), spaces = s.Count(c => c == ' ');
            if (dots >= 2 && dots > spaces) s = s.Replace('.', ' ');
            s = s.Replace('_', ' ');
            year = 0; title = s;
            var ms = Regex.Matches(s, @"(?:^|[\s\(\[\-;,])((?:19[0-9]{2})|(?:20[0-4][0-9]))(?=[\s\)\]\-;,]|$)");
            Match ym = null;
            var br = Regex.Match(s, @"[\(\[]((?:19[0-9]{2})|(?:20[0-4][0-9]))[\)\]]");
            if (br.Success && br.Groups[1].Index > 0) ym = br;
            else foreach (Match m in ms) { if (m.Groups[1].Index > 0) { ym = m; break; } }
            if (ym != null)
            {
                year = int.Parse(ym.Groups[1].Value);
                title = s.Substring(0, ym.Groups[1].Index);
            }
            title = Regex.Replace(title, @"\[[^\]]*\]|\([^\)]*\)", " ");
            var cut = Regex.Match(title, @"\b(2160p|1080p|720p|480p|4k|uhd|bluray|blu-ray|brrip|bdrip|webrip|web-dl|web|hdtv|dvdrip|x264|x265|h264|h265|hevc|yts|yify|rarbg|extended|remastered|unrated|proper|repack|hybrid|dual audio|hindi|s\d{1,2}e\d{1,3}|s\d{1,2}|season \d+|complete)\b", RegexOptions.IgnoreCase);
            if (cut.Success && cut.Index > 0) title = title.Substring(0, cut.Index);
            title = Regex.Replace(title, @"\s+", " ").Trim(' ', '-', '.', ',', ';', '[', '(');
            if (title.Length == 0) title = raw;
        }

        public static string Norm(string s)
        {
            s = s.ToLowerInvariant().Replace("&", " and ");
            var sb = new StringBuilder();
            foreach (char c in s.Normalize(NormalizationForm.FormD))
            {
                var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        static bool IsTvName(string n) { return Regex.IsMatch(n, @"\b[Ss]\d{1,2}[Ee]\d{1,3}\b|\b\d{1,2}x\d{2}\b|\bEp?\s?\d{2,3}\b| - Special \d+ - "); }
        static bool IsSkipSeg(string seg)
        {
            string l = seg.ToLowerInvariant();
            foreach (var p in SkipSeg) if (l == p || l.StartsWith(p + " ")) return true;
            return Regex.IsMatch(l, @"^s\d{1,2}$|^season\s*\d+");
        }
        static bool IsExtra(string n)
        {
            return Regex.IsMatch(n, @"\b(sample|trailer|teaser|featurette|behind the scenes|deleted scenes?|interview|ncop|nced|creditless|opening|ending|making of|bonus)\b", RegexOptions.IgnoreCase);
        }

        // how good a picture is as cover art (0 = not artwork: fanart, episode stills, logos, random photos)
        public static int PosterRank(string stem)
        {
            string n = (stem ?? "").ToLowerInvariant().Trim();
            if (Regex.IsMatch(n, @"(fanart|backdrop|background|banner|landscape|clearlogo|clearart|logo|thumb|still|screenshot|keyart-wide)")) return 0;
            if (n == "poster" || n.EndsWith("-poster") && !n.StartsWith("season")) return 100;
            if (n == "folder") return 90;
            if (n == "cover" || n.EndsWith("-cover") || n == "front" || n == "keyart") return 85;
            if (n == "movie" || n == "show") return 80;
            if (n == "season01-poster" || n == "season1-poster" || n == "season-all-poster") return 60;
            if (n.StartsWith("season") && n.EndsWith("-poster")) return 50;
            if (n == "disc" || n == "discart" || n.EndsWith("-disc") || n.EndsWith("-discart") || n == "cdart") return 40;
            return 0;
        }

        // ------------------------------------------------------------ grouping
        public static void Rebuild()
        {
            List<CatVolume> vols;
            lock (Catalog.Sync) vols = Catalog.Vols.Values.ToList();
            var titles = new Dictionary<string, Title>(StringComparer.OrdinalIgnoreCase);
            var byFile = new Dictionary<string, Title>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in vols)
            {
                // how many sizeable videos sit in each folder (to tell "movie folder" from "folder of movies")
                var perDir = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                // only real artwork counts: poster / folder / cover / disc art -- never fanart, episode stills or random pictures
                var imgs = new Dictionary<string, KeyValuePair<string, int>>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in v.Files)
                {
                    string dir = Catalog.SDir(f.Rel);
                    if (f.Type == "Video" && f.Size >= 80L * 1048576) { int c; perDir.TryGetValue(dir, out c); perDir[dir] = c + 1; }
                    else if (f.Type == "Image")
                    {
                        int rank = PosterRank(Catalog.SStem(f.Rel));
                        if (rank <= 0) continue;
                        KeyValuePair<string, int> cur;
                        if (!imgs.TryGetValue(dir, out cur) || rank > cur.Value) imgs[dir] = new KeyValuePair<string, int>(f.Rel, rank);
                    }
                }
                foreach (var f in v.Files)
                {
                    if (f.Type != "Video") continue;
                    string fileName = Catalog.SFile(f.Rel), stem = Catalog.SStem(f.Rel);
                    var segs = f.Rel.Split('\\');
                    if (segs.Take(segs.Length - 1).Any(s => Array.IndexOf(NonMedia, s.ToLowerInvariant()) >= 0 || Settings.IgnoreFolders.Contains(s.ToLowerInvariant()))) continue;
                    if (segs.Take(segs.Length - 1).Any(s => s.StartsWith(".") || s.StartsWith("_Duplicates", StringComparison.OrdinalIgnoreCase))) continue;   // .deletedByTMM, .tmp, set-aside duplicates
                    bool tv = IsTvName(fileName) || segs.Take(segs.Length - 1).Any(s => Regex.IsMatch(s, @"^(Season|Series)\s*\d+|^S\d{1,2}$|^Specials$", RegexOptions.IgnoreCase));
                    if (f.Size < (tv ? 30L : 150L) * 1048576) continue;
                    if (Regex.IsMatch(fileName, @"^(IMG|VID|MOV|PXL|DJI|GOPR|GH0|GX0|DSC|MVI|MAH|VID-\d{8}-WA|WhatsApp Video|Screen ?Recording|\d{8}_\d{6})", RegexOptions.IgnoreCase)) continue;
                    if (IsExtra(stem) || segs.Any(s => s.Equals("Extras", StringComparison.OrdinalIgnoreCase) || s.Equals("Featurettes", StringComparison.OrdinalIgnoreCase))) continue;
                    bool anime = segs.Any(s => s.Equals("Anime", StringComparison.OrdinalIgnoreCase));
                    bool animation = segs.Any(s => s.Equals("Animation", StringComparison.OrdinalIgnoreCase));

                    string name; int year; string folder;
                    int idx = segs.Length - 2;
                    if (tv)
                    {
                        while (idx >= 0 && IsSkipSeg(segs[idx])) idx--;
                        string showSeg = idx >= 0 ? segs[idx] : "";
                        if (idx < 0 || Array.IndexOf(Generic, showSeg.ToLowerInvariant()) >= 0)
                        {
                            // episodes lying loose: take the show name from the file name
                            var m = Regex.Match(stem.Replace('.', ' '), @"^(.*?)(?:\s*-\s*)?\b(?:[Ss]\d{1,2}[Ee]\d{1,3}|\d{1,2}x\d{2}|Special \d+ - )");
                            ParseName(m.Success && m.Groups[1].Value.Trim().Length > 0 ? m.Groups[1].Value : stem, out name, out year);
                            folder = v.Root + Catalog.SDir(f.Rel);
                        }
                        else
                        {
                            ParseName(showSeg, out name, out year);
                            folder = v.Root + string.Join("\\", segs.Take(idx + 1));
                        }
                        name = Regex.Replace(name, @"\s+(US|UK)$", "", RegexOptions.IgnoreCase);
                    }
                    else
                    {
                        string parent = idx >= 0 ? segs[idx] : "";
                        int count; perDir.TryGetValue(Catalog.SDir(f.Rel), out count);
                        bool folderIsMovie = parent.Length > 0 && Array.IndexOf(Generic, parent.ToLowerInvariant()) < 0 && count <= 3 && !IsSkipSeg(parent);
                        string sName; int sYear; ParseName(stem, out sName, out sYear);
                        if (folderIsMovie && count > 1)
                        {
                            string fName; int fYear; ParseName(parent, out fName, out fYear);
                            string a = Regex.Replace(Norm(sName), @"\b(cd|disc|part|pt)\s*\d\b", "").Trim();
                            if (sYear > 0 && a != Norm(fName) && !a.StartsWith(Norm(fName) + " cd")) folderIsMovie = false;
                        }
                        if (folderIsMovie) ParseName(parent, out name, out year); else { name = sName; year = sYear; }
                        if (folderIsMovie && year == 0 && sYear > 0) year = sYear;
                        folder = v.Root + Catalog.SDir(f.Rel);
                    }
                    if (name.Length < 1) continue;
                    string kind = anime ? "Anime" : tv ? (animation ? "Animation" : "TV") : "Movie";
                    string key = Norm(name) + "|" + (tv ? "" : year.ToString()) + "|" + (tv ? "tv" : "m");
                    Title t;
                    if (!titles.TryGetValue(key, out t))
                    {
                        t = new Title { Key = key, Kind = kind, Name = name, Year = year, Folder = folder, IsAnimeHint = anime || animation,
                                        Adult = segs.Any(x => x.Equals("NC17", StringComparison.OrdinalIgnoreCase)) || Regex.IsMatch(name, @"\bXXX\b", RegexOptions.IgnoreCase) };
                        titles[key] = t;
                    }
                    if (t.Year == 0 && year > 0) t.Year = year;
                    string full = v.Root + f.Rel;
                    t.Files.Add(full);
                    byFile[full] = t;
                    t.Size += f.Size;
                    if (f.Mod > t.Added) t.Added = f.Mod;
                    if (!t.Drives.Contains(v.Display)) t.Drives.Add(v.Display);
                    if (v.Online) t.Online = true;
                    if (tv)
                    {
                        t.Episodes++;
                        var sm = Regex.Match(fileName, @"[Ss](\d{1,2})[Ee]\d{1,3}|\b(\d{1,2})x\d{2}\b");
                        if (sm.Success) t.Seasons.Add(int.Parse(sm.Groups[1].Success && sm.Groups[1].Value.Length > 0 ? sm.Groups[1].Value : sm.Groups[2].Value));
                        if (t.Path == null || (v.Online && !PathOnline(t.Path))) t.Path = folder;
                    }
                    else
                    {
                        bool curOnline = t.Path != null && PathOnline(t.Path);
                        if (t.Path == null || (v.Online && !curOnline) || (v.Online == curOnline && f.Size > t.MainSize)) { t.Path = full; t.Folder = folder; t.MainSize = f.Size; }
                    }
                    if (f.Quality.Length > 0 && QRank(f.Quality) > QRank(t.Quality)) { t.Quality = f.Quality; t.Res = f.W > 0 ? f.W + "\u00D7" + f.H : ""; }
                    if (!tv) t.DurSec = Math.Max(t.DurSec, f.DurSec);
                    foreach (string artDir in tv && folder.Length > v.Root.Length ? new[] { folder.Substring(v.Root.Length), Catalog.SDir(f.Rel) } : new[] { Catalog.SDir(f.Rel) })
                    {
                        KeyValuePair<string, int> art;
                        if (!imgs.TryGetValue(artDir, out art)) continue;
                        int r = art.Value;
                        string an = Catalog.SStem(art.Key).ToLowerInvariant();
                        if (!tv && an.StartsWith(stem.ToLowerInvariant())) r += 5;               // "<movie>-poster" for this very file
                        if (tv && artDir.Length < Catalog.SDir(f.Rel).Length) r += 5;            // the show's own poster beats a season poster
                        if (v.Online) r += 1;
                        if (r > t.PosterRank) { t.Poster = v.Root + art.Key; t.PosterRank = r; }
                    }
                }
            }
            lock (Sync)
            {
                foreach (var t in titles.Values)
                {
                    MetaInfo m; if (Meta.TryGetValue(t.Key, out m) && m.Ok) t.Meta = m;
                    if (t.Poster.Length == 0) t.Poster = Posters.Get(t.Key);
                    // Anime = Japanese animation; everything else animated (Pixar, DC animated, Laika ...) is Animation
                    if (t.Kind == "Movie" && t.IsAnimeHint) t.Kind = t.Meta != null && IsJapaneseAnimation(t.Meta) ? "Anime" : "Animation";
                    if (t.Meta != null && t.Kind == "TV" && IsJapaneseAnimation(t.Meta)) t.Kind = "Anime";
                }
                Titles = titles.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
                ByFile = byFile;
            }
            try { List<TripInfo> evs; var trips = FindTrips(vols, out evs); BuildHomeVideos(vols, byFile, trips, evs); } catch (Exception ex) { Log.Write("home videos: " + ex.Message); }
        }

        static bool PathOnline(string p)
        {
            var v = Catalog.VolumeOfPath(p);
            return v != null && v.Online;
        }
        static int QRank(string q)
        {
            if (string.IsNullOrEmpty(q)) return 0;
            if (q.StartsWith("4K")) return 5; if (q.StartsWith("1440p")) return 4; if (q.StartsWith("1080p")) return 3; if (q.StartsWith("720p")) return 2; return 1;
        }
        static bool IsJapaneseAnimation(MetaInfo m)
        {
            bool jaLang = m.Langs.Any(x => L(x).IndexOf("Japanese", StringComparison.OrdinalIgnoreCase) >= 0);
            bool enLang = m.Langs.Any(x => L(x) == "English");
            bool ja = jaLang || (m.Countries.Count > 0 && L(m.Countries[0]) == "Japan" && !enLang);
            bool anim = m.Genres.Concat(m.Types).Any(x => { string s = L(x).ToLowerInvariant(); return s.Contains("anime") || s.Contains("animat"); });
            return ja && anim;
        }
        public static string L(string q) { string s; lock (Sync) return Labels.TryGetValue(q, out s) ? s : ""; }
        static List<string> Ls(IEnumerable<string> qs) { return qs.Select(L).Where(s => s.Length > 0).ToList(); }

        // ------------------------------------------------------------ launcher items
        public static void Merge(List<Item> list)
        {
            List<Title> titles; Dictionary<string, Title> byFile;
            lock (Sync) { titles = Titles; byFile = ByFile; }
            if (titles.Count == 0) return;
            var movieFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in titles) if (t.Kind != "TV" && t.Episodes == 0) foreach (var f in t.Files) movieFiles.Add(f);
            list.RemoveAll(it => !it.IsDir && it.Kind == "video" && movieFiles.Contains(it.Path));
            foreach (var it in list)
            {
                Title t;
                if (it.Kind == "video" && byFile.TryGetValue(it.Path, out t))
                {
                    it.Cat = t.Kind == "Anime" || t.Kind == "Animation" ? t.Kind : "TV";
                    if (string.IsNullOrEmpty(it.Thumbnail)) it.Thumbnail = t.Poster;
                    it.Tags = (t.Name + " " + string.Join(" ", MetaWords(t))).ToLowerInvariant();
                    Engine.Refinish(it);
                }
            }
            List<HomeVideo> hvs; lock (Sync) hvs = HomeVideos;
            var hvSet = new HashSet<string>(hvs.Select(h => h.Path), StringComparer.OrdinalIgnoreCase);
            list.RemoveAll(it => !it.IsDir && it.Kind == "video" && hvSet.Contains(it.Path));
            foreach (var h in hvs)
            {
                var it = new Item();
                it.Name = h.Name; it.Path = h.Path; it.Ext = System.IO.Path.GetExtension(h.Path).ToLowerInvariant();
                it.Cat = "Photos"; it.Mode = "homevideo"; it.Kind = "video"; it.Modified = h.Date.Ticks; it.Offline = !h.Online; it.Where = h.Drive;
                var parts = new List<string> { "Home video" };
                if (h.Trip.Length > 0) parts.Add(h.Trip + " trip"); else if (h.Event.Length > 0) parts.Add(h.Event);
                parts.Add(h.Date.ToString("d MMM yyyy"));
                if (h.DurSec > 0) parts.Add(Catalog.Dur(h.DurSec));
                if (h.People.Count > 0) parts.Add(string.Join(", ", h.People));
                parts.Add(h.Online ? h.Drive : h.Drive + " \u2014 not connected");
                it.Sub = string.Join("  \u00B7  ", parts);
                it.Tags = ("home video clip " + h.Trip + " " + h.Event + " " + h.Activity + " " + string.Join(" ", h.People) + " " + h.Date.ToString("MMMM yyyy")).ToLowerInvariant();
                Engine.Refinish(it);
                list.Add(it);
            }
            foreach (var t in titles)
            {
                var it = new Item();
                it.Name = t.Name;
                it.Path = t.Path;
                it.IsDir = t.Kind != "Movie" && t.Episodes > 0;
                it.Ext = it.IsDir ? "" : System.IO.Path.GetExtension(t.Path ?? "").ToLowerInvariant();
                it.Cat = t.Kind == "Movie" ? "Movies" : t.Kind;
                it.Mode = "title";
                it.Kind = it.IsDir ? "folder" : "video";
                it.Modified = t.Added;
                it.Offline = !t.Online;
                it.Where = string.Join(", ", t.Drives);
                it.Person = t.Meta != null ? string.Join(", ", Ls(t.Meta.Directors.Take(2))) : "";
                it.Thumbnail = t.Poster;
                it.Sub = SubLine(t);
                it.Tags = string.Join(" ", MetaWords(t)).ToLowerInvariant();
                Engine.Refinish(it);
                list.Add(it);
            }
        }

        public static IEnumerable<string> MetaWords(Title t)
        {
            var w = new List<string> { t.Kind == "Movie" ? "movie film" : t.Kind == "TV" ? "tv show series" : t.Kind == "Animation" ? "animation animated cartoon film" : "anime" };
            if (t.Year > 0) w.Add(t.Year.ToString());
            if (t.Quality.Length > 0) w.Add(t.Quality.Replace("\u00B7", " "));
            var m = t.Meta;
            if (m != null)
            {
                w.AddRange(Ls(m.Directors)); w.AddRange(Ls(m.Cast)); w.AddRange(Ls(m.Langs)); w.AddRange(Ls(m.Countries)); w.AddRange(Ls(m.Genres));
                if (m.Label.Length > 0 && !m.Label.Equals(t.Name, StringComparison.OrdinalIgnoreCase)) w.Add(m.Label);
            }
            return w;
        }

        static string SubLine(Title t)
        {
            var p = new List<string>();
            p.Add(t.Kind == "Movie" ? "Movie" : t.Kind == "TV" ? "TV show" : t.Kind == "Animation" ? (t.Episodes > 0 ? "Animated series" : "Animated film") : "Anime");
            int y = t.Year > 0 ? t.Year : t.Meta != null ? t.Meta.Year : 0;
            if (y > 0) p.Add(y.ToString());
            if (t.Meta != null)
            {
                var d = Ls(t.Meta.Directors.Take(2)); if (d.Count > 0) p.Add(string.Join(", ", d));
                var l = Ls(t.Meta.Langs.Take(2)).Select(s => s.Replace(" language", "")); if (l.Any()) p.Add(string.Join("/", l));
            }
            if (t.Episodes > 0) p.Add(t.Episodes + " episode" + (t.Episodes == 1 ? "" : "s") + (t.Seasons.Count > 1 ? " \u00B7 " + t.Seasons.Count + " seasons" : ""));
            if (t.Quality.Length > 0) p.Add(t.Quality.Split('\u00B7')[0].Trim());
            p.Add(Catalog.SizeText(t.Size));
            p.Add(t.Online ? string.Join(", ", t.Drives) : string.Join(", ", t.Drives) + " \u2014 not connected");
            return string.Join("  \u00B7  ", p);
        }

        // ------------------------------------------------------------ metadata store
        public static void Init(string dir, Action onChanged)
        {
            changed = onChanged;
            metaFile = System.IO.Path.Combine(dir, "media-info.json");
            try
            {
                if (File.Exists(metaFile))
                {
                    var js = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var root = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(metaFile, Encoding.UTF8));
                    object o;
                    if (root.TryGetValue("labels", out o) && o is Dictionary<string, object>)
                        foreach (var kv in (Dictionary<string, object>)o) Labels[kv.Key] = Convert.ToString(kv.Value);
                    if (root.TryGetValue("titles", out o) && o is Dictionary<string, object>)
                        foreach (var kv in (Dictionary<string, object>)o)
                        {
                            var d = kv.Value as Dictionary<string, object>; if (d == null) continue;
                            var m = new MetaInfo();
                            m.Q = S(d, "q"); m.Label = S(d, "label"); m.Desc = S(d, "desc"); m.Imdb = S(d, "imdb");
                            m.Year = (int)N(d, "year"); m.Minutes = (int)N(d, "min"); m.Tried = N(d, "tried"); m.Ok = S(d, "ok") == "1";
                            m.Directors = A(d, "dir"); m.Cast = A(d, "cast"); m.Langs = A(d, "lang"); m.Countries = A(d, "ctry"); m.Genres = A(d, "gen"); m.Types = A(d, "type");
                            Meta[kv.Key] = m;
                        }
                }
            }
            catch (Exception ex) { Log.Write("media-info load: " + ex.Message); }
            LoadPeople(dir);
            var t = new Thread(Loop); t.IsBackground = true; t.Priority = ThreadPriority.BelowNormal; t.Start();
            Posters.Init(dir);
        }

        static void LoadPeople(string dir)
        {
            string f = System.IO.Path.Combine(dir, "people-and-places.json");
            try
            {
                if (!File.Exists(f))
                    File.WriteAllText(f, "{\r\n  \"_help\": \"Names here are used to tag your home videos and photos. Add family and friends with any nicknames that appear in folder or file names. 'places' maps short folder names to trip names.\",\r\n" +
                        "  \"people\": {\r\n    \"Me\": [\"" + (Settings.UserName.Length > 0 ? Settings.UserName : Environment.UserName) + "\"]\r\n  },\r\n" +
                        "  \"places\": {\r\n    \"NYC\": \"New York\"\r\n  }\r\n}\r\n", new UTF8Encoding(false));
                var root = Json(File.ReadAllText(f, Encoding.UTF8));
                var list = new List<KeyValuePair<string, string[]>>();
                var pd = D(root, "people");
                if (pd != null) foreach (var kv in pd)
                {
                    var al = new List<string> { kv.Key };
                    var arr = kv.Value as System.Collections.ArrayList;
                    if (arr != null) foreach (var a in arr) al.Add(Convert.ToString(a));
                    list.Add(new KeyValuePair<string, string[]>(kv.Key, al.Where(x => x.Trim().Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
                }
                var al2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var pl = D(root, "places");
                if (pl != null) foreach (var kv in pl) al2[kv.Key] = Convert.ToString(kv.Value);
                lock (Sync) { people = list; placeAlias = al2; }
            }
            catch (Exception ex) { Log.Write("people-and-places: " + ex.Message); }
        }

        public static DateTime DateFromName(string n, long fallbackTicks)
        {
            var m = Regex.Match(n, @"(?<!\d)(20\d{2}|19\d{2})[-_\.]?(0[1-9]|1[0-2])[-_\.]?(0[1-9]|[12]\d|3[01])(?!\d)");
            DateTime d;
            if (m.Success && DateTime.TryParse(m.Groups[1].Value + "-" + m.Groups[2].Value + "-" + m.Groups[3].Value, out d)) return d;
            return new DateTime(Math.Max(fallbackTicks, DateTime.MinValue.Ticks));
        }

        static string Place(string seg)
        {
            string a; lock (Sync) if (placeAlias.TryGetValue(seg, out a)) return a;
            string c = Regex.Replace(seg, @"[_]+", " ");
            c = Regex.Replace(c, @"\s*[-@]\s*\d{2}\.\d{2}\.\d{2,4}$", "");
            c = Regex.Replace(c, @"\s+", " ").Trim(' ', '-', '_', '!');
            return c;
        }

        public static List<string> PeopleIn(string text)
        {
            var r = new List<string>();
            List<KeyValuePair<string, string[]>> ps; lock (Sync) ps = people;
            foreach (var p in ps)
                foreach (var a in p.Value)
                    if (Regex.IsMatch(text, @"(?<![A-Za-z])" + Regex.Escape(a) + @"(?![A-Za-z])", RegexOptions.IgnoreCase)) { r.Add(p.Key); break; }
            return r;
        }

        // trips = dated albums ("2025-12 Japan", "2025-12-14 - Japan - 001.jpg"); events = named photo folders ("Himachal", "Durga Puja _15 @IIMI").
        // Each gets the date range of its photos (5th-95th percentile, so a few mis-dated files don't stretch it).
        static List<TripInfo> FindTrips(List<CatVolume> vols, out List<TripInfo> events)
        {
            var trips = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
            var evs = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in vols)
                foreach (var f in v.Files)
                {
                    if (f.Type != "Image" && f.Type != "Video") continue;
                    string name = Catalog.SFile(f.Rel), tripName = null, evName = null;
                    var segs = f.Rel.Split('\\');
                    var fm = Regex.Match(name, @"^(\d{4}-\d{2}-\d{2}) - (.+?) - \d{2,4}");
                    if (fm.Success) tripName = fm.Groups[2].Value;
                    int rootIdx = -1;
                    for (int i = 0; i < segs.Length - 1; i++)
                    {
                        string seg = segs[i], l = seg.ToLowerInvariant();
                        var sm = Regex.Match(seg, @"^(\d{4})-(\d{2})(?:-\d{2})?\s+(.+)$");
                        if (sm.Success && tripName == null) tripName = sm.Groups[3].Value;
                        if (rootIdx < 0 && Array.IndexOf(PhotoRoots, l) >= 0) { rootIdx = i; continue; }
                        if (rootIdx >= 0 && evName == null && Array.IndexOf(PhotoGeneric, l) < 0 && !Regex.IsMatch(l, @"^\d{3,4}[a-z_]*$|^\d{4}-\d{2}")) evName = seg;
                    }
                    bool named = Regex.IsMatch(name, @"(19|20)\d{2}[-_\.]?(0[1-9]|1[0-2])[-_\.]?(0[1-9]|[12]\d|3[01])");
                    var d = DateFromName(name, f.Mod);
                    if (d.Year < 1990) continue;
                    if (tripName != null && !Regex.IsMatch(tripName, @"whatsapp|screenshot|wallpaper|profile|misc|phone|camera|complaint|scan", RegexOptions.IgnoreCase))
                    {
                        string tn = Place(tripName); List<DateTime> l1; if (!trips.TryGetValue(tn, out l1)) { l1 = new List<DateTime>(); trips[tn] = l1; } l1.Add(d.Date);
                    }
                    else if (evName != null && (named || f.Type == "Image"))
                    {
                        string en = Place(evName); string alias; bool isPlace; lock (Sync) isPlace = placeAlias.TryGetValue(evName, out alias);
                        if (isPlace) { List<DateTime> l2; if (!trips.TryGetValue(alias, out l2)) { l2 = new List<DateTime>(); trips[alias] = l2; } l2.Add(d.Date); }
                        else { List<DateTime> l3; if (!evs.TryGetValue(en, out l3)) { l3 = new List<DateTime>(); evs[en] = l3; } l3.Add(d.Date); }
                    }
                }
            Func<Dictionary<string, List<DateTime>>, int, int, List<TripInfo>> ranges = (dict, minCount, maxDays) =>
            {
                var r = new List<TripInfo>();
                foreach (var kv in dict)
                {
                    if (kv.Value.Count < minCount) continue;
                    var ds = kv.Value.OrderBy(x => x).ToList();
                    var t = new TripInfo { Name = kv.Key, Photos = ds.Count, Start = ds[(int)(ds.Count * 0.05)], End = ds[Math.Min(ds.Count - 1, (int)(ds.Count * 0.95))] };
                    if ((t.End - t.Start).TotalDays <= maxDays) r.Add(t);
                }
                return r;
            };
            events = ranges(evs, 8, 21);
            return ranges(trips, 10, 45);
        }

        static void BuildHomeVideos(List<CatVolume> vols, Dictionary<string, Title> byFile, List<TripInfo> trips, List<TripInfo> events)
        {
            var list = new List<HomeVideo>();
            foreach (var v in vols)
                foreach (var f in v.Files)
                {
                    if (f.Type != "Video") continue;
                    string full = v.Root + f.Rel;
                    if (byFile.ContainsKey(full)) continue;
                    var segs = f.Rel.Split('\\');
                    var dirs = segs.Take(segs.Length - 1).ToArray();
                    string name = Catalog.SFile(f.Rel);
                    bool cam = Regex.IsMatch(name, @"^(IMG|VID|MOV|PXL|DJI|GOPR|GH0|GX0|DSC|MVI|MAH|C\d{4}|VID-\d{8}-WA|WhatsApp Video|Screen ?Recording|\d{8}_\d{6})", RegexOptions.IgnoreCase);
                    int rootIdx = Array.FindIndex(dirs, d => Array.IndexOf(PhotoRoots, d.ToLowerInvariant()) >= 0 || Regex.IsMatch(d, @"^\d{4}-\d{2}"));
                    if (rootIdx < 0 && !cam) continue;
                    if (dirs.Any(d => Regex.IsMatch(d, @"^(software|projects|sam_work|gen ai|ic_work|media server|movies|anime|nc17)$", RegexOptions.IgnoreCase))) continue;
                    var hv = new HomeVideo { Path = full, Name = Catalog.SStem(f.Rel), Size = f.Size, DurSec = f.DurSec, Quality = f.Quality, Drive = v.Display, Online = v.Online };
                    hv.Date = DateFromName(name, f.Mod);
                    // trip / event from the folder names
                    var fm = Regex.Match(name, @"^(\d{4}-\d{2}-\d{2}) - (.+?) - \d{2,4}");
                    if (fm.Success) hv.Trip = Place(fm.Groups[2].Value);
                    for (int i = Math.Max(0, rootIdx); i < dirs.Length && hv.Trip.Length == 0; i++)
                    {
                        var sm = Regex.Match(dirs[i], @"^(\d{4})-(\d{2})(?:-\d{2})?\s+(.+)$");
                        if (sm.Success && !Regex.IsMatch(sm.Groups[3].Value, @"whatsapp|screenshot", RegexOptions.IgnoreCase)) hv.Trip = Place(sm.Groups[3].Value);
                    }
                    if (hv.Trip.Length == 0)
                    {
                        var t = trips.FirstOrDefault(x => hv.Date.Date >= x.Start.AddDays(-1) && hv.Date.Date <= x.End.AddDays(1));
                        if (t != null) hv.Trip = t.Name;
                    }
                    bool datedName = Regex.IsMatch(name, @"(19|20)\d{2}[-_\.]?(0[1-9]|1[0-2])[-_\.]?(0[1-9]|[12]\d|3[01])");
                    for (int i = Math.Max(0, rootIdx); i < dirs.Length; i++)
                    {
                        string d = dirs[i], l = d.ToLowerInvariant();
                        if (Array.IndexOf(PhotoGeneric, l) >= 0 || Array.IndexOf(PhotoRoots, l) >= 0 || Regex.IsMatch(l, @"^\d{3,4}[a-z_]*$|^\d{4}-\d{2}")) continue;
                        string place; bool aliased; lock (Sync) aliased = placeAlias.TryGetValue(d, out place);
                        if (aliased && hv.Trip.Length == 0) { hv.Trip = place; continue; }
                        if (hv.Event.Length == 0) hv.Event = Place(d);
                    }
                    if (hv.Trip.Length == 0 && hv.Event.Length == 0 && datedName)
                    {
                        var e = events.FirstOrDefault(x => hv.Date.Date >= x.Start && hv.Date.Date <= x.End);
                        if (e != null) hv.Event = e.Name;
                    }
                    string act = ActivityWords.FirstOrDefault(w => Regex.IsMatch(f.Rel, @"(?<![a-z])" + w + @"(?![a-z])", RegexOptions.IgnoreCase));
                    if (act != null) hv.Activity = hv.Event.Length > 0 ? hv.Event : char.ToUpper(act[0]) + act.Substring(1);
                    hv.People = PeopleIn(f.Rel);
                    list.Add(hv);
                }
            lock (Sync) { HomeVideos = list.OrderByDescending(x => x.Date).ToList(); Trips = trips; }
        }

        static string S(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? Convert.ToString(o) : ""; }
        static long N(Dictionary<string, object> d, string k) { object o; long v = 0; if (d.TryGetValue(k, out o) && o != null) long.TryParse(Convert.ToString(o), out v); return v; }
        static List<string> A(Dictionary<string, object> d, string k)
        {
            object o; var r = new List<string>();
            if (d.TryGetValue(k, out o) && o is System.Collections.IEnumerable && !(o is string)) foreach (var x in (System.Collections.IEnumerable)o) r.Add(Convert.ToString(x));
            return r;
        }

        static void SaveMeta()
        {
            try
            {
                var titles = new Dictionary<string, object>();
                Dictionary<string, string> labels;
                lock (Sync)
                {
                    foreach (var kv in Meta)
                    {
                        var m = kv.Value;
                        titles[kv.Key] = new Dictionary<string, object> {
                            { "q", m.Q }, { "label", m.Label }, { "desc", m.Desc }, { "imdb", m.Imdb }, { "year", m.Year }, { "min", m.Minutes },
                            { "tried", m.Tried }, { "ok", m.Ok ? "1" : "0" }, { "dir", m.Directors }, { "cast", m.Cast }, { "lang", m.Langs },
                            { "ctry", m.Countries }, { "gen", m.Genres }, { "type", m.Types } };
                    }
                    labels = new Dictionary<string, string>(Labels);
                }
                var js = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                string tmp = metaFile + ".tmp";
                File.WriteAllText(tmp, js.Serialize(new Dictionary<string, object> { { "labels", labels }, { "titles", titles } }), new UTF8Encoding(false));
                if (File.Exists(metaFile)) File.Delete(metaFile);
                File.Move(tmp, metaFile);
            }
            catch (Exception ex) { Log.Write("media-info save: " + ex.Message); }
        }

        public static void Kick() { wake.Set(); }

        static void Loop()
        {
            wake.WaitOne(TimeSpan.FromSeconds(40));
            while (true)
            {
                try { Enrich(); } catch (Exception ex) { Log.Write("enrich: " + ex); StatusText = ""; }
                if (!hintsDone) { hintsDone = true; try { WriteScraperHints(); } catch (Exception ex) { Log.Write("hints: " + ex.Message); } }
                wake.WaitOne(TimeSpan.FromMinutes(20));
            }
        }

        // ------------------------------------------------------------ Wikidata
        internal static string Get(string url)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.UserAgent = "DesktopOrganizer/1.0 (personal media catalogue on a home PC)";
                    req.Timeout = 20000; req.ReadWriteTimeout = 20000;
                    req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return r.ReadToEnd();
                }
                catch (WebException ex)
                {
                    var hr = ex.Response as HttpWebResponse;
                    if (hr != null && (int)hr.StatusCode == 429) Thread.Sleep(5000 * (attempt + 1));
                    else if (attempt == 2) throw; else Thread.Sleep(1500);
                }
            }
            return null;
        }

        internal static Dictionary<string, object> Json(string s)
        {
            var js = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return js.Deserialize<Dictionary<string, object>>(s);
        }
        internal static Dictionary<string, object> D(object o, string k) { var d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null; }
        internal static System.Collections.ArrayList Arr(object o, string k) { var d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(k, out v) ? v as System.Collections.ArrayList : null; }
        internal static string Str(object o, string k) { var d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; }

        static readonly string[] FilmWords = { "film", "movie", "documentary" };
        static readonly string[] TvWords = { "series", "television", "tv ", "miniseries", "anime", "docuseries", "programme", "program", "sitcom" };

        public static string PickCandidate(string json, string name, int year, bool tv)
        {
            var root = Json(json);
            var list = Arr(root, "search");
            if (list == null) return null;
            string want = Norm(name);
            string best = null; int bestScore = int.MinValue;
            int rank = 0;
            foreach (var o in list)
            {
                rank++;
                string id = Str(o, "id"), label = Str(o, "label"), desc = Str(o, "description").ToLowerInvariant();
                bool typeOk = tv ? TvWords.Any(w => desc.Contains(w)) : FilmWords.Any(w => desc.Contains(w)) && !desc.Contains("series");
                if (!typeOk) continue;
                int sc = 10 - rank;
                if (Norm(label) == want) sc += 6;
                else { var al = Str(D(o, "match"), "text"); if (Norm(al) == want) sc += 4; }
                var ym = Regex.Match(desc, @"\b(18|19|20)\d\d\b");
                if (year > 0 && ym.Success)
                {
                    int dy = Math.Abs(int.Parse(ym.Value) - year);
                    if (dy == 0) sc += 8; else if (dy == 1) sc += 5; else if (!tv) sc -= 12;
                }
                if (sc > bestScore) { bestScore = sc; best = id; }
            }
            return best;
        }

        static List<string> Variants(Title t)
        {
            var v = new List<string> { t.Name };
            // "James Bond Octopussy" inside a "James Bond" collection folder -> "Octopussy"
            string folder = t.Folder ?? "";
            var segs = folder.Split('\\');
            foreach (var seg in segs.Reverse().Take(3))
            {
                string c = Norm(seg), n = Norm(t.Name);
                if (c.Length > 2 && n.StartsWith(c + " ") && n.Length > c.Length + 2)
                {
                    string rest = t.Name.Substring(Math.Min(t.Name.Length, seg.Length)).Trim(' ', '-', ':', '_');
                    if (rest.Length > 1) v.Add(rest);
                }
            }
            // "Pirates of the Caribbean 2 Dead Man's Chest" -> "Pirates of the Caribbean Dead Man's Chest"
            string noNum = Regex.Replace(t.Name, @"\s\d\s", " ").Trim();
            if (noNum != t.Name) v.Add(noNum);
            string colon = Regex.Replace(t.Name, @"\s+-\s+", ": ");
            if (colon != t.Name) v.Add(colon);
            return v.Distinct().ToList();
        }

        static List<string> Claims(Dictionary<string, object> claims, string p, int max)
        {
            var r = new List<string>();
            var a = claims != null && claims.ContainsKey(p) ? claims[p] as System.Collections.ArrayList : null;
            if (a == null) return r;
            foreach (var st in a)
            {
                var dv = D(D(st, "mainsnak"), "datavalue");
                if (dv == null) continue;
                object val; if (!dv.TryGetValue("value", out val)) continue;
                var vd = val as Dictionary<string, object>;
                string s = vd != null ? (vd.ContainsKey("id") ? Convert.ToString(vd["id"]) : vd.ContainsKey("time") ? Convert.ToString(vd["time"]) : vd.ContainsKey("amount") ? Convert.ToString(vd["amount"]) : vd.ContainsKey("text") ? Convert.ToString(vd["text"]) : "") : Convert.ToString(val);
                if (s.Length > 0 && !r.Contains(s)) r.Add(s);
                if (r.Count >= max) break;
            }
            return r;
        }

        public static MetaInfo ParseEntity(string json, string q)
        {
            var root = Json(json);
            var e = D(D(root, "entities"), q);
            if (e == null) return null;
            var m = new MetaInfo { Q = q, Ok = true };
            m.Label = Str(D(D(e, "labels"), "en"), "value");
            m.Desc = Str(D(D(e, "descriptions"), "en"), "value");
            var c = D(e, "claims");
            m.Types = Claims(c, "P31", 6);
            m.Directors = Claims(c, "P57", 4);
            m.Cast = Claims(c, "P161", 12);
            m.Langs = Claims(c, "P364", 3);
            m.Countries = Claims(c, "P495", 3);
            m.Genres = Claims(c, "P136", 6);
            var imdb = Claims(c, "P345", 1); if (imdb.Count > 0) m.Imdb = imdb[0];
            var dates = Claims(c, "P577", 10).Concat(Claims(c, "P580", 2)).ToList();
            foreach (var d in dates) { var ym = Regex.Match(d, @"^[+-]?(\d{4})"); if (ym.Success) { int y = int.Parse(ym.Groups[1].Value); if (m.Year == 0 || y < m.Year) m.Year = y; } }
            var dur = Claims(c, "P2047", 1); double mins; if (dur.Count > 0 && double.TryParse(dur[0].TrimStart('+'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out mins)) m.Minutes = (int)mins;
            return m;
        }

        public static void ParseLabels(string json, Dictionary<string, string> into)
        {
            var root = Json(json);
            var ents = D(root, "entities");
            if (ents == null) return;
            foreach (var kv in ents)
            {
                string l = Str(D(D(kv.Value, "labels"), "en"), "value");
                if (l.Length > 0) into[kv.Key] = l;
            }
        }

        static void Enrich()
        {
            List<Title> todo;
            lock (Sync)
            {
                long retry = DateTime.Now.AddDays(-14).Ticks;
                todo = Titles.Where(t => { MetaInfo m; return !Meta.TryGetValue(t.Key, out m) || (!m.Ok && m.Tried < retry); })
                             .Where(t => !Regex.IsMatch(t.Name, @"\bXXX\b|\b\d{2} \d{2} \d{2}\b", RegexOptions.IgnoreCase)).ToList();
            }
            if (todo.Count == 0) return;
            int done = 0, found = 0;
            var needLabels = new HashSet<string>();
            foreach (var t in todo)
            {
                StatusText = "Looking up films & shows  \u00B7  " + done + " of " + todo.Count;
                var m = new MetaInfo { Tried = DateTime.Now.Ticks };
                try
                {
                    string q = null;
                    foreach (var variant in Variants(t))
                    {
                        string s = Get("https://www.wikidata.org/w/api.php?action=wbsearchentities&format=json&language=en&uselang=en&type=item&limit=10&search=" + Uri.EscapeDataString(variant));
                        if (s != null) q = PickCandidate(s, variant, t.Year, t.Kind != "Movie");
                        if (q != null) break;
                        Thread.Sleep(150);
                    }
                    if (q == null)
                    {
                        string filter = t.Kind == "Movie" ? "haswbstatement:P31=Q11424|P31=Q202866|P31=Q93204|P31=Q24862|P31=Q506240" : "haswbstatement:P31=Q5398426|P31=Q581714|P31=Q63952888|P31=Q1259759|P31=Q15416";
                        string s2 = Get("https://www.wikidata.org/w/api.php?action=query&list=search&format=json&srlimit=5&srsearch=" + Uri.EscapeDataString(t.Name + (t.Year > 0 && t.Kind == "Movie" ? " " + t.Year : "") + " " + filter));
                        var hits = Arr(D(Json(s2), "query"), "search");
                        if (hits != null && hits.Count > 0) q = Str(hits[0], "title");
                    }
                    if (q != null)
                    {
                        string ej = Get("https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&languages=en&props=labels|descriptions|claims&ids=" + q);
                        var pm = ParseEntity(ej, q);
                        if (pm != null)
                        {
                            if (t.Year > 0 && pm.Year > 0 && Math.Abs(pm.Year - t.Year) > 2 && t.Kind == "Movie") pm = null;
                        }
                        if (pm != null)
                        {
                            pm.Tried = m.Tried; m = pm; found++;
                            foreach (var id in m.Directors.Concat(m.Cast).Concat(m.Langs).Concat(m.Countries).Concat(m.Genres).Concat(m.Types))
                                if (id.StartsWith("Q") && !Labels.ContainsKey(id)) needLabels.Add(id);
                        }
                    }
                }
                catch (Exception ex) { Log.Write("wikidata " + t.Name + ": " + ex.Message); m.Ok = false; if (ex is WebException && done == 0) { StatusText = ""; return; } }
                lock (Sync) Meta[t.Key] = m;
                done++;
                if (needLabels.Count >= 45) ResolveLabels(needLabels);
                if (done % 25 == 0) { ResolveLabels(needLabels); SaveMeta(); Apply(); }
                Thread.Sleep(250);
            }
            ResolveLabels(needLabels);
            SaveMeta();
            Log.Write("wikidata: looked up " + done + " titles, matched " + found);
            StatusText = "";
            Apply();
        }

        static void ResolveLabels(HashSet<string> ids)
        {
            while (ids.Count > 0)
            {
                var batch = ids.Take(48).ToList();
                foreach (var b in batch) ids.Remove(b);
                try
                {
                    string s = Get("https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&languages=en&props=labels&ids=" + string.Join("|", batch));
                    var tmp = new Dictionary<string, string>();
                    ParseLabels(s, tmp);
                    lock (Sync) foreach (var kv in tmp) Labels[kv.Key] = kv.Value;
                }
                catch (Exception ex) { Log.Write("labels: " + ex.Message); }
                Thread.Sleep(200);
            }
        }

        // Scraper hints: a film folder with no .nfo, which we could match on Wikidata, gets a minimal Kodi .nfo
        // carrying the English title and the IMDb id -- so tinyMediaManager (or Kodi/Plex) scrapes it by id even when
        // the file name is a romanised / non-English title it can't search for.
        static bool hintsDone;
        static void WriteScraperHints()
        {
            List<Title> titles; lock (Sync) titles = Titles.ToList();
            int n = 0;
            foreach (var t in titles)
            {
                try
                {
                    if (t.Episodes > 0 || t.Meta == null || !t.Meta.Ok || t.Meta.Imdb.Length == 0 || t.Path == null || !t.Online) continue;
                    string dir = System.IO.Path.GetDirectoryName(t.Path);
                    if (dir == null || !Directory.Exists(dir) || Directory.GetFiles(dir, "*.nfo").Length > 0) continue;
                    if (Directory.GetFiles(dir).Count(f => Regex.IsMatch(f, @"\.(mkv|mp4|avi|m4v|mov)$", RegexOptions.IgnoreCase)) > 3) continue;   // a folder of many films
                    string nfo = System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(t.Path) + ".nfo");
                    string label = System.Security.SecurityElement.Escape(t.Meta.Label.Length > 0 ? t.Meta.Label : t.Name);
                    int year = t.Meta.Year > 0 ? t.Meta.Year : t.Year;
                    File.WriteAllText(nfo, "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<movie>\r\n  <title>" + label + "</title>\r\n" +
                        (year > 0 ? "  <year>" + year + "</year>\r\n" : "") +
                        "  <uniqueid type=\"imdb\" default=\"true\">" + t.Meta.Imdb + "</uniqueid>\r\n  <id>" + t.Meta.Imdb + "</id>\r\n</movie>\r\n", new UTF8Encoding(false));
                    n++;
                }
                catch (Exception ex) { Log.Write("hint " + t.Name + ": " + ex.Message); }
            }
            if (n > 0) Log.Write("scraper hints: wrote " + n + " .nfo files with IMDb ids");
        }

        internal static void Apply()
        {
            Rebuild();
            try { Dashboard.Write(); } catch (Exception ex) { Log.Write("dashboard: " + ex.Message); }
            var cb = changed; if (cb != null) cb();
        }

        public static void AfterCatalog()
        {
            Rebuild();
            try { Dashboard.Write(); } catch (Exception ex) { Log.Write("dashboard: " + ex.Message); }
            Kick();
        }
    }


    // ---------------------------------------------------------------- cover art download
    // For titles with no poster/folder art next to the files: the film's poster from its English Wikipedia
    // article (portrait images only), or the show poster from TVmaze. Cached in Drive Catalog\posters.
    public static class Posters
    {
        static string dir, indexFile;
        static readonly Dictionary<string, Dictionary<string, object>> map = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        static readonly AutoResetEvent wake = new AutoResetEvent(false);
        public static volatile string StatusText = "";

        public static void Init(string catDir)
        {
            dir = System.IO.Path.Combine(catDir, "posters");
            indexFile = System.IO.Path.Combine(dir, "index.json");
            try
            {
                Directory.CreateDirectory(dir);
                if (File.Exists(indexFile))
                {
                    var root = MediaLib.Json(File.ReadAllText(indexFile, Encoding.UTF8));
                    foreach (var kv in root) { var d = kv.Value as Dictionary<string, object>; if (d != null) map[kv.Key] = d; }
                }
            }
            catch (Exception ex) { Log.Write("posters load: " + ex.Message); }
            var t = new Thread(Loop); t.IsBackground = true; t.Priority = ThreadPriority.BelowNormal; t.Start();
        }

        public static string Get(string key)
        {
            lock (map)
            {
                Dictionary<string, object> d;
                if (dir == null || !map.TryGetValue(key, out d)) return "";
                string f = MediaLib.Str(d, "f");
                if (f.Length == 0) return "";
                string p = System.IO.Path.Combine(dir, f);
                return File.Exists(p) ? p : "";
            }
        }

        public static void Kick() { wake.Set(); }

        static void Loop()
        {
            wake.WaitOne(TimeSpan.FromSeconds(50));
            while (true)
            {
                try { Run(); } catch (Exception ex) { Log.Write("posters: " + ex.Message); StatusText = ""; }
                wake.WaitOne(TimeSpan.FromMinutes(30));
            }
        }

        static bool Needs(string key)
        {
            lock (map)
            {
                Dictionary<string, object> d;
                if (!map.TryGetValue(key, out d)) return true;
                if (MediaLib.Str(d, "f").Length > 0) return !File.Exists(System.IO.Path.Combine(dir, MediaLib.Str(d, "f")));
                long tried; long.TryParse(MediaLib.Str(d, "t"), out tried);
                return tried < DateTime.Now.AddDays(-10).Ticks;
            }
        }

        static void Run()
        {
            List<Title> todo;
            lock (MediaLib.Sync) todo = MediaLib.Titles.Where(t => t.PosterRank == 0 && Needs(t.Key)
                                   && !Regex.IsMatch(t.Name, @"\bXXX\b|\b\d{2} \d{2} \d{2}\b", RegexOptions.IgnoreCase)).ToList();
            if (todo.Count == 0) return;
            int done = 0, got = 0;
            foreach (var t in todo)
            {
                StatusText = "Fetching cover art  \u00B7  " + done + " of " + todo.Count;
                string url = null, file = "";
                try
                {
                    if (t.Kind != "Movie") url = TvMaze(t);
                    if (url == null) url = Wiki(t);
                    if (url != null)
                    {
                        file = Regex.Replace(t.Key, @"[^a-z0-9]+", "_").Trim('_') + ".jpg";
                        Download(url, System.IO.Path.Combine(dir, file));
                        got++;
                    }
                }
                catch (Exception ex)
                {
                    var hr = ex is WebException ? ((WebException)ex).Response as HttpWebResponse : null;
                    if (hr != null && (int)hr.StatusCode == 429)
                    {
                        // Wikimedia asks bots to slow down: leave this title for the next pass and back off
                        Log.Write("posters: rate-limited, pausing");
                        done++; Thread.Sleep(60000); continue;
                    }
                    Log.Write("poster " + t.Name + ": " + ex.Message); file = "";
                    if (ex is WebException && done == 0 && got == 0) { StatusText = ""; return; }
                }
                lock (map) map[t.Key] = new Dictionary<string, object> { { "f", file }, { "t", DateTime.Now.Ticks.ToString() }, { "s", url ?? "" }, { "n", t.Name } };
                done++;
                if (done % 15 == 0) { Save(); if (got > 0) MediaLib.Apply(); }
                Thread.Sleep(url != null && url.Contains("wikimedia.org") ? 4000 : 600);   // be gentle with Wikimedia's image servers
            }
            Save();
            Log.Write("posters: " + got + " of " + todo.Count + " found");
            StatusText = "";
            if (got > 0) MediaLib.Apply();
        }

        static void Save()
        {
            try
            {
                string json;
                lock (map) json = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(map);
                File.WriteAllText(indexFile + ".tmp", json, new UTF8Encoding(false));
                if (File.Exists(indexFile)) File.Delete(indexFile);
                File.Move(indexFile + ".tmp", indexFile);
            }
            catch (Exception ex) { Log.Write("posters save: " + ex.Message); }
        }

        static string Clean(string s) { return MediaLib.Norm(Regex.Replace(s ?? "", @"\s*\([^)]*\)\s*$", "")); }

        static string TvMaze(Title t)
        {
            string s = MediaLib.Get("https://api.tvmaze.com/singlesearch/shows?q=" + Uri.EscapeDataString(t.Name));
            if (string.IsNullOrEmpty(s)) return null;
            var d = MediaLib.Json(s);
            string name = MediaLib.Str(d, "name"), prem = MediaLib.Str(d, "premiered");
            int y = prem.Length >= 4 ? int.Parse(prem.Substring(0, 4)) : 0;
            bool same = Clean(name) == Clean(t.Name) || Clean(name).StartsWith(Clean(t.Name) + " ");
            if (!same) return null;
            if (t.Year > 0 && y > 0 && Math.Abs(y - t.Year) > 1) return null;
            var img = MediaLib.D(d, "image");
            string u = MediaLib.Str(img, "original"); if (u.Length == 0) u = MediaLib.Str(img, "medium");
            return u.Length > 0 ? u : null;
        }

        static string Wiki(Title t)
        {
            var m = t.Meta;
            string name = m != null && m.Label.Length > 0 ? m.Label : t.Name;
            int year = t.Year > 0 ? t.Year : m != null ? m.Year : 0;
            string q = name + (t.Kind == "Movie" ? (year > 0 ? " " + year : "") + " film" : t.Kind == "Anime" ? " anime" : " TV series");
            string s = MediaLib.Get("https://en.wikipedia.org/w/api.php?action=query&format=json&generator=search&gsrnamespace=0&gsrlimit=6&gsrsearch="
                + Uri.EscapeDataString(q) + "&prop=pageimages%7Cpageprops&ppprop=wikibase_item&piprop=thumbnail&pithumbsize=600&pilicense=any");
            if (string.IsNullOrEmpty(s)) return null;
            var pages = MediaLib.D(MediaLib.D(MediaLib.Json(s), "query"), "pages");
            if (pages == null) return null;
            var list = pages.Values.OfType<Dictionary<string, object>>().OrderBy(p => { int i; int.TryParse(MediaLib.Str(p, "index"), out i); return i; }).ToList();
            Dictionary<string, object> pick = null;
            if (m != null && m.Q.Length > 0) pick = list.FirstOrDefault(p => MediaLib.Str(MediaLib.D(p, "pageprops"), "wikibase_item") == m.Q);
            if (pick == null && (m == null || m.Q.Length == 0))
                pick = list.FirstOrDefault(p => { string c = Clean(MediaLib.Str(p, "title")); return c == MediaLib.Norm(name) || c == MediaLib.Norm(t.Name); });
            if (pick == null) return null;
            var th = MediaLib.D(pick, "thumbnail");
            if (th == null) return null;
            int w, h; int.TryParse(MediaLib.Str(th, "width"), out w); int.TryParse(MediaLib.Str(th, "height"), out h);
            if (w <= 0 || h < w * 1.15) return null;     // posters are portrait; skip stills, logos, title cards
            string u = MediaLib.Str(th, "source");
            return u.Length > 0 ? u : null;
        }

        static void Download(string url, string path)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "DesktopOrganizer/1.0 (personal media catalogue on a home PC)";
            req.Timeout = 20000; req.ReadWriteTimeout = 20000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var rs = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                rs.CopyTo(ms);
                if (ms.Length < 2000) throw new Exception("image too small");
                File.WriteAllBytes(path, ms.ToArray());
            }
        }
    }


    // ---------------------------------------------------------------- duplicate films & episodes
    // Same film (or same episode) more than once: keep the best copy -- resolution, HDR, source (remux > Blu-ray > web),
    // codec-adjusted bitrate -- and set the others aside in "<drive>\_Duplicates (review)". Different editions
    // (extended, director's cut, dubbed ...) are never treated as duplicates. Nothing is deleted.
    public static class Duplicates
    {
        public class Pick { public string Title, Keep, Drop, Why; public long DropSize; }

        static readonly Regex Edition = new Regex(@"\b(extended|director'?s|dub(bed)?|unrated|theatrical|uncut|imax|special edition|hindi|tamil|telugu|english|japanese)\b", RegexOptions.IgnoreCase);
        static readonly Regex Part = new Regex(@"\b(cd|disc|disk|part|pt)\s*(\d+)\b", RegexOptions.IgnoreCase);
        static readonly Regex Ep = new Regex(@"[Ss](\d{1,2})[Ee](\d{1,3})|\b(\d{1,2})x(\d{2})\b| - Special (\d+) - ");

        static double[] Score(string path, CatFile f, out string label)
        {
            int w = f != null ? f.W : 0, h = f != null ? f.H : 0;
            int tier = (w >= 3200 || h >= 2000) ? 2160 : (w >= 1800 || h >= 1000) ? 1080 : (w >= 1200 || h >= 700) ? 720 : h > 0 ? 480 : 0;
            string p = path + " " + (f != null ? f.Quality : "");
            int cam = Regex.IsMatch(p, @"\b(cam|hdcam|ts|telesync|hdts)\b", RegexOptions.IgnoreCase) ? -1 : 0;
            int hdr = Regex.IsMatch(p, @"\bhdr|dolby.?vision|\bdv\b", RegexOptions.IgnoreCase) ? 1 : 0;
            int src = Regex.IsMatch(p, "remux", RegexOptions.IgnoreCase) ? 3 : Regex.IsMatch(p, @"blu-?ray|bdrip|brrip", RegexOptions.IgnoreCase) ? 2 : Regex.IsMatch(p, "web", RegexOptions.IgnoreCase) ? 1 : 0;
            bool hevc = Regex.IsMatch((f != null ? f.Codec : "") + " " + p, @"hevc|265|av1", RegexOptions.IgnoreCase);
            double eff = (f != null ? f.Kbps : 0) * (hevc ? 1.5 : 1.0);
            label = (tier > 0 ? (tier == 2160 ? "4K" : tier + "p") : "?") + (hdr > 0 ? " HDR" : "") + (hevc ? " HEVC" : "") + (f != null && f.Kbps > 0 ? " " + f.Kbps + " kbps" : "");
            return new double[] { cam, tier, hdr, src, eff, f != null ? f.Size : 0 };
        }

        public static List<Pick> Find()
        {
            var res = new List<Pick>();
            List<Title> titles; lock (MediaLib.Sync) titles = MediaLib.Titles.ToList();
            var files = new Dictionary<string, CatFile>(StringComparer.OrdinalIgnoreCase);
            List<CatVolume> vols; lock (Catalog.Sync) vols = Catalog.Vols.Values.Where(v => v.Online).ToList();
            foreach (var v in vols) foreach (var f in v.Files) if (f.Type == "Video") files[v.Root + f.Rel] = f;
            foreach (var t in titles)
            {
                if (t.Adult) continue;                                    // scene packs with generic names group loosely -- never guess there
                var groups = new Dictionary<string, List<string>>();
                foreach (var path in t.Files)
                {
                    if (!files.ContainsKey(path)) continue;          // only copies on connected drives
                    string fn = System.IO.Path.GetFileNameWithoutExtension(path);
                    string key;
                    var m = Ep.Match(fn);
                    if (t.Episodes > 0) { if (!m.Success) continue; key = m.Groups[1].Success && m.Groups[1].Length > 0 ? m.Groups[1].Value + "x" + int.Parse(m.Groups[2].Value) : m.Groups[3].Success && m.Groups[3].Length > 0 ? m.Groups[3].Value + "x" + int.Parse(m.Groups[4].Value) : "0x" + m.Groups[5].Value; key = Regex.Replace(key, "^0+(?=\\d)", ""); }
                    else
                    {
                        if (Regex.IsMatch(fn, @"^(scene|clip|video|title|vts)[ _-]*\d*$", RegexOptions.IgnoreCase)) continue;
                        var pm = Part.Match(fn); key = pm.Success ? pm.Groups[1].Value.ToLowerInvariant().Substring(0, 1) + pm.Groups[2].Value : "main";
                    }
                    List<string> g; if (!groups.TryGetValue(key, out g)) { g = new List<string>(); groups[key] = g; }
                    g.Add(path);
                }
                foreach (var g in groups.Values.Where(x => x.Count > 1))
                {
                    var editions = g.Select(p => string.Join("+", Edition.Matches(System.IO.Path.GetFileName(p) + " " + System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(p) ?? "")).Cast<Match>().Select(x => x.Value.ToLowerInvariant().Replace("'", "")).Distinct().OrderBy(x => x))).Distinct().ToList();
                    if (editions.Count > 1) continue;            // e.g. extended vs theatrical, dub vs original audio
                    var ranked = g.Select(p => { string lab; var sc = Score(p, files[p], out lab); return new { p, sc, lab, organised = Directory.Exists(System.IO.Path.GetDirectoryName(p)) && Directory.GetFiles(System.IO.Path.GetDirectoryName(p), "*.nfo").Length > 0 }; })
                                  .OrderByDescending(x => x.sc[0]).ThenByDescending(x => x.sc[1]).ThenByDescending(x => x.sc[2]).ThenByDescending(x => x.sc[3]).ThenByDescending(x => x.sc[4]).ToList();
                    var best = ranked[0];
                    // near-equal quality (same resolution, bitrate within 30%): keep the copy that's already organised, then the bigger one
                    var ties = ranked.Where(x => x.sc[1] == best.sc[1] && x.sc[2] == best.sc[2] && x.sc[4] > 0 && best.sc[4] / x.sc[4] < 1.3).ToList();
                    if (ties.Count > 1) best = ties.OrderByDescending(x => x.organised).ThenByDescending(x => x.sc[5]).First();
                    foreach (var x in ranked.Where(x => x != best))
                        res.Add(new Pick { Title = t.Name + (t.Year > 0 ? " (" + t.Year + ")" : ""), Keep = best.p, Drop = x.p, DropSize = (long)x.sc[5], Why = "keeps " + best.lab + " over " + x.lab });
                }
            }
            return res;
        }

        // move a lower-quality copy (and its subtitles / artwork with the same name) to <drive>\_Duplicates (review)\<same path>
        public static int SetAside(List<Pick> picks)
        {
            int n = 0;
            string log = System.IO.Path.Combine(Catalog.Dir ?? Program.DataDir, "duplicates-log.tsv");
            foreach (var p in picks)
            {
                try
                {
                    if (!File.Exists(p.Drop) || !File.Exists(p.Keep)) continue;
                    string root = System.IO.Path.GetPathRoot(p.Drop);
                    string dir = System.IO.Path.GetDirectoryName(p.Drop), stem = System.IO.Path.GetFileNameWithoutExtension(p.Drop);
                    string destDir = System.IO.Path.Combine(root, "_Duplicates (review)", dir.Substring(root.Length));
                    Directory.CreateDirectory(destDir);
                    foreach (var f in Directory.GetFiles(dir, stem + "*"))
                    {
                        string to = System.IO.Path.Combine(destDir, System.IO.Path.GetFileName(f));
                        if (File.Exists(to)) continue;
                        File.Move(f, to);
                        File.AppendAllText(log, DateTime.Now.ToString("s") + "\t" + f + "\t" + to + "\t" + p.Why + Environment.NewLine);
                    }
                    n++;
                }
                catch (Exception ex) { Log.Write("duplicates: " + p.Drop + ": " + ex.Message); }
            }
            if (n > 0) Catalog.RescanNow();
            return n;
        }
    }

    // ---------------------------------------------------------------- HTML dashboard
    public static class Dashboard
    {
        public static string HtmlPath { get { return System.IO.Path.Combine(Catalog.Dir, "Drive Catalog.html"); } }
        static readonly object writeLock = new object();

        public static void Write()
        {
            string tpl = System.IO.Path.Combine(Program.AppDir, "dashboard", "template.html");
            if (!File.Exists(tpl) || Catalog.Dir == null) return;
            List<CatVolume> vols;
            lock (Catalog.Sync) vols = Catalog.Vols.Values.OrderBy(v => v.Letter).ToList();
            List<Title> titles;
            lock (MediaLib.Sync) titles = MediaLib.Titles.ToList();
            var types = new List<string>();
            var drives = new List<object>();
            var files = new List<object>();
            for (int di = 0; di < vols.Count; di++)
            {
                var v = vols[di];
                var byType = new Dictionary<string, long[]>();
                foreach (var f in v.Files)
                {
                    long[] a; if (!byType.TryGetValue(f.Type, out a)) { a = new long[2]; byType[f.Type] = a; }
                    a[0]++; a[1] += f.Size;
                    int ti = types.IndexOf(f.Type); if (ti < 0) { types.Add(f.Type); ti = types.Count - 1; }
                    files.Add(new object[] { di, f.Rel, f.Size, ti, f.Quality ?? "", (int)((DateTime.Now.Ticks - f.Mod) / TimeSpan.TicksPerDay) });
                }
                drives.Add(new Dictionary<string, object> {
                    { "label", v.Nick + (v.Nick == "Laptop" ? "" : " drive") }, { "vol", v.Label }, { "letter", v.Letter }, { "online", v.Online }, { "type", v.DriveType }, { "id", v.Id },
                    { "total", v.Total }, { "free", v.Free }, { "files", v.Files.Count }, { "size", v.Files.Sum(f => f.Size) },
                    { "scanned", v.Scanned == DateTime.MinValue ? "" : v.Scanned.ToString("d MMM yyyy, HH:mm") },
                    { "byType", byType.ToDictionary(kv => kv.Key, kv => (object)new long[] { kv.Value[0], kv.Value[1] }) } });
            }
            var tl = new List<object>();
            foreach (var t in titles)
            {
                var m = t.Meta;
                Func<IEnumerable<string>, List<string>> L = qs => qs == null ? new List<string>() : qs.Select(MediaLib.L).Where(x => x.Length > 0).Select(x => x.Replace(" language", "")).ToList();
                tl.Add(new Dictionary<string, object> {
                    { "k", t.Kind }, { "t", t.Name }, { "y", t.Year > 0 ? t.Year : m != null ? m.Year : 0 }, { "q", t.Quality }, { "res", t.Res },
                    { "dur", t.DurSec }, { "sz", t.Size }, { "ep", t.Episodes }, { "se", t.Seasons.Count }, { "dr", t.Drives }, { "on", t.Online },
                    { "p", t.Path ?? "" }, { "f", t.Folder ?? "" }, { "img", t.Poster }, { "add", new DateTime(Math.Max(t.Added, DateTime.MinValue.Ticks)).ToString("yyyy-MM-dd") },
                    { "dir", L(m != null ? m.Directors : null) }, { "cast", L(m != null ? m.Cast : null) }, { "lang", L(m != null ? m.Langs : null) },
                    { "ctry", L(m != null ? m.Countries : null) }, { "gen", L(m != null ? m.Genres : null).Select(g => Regex.Replace(g, @"\s+(film|films|television series|series|genre|fiction film)$", "")).Where(g => g.Length > 0).Distinct().ToList() }, { "imdb", m != null ? m.Imdb : "" },
                    { "desc", m != null ? m.Desc : "" }, { "min", m != null ? m.Minutes : 0 }, { "wd", m != null ? m.Q : "" }, { "ot", m != null && m.Label != t.Name ? m.Label : "" }, { "a", t.Adult } });
            }
            List<HomeVideo> hvs; lock (MediaLib.Sync) hvs = MediaLib.HomeVideos.ToList();
            var groups = new List<object>();
            foreach (var g in hvs.GroupBy(h => h.Group))
            {
                var vs = g.OrderBy(h => h.Date).ToList();
                groups.Add(new Dictionary<string, object> {
                    { "g", g.Key }, { "kind", vs[0].Trip.Length > 0 ? "Trip" : vs[0].Event.Length > 0 ? (vs.Any(x => x.Activity.Length > 0) ? "Activity" : "Event") : "Month" },
                    { "start", vs.First().Date.ToString("yyyy-MM-dd") }, { "end", vs.Last().Date.ToString("yyyy-MM-dd") }, { "n", vs.Count }, { "sz", vs.Sum(x => x.Size) },
                    { "dur", vs.Sum(x => x.DurSec) }, { "people", vs.SelectMany(x => x.People).Distinct().ToList() }, { "on", vs.Any(x => x.Online) },
                    { "dr", vs.Select(x => x.Drive).Distinct().ToList() }, { "events", vs.Select(x => x.Event).Where(x => x.Length > 0).Distinct().Take(6).ToList() },
                    { "items", vs.Select(x => (object)new Dictionary<string, object> { { "p", x.Path }, { "n", x.Name }, { "d", x.Date.ToString("d MMM yyyy") },
                        { "dur", x.DurSec }, { "q", x.Quality }, { "pe", x.People }, { "ev", x.Event }, { "on", x.Online } }).ToList() } });
            }
            var data = new Dictionary<string, object> { { "home", groups },
                { "generated", DateTime.Now.ToString("d MMM yyyy, HH:mm") }, { "xlsx", Catalog.XlsxPath }, { "types", types },
                { "drives", drives }, { "titles", tl }, { "files", files } };
            var js = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string json = js.Serialize(data).Replace("</", "<\\/");
            string html = File.ReadAllText(tpl, Encoding.UTF8).Replace("/*__DATA__*/null", json);
            lock (writeLock)
            {
                string tmp = HtmlPath + ".tmp";
                File.WriteAllText(tmp, html, new UTF8Encoding(false));
                if (File.Exists(HtmlPath)) File.Delete(HtmlPath);
                File.Move(tmp, HtmlPath);
            }
        }
    }
}
