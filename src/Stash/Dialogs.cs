using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using Stash.Core;

namespace Stash;

internal static class Ui
{
    public static Brush B(string key) => (Brush)Application.Current.Resources[key];
    public static Style S(string key) => (Style)Application.Current.Resources[key];
    public static TextBlock Text(string text, string? style = null, Thickness? margin = null)
    {
        var t = new TextBlock { Text = L.T(text) };
        if (style is not null) t.Style = S(style);
        if (margin is not null) t.Margin = margin.Value;
        return t;
    }
    public static Button Button(string text, RoutedEventHandler click, bool primary = false)
    {
        var b = new Button { Content = L.T(text) };
        if (primary) b.Style = S("Primary");
        b.Click += click;
        return b;
    }
    public static Window Frame(string title, double width)
        => new() { Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = B("Ground"), FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") };
}

/// <summary>The recovery card: 24 numbered words, the fingerprint, and a three-word check before the app will back anything up.</summary>
public sealed class RecoveryCardWindow : Window
{
    private readonly Shell _shell; private readonly MasterKey _key;
    private readonly int[] _ask;
    private readonly TextBox[] _answers = new TextBox[3];
    private readonly TextBlock _wrong;

    public RecoveryCardWindow(Shell shell, MasterKey key)
    {
        _shell = shell; _key = key;
        Title = L.T("Your recovery card"); Width = 760; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Ui.B("Ground");
        SourceInitialized += (_, _) => Theme.DecorateTitleBar(this);
        var rnd = new Random();
        _ask = Enumerable.Range(0, 24).OrderBy(_ => rnd.Next()).Take(3).OrderBy(i => i).ToArray();
        var panel = new StackPanel { Margin = new Thickness(28, 22, 28, 20) };
        panel.Children.Add(Ui.Text("Your recovery card", "H1"));
        panel.Children.Add(Ui.Text("Write these 24 words down, or save the card, then put it somewhere safe. Stash does not keep a copy. The same card works in Stash for Mac.", "Lead"));
        var card = new Border { Style = Ui.S("CardStyle"), Padding = new Thickness(20, 16, 20, 16) };
        var grid = new Grid();
        for (int c = 0; c < 3; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        for (int r = 0; r < 8; r++) grid.RowDefinitions.Add(new RowDefinition());
        var words = key.Words;
        for (int i = 0; i < 24; i++)
        {
            var t = new TextBlock { Margin = new Thickness(0, 2, 8, 2), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 14 };
            t.Inlines.Add(new Run($"{i + 1,2}. ") { Foreground = Ui.B("Muted") });
            t.Inlines.Add(new Run(words[i]) { FontWeight = FontWeights.SemiBold });
            Grid.SetRow(t, i % 8); Grid.SetColumn(t, i / 8);
            grid.Children.Add(t);
        }
        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition()); inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var wordsAndFp = new StackPanel();
        wordsAndFp.Children.Add(grid);
        wordsAndFp.Children.Add(Ui.Text(L.F("Key fingerprint {0}. Two cards can be told apart by it.", key.Fingerprint), "Small", new Thickness(0, 10, 0, 0)));
        inner.Children.Add(wordsAndFp);
        var qrPng = QrCode.Encode(key.QrPayload).ToPng(4);
        var qrImg = new System.Windows.Media.Imaging.BitmapImage();
        qrImg.BeginInit(); qrImg.StreamSource = new MemoryStream(qrPng); qrImg.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; qrImg.EndInit();
        var qrBox = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var qrView = new Image { Source = qrImg, Width = 150, Height = 150 };
        RenderOptions.SetBitmapScalingMode(qrView, BitmapScalingMode.NearestNeighbor);
        qrBox.Children.Add(qrView);
        qrBox.Children.Add(Ui.Text("Scan with a phone or a Mac.", "Small", new Thickness(0, 4, 0, 0)));
        Grid.SetColumn(qrBox, 1);
        inner.Children.Add(qrBox);
        card.Child = inner;
        panel.Children.Add(card);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        actions.Children.Add(Ui.Button("Save as image…", (_, _) => SaveImage()));
        actions.Children.Add(Ui.Button("Save as text…", (_, _) => Save()));
        actions.Children.Add(Ui.Button("Print…", (_, _) => Print()));
        actions.Children.Add(Ui.Button("Copy the words", (_, _) => { try { Clipboard.SetText(string.Join(' ', words)); } catch { } }));
        panel.Children.Add(actions);

        panel.Children.Add(Ui.Text("Prove the card is safe: type these three words from it.", "H2"));
        var ask = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        for (int j = 0; j < 3; j++)
        {
            var col = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            col.Children.Add(Ui.Text(L.F("Word {0}", _ask[j] + 1), "Small"));
            _answers[j] = new TextBox { Width = 150 };
            if (j == 0) _answers[j].Loaded += (s2, _) => ((TextBox)s2).Focus();
            col.Children.Add(_answers[j]);
            ask.Children.Add(col);
        }
        panel.Children.Add(ask);
        _wrong = Ui.Text("That does not match the card. Check the numbers and try again.", "Small"); _wrong.Foreground = Ui.B("BadInk"); _wrong.Visibility = Visibility.Collapsed;
        panel.Children.Add(_wrong);
        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        foot.Children.Add(Ui.Button(shell.Config.CardConfirmed ? "Close" : "Later", (_, _) => Close()));
        var confirm = Ui.Button("I have the card", (_, _) => Confirm(), primary: true); confirm.IsDefault = true;
        foot.Children.Add(confirm);
        panel.Children.Add(foot);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        MaxHeight = SystemParameters.WorkArea.Height - 40;
    }

    private void Confirm()
    {
        var words = _key.Words;
        bool ok = Enumerable.Range(0, 3).All(j => string.Equals(_answers[j].Text.Trim(), words[_ask[j]], StringComparison.OrdinalIgnoreCase));
        if (!ok) { _wrong.Visibility = Visibility.Visible; return; }
        _shell.ConfirmCard();
        Close();
    }

    private void Save()
    {
        var dlg = new SaveFileDialog { FileName = $"Stash recovery card {_key.Fingerprint}.txt", Filter = "Text|*.txt" };
        if (dlg.ShowDialog(this) == true) File.WriteAllText(dlg.FileName, RecoveryCard.Text(_key));
    }

    private void SaveImage()
    {
        var dlg = new SaveFileDialog { FileName = $"Stash recovery card {_key.Fingerprint}.png", Filter = "PNG image|*.png" };
        if (dlg.ShowDialog(this) == true) File.WriteAllBytes(dlg.FileName, CardImage.Render(_key));
    }

    private void Print()
    {
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        // The same card as the image, words and QR, scaled to the printable width.
        var card = CardImage.Build(_key, 1000);
        card.Measure(new Size(1000, double.PositiveInfinity));
        card.Arrange(new Rect(0, 0, 1000, card.DesiredSize.Height));
        double scale = Math.Min(dlg.PrintableAreaWidth / 1000, 1.0);
        card.LayoutTransform = new ScaleTransform(scale, scale);
        card.Measure(new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight));
        card.Arrange(new Rect(0, 0, card.DesiredSize.Width, card.DesiredSize.Height));
        dlg.PrintVisual(card, "Stash recovery card");
    }
}

/// <summary>Enter a card from another PC or Mac: the 24 words, or the QR payload text.</summary>
public sealed class EnterCardWindow : Window
{
    public EnterCardWindow(Shell shell)
    {
        Title = L.T("Enter a recovery card"); Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Ui.B("Ground");
        SourceInitialized += (_, _) => Theme.DecorateTitleBar(this);
        var panel = new StackPanel { Margin = new Thickness(28, 22, 28, 20) };
        panel.Children.Add(Ui.Text("Enter a recovery card", "H1"));
        panel.Children.Add(Ui.Text("The 24 words in order, any spacing or capitals, or the text behind the card's QR code. A wrong word is caught before anything happens.", "Lead"));
        var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
        panel.Children.Add(box);
        var problem = Ui.Text("", "Small", new Thickness(0, 6, 0, 0)); problem.Foreground = Ui.B("BadInk");
        panel.Children.Add(problem);
        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        foot.Children.Add(Ui.Button("Cancel", (_, _) => Close()));
        foot.Children.Add(Ui.Button("Use this key", (_, _) =>
        {
            var text = box.Text.Trim();
            var key = MasterKey.FromQrPayload(text);
            if (key is null) { try { key = MasterKey.FromWords(text); } catch (Mnemonic.WordsException ex) { problem.Text = ex.Message; return; } }
            shell.InstallKey(key);
            Close();
        }, primary: true));
        panel.Children.Add(foot);
        Content = panel;
    }
}

/// <summary>Pick files or folders from a snapshot and where they go.</summary>
public sealed class RestoreWindow : Window
{
    private readonly Shell _shell; private readonly SnapshotRow _snap;
    private readonly ListBox _list; private readonly TextBox _filter; private readonly TextBlock _target;
    private readonly List<Manifest.Entry> _all;
    private string? _targetPath;

    public RestoreWindow(Shell shell, SnapshotRow snap)
    {
        _shell = shell; _snap = snap;
        Title = L.T("Restore"); Width = 760; Height = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Ui.B("Ground");
        SourceInitialized += (_, _) => Theme.DecorateTitleBar(this);
        _all = shell.OpenSnapshot(snap).Files;
        var root = new Grid { Margin = new Thickness(28, 22, 28, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new StackPanel();
        head.Children.Add(Ui.Text(L.F("Restore from {0}", snap.When), "H1"));
        head.Children.Add(Ui.Text("Select files (Ctrl-click for several), or restore everything. Restored files go into a folder named after the original, in the place you choose. Nothing at the original location is touched.", "Lead"));
        Grid.SetRow(head, 0); root.Children.Add(head);
        _filter = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        _filter.TextChanged += (_, _) => Fill();
        Grid.SetRow(_filter, 1); root.Children.Add(_filter);
        _list = new ListBox { SelectionMode = SelectionMode.Extended, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Background = Ui.B("Card"), Foreground = Ui.B("Ink"), BorderBrush = Ui.B("Line") };
        ScrollViewer.SetCanContentScroll(_list, true);
        Grid.SetRow(_list, 2); root.Children.Add(_list);
        var foot = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition()); foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        _target = Ui.Text("Where: choose a folder", "Small");
        left.Children.Add(_target);
        left.Children.Add(Ui.Button("Choose folder…", (_, _) => { var d = new OpenFolderDialog { Title = L.T("Restore into") }; if (d.ShowDialog(this) == true) { _targetPath = d.FolderName; _target.Text = L.T("Where: ") + _targetPath; } }));
        foot.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        right.Children.Add(Ui.Button("Restore selected", (_, _) => Go(false)));
        right.Children.Add(Ui.Button("Restore everything", (_, _) => Go(true), primary: true));
        Grid.SetColumn(right, 1); foot.Children.Add(right);
        Grid.SetRow(foot, 3); root.Children.Add(foot);
        Content = root;
        Fill();
    }

    private void Fill()
    {
        var q = _filter.Text.Trim();
        _list.ItemsSource = _all.Where(f => q.Length == 0 || f.Path.Contains(q, StringComparison.OrdinalIgnoreCase)).Select(f => $"{f.Path}  ({Shell.Human(f.Size)})").ToList();
    }

    private async void Go(bool everything)
    {
        if (_targetPath is null) { MessageBox.Show(this, L.T("Choose where the files should go first."), "Stash for Windows"); return; }
        var only = everything ? new List<string>() : _list.SelectedItems.Cast<string>().Select(s => s[..s.LastIndexOf("  (", StringComparison.Ordinal)]).ToList();
        if (!everything && only.Count == 0) { MessageBox.Show(this, L.T("Select some files, or restore everything."), "Stash for Windows"); return; }
        IsEnabled = false;
        var outcome = await Task.Run(() => _shell.RestoreSnapshot(_snap, _targetPath, only));
        IsEnabled = true;
        MessageBox.Show(this, outcome.Message, "Stash for Windows", MessageBoxButton.OK, outcome.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (outcome.Ok) Close();
    }
}

public sealed class SettingsWindow : Window
{
    public SettingsWindow(Shell shell)
    {
        Title = L.T("Settings"); Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Ui.B("Ground");
        SourceInitialized += (_, _) => Theme.DecorateTitleBar(this);
        var cfg = shell.Config;
        var panel = new StackPanel { Margin = new Thickness(28, 22, 28, 20) };
        panel.Children.Add(Ui.Text("Settings", "H1"));

        panel.Children.Add(Ui.Text("When to back up", "H2", new Thickness(0, 8, 0, 4)));
        var schedule = new ComboBox { Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var (v, label) in new[] { ("off", "Only when I press Back Up Now"), ("hourly", "Every hour"), ("daily", "Once a day, at 20:00") }) schedule.Items.Add(new ComboBoxItem { Content = L.T(label), Tag = v });
        schedule.SelectedIndex = cfg.Schedule switch { "hourly" => 1, "daily" => 2, _ => 0 };
        panel.Children.Add(schedule);
        panel.Children.Add(Ui.Text("A Task Scheduler task for this user runs the backup whether or not the window is open, only to destinations that are reachable, uploading only what changed.", "Small", new Thickness(0, 4, 0, 0)));

        panel.Children.Add(Ui.Text("How many snapshots to keep", "H2", new Thickness(0, 14, 0, 4)));
        var last = new RadioButton { Content = L.T("The newest"), IsChecked = cfg.Retention != "thin", Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center, Foreground = Ui.B("Ink") };
        var keep = new TextBox { Width = 60, Text = cfg.KeepSnapshots.ToString() };
        var thin = new RadioButton { Content = L.T("Thin out over time: every one from the last week, one a day for a month, one a week for a year, one a month after that"), IsChecked = cfg.Retention == "thin", Margin = new Thickness(0, 6, 0, 0), Foreground = Ui.B("Ink") };
        var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(last); row.Children.Add(keep); row.Children.Add(Ui.Text(" snapshots", null, new Thickness(6, 0, 0, 0)));
        panel.Children.Add(row); panel.Children.Add(thin);
        panel.Children.Add(Ui.Text("Older snapshots and the pieces only they used are deleted after each backup, so the destination stays a sensible size.", "Small", new Thickness(0, 4, 0, 0)));

        panel.Children.Add(Ui.Text("Skip", "H2", new Thickness(0, 14, 0, 4)));
        var excludes = new TextBox { Text = string.Join(", ", cfg.Excludes) };
        panel.Children.Add(excludes);
        var maxRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        maxRow.Children.Add(Ui.Text("Skip files larger than ", null));
        var max = new TextBox { Width = 70, Text = cfg.MaxFileMB > 0 ? cfg.MaxFileMB.ToString() : "" };
        maxRow.Children.Add(max); maxRow.Children.Add(Ui.Text(" MB (empty: no limit)", null, new Thickness(6, 0, 0, 0)));
        panel.Children.Add(maxRow);
        panel.Children.Add(Ui.Text("Name patterns, comma separated. Files and folders whose name matches are left out. Virtual machine disks are skipped by default: they are huge and change constantly.", "Small", new Thickness(0, 4, 0, 0)));

        var readAll = new CheckBox { Content = L.T("Read every file every time, instead of trusting size and modified time for unchanged files (slower, thorough)"), IsChecked = cfg.ReadAll, Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(readAll);
        var weekly = new CheckBox { Content = L.T("Check the backup once a week: open every piece and restore one random file"), IsChecked = cfg.WeeklyVerify, Margin = new Thickness(0, 14, 0, 0) };
        panel.Children.Add(weekly);
        panel.Children.Add(Ui.Text("The app", "H2", new Thickness(0, 14, 0, 4)));
        var tray = new CheckBox { Content = L.T("Stay in the tray when the window closes, with the last and next backup"), IsChecked = cfg.Tray };
        var login = new CheckBox { Content = L.T("Open at sign-in, in the tray"), IsChecked = Startup.IsEnabled(), Margin = new Thickness(0, 6, 0, 0) };
        var updates = new CheckBox { Content = L.T("Check GitHub once a day for a new version"), IsChecked = cfg.UpdateCheck, Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(tray); panel.Children.Add(login); panel.Children.Add(updates);
        panel.Children.Add(Ui.Text("The update check is one request for a version number, with no identifiers, and the only thing this app ever sends anywhere other than your destinations. A new version is offered as a link; nothing installs by itself.", "Small", new Thickness(0, 4, 0, 0)));

        var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var note = Ui.Text("", "Small"); note.HorizontalAlignment = HorizontalAlignment.Left;
        foot.Children.Add(Ui.Button("Cancel", (_, _) => Close()));
        foot.Children.Add(Ui.Button("Save", (_, _) =>
        {
            cfg.Retention = thin.IsChecked == true ? "thin" : "last";
            if (int.TryParse(keep.Text, out var n) && n > 0) cfg.KeepSnapshots = n;
            cfg.Excludes = excludes.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            cfg.MaxFileMB = int.TryParse(max.Text, out var mb) && mb > 0 ? mb : 0;
            cfg.WeeklyVerify = weekly.IsChecked == true;
            cfg.ReadAll = readAll.IsChecked == true;
            cfg.Tray = tray.IsChecked == true;
            cfg.UpdateCheck = updates.IsChecked == true;
            Startup.Set(login.IsChecked == true); cfg.OpenAtSignIn = login.IsChecked == true;
            cfg.Save();
            var chosen = (string)((ComboBoxItem)schedule.SelectedItem).Tag;
            var msg = shell.ApplySchedule(chosen);
            shell.LastLine = msg;
            Close();
        }, primary: true));
        panel.Children.Add(foot);
        Content = panel;
    }
}

public sealed class AboutDialog : Window
{
    public AboutDialog()
    {
        Title = L.T("About Stash for Windows"); Width = 520; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Ui.B("Ground");
        SourceInitialized += (_, _) => Theme.DecorateTitleBar(this);
        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 20) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        head.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/stash-256.png")), Width = 64, Height = 64, Margin = new Thickness(0, 0, 16, 0) });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Stash for Windows", FontSize = 22, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(Ui.Text(L.F("Version {0}, MIT licence", Cli.Version), "Small"));
        head.Children.Add(titles);
        panel.Children.Add(head);
        foreach (var line in new[]
        {
            "Encrypted backup of the folders that matter, into the storage you already have. The provider only ever holds scrambled blobs. You hold the key, as 24 words on a card.",
            "No password, no recovery, no server. Lose the card and nobody can read the backup, including you. Same format as Stash for Mac: one card restores on either.",
            "Free and open source. No account, no update check, no analytics. Built by Keith Adler and signed by its author, which is why Windows asked before the first open.",
        }) panel.Children.Add(Ui.Text(line, null, new Thickness(0, 0, 0, 8)));
        panel.Children.Add(Ui.Text("Settings and the wrapped key: " + Paths.Home, "Mono", new Thickness(0, 4, 0, 14)));
        var links = new TextBlock();
        var src = new Hyperlink(new Run(L.T("Source and releases"))) { NavigateUri = new Uri("https://github.com/keithadler/stashwin") };
        var more = new Hyperlink(new Run(L.T("More from the same maker"))) { NavigateUri = new Uri("https://keithadler.github.io/") };
        foreach (var h in new[] { src, more }) h.RequestNavigate += (_, ev) => { try { Process.Start(new ProcessStartInfo(ev.Uri.ToString()) { UseShellExecute = true }); } catch { } };
        links.Inlines.Add(src); links.Inlines.Add(new Run("    ")); links.Inlines.Add(more);
        panel.Children.Add(links);
        var close = Ui.Button("Close", (_, _) => Close()); close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(0, 18, 0, 0); close.IsDefault = true; close.IsCancel = true;
        panel.Children.Add(close);
        Content = panel;
    }
}
