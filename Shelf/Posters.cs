using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace Shelf
{
    /// <summary>
    /// Real posters for shows in the guide that don't have one: TVmaze first (free, no key: posters for almost
    /// every TV series), then TMDB (shows and movies; your key). Exact titles only. Each title is looked up once and
    /// remembered (Data\livetv\posters.json); lookups run in the background after each guide load.
    /// Games keep their own pictures (matchup posters, TheSportsDB, ESPN).
    /// TV information and images from TVmaze.com (CC BY-SA).
    /// </summary>
    public static class Posters
    {
        static Dictionary<string, (string Url, long At)> cache;
        static readonly object Lock = new object();
        static bool running;
        static string File => Storage.DataFolder("livetv") + "\\posters.json";

        /// <summary>Pictures that are already posters (portrait): kept as they are.</summary>
        public static bool IsPoster(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return Regex.IsMatch(url, @"tmsimg\.com/assets/[^/]*_v\d", RegexOptions.IgnoreCase)  // Gracenote vertical
                || url.Contains("images.pluto.tv") && url.Contains("/poster.jpg")
                || url.Contains("image.tmdb.org/t/p/w500")
                || url.Contains("static.tvmaze.com") && !url.Contains("original_untouched")
                || url.Contains("images.metadata.sky.com") && url.Contains("/3-4/");
        }

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

        static void Save()
        {
            var o = new JsonObject();
            lock (Lock) foreach (var kv in cache) o[kv.Key] = new JsonArray { JsonValue.CreateStringValue(kv.Value.Url), JsonValue.CreateNumberValue(kv.Value.At) };
            try { Fs.WriteAllText(File, o.Stringify()); } catch { }
        }

        static bool IsGame(TvProgram p) => LiveSports.Teams(p.Subtitle) != null || LiveSports.Teams(p.Title) != null;
        /// <summary>Shows whose picture isn't a poster (none, or a wide still).</summary>
        static bool Wanted(TvProgram p) => !IsPoster(Pictures.Wide(p.Image)) && !IsGame(p) && Tmdb.Clean(p.Title) != null;

        /// <summary>Puts known posters on the guide. True if anything changed.</summary>
        public static bool Apply(List<TvProgram> guide)
        {
            Load();
            var changed = false;
            lock (Lock)
                foreach (var p in guide.Where(Wanted))
                    if (cache.TryGetValue(Tmdb.Clean(p.Title).ToLowerInvariant(), out var c) && c.Url.Length > 0 && p.Image != c.Url)
                    { p.Image = c.Url; changed = true; }
            return changed;
        }

        /// <summary>Looks up posters for shows on now and soon (up to 300 titles a run), then updates the guide.</summary>
        public static void FillInBackground(List<TvProgram> guide)
        {
            Load();
            if (running) return;
            var now = LiveTv.Now;
            var week = 7 * 86_400_000L;
            List<string> todo;
            lock (Lock)
                todo = guide.Where(p => p.End > now && Wanted(p)).OrderBy(p => p.Start)
                    .Select(p => Tmdb.Clean(p.Title)).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(t => !cache.TryGetValue(t.ToLowerInvariant(), out var c) || (c.Url.Length == 0 && now - c.At > week))
                    .Take(300).ToList();
            if (todo.Count == 0) return;
            running = true;
            var _ = Task.Run(async () =>
            {
                int n = 0;
                try
                {
                    foreach (var title in todo)
                    {
                        // The listing's own picture has a poster version (Gracenote / Roku)?
                        try
                        {
                            var pic = LiveTv.Guide.FirstOrDefault(p => Tmdb.Clean(p.Title) == title && p.Image.Length > 0)?.Image;
                            var fromPic = await PosterFromAsync(pic);
                            if (fromPic != null) { lock (Lock) cache[title.ToLowerInvariant()] = (fromPic, LiveTv.Now); continue; }
                        }
                        catch { }
                        string url = null;
                        try { url = await TvMazeAsync(title, false); } catch { }
                        if (url == null && Tmdb.Enabled) { try { url = await Tmdb.PosterAsync(title); } catch { } }
                        lock (Lock) cache[title.ToLowerInvariant()] = (url ?? "", LiveTv.Now);
                        if (++n % 20 == 0) { Save(); if (Apply(LiveTv.Guide)) { await LiveTv.SaveGuideAsync(); LiveTv.NotifyChanged(); } }
                        await Task.Delay(550); // TVmaze allows 20 requests per 10 seconds
                    }
                }
                catch { }
                finally
                {
                    running = false;
                    Save();
                    if (Apply(LiveTv.Guide)) { await LiveTv.SaveGuideAsync(); LiveTv.NotifyChanged(); }
                }
            });
        }

        /// <summary>
        /// A poster for this show right now (for a recording): the remembered one, else TVmaze, else TMDB.
        /// Null when there is none.
        /// </summary>
        static readonly System.Threading.SemaphoreSlim NowGate = new System.Threading.SemaphoreSlim(1, 1);
        public static async Task<string> LookUpNowAsync(string title, string picture = null, string channel = "")
        {
            // 1) The poster version of the picture it already has (Pluto, Sky, Gracenote / Roku).
            var fromPic = await PosterFromAsync(picture);
            if (fromPic != null) return fromPic;
            var t = Tmdb.Clean(title);
            if (t == null) return null;
            // "Antiques Roadshow UK" → the show "Antiques Roadshow", the British one.
            var uk = System.Text.RegularExpressions.Regex.IsMatch(t + " " + channel, @"\b(UK|GB|BBC|ITV)\b");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\s*\(?\b(UK|US|USA)\b\)?\s*$", "").Trim();
            Load();
            lock (Lock) if (cache.TryGetValue(t.ToLowerInvariant(), out var c) && c.Url.Length > 0) return c.Url;
            string url = null;
            // One lookup at a time, spaced out (TVmaze allows 20 requests per 10 seconds; many recordings ask at once).
            await NowGate.WaitAsync();
            try { await Task.Delay(600); url = await TvMazeAsync(t, uk); } catch { }
            finally { NowGate.Release(); }
            if (url == null && Tmdb.Enabled) { try { url = await Tmdb.PosterAsync(t); } catch { } }
            if (url != null) { lock (Lock) cache[t.ToLowerInvariant()] = (url, LiveTv.Now); Save(); }
            return url;
        }

        static string Norm(string s) => Regex.Replace((s ?? "").ToLowerInvariant().Replace("&", "and"), "^the |[^a-z0-9]", "");

        /// <summary>
        /// TVmaze: the show with this name (exactly, else one whose name starts the other's: "Fox News Saturday Night" for
        /// "FOX News Saturday Night With Jimmy Failla"), the British one first when asked; its poster.
        /// </summary>
        static async Task<string> TvMazeAsync(string title, bool preferGb)
        {
            var json = await LiveTv.Client.GetStringAsync("https://api.tvmaze.com/search/shows?q=" + Uri.EscapeDataString(title));
            var want = Norm(title);
            var hits = new List<(int Score, string Url)>();
            foreach (var v in JsonArray.Parse(json))
            {
                if (v.ValueType != JsonValueType.Object) continue;
                var show = v.GetObject().GetNamedObject("show", null);
                if (show == null || !show.ContainsKey("image") || show["image"].ValueType != JsonValueType.Object) continue;
                var img = show.GetNamedObject("image");
                var url = img.ContainsKey("medium") && img["medium"].ValueType == JsonValueType.String ? img.GetNamedString("medium") : "";
                if (!url.StartsWith("http")) continue;
                var name = Norm(show.GetNamedString("name", ""));
                int score = name == want ? 0 : name.Length >= 6 && (want.StartsWith(name) || name.StartsWith(want)) ? 2 : -1;
                if (score < 0) continue;
                string country = "";
                try
                {
                    var net = show.ContainsKey("network") && show["network"].ValueType == JsonValueType.Object ? show.GetNamedObject("network")
                            : show.ContainsKey("webChannel") && show["webChannel"].ValueType == JsonValueType.Object ? show.GetNamedObject("webChannel") : null;
                    var co = net != null && net.ContainsKey("country") && net["country"].ValueType == JsonValueType.Object ? net.GetNamedObject("country") : null;
                    country = co?.GetNamedString("code", "") ?? "";
                }
                catch { }
                if (preferGb && country != "GB") score++;
                hits.Add((score, url));
            }
            return hits.OrderBy(h => h.Score).Select(h => h.Url).FirstOrDefault();
        }

        static readonly string[] GracenoteVertical = { "b_v13_aa", "b_v12_aa", "b_v9_aa", "b_v8_aa", "b_v8_ab", "b_v11_aa", "b_v7_aa", "b_v6_aa", "b_v5_aa", "b_v4_aa" };

        /// <summary>
        /// The poster version of a picture: Pluto's and Sky's (by address), Gracenote's (also as the Roku Channel serves
        /// them: the show's portrait art under the same number, found once and remembered). Null if none.
        /// </summary>
        public static async Task<string> PosterFromAsync(string picture)
        {
            if (string.IsNullOrEmpty(picture)) return null;
            var w = Pictures.Wide(picture);
            if (IsPoster(w)) return w;
            var m = System.Text.RegularExpressions.Regex.Match(picture, @"(?:tmsimg\.com|/gracenote)/assets/(p\d+)_[a-z]+_[hv]\d+_[a-z]+");
            if (!m.Success) return null;
            var pid = m.Groups[1].Value;
            Load();
            lock (Lock) if (cache.TryGetValue("gn:" + pid, out var c)) return c.Url.Length > 0 ? c.Url : null;
            string found = null;
            foreach (var v in GracenoteVertical)
            {
                var u = $"https://demo.tmsimg.com/assets/{pid}_{v}.jpg";
                try
                {
                    using (var r = await LiveTv.Client.SendAsync(new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, u)))
                        if (r.IsSuccessStatusCode) { found = u; break; }
                }
                catch { }
            }
            lock (Lock) cache["gn:" + pid] = (found ?? "", LiveTv.Now);
            Save();
            return found;
        }
    }
}
