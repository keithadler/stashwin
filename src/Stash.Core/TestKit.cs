namespace Stash.Core;

/// <summary>A tiny test harness so the app can check itself with "stash selftest". No framework, no network, temp folders only.</summary>
public sealed class Suite
{
    public string Name { get; }
    public List<(string Check, bool Ok, string Detail)> Results { get; } = new();
    public Suite(string name) => Name = name;
    public void Check(string name, bool ok, string detail = "") => Results.Add((name, ok, detail));
    public void Equal<T>(string name, T expected, T actual)
        => Check(name, Equals(expected, actual), Equals(expected, actual) ? "" : $"expected {expected}, got {actual}");
    public void Throws<TEx>(string name, Action act, Func<TEx, bool>? also = null) where TEx : Exception
    {
        try { act(); Check(name, false, "did not throw"); }
        catch (TEx ex) { Check(name, also is null || also(ex), also is null ? "" : "threw, but not as expected: " + ex.Message); }
        catch (Exception ex) { Check(name, false, $"threw {ex.GetType().Name}: {ex.Message}"); }
    }
    public int Failed => Results.Count(r => !r.Ok);

    /// <summary>A scratch folder that disappears when disposed.</summary>
    public static Scratch Temp() => new();
    public sealed class Scratch : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stash-test-" + Guid.NewGuid().ToString("N"));
        public Scratch() => Directory.CreateDirectory(Path);
        public string Dir(string name) { var p = System.IO.Path.Combine(Path, name); Directory.CreateDirectory(p); return p; }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}

public static class SelfTest
{
    public static IReadOnlyList<Func<Suite>> All => new Func<Suite>[]
    {
        Tests.CryptoSuite.Run,
        Tests.KeySuite.Run,
        Tests.ChunkSuite.Run,
        Tests.BackupSuite.Run,
        Tests.PruneSuite.Run,
        Tests.InteropSuite.Run,
    };

    public static int Run(TextWriter output, string? filter = null, bool list = false)
    {
        int passed = 0, failed = 0;
        foreach (var run in All)
        {
            Suite suite;
            try { suite = run(); }
            catch (Exception ex) { output.WriteLine($"FAIL suite crashed: {ex}"); failed++; continue; }
            if (filter is not null && !suite.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var (check, ok, detail) in suite.Results)
            {
                if (list) { output.WriteLine($"{suite.Name}: {check}"); continue; }
                if (ok) passed++; else failed++;
                output.WriteLine($"{(ok ? "ok  " : "FAIL")} {suite.Name}: {check}{(detail.Length > 0 ? " (" + detail + ")" : "")}");
            }
        }
        if (!list) output.WriteLine($"{passed} passed, {failed} failed");
        return failed;
    }
}
