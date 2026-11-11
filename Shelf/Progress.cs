using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;

namespace Shelf
{
    /// <summary>Where you left off in each book, comic or player (kept in Data\progress.json).</summary>
    public static class Progress
    {
        static Dictionary<string, double> map;

        static async Task LoadAsync()
        {
            if (map != null) return;
            map = new Dictionary<string, double>();
            try
            {
                var data = await Storage.FolderAsync("Data");
                var f = await data.GetFileAsync("progress.json");
                var o = JsonObject.Parse(await FileIO.ReadTextAsync(f));
                foreach (var kv in o) map[kv.Key] = kv.Value.GetNumber();
            }
            catch { }
        }

        public static async Task<double> GetAsync(Item it)
        {
            await LoadAsync();
            return map.TryGetValue(it.File.Path, out var v) ? v : 0;
        }

        public static async Task SetAsync(Item it, double value)
        {
            await LoadAsync();
            map[it.File.Path] = value;
            var o = new JsonObject();
            foreach (var kv in map) o[kv.Key] = JsonValue.CreateNumberValue(kv.Value);
            try
            {
                var data = await Storage.FolderAsync("Data");
                var f = await data.CreateFileAsync("progress.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(f, o.Stringify());
            }
            catch { }
        }
    }
}
