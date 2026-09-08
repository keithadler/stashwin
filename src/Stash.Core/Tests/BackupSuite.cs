using System.Security.Cryptography;

namespace Stash.Core.Tests;

public static class BackupSuite
{
    public static Suite Run()
    {
        var s = new Suite("backup");
        using var t = Suite.Temp();
        var key = MasterKey.Random();
        var src = t.Dir("Documents");
        var dest = t.Dir("OneDrive");
        var big = RandomNumberGenerator.GetBytes(Chunk.Size + 4321);
        File.WriteAllText(Path.Combine(src, "notes.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(src, "tax"));
        File.WriteAllBytes(Path.Combine(src, "tax", "2025.bin"), big);
        File.WriteAllBytes(Path.Combine(src, "tax", "copy.bin"), big);          // identical content: dedupes
        File.WriteAllText(Path.Combine(src, "empty.txt"), "");
        File.WriteAllText(Path.Combine(src, "scratch.tmp"), "junk");           // excluded by rule
        File.WriteAllText(Path.Combine(src, "Thumbs.db"), "junk");             // ignored name
        Directory.CreateDirectory(Path.Combine(src, "node_modules"));
        File.WriteAllText(Path.Combine(src, "node_modules", "x.js"), "junk");
        Manifest.HostOverride = "SAMS-PC";
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddMinutes(-5)); // old enough for the shortcut

        var report = Backup.Run(new[] { src }, dest, key);
        s.Equal("files backed up", 4, report.Files);
        s.Equal("big file is two chunks, shared by its copy", 4, report.NewChunks); // notes, empty, big×2
        s.Equal("the copy reused both chunks", 2, report.ReusedChunks);
        s.Equal("skipped by rule", 1, report.SkippedByRule);
        s.Check("layout folder is the Mac's", Directory.Exists(Path.Combine(dest, "Stash for Mac", key.Fingerprint, "chunks")));
        s.Check("README explains the folder", File.ReadAllText(Path.Combine(dest, "Stash for Mac", key.Fingerprint, "README.txt")).Contains("24-word"));
        s.Check("nothing readable at the destination", !Directory.EnumerateFiles(Path.Combine(dest, "Stash for Mac"), "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("README.txt")).Any(f => File.ReadAllBytes(f).AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes("hello")) >= 0));

        var store = new FolderStore(dest, key);
        var snaps = Restore.Snapshots(store, key);
        s.Equal("one snapshot", 1, snaps.Count);
        s.Check("snapshot carries host and counts", snaps[0].Host == "SAMS-PC" && snaps[0].Files == 4 && snaps[0].Bytes == 5 + 2 * big.Length);

        // Second backup, unchanged: nothing new uploaded, and nothing even read.
        var second = Backup.Run(new[] { src }, dest, key);
        s.Equal("unchanged backup uploads nothing", 0, second.NewChunks);
        s.Equal("unchanged files were carried over without reading", 4, second.Unchanged);
        s.Equal("two snapshots now", 2, Restore.Snapshots(store, key).Count);
        // A touched file is read again; a file whose pieces vanished is read again too.
        File.WriteAllText(Path.Combine(src, "notes.txt"), "hello!");
        var touched = Backup.Run(new[] { src }, dest, key);
        s.Check("a changed file is read and uploaded", touched.Unchanged == 3 && touched.NewChunks == 1);
        // Racy: rewritten with the same size within two seconds of the snapshot: reread, never trusted.
        File.WriteAllText(Path.Combine(src, "notes.txt"), "HELLO!");
        var racy = Backup.Run(new[] { src }, dest, key);
        s.Check("a same-size rewrite right after a snapshot is reread", racy.NewChunks == 1 && Manifest.Open(store.GetManifest(racy.Manifest), key).Files.First(f => f.Path == "notes.txt").Chunks[0] == Chunk.Name(System.Text.Encoding.UTF8.GetBytes("HELLO!"), key));
        File.SetLastWriteTimeUtc(Path.Combine(src, "notes.txt"), DateTime.UtcNow.AddMinutes(-5));
        Backup.Run(new[] { src }, dest, key);
        var missing = Manifest.Open(store.GetManifest(store.ManifestNames()[0]), key).Files.First(f => f.Path == "empty.txt").Chunks[0];
        var missingPath = store.Layout.ChunkPath(missing); var missingBytes = File.ReadAllBytes(missingPath); File.Delete(missingPath);
        var healed = Backup.Run(new[] { src }, dest, key);
        s.Check("a file whose piece went missing is re-uploaded", healed.Unchanged == 3 && healed.NewChunks == 1 && store.HasChunk(missing));
        File.WriteAllText(Path.Combine(src, "notes.txt"), "hello"); File.SetLastWriteTimeUtc(Path.Combine(src, "notes.txt"), DateTime.UtcNow.AddMinutes(-5));
        var strict = Backup.Run(new[] { src }, dest, key, new Rules { TrustTimestamps = false });
        s.Equal("with timestamps distrusted every file is read", 0, strict.Unchanged);
        second = strict;
        // Cancellation stops between files and writes no manifest.
        using var cts = new CancellationTokenSource(); cts.Cancel();
        int before = store.ManifestNames().Count;
        var cancelled = Backup.Run(new[] { src }, dest, key, cancel: cts.Token);
        s.Check("cancelled backup writes no manifest", cancelled.Cancelled && store.ManifestNames().Count == before);

        // Restore everything and compare.
        var target = t.Dir("restore");
        var rr = Restore.Run(second.Manifest, store, key, target);
        s.Equal("all files restored", 4, rr.Restored);
        s.Check("contents match", File.ReadAllText(Path.Combine(target, "Documents", "notes.txt")) == "hello"
            && File.ReadAllBytes(Path.Combine(target, "Documents", "tax", "2025.bin")).AsSpan().SequenceEqual(big)
            && new FileInfo(Path.Combine(target, "Documents", "empty.txt")).Length == 0);
        s.Check("excluded and ignored files did not come back", !File.Exists(Path.Combine(target, "Documents", "scratch.tmp")) && !File.Exists(Path.Combine(target, "Documents", "Thumbs.db")));

        // Restore only one folder.
        var t2 = t.Dir("restore2");
        var only = Restore.Run(second.Manifest, store, key, t2, new[] { "tax" });
        s.Equal("only the tax folder", 2, only.Restored);
        s.Check("notes not restored", !File.Exists(Path.Combine(t2, "Documents", "notes.txt")));

        // Verify: clean, then a damaged chunk is reported and never written.
        var v = Restore.Verify(store, key);
        s.Check("verify is clean", v.Clean && v.ChunksChecked == 4 && v.SampleOk == true, $"bad {v.ChunksBad.Count} missing {v.ChunksMissing.Count} sample {v.SampleOk}");
        s.Check("verify compared the sample byte for byte with the live file", v.SampleCompared);
        var chunkName = Manifest.Open(store.GetManifest(second.Manifest), key).Files.First(f => f.Path == "notes.txt").Chunks[0];
        var p = store.Layout.ChunkPath(chunkName);
        var bytes = File.ReadAllBytes(p); bytes[^1] ^= 1; File.WriteAllBytes(p, bytes);
        var v2 = Restore.Verify(store, key);
        s.Check("damaged chunk found by verify", v2.ChunksBad.Count == 1 && v2.ChunksBad[0] == chunkName);
        var t3 = t.Dir("restore3");
        var r3 = Restore.Run(second.Manifest, store, key, t3);
        s.Check("damaged file fails alone, nothing partial written", r3.Failed.Count == 1 && r3.Restored == 3 && !File.Exists(Path.Combine(t3, "Documents", "notes.txt")) && !Directory.EnumerateFiles(t3, "*.stash-partial", SearchOption.AllDirectories).Any());
        bytes[^1] ^= 1; File.WriteAllBytes(p, bytes); // repair the chunk for the checks that follow

        // Wrong key sees no snapshots.
        s.Equal("wrong key lists nothing", 0, Restore.Snapshots(new FolderStore(dest, MasterKey.Random()), MasterKey.Random()).Count);

        // Placeholders: listed, never read.
        var ph = Path.Combine(src, "cloud-only.docx");
        File.WriteAllText(ph, "should never be read");
        FolderStore.IsDataless = path => path.EndsWith("cloud-only.docx");
        try
        {
            var third = Backup.Run(new[] { src }, dest, key);
            s.Equal("placeholder listed", 1, third.SkippedPlaceholders);
            var m3 = Manifest.Open(store.GetManifest(third.Manifest), key);
            s.Check("placeholder named in the manifest, not stored", m3.SkippedPlaceholders.Contains("Documents/cloud-only.docx") && m3.Files.All(f => f.Path != "cloud-only.docx"));
        }
        finally { FolderStore.IsDataless = _ => false; }

        // Two sources, source names kept apart on restore.
        var src2 = t.Dir("Pictures");
        File.WriteAllText(Path.Combine(src2, "notes.txt"), "different");
        var four = Backup.Run(new[] { src, src2 }, dest, key);
        var t4 = t.Dir("restore4");
        Restore.Run(four.Manifest, store, key, t4);
        s.Check("two sources restore side by side", File.Exists(Path.Combine(t4, "Documents", "notes.txt")) && File.ReadAllText(Path.Combine(t4, "Documents", "notes.txt")) == "hello" && File.ReadAllText(Path.Combine(t4, "Pictures", "notes.txt")) == "different");

        s.Check("pattern match", Backup.Matches("Cache", "Cache") && Backup.Matches("x.TMP", "*.tmp") && !Backup.Matches("tmp.x", "*.tmp") && Backup.Matches("Photos.photoslibrary", "*.photoslibrary"));
        Manifest.HostOverride = null;
        return s;
    }
}
