namespace EAX;

/// <summary>Small window to stretch / move the background picture (it always stays inside the windows).</summary>
sealed class AdjustForm : Form
{
    static AdjustForm inst;
    readonly TrackBar tw, th, tx, ty;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 80 };
    bool loading;
    int rowY = 10;

    public static void ShowFor(Form owner)
    {
        if (inst == null || inst.IsDisposed) inst = new AdjustForm();
        inst.Sync();
        if (!inst.Visible) inst.Show(owner); else inst.Activate();
    }

    public static void Refresh() { if (inst != null && !inst.IsDisposed) inst.Sync(); }

    AdjustForm()
    {
        Text = "Stretch / move image";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(320, 270);
        TopMost = true;
        Font = new Font("Tahoma", 8.25f);

        tw = Row("Width", 100, 300);
        th = Row("Height", 100, 300);
        tx = Row("Move left / right", 0, 100);
        ty = Row("Move up / down", 0, 100);

        var fill = new Button { Text = "Stretch to fill", Left = 10, Top = rowY + 6, Width = 95, Height = 26 };
        var keep = new Button { Text = "Keep proportions", Left = 111, Top = rowY + 6, Width = 105, Height = 26 };
        var close = new Button { Text = "Close", Left = 222, Top = rowY + 6, Width = 88, Height = 26 };
        fill.Click += (s, e) => { Theme.SetFit(1, 1, .5, .5); SkinForm.ApplyAll(); Sync(); };
        keep.Click += (s, e) => { Theme.KeepProportions(); SkinForm.ApplyAll(); Sync(); };
        close.Click += (s, e) => Close();
        Controls.AddRange(new Control[] { fill, keep, close });

        timer.Tick += (s, e) => { timer.Stop(); SkinForm.ApplyAll(); Theme.Save(); };
        FormClosed += (s, e) => timer.Dispose();
    }

    TrackBar Row(string label, int min, int max)
    {
        Controls.Add(new Label { Text = label, Left = 10, Top = rowY, Width = 300, Height = 14 });
        var t = new TrackBar { Left = 6, Top = rowY + 14, Width = 306, Height = 30, Minimum = min, Maximum = max, TickStyle = TickStyle.None };
        t.Scroll += (s, e) =>
        {
            if (loading) return;
            Theme.SetFit(tw.Value / 100.0, th.Value / 100.0, tx.Value / 100.0, ty.Value / 100.0, false);
            timer.Stop(); timer.Start();
        };
        Controls.Add(t);
        rowY += 52;
        return t;
    }

    void Sync()
    {
        loading = true;
        Theme.GetFit(out var w, out var h, out var x, out var y);
        tw.Value = Math.Clamp((int)Math.Round(w * 100), 100, 300);
        th.Value = Math.Clamp((int)Math.Round(h * 100), 100, 300);
        tx.Value = Math.Clamp((int)Math.Round(x * 100), 0, 100);
        ty.Value = Math.Clamp((int)Math.Round(y * 100), 0, 100);
        loading = false;
    }
}
