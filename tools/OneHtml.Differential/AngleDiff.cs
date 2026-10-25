using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// The AngleSharp tree differential. Both parsers get the same decoded text (the text our sniffer chose), so the tree
/// comparison is not muddied by encoding; the two sniffed encodings are compared as a field of their own.
/// </summary>
internal static class AngleDiff
{
    private sealed record PageResult(
        CorpusItem Item,
        string? Error,
        long Nodes,
        string OurEncoding,
        string TheirEncoding,
        List<Mismatch> Mismatches);

    private sealed record ClassInfo(string Key, string Field, List<PageResult> Pages, Mismatch Example);

    private static readonly string[] s_fields =
    [
        "encoding", "quirks", "kind", "namespace", "localName", "attr.name", "attr.namespace", "attr.value", "attr.order",
        "text", "comment", "doctype.name", "doctype.public", "doctype.system", "template", "childCount",
    ];

    public static int Run(Options o)
    {
        List<CorpusItem> items = Corpus.Load(o.Corpus, pages: true, stylesheets: false);
        if (items.Count > o.Limit)
        {
            items.RemoveRange(o.Limit, items.Count - o.Limit);
        }

        Console.WriteLine($"angle: {items.Count} pages, jobs {o.Jobs}, timeout {o.TimeoutSeconds}s");
        var results = new List<PageResult>(items.Count);
        var clock = Stopwatch.StartNew();
        int done = 0;
        Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = o.Jobs }, item =>
        {
            PageResult r = ComparePage(item, o.TimeoutSeconds);
            lock (results)
            {
                results.Add(r);
            }

            int n = Interlocked.Increment(ref done);
            if (n % 200 == 0)
            {
                Console.WriteLine($"  {n}/{items.Count} after {clock.Elapsed.TotalSeconds:0}s");
            }
        });

        results.Sort(static (a, b) => string.CompareOrdinal(a.Item.Sha256 + a.Item.Url, b.Item.Sha256 + b.Item.Url));
        Directory.CreateDirectory(o.Out);
        List<ClassInfo> classes = Group(results);
        Reproduce(o, classes);
        WriteReports(o, results, classes, clock.Elapsed.TotalSeconds);
        int bad = results.Count(r => r.Error is not null || r.Mismatches.Count > 0);
        Console.WriteLine($"angle: {results.Count - bad} identical, {bad} differ in {classes.Count} classes; summary in {Path.GetFullPath(o.Out)}");
        return 0;
    }

    private static PageResult ComparePage(CorpusItem item, int timeoutSeconds)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(item.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PageResult(item, "missing-body", 0, string.Empty, string.Empty, []);
        }

        (Document? ours, string? failure) = Guard.Run(ct => ParseOurs(bytes, item.ContentType, ct), timeoutSeconds);
        if (ours is null)
        {
            return new PageResult(item, "ours:" + failure, 0, string.Empty, string.Empty, []);
        }

        (IDocument? theirs, failure) = Guard.Run(ct => new HtmlParser().ParseDocument(ours.Source.Text), timeoutSeconds);
        if (theirs is null)
        {
            return new PageResult(item, "angle:" + failure, ours.NodeCount, ours.Source.Encoding.Name, string.Empty, []);
        }

        (string? theirEnc, _) = Guard.Run(ct => SniffedByAngleSharp(bytes, item.ContentType), timeoutSeconds);
        theirEnc ??= "(error)";
        List<Mismatch> found = TreeComparer.Compare(ours, theirs);
        string ourEnc = ours.Source.Encoding.Name;
        if (!SameEncoding(ourEnc, theirEnc))
        {
            found.Insert(0, new Mismatch("encoding", "#document", ourEnc, theirEnc, "#document", -1));
        }

        return new PageResult(item, null, ours.NodeCount, ourEnc, theirEnc, found);
    }

    private static Document ParseOurs(byte[] bytes, string? contentType, CancellationToken ct) =>
        Document.Parse(bytes, new ParseOptions { ContentType = contentType }, ct);

    private static string SniffedByAngleSharp(byte[] bytes, string? contentType)
    {
        IBrowsingContext context = BrowsingContext.New(Configuration.Default);
        using var stream = new MemoryStream(bytes, writable: false);
        IDocument doc = context.OpenAsync(res =>
        {
            res.Content(stream);
            if (contentType is not null)
            {
                res.Header("Content-Type", contentType);
            }
        }).GetAwaiter().GetResult();
        return doc.CharacterSet ?? "(none)";
    }

    private static bool SameEncoding(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ---- classes and reproducers ----

    private static string ClassKey(Mismatch m) => m.Signature + " @ " + m.NodeDesc;

    private static List<ClassInfo> Group(List<PageResult> results)
    {
        var map = new Dictionary<string, ClassInfo>(StringComparer.Ordinal);
        foreach (PageResult r in results)
        {
            // One class per page for the document-level fields, and one for the first difference in the tree.
            var picked = new List<Mismatch>();
            picked.AddRange(r.Mismatches.Where(m => m.Field is "encoding" or "quirks"));
            Mismatch? first = r.Mismatches.FirstOrDefault(m => m.Field is not ("encoding" or "quirks"));
            if (first is not null)
            {
                picked.Add(first);
            }

            foreach (Mismatch m in picked)
            {
                string key = ClassKey(m);
                if (!map.TryGetValue(key, out ClassInfo? c))
                {
                    c = new ClassInfo(key, m.Field, [], m);
                    map[key] = c;
                }

                c.Pages.Add(r);
            }
        }

        return [.. map.Values.OrderByDescending(c => c.Pages.Count).ThenBy(c => c.Key, StringComparer.Ordinal)];
    }

    internal static string ClassId(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..10];

    private static readonly Dictionary<string, string> s_reproNotes = new(StringComparer.Ordinal);

    private static void Reproduce(Options o, List<ClassInfo> classes)
    {
        string dir = Path.Combine(o.Out, "repro");
        Directory.CreateDirectory(dir);
        var targets = classes.Where(c => c.Field != "encoding").Take(o.MaxReduce).ToList();
        Console.WriteLine($"angle: minimizing {targets.Count} classes");
        Parallel.ForEach(targets, new ParallelOptions { MaxDegreeOfParallelism = o.Jobs }, c =>
        {
            PageResult page = c.Pages[0];
            string note;
            try
            {
                byte[] bytes = File.ReadAllBytes(page.Item.Path);
                Document ours = Document.Parse(bytes, new ParseOptions { ContentType = page.Item.ContentType });
                string text = ours.Source.Text;
                Mismatch m = page.Mismatches.First(x => ClassKey(x) == c.Key);
                string sig = m.Signature;
                bool Diverges(string s)
                {
                    try
                    {
                        Document d = Document.Parse(s);
                        IDocument t = new HtmlParser().ParseDocument(s);
                        return TreeComparer.Compare(d, t).Any(x => x.Signature == sig);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        return false;
                    }
                }

                if (!Diverges(text))
                {
                    note = "not reproducible from the decoded text alone";
                }
                else
                {
                    string min = Reducer.Reduce(text, m.Hint, Diverges);
                    string classDir = Path.Combine(dir, ClassId(c.Key));
                    Directory.CreateDirectory(classDir);
                    File.WriteAllText(Path.Combine(classDir, "min.html"), min, new UTF8Encoding(false));
                    note = $"minimized {text.Length} -> {min.Length} chars";
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                note = "reducer failed: " + ex.GetType().Name;
            }

            lock (s_reproNotes)
            {
                s_reproNotes[c.Key] = note;
            }
        });
    }

    // ---- reports ----

    private static void WriteReports(Options o, List<PageResult> results, List<ClassInfo> classes, double seconds)
    {
        int compared = results.Count(r => r.Error is null);
        int identical = results.Count(r => r.Error is null && r.Mismatches.Count == 0);
        var errors = results.Where(r => r.Error is not null).GroupBy(r => r.Error!).OrderByDescending(g => g.Count()).ToList();

        string shaDir = Path.Combine(o.Out, "classes");
        Directory.CreateDirectory(shaDir);
        foreach (ClassInfo c in classes)
        {
            File.WriteAllLines(Path.Combine(shaDir, ClassId(c.Key) + ".sha256.txt"), c.Pages.Select(p => p.Item.Sha256).Distinct(StringComparer.Ordinal));
        }

        using (var fs = File.Create(Path.Combine(o.Out, "pages.jsonl")))
        {
            foreach (PageResult r in results)
            {
                using var w = new Utf8JsonWriter(fs);
                w.WriteStartObject();
                w.WriteString("sha256", r.Item.Sha256);
                w.WriteNumber("nodes", r.Nodes);
                if (r.Error is not null)
                {
                    w.WriteString("error", r.Error);
                }

                w.WriteString("ourEncoding", r.OurEncoding);
                w.WriteString("theirEncoding", r.TheirEncoding);
                w.WriteStartObject("mismatchesByField");
                foreach (IGrouping<string, Mismatch> g in r.Mismatches.GroupBy(m => m.Field).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    w.WriteNumber(g.Key, g.Count());
                }

                w.WriteEndObject();
                w.WriteEndObject();
                w.Flush();
                fs.WriteByte((byte)'\n');
            }
        }

        var encPairs = results.Where(r => r.Error is null && !SameEncoding(r.OurEncoding, r.TheirEncoding))
            .GroupBy(r => r.OurEncoding + " -> " + r.TheirEncoding).OrderByDescending(g => g.Count()).ToList();

        using (var fs = File.Create(Path.Combine(o.Out, "summary.json")))
        using (var w = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("command", "angle");
            w.WriteNumber("pages", results.Count);
            w.WriteNumber("compared", compared);
            w.WriteNumber("identical", identical);
            w.WriteNumber("wallSeconds", Math.Round(seconds, 1));
            w.WriteStartObject("errors");
            foreach (IGrouping<string, PageResult> g in errors)
            {
                w.WriteNumber(g.Key, g.Count());
            }

            w.WriteEndObject();
            w.WriteStartArray("fields");
            foreach (string f in s_fields)
            {
                int pagesWith = results.Count(r => r.Mismatches.Any(m => m.Field == f));
                long occurrences = results.Sum(r => r.Mismatches.Count(m => m.Field == f));
                w.WriteStartObject();
                w.WriteString("field", f);
                w.WriteNumber("pagesWithMismatch", pagesWith);
                w.WriteNumber("pageRate", compared == 0 ? 0 : Math.Round((double)pagesWith / compared, 6));
                w.WriteNumber("occurrences", occurrences);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartArray("encodingPairs");
            foreach (IGrouping<string, PageResult> g in encPairs.Take(o.Top))
            {
                w.WriteStartObject();
                w.WriteString("pair", g.Key);
                w.WriteNumber("pages", g.Count());
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartArray("classes");
            foreach (ClassInfo c in classes)
            {
                w.WriteStartObject();
                w.WriteString("id", ClassId(c.Key));
                w.WriteString("key", c.Key);
                w.WriteString("field", c.Field);
                w.WriteNumber("pages", c.Pages.Count);
                w.WriteString("exampleSha256", c.Pages[0].Item.Sha256);
                w.WriteString("examplePath", c.Example.Path);
                if (s_reproNotes.TryGetValue(c.Key, out string? note))
                {
                    w.WriteString("reproducer", note);
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        var md = new StringBuilder();
        md.AppendLine("# AngleSharp tree differential").AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"- pages: {results.Count}; compared: {compared}; identical trees: {identical} ({Corpus.Pct(identical, compared)}); wall time {seconds:0.0}s");
        foreach (IGrouping<string, PageResult> g in errors)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"- not compared, `{g.Key}`: {g.Count()}");
        }

        md.AppendLine().AppendLine("## Mismatch rate per field").AppendLine();
        md.AppendLine("| field | pages | page rate | occurrences |").AppendLine("|---|---:|---:|---:|");
        foreach (string f in s_fields)
        {
            int pagesWith = results.Count(r => r.Mismatches.Any(m => m.Field == f));
            long occurrences = results.Sum(r => r.Mismatches.Count(m => m.Field == f));
            md.AppendLine(CultureInfo.InvariantCulture, $"| {f} | {pagesWith} | {Corpus.Pct(pagesWith, compared)} | {occurrences} |");
        }

        md.AppendLine().AppendLine("Quirks: AngleSharp reports only quirks and not-quirks, so limited-quirks is compared as standards mode.").AppendLine();
        if (encPairs.Count > 0)
        {
            md.AppendLine("## Encoding disagreements (ours -> AngleSharp)").AppendLine();
            foreach (IGrouping<string, PageResult> g in encPairs.Take(o.Top))
            {
                md.AppendLine(CultureInfo.InvariantCulture, $"- {g.Key}: {g.Count()}");
            }

            md.AppendLine();
        }

        md.AppendLine("## Classes (first divergence per page)").AppendLine();
        md.AppendLine("| id | pages | class | example | reproducer |").AppendLine("|---|---:|---|---|---|");
        foreach (ClassInfo c in classes.Take(Math.Max(o.Top, 50)))
        {
            s_reproNotes.TryGetValue(c.Key, out string? note);
            md.AppendLine(CultureInfo.InvariantCulture, $"| {ClassId(c.Key)} | {c.Pages.Count} | `{c.Key}` | `{c.Pages[0].Item.Sha256[..12]}` | {note ?? "-"} |");
        }

        File.WriteAllText(Path.Combine(o.Out, "summary.md"), md.ToString());
    }
}
