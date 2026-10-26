using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>Aggregates capture results and writes <c>summary.md</c>, <c>summary.json</c>, <c>classes.json</c> and <c>captures.jsonl</c>.</summary>
internal static class AxReport
{
    private const int ExamplesPerClass = 5;

    private sealed class FieldAgg
    {
        public long Compared;
        public long Occurrences;
        public readonly HashSet<string> Nodes = new(StringComparer.Ordinal);
        public readonly HashSet<string> Pages = new(StringComparer.Ordinal);
        public readonly HashSet<string> PagesCompared = new(StringComparer.Ordinal);
    }

    private sealed class ClassAgg(string field, string pattern)
    {
        public string Field { get; } = field;

        public string Pattern { get; } = pattern;

        public long Occurrences;
        public readonly HashSet<string> Nodes = new(StringComparer.Ordinal);
        public readonly HashSet<string> Pages = new(StringComparer.Ordinal);
        public readonly List<(string Sha, string Viewport, string Url, AxMismatch M)> Examples = [];
    }

    private sealed record Totals(int Pages, long ChromeNodes, long OursNodes, long Matched, long Anonymous);

    private static readonly string[] s_fieldOrder =
    [
        "ignored", "hidden", "ignoredReasons", "role", "name", "nameSource", "description", "value", "parent", "exposedParent", "childOrder", "chromeOnly", "oursOnly", "chromeOnlyIgnored", "oursOnlyIgnored",
    ];

    private static int FieldRank(string f)
    {
        int i = Array.IndexOf(s_fieldOrder, f);
        if (i >= 0)
        {
            return i;
        }

        return f.StartsWith("prop.", StringComparison.Ordinal) ? 100 : 200;
    }

    public static void Write(Options o, List<AxCommand.CaptureResult> results, string environment, string factsMode, double seconds)
    {
        var fields = new Dictionary<string, FieldAgg>(StringComparer.Ordinal);
        var classes = new Dictionary<string, ClassAgg>(StringComparer.Ordinal);
        long chromeNodes = 0, oursNodes = 0, matched = 0, anonymous = 0;
        var comparedPages = new HashSet<string>(StringComparer.Ordinal);
        foreach (AxCommand.CaptureResult r in results.Where(r => r.Diff is not null))
        {
            AxPageDiff d = r.Diff!;
            comparedPages.Add(r.Ref.Sha);
            chromeNodes += d.ChromeNodes;
            oursNodes += d.OursNodes;
            matched += d.Matched;
            anonymous += d.MatchedByAnonymousRule;
            foreach ((string f, long n) in d.Compared)
            {
                FieldAgg a = Get(fields, f);
                a.Compared += n;
                a.PagesCompared.Add(r.Ref.Sha);
            }

            foreach (AxMismatch m in d.Mismatches)
            {
                FieldAgg a = Get(fields, m.Field);
                a.Occurrences++;
                a.Nodes.Add(r.Ref.Sha + "|" + m.NodeKey);
                a.Pages.Add(r.Ref.Sha);
                if (!classes.TryGetValue(m.ClassKey, out ClassAgg? c))
                {
                    classes[m.ClassKey] = c = new ClassAgg(m.Field, m.Pattern);
                }

                c.Occurrences++;
                c.Nodes.Add(r.Ref.Sha + "|" + m.NodeKey);
                c.Pages.Add(r.Ref.Sha);
                if (c.Examples.Count < ExamplesPerClass && !c.Examples.Any(e => e.Sha == r.Ref.Sha && e.M.NodeKey == m.NodeKey))
                {
                    c.Examples.Add((r.Ref.Sha, r.Ref.Viewport, r.Url, m));
                }
            }
        }

        List<ClassAgg> ranked = [.. classes.Values.OrderByDescending(c => c.Occurrences).ThenBy(c => c.Field, StringComparer.Ordinal).ThenBy(c => c.Pattern, StringComparer.Ordinal)];
        List<string> fieldNames = [.. fields.Keys.OrderBy(FieldRank).ThenBy(f => f, StringComparer.Ordinal)];
        var totals = new Totals(comparedPages.Count, chromeNodes, oursNodes, matched, anonymous);

        File.WriteAllText(System.IO.Path.Combine(o.Out, "summary.md"), Markdown(o, results, environment, factsMode, seconds, fields, fieldNames, ranked, totals));
        File.WriteAllBytes(System.IO.Path.Combine(o.Out, "summary.json"), SummaryJson(results, environment, factsMode, fields, fieldNames, ranked, totals));
        File.WriteAllBytes(System.IO.Path.Combine(o.Out, "classes.json"), ClassesJson(ranked));
        File.WriteAllText(System.IO.Path.Combine(o.Out, "captures.jsonl"), CapturesJsonl(results));
    }

    private static FieldAgg Get(Dictionary<string, FieldAgg> d, string f)
    {
        if (!d.TryGetValue(f, out FieldAgg? a))
        {
            d[f] = a = new FieldAgg();
        }

        return a;
    }

    private static string Esc(string s) => s.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string Markdown(Options o, List<AxCommand.CaptureResult> results, string environment, string factsMode, double seconds, Dictionary<string, FieldAgg> fields, List<string> fieldNames, List<ClassAgg> ranked, Totals t)
    {
        var sb = new StringBuilder();
        sb.Append("# Chrome accessibility-tree differential\n\n");
        sb.Append("Format `").Append(AxCommand.Schema).Append("`; see `docs/chrome-ax-differential.md`.\n\n");
        sb.Append("- captures read: ").Append(results.Count).Append(", compared: ").Append(results.Count(r => r.Status == "ok")).Append(" (").Append(t.Pages).Append(" pages)\n");
        sb.Append("- refused snapshots: ").Append(results.Count(r => r.Status == "refused")).Append(", incomplete or failed: ").Append(results.Count(r => r.Status is "skipped" or "error")).Append('\n');
        sb.Append("- Chrome nodes compared: ").Append(t.ChromeNodes).Append(", our nodes: ").Append(t.OursNodes).Append(", matched: ").Append(t.Matched).Append(" (").Append(t.Anonymous).Append(" by the anonymous-node rule)\n");
        sb.Append("- facts mode: ").Append(factsMode).Append("; run time ").Append(seconds.ToString("0.0", CultureInfo.InvariantCulture)).Append(" s\n");
        if (environment.Length > 0)
        {
            sb.Append("- reference: ").Append(environment).Append('\n');
        }

        sb.Append("\n## Refused, incomplete and failed captures\n\n");
        var bad = results.Where(r => r.Status != "ok").ToList();
        if (bad.Count == 0)
        {
            sb.Append("None.\n");
        }
        else
        {
            sb.Append("| status | page | viewport | reason |\n|---|---|---|---|\n");
            foreach (AxCommand.CaptureResult r in bad.Take(100))
            {
                sb.Append("| ").Append(r.Status).Append(" | ").Append(Esc(r.Url.Length > 0 ? r.Url : r.Ref.Sha[..Math.Min(12, r.Ref.Sha.Length)])).Append(" | ").Append(r.Ref.Viewport).Append(" | ").Append(Esc(r.Detail)).Append(" |\n");
            }

            if (bad.Count > 100)
            {
                sb.Append("\n… and ").Append(bad.Count - 100).Append(" more in `captures.jsonl`.\n");
            }

            sb.Append("\nRefusal counts by reason: ").Append(string.Join(", ", bad.Where(r => r.Status == "refused").GroupBy(r => r.Detail.Split(':')[0], StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + " " + g.Count()))).Append('\n');
        }

        sb.Append("\n## Mismatch rate per field\n\n");
        sb.Append("Occurrences are mismatching (capture, node) pairs; nodes are distinct nodes per page; rate is occurrences over the times the field was compared (for a property, the nodes where either side has it).\n\n");
        sb.Append("| field | compared | occurrences | nodes | pages | rate |\n|---|---:|---:|---:|---:|---:|\n");
        foreach (string f in fieldNames)
        {
            FieldAgg a = fields[f];
            sb.Append("| ").Append(f).Append(" | ").Append(a.Compared).Append(" | ").Append(a.Occurrences).Append(" | ").Append(a.Nodes.Count).Append(" | ").Append(a.Pages.Count).Append('/').Append(a.PagesCompared.Count).Append(" | ").Append(Corpus.Pct(a.Occurrences, a.Compared)).Append(" |\n");
        }

        sb.Append("\n## With and without rendered facts\n\n");
        sb.Append("Nodes are matched through the snapshot's backend node ids in both runs. The tree computation does not read rendered facts yet, so the two runs build the same tree and their rates are identical. The table is kept so the runs can be compared when it does.\n\n");
        sb.Append("| field | without facts | with facts |\n|---|---:|---:|\n");
        foreach (string f in fieldNames.Where(f => f is "role" or "name" or "nameSource" or "description" or "ignored" or "hidden" or "parent" or "exposedParent" or "chromeOnly" or "oursOnly" or "chromeOnlyIgnored" or "oursOnlyIgnored" || f.StartsWith("prop.", StringComparison.Ordinal)))
        {
            FieldAgg a = fields[f];
            string r = Corpus.Pct(a.Occurrences, a.Compared);
            sb.Append("| ").Append(f).Append(" | ").Append(r).Append(" | ").Append(r).Append(" |\n");
        }

        sb.Append("\n## Top mismatch classes\n\n");
        foreach (ClassAgg c in ranked.Take(o.Top))
        {
            sb.Append("### ").Append(c.Field).Append(": ").Append(Esc(c.Pattern)).Append("\n\n");
            sb.Append(c.Occurrences).Append(" occurrences, ").Append(c.Nodes.Count).Append(" nodes, ").Append(c.Pages.Count).Append(" pages.\n\n");
            foreach ((string sha, string viewport, string url, AxMismatch m) in c.Examples.Take(3))
            {
                sb.Append("- `").Append(sha[..Math.Min(12, sha.Length)]).Append("` ").Append(viewport).Append(' ').Append(url).Append(" — `").Append(m.Path).Append("` chrome: `").Append(Esc(m.Chrome)).Append("` ours: `").Append(Esc(m.Ours)).Append("`\n");
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static byte[] SummaryJson(List<AxCommand.CaptureResult> results, string environment, string factsMode, Dictionary<string, FieldAgg> fields, List<string> fieldNames, List<ClassAgg> ranked, Totals t)
    {
        var buffer = new ArrayBufferWriter<byte>(1 << 14);
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("schema", AxCommand.Schema);
            w.WriteString("reference", environment);
            w.WriteString("facts", factsMode);
            w.WriteNumber("captures", results.Count);
            w.WriteNumber("compared", results.Count(r => r.Status == "ok"));
            w.WriteNumber("pages", t.Pages);
            w.WriteNumber("refused", results.Count(r => r.Status == "refused"));
            w.WriteNumber("failed", results.Count(r => r.Status is "skipped" or "error"));
            w.WriteNumber("chromeNodes", t.ChromeNodes);
            w.WriteNumber("oursNodes", t.OursNodes);
            w.WriteNumber("matched", t.Matched);
            w.WriteNumber("matchedAnonymous", t.Anonymous);
            w.WritePropertyName("refusals");
            w.WriteStartArray();
            foreach (AxCommand.CaptureResult r in results.Where(r => r.Status == "refused"))
            {
                w.WriteStartObject();
                w.WriteString("sha256", r.Ref.Sha);
                w.WriteString("viewport", r.Ref.Viewport);
                w.WriteString("url", r.Url);
                w.WriteString("reason", r.Detail);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WritePropertyName("fields");
            w.WriteStartArray();
            foreach (string f in fieldNames)
            {
                FieldAgg a = fields[f];
                w.WriteStartObject();
                w.WriteString("field", f);
                w.WriteNumber("compared", a.Compared);
                w.WriteNumber("occurrences", a.Occurrences);
                w.WriteNumber("nodes", a.Nodes.Count);
                w.WriteNumber("pages", a.Pages.Count);
                w.WriteNumber("pagesCompared", a.PagesCompared.Count);
                w.WriteNumber("rate", a.Compared == 0 ? 0 : Math.Round((double)a.Occurrences / a.Compared, 6));
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WritePropertyName("topClasses");
            w.WriteStartArray();
            foreach (ClassAgg c in ranked.Take(20))
            {
                WriteClass(w, c, 3);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] ClassesJson(List<ClassAgg> ranked)
    {
        var buffer = new ArrayBufferWriter<byte>(1 << 14);
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("schema", AxCommand.Schema + "/classes");
            w.WritePropertyName("classes");
            w.WriteStartArray();
            foreach (ClassAgg c in ranked)
            {
                WriteClass(w, c, ExamplesPerClass);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteClass(Utf8JsonWriter w, ClassAgg c, int examples)
    {
        w.WriteStartObject();
        w.WriteString("field", c.Field);
        w.WriteString("pattern", c.Pattern);
        w.WriteNumber("occurrences", c.Occurrences);
        w.WriteNumber("nodes", c.Nodes.Count);
        w.WriteNumber("pages", c.Pages.Count);
        w.WritePropertyName("examples");
        w.WriteStartArray();
        foreach ((string sha, string viewport, string url, AxMismatch m) in c.Examples.Take(examples))
        {
            w.WriteStartObject();
            w.WriteString("page", sha);
            w.WriteString("viewport", viewport);
            w.WriteString("url", url);
            w.WriteString("node", m.Path);
            w.WriteString("chrome", m.Chrome);
            w.WriteString("ours", m.Ours);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static string CapturesJsonl(List<AxCommand.CaptureResult> results)
    {
        var sb = new StringBuilder();
        foreach (AxCommand.CaptureResult r in results)
        {
            var buffer = new ArrayBufferWriter<byte>(256);
            using (var w = new Utf8JsonWriter(buffer))
            {
                w.WriteStartObject();
                w.WriteString("sha256", r.Ref.Sha);
                w.WriteString("viewport", r.Ref.Viewport);
                w.WriteString("url", r.Url);
                w.WriteString("status", r.Status);
                if (r.Detail.Length > 0)
                {
                    w.WriteString("detail", r.Detail);
                }

                if (r.Diff is { } d)
                {
                    w.WriteNumber("chromeNodes", d.ChromeNodes);
                    w.WriteNumber("oursNodes", d.OursNodes);
                    w.WriteNumber("matched", d.Matched);
                    w.WriteNumber("mismatches", d.Mismatches.Count);
                }

                w.WriteEndObject();
            }

            sb.Append(Encoding.UTF8.GetString(buffer.WrittenSpan)).Append('\n');
        }

        return sb.ToString();
    }
}
