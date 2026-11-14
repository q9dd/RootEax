namespace EAX;

using System.Diagnostics;
using System.Net.Http;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

/// <summary>
/// Plays the audio of a YouTube / Shorts link (or any link the helper tool understands).
/// The decoded sound (48 kHz stereo) goes into a ring buffer; the mic callback mixes it into the cable / headphones.
/// </summary>
static class Song
{
    const string ToolUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

    static readonly object lk = new();
    static readonly float[] ring = new float[48000 * 2 * 4];    // 4 s, interleaved stereo
    static int rd, wr, cnt;
    static volatile int gen;
    static volatile bool decodeDone;

    public static volatile bool Active;
    public static volatile string Status = "";
    public static float Volume = 0.7f;
    public static event Action Ended;       // finished by itself or failed
    public static event Action Changed;     // status text changed

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EAX");

    public static void Play(string url)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0) return;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
        int my = ++gen;
        lock (lk) { rd = wr = cnt = 0; }
        decodeDone = false;
        Active = true;
        Status = "Loading...";
        Changed?.Invoke();
        Task.Run(() => Run(url, my));
    }

    public static void Stop()
    {
        gen++;
        Active = false;
        lock (lk) { rd = wr = cnt = 0; }
        Status = "";
        Changed?.Invoke();
    }

    static void Fail(int my, string msg)
    {
        if (my != gen) return;
        Active = false;
        Status = msg;
        Changed?.Invoke();
        Ended?.Invoke();
    }

    static void Run(string url, int my)
    {
        string file = null;
        try
        {
            var exe = EnsureTool();
            file = Fetch(exe, url, my);
            if (my != gen) return;
            if (file == null) { Fail(my, "Could not load this link."); return; }
            Status = "Playing";
            Changed?.Invoke();
            Decode(file, my);
        }
        catch (Exception) { Fail(my, "Could not play this link."); }
        finally
        {
            try { if (file != null && File.Exists(file)) File.Delete(file); } catch { }
        }
    }

    // ---- helper tool (fetched quietly the first time it is needed) ----
    static string EnsureTool()
    {
        Directory.CreateDirectory(Dir);
        var exe = Path.Combine(Dir, "yt-dlp.exe");
        if (File.Exists(exe) && new FileInfo(exe).Length > 1_000_000) return exe;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        var tmp = exe + ".tmp";
        using (var resp = http.GetAsync(ToolUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
        {
            resp.EnsureSuccessStatusCode();
            using var fs = File.Create(tmp);
            resp.Content.CopyToAsync(fs).GetAwaiter().GetResult();
        }
        File.Move(tmp, exe, true);
        return exe;
    }

    static int RunTool(string exe, string args, int my, int timeoutMs)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Dir
        };
        using var p = Process.Start(psi);
        p.OutputDataReceived += (s, e) => { };
        p.ErrorDataReceived += (s, e) => { };
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        var sw = Stopwatch.StartNew();
        while (!p.WaitForExit(200))
        {
            if (my != gen || sw.ElapsedMilliseconds > timeoutMs) { try { p.Kill(true); } catch { } return -1; }
        }
        return p.ExitCode;
    }

    static string Fetch(string exe, string url, int my)
    {
        var dir = Path.Combine(Dir, "song");
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir)) { try { File.Delete(old); } catch { } }
        var name = "s" + DateTime.Now.Ticks;
        string args = $"-f \"ba[ext=m4a]/ba/b\" --no-playlist --no-warnings --no-progress --no-part -o \"{Path.Combine(dir, name)}.%(ext)s\" \"{url}\"";

        for (int attempt = 0; attempt < 2; attempt++)
        {
            RunTool(exe, args, my, 180_000);
            if (my != gen) return null;
            var f = Directory.GetFiles(dir, name + ".*").FirstOrDefault(x => !x.EndsWith(".part") && !x.EndsWith(".ytdl"));
            if (f != null) return f;
            if (attempt == 0) RunTool(exe, "-U", my, 90_000);       // out of date -> refresh once and retry
        }
        return null;
    }

    // ---- decode into the ring ----
    static void Decode(string file, int my)
    {
        using var reader = new MediaFoundationReader(file);
        ISampleProvider sp = reader.ToSampleProvider();
        if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
        if (sp.WaveFormat.SampleRate != 48000) sp = new WdlResamplingSampleProvider(sp, 48000);
        var buf = new float[9600];
        while (my == gen)
        {
            int n = sp.Read(buf, 0, buf.Length);
            if (n <= 0) break;
            n &= ~1;
            int done = 0;
            while (done < n && my == gen)
            {
                lock (lk)
                {
                    int free = ring.Length - cnt;
                    int take = Math.Min(free, n - done) & ~1;
                    for (int i = 0; i < take; i++) { ring[wr] = buf[done + i]; wr = (wr + 1) % ring.Length; }
                    cnt += take;
                    done += take;
                }
                if (done < n) Thread.Sleep(5);
            }
        }
        if (my == gen) decodeDone = true;
    }

    /// <summary>Called from the mic callback: fills dst with up to frames stereo frames, returns how many were available.</summary>
    public static int Read(float[] dst, int frames)
    {
        int got;
        lock (lk)
        {
            got = Math.Min(cnt / 2, frames);
            for (int i = 0; i < got * 2; i++) { dst[i] = ring[rd]; rd = (rd + 1) % ring.Length; }
            cnt -= got * 2;
        }
        if (decodeDone && Active)
        {
            bool empty; lock (lk) empty = cnt == 0;
            if (empty) { Active = false; Status = ""; Changed?.Invoke(); Ended?.Invoke(); }
        }
        return got;
    }
}
