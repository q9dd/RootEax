namespace EAX;

using System.Runtime.InteropServices;
using NAudio.Wave;

/// <summary>The sound engine (the original engine rooms, as native code) - the calls into eaxcore.dll.</summary>
static class Native
{
    [DllImport("eaxcore")] public static extern IntPtr eax_create();
    [DllImport("eaxcore")] public static extern void eax_destroy(IntPtr h);
    [DllImport("eaxcore")] public static extern void eax_set(IntPtr h, int preset, int amount, int enabled);
    [DllImport("eaxcore")] public static extern void eax_set_voice(IntPtr h, int micVol, int ampOn, int ampDb, int boostOn, int boostDb, int maskOn, int maskIdx, int pitchHz, int sizePct, int echoOrig);
    [DllImport("eaxcore")] public static extern void eax_process_song(IntPtr h, float[] mono, float[] song, float songGain, float[] outL, float[] outR, int n);
    [DllImport("eaxcore")] public static extern int eax_mask_count();
    [DllImport("eaxcore")] public static extern int eax_mask_at(int pos);
    [DllImport("eaxcore")] public static extern IntPtr eax_mask_name(int i);
    [DllImport("eaxcore")] public static extern int eax_mask_size(int i);
    [DllImport("eaxcore")] public static extern void eax_process(IntPtr h, [In] float[] mono, [Out] float[] left, [Out] float[] right, int n);
}

/// <summary>
/// Microphone -> room (same DSP as the original engine) -> your headphones ("Hear myself") and/or the VB-Cable
/// input (so Discord / games can use "CABLE Output" as their mic).
/// </summary>
static class Engine
{
    const int FS = 48000;                 // the room engine runs at 48 kHz

    static IntPtr h;
    static bool ready;
    static readonly object gate = new();

    static WaveInEvent mic;
    static WaveOutEvent ear, cab;
    static BufferedWaveProvider earBuf, cabBuf;
    static float[] mono = new float[1024], left = new float[1024], right = new float[1024];
    static byte[] pcm = new byte[4096], pcmEar = new byte[4096];
    static float[] songBuf = new float[2048];

    static int micDev = -1, outDev = -1, cableDev = -1;      // -1 = Windows default
    static string[] micNames = { "Default" }, outNames = { "Default" };

    public static bool Hear { get; private set; }
    public static bool Cable { get; private set; }
    public static bool CableFound => cableDev >= 0;
    public static string Error { get; private set; } = "";
    // ---- mic volume / amplifier / bass boost / voice mask (all go into the sound engine) ----
    public const int MaskNeutral = 13;
    public static int MicVol = 100;                       // %, 0..200
    public static bool AmpOn = true; public static int AmpDb = 10;       // 0..26 dB
    public static bool BoostOn; public static int BoostDb;               // 0..12 dB
    public static bool EchoOrig = true;                  // with a mask on, the echo comes from the normal voice
    public static bool MaskOn; public static int MaskIdx = MaskNeutral, Pitch, Size = 100;

    public static void PushVoice()
    {
        if (ready) Native.eax_set_voice(h, MicVol, AmpOn ? 1 : 0, AmpDb, BoostOn ? 1 : 0, BoostDb, MaskOn ? 1 : 0, MaskIdx, Pitch, Size, EchoOrig ? 1 : 0);
    }

    public static void ResetVoice()
    {
        MicVol = 100; AmpOn = true; AmpDb = 10; BoostOn = false; BoostDb = 0;
        MaskOn = false; MaskIdx = MaskNeutral; Pitch = 0; Size = 100; EchoOrig = true;
        PushVoice();
    }

    /// <summary>Names of the voice masks in list order (X-Fi masks first, then the others).</summary>
    static string[] maskCache;
    public static string[] MaskNames()
    {
        if (!ready) return new[] { "Neutral" };
        if (maskCache != null) return maskCache;
        int n = Native.eax_mask_count();
        var r = new string[n];
        for (int i = 0; i < n; i++)
        {
            int idx = Native.eax_mask_at(i);
            r[i] = idx == MaskNeutral ? "Neutral (my own voice)" : System.Runtime.InteropServices.Marshal.PtrToStringAnsi(Native.eax_mask_name(idx)) ?? "";
        }
        return maskCache = r;
    }
    public static int MaskIndexAt(int pos) => ready ? Native.eax_mask_at(pos) : MaskNeutral;
    public static int MaskPosOf(int idx) { int n = ready ? Native.eax_mask_count() : 0; for (int i = 0; i < n; i++) if (Native.eax_mask_at(i) == idx) return i; return 0; }

    /// <summary>Picks a mask: pitch / size go to that mask's own values and the mask switches on.</summary>
    public static void PickMask(int pos)
    {
        int idx = MaskIndexAt(pos);
        MaskIdx = idx;
        Pitch = 0;
        Size = ready ? Native.eax_mask_size(idx) : 100;
        MaskOn = true;
        PushVoice();
    }

    // ---- song (YouTube / Shorts link) ----
    public static void PlaySong(string url)
    {
        if (CableFound && !Cable) Cable = true;           // the song goes to Discord through the cable
        Song.Play(url);
        Apply();
    }
    public static void StopSong() { Song.Stop(); Apply(); }

    public static string[] MicNames() => micNames;
    public static string[] OutNames() => outNames;
    public static int MicIndex => Math.Clamp(micDev + 1, 0, micNames.Length - 1);
    public static int OutIndex => Math.Clamp(outDev + 1, 0, outNames.Length - 1);

    public static string Status =>
        !ready ? "Sound engine not loaded." :
        Error.Length > 0 ? Error :
        Hear ? "Use headphones, or you will get feedback." : "";
    public static string CableName => "Nader Eax";

    /// <summary>Extracts eaxcore.dll (it is inside the exe) and starts the room engine.</summary>
    public static void Init()
    {
        try
        {
            var asm = typeof(Engine).Assembly;
            using var res = asm.GetManifestResourceStream("eaxcore.dll");
            if (res == null) { Error = "eaxcore.dll is not inside this build."; }
            else
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EAX");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "eaxcore-" + res.Length + ".dll");
                if (!File.Exists(path) || new FileInfo(path).Length != res.Length)
                {
                    using var fs = File.Create(path);
                    res.CopyTo(fs);
                }
                NativeLibrary.SetDllImportResolver(asm, (name, a, p) => name == "eaxcore" ? NativeLibrary.Load(path) : IntPtr.Zero);
                h = Native.eax_create();
                ready = h != IntPtr.Zero;
                PushVoice();
            }
        }
        catch (Exception ex) { Error = ex.Message; ready = false; }
        Song.Ended += () => ThreadPool.QueueUserWorkItem(_ => Apply());     // song finished -> release the devices if nothing else needs them
        RefreshDevices();
    }

    public static void Shutdown()
    {
        Stop();
        if (ready) { ready = false; Native.eax_destroy(h); h = IntPtr.Zero; }
    }

    /// <summary>Room number, Effects Amount (0..100) and the Enable box - may be called at any time, also while playing.</summary>
    public static void SetRoom(int preset, int amount, bool on)
    {
        if (ready) Native.eax_set(h, preset, amount, on ? 1 : 0);
    }

    public static void RefreshDevices()
    {
        try
        {
            var m = new List<string> { "Default" };
            for (int i = 0; i < WaveInEvent.DeviceCount; i++) m.Add(WaveInEvent.GetCapabilities(i).ProductName);
            var o = new List<string> { "Default" };
            int cable = -1;
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var n = WaveOut.GetCapabilities(i).ProductName;
                o.Add(n);
                if (cable < 0 && n.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)) cable = i;
            }
            micNames = m.ToArray();
            outNames = o.ToArray();
            cableDev = cable;
            if (micDev >= m.Count - 1) micDev = -1;
            if (outDev >= o.Count - 1) outDev = -1;
            if (cable < 0 && Cable) { Cable = false; Apply(); }
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    public static void SetHear(bool v) { Hear = v; Apply(); }
    public static void SetCable(bool v) { Cable = v && CableFound; Apply(); }
    public static void SetMic(int index) { micDev = index - 1; Apply(); }
    public static void SetOut(int index) { outDev = index - 1; Apply(); }

    static void Apply()
    {
        lock (gate)
        {
            Stop();
            Start();
        }
    }

    static BufferedWaveProvider NewBuffer()
    {
        var b = new BufferedWaveProvider(new WaveFormat(FS, 16, 2))
        {
            BufferDuration = TimeSpan.FromMilliseconds(500),
            DiscardOnBufferOverflow = true
        };
        b.AddSamples(new byte[FS * 4 * 80 / 1000], 0, FS * 4 * 80 / 1000);     // 80 ms cushion so the output never runs dry (dry = crackle)
        return b;
    }

    static void Start()
    {
        bool wantEar = Hear || Song.Active, wantCab = Cable && CableFound;
        if (!ready || !(wantEar || wantCab)) return;
        try
        {
            Error = "";
            if (wantEar)
            {
                earBuf = NewBuffer();
                ear = new WaveOutEvent { DeviceNumber = outDev, DesiredLatency = 120, NumberOfBuffers = 3 };
                ear.Init(earBuf);
                ear.Play();
            }
            if (wantCab)
            {
                cabBuf = NewBuffer();
                cab = new WaveOutEvent { DeviceNumber = cableDev, DesiredLatency = 120, NumberOfBuffers = 3 };
                cab.Init(cabBuf);
                cab.Play();
            }
            mic = new WaveInEvent { DeviceNumber = micDev, WaveFormat = new WaveFormat(FS, 16, 1), BufferMilliseconds = 20, NumberOfBuffers = 6 };
            mic.DataAvailable += OnMic;
            mic.StartRecording();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Stop();
        }
    }

    static void Stop()
    {
        try
        {
            if (mic != null) { mic.DataAvailable -= OnMic; mic.StopRecording(); mic.Dispose(); }
        }
        catch { }
        mic = null;
        try { ear?.Stop(); ear?.Dispose(); } catch { }
        try { cab?.Stop(); cab?.Dispose(); } catch { }
        ear = null; cab = null; earBuf = null; cabBuf = null;
    }

    static void OnMic(object sender, WaveInEventArgs e)
    {
        int n = e.BytesRecorded / 2;
        if (n <= 0 || !ready) return;
        if (mono.Length < n) { mono = new float[n]; left = new float[n]; right = new float[n]; pcm = new byte[n * 4]; pcmEar = new byte[n * 4]; }
        if (songBuf.Length < n * 2) songBuf = new float[n * 2];
        for (int i = 0; i < n; i++) mono[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;

        // the song goes through the room too (so the echo amount applies to it) and is also added dry below
        int sf = Song.Active ? Song.Read(songBuf, n) : 0;
        float sv = Song.Volume;
        if (sf > 0)
        {
            if (sf < n) Array.Clear(songBuf, sf * 2, (n - sf) * 2);
            Native.eax_process_song(h, mono, songBuf, sv, left, right, n);
        }
        else Native.eax_process(h, mono, left, right, n);
        bool hear = Hear;
        for (int i = 0; i < n; i++)
        {
            float sl = i < sf ? songBuf[2 * i] * sv : 0f, sr = i < sf ? songBuf[2 * i + 1] * sv : 0f;
            short a = ToShort(left[i] + sl), b = ToShort(right[i] + sr);              // cable: voice + song
            pcm[4 * i] = (byte)a; pcm[4 * i + 1] = (byte)(a >> 8);
            pcm[4 * i + 2] = (byte)b; pcm[4 * i + 3] = (byte)(b >> 8);
            short c = ToShort((hear ? left[i] : 0f) + sl), d = ToShort((hear ? right[i] : 0f) + sr);   // headphones: song (+ voice if "Hear myself")
            pcmEar[4 * i] = (byte)c; pcmEar[4 * i + 1] = (byte)(c >> 8);
            pcmEar[4 * i + 2] = (byte)d; pcmEar[4 * i + 3] = (byte)(d >> 8);
        }
        var eb = earBuf; var cb = cabBuf;
        // the mic and the output run on different clocks: if the queue grows too long, drop it so the delay never builds up
        if (eb != null && eb.BufferedDuration.TotalMilliseconds > 300) eb.ClearBuffer();
        if (cb != null && cb.BufferedDuration.TotalMilliseconds > 300) cb.ClearBuffer();
        eb?.AddSamples(pcmEar, 0, n * 4);
        cb?.AddSamples(pcm, 0, n * 4);
    }

    static short ToShort(float v)
    {
        float a = Math.Abs(v);
        if (a > 0.97f) { float y = 0.97f + 0.03f * MathF.Tanh((a - 0.97f) / 0.03f); v = v < 0 ? -y : y; }   // soft clip
        int s = (int)Math.Round(v * 32767.0);
        return (short)(s > 32767 ? 32767 : s < -32768 ? -32768 : s);
    }
}
