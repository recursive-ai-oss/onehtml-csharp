using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        List<Mismatch> Mismatches,
        bool EncodingDefaultOnly = false);

    private sealed record ClassInfo(string Key, string Field, List<PageResult> Pages, Mismatch Example);

    private static readonly string[] s_fields =
    [
        "encoding", "quirks", "kind", "namespace", "localName", "attr.name", "attr.prefix", "attr.namespace", "attr.value", "attr.order",
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
        Dictionary<(string, Mismatch), string> causes = Diagnose(o, results);
        List<ClassInfo> classes = Group(results, causes);
        WriteReports(o, results, classes, clock.Elapsed.TotalSeconds);
        int bad = results.Count(r => r.Error is not null || r.Mismatches.Count > 0);
        Console.WriteLine($"angle: {results.Count - bad} identical, {bad} differ in {classes.Count} classes; summary in {Path.GetFullPath(o.Out)}");
        return 0;
    }

    private static PageResult ComparePage(CorpusItem item, int timeoutSeconds)
    {
        try
        {
            return ComparePageCore(item, timeoutSeconds);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The comparison itself must never abort a corpus run.
            return new PageResult(item, "compare:" + Guard.Classify(ex), 0, string.Empty, string.Empty, []);
        }
    }

    private static PageResult ComparePageCore(CorpusItem item, int timeoutSeconds)
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
        // HTML 13.2.3.2 leaves the last-resort encoding to the user agent's locale: windows-1252 for most locales, which is
        // ours. If only that default separates us from AngleSharp (it falls back to UTF-8), the page is not a mismatch.
        bool defaultOnly = false;
        if (!SameEncoding(ourEnc, theirEnc))
        {
            defaultOnly = SameEncoding(SniffedWithUtf8Default(bytes, item.ContentType), theirEnc);
            if (!defaultOnly)
            {
                found.Insert(0, new Mismatch("encoding", "#document", ourEnc, theirEnc, "#document", -1));
            }
        }

        return new PageResult(item, null, ours.NodeCount, ourEnc, theirEnc, found, defaultOnly);
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

    private static string SniffedWithUtf8Default(byte[] bytes, string? contentType) =>
        Encodings.SourceText.Decode(bytes, new Encodings.DecodeOptions { ContentType = contentType, DefaultEncoding = Encodings.WhatwgEncoding.ForLabel("utf-8") }).Encoding.Name;

    private static bool SameEncoding(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ---- classes and reproducers ----

    /// <summary>
    /// The class a mismatch is counted under: its field and node types, and the innermost element of its path (the
    /// ancestors are left out, so one cause is not split into a class per page layout).
    /// </summary>
    private static string ClassKey(Mismatch m)
    {
        string d = m.NodeDesc;
        int gt = d.LastIndexOf('>');
        return m.Signature + " @ " + (gt >= 0 ? d[(gt + 1)..] : d);
    }

    private static bool IsAttributeLevel(Mismatch m) => m.Field is "attr.prefix" or "attr.name" or "attr.value" or "attr.namespace" or "attr.order";

    /// <summary>
    /// The mismatches of a page that count towards classes: the document-level ones and every other one that is not
    /// inside the subtree of an earlier structural difference. Below a structural difference (different child counts or
    /// kinds) the two trees no longer line up by position, so what the comparison reports there is a consequence, not a
    /// second difference.
    /// </summary>
    private static List<Mismatch> Picked(PageResult r)
    {
        var picked = new List<Mismatch>();
        var structural = new List<string>();
        foreach (Mismatch m in r.Mismatches)
        {
            if (m.Field is "encoding" or "quirks")
            {
                picked.Add(m);
                continue;
            }

            if (structural.Exists(p => m.Path.StartsWith(p + "/", StringComparison.Ordinal)))
            {
                continue;
            }

            picked.Add(m);
            if (!IsAttributeLevel(m))
            {
                structural.Add(m.Path);
            }
        }

        // One mismatch per class and page: the same difference repeated on many elements counts once.
        return [.. picked.GroupBy(ClassKey, StringComparer.Ordinal).Select(g => g.First())];
    }

    private static List<ClassInfo> Group(List<PageResult> results, Dictionary<(string Sha, Mismatch M), string> causes)
    {
        var map = new Dictionary<string, ClassInfo>(StringComparer.Ordinal);
        foreach (PageResult r in results)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Mismatch m in Picked(r))
            {
                string key = ClassKey(m);
                key += causes.TryGetValue((r.Item.Sha256, m), out string? cause) ? " [" + cause + "]" : (m.Field is "encoding" or "quirks" ? string.Empty : " [not-analysed]");

                if (!seen.Add(key))
                {
                    continue;
                }

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

    private static string CauseTag(ClassInfo c)
    {
        int open = c.Key.LastIndexOf('[');
        return open >= 0 && c.Key.EndsWith(']') ? c.Key[(open + 1)..^1] : "(document-level)";
    }

    internal static string ClassId(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..10];

    private static readonly Dictionary<string, string> s_reproNotes = new(StringComparer.Ordinal);

    /// <summary>
    /// Known causes of divergence, each a section of <c>docs/anglesharp-differential.md</c> where our behaviour is tied to
    /// the standard's text. A cause is recognised from the field and node types of a mismatch, or, for structural
    /// differences, from the minimized input that still shows it. A class with no cause is <c>unexplained</c>.
    /// </summary>
    private static readonly (string Id, string Fields, Regex Signature, Regex? Input)[] s_causes =
    [
        ("xlink-prefix", "attr.prefix", Re(".*"), null),
        ("xlink-attributes", "attr.name", Re(".*"), Re(@"xlink:")),
        ("processing-instruction", "kind", Re(@"^kind\|#ProcessingInstruction\|#Comment$"), null),
        ("svg-tag-names", "localName", Re(@"^localName\|[a-z]+[A-Z]\w*\|[a-z]+$"), null),
        ("encoding-gbk-name", "encoding", Re(@"^encoding\|GBK\|gb18030$"), null),
        ("encoding-xml-declaration", "encoding", Re(@"^encoding\|(Shift_JIS|EUC-JP|ISO-2022-JP|GBK|Big5|EUC-KR)\|utf-8$"), null),
        ("encoding-charset-parameter-case", "encoding", Re(@"^encoding\|(UTF-8\|windows-1252|windows-1252\|utf-8)$"), null),
        ("declarative-shadow-root", "childCount|template", Re(".*"), Re(@"shadowrootmode")),
        ("search-element", "childCount|text|kind", Re(".*"), Re(@"<search\b")),
        ("menuitem-element", "childCount|text|kind", Re(".*"), Re(@"<menuitem\b")),
        ("select-content", "childCount|text|kind", Re(".*"), Re(@"<select\b")),
        ("cell-end-tag", "childCount|text|kind", Re(".*"), Re(@"</t[dh]\b")),
        ("character-reference-bare-ampersand", "text|attr.value", Re(".*"), Re(@"&;")),
        ("raw-text-end-tag-case", "text", Re(".*"), Re(@"<(style|script|title|textarea|xmp|iframe|noembed|noframes)\b[\s\S]*</[A-Za-z]")),
        ("adoption-agency-time-element", "childCount|text|kind", Re(".*"), Re(@"<time\b")),
        ("form-end-tag-in-template", "childCount|text|kind", Re(".*"), Re(@"<template\b[\s\S]*</form")),
    ];

    private static Regex Re(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>Names the cause of a mismatch from its signature alone, or, if the cause needs an input, from the minimized input.</summary>
    private static string? CauseOf(Mismatch m, string? minimized)
    {
        // AngleSharp stops nesting at 512 open elements and adds later elements as siblings; we keep nesting up to
        // ParseLimits.MaxOpenElements (4096). The path of such a mismatch is that deep.
        if (m.Field is "childCount" or "text" or "kind" && m.Path.Count(c => c == '/') >= 500)
        {
            return "nesting-depth-cap";
        }

        foreach ((string id, string fields, Regex signature, Regex? input) in s_causes)
        {
            if (!fields.Split('|').Contains(m.Field) || !signature.IsMatch(m.Signature))
            {
                continue;
            }

            if (input is null || (minimized is not null && input.IsMatch(minimized)))
            {
                return id;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the cause of every counted mismatch. Differences that need the input to be understood are minimized per page
    /// (a reproducer keeps the class it was made for: the same class key and the same path shape), so that classes group
    /// pages by cause. Reproducers are written once per class.
    /// </summary>
    private static Dictionary<(string, Mismatch), string> Diagnose(Options o, List<PageResult> results)
    {
        string dir = Path.Combine(o.Out, "repro");
        Directory.CreateDirectory(dir);
        var causes = new System.Collections.Concurrent.ConcurrentDictionary<(string, Mismatch), string>();
        var work = new List<(PageResult Page, Mismatch M)>();
        foreach (PageResult r in results)
        {
            foreach (Mismatch m in Picked(r))
            {
                string? direct = CauseOf(m, null);
                if (direct is not null)
                {
                    causes[(r.Item.Sha256, m)] = direct;
                }
                else if (m.Field is not ("encoding" or "quirks"))
                {
                    work.Add((r, m));
                }
            }
        }

        if (work.Count > o.MaxReduce)
        {
            work.RemoveRange(o.MaxReduce, work.Count - o.MaxReduce);
        }

        Console.WriteLine($"angle: minimizing {work.Count} mismatches");
        var written = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = o.Jobs }, w =>
        {
            string note;
            string? minimized = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(w.Page.Item.Path);
                Document ours = Document.Parse(bytes, new ParseOptions { ContentType = w.Page.Item.ContentType });
                string text = ours.Source.Text;
                string baseKey = ClassKey(w.M);
                bool Diverges(string s)
                {
                    try
                    {
                        Document d = Document.Parse(s);
                        IDocument t = new HtmlParser().ParseDocument(s);
                        return TreeComparer.Compare(d, t).Any(x => ClassKey(x) == baseKey && x.NodeDesc == w.M.NodeDesc);
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
                    minimized = Reducer.Reduce(text, w.M.Hint, Diverges);
                    note = $"minimized {text.Length} -> {minimized.Length} chars";
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                note = "reducer failed: " + ex.GetType().Name;
            }

            string cause = CauseOf(w.M, minimized) ?? "unexplained";
            causes[(w.Page.Item.Sha256, w.M)] = cause;
            string key = ClassKey(w.M) + " [" + cause + "]";
            if (written.TryAdd(key, 0))
            {
                lock (s_reproNotes)
                {
                    s_reproNotes[key] = note;
                }

                if (minimized is not null)
                {
                    string classDir = Path.Combine(dir, ClassId(key));
                    Directory.CreateDirectory(classDir);
                    File.WriteAllText(Path.Combine(classDir, "min.html"), minimized, new UTF8Encoding(false));
                }
            }
        });
        return new Dictionary<(string, Mismatch), string>(causes);
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
            .GroupBy(r => r.OurEncoding + " -> " + r.TheirEncoding + (r.EncodingDefaultOnly ? " (locale default only, not a mismatch)" : string.Empty)).OrderByDescending(g => g.Count()).ToList();

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
                w.WriteString("exampleOurs", c.Example.Ours);
                w.WriteString("exampleTheirs", c.Example.Theirs);
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

        md.AppendLine("## Causes").AppendLine();
        md.AppendLine("Each class is tagged with the cause it was traced to; the causes are explained in `docs/anglesharp-differential.md`.").AppendLine();
        md.AppendLine("| cause | classes | pages |").AppendLine("|---|---:|---:|");
        foreach (IGrouping<string, ClassInfo> g in classes.GroupBy(CauseTag, StringComparer.Ordinal).OrderBy(g => g.Key is "unexplained" or "not-analysed" ? 0 : 1).ThenByDescending(g => g.Sum(c => c.Pages.Count)))
        {
            int distinctPages = g.SelectMany(c => c.Pages).Select(p => p.Item.Sha256).Distinct(StringComparer.Ordinal).Count();
            md.AppendLine(CultureInfo.InvariantCulture, $"| {g.Key} | {g.Count()} | {distinctPages} |");
        }

        md.AppendLine().AppendLine("## Classes (per page: each difference outside the subtree of an earlier structural one)").AppendLine();
        md.AppendLine("| id | pages | class | first example | example page | reproducer |").AppendLine("|---|---:|---|---|---|---|");
        foreach (ClassInfo c in classes.Take(Math.Max(o.Top, 50)))
        {
            s_reproNotes.TryGetValue(c.Key, out string? note);
            md.AppendLine(CultureInfo.InvariantCulture, $"| {ClassId(c.Key)} | {c.Pages.Count} | `{c.Key}` | ours `{c.Example.Ours.Replace('|', '/')}` / theirs `{c.Example.Theirs.Replace('|', '/')}` | `{c.Pages[0].Item.Sha256[..12]}` | {note ?? "-"} |");
        }

        File.WriteAllText(Path.Combine(o.Out, "summary.md"), md.ToString());
    }
}
