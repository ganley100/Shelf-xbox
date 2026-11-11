using System;
using System.Threading.Tasks;
using Windows.Storage;

namespace Shelf
{
    /// <summary>
    /// Where everything lives. On an Xbox in Developer Mode that's D:\DevelopmentFiles\Shelf (reachable from
    /// Device Portal's file explorer too). If that folder can't be opened (e.g. on a PC), the app's own folder.
    /// </summary>
    public static class Storage
    {
        public const string XboxRoot = @"D:\DevelopmentFiles\Shelf";

        public static readonly string[] Folders =
        {
            "Books", "Comics", "Manga", "Audiobooks", "Music", "Podcasts", "Movies", "Photos", "Games", "Recordings", "Saves", "Data",
        };

        public static StorageFolder Root { get; private set; }
        public static bool OnDevelopmentFiles { get; private set; }
        public static string Problem { get; private set; } = "";

        public static async Task InitAsync()
        {
            try
            {
                var dev = await StorageFolder.GetFolderFromPathAsync(@"D:\DevelopmentFiles");
                Root = await dev.CreateFolderAsync("Shelf", CreationCollisionOption.OpenIfExists);
                // Prove we can write there, not only read.
                var probe = await Root.CreateFileAsync(".write-test", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(probe, DateTime.Now.ToString("o"));
                await probe.DeleteAsync();
                OnDevelopmentFiles = true;
            }
            catch (Exception e)
            {
                Problem = e.Message;
                Root = await ApplicationData.Current.LocalFolder.CreateFolderAsync("Shelf", CreationCollisionOption.OpenIfExists);
                OnDevelopmentFiles = false;
            }
            foreach (var f in Folders) await Root.CreateFolderAsync(f, CreationCollisionOption.OpenIfExists);
        }

        public static Task<StorageFolder> FolderAsync(string name) =>
            Root.CreateFolderAsync(name, CreationCollisionOption.OpenIfExists).AsTask();
    }
}
