using System.Security.Cryptography;
using System.Text;

namespace Stash.Core;

/// <summary>One random 256-bit master key per stash. Everything else is derived from it with HKDF, so the
/// 24 words recover all of it: the chunk-encryption key, the chunk-naming key, and the manifest key.
/// Byte-for-byte the same derivation as Stash for Mac, so one card serves both.</summary>
public sealed class MasterKey : IEquatable<MasterKey>
{
    public byte[] Entropy { get; }
    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("stashmac-v1");

    public MasterKey(byte[] entropy)
    {
        if (entropy.Length != 32) throw new ArgumentException("a master key is 32 bytes");
        Entropy = (byte[])entropy.Clone();
    }

    public static MasterKey Random() => new(RandomNumberGenerator.GetBytes(32));
    public static MasterKey FromWords(string words) => new(Mnemonic.Entropy(words));
    public string[] Words => Mnemonic.Words(Entropy);

    private byte[] Derive(string purpose) => HKDF.DeriveKey(HashAlgorithmName.SHA256, Entropy, 32, Salt, Encoding.UTF8.GetBytes(purpose));
    public byte[] ChunkKey => Derive("chunk-encryption");
    public byte[] NameKey => Derive("chunk-naming");
    public byte[] ManifestKey => Derive("manifest-encryption");

    /// <summary>Short, safe-to-show identifier of the key (first 8 hex of a hash), so two cards can be told apart.</summary>
    public string Fingerprint
    {
        get
        {
            var input = Encoding.UTF8.GetBytes("fingerprint").Concat(Entropy).ToArray();
            return Convert.ToHexString(SHA256.HashData(input).AsSpan(0, 4)).ToLowerInvariant();
        }
    }

    /// <summary>What the QR code carries: the same versioned URI as the Mac app, so a card scans on either.</summary>
    public string QrPayload => "stashmac:key/v1/" + Convert.ToBase64String(Entropy);
    public static MasterKey? FromQrPayload(string s)
    {
        const string prefix = "stashmac:key/v1/";
        if (!s.StartsWith(prefix, StringComparison.Ordinal)) return null;
        try { var d = Convert.FromBase64String(s[prefix.Length..]); return d.Length == 32 ? new MasterKey(d) : null; }
        catch { return null; }
    }

    public bool Equals(MasterKey? other) => other is not null && CryptographicOperations.FixedTimeEquals(Entropy, other.Entropy);
    public override bool Equals(object? obj) => Equals(obj as MasterKey);
    public override int GetHashCode() => BitConverter.ToInt32(SHA256.HashData(Entropy), 0);
}

/// <summary>The key as 24 words (BIP-39 encoding of 256 bits of entropy plus an 8-bit checksum). The words ARE
/// the key: no passphrase, nothing to guess, nothing to forget except the card. A wrong word is caught by the
/// checksum before any restore starts.</summary>
public static class Mnemonic
{
    public sealed class WordsException : Exception
    {
        public enum Kind { WordCount, UnknownWord, Checksum }
        public Kind Reason { get; }
        public string? Word { get; }
        public int Count { get; }
        public WordsException(Kind reason, string message, string? word = null, int count = 0) : base(message) { Reason = reason; Word = word; Count = count; }
    }

    public static string[] Words(byte[] entropy)
    {
        if (entropy.Length != 32) throw new ArgumentException("24-word keys encode exactly 256 bits");
        var hash = SHA256.HashData(entropy);
        var bits = new List<bool>(264);
        foreach (var b in entropy) for (int i = 7; i >= 0; i--) bits.Add(((b >> i) & 1) == 1);
        for (int i = 7; i >= 0; i--) bits.Add(((hash[0] >> i) & 1) == 1);
        var words = new string[24];
        for (int w = 0; w < 24; w++)
        {
            int idx = 0;
            for (int i = 0; i < 11; i++) idx = (idx << 1) | (bits[w * 11 + i] ? 1 : 0);
            words[w] = Wordlist.English[idx];
        }
        return words;
    }

    /// <summary>24 words → 32 bytes, verifying the checksum. Case and extra spaces are forgiven.</summary>
    public static byte[] Entropy(string text)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetter(ch)) sb.Append(ch);
            else if (sb.Length > 0) { words.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) words.Add(sb.ToString());
        if (words.Count != 24) throw new WordsException(WordsException.Kind.WordCount, $"A recovery key has 24 words; this has {words.Count}.", count: words.Count);
        var bits = new List<bool>(264);
        foreach (var w in words)
        {
            int idx = Wordlist.IndexOf(w);
            if (idx < 0) throw new WordsException(WordsException.Kind.UnknownWord, $"\"{w}\" is not a recovery-key word. Check the spelling.", w);
            for (int i = 10; i >= 0; i--) bits.Add(((idx >> i) & 1) == 1);
        }
        var entropy = new byte[32];
        for (int i = 0; i < 256; i++) if (bits[i]) entropy[i / 8] |= (byte)(1 << (7 - i % 8));
        byte expected = SHA256.HashData(entropy)[0];
        byte got = 0;
        for (int i = 256; i < 264; i++) got = (byte)((got << 1) | (bits[i] ? 1 : 0));
        if (expected != got) throw new WordsException(WordsException.Kind.Checksum, "The words don't add up. One of them is probably wrong.");
        return entropy;
    }

    /// <summary>Words numbered in four columns, for the recovery card and the console.</summary>
    public static string Card(string[] words)
    {
        var lines = new List<string>();
        for (int row = 0; row < words.Length; row += 4)
            lines.Add(string.Join("  ", Enumerable.Range(row, Math.Min(4, words.Length - row)).Select(i => $"{i + 1,2}. {words[i],-10}")));
        return string.Join("\n", lines);
    }
}
