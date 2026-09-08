namespace Stash.Core;

public sealed record SnapshotInfo(string FileName, DateTimeOffset CreatedAt, string Host, int Files, long Bytes, int Placeholders);

public sealed class RestoreReport
{
    public int Restored;
    public long Bytes;
    public List<string> Failed { get; } = new();
}

public sealed class VerifyReport
{
    public int ChunksChecked, ChunksInCloudOnly;
    public List<string> ChunksBad { get; } = new();
    public List<string> ChunksMissing { get; } = new();
    public string? SampleFile;
    public bool? SampleOk;
    public bool Clean => ChunksBad.Count == 0 && ChunksMissing.Count == 0 && SampleOk != false;
}

/// <summary>Reading a backup back: list snapshots, restore files to a place of your choosing, and verify that every
/// chunk still opens. A chunk that fails authentication stops that file and is reported; nothing partial is written
/// where a good file should go.</summary>
public static class Restore
{
    public static List<SnapshotInfo> Snapshots(IChunkStore store, MasterKey key)
    {
        var list = new List<SnapshotInfo>();
        foreach (var name in store.ManifestNames())
        {
            try
            {
                var m = Manifest.Open(store.GetManifest(name), key);
                list.Add(new SnapshotInfo(name, m.CreatedAt, m.Host, m.Files.Count, m.TotalBytes, m.SkippedPlaceholders.Count));
            }
            catch { /* a manifest under another key, or damaged: not ours to list */ }
        }
        return list;
    }

    public static Manifest Open(string name, IChunkStore store, MasterKey key) => Manifest.Open(store.GetManifest(name), key);

    /// <summary>True when path is one of the prefixes or lives under it. Empty prefixes select everything.</summary>
    public static bool Selected(string path, IReadOnlyList<string> prefixes)
        => prefixes.Count == 0 || prefixes.Any(p => path == p || path.StartsWith(p.EndsWith('/') ? p : p + "/", StringComparison.Ordinal));

    /// <summary>Restores files from a snapshot into target/&lt;source folder name&gt;/&lt;relative path&gt;.</summary>
    public static RestoreReport Run(string snapshot, IChunkStore store, MasterKey key, string target, IReadOnlyList<string>? only = null, Action<int, int>? progress = null)
    {
        only ??= Array.Empty<string>();
        var m = Open(snapshot, store, key);
        var wanted = m.Files.Where(f => Selected(f.Path, only)).ToList();
        var report = new RestoreReport();
        for (int n = 0; n < wanted.Count; n++)
        {
            var f = wanted[n];
            var sourceName = Path.GetFileName(m.Sources[f.Source].TrimEnd('/', '\\'));
            var outPath = Path.Combine(target, sourceName, f.Path.Normalize(System.Text.NormalizationForm.FormC).Replace('/', Path.DirectorySeparatorChar));
            var tmp = outPath + ".stash-partial";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                using (var h = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                    foreach (var name in f.Chunks) h.Write(Chunk.Open(store.GetChunk(name), key));
                File.Move(tmp, outPath, overwrite: true);
                try { File.SetLastWriteTimeUtc(outPath, f.Modified.UtcDateTime); } catch { }
                report.Restored++; report.Bytes += f.Size;
            }
            catch (Exception ex)
            {
                try { File.Delete(tmp); } catch { }
                report.Failed.Add(f.Path + ": " + ex.Message);
            }
            progress?.Invoke(n + 1, wanted.Count);
        }
        return report;
    }

    /// <summary>Opens every chunk the latest snapshot references, and restores one random file to a temporary folder
    /// to prove the whole path works. Chunks evicted to the cloud are counted, not downloaded.</summary>
    public static VerifyReport Verify(IChunkStore store, MasterKey key, Action<int, int>? progress = null)
    {
        var report = new VerifyReport();
        var latest = store.ManifestNames().FirstOrDefault();
        if (latest is null) return report;
        var m = Open(latest, store, key);
        var names = m.Files.SelectMany(f => f.Chunks).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        for (int n = 0; n < names.Count; n++)
        {
            var name = names[n];
            if (!store.HasChunk(name)) { report.ChunksMissing.Add(name); }
            else if (store.IsEvicted(name)) { report.ChunksInCloudOnly++; }
            else
            {
                byte[]? blob = null;
                try { blob = store.GetChunk(name); } catch { report.ChunksMissing.Add(name); }
                if (blob is not null)
                {
                    try { Chunk.Open(blob, key); } catch { report.ChunksBad.Add(name); }
                    report.ChunksChecked++;
                }
            }
            progress?.Invoke(n + 1, names.Count);
        }
        var candidates = m.Files.Where(f => f.Chunks.All(c => !store.IsEvicted(c))).ToList();
        if (candidates.Count > 0)
        {
            var pick = candidates[System.Security.Cryptography.RandomNumberGenerator.GetInt32(candidates.Count)];
            report.SampleFile = pick.Path;
            var tmp = Path.Combine(Path.GetTempPath(), "stash-verify-" + Guid.NewGuid().ToString("N"));
            try
            {
                var r = Run(latest, store, key, tmp, new[] { pick.Path });
                var sourceName = Path.GetFileName(m.Sources[pick.Source].TrimEnd('/', '\\'));
                var restored = Path.Combine(tmp, sourceName, pick.Path.Normalize(System.Text.NormalizationForm.FormC).Replace('/', Path.DirectorySeparatorChar));
                long size = File.Exists(restored) ? new FileInfo(restored).Length : -1;
                report.SampleOk = r.Failed.Count == 0 && size == pick.Size;
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }
        return report;
    }
}
