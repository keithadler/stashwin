namespace Stash.Core.Tests;

public static class PruneSuite
{
    public static Suite Run()
    {
        var s = new Suite("prune");
        var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        // Thinning: 3 in the last week, 4 on one day three weeks ago, 3 in one week five months ago, 2 in one month two years ago.
        var dates = new List<DateTimeOffset>
        {
            now.AddHours(-1), now.AddDays(-2), now.AddDays(-6),
            now.AddDays(-21), now.AddDays(-21).AddHours(-1), now.AddDays(-21).AddHours(-2), now.AddDays(-21).AddHours(-3),
            now.AddDays(-150), now.AddDays(-151), now.AddDays(-152),
            now.AddDays(-730), now.AddDays(-731),
        };
        var keep = Retention.Thin(dates, now, TimeZoneInfo.Utc);
        s.Check("everything from the last week kept", keep.Contains(0) && keep.Contains(1) && keep.Contains(2));
        s.Check("one per day three weeks ago, the newest", keep.Contains(3) && !keep.Contains(4) && !keep.Contains(5) && !keep.Contains(6));
        s.Check("one per week five months ago", new[] { 7, 8, 9 }.Count(keep.Contains) == 1 && keep.Contains(7));
        s.Check("one per month two years ago", new[] { 10, 11 }.Count(keep.Contains) == 1);
        s.Check("an empty set keeps nothing", Retention.Thin(new List<DateTimeOffset>(), now, TimeZoneInfo.Utc).Count == 0);
        s.Check("a single old snapshot is kept", Retention.Thin(new List<DateTimeOffset> { now.AddDays(-900) }, now, TimeZoneInfo.Utc).Count == 1);
        var last = Retention.Last(dates, 3);
        s.Check("newest N", last.SetEquals(new[] { 0, 1, 2 }));

        using var t = Suite.Temp();
        var key = MasterKey.Random();
        var src = t.Dir("src"); var dest = t.Dir("dest");
        var store = new FolderStore(dest, key);
        File.WriteAllText(Path.Combine(src, "a.txt"), "version one");
        var r1 = Backup.Run(new[] { src }, dest, key);
        File.WriteAllText(Path.Combine(src, "a.txt"), "version two");
        var r2 = Backup.Run(new[] { src }, dest, key);
        File.WriteAllText(Path.Combine(src, "a.txt"), "version three");
        var r3 = Backup.Run(new[] { src }, dest, key);
        s.Equal("three snapshots, three chunks", 3, store.ChunkNames().Count);
        var sizes = Prune.UniqueSizes(store, key);
        s.Check("each snapshot holds one unique chunk", sizes.Count == 3 && sizes.Values.All(v => v > 0));

        var p = Prune.Run(store, key, keep: 2);
        s.Equal("oldest snapshot removed", 1, p.SnapshotsRemoved);
        s.Equal("its chunk removed", 1, p.ChunksRemoved);
        s.Equal("two chunks kept", 2, p.ChunksKept);
        s.Check("manifest gone", !store.ManifestNames().Contains(r1.Manifest));

        // A fresh orphan chunk is left alone (an upload in flight), an old one is removed.
        var orphan = Chunk.Name(new byte[] { 9, 9, 9 }, key);
        store.PutChunk(orphan, Chunk.Seal(new byte[] { 9, 9, 9 }, key));
        var p2 = Prune.Run(store, key, keep: 2);
        s.Equal("fresh orphan kept", 0, p2.ChunksRemoved);
        var p3 = Prune.Run(store, key, keep: 2, now: DateTimeOffset.UtcNow.AddHours(3));
        s.Equal("old orphan removed", 1, p3.ChunksRemoved);

        // Wrong key: refuses before deleting anything.
        s.Throws<ChunkException>("wrong key deletes nothing", () => Prune.Run(store, MasterKey.Random(), keep: 1));
        s.Equal("still two snapshots", 2, store.ManifestNames().Count);

        // Delete one snapshot by name.
        var d = Prune.Delete(r2.Manifest, store, key);
        s.Check("one snapshot and its chunk gone", d.SnapshotsRemoved == 1 && d.ChunksRemoved == 1 && store.ManifestNames().Single() == r3.Manifest);
        return s;
    }
}
