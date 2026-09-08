namespace Stash.Core.Tests;

public static class KeySuite
{
    public static Suite Run()
    {
        var s = new Suite("key");
        // BIP-39 reference vectors, the same ones the Mac app checks.
        s.Equal("all zeros", "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon art",
            string.Join(' ', Mnemonic.Words(new byte[32])));
        s.Equal("all 0x7f", "legal winner thank year wave sausage worth useful legal winner thank year wave sausage worth useful legal winner thank year wave sausage worth title",
            string.Join(' ', Mnemonic.Words(Enumerable.Repeat((byte)0x7f, 32).ToArray())));
        s.Equal("all 0xff", "zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo vote",
            string.Join(' ', Mnemonic.Words(Enumerable.Repeat((byte)0xff, 32).ToArray())));

        bool allRound = true;
        for (int i = 0; i < 50; i++) { var k = MasterKey.Random(); if (!MasterKey.FromWords(string.Join(' ', k.Words)).Equals(k)) { allRound = false; break; } }
        s.Check("50 random keys round-trip", allRound);
        var key = MasterKey.Random();
        var messy = string.Join(",  \n", key.Words.Select((w, i) => i % 2 == 0 ? w.ToUpperInvariant() : w));
        s.Check("case and separators forgiven", MasterKey.FromWords(messy).Equals(key));

        var words = key.Words.ToArray();
        words[5] = words[5] == "zoo" ? "abandon" : "zoo";
        s.Throws<Mnemonic.WordsException>("a wrong word fails the checksum", () => MasterKey.FromWords(string.Join(' ', words)), e => e.Reason == Mnemonic.WordsException.Kind.Checksum);
        s.Throws<Mnemonic.WordsException>("23 words is a count error", () => MasterKey.FromWords(string.Join(' ', words.Take(23))), e => e.Reason == Mnemonic.WordsException.Kind.WordCount && e.Count == 23);
        var unknown = key.Words.ToArray(); unknown[0] = "clipboard";
        s.Throws<Mnemonic.WordsException>("an unknown word is named", () => MasterKey.FromWords(string.Join(' ', unknown)), e => e.Reason == Mnemonic.WordsException.Kind.UnknownWord && e.Word == "clipboard");

        s.Check("purposes separate", !key.ChunkKey.AsSpan().SequenceEqual(key.NameKey) && !key.NameKey.AsSpan().SequenceEqual(key.ManifestKey));
        var again = new MasterKey(key.Entropy);
        s.Check("deterministic", key.ChunkKey.AsSpan().SequenceEqual(again.ChunkKey) && key.Fingerprint == again.Fingerprint);
        s.Equal("fingerprint is 8 hex", 8, key.Fingerprint.Length);
        s.Check("fingerprints differ", MasterKey.Random().Fingerprint != key.Fingerprint);
        s.Check("QR payload parses", MasterKey.FromQrPayload(key.QrPayload)?.Equals(key) == true);
        s.Check("junk QR rejected", MasterKey.FromQrPayload("stashmac:key/v1/nope") is null && MasterKey.FromQrPayload("https://x") is null);
        s.Check("card lists 24 numbered words", Mnemonic.Card(key.Words).Split('\n').Length == 6 && Mnemonic.Card(key.Words).Contains("24."));
        return s;
    }
}
