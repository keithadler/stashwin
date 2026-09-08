using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using Stash.Core;

namespace Stash;

public partial class MainWindow : Window
{
    public Shell Shell { get; }
    public bool TrayEnabled { get; set; }
    public bool ReallyClose { get; set; }

    public void BackUpFromTray() => BackUp_Click(this, new RoutedEventArgs());
    public void VerifyFromTray() => Verify_Click(this, new RoutedEventArgs());

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // With the tray on, closing the window keeps the app in the tray, like the Mac's menu bar item. Quit is in the tray menu.
        if (TrayEnabled && !ReallyClose) { e.Cancel = true; Hide(); return; }
        base.OnClosing(e);
    }

    public MainWindow(Shell shell)
    {
        Shell = shell;
        InitializeComponent();
        L.Localize(this);
        DataContext = Shell;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 24);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 24);
        MinWidth = Math.Min(MinWidth, Width); MinHeight = Math.Min(MinHeight, Height);
        SourceInitialized += (_, _) => Theme.DecorateTitleBar(this);
        Loaded += (_, _) => { if (!Shell.HasKey && Shell.Sources.Count == 0) Shell.Load(); Refresh(); };
        Shell.Sources.CollectionChanged += (_, _) => Refresh();
        Shell.Destinations.CollectionChanged += (_, _) => Refresh();
        Shell.Snapshots.CollectionChanged += (_, _) => Refresh();
        // Keyboard: Ctrl+K key or card, Ctrl+O add folder, Ctrl+D add destination, Ctrl+Enter back up, Ctrl+T verify, Ctrl+, settings.
        PreviewKeyDown += (_, e) =>
        {
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
            switch (e.Key)
            {
                case System.Windows.Input.Key.K: if (Shell.HasKey) ShowCard_Click(this, new RoutedEventArgs()); else MakeKey_Click(this, new RoutedEventArgs()); e.Handled = true; break;
                case System.Windows.Input.Key.O: AddSource_Click(this, new RoutedEventArgs()); e.Handled = true; break;
                case System.Windows.Input.Key.D: AddDestination_Click(this, new RoutedEventArgs()); e.Handled = true; break;
                case System.Windows.Input.Key.Enter: if (Shell.CanAct) BackUp_Click(this, new RoutedEventArgs()); e.Handled = true; break;
                case System.Windows.Input.Key.T: if (Shell.CanAct) Verify_Click(this, new RoutedEventArgs()); e.Handled = true; break;
                case System.Windows.Input.Key.OemComma: Settings_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            }
        };
    }

    private void Refresh()
    {
        NoSources.Visibility = Shell.Sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OneDestination.Visibility = Shell.Destinations.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
        NoSnapshots.Visibility = Shell.Snapshots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string? PickFolder(Window owner, string title)
    {
        var dlg = new OpenFolderDialog { Title = L.T(title), Multiselect = false };
        return dlg.ShowDialog(owner) == true ? dlg.FolderName : null;
    }

    // ---- Key ----
    private void MakeKey_Click(object sender, RoutedEventArgs e)
    {
        var k = Shell.MakeKey();
        new RecoveryCardWindow(Shell, k) { Owner = this }.ShowDialog();
        Refresh();
    }
    private void EnterCard_Click(object sender, RoutedEventArgs e) { new EnterCardWindow(Shell) { Owner = this }.ShowDialog(); Refresh(); }
    private void ShowCard_Click(object sender, RoutedEventArgs e) { if (Shell.Key is { } k) new RecoveryCardWindow(Shell, k) { Owner = this }.ShowDialog(); }
    private void ForgetKey_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(this, L.T("Remove the key from this PC? The backups stay where they are, encrypted, and only the card can read them again. Make sure you have the card."), "Stash for Windows", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r == MessageBoxResult.OK) { Shell.ForgetKey(); Refresh(); }
    }

    // ---- Folders and destinations ----
    private void AddSource_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder(this, "Folder to protect") is not { } f) return;
        if (Shell.AddSource(f) is { } why) MessageBox.Show(this, why, "Stash for Windows");
    }
    private void RemoveSource_Click(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is string p) Shell.RemoveSource(p); }
    private void AddDestination_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder(this, "Where the encrypted backup goes") is not { } f) return;
        if (Shell.AddDestination(f) is { } why) MessageBox.Show(this, why, "Stash for Windows");
    }
    private void UseSuggested_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string p) return;
        if (Shell.AddDestination(p) is { } why) MessageBox.Show(this, why, "Stash for Windows");
    }
    private void RemoveDestination_Click(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is string p) Shell.RemoveDestination(p); }

    // ---- Work ----
    private async Task RunBusy(string what, Func<Action<string, double>, CancellationToken, Shell.Outcome> work)
    {
        Shell.Busy = true; Shell.BusyWhat = what; Shell.Progress = 0;
        Shell.Cancel = new CancellationTokenSource();
        var token = Shell.Cancel.Token;
        var outcome = await Task.Run(() => work((line, p) => Dispatcher.Invoke(() => { Shell.BusyWhat = line; Shell.Progress = p; }), token));
        Shell.Cancel = null;
        Shell.Busy = false;
        Shell.Load();
        Shell.LastLine = outcome.Message;
        Refresh();
        (Application.Current as App)?.RefreshTray();
        if (!outcome.Ok) MessageBox.Show(this, outcome.Message, "Stash for Windows", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void BackUp_Click(object sender, RoutedEventArgs e)
    {
        if (!Shell.Config.CardConfirmed && Shell.Key is { } k)
        {
            var r = MessageBox.Show(this, L.T("You have not confirmed the recovery card yet. Without it the backup can never be read. Show the card now?"), "Stash for Windows", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes) { new RecoveryCardWindow(Shell, k) { Owner = this }.ShowDialog(); if (!Shell.Config.CardConfirmed) return; }
        }
        await RunBusy(L.T("Backing up"), (p, c) => Shell.BackUp(p, c));
    }
    private async void Verify_Click(object sender, RoutedEventArgs e) => await RunBusy(L.T("Checking the backup"), (p, c) => Shell.Verify(p, c));
    private void CancelBusy_Click(object sender, RoutedEventArgs e) { Shell.RequestCancel(); Shell.BusyWhat = L.T("Stopping after the current file…"); }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SnapshotRow snap) return;
        new RestoreWindow(Shell, snap) { Owner = this }.ShowDialog();
        Shell.Load(); Refresh();
    }
    private void ForgetSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SnapshotRow snap) return;
        var r = MessageBox.Show(this, L.F("Delete the snapshot from {0}? Files that exist only in this snapshot are gone for good; files that also exist in other snapshots are unaffected. Frees {1}.", snap.When, Shell.Human(snap.Frees)), "Stash for Windows", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r != MessageBoxResult.OK) return;
        var outcome = Shell.ForgetSnapshot(snap);
        Shell.Load(); Shell.LastLine = outcome.Message; Refresh();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) { new SettingsWindow(Shell) { Owner = this }.ShowDialog(); Shell.Load(); Refresh(); TrayEnabled = Shell.Config.Tray; (Application.Current as App)?.RefreshTray(); }
    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (Shell.UpdatePage is { } p) { try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); } catch { } }
    }
    private void SkipUpdate_Click(object sender, RoutedEventArgs e)
    {
        var c = Shell.Config; c.SkippedVersion = Shell.UpdateLine.Replace("Version ", "").Replace(" is available.", ""); c.Save(); Shell.UpdateLine = "";
    }
    private void About_Click(object sender, RoutedEventArgs e) => new AboutDialog { Owner = this }.ShowDialog();
    private void Help_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "Stash for Windows Help.html");
            using var res = (L.Spanish ? typeof(MainWindow).Assembly.GetManifestResourceStream("Help.es.html") : null) ?? typeof(MainWindow).Assembly.GetManifestResourceStream("Help.html") ?? throw new InvalidOperationException("Help is not bundled");
            using (var file = File.Create(path)) res.CopyTo(file);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(this, L.T("Could not open Help: ") + ex.Message, "Stash for Windows"); }
    }
}

public sealed class InverseVisibility : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class NotEmptyToVisibility : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch { string s => s.Length > 0 ? Visibility.Visible : Visibility.Collapsed, int n => n > 0 ? Visibility.Visible : Visibility.Collapsed, _ => Visibility.Collapsed };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class BadgeBrush : IValueConverter
{
    public string Mode { get; set; } = "Bg";
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var key = (value as string) switch { "ok" => "Good", "away" => "Bad", _ => "Warn" };
        return Application.Current.Resources[key + Mode] as Brush ?? Brushes.Transparent;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
