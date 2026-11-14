using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;

namespace Shelf
{
    /// <summary>One game card on the Sports screen: from the TV guide or from TheSportsDB's schedule.</summary>
    public sealed class SportsCard : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        void Notify(string n) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n));

        public string League = "", TeamA = "", TeamB = "", Middle = "AT", Line = "", LogoUrlA, LogoUrlB;
        /// <summary>A picture for the game when the team logos aren't known: the show's picture, the event's, or the channel logo.</summary>
        public string PictureUrl;
        public long Start;
        public Channel Channel;      // a channel showing it (watch / record)
        public TvProgram Program;    // its guide entry
        public bool LiveNow => Program != null && Program.Start <= LiveTv.Now && Program.End > LiveTv.Now;

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
        public Visibility LogosVisibility => HasLogos ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PictureVisibility => !HasLogos && picture != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NamesVisibility => !HasLogos && picture == null ? Visibility.Visible : Visibility.Collapsed;
        public string Matchup => $"{TeamA}  {Middle}  {TeamB}";
        public Visibility LiveVisibility => LiveNow ? Visibility.Visible : Visibility.Collapsed;
        bool focused;
        public bool Focused { get => focused; set { focused = value; Notify(nameof(FocusVisibility)); } }
        public Visibility FocusVisibility => focused ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Team logos if both are known; otherwise the game's picture (and the logos later, when they arrive).</summary>
        public async Task LoadArtAsync()
        {
            if (HasLogos) return;
            var a = LogoUrlA ?? TeamLogos.LogoUrl(TeamA); var b = LogoUrlB ?? TeamLogos.LogoUrl(TeamB);
            if (a != null && b != null)
            {
                var la = await ChannelRow.LoadAsync(a, 256); var lb = await ChannelRow.LoadAsync(b, 256);
                if (la != null && lb != null) { LogoA = la; LogoB = lb; return; }
            }
            if (picture == null && !string.IsNullOrEmpty(PictureUrl)) Picture = await ChannelRow.LoadAsync(PictureUrl, 400);
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

        /// <summary>A channel in your guide showing this game (both team names in the listing), if any.</summary>
        public static void LinkToGuide(SportsCard c)
        {
            if (c.Channel != null) return;
            string Key(string t) => t.Split(' ').Last().ToLowerInvariant(); // "Green Bay Packers" -> "packers"
            var a = Key(c.TeamA); var b = Key(c.TeamB);
            var p = LiveTv.Guide.FirstOrDefault(x => x.IsSports && Math.Abs(x.Start - c.Start) < 3 * 3_600_000L &&
                (x.FullTitle.ToLowerInvariant().Contains(a) && x.FullTitle.ToLowerInvariant().Contains(b)));
            if (p == null) return;
            c.Program = p;
            c.Channel = LiveTv.Channels.FirstOrDefault(ch => ch.Id == p.ChannelId);
            if (c.PictureUrl == null && p.Image.Length > 0) c.PictureUrl = p.Image;
            if (c.Channel != null) c.Line += "  ·  " + (c.Channel.Number.Length > 0 ? c.Channel.Number + " " : "") + c.Channel.Name;
        }

        /// <summary>Games on your channels now and in the next 12 hours, from the guide.</summary>
        public static List<SportsCard> FromGuide()
        {
            var now = LiveTv.Now;
            var cards = new List<SportsCard>();
            foreach (var p in LiveTv.Guide.Where(x => x.End > now && x.Start < now + 12 * 3_600_000L && x.IsSports).OrderBy(x => x.Start))
            {
                var teams = LiveSports.Teams(p.Subtitle) ?? LiveSports.Teams(p.Title);
                if (teams == null) continue; // shows about sports, not games
                var ch = LiveTv.Channels.FirstOrDefault(c => c.Id == p.ChannelId);
                if (ch == null) continue;
                var live = p.Start <= now;
                var chName = (ch.Number.Length > 0 ? ch.Number + " " : "") + ch.Name;
                cards.Add(new SportsCard
                {
                    League = LiveSports.Teams(p.Subtitle) != null ? p.Title : "Live sports",
                    TeamA = teams.Item1, TeamB = teams.Item3, Middle = teams.Item2.ToUpperInvariant(),
                    Start = p.Start, Program = p, Channel = ch, PictureUrl = p.Image.Length > 0 ? p.Image : null,
                    Line = (live ? $"Live now · ends {DateTimeOffset.FromUnixTimeMilliseconds(p.End).LocalDateTime:h:mm tt}" : DateTimeOffset.FromUnixTimeMilliseconds(p.Start).LocalDateTime.ToString("h:mm tt")) + "  ·  " + chName,
                });
            }
            // The same game on two channels (East/West feeds): keep the first.
            return cards.GroupBy(c => c.TeamA + "|" + c.TeamB + "|" + c.Start / 600_000).Select(g => g.First()).ToList();
        }
    }
}
