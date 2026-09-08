using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Stash.Core;

/// <summary>ChaCha20-Poly1305 (RFC 8439), in managed code so every platform seals and opens the same bytes.
/// The output is exactly what CryptoKit's ChaChaPoly produces on the Mac: 12-byte nonce, ciphertext, 16-byte tag.</summary>
public static class ChaChaPoly
{
    public const int NonceSize = 12, TagSize = 16, KeySize = 32;

    /// <summary>The operating system's implementation is used when it exists (Windows 10 20H1 and later, Linux); the managed one
    /// below is the fallback and the reference the self-test compares it against. Same bytes either way.</summary>
    public static bool UsePlatform { get; set; } = System.Security.Cryptography.ChaCha20Poly1305.IsSupported;

    /// <summary>nonce || ciphertext || tag, with a fresh random nonce.</summary>
    public static byte[] Seal(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize) throw new ArgumentException("key must be 32 bytes");
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var output = new byte[NonceSize + plaintext.Length + TagSize];
        nonce.CopyTo(output, 0);
        var ct = output.AsSpan(NonceSize, plaintext.Length);
        var tagSpan = output.AsSpan(NonceSize + plaintext.Length, TagSize);
        if (UsePlatform)
        {
            using var aead = new System.Security.Cryptography.ChaCha20Poly1305(key);
            aead.Encrypt(nonce, plaintext, ct, tagSpan);
            return output;
        }
        ChaCha20(key, nonce, 1, plaintext, ct);
        Tag(key, nonce, ct).CopyTo(tagSpan);
        return output;
    }

    /// <summary>Opens nonce || ciphertext || tag. Throws CryptographicException on any tampering or a wrong key.</summary>
    public static byte[] Open(ReadOnlySpan<byte> combined, ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize) throw new ArgumentException("key must be 32 bytes");
        if (combined.Length < NonceSize + TagSize) throw new CryptographicException("too short");
        var nonce = combined[..NonceSize];
        var ct = combined[NonceSize..^TagSize];
        var tag = combined[^TagSize..];
        var plain = new byte[ct.Length];
        if (UsePlatform)
        {
            using var aead = new System.Security.Cryptography.ChaCha20Poly1305(key);
            aead.Decrypt(nonce, ct, tag, plain); // throws AuthenticationTagMismatchException, a CryptographicException
            return plain;
        }
        var expected = Tag(key, nonce, ct);
        if (!CryptographicOperations.FixedTimeEquals(expected, tag)) throw new CryptographicException("authentication failed");
        ChaCha20(key, nonce, 1, ct, plain);
        return plain;
    }

    private static byte[] Tag(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ct)
    {
        Span<byte> otk = stackalloc byte[64];
        Span<byte> zero = stackalloc byte[64];
        ChaCha20(key, nonce, 0, zero, otk); // poly key = first 32 bytes of block 0
        // AEAD construction with empty AAD: pad16(ct) || len(aad)=0 || len(ct)
        int pad = (16 - ct.Length % 16) % 16;
        var mac = new byte[ct.Length + pad + 16];
        ct.CopyTo(mac);
        BinaryPrimitives.WriteUInt64LittleEndian(mac.AsSpan(ct.Length + pad), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(mac.AsSpan(ct.Length + pad + 8), (ulong)ct.Length);
        return Poly1305(otk[..32], mac);
    }

    // ---- ChaCha20 block function and stream ----

    private static void QuarterRound(Span<uint> s, int a, int b, int c, int d)
    {
        s[a] += s[b]; s[d] ^= s[a]; s[d] = uint.RotateLeft(s[d], 16);
        s[c] += s[d]; s[b] ^= s[c]; s[b] = uint.RotateLeft(s[b], 12);
        s[a] += s[b]; s[d] ^= s[a]; s[d] = uint.RotateLeft(s[d], 8);
        s[c] += s[d]; s[b] ^= s[c]; s[b] = uint.RotateLeft(s[b], 7);
    }

    private static void Block(ReadOnlySpan<uint> input, Span<byte> output64)
    {
        Span<uint> x = stackalloc uint[16];
        input.CopyTo(x);
        for (int i = 0; i < 10; i++)
        {
            QuarterRound(x, 0, 4, 8, 12); QuarterRound(x, 1, 5, 9, 13); QuarterRound(x, 2, 6, 10, 14); QuarterRound(x, 3, 7, 11, 15);
            QuarterRound(x, 0, 5, 10, 15); QuarterRound(x, 1, 6, 11, 12); QuarterRound(x, 2, 7, 8, 13); QuarterRound(x, 3, 4, 9, 14);
        }
        for (int i = 0; i < 16; i++) BinaryPrimitives.WriteUInt32LittleEndian(output64.Slice(i * 4, 4), x[i] + input[i]);
    }

    /// <summary>XORs <paramref name="input"/> with the ChaCha20 keystream into <paramref name="output"/> (same length).</summary>
    public static void ChaCha20(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, uint counter, ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (output.Length < input.Length) throw new ArgumentException("output too small");
        Span<uint> state = stackalloc uint[16];
        state[0] = 0x61707865; state[1] = 0x3320646e; state[2] = 0x79622d32; state[3] = 0x6b206574;
        for (int i = 0; i < 8; i++) state[4 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key.Slice(i * 4, 4));
        state[12] = counter;
        state[13] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[..4]);
        state[14] = BinaryPrimitives.ReadUInt32LittleEndian(nonce.Slice(4, 4));
        state[15] = BinaryPrimitives.ReadUInt32LittleEndian(nonce.Slice(8, 4));
        Span<byte> ks = stackalloc byte[64];
        int off = 0;
        while (off < input.Length)
        {
            Block(state, ks);
            state[12]++;
            int n = Math.Min(64, input.Length - off);
            for (int i = 0; i < n; i++) output[off + i] = (byte)(input[off + i] ^ ks[i]);
            off += n;
        }
    }

    // ---- Poly1305 ----

    public static byte[] Poly1305(ReadOnlySpan<byte> key32, ReadOnlySpan<byte> msg)
    {
        // r clamped, s as-is; arithmetic in 130-bit with UInt128 pieces via BigInteger-free limbs.
        ulong t0 = BinaryPrimitives.ReadUInt64LittleEndian(key32[..8]);
        ulong t1 = BinaryPrimitives.ReadUInt64LittleEndian(key32.Slice(8, 8));
        // 26-bit limbs of r
        uint r0 = (uint)(t0 & 0x3ffffff);
        uint r1 = (uint)((t0 >> 26) & 0x3ffff03);
        uint r2 = (uint)((t0 >> 52 | t1 << 12) & 0x3ffc0ff);
        uint r3 = (uint)((t1 >> 14) & 0x3f03fff);
        uint r4 = (uint)((t1 >> 40) & 0x00fffff);
        uint s1 = r1 * 5, s2 = r2 * 5, s3 = r3 * 5, s4 = r4 * 5;
        uint h0 = 0, h1 = 0, h2 = 0, h3 = 0, h4 = 0;
        Span<byte> block = stackalloc byte[16];
        int off = 0;
        while (off < msg.Length)
        {
            int n = Math.Min(16, msg.Length - off);
            block.Clear();
            msg.Slice(off, n).CopyTo(block);
            uint hibit;
            if (n == 16) hibit = 1u << 24; else { block[n] = 1; hibit = 0; }
            ulong b0 = BinaryPrimitives.ReadUInt64LittleEndian(block[..8]);
            ulong b1 = BinaryPrimitives.ReadUInt64LittleEndian(block.Slice(8, 8));
            h0 += (uint)(b0 & 0x3ffffff);
            h1 += (uint)((b0 >> 26) & 0x3ffffff);
            h2 += (uint)((b0 >> 52 | b1 << 12) & 0x3ffffff);
            h3 += (uint)((b1 >> 14) & 0x3ffffff);
            h4 += (uint)(b1 >> 40) | hibit;
            ulong d0 = (ulong)h0 * r0 + (ulong)h1 * s4 + (ulong)h2 * s3 + (ulong)h3 * s2 + (ulong)h4 * s1;
            ulong d1 = (ulong)h0 * r1 + (ulong)h1 * r0 + (ulong)h2 * s4 + (ulong)h3 * s3 + (ulong)h4 * s2;
            ulong d2 = (ulong)h0 * r2 + (ulong)h1 * r1 + (ulong)h2 * r0 + (ulong)h3 * s4 + (ulong)h4 * s3;
            ulong d3 = (ulong)h0 * r3 + (ulong)h1 * r2 + (ulong)h2 * r1 + (ulong)h3 * r0 + (ulong)h4 * s4;
            ulong d4 = (ulong)h0 * r4 + (ulong)h1 * r3 + (ulong)h2 * r2 + (ulong)h3 * r1 + (ulong)h4 * r0;
            uint c = (uint)(d0 >> 26); h0 = (uint)d0 & 0x3ffffff; d1 += c;
            c = (uint)(d1 >> 26); h1 = (uint)d1 & 0x3ffffff; d2 += c;
            c = (uint)(d2 >> 26); h2 = (uint)d2 & 0x3ffffff; d3 += c;
            c = (uint)(d3 >> 26); h3 = (uint)d3 & 0x3ffffff; d4 += c;
            c = (uint)(d4 >> 26); h4 = (uint)d4 & 0x3ffffff; h0 += c * 5;
            c = h0 >> 26; h0 &= 0x3ffffff; h1 += c;
            off += n;
        }
        // full carry
        uint cc = h1 >> 26; h1 &= 0x3ffffff; h2 += cc; cc = h2 >> 26; h2 &= 0x3ffffff; h3 += cc; cc = h3 >> 26; h3 &= 0x3ffffff; h4 += cc;
        cc = h4 >> 26; h4 &= 0x3ffffff; h0 += cc * 5; cc = h0 >> 26; h0 &= 0x3ffffff; h1 += cc;
        // compute h + -p
        uint g0 = h0 + 5; cc = g0 >> 26; g0 &= 0x3ffffff;
        uint g1 = h1 + cc; cc = g1 >> 26; g1 &= 0x3ffffff;
        uint g2 = h2 + cc; cc = g2 >> 26; g2 &= 0x3ffffff;
        uint g3 = h3 + cc; cc = g3 >> 26; g3 &= 0x3ffffff;
        uint g4 = h4 + cc - (1u << 26);
        uint mask = (g4 >> 31) - 1; // all ones if g4 >= 0 (no borrow), i.e. h >= p
        g0 &= mask; g1 &= mask; g2 &= mask; g3 &= mask; g4 &= mask;
        mask = ~mask;
        h0 = (h0 & mask) | g0; h1 = (h1 & mask) | g1; h2 = (h2 & mask) | g2; h3 = (h3 & mask) | g3; h4 = (h4 & mask) | g4;
        ulong f0 = (h0 | (ulong)h1 << 26) & 0xffffffff;
        ulong f1 = (h1 >> 6 | (ulong)h2 << 20) & 0xffffffff;
        ulong f2 = (h2 >> 12 | (ulong)h3 << 14) & 0xffffffff;
        ulong f3 = (h3 >> 18 | (ulong)h4 << 8) & 0xffffffff;
        ulong k0 = BinaryPrimitives.ReadUInt32LittleEndian(key32.Slice(16, 4));
        ulong k1 = BinaryPrimitives.ReadUInt32LittleEndian(key32.Slice(20, 4));
        ulong k2 = BinaryPrimitives.ReadUInt32LittleEndian(key32.Slice(24, 4));
        ulong k3 = BinaryPrimitives.ReadUInt32LittleEndian(key32.Slice(28, 4));
        f0 += k0; f1 += k1 + (f0 >> 32); f2 += k2 + (f1 >> 32); f3 += k3 + (f2 >> 32);
        var tag = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(0), (uint)f0);
        BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(4), (uint)f1);
        BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(8), (uint)f2);
        BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(12), (uint)f3);
        return tag;
    }
}
