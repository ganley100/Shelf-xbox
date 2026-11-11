using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace Shelf
{
    /// <summary>Where you left off in each book, comic or player (Data\progress.json).</summary>
    public static class Progress
    {
        static Dictionary<string, double> map;
        static string File => Storage.Folder("Data") + "\\progress.json";

        static void Load()
        {
            if (map != null) return;
            map = new Dictionary<string, double>();
            try { foreach (var kv in JsonObject.Parse(Fs.ReadAllText(File))) map[kv.Key] = kv.Value.GetNumber(); } catch { }
        }

        public static Task<double> GetAsync(Item it)
        {
            Load();
            return Task.FromResult(map.TryGetValue(it.Path, out var v) ? v : 0);
        }

        public static Task SetAsync(Item it, double value)
        {
            Load();
            map[it.Path] = value;
            var o = new JsonObject();
            foreach (var kv in map) o[kv.Key] = JsonValue.CreateNumberValue(kv.Value);
            try { Fs.WriteAllText(File, o.Stringify()); } catch { }
            return Task.CompletedTask;
        }
    }
}
