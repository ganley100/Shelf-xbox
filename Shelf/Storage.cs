using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Windows.Storage;

namespace Shelf
{
    /// <summary>
    /// Files by path through the Win32 "FromApp" calls (what emulators use on Xbox): they may write in
    /// D:\DevelopmentFiles in Developer Mode, where the normal StorageFolder calls are refused.
    /// </summary>
    public static class Fs
    {
        const string Dll = "api-ms-win-core-file-fromapp-l1-1-0.dll";

        [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateDirectoryFromAppW(string path, IntPtr sa);
        [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr CreateFile2FromAppW(string path, uint access, uint share, uint disposition, IntPtr extended);
        [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFileFromAppW(string path);
        [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool RemoveDirectoryFromAppW(string path);
        [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool MoveFileFromAppW(string from, string to);
        [DllImport(Dll, CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindFirstFileExFromAppW(string path, int infoLevel, out FindData data, int searchOp, IntPtr filter, int flags);
        [DllImport("api-ms-win-core-file-l1-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool FindNextFileW(IntPtr h, out FindData data);
        [DllImport("api-ms-win-core-file-l1-1-0.dll", SetLastError = true)]
        static extern bool FindClose(IntPtr h);

        // WIN32_FIND_DATAW: FILETIMEs are two DWORDs (4-byte aligned), hence Pack = 4.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
        struct FindData
        {
            public uint Attributes;
            public long Created, Accessed, Written; // FILETIME
            public uint SizeHigh, SizeLow, Reserved0, Reserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AltName;
        }

        const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
        const uint ShareRead = 1, ShareWrite = 2, ShareDelete = 4;
        const uint CreateAlways = 2, OpenExisting = 3, CreateNew = 1;
        const uint DirectoryAttr = 0x10;
        static readonly IntPtr Invalid = new IntPtr(-1);

        public sealed class Entry
        {
            public string Path, Name;
            public bool IsDirectory;
            public long Size;
            public DateTimeOffset Modified, Created;
        }

        public static int LastError => Marshal.GetLastWin32Error();

        public static bool CreateDirectory(string path)
        {
            if (CreateDirectoryFromAppW(path, IntPtr.Zero)) return true;
            return LastError == 183; // already exists
        }

        /// <summary>Creates a folder and any missing parents.</summary>
        public static void CreateDirectories(string path)
        {
            var parts = path.TrimEnd('\\').Split('\\');
            var cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                cur += "\\" + parts[i];
                CreateDirectory(cur);
            }
        }

        public static List<Entry> List(string dir)
        {
            var list = new List<Entry>();
            var h = FindFirstFileExFromAppW(dir.TrimEnd('\\') + "\\*", 1, out var d, 0, IntPtr.Zero, 0);
            if (h == Invalid) return list;
            try
            {
                do
                {
                    if (d.Name == "." || d.Name == "..") continue;
                    list.Add(new Entry
                    {
                        Name = d.Name, Path = dir.TrimEnd('\\') + "\\" + d.Name,
                        IsDirectory = (d.Attributes & DirectoryAttr) != 0,
                        Size = ((long)d.SizeHigh << 32) | d.SizeLow,
                        Modified = DateTimeOffset.FromFileTime(d.Written),
                        Created = DateTimeOffset.FromFileTime(d.Created),
                    });
                } while (FindNextFileW(h, out d));
            }
            finally { FindClose(h); }
            return list;
        }

        /// <summary>Every file under [dir], in subfolders too.</summary>
        public static IEnumerable<Entry> Walk(string dir)
        {
            foreach (var e in List(dir))
            {
                if (e.IsDirectory) { foreach (var x in Walk(e.Path)) yield return x; }
                else yield return e;
            }
        }

        public static Entry Info(string path)
        {
            var h = FindFirstFileExFromAppW(path, 1, out var d, 0, IntPtr.Zero, 0);
            if (h == Invalid) return null;
            FindClose(h);
            return new Entry
            {
                Name = d.Name, Path = path, IsDirectory = (d.Attributes & DirectoryAttr) != 0,
                Size = ((long)d.SizeHigh << 32) | d.SizeLow, Modified = DateTimeOffset.FromFileTime(d.Written), Created = DateTimeOffset.FromFileTime(d.Created),
            };
        }

        public static bool Exists(string path) => Info(path) != null;

        static FileStream Open(string path, uint access, uint share, uint disposition, FileAccess fa)
        {
            var h = CreateFile2FromAppW(path, access, share, disposition, IntPtr.Zero);
            if (h == Invalid) throw new IOException($"Couldn't open {path} (error {LastError}).");
            return new FileStream(new SafeFileHandle(h, true), fa, 64 * 1024);
        }

        public static FileStream OpenRead(string path) => Open(path, GenericRead, ShareRead | ShareWrite | ShareDelete, OpenExisting, FileAccess.Read);
        public static FileStream Create(string path) => Open(path, GenericRead | GenericWrite, ShareRead, CreateAlways, FileAccess.ReadWrite);

        /// <summary>A new file that doesn't replace an existing one: "name (2).ext" if needed.</summary>
        public static string UniquePath(string dir, string name)
        {
            var p = dir + "\\" + name;
            var stem = System.IO.Path.GetFileNameWithoutExtension(name); var ext = System.IO.Path.GetExtension(name);
            for (int i = 2; Exists(p); i++) p = $"{dir}\\{stem} ({i}){ext}";
            return p;
        }

        public static bool Delete(string path) => DeleteFileFromAppW(path);
        public static bool Move(string from, string to) => MoveFileFromAppW(from, to);

        public static byte[] ReadAllBytes(string path)
        {
            using (var s = OpenRead(path)) using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
        }

        public static string ReadAllText(string path) => Encoding.UTF8.GetString(ReadAllBytes(path));

        /// <summary>Writes to a temporary file first, then swaps it in, so a crash never leaves half a file.</summary>
        public static void WriteAllBytes(string path, byte[] bytes)
        {
            var tmp = path + ".part";
            using (var s = Create(tmp)) s.Write(bytes, 0, bytes.Length);
            Delete(path);
            if (!Move(tmp, path)) { using (var s = Create(path)) s.Write(bytes, 0, bytes.Length); Delete(tmp); }
        }

        public static void WriteAllText(string path, string text) => WriteAllBytes(path, Encoding.UTF8.GetBytes(text));

        /// <summary>Whether this folder can really be written (create and delete a test folder).</summary>
        public static string CheckWritable(string dir)
        {
            var probe = dir + "\\.shelf-probe-" + DateTime.Now.Ticks;
            if (!CreateDirectoryFromAppW(probe, IntPtr.Zero)) return "error " + LastError;
            RemoveDirectoryFromAppW(probe);
            return null;
        }
    }

    /// <summary>
    /// Where everything lives: D:\DevelopmentFiles\Shelf on an Xbox in Developer Mode (Device Portal → File
    /// explorer → DevelopmentFiles → Shelf). If it can't be written, the app's own folder.
    /// </summary>
    public static class Storage
    {
        public const string XboxRoot = @"D:\DevelopmentFiles\Shelf";

        public static readonly string[] Folders =
        {
            "Books", "Comics", "Manga", "Audiobooks", "Music", "Podcasts", "Movies", "Photos", "Games", "Recordings", "Saves", "Data",
        };

        public static string Root { get; private set; }
        public static bool OnDevelopmentFiles { get; private set; }
        public static string Problem { get; private set; } = "";
        public static string Where => Root;

        public static Task InitAsync()
        {
            Fs.CreateDirectory(@"D:\DevelopmentFiles\Shelf");
            var why = Fs.CheckWritable(XboxRoot);
            if (why == null)
            {
                Root = XboxRoot; OnDevelopmentFiles = true; Problem = "";
            }
            else
            {
                Problem = why;
                Root = ApplicationData.Current.LocalFolder.Path + "\\Shelf";
                OnDevelopmentFiles = false;
                Fs.CreateDirectory(Root);
            }
            foreach (var f in Folders) Fs.CreateDirectory(Root + "\\" + f);
            return Task.CompletedTask;
        }

        /// <summary>The full path of one of Shelf's folders (made if missing).</summary>
        public static string Folder(string name)
        {
            var p = Root + "\\" + name;
            Fs.CreateDirectory(p);
            return p;
        }

        /// <summary>A subfolder of Data, e.g. Data\livetv.</summary>
        public static string DataFolder(string sub)
        {
            var p = Folder("Data") + "\\" + sub;
            Fs.CreateDirectories(p);
            return p;
        }
    }
}
