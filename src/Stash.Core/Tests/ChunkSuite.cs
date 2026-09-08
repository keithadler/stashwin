namespace Stash.Core.Tests;

public static class ChunkSuite
{
    public static Suite Run()
    {
        var s = new Suite("chunk");
        var k = MasterKey.Random();
        var plain = Enumerable.Range(0, 10_000).Select(i => (byte)(i % 251)).ToArray();
        var blob = Chunk.Seal(plain, k);
        s.Check("magic", blob.AsSpan(0, 5).SequenceEqual(System.Text.Encoding.ASCII.GetBytes("STSH1")));
        s.Equal("overhead is magic + nonce + tag", plain.Length + 5 + 12 + 16, blob.Length);
        s.Check("round trip", Chunk.Open(blob, k).AsSpan().SequenceEqual(plain));
        s.Check("fresh nonce every time", !Chunk.Seal(plain, k).AsSpan().SequenceEqual(blob));

        var other = MasterKey.Random();
        var secret = Chunk.Seal(System.Text.Encoding.UTF8.GetBytes("secret"), k);
        s.Throws<ChunkException>("other key fails", () => Chunk.Open(secret, other), e => !e.NotAChunk);
        var tampered = (byte[])secret.Clone(); tampered[^3] ^= 1;
        s.Throws<ChunkException>("flipped tag bit fails", () => Chunk.Open(tampered, k));
        var body = (byte[])secret.Clone(); body[10] ^= 1;
        s.Throws<ChunkException>("ciphertext change fails", () => Chunk.Open(body, k));
        s.Throws<ChunkException>("junk is not a chunk", () => Chunk.Open(System.Text.Encoding.UTF8.GetBytes("hello"), k), e => e.NotAChunk);

        var a = System.Text.Encoding.UTF8.GetBytes("the same content");
        s.Equal("same content, same name", Chunk.Name(a, k), Chunk.Name(a, k));
        s.Check("different key, different name", Chunk.Name(a, k) != Chunk.Name(a, other));
        s.Check("different content, different name", Chunk.Name(a, k) != Chunk.Name(System.Text.Encoding.UTF8.GetBytes("the same content!"), k));
        s.Equal("64 hex chars", 64, Chunk.Name(a, k).Length);

        var big = new byte[Chunk.Size * 2 + 123];
        var parts = Chunk.Split(big);
        s.Equal("two full chunks and a tail", 3, parts.Count);
        s.Equal("tail size", 123, parts[^1].Length);
        s.Equal("empty file is one empty chunk", 1, Chunk.Split(Array.Empty<byte>()).Count);

        // Manifest seal/open with the Mac's JSON shape.
        var m = new Manifest { Host = "SAMS-PC", Sources = new List<string> { "C:\\Users\\sam\\Documents" } };
        m.Files.Add(new Manifest.Entry { Source = 0, Path = "tax/2025.pdf", Size = 3, Modified = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), Chunks = new List<string> { Chunk.Name(new byte[] { 1, 2, 3 }, k) } });
        var json = System.Text.Encoding.UTF8.GetString(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(m, Manifest.Json));
        s.Check("manifest json uses the Mac field names", json.Contains("\"createdAt\"") && json.Contains("\"skippedPlaceholders\"") && json.Contains("\"modified\":\"2026-01-02T03:04:05Z\""));
        var sealedM = m.Sealed(k);
        s.Check("manifest magic", sealedM.AsSpan(0, 5).SequenceEqual(System.Text.Encoding.ASCII.GetBytes("STSM1")));
        var back = Manifest.Open(sealedM, k);
        s.Check("manifest round trip", back.Files.Count == 1 && back.Files[0].Path == "tax/2025.pdf" && back.Host == "SAMS-PC" && back.Files[0].Modified == m.Files[0].Modified);
        s.Throws<ChunkException>("manifest under another key is refused", () => Manifest.Open(sealedM, other));
        s.Check("manifest file name is sortable UTC", System.Text.RegularExpressions.Regex.IsMatch(m.FileName, @"^\d{8}-\d{6}-\d{3}\.stsm$"));
        return s;
    }
}
