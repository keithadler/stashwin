using System.IO;
using System.Security.Cryptography;
using Stash.Core;

namespace Stash;

/// <summary>Sample data for screenshots: Sam Rivera's PC with a real, tiny stash in a temp folder. Never real data.</summary>
public static class Demo
{
    public static Shell Shell()
    {
        var root = Path.Combine(Path.GetTempPath(), "stash-demo-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("STASH_HOME", Path.Combine(root, "home"));
        Directory.CreateDirectory(Path.Combine(root, "home"));
        var docs = Path.Combine(root, "Documents"); var pics = Path.Combine(root, "Pictures");
        foreach (var d in new[] { Path.Combine(docs, "Tax"), Path.Combine(docs, "Pine Street Holdings"), Path.Combine(pics, "2026") }) Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(docs, "Tax", "2025 return.pdf"), new string('x', 40_000));
        File.WriteAllText(Path.Combine(docs, "Pine Street Holdings", "lease.docx"), new string('y', 120_000));
        File.WriteAllText(Path.Combine(docs, "notes.txt"), "Sam's notes");
        File.WriteAllBytes(Path.Combine(pics, "2026", "IMG_0412.jpg"), RandomNumberGenerator.GetBytes(300_000));
        File.WriteAllBytes(Path.Combine(pics, "2026", "IMG_0413.jpg"), RandomNumberGenerator.GetBytes(280_000));
        var one = Path.Combine(root, "OneDrive"); Directory.CreateDirectory(one);
        var disk = Path.Combine(root, "Sam's backup disk"); Directory.CreateDirectory(disk);

        var key = MasterKey.FromWords("legal winner thank year wave sausage worth useful legal winner thank year wave sausage worth useful legal winner thank year wave sausage worth title");
        KeyStore.Save(key);
        var cfg = new Config { Sources = { docs, pics }, Destinations = { one, disk }, CardConfirmed = true, Schedule = "daily" };
        cfg.Save();
        Manifest.HostOverride = "SAMS-LAPTOP";
        Backup.Run(cfg.Sources, one, key); Backup.Run(cfg.Sources, disk, key);
        File.WriteAllText(Path.Combine(docs, "notes.txt"), "Sam's notes, revised");
        Backup.Run(cfg.Sources, one, key);
        cfg.LastBackup = DateTimeOffset.Now.AddHours(-3); cfg.LastVerify = DateTimeOffset.Now.AddDays(-4); cfg.Save();
        Directory.Delete(disk, true); // the disk is unplugged today
        var shell = new Shell();
        shell.Load();
        return shell;
    }
}
