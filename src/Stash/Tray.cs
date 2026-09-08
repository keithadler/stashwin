using System.IO;
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace Stash;

/// <summary>The tray presence, the Windows twin of the Mac's menu bar item: last backup, next run, Back Up Now, Verify, Open,
/// and one balloon when a scheduled backup or verify fails. Nothing chatty: a backup that works should be invisible.</summary>
public sealed class Tray : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly MainWindow _window;
    private readonly DispatcherTimer _timer;
    private string? _lastReported;

    public Tray(MainWindow window)
    {
        _window = window;
        _icon = new WinForms.NotifyIcon { Text = "Stash for Windows", Visible = true };
        try
        {
            using var s = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/stash-256.png"))?.Stream;
            if (s is not null) { using var bmp = new System.Drawing.Bitmap(s); using var small = new System.Drawing.Bitmap(bmp, new System.Drawing.Size(32, 32)); _icon.Icon = System.Drawing.Icon.FromHandle(small.GetHicon()); }
        }
        catch { _icon.Icon = System.Drawing.SystemIcons.Application; }
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(L.T("Open Stash"), null, (_, _) => Show());
        menu.Items.Add(L.T("Back Up Now"), null, (_, _) => { Show(); _window.BackUpFromTray(); });
        menu.Items.Add(L.T("Verify"), null, (_, _) => { Show(); _window.VerifyFromTray(); });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(L.T("Quit"), null, (_, _) => { _window.ReallyClose = true; _window.Close(); });
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => Show();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    private void Show()
    {
        _window.Show(); _window.WindowState = WindowState.Normal; _window.Activate();
    }

    public void Refresh()
    {
        var cfg = Config.Load();
        var last = cfg.LastBackup is { } lb ? L.F("Last backup {0}", lb.ToLocalTime().ToString("d MMM HH:mm")) : L.T("No backup yet");
        var next = cfg.Interval is { } i && cfg.LastBackup is { } l ? L.F("Next about {0}", l.Add(i).ToLocalTime().ToString("d MMM HH:mm")) : cfg.Schedule == "off" ? L.T("No schedule") : L.T("Next: on the schedule");
        _icon.Text = Truncate($"Stash for Windows. {last}. {next}.", 127);
        // One balloon when a scheduled run failed, once per result.
        if (cfg.LastScheduledResult is { } r && r != "ok" && cfg.LastScheduledAt is { } at)
        {
            var tag = at.ToString("O") + r;
            if (_lastReported != tag)
            {
                _lastReported = tag;
                _icon.ShowBalloonTip(8000, "Stash for Windows", L.T(r == "partial" ? "The scheduled backup skipped something. Open Stash to see what." : "The scheduled backup or check failed. Open Stash to see why."), WinForms.ToolTipIcon.Warning);
            }
        }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)];

    public void Dispose() { _timer.Stop(); _icon.Visible = false; _icon.Dispose(); }
}
