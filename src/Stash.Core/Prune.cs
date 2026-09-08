using System.Globalization;

namespace Stash.Core;

/// <summary>Which snapshots to keep. Two policies: the newest N, or "thin out": everything from the last week, one a
/// day for a month, one a week for a year, one a month after that. A pure function of dates so it can be tested with
/// made-up calendars.</summary>
public static class Retention
{
    public enum Policy { Last, Thin }

    /// <summary>Indices (into dates, any order) of snapshots to keep under the thinning policy. The newest in each bucket survives.</summary>
    public static HashSet<int> Thin(IReadOnlyList<DateTimeOffset> dates, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var keep = new HashSet<int>();
        var day = new Dictionary<string, (int, DateTimeOffset)>(); var week = new Dictionary<string, (int, DateTimeOffset)>(); var month = new Dictionary<string, (int, DateTimeOffset)>();
        var weekAgo = now.AddDays(-7); var monthAgo = now.AddDays(-30); var yearAgo = now.AddDays(-365);
        var cal = CultureInfo.InvariantCulture.Calendar;
        for (int i = 0; i < dates.Count; i++)
        {
            var d = dates[i];
            if (d >= weekAgo) { keep.Add(i); continue; }
            var local = TimeZoneInfo.ConvertTime(d, zone);
            void Newest(Dictionary<string, (int, DateTimeOffset)> table, string key) { if (table.TryGetValue(key, out var cur) && cur.Item2 >= d) return; table[key] = (i, d); }
            if (d >= monthAgo) Newest(day, $"{local.Year}-{local.Month}-{local.Day}");
            else if (d >= yearAgo) Newest(week, $"{local.Year}-w{cal.GetWeekOfYear(local.DateTime, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday)}");
            else Newest(month, $"{local.Year}-{local.Month}");
        }
        foreach (var t in new[] { day, week, month }) foreach (var v in t.Values) keep.Add(v.Item1);
        if (keep.Count == 0 && dates.Count > 0)
        {
            int newest = 0;
            for (int i = 1; i < dates.Count; i++) if (dates[i] > dates[newest]) newest = i;
            keep.Add(newest);
        }
        return keep;
    }

    /// <summary>Indices to keep under the "newest N" policy.</summary>
    public static HashSet<int> Last(IReadOnlyList<DateTimeOffset> dates, int n)
        => Enumerable.Range(0, dates.Count).OrderByDescending(i => dates[i]).Take(Math.Max(n, 1)).ToHashSet();
}

public sealed class PruneReport
{
    public int SnapshotsRemoved, ChunksRemoved, ChunksKept;
    public long BytesFreed, BytesKept;
}

/// <summary>Keeping the destination from growing forever: apply the retention policy, then delete every chunk no
/// remaining snapshot references. Refuses to touch anything if a remaining manifest cannot be opened: a wrong key or
/// a damaged manifest must never turn into deleted chunks.</summary>
public static class Prune
{
    /// <summary>Unreferenced chunks younger than this are never deleted: an orphan from an interrupted run, or an upload in flight from another PC on the same card.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromHours(2);

    public static PruneReport Run(IChunkStore store, MasterKey key, int keep = 30, Retention.Policy policy = Retention.Policy.Last, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.UtcNow;
        var manifests = store.ManifestNames();
        var opened = manifests.Select(n => Manifest.Open(store.GetManifest(n), key)).ToList(); // throws on a wrong key: nothing deleted
        var dates = opened.Select(m => m.CreatedAt).ToList();
        var keepSet = policy == Retention.Policy.Thin ? Retention.Thin(dates, at) : Retention.Last(dates, keep);
        return Sweep(store, manifests, opened, keepSet, at);
    }

    /// <summary>Deletes one snapshot by name, then the chunks only it used.</summary>
    public static PruneReport Delete(string snapshot, IChunkStore store, MasterKey key, DateTimeOffset? now = null)
    {
        var manifests = store.ManifestNames();
        if (!manifests.Contains(snapshot)) throw ChunkException.NotChunk();
        var opened = manifests.Select(n => Manifest.Open(store.GetManifest(n), key)).ToList();
        var keepSet = Enumerable.Range(0, manifests.Count).Where(i => manifests[i] != snapshot).ToHashSet();
        return Sweep(store, manifests, opened, keepSet, now ?? DateTimeOffset.UtcNow);
    }

    private static PruneReport Sweep(IChunkStore store, List<string> manifests, List<Manifest> opened, HashSet<int> keepSet, DateTimeOffset now)
    {
        var report = new PruneReport();
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var released = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < opened.Count; i++)
            foreach (var f in opened[i].Files) { if (keepSet.Contains(i)) referenced.UnionWith(f.Chunks); else released.UnionWith(f.Chunks); }
        for (int i = 0; i < manifests.Count; i++)
            if (!keepSet.Contains(i)) { store.DeleteManifest(manifests[i]); report.SnapshotsRemoved++; }
        foreach (var name in store.ChunkNames())
        {
            var size = store.ChunkSize(name);
            if (referenced.Contains(name)) { report.ChunksKept++; report.BytesKept += size; continue; }
            if (!released.Contains(name) && store.ChunkDate(name) is { } written && now - written < Grace) { report.ChunksKept++; report.BytesKept += size; continue; }
            store.DeleteChunk(name); report.ChunksRemoved++; report.BytesFreed += size;
        }
        return report;
    }

    /// <summary>Per snapshot: bytes held by chunks no other snapshot references, i.e. what deleting it frees.</summary>
    public static Dictionary<string, long> UniqueSizes(IChunkStore store, MasterKey key)
    {
        var refs = new Dictionary<string, int>(StringComparer.Ordinal);
        var per = new Dictionary<string, HashSet<string>>();
        foreach (var name in store.ManifestNames())
        {
            Manifest m;
            try { m = Manifest.Open(store.GetManifest(name), key); } catch { continue; }
            var set = m.Files.SelectMany(f => f.Chunks).ToHashSet(StringComparer.Ordinal);
            per[name] = set;
            foreach (var c in set) refs[c] = refs.GetValueOrDefault(c) + 1;
        }
        return per.ToDictionary(kv => kv.Key, kv => kv.Value.Where(c => refs[c] == 1).Sum(store.ChunkSize));
    }
}
