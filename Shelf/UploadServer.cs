using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage;

namespace Shelf
{
    /// <summary>
    /// A small web page on the home network for adding files from a phone or PC: open the address shown
    /// on the TV, pick a shelf and files, and they're saved into that folder under D:\DevelopmentFiles\Shelf.
    /// </summary>
    public static class UploadServer
    {
        static StreamSocketListener listener;
        public static int Port { get; private set; }
        public static event Action Uploaded;

        public static string Address
        {
            get
            {
                var ip = NetworkInformation.GetHostNames()
                    .FirstOrDefault(h => h.Type == HostNameType.Ipv4 && h.IPInformation?.NetworkAdapter != null && !h.CanonicalName.StartsWith("169.254"));
                return ip == null || Port == 0 ? null : $"http://{ip.CanonicalName}:{Port}";
            }
        }

        public static async Task StartAsync()
        {
            if (listener != null) return;
            for (int port = 8080; port <= 8095; port++)
            {
                var l = new StreamSocketListener();
                l.Control.KeepAlive = false;
                l.ConnectionReceived += (s, a) => { var _ = HandleAsync(a.Socket); };
                try { await l.BindServiceNameAsync(port.ToString()); listener = l; Port = port; return; }
                catch { l.Dispose(); }
            }
        }

        static async Task HandleAsync(StreamSocket socket)
        {
            using (socket)
            using (var input = socket.InputStream.AsStreamForRead(64 * 1024))
            using (var output = socket.OutputStream.AsStreamForWrite())
            {
                try
                {
                    var head = await ReadHeadAsync(input);
                    if (head == null) return;
                    var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var first = lines[0].Split(' ');
                    if (first.Length < 2) return;
                    var method = first[0];
                    var target = first[1];
                    var path = target.Split('?')[0];
                    var query = ParseQuery(target.Contains("?") ? target.Substring(target.IndexOf('?') + 1) : "");
                    var headers = lines.Skip(1).Where(l => l.Contains(":"))
                        .ToDictionary(l => l.Substring(0, l.IndexOf(':')).Trim().ToLowerInvariant(), l => l.Substring(l.IndexOf(':') + 1).Trim());

                    if (method == "GET" && (path == "/" || path == "/index.html"))
                        await SendAsync(output, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page));
                    else if ((method == "PUT" || method == "POST") && path == "/api/upload")
                        await UploadAsync(input, output, headers, query);
                    else if (path.StartsWith("/api/tv"))
                        await TvAsync(output, method, path, query);
                    else
                        await SendAsync(output, 404, "text/plain", Encoding.UTF8.GetBytes("Not found"));
                }
                catch (Exception e)
                {
                    try { await SendAsync(output, 500, "application/json", Json("error", e.Message)); } catch { }
                }
            }
        }

        static async Task UploadAsync(Stream input, Stream output, Dictionary<string, string> headers, Dictionary<string, string> q)
        {
            if (!headers.TryGetValue("content-length", out var lenText) || !long.TryParse(lenText, out var length))
            { await SendAsync(output, 411, "application/json", Json("error", "Upload needs a Content-Length.")); return; }
            q.TryGetValue("folder", out var folderName);
            q.TryGetValue("name", out var name);
            if (!Storage.Folders.Contains(folderName) || folderName == "Saves" || folderName == "Data")
            { await SendAsync(output, 400, "application/json", Json("error", "Pick a shelf.")); return; }
            name = Path.GetFileName((name ?? "").Replace('\\', '/').Split('/').Last());
            if (string.IsNullOrWhiteSpace(name)) { await SendAsync(output, 400, "application/json", Json("error", "Missing file name.")); return; }

            var folder = Storage.Folder(folderName); // every file is its own item: no grouping
            var file = Fs.UniquePath(folder, Clean(name));
            try
            {
                using (var dst = Fs.Create(file))
                {
                    var buf = new byte[256 * 1024];
                    long left = length;
                    while (left > 0)
                    {
                        var n = await input.ReadAsync(buf, 0, (int)Math.Min(buf.Length, left));
                        if (n <= 0) throw new IOException("The upload was cut off.");
                        await dst.WriteAsync(buf, 0, n);
                        left -= n;
                    }
                }
            }
            catch
            {
                Fs.Delete(file);
                throw;
            }
            await SendAsync(output, 200, "application/json", Json("saved", Path.GetFileName(file)));
            Uploaded?.Invoke();
        }

        /// <summary>Live TV sources from the phone: playlists, tuners, remove, reload.</summary>
        static async Task TvAsync(Stream output, string method, string path, Dictionary<string, string> q)
        {
            await LiveTv.InitAsync();
            string Arg(string k) => q.TryGetValue(k, out var v) ? v : "";
            if (method == "POST")
            {
                try
                {
                    switch (path)
                    {
                        case "/api/tv/playlist": await LiveTv.AddPlaylistAsync(Arg("url"), Arg("guide")); LiveTv.RefreshInBackground(false); break;
                        case "/api/tv/tuner": await LiveTv.AddTunerAsync(Arg("address")); LiveTv.RefreshInBackground(false); break;
                        case "/api/tv/remove": await LiveTv.RemoveAsync(Arg("key")); LiveTv.RefreshInBackground(false); break;
                        case "/api/tv/refresh": LiveTv.RefreshInBackground(true); break;
                        default: await SendAsync(output, 404, "application/json", Json("error", "Not found.")); return;
                    }
                }
                catch (Exception e)
                {
                    await SendAsync(output, 400, "application/json", Json("error", e.Message)); return;
                }
                await Task.Delay(300); // let the reload start, so the page shows it
            }
            var o = new Windows.Data.Json.JsonObject();
            var t = new Windows.Data.Json.JsonArray(); foreach (var x in LiveTv.Tuners) t.Add(Windows.Data.Json.JsonValue.CreateStringValue(x));
            var p = new Windows.Data.Json.JsonArray(); foreach (var x in LiveTv.Playlists) p.Add(Windows.Data.Json.JsonValue.CreateStringValue(x.Url));
            o["tuners"] = t; o["playlists"] = p;
            o["channels"] = Windows.Data.Json.JsonValue.CreateNumberValue(LiveTv.Channels.Count);
            o["withGuide"] = Windows.Data.Json.JsonValue.CreateNumberValue(LiveTv.Channels.Count(c => LiveTv.NowPlaying(c.Id) != null));
            o["busy"] = Windows.Data.Json.JsonValue.CreateBooleanValue(LiveTv.Busy);
            o["status"] = Windows.Data.Json.JsonValue.CreateStringValue(LiveTv.Status ?? "");
            o["error"] = Windows.Data.Json.JsonValue.CreateStringValue(LiveTv.LastError ?? "");
            await SendAsync(output, 200, "application/json", Encoding.UTF8.GetBytes(o.Stringify()));
        }

        static string Clean(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim().Length == 0 ? "file" : s.Trim();
        }

        static byte[] Json(string k, string v) =>
            Encoding.UTF8.GetBytes("{\"" + k + "\":\"" + (v ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");

        static Dictionary<string, string> ParseQuery(string q) =>
            q.Split('&').Where(p => p.Length > 0).Select(p => p.Split(new[] { '=' }, 2))
             .GroupBy(p => WebUtility.UrlDecode(p[0])).ToDictionary(g => g.Key, g => WebUtility.UrlDecode(g.First().Length > 1 ? g.First()[1] : ""));

        /// <summary>Reads up to the blank line ending the request headers, byte by byte (the body follows).</summary>
        static async Task<string> ReadHeadAsync(Stream s)
        {
            var sb = new StringBuilder();
            var one = new byte[1];
            while (sb.Length < 16 * 1024)
            {
                if (await s.ReadAsync(one, 0, 1) <= 0) return null;
                sb.Append((char)one[0]);
                if (sb.Length >= 4 && sb[sb.Length - 1] == '\n' && sb[sb.Length - 2] == '\r' && sb[sb.Length - 3] == '\n' && sb[sb.Length - 4] == '\r')
                    return sb.ToString(0, sb.Length - 4);
            }
            return null;
        }

        static async Task SendAsync(Stream o, int code, string type, byte[] body)
        {
            var reason = code == 200 ? "OK" : code == 404 ? "Not Found" : "Error";
            var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
            await o.WriteAsync(head, 0, head.Length);
            await o.WriteAsync(body, 0, body.Length);
            await o.FlushAsync();
        }

        const string Page = @"<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>Shelf</title><style>
body{background:#111;color:#eee;font:17px -apple-system,Segoe UI,sans-serif;margin:0;padding:24px 16px;max-width:640px;margin:auto}
h1{font-family:Georgia,serif;margin:0}p.sub{color:#999;margin:4px 0 24px}
select,button,input{font:inherit;border-radius:10px;border:1px solid #444;background:#1c1c1c;color:#eee;padding:12px;width:100%;box-sizing:border-box;margin:6px 0}
button{background:#f5b82e;color:#111;border:0;font-weight:600}ul{padding:0;list-style:none}li{padding:8px 0;border-bottom:1px solid #222;color:#bbb}
.ok{color:#7bd67b}.bad{color:#ff8a80}</style></head><body>
<h1>Shelf</h1><p class='sub'>Your media library · add files to your Xbox. Every file shows up as its own item.</p>
<label>Shelf</label><select id='folder'>
<option>Books</option><option>Comics</option><option>Manga</option><option>Audiobooks</option><option>Music</option>
<option>Podcasts</option><option>Movies</option><option>Photos</option><option>Games</option></select>
<input type='file' id='files' multiple><button id='go'>Upload</button><ul id='log'></ul>
<h2 style='margin-top:36px'>Live TV</h2>
<p class='sub' id='tvst'>Loading…</p>
<label>Channel playlist (M3U) address</label><input id='m3u' placeholder='https://iptv-org.github.io/iptv/countries/us.m3u'>
<button class='ghost' id='usList' style='background:#262626;color:#eee'>Use the free US list (iptv-org)</button>
<label>Guide (XMLTV) address, optional</label><input id='xml' placeholder='Leave empty: iptv-org lists get free guides with pictures'>
<button id='addList'>Add playlist</button>
<label>HDHomeRun antenna tuner address</label><input id='tuner' placeholder='e.g. 192.168.1.50'>
<button id='addTuner'>Add tuner</button>
<button id='find' style='background:#262626;color:#eee'>Find tuners &amp; reload channels</button>
<ul id='src'></ul>
<script>
document.getElementById('go').onclick=async function(){
 var fs=document.getElementById('files').files,folder=document.getElementById('folder').value,log=document.getElementById('log');
 for(var i=0;i<fs.length;i++){var f=fs[i],li=document.createElement('li');li.textContent='Uploading '+f.name+'…';log.prepend(li);
  try{var r=await fetch('/api/upload?folder='+encodeURIComponent(folder)+'&name='+encodeURIComponent(f.name),{method:'PUT',body:f});
   var j=await r.json();if(!r.ok)throw new Error(j.error||'failed');li.textContent='✓ '+f.name;li.className='ok';}
  catch(e){li.textContent='✗ '+f.name+': '+e.message;li.className='bad';}}
};
function tv(path,params){var q=Object.keys(params||{}).map(function(k){return k+'='+encodeURIComponent(params[k]);}).join('&');
 return fetch('/api/tv'+path+(q?'?'+q:''),{method:path?'POST':'GET'}).then(function(r){return r.json().then(function(j){if(!r.ok)throw new Error(j.error||'That didn\'t work.');return j;});})
 .then(show).catch(function(e){document.getElementById('tvst').textContent=e.message;});}
var poll=null;
function show(t){var st=document.getElementById('tvst');
 st.textContent=t.busy?((t.status||'Loading…')+' ('+t.channels+' channels so far). You can leave this page.'):(t.error?t.error+' · ':'')+t.channels+' channels on the TV, '+t.withGuide+' with guide info';
 var src=document.getElementById('src');src.innerHTML='';
 t.tuners.concat(t.playlists).forEach(function(k){var li=document.createElement('li');li.textContent=k+' ';var b=document.createElement('button');b.textContent='Remove';b.style.width='auto';b.style.padding='4px 12px';b.onclick=function(){tv('/remove',{key:k});};li.appendChild(b);src.appendChild(li);});
 if(t.busy&&!poll)poll=setTimeout(function(){poll=null;tv('',{});},2000);}
document.getElementById('usList').onclick=function(){document.getElementById('m3u').value='https://iptv-org.github.io/iptv/countries/us.m3u';};
document.getElementById('addList').onclick=function(){document.getElementById('tvst').textContent='Checking the link…';tv('/playlist',{url:document.getElementById('m3u').value,guide:document.getElementById('xml').value});};
document.getElementById('addTuner').onclick=function(){tv('/tuner',{address:document.getElementById('tuner').value});};
document.getElementById('find').onclick=function(){tv('/refresh',{});};
tv('',{});
</script></body></html>";
    }
}
