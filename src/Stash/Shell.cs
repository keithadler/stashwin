using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Stash.Core;

namespace Stash;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(name); return true;
    }
}

public sealed record FolderRow(string Path, string Name, bool Exists, string Note);

public sealed class DestinationRow
{
    public DestinationRow(string path, string provider, bool reachable, long size, int snapshots, DateTimeOffset? last)
    { Path = path; Provider = provider; Reachable = reachable; Size = size; Snapshots = snapshots; Last = last; }
    public string Path { get; }
    public string Provider { get; }
    public bool Reachable { get; }
    public long Size { get; }
    public int Snapshots { get; }
    public DateTimeOffset? Last { get; }
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
    public string Detail => !Reachable ? L.T("Not reachable right now; skipped until it is back.") : Snapshots == 0 ? L.T("No backup here yet.") : L.F(Snapshots == 1 ? "{0} snapshot, {1} used, last {2}." : "{0} snapshots, {1} used, last {2}.", Snapshots, Shell.Human(Size), Last?.ToLocalTime().ToString("d MMM yyyy HH:mm") ?? "");
    public string Badge => !Reachable ? "away" : Snapshots == 0 ? "empty" : "ok";
}

public sealed class SnapshotRow
{
    public SnapshotRow(string destination, SnapshotInfo info, long frees) { Destination = destination; Info = info; Frees = frees; }
    public string Destination { get; }
    public SnapshotInfo Info { get; }
    public long Frees { get; }
    public string FileName => Info.FileName;
    public string When => Info.CreatedAt.ToLocalTime().ToString("d MMMM yyyy, HH:mm");
    public string Line => L.F("{0} files, {1}, from {2}", Info.Files, Shell.Human(Info.Bytes), Info.Host) + (Info.Placeholders > 0 ? L.F(", {0} cloud placeholders listed", Info.Placeholders) : "") + L.F(". Frees {0}.", Shell.Human(Frees));
    public string DestinationName => Path.GetFileName(Destination.TrimEnd('\\', '/'));
}

/// <summary>Everything the window shows and does, kept apart from the window so screenshots render from sample data.</summary>
public sealed class Shell : Observable
{
    public Config Config { get; private set; } = Config.Load();
    public MasterKey? Key { get; private set; }
    public ObservableCollection<FolderRow> Sources { get; } = new();
    public ObservableCollection<DestinationRow> Destinations { get; } = new();
    public ObservableCollection<SnapshotRow> Snapshots { get; } = new();
    public ObservableCollection<Providers.Found> Suggested { get; } = new();

    private bool _busy; private string _lastLine = "", _busyWhat = ""; private double _progress;
    public CancellationTokenSource? Cancel { get; set; }
    public void RequestCancel() { try { Cancel?.Cancel(); } catch { } }
    public bool Busy { get => _busy; set { if (Set(ref _busy, value)) Raise(nameof(CanAct)); } }
    public string BusyWhat { get => _busyWhat; set => Set(ref _busyWhat, value); }
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public string LastLine { get => _lastLine; set => Set(ref _lastLine, value); }
    private string _updateLine = "";
    public string UpdateLine { get => _updateLine; set => Set(ref _updateLine, value); }
    public string? UpdatePage { get; set; }

    public bool HasKey => Key is not null;
    public string KeyLine => Key is null ? L.T("No key on this PC yet.") : L.F(Config.CardConfirmed ? "Key {0} on this PC, card confirmed." : "Key {0} on this PC, card not yet confirmed.", Key.Fingerprint);
    public bool CanAct => !Busy && HasKey && Sources.Count > 0 && Destinations.Any(d => d.Reachable);
    public string ReadyLine => Key is null ? L.T("Step 1: make a key, or enter the card from another PC or Mac.")
        : Sources.Count == 0 ? L.T("Step 2: add a folder to protect.")
        : Destinations.Count == 0 ? L.T("Step 3: choose where it goes.")
        : Destinations.Count == 1 ? L.T("Two destinations are safer: an account can be lost, a disk can fail.")
        : Config.LastBackup is { } lb ? L.F("Last backup {0}. {1}", lb.ToLocalTime().ToString("d MMMM yyyy, HH:mm"), ScheduleLine) : L.T("Ready. Press Back Up Now.");
    public string ScheduleLine => L.T(Config.Schedule switch { "hourly" => "Runs every hour.", "daily" => "Runs once a day.", _ => "Runs only when you press Back Up Now." });
    public string VerifyLine => Config.LastVerify is { } lv ? L.F("Last checked {0}.", lv.ToLocalTime().ToString("d MMMM yyyy")) : L.T("Never checked yet: Verify restores one random file to prove the whole path works.");

    public static string Human(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1_048_576 ? $"{bytes / 1024.0:0.#} KB" : bytes < 1_073_741_824 ? $"{bytes / 1_048_576.0:0.#} MB" : $"{bytes / 1_073_741_824.0:0.##} GB";

    public void Load()
    {
        Config = Config.Load();
        Key = KeyStore.Load();
        Sources.Clear();
        foreach (var s in Config.Sources) Sources.Add(new FolderRow(s, Path.GetFileName(s.TrimEnd('\\', '/')), Directory.Exists(s), Directory.Exists(s) ? "" : L.T("Not found right now.")));
        Destinations.Clear();
        Snapshots.Clear();
        var providers = Providers.Detect();
        foreach (var d in Config.Destinations)
        {
            bool reachable = Directory.Exists(d);
            var provider = providers.FirstOrDefault(p => Config.IsInside(d, p.Path))?.Name ?? L.T(d.StartsWith(@"\\") ? "Network" : "Folder");
            int count = 0; long size = 0; DateTimeOffset? last = null;
            if (reachable && Key is not null)
            {
                var store = new FolderStore(d, Key);
                var snaps = Restore.Snapshots(store, Key);
                var frees = Prune.UniqueSizes(store, Key);
                count = snaps.Count; size = store.TotalSize(); last = snaps.FirstOrDefault()?.CreatedAt;
                foreach (var s in snaps) Snapshots.Add(new SnapshotRow(d, s, frees.GetValueOrDefault(s.FileName)));
            }
            Destinations.Add(new DestinationRow(d, provider, reachable, size, count, last));
        }
        Suggested.Clear();
        foreach (var p in providers.Where(p => !Config.Destinations.Any(d => Config.IsInside(d, p.Path) || Config.IsInside(p.Path, d)))) Suggested.Add(p);
        Raise(nameof(HasKey)); Raise(nameof(KeyLine)); Raise(nameof(CanAct)); Raise(nameof(ReadyLine)); Raise(nameof(VerifyLine)); Raise(nameof(ScheduleLine));
    }

    // ---- Key ----
    public MasterKey MakeKey() { var k = MasterKey.Random(); KeyStore.Save(k); Config.CardConfirmed = false; Config.Save(); Load(); return k; }
    public void InstallKey(MasterKey k) { KeyStore.Save(k); Config.CardConfirmed = true; Config.Save(); Load(); }
    public void ConfirmCard() { Config.CardConfirmed = true; Config.Save(); Load(); }
    public void ForgetKey() { KeyStore.Delete(); Load(); }

    // ---- Folders and destinations ----
    public string? AddSource(string path)
    {
        if (Config.ObjectionToSource(path) is { } why) return why;
        if (!Config.Sources.Contains(path, StringComparer.OrdinalIgnoreCase)) Config.Sources.Add(path);
        Config.Save(); Load(); return null;
    }
    public void RemoveSource(string path) { Config.Sources.RemoveAll(s => string.Equals(s, path, StringComparison.OrdinalIgnoreCase)); Config.Save(); Load(); }
    public string? AddDestination(string path)
    {
        if (Config.ObjectionToDestination(path) is { } why) return why;
        if (!Config.Destinations.Contains(path, StringComparer.OrdinalIgnoreCase)) Config.Destinations.Add(path);
        Config.Save(); Load(); return null;
    }
    public void RemoveDestination(string path) { Config.Destinations.RemoveAll(s => string.Equals(s, path, StringComparison.OrdinalIgnoreCase)); Config.Save(); Load(); }

    // ---- Work ----
    public sealed record Outcome(bool Ok, string Message);

    private static string Rate(long bytes, System.Diagnostics.Stopwatch sw) => sw.Elapsed.TotalSeconds < 0.5 ? "" : $", {Human((long)(bytes / sw.Elapsed.TotalSeconds))}/s";

    public Outcome BackUp(Action<string, double>? progress = null, CancellationToken cancel = default)
    {
        if (Key is null) return new(false, L.T("No key."));
        var lines = new List<string>(); bool ok = true; bool cancelled = false;
        foreach (var d in Config.Destinations)
        {
            var name = Path.GetFileName(d.TrimEnd('\\'));
            if (cancel.IsCancellationRequested) { cancelled = true; break; }
            if (!Directory.Exists(d)) { lines.Add(L.F("{0}: not reachable, skipped.", name)); continue; }
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = Backup.Run(Config.Sources, d, Key, Config.Rules, (done, total, bytes) => progress?.Invoke(L.F("{0}: {1} of {2} files", name, done, total) + Rate(bytes, sw), total == 0 ? 0 : (double)done / total), cancel);
                if (r.Cancelled) { cancelled = true; lines.Add(L.F("{0}: stopped; pieces already uploaded will be reused next time.", name)); break; }
                var p = Prune.Run(new FolderStore(d, Key), Key, Config.KeepSnapshots, Config.Policy);
                lines.Add(L.F("{0}: {1} files, uploaded {2}, reused {3} pieces", name, r.Files, Human(r.NewBytes), r.ReusedChunks) + (r.Unchanged > 0 ? L.F(", {0} unchanged and not reread", r.Unchanged) : "") + (r.SkippedPlaceholders > 0 ? L.F(", {0} cloud placeholders listed not downloaded", r.SkippedPlaceholders) : "") + (r.Unreadable.Count > 0 ? L.F(", {0} unreadable", r.Unreadable.Count) : "") + (p.BytesFreed > 0 ? L.F(", reclaimed {0}", Human(p.BytesFreed)) : "") + L.F(" in {0:0.0} s.", sw.Elapsed.TotalSeconds));
            }
            catch (Exception ex) { ok = false; lines.Add($"{name}: {ex.Message}"); }
        }
        if (!cancelled) { Config.LastBackup = DateTimeOffset.Now; Config.Save(); }
        Paths.Log_("backup (window): " + string.Join(" ", lines));
        return new(ok && !cancelled, string.Join(" ", lines));
    }

    public Outcome Verify(Action<string, double>? progress = null, CancellationToken cancel = default)
    {
        if (Key is null) return new(false, L.T("No key."));
        var lines = new List<string>(); bool ok = true;
        foreach (var d in Config.Destinations.Where(Directory.Exists))
        {
            var name = Path.GetFileName(d.TrimEnd('\\'));
            var v = Restore.Verify(new FolderStore(d, Key), Key, (done, total) => progress?.Invoke(L.F("{0}: checking {1} of {2} pieces", name, done, total), total == 0 ? 0 : (double)done / total), cancel);
            if (v.Cancelled) { lines.Add(L.F("{0}: stopped.", name)); ok = false; break; }
            if (!v.Clean) ok = false;
            lines.Add(L.F("{0}: {1} pieces open", name, v.ChunksChecked) + (v.ChunksInCloudOnly > 0 ? L.F(", {0} in the cloud only", v.ChunksInCloudOnly) : "") + (v.ChunksBad.Count > 0 ? L.F(", {0} damaged", v.ChunksBad.Count) : "") + (v.ChunksMissing.Count > 0 ? L.F(", {0} missing", v.ChunksMissing.Count) : "") + (v.SampleFile is not null ? (v.SampleOk == true ? (v.SampleCompared ? L.F("; {0} restored and identical to the original", Path.GetFileName(v.SampleFile)) : L.F("; {0} restored and matched", Path.GetFileName(v.SampleFile))) : L.F("; {0} did NOT restore correctly", Path.GetFileName(v.SampleFile))) : "") + ".");
        }
        if (ok) { Config.LastVerify = DateTimeOffset.Now; Config.Save(); }
        return new(ok, lines.Count == 0 ? L.T("No destination reachable.") : string.Join(" ", lines));
    }

    public Outcome RestoreSnapshot(SnapshotRow snap, string target, IReadOnlyList<string> only, Action<string, double>? progress = null, CancellationToken cancel = default)
    {
        if (Key is null) return new(false, L.T("No key."));
        var r = Restore.Run(snap.FileName, new FolderStore(snap.Destination, Key), Key, target, only, (done, total) => progress?.Invoke(L.F("restoring {0} of {1} files", done, total), total == 0 ? 0 : (double)done / total), cancel);
        return new(r.Failed.Count == 0 && !r.Cancelled, L.F("Restored {0} files ({1}) into {2}.", r.Restored, Human(r.Bytes), target) + (r.Cancelled ? L.T(" Stopped before the end.") : "") + (r.Failed.Count > 0 ? L.F(" {0} failed: {1}", r.Failed.Count, string.Join("; ", r.Failed.Take(3))) : ""));
    }

    public Outcome ForgetSnapshot(SnapshotRow snap)
    {
        if (Key is null) return new(false, "No key.");
        var p = Prune.Delete(snap.FileName, new FolderStore(snap.Destination, Key), Key);
        return new(true, $"Removed the snapshot and freed {Human(p.BytesFreed)}.");
    }

    public Manifest OpenSnapshot(SnapshotRow snap) => Restore.Open(snap.FileName, new FolderStore(snap.Destination, Key!), Key!);

    public string ApplySchedule(string schedule)
    {
        Config.Schedule = schedule; Config.Save();
        var (_, msg) = Schedule.Install(schedule);
        Raise(nameof(ScheduleLine)); Raise(nameof(ReadyLine));
        return msg;
    }
}
