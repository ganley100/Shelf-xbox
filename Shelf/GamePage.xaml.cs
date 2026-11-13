using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Gaming.Input;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace Shelf
{
    [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess { void GetBuffer(out byte* buffer, out uint capacity); }

    /// <summary>
    /// Playing a game with a built-in emulator core. A = B button (bottom), B = A (right), X = Y, Y = X
    /// (Nintendo positions), View = Select, Menu = Start, LB/RB, LT/RT, sticks. View + Menu opens the menu.
    /// Battery saves and save states go to Saves\ next to the library.
    /// </summary>
    public sealed unsafe partial class GamePage : Page
    {
        public static bool Playing { get; private set; }

        Item item;
        Systems.Sys sys;
        LibretroCore core;
        Thread thread;
        volatile bool running, paused;
        readonly object coreLock = new object();

        // video: the emulator thread fills [back], the screen copies it on each display refresh
        byte[] back = new byte[0];
        int frameW, frameH, shownW, shownH;
        volatile bool frameReady;
        readonly object frameLock = new object();
        WriteableBitmap bitmap;
        double aspect = 4.0 / 3.0;

        // audio
        AudioGraph graph;
        AudioFrameInputNode input;
        float[] ring = new float[1];
        int ringRead, ringWrite, ringCount;
        readonly object audioLock = new object();

        // input
        GamepadReading pad;
        bool menuCombo;

        string savePath, statePath;
        byte[] lastSram;

        public GamePage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            item = (Item)e.Parameter;
            sys = Systems.For(item.Path);
            Playing = true;
            MenuTitle.Text = item.Title; MenuSystem.Text = sys?.Name ?? "";
            Message.Text = "Starting " + item.Title + "…";
            CompositionTarget.Rendering += OnRendering;
            SizeChanged += (s, a) => Layout();
            var saves = Storage.Folder("Saves");
            var stem = Path.GetFileNameWithoutExtension(item.Path);
            savePath = saves + "\\" + stem + ".srm";
            statePath = saves + "\\" + stem + ".state";
            if (sys?.Core == null)
            {
                Message.Text = (sys?.Name ?? "This system") + " games can't be played on Xbox yet: its emulators need graphics features Xbox apps don't have.";
                return;
            }
            running = true;
            thread = new Thread(EmulatorThread) { IsBackground = true, Name = "emulator" };
            thread.Start();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Stop();
            CompositionTarget.Rendering -= OnRendering;
            Playing = false;
            base.OnNavigatedFrom(e);
        }

        void Ui(Action a) { var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => a()); }

        // ---------- the emulator thread ----------

        void EmulatorThread()
        {
            try
            {
                var systemDir = PrepareSystemFolder();
                var cacheSaves = Directory.CreateDirectory(Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "saves")).FullName;
                core = new LibretroCore(sys.Core, systemDir, cacheSaves);
                core.OnVideo = OnVideo;
                core.OnAudio = OnAudio;
                core.OnPoll = OnPoll;
                core.OnInput = OnInput;

                string path = item.Path; byte[] data = null;
                if (core.NeedFullpath) path = CopyForCore(item.Path);
                else data = Fs.ReadAllBytes(item.Path);
                core.Load(path, data);

                try { if (Fs.Exists(savePath)) core.WriteSaveRam(Fs.ReadAllBytes(savePath)); } catch { }
                lastSram = core.ReadSaveRam();

                aspect = core.Av.Aspect > 0 ? core.Av.Aspect : (core.Av.BaseHeight > 0 ? (double)core.Av.BaseWidth / core.Av.BaseHeight : 4.0 / 3.0);
                StartAudio((int)Math.Round(core.Av.SampleRate)).Wait();
                Ui(() => { Message.Text = ""; Layout(); });

                var sw = Stopwatch.StartNew();
                double frame = 1000.0 / core.Av.Fps, next = 0;
                long lastSave = 0;
                while (running)
                {
                    if (paused) { Thread.Sleep(30); next = sw.Elapsed.TotalMilliseconds; continue; }
                    lock (coreLock) core.Run();
                    if (core.AvChanged)
                    {
                        core.AvChanged = false;
                        if (core.Av.Aspect > 0) aspect = core.Av.Aspect;
                        Ui(Layout);
                    }
                    // Keep battery saves safe: write them when they change (checked every 10 s).
                    if (sw.ElapsedMilliseconds - lastSave > 10_000) { lastSave = sw.ElapsedMilliseconds; SaveBattery(); }

                    next += frame;
                    var now = sw.Elapsed.TotalMilliseconds;
                    if (next - now > 2) Thread.Sleep((int)(next - now - 1));
                    while (sw.Elapsed.TotalMilliseconds < next) { }
                    if (now - next > 100) next = now; // fell behind (slow game): don't race to catch up
                    // Too much sound queued (the TV's clock runs slightly slower): wait a little.
                    int queued; lock (audioLock) queued = ringCount;
                    if (queued > ring.Length / 2) Thread.Sleep(2);
                }
                SaveBattery();
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException?.Message ?? ex.Message;
                Ui(() => Message.Text = "This game didn't start: " + msg);
            }
        }

        /// <summary>BIOS files (Games\BIOS on the D: drive) copied where the cores look for them.</summary>
        static string PrepareSystemFolder()
        {
            var dir = Directory.CreateDirectory(Path.Combine(ApplicationData.Current.LocalFolder.Path, "system")).FullName;
            try
            {
                var bios = Storage.Folder("Games") + "\\BIOS";
                Fs.CreateDirectory(bios);
                foreach (var f in Fs.Walk(bios))
                {
                    var rel = f.Path.Substring(bios.Length + 1);
                    var dest = Path.Combine(dir, rel);
                    if (File.Exists(dest) && new FileInfo(dest).Length == f.Size) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.WriteAllBytes(dest, Fs.ReadAllBytes(f.Path));
                }
            }
            catch { }
            return dir;
        }

        /// <summary>Cores that open the game file themselves get a copy in the app's cache (with a CD's track files).</summary>
        static string CopyForCore(string path)
        {
            var dir = Directory.CreateDirectory(Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "run")).FullName;
            foreach (var old in Directory.GetFiles(dir)) try { File.Delete(old); } catch { }
            var folder = Path.GetDirectoryName(path);
            var stem = Path.GetFileNameWithoutExtension(path);
            foreach (var f in Fs.List(folder).Where(f => !f.IsDirectory && (f.Path == path || f.Name.StartsWith(stem, StringComparison.OrdinalIgnoreCase))))
                File.WriteAllBytes(Path.Combine(dir, f.Name), Fs.ReadAllBytes(f.Path));
            return Path.Combine(dir, Path.GetFileName(path));
        }

        void SaveBattery()
        {
            try
            {
                byte[] s; lock (coreLock) s = core?.ReadSaveRam();
                if (s == null || (lastSram != null && s.SequenceEqual(lastSram))) return;
                if (s.All(b => b == 0) && !Fs.Exists(savePath)) return; // nothing saved yet
                Fs.WriteAllBytes(savePath, s);
                lastSram = s;
            }
            catch { }
        }

        void Stop()
        {
            running = false;
            try { thread?.Join(3000); } catch { }
            try { graph?.Stop(); graph?.Dispose(); } catch { }
            graph = null;
            try { lock (coreLock) { core?.Dispose(); core = null; } } catch { }
        }

        // ---------- video ----------

        void OnVideo(IntPtr data, int w, int h, int pitch)
        {
            lock (frameLock)
            {
                if (back.Length != w * h * 4) back = new byte[w * h * 4];
                fixed (byte* dst = back)
                {
                    var src = (byte*)data;
                    switch (core.PixelFormat)
                    {
                        case 1: // XRGB8888 → BGRA (same byte order), opaque
                            for (int y = 0; y < h; y++)
                            {
                                var s = (uint*)(src + y * pitch); var d = (uint*)(dst + y * w * 4);
                                for (int x = 0; x < w; x++) d[x] = s[x] | 0xFF000000;
                            }
                            break;
                        case 2: // RGB565
                            for (int y = 0; y < h; y++)
                            {
                                var s = (ushort*)(src + y * pitch); var d = dst + y * w * 4;
                                for (int x = 0; x < w; x++)
                                {
                                    var p = s[x];
                                    int r = (p >> 11) & 31, g = (p >> 5) & 63, b = p & 31;
                                    d[x * 4] = (byte)((b << 3) | (b >> 2)); d[x * 4 + 1] = (byte)((g << 2) | (g >> 4));
                                    d[x * 4 + 2] = (byte)((r << 3) | (r >> 2)); d[x * 4 + 3] = 255;
                                }
                            }
                            break;
                        default: // 0RGB1555
                            for (int y = 0; y < h; y++)
                            {
                                var s = (ushort*)(src + y * pitch); var d = dst + y * w * 4;
                                for (int x = 0; x < w; x++)
                                {
                                    var p = s[x];
                                    int r = (p >> 10) & 31, g = (p >> 5) & 31, b = p & 31;
                                    d[x * 4] = (byte)((b << 3) | (b >> 2)); d[x * 4 + 1] = (byte)((g << 3) | (g >> 2));
                                    d[x * 4 + 2] = (byte)((r << 3) | (r >> 2)); d[x * 4 + 3] = 255;
                                }
                            }
                            break;
                    }
                }
                frameW = w; frameH = h; frameReady = true;
            }
        }

        void OnRendering(object sender, object e)
        {
            if (!frameReady) return;
            lock (frameLock)
            {
                if (bitmap == null || shownW != frameW || shownH != frameH)
                {
                    bitmap = new WriteableBitmap(frameW, frameH);
                    shownW = frameW; shownH = frameH;
                    Screen.Source = bitmap;
                    Layout();
                }
                using (var s = bitmap.PixelBuffer.AsStream()) s.Write(back, 0, frameW * frameH * 4);
                frameReady = false;
            }
            bitmap.Invalidate();
        }

        /// <summary>The picture as large as the TV allows, in the system's shape.</summary>
        void Layout()
        {
            var w = Root.ActualWidth; var h = Root.ActualHeight;
            if (w <= 0 || h <= 0) return;
            var a = aspect > 0 ? aspect : 4.0 / 3.0;
            if (w / h > a) { Screen.Height = h; Screen.Width = h * a; } else { Screen.Width = w; Screen.Height = w / a; }
        }

        // ---------- audio ----------

        async Task StartAudio(int rate)
        {
            ring = new float[Math.Max(8192, rate)]; // ~0.5 s of stereo
            ringRead = ringWrite = ringCount = 0;
            var created = await AudioGraph.CreateAsync(new AudioGraphSettings(AudioRenderCategory.GameMedia));
            if (created.Status != AudioGraphCreationStatus.Success) return;
            graph = created.Graph;
            var output = await graph.CreateDeviceOutputNodeAsync();
            if (output.Status != AudioDeviceNodeCreationStatus.Success) return;
            var props = AudioEncodingProperties.CreatePcm((uint)rate, 2, 32);
            props.Subtype = MediaEncodingSubtypes.Float;
            input = graph.CreateFrameInputNode(props);
            input.AddOutgoingConnection(output.DeviceOutputNode);
            input.QuantumStarted += OnQuantum;
            graph.Start();
        }

        void OnAudio(IntPtr data, int frames)
        {
            var s = (short*)data;
            lock (audioLock)
            {
                for (int i = 0; i < frames * 2; i++)
                {
                    if (ringCount == ring.Length) { ringRead = (ringRead + 1) % ring.Length; ringCount--; } // overflow: drop the oldest
                    ring[ringWrite] = s[i] / 32768f;
                    ringWrite = (ringWrite + 1) % ring.Length; ringCount++;
                }
            }
        }

        void OnQuantum(AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args)
        {
            var samples = args.RequiredSamples;
            if (samples <= 0) return;
            var frame = new AudioFrame((uint)(samples * 2 * sizeof(float)));
            using (var buffer = frame.LockBuffer(AudioBufferAccessMode.Write))
            using (var reference = buffer.CreateReference())
            {
                ((IMemoryBufferByteAccess)reference).GetBuffer(out byte* bytes, out uint capacity);
                var f = (float*)bytes;
                lock (audioLock)
                    for (int i = 0; i < samples * 2; i++)
                    {
                        if (ringCount > 0 && !paused) { f[i] = ring[ringRead]; ringRead = (ringRead + 1) % ring.Length; ringCount--; }
                        else f[i] = 0;
                    }
            }
            sender.AddFrame(frame);
        }

        // ---------- input ----------

        void OnPoll()
        {
            var g = Gamepad.Gamepads.FirstOrDefault();
            pad = g != null ? g.GetCurrentReading() : default(GamepadReading);
            var combo = (pad.Buttons & GamepadButtons.View) != 0 && (pad.Buttons & GamepadButtons.Menu) != 0;
            if (combo && !menuCombo) { paused = true; Ui(ShowMenu); }
            menuCombo = combo;
        }

        short OnInput(uint port, uint device, uint id)
        {
            if (paused) return 0;
            var b = pad.Buttons;
            bool Has(GamepadButtons x) => (b & x) != 0;
            const double dead = 0.5;
            if (device == 1) // joypad
            {
                bool on;
                switch (id)
                {
                    case 0: on = Has(GamepadButtons.A); break;                     // B (bottom)
                    case 1: on = Has(GamepadButtons.X); break;                     // Y (left)
                    case 2: on = Has(GamepadButtons.View) && !Has(GamepadButtons.Menu); break; // Select
                    case 3: on = Has(GamepadButtons.Menu) && !Has(GamepadButtons.View); break; // Start
                    case 4: on = Has(GamepadButtons.DPadUp) || pad.LeftThumbstickY > dead; break;
                    case 5: on = Has(GamepadButtons.DPadDown) || pad.LeftThumbstickY < -dead; break;
                    case 6: on = Has(GamepadButtons.DPadLeft) || pad.LeftThumbstickX < -dead; break;
                    case 7: on = Has(GamepadButtons.DPadRight) || pad.LeftThumbstickX > dead; break;
                    case 8: on = Has(GamepadButtons.B); break;                     // A (right)
                    case 9: on = Has(GamepadButtons.Y); break;                     // X (top)
                    case 10: on = Has(GamepadButtons.LeftShoulder); break;
                    case 11: on = Has(GamepadButtons.RightShoulder); break;
                    case 12: on = pad.LeftTrigger > 0.5; break;
                    case 13: on = pad.RightTrigger > 0.5; break;
                    case 14: on = Has(GamepadButtons.LeftThumbstick); break;
                    case 15: on = Has(GamepadButtons.RightThumbstick); break;
                    default: on = false; break;
                }
                return (short)(on ? 1 : 0);
            }
            if (device == 5) // analog sticks
            {
                return 0; // index/id are folded away by the core wrapper; joypad directions cover the sticks
            }
            return 0;
        }

        // ---------- menu ----------

        void ShowMenu()
        {
            MenuStatus.Text = "";
            Menu.Visibility = Visibility.Visible;
            ResumeButton.Focus(FocusState.Programmatic);
        }

        void Resume_Click(object sender, RoutedEventArgs e)
        {
            Menu.Visibility = Visibility.Collapsed;
            paused = false;
        }

        async void SaveState_Click(object sender, RoutedEventArgs e)
        {
            var ok = await Task.Run(() =>
            {
                byte[] s; lock (coreLock) s = core?.SaveState();
                if (s == null) return false;
                Fs.WriteAllBytes(statePath, s); return true;
            });
            MenuStatus.Text = ok ? "Saved." : "This game can't be saved that way.";
        }

        async void LoadState_Click(object sender, RoutedEventArgs e)
        {
            if (!Fs.Exists(statePath)) { MenuStatus.Text = "No saved state yet."; return; }
            var ok = await Task.Run(() => { var s = Fs.ReadAllBytes(statePath); lock (coreLock) return core != null && core.LoadState(s); });
            if (ok) Resume_Click(sender, e); else MenuStatus.Text = "That saved state didn't load.";
        }

        void Reset_Click(object sender, RoutedEventArgs e)
        {
            lock (coreLock) core?.Reset();
            Resume_Click(sender, e);
        }

        void Quit_Click(object sender, RoutedEventArgs e)
        {
            Stop();
            if (Frame.CanGoBack) Frame.GoBack();
        }

        /// <summary>B goes back in the menu (resume); during play it's a game button.</summary>
        public void OnBack()
        {
            if (Menu.Visibility == Visibility.Visible) Resume_Click(this, null);
            else if (sys?.Core == null || core == null) { if (Frame.CanGoBack) Frame.GoBack(); }
        }
    }
}
