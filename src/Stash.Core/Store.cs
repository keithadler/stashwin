namespace Stash.Core;

/// <summary>Where chunks and manifests live. Backup, restore, verify and prune speak only to this, so a provider API
/// store can be added without touching any of them. The one implementation is a folder, which already covers every
/// provider that has a desktop app.</summary>
public interface IChunkStore
{
    string Name { get; }
    void Prepare();
    bool HasChunk(string name);
    /// <summary>The chunk exists but its bytes are not on this PC (a cloud folder with Files On-Demand).</summary>
    bool IsEvicted(string name);
    /// <summary>When the chunk was written, for prune's grace period.</summary>
    DateTimeOffset? ChunkDate(string name);
    void PutChunk(string name, byte[] data);
    byte[] GetChunk(string name);
    void DeleteChunk(string name);
    List<string> ChunkNames();
    long ChunkSize(string name);
    /// <summary>Newest first.</summary>
    List<string> ManifestNames();
    void PutManifest(string name, byte[] data);
    byte[] GetManifest(string name);
    void DeleteManifest(string name);
    long TotalSize();
}

/// <summary>A folder: local disk, NAS, or the folder a cloud provider's desktop app syncs.</summary>
public sealed class FolderStore : IChunkStore
{
    public Layout Layout { get; }
    public string Destination { get; }
    public string Name => Path.GetFileName(Destination.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>Platform hook: is this path a cloud placeholder whose bytes are not local? The app installs the Windows check.</summary>
    public static Func<string, bool> IsDataless { get; set; } = _ => false;

    public FolderStore(string destination, MasterKey key)
    {
        Destination = destination;
        Layout = new Layout(destination, key);
    }

    public void Prepare() => Layout.Prepare();
    public bool HasChunk(string name) => File.Exists(Layout.ChunkPath(name));
    public bool IsEvicted(string name) => IsDataless(Layout.ChunkPath(name));
    public DateTimeOffset? ChunkDate(string name)
    {
        var p = Layout.ChunkPath(name);
        return File.Exists(p) ? new DateTimeOffset(File.GetLastWriteTimeUtc(p), TimeSpan.Zero) : null;
    }

    public void PutChunk(string name, byte[] data)
    {
        var path = Layout.ChunkPath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteAtomic(path, data);
    }

    public byte[] GetChunk(string name) => File.ReadAllBytes(Layout.ChunkPath(name));

    public void DeleteChunk(string name)
    {
        var path = Layout.ChunkPath(name);
        File.Delete(path);
        var dir = Path.GetDirectoryName(path)!;
        try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
    }

    public List<string> ChunkNames()
    {
        if (!Directory.Exists(Layout.Chunks)) return new List<string>();
        return Directory.GetDirectories(Layout.Chunks).SelectMany(d => Directory.GetFiles(d)).Select(Path.GetFileName)
            .Where(n => n is not null && n.Length == 64).Select(n => n!).ToList();
    }

    public long ChunkSize(string name)
    {
        var p = Layout.ChunkPath(name);
        return File.Exists(p) ? new FileInfo(p).Length : 0;
    }

    public List<string> ManifestNames() => Layout.ManifestFiles();
    public void PutManifest(string name, byte[] data) => WriteAtomic(Path.Combine(Layout.Manifests, name), data);
    public byte[] GetManifest(string name) => File.ReadAllBytes(Path.Combine(Layout.Manifests, name));
    public void DeleteManifest(string name) => File.Delete(Path.Combine(Layout.Manifests, name));

    public long TotalSize()
    {
        if (!Directory.Exists(Layout.Root)) return 0;
        long total = 0;
        foreach (var f in Directory.EnumerateFiles(Layout.Root, "*", SearchOption.AllDirectories))
            try { total += new FileInfo(f).Length; } catch { }
        return total;
    }

    /// <summary>Write to a sibling temp file, then move into place, so a sync client never sees a half-written blob.</summary>
    private static void WriteAtomic(string path, byte[] data)
    {
        var tmp = path + ".stash-partial";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }
}
