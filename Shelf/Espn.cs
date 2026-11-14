using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace Shelf
{
    /// <summary>One game from ESPN's scoreboard.</summary>
    public sealed class EspnGame
    {
        public string League, Sport, Away, Home, AwayShort, HomeShort, AwayLogo, HomeLogo, AwayScore = "", HomeScore = "", State = "pre", Detail = "";
        public string Id = "", Path = "";
        /// <summary>A wide photo from the game (highlight video / recap), once it's underway or over.</summary>
        public string Photo;
        /// <summary>ESPN's matchup graphic (both teams, split diagonally): every game has one; square.</summary>
        public string Graphic => Id.Length > 0 ? $"https://s.espncdn.com/stitcher/sports/{Path}/events/{Id}.png?templateId=espn.com.share.1&width=720&height=720" : null;
        /// <summary>The best picture of this game: TheSportsDB's, then ESPN's photo, then ESPN's graphic.</summary>
        public string BestPicture
        {
            get
            {
                var t = SportsDb.CachedThumb(Away, Home, DateTimeOffset.FromUnixTimeMilliseconds(Start).LocalDateTime);
                return !string.IsNullOrEmpty(t) ? t : Photo ?? Graphic;
            }
        }
        public long Start;
        public List<string> Networks = new List<string>();
        public List<Channel> Channels = new List<Channel>(); // your channels showing it
        public string Matchup => $"{Away} at {Home}";
    }

    /// <summary>
    /// Today's games from ESPN's public scoreboard feed (unofficial: no key, and it may change without notice):
    /// teams, logos, scores, and the TV networks showing each game. Shelf uses the networks to find the games
    /// on your channels and writes the teams into your guide, so "College Football" on TNT becomes
    /// "Cincinnati Bearcats at Iowa State Cyclones".
    /// </summary>
    public static class Espn
    {
        static readonly (string Path, string Query, string League, string Sport, double Hours)[] Feeds =
        {
            ("football/nfl", "", "NFL", "nfl", 3.5),
            ("football/college-football", "groups=80", "College Football", "cfb", 3.5),
            ("basketball/nba", "", "NBA", "nba", 2.5),
            ("basketball/wnba", "", "WNBA", "wnba", 2.2),
            ("basketball/mens-college-basketball", "groups=50", "College Basketball", "cbb", 2.2),
            ("basketball/womens-college-basketball", "groups=50", "Women's College Basketball", "cbb", 2.2),
            ("hockey/nhl", "", "NHL", "nhl", 3),
            ("baseball/mlb", "", "MLB", "mlb", 3.2),
            ("soccer/usa.1", "", "MLS", "soccer", 2),
            ("soccer/eng.1", "", "Premier League", "soccer", 2),
        };

        /// <summary>ESPN's network names → how your channels may be called.</summary>
        static readonly Dictionary<string, string[]> Aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["FOX"] = new[] { "fox" }, ["FS1"] = new[] { "foxsports1", "fs1" }, ["FS2"] = new[] { "foxsports2", "fs2" },
            ["ESPN"] = new[] { "espn" }, ["ESPN2"] = new[] { "espn2" }, ["ESPNU"] = new[] { "espnu" }, ["ESPNEWS"] = new[] { "espnews" },
            ["SECN"] = new[] { "secnetwork", "sec" }, ["SEC Network"] = new[] { "secnetwork", "sec" },
            ["ACCN"] = new[] { "accnetwork", "acc" }, ["ACC Network"] = new[] { "accnetwork", "acc" },
            ["BTN"] = new[] { "bigtennetwork", "btn" }, ["Big Ten Network"] = new[] { "bigtennetwork", "btn" },
            ["CBSSN"] = new[] { "cbssportsnetwork", "cbssn" }, ["CBS Sports Network"] = new[] { "cbssportsnetwork", "cbssn" },
            ["TNT"] = new[] { "tnt" }, ["TBS"] = new[] { "tbs" }, ["truTV"] = new[] { "trutv" },
            ["NBA TV"] = new[] { "nbatv" }, ["NHL Net"] = new[] { "nhlnetwork" }, ["NHL Network"] = new[] { "nhlnetwork" },
            ["MLB Network"] = new[] { "mlbnetwork" }, ["MLBN"] = new[] { "mlbnetwork" },
            ["NFL Network"] = new[] { "nflnetwork" }, ["NFL Net"] = new[] { "nflnetwork" },
            ["USA Net"] = new[] { "usanetwork", "usa" }, ["USA Network"] = new[] { "usanetwork", "usa" },
            ["Golf"] = new[] { "golfchannel" }, ["Tennis Channel"] = new[] { "tennischannel" },
            ["CW"] = new[] { "cw", "thecw" }, ["ION"] = new[] { "ion" },
            ["ABC"] = new[] { "abc" }, ["CBS"] = new[] { "cbs" }, ["NBC"] = new[] { "nbc" },
            ["Univision"] = new[] { "univision" }, ["Telemundo"] = new[] { "telemundo" }, ["TUDN"] = new[] { "tudn" },
            ["Fox Deportes"] = new[] { "foxdeportes" }, ["ESPN Deportes"] = new[] { "espndeportes" },
        };
        /// <summary>The big broadcast networks: on an antenna these are local stations named by call sign.</summary>
        static readonly string[] Broadcast = { "ABC", "CBS", "NBC", "FOX", "CW", "ION" };
        public static readonly string[] Streaming = { "ESPN+", "Peacock", "Prime Video", "Netflix", "YouTube", "Disney+", "Apple TV", "Paramount+", "Max", "DAZN", "fuboTV" };

        static List<EspnGame> cached;
        static long cachedAt;

        static string S(JsonObject o, string f) => o != null && o.ContainsKey(f) && o[f].ValueType == JsonValueType.String ? o.GetNamedString(f) : "";
        static JsonObject O(JsonObject o, string f) => o != null && o.ContainsKey(f) && o[f].ValueType == JsonValueType.Object ? o.GetNamedObject(f) : null;
        static JsonArray A(JsonObject o, string f) => o != null && o.ContainsKey(f) && o[f].ValueType == JsonValueType.Array ? o.GetNamedArray(f) : new JsonArray();

        /// <summary>Today's games (and tonight's, into early tomorrow), kept 5 minutes.</summary>
        public static async Task<List<EspnGame>> GamesAsync()
        {
            if (cached != null && LiveTv.Now - cachedAt < 5 * 60_000L) return cached;
            cached = await FetchDayAsync(DateTime.Now.Date);
            cachedAt = LiveTv.Now;
            foreach (var g in cached)
            {
                TeamLogos.Remember(g.Away, g.AwayLogo); TeamLogos.Remember(g.Home, g.HomeLogo);
                TeamLogos.Remember(g.AwayShort, g.AwayLogo); TeamLogos.Remember(g.HomeShort, g.HomeLogo);
            }
            TeamLogos.Flush();
            return cached;
        }

        static readonly Dictionary<DateTime, List<EspnGame>> pastDays = new Dictionary<DateTime, List<EspnGame>>();

        /// <summary>
        /// The game a channel was showing at a time (for recordings made before the guide named the teams):
        /// a game on that channel's network that was on then.
        /// </summary>
        public static async Task<EspnGame> GameOnAsync(string channelName, long at, string sportHint)
        {
            var day = DateTimeOffset.FromUnixTimeMilliseconds(at).LocalDateTime.Date;
            List<EspnGame> games;
            lock (pastDays) pastDays.TryGetValue(day, out games);
            if (games == null)
            {
                games = day == DateTime.Now.Date ? await GamesAsync() : await FetchDayAsync(day);
                lock (pastDays) pastDays[day] = games;
            }
            var chKeys = new[] { Compact(LiveTv.NormalizeName(channelName)) };
            var hits = games.Where(g => at >= g.Start - 30 * 60_000L && at <= g.Start + 4 * 3_600_000L &&
                    (sportHint == null || g.Sport == sportHint) &&
                    g.Networks.Any(n => (Aliases.TryGetValue(n.Trim(), out var a) ? a : new[] { Compact(n) }).Intersect(chKeys).Any()))
                .ToList();
            return hits.Count == 1 ? hits[0] : null;
        }

        /// <summary>A game by its teams on a day (for recordings), or null.</summary>
        public static async Task<EspnGame> FindAsync(string away, string home, DateTime day)
        {
            List<EspnGame> games;
            lock (pastDays) pastDays.TryGetValue(day.Date, out games);
            if (games == null)
            {
                games = day.Date == DateTime.Now.Date ? await GamesAsync() : await FetchDayAsync(day.Date);
                lock (pastDays) pastDays[day.Date] = games;
            }
            return games.FirstOrDefault(g => g.Away == away && g.Home == home)
                ?? games.FirstOrDefault(g => (g.Away.StartsWith(away) || away.StartsWith(g.Away)) && (g.Home.StartsWith(home) || home.StartsWith(g.Home)));
        }

        /// <summary>One day's games (that local day, with the next day's feed for late games).</summary>
        static async Task<List<EspnGame>> FetchDayAsync(DateTime localDay)
        {
            var dates = new[] { localDay, localDay.AddDays(1) }.Select(d => d.ToString("yyyyMMdd")).ToList();
            var games = new List<EspnGame>();
            await Task.WhenAll(Feeds.SelectMany(f => dates.Select(async d =>
            {
                try
                {
                    var url = $"https://site.api.espn.com/apis/site/v2/sports/{f.Path}/scoreboard?dates={d}&limit=400" + (f.Query.Length > 0 ? "&" + f.Query : "");
                    var root = JsonObject.Parse(await LiveTv.Client.GetStringAsync(url));
                    foreach (var ev in A(root, "events"))
                    {
                        if (ev.ValueType != JsonValueType.Object) continue;
                        var e = ev.GetObject();
                        if (!DateTimeOffset.TryParse(S(e, "date"), out var when)) continue;
                        var comp = A(e, "competitions").Select(x => x.GetObject()).FirstOrDefault();
                        if (comp == null) continue;
                        var g = new EspnGame { League = f.League, Sport = f.Sport, Start = when.ToUnixTimeMilliseconds(), Id = S(e, "id"), Path = f.Path };
                        // A photo from the game: the headline video's thumbnail (live and finished games).
                        foreach (var hv in A(comp, "headlines"))
                        {
                            if (hv.ValueType != JsonValueType.Object || g.Photo != null) continue;
                            foreach (var vv in A(hv.GetObject(), "video"))
                                if (vv.ValueType == JsonValueType.Object && S(vv.GetObject(), "thumbnail").StartsWith("http")) { g.Photo = S(vv.GetObject(), "thumbnail"); break; }
                        }
                        foreach (var cv in A(comp, "competitors"))
                        {
                            var c = cv.GetObject(); var t = O(c, "team");
                            if (S(c, "homeAway") == "home") { g.Home = S(t, "displayName"); g.HomeShort = S(t, "shortDisplayName"); g.HomeLogo = S(t, "logo"); g.HomeScore = S(c, "score"); }
                            else { g.Away = S(t, "displayName"); g.AwayShort = S(t, "shortDisplayName"); g.AwayLogo = S(t, "logo"); g.AwayScore = S(c, "score"); }
                        }
                        if (string.IsNullOrEmpty(g.Home) || string.IsNullOrEmpty(g.Away)) continue;
                        var type = O(O(e, "status"), "type");
                        g.State = S(type, "state"); g.Detail = S(type, "shortDetail");
                        foreach (var bv in A(comp, "broadcasts"))
                            foreach (var n in A(bv.GetObject(), "names"))
                                if (n.ValueType == JsonValueType.String && !g.Networks.Contains(n.GetString())) g.Networks.Add(n.GetString());
                        lock (games) games.Add(g);
                    }
                }
                catch { }
            })));
            // The next day's feed overlaps late games: one copy each; only games from that morning to the next morning.
            var from = new DateTimeOffset(localDay).ToUnixTimeMilliseconds();
            return games.Where(g => g.Start >= from && g.Start < from + 30 * 3_600_000L)
                .GroupBy(g => g.Away + "|" + g.Home + "|" + g.Start).Select(x => x.First()).OrderBy(g => g.Start).ToList();
        }

        static string Compact(string s) => Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]", "");

        /// <summary>Your channels for a network name: "TNT" → TNT East and TNT West, "FS1" → Fox Sports 1.</summary>
        static IEnumerable<Channel> ChannelsFor(string network)
        {
            var keys = Aliases.TryGetValue(network.Trim(), out var a) ? a : new[] { Compact(network) };
            foreach (var c in LiveTv.Channels)
            {
                var names = new[] { Compact(LiveTv.NormalizeName(c.Name)), Compact(c.GuideKey.Split('@')[0].Split('.')[0]) };
                if (names.Any(n => n.Length > 0 && keys.Contains(n))) yield return c;
            }
        }

        static bool IsGameListingFor(TvProgram p, EspnGame g)
        {
            var key = SportsDb.SportKey(p.Title + " " + p.Subtitle);
            var t = p.Title.ToLowerInvariant();
            if (t.Contains("pregame") || t.Contains("postgame") || t.Contains("countdown") || t.Contains("halftime")) return false;
            return key == g.Sport || (key == null && p.Title.Length == 0);
        }

        /// <summary>
        /// Puts the games into your guide: on each channel showing one, the listing at that time gets the teams
        /// (or a listing is added when the channel has none). Returns true if the guide changed.
        /// </summary>
        public static bool Enrich(List<EspnGame> games)
        {
            var changed = false;
            var hours = Feeds.ToDictionary(f => f.Sport + "|" + f.League, f => f.Hours);
            foreach (var g in games)
            {
                g.Channels.Clear();
                var nets = g.Networks.Where(n => !Streaming.Any(s => n.StartsWith(s, StringComparison.OrdinalIgnoreCase))).ToList();
                var chans = nets.SelectMany(ChannelsFor).Distinct().ToList();
                // A broadcast network on an antenna: the local station whose listing at that time is this sport.
                if (nets.Any(n => Broadcast.Contains(n, StringComparer.OrdinalIgnoreCase)))
                {
                    var local = LiveTv.Channels.Where(c => c.Id.StartsWith("hdhr:") && LiveTv.Guide.Any(p => p.ChannelId == c.Id &&
                        Math.Abs(p.Start - g.Start) <= 20 * 60_000L && IsGameListingFor(p, g) &&
                        (LiveSports.Teams(p.Subtitle) == null || (Mentions(p, g.Home) && Mentions(p, g.Away))))).ToList();
                    if (local.Count == 1) chans.Add(local[0]);
                }
                foreach (var c in chans.Distinct())
                {
                    var listing = LiveTv.Guide.Where(p => p.ChannelId == c.Id && p.Start <= g.Start + 20 * 60_000L && p.End > g.Start + 5 * 60_000L)
                        .OrderBy(p => Math.Abs(p.Start - g.Start)).FirstOrDefault();
                    if (listing != null && LiveSports.Teams(listing.Subtitle) != null && !(Mentions(listing, g.Home) && Mentions(listing, g.Away)))
                        continue; // that channel lists a different game then
                    g.Channels.Add(c);
                    if (listing == null)
                    {
                        // No guide for that channel then: add the game.
                        var len = (long)((hours.TryGetValue(g.Sport + "|" + g.League, out var h) ? h : 3) * 3_600_000);
                        LiveTv.Guide.Add(new TvProgram { ChannelId = c.Id, Title = g.League, Subtitle = g.Matchup, Start = g.Start, End = g.Start + len, Sports = true,
                            Detail = "Live " + g.League + ": " + g.Matchup + "." });
                        changed = true;
                    }
                    else if (LiveSports.Teams(listing.Subtitle) == null && (IsGameListingFor(listing, g) || listing.Title.Length == 0 || SportsDb.SportKey(listing.Title) == null && listing.Title.ToLowerInvariant().Contains("live")))
                    {
                        listing.Subtitle = g.Matchup; listing.Sports = true;
                        changed = true;
                    }
                }
            }
            if (changed) LiveTv.Guide = LiveTv.Guide.OrderBy(p => p.Start).ToList();
            return changed;
        }

        static bool Mentions(TvProgram p, string team)
        {
            var text = (p.FullTitle + " " + p.Detail).ToLowerInvariant();
            var words = (team ?? "").ToLowerInvariant().Split(' ');
            return text.Contains((team ?? "").ToLowerInvariant()) || (words.Length > 1 && text.Contains(string.Join(" ", words.Take(words.Length - 1))))
                   || (words.Length > 0 && words.Last().Length >= 5 && text.Contains(words.Last()));
        }

        /// <summary>A card for the Sports screen.</summary>
        public static SportsCard Card(EspnGame g)
        {
            var live = g.State == "in"; var done = g.State == "post";
            var c = new SportsCard
            {
                League = g.League, TeamA = g.Away, TeamB = g.Home, LogoUrlA = Nz(g.AwayLogo), LogoUrlB = Nz(g.HomeLogo), Start = g.Start,
                ThumbUrl = g.BestPicture,
                Middle = live || done ? $"{g.AwayScore} – {g.HomeScore}" : "AT",
                Channel = g.Channels.FirstOrDefault(),
            };
            var when = done ? "Final" : live ? "Live · " + g.Detail : DateTimeOffset.FromUnixTimeMilliseconds(g.Start).LocalDateTime.ToString("h:mm tt");
            var on = c.Channel != null
                ? "On " + (c.Channel.Number.Length > 0 ? c.Channel.Number + " " : "") + c.Channel.Name
                : g.Networks.Count > 0 ? string.Join(", ", g.Networks.Take(3)) + " (not in your channels)" : "";
            c.Line = when + (on.Length > 0 ? "  ·  " + on : "");
            if (c.Channel != null)
            {
                c.Program = LiveTv.Guide.FirstOrDefault(p => p.ChannelId == c.Channel.Id && p.Start <= g.Start + 20 * 60_000L && p.End > g.Start);
                c.ForceLive = live;
            }
            return c;
        }

        static string Nz(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
