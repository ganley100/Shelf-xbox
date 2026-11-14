using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;

namespace Shelf
{
    /// <summary>Pictures shown in the wide (16:9) guide slots.</summary>
    public static class Pictures
    {
        static readonly System.Text.RegularExpressions.Regex PlutoTile = new System.Text.RegularExpressions.Regex(
            @"^(https?://images\.pluto\.tv/(series|channels)/[0-9a-f]+)/tile[^/?]*\.(jpg|png)(\?.*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>The landscape version when the source has one: Pluto's square "tile" → its 16:9 "featuredImage".</summary>
        public static string Wide(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            var m = PlutoTile.Match(url);
            return m.Success ? m.Groups[1].Value + "/featuredImage.jpg?fm=jpg&q=75&w=1280&h=720" : url;
        }
    }

    /// <summary>One game card on the Sports screen: from the TV guide or from TheSportsDB's schedule.</summary>
    public sealed class SportsCard : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        void Notify(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));

        public string League = "", TeamA = "", TeamB = "", Middle = "AT", Line = "", LogoUrlA, LogoUrlB;
        /// <summary>A picture for the game when the team logos aren't known: the show's picture, the event's, or the channel logo.</summary>
        public string PictureUrl;
        /// <summary>TheSportsDB's picture of this game: shown first when there is one.</summary>
        public string ThumbUrl;
        public DateTime Day => DateTimeOffset.FromUnixTimeMilliseconds(Start).LocalDateTime;
        public long Start;
        public Channel Channel;      // a channel showing it (watch / record)
        public List<Channel> Maybe;  // channels that might show it (same sport, same time)
        public List<TvProgram> MaybePrograms;
        public TvProgram Program;    // its guide entry
        public bool ForceLive;
        public bool LiveNow => ForceLive || (Program != null && Program.Start <= LiveTv.Now && Program.End > LiveTv.Now);

        public string LeagueUpper => League.ToUpperInvariant();
        public string A => TeamA;
        public string B => TeamB;
        public string MiddleText => Middle;
        public string LineText => Line;
        BitmapImage logoA, logoB;
        public BitmapImage LogoA { get => logoA; set { logoA = value; Notify(nameof(LogoA)); Notify(nameof(LogosVisibility)); Notify(nameof(NamesVisibility)); } }
        public BitmapImage LogoB { get => logoB; set { logoB = value; Notify(nameof(LogoB)); Notify(nameof(LogosVisibility)); Notify(nameof(NamesVisibility)); } }
        BitmapImage picture;
        public BitmapImage Picture { get => picture; set { picture = value; Notify(nameof(Picture)); Notify(nameof(PictureVisibility)); Notify(nameof(NamesVisibility)); } }
        bool HasLogos => logoA != null && logoB != null;
        bool thumbShown;
        bool ShowPicture => picture != null && (thumbShown || !HasLogos);
        public Visibility LogosVisibility => HasLogos && !ShowPicture ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PictureVisibility => ShowPicture ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NamesVisibility => !HasLogos && picture == null ? Visibility.Visible : Visibility.Collapsed;
        public string Matchup => $"{TeamA}  {Middle}  {TeamB}";
        public Visibility LiveVisibility => LiveNow ? Visibility.Visible : Visibility.Collapsed;
        bool focused;
        public bool Focused { get => focused; set { focused = value; Notify(nameof(FocusVisibility)); } }
        public Visibility FocusVisibility => focused ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Team logos if both are known; otherwise the game's picture (and the logos later, when they arrive).</summary>
        public async Task LoadArtAsync()
        {
            // 1) TheSportsDB's picture of the game.
            if (!thumbShown && !string.IsNullOrEmpty(ThumbUrl))
            {
                var t = await ChannelRow.LoadAsync(ThumbUrl, 640);
                if (t != null) { thumbShown = true; Picture = t; Notify(nameof(LogosVisibility)); return; }
            }
            if (thumbShown || HasLogos) return;
            // 2) Both teams' logos; 3) the show's picture or the channel logo.
            var a = LogoUrlA ?? TeamLogos.LogoUrl(TeamA); var b = LogoUrlB ?? TeamLogos.LogoUrl(TeamB);
            if (a != null && b != null)
            {
                var la = await ChannelRow.LoadAsync(a, 256); var lb = await ChannelRow.LoadAsync(b, 256);
                if (la != null && lb != null) { LogoA = la; LogoB = lb; return; }
            }
            if (picture == null && !string.IsNullOrEmpty(PictureUrl)) Picture = await ChannelRow.LoadAsync(Pictures.Wide(PictureUrl), 640);
            if (picture == null && Channel != null && !string.IsNullOrEmpty(Channel.Logo) && Channel.Logo != PictureUrl) Picture = await ChannelRow.LoadAsync(Channel.Logo, 400);
        }
    }

    /// <summary>
    /// Today's schedule and scores from TheSportsDB. The free key gives only a few games per sport; your own
    /// key (Patreon supporters, entered on the phone page) gives every game.
    /// </summary>
    public static class SportsDb
    {
        public static readonly (int Id, string Name)[] Leagues =
        {
            (4391, "NFL"), (4479, "NCAA Football"), (4387, "NBA"), (4607, "NCAA Basketball"), (4380, "NHL"),
            (4424, "MLB"), (4516, "WNBA"), (4346, "MLS"), (4328, "Premier League"),
        };

        static string File => Storage.DataFolder("livetv") + "\\sportsdb.json";
        public static string Key
        {
            get { try { return JsonObject.Parse(Fs.ReadAllText(File)).GetNamedString("key", ""); } catch { return ""; } }
        }
        public static bool HasOwnKey => Key.Length > 0;
        public static string Api => "https://www.thesportsdb.com/api/v1/json/" + (HasOwnKey ? Uri.EscapeDataString(Key) : "123");

        public static async Task SetKeyAsync(string key)
        {
            var k = (key ?? "").Trim();
            if (k.Length > 0)
            {
                try
                {
                    var json = await LiveTv.Client.GetStringAsync("https://www.thesportsdb.com/api/v1/json/" + Uri.EscapeDataString(k) + "/all_sports.php");
                    if (!json.Contains("sports")) throw new Exception();
                }
                catch { throw new ArgumentException("TheSportsDB didn't accept that key."); }
            }
            Fs.WriteAllText(File, new JsonObject { ["key"] = JsonValue.CreateStringValue(k) }.Stringify());
        }

        static string S(JsonObject o, string f) => o.ContainsKey(f) && o[f].ValueType == JsonValueType.String ? o.GetNamedString(f) : "";

        /// <summary>Today's games (your local day) in the leagues above, with scores when played.</summary>
        public static async Task<List<SportsCard>> TodayAsync()
        {
            var today = DateTime.Now.Date;
            // The local day spans two UTC dates: ask for both, keep what falls on today here.
            var days = new[] { today.ToUniversalTime().Date, today.AddDays(1).ToUniversalTime().Date }.Distinct().ToList();
            var cards = new List<SportsCard>();
            var seen = new HashSet<string>();
            await Task.WhenAll(Leagues.SelectMany(l => days.Select(async d =>
            {
                try
                {
                    var json = await LiveTv.Client.GetStringAsync($"{Api}/eventsday.php?d={d:yyyy-MM-dd}&l={l.Id}");
                    var root = JsonObject.Parse(json);
                    if (!root.ContainsKey("events") || root["events"].ValueType != JsonValueType.Array) return;
                    foreach (var v in root.GetNamedArray("events"))
                    {
                        if (v.ValueType != JsonValueType.Object) continue;
                        var e = v.GetObject();
                        var id = S(e, "idEvent");
                        if (!DateTimeOffset.TryParse(S(e, "strTimestamp") + "Z", out var when)) continue;
                        if (when.LocalDateTime.Date != today) continue;
                        lock (seen) if (!seen.Add(id)) continue;
                        var home = S(e, "strHomeTeam"); var away = S(e, "strAwayTeam");
                        var hs = S(e, "intHomeScore"); var aws = S(e, "intAwayScore");
                        var status = S(e, "strStatus");
                        var played = hs.Length > 0 && aws.Length > 0;
                        var card = new SportsCard
                        {
                            League = l.Name, TeamA = away, TeamB = home, LogoUrlA = Nz(S(e, "strAwayTeamBadge")), LogoUrlB = Nz(S(e, "strHomeTeamBadge")),
                            PictureUrl = Nz(S(e, "strThumb")) ?? Nz(S(e, "strPoster")) ?? Nz(S(e, "strSquare")),
                            Middle = played ? $"{aws} – {hs}" : "AT", Start = when.ToUnixTimeMilliseconds(),
                            Line = played
                                ? (status == "FT" || status == "AOT" || status.StartsWith("Match Finished") ? "Final" : "Live · " + status)
                                : when.LocalDateTime.ToString("h:mm tt"),
                        };
                        lock (cards) cards.Add(card);
                    }
                }
                catch { }
            })));
            return cards.OrderBy(c => c.Start).ToList();
        }

        static string Nz(string s) => string.IsNullOrEmpty(s) ? null : s;

        // ---- game pictures (TheSportsDB is the first choice for live sports) ----
        static Dictionary<string, string> thumbs;
        static string ThumbFile => Storage.DataFolder("livetv") + "\\game-thumbs.json";
        static string ThumbKey(string away, string home, DateTime day) => $"{away}|{home}|{day:yyyy-MM-dd}".ToLowerInvariant();

        static void LoadThumbs()
        {
            if (thumbs != null) return;
            thumbs = new Dictionary<string, string>();
            try { foreach (var kv in JsonObject.Parse(Fs.ReadAllText(ThumbFile))) thumbs[kv.Key] = kv.Value.GetString(); } catch { }
        }

        static void SaveThumbs()
        {
            var o = new JsonObject();
            lock (thumbs) foreach (var kv in thumbs) o[kv.Key] = JsonValue.CreateStringValue(kv.Value);
            try { Fs.WriteAllText(ThumbFile, o.Stringify()); } catch { }
        }

        /// <summary>The game's picture if already found ("" = TheSportsDB has none, null = not asked yet).</summary>
        public static string CachedThumb(string away, string home, DateTime day)
        {
            LoadThumbs();
            lock (thumbs) return thumbs.TryGetValue(ThumbKey(away, home, day), out var u) ? u : null;
        }

        /// <summary>The game's picture from TheSportsDB (asked once per game, then remembered). Null if none.</summary>
        public static async Task<string> ThumbAsync(string away, string home, DateTime day)
        {
            var known = CachedThumb(away, home, day);
            if (known != null) return known.Length > 0 ? known : null;
            var url = await EventThumbAsync(away, home, day);
            lock (thumbs) thumbs[ThumbKey(away, home, day)] = url ?? "";
            SaveThumbs();
            return url;
        }

        /// <summary>Puts known game pictures on the games' guide listings (they replace other pictures). True if changed.</summary>
        public static bool ApplyThumbs(List<EspnGame> games)
        {
            var changed = false;
            foreach (var g in games.Where(x => x.Channels.Count > 0))
            {
                var url = CachedThumb(g.Away, g.Home, DateTimeOffset.FromUnixTimeMilliseconds(g.Start).LocalDateTime);
                if (string.IsNullOrEmpty(url)) continue;
                foreach (var p in LiveTv.Guide.Where(p => g.Channels.Any(c => c.Id == p.ChannelId) && p.Start <= g.Start + 20 * 60_000L && p.End > g.Start && p.Subtitle == g.Matchup))
                    if (p.Image != url) { p.Image = url; changed = true; }
            }
            return changed;
        }

        static bool filling;
        /// <summary>Looks up pictures for today's games on your channels, in the background, then updates the guide.</summary>
        public static void FillGuideInBackground(List<EspnGame> games)
        {
            if (filling) return;
            var todo = games.Where(g => g.Channels.Count > 0 && CachedThumb(g.Away, g.Home, DateTimeOffset.FromUnixTimeMilliseconds(g.Start).LocalDateTime) == null)
                .OrderBy(g => g.State == "in" ? 0 : 1).ThenBy(g => g.Start).Take(30).ToList();
            if (todo.Count == 0) return;
            filling = true;
            var _ = Task.Run(async () =>
            {
                try
                {
                    foreach (var g in todo)
                    {
                        await ThumbAsync(g.Away, g.Home, DateTimeOffset.FromUnixTimeMilliseconds(g.Start).LocalDateTime);
                        await Task.Delay(1500);
                    }
                    if (ApplyThumbs(games)) { await LiveTv.SaveGuideAsync(); LiveTv.NotifyChanged(); }
                }
                catch { }
                finally { filling = false; }
            });
        }

        /// <summary>
        /// TheSportsDB's picture for a game (strThumb: both teams, designed for the matchup), found by its
        /// "Home vs Away" name around that date. School names are tried too ("Iowa State Cyclones" → "Iowa State").
        /// Null when TheSportsDB has none (common for smaller games).
        /// </summary>
        public static async Task<string> EventThumbAsync(string away, string home, DateTime day)
        {
            IEnumerable<string> Names(string t)
            {
                yield return t;
                var w = (t ?? "").Split(' ');
                if (w.Length > 1) yield return string.Join(" ", w.Take(w.Length - 1));
            }
            foreach (var h in Names(home).Distinct())
                foreach (var a in Names(away).Distinct())
                {
                    try
                    {
                        var e = Uri.EscapeDataString((h + " vs " + a).Replace(' ', '_'));
                        var root = JsonObject.Parse(await LiveTv.Client.GetStringAsync($"{Api}/searchevents.php?e={e}"));
                        var list = root.ContainsKey("event") && root["event"].ValueType == JsonValueType.Array ? root.GetNamedArray("event") : null;
                        if (list == null) continue;
                        foreach (var v in list)
                        {
                            if (v.ValueType != JsonValueType.Object) continue;
                            var o = v.GetObject();
                            if (!DateTime.TryParse(S(o, "dateEvent"), out var d) || Math.Abs((d - day.Date).TotalDays) > 1) continue;
                            var pic = new[] { "strThumb", "strPoster", "strSquare" }.Select(f => S(o, f)).FirstOrDefault(x => x.StartsWith("http"));
                            if (pic != null) return pic;
                        }
                    }
                    catch { }
                    await Task.Delay(400); // gentle with the free API
                }
            return null;
        }

        /// <summary>"College Football", "NCAA Football" → "cfb"; "NBA" → "nba"… null when it isn't a game listing.</summary>
        public static string SportKey(string text)
        {
            var t = (text ?? "").ToLowerInvariant();
            bool college = t.Contains("college") || t.Contains("ncaa");
            if (t.Contains("wnba")) return "wnba";
            if (t.Contains("football") && !t.Contains("soccer")) return college ? "cfb" : (t.Contains("nfl") || t.Contains("football") ? "nfl" : null);
            if (t.Contains("basketball") || t.Contains("nba")) return college ? "cbb" : "nba";
            if (t.Contains("hockey") || t.Contains("nhl")) return "nhl";
            if (t.Contains("baseball") || t.Contains("mlb")) return "mlb";
            if (t.Contains("soccer") || t.Contains("mls") || t.Contains("premier league")) return "soccer";
            return null;
        }

        /// <summary>
        /// Ways a team can be written: "Iowa State Cyclones" → "iowa state cyclones", "iowa state", "cyclones".
        /// </summary>
        static IEnumerable<string> TeamKeys(string team)
        {
            var t = System.Text.RegularExpressions.Regex.Replace((team ?? "").ToLowerInvariant(), "[^a-z0-9 &.'-]", " ").Trim();
            if (t.Length == 0) yield break;
            yield return t;
            var words = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1)
            {
                yield return string.Join(" ", words.Take(words.Length - 1));     // the school / city
                var last = words.Last();
                if (last.Length >= 5 && !new[] { "state", "city", "united", "county" }.Contains(last)) yield return last; // the mascot
            }
        }

        static bool Mentions(string text, string team)
        {
            var t = " " + System.Text.RegularExpressions.Regex.Replace((text ?? "").ToLowerInvariant(), "[^a-z0-9 &.'-]", " ") + " ";
            return TeamKeys(team).Any(k => t.Contains(" " + k + " "));
        }

        /// <summary>
        /// Finds a channel showing this game. First a listing naming both teams; then, for listings that only
        /// say the sport ("College Football" on TNT), the same sport starting at the same time. When that's
        /// certain (one game, one listing in that slot) the listing gets the teams, so Live TV shows them too.
        /// When several channels could have it, they're offered as "Maybe on".
        /// </summary>
        public static bool LinkToGuide(SportsCard c, List<SportsCard> all)
        {
            if (c.Channel != null) return false;
            var p = LiveTv.Guide.FirstOrDefault(x => x.IsSports && Math.Abs(x.Start - c.Start) < 3 * 3_600_000L &&
                Mentions(x.FullTitle + " " + x.Detail, c.TeamA) && Mentions(x.FullTitle + " " + x.Detail, c.TeamB));
            var enriched = false;
            if (p == null)
            {
                var key = SportKey(c.League);
                if (key == null) return false;
                var window = 20 * 60_000L;
                var generic = LiveTv.Guide.Where(x => Math.Abs(x.Start - c.Start) <= window && x.End > LiveTv.Now &&
                        LiveSports.Teams(x.Subtitle) == null && LiveSports.Teams(x.Title) == null &&
                        SportKey(x.Title + " " + x.Subtitle) == key && !x.Title.ToLowerInvariant().Contains("pregame") &&
                        !x.Title.ToLowerInvariant().Contains("postgame") && !x.Title.ToLowerInvariant().Contains("countdown"))
                    .ToList();
                // East and West feeds of one network count once.
                var byNetwork = generic.GroupBy(x => NetworkOf(x.ChannelId)).Select(g => g.First()).ToList();
                var sameSlot = all.Count(o => SportKey(o.League) == key && Math.Abs(o.Start - c.Start) <= window);
                if (byNetwork.Count == 1 && sameSlot == 1)
                {
                    p = byNetwork[0];
                    if (p.Subtitle.Length == 0 || p.Subtitle == p.Title)
                    {
                        // TNT's listing now says who's playing (and gets its matchup card in Live TV).
                        foreach (var feed in generic) { feed.Subtitle = $"{c.TeamA} at {c.TeamB}"; feed.Sports = true; }
                        enriched = true;
                    }
                }
                else if (byNetwork.Count > 0)
                {
                    c.Maybe = byNetwork.Select(x => LiveTv.Channels.FirstOrDefault(ch => ch.Id == x.ChannelId)).Where(ch => ch != null).ToList();
                    c.MaybePrograms = byNetwork;
                    c.Line += "  ·  Maybe on " + string.Join(", ", c.Maybe.Take(3).Select(ch => ch.Name)) + (c.Maybe.Count > 3 ? "…" : "");
                    return false;
                }
                else return false;
            }
            c.Program = p;
            c.Channel = LiveTv.Channels.FirstOrDefault(ch => ch.Id == p.ChannelId);
            if (c.PictureUrl == null && p.Image.Length > 0) c.PictureUrl = p.Image;
            if (c.Channel != null) c.Line += "  ·  " + (c.Channel.Number.Length > 0 ? c.Channel.Number + " " : "") + c.Channel.Name;
            return enriched;
        }

        /// <summary>"TNT East" and "TNT West" (or "TNT.us@East" / "@West") are one network.</summary>
        static string NetworkOf(string channelId)
        {
            var ch = LiveTv.Channels.FirstOrDefault(c => c.Id == channelId);
            var n = (ch?.GuideKey.Length > 0 ? ch.GuideKey.Split('@')[0] : ch?.Name ?? channelId).ToLowerInvariant();
            return System.Text.RegularExpressions.Regex.Replace(n, @"\b(east|west|hd)\b|\(.*?\)", "").Trim();
        }

        /// <summary>Games on your channels now and in the next 12 hours, from the guide.</summary>
        public static List<SportsCard> FromGuide()
        {
            var now = LiveTv.Now;
            var cards = new List<SportsCard>();
            foreach (var p in LiveTv.Guide.Where(x => x.End > now && x.Start < now + 12 * 3_600_000L && x.IsSports).OrderBy(x => x.Start))
            {
                var teams = LiveSports.Teams(p.Subtitle) ?? LiveSports.Teams(p.Title);
                if (teams == null)
                {
                    // "College Football" with no teams named: still a game (pregame shows and talk shows aren't).
                    var tl = p.Title.ToLowerInvariant();
                    if (SportKey(p.Title) == null || tl.Contains("pregame") || tl.Contains("postgame") || tl.Contains("countdown") ||
                        tl.Contains("highlights") || tl.Contains("tonight") || tl.Contains("center") || tl.Contains("live:")) continue;
                    teams = Tuple.Create(p.Title, "", "");
                }
                var ch = LiveTv.Channels.FirstOrDefault(c => c.Id == p.ChannelId);
                if (ch == null) continue;
                var live = p.Start <= now;
                var chName = (ch.Number.Length > 0 ? ch.Number + " " : "") + ch.Name;
                cards.Add(new SportsCard
                {
                    League = LiveSports.Teams(p.Subtitle) != null || teams.Item3.Length == 0 ? p.Title : "Live sports",
                    TeamA = teams.Item1, TeamB = teams.Item3, Middle = teams.Item2.ToUpperInvariant(),
                    Start = p.Start, Program = p, Channel = ch, PictureUrl = p.Image.Length > 0 ? p.Image : null,
                    ThumbUrl = teams.Item3.Length > 0 ? Nz(CachedThumb(teams.Item1, teams.Item3, DateTimeOffset.FromUnixTimeMilliseconds(p.Start).LocalDateTime)) : null,
                    Line = (live ? $"Live now · ends {DateTimeOffset.FromUnixTimeMilliseconds(p.End).LocalDateTime:h:mm tt}" : DateTimeOffset.FromUnixTimeMilliseconds(p.Start).LocalDateTime.ToString("h:mm tt")) + "  ·  " + chName,
                });
            }
            // The same game on two channels (East/West feeds): keep the first.
            return cards.GroupBy(c => c.TeamA + "|" + c.TeamB + "|" + c.Start / 600_000 + (c.TeamB.Length == 0 ? "|" + NetworkOf(c.Channel.Id) : ""))
                .Select(g => g.First()).ToList();
        }
    }
}
