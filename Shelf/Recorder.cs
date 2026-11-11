using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Shelf
{
    /// <summary>
    /// Records a channel into D:\DevelopmentFiles\Shelf\Recordings while Shelf is open: plain TS streams
    /// (HDHomeRun) and unencrypted HLS. Encrypted (copy-protected) streams are refused: they can be watched,
    /// not saved. Stops at the end of the show (or after an hour without guide info), or when you press Y.
    /// </summary>
    public static class Recorder
    {
        sealed class Job { public CancellationTokenSource Cancel; public DateTime Started; public string Name; }
        static readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();
        public static event Action Changed;
        public const string Protected = "This channel is copy-protected (encrypted), so it can't be recorded. You can still watch it.";
        static HttpClient Http => LiveTv.Client;

        public static bool IsRecording(string channelId) { lock (jobs) return jobs.ContainsKey(channelId); }

        public static string Elapsed(string channelId)
        {
            lock (jobs) return jobs.TryGetValue(channelId, out var j) ? $"{(int)(DateTime.Now - j.Started).TotalMinutes} min" : "";
        }

        public static void Stop(string channelId)
        {
            lock (jobs) if (jobs.TryGetValue(channelId, out var j)) j.Cancel.Cancel();
        }

        /// <summary>Starts recording; returns null when it's running, or why it can't.</summary>
        public static async Task<string> StartAsync(Channel c, TvProgram now)
        {
            string why;
            try { why = await WhyNotAsync(c.Url); } catch (Exception e) { return "Couldn't reach this channel: " + e.Message; }
            if (why != null) return why;

            var end = now != null && now.End > LiveTv.Now ? DateTimeOffset.FromUnixTimeMilliseconds(now.End).LocalDateTime.AddMinutes(2) : DateTime.Now.AddHours(1);
            var title = now?.Title ?? c.Name;
            var name = Clean($"{c.Name} - {title} - {DateTime.Now:yyyy-MM-dd HHmm}");
            var job = new Job { Cancel = new CancellationTokenSource(end - DateTime.Now), Started = DateTime.Now, Name = name };
            lock (jobs) { if (jobs.ContainsKey(c.Id)) return null; jobs[c.Id] = job; }
            Changed?.Invoke();
            var _ = Task.Run(async () =>
            {
                StorageFile file = null;
                try
                {
                    var folder = await Storage.FolderAsync("Recordings");
                    file = await folder.CreateFileAsync(name + ".ts", CreationCollisionOption.GenerateUniqueName);
                    using (var output = await file.OpenStreamForWriteAsync())
                    {
                        if (IsHls(c.Url)) await RecordHlsAsync(c.Url, output, job.Cancel.Token);
                        else await RecordStreamAsync(c.Url, output, job.Cancel.Token);
                    }
                }
                catch (Exception) { }
                finally
                {
                    lock (jobs) jobs.Remove(c.Id);
                    try { if (file != null && (await file.GetBasicPropertiesAsync()).Size < 256 * 1024) await file.DeleteAsync(); } catch { }
                    Changed?.Invoke();
                }
            });
            return null;
        }

        static bool IsHls(string url) => url.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0 || url.Contains("jmp2.uk");

        static string Clean(string s)
        {
            foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s.Length > 120 ? s.Substring(0, 120) : s;
        }

        /// <summary>Null if it can be recorded, else the reason (e.g. encrypted).</summary>
        static async Task<string> WhyNotAsync(string url)
        {
            if (!IsHls(url)) return null;
            var (text, final) = await GetTextAsync(url);
            if (text.Contains("#EXT-X-STREAM-INF"))
            {
                var variant = BestVariant(text, final);
                (text, final) = await GetTextAsync(variant.ToString());
            }
            return IsEncrypted(text) ? Protected : null;
        }

        static bool IsEncrypted(string playlist) =>
            Regex.IsMatch(playlist, @"#EXT-X-(SESSION-)?KEY:.*METHOD=(?!NONE)", RegexOptions.IgnoreCase);

        static async Task<(string, Uri)> GetTextAsync(string url)
        {
            using (var r = await Http.GetAsync(url))
            {
                r.EnsureSuccessStatusCode();
                return (await r.Content.ReadAsStringAsync(), r.RequestMessage.RequestUri); // after redirects
            }
        }

        static Uri BestVariant(string master, Uri baseUri)
        {
            Uri best = null; long bestBw = -1; long bw = 0; bool next = false;
            foreach (var raw in master.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("#EXT-X-STREAM-INF"))
                {
                    var m = Regex.Match(line, @"BANDWIDTH=(\d+)");
                    bw = m.Success ? long.Parse(m.Groups[1].Value) : 0; next = true;
                }
                else if (next && line.Length > 0 && !line.StartsWith("#"))
                {
                    if (bw > bestBw) { bestBw = bw; best = new Uri(baseUri, line); }
                    next = false;
                }
            }
            return best ?? baseUri;
        }

        static async Task RecordStreamAsync(string url, Stream output, CancellationToken ct)
        {
            using (var r = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            using (var input = await r.Content.ReadAsStreamAsync())
            {
                var buf = new byte[256 * 1024];
                try
                {
                    int n;
                    while ((n = await input.ReadAsync(buf, 0, buf.Length, ct)) > 0) await output.WriteAsync(buf, 0, n);
                }
                catch (OperationCanceledException) { }
            }
        }

        /// <summary>Follows a live HLS playlist, appending each new segment to the file.</summary>
        static async Task RecordHlsAsync(string url, Stream output, CancellationToken ct)
        {
            var (text, final) = await GetTextAsync(url);
            var media = final;
            if (text.Contains("#EXT-X-STREAM-INF")) media = BestVariant(text, final);
            var seen = new HashSet<string>();
            bool wroteInit = false;
            while (!ct.IsCancellationRequested)
            {
                string pl; Uri plUri;
                try { (pl, plUri) = await GetTextAsync(media.ToString()); }
                catch { await Delay(3000, ct); continue; }
                if (IsEncrypted(pl)) return; // became protected mid-way: stop with what we have
                var target = 4.0;
                var tm = Regex.Match(pl, @"#EXT-X-TARGETDURATION:(\d+)");
                if (tm.Success) target = double.Parse(tm.Groups[1].Value);
                var map = Regex.Match(pl, @"#EXT-X-MAP:.*URI=""([^""]+)""");
                if (map.Success && !wroteInit)
                {
                    var init = await Http.GetByteArrayAsync(new Uri(plUri, map.Groups[1].Value));
                    await output.WriteAsync(init, 0, init.Length);
                    wroteInit = true;
                }
                foreach (var raw in pl.Replace("\r", "").Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var seg = new Uri(plUri, line);
                    var key = seg.AbsolutePath;
                    if (!seen.Add(key)) continue;
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var bytes = await Http.GetByteArrayAsync(seg);
                        await output.WriteAsync(bytes, 0, bytes.Length);
                    }
                    catch { }
                }
                if (pl.Contains("#EXT-X-ENDLIST")) break;
                await Delay((int)(target * 500), ct);
            }
        }

        static async Task Delay(int ms, CancellationToken ct)
        {
            try { await Task.Delay(ms, ct); } catch (OperationCanceledException) { }
        }
    }
}
