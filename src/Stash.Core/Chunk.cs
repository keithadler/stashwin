using System.Security.Cryptography;
using System.Text;

namespace Stash.Core;

public sealed class ChunkException : Exception
{
    public bool NotAChunk { get; }
    public ChunkException(bool notAChunk, string message) : base(message) { NotAChunk = notAChunk; }
    public static ChunkException NotChunk() => new(true, "This file is not a Stash chunk.");
    public static ChunkException CorruptOrWrongKey() => new(false, "The chunk is damaged or was made with a different key. Nothing was written.");
}

/// <summary>Chunks are what the provider stores: same-shaped encrypted blobs named by an HMAC of their plaintext,
/// so identical content dedupes within one stash but nobody without the key can tell what a blob is or whether
/// two people stored the same file. Every blob is authenticated; tampering is detected on restore, never written to disk.</summary>
public static class Chunk
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("STSH1");
    /// <summary>Content-defined boundaries would be better for edited documents; fixed 4 MB is the honest version 1 and still dedupes identical files exactly.</summary>
    public const int Size = 4 * 1024 * 1024;

    /// <summary>Provider-visible name for a chunk: HMAC-SHA256 of the plaintext under the naming key, hex.</summary>
    public static string Name(ReadOnlySpan<byte> plaintext, MasterKey key)
        => Convert.ToHexString(HMACSHA256.HashData(key.NameKey, plaintext)).ToLowerInvariant();

    /// <summary>magic || 12-byte nonce || ciphertext || 16-byte tag. Random nonce per seal.</summary>
    public static byte[] Seal(ReadOnlySpan<byte> plaintext, MasterKey key)
    {
        var box = ChaChaPoly.Seal(plaintext, key.ChunkKey);
        var blob = new byte[Magic.Length + box.Length];
        Magic.CopyTo(blob, 0);
        box.CopyTo(blob, Magic.Length);
        return blob;
    }

    public static byte[] Open(ReadOnlySpan<byte> blob, MasterKey key)
    {
        if (blob.Length < Magic.Length + 28 || !blob[..Magic.Length].SequenceEqual(Magic)) throw ChunkException.NotChunk();
        try { return ChaChaPoly.Open(blob[Magic.Length..], key.ChunkKey); }
        catch (CryptographicException) { throw ChunkException.CorruptOrWrongKey(); }
    }

    public static List<byte[]> Split(byte[] data)
    {
        var parts = new List<byte[]>();
        if (data.Length == 0) { parts.Add(Array.Empty<byte>()); return parts; }
        for (int off = 0; off < data.Length; off += Size) parts.Add(data.AsSpan(off, Math.Min(Size, data.Length - off)).ToArray());
        return parts;
    }
}
