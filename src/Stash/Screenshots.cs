using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Stash.Core;

namespace Stash;

/// <summary>Renders the windows from sample data to PNG files, for the README and the cards.</summary>
public static class Screenshots
{
    public static int Render(string dir, TextWriter o, bool announce = false, bool dark = false, bool spanish = false)
    {
        Directory.CreateDirectory(dir);
        if (spanish) L.Spanish = true;
        var app = Application.Current ?? new App();
        if (app.Resources.Count == 0) (app as App)?.InitializeComponent();
        Theme.Apply(app, dark);
        var shell = Demo.Shell();
        var suffix = (dark ? "-dark" : "") + (spanish ? "-es" : "");
        var window = new MainWindow(shell) { Width = 980, Height = 820, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowInTaskbar = false };
        window.Show();
        window.UpdateLayout();
        Save(Capture((FrameworkElement)window.Content), Path.Combine(dir, "main" + suffix + ".png"), o);

        var card = new RecoveryCardWindow(shell, shell.Key!) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowInTaskbar = false };
        card.Show(); card.UpdateLayout();
        Save(Capture((FrameworkElement)card.Content), Path.Combine(dir, "card" + suffix + ".png"), o);

        var restore = new RestoreWindow(shell, shell.Snapshots[0]) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowInTaskbar = false };
        restore.Show(); restore.UpdateLayout();
        Save(Capture((FrameworkElement)restore.Content), Path.Combine(dir, "restore" + suffix + ".png"), o);

        var settings = new SettingsWindow(shell) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, ShowInTaskbar = false };
        settings.Show(); settings.UpdateLayout();
        Save(Capture((FrameworkElement)settings.Content), Path.Combine(dir, "settings" + suffix + ".png"), o);

        if (announce) Promo.Render(dir, window, card, o);
        foreach (var w in new Window[] { settings, restore, card, window }) w.Close();
        return 0;
    }

    public static RenderTargetBitmap Capture(FrameworkElement root)
    {
        root.UpdateLayout();
        // Draw through a VisualBrush so the element's own offset inside its window (a margin, a scroll viewer's padding) does not shift the picture.
        var w = root.ActualWidth + root.Margin.Left + root.Margin.Right;
        var h = root.ActualHeight + root.Margin.Top + root.Margin.Bottom;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle((Brush)Application.Current.Resources["Ground"], null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(root.Margin.Left, root.Margin.Top, root.ActualWidth, root.ActualHeight));
        }
        var bmp = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
        bmp.Render(dv);
        return bmp;
    }

    public static void Save(BitmapSource bmp, string path, TextWriter o)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
        o.WriteLine(path);
    }
}

/// <summary>Four 1600×900 announcement cards, rendered at 2× from the same sample data as the screenshots.</summary>
public static class Promo
{
    private sealed record Card(string File, string Headline, string Sub, string? Shot, string? Mono = null);

    private static readonly Card[] Cards =
    {
        new("1-hero", "Encrypted backup into the\nstorage you already have.", "The OneDrive that comes with 365, the Google Drive that comes with Gmail, a disk, a NAS. Several at once, on a schedule.", "main"),
        new("2-card", "No password. A card.", "Your key is 24 words. There is nothing to guess and nothing to reset. The same card restores on a Mac.", "card"),
        new("3-honest", "The provider only ever\nsees ciphertext.", "Every piece is encrypted on the PC first and named by a keyed hash. Lose the card and nobody can read the backup, including you.", "restore"),
        new("4-free", "Free. Open source. No account.", "Nothing leaves the PC unencrypted. One exe, no installer. Built by Keith Adler.", null, "stash backup"),
    };

    public static void Render(string dir, MainWindow main, RecoveryCardWindow card, TextWriter o)
    {
        var promoDir = Path.Combine(dir, "promo");
        Directory.CreateDirectory(promoDir);
        var icon = new BitmapImage(new Uri("pack://application:,,,/Assets/stash-256.png"));
        RestoreWindow? restore = null;
        foreach (var c in Cards)
        {
            var root = new Grid { Width = 1600, Height = 900 };
            root.Background = new LinearGradientBrush(new GradientStopCollection { new(Color.FromRgb(0x2F, 0x8F, 0x5B), 0), new(Color.FromRgb(0x1B, 0x5E, 0x3A), 0.6), new(Color.FromRgb(0x0F, 0x3D, 0x26), 1) }, new Point(0, 0), new Point(1, 1));
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(72, 56, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
            brand.Children.Add(new Image { Source = icon, Width = 56, Height = 56, Margin = new Thickness(0, 0, 18, 0) });
            brand.Children.Add(new TextBlock { Text = "Stash for Windows", FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") });
            root.Children.Add(brand);
            bool hasShot = c.Shot is not null;
            var text = new StackPanel { Margin = new Thickness(72, 0, hasShot ? 0 : 72, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = hasShot ? HorizontalAlignment.Left : HorizontalAlignment.Center, Width = hasShot ? 720 : 1200 };
            text.Children.Add(new TextBlock { Text = c.Headline, FontSize = 58, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, LineHeight = 68, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"), TextAlignment = hasShot ? TextAlignment.Left : TextAlignment.Center });
            text.Children.Add(new TextBlock { Text = c.Sub, FontSize = 25, Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 26, 0, 0), LineHeight = 36, TextAlignment = hasShot ? TextAlignment.Left : TextAlignment.Center });
            if (c.Mono is not null)
            {
                var pill = new Border { Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(12), Padding = new Thickness(28, 14, 28, 14), Margin = new Thickness(0, 44, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
                pill.Child = new TextBlock { Text = c.Mono, FontSize = 28, Foreground = Brushes.White, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
                text.Children.Add(pill);
            }
            root.Children.Add(text);
            if (c.Shot is not null)
            {
                FrameworkElement el = c.Shot switch
                {
                    "card" => (FrameworkElement)card.Content,
                    "restore" => (FrameworkElement)(restore ??= Open(new RestoreWindow(main.Shell, main.Shell.Snapshots[0]))).Content,
                    _ => (FrameworkElement)main.Content,
                };
                var shot = Screenshots.Capture(el);
                var frame = new Border
                {
                    Width = 760, Height = 500, CornerRadius = new CornerRadius(14), ClipToBounds = true,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 60, 56, 0),
                    Background = new ImageBrush(shot) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    Effect = new DropShadowEffect { BlurRadius = 60, ShadowDepth = 18, Opacity = 0.55, Direction = 270 },
                };
                root.Children.Add(frame);
            }
            root.Children.Add(new TextBlock { Text = "github.com/keithadler/stashwin", FontSize = 22, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 0, 72, 44), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, FontFamily = new FontFamily("Cascadia Mono, Consolas") });
            root.Measure(new Size(1600, 900)); root.Arrange(new Rect(0, 0, 1600, 900)); root.UpdateLayout();
            var bmp = new RenderTargetBitmap(3200, 1800, 192, 192, PixelFormats.Pbgra32);
            bmp.Render(root);
            Screenshots.Save(bmp, Path.Combine(promoDir, c.File + ".png"), o);
        }
        restore?.Close();
    }

    private static T Open<T>(T w) where T : Window { w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = -20000; w.Top = -20000; w.ShowInTaskbar = false; w.Show(); w.UpdateLayout(); return w; }
}
