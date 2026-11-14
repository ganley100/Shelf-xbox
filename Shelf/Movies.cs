using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace Shelf
{
    /// <summary>A movie's details from TMDB.</summary>
    public sealed class MovieInfo
    {
        public string Title = "", Year = "", Overview = "", Poster = "";
    }

    /// <summary>
    /// Movie posters and details from TMDB, found from the file name ("The Dark Knight (2008).mkv",
    /// "The.Dark.Knight.2008.1080p.BluRay.x264.mkv"). Each file is looked up once and remembered in
    /// Data\movies.json. Needs the TMDB key from the phone page.
    /// </summary>
    public static class Movies
    {
        static Dictionary<string, JsonObject> cache;
        static string File => Storage.Folder("Data") + "\\movies.json";

        static void Load()
        {
            if (cache != null) return;
            cache = new Dictionary<string, JsonObject>();
            try { foreach (var kv in JsonObject.Parse(Fs.ReadAllText(File))) cache[kv.Key] = kv.Value.GetObject(); } catch { }
        }

        static void Save()
        {
            var o = new JsonObject();
            lock (cache) foreach (var kv in cache) o[kv.Key] = kv.Value;
            try { Fs.WriteAllText(File, o.Stringify()); } catch { }
        }

        /// <summary>"The.Dark.Knight.2008.1080p.BluRay.x264" → ("The Dark Knight", "2008").</summary>
        public static (string Title, string Year) FromFileName(string name)
        {
            var s = name.Replace('.', ' ').Replace('_', ' ');
            string year = "";
            // The last year in the name ("Blade Runner 2049 (2017)" → 2017, "1917 (2019)" → 2019).
            var all = Regex.Matches(s, @"[\(\[ ]((19|20)\d{2})[\)\] ]|[\(\[ ]((19|20)\d{2})$");
            var y = all.Count > 0 ? all[all.Count - 1] : Match.Empty;
            if (y.Success)
            {
                year = y.Groups[1].Success ? y.Groups[1].Value : y.Groups[3].Value;
                s = s.Substring(0, y.Index); // everything after the year is release info
            }
            s = Regex.Replace(s, @"\b(2160p|1080p|720p|480p|4k|uhd|hdr|hdr10|dv|bluray|blu ray|bdrip|brrip|remux|web ?dl|webrip|hdtv|dvdrip|x264|x265|h ?264|h ?265|hevc|aac|ac3|dts|atmos|truehd|10bit|extended|unrated|remastered|directors cut|imax)\b.*$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"[\[\(].*?[\]\)]", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim(' ', '-');
            return (s, year);
        }

        public static MovieInfo Cached(string path)
        {
            Load();
            lock (cache)
                if (cache.TryGetValue(path.ToLowerInvariant(), out var o) && o.ContainsKey("title"))
                    return new MovieInfo { Title = o.GetNamedString("title", ""), Year = o.GetNamedString("year", ""), Overview = o.GetNamedString("overview", ""), Poster = o.GetNamedString("poster", "") };
            return null;
        }

        /// <summary>The movie's details (looked up once). Null if TMDB is off or has no match.</summary>
        public static async Task<MovieInfo> LookUpAsync(string path)
        {
            Load();
            var k = path.ToLowerInvariant();
            lock (cache)
                if (cache.TryGetValue(k, out var o))
                {
                    var tried = o.GetNamedNumber("tried", 0);
                    if (o.ContainsKey("title")) return Cached(path);
                    if (LiveTv.Now - tried < 7 * 86_400_000L) return null; // no match: ask again in a week
                }
            if (!Tmdb.Enabled) return null;
            var (title, year) = FromFileName(System.IO.Path.GetFileNameWithoutExtension(path));
            if (title.Length < 1) return null;
            var m = await Tmdb.FindMovieAsync(title, year);
            var entry = new JsonObject { ["tried"] = JsonValue.CreateNumberValue(LiveTv.Now) };
            if (m != null)
            {
                entry["title"] = JsonValue.CreateStringValue(m.Title); entry["year"] = JsonValue.CreateStringValue(m.Year);
                entry["overview"] = JsonValue.CreateStringValue(m.Overview); entry["poster"] = JsonValue.CreateStringValue(m.Poster);
            }
            lock (cache) cache[k] = entry;
            Save();
            return m;
        }
    }
}
