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
                || url.Contains("static.tvmaze.com")
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
        static bool Wanted(TvProgram p) => !IsPoster(p.Image) && !IsGame(p) && Tmdb.Clean(p.Title) != null;

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
                        string url = null;
                        try { url = await TvMazeAsync(title); } catch { }
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

        static string Norm(string s) => Regex.Replace((s ?? "").ToLowerInvariant().Replace("&", "and"), "^the |[^a-z0-9]", "");

        /// <summary>TVmaze: a show with exactly this name (the most popular if several), its poster.</summary>
        static async Task<string> TvMazeAsync(string title)
        {
            var json = await LiveTv.Client.GetStringAsync("https://api.tvmaze.com/search/shows?q=" + Uri.EscapeDataString(title));
            var want = Norm(title);
            foreach (var v in JsonArray.Parse(json))
            {
                if (v.ValueType != JsonValueType.Object) continue;
                var show = v.GetObject().GetNamedObject("show", null);
                if (show == null || Norm(show.GetNamedString("name", "")) != want) continue;
                if (!show.ContainsKey("image") || show["image"].ValueType != JsonValueType.Object) continue;
                var img = show.GetNamedObject("image");
                var url = img.ContainsKey("original") && img["original"].ValueType == JsonValueType.String ? img.GetNamedString("original") : "";
                if (url.Length == 0 && img.ContainsKey("medium") && img["medium"].ValueType == JsonValueType.String) url = img.GetNamedString("medium");
                if (url.StartsWith("http")) return url;
            }
            return null;
        }
    }
}
