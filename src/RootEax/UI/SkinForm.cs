using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EAX;

/// <summary>Borderless window whose look is the pixel-exact reference bitmap.</summary>
abstract class SkinForm : Form
{
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, int w, int l);

    protected static readonly Font F = new("Tahoma", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
    protected static readonly Font FB = new("Tahoma", 11f, FontStyle.Bold, GraphicsUnit.Pixel);
    protected static readonly Font FT = new("Tahoma", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
    protected static readonly Font FS = new("Tahoma", 10f, FontStyle.Regular, GraphicsUnit.Pixel);
    static readonly StringFormat SF = new(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces };
    protected static readonly Color Ink = Color.FromArgb(61, 61, 61);
    protected static readonly Color PanelCol = Color.FromArgb(219, 218, 219);
    static Icon appIcon;

    static readonly List<SkinForm> All = new();
    protected Bitmap Bg;                 // window picture (themed)
    protected Bitmap Plate;              // background only (picture), null when no theme
    protected bool LightText;            // theme text colour: white with shadow
    readonly string res;
    protected virtual ThemeCfg Cfg => null;
    protected virtual void SkinApplied() { }
    protected string Pressed;
    protected bool HideOnClose;           // secondary pages hide instead of closing
    readonly Dictionary<string, Rectangle> buttons = new();
    readonly List<Hot> hots = new();
    Hot active;
    static readonly SolidBrush PressBrush = new(Color.FromArgb(48, 0, 0, 0));

    sealed class Hot { public Rectangle R; public Action<Point> Down, Move; public Action Up; public Func<bool> When; }

    protected SkinForm(string resource, string title, Point location)
    {
        res = resource;
        All.Add(this);
        ApplySkin();
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Location = location;
        Text = title;
        BackColor = Color.White;
        Icon = AppIcon();
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);

        int w = Bg.Width;
        // title bar drag, then minimize / close on top of it
        AddHot(new Rectangle(0, 0, w, 34), down: _ => { ReleaseCapture(); SendMessage(Handle, 0xA1, 2, 0); });
        AddHot(new Rectangle(w - 64, 6, 26, 24), up: () => { if (Inside(w - 64, 6, 26, 24)) WindowState = FormWindowState.Minimized; });
        AddHot(new Rectangle(w - 34, 6, 26, 24), up: () => { if (Inside(w - 34, 6, 26, 24)) Close(); });

        // device box, help "?" and the little arrow
        var dev = new Rectangle(196, 6, 176, 22);
        AddHot(dev, up: () => { if (Inside(dev)) Drop(dev, new[] { "SB Live! 24-bit External" }, 0, _ => { }); });
        var help = new Rectangle(373, 7, 38, 22);
        AddHot(help, up: () => { if (Inside(help)) About(); });
        var arrow = new Rectangle(413, 7, 16, 22);
        var menu = new Rectangle(arrow.X - 190, arrow.Y, 206, arrow.Height);
        AddHot(arrow, up: () =>
        {
            if (Inside(arrow))
                Drop(menu, new[]
                {
                    "Choose background image…", "Reset background (original)", "Stretch / move image…", "Stretch to fill", "Keep proportions",   // 0-4
                    "-",
                    "Text color: Auto", "Text color: Light", "Text color: Dark", "Pick any color…",                                              // 6-9
                    "-",
                    "Icon: EAX…", "Icon: Speaker…", "Icon: Equalizer…", "Reset icons",                                                            // 11-14
                    "-",
                    "Export skin images (to edit)", "Open skin folder", "Reload skin images", "Reset everything (original look)",                    // 16-19
                    Theme.Square ? "Round the corners again (original shape)" : "Square window (no rounded corners)", "About"   // 20-21
                },
                    Theme.UseAccent ? 9 : 6 + Theme.TextMode, MenuAction);
        });
    }

    /// <summary>(Re)loads the skin image and builds the themed window picture.</summary>
    public void ApplySkin()
    {
        var src = Theme.LoadSkin(res);
        Theme.Compose(src, Cfg, out var nb, out var np, out var lt);
        var oldBg = Bg; var oldPlate = Plate;
        Bg = nb; Plate = np; LightText = lt;
        ClientSize = Bg.Size;
        var oldRegion = Region;
        Region = Theme.Square ? new Region(new Rectangle(0, 0, src.Width, src.Height)) : MakeRegion(src);
        oldRegion?.Dispose(); oldBg?.Dispose(); oldPlate?.Dispose();
        Skin = src;
        SkinApplied();
        Invalidate();
    }
    protected Bitmap Skin;               // untouched skin image (for sprites)
    public static void ApplyAll() { foreach (var f in All.ToArray()) f.ApplySkin(); }
    public static void InvalidateAll() { foreach (var f in All.ToArray()) f.Invalidate(); }

    void PickIcon(string key)
    {
        using var d = new OpenFileDialog { Title = "Choose icon image", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico|All files|*.*" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        Theme.SetIcon(key, d.FileName);
        InvalidateAll();
    }

    void MenuAction(int k)
    {
        switch (k)
        {
            case 0:
                using (var d = new OpenFileDialog { Title = "Choose background image", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" })
                    if (d.ShowDialog(this) == DialogResult.OK) { Theme.SetImage(d.FileName); ApplyAll(); }
                break;
            case 1: Theme.SetImage(""); ApplyAll(); break;
            case 2: AdjustForm.ShowFor(this); break;
            case 3: Theme.SetFit(1, 1, .5, .5); ApplyAll(); AdjustForm.Refresh(); break;
            case 4: Theme.KeepProportions(); ApplyAll(); AdjustForm.Refresh(); break;
            case 6: case 7: case 8: Theme.SetTextMode(k - 6); ApplyAll(); break;
            case 9:
                using (var d = new ColorDialog { FullOpen = true, AnyColor = true, Color = Theme.Accent })
                    if (d.ShowDialog(this) == DialogResult.OK) { Theme.SetAccent(d.Color); ApplyAll(); }
                break;
            case 11: PickIcon("eax"); break;
            case 12: PickIcon("speaker"); break;
            case 13: PickIcon("eq"); break;
            case 14: Theme.ResetIcons(); InvalidateAll(); break;
            case 16:
                try
                {
                    Theme.Export(Nav.Main?.ThumbSprite);
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Theme.SkinDir + "\"") { UseShellExecute = true });
                    MessageBox.Show("Skin images are in:\n" + Theme.SkinDir + "\n\nEdit any of them (keep the same size), then choose \"Reload skin images\".\nDelete a file to go back to the built-in one.", "Root Eax", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception e) { MessageBox.Show(e.Message, "Root Eax"); }
                break;
            case 17:
                try { Directory.CreateDirectory(Theme.SkinDir); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Theme.SkinDir + "\"") { UseShellExecute = true }); }
                catch (Exception e) { MessageBox.Show(e.Message, "Root Eax"); }
                break;
            case 18: ApplyAll(); break;
            case 19: Theme.ResetAll(); ApplyAll(); AdjustForm.Refresh(); break;
            case 20: Theme.SetSquare(!Theme.Square); ApplyAll(); break;
            default: About(); break;
        }
    }

    static void About() => MessageBox.Show("Root Eax\nCopyright © Root", "About", MessageBoxButtons.OK, MessageBoxIcon.Information);

    static Icon AppIcon()
    {
        if (appIcon != null) return appIcon;
        using var b = Theme.LoadSkin("17517.png");
        return appIcon = Icon.FromHandle(b.GetHicon());
    }

    static Region MakeRegion(Bitmap b)
    {
        static bool Black(Color c) => c.R == 0 && c.G == 0 && c.B == 0;
        var p = new GraphicsPath();
        for (int y = 0; y < b.Height; y++)
        {
            int l = 0; while (l < b.Width && Black(b.GetPixel(l, y))) l++;
            if (l >= b.Width) continue;
            int r = 0; while (r < b.Width - l && Black(b.GetPixel(b.Width - 1 - r, y))) r++;
            p.AddRectangle(new Rectangle(l, y, b.Width - l - r, 1));
        }
        return new Region(p);
    }

    protected static Bitmap LoadBitmap(string name) => Theme.LoadSkin(name);

    protected bool Inside(Rectangle r) => r.Contains(PointToClient(Cursor.Position));
    protected bool Inside(int x, int y, int w, int h) => Inside(new Rectangle(x, y, w, h));

    protected void AddHot(Rectangle r, Action<Point> down = null, Action<Point> move = null, Action up = null, Func<bool> when = null)
        => hots.Add(new Hot { R = r, Down = down, Move = move, Up = up, When = when });

    protected void AddButton(string key, Rectangle r, Action click, Func<bool> when = null)
    {
        buttons[key] = r;
        AddHot(r,
            down: _ => { Pressed = key; Invalidate(); },
            up: () => { if (r.Contains(PointToClient(Cursor.Position))) click?.Invoke(); },
            when: when);
    }

    /// <summary>Simple drop-down list under a combo box area.</summary>
    protected void Drop(Rectangle anchor, string[] items, int sel, Action<int> pick)
    {
        var m = new ContextMenuStrip { Font = new Font("Tahoma", 8.25f), ShowImageMargin = false, ShowCheckMargin = true };
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] == "-") { m.Items.Add(new ToolStripSeparator()); continue; }
            int k = i;
            var it = new ToolStripMenuItem(items[i]) { Checked = i == sel };
            it.Click += (s, e) => pick(k);
            m.Items.Add(it);
        }
        m.MinimumSize = new Size(anchor.Width, 0);
        m.Closed += (s, e) => BeginInvoke(new Action(m.Dispose));
        m.Show(this, new Point(anchor.Left, anchor.Bottom));
    }

    protected static void Txt(Graphics g, string s, int x, int y, Color c)
        => TextRenderer.DrawText(g, s, F, new Point(x, y), c,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

    static double LumC(Color c) => (c.R + c.G + c.B) / 3.0;

    /// <summary>
    /// Label drawn by the program (smooth, anti-aliased): dark gray, white, or the chosen colour, with a soft
    /// shadow so it stays readable on any picture. y is the same "top" the old Txt() used.
    /// </summary>
    protected void DynTxt(Graphics g, string s, int x, int y, Font f = null)
    {
        f ??= F;
        var oldHint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        float fy = y - 1f;
        Color col, shadow = Color.Empty;
        if (Theme.UseAccent)
        {
            col = Theme.Accent;
            shadow = LumC(col) > 110 ? Color.FromArgb(150, 0, 0, 0) : Color.FromArgb(150, 255, 255, 255);
        }
        else if (LightText) { col = Color.White; shadow = Color.FromArgb(150, 0, 0, 0); }
        else
        {
            col = Ink;
            if (Plate != null) shadow = Color.FromArgb(110, 255, 255, 255);
        }
        if (shadow != Color.Empty)
            using (var sb = new SolidBrush(shadow)) g.DrawString(s, f, sb, x + 1, fy + 1, SF);
        using (var tb = new SolidBrush(col)) g.DrawString(s, f, tb, x, fy, SF);
        g.TextRenderingHint = oldHint;
    }

    protected float TextW(Graphics g, string s, Font f = null) => g.MeasureString(s, f ?? F, 10000, SF).Width;
    protected void DynTxtR(Graphics g, string s, int right, int y, Font f = null) => DynTxt(g, s, (int)Math.Round(right - TextW(g, s, f)), y, f);
    protected void DynTxtC(Graphics g, string s, float cx, int y, Font f = null) => DynTxt(g, s, (int)Math.Round(cx - TextW(g, s, f) / 2), y, f);

    /// <summary>Title text and the minimize / close marks (the skin's own ones are wiped, these follow the text colour).</summary>
    protected void Chrome(Graphics g, string title, int tx)
    {
        DynTxt(g, title, tx, 9, FT);
        Color col = Theme.UseAccent ? Theme.Accent : (LightText ? Color.White : Ink);
        var oldS = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Theme.UseAccent || LightText)
            using (var sp = new Pen(Color.FromArgb(130, 0, 0, 0), 1.6f))
            {
                g.DrawLine(sp, 451, 21, 461, 21);
                g.DrawLine(sp, 476, 14, 485, 23); g.DrawLine(sp, 485, 14, 476, 23);
            }
        using (var p = new Pen(col, 1.6f))
        {
            g.DrawLine(p, 450, 20, 460, 20);
            g.DrawLine(p, 475, 13, 484, 22); g.DrawLine(p, 484, 13, 475, 22);
        }
        g.SmoothingMode = oldS;
    }

    protected static void DrawFaded(Graphics g, Bitmap b, int x, int y, float alpha)
    {
        using var ia = new ImageAttributes();
        ia.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
        g.DrawImage(b, new Rectangle(x, y, b.Width, b.Height), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
    }

    /// <summary>Wipes a label of the skin: every row is filled with the background pixel at refX (so gradients stay right).</summary>
    protected static void Wipe(int[] px, int w, int x, int y, int rw, int rh, int refX)
    {
        for (int yy = y; yy < y + rh; yy++)
        {
            int v = px[yy * w + refX];
            for (int xx = x; xx < x + rw; xx++) px[yy * w + xx] = v;
        }
    }

    /// <summary>Empties a rectangle down to the plain background (picture or gray).</summary>
    protected void Blank(Graphics g, Rectangle r)
    {
        if (Plate != null) g.DrawImage(Plate, r, r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel);
        else using (var b = new SolidBrush(PanelCol)) g.FillRectangle(b, r);
    }


    /// <summary>Slider in the same look as the original (dark groove, white thumb).</summary>
    protected static void DrawMiniSlider(Graphics g, int x, int y, int w, float frac)
    {
        var track = new Rectangle(x, y + 4, w, 5);
        using (var lb = new LinearGradientBrush(track, Color.FromArgb(61, 61, 61), Color.FromArgb(180, 178, 180), 90f))
            g.FillRectangle(lb, track);
        var th = new Rectangle(x + (int)Math.Round((w - 18) * frac), y, 18, 12);
        using (var lb = new LinearGradientBrush(th, Color.White, Color.FromArgb(206, 203, 206), 90f))
            g.FillRectangle(lb, th);
        using var pen = new Pen(Color.FromArgb(99, 101, 99));
        g.DrawRectangle(pen, th.X, th.Y, th.Width - 1, th.Height - 1);
    }

    /// <summary>13x13 check box; the border and the tick follow the chosen colour.</summary>
    protected static void DrawCheck(Graphics g, int x, int y, bool on)
    {
        var col = Theme.UseAccent ? Theme.Accent : Color.FromArgb(33, 110, 214);
        g.FillRectangle(Brushes.White, x, y, 13, 13);
        using (var pen = new Pen(Theme.UseAccent ? Theme.Accent : Color.FromArgb(110, 110, 110)))
            g.DrawRectangle(pen, x, y, 12, 12);
        if (!on) return;
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var tick = new Pen(col, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            g.DrawLines(tick, new[] { new PointF(x + 2.8f, y + 6.4f), new PointF(x + 5.3f, y + 9.0f), new PointF(x + 10.0f, y + 3.6f) });
        g.SmoothingMode = old;
    }

    /// <summary>Custom pictures for the little icons in the bottom bar (EAX / speaker / equalizer).</summary>
    void DrawIcons(Graphics g)
    {
        foreach (var kv in buttons)
        {
            var ic = Theme.GetIcon(kv.Key);
            if (ic == null) continue;
            var r = kv.Value;
            var er = Rectangle.Inflate(r, 1, 1);
            if (Plate != null) g.DrawImage(Plate, er, er.X, er.Y, er.Width, er.Height, GraphicsUnit.Pixel);
            else g.DrawImage(Bg, er, 112, er.Y, 2, er.Height, GraphicsUnit.Pixel);   // clean strip of the bar, stretched
            float sc = Math.Min((float)r.Width / ic.Width, (float)r.Height / ic.Height);
            int dw = Math.Max(1, (int)Math.Round(ic.Width * sc)), dh = Math.Max(1, (int)Math.Round(ic.Height * sc));
            var oi = g.InterpolationMode; var op = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(ic, r.X + (r.Width - dw) / 2, r.Y + (r.Height - dh) / 2, dw, dh);
            g.InterpolationMode = oi; g.PixelOffsetMode = op;
        }
    }

    protected virtual void Overlay(Graphics g) { }
    protected virtual void Wheel(int notches) { }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (HideOnClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImage(Bg, new Rectangle(0, 0, Bg.Width, Bg.Height), 0, 0, Bg.Width, Bg.Height, GraphicsUnit.Pixel);
        Overlay(g);
        DrawIcons(g);
        if (Pressed != null && buttons.TryGetValue(Pressed, out var r)) g.FillRectangle(PressBrush, r);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Wheel(e.Delta / 120);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        for (int i = hots.Count - 1; i >= 0; i--)
            if (hots[i].R.Contains(e.Location) && (hots[i].When == null || hots[i].When()))
            {
                active = hots[i];
                active.Down?.Invoke(e.Location);
                return;
            }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (active != null && e.Button == MouseButtons.Left) active.Move?.Invoke(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var h = active; active = null;
        h?.Up?.Invoke();
        Pressed = null;
        Invalidate();
    }
}
