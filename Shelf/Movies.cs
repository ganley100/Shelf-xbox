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
        public string Title = "", Year = "", Overview = "", Poster = "", Backdrop = "";
        /// <summary>More of the movie's backdrops (best first): each edition of a movie gets its own.</summary>
        public List<string> Backdrops = new List<string>();
        /// <summary>The movie's title logo (transparent PNG from TMDB), shown over its thumbnail.</summary>
        public string Logo = "";
        /// <summary>The movie's posters (best first): each edition gets its own.</summary>
        public List<string> Posters = new List<string>();
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

        static readonly (string Pattern, string Name)[] Editions =
        {
            (@"director'?s[\s\-]*cut|directors[\s\-]*cut", "Director's Cut"),
            (@"theatrical([\s\-]*(cut|version|edition))?", "Theatrical Cut"),
            (@"extended([\s\-]*(cut|edition|version))?", "Extended Edition"),
            (@"final[\s\-]*cut", "Final Cut"),
            (@"ultimate[\s\-]*(cut|edition)", "Ultimate Cut"),
            (@"special[\s\-]*edition", "Special Edition"),
            (@"black[\s\-]*(and|&|n)[\s\-]*chrome", "Black & Chrome"),
            (@"black[\s\-]*(and|&|n)[\s\-]*white|\bb[\s]?&[\s]?w\b|\bb-w\b|noir[\s\-]*(edition|version)|\bgr[ae]yscale\b", "Black & White"),
            (@"\bunrated\b", "Unrated"),
            (@"\buncut\b", "Uncut"),
            (@"\bredux\b", "Redux"),
            (@"\bimax\b", "IMAX"),
            (@"\bremaster(ed)?\b", "Remastered"),
        };

        /// <summary>The edition in a file name ("Spawn (1997) Director's Cut" → "Director's Cut"); "" for none.</summary>
        public static string Edition(string name)
        {
            var s = name.Replace('.', ' ').Replace('_', ' ');
            foreach (var (pattern, n) in Editions)
                if (Regex.IsMatch(s, pattern, RegexOptions.IgnoreCase)) return n;
            return "";
        }

        /// <summary>A black-and-white edition: its thumbnail is shown in black and white too.</summary>
        public static bool IsBlackAndWhite(string edition) => edition == "Black & White" || edition == "Black & Chrome";

        /// <summary>
        /// The poster for this edition: the theatrical / plain version gets the movie's best poster, every other edition
        /// its own one of the movie's other posters (always the same one for that edition).
        /// </summary>
        public static string PosterFor(MovieInfo m, string edition)
        {
            var all = m.Posters.Count > 0 ? m.Posters : (m.Poster.Length > 0 ? new List<string> { m.Poster } : new List<string>());
            if (all.Count == 0) return "";
            if (edition.Length == 0 || edition == "Theatrical Cut" || IsBlackAndWhite(edition) || all.Count == 1) return all[0];
            int h = 0; unchecked { foreach (var ch in edition) h = h * 31 + ch; }
            return all[1 + Math.Abs(h % (all.Count - 1))];
        }

        /// <summary>"The.Dark.Knight.2008.1080p.BluRay.x264" → ("The Dark Knight", "2008").</summary>
        public static (string Title, string Year) FromFileName(string name)
        {
            var s = name.Replace('.', ' ').Replace('_', ' ');
            foreach (var (pattern, _) in Editions) s = Regex.Replace(s, pattern, " ", RegexOptions.IgnoreCase); // the edition isn't part of the title
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
                    return new MovieInfo { Title = o.GetNamedString("title", ""), Year = o.GetNamedString("year", ""), Overview = o.GetNamedString("overview", ""), Poster = o.GetNamedString("poster", ""), Backdrop = o.GetNamedString("backdrop", ""),
                        Logo = o.GetNamedString("logo", ""),
                        Posters = o.ContainsKey("posters") ? o.GetNamedArray("posters").Where(v => v.ValueType == JsonValueType.String).Select(v => v.GetString()).ToList() : new List<string>(),
                        Backdrops = o.ContainsKey("backdrops") ? o.GetNamedArray("backdrops").Where(v => v.ValueType == JsonValueType.String).Select(v => v.GetString()).ToList() : new List<string>() };
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
                    if (o.ContainsKey("title") && (o.ContainsKey("posters") || !Tmdb.Enabled)) return Cached(path); // older entries: once more, for the wide picture
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
                entry["backdrop"] = JsonValue.CreateStringValue(m.Backdrop);
                var arr = new JsonArray(); foreach (var b in m.Backdrops) arr.Add(JsonValue.CreateStringValue(b));
                entry["backdrops"] = arr;
                entry["logo"] = JsonValue.CreateStringValue(m.Logo);
                var parr = new JsonArray(); foreach (var b in m.Posters) parr.Add(JsonValue.CreateStringValue(b));
                entry["posters"] = parr;
            }
            lock (cache) cache[k] = entry;
            Save();
            return m;
        }
    }
}
