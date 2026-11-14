using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Data.Json;
using Windows.Storage;

namespace Shelf
{
    public sealed class Channel
    {
        public string Id, Number = "", Name, Url, Logo = "", GuideKey = "", Group = "";
        public bool IsSportsChannel => Group.IndexOf("sport", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public sealed class TvProgram
    {
        public string ChannelId, Title, Detail = "", Subtitle = "", Image = "";
        public long Start, End; // unix ms
        public bool Sports;
        public bool IsSports => Sports || LiveSports.LooksLikeSports(Title, Subtitle);
        /// <summary>"48 Hours" + "48 Hours" is just "48 Hours".</summary>
        public string Extra => string.IsNullOrWhiteSpace(Subtitle) || string.Equals(Subtitle.Trim(), Title.Trim(), StringComparison.OrdinalIgnoreCase) ? "" : Subtitle.Trim();
        public string FullTitle => Extra.Length > 0 ? Title + ": " + Extra : Title;
    }

    public static class LiveSports
    {
        static readonly Regex Leagues = new Regex(
            @"\b(NFL|NBA|WNBA|MLB|NHL|MLS|NCAA[A-Z]*|UFC|WWE|PGA|LPGA|NASCAR|IndyCar|F1|Formula 1|Premier League|Champions League|" +
            @"La Liga|Serie A|Bundesliga|Liga MX|FIFA|Olympic|World Cup|Super Bowl|World Series|Stanley Cup|March Madness|" +
            @"Football|Basketball|Baseball|Hockey|Soccer|Tennis|Golf|Boxing|Wrestling|Rugby|Cricket|Volleyball|Softball|Lacrosse|" +
            @"Motorsport|Racing|Grand Prix|Bowl Game|Playoffs?|Championship)\b", RegexOptions.IgnoreCase);
        static readonly Regex Matchup = new Regex(@"^\s*(.+?)\s+(at|vs\.?|v\.?|@)\s+(.+?)\s*$", RegexOptions.IgnoreCase);

        public static bool LooksLikeSports(string title, string subtitle) =>
            Leagues.IsMatch(title ?? "") || (Leagues.IsMatch(subtitle ?? "") && Teams(subtitle) != null);

        /// <summary>"Chicago Bears at Green Bay Packers" -> (Chicago Bears, at, Green Bay Packers).</summary>
        public static Tuple<string, string, string> Teams(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = Matchup.Match(text);
            if (!m.Success) return null;
            var a = m.Groups[1].Value.Trim(); var b = m.Groups[3].Value.Trim();
            if (a.Length > 40 || b.Length > 40) return null;
            var sep = m.Groups[2].Value.ToLowerInvariant();
            return Tuple.Create(a, sep == "@" ? "at" : sep, b);
        }
    }

    /// <summary>
    /// Live TV: antenna channels from HDHomeRun tuners and internet channels from M3U playlists, with guides
    /// (XMLTV). Kept in D:\DevelopmentFiles\Shelf\Data\livetv. Only the small guides with show pictures are
    /// downloaded (for slow internet), refreshed twice a day.
    /// </summary>
    public static class LiveTv
    {
        /// <summary>
        /// Your own guide, built from the iptv-org US playlist by github.com/ganley100/epg three times a day:
        /// ~570 channels (Pluto, Plex, TV Guide, TV Passport), with the playlist's channel IDs. Small (~2 MB).
        /// </summary>
        public const string MyGuide = "https://raw.githubusercontent.com/ganley100/epg/shelf-guide/guide.xml.gz";

        public static readonly string[] PictureGuides =
        {
            MyGuide,
            "https://i.mjh.nz/Roku/all.xml.gz",
            "https://i.mjh.nz/SamsungTVPlus/us.xml.gz",
            "https://i.mjh.nz/PlutoTV/us.xml.gz",
            "https://i.mjh.nz/Plex/us.xml.gz",
            "https://i.mjh.nz/PBS/all.xml.gz",
            "https://i.mjh.nz/MeTV/epg.xml.gz",
            "https://i.mjh.nz/hgtv_go/epg.xml.gz",
        };
        /// <summary>
        /// The big text-only guides (Full mode): epg.pw (~21 MB) and Open-EPG's US guide (~5 MB, ~2,700 channels;
        /// free, updated daily; at most 20 downloads a day, and Shelf keeps each copy 12 hours). Loaded last.
        /// </summary>
        public static readonly string[] BigGuides =
        {
            "https://epg.pw/xmltv/epg_US.xml.gz",
            "https://www.open-epg.com/files/unitedstates11.xml.gz",
        };
        /// <summary>"full": every guide (default); "light": only the small picture guides, for slow Wi-Fi.</summary>
        public static string GuideMode = "full";
        public const string UsPlaylist = "https://iptv-org.github.io/iptv/countries/us.m3u";

        static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        static readonly HttpClient Http = MakeHttp();
        static string dir;

        public static List<string> Tuners = new List<string>();
        public static List<(string Url, string Guide)> Playlists = new List<(string, string)>();
        public static List<Channel> Channels = new List<Channel>();
        public static List<TvProgram> Guide = new List<TvProgram>();
        public static long LastRefresh;
        public static string LastError = "";
        public static volatile string Status = "";
        public static bool Busy => Gate.CurrentCount == 0;
        public static event Action Changed;
        public static void NotifyChanged() => Changed?.Invoke();

        static HttpClient MakeHttp()
        {
            var h = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate });
            h.Timeout = TimeSpan.FromSeconds(60);
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Shelf/1.0 (Xbox)");
            return h;
        }

        // ---------- storage ----------

        public static async Task InitAsync()
        {
            if (dir != null) return;
            dir = Storage.DataFolder("livetv");
            await Task.CompletedTask;
            try
            {
                var o = JsonObject.Parse(await Read("sources.json"));
                foreach (var t in o.GetNamedArray("tuners", new JsonArray())) Tuners.Add(t.GetString());
                GuideMode = o.GetNamedString("guideMode", "full") == "light" ? "light" : "full";
                foreach (var p in o.GetNamedArray("playlists", new JsonArray()))
                {
                    var po = p.GetObject();
                    Playlists.Add((po.GetNamedString("url"), po.GetNamedString("guide", "")));
                }
            }
            catch { }
            try
            {
                var o = JsonObject.Parse(await Read("channels.json"));
                LastRefresh = (long)o.GetNamedNumber("at", 0);
                foreach (var v in o.GetNamedArray("channels"))
                {
                    var c = v.GetObject();
                    Channels.Add(new Channel
                    {
                        Id = c.GetNamedString("id"), Number = c.GetNamedString("number", ""), Name = c.GetNamedString("name"),
                        Url = c.GetNamedString("url"), Logo = c.GetNamedString("logo", ""), GuideKey = c.GetNamedString("guideKey", ""),
                        Group = c.GetNamedString("group", ""),
                    });
                }
                foreach (var v in o.GetNamedArray("guide"))
                {
                    var p = v.GetObject();
                    Guide.Add(new TvProgram
                    {
                        ChannelId = p.GetNamedString("c"), Title = p.GetNamedString("t"), Start = (long)p.GetNamedNumber("s"), End = (long)p.GetNamedNumber("e"),
                        Detail = p.GetNamedString("d", ""), Subtitle = p.GetNamedString("st", ""), Image = p.GetNamedString("i", ""), Sports = p.GetNamedBoolean("sp", false),
                    });
                }
            }
            catch { }
        }

        static Task<string> Read(string name) => Task.Run(() => Fs.ReadAllText(dir + "\\" + name));

        static Task Write(string name, string text) => Task.Run(() => Fs.WriteAllText(dir + "\\" + name, text));

        static async Task SaveSourcesAsync()
        {
            var o = new JsonObject();
            var t = new JsonArray(); foreach (var x in Tuners) t.Add(JsonValue.CreateStringValue(x));
            var p = new JsonArray();
            foreach (var (url, guide) in Playlists)
                p.Add(new JsonObject { ["url"] = JsonValue.CreateStringValue(url), ["guide"] = JsonValue.CreateStringValue(guide ?? "") });
            o["tuners"] = t; o["playlists"] = p;
            o["guideMode"] = JsonValue.CreateStringValue(GuideMode);
            await Write("sources.json", o.Stringify());
        }

        /// <summary>Keeps guide changes (e.g. teams added to a "College Football" listing by the Sports screen).</summary>
        public static Task SaveGuideAsync() => SaveAsync();

        static async Task SaveAsync()
        {
            var o = new JsonObject { ["at"] = JsonValue.CreateNumberValue(LastRefresh) };
            var ca = new JsonArray();
            foreach (var c in Channels)
                ca.Add(new JsonObject
                {
                    ["id"] = S(c.Id), ["number"] = S(c.Number), ["name"] = S(c.Name), ["url"] = S(c.Url),
                    ["logo"] = S(c.Logo), ["guideKey"] = S(c.GuideKey), ["group"] = S(c.Group),
                });
            var ga = new JsonArray();
            foreach (var p in Guide)
                ga.Add(new JsonObject
                {
                    ["c"] = S(p.ChannelId), ["t"] = S(p.Title), ["s"] = JsonValue.CreateNumberValue(p.Start), ["e"] = JsonValue.CreateNumberValue(p.End),
                    ["d"] = S(p.Detail), ["st"] = S(p.Subtitle), ["i"] = S(p.Image), ["sp"] = JsonValue.CreateBooleanValue(p.Sports),
                });
            o["channels"] = ca; o["guide"] = ga;
            await Write("channels.json", o.Stringify());
        }

        static IJsonValue S(string s) => JsonValue.CreateStringValue(s ?? "");

        // ---------- sources ----------

        /// <summary>Turns GitHub page links into the raw file and adds https:// when missing.</summary>
        public static string NormalizeUrl(string url)
        {
            var u = (url ?? "").Trim();
            if (u.Length == 0) throw new ArgumentException("Enter the playlist's web address.");
            if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) u = "https://" + u;
            var m = Regex.Match(u, @"^https://github\.com/([^/]+)/([^/]+)/blob/(.+)$");
            if (m.Success) u = $"https://raw.githubusercontent.com/{m.Groups[1].Value}/{m.Groups[2].Value}/{m.Groups[3].Value}";
            return u;
        }

        public static async Task AddPlaylistAsync(string url, string guide)
        {
            var u = NormalizeUrl(url);
            var text = await Http.GetStringAsync(u);
            if (text.IndexOf("#EXTINF", StringComparison.OrdinalIgnoreCase) < 0)
                throw new ArgumentException("That address isn't a channel playlist (M3U). Use the raw .m3u link.");
            Playlists.RemoveAll(p => p.Url == u);
            Playlists.Add((u, (guide ?? "").Trim()));
            await SaveSourcesAsync();
        }

        public static async Task AddTunerAsync(string address)
        {
            var a = (address ?? "").Trim().TrimEnd('/');
            if (a.Length == 0) throw new ArgumentException("Enter the tuner's address, e.g. 192.168.1.50.");
            if (!a.StartsWith("http")) a = "http://" + a;
            if (!Tuners.Contains(a)) Tuners.Add(a);
            await SaveSourcesAsync();
        }

        public static async Task RemoveAsync(string key)
        {
            Tuners.Remove(key);
            Playlists.RemoveAll(p => p.Url == key);
            await SaveSourcesAsync();
        }

        public static async Task SetGuideModeAsync(string mode)
        {
            GuideMode = mode == "light" ? "light" : "full";
            await SaveSourcesAsync();
        }

        public static bool HasSources => Tuners.Count > 0 || Playlists.Count > 0;

        static List<string> GuidesFor(string playlistUrl, string guide)
        {
            var list = (guide ?? "").Split(new[] { ' ', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            // iptv-org playlists get the free guides with show pictures automatically.
            if (list.Count == 0 && playlistUrl.IndexOf("iptv-org", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                list.AddRange(PictureGuides);
                if (GuideMode == "full") list.AddRange(BigGuides); // last: the picture guides show first
            }
            return list;
        }

        // ---------- refresh ----------

        public static void RefreshInBackground(bool discover = true)
        {
            var _ = Task.Run(async () => { try { await RefreshAsync(discover); } catch { } });
        }

        /// <summary>Channels first (shown at once), then the guides.</summary>
        public static async Task RefreshAsync(bool discover = true)
        {
            if (!await Gate.WaitAsync(0)) return;
            var errors = new List<string>();
            try
            {
                await InitAsync();
                Status = "Loading channels…"; Changed?.Invoke();
                var found = new List<Channel>();
                var programs = new List<TvProgram>();
                var tuners = Tuners.ToList();
                if (discover)
                    foreach (var t in await DiscoverTunersAsync()) if (!tuners.Contains(t)) tuners.Add(t);
                var tunerAuth = new Dictionary<string, string>();
                foreach (var t in tuners)
                {
                    try
                    {
                        var (chs, auth) = await TunerLineupAsync(t);
                        found.AddRange(chs);
                        if (auth != null) tunerAuth[t] = auth;
                    }
                    catch (Exception e) { errors.Add("Tuner " + t + ": " + e.Message); }
                }
                var playlistGuides = new List<(List<Channel> Chs, List<string> Guides)>();
                foreach (var (url, guide) in Playlists.ToList())
                {
                    try
                    {
                        var (chs, header) = ParseM3u(await Http.GetStringAsync(url), url);
                        found.AddRange(chs);
                        var g = GuidesFor(url, string.IsNullOrWhiteSpace(guide) ? header : guide);
                        if (g.Count > 0) playlistGuides.Add((chs, g));
                    }
                    catch (Exception e)
                    {
                        errors.Add("Playlist " + url + ": " + e.Message);
                        found.AddRange(Channels.Where(c => c.Id.StartsWith("m3u:" + Hash(url) + "|"))); // keep last-known channels
                    }
                }
                var unique = found.GroupBy(c => c.Id).Select(g => g.First()).ToList();
                foreach (var c in unique.Where(c => string.IsNullOrEmpty(c.Logo)))
                    c.Logo = Channels.FirstOrDefault(o => o.Id == c.Id)?.Logo ?? "";
                var ids = new HashSet<string>(unique.Select(c => c.Id));
                Channels = unique;
                Guide = Guide.Where(p => ids.Contains(p.ChannelId)).ToList();
                Changed?.Invoke();

                // Guides.
                foreach (var kv in tunerAuth)
                {
                    try
                    {
                        Status = "Loading the antenna guide…"; Changed?.Invoke();
                        programs.AddRange(await TunerGuideAsync(kv.Value, unique.Where(c => c.Id.StartsWith("hdhr:" + kv.Key + "|")).ToList()));
                    }
                    catch (Exception e) { errors.Add("Antenna guide: " + e.Message); }
                }
                var perGuide = new List<List<TvProgram>> { programs };
                var logos = new Dictionary<string, string>();
                int n = 0, total = playlistGuides.Sum(p => p.Guides.Count);
                foreach (var (chs, guides) in playlistGuides)
                    foreach (var g in guides)
                    {
                        n++;
                        Status = BigGuides.Contains(g) ? $"Adding more guide information ({n} of {total})…"
                               : total > 1 ? $"Loading the guide ({n} of {total})…" : "Loading the guide…";
                        Changed?.Invoke();
                        try
                        {
                            using (var s = await OpenGuideAsync(g))
                            {
                                var (p, l) = ParseXmltv(s, chs);
                                perGuide.Add(p);
                                foreach (var x in l) if (!logos.ContainsKey(x.Key)) logos[x.Key] = x.Value;
                            }
                        }
                        catch (Exception e) { errors.Add("Guide " + g + ": " + e.Message); }
                    }
                foreach (var c in unique) if (string.IsNullOrEmpty(c.Logo) && logos.TryGetValue(c.Id, out var lg)) c.Logo = lg;
                var now = Now;
                Guide = BestPerChannel(perGuide).Where(p => p.End > now - 3_600_000 && p.Start < now + 2 * 86_400_000L).OrderBy(p => p.Start).ToList();
                LastRefresh = now;
                LastError = errors.FirstOrDefault() ?? "";
                await SaveAsync();
                Changed?.Invoke();
                // Pictures from TMDB for shows no guide has a picture for (your own key, optional).
                if (Tmdb.Enabled)
                {
                    Status = "Finding show pictures…"; Changed?.Invoke();
                    try { if (await Tmdb.FillAsync(Guide) > 0) await SaveAsync(); } catch { }
                }
                // Real games from ESPN's scoreboard into the guide ("College Football" on TNT → the teams).
                try
                {
                    Status = "Adding today's games…"; Changed?.Invoke();
                    var games = await Espn.GamesAsync();
                    var changed = Espn.Enrich(games);
                    // Live sports pictures: TheSportsDB's picture of each game first (known ones now, the rest in the background).
                    changed |= SportsDb.ApplyThumbs(games);
                    if (changed) await SaveAsync();
                    SportsDb.FillGuideInBackground(games);
                }
                catch { }
                // Team logos for game cards: in the background, nothing waits for them.
                TeamLogos.LookUpInBackground(Guide);
            }
            finally
            {
                Status = "";
                Gate.Release();
                Changed?.Invoke();
            }
        }

        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public static TvProgram NowPlaying(string channelId)
        {
            var now = Now;
            return Guide.FirstOrDefault(p => p.ChannelId == channelId && p.Start <= now && p.End > now);
        }

        public static List<TvProgram> Upcoming(string channelId)
        {
            var now = Now;
            return Guide.Where(p => p.ChannelId == channelId && p.End > now).OrderBy(p => p.Start).ToList();
        }

        public static string Hash(string s)
        {
            unchecked { int h = 17; foreach (var ch in s) h = h * 31 + ch; return ((uint)h).ToString("x8"); }
        }

        // ---------- HDHomeRun ----------

        /// <summary>Tuners on this home network, via SiliconDust's discovery service.</summary>
        static async Task<List<string>> DiscoverTunersAsync()
        {
            var list = new List<string>();
            try
            {
                var arr = JsonArray.Parse(await Http.GetStringAsync("https://ipv4-api.hdhomerun.com/discover"));
                foreach (var v in arr)
                {
                    var o = v.GetObject();
                    if (!o.ContainsKey("LocalIP")) continue;
                    if (o.ContainsKey("StorageID")) continue; // a DVR box, not a tuner
                    list.Add("http://" + o.GetNamedString("LocalIP"));
                }
            }
            catch { }
            return list;
        }

        static async Task<(List<Channel>, string)> TunerLineupAsync(string baseUrl)
        {
            var info = JsonObject.Parse(await Http.GetStringAsync(baseUrl + "/discover.json"));
            var auth = info.GetNamedString("DeviceAuth", null);
            var lineupUrl = info.GetNamedString("LineupURL", baseUrl + "/lineup.json");
            var arr = JsonArray.Parse(await Http.GetStringAsync(lineupUrl));
            var chs = new List<Channel>();
            foreach (var v in arr)
            {
                var o = v.GetObject();
                if (o.ContainsKey("DRM") && o.GetNamedNumber("DRM", 0) == 1) continue; // encrypted: can't play
                var number = o.GetNamedString("GuideNumber", "");
                chs.Add(new Channel
                {
                    Id = "hdhr:" + baseUrl + "|" + number, Number = number, Name = o.GetNamedString("GuideName", number),
                    Url = o.GetNamedString("URL"), GuideKey = number,
                });
            }
            return (chs, auth);
        }

        static async Task<List<TvProgram>> TunerGuideAsync(string auth, List<Channel> chs)
        {
            var arr = JsonArray.Parse(await Http.GetStringAsync("https://api.hdhomerun.com/api/guide?DeviceAuth=" + Uri.EscapeDataString(auth)));
            var byNumber = chs.ToDictionary(c => c.Number, c => c);
            var list = new List<TvProgram>();
            foreach (var v in arr)
            {
                var o = v.GetObject();
                if (!byNumber.TryGetValue(o.GetNamedString("GuideNumber", ""), out var ch)) continue;
                if (string.IsNullOrEmpty(ch.Logo)) ch.Logo = o.GetNamedString("ImageURL", "");
                foreach (var gv in o.GetNamedArray("Guide", new JsonArray()))
                {
                    var g = gv.GetObject();
                    var filters = g.GetNamedArray("Filter", new JsonArray()).Select(f => f.GetString()).ToList();
                    list.Add(new TvProgram
                    {
                        ChannelId = ch.Id, Title = g.GetNamedString("Title", "Program"),
                        Start = (long)g.GetNamedNumber("StartTime", 0) * 1000, End = (long)g.GetNamedNumber("EndTime", 0) * 1000,
                        Detail = g.GetNamedString("Synopsis", ""), Subtitle = g.GetNamedString("EpisodeTitle", ""),
                        Image = g.GetNamedString("ImageURL", ""), Sports = filters.Any(f => f.IndexOf("sport", StringComparison.OrdinalIgnoreCase) >= 0),
                    });
                }
            }
            return list;
        }

        // ---------- M3U ----------

        static readonly Regex Attr = new Regex(@"([\w-]+)=""([^""]*)""");

        public static (List<Channel>, string) ParseM3u(string text, string sourceUrl)
        {
            var list = new List<Channel>();
            string header = null;
            string pendingInfo = null;
            int i = 0;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("#EXTM3U"))
                {
                    var m = Regex.Match(line, @"(?:x-tvg-url|url-tvg)=""([^""]+)""");
                    if (m.Success) header = m.Groups[1].Value;
                }
                else if (line.StartsWith("#EXTINF")) pendingInfo = line;
                else if (line.Length > 0 && !line.StartsWith("#") && pendingInfo != null)
                {
                    var info = pendingInfo; pendingInfo = null;
                    if (!line.StartsWith("http://") && !line.StartsWith("https://")) continue; // srt://, rtmp://… can't play
                    var attrs = Attr.Matches(info).Cast<Match>().GroupBy(m => m.Groups[1].Value.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Groups[2].Value);
                    var comma = info.LastIndexOf(',');
                    var name = comma >= 0 ? info.Substring(comma + 1).Trim() : "Channel";
                    name = Regex.Replace(name, @"\s*\((\d{3,4}p|\d+k)\)|\s*\[[^\]]*\]", "").Trim();
                    i++;
                    attrs.TryGetValue("tvg-id", out var key);
                    attrs.TryGetValue("tvg-logo", out var logo);
                    attrs.TryGetValue("group-title", out var group);
                    attrs.TryGetValue("tvg-chno", out var chno);
                    list.Add(new Channel
                    {
                        Id = "m3u:" + Hash(sourceUrl) + "|" + i + "|" + name, Number = chno ?? "", Name = name.Length > 0 ? name : "Channel " + i,
                        Url = line, Logo = logo ?? "", GuideKey = key ?? "", Group = group ?? "",
                    });
                }
            }
            return (list, header);
        }

        // ---------- XMLTV ----------

        /// <summary>A downloaded guide, kept 12 hours (twice a day).</summary>
        static async Task<Stream> OpenGuideAsync(string url)
        {
            var path = Storage.DataFolder("livetv\\guides") + "\\" + Hash(url) + ".dl";
            var info = Fs.Info(path);
            var fresh = info != null && (DateTimeOffset.Now - info.Modified).TotalHours < 12;
            if (!fresh)
            {
                try
                {
                    var bytes = await Http.GetByteArrayAsync(url);
                    await Task.Run(() => Fs.WriteAllBytes(path, bytes));
                }
                catch { if (info == null) throw; } // keep the old copy if the download fails
            }
            Stream s = Fs.OpenRead(path);
            var head = new byte[2];
            var n = s.Read(head, 0, 2);
            s.Seek(0, SeekOrigin.Begin);
            if (n == 2 && head[0] == 0x1f && head[1] == 0x8b) return new GZipStream(s, CompressionMode.Decompress);
            if (n == 2 && head[0] == 'P' && head[1] == 'K')
            {
                var zip = new ZipArchive(s, ZipArchiveMode.Read);
                var e = zip.Entries.FirstOrDefault(x => x.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) ?? throw new IOException("No guide inside that zip.");
                return e.Open();
            }
            return s;
        }

        public static string NormalizeName(string n)
        {
            var s = (n ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
            s = new string(s.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
            s = Regex.Replace(s, @"\.(us|ca|uk|mx)$", " ");
            s = Regex.Replace(s, @"[(\[][^)\]]*[)\]]", " ");
            s = Regex.Replace(s, @"\b(hd|sd|fhd|uhd|4k|east|west|eastern|pacific|feed|tv|channel)\b", " ");
            s = Regex.Replace(s, @"[^a-z0-9+]+", " ");
            return s.Trim();
        }

        public static long GuideTime(string raw)
        {
            var s = (raw ?? "").Trim();
            var zone = Regex.Match(s.Length > 8 ? s.Substring(8) : "", @"(Z|[+-]\d{2}:?\d{2})\s*$");
            var body = zone.Success ? s.Substring(0, s.Length - zone.Value.Length) : s;
            var d = new string(body.Where(char.IsDigit).ToArray()).PadRight(14, '0').Substring(0, 14);
            var t = new DateTimeOffset(int.Parse(d.Substring(0, 4)), int.Parse(d.Substring(4, 2)), int.Parse(d.Substring(6, 2)),
                int.Parse(d.Substring(8, 2)), int.Parse(d.Substring(10, 2)), int.Parse(d.Substring(12, 2)), TimeSpan.Zero).ToUnixTimeMilliseconds();
            if (zone.Success && zone.Value.Trim() != "Z")
            {
                var z = zone.Value.Trim().Replace(":", "");
                var sign = z[0] == '-' ? -1 : 1;
                t -= sign * (int.Parse(z.Substring(1, 2)) * 3_600_000L + int.Parse(z.Substring(3, 2)) * 60_000L);
            }
            return t;
        }

        /// <summary>Programmes for [channels] (matched by guide ID, name, or name without quality tags).</summary>
        public static (List<TvProgram>, Dictionary<string, string>) ParseXmltv(Stream input, List<Channel> channels)
        {
            var byKey = new Dictionary<string, Channel>();
            foreach (var c in channels)
            {
                if (c.GuideKey.Length > 0) byKey[c.GuideKey.ToLowerInvariant()] = c;
                byKey[c.Name.ToLowerInvariant()] = c;
                var nn = "~" + NormalizeName(c.Name);
                if (!byKey.ContainsKey(nn)) byKey[nn] = c;
            }
            Channel Lookup(string k)
            {
                if (string.IsNullOrEmpty(k)) return null;
                if (byKey.TryGetValue(k.ToLowerInvariant(), out var c)) return c;
                var n = NormalizeName(k);
                return n.Length > 0 && byKey.TryGetValue("~" + n, out c) ? c : null;
            }
            var idMap = new Dictionary<string, Channel>();
            var claimOwner = new Dictionary<string, string>(); // our channel id -> the guide channel that got it
            var aliasOf = new Dictionary<string, string>();    // display name -> guide channel id
            bool Claim(string guideId, Channel c)
            {
                if (claimOwner.TryGetValue(c.Id, out var owner)) return owner == guideId;
                claimOwner[c.Id] = guideId; idMap[guideId] = c; return true;
            }
            var logos = new Dictionary<string, string>();
            var output = new List<TvProgram>();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true, IgnoreWhitespace = true, CheckCharacters = false };
            using (var r = XmlReader.Create(input, settings))
            {
                string chId = null;
                TvProgram prog = null;
                while (true)
                {
                    try { if (!r.Read()) break; } catch (XmlException) { break; } // a broken tail still gives what came before
                    if (r.NodeType == XmlNodeType.Element)
                    {
                        switch (r.Name)
                        {
                            case "channel":
                                chId = r.GetAttribute("id");
                                var c0 = Lookup(chId); if (c0 != null && chId != null) Claim(chId, c0); // Eastern + Pacific feeds: the first wins
                                break;
                            case "display-name":
                                if (chId != null)
                                {
                                    var dn = SafeText(r);
                                    if (dn.Length > 0 && !aliasOf.ContainsKey(dn)) aliasOf[dn] = chId;
                                    if (!idMap.ContainsKey(chId)) { var c1 = Lookup(dn); if (c1 != null) Claim(chId, c1); }
                                }
                                break;
                            case "icon":
                                var src = r.GetAttribute("src") ?? "";
                                if (src.StartsWith("http"))
                                {
                                    if (prog != null) { if (prog.Image.Length == 0) prog.Image = src; }
                                    else if (chId != null && idMap.TryGetValue(chId, out var lc) && !logos.ContainsKey(lc.Id)) logos[lc.Id] = src;
                                }
                                break;
                            case "programme":
                                var cid = r.GetAttribute("channel") ?? "";
                                Channel ch;
                                if (idMap.TryGetValue(cid, out var m)) ch = m;
                                else
                                {
                                    // Some guides list programmes under a display name rather than the channel id.
                                    var owner = aliasOf.TryGetValue(cid, out var o2) ? o2 : cid;
                                    ch = idMap.TryGetValue(owner, out var m2) ? m2 : Lookup(cid);
                                    if (ch != null && !Claim(owner, ch)) ch = null;
                                }
                                prog = null;
                                if (ch != null)
                                    try { prog = new TvProgram { ChannelId = ch.Id, Title = "", Start = GuideTime(r.GetAttribute("start")), End = GuideTime(r.GetAttribute("stop")) }; } catch { }
                                if (prog != null && r.IsEmptyElement) prog = null;
                                break;
                            case "title": if (prog != null && prog.Title.Length == 0) prog.Title = SafeText(r); break;
                            case "sub-title": if (prog != null && prog.Subtitle.Length == 0) prog.Subtitle = SafeText(r); break;
                            case "desc": if (prog != null && prog.Detail.Length == 0) prog.Detail = SafeText(r); break;
                            case "category": if (prog != null && SafeText(r).IndexOf("sport", StringComparison.OrdinalIgnoreCase) >= 0) prog.Sports = true; break;
                        }
                    }
                    else if (r.NodeType == XmlNodeType.EndElement)
                    {
                        if (r.Name == "channel") chId = null;
                        else if (r.Name == "programme" && prog != null)
                        {
                            if (prog.Title.Length == 0) prog.Title = "Program";
                            output.Add(prog);
                            prog = null;
                        }
                    }
                }
            }
            return (output, logos);
        }

        static string SafeText(XmlReader r)
        {
            try { return r.IsEmptyElement ? "" : r.ReadElementContentAsString().Trim(); } catch { return ""; }
        }

        /// <summary>For each channel, the guide with the most pictures (then the most shows).</summary>
        public static List<TvProgram> BestPerChannel(List<List<TvProgram>> perGuide)
        {
            var best = new Dictionary<string, List<TvProgram>>();
            int Score(List<TvProgram> l) => l.Count(p => p.Image.Length > 0) * 1000 + l.Count;
            foreach (var g in perGuide)
                foreach (var grp in g.GroupBy(p => p.ChannelId))
                {
                    var l = grp.ToList();
                    if (!best.TryGetValue(grp.Key, out var cur) || Score(l) > Score(cur)) best[grp.Key] = l;
                }
            return best.Values.SelectMany(x => x).ToList();
        }

        // ---------- pictures ----------

        public static async Task<byte[]> ImageBytesAsync(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("http")) return null;
            var path = Storage.DataFolder("img") + "\\" + Hash(url) + ".img";
            if (Fs.Exists(path)) return await Task.Run(() => Fs.ReadAllBytes(path));
            try
            {
                var b = await Http.GetByteArrayAsync(url);
                await Task.Run(() => Fs.WriteAllBytes(path, b));
                return b;
            }
            catch { return null; }
        }

        public static HttpClient Client => Http;
    }
}
