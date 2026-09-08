using System.Security.Cryptography;

namespace Stash.Core.Tests;

public static class CryptoSuite
{
    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", "").Replace("\n", ""));

    public static Suite Run()
    {
        var s = new Suite("crypto");
        // RFC 8439 §2.4.2: ChaCha20 encryption test vector.
        var key = Hex("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");
        var nonce = Hex("000000000000004a00000000");
        var plain = System.Text.Encoding.ASCII.GetBytes("Ladies and Gentlemen of the class of '99: If I could offer you only one tip for the future, sunscreen would be it.");
        var expected = Hex("6e2e359a2568f98041ba0728dd0d6981e97e7aec1d4360c20a27afccfd9fae0bf91b65c5524733ab8f593dabcd62b3571639d624e65152ab8f530c359f0861d807ca0dbf500d6a6156a38e088a22b65e52bc514d16ccf806818ce91ab77937365af90bbf74a35be6b40b8eedf2785e42874d");
        var ct = new byte[plain.Length];
        ChaChaPoly.ChaCha20(key, nonce, 1, plain, ct);
        s.Check("ChaCha20 RFC 8439 vector", ct.AsSpan().SequenceEqual(expected));

        // RFC 8439 §2.5.2: Poly1305 MAC test vector.
        var pkey = Hex("85d6be7857556d337f4452fe42d506a80103808afb0db2fd4abff6af4149f51b");
        var msg = System.Text.Encoding.ASCII.GetBytes("Cryptographic Forum Research Group");
        var tag = ChaChaPoly.Poly1305(pkey, msg);
        s.Check("Poly1305 RFC 8439 vector", tag.AsSpan().SequenceEqual(Hex("a8061dc1305136c6c22b8baf0c0127a9")));

        // RFC 8439 §2.6.2: Poly1305 key generation from ChaCha20 block 0.
        var k2 = Hex("808182838485868788898a8b8c8d8e8f909192939495969798999a9b9c9d9e9f");
        var n2 = Hex("000000000001020304050607");
        var zero = new byte[64]; var otk = new byte[64];
        ChaChaPoly.ChaCha20(k2, n2, 0, zero, otk);
        s.Check("Poly1305 one-time key RFC 8439 vector", otk.AsSpan(0, 32).SequenceEqual(Hex("8ad5a08b905f81cc815040274ab29471a833b637e3fd0da508dbb8e2fdd1a646")));

        // Seal/open round trip, tamper, wrong key, and the platform implementation agrees where it exists.
        var mk = RandomNumberGenerator.GetBytes(32);
        var data = RandomNumberGenerator.GetBytes(100_003);
        var box = ChaChaPoly.Seal(data, mk);
        s.Equal("combined length is nonce + data + tag", data.Length + 28, box.Length);
        s.Check("open returns the plaintext", ChaChaPoly.Open(box, mk).AsSpan().SequenceEqual(data));
        var tampered = (byte[])box.Clone(); tampered[50] ^= 1;
        s.Throws<CryptographicException>("a flipped bit is refused", () => ChaChaPoly.Open(tampered, mk));
        s.Throws<CryptographicException>("a wrong key is refused", () => ChaChaPoly.Open(box, RandomNumberGenerator.GetBytes(32)));
        s.Check("empty plaintext seals and opens", ChaChaPoly.Open(ChaChaPoly.Seal(Array.Empty<byte>(), mk), mk).Length == 0);
        if (System.Security.Cryptography.ChaCha20Poly1305.IsSupported)
        {
            using var platform = new System.Security.Cryptography.ChaCha20Poly1305(mk);
            var nonce2 = box.AsSpan(0, 12).ToArray();
            var pt = new byte[data.Length];
            platform.Decrypt(nonce2, box.AsSpan(12, data.Length), box.AsSpan(box.Length - 16), pt);
            s.Check("the platform ChaCha20-Poly1305 opens our box", pt.AsSpan().SequenceEqual(data));
        }
        else s.Check("platform ChaCha20-Poly1305 not available here; managed only", true);
        return s;
    }
}
