namespace EAX;

using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

sealed class SettingsForm : SkinForm
{
    // The 15 rooms of the original engine (same names, same order as the sound engine's preset table, alphabetical)
    static readonly string[] Env =
    {
        "Auditorium", "Ball Park", "Bathroom", "Cathedral", "Chapel", "Church", "Concert Hall", "Garage",
        "Haunted Cavern", "Indoor Arena", "Jazz Club", "Living Room", "Opera House", "Small Room", "Theater"
    };
    const int Row = 16;
    static readonly Rectangle ListR = new(178, 190, 168, 104);   // inside the black border
    const int DefSel = 8, DefScroll = 48;                        // Haunted Cavern, drawn at the very same spot as before

    Bitmap thumb;
    public Bitmap ThumbSprite => thumb;
    static readonly ThemeCfg C = new()
    {
        Region = new Rectangle(2, 1, 496, 344),            // the picture covers the whole window
        Seeds = new[] { new Point(150, 12), new Point(100, 48), new Point(100, 100), new Point(100, 200), new Point(420, 250), new Point(250, 300) },
        Text = new[]
        {
            new Rectangle(3, 66, 478, 30), new Rectangle(34, 121, 102, 17), new Rectangle(50, 152, 66, 16),
            new Rectangle(175, 152, 61, 16), new Rectangle(350, 152, 92, 16), new Rectangle(352, 170, 14, 14),
            new Rectangle(462, 170, 30, 14),
            new Rectangle(91, 8, 58, 18),      // title
            new Rectangle(230, 41, 36, 15)     // tab label
        }.Concat(Theme.WinGlyphs).ToArray(),
        Shade = new[] { new Rectangle(367, 172, 91, 16) },
        Keep = new[] { new Rectangle(19, 320, 92, 26), new Rectangle(12, 3, 76, 28), new Rectangle(205, 37, 26, 25), new Rectangle(115, 319, 27, 26), new Rectangle(147, 319, 28, 26) }.Concat(Theme.HelpKeep).ToArray(),   // Default button, EAX logo, tab icon, bottom icons (stay original)
        Soft = new[] { new Rectangle(2, 311, 496, 34) },
        Emboss = new[] { new Rectangle(368, 322, 104, 22) },
        Btn = new[] { new Rectangle(45, 326, 38, 12) }.Concat(Theme.HelpBtn).ToArray(),
        Prepare = Prep
    };
    protected override ThemeCfg Cfg => C;

    /// <summary>Clears the preset name, the number and the slider thumb (the program draws them itself).</summary>
    static void Prep(int[] px, int w)
    {
        int gray = unchecked((int)0xFFDBDADB);
        void Fill(int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) px[y * w + x] = gray;
        }
        Fill(176, 171, 336, 186);
        // check box (drawn by the program in the chosen colour)
        for (int y = 120; y < 139; y++) for (int x = 13; x < 31; x++) px[y * w + x] = unchecked((int)0xFFFFFFFF);
        Fill(434, 153, 466, 169);
        // labels of the skin are wiped: the program draws them itself (smooth, in the chosen colour)
        Wipe(px, w, 4, 69, 480, 29, 3);        // description (2 lines)
        Wipe(px, w, 33, 122, 106, 13, 32);     // Enable Audio Effects
        Wipe(px, w, 50, 153, 70, 14, 48);      // Environment
        Wipe(px, w, 174, 153, 66, 14, 172);    // Active Preset
        Wipe(px, w, 350, 153, 84, 14, 348);    // Effects Amount :
        Wipe(px, w, 354, 172, 11, 12, 352);    // 0
        Wipe(px, w, 461, 172, 20, 12, 486);    // 100
        Wipe(px, w, 90, 9, 62, 17, 88);        // title
        Wipe(px, w, 229, 40, 38, 13, 272);     // tab label
        Wipe(px, w, 449, 10, 38, 14, 446);     // minimize / close
        for (int x = 367; x < 386; x++)
            for (int y = 172; y < 188; y++) px[y * w + x] = px[y * w + 420];
    }

    protected override void SkinApplied()
    {
        thumb?.Dispose();
        thumb = null;
        try
        {
            var user = Path.Combine(Theme.SkinDir, "thumb.png");
            if (File.Exists(user)) thumb = Theme.FromFile(user);
        }
        catch { }
        thumb ??= CutThumb(Skin);
    }

    /// <summary>Slider thumb from the skin image, corners made transparent.</summary>
    static Bitmap CutThumb(Bitmap src)
    {
        var t = new Bitmap(19, 13, PixelFormat.Format32bppArgb);
        for (int y = 0; y < 13; y++)
            for (int x = 0; x < 19; x++) t.SetPixel(x, y, src.GetPixel(367 + x, 173 + y));
        bool Near(Color c) => Math.Abs(c.R - 219) + Math.Abs(c.G - 218) + Math.Abs(c.B - 219) < 30;
        var seen = new bool[19, 13];
        var st = new Stack<Point>();
        void Push(int x, int y)
        {
            if (x < 0 || y < 0 || x >= 19 || y >= 13 || seen[x, y] || !Near(t.GetPixel(x, y))) return;
            seen[x, y] = true; st.Push(new Point(x, y));
        }
        for (int x = 0; x < 19; x++) { Push(x, 0); Push(x, 12); }
        for (int y = 0; y < 13; y++) { Push(0, y); Push(18, y); }
        while (st.Count > 0)
        {
            var p = st.Pop();
            t.SetPixel(p.X, p.Y, Color.Transparent);
            Push(p.X + 1, p.Y); Push(p.X - 1, p.Y); Push(p.X, p.Y + 1); Push(p.X, p.Y - 1);
        }
        return t;
    }
    static readonly SolidBrush Veil = new(Color.FromArgb(150, 235, 235, 235));   // light veil, same as the equalizer
    bool on = true;
    int sel = DefSel, scroll = DefScroll, amount = 0;
    const int VisH = 96;                                          // 6 whole rows are shown, so no row is ever cut in half
    int MaxScroll => Env.Length * Row - VisH;
    int ThumbH => Math.Max(16, ListR.Height * VisH / (Env.Length * Row));
    static int Snap(int v) => (v + Row / 2) / Row * Row;

    public SettingsForm() : base("settings.png", "Settings", new Point(60, 60))
    {
        // enable checkbox
        AddHot(new Rectangle(14, 120, 150, 18), down: _ => { on = !on; Push(); Invalidate(); });

        // environment list + its scrollbar
        AddHot(new Rectangle(178, 190, 157, VisH), down: p =>
        {
            int i = (p.Y - ListR.Y + scroll) / Row;
            if (i >= 0 && i < Env.Length) { sel = i; Push(); Invalidate(); }
        }, when: () => on);
        void bar(Point p)
        {
            int room = ListR.Height - ThumbH;
            scroll = Math.Clamp(Snap(Math.Clamp((p.Y - ListR.Y - ThumbH / 2) * MaxScroll / room, 0, MaxScroll)), 0, MaxScroll);
            Invalidate();
        }
        AddHot(new Rectangle(336, 190, 10, 104), down: bar, move: bar, when: () => on);

        // effects amount slider (0..100)
        void slide(Point p)
        {
            amount = Math.Clamp((int)Math.Round((p.X - 376) * 100.0 / 72), 0, 100);
            Push();
            Invalidate();
        }
        AddHot(new Rectangle(360, 170, 104, 20), down: slide, move: slide, when: () => on);

        AddButton("default", new Rectangle(21, 323, 88, 19), () =>
        {
            on = true; sel = DefSel; scroll = DefScroll; amount = 0; Push(); Invalidate();
        });
        AddButton("speaker", new Rectangle(116, 320, 24, 24), () => Nav.ToSpeaker(this));
        AddButton("eq", new Rectangle(148, 320, 26, 24), () => Nav.ToEqualizer(this));
        Push();
    }

    /// <summary>Sends the chosen room, the Effects Amount slider and the on/off box to the sound engine.</summary>
    void Push() => Engine.SetRoom(sel, amount, on);

    protected override void Wheel(int n)
    {
        if (!on) return;
        scroll = Math.Clamp(scroll - n * Row, 0, MaxScroll);
        Invalidate();
    }

    protected override void Overlay(Graphics g)
    {
        Chrome(g, "Settings", 91);
        DynTxt(g, "Effects", 230, 40);
        DynTxt(g, "Audio Effects are high quality music enhancement effects created by the powerful audio engine in", 6, 70);
        DynTxt(g, "your sound device.", 6, 83);
        DynTxt(g, "Enable Audio Effects", 34, 122);
        DynTxt(g, "Environment", 53, 154);
        DynTxt(g, "Active Preset", 176, 154);
        DynTxt(g, "Effects Amount :", 352, 154);
        DynTxt(g, "0", 355, 172);
        DynTxt(g, "100", 462, 172);
        DrawCheck(g, 15, 122, on);

        // list
        g.SetClip(ListR);
        g.FillRectangle(Brushes.White, ListR);
        g.SetClip(new Rectangle(ListR.X, ListR.Y, ListR.Width, VisH));
        using var stripe = new SolidBrush(Color.FromArgb(245, 245, 245));
        using var blue = new SolidBrush(Theme.Hi);
        for (int i = scroll / Row; i < Env.Length; i++)
        {
            int y = ListR.Y + i * Row - scroll;
            if (y >= ListR.Y + VisH) break;
            var r = new Rectangle(ListR.X, y, 158, Row);
            if (i == sel) g.FillRectangle(blue, r);
            else if (i % 2 == 0) g.FillRectangle(stripe, r);
            Txt(g, Env[i], 183, y + 2, i == sel ? Theme.HiText : Color.Black);
        }
        g.SetClip(ListR);
        // scrollbar
        using var track = new SolidBrush(Color.FromArgb(240, 240, 240));
        g.FillRectangle(track, 336, ListR.Y, 10, ListR.Height);
        int th = ThumbH, ty = ListR.Y + (ListR.Height - th) * scroll / MaxScroll;
        using var tb = new SolidBrush(Color.FromArgb(205, 205, 205));
        using var pen = new Pen(Color.FromArgb(120, 120, 120));
        g.FillRectangle(tb, 337, ty, 8, th);
        g.DrawRectangle(pen, 337, ty, 7, th - 1);
        g.ResetClip();

        // active preset name + amount number (the background is clean, so just draw)
        DynTxt(g, amount.ToString(), 436, 154);

        // slider thumb (sprite with transparent corners)
        int tx = 367 + (int)Math.Round(72 * amount / 100.0);
        if (on) g.DrawImage(thumb, tx, 173, thumb.Width, thumb.Height);
        else DrawFaded(g, thumb, tx, 173, 0.4f);

        // effects off -> only the white list box greys out; nothing is laid over the picture
        if (!on) g.FillRectangle(Veil, 176, 188, 172, 108);
    }
}

sealed class SpeakerForm : SkinForm
{
    static readonly string[] Modes =
        { "2 Speakers", "2.1 Speakers", "4 Speakers", "4.1 Speakers", "5.1 Speakers", "7.1 Speakers", "Headphone" };
    // four tabs: the old third tab area is shared by "Bass Boost" and "Voice Masks"
    static readonly int[] TabX = { 88, 252, 384, 461 };      // label centres
    static readonly int[] TabW = { 100, 92, 58, 70 };

    static readonly ThemeCfg C = new()
    {
        Region = new Rectangle(2, 1, 496, 344),            // the picture covers the whole window
        Seeds = new[] { new Point(150, 12), new Point(20, 100), new Point(400, 300), new Point(100, 66) },
        Text = new[]
        {
            new Rectangle(12, 75, 160, 17), new Rectangle(254, 75, 80, 17),
            new Rectangle(36, 9, 114, 17),                                                     // title
            new Rectangle(44, 41, 90, 15), new Rectangle(220, 41, 92, 15), new Rectangle(398, 41, 56, 15)   // tab labels
        }.Concat(Theme.WinGlyphs).ToArray(),
        Keep = new[] { new Rectangle(19, 320, 92, 26), new Rectangle(5, 2, 32, 31), new Rectangle(115, 319, 27, 26), new Rectangle(147, 319, 28, 26) }.Concat(Theme.HelpKeep).ToArray(),
        Soft = new[] { new Rectangle(2, 35, 496, 28), new Rectangle(2, 311, 496, 34) },
        Emboss = new[] { new Rectangle(368, 322, 104, 22) },
        Btn = new[] { new Rectangle(45, 326, 38, 12) }.Concat(Theme.HelpBtn).ToArray(),
        Prepare = Prep
    };

    /// <summary>Wipes the skin's own labels (the program draws them itself).</summary>
    static void Prep(int[] px, int w)
    {
        Wipe(px, w, 36, 9, 114, 17, 160);       // title
        Wipe(px, w, 44, 41, 90, 14, 140);       // Speaker Selection
        Wipe(px, w, 221, 41, 92, 14, 318);      // Bass Management
        Wipe(px, w, 399, 41, 56, 14, 462);      // Bass Boost
        Wipe(px, w, 13, 76, 148, 15, 163);      // Speaker/Headphone Selection
        Wipe(px, w, 255, 76, 66, 15, 324);      // Speaker Test
        Wipe(px, w, 292, 269, 48, 12, 285);     // button label "Channel" (drawn as "Play")
        Wipe(px, w, 396, 269, 36, 12, 389);     // button label "Noise"   (drawn as "Stop")
        Wipe(px, w, 449, 10, 38, 14, 446);      // minimize / close
    }
    protected override ThemeCfg Cfg => C;

    static readonly Rectangle MicBox = new(14, 185, 206, 19), OutBox = new(14, 221, 206, 19);
    static readonly Rectangle ML = new(22, 104, 190, 192);        // voice mask list
    const int MRow = 16;
    int tab, mode = 6;
    bool bassOn = true;
    int freq = 80;
    int mScroll;
    readonly TextBox url;

    int MaskTotal => Engine.MaskNames().Length;
    int MaxMScroll => Math.Max(0, MaskTotal * MRow - ML.Height);
    int MThumbH => Math.Max(16, ML.Height * ML.Height / Math.Max(1, MaskTotal * MRow));

    void SetTab(int t) { tab = t; url.Visible = t == 0; Invalidate(); }

    public SpeakerForm() : base("speaker.png", "Speaker Settings", new Point(100, 100))
    {
        HideOnClose = true;

        url = new TextBox
        {
            Bounds = new Rectangle(273, 89, 188, 20), Font = new Font("Tahoma", 8f), BorderStyle = BorderStyle.FixedSingle,
            MaxLength = 400, PlaceholderText = "Paste a YouTube / Shorts link"
        };
        url.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; PlayUrl(); } };
        Controls.Add(url);
        Song.Changed += () => { try { if (IsHandleCreated) BeginInvoke(new Action(Invalidate)); } catch { } };

        AddHot(new Rectangle(0, 34, 178, 28), down: _ => SetTab(0));
        AddHot(new Rectangle(178, 34, 168, 28), down: _ => SetTab(1));
        AddHot(new Rectangle(346, 34, 77, 28), down: _ => SetTab(2));
        AddHot(new Rectangle(423, 34, 77, 28), down: _ => SetTab(3));

        // speaker / headphone combo
        var combo = new Rectangle(33, 95, 187, 19);
        AddHot(combo, up: () =>
        {
            if (tab == 0 && Inside(combo)) Drop(combo, Modes, mode, i => { mode = i; Invalidate(); });
        }, when: () => tab == 0);

        // hear myself / send to the cable / pick the devices (left panel, under the speaker combo)
        AddHot(new Rectangle(14, 126, 214, 18), down: _ => { Engine.SetHear(!Engine.Hear); Invalidate(); }, when: () => tab == 0);
        AddHot(new Rectangle(14, 148, 214, 18), down: _ => { if (Engine.CableFound) { Engine.SetCable(!Engine.Cable); Invalidate(); } }, when: () => tab == 0);
        AddHot(MicBox, up: () =>
        {
            if (tab == 0 && Inside(MicBox)) { Engine.RefreshDevices(); Drop(MicBox, Engine.MicNames(), Engine.MicIndex, i => { Engine.SetMic(i); Invalidate(); }); }
        }, when: () => tab == 0);
        AddHot(OutBox, up: () =>
        {
            if (tab == 0 && Inside(OutBox)) { Engine.RefreshDevices(); Drop(OutBox, Engine.OutNames(), Engine.OutIndex, i => { Engine.SetOut(i); Invalidate(); }); }
        }, when: () => tab == 0);

        // mic volume (0..200 %) and song volume (0..100 %)
        void micVol(Point p)
        {
            int v = (int)Math.Round(Math.Clamp((p.X - 80 - 9) / 82.0, 0, 1) * 200);
            if (Math.Abs(v - 100) < 5) v = 100;
            Engine.MicVol = v; Engine.PushVoice(); Invalidate();
        }
        AddHot(new Rectangle(78, 244, 106, 18), down: micVol, move: micVol, when: () => tab == 0);
        void songVol(Point p)
        {
            Song.Volume = (float)Math.Clamp((p.X - 80 - 9) / 82.0, 0, 1);
            Invalidate();
        }
        AddHot(new Rectangle(78, 262, 106, 18), down: songVol, move: songVol, when: () => tab == 0);

        // play a song from a link (to Discord through the cable, and to your headphones)
        AddButton("channel", new Rectangle(270, 263, 94, 22), PlayUrl, () => tab == 0);
        AddButton("noise", new Rectangle(369, 263, 93, 22), () => { Engine.StopSong(); Invalidate(); }, () => tab == 0);

        // bass tabs: checkbox + slider
        AddHot(new Rectangle(20, 88, 210, 18), down: _ =>
        {
            if (tab == 1) bassOn = !bassOn; else { Engine.BoostOn = !Engine.BoostOn; Engine.PushVoice(); }
            Invalidate();
        }, when: () => tab == 1 || tab == 2);
        void slide(Point p)
        {
            double f = Math.Clamp((p.X - 22 - 9) / 162.0, 0, 1);
            if (tab == 1) freq = 50 + (int)Math.Round(f * 150);
            else { Engine.BoostDb = (int)Math.Round(f * 12); Engine.PushVoice(); }
            Invalidate();
        }
        AddHot(new Rectangle(20, 144, 184, 18), down: slide, move: slide, when: () => tab == 1 || tab == 2);

        // amplifier (right panel of the Bass Boost tab)
        AddHot(new Rectangle(264, 88, 210, 18), down: _ => { Engine.AmpOn = !Engine.AmpOn; Engine.PushVoice(); Invalidate(); }, when: () => tab == 2);
        void amp(Point p)
        {
            Engine.AmpDb = (int)Math.Round(Math.Clamp((p.X - 266 - 9) / 162.0, 0, 1) * 26);
            Engine.PushVoice(); Invalidate();
        }
        AddHot(new Rectangle(264, 144, 184, 18), down: amp, move: amp, when: () => tab == 2);

        // voice masks: on/off, list + scrollbar, pitch, size
        AddHot(new Rectangle(20, 82, 200, 18), down: _ => { Engine.MaskOn = !Engine.MaskOn; Engine.PushVoice(); Invalidate(); }, when: () => tab == 3);
        AddHot(new Rectangle(ML.X, ML.Y, ML.Width - 10, ML.Height), down: p =>
        {
            int i = (p.Y - ML.Y + mScroll) / MRow;
            if (i >= 0 && i < MaskTotal) { Engine.PickMask(i); Invalidate(); }
        }, when: () => tab == 3);
        void mbar(Point p)
        {
            int room = ML.Height - MThumbH;
            mScroll = Math.Clamp((p.Y - ML.Y - MThumbH / 2) * MaxMScroll / Math.Max(1, room), 0, MaxMScroll);
            Invalidate();
        }
        AddHot(new Rectangle(ML.Right - 10, ML.Y, 10, ML.Height), down: mbar, move: mbar, when: () => tab == 3);
        void pitch(Point p)
        {
            int v = (int)Math.Round(Math.Clamp((p.X - 266 - 9) / 172.0, 0, 1) * 300) - 150;
            if (v > -6 && v < 6) v = 0;
            Engine.Pitch = v; Engine.MaskOn = true; Engine.PushVoice(); Invalidate();
        }
        AddHot(new Rectangle(264, 148, 196, 20), down: pitch, move: pitch, when: () => tab == 3);
        void size(Point p)
        {
            int v = 60 + (int)Math.Round(Math.Clamp((p.X - 266 - 9) / 172.0, 0, 1) * 90);
            if (v > 96 && v < 104) v = 100;
            Engine.Size = v; Engine.MaskOn = true; Engine.PushVoice(); Invalidate();
        }
        AddHot(new Rectangle(264, 212, 196, 20), down: size, move: size, when: () => tab == 3);
        AddHot(new Rectangle(264, 250, 214, 18), down: _ => { Engine.EchoOrig = !Engine.EchoOrig; Engine.PushVoice(); Invalidate(); }, when: () => tab == 3);

        AddButton("default", new Rectangle(21, 323, 88, 19), () =>
        {
            SetTab(0); mode = 6; bassOn = true; freq = 80; mScroll = 0; Song.Volume = 0.7f; Engine.ResetVoice(); Invalidate();
        });
        AddButton("eax", new Rectangle(116, 320, 24, 24), () => Nav.ToSettings(this));
        AddButton("eq", new Rectangle(148, 320, 26, 24), () => Nav.ToEqualizer(this));
    }

    void PlayUrl()
    {
        if (url.Text.Trim().Length == 0) return;
        Engine.PlaySong(url.Text);
        Invalidate();
    }

    protected override void Wheel(int n)
    {
        if (tab != 3) return;
        mScroll = Math.Clamp(mScroll - n * MRow, 0, MaxMScroll);
        Invalidate();
    }

    /// <summary>White drop-down box (text + small arrow) in the same look as the speaker combo.</summary>
    static void DrawCombo(Graphics g, Rectangle r, string text)
    {
        if (text.Length > 32) text = text.Substring(0, 31) + "...";
        g.FillRectangle(Brushes.White, r);
        using (var pen = new Pen(Color.FromArgb(120, 120, 120))) g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        Txt(g, text, r.X + 4, r.Y + 3, Color.Black);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int ax = r.Right - 14, ay = r.Y + 7;
        using (var pen = new Pen(Color.FromArgb(70, 70, 70), 1.6f))
            g.DrawLines(pen, new[] { new PointF(ax, ay), new PointF(ax + 3.5f, ay + 4), new PointF(ax + 7, ay) });
        g.SmoothingMode = old;
    }

    static void BtnTxt(Graphics g, string s, Rectangle r)
        => TextRenderer.DrawText(g, s, F, r, Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

    static string Db(int v) => (v > 0 ? "+" : "") + v + " dB";

    protected override void Overlay(Graphics g)
    {
        Chrome(g, "Speaker Settings", 36);
        DynTxt(g, "Speaker Selection", 45, 41);
        DynTxt(g, "Bass Management", 222, 41);
        DynTxt(g, "Bass Boost", 358, 41);
        DynTxt(g, "Voice Masks", 430, 41);
        if (tab == 0)
        {
            DynTxt(g, "Speaker/Headphone Selection", 14, 76);
            DynTxt(g, "Play a Song (YouTube / Shorts)", 256, 76);
            g.FillRectangle(Brushes.White, 36, 97, 170, 15);
            Txt(g, Modes[mode], 37, 98, Color.Black);

            DrawCheck(g, 15, 129, Engine.Hear);
            DynTxt(g, "Hear myself", 35, 131);
            DrawCheck(g, 15, 151, Engine.Cable);
            DynTxt(g, Engine.CableFound ? "Send to " + Engine.CableName + " (Discord / games)" : Engine.CableName + " cable not installed", 35, 153);
            DynTxt(g, "Microphone", 14, 172);
            DrawCombo(g, MicBox, Engine.MicNames()[Engine.MicIndex]);
            DynTxt(g, "Output", 14, 208);
            DrawCombo(g, OutBox, Engine.OutNames()[Engine.OutIndex]);

            DynTxt(g, "Mic volume", 14, 248);
            DrawMiniSlider(g, 80, 246, 100, Engine.MicVol / 200f);
            DynTxt(g, Engine.MicVol + "%", 188, 248);
            DynTxt(g, "Song volume", 14, 266);
            DrawMiniSlider(g, 80, 264, 100, Song.Volume);
            DynTxt(g, (int)Math.Round(Song.Volume * 100) + "%", 188, 266);

            string st = Song.Status.Length > 0 ? Song.Status : Engine.Status;
            DynTxt(g, st, 14, 285);

            BtnTxt(g, "Play", new Rectangle(270, 263, 94, 22));
            BtnTxt(g, "Stop", new Rectangle(369, 263, 93, 22));
            return;
        }

        Blank(g, new Rectangle(14, 76, 226, 226));       // blank both panels
        Blank(g, new Rectangle(258, 76, 224, 226));
        using var blue = new SolidBrush(Theme.Hi);
        g.FillRectangle(blue, TabX[tab] - TabW[tab] / 2, 58, TabW[tab], 2);   // active tab marker

        if (tab == 1)
        {
            DrawCheck(g, 22, 90, bassOn);
            DynTxt(g, "Enable Bass Management", 42, 92);
            DynTxt(g, $"Crossover Frequency : {freq} Hz", 22, 124);
            DynTxt(g, "50", 22, 166); DynTxt(g, "200", 182, 166);
            DrawMiniSlider(g, 22, 146, 180, (freq - 50) / 150f);
        }
        else if (tab == 2)
        {
            DrawCheck(g, 22, 90, Engine.BoostOn);
            DynTxt(g, "Enable Bass Boost", 42, 92);
            DynTxt(g, $"Boost Amount : {Engine.BoostDb} dB", 22, 124);
            DynTxt(g, "0", 22, 166); DynTxt(g, "12", 188, 166);
            DrawMiniSlider(g, 22, 146, 180, Engine.BoostDb / 12f);

            DrawCheck(g, 266, 90, Engine.AmpOn);
            DynTxt(g, "Enable Amplifier", 286, 92);
            DynTxt(g, $"Amplifier Gain : {Db(Engine.AmpDb)}", 266, 124);
            DynTxt(g, "0", 266, 166); DynTxt(g, "26", 432, 166);
            DrawMiniSlider(g, 266, 146, 180, Engine.AmpDb / 26f);
            DynTxt(g, "Makes your voice (and the echo) louder.", 266, 190);
        }
        else
        {
            DrawCheck(g, 22, 84, Engine.MaskOn);
            DynTxt(g, "Enable Voice Mask", 42, 86);

            // mask list (whole rows, selected one highlighted)
            var names = Engine.MaskNames();
            int cur = Engine.MaskPosOf(Engine.MaskIdx);
            g.SetClip(ML);
            g.FillRectangle(Brushes.White, ML);
            using var stripe = new SolidBrush(Color.FromArgb(245, 245, 245));
            for (int i = mScroll / MRow; i < names.Length; i++)
            {
                int y = ML.Y + i * MRow - mScroll;
                if (y >= ML.Bottom) break;
                var r = new Rectangle(ML.X, y, ML.Width - 10, MRow);
                if (i == cur) g.FillRectangle(blue, r);
                else if (i % 2 == 0) g.FillRectangle(stripe, r);
                Txt(g, names[i], ML.X + 5, y + 2, i == cur ? Theme.HiText : Color.Black);
            }
            using var track = new SolidBrush(Color.FromArgb(240, 240, 240));
            g.FillRectangle(track, ML.Right - 10, ML.Y, 10, ML.Height);
            int th = MThumbH, ty = ML.Y + (ML.Height - th) * mScroll / Math.Max(1, MaxMScroll);
            using var tb = new SolidBrush(Color.FromArgb(205, 205, 205));
            using var pen = new Pen(Color.FromArgb(120, 120, 120));
            g.FillRectangle(tb, ML.Right - 9, ty, 8, th);
            g.DrawRectangle(pen, ML.Right - 9, ty, 7, th - 1);
            g.ResetClip();
            using (var bp = new Pen(Color.FromArgb(120, 120, 120))) g.DrawRectangle(bp, ML.X - 1, ML.Y - 1, ML.Width + 1, ML.Height + 1);

            // options
            DynTxt(g, "Mask Options", 266, 84, FB);
            DynTxt(g, "Mask : " + names[cur], 266, 108);
            DynTxt(g, $"Pitch : {(Engine.Pitch > 0 ? "+" : "")}{Engine.Pitch} Hz", 266, 132);
            DrawMiniSlider(g, 266, 152, 190, (Engine.Pitch + 150) / 300f);
            DynTxt(g, "-150", 266, 170); DynTxtR(g, "+150", 456, 170);
            DynTxt(g, $"Size : {Engine.Size} %", 266, 196);
            DrawMiniSlider(g, 266, 216, 190, (Engine.Size - 60) / 90f);
            DynTxt(g, "60", 266, 234); DynTxtR(g, "150", 456, 234);
            DrawCheck(g, 266, 252, Engine.EchoOrig);
            DynTxt(g, "Echo uses my normal voice", 286, 254);
            DynTxt(g, Engine.MaskOn ? "Mask is ON" : "Mask is OFF", 266, 276);
        }
    }
}

sealed class EqualizerForm : SkinForm
{
    // left border x of each of the 7 band boxes (measured from the reference image)
    static readonly int[] L = { 61, 108, 155, 201, 247, 293, 339 };
    const int Zero = 179;            // y of the 0 dB thumb centre
    const float PxPerDb = 54.5f / 12f;

    // 125, 250, 500, 1k, 2k, 4k, 8k  (dB)
    static readonly (string Name, int[] V)[] Presets =
    {
        ("<Custom>", null),
        ("Classical", new[] {  0,  0,  0,  0, -3, -3, -5 }),
        ("Club", new[] {  0,  0,  4,  3,  3,  3,  0 }),
        ("Dance", new[] {  6,  4,  1,  0,  0, -4, -5 }),
        ("Full Bass", new[] {  6,  6,  6,  3,  1, -2, -4 }),
        ("Full Bass & Treble", new[] {  5,  4,  0, -4, -2,  3,  6 }),
        ("Full Treble", new[] { -6, -6, -6, -2,  2,  6,  8 }),
        ("Headphones", new[] {  3,  6,  3, -2, -1,  2,  5 }),
        ("Large Hall", new[] {  6,  6,  3,  3,  0, -3, -3 }),
        ("Live", new[] { -3,  0,  2,  3,  3,  2,  1 }),
        ("Party", new[] {  4,  4,  0,  0,  0,  4,  4 }),
        ("Pop", new[] { -1,  3,  4,  3,  0, -2, -2 }),
        ("Reggae", new[] {  0,  0, -1, -4,  0, -4, -6 }),
        ("Rock", new[] {  4,  3, -3, -4, -2,  2,  5 }),
        ("Ska", new[] { -2, -3, -2,  0,  2,  3,  4 }),
        ("Soft", new[] {  3,  1,  0, -1,  0,  2,  5 }),
        ("Soft Rock", new[] {  2,  2,  1,  0, -2, -3, -1 }),
        ("Techno", new[] {  4,  3,  0, -3, -3,  0,  4 }),
    };

    static readonly SolidBrush Veil = new(Color.FromArgb(150, 235, 235, 235));
    static readonly ThemeCfg C = new()
    {
        Region = new Rectangle(2, 1, 496, 308),            // the picture covers the whole window
        Seeds = new[] { new Point(150, 12), new Point(150, 75), new Point(100, 66), new Point(20, 100), new Point(480, 150) },
        Text = new[]
        {
            new Rectangle(24, 68, 84, 17), new Rectangle(218, 68, 44, 17), new Rectangle(28, 118, 32, 13),
            new Rectangle(33, 172, 27, 13), new Rectangle(23, 227, 37, 13), new Rectangle(418, 125, 20, 14),
            new Rectangle(408, 226, 30, 14), new Rectangle(442, 252, 28, 12),
            new Rectangle(45, 8, 113, 19),     // title
            new Rectangle(226, 41, 46, 14)     // tab label
        }.Concat(Theme.WinGlyphs).ToArray(),
        Keep = new[] { new Rectangle(19, 284, 92, 26), new Rectangle(5, 2, 36, 31), new Rectangle(115, 283, 27, 26), new Rectangle(147, 283, 28, 26) }.Concat(Theme.HelpKeep).ToArray(),
        Soft = new[] { new Rectangle(2, 35, 496, 28), new Rectangle(2, 275, 496, 34) },
        Emboss = new[] { new Rectangle(368, 286, 104, 22) },
        Btn = new[] { new Rectangle(45, 290, 38, 12) }.Concat(Theme.HelpBtn).ToArray(),
        Prepare = Prep
    };

    /// <summary>Clears the skin's own check box (drawn by the program in the chosen colour).</summary>
    static void Prep(int[] px, int w)
    {
        int gray = unchecked((int)0xFFDBDFDB);
        for (int y = 68; y < 86; y++) for (int x = 7; x < 24; x++) px[y * w + x] = gray;
        foreach (int l in L)             // band thumbs + their little arrows: only the rail and the ticks stay
            for (int y = 166; y < 192; y++)
            {
                for (int x = l + 11; x < l + 25; x++) px[y * w + x] = px[165 * w + x];
                for (int x = l + 25; x < l + 33; x++) px[y * w + x] = px[(y + 26) * w + x];
            }
        for (int y = 123; y < 148; y++)  // level thumb
            for (int x = 450; x < 464; x++) px[y * w + x] = px[148 * w + x];
        // labels of the skin are wiped: the program draws them itself
        Wipe(px, w, 45, 9, 115, 17, 165);      // title
        Wipe(px, w, 226, 40, 46, 14, 280);     // tab label
        Wipe(px, w, 25, 70, 84, 14, 114);      // Enable Equalizer
        Wipe(px, w, 219, 70, 40, 14, 216);     // Presets
        Wipe(px, w, 26, 118, 33, 14, 24);      // 12dB
        Wipe(px, w, 26, 172, 33, 14, 24);      // 0dB
        Wipe(px, w, 26, 227, 33, 14, 24);      // -12dB
        Wipe(px, w, 418, 124, 22, 13, 416);    // 0dB (level)
        Wipe(px, w, 408, 220, 30, 13, 406);    // -24dB
        Wipe(px, w, 442, 252, 28, 13, 440);    // Level
        foreach (int l in L) Wipe(px, w, l + 3, 253, 28, 14, l + 1);   // 125 ... 8k
        Wipe(px, w, 449, 10, 38, 14, 446);     // minimize / close
    }

    Bitmap thumbSpr, lvlSpr;

    protected override void SkinApplied()
    {
        thumbSpr?.Dispose(); lvlSpr?.Dispose();
        thumbSpr = Cut(Skin, L[0] + 11, 167, 23, 24, 14);   // body (14 px) + arrow
        lvlSpr = Cut(Skin, 450, 123, 14, 24, 14);
    }

    /// <summary>Thumb sprite with see-through corners; only the first bodyW columns are the body, the rest keeps just the black arrow.</summary>
    static Bitmap Cut(Bitmap src, int x0, int y0, int w, int h, int bodyW)
    {
        var t = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var bgc = src.GetPixel(x0 - 1, y0);
        var seen = new bool[w, h];
        var st = new Stack<Point>();
        void Push(int x, int y)
        {
            if (x < 0 || y < 0 || x >= bodyW || y >= h || seen[x, y] || src.GetPixel(x0 + x, y0 + y).ToArgb() != bgc.ToArgb()) return;
            seen[x, y] = true; st.Push(new Point(x, y));
        }
        for (int x = 0; x < bodyW; x++) { Push(x, 0); Push(x, h - 1); }
        for (int y = 0; y < h; y++) { Push(0, y); Push(bodyW - 1, y); }
        while (st.Count > 0)
        {
            var p = st.Pop();
            Push(p.X + 1, p.Y); Push(p.X - 1, p.Y); Push(p.X, p.Y + 1); Push(p.X, p.Y - 1);
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = src.GetPixel(x0 + x, y0 + y);
                bool keep = x < bodyW ? !seen[x, y] : c.R + c.G + c.B < 270;      // arrow = black pixels only
                if (keep) t.SetPixel(x, y, c);
            }
        return t;
    }
    protected override ThemeCfg Cfg => C;
    readonly float[] val = new float[7];
    readonly bool[] moved = new bool[7];
    float level; bool levelMoved; bool on = true;
    int preset;

    public EqualizerForm() : base("equalizer.png", "Graphic Equalizer", new Point(140, 140))
    {
        HideOnClose = true;

        AddHot(new Rectangle(8, 68, 130, 20), down: _ => { on = !on; Invalidate(); });

        var combo = new Rectangle(262, 68, 157, 20);
        AddHot(combo, up: () =>
        {
            if (!on || !Inside(combo)) return;
            Drop(combo, Presets.Select(p => p.Name).ToArray(), preset, i =>
            {
                preset = i;
                if (Presets[i].V != null)
                    for (int b = 0; b < 7; b++) { val[b] = Presets[i].V[b]; moved[b] = true; }
                Invalidate();
            });
        });

        for (int i = 0; i < 7; i++)
        {
            int k = i;
            void set(Point p)
            {
                val[k] = (float)Math.Round(Math.Clamp((Zero - p.Y) / PxPerDb, -12f, 12f), 1);
                moved[k] = true; preset = 0; Invalidate();
            }
            AddHot(new Rectangle(L[i], 100, 34, 154), down: set, move: set, when: () => on);
        }
        void setLevel(Point p)
        {
            level = (float)Math.Round(Math.Clamp((135 - p.Y) / 4f, -24f, 0f), 1);
            levelMoved = true; Invalidate();
        }
        AddHot(new Rectangle(440, 118, 36, 126), down: setLevel, move: setLevel, when: () => on);

        AddButton("default", new Rectangle(21, 287, 88, 19), () =>
        {
            Array.Clear(val); Array.Clear(moved); level = 0; levelMoved = false; on = true; preset = 0; Invalidate();
        });
        AddButton("eax", new Rectangle(116, 284, 24, 24), () => Nav.ToSettings(this));
        AddButton("speaker", new Rectangle(148, 284, 26, 24), () => Nav.ToSpeaker(this));
    }

    protected override void Overlay(Graphics g)
    {
        Chrome(g, "Graphic Equalizer", 45);
        DynTxt(g, "Equalizer", 227, 40);
        DynTxt(g, "Enable Equalizer", 25, 70);
        DynTxt(g, "Presets", 220, 70);
        DynTxtR(g, "12dB", 55, 119, FS); DynTxtR(g, "0dB", 55, 173, FS); DynTxtR(g, "-12dB", 55, 228, FS);
        DynTxtR(g, "0dB", 437, 125, FS); DynTxtR(g, "-24dB", 437, 221, FS);
        DynTxt(g, "Level", 443, 252, FS);
        string[] bandNames = { "125", "250", "500", "1k", "2k", "4k", "8k" };
        for (int i = 0; i < 7; i++) DynTxtC(g, bandNames[i], L[i] + 16.5f, 253, FB);
        DrawCheck(g, 9, 70, on);
        g.FillRectangle(Brushes.White, 265, 71, 140, 15);
        Txt(g, Presets[preset].Name, 266, 71, Color.Black);

        for (int i = 0; i < 7; i++)      // thumbs (with their arrows) on top of the see-through boxes
        {
            int top = (int)Math.Round(Zero - val[i] * PxPerDb) - 12;
            if (on) g.DrawImage(thumbSpr, L[i] + 11, top, thumbSpr.Width, thumbSpr.Height);
            else DrawFaded(g, thumbSpr, L[i] + 11, top, 0.4f);
        }
        int lt = 123 + (int)Math.Round(-level * 4f);
        if (on) g.DrawImage(lvlSpr, 450, lt, lvlSpr.Width, lvlSpr.Height);
        else DrawFaded(g, lvlSpr, 450, lt, 0.4f);
        if (!on) g.FillRectangle(Veil, 262, 68, 157, 20);   // only the white preset box greys out
    }
}
