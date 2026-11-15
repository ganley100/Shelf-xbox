using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace Shelf
{
    /// <summary>
    /// The library's 16:9 thumbnails: a movie's backdrop (with its title logo), a TV show's background picture, a game's
    /// TheSportsDB thumbnail, a recording's guide picture or a frame from the video. Saved in Data\thumbs the first time.
    /// Your own poster (next to the video or in the Posters folder) wins: it is shown inside the thumbnail instead.
    /// </summary>
    public static class Thumbs
    {
        static readonly System.Threading.SemaphoreSlim Gate = new System.Threading.SemaphoreSlim(1, 1);

        static string Saved(string key) => Storage.DataFolder("thumbs") + "\\" + LiveTv.Hash(key) + ".img";

        /// <summary>Saved bytes for this key; else fetched once (an empty file means "none": not asked again).</summary>
        static async Task<byte[]> OnceAsync(string key, Func<Task<byte[]>> fetch)
        {
            var p = Saved(key);
            if (Fs.Exists(p)) { var b = await Task.Run(() => Fs.ReadAllBytes(p)); return b.Length > 0 ? b : null; }
            byte[] got = null;
            try { got = await fetch(); } catch { }
            try { Fs.WriteAllBytes(p, got ?? new byte[0]); } catch { }
            return got;
        }

        public static async Task LoadAsync(Item it)
        {
            if (it.IsGroup)
            {
                if (PosterFolder.Find(it) != null) return; // your poster for the season
                var first = it.Children[0];
                await LoadAsync(first);
                if (first.Wide != null) { it.LogoArt = first.LogoArt; it.Wide = first.Wide; }
                return;
            }
            if (Library.OwnPoster(it.Path) != null || PosterFolder.Find(it) != null) return;
            byte[] bytes = null;
            bool gray = false;
            switch (it.Kind)
            {
                case Kind.Movie:
                {
                    var m = await Movies.LookUpAsync(it.Path);
                    if (m == null) break;
                    // Each edition its own backdrop where the movie has more than one; black-and-white editions in black and white.
                    var all = m.Backdrops.Count > 0 ? m.Backdrops : (m.Backdrop.Length > 0 ? new System.Collections.Generic.List<string> { m.Backdrop } : null);
                    if (all == null) break;
                    var i = it.Episode.Length > 0 && it.Episode != "Theatrical Cut" && all.Count > 1 ? 1 : 0;
                    gray = Movies.IsBlackAndWhite(it.Episode);
                    bytes = await OnceAsync("movie|" + all[i], () => LiveTv.ImageBytesAsync(all[i]));
                    if (m.Logo.Length > 0)
                    {
                        var logo = await OnceAsync("logo|" + m.Logo, () => LiveTv.ImageBytesAsync(m.Logo));
                        if (logo != null) it.LogoArt = gray ? await Covers.GrayFromBytesAsync(logo, 120, true) : await Covers.FromBytesAsync(logo, 120);
                    }
                    break;
                }
                case Kind.TvShow:
                    bytes = await OnceAsync("show|" + it.Title.ToLowerInvariant(), () => ShowBackgroundAsync(it.Title));
                    if (bytes == null) bytes = await Covers.VideoFrameAsync(it);
                    break;
                case Kind.SportsEvent:
                {
                    var th = SportsEvents.WideThumb(it);
                    if (th.Length > 0) bytes = await OnceAsync("sdb|" + th, () => LiveTv.ImageBytesAsync(th));
                    break;
                }
                case Kind.Recording:
                case Kind.SportsRecording:
                {
                    string pic = "";
                    try { var mp = Recorder.MetaPath(it.Path); if (Fs.Exists(mp)) pic = JsonObject.Parse(Fs.ReadAllText(mp)).GetNamedString("thumb", ""); } catch { }
                    if (pic.StartsWith("http") && !Posters.IsPoster(pic)) bytes = await OnceAsync("rec|" + pic, () => LiveTv.ImageBytesAsync(pic));
                    if (bytes != null && !await IsWideAsync(bytes)) bytes = null;
                    // A game without a 16:9 picture keeps its poster (shown inside the thumbnail), not a frame.
                    if (bytes == null && it.Kind == Kind.Recording) bytes = await Covers.VideoFrameAsync(it);
                    break;
                }
            }
            if (bytes == null) return;
            it.Wide = gray ? await Covers.GrayFromBytesAsync(bytes, 216) : await Covers.FromBytesAsync(bytes, 216);
        }

        /// <summary>Wider than tall (a thumbnail, not a poster or a square logo).</summary>
        static async Task<bool> IsWideAsync(byte[] bytes)
        {
            try
            {
                var bmp = await Covers.FromBytesAsync(bytes, 100);
                return bmp != null && bmp.PixelWidth > bmp.PixelHeight * 1.2;
            }
            catch { return false; }
        }

        /// <summary>TVmaze: the show's background picture (the British one first for UK names).</summary>
        static async Task<byte[]> ShowBackgroundAsync(string title)
        {
            var t = Tmdb.Clean(title) ?? title;
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\s*\(?\b(UK|US|USA)\b\)?\s*$", "").Trim();
            await Gate.WaitAsync();
            try
            {
                await Task.Delay(600); // TVmaze allows 20 requests per 10 seconds
                var json = await LiveTv.Client.GetStringAsync("https://api.tvmaze.com/singlesearch/shows?q=" + Uri.EscapeDataString(t));
                var id = (int)JsonObject.Parse(json).GetNamedNumber("id", 0);
                if (id == 0) return null;
                await Task.Delay(600);
                var imgs = JsonArray.Parse(await LiveTv.Client.GetStringAsync("https://api.tvmaze.com/shows/" + id + "/images"));
                string best = null;
                foreach (var v in imgs.Where(v => v.ValueType == JsonValueType.Object).Select(v => v.GetObject()))
                {
                    if (v.GetNamedString("type", "") != "background") continue;
                    var res = v.GetNamedObject("resolutions", null);
                    var r = res?.GetNamedObject("medium", null) ?? res?.GetNamedObject("original", null);
                    var u = r?.GetNamedString("url", "");
                    if (string.IsNullOrEmpty(u)) continue;
                    if (best == null || v.GetNamedBoolean("main", false)) best = u;
                }
                return best == null ? null : await LiveTv.ImageBytesAsync(best);
            }
            catch { return null; }
            finally { Gate.Release(); }
        }
    }
}
