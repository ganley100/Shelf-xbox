using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Shelf
{
    /// <summary>Which core plays which files, and the systems' names.</summary>
    public static class Systems
    {
        public sealed class Sys
        {
            public string Short, Name, Core; public uint Color; public bool Cartridge = true; public string[] Ext;
        }

        public static readonly Sys[] All =
        {
            new Sys { Short = "NES", Name = "NES", Core = "fceumm", Color = 0xFF1A1A1A, Ext = new[] { ".nes", ".fds", ".unf", ".unif" } },
            new Sys { Short = "SNES", Name = "Super Nintendo", Core = "snes9x", Color = 0xFF6A54A8, Ext = new[] { ".sfc", ".smc" } },
            new Sys { Short = "GB", Name = "Game Boy", Core = "gambatte", Color = 0xFF8B2252, Ext = new[] { ".gb" } },
            new Sys { Short = "GBC", Name = "Game Boy Color", Core = "gambatte", Color = 0xFF2E7D6B, Ext = new[] { ".gbc" } },
            new Sys { Short = "GBA", Name = "Game Boy Advance", Core = "vbam", Color = 0xFF5B4FCF, Ext = new[] { ".gba" } },
            new Sys { Short = "MD", Name = "Genesis / Mega Drive", Core = "genesis_plus_gx", Color = 0xFF202020, Ext = new[] { ".md", ".gen", ".smd" } },
            new Sys { Short = "SMS", Name = "Master System", Core = "genesis_plus_gx", Color = 0xFFB71C1C, Ext = new[] { ".sms" } },
            new Sys { Short = "GG", Name = "Game Gear", Core = "genesis_plus_gx", Color = 0xFF263238, Ext = new[] { ".gg" } },
            new Sys { Short = "32X", Name = "Sega 32X", Core = "picodrive", Color = 0xFF424242, Ext = new[] { ".32x" } },
            new Sys { Short = "PCE", Name = "PC Engine / TurboGrafx-16", Core = "mednafen_pce_fast", Color = 0xFFE65100, Ext = new[] { ".pce" } },
            new Sys { Short = "NGP", Name = "Neo Geo Pocket", Core = "mednafen_ngp", Color = 0xFF0D47A1, Ext = new[] { ".ngp", ".ngc" } },
            new Sys { Short = "WS", Name = "WonderSwan", Core = "mednafen_wswan", Color = 0xFF37474F, Ext = new[] { ".ws", ".wsc" } },
            new Sys { Short = "2600", Name = "Atari 2600", Core = "stella2014", Color = 0xFF5D4037, Ext = new[] { ".a26" } },
            new Sys { Short = "7800", Name = "Atari 7800", Core = "prosystem", Color = 0xFF4E342E, Ext = new[] { ".a78" } },
            new Sys { Short = "LYNX", Name = "Atari Lynx", Core = "handy", Color = 0xFF3E2723, Ext = new[] { ".lnx" } },
            new Sys { Short = "VB", Name = "Virtual Boy", Core = "mednafen_vb", Color = 0xFFC62828, Ext = new[] { ".vb", ".vboy" } },
            new Sys { Short = "PS1", Name = "PlayStation", Core = "mednafen_psx", Color = 0xFF546E7A, Cartridge = false, Ext = new[] { ".cue", ".chd", ".pbp", ".m3u" } },
            new Sys { Short = "ARCADE", Name = "Arcade", Core = "fbneo", Color = 0xFF311B92, Cartridge = false, Ext = new[] { ".zip" } },
            new Sys { Short = "N64", Name = "Nintendo 64", Core = null, Color = 0xFFC62828, Ext = new[] { ".n64", ".z64", ".v64" } },
        };

        public static readonly string[] Extensions = All.SelectMany(s => s.Ext).Distinct().ToArray();

        /// <summary>The system for a game file; zips only count as arcade games inside Games\Arcade.</summary>
        public static Sys For(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".zip" && path.IndexOf("\\Arcade\\", StringComparison.OrdinalIgnoreCase) < 0) return null;
            if ((ext == ".cue" || ext == ".chd") && Regex.IsMatch(path, @"sega ?cd|mega ?cd", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return new Sys { Short = "SCD", Name = "Sega CD", Core = "genesis_plus_gx", Color = 0xFF202020, Cartridge = false, Ext = new[] { ext } };
            return All.FirstOrDefault(s => s.Ext.Contains(ext));
        }

        static class Regex
        {
            public static bool IsMatch(string s, string p, System.Text.RegularExpressions.RegexOptions o) => System.Text.RegularExpressions.Regex.IsMatch(s, p, o);
        }
    }

    /// <summary>
    /// A libretro core (an emulator DLL packed with Shelf in Cores\) and the calls between it and Shelf:
    /// it sends pictures and sound, and asks for the controller's buttons and a few settings.
    /// </summary>
    public sealed unsafe class LibretroCore : IDisposable
    {
        // ---- native glue ----
        [DllImport("api-ms-win-core-libraryloader-l2-1-0.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadPackagedLibrary(string name, uint reserved);
        [DllImport("api-ms-win-core-libraryloader-l1-2-0.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] public delegate bool EnvironmentFn(uint cmd, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void VideoFn(IntPtr data, uint width, uint height, UIntPtr pitch);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void AudioSampleFn(short left, short right);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate UIntPtr AudioBatchFn(IntPtr data, UIntPtr frames);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void InputPollFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate short InputStateFn(uint port, uint device, uint index, uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void VoidFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetPtrFn(IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetPortFn(uint port, uint device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] delegate bool LoadGameFn(IntPtr game);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate UIntPtr SizeFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] delegate bool SerializeFn(IntPtr data, UIntPtr size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr MemDataFn(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate UIntPtr MemSizeFn(uint id);

        [StructLayout(LayoutKind.Sequential)]
        struct SystemInfo { public IntPtr Name, Version, Extensions; [MarshalAs(UnmanagedType.I1)] public bool NeedFullpath; [MarshalAs(UnmanagedType.I1)] public bool BlockExtract; }
        [StructLayout(LayoutKind.Sequential)]
        struct GameInfo { public IntPtr Path, Data; public UIntPtr Size; public IntPtr Meta; }
        [StructLayout(LayoutKind.Sequential)]
        public struct AvInfo { public uint BaseWidth, BaseHeight, MaxWidth, MaxHeight; public float Aspect; public double Fps, SampleRate; }
        [StructLayout(LayoutKind.Sequential)]
        struct Variable { public IntPtr Key, Value; }

        static readonly Dictionary<string, IntPtr> Loaded = new Dictionary<string, IntPtr>();

        IntPtr module;
        VoidFn init, deinit, run, reset, unloadGame;
        SetPtrFn setEnv, setVideo, setAudio, setAudioBatch, setInputPoll, setInputState, getSystemInfo, getAvInfo;
        SetPortFn setPortDevice;
        LoadGameFn loadGame;
        SizeFn serializeSize;
        SerializeFn serialize, unserialize;
        MemDataFn memData;
        MemSizeFn memSize;

        // Kept alive while the core may call them.
        EnvironmentFn envCb; VideoFn videoCb; AudioSampleFn audioCb; AudioBatchFn audioBatchCb; InputPollFn pollCb; InputStateFn stateCb;
        readonly List<IntPtr> strings = new List<IntPtr>();
        readonly Dictionary<string, IntPtr> variables = new Dictionary<string, IntPtr>();
        IntPtr systemDir, saveDir;
        readonly short[] one = new short[2];
        bool inited, loaded;

        public string Name { get; private set; } = "";
        public bool NeedFullpath { get; private set; }
        public AvInfo Av;
        public int PixelFormat; // 0 = 0RGB1555, 1 = XRGB8888, 2 = RGB565
        public bool AvChanged;

        public Action<IntPtr, int, int, int> OnVideo;      // data, width, height, pitch
        public Action<IntPtr, int> OnAudio;                // interleaved int16 stereo, frames
        public Action OnPoll;
        public Func<uint, uint, uint, short> OnInput;      // port, device, id

        T Fn<T>(string name) where T : class
        {
            var p = GetProcAddress(module, name);
            if (p == IntPtr.Zero) throw new InvalidOperationException("The emulator is missing " + name + ".");
            return Marshal.GetDelegateForFunctionPointer<T>(p);
        }

        public LibretroCore(string core, string systemDirectory, string saveDirectory)
        {
            var file = "Cores\\" + core + "_libretro.dll";
            if (!Loaded.TryGetValue(file, out module))
            {
                module = LoadPackagedLibrary(file, 0);
                if (module == IntPtr.Zero) throw new InvalidOperationException($"The {core} emulator isn't in this build of Shelf yet (error {Marshal.GetLastWin32Error()}).");
                Loaded[file] = module;
            }
            init = Fn<VoidFn>("retro_init"); deinit = Fn<VoidFn>("retro_deinit"); run = Fn<VoidFn>("retro_run");
            reset = Fn<VoidFn>("retro_reset"); unloadGame = Fn<VoidFn>("retro_unload_game");
            setEnv = Fn<SetPtrFn>("retro_set_environment"); setVideo = Fn<SetPtrFn>("retro_set_video_refresh");
            setAudio = Fn<SetPtrFn>("retro_set_audio_sample"); setAudioBatch = Fn<SetPtrFn>("retro_set_audio_sample_batch");
            setInputPoll = Fn<SetPtrFn>("retro_set_input_poll"); setInputState = Fn<SetPtrFn>("retro_set_input_state");
            getSystemInfo = Fn<SetPtrFn>("retro_get_system_info"); getAvInfo = Fn<SetPtrFn>("retro_get_system_av_info");
            setPortDevice = Fn<SetPortFn>("retro_set_controller_port_device"); loadGame = Fn<LoadGameFn>("retro_load_game");
            serializeSize = Fn<SizeFn>("retro_serialize_size"); serialize = Fn<SerializeFn>("retro_serialize"); unserialize = Fn<SerializeFn>("retro_unserialize");
            memData = Fn<MemDataFn>("retro_get_memory_data"); memSize = Fn<MemSizeFn>("retro_get_memory_size");

            systemDir = Str(systemDirectory); saveDir = Str(saveDirectory);
            envCb = Environment; videoCb = (d, w, h, p) => { if (d != IntPtr.Zero) OnVideo?.Invoke(d, (int)w, (int)h, (int)(ulong)p); };
            audioCb = (l, r) => { one[0] = l; one[1] = r; fixed (short* s = one) OnAudio?.Invoke((IntPtr)s, 1); };
            audioBatchCb = (d, f) => { OnAudio?.Invoke(d, (int)(ulong)f); return f; };
            pollCb = () => OnPoll?.Invoke();
            stateCb = (port, device, index, id) => port == 0 ? (OnInput?.Invoke(port, device & 0xff, id) ?? 0) : (short)0;

            setEnv(Marshal.GetFunctionPointerForDelegate(envCb));
            init(); inited = true;
            setVideo(Marshal.GetFunctionPointerForDelegate(videoCb));
            setAudio(Marshal.GetFunctionPointerForDelegate(audioCb));
            setAudioBatch(Marshal.GetFunctionPointerForDelegate(audioBatchCb));
            setInputPoll(Marshal.GetFunctionPointerForDelegate(pollCb));
            setInputState(Marshal.GetFunctionPointerForDelegate(stateCb));

            var info = Marshal.AllocHGlobal(Marshal.SizeOf<SystemInfo>());
            try
            {
                getSystemInfo(info);
                var si = Marshal.PtrToStructure<SystemInfo>(info);
                Name = Marshal.PtrToStringAnsi(si.Name) ?? core;
                NeedFullpath = si.NeedFullpath;
            }
            finally { Marshal.FreeHGlobal(info); }
        }

        IntPtr Str(string s) { var p = Marshal.StringToHGlobalAnsi(s); strings.Add(p); return p; }

        /// <summary>Loads a game: from memory, or from [path] when the core wants a real file.</summary>
        public void Load(string path, byte[] data)
        {
            var gi = new GameInfo { Path = Str(path) };
            GCHandle pin = default;
            if (data != null && !NeedFullpath)
            {
                pin = GCHandle.Alloc(data, GCHandleType.Pinned);
                gi.Data = pin.AddrOfPinnedObject(); gi.Size = (UIntPtr)(ulong)data.Length;
            }
            var p = Marshal.AllocHGlobal(Marshal.SizeOf<GameInfo>());
            try
            {
                Marshal.StructureToPtr(gi, p, false);
                if (!loadGame(p)) throw new InvalidOperationException("The emulator couldn't start this game (a missing BIOS file, or a format it doesn't read).");
            }
            finally
            {
                Marshal.FreeHGlobal(p);
                if (pin.IsAllocated) pin.Free(); // libretro cores copy what they need during load
            }
            loaded = true;
            setPortDevice(0, 1); // a joypad in port 1
            var av = Marshal.AllocHGlobal(Marshal.SizeOf<AvInfo>());
            try { getAvInfo(av); Av = Marshal.PtrToStructure<AvInfo>(av); } finally { Marshal.FreeHGlobal(av); }
            if (Av.Fps <= 0) Av.Fps = 60;
            if (Av.SampleRate <= 0) Av.SampleRate = 44100;
        }

        public void Run() => run();
        public void Reset() => reset();

        public byte[] SaveState()
        {
            var n = (int)(ulong)serializeSize();
            if (n <= 0) return null;
            var buf = new byte[n];
            fixed (byte* b = buf) return serialize((IntPtr)b, (UIntPtr)(ulong)n) ? buf : null;
        }

        public bool LoadState(byte[] state)
        {
            fixed (byte* b = state) return unserialize((IntPtr)b, (UIntPtr)(ulong)state.Length);
        }

        /// <summary>Battery saves (cartridge RAM): what's in the game's save slot.</summary>
        public byte[] ReadSaveRam()
        {
            var p = memData(0); var n = (int)(ulong)memSize(0);
            if (p == IntPtr.Zero || n <= 0) return null;
            var b = new byte[n]; Marshal.Copy(p, b, 0, n); return b;
        }

        public void WriteSaveRam(byte[] data)
        {
            var p = memData(0); var n = (int)(ulong)memSize(0);
            if (p == IntPtr.Zero || n <= 0 || data == null) return;
            Marshal.Copy(data, 0, p, Math.Min(n, data.Length));
        }

        // ---- the core asks Shelf things ----
        bool Environment(uint cmd, IntPtr data)
        {
            try
            {
                switch (cmd & 0xFFFF)
                {
                    case 3: Marshal.WriteByte(data, 1); return true;                       // GET_CAN_DUPE
                    case 2: Marshal.WriteByte(data, 0); return true;                       // GET_OVERSCAN
                    case 9: Marshal.WriteIntPtr(data, systemDir); return true;             // GET_SYSTEM_DIRECTORY
                    case 31: Marshal.WriteIntPtr(data, saveDir); return true;              // GET_SAVE_DIRECTORY
                    case 10:                                                               // SET_PIXEL_FORMAT
                        var f = Marshal.ReadInt32(data);
                        if (f < 0 || f > 2) return false;
                        PixelFormat = f; return true;
                    case 16:                                                               // SET_VARIABLES: remember the defaults
                        for (var p = data; ; p += Marshal.SizeOf<Variable>())
                        {
                            var v = Marshal.PtrToStructure<Variable>(p);
                            if (v.Key == IntPtr.Zero) break;
                            var key = Marshal.PtrToStringAnsi(v.Key); var desc = Marshal.PtrToStringAnsi(v.Value) ?? "";
                            var semi = desc.IndexOf("; ", StringComparison.Ordinal);
                            var def = (semi >= 0 ? desc.Substring(semi + 2) : desc).Split('|')[0];
                            if (!variables.ContainsKey(key)) variables[key] = Str(Override(key, def));
                        }
                        return true;
                    case 15:                                                               // GET_VARIABLE
                    {
                        var v = Marshal.PtrToStructure<Variable>(data);
                        var key = Marshal.PtrToStringAnsi(v.Key) ?? "";
                        if (!variables.TryGetValue(key, out var val)) return false;
                        Marshal.WriteIntPtr(data, IntPtr.Size, val);
                        return true;
                    }
                    case 17: Marshal.WriteByte(data, 0); return true;                      // GET_VARIABLE_UPDATE
                    case 52: Marshal.WriteInt32(data, 0); return true;                     // GET_CORE_OPTIONS_VERSION: use SET_VARIABLES
                    case 39: Marshal.WriteInt32(data, 0); return true;                     // GET_LANGUAGE: English
                    case 47: if (data != IntPtr.Zero) Marshal.WriteInt32(data, 3); return true; // GET_AUDIO_VIDEO_ENABLE
                    case 24: Marshal.WriteInt64(data, (1L << 1) | (1L << 5)); return true; // GET_INPUT_DEVICE_CAPABILITIES
                    case 32:                                                               // SET_SYSTEM_AV_INFO
                        Av = Marshal.PtrToStructure<AvInfo>(data); AvChanged = true; return true;
                    case 37:                                                               // SET_GEOMETRY
                        Av.BaseWidth = (uint)Marshal.ReadInt32(data); Av.BaseHeight = (uint)Marshal.ReadInt32(data, 4);
                        var a = BitConverter.ToSingle(BitConverter.GetBytes(Marshal.ReadInt32(data, 16)), 0);
                        if (a > 0) Av.Aspect = a;
                        AvChanged = true; return true;
                    case 1: case 6: case 8: case 11: case 18: case 34: case 35: case 36: case 42: case 44: case 55: case 65:
                        return true;                                                       // noted, nothing to do
                    default:
                        return false;                                                      // not offered (logging, OpenGL, rumble…)
                }
            }
            catch { return false; }
        }

        /// <summary>Settings Shelf prefers over a core's defaults.</summary>
        static string Override(string key, string def)
        {
            switch (key)
            {
                case "beetle_psx_renderer": return "software";
                case "beetle_psx_cpu_dynarec": return "disabled"; // no code generation inside an Xbox app
                case "gambatte_gb_colorization": return "auto";
                default: return def;
            }
        }

        public void Dispose()
        {
            try { if (loaded) unloadGame(); } catch { }
            try { if (inited) deinit(); } catch { }
            loaded = inited = false;
            foreach (var p in strings) Marshal.FreeHGlobal(p);
            strings.Clear();
            GC.KeepAlive(envCb); GC.KeepAlive(videoCb); GC.KeepAlive(audioCb); GC.KeepAlive(audioBatchCb); GC.KeepAlive(pollCb); GC.KeepAlive(stateCb);
        }
    }
}
