namespace Stash.Core.Tests;

/// <summary>The QR encoder, the PNG writer and the update-check logic.</summary>
public static class ExtrasSuite
{
    public static Suite Run()
    {
        var s = new Suite("extras");
        var key = MasterKey.Random();
        var qr = QrCode.Encode(key.QrPayload);
        s.Check("payload fits a small version", qr.Version >= 3 && qr.Version <= 6, $"version {qr.Version}");
        s.Equal("size follows the version", qr.Version * 4 + 17, qr.Size);
        // Finder patterns: a 3x3 dark centre with a light ring around it, top-left.
        bool finder = qr[3, 3] && qr[2, 2] && qr[4, 4] && !qr[1, 1] && !qr[5, 5] && qr[0, 0] && qr[6, 6];
        s.Check("finder pattern in the corner", finder);
        s.Check("timing pattern alternates", qr[8, 6] && !qr[9, 6] && qr[10, 6] && qr[6, 8] && !qr[6, 9]);
        s.Check("dark module present", qr[8, qr.Size - 8]);
        var again = QrCode.Encode(key.QrPayload);
        bool same = true; for (int y = 0; y < qr.Size && same; y++) for (int x = 0; x < qr.Size; x++) if (qr[x, y] != again[x, y]) { same = false; break; }
        s.Check("deterministic", same);
        s.Throws<ArgumentException>("far too long is refused", () => QrCode.Encode(new string('x', 3000)));
        var png = qr.ToPng(4);
        s.Check("PNG signature", png.Length > 100 && png[0] == 0x89 && png[1] == 0x50 && png[2] == 0x4E && png[3] == 0x47);
        s.Check("PNG ends with IEND", System.Text.Encoding.ASCII.GetString(png, png.Length - 8, 4) == "IEND");
        s.Check("text rendering has the right rows", qr.ToText().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == qr.Size);

        s.Check("1.2.0 is newer than 1.1.9", Updates.IsNewer("1.2.0", "1.1.9"));
        s.Check("1.0.0 is not newer than 1.0", !Updates.IsNewer("1.0.0", "1.0"));
        s.Check("1.0.1 is newer than 1.0", Updates.IsNewer("1.0.1", "1.0"));
        s.Check("a tag with v is parsed", Updates.Parse(200, "{\"tag_name\":\"v1.2.0\",\"html_url\":\"https://x/r\"}", "1.0.0") is Updates.Available { Version: "1.2.0", Page: "https://x/r" });
        s.Check("same version is up to date", Updates.Parse(200, "{\"tag_name\":\"v1.0.0\"}", "1.0.0") is Updates.UpToDate);
        s.Check("404 means no release yet", Updates.Parse(404, "", "1.0.0") is Updates.Unknown u && u.Reason.Contains("No public release"));
        s.Check("junk is unknown, not a crash", Updates.Parse(200, "not json", "1.0.0") is Updates.Unknown);
        var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        s.Check("check due when never checked", Updates.ShouldCheck(true, null, now));
        s.Check("not due after two hours", !Updates.ShouldCheck(true, now.AddHours(-2), now));
        s.Check("due after 23 hours", Updates.ShouldCheck(true, now.AddHours(-23), now));
        s.Check("never when disabled", !Updates.ShouldCheck(false, null, now));
        return s;
    }
}
