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
        static readonly System.Text.RegularExpressions.Regex PlutoArt = new System.Text.RegularExpressions.Regex(
            @"^(https?://images\.pluto\.tv/(series|episodes)/[0-9a-f]+)/(tile|poster|featuredImage)[^/?]*\.(jpg|png)(\?.*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// A real picture made for this game by the guide (Gracenote's, not its stock sport art; Sky's, with both teams'
        /// badges and the league): shown instead of the drawn one.
        /// </summary>
        public static bool IsGamePoster(string url) => !string.IsNullOrEmpty(url) && !IsGenericSportsArt(url)
            && (url.Contains("images.metadata.sky.com") || url.Contains("tmsimg.com/assets/p") && !Posters.IsPoster(url)); // wide ones only

        /// <summary>ESPN's team logos in their version for dark backgrounds (white outlines), which read on any team colour.</summary>
        public static string DarkLogo(string url) =>
            !string.IsNullOrEmpty(url) && url.Contains("espncdn.com/i/teamlogos/") && url.Contains("/500/") ? url.Replace("/500/", "/500-dark/") : url;

        /// <summary>
        /// Gracenote's stock sport pictures (asset names starting GNLZZ: the same "College Football" art for every
        /// game, often showing other teams). For games they're skipped in favour of a picture of that game.
        /// </summary>
        public static bool IsGenericSportsArt(string url) => !string.IsNullOrEmpty(url) && url.Contains("tmsimg.com/assets/GNLZZ");

        /// <summary>
        /// Wide 16:9 thumbnails for everything: Pluto's poster or tile → the episode's still (screenshot16_9) or the
        /// show's featured image; Sky's poster → its 16:9 still; Gracenote's old image host → the one that serves them.
        /// </summary>
        public static string Wide(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            if (url.StartsWith("https://zap2it.tmsimg.com/")) return "https://demo.tmsimg.com/" + url.Substring("https://zap2it.tmsimg.com/".Length);
            var sky = System.Text.RegularExpressions.Regex.Match(url, @"^(https?://images\.metadata\.sky\.com/pd-image/[0-9a-f-]+)/(3-4|16-9)/\d+");
            if (sky.Success) return sky.Groups[1].Value + "/16-9/640";
            var m = PlutoArt.Match(url);
            if (m.Success) return m.Groups[1].Value + (m.Groups[2].Value.ToLowerInvariant() == "episodes" ? "/screenshot16_9.jpg" : "/featuredImage.jpg") + "?fm=jpg&q=75&w=640&h=360";
            return url;
        }

        /// <summary>A picture that is already a wide thumbnail (after Wide).</summary>
        public static bool IsWide(string url) => !string.IsNullOrEmpty(url) && !Posters.IsPoster(url);
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
        public BitmapImage Picture { get => picture; set { picture = value; Notify(nameof(Picture)); Notify(nameof(PictureVisibility)); Notify(nameof(NamesVisibility)); Notify(nameof(LogosOrNamesVisibility)); } }
        bool HasLogos => logoA != null && logoB != null;
        bool thumbShown;   // a real poster made for this game (the guide's) is showing
        /// <summary>Two teams: drawn as a cable-style poster in the teams' colours.</summary>
        bool HasTeams => TeamA.Length > 0 && TeamB.Length > 0;
        bool ShowPicture => picture != null && (thumbShown || !HasTeams || (!HasLogos && TeamColors.Get(TeamA) == null));
        public Visibility LogosVisibility => HasLogos && !ShowPicture ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PictureVisibility => ShowPicture ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NamesVisibility => !HasLogos && !ShowPicture ? Visibility.Visible : Visibility.Collapsed;
        /// <summary>The team-colour poster: whenever a picture isn't shown.</summary>
        public Visibility LogosOrNamesVisibility => ShowPicture ? Visibility.Collapsed : Visibility.Visible;
        public string Matchup => $"{TeamA}  {Middle}  {TeamB}";
        public Visibility LiveVisibility => LiveNow ? Visibility.Visible : Visibility.Collapsed;
        /// <summary>The red LIVE badge over a picture (the drawn poster says LIVE itself).</summary>
        public Visibility LiveBadgeVisibility => LiveNow && ShowPicture ? Visibility.Visible : Visibility.Collapsed;
        bool focused;
        public bool Focused { get => focused; set { focused = value; Notify(nameof(FocusVisibility)); } }
        public Visibility FocusVisibility => focused ? Visibility.Visible : Visibility.Collapsed;

        void NotifyPicture() { Notify(nameof(LiveBadgeVisibility)); Notify(nameof(LogosVisibility)); Notify(nameof(PictureVisibility)); Notify(nameof(NamesVisibility)); Notify(nameof(LogosOrNamesVisibility)); }

        /// <summary>
        /// Like a cable guide: the guide's own poster for this game if it has one; otherwise the teams' logos on their
        /// colours; a photo of the game (TheSportsDB / ESPN) only when the teams' logos and colours aren't known.
        /// </summary>
        public async Task LoadArtAsync()
        {
            if (thumbShown || HasLogos) return;
            // 1) A real poster for this game from the guide (Gracenote, not its stock sport art).
            if (Pictures.IsGamePoster(ThumbUrl))
            {
                var t = await ChannelRow.LoadAsync(ThumbUrl, 480);
                if (t != null) { thumbShown = true; Picture = t; NotifyPicture(); return; }
            }
            // 2) Both teams' logos, on the teams' colours.
            var a = LogoUrlA ?? TeamLogos.LogoUrl(TeamA); var b = LogoUrlB ?? TeamLogos.LogoUrl(TeamB);
            if (a != null && b != null)
            {
                var la = await ChannelRow.LoadAsync(Pictures.DarkLogo(a), 256); var lb = await ChannelRow.LoadAsync(Pictures.DarkLogo(b), 256);
                if (la != null && lb != null) { LogoA = la; LogoB = lb; NotifyPicture(); return; }
            }
            // 3) A photo of the game; 4) the show's picture or the channel logo.
            if (picture == null && !string.IsNullOrEmpty(ThumbUrl)) Picture = await ChannelRow.LoadAsync(ThumbUrl, 640);
            if (picture == null && !string.IsNullOrEmpty(PictureUrl)) Picture = await ChannelRow.LoadAsync(Pictures.Wide(PictureUrl), 640);
            if (picture == null && Channel != null && !string.IsNullOrEmpty(Channel.Logo) && Channel.Logo != PictureUrl) Picture = await ChannelRow.LoadAsync(Channel.Logo, 400);
        }

        /// <summary>The time and channel, under the matchup on the poster.</summary>
        public string PosterFooter => Channel != null ? Channel.Name : "";
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
                var url = g.BestPicture; // TheSportsDB, then ESPN's photo, then ESPN's matchup graphic
                if (string.IsNullOrEmpty(url)) continue;
                // The guide's own poster comes first: these only fill games that have no picture.
                foreach (var p in LiveTv.Guide.Where(p => g.Channels.Any(c => c.Id == p.ChannelId) && p.Start <= g.Start + 20 * 60_000L && p.End > g.Start && p.Subtitle == g.Matchup))
                    if (p.Image.Length == 0 || p.Image.Contains("espncdn.com") || p.Image.Contains("thesportsdb.com") || Pictures.IsGenericSportsArt(p.Image))
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
                    // The show's poster from the guide first, then TheSportsDB's picture of the game.
                    ThumbUrl = p.Image.Length > 0 && !Pictures.IsGenericSportsArt(p.Image) ? Pictures.Wide(p.Image)
                        : teams.Item3.Length > 0 ? Nz(CachedThumb(teams.Item1, teams.Item3, DateTimeOffset.FromUnixTimeMilliseconds(p.Start).LocalDateTime)) : null,
                    Line = (live ? $"Live now · ends {DateTimeOffset.FromUnixTimeMilliseconds(p.End).LocalDateTime:h:mm tt}" : DateTimeOffset.FromUnixTimeMilliseconds(p.Start).LocalDateTime.ToString("h:mm tt")) + "  ·  " + chName,
                });
            }
            // The same game on two channels (East/West feeds): keep the first.
            return cards.GroupBy(c => c.TeamA + "|" + c.TeamB + "|" + c.Start / 600_000 + (c.TeamB.Length == 0 ? "|" + NetworkOf(c.Channel.Id) : ""))
                .Select(g => g.First()).ToList();
        }
    }

    /// <summary>
    /// Each team's colours, from ESPN's scoreboard (every NFL, NBA, WNBA, NHL, MLB, MLS and college team), for the
    /// cable-style game posters. Kept in Data\livetv\team-colors.json so recordings and later games have them.
    /// </summary>
    public static class TeamColors
    {
        static Dictionary<string, (string Main, string Alt)> cache;
        static readonly object Lock = new object();
        static bool dirty;
        static string File => Storage.DataFolder("livetv") + "\\team-colors.json";

        static void Load()
        {
            if (cache != null) return;
            var c = new Dictionary<string, (string, string)>();
            try
            {
                foreach (var kv in JsonObject.Parse(Fs.ReadAllText(File)))
                {
                    var a = kv.Value.GetArray();
                    c[kv.Key] = (a.GetStringAt(0), a.Count > 1 ? a.GetStringAt(1) : "");
                }
            }
            catch { }
            cache = c;
        }

        public static void Remember(string team, string main, string alt)
        {
            if (string.IsNullOrEmpty(team) || Parse(main) == null) return;
            Load();
            var k = TeamLogos.Norm(team);
            if (k.Length < 3) return;
            lock (Lock)
            {
                if (cache.TryGetValue(k, out var c) && c.Main == main && c.Alt == (alt ?? "")) return;
                cache[k] = (main, alt ?? "");
                dirty = true;
            }
        }

        public static void Flush()
        {
            if (!dirty) return;
            try
            {
                var o = new JsonObject();
                lock (Lock)
                {
                    foreach (var kv in cache)
                    {
                        var a = new JsonArray { JsonValue.CreateStringValue(kv.Value.Main), JsonValue.CreateStringValue(kv.Value.Alt) };
                        o[kv.Key] = a;
                    }
                    dirty = false;
                }
                Fs.WriteAllText(File, o.Stringify());
            }
            catch { }
        }

        /// <summary>The team's colours ("9e1b32" style), if known; also tries the name without its first or last word.</summary>
        public static (string Main, string Alt)? Get(string team)
        {
            Load();
            var k = TeamLogos.Norm(team);
            if (k.Length < 3) return null;
            lock (Lock)
            {
                if (cache.TryGetValue(k, out var c)) return c;
                // "Alabama" ↔ "Alabama Crimson Tide", "Browns" ↔ "Cleveland Browns"
                foreach (var kv in cache)
                    if (kv.Key.StartsWith(k + " ") || kv.Key.EndsWith(" " + k)) return kv.Value;
            }
            return null;
        }

        static Windows.UI.Color? Parse(string hex)
        {
            hex = (hex ?? "").Trim().TrimStart('#');
            if (hex.Length != 6) return null;
            try { var v = Convert.ToInt32(hex, 16); return Windows.UI.Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v); }
            catch { return null; }
        }

        static double Light(Windows.UI.Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
        static double Distance(Windows.UI.Color a, Windows.UI.Color b) =>
            Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));

        /// <summary>Deep enough for white logos and text to read on it.</summary>
        static Windows.UI.Color Deep(Windows.UI.Color c)
        {
            var keep = Light(c) > 0.55 ? 0.5 : Light(c) > 0.35 ? 0.75 : 0.9;
            return Windows.UI.Color.FromArgb(255, (byte)(c.R * keep), (byte)(c.G * keep), (byte)(c.B * keep));
        }

        /// <summary>The two sides of a game's poster: each team's colour (the home team's second colour when the two clash).</summary>
        public static Tuple<Windows.UI.Color, Windows.UI.Color> Pair(string away, string home)
        {
            var ka = Get(away); var kh = Get(home);
            var a = (ka.HasValue ? Parse(ka.Value.Main) : null) ?? ChannelRow.Palette(away);
            var h = (kh.HasValue ? Parse(kh.Value.Main) : null) ?? ChannelRow.Palette(home + "*");
            if (Distance(Deep(a), Deep(h)) < 70)
            {
                var alt = kh.HasValue ? Parse(kh.Value.Alt) : null;
                if (alt != null && Distance(Deep(a), Deep(alt.Value)) >= 70) h = alt.Value;
                else if (ka.HasValue && Parse(ka.Value.Alt) is Windows.UI.Color aa && Distance(Deep(aa), Deep(h)) >= 70) a = aa;
            }
            return Tuple.Create(Deep(a), Deep(h));
        }
    }
}
