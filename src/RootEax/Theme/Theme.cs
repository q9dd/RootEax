using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace EAX;

/// <summary>Where the background lives in one skin image and which pixels are label text.</summary>
sealed class ThemeCfg
{
    public Rectangle Region;                 // only inside this rectangle the background is replaced
    public Point[] Seeds;                    // points sitting on plain background colour (flood-fill starts)
    public Rectangle[] Text = Array.Empty<Rectangle>();   // label text (tinted light/dark)
    public Rectangle[] Shade = Array.Empty<Rectangle>();  // dark groove lines
    public Action<int[], int> Prepare;       // clears things the program redraws itself
    public Rectangle[] Keep = Array.Empty<Rectangle>();   // buttons that must stay exactly as drawn (rounded corners)
    public Rectangle[] Soft = Array.Empty<Rectangle>();   // gradient strips (tabs, bottom bar) that are replaced too
    public Rectangle[] Emboss = Array.Empty<Rectangle>(); // the CREATIVE logo: relief kept on top of the picture
    public Rectangle[] Btn = Array.Empty<Rectangle>();    // button labels / glyphs that follow the chosen colour
    public Rectangle[] Glass = Array.Empty<Rectangle>();  // boxes whose light fill becomes see-through (the picture shows)
}

/// <summary>Skin images (embedded or edited copies in %AppData%\EAX\skin) + the user's background theme.</summary>
static class Theme
{
    public static Bitmap Banner;             // chosen background picture (null = original look)
    public static string BannerPath = "";
    public static int TextMode;              // 0 auto, 1 light, 2 dark
    // chosen colour: text, selection, check marks, tab line and button labels follow it
    public static bool UseAccent;
    public static Color Accent = Color.FromArgb(51, 153, 255);
    public static Color Hi => UseAccent ? Accent : Color.FromArgb(51, 153, 255);
    public static Color HiText => (Hi.R + Hi.G + Hi.B) / 3.0 > 150 ? Color.Black : Color.White;
    // title bar: help "?" + arrow (kept as they are, only their glyphs get the colour)
    public static readonly Rectangle[] HelpKeep = { new Rectangle(372, 5, 66, 26) };
    public static readonly Rectangle[] HelpBtn = { new Rectangle(383, 10, 18, 16), new Rectangle(414, 12, 14, 12) };
    public static readonly Rectangle[] WinGlyphs = { new Rectangle(448, 9, 38, 15) };   // minimize / close
    // picture placement inside the window background: size as a multiple of the background area
    // (1 = exactly the area, always >= 1 so it never leaves a gap) and position 0..1 inside the extra room
    public static bool Square;               // square window (no rounded corners)
    public static bool CustomFit;
    static double fitW = 1, fitH = 1, fitX = .5, fitY = .5;

    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EAX");
    public static readonly string SkinDir = Path.Combine(Dir, "skin");
    static string Cfg => Path.Combine(Dir, "config.ini");

    // ---------- skin images ----------
    public static Bitmap LoadSkin(string name)
    {
        try
        {
            var f = Path.Combine(SkinDir, name);
            if (File.Exists(f)) return FromFile(f);
        }
        catch { /* broken edited file -> use the built-in one */ }
        return LoadBuiltIn(name);
    }

    public static Bitmap LoadBuiltIn(string name)
    {
        using var ms = new MemoryStream(Convert.FromBase64String(Img.B64[name]));
        using var tmp = new Bitmap(ms);
        return new Bitmap(tmp);
    }

    public static Bitmap FromFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var tmp = new Bitmap(fs);
        return new Bitmap(tmp);
    }

    /// <summary>Writes the built-in images so the user can edit them.</summary>
    public static void Export(Bitmap thumb)
    {
        Directory.CreateDirectory(SkinDir);
        foreach (var n in Img.B64.Keys)
        {
            var f = Path.Combine(SkinDir, n);
            if (!File.Exists(f)) File.WriteAllBytes(f, Convert.FromBase64String(Img.B64[n]));
        }
        var t = Path.Combine(SkinDir, "thumb.png");
        if (!File.Exists(t) && thumb != null) thumb.Save(t, ImageFormat.Png);
    }

    // ---------- saved settings ----------
    public static void Load()
    {
        try
        {
            if (!File.Exists(Cfg)) return;
            foreach (var line in File.ReadAllLines(Cfg))
            {
                int i = line.IndexOf('=');
                if (i < 0) continue;
                string k = line[..i].Trim(), v = line[(i + 1)..].Trim();
                if (k == "image") SetImage(v, false);
                else if (k == "text" && int.TryParse(v, out var m)) TextMode = Math.Clamp(m, 0, 2);
                else if (k == "accent" && int.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                { Accent = Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); UseAccent = true; }
                else if (k == "icon_eax") LoadIcon(0, v);
                else if (k == "icon_speaker") LoadIcon(1, v);
                else if (k == "icon_eq") LoadIcon(2, v);
                else if (k == "custom") CustomFit = v == "1";
                else if (k == "square") Square = v == "1";
                else if (k == "fitw") fitW = D(v, 1);
                else if (k == "fith") fitH = D(v, 1);
                else if (k == "fitx") fitX = D(v, .5);
                else if (k == "fity") fitY = D(v, .5);
            }
        }
        catch { }
    }

    static double D(string v, double def) =>
        double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;
    static string S(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllLines(Cfg, new[]
            {
                "image=" + BannerPath, "text=" + TextMode, "square=" + (Square ? "1" : "0"), "custom=" + (CustomFit ? "1" : "0"),
                "accent=" + (UseAccent ? (Accent.ToArgb() & 0xFFFFFF).ToString("X6") : ""),
                "icon_eax=" + iconPaths[0], "icon_speaker=" + iconPaths[1], "icon_eq=" + iconPaths[2],
                "fitw=" + S(fitW), "fith=" + S(fitH), "fitx=" + S(fitX), "fity=" + S(fitY)
            });
        }
        catch { }
    }

    /// <summary>Current placement (the "keep proportions" one is worked out from the picture).</summary>
    public static void GetFit(out double w, out double h, out double x, out double y)
    {
        if (CustomFit || Banner == null) { w = fitW; h = fitH; x = fitX; y = fitY; return; }
        const double rw = 496, rh = 344;
        double sc = Math.Max(rw / Banner.Width, rh / Banner.Height);
        w = Math.Clamp(Banner.Width * sc / rw, 1, 3); h = Math.Clamp(Banner.Height * sc / rh, 1, 3); x = .5; y = .5;
    }

    public static void SetFit(double w, double h, double x, double y, bool save = true)
    {
        CustomFit = true;
        fitW = Math.Clamp(w, 1, 3); fitH = Math.Clamp(h, 1, 3);
        fitX = Math.Clamp(x, 0, 1); fitY = Math.Clamp(y, 0, 1);
        if (save) Save();
    }

    public static void KeepProportions() { CustomFit = false; fitX = fitY = .5; Save(); }

    public static void SetImage(string path, bool save = true)
    {
        Banner?.Dispose(); Banner = null; BannerPath = "";
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try { Banner = FromFile(path); BannerPath = path; } catch { }
        }
        if (save) Save();
    }

    public static void SetSquare(bool on) { Square = on; Save(); }

    public static void SetTextMode(int m) { TextMode = m; UseAccent = false; Save(); }

    /// <summary>Back to the original look: no picture, no colour, original icons.</summary>
    public static void ResetAll()
    {
        Banner?.Dispose(); Banner = null; BannerPath = "";
        TextMode = 0; UseAccent = false; Square = false; CustomFit = false; fitW = fitH = 1; fitX = fitY = .5;
        for (int i = 0; i < 3; i++)
        {
            LoadIcon(i, "");
            try { File.Delete(Path.Combine(Dir, "icon_" + IconKeys[i] + ".png")); } catch { }
        }
        Save();
    }

    public static void SetAccent(Color c) { Accent = Color.FromArgb(255, c.R, c.G, c.B); UseAccent = true; Save(); }

    // ---------- bottom-bar icons (EAX / speaker / equalizer) ----------
    static readonly string[] IconKeys = { "eax", "speaker", "eq" };
    static readonly Bitmap[] icons = new Bitmap[3];
    static readonly string[] iconPaths = { "", "", "" };

    public static Bitmap GetIcon(string key)
    {
        int i = Array.IndexOf(IconKeys, key);
        return i < 0 ? null : icons[i];
    }

    static void LoadIcon(int i, string path)
    {
        icons[i]?.Dispose(); icons[i] = null; iconPaths[i] = "";
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try { icons[i] = FromFile(path); iconPaths[i] = path; } catch { }
    }

    /// <summary>Uses any picture as one of the bottom-bar icons (a copy is kept in %AppData%\EAX).</summary>
    public static void SetIcon(string key, string path)
    {
        int i = Array.IndexOf(IconKeys, key);
        if (i < 0) return;
        try
        {
            using var b = FromFile(path);
            Directory.CreateDirectory(Dir);
            var dst = Path.Combine(Dir, "icon_" + key + ".png");
            b.Save(dst, ImageFormat.Png);
            LoadIcon(i, dst);
            Save();
        }
        catch { }
    }

    public static void ResetIcons()
    {
        for (int i = 0; i < 3; i++)
        {
            LoadIcon(i, "");
            try { File.Delete(Path.Combine(Dir, "icon_" + IconKeys[i] + ".png")); } catch { }
        }
        Save();
    }

    // ---------- pixels ----------
    static int[] Read(Bitmap b)
    {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var a = new int[b.Width * b.Height];
        Marshal.Copy(d.Scan0, a, 0, a.Length);
        b.UnlockBits(d);
        return a;
    }

    static Bitmap Write(int[] a, int w, int h)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(a, 0, d.Scan0, a.Length);
        b.UnlockBits(d);
        return b;
    }

    static int R(int p) => (p >> 16) & 255;
    static int G(int p) => (p >> 8) & 255;
    static int B(int p) => p & 255;
    static double Lum(int p) => (R(p) + G(p) + B(p)) / 3.0;
    static int Rgb(double r, double g, double b) =>
        unchecked((int)0xFF000000) | (Math.Clamp((int)Math.Round(r), 0, 255) << 16) | (Math.Clamp((int)Math.Round(g), 0, 255) << 8) | Math.Clamp((int)Math.Round(b), 0, 255);

    static double LumC(Color c) => (c.R + c.G + c.B) / 3.0;

    /// <summary>Square window: the black rounded-corner pixels get the colour of the edge next to them.</summary>
    static void FillCorners(int[] px, int w, int h)
    {
        static bool Black(int p) => (p & 0xFFFFFF) == 0;
        for (int y = 0; y < h; y++)
        {
            int l = 0; while (l < w && Black(px[y * w + l])) l++;
            if (l >= w) continue;
            int r = 0; while (r < w - l && Black(px[y * w + w - 1 - r])) r++;
            for (int x = 0; x < l; x++) px[y * w + x] = px[y * w + l];
            for (int x = 0; x < r; x++) px[y * w + w - 1 - x] = px[y * w + w - 1 - r];
        }
    }

    /// <summary>
    /// Builds the window image. Without a picture: the original skin (minus what the program redraws).
    /// With a picture: every plain background pixel of the WHOLE window (title bar, tabs, body and the
    /// bottom bar) is replaced by the picture. Plain pixels are found by flood fill from the seeds and
    /// then grown over soft edges / gradients, so the insides of boxes and buttons stay as they are.
    /// With a chosen colour the labels (and button labels) get that colour.
    /// </summary>
    public static void Compose(Bitmap src, ThemeCfg c, out Bitmap bg, out Bitmap plate, out bool light)
    {
        int w = src.Width, h = src.Height;
        var px = Read(src);
        c?.Prepare?.Invoke(px, w);
        if (Square) FillCorners(px, w, h);
        plate = null; light = false;
        bool pic = Banner != null;
        if ((!pic && !UseAccent) || c == null) { bg = Write(px, w, h); return; }

        var reg = Rectangle.Intersect(c.Region, new Rectangle(0, 0, w, h));
        var all = new Rectangle(0, 0, w, h);

        // 1) flood fill the plain background colours
        var isBg = new bool[w * h];
        var stack = new Stack<int>();
        foreach (var s in c.Seeds)
        {
            if (!reg.Contains(s)) continue;
            int si = s.Y * w + s.X;
            if (isBg[si]) continue;
            int col = px[si];
            isBg[si] = true; stack.Push(si);
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % w, y = i / w;
                void Try(int nx, int ny)
                {
                    if (!reg.Contains(nx, ny)) return;
                    int j = ny * w + nx;
                    if (!isBg[j] && px[j] == col) { isBg[j] = true; stack.Push(j); }
                }
                Try(x + 1, y); Try(x - 1, y); Try(x, y + 1); Try(x, y - 1);
            }
        }

        // 1b) grow over soft edges / gradients (light + grey only; dark borders stop it)
        var keep = new bool[w * h];
        // Keep rectangles (buttons, icons, logo) stay exactly as drawn, but the plain light pixels around the
        // shape that touch the rectangle's edge become background (no white box, no rounded-corner leftovers)
        foreach (var r0 in c.Keep)
        {
            var r = Rectangle.Intersect(r0, all);
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++) keep[y * w + x] = true;
            var st2 = new Stack<int>();
            var vis = new bool[w * h];
            void Seed(int sx, int sy)
            {
                if (!r.Contains(sx, sy)) return;
                int j = sy * w + sx, p = px[j];
                if (vis[j]) return;
                bool plain = Lum(p) >= 225 && Math.Max(R(p), Math.Max(G(p), B(p))) - Math.Min(R(p), Math.Min(G(p), B(p))) <= 18;
                if (!isBg[j] && !plain) return;
                vis[j] = true; isBg[j] = true; keep[j] = false; st2.Push(j);
            }
            for (int x = r.Left; x < r.Right; x++) { Seed(x, r.Top); Seed(x, r.Bottom - 1); }
            for (int y = r.Top; y < r.Bottom; y++) { Seed(r.Left, y); Seed(r.Right - 1, y); }
            while (st2.Count > 0)
            {
                int j = st2.Pop(), jx = j % w, jy = j / w;
                Seed(jx + 1, jy); Seed(jx - 1, jy); Seed(jx, jy + 1); Seed(jx, jy - 1);
            }
        }
        var soft = new bool[w * h];
        foreach (var r0 in c.Soft)
        {
            var r = Rectangle.Intersect(r0, all);
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++) soft[y * w + x] = true;
        }
        var q = new Queue<int>();
        for (int i = 0; i < isBg.Length; i++) if (isBg[i]) q.Enqueue(i);
        while (q.Count > 0)
        {
            int i = q.Dequeue(), x = i % w, y = i / w;
            double li = Lum(px[i]);
            void Grow(int nx, int ny)
            {
                if (!reg.Contains(nx, ny)) return;
                int j = ny * w + nx;
                if (isBg[j] || keep[j]) return;
                int p = px[j];
                double l = Lum(p);
                bool sf = soft[j];
                if (l < (sf ? 170 : 205) || Math.Abs(l - li) > (sf ? 90 : 40)) return;
                if (Math.Max(R(p), Math.Max(G(p), B(p))) - Math.Min(R(p), Math.Min(G(p), B(p))) > 18) return;
                isBg[j] = true; q.Enqueue(j);
            }
            Grow(x + 1, y); Grow(x - 1, y); Grow(x, y + 1); Grow(x, y - 1);
        }

        // 2) the picture, scaled to cover the region (same proportions, centred)
        int[] pp;
        Bitmap pl = null;
        if (pic)
        {
            pl = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(pl))
            {
                g.Clear(Color.White);
                double dw, dh, ax = .5, ay = .5;
                if (CustomFit) { dw = reg.Width * fitW; dh = reg.Height * fitH; ax = fitX; ay = fitY; }
                else
                {
                    double sc = Math.Max((double)reg.Width / Banner.Width, (double)reg.Height / Banner.Height);
                    dw = Banner.Width * sc; dh = Banner.Height * sc;
                }
                int bw = Math.Max(reg.Width, (int)Math.Ceiling(dw)), bh = Math.Max(reg.Height, (int)Math.Ceiling(dh));
                g.SetClip(reg);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(Banner, new Rectangle(reg.X - (int)Math.Round((bw - reg.Width) * ax), reg.Y - (int)Math.Round((bh - reg.Height) * ay), bw, bh));
            }
            pp = Read(pl);
        }
        else pp = px;                 // no picture: the background keeps the original pixels

        var kind = new byte[w * h];       // 1 text light/colour, 2 shade, 3 text dark, 4 logo relief
        var alpha = new double[w * h];

        // 2b) the CREATIVE logo: its relief (how much lighter / darker than the row's plain colour) is added
        //     on top of the picture, so it looks exactly as in the original on any picture
        short[] dR = null, dG = null, dB = null;
        if (pic)
        {
            dR = new short[w * h]; dG = new short[w * h]; dB = new short[w * h];
            foreach (var r0 in c.Emboss)
            {
                var r = Rectangle.Intersect(r0, reg);
                if (r.Width <= 0 || r.Height <= 0) continue;
                for (int y = r.Top; y < r.Bottom; y++)
                {
                    var cnt = new Dictionary<int, int>();
                    for (int x = r.Left; x < r.Right; x++) { int v = px[y * w + x]; cnt[v] = cnt.TryGetValue(v, out var k0) ? k0 + 1 : 1; }
                    int mode = 0, best = -1;
                    foreach (var kv in cnt) if (kv.Value > best) { best = kv.Value; mode = kv.Key; }
                    for (int x = r.Left; x < r.Right; x++)
                    {
                        int i = y * w + x, v = px[i];
                        isBg[i] = false; kind[i] = 4;
                        dR[i] = (short)(R(v) - R(mode)); dG[i] = (short)(G(v) - G(mode)); dB[i] = (short)(B(v) - B(mode));
                    }
                }
            }
        }

        // 3) text + groove alpha. Every pixel of a label rectangle is rebuilt: picture + the label's own
        //    shape (how much darker than that row's background it was), so no grey fringe is left behind.
        void Mark(Rectangle r, byte k)
        {
            r.Intersect(reg);
            if (r.Width <= 0 || r.Height <= 0) return;
            double psum = 0; int cnt = 0;
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++) { psum += Lum(pp[y * w + x]); cnt++; }
            if (k == 1)   // each label: chosen colour / white on a dark picture, dark on a light one
                k = (UseAccent || TextMode == 1 || (TextMode == 0 && psum / Math.Max(1, cnt) < 190)) ? (byte)1 : (byte)3;
            var hist = new int[256];
            for (int y = r.Top; y < r.Bottom; y++)
            {
                Array.Clear(hist);
                for (int x = r.Left; x < r.Right; x++) hist[Math.Clamp((int)Math.Round(Lum(px[y * w + x])), 0, 255)]++;
                int lv = 0;
                for (int t = 1; t < 256; t++) if (hist[t] > hist[lv]) lv = t;     // most common value = background of this row
                double level = Math.Max(lv, 120);
                for (int x = r.Left; x < r.Right; x++)
                {
                    int i = y * w + x;
                    if (kind[i] == 4) continue;
                    double l = Lum(px[i]);
                    kind[i] = k; isBg[i] = false;
                    alpha[i] = l >= level - 4 ? 0 : Math.Clamp((level - l) / Math.Max(1.0, level - 61.0), 0, 1);
                }
            }
        }
        foreach (var r in c.Shade) Mark(r, 2);

        // 4) light or dark text, from the picture under the labels
        double ls = 0; int ln = 0;
        foreach (var r0 in c.Text)
        {
            var r = Rectangle.Intersect(r0, reg);
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++) { ls += Lum(pp[y * w + x]); ln++; }
        }
        light = UseAccent || TextMode == 1 || (TextMode == 0 && ln > 0 && ls / ln < 170);

        // 4b) soft edges: a pixel that is a blend of the plain background and the object next to it (anti-aliased
        //     rim of a box / button) keeps only the object's share, the rest becomes picture: no grey halo
        bool[] mt = null; float[] mA = null; int[] mO = null;
        if (pic)
        {
            mt = new bool[w * h]; mA = new float[w * h]; mO = new int[w * h];
            var edge = new bool[w * h];
            bool Bgn(int i) => isBg[i];
            for (int y = reg.Top + 1; y < reg.Bottom - 1; y++)
                for (int x = reg.Left + 1; x < reg.Right - 1; x++)
                {
                    int i = y * w + x;
                    if (isBg[i] || kind[i] != 0) continue;
                    bool nb = false;
                    for (int dy = -1; dy <= 1 && !nb; dy++) for (int dx = -1; dx <= 1; dx++) if ((dx != 0 || dy != 0) && Bgn(i + dy * w + dx)) { nb = true; break; }
                    edge[i] = nb;
                }
            for (int y = reg.Top + 1; y < reg.Bottom - 1; y++)
                for (int x = reg.Left + 1; x < reg.Right - 1; x++)
                {
                    int i = y * w + x;
                    if (!edge[i]) continue;
                    double br = 0, bg2 = 0, bb = 0; int bn = 0;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    { int j = i + dy * w + dx; if (j != i && isBg[j]) { br += R(px[j]); bg2 += G(px[j]); bb += B(px[j]); bn++; } }
                    if (bn == 0) continue;
                    br /= bn; bg2 /= bn; bb /= bn;
                    int o2 = -1; double od = 0;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int j = i + dy * w + dx;
                        if (j == i || isBg[j] || edge[j] || kind[j] != 0) continue;
                        double d = Math.Pow(R(px[j]) - br, 2) + Math.Pow(G(px[j]) - bg2, 2) + Math.Pow(B(px[j]) - bb, 2);
                        if (d > od) { od = d; o2 = px[j]; }
                    }
                    if (o2 < 0 || od < 100) continue;
                    double vr = R(o2) - br, vg = G(o2) - bg2, vb = B(o2) - bb;
                    double er = R(px[i]) - br, eg = G(px[i]) - bg2, eb = B(px[i]) - bb;
                    double t = (er * vr + eg * vg + eb * vb) / od;
                    if (t >= 0.98) continue;
                    t = Math.Max(0, t);
                    double rr = er - t * vr, rg = eg - t * vg, rb = eb - t * vb;
                    if (rr * rr + rg * rg + rb * rb > 200) continue;      // not a blend: a real line / colour of its own
                    mt[i] = true; mA[i] = (float)t; mO[i] = o2;
                }
        }

        // 5) compose
        var o = (int[])px.Clone();
        for (int y = reg.Top; y < reg.Bottom; y++)
            for (int x = reg.Left; x < reg.Right; x++)
            {
                int i = y * w + x, p = pp[i];
                if (isBg[i]) { o[i] = p; continue; }
                if (kind[i] == 0)                               // buttons, borders, pictures: untouched
                {
                    if (mt != null && mt[i])                    // ...except the soft rim next to the background
                    {
                        double ma = mA[i]; int oc = mO[i];
                        o[i] = Rgb(R(oc) * ma + R(p) * (1 - ma), G(oc) * ma + G(p) * (1 - ma), B(oc) * ma + B(p) * (1 - ma));
                    }
                    continue;
                }
                double a = alpha[i];
                double br = R(p), bgc = G(p), bb = B(p);
                if (kind[i] == 4)                                // CREATIVE logo: the picture + the logo's own relief
                {
                    o[i] = Rgb(br + dR[i], bgc + dG[i], bb + dB[i]);
                    continue;
                }
                double ir = 61, ig = 61, ib = 61;                // slider groove
                o[i] = Rgb(br * (1 - a) + ir * a, bgc * (1 - a) + ig * a, bb * (1 - a) + ib * a);
            }

        bg = Write(o, w, h);
        plate = pl;
    }
}
