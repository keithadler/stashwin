using System.Text.RegularExpressions;

namespace Stash.Core;

public sealed class BackupReport
{
    public int Files, NewChunks, ReusedChunks, SkippedPlaceholders, SkippedByRule;
    public long Bytes, NewBytes;
    public List<string> Unreadable { get; } = new();
    public string Manifest = "";
}

/// <summary>What to back up and the rules. Plain values; the key is never here.</summary>
public sealed class Rules
{
    public static readonly string[] DefaultExcludes = { "*.tmp", "*.part", "*.crdownload", "Cache", "Caches", ".cache", "*.vmdk", "*.vdi", "*.vhdx" };
    public List<string> Excludes { get; set; } = DefaultExcludes.ToList();
    /// <summary>0 means no cap.</summary>
    public long MaxFileBytes { get; set; }
}

/// <summary>The backup itself: walk the chosen folders, split each file into chunks, upload the ones the destination
/// does not have, write the manifest. Streams files in 4 MB pieces so a 20 GB video never sits in memory. Cloud
/// placeholders are listed and skipped; reading one would make the provider download it behind the user's back.</summary>
public static class Backup
{
    /// <summary>Names never backed up: OS clutter and things with their own copies.</summary>
    public static readonly HashSet<string> IgnoredNames = new(StringComparer.OrdinalIgnoreCase)
        { "desktop.ini", "Thumbs.db", ".DS_Store", "$RECYCLE.BIN", "System Volume Information", "node_modules", ".git" };

    /// <summary>Shell-style pattern match on a file or folder name (*.tmp, Cache*, *.vhdx), case-insensitive.</summary>
    public static bool Matches(string name, string pattern)
    {
        var rx = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(name, rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public sealed record Walked(string Rel, string Path, long Size, DateTimeOffset Modified);

    /// <summary>Regular files under a root, relative paths (forward slashes) sorted, placeholders separated out.
    /// Never follows reparse points (symlinks, junctions). Excluded names and oversize files are counted in skipped.</summary>
    public static (List<Walked> Files, List<string> Placeholders, int Skipped) Walk(string root, Rules rules)
    {
        var files = new List<Walked>();
        var placeholders = new List<string>();
        int skipped = 0;
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var stack = new Stack<string>();
        stack.Push(rootFull);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(dir).EnumerateFileSystemInfos(); }
            catch { continue; }
            foreach (var e in entries)
            {
                var name = e.Name;
                if (IgnoredNames.Contains(name)) continue;
                if (rules.Excludes.Any(p => Matches(name, p))) { skipped++; continue; }
                if ((e.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (e is DirectoryInfo) { stack.Push(e.FullName); continue; }
                if (e is not FileInfo fi) continue;
                var rel = fi.FullName[(rootFull.Length + 1)..].Replace('\\', '/').Normalize(System.Text.NormalizationForm.FormC); // NTFS keeps NFC and NFD apart; the Mac treats them as one name
                if (FolderStore.IsDataless(fi.FullName)) { placeholders.Add(rel); continue; }
                if (rules.MaxFileBytes > 0 && fi.Length > rules.MaxFileBytes) { skipped++; continue; }
                files.Add(new Walked(rel, fi.FullName, fi.Length, new DateTimeOffset(fi.LastWriteTimeUtc, TimeSpan.Zero)));
            }
        }
        files.Sort((a, b) => string.CompareOrdinal(a.Rel, b.Rel));
        return (files, placeholders, skipped);
    }

    public static BackupReport Run(IReadOnlyList<string> sources, string destination, MasterKey key, Rules? rules = null, Action<int, int, long>? progress = null)
        => Run(sources, new FolderStore(destination, key), key, rules, progress);

    /// <summary>Runs one backup of every source into a chunk store. Progress gets (files done, files total, bytes done).</summary>
    public static BackupReport Run(IReadOnlyList<string> sources, IChunkStore store, MasterKey key, Rules? rules = null, Action<int, int, long>? progress = null)
    {
        rules ??= new Rules();
        store.Prepare();
        var report = new BackupReport();
        var manifest = new Manifest { Sources = sources.Select(s => Path.GetFullPath(s)).ToList() };
        var walked = new List<(int Source, Walked File)>();
        for (int i = 0; i < sources.Count; i++)
        {
            var (files, placeholders, skipped) = Walk(sources[i], rules);
            walked.AddRange(files.Select(f => (i, f)));
            var srcName = Path.GetFileName(Path.GetFullPath(sources[i]).TrimEnd(Path.DirectorySeparatorChar));
            manifest.SkippedPlaceholders.AddRange(placeholders.Select(p => srcName + "/" + p));
            report.SkippedByRule += skipped;
        }
        report.SkippedPlaceholders = manifest.SkippedPlaceholders.Count;
        long done = 0;
        var buffer = new byte[Chunk.Size];
        for (int n = 0; n < walked.Count; n++)
        {
            var (source, f) = walked[n];
            FileStream h;
            try { h = new FileStream(f.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16); }
            catch { report.Unreadable.Add(f.Rel); continue; }
            var names = new List<string>();
            using (h)
            {
                bool readAny = false;
                while (true)
                {
                    int got = 0;
                    while (got < buffer.Length)
                    {
                        int r = h.Read(buffer, got, buffer.Length - got);
                        if (r == 0) break;
                        got += r;
                    }
                    if (got == 0 && readAny) break;
                    readAny = true;
                    var piece = buffer.AsSpan(0, got);
                    var name = Chunk.Name(piece, key);
                    if (store.HasChunk(name)) report.ReusedChunks++;
                    else
                    {
                        var blob = Chunk.Seal(piece, key);
                        store.PutChunk(name, blob);
                        report.NewChunks++; report.NewBytes += blob.Length;
                    }
                    names.Add(name);
                    done += got;
                    if (got < buffer.Length) break;
                }
            }
            manifest.Files.Add(new Manifest.Entry { Source = source, Path = f.Rel, Size = f.Size, Modified = f.Modified, Chunks = names });
            report.Files++; report.Bytes += f.Size;
            progress?.Invoke(n + 1, walked.Count, done);
        }
        // Two backups in the same millisecond still get distinct names.
        var fileName = manifest.FileName;
        var existing = new HashSet<string>(store.ManifestNames(), StringComparer.Ordinal);
        int k = 1;
        while (existing.Contains(fileName)) fileName = manifest.FileName.Replace(".stsm", $"-{k++}.stsm");
        store.PutManifest(fileName, manifest.Sealed(key));
        report.Manifest = fileName;
        return report;
    }
}
