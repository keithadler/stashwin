using System.Text.Json;

namespace Stash.Core;

/// <summary>Updates without a framework: one GET to GitHub's releases API, compare versions, open the release page.
/// On by default like the Mac app, a toggle in Settings, one line in the window per new version. Nothing is downloaded
/// or installed by itself and no identifiers are sent. The pure parts live here so they can be tested without a network.</summary>
public static class Updates
{
    public const string Repo = "keithadler/stashwin";
    public static readonly string ReleasesPage = $"https://github.com/{Repo}/releases/latest";
    public static readonly string Api = $"https://api.github.com/repos/{Repo}/releases/latest";
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public abstract record Result;
    public sealed record UpToDate(string Latest) : Result;
    public sealed record Available(string Version, string Page) : Result;
    public sealed record Unknown(string Reason) : Result;

    public static Result Parse(int status, string body, string current)
    {
        if (status == 404) return new Unknown("No public release yet.");
        if (status != 200) return new Unknown($"HTTP {status}");
        try
        {
            using var doc = JsonDocument.Parse(body);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var latest = tag.StartsWith('v') ? tag[1..] : tag;
            var page = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
            return IsNewer(latest, current) ? new Available(latest, page) : new UpToDate(latest);
        }
        catch (Exception ex) { return new Unknown(ex.Message); }
    }

    public static bool IsNewer(string a, string b)
    {
        var pa = a.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        var pb = b.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    /// <summary>Pure: is a check due? An hour of slack so a daily check still happens when the app opens a little early.</summary>
    public static bool ShouldCheck(bool enabled, DateTimeOffset? last, DateTimeOffset now)
        => enabled && (last is null || now - last.Value >= Interval - TimeSpan.FromHours(1));
}
