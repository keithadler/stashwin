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

/// <summary>The master key for daily use, wrapped by Windows' own Data Protection, the way the Mac keeps it in the login
/// Keychain. Normally wrapped for this user's account, which only that account signed in can open. When the schedule runs
/// with nobody signed in (a service-for-user logon has no credentials, so the account wrap fails with "key not valid for
/// use in specified state") it is wrapped for this PC instead: any administrator on this PC could then read it. The words
/// on the card are the only other copy, and they live with you.</summary>
public static class KeyStore
{
    private static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("com.keithadler.stashwin");
    private static bool Plain => Environment.GetEnvironmentVariable("STASH_HOME") is not null;

    public static MasterKey? Load(string stash = "default") => Read(stash).Key;

    /// <summary>"account", "pc", or null when there is no key. A test home (STASH_HOME) keeps the key plain and reports "test".</summary>
    public static string? Scope(string stash = "default") => Read(stash).Scope;

    // Key files begin with "STKW1" and one scope byte ('a' account, 'p' PC) before the DPAPI blob. Files from 1.0.0 are a
    // bare blob wrapped for the account. The scope is recorded because Windows unwraps a blob with whichever scope it was
    // made with, whatever the caller asks for, so the file itself has to say.
    private static readonly byte[] Header = System.Text.Encoding.ASCII.GetBytes("STKW1");

    private static (MasterKey? Key, string? Scope) Read(string stash)
    {
        var f = Paths.KeyFile(stash);
        if (!File.Exists(f)) return (null, null);
        byte[] raw;
        try { raw = File.ReadAllBytes(f); } catch { return (null, null); }
        if (Plain && raw.Length == 32) return (new MasterKey(raw), "test");
        var scope = "account"; var blob = raw;
        if (raw.Length > Header.Length + 1 && raw.AsSpan(0, Header.Length).SequenceEqual(Header))
        {
            scope = raw[Header.Length] == (byte)'p' ? "pc" : "account";
            blob = raw[(Header.Length + 1)..];
        }
        try
        {
            var plain = ProtectedData.Unprotect(blob, Entropy, scope == "pc" ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser);
            return plain.Length == 32 ? (new MasterKey(plain), scope) : (null, null);
        }
        catch { return (null, null); }
    }

    /// <summary>forPc wraps the key for this PC (readable by an administrator here) so a schedule with nobody signed in can open it.</summary>
    public static void Save(MasterKey key, bool forPc = false, string stash = "default")
    {
        Directory.CreateDirectory(Paths.Home);
        if (Plain) { File.WriteAllBytes(Paths.KeyFile(stash), key.Entropy); return; }
        var blob = ProtectedData.Protect(key.Entropy, Entropy, forPc ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Paths.KeyFile(stash), [.. Header, (byte)(forPc ? 'p' : 'a'), .. blob]);
    }

    /// <summary>Re-wrap the key for the PC or back for the account, only when it is not already so. False when there is no key to re-wrap.</summary>
    public static bool Rewrap(bool forPc, string stash = "default")
    {
        var (key, scope) = Read(stash);
        if (key is null) return false;
        if (scope == "test" || scope == (forPc ? "pc" : "account")) return true;
        Save(key, forPc, stash);
        return true;
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
    /// <summary>Read every file every backup instead of trusting size and modified time for unchanged ones.</summary>
    public bool ReadAll { get; set; }
    public bool WeeklyVerify { get; set; } = true;
    public bool CardConfirmed { get; set; }
    public bool UpdateCheck { get; set; } = true;
    public DateTimeOffset? LastUpdateCheck { get; set; }
    public string? SkippedVersion { get; set; }
    public bool OpenAtSignIn { get; set; } = true;
    public bool OpenAtSignInOffered { get; set; }
    public bool Tray { get; set; } = true;
    /// <summary>Register the schedule to run whether or not the user is signed in (S4U logon, no password stored).</summary>
    public bool WhenSignedOut { get; set; }
    public string? LastScheduledResult { get; set; }
    public DateTimeOffset? LastScheduledAt { get; set; }

    [JsonIgnore] public Rules Rules => new() { Excludes = Excludes.ToList(), MaxFileBytes = (long)MaxFileMB * 1_048_576, TrustTimestamps = !ReadAll };
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

/// <summary>Open at sign-in: one Run value for this user, registered once, a toggle in Settings, like the Mac's login item.</summary>
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled()
    {
        try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("Stash for Windows") is string; } catch { return false; }
    }
    public static void Set(bool on)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey)!;
            if (on) k.SetValue("Stash for Windows", $"\"{Environment.ProcessPath}\" --tray"); else k.DeleteValue("Stash for Windows", false);
        }
        catch { }
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
/// window is open. Registered for this user only through Task Scheduler's XML, so the settings are exact: catch up a
/// missed run when the PC wakes, allow running on battery, one instance at a time, and optionally run whether or not
/// the user is signed in (a service-for-user logon, no password stored). No administrator, no service.</summary>
public static class Schedule
{
    /// <summary>A test run (STASH_HOME set) registers under its own name so it never touches the real schedule.</summary>
    public static string TaskName => Environment.GetEnvironmentVariable("STASH_HOME") is null ? "Stash for Windows" : "Stash for Windows (test)";

    private static string ConsoleExe()
    {
        var dir = AppContext.BaseDirectory;
        var exe = Environment.ProcessPath ?? "";
        var candidate = Path.Combine(Path.GetDirectoryName(exe) ?? dir, "stash.exe");
        return File.Exists(candidate) ? candidate : exe;
    }

    /// <summary>The task definition. whenSignedOut chooses an S4U logon (runs with no one signed in, no password kept) over the interactive token.</summary>
    public static string Xml(string schedule, string exe, bool whenSignedOut, string userId)
    {
        var trigger = schedule == "hourly"
            ? "<TimeTrigger><StartBoundary>2026-01-01T00:00:00</StartBoundary><Repetition><Interval>PT1H</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition><Enabled>true</Enabled></TimeTrigger>"
            : "<CalendarTrigger><StartBoundary>2026-01-01T20:00:00</StartBoundary><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay><Enabled>true</Enabled></CalendarTrigger>";
        var logon = whenSignedOut ? "S4U" : "InteractiveToken";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Stash for Windows: encrypted backup into the folders you chose. Registered by the app; remove it from the app's Settings.</Description></RegistrationInfo>
              <Triggers>{trigger}</Triggers>
              <Principals><Principal id="Author"><UserId>{System.Security.SecurityElement.Escape(userId)}</UserId><LogonType>{logon}</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT12H</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{System.Security.SecurityElement.Escape(exe)}</Command><Arguments>backup --scheduled</Arguments></Exec></Actions>
            </Task>
            """;
    }

    public static (bool Ok, string Message) Install(string schedule, bool whenSignedOut = false)
    {
        if (schedule == "off") return Remove();
        var exe = ConsoleExe();
        if (!exe.EndsWith("stash.exe", StringComparison.OrdinalIgnoreCase)) return (false, "Put stash.exe next to the app so the schedule can run it.");
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        KeyStore.Rewrap(forPc: whenSignedOut);
        var xmlPath = Path.Combine(Path.GetTempPath(), "stash-task.xml");
        File.WriteAllText(xmlPath, Xml(schedule, exe, whenSignedOut, user), new System.Text.UnicodeEncoding(false, true));
        var (ok, output) = Run("schtasks.exe", $"/create /f /tn \"{TaskName}\" /xml \"{xmlPath}\"");
        try { File.Delete(xmlPath); } catch { }
        if (!ok) return (false, output);
        return (true, (schedule == "hourly" ? "Scheduled: every hour" : "Scheduled: once a day at 20:00") + (whenSignedOut ? ", whether or not you are signed in; the key is now protected for this PC rather than for your account." : ", while you are signed in (the screen may be locked)."));
    }

    public static (bool Ok, string Message) Remove()
    {
        KeyStore.Rewrap(forPc: false);
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
