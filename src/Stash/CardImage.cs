using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Stash.Core;

namespace Stash;

/// <summary>The recovery card drawn as a picture: 24 numbered words, the QR code, the fingerprint and the date. Same content as the Mac's PDF card.</summary>
public static class CardImage
{
    public static FrameworkElement Build(MasterKey key, double width = 1000)
    {
        var root = new Border { Width = width, Background = Brushes.White, Padding = new Thickness(48) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = "Stash recovery card", FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI") });
        left.Children.Add(new TextBlock { Text = "These 24 words are the only way to read the backup. Keep this card somewhere safe; treat it like a passport. Works in Stash for Windows and Stash for Mac.", FontSize = 14, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 24, 18), FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") });
        var words = new Grid();
        for (int c = 0; c < 3; c++) words.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < 8; r++) words.RowDefinitions.Add(new RowDefinition());
        var w = key.Words;
        for (int i = 0; i < 24; i++)
        {
            var t = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 18, Margin = new Thickness(0, 3, 0, 3), Foreground = Brushes.Black };
            t.Inlines.Add(new Run($"{i + 1,2}. ") { Foreground = Brushes.Gray });
            t.Inlines.Add(new Run(w[i]) { FontWeight = FontWeights.SemiBold });
            Grid.SetRow(t, i % 8); Grid.SetColumn(t, i / 8);
            words.Children.Add(t);
        }
        left.Children.Add(words);
        left.Children.Add(new TextBlock { Text = $"Fingerprint {key.Fingerprint}    Made {DateTime.Now:d MMMM yyyy}", FontSize = 13, Foreground = Brushes.DimGray, Margin = new Thickness(0, 18, 0, 0), FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") });
        grid.Children.Add(left);
        var qr = QrCode.Encode(key.QrPayload);
        var png = qr.ToPng(6);
        var img = new BitmapImage();
        img.BeginInit(); img.StreamSource = new MemoryStream(png); img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit();
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 0, 0, 0) };
        right.Children.Add(new Image { Source = img, Width = 260, Height = 260, SnapsToDevicePixels = true });
        RenderOptions.SetBitmapScalingMode(right.Children[0], BitmapScalingMode.NearestNeighbor);
        right.Children.Add(new TextBlock { Text = "Scan with a phone, or with Stash for Mac.", FontSize = 12, Foreground = Brushes.DimGray, TextAlignment = TextAlignment.Center, Width = 260, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") });
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        root.Child = grid;
        return root;
    }

    public static byte[] Render(MasterKey key)
    {
        var el = Build(key);
        el.Measure(new Size(el.Width, double.PositiveInfinity));
        el.Arrange(new Rect(0, 0, el.Width, el.DesiredSize.Height));
        el.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)(el.ActualWidth * 2), (int)(el.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bmp.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
