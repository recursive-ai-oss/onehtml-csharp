using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// What a gate command (<c>locators</c>, <c>styles</c>, <c>editfuzz</c>) found: failures by class with the sha256 of the
/// pages that hit them, counters, and distributions. Holds no page content: only hashes, tag and property names, and
/// counts. Safe to share between the run's worker threads.
/// </summary>
internal sealed class GateReport
{
    private const int SeriesCap = 4096;

    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _failures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _examples = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long[]> _series = new(StringComparer.Ordinal);

    public long FailureCount => _failures.Values.Sum(b => (long)b.Count);

    /// <summary>Records a failure of a class. <paramref name="id"/> goes into the class's list (a sha256, optionally with a seed).</summary>
    public void Fail(string failureClass, string id, string? example = null)
    {
        _failures.GetOrAdd(failureClass, static _ => []).Add(id);
        if (example is not null)
        {
            ConcurrentQueue<string> q = _examples.GetOrAdd(failureClass, static _ => new ConcurrentQueue<string>());
            if (q.Count < 5)
            {
                q.Enqueue(id + ": " + example);
            }
        }
    }

    public void Count(string key, long n = 1) => _counters.AddOrUpdate(key, n, (_, v) => v + n);

    /// <summary>Adds an observation to a distribution; values above the cap share the last bucket.</summary>
    public void Observe(string series, int value)
    {
        long[] a = _series.GetOrAdd(series, static _ => new long[SeriesCap + 1]);
        Interlocked.Increment(ref a[Math.Clamp(value, 0, SeriesCap)]);
    }

    private static string Pct(long[] hist, double p)
    {
        long total = hist.Sum();
        if (total == 0)
        {
            return "n/a";
        }

        long target = (long)Math.Ceiling(total * p);
        long seen = 0;
        for (int i = 0; i < hist.Length; i++)
        {
            seen += hist[i];
            if (seen >= target)
            {
                return i == SeriesCap ? ">=" + SeriesCap.ToString(CultureInfo.InvariantCulture) : i.ToString(CultureInfo.InvariantCulture);
            }
        }

        return "n/a";
    }

    public void Write(Options o, string command, int items, double seconds)
    {
        Directory.CreateDirectory(o.Out);
        string failDir = Path.Combine(o.Out, "failures");
        if (Directory.Exists(failDir))
        {
            Directory.Delete(failDir, true);
        }

        var classes = _failures.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
        if (classes.Count > 0)
        {
            Directory.CreateDirectory(failDir);
        }

        foreach (KeyValuePair<string, ConcurrentBag<string>> c in classes)
        {
            string file = new([.. c.Key.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '.' ? ch : '_')]);
            File.WriteAllLines(Path.Combine(failDir, (file.Length > 100 ? file[..100] : file) + ".txt"), c.Value.Distinct().OrderBy(s => s, StringComparer.Ordinal));
        }

        using (var stream = File.Create(Path.Combine(o.Out, "summary.json")))
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("command", command);
            w.WriteNumber("items", items);
            w.WriteNumber("seconds", Math.Round(seconds, 1));
            w.WriteNumber("failures", FailureCount);
            w.WriteStartObject("failureClasses");
            foreach (KeyValuePair<string, ConcurrentBag<string>> c in classes)
            {
                w.WriteNumber(c.Key, c.Value.Count);
            }

            w.WriteEndObject();
            w.WriteStartObject("counters");
            foreach (KeyValuePair<string, long> c in _counters.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                w.WriteNumber(c.Key, c.Value);
            }

            w.WriteEndObject();
            w.WriteStartObject("distributions");
            foreach (KeyValuePair<string, long[]> s in _series.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                w.WriteStartObject(s.Key);
                w.WriteNumber("count", s.Value.Sum());
                w.WriteString("p50", Pct(s.Value, 0.5));
                w.WriteString("p90", Pct(s.Value, 0.9));
                w.WriteString("p99", Pct(s.Value, 0.99));
                w.WriteString("max", Pct(s.Value, 1.0));
                w.WriteEndObject();
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }

        var md = new StringBuilder();
        md.Append("# ").Append(command).AppendLine(" gate").AppendLine();
        md.Append(CultureInfo.InvariantCulture, $"{items} items in {seconds:0}s, {FailureCount} failures.\n\n");
        md.AppendLine("## Failure classes").AppendLine();
        if (classes.Count == 0)
        {
            md.AppendLine("none");
        }

        foreach (KeyValuePair<string, ConcurrentBag<string>> c in classes)
        {
            md.Append("- `").Append(c.Key).Append("`: ").Append(c.Value.Count).AppendLine();
            if (_examples.TryGetValue(c.Key, out ConcurrentQueue<string>? q))
            {
                foreach (string e in q)
                {
                    md.Append("  - ").AppendLine(e);
                }
            }
        }

        md.AppendLine().AppendLine("## Counters").AppendLine();
        foreach (KeyValuePair<string, long> c in _counters.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            md.Append("- ").Append(c.Key).Append(": ").Append(c.Value).AppendLine();
        }

        md.AppendLine().AppendLine("## Distributions").AppendLine();
        foreach (KeyValuePair<string, long[]> s in _series.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            md.Append("- ").Append(s.Key).Append(": n ").Append(s.Value.Sum()).Append(", p50 ").Append(Pct(s.Value, 0.5)).Append(", p90 ").Append(Pct(s.Value, 0.9))
                .Append(", p99 ").Append(Pct(s.Value, 0.99)).Append(", max ").AppendLine(Pct(s.Value, 1.0));
        }

        File.WriteAllText(Path.Combine(o.Out, "summary.md"), md.ToString());
    }
}

/// <summary>Runs a per-item gate over a corpus under the shared conventions: sha256 order, <c>--skip</c>/<c>--limit</c>, a timeout per item, every exception classified, exit status 1 on any failure.</summary>
internal static class GateRunner
{
    /// <summary>True when ONEHTML_GATE_TRACE is set: commands then print what they did, including page content, to stderr. For local debugging of one finding only.</summary>
    public static bool Trace { get; } = Environment.GetEnvironmentVariable("ONEHTML_GATE_TRACE") is { Length: > 0 };

    public static int Run(Options o, string command, List<CorpusItem> items, Action<CorpusItem, byte[], GateReport, CancellationToken> work)
    {
        // ONEHTML_GATE_SHA restricts a run to the items whose sha256 starts with the value, to re-run one finding.
        if (Environment.GetEnvironmentVariable("ONEHTML_GATE_SHA") is { Length: > 0 } only)
        {
            items.RemoveAll(i => !i.Sha256.StartsWith(only, StringComparison.Ordinal));
        }

        if (o.Skip > 0)
        {
            items.RemoveRange(0, Math.Min(o.Skip, items.Count));
        }

        if (items.Count > o.Limit)
        {
            items.RemoveRange(o.Limit, items.Count - o.Limit);
        }

        Console.WriteLine($"{command}: {items.Count} items (skip {o.Skip}), jobs {o.Jobs}, timeout {o.TimeoutSeconds}s, seed {o.Seed}");
        var report = new GateReport();
        var clock = Stopwatch.StartNew();
        int done = 0;
        Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = o.Jobs }, item =>
        {
            byte[]? bytes = null;
            try
            {
                bytes = File.ReadAllBytes(item.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                report.Fail("missing-body", item.Sha256);
            }

            if (bytes is not null)
            {
                (_, string? failure) = Guard.Run<int>(ct =>
                {
                    work(item, bytes, report, ct);
                    return 0;
                }, o.TimeoutSeconds);
                if (failure is not null)
                {
                    report.Fail(failure, item.Sha256);
                }
                else
                {
                    report.Count("items-completed");
                }
            }

            int n = Interlocked.Increment(ref done);
            if (n % 200 == 0)
            {
                Console.WriteLine($"  {n}/{items.Count} after {clock.Elapsed.TotalSeconds:0}s, {report.FailureCount} failures so far");
            }
        });

        report.Write(o, command, items.Count, clock.Elapsed.TotalSeconds);
        Console.WriteLine($"{command}: {items.Count} items, {report.FailureCount} failures; summary in {Path.GetFullPath(o.Out)}");
        return report.FailureCount == 0 ? 0 : 1;
    }

    /// <summary>The class of an exception thrown inside a check: the library's typed exceptions by name, nothing from the message (it may quote page content).</summary>
    public static string Classify(Exception ex) => Guard.Classify(ex);
}
