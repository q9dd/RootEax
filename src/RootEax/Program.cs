namespace EAX;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); // real 1:1 pixels, no blur scaling
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Theme.Load();
        Engine.Init();                                   // sound engine (rooms + mic)
        Application.Run(new Ctx());
        Engine.Shutdown();
    }
}

/// <summary>Only the Settings window opens at start; closing it exits the app.</summary>
sealed class Ctx : ApplicationContext
{
    public Ctx()
    {
        var f = new SettingsForm();
        Nav.Main = f;
        MainForm = f;
        f.Show();
    }
}

/// <summary>Opens the other two pages from the little icons in the bottom bar.</summary>
static class Nav
{
    public static SettingsForm Main;
    static SpeakerForm sp;
    static EqualizerForm eq;

    public static void ToSettings(SkinForm from) => Show(Main, from);
    public static void ToSpeaker(SkinForm from) => Show(sp ??= new SpeakerForm(), from);
    public static void ToEqualizer(SkinForm from) => Show(eq ??= new EqualizerForm(), from);

    static void Show(SkinForm f, SkinForm from)
    {
        if (f == from) return;
        if (!f.Visible) f.Location = new Point(from.Left + 30, from.Top + 30);
        f.Show();
        if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
        f.Activate();
    }
}
