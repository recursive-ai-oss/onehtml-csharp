using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Encodings;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>Maps CSS Syntax's encoding hooks onto the library's WHATWG encodings.</summary>
internal sealed class CssEncodings : ICssEncodingProvider
{
    public static CssEncodings Instance { get; } = new();

    public string? GetEncodingName(string label) => WhatwgEncoding.ForLabel(label)?.Name.ToLowerInvariant();

    public string Decode(ReadOnlySpan<byte> bytes, string fallbackEncodingName)
    {
        WhatwgEncoding enc = WhatwgEncoding.ForLabel(fallbackEncodingName) ?? WhatwgEncoding.ForLabel("utf-8")!;
        return SourceText.Decode(bytes.ToArray(), enc, EncodingConfidence.Certain).Text;
    }
}

/// <summary>
/// The round-trip and robustness run: every stored page and stylesheet is parsed under a timeout, and the lossless
/// output must equal the input. Nothing from the corpus is written out except SHA-256 values.
/// </summary>
internal static class RoundTrip
{
    private sealed record Result(CorpusItem Item, string Class, string Detail, double ParseMs, long Nodes);

    public static int Run(Options o)
    {
        List<CorpusItem> items = Corpus.Load(o.Corpus);
        if (items.Count > o.Limit)
        {
            items.RemoveRange(o.Limit, items.Count - o.Limit);
        }

        Console.WriteLine($"roundtrip: {items.Count} items, jobs {o.Jobs}, timeout {o.TimeoutSeconds}s");
        var results = new List<Result>(items.Count);
        var clock = Stopwatch.StartNew();
        int done = 0;
        Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = o.Jobs }, item =>
        {
            Result r = Check(item, o.TimeoutSeconds);
            lock (results)
            {
                results.Add(r);
            }

            int n = Interlocked.Increment(ref done);
            if (n % 500 == 0)
            {
                Console.WriteLine($"  {n}/{items.Count} after {clock.Elapsed.TotalSeconds:0}s");
            }
        });

        results.Sort(static (a, b) => string.CompareOrdinal(a.Item.Sha256 + a.Item.Url, b.Item.Sha256 + b.Item.Url));
        Write(o, results, clock.Elapsed.TotalSeconds);
        int failures = results.Count(r => r.Class != "ok");
        Console.WriteLine($"roundtrip: {results.Count - failures} ok, {failures} failed; summary in {Path.GetFullPath(o.Out)}");
        return failures == 0 ? 0 : 1;
    }

    private static Result Check(CorpusItem item, int timeoutSeconds)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(item.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result(item, "missing-body", ex.GetType().Name, 0, 0);
        }

        var sw = Stopwatch.StartNew();
        (Result? r, string? failure) = Guard.Run(
            ct => item.IsStylesheet ? CheckStylesheet(item, bytes, ct) : CheckPage(item, bytes, ct),
            timeoutSeconds);
        double ms = sw.Elapsed.TotalMilliseconds;
        return r ?? new Result(item, failure!, string.Empty, ms, 0);
    }

    private static Result CheckPage(CorpusItem item, byte[] bytes, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        Document doc = Document.Parse(bytes, new ParseOptions { ContentType = item.ContentType }, ct);
        double ms = sw.Elapsed.TotalMilliseconds;
        byte[] back = doc.ToLosslessBytes();
        int at = FirstDifference(bytes, back);
        return at < 0
            ? new Result(item, "ok", string.Empty, ms, doc.NodeCount)
            : new Result(item, "mismatch", $"first difference at byte {at} (input {bytes.Length} bytes, output {back.Length} bytes)", ms, doc.NodeCount);
    }

    private static Result CheckStylesheet(CorpusItem item, byte[] bytes, CancellationToken ct)
    {
        string? label = CharsetOf(item.ContentType);
        string fallback = CssEncoding.DetermineFallbackEncoding(bytes, label, null, CssEncodings.Instance);
        string expected = CssEncodings.Instance.Decode(bytes, fallback);
        var sw = Stopwatch.StartNew();
        Stylesheet sheet = Stylesheet.Parse(bytes, CssEncodings.Instance, label, null, null, null, ct);
        double ms = sw.Elapsed.TotalMilliseconds;
        string back = sheet.ToSourceText();
        if (string.Equals(back, expected, StringComparison.Ordinal))
        {
            return new Result(item, "ok", string.Empty, ms, sheet.Rules.Count);
        }

        int at = 0;
        while (at < back.Length && at < expected.Length && back[at] == expected[at])
        {
            at++;
        }

        return new Result(item, "mismatch", $"first difference at char {at} (expected {expected.Length} chars, output {back.Length} chars)", ms, sheet.Rules.Count);
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        int common = a.AsSpan(0, n).CommonPrefixLength(b.AsSpan(0, n));
        return common < n ? common : (a.Length == b.Length ? -1 : n);
    }

    /// <summary>The <c>charset</c> parameter of a Content-Type value, or null.</summary>
    private static string? CharsetOf(string? contentType)
    {
        if (contentType is null)
        {
            return null;
        }

        int i = contentType.IndexOf("charset=", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            return null;
        }

        string v = contentType[(i + 8)..];
        int end = v.IndexOf(';', StringComparison.Ordinal);
        return (end >= 0 ? v[..end] : v).Trim().Trim('"');
    }

    private static void Write(Options o, List<Result> results, double seconds)
    {
        Directory.CreateDirectory(o.Out);
        string shaDir = Path.Combine(o.Out, "failures");
        Directory.CreateDirectory(shaDir);
        var failures = results.Where(r => r.Class != "ok").ToList();
        foreach (IGrouping<string, Result> g in failures.GroupBy(r => r.Class))
        {
            File.WriteAllLines(
                Path.Combine(shaDir, g.Key.Replace(':', '_') + ".sha256.txt"),
                g.Select(r => r.Item.Sha256).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        }

        var byClass = failures.GroupBy(r => r.Class).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
        var slowest = results.OrderByDescending(r => r.ParseMs).Take(o.Top).ToList();
        long pages = results.Count(r => !r.Item.IsStylesheet);
        long sheets = results.Count - pages;

        using (var fs = File.Create(Path.Combine(o.Out, "summary.json")))
        using (var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("command", "roundtrip");
            w.WriteNumber("items", results.Count);
            w.WriteNumber("pages", pages);
            w.WriteNumber("stylesheets", sheets);
            w.WriteNumber("ok", results.Count - failures.Count);
            w.WriteNumber("failed", failures.Count);
            w.WriteNumber("wallSeconds", Math.Round(seconds, 1));
            w.WriteNumber("totalParseMs", Math.Round(results.Sum(r => r.ParseMs), 1));
            w.WriteNumber("totalNodes", results.Sum(r => r.Nodes));
            w.WriteStartObject("failuresByClass");
            foreach (IGrouping<string, Result> g in byClass)
            {
                w.WriteNumber(g.Key, g.Count());
            }

            w.WriteEndObject();
            w.WriteStartArray("slowest");
            foreach (Result r in slowest)
            {
                WriteResult(w, r);
            }

            w.WriteEndArray();
            w.WriteStartArray("topFailures");
            foreach (Result r in failures.Take(o.Top))
            {
                WriteResult(w, r);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        var md = new StringBuilder();
        md.AppendLine("# Round trip and robustness").AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"- items: {results.Count} ({pages} pages, {sheets} stylesheets)");
        md.AppendLine(CultureInfo.InvariantCulture, $"- ok: {results.Count - failures.Count}; failed: {failures.Count} ({Corpus.Pct(failures.Count, results.Count)})");
        md.AppendLine(CultureInfo.InvariantCulture, $"- wall time {seconds:0.0}s, summed parse time {results.Sum(r => r.ParseMs) / 1000:0.0}s, {results.Sum(r => r.Nodes)} nodes").AppendLine();
        md.AppendLine("## Failures by class").AppendLine();
        foreach (IGrouping<string, Result> g in byClass)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"- `{g.Key}`: {g.Count()}");
        }

        md.AppendLine().AppendLine("## Top failures").AppendLine();
        foreach (Result r in failures.Take(o.Top))
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"- `{r.Item.Sha256}` {Kind(r)} `{r.Class}` {r.Detail}");
        }

        md.AppendLine().AppendLine("## Slowest").AppendLine();
        foreach (Result r in slowest)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"- `{r.Item.Sha256}` {Kind(r)} {r.ParseMs:0.0} ms, {r.Item.Bytes} bytes, {r.Nodes} nodes");
        }

        File.WriteAllText(Path.Combine(o.Out, "summary.md"), md.ToString());
    }

    private static string Kind(Result r) => r.Item.IsStylesheet ? "stylesheet" : "page";

    private static void WriteResult(Utf8JsonWriter w, Result r)
    {
        w.WriteStartObject();
        w.WriteString("sha256", r.Item.Sha256);
        w.WriteString("kind", Kind(r));
        w.WriteString("class", r.Class);
        w.WriteString("detail", r.Detail);
        w.WriteNumber("bytes", r.Item.Bytes);
        w.WriteNumber("parseMs", Math.Round(r.ParseMs, 2));
        w.WriteNumber("nodes", r.Nodes);
        w.WriteEndObject();
    }
}
