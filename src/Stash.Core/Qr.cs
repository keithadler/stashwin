using System.Text;

namespace Stash.Core;

/// <summary>A QR code encoder (ISO 18004, byte mode, error correction level M, versions 1 to 20), small enough to own.
/// Used for the recovery card so a phone camera or Stash for Mac can read the key. No decoder: a card typed in works too.</summary>
public sealed class QrCode
{
    public int Size { get; }
    public int Version { get; }
    private readonly bool[,] _modules;
    private readonly bool[,] _reserved;

    public bool this[int x, int y] => _modules[y, x];

    private QrCode(int version)
    {
        Version = version;
        Size = version * 4 + 17;
        _modules = new bool[Size, Size];
        _reserved = new bool[Size, Size];
    }

    // Error correction level M: (total codewords, ec codewords per block, blocks group 1, data cw group 1, blocks group 2, data cw group 2)
    private static readonly int[][] EcM =
    {
        new[]{26,10,1,16,0,0}, new[]{44,16,1,28,0,0}, new[]{70,26,1,44,0,0}, new[]{100,18,2,32,0,0}, new[]{134,24,2,43,0,0},
        new[]{172,16,4,27,0,0}, new[]{196,18,4,31,0,0}, new[]{242,22,2,38,2,39}, new[]{292,22,3,36,2,37}, new[]{346,26,4,43,1,44},
        new[]{404,30,1,50,4,51}, new[]{466,22,6,36,2,37}, new[]{532,22,8,37,1,38}, new[]{581,24,4,40,5,41}, new[]{655,24,5,41,5,42},
        new[]{733,28,7,45,3,46}, new[]{815,28,10,46,1,47}, new[]{901,26,9,43,4,44}, new[]{991,26,3,44,11,45}, new[]{1085,26,3,41,13,42},
    };

    private static readonly int[][] AlignmentPositions =
    {
        Array.Empty<int>(), new[]{6,18}, new[]{6,22}, new[]{6,26}, new[]{6,30}, new[]{6,34}, new[]{6,22,38}, new[]{6,24,42}, new[]{6,26,46}, new[]{6,28,50},
        new[]{6,30,54}, new[]{6,32,58}, new[]{6,34,62}, new[]{6,26,46,66}, new[]{6,26,48,70}, new[]{6,26,50,74}, new[]{6,30,54,78}, new[]{6,30,56,82}, new[]{6,30,58,86}, new[]{6,34,62,90},
    };

    public static QrCode Encode(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        int version = 1;
        while (true)
        {
            if (version > EcM.Length) throw new ArgumentException("too long for a QR code");
            var t = EcM[version - 1];
            int dataCw = t[2] * t[3] + t[4] * t[5];
            int lenBits = version <= 9 ? 8 : 16;
            int needBits = 4 + lenBits + data.Length * 8;
            if (needBits <= dataCw * 8) break;
            version++;
        }
        var table = EcM[version - 1];
        int totalData = table[2] * table[3] + table[4] * table[5];
        // bit stream: mode 0100, length, bytes, terminator, pad
        var bits = new List<bool>();
        void Put(int value, int n) { for (int i = n - 1; i >= 0; i--) bits.Add(((value >> i) & 1) == 1); }
        Put(0b0100, 4);
        Put(data.Length, version <= 9 ? 8 : 16);
        foreach (var b in data) Put(b, 8);
        int cap = totalData * 8;
        for (int i = 0; i < 4 && bits.Count < cap; i++) bits.Add(false);
        while (bits.Count % 8 != 0) bits.Add(false);
        var pads = new[] { 0xEC, 0x11 };
        for (int i = 0; bits.Count < cap; i++) Put(pads[i % 2], 8);
        var codewords = new byte[totalData];
        for (int i = 0; i < totalData; i++) { int v = 0; for (int b = 0; b < 8; b++) v = (v << 1) | (bits[i * 8 + b] ? 1 : 0); codewords[i] = (byte)v; }

        // split into blocks, compute EC, interleave
        var blocks = new List<byte[]>(); var ecs = new List<byte[]>();
        int pos = 0;
        for (int g = 0; g < 2; g++)
        {
            int count = table[2 + g * 2], len = table[3 + g * 2];
            for (int b = 0; b < count; b++) { var block = codewords.AsSpan(pos, len).ToArray(); pos += len; blocks.Add(block); ecs.Add(ReedSolomon(block, table[1])); }
        }
        var interleaved = new List<byte>();
        int maxLen = blocks.Max(b => b.Length);
        for (int i = 0; i < maxLen; i++) foreach (var b in blocks) if (i < b.Length) interleaved.Add(b[i]);
        for (int i = 0; i < table[1]; i++) foreach (var e in ecs) interleaved.Add(e[i]);

        var qr = new QrCode(version);
        qr.PlacePatterns();
        qr.PlaceData(interleaved);
        int mask = qr.ChooseMask();
        qr.ApplyMask(mask);
        qr.PlaceFormat(mask);
        return qr;
    }

    // ---- Reed-Solomon over GF(256) ----
    private static readonly byte[] Exp = new byte[512], Log = new byte[256];
    static QrCode()
    {
        int x = 1;
        for (int i = 0; i < 255; i++) { Exp[i] = (byte)x; Log[x] = (byte)i; x <<= 1; if (x >= 256) x ^= 0x11D; }
        for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
    }
    private static byte Mul(byte a, byte b) => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

    private static byte[] ReedSolomon(byte[] data, int ecLen)
    {
        var gen = new byte[] { 1 };
        for (int i = 0; i < ecLen; i++)
        {
            var next = new byte[gen.Length + 1];
            for (int j = 0; j < gen.Length; j++) { next[j] ^= gen[j]; next[j + 1] ^= Mul(gen[j], Exp[i]); }
            gen = next;
        }
        var rem = new byte[ecLen];
        foreach (var d in data)
        {
            byte factor = (byte)(d ^ rem[0]);
            Array.Copy(rem, 1, rem, 0, ecLen - 1); rem[ecLen - 1] = 0;
            for (int j = 0; j < ecLen; j++) rem[j] ^= Mul(gen[j + 1], factor);
        }
        return rem;
    }

    // ---- Patterns ----
    private void Set(int x, int y, bool on, bool reserve = true) { _modules[y, x] = on; if (reserve) _reserved[y, x] = true; }

    private void PlacePatterns()
    {
        void Finder(int cx, int cy)
        {
            for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++)
            {
                int x = cx + dx, y = cy + dy; if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                Set(x, y, d != 2 && d != 4);
            }
        }
        Finder(3, 3); Finder(Size - 4, 3); Finder(3, Size - 4);
        for (int i = 8; i < Size - 8; i++) { Set(i, 6, i % 2 == 0); Set(6, i, i % 2 == 0); }
        var ap = AlignmentPositions[Version - 1];
        foreach (var cy in ap) foreach (var cx in ap)
        {
            if (_reserved[cy, cx]) continue;
            for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) Set(cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }
        // reserve format areas
        for (int i = 0; i < 9; i++) { if (i < Size) { _reserved[8, i] = true; _reserved[i, 8] = true; } }
        for (int i = 0; i < 8; i++) { _reserved[8, Size - 1 - i] = true; _reserved[Size - 1 - i, 8] = true; }
        Set(8, Size - 8, true); // dark module
        if (Version >= 7)
        {
            int v = Version; int rem = v;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int info = v << 12 | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((info >> i) & 1) == 1;
                Set(Size - 11 + i % 3, i / 3, bit); Set(i / 3, Size - 11 + i % 3, bit);
            }
        }
    }

    private void PlaceData(List<byte> cw)
    {
        int bit = 0; int total = cw.Count * 8;
        bool upward = true;
        for (int right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int i = 0; i < Size; i++)
            {
                int y = upward ? Size - 1 - i : i;
                for (int dx = 0; dx < 2; dx++)
                {
                    int x = right - dx;
                    if (_reserved[y, x]) continue;
                    bool on = bit < total && ((cw[bit / 8] >> (7 - bit % 8)) & 1) == 1;
                    _modules[y, x] = on; bit++;
                }
            }
            upward = !upward;
        }
    }

    private static bool MaskBit(int mask, int x, int y) => mask switch
    {
        0 => (x + y) % 2 == 0, 1 => y % 2 == 0, 2 => x % 3 == 0, 3 => (x + y) % 3 == 0,
        4 => (y / 2 + x / 3) % 2 == 0, 5 => x * y % 2 + x * y % 3 == 0, 6 => (x * y % 2 + x * y % 3) % 2 == 0, _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
    };

    private void ApplyMask(int mask)
    {
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++) if (!_reserved[y, x] && MaskBit(mask, x, y)) _modules[y, x] = !_modules[y, x];
    }

    private int ChooseMask()
    {
        int best = 0, bestScore = int.MaxValue;
        for (int m = 0; m < 8; m++)
        {
            ApplyMask(m); PlaceFormat(m);
            int score = Penalty();
            ApplyMask(m);
            if (score < bestScore) { bestScore = score; best = m; }
        }
        return best;
    }

    private int Penalty()
    {
        int score = 0;
        for (int y = 0; y < Size; y++) { int run = 1; for (int x = 1; x < Size; x++) { if (_modules[y, x] == _modules[y, x - 1]) { run++; if (run == 5) score += 3; else if (run > 5) score++; } else run = 1; } }
        for (int x = 0; x < Size; x++) { int run = 1; for (int y = 1; y < Size; y++) { if (_modules[y, x] == _modules[y - 1, x]) { run++; if (run == 5) score += 3; else if (run > 5) score++; } else run = 1; } }
        for (int y = 0; y < Size - 1; y++) for (int x = 0; x < Size - 1; x++) { var c = _modules[y, x]; if (c == _modules[y, x + 1] && c == _modules[y + 1, x] && c == _modules[y + 1, x + 1]) score += 3; }
        int[] p1 = { 1, 0, 1, 1, 1, 0, 1, 0, 0, 0, 0 }, p2 = { 0, 0, 0, 0, 1, 0, 1, 1, 1, 0, 1 };
        for (int y = 0; y < Size; y++) for (int x = 0; x + 11 <= Size; x++)
        {
            bool a = true, b = true;
            for (int k = 0; k < 11; k++) { bool v = _modules[y, x + k]; if (v != (p1[k] == 1)) a = false; if (v != (p2[k] == 1)) b = false; }
            if (a || b) score += 40;
        }
        for (int x = 0; x < Size; x++) for (int y = 0; y + 11 <= Size; y++)
        {
            bool a = true, b = true;
            for (int k = 0; k < 11; k++) { bool v = _modules[y + k, x]; if (v != (p1[k] == 1)) a = false; if (v != (p2[k] == 1)) b = false; }
            if (a || b) score += 40;
        }
        int dark = 0; for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++) if (_modules[y, x]) dark++;
        int pct = dark * 100 / (Size * Size);
        score += Math.Min(Math.Abs(pct - 50) / 5, Math.Abs(pct - 50 + 4) / 5) * 10;
        return score;
    }

    private void PlaceFormat(int mask)
    {
        int data = (0b00 << 3) | mask; // EC level M = 00
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = ((data << 10) | rem) ^ 0x5412;
        for (int i = 0; i <= 5; i++) _modules[i, 8] = ((bits >> i) & 1) == 1;
        _modules[7, 8] = ((bits >> 6) & 1) == 1; _modules[8, 8] = ((bits >> 7) & 1) == 1; _modules[8, 7] = ((bits >> 8) & 1) == 1;
        for (int i = 9; i < 15; i++) _modules[8, 14 - i] = ((bits >> i) & 1) == 1;
        for (int i = 0; i < 8; i++) _modules[8, Size - 1 - i] = ((bits >> i) & 1) == 1;
        for (int i = 8; i < 15; i++) _modules[Size - 15 + i, 8] = ((bits >> i) & 1) == 1;
        _modules[Size - 8, 8] = true;
    }

    /// <summary>The code as a PNG, scale pixels per module with a 4-module quiet zone. Pure managed, so the card image works anywhere.</summary>
    public byte[] ToPng(int scale = 8)
    {
        int quiet = 4, dim = (Size + quiet * 2) * scale;
        var pixels = new byte[dim * dim];
        Array.Fill(pixels, (byte)255);
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
        {
            if (!_modules[y, x]) continue;
            for (int dy = 0; dy < scale; dy++) for (int dx = 0; dx < scale; dx++) pixels[((y + quiet) * scale + dy) * dim + (x + quiet) * scale + dx] = 0;
        }
        return Png.Gray(dim, dim, pixels);
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        for (int y = 0; y < Size; y++) { for (int x = 0; x < Size; x++) sb.Append(_modules[y, x] ? "██" : "  "); sb.Append('\n'); }
        return sb.ToString();
    }
}

/// <summary>A minimal PNG writer (8-bit grayscale), so the core can hand back an image without a UI framework.</summary>
public static class Png
{
    public static byte[] Gray(int width, int height, byte[] pixels)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BitConverterBig(ihdr, 0, width); BitConverterBig(ihdr, 4, height); ihdr[8] = 8; ihdr[9] = 0; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        Chunk(ms, "IHDR", ihdr);
        var raw = new byte[(width + 1) * height];
        for (int y = 0; y < height; y++) { raw[y * (width + 1)] = 0; Array.Copy(pixels, y * width, raw, y * (width + 1) + 1, width); }
        using var z = new MemoryStream();
        using (var deflate = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(raw);
        Chunk(ms, "IDAT", z.ToArray());
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void BitConverterBig(byte[] b, int off, int v) { b[off] = (byte)(v >> 24); b[off + 1] = (byte)(v >> 16); b[off + 2] = (byte)(v >> 8); b[off + 3] = (byte)v; }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BitConverterBig(len, 0, data.Length); s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes); s.Write(data);
        var crc = Crc32(typeBytes.Concat(data).ToArray());
        var c = new byte[4]; BitConverterBig(c, 0, (int)crc); s.Write(c);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1; }
        return crc ^ 0xFFFFFFFF;
    }
}
