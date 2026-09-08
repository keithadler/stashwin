using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Stash.Core;

namespace Stash;

/// <summary>The command line. Exit codes: 0 fine, 1 something to look at, 2 problem, 64 usage.</summary>
public static class Cli
{
    public const string Version = "1.0.0";

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);
    [DllImport("kernel32.dll")] private static extern bool AllocConsole();

    public static int Run(string[] args)
    {
        if (!AttachConsole(-1)) AllocConsole();
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
        try { return Dispatch(args, stdout, stderr); }
        finally { stdout.WriteLine(); }
    }

    private const string Help = """
        Stash for Windows: encrypted backup into storage you already have (command-line twin)

          stash key new [--json]            make a key on this PC and print the 24 words
          stash key show [--json]           print this PC's words and fingerprint
          stash key card <file.txt|.png>    write the recovery card as text, or as an image with the QR code
          stash key restore "<24 words>"    install a key from a card (or a stashmac:key/v1/... QR payload)
          stash key forget                  remove the key from this PC
          stash add <folder>                back this folder up (repeatable); stash remove <folder>
          stash dest <folder>               a destination: any folder a provider syncs, a disk, a NAS; stash undest <folder>
          stash providers                   the OneDrive, Google Drive, Dropbox and iCloud folders found on this PC
          stash exclude [add|remove <pattern>] [--max-mb N]   name patterns to skip (*.tmp, Cache) and a size cap
          stash backup [--json] [--scheduled] [--read-all]   back up now; --read-all rereads unchanged files instead of trusting timestamps
          stash snapshots [--json]          list snapshots with what deleting each would free
          stash forget <snapshot>           delete one snapshot and the pieces only it used
          stash restore <snapshot|latest> <target folder> [--only <path>[,<path>...]] [--dest <folder>]
          stash verify [--json]             open every chunk of the latest snapshot; restore one random file
          stash prune [--keep N | --thin] [--json]   apply the retention policy now (also runs after each backup)
          stash schedule off|hourly|daily   register the backup with Task Scheduler for this user; --when-signed-out runs it
                                            with nobody signed in (no password stored; the key is then protected for this PC,
                                            readable by an administrator here), --signed-in-only goes back
          stash seal <in> <out>             encrypt one file as a chunk (proof of the format); stash open <in> <out>
          stash update                      ask GitHub whether a newer version exists (the only network call, off in Settings)
          stash status [--json]
          stash screenshots <dir> [--announce] [--dark] [--es]
          stash selftest [filter] [--list]
          stash help | version

        Destinations are folders on purpose: OneDrive, Google Drive, Dropbox and iCloud all appear as folders
        through their own apps, so no API keys or accounts are needed. Everything written there is encrypted;
        the 24-word card is the only way to read it back. Backups made here restore with Stash for Mac, and the
        other way round: same format, same card.
        Exit codes: 0 fine, 1 something to look at, 2 problem, 64 usage. STASH_HOME moves the settings folder.
        """;

    private static bool Flag(string[] a, string name) => a.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
    private static string? Opt(string[] a, string name) { int i = Array.FindIndex(a, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
    private static List<string> Positional(string[] a)
    {
        var list = new List<string>();
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].StartsWith("--")) { if (a[i] is "--only" or "--dest" or "--max-mb" or "--keep") i++; continue; }
            list.Add(a[i]);
        }
        return list;
    }
    private static string J(object o) => JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true });
    private static string Human(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1_048_576 ? $"{bytes / 1024.0:0.#} KB" : bytes < 1_073_741_824 ? $"{bytes / 1_048_576.0:0.#} MB" : $"{bytes / 1_073_741_824.0:0.##} GB";

    public static int Dispatch(string[] args, TextWriter o, TextWriter err)
    {
        var verb = args.Length == 0 ? "help" : args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        bool json = Flag(args, "--json");
        var pos = Positional(rest);
        var cfg = Config.Load();
        try
        {
            switch (verb)
            {
                case "help": case "--help": case "-h": o.WriteLine(Help); return 0;
                case "version": case "--version": o.WriteLine(json ? J(new { version = Version }) : Version); return 0;
                case "selftest": return SelfTest.Run(o, pos.FirstOrDefault(), Flag(rest, "--list")) == 0 ? 0 : 1;
                case "screenshots": return Screenshots.Render(pos.FirstOrDefault() ?? "screenshots", o, Flag(rest, "--announce"), Flag(rest, "--dark"), Flag(rest, "--es"));

                case "key":
                {
                    var sub = pos.FirstOrDefault() ?? "";
                    switch (sub)
                    {
                        case "new":
                            if (KeyStore.Load() is not null) { err.WriteLine("This PC already has a key. \"stash key forget\" first if you really want a new one; the old backups need the old card."); return 2; }
                            var k = MasterKey.Random(); KeyStore.Save(k, cfg.WhenSignedOut && cfg.Schedule != "off");
                            if (json) o.WriteLine(J(new { fingerprint = k.Fingerprint, words = k.Words }));
                            else { o.WriteLine($"New key {k.Fingerprint}. Write these 24 words on a card and keep it somewhere safe; nothing else can read the backup.\n"); o.WriteLine(Mnemonic.Card(k.Words)); }
                            return 0;
                        case "show":
                            if (KeyStore.Load() is not { } ks) { err.WriteLine("no key on this PC (stash key new)"); return 2; }
                            if (json) o.WriteLine(J(new { fingerprint = ks.Fingerprint, words = ks.Words })); else { o.WriteLine($"key {ks.Fingerprint}\n"); o.WriteLine(Mnemonic.Card(ks.Words)); }
                            return 0;
                        case "card":
                            if (KeyStore.Load() is not { } kc) { err.WriteLine("no key on this PC"); return 2; }
                            if (pos.Count < 2) { err.WriteLine("key card <file.txt|file.png>"); return 64; }
                            if (pos[1].EndsWith(".png", StringComparison.OrdinalIgnoreCase)) File.WriteAllBytes(pos[1], RecoveryCard.Png(kc));
                            else File.WriteAllText(pos[1], RecoveryCard.Text(kc));
                            o.WriteLine($"wrote {pos[1]}"); return 0;
                        case "restore":
                            if (pos.Count < 2) { err.WriteLine("key restore \"<24 words>\""); return 64; }
                            var text = string.Join(' ', pos.Skip(1));
                            MasterKey? kr = MasterKey.FromQrPayload(text.Trim());
                            if (kr is null) { try { kr = MasterKey.FromWords(text); } catch (Mnemonic.WordsException ex) { err.WriteLine(ex.Message); return 2; } }
                            KeyStore.Save(kr, cfg.WhenSignedOut && cfg.Schedule != "off"); o.WriteLine($"key {kr.Fingerprint} installed"); return 0;
                        case "forget":
                            KeyStore.Delete(); o.WriteLine("key removed from this PC; the card is now the only copy"); return 0;
                        default: err.WriteLine("key new | show | card | restore | forget"); return 64;
                    }
                }

                case "add": case "remove":
                {
                    if (pos.Count == 0) { err.WriteLine($"{verb} <folder>"); return 64; }
                    var f = Path.GetFullPath(pos[0]);
                    if (verb == "add")
                    {
                        if (!Directory.Exists(f)) { err.WriteLine($"{f} is not a folder"); return 2; }
                        if (cfg.ObjectionToSource(f) is { } why) { err.WriteLine(why); return 2; }
                        if (!cfg.Sources.Contains(f, StringComparer.OrdinalIgnoreCase)) cfg.Sources.Add(f);
                        o.WriteLine($"backing up {f}");
                    }
                    else { cfg.Sources.RemoveAll(s => string.Equals(s, f, StringComparison.OrdinalIgnoreCase)); o.WriteLine($"no longer backing up {f}"); }
                    cfg.Save(); return 0;
                }
                case "dest": case "undest":
                {
                    if (pos.Count == 0) { err.WriteLine($"{verb} <folder>"); return 64; }
                    var f = Path.GetFullPath(pos[0]);
                    if (verb == "dest")
                    {
                        if (!Directory.Exists(f)) { err.WriteLine($"{f} is not a folder"); return 2; }
                        if (cfg.ObjectionToDestination(f) is { } why) { err.WriteLine(why); return 2; }
                        if (!cfg.Destinations.Contains(f, StringComparer.OrdinalIgnoreCase)) cfg.Destinations.Add(f);
                        o.WriteLine($"backups go to {f}");
                    }
                    else { cfg.Destinations.RemoveAll(s => string.Equals(s, f, StringComparison.OrdinalIgnoreCase)); o.WriteLine($"no longer a destination: {f}"); }
                    cfg.Save(); return 0;
                }
                case "providers":
                {
                    var found = Providers.Detect();
                    if (json) o.WriteLine(J(found));
                    else if (found.Count == 0) o.WriteLine("No provider folders found. Any folder works as a destination: a disk, a NAS, a synced folder.");
                    else foreach (var p in found) o.WriteLine($"  {p.Name,-18} {p.Path}");
                    return 0;
                }
                case "exclude":
                {
                    if (Opt(rest, "--max-mb") is { } mb && int.TryParse(mb, out var n)) { cfg.MaxFileMB = n; cfg.Save(); }
                    if (pos.Count >= 2 && pos[0] == "add") { if (!cfg.Excludes.Contains(pos[1])) cfg.Excludes.Add(pos[1]); cfg.Save(); }
                    if (pos.Count >= 2 && pos[0] == "remove") { cfg.Excludes.Remove(pos[1]); cfg.Save(); }
                    if (json) o.WriteLine(J(new { patterns = cfg.Excludes, maxMB = cfg.MaxFileMB }));
                    else o.WriteLine($"skip: {string.Join(", ", cfg.Excludes)}{(cfg.MaxFileMB > 0 ? $"; files over {cfg.MaxFileMB} MB" : "")}");
                    return 0;
                }

                case "backup":
                {
                    if (KeyStore.Load() is not { } k) { err.WriteLine("no key on this PC (stash key new)"); return 2; }
                    if (cfg.Sources.Count == 0) { err.WriteLine("nothing to back up (stash add <folder>)"); return 2; }
                    if (cfg.Destinations.Count == 0) { err.WriteLine("nowhere to put it (stash dest <folder>)"); return 2; }
                    var results = new List<object>();
                    int worst = 0;
                    foreach (var d in cfg.Destinations)
                    {
                        if (!Directory.Exists(d)) { results.Add(new { destination = d, skipped = "not reachable right now" }); if (!json) o.WriteLine($"{d}: not reachable right now, skipped"); worst = Math.Max(worst, 1); continue; }
                        try
                        {
                            var rules = cfg.Rules; if (Flag(rest, "--read-all")) rules.TrustTimestamps = false;
                            var r = Backup.Run(cfg.Sources, d, k, rules, (done, total, _) => { if (!json && done % 50 == 0) err.Write($"\r{done}/{total} files"); });
                            if (!json) err.Write("\r");
                            results.Add(new { destination = d, files = r.Files, bytes = r.Bytes, new_chunks = r.NewChunks, new_bytes = r.NewBytes, reused_chunks = r.ReusedChunks, unchanged = r.Unchanged, skipped_placeholders = r.SkippedPlaceholders, skipped_by_rule = r.SkippedByRule, unreadable = r.Unreadable, manifest = r.Manifest });
                            if (!json) o.WriteLine($"{d}: {r.Files} files, {Human(r.Bytes)}; uploaded {r.NewChunks} chunks ({Human(r.NewBytes)}), reused {r.ReusedChunks}" + (r.Unchanged > 0 ? $", {r.Unchanged} unchanged and not reread" : "")
                                + (r.SkippedPlaceholders > 0 ? $"; {r.SkippedPlaceholders} cloud placeholders listed, not downloaded" : "")
                                + (r.SkippedByRule > 0 ? $"; {r.SkippedByRule} skipped by your rules" : "")
                                + (r.Unreadable.Count > 0 ? $"; {r.Unreadable.Count} unreadable" : ""));
                            if (r.SkippedPlaceholders > 0 || r.Unreadable.Count > 0) worst = Math.Max(worst, 1);
                            var p = Prune.Run(new FolderStore(d, k), k, cfg.KeepSnapshots, cfg.Policy);
                            if (p.BytesFreed > 0 && !json) o.WriteLine($"{d}: reclaimed {Human(p.BytesFreed)} from {p.SnapshotsRemoved} old snapshots");
                        }
                        catch (Exception ex) { results.Add(new { destination = d, error = ex.Message }); if (!json) err.WriteLine($"{d}: {ex.Message}"); worst = 2; }
                    }
                    cfg.LastBackup = DateTimeOffset.Now;
                    bool scheduled = Flag(rest, "--scheduled");
                    if (scheduled)
                    {
                        cfg.LastScheduledAt = DateTimeOffset.Now;
                        cfg.LastScheduledResult = worst == 0 ? "ok" : worst == 1 ? "partial" : "failed";
                        // The weekly self-check rides along with the schedule, like the Mac's.
                        if (cfg.WeeklyVerify && Schedule.IsDue(cfg.LastVerify, TimeSpan.FromDays(7), DateTimeOffset.Now))
                        {
                            bool clean = true;
                            foreach (var d in cfg.Destinations.Where(Directory.Exists)) { var v = Restore.Verify(new FolderStore(d, k), k); if (!v.Clean) clean = false; }
                            cfg.LastVerify = DateTimeOffset.Now;
                            if (!clean) { cfg.LastScheduledResult = "verify failed"; worst = 2; }
                            Paths.Log_($"weekly verify: {(clean ? "clean" : "PROBLEM")}");
                        }
                    }
                    cfg.Save();
                    Paths.Log_($"backup{(scheduled ? " (scheduled)" : "")}: {results.Count} destinations, worst {worst}");
                    if (json) o.WriteLine(J(new { results }));
                    return worst;
                }

                case "snapshots":
                {
                    if (KeyStore.Load() is not { } k) { err.WriteLine("no key on this PC"); return 2; }
                    var all = new List<object>();
                    foreach (var d in cfg.Destinations.Where(Directory.Exists))
                    {
                        var store = new FolderStore(d, k);
                        var sizes = Prune.UniqueSizes(store, k);
                        foreach (var s in Restore.Snapshots(store, k))
                        {
                            all.Add(new { destination = d, snapshot = s.FileName, created = s.CreatedAt, host = s.Host, files = s.Files, bytes = s.Bytes, placeholders = s.Placeholders, frees = sizes.GetValueOrDefault(s.FileName) });
                            if (!json) o.WriteLine($"{s.FileName}  {s.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}  {s.Host,-16} {s.Files,6} files  {Human(s.Bytes),9}  frees {Human(sizes.GetValueOrDefault(s.FileName))}  ({d})");
                        }
                    }
                    if (json) o.WriteLine(J(new { snapshots = all }));
                    else if (all.Count == 0) o.WriteLine("no snapshots yet");
                    return 0;
                }
                case "forget":
                {
                    if (KeyStore.Load() is not { } k) { err.WriteLine("no key on this PC"); return 2; }
                    if (pos.Count == 0) { err.WriteLine("forget <snapshot>"); return 64; }
                    int n = 0;
                    foreach (var d in cfg.Destinations.Where(Directory.Exists))
                    {
                        var store = new FolderStore(d, k);
                        if (!store.ManifestNames().Contains(pos[0])) continue;
                        var p = Prune.Delete(pos[0], store, k); n++;
                        o.WriteLine($"{d}: removed {pos[0]}, freed {Human(p.BytesFreed)}");
                    }
                    if (n == 0) { err.WriteLine("no such snapshot at any destination"); return 2; }
                    return 0;
                }
                case "restore":
                {
                    if (KeyStore.Load() is not { } k) { err.WriteLine("no key on this PC (stash key restore \"<24 words>\")"); return 2; }
                    if (pos.Count < 2) { err.WriteLine("restore <snapshot|latest> <target folder> [--only a,b] [--dest <folder>]"); return 64; }
                    var dests = Opt(rest, "--dest") is { } dd ? new List<string> { Path.GetFullPath(dd) } : cfg.Destinations.Where(Directory.Exists).ToList();
                    if (dests.Count == 0) { err.WriteLine("no destination reachable; name one with --dest"); return 2; }
                    var only = Opt(rest, "--only")?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().Replace('\\', '/')).ToList() ?? new List<string>();
                    foreach (var d in dests)
                    {
                        var store = new FolderStore(d, k);
                        var names = store.ManifestNames();
                        var snap = pos[0] == "latest" ? names.FirstOrDefault() : names.FirstOrDefault(x => x == pos[0]);
                        if (snap is null) continue;
                        var r = Restore.Run(snap, store, k, Path.GetFullPath(pos[1]), only, (done, total) => { if (!json && done % 50 == 0) err.Write($"\r{done}/{total}"); });
                        if (!json) err.Write("\r");
                        if (json) o.WriteLine(J(new { destination = d, snapshot = snap, restored = r.Restored, bytes = r.Bytes, failed = r.Failed }));
                        else { o.WriteLine($"restored {r.Restored} files ({Human(r.Bytes)}) from {snap} into {pos[1]}"); foreach (var f in r.Failed) o.WriteLine("  failed: " + f); }
                        return r.Failed.Count == 0 ? 0 : 1;
                    }
                    err.WriteLine("no such snapshot at any reachable destination"); return 2;
                }
                case "verify":
                {
                    if (KeyStore.Load() is not { } k) { err.WriteLine("no key on this PC"); return 2; }
                    int worst = 0; var all = new List<object>();
                    foreach (var d in cfg.Destinations.Where(Directory.Exists))
                    {
                        var v = Restore.Verify(new FolderStore(d, k), k, (done, total) => { if (!json && done % 50 == 0) err.Write($"\r{done}/{total}"); });
                        if (!json) err.Write("\r");
                        all.Add(new { destination = d, checked_ = v.ChunksChecked, in_cloud_only = v.ChunksInCloudOnly, bad = v.ChunksBad, missing = v.ChunksMissing, sample = v.SampleFile, sample_ok = v.SampleOk });
                        if (!json) o.WriteLine($"{d}: {v.ChunksChecked} chunks open" + (v.ChunksInCloudOnly > 0 ? $", {v.ChunksInCloudOnly} in the cloud only (not downloaded)" : "") + (v.ChunksBad.Count > 0 ? $", {v.ChunksBad.Count} DAMAGED" : "") + (v.ChunksMissing.Count > 0 ? $", {v.ChunksMissing.Count} MISSING" : "") + (v.SampleFile is not null ? $"; sample {v.SampleFile}: {(v.SampleOk == true ? "restored and matched" : "FAILED")}" : ""));
                        if (!v.Clean) worst = 2;
                    }
                    cfg.LastVerify = DateTimeOffset.Now; cfg.Save();
                    if (json) o.WriteLine(J(new { results = all }));
                    return worst;
                }
                case "prune":
                {
                    if (KeyStore.Load() is not { } k) { err.WriteLine("no key on this PC"); return 2; }
                    if (Opt(rest, "--keep") is { } kp && int.TryParse(kp, out var keepN)) { cfg.KeepSnapshots = keepN; cfg.Retention = "last"; cfg.Save(); }
                    if (Flag(rest, "--thin")) { cfg.Retention = "thin"; cfg.Save(); }
                    foreach (var d in cfg.Destinations.Where(Directory.Exists))
                    {
                        var p = Prune.Run(new FolderStore(d, k), k, cfg.KeepSnapshots, cfg.Policy);
                        if (json) o.WriteLine(J(new { destination = d, snapshots_removed = p.SnapshotsRemoved, chunks_removed = p.ChunksRemoved, bytes_freed = p.BytesFreed, bytes_kept = p.BytesKept }));
                        else o.WriteLine($"{d}: removed {p.SnapshotsRemoved} snapshots and {p.ChunksRemoved} chunks, freed {Human(p.BytesFreed)}, keeping {Human(p.BytesKept)}");
                    }
                    return 0;
                }
                case "update":
                {
                    var r = UpdateCheck.Now(cfg).GetAwaiter().GetResult();
                    o.WriteLine(r switch { Updates.Available a => $"Version {a.Version} is available: {a.Page}", Updates.UpToDate u => $"Up to date ({u.Latest}).", Updates.Unknown x => $"Could not check: {x.Reason}", _ => "?" });
                    return r is Updates.Available ? 1 : 0;
                }
                case "schedule":
                {
                    var which = pos.FirstOrDefault()?.ToLowerInvariant();
                    if (which is not ("off" or "hourly" or "daily")) { err.WriteLine("schedule off | hourly | daily"); return 64; }
                    cfg.Schedule = which; if (Flag(rest, "--when-signed-out")) cfg.WhenSignedOut = true; if (Flag(rest, "--signed-in-only")) cfg.WhenSignedOut = false; cfg.Save();
                    var (ok, msg) = Schedule.Install(which, cfg.WhenSignedOut);
                    o.WriteLine(msg); return ok ? 0 : 2;
                }
                case "seal": case "open":
                {
                    if (pos.Count < 2) { err.WriteLine($"{verb} <in> <out>"); return 64; }
                    if (KeyStore.Load() is not { } k) { err.WriteLine("no key on this PC"); return 2; }
                    var input = File.ReadAllBytes(pos[0]);
                    var output = verb == "seal" ? Chunk.Seal(input, k) : Chunk.Open(input, k);
                    File.WriteAllBytes(pos[1], output);
                    o.WriteLine(verb == "seal" ? $"sealed {input.Length} bytes as {output.Length} bytes, chunk name {Chunk.Name(input, k)[..16]}..." : $"opened {output.Length} bytes");
                    return 0;
                }
                case "status":
                {
                    var k = KeyStore.Load();
                    if (json) { o.WriteLine(J(new { version = Version, key = k?.Fingerprint, key_protected_for = KeyStore.Scope(), folders = cfg.Sources, destinations = cfg.Destinations, last_backup = cfg.LastBackup, last_verify = cfg.LastVerify, schedule = cfg.Schedule, scheduled = Schedule.IsInstalled(), retention = cfg.Retention, keep = cfg.KeepSnapshots })); }
                    else
                    {
                        o.WriteLine($"Stash for Windows {Version}");
                        o.WriteLine($"key:           {(k is null ? "none (stash key new)" : k.Fingerprint + KeyStore.Scope() switch { "pc" => " (protected for this PC, so the schedule runs with nobody signed in)", "account" => " (protected for your account)", _ => "" })}");
                        o.WriteLine($"folders:       {(cfg.Sources.Count == 0 ? "none (stash add <folder>)" : string.Join(", ", cfg.Sources))}");
                        o.WriteLine($"destinations:  {(cfg.Destinations.Count == 0 ? "none (stash dest <folder>)" : string.Join(", ", cfg.Destinations.Select(d => d + (Directory.Exists(d) ? "" : " (not reachable)"))))}");
                        o.WriteLine($"last backup:   {(cfg.LastBackup is { } lb ? lb.ToLocalTime().ToString("g") : "never")}");
                        o.WriteLine($"last verify:   {(cfg.LastVerify is { } lv ? lv.ToLocalTime().ToString("g") : "never")}");
                        o.WriteLine($"schedule:      {cfg.Schedule}{(Schedule.IsInstalled() ? (cfg.WhenSignedOut ? " (Task Scheduler, even when nobody is signed in)" : " (Task Scheduler, while signed in)") : cfg.Schedule == "off" ? "" : " (not registered: stash schedule " + cfg.Schedule + ")")}");
                        o.WriteLine($"keep:          {(cfg.Retention == "thin" ? "thin out over time" : $"newest {cfg.KeepSnapshots} snapshots")}");
                        if (k is not null) o.WriteLine($"at destination:{string.Join(",", cfg.Destinations.Where(Directory.Exists).Select(d => " " + Human(new FolderStore(d, k).TotalSize())))}");
                    }
                    return k is null || cfg.Sources.Count == 0 || cfg.Destinations.Count == 0 ? 1 : 0;
                }
                default:
                    err.WriteLine($"stash: unknown command \"{verb}\""); o.WriteLine(Help); return 64;
            }
        }
        catch (Exception ex) { err.WriteLine("stash: " + ex.Message); return 2; }
    }
}

/// <summary>One GET to GitHub's releases API when due. The pure comparison lives in the core.</summary>
public static class UpdateCheck
{
    public static async Task<Updates.Result> Now(Config cfg)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Stash-for-Windows/" + Cli.Version);
            var resp = await http.GetAsync(Updates.Api);
            var body = await resp.Content.ReadAsStringAsync();
            cfg.LastUpdateCheck = DateTimeOffset.Now; cfg.Save();
            return Updates.Parse((int)resp.StatusCode, body, Cli.Version);
        }
        catch (Exception ex) { return new Updates.Unknown(ex.Message); }
    }
}

/// <summary>The recovery card as text: words numbered, the fingerprint, and what to do with it.</summary>
public static class RecoveryCard
{
    /// <summary>The card as an image: the QR code, the numbered words and the fingerprint. Rendered with WPF off screen.</summary>
    public static byte[] Png(MasterKey key)
    {
        byte[]? result = null;
        var t = new Thread(() =>
        {
            var _ = System.Windows.Application.Current ?? new App();
            result = CardImage.Render(key);
        });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        return result ?? throw new InvalidOperationException("could not render the card");
    }

    public static string Text(MasterKey key) =>
        "STASH RECOVERY CARD\n\n" +
        "These 24 words are the only way to read your backup. Keep this card somewhere safe; treat it like a passport.\n\n" +
        Mnemonic.Card(key.Words) + "\n\n" +
        $"Key fingerprint: {key.Fingerprint}\n" +
        $"QR payload (type or scan): {key.QrPayload}\n\n" +
        "To restore: install Stash for Windows (github.com/keithadler/stashwin) or Stash for Mac (github.com/keithadler/stashmac),\n" +
        "enter the words, choose the folder the backup was written to.\n";
}
