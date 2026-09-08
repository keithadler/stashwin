using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Stash;

public partial class App : Application
{
    public static bool StartHidden { get; set; }
    private Tray? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Theme.Apply(this);
        DispatcherUnhandledException += (_, ex) => Paths.Log_("unhandled: " + ex.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Paths.Log_("fatal: " + ex.ExceptionObject);
        var shell = new Shell();
        var window = new MainWindow(shell);
        MainWindow = window;
        // Open at sign-in, registered once, like the Mac's login item; a toggle in Settings after that.
        var cfg = shell.Config;
        if (!cfg.OpenAtSignInOffered) { global::Stash.Startup.Set(cfg.OpenAtSignIn); cfg.OpenAtSignInOffered = true; cfg.Save(); }
        if (cfg.Tray) _tray = new Tray(window);
        window.TrayEnabled = cfg.Tray;
        if (!StartHidden) window.Show();
        Exit += (_, _) => _tray?.Dispose();
        // The daily update check, 20 seconds after launch when due and enabled. One GET, no identifiers.
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += async (_, _) =>
        {
            timer.Interval = TimeSpan.FromHours(1);
            var c = Config.Load();
            if (!Stash.Core.Updates.ShouldCheck(c.UpdateCheck, c.LastUpdateCheck, DateTimeOffset.Now)) return;
            var r = await UpdateCheck.Now(c);
            if (r is Stash.Core.Updates.Available a && a.Version != c.SkippedVersion) shell.UpdateLine = L.F("Version {0} is available.", a.Version);
            shell.UpdatePage = r is Stash.Core.Updates.Available b ? b.Page : null;
        };
        timer.Start();
    }

    public void RefreshTray() => _tray?.Refresh();
}

/// <summary>Follows the Windows light or dark setting: swaps the brush palette and asks DWM for a matching title bar.</summary>
public static class Theme
{
    public static bool IsDark { get; private set; }

    public static void Apply(Application app, bool? force = null)
    {
        IsDark = force ?? SystemPrefersDark();
        if (!IsDark) return;
        var dark = new Dictionary<string, string>
        {
            ["Ground"] = "#1A1D1A", ["Card"] = "#252925", ["Line"] = "#363B36", ["Ink"] = "#F1F4F1", ["Muted"] = "#A0A8A1",
            ["Accent"] = "#3FA96F", ["AccentInk"] = "#FFFFFF",
            ["GoodBg"] = "#1E3A29", ["GoodInk"] = "#9BDBAE", ["WarnBg"] = "#443417", ["WarnInk"] = "#F0C56B", ["BadBg"] = "#4A2323", ["BadInk"] = "#F3A5A5",
            ["NavBg"] = "#1F231F", ["NavSelected"] = "#2B3D31",
        };
        foreach (var (key, hex) in dark) app.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
    }

    private static bool SystemPrefersDark()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return k?.GetValue("AppsUseLightTheme") is int v && v == 0; }
        catch { return false; }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void DecorateTitleBar(Window w)
    {
        if (!IsDark) return;
        try { var hwnd = new WindowInteropHelper(w).Handle; int on = 1; DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)); } catch { }
    }
}
