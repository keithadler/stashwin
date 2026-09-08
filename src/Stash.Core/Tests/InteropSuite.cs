namespace Stash.Core.Tests;

/// <summary>A stash made by Stash for Mac (tests/fixtures/mac-stash, with its card in words.txt) restores here, byte for byte.
/// The fixture is generated once with the Mac CLI and committed, so this runs on every platform without a Mac.</summary>
public static class InteropSuite
{
    public static string? FixtureRoot
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("STASH_FIXTURES");
            if (env is not null && Directory.Exists(env)) return env;
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir is not null; i++)
            {
                var candidate = Path.Combine(dir, "tests", "fixtures");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }
    }

    public static Suite Run()
    {
        var s = new Suite("interop");
        var root = FixtureRoot;
        if (root is null || !File.Exists(Path.Combine(root, "words.txt"))) { s.Check("fixture from Stash for Mac present (skipped: not found)", true); return s; }
        var key = MasterKey.FromWords(File.ReadAllText(Path.Combine(root, "words.txt")));
        var expectedFp = File.ReadAllText(Path.Combine(root, "fingerprint.txt")).Trim();
        s.Equal("fingerprint matches the Mac's", expectedFp, key.Fingerprint);
        var store = new FolderStore(Path.Combine(root, "dest"), key);
        var snaps = Restore.Snapshots(store, key);
        s.Check("the Mac's manifest opens", snaps.Count >= 1, $"{snaps.Count} snapshots");
        if (snaps.Count == 0) return s;
        using var t = Suite.Temp();
        var r = Restore.Run(snaps[0].FileName, store, key, t.Path);
        s.Check("every file restores", r.Failed.Count == 0 && r.Restored == snaps[0].Files, string.Join("; ", r.Failed));
        var original = Path.Combine(root, "source");
        var restoredRoot = Path.Combine(t.Path, Path.GetFileName(original));
        foreach (var f in Directory.EnumerateFiles(original, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(original, f).Normalize(System.Text.NormalizationForm.FormC);
            var restored = Path.Combine(restoredRoot, rel);
            var a = File.Exists(restored) ? File.ReadAllBytes(restored) : null; var b = File.ReadAllBytes(f);
            s.Check($"{rel.Replace("é", "e")} is identical", a is not null && a.AsSpan().SequenceEqual(b),
                a is null ? "not restored: " + restored : $"restored {a.Length} bytes {Convert.ToHexString(a.Take(24).ToArray())} vs fixture {b.Length} bytes {Convert.ToHexString(b.Take(24).ToArray())}");
        }
        // The two big files are not stored in the fixture: 4 MiB + 1 bytes of i % 251, so the chunk boundary is crossed by exactly one byte.
        var pattern = new byte[Chunk.Size + 1];
        for (int i = 0; i < pattern.Length; i++) pattern[i] = (byte)(i % 251);
        foreach (var name in new[] { "big.bin", "copy.bin" })
        {
            var restored = Path.Combine(restoredRoot, "tax", name);
            s.Check($"tax/{name} is identical (two chunks, 4 MiB and 1 byte)", File.Exists(restored) && File.ReadAllBytes(restored).AsSpan().SequenceEqual(pattern));
        }
        s.Equal("the Mac deduplicated the copy: two chunks serve both big files", 2, m0Chunks(store, key, snaps[0].FileName));
        var v = Restore.Verify(store, key);
        s.Check("verify passes on the Mac's chunks", v.Clean && v.ChunksChecked > 0);
        // And a chunk we seal is one the Mac's naming agrees on: name derived the same way.
        var sample = File.ReadAllBytes(Directory.EnumerateFiles(original, "*", SearchOption.AllDirectories).First());
        var m = Restore.Open(snaps[0].FileName, store, key);
        s.Check("our chunk names match the Mac's for the same bytes", m.Files.Any(f => f.Chunks.Contains(Chunk.Name(sample, key))));
        s.Check("and for the 4 MiB piece", m.Files.Any(f => f.Chunks.Contains(Chunk.Name(pattern.AsSpan(0, Chunk.Size), key))));
        // Round trip the other way: seal here, the Mac's own blob opens with the same key, and ours has the same shape.
        var macBlob = store.GetChunk(m.Files.First(f => f.Path == "notes.txt").Chunks[0]);
        var ours = Chunk.Seal(Chunk.Open(macBlob, key), key);
        s.Equal("same blob length as the Mac's", macBlob.Length, ours.Length);
        return s;
    }

    private static int m0Chunks(IChunkStore store, MasterKey key, string snapshot)
        => Restore.Open(snapshot, store, key).Files.Where(f => f.Path.StartsWith("tax/") && f.Path.EndsWith(".bin")).SelectMany(f => f.Chunks).Distinct().Count();
}
