using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace Shelf
{
    /// <summary>
    /// Show pictures from TMDB (themoviedb.org) for guide entries that have none, with your own free key
    /// (entered on the phone page, kept in Data\livetv\tmdb.json on the Xbox). Each title is looked up once;
    /// only exact title matches are used; games keep their matchup cards.
    /// This product uses the TMDB API but is not endorsed or certified by TMDB.
    /// </summary>
    public static class Tmdb
    {
        const string Api = "https://api.themoviedb.org/3";
        const string Images = "https://image.tmdb.org/t/p/";
        static string key = "";
        static Dictionary<string, (string Url, long At)> cache;
        static readonly object Lock = new object();
        static string Dir => Storage.DataFolder("livetv");

        public static bool Enabled { get { Load(); return key.Length > 0; } }

        static void Load()
        {
            if (cache != null) return;
            cache = new Dictionary<string, (string, long)>();
            try { key = JsonObject.Parse(Fs.ReadAllText(Dir + "\\tmdb.json")).GetNamedString("key", ""); } catch { key = ""; }
            try
            {
                foreach (var kv in JsonObject.Parse(Fs.ReadAllText(Dir + "\\tmdb-cache.json")))
                {
                    var a = kv.Value.GetArray();
                    cache[kv.Key] = (a.GetStringAt(0), (long)a.GetNumberAt(1));
                }
            }
            catch { }
        }

        static void Save()
        {
            var o = new JsonObject();
            lock (Lock)
                foreach (var kv in cache.OrderByDescending(x => x.Value.At).Take(20000))
                    o[kv.Key] = new JsonArray { JsonValue.CreateStringValue(kv.Value.Url), JsonValue.CreateNumberValue(kv.Value.At) };
            try { Fs.WriteAllText(Dir + "\\tmdb-cache.json", o.Stringify()); } catch { }
        }

        /// <summary>Checks the key with TMDB, then keeps it. Blank turns TMDB off.</summary>
        public static async Task SetKeyAsync(string raw)
        {
            Load();
            var k = (raw ?? "").Trim();
            if (k.Length > 0)
            {
                HttpResponseMessage r;
                try { r = await Send(Api + "/configuration", k); }
                catch (Exception e) { throw new ArgumentException("Couldn't reach TMDB: " + e.Message); }
                if ((int)r.StatusCode == 401) throw new ArgumentException("TMDB didn't accept that key. Copy the API Key (or Read Access Token) from themoviedb.org → Settings → API.");
                if (!r.IsSuccessStatusCode) throw new ArgumentException("TMDB answered " + (int)r.StatusCode + ". Try again in a minute.");
            }
            key = k;
            Fs.WriteAllText(Dir + "\\tmdb.json", new JsonObject { ["key"] = JsonValue.CreateStringValue(k) }.Stringify());
        }

        static Task<HttpResponseMessage> Send(string url, string k)
        {
            var bearer = k.StartsWith("eyJ"); // v4 read access token
            var req = new HttpRequestMessage(HttpMethod.Get, bearer ? url : url + (url.Contains("?") ? "&" : "?") + "api_key=" + Uri.EscapeDataString(k));
            if (bearer) req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", k);
            return LiveTv.Client.SendAsync(req);
        }

        static bool IsGame(TvProgram p) => LiveSports.Teams(p.Subtitle) != null || LiveSports.Teams(p.Title) != null;

        /// <summary>"Bones - The Parts…" → "Bones", "New: Seinfeld" → "Seinfeld"; null for filler.</summary>
        public static string Clean(string title)
        {
            var t = Regex.Replace((title ?? "").Trim(), @"^(live|new|premiere|season premiere|series premiere|encore|replay)\s*:\s*", "", RegexOptions.IgnoreCase);
            var dash = t.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0) t = t.Substring(0, dash);
            t = Regex.Replace(t, @"\s*\(\d{4}\)\s*$", "").Trim();
            t = Regex.Replace(t, @"\s+", " ");
            if (t.Length < 2) return null;
            if (Regex.IsMatch(t, @"^(paid program(ming)?|local programming|to be announced|tba|tbd|sign off|off air|program|programming|news|weather|movie|film|infomercial|shopping|channel no longer available|no information)$", RegexOptions.IgnoreCase))
                return null;
            return t;
        }

        static string Norm(string s)
        {
            var n = (s ?? "").ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            n = new string(n.Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
            n = n.Replace("&", "and");
            n = Regex.Replace(n, @"^the\s+", "");
            return Regex.Replace(n, "[^a-z0-9]+", "");
        }

        /// <summary>Gives pictures to shows that have none (soonest first, up to [limit] new lookups). Returns how many got one.</summary>
        public static async Task<int> FillAsync(List<TvProgram> guide, int limit = 400)
        {
            if (!Enabled) return 0;
            var now = LiveTv.Now;
            var week = 7 * 86_400_000L;
            var need = guide.Where(p => p.Image.Length == 0 && !IsGame(p) && p.End > now).ToList();
            var todo = need.OrderBy(p => p.Start).Select(p => Clean(p.Title)).Where(t => t != null).Distinct()
                .Where(t => { lock (Lock) return !cache.TryGetValue(t, out var c) || (c.Url.Length == 0 && now - c.At > week); })
                .Take(limit).ToList();
            var gate = new SemaphoreSlim(4); // well under TMDB's rate limit
            var stop = false;
            await Task.WhenAll(todo.Select(async t =>
            {
                await gate.WaitAsync();
                try
                {
                    if (stop) return;
                    var url = await SearchAsync(t);
                    if (url != null) lock (Lock) cache[t] = (url, now);
                }
                catch (UnauthorizedAccessException) { stop = true; }
                catch { }
                finally { gate.Release(); }
            }));
            if (todo.Count > 0) Save();
            int n = 0;
            lock (Lock)
                foreach (var p in need)
                {
                    var t = Clean(p.Title);
                    if (t != null && cache.TryGetValue(t, out var c) && c.Url.Length > 0) { p.Image = c.Url; n++; }
                }
            return n;
        }

        /// <summary>The picture for an exact title match; "" when TMDB has none; null on trouble (try again later).</summary>
        static async Task<string> SearchAsync(string title)
        {
            var r = await Send(Api + "/search/multi?include_adult=false&language=en-US&query=" + Uri.EscapeDataString(title), key);
            if ((int)r.StatusCode == 401) throw new UnauthorizedAccessException();
            if (!r.IsSuccessStatusCode) return null;
            var results = JsonObject.Parse(await r.Content.ReadAsStringAsync()).GetNamedArray("results", new JsonArray());
            var want = Norm(title);
            foreach (var v in results)
            {
                if (v.ValueType != JsonValueType.Object) continue;
                var o = v.GetObject();
                var type = o.GetNamedString("media_type", "");
                if (type != "tv" && type != "movie") continue;
                var names = new[] { "name", "title", "original_name", "original_title" }.Select(f => o.ContainsKey(f) && o[f].ValueType == JsonValueType.String ? o.GetNamedString(f) : "");
                if (!names.Any(x => x.Length > 0 && Norm(x) == want)) continue;
                string Path(string f) => o.ContainsKey(f) && o[f].ValueType == JsonValueType.String ? o.GetNamedString(f) : "";
                if (Path("backdrop_path").StartsWith("/")) return Images + "w780" + Path("backdrop_path");
                if (Path("poster_path").StartsWith("/")) return Images + "w500" + Path("poster_path");
            }
            return "";
        }
    }

    /// <summary>
    /// Team logos for game cards from TheSportsDB's free API: exact team names only (the sport decides when
    /// names clash), each looked up once and remembered, one lookup every 2 seconds in the background.
    /// </summary>
    public static class TeamLogos
    {
        const string Api = "https://www.thesportsdb.com/api/v1/json/123";
        static Dictionary<string, (string Url, long At)> cache;
        static readonly object Lock = new object();
        static bool running;
        public static event Action Changed;
        static string File => Storage.DataFolder("livetv") + "\\teams.json";

        static void Load()
        {
            if (cache != null) return;
            cache = new Dictionary<string, (string, long)>();
            try
            {
                foreach (var kv in JsonObject.Parse(Fs.ReadAllText(File)))
                {
                    var a = kv.Value.GetArray();
                    cache[kv.Key] = (a.GetStringAt(0), (long)a.GetNumberAt(1));
                }
            }
            catch { }
        }

        static string Norm(string s)
        {
            var n = (s ?? "").ToLowerInvariant();
            n = Regex.Replace(n, @"^(no\.?|#)\s*\d+\s+", "");          // "No. 5 Duke"
            n = Regex.Replace(n, @"\s*\(\d+-\d+(-\d+)?\)$", "");        // "Duke (7-1)"
            return Regex.Replace(n.Replace("&", "and"), "[^a-z0-9]+", " ").Trim();
        }

        /// <summary>The team's logo address, if already known.</summary>
        public static string LogoUrl(string team)
        {
            Load();
            lock (Lock) return cache.TryGetValue(Norm(team), out var c) && c.Url.Length > 0 ? c.Url : null;
        }

        static string SportOf(string title)
        {
            var t = (title ?? "").ToLowerInvariant();
            if (t.Contains("basketball") || Regex.IsMatch(t, @"\b(nba|wnba)\b")) return "Basketball";
            if (t.Contains("hockey") || Regex.IsMatch(t, @"\bnhl\b")) return "Ice Hockey";
            if (t.Contains("baseball") || Regex.IsMatch(t, @"\bmlb\b")) return "Baseball";
            if (t.Contains("soccer") || Regex.IsMatch(t, @"\b(mls|premier league|liga|serie a|bundesliga|champions league|fifa)\b")) return "Soccer";
            if (t.Contains("football") || Regex.IsMatch(t, @"\bnfl\b")) return "American Football";
            return null;
        }

        /// <summary>Looks up the teams of upcoming games, in the background (soonest first, 40 per run).</summary>
        static readonly LinkedList<(string Team, string Title)> queue = new LinkedList<(string, string)>();

        /// <summary>Looks up the teams of upcoming games in the guide, in the background (soonest first).</summary>
        public static void LookUpInBackground(List<TvProgram> guide)
        {
            var now = LiveTv.Now;
            var games = guide.Where(p => p.End > now && p.IsSports).OrderBy(p => p.Start)
                .Select(p => (Teams: LiveSports.Teams(p.Subtitle) ?? LiveSports.Teams(p.Title), p.Title))
                .Where(x => x.Teams != null)
                .SelectMany(x => new[] { (x.Teams.Item1, x.Title), (x.Teams.Item3, x.Title) });
            Enqueue(games.Take(80), front: false);
        }

        /// <summary>A logo known from elsewhere (ESPN's scoreboard): no lookup needed for that team.</summary>
        public static void Remember(string team, string url)
        {
            if (string.IsNullOrEmpty(team) || string.IsNullOrEmpty(url)) return;
            Load();
            var k = Norm(team);
            if (k.Length < 3) return;
            lock (Lock)
            {
                if (cache.TryGetValue(k, out var c) && c.Url == url) return;
                cache[k] = (url, LiveTv.Now);
            }
            dirty = true;
        }
        static bool dirty;

        /// <summary>Writes remembered logos to the Xbox (after a batch of Remember calls).</summary>
        public static void Flush() { if (dirty) { dirty = false; Save(); } }

        /// <summary>Teams someone is looking at right now (the Sports screen): ahead of everything else.</summary>
        public static void LookUpNow(IEnumerable<(string Team, string Title)> teams) => Enqueue(teams, front: true);

        static void Enqueue(IEnumerable<(string Team, string Title)> teams, bool front)
        {
            Load();
            var now = LiveTv.Now;
            var week = 7 * 86_400_000L;
            lock (Lock)
            {
                foreach (var (team, title) in front ? teams.Reverse() : teams)
                {
                    var k = Norm(team);
                    if (k.Length < 3) continue;
                    if (cache.TryGetValue(k, out var c) && (c.Url.Length > 0 || now - c.At < week)) continue;
                    var existing = queue.FirstOrDefault(q => Norm(q.Team) == k);
                    if (existing.Team != null) { if (!front) continue; queue.Remove(existing); }
                    if (front) queue.AddFirst((team, title)); else queue.AddLast((team, title));
                }
                if (running || queue.Count == 0) return;
                running = true;
            }
            var _ = Task.Run(async () =>
            {
                int n = 0;
                try
                {
                    while (true)
                    {
                        (string Team, string Title) next;
                        lock (Lock)
                        {
                            if (queue.Count == 0) break;
                            next = queue.First.Value; queue.RemoveFirst();
                        }
                        var url = await SearchAsync(next.Team, next.Title);
                        if (url != null)
                        {
                            if (url.Length > 0) await LiveTv.ImageBytesAsync(url); // keep the picture on the Xbox
                            lock (Lock) cache[Norm(next.Team)] = (url, LiveTv.Now);
                            if (url.Length > 0) Changed?.Invoke();
                        }
                        if (++n % 5 == 0) Save();
                        await Task.Delay(2100); // the free API allows about 30 a minute
                    }
                }
                catch { }
                finally { lock (Lock) running = false; Save(); }
            });
        }

        static void Save()
        {
            try
            {
                var o = new JsonObject();
                lock (Lock) foreach (var kv in cache) o[kv.Key] = new JsonArray { JsonValue.CreateStringValue(kv.Value.Url), JsonValue.CreateNumberValue(kv.Value.At) };
                Fs.WriteAllText(File, o.Stringify());
            }
            catch { }
        }

        static async Task<string> SearchAsync(string team, string title)
        {
            try
            {
                var json = await LiveTv.Client.GetStringAsync(Api + "/searchteams.php?t=" + Uri.EscapeDataString(team.Trim()));
                var root = JsonObject.Parse(json);
                if (!root.ContainsKey("teams") || root["teams"].ValueType != JsonValueType.Array) return "";
                var want = Norm(team);
                var exact = root.GetNamedArray("teams").Where(v => v.ValueType == JsonValueType.Object).Select(v => v.GetObject()).Where(o =>
                {
                    string S(string f) => o.ContainsKey(f) && o[f].ValueType == JsonValueType.String ? o.GetNamedString(f) : "";
                    return Norm(S("strTeam")) == want || S("strTeamAlternate").Split(',').Any(a => a.Trim().Length > 0 && Norm(a) == want);
                }).ToList();
                var sport = SportOf(title);
                var best = exact.FirstOrDefault(o => sport != null && o.ContainsKey("strSport") && o["strSport"].ValueType == JsonValueType.String && o.GetNamedString("strSport") == sport) ?? exact.FirstOrDefault();
                if (best == null) return "";
                foreach (var f in new[] { "strBadge", "strTeamBadge", "strLogo" })
                    if (best.ContainsKey(f) && best[f].ValueType == JsonValueType.String && best.GetNamedString(f).StartsWith("http")) return best.GetNamedString(f);
                return "";
            }
            catch { return null; }
        }
    }
}
