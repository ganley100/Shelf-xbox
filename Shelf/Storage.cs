using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;

namespace Shelf
{
    /// <summary>
    /// Where everything lives: D:\DevelopmentFiles\Shelf on an Xbox in Developer Mode. Tried in order:
    /// a folder you picked before (remembered permission), the path itself, creating it with the *FromApp
    /// Win32 calls (what emulators use for full disk access), and finally the app's own folder, in which
    /// case the home screen offers "Choose the Shelf folder" (the system folder picker grants access).
    /// </summary>
    public static class Storage
    {
        public const string XboxRoot = @"D:\DevelopmentFiles\Shelf";
        const string Token = "shelf-root";

        public static readonly string[] Folders =
        {
            "Books", "Comics", "Manga", "Audiobooks", "Music", "Podcasts", "Movies", "Photos", "Games", "Recordings", "Saves", "Data",
        };

        public static StorageFolder Root { get; private set; }
        public static bool OnDevelopmentFiles { get; private set; }
        public static string Problem { get; private set; } = "";

        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateDirectoryFromAppW(string path, IntPtr securityAttributes);
        [DllImport("api-ms-win-core-file-fromapp-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool RemoveDirectoryFromAppW(string path);

        /// <summary>
        /// Whether the low-level (Win32 "FromApp") file calls may write in D:\DevelopmentFiles: the way emulators
        /// reach it. Shown on the home screen so we know which way to go.
        /// </summary>
        public static string Win32Check { get; private set; } = "";

        static void CheckWin32()
        {
            try
            {
                var probe = @"D:\DevelopmentFiles\.shelf-probe-" + DateTime.Now.Ticks;
                if (CreateDirectoryFromAppW(probe, IntPtr.Zero))
                {
                    RemoveDirectoryFromAppW(probe);
                    Win32Check = "Win32 access to D:\\DevelopmentFiles: OK";
                }
                else Win32Check = "Win32 access to D:\\DevelopmentFiles: error " + Marshal.GetLastWin32Error();
            }
            catch (Exception e) { Win32Check = "Win32 access check failed: " + e.Message; }
        }

        public static async Task InitAsync()
        {
            Root = null;
            CheckWin32();
            // 1) A folder picked earlier.
            try
            {
                if (StorageApplicationPermissions.FutureAccessList.ContainsItem(Token))
                    Root = await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(Token);
            }
            catch (Exception e) { Problem = e.Message; }

            // 2) The path, or 3) create it the Win32 way and open it.
            if (Root == null) Root = await TryPathAsync();

            if (Root != null && await CanWriteAsync(Root))
            {
                OnDevelopmentFiles = true;
                Problem = "";
            }
            else
            {
                Root = await ApplicationData.Current.LocalFolder.CreateFolderAsync("Shelf", CreationCollisionOption.OpenIfExists);
                OnDevelopmentFiles = false;
            }
            foreach (var f in Folders) await Root.CreateFolderAsync(f, CreationCollisionOption.OpenIfExists);
        }

        static async Task<StorageFolder> TryPathAsync()
        {
            try { return await StorageFolder.GetFolderFromPathAsync(XboxRoot); } catch (Exception e) { Problem = e.Message; }
            try
            {
                var dev = await StorageFolder.GetFolderFromPathAsync(@"D:\DevelopmentFiles");
                return await dev.CreateFolderAsync("Shelf", CreationCollisionOption.OpenIfExists);
            }
            catch (Exception e) { Problem = e.Message; }
            try
            {
                CreateDirectoryFromAppW(XboxRoot, IntPtr.Zero); // fails harmlessly if it exists
                return await StorageFolder.GetFolderFromPathAsync(XboxRoot);
            }
            catch (Exception e) { Problem = e.Message; }
            return null;
        }

        static async Task<bool> CanWriteAsync(StorageFolder f)
        {
            try
            {
                var probe = await f.CreateFileAsync(".write-test", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(probe, DateTime.Now.ToString("o"));
                await probe.DeleteAsync();
                return true;
            }
            catch (Exception e) { Problem = e.Message; return false; }
        }

        /// <summary>
        /// The system folder picker: pick D:\DevelopmentFiles (or a folder inside it). Shelf uses a "Shelf"
        /// folder inside what you pick (or the folder itself if it's already called Shelf) and remembers it.
        /// </summary>
        public static async Task<bool> ChooseAsync()
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder, ViewMode = PickerViewMode.List };
            picker.FileTypeFilter.Add("*");
            var picked = await picker.PickSingleFolderAsync();
            if (picked == null) return false;
            var root = string.Equals(picked.Name, "Shelf", StringComparison.OrdinalIgnoreCase)
                ? picked
                : await picked.CreateFolderAsync("Shelf", CreationCollisionOption.OpenIfExists);
            if (!await CanWriteAsync(root)) return false;
            StorageApplicationPermissions.FutureAccessList.AddOrReplace(Token, root);
            await InitAsync();
            return true;
        }

        public static Task<StorageFolder> FolderAsync(string name) =>
            Root.CreateFolderAsync(name, CreationCollisionOption.OpenIfExists).AsTask();

        public static string Where => Root?.Path ?? "";
    }
}
