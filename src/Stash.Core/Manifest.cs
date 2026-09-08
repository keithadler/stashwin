using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stash.Core;

/// <summary>A snapshot manifest: every file in the chosen folders at one moment, with the chunk names that rebuild it.
/// Stored encrypted under the manifest key, so the provider never sees a file name. The JSON is the Mac app's, field for field.</summary>
public sealed class Manifest
{
    public sealed class Entry
    {
        [JsonPropertyName("source")] public int Source { get; set; }
        /// <summary>Relative to the source root, always with forward slashes, as the Mac writes it.</summary>
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("modified")] public DateTimeOffset Modified { get; set; }
        [JsonPropertyName("chunks")] public List<string> Chunks { get; set; } = new();
    }

    [JsonPropertyName("format")] public int Format { get; set; } = 1;
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("host")] public string Host { get; set; } = HostOverride ?? Environment.MachineName;
    [JsonPropertyName("sources")] public List<string> Sources { get; set; } = new();
    [JsonPropertyName("files")] public List<Entry> Files { get; set; } = new();
    [JsonPropertyName("skippedPlaceholders")] public List<string> SkippedPlaceholders { get; set; } = new();

    [JsonIgnore] public long TotalBytes => Files.Sum(f => f.Size);
    /// <summary>Screenshots and tests use a sample name instead of the real PC's.</summary>
    public static string? HostOverride { get; set; }

    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("STSM1");

    /// <summary>Swift's ISO 8601 encoder writes whole seconds in UTC with a Z; we read anything ISO and write the same.</summary>
    private sealed class Iso8601 : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
            => DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions o)
            => writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }

    public static readonly JsonSerializerOptions Json = new() { Converters = { new Iso8601() }, WriteIndented = false };

    public byte[] Sealed(MasterKey key)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(this, Json);
        var box = ChaChaPoly.Seal(json, key.ManifestKey);
        var blob = new byte[Magic.Length + box.Length];
        Magic.CopyTo(blob, 0);
        box.CopyTo(blob, Magic.Length);
        return blob;
    }

    public static Manifest Open(ReadOnlySpan<byte> blob, MasterKey key)
    {
        if (blob.Length < Magic.Length || !blob[..Magic.Length].SequenceEqual(Magic)) throw ChunkException.NotChunk();
        byte[] plain;
        try { plain = ChaChaPoly.Open(blob[Magic.Length..], key.ManifestKey); }
        catch (CryptographicException) { throw ChunkException.CorruptOrWrongKey(); }
        return JsonSerializer.Deserialize<Manifest>(plain, Json) ?? throw ChunkException.CorruptOrWrongKey();
    }

    /// <summary>File name at the destination: sortable, no information about contents. UTC, milliseconds, like the Mac.</summary>
    public string FileName => CreatedAt.ToUniversalTime().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".stsm";
}

/// <summary>Where things live inside a destination folder: one subtree per key fingerprint, so one drive can hold
/// several stashes and a wrong card never overwrites the right one. The folder is called "Stash for Mac" because the
/// Mac app created the format; keeping the name is what lets one card restore on either.</summary>
public sealed class Layout
{
    public const string FolderName = "Stash for Mac";
    public string Root { get; }
    public string Chunks => Path.Combine(Root, "chunks");
    public string Manifests => Path.Combine(Root, "manifests");

    public Layout(string destination, MasterKey key) => Root = Path.Combine(destination, FolderName, key.Fingerprint);

    public string ChunkPath(string name) => Path.Combine(Chunks, name[..2], name);

    public void Prepare()
    {
        Directory.CreateDirectory(Chunks);
        Directory.CreateDirectory(Manifests);
        var note = Path.Combine(Root, "README.txt");
        if (!File.Exists(note))
            File.WriteAllText(note,
                "This folder is a Stash backup. Every file in it is encrypted; nothing here can be read\n" +
                "without the 24-word recovery card that was shown when the backup was set up.\n" +
                "To restore: install Stash for Windows (github.com/keithadler/stashwin) or Stash for Mac\n" +
                "(github.com/keithadler/stashmac), enter the card, choose this folder.\n");
    }

    /// <summary>Manifest file names, newest first.</summary>
    public List<string> ManifestFiles()
    {
        if (!Directory.Exists(Manifests)) return new List<string>();
        return Directory.GetFiles(Manifests, "*.stsm").Select(Path.GetFileName).Where(n => n is not null).Select(n => n!)
            .OrderByDescending(n => n, StringComparer.Ordinal).ToList();
    }
}
