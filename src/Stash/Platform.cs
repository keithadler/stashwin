using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stash.Core;

namespace Stash;

/// <summary>Where the app keeps its settings and key. STASH_HOME overrides it, so tests never touch the real folder.</summary>
public static class Paths
{
    public static string Home => Environment.GetEnvironmentVariable("STASH_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stash for Windows");
    public static string ConfigFile => Path.Combine(Home, "config.json");
    public static string KeyFile(string stash) => Path.Combine(Home, $"key-{stash}.bin");
    public static string Log => Path.Combine(Home, "stash.log");

    public static void Log_(string line)
    {
        try { Directory.CreateDirectory(Home); File.AppendAllText(Log, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}"); } catch { }
    }
}

/// <summary>The master key for daily use, wrapped by Windows' own Data Protection for this user on this PC, the way the
/// Mac keeps it in the login Keychain. The words on the card are the only other copy, and they live with you.</summary>
public static class KeyStore
{
    private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("com.keithadler.stashwin");

    public static MasterKey? Load(string stash = "default")
    {
        var f = Paths.KeyFile(stash);
        if (!File.Exists(f)) return null;
        try
        {
            var raw = File.ReadAllBytes(f);
            var plain = Environment.GetEnvironmentVariable("STASH_HOME") is not null && raw.Length == 32 ? raw : ProtectedData.Unprotect(raw, Entropy, DataProtectionScope.CurrentUser);
            return plain.Length == 32 ? new MasterKey(plain) : null;
        }
        catch { return null; }
    }

    public static void Save(MasterKey key, string stash = "default")
    {
        Directory.CreateDirectory(Paths.Home);
        var bytes = Environment.GetEnvironmentVariable("STASH_HOME") is not null ? key.Entropy : ProtectedData.Protect(key.Entropy, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Paths.KeyFile(stash), bytes);
    }

    public static void Delete(string stash = "default") { try { File.Delete(Paths.KeyFile(stash)); } catch { } }
}

/// <summary>What to back up and where. Plain paths in a JSON file; the key is never here.</summary>
public sealed class Config
{
    public List<string> Sources { get; set; } = new();
    public List<string> Destinations { get; set; } = new();
    public DateTimeOffset? LastBackup { get; set; }
    public DateTimeOffset? LastVerify { get; set; }
    public string Schedule { get; set; } = "daily";     // off | hourly | daily
    public string Retention { get; set; } = "last";     // last | thin
    public int KeepSnapshots { get; set; } = 30;
    public List<string> Excludes { get; set; } = Rules.DefaultExcludes.ToList();
    public int MaxFileMB { get; set; }
    public bool WeeklyVerify { get; set; } = true;
    public bool CardConfirmed { get; set; }

    [JsonIgnore] public Rules Rules => new() { Excludes = Excludes.ToList(), MaxFileBytes = (long)MaxFileMB * 1_048_576 };
    [JsonIgnore] public Retention.Policy Policy => Retention == "thin" ? Core.Retention.Policy.Thin : Core.Retention.Policy.Last;
    [JsonIgnore] public TimeSpan? Interval => Schedule switch { "hourly" => TimeSpan.FromHours(1), "daily" => TimeSpan.FromDays(1), _ => null };

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Config Load()
    {
        try { if (File.Exists(Paths.ConfigFile)) return JsonSerializer.Deserialize<Config>(File.ReadAllText(Paths.ConfigFile), Json) ?? new Config(); }
        catch { }
        return new Config();
    }

    public void Save()
    {
        Directory.CreateDirectory(Paths.Home);
        File.WriteAllText(Paths.ConfigFile, JsonSerializer.Serialize(this, Json));
    }

    public static bool IsInside(string path, string folder)
    {
        var p = Path.GetFullPath(path).TrimEnd('\\', '/'); var f = Path.GetFullPath(folder).TrimEnd('\\', '/');
        return string.Equals(p, f, StringComparison.OrdinalIgnoreCase) || p.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Why a folder cannot be added, in plain words; null when it can.</summary>
    public string? ObjectionToSource(string folder)
    {
        var p = Path.GetFullPath(folder);
        if (Path.GetPathRoot(p)?.TrimEnd('\\') == p.TrimEnd('\\')) return "The whole drive is too much. Choose the folders that matter.";
        foreach (var d in Destinations) if (IsInside(p, d) || IsInside(d, p)) return $"{Path.GetFileName(p.TrimEnd('\\'))} overlaps the destination {Path.GetFileName(d.TrimEnd('\\'))}. A backup must not contain itself.";
        foreach (var s in Sources) if (IsInside(p, s)) return $"{Path.GetFileName(p.TrimEnd('\\'))} is already inside {Path.GetFileName(s.TrimEnd('\\'))}, which is backed up.";
        return null;
    }

    public string? ObjectionToDestination(string folder)
    {
        var p = Path.GetFullPath(folder);
        foreach (var s in Sources) if (IsInside(p, s) || IsInside(s, p)) return $"{Path.GetFileName(p.TrimEnd('\\'))} overlaps {Path.GetFileName(s.TrimEnd('\\'))}, which is backed up. A backup must not contain itself.";
        return null;
    }
}

/// <summary>Cloud placeholders: files whose bytes are not on this PC (OneDrive Files On-Demand, Google Drive streaming,
/// Dropbox online-only, iCloud for Windows). Reading one would make the provider download it behind the user's back.</summary>
public static class Placeholders
{
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;

    public static bool IsDataless(string path)
    {
        try
        {
            var a = File.GetAttributes(path);
            return (a & RecallOnDataAccess) != 0 || (a & RecallOnOpen) != 0 || (a & FileAttributes.Offline) != 0;
        }
        catch { return false; }
    }

    public static void Install() => FolderStore.IsDataless = IsDataless;
}

/// <summary>Folders the usual providers sync on this PC, offered as destinations when they exist.</summary>
public static class Providers
{
    public sealed record Found(string Name, string Path);

    public static List<Found> Detect()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var list = new List<Found>();
        void Add(string name, string? path) { if (path is not null && Directory.Exists(path) && list.All(f => !string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) list.Add(new Found(name, path)); }
        Add("OneDrive", Environment.GetEnvironmentVariable("OneDrive"));
        Add("OneDrive", Environment.GetEnvironmentVariable("OneDriveConsumer"));
        Add("OneDrive for work", Environment.GetEnvironmentVariable("OneDriveCommercial"));
        Add("OneDrive", Path.Combine(home, "OneDrive"));
        Add("Google Drive", Path.Combine(home, "Google Drive"));
        Add("Google Drive", Path.Combine(home, "My Drive"));
        foreach (var d in DriveInfo.GetDrives())
        {
            try { if (d.IsReady && d.VolumeLabel.Contains("Google Drive", StringComparison.OrdinalIgnoreCase)) Add("Google Drive", Path.Combine(d.RootDirectory.FullName, "My Drive")); } catch { }
        }
        Add("Dropbox", Path.Combine(home, "Dropbox"));
        Add("iCloud Drive", Path.Combine(home, "iCloudDrive"));
        return list;
    }
}

/// <summary>The schedule is a Task Scheduler task that runs the console twin, so backups happen whether or not the
/// window is open. Registered for this user only; no administrator, no service.</summary>
public static class Schedule
{
    public const string TaskName = "Stash for Windows";

    private static string ConsoleExe()
    {
        var dir = AppContext.BaseDirectory;
        var exe = Environment.ProcessPath ?? "";
        var candidate = Path.Combine(Path.GetDirectoryName(exe) ?? dir, "stash.exe");
        return File.Exists(candidate) ? candidate : exe;
    }

    public static (bool Ok, string Message) Install(string schedule)
    {
        if (schedule == "off") return Remove();
        var exe = ConsoleExe();
        if (!exe.EndsWith("stash.exe", StringComparison.OrdinalIgnoreCase)) return (false, "Put stash.exe next to the app so the schedule can run it.");
        var timing = schedule == "hourly" ? "/sc hourly /mo 1" : "/sc daily /st 20:00";
        var args = $"/create /f /tn \"{TaskName}\" /tr \"\\\"{exe}\\\" backup --scheduled\" {timing}";
        var (ok, output) = Run("schtasks.exe", args);
        return ok ? (true, $"Scheduled: {(schedule == "hourly" ? "every hour" : "once a day at 20:00")}, whether or not the window is open.") : (false, output);
    }

    public static (bool Ok, string Message) Remove()
    {
        var (ok, output) = Run("schtasks.exe", $"/delete /f /tn \"{TaskName}\"");
        return ok || output.Contains("cannot find", StringComparison.OrdinalIgnoreCase) ? (true, "No schedule.") : (false, output);
    }

    public static bool IsInstalled() => Run("schtasks.exe", $"/query /tn \"{TaskName}\"").Ok;

    private static (bool Ok, string Output) Run(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(30_000);
            return (p.ExitCode == 0, o.Trim());
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>Pure: is a run due? A little slack so "daily" still runs when the PC wakes a few minutes early.</summary>
    public static bool IsDue(DateTimeOffset? last, TimeSpan? interval, DateTimeOffset now)
        => interval is { } i && (last is null || now - last.Value >= i * 0.95);
}
