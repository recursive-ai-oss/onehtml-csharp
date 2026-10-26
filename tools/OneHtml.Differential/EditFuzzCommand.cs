using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using RecursiveAI.OneHtml.Fuzz;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// <c>editfuzz</c>: random edit transactions on corpus pages and stylesheets. Page transactions over every HTML edit
/// operation (inline <c>style=""</c> included) run the fuzz target's checks: the re-parsed tree equals an independent
/// reference, bytes outside the edited ranges are unchanged, change sets are consistent, and a clean single edit is
/// undone exactly by its inverse. <c>&lt;style&gt;</c> element transactions and linked-stylesheet transactions check
/// the same for rule and declaration edits. Reports the fallback rate per operation.
/// </summary>
internal static class EditFuzzCommand
{
    private static readonly string[] s_props = ["color", "margin", "--x", "background", "display", "Width", "font-family", "--Y"];
    private static readonly string[] s_values = ["red", "0", "1px solid #fff", "calc(1px + 2px)", "var(--x)", "url(a.png)", "\"s\"", "a b c", "rgb(1 2 3 / 50%)", "{a}", "1px/2px", "\\31 23"];
    private static readonly string[] s_rules = ["x{y:z}", "@media print{a{b:c}}", "@import \"a.css\";", "p, q > r {\n  s: t;\n}", "a{}", "@font-face{src:url(x)}", "u { v: w; x: y !important }", "  b  { c : d }  ", ".a:has(.b){c:d}", "a{&:hover{b:c}}"];

    private static readonly string[] s_checkPhrases =
    [
        "failed but returned a snapshot", "produced a broken document", "differs from the reference", "ranges overlap or are out of order",
        "changed bytes outside the edited ranges", "reported bytes that are not in the output", "changed text outside the edited ranges",
        "reported text that is not in the output", "changed text after the last range", "lists an added node of the wrong snapshot",
        "lists a removed node of the wrong snapshot", "lists an inconsistent change", "could not be reverted", "was not restored by its inverse",
    ];

    private static readonly Lock s_lock = new();
    private static readonly EditStats s_stats = new();
    private static readonly SortedDictionary<string, long[]> s_sheetOps = [];

    public static int Run(Options o)
    {
        List<CorpusItem> items = Corpus.Load(o.Corpus, pages: o.Only != "stylesheets", stylesheets: o.Only != "pages");
        int code = GateRunner.Run(o, "editfuzz", items, (item, bytes, report, ct) =>
        {
            if (item.IsStylesheet)
            {
                CheckStylesheet(o, item, bytes, report, ct);
            }
            else
            {
                CheckPage(o, item, bytes, report, ct);
            }
        });
        WriteOperations(o);
        return code;
    }

    private static void CheckPage(Options o, CorpusItem item, byte[] bytes, GateReport report, CancellationToken ct)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Document doc = Document.Parse(bytes, new ParseOptions { ContentType = item.ContentType }, ct);
        if (GateRunner.Trace)
        {
            Console.Error.WriteLine(FormattableString.Invariant($"{item.Sha256[..7]}: {bytes.Length} bytes, {doc.NodeCount} nodes, parse {clock.Elapsed.TotalMilliseconds:0.0} ms"));
        }

        var rng = new Random(o.Seed ^ Convert.ToInt32(item.Sha256[..7], 16));
        report.Count("pages");
        var local = new EditStats();
        if (GateRunner.Trace && o.Jobs == 1)
        {
            EditTarget.Observer = (_, edits, result) => Console.Error.WriteLine(FormattableString.Invariant(
                $"    apply at {clock.Elapsed.TotalMilliseconds:0.0} ms: {string.Join(',', edits.Select(e => e.Operation + "#" + e.Target.Index))} succeeded {result.Succeeded} fallbacks {result.Fallbacks.Count} verifications {result.Verifications}"));
        }

        var failed = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < o.PerPage; i++)
        {
            ct.ThrowIfCancellationRequested();
            int seed = rng.Next();
            clock.Restart();
            try
            {
                EditTarget.CheckDocument(doc, seed, local);
                if (GateRunner.Trace)
                {
                    Console.Error.WriteLine(FormattableString.Invariant($"  html transaction {i} seed {seed}: {clock.Elapsed.TotalMilliseconds:0.0} ms"));
                }
            }
            catch (EditCheckException ex)
            {
                string cls = "html:" + CheckClass(ex.Message);
                if (failed.Add(cls))
                {
                    report.Fail(cls, item.Sha256 + " seed " + seed.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                string cls = "html:" + GateRunner.Classify(ex);
                if (failed.Add(cls))
                {
                    report.Fail(cls, item.Sha256 + " seed " + seed.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        // <style> elements: sheet edits spliced into the page.
        IReadOnlyList<InlineStylesheet> inlines;
        try
        {
            inlines = InlineStylesheets.Of(doc);
        }
        catch (CssLimitExceededException)
        {
            inlines = [];
        }

        for (int i = 0; i < o.PerPage && inlines.Count > 0; i++)
        {
            ct.ThrowIfCancellationRequested();
            int seed = rng.Next();
            var r2 = new Random(seed);
            InlineStylesheet inl = inlines[r2.Next(inlines.Count)];
            clock.Restart();
            try
            {
                CheckStyleElement(doc, inl, r2, report, ct, out string? problem, out string label);
                if (GateRunner.Trace)
                {
                    Console.Error.WriteLine(FormattableString.Invariant($"  style transaction {i} ({inl.Sheet.Text.Length} chars) {label}: {clock.Elapsed.TotalMilliseconds:0.0} ms"));
                }

                if (problem is not null && failed.Add("style-element:" + problem))
                {
                    report.Fail("style-element:" + problem, item.Sha256 + " seed " + seed.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                string cls = "style-element:" + GateRunner.Classify(ex);
                if (failed.Add(cls))
                {
                    report.Fail(cls, item.Sha256 + " seed " + seed.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        lock (s_lock)
        {
            s_stats.Add(local);
        }

        report.Count("html-transactions", local.Transactions);
        report.Count("html-transactions-applied", local.Applied);
        report.Count("html-transactions-with-fallback", local.WithFallback);
        report.Count("html-reverts-checked", local.Reverts);
    }

    private static string CheckClass(string message)
    {
        int threw = message.IndexOf(" threw ", StringComparison.Ordinal);
        if (threw >= 0)
        {
            string rest = message[(threw + 7)..];
            int end = rest.IndexOfAny([':', ' ', '\n', '\r']);
            return "threw:" + (end > 0 ? rest[..end] : rest);
        }

        foreach (string p in s_checkPhrases)
        {
            if (message.Contains(p, StringComparison.Ordinal))
            {
                return p;
            }
        }

        return "unclassified";
    }

    private static void CheckStyleElement(Document doc, InlineStylesheet inl, Random rng, GateReport report, CancellationToken ct, out string? problem, out string label)
    {
        problem = null;
        List<SheetEdit> edits = RandomSheetEdits(inl.Sheet, rng, out label, 1 + rng.Next(3));
        if (edits.Count == 0)
        {
            return;
        }

        BatchResult r = new EditBatch(doc, null, edits).Apply(new EditOptions { MaxVerifications = 64 }, ct);
        if (!r.Succeeded)
        {
            CountSheet("style-element:" + label, applied: false, fallback: false, r.Outcomes.Any(x => x.Status == EditStatus.Failed));
            CountRefusal(report, "style-element", r);
            return;
        }

        bool fallback = r.Sheets.Any(s => s.Changes.Ranges.Any(x => x.IsFallback)) || r.Fallbacks.Count > 0;
        CountSheet("style-element:" + label, applied: true, fallback, false);
        Document newDoc = r.Document!;
        if (r.Changes is null)
        {
            problem = "no-change-set";
            return;
        }

        problem = CheckPageBytes(doc, r.Changes);
        if (problem is not null)
        {
            return;
        }

        if (RecursiveAI.OneHtml.Tests.DocumentInvariants.Check(newDoc) is not null)
        {
            problem = "broken-document";
            return;
        }

        IReadOnlyList<InlineStylesheet> after = InlineStylesheets.Of(newDoc);
        SheetUpdate su = r.Sheets[0];
        if (su.OldInline is null || su.OldInline.Index >= after.Count || !string.Equals(after[su.OldInline.Index].Sheet.Text, su.NewSheet.Text, StringComparison.Ordinal))
        {
            problem = "new-sheet-differs-from-expected";
        }
    }

    /// <summary>The bytes outside the reported ranges are the old bytes and the ranges' new bytes fill the rest.</summary>
    private static string? CheckPageBytes(Document oldDoc, ChangeSet changes)
    {
        ReadOnlySpan<byte> a = oldDoc.Source.Bytes.Span;
        ReadOnlySpan<byte> b = changes.NewDocument.Source.Bytes.Span;
        int oldPos = 0;
        int newPos = 0;
        foreach (EditedRange r in changes.Ranges)
        {
            if (r.OldByteRange.Start < oldPos || r.OldByteRange.End < r.OldByteRange.Start || r.NewByteRange.Start < newPos || r.NewByteRange.End > b.Length || r.OldByteRange.End > a.Length)
            {
                return "ranges-out-of-order";
            }

            int gap = r.OldByteRange.Start - oldPos;
            if (r.NewByteRange.Start - newPos != gap || !a.Slice(oldPos, gap).SequenceEqual(b.Slice(newPos, gap)))
            {
                return "bytes-changed-outside-ranges";
            }

            if (!b[r.NewByteRange.Start..r.NewByteRange.End].SequenceEqual(r.NewBytes.Span))
            {
                return "range-bytes-not-in-output";
            }

            oldPos = r.OldByteRange.End;
            newPos = r.NewByteRange.End;
        }

        return a.Length - oldPos == b.Length - newPos && a[oldPos..].SequenceEqual(b[newPos..]) ? null : "bytes-changed-after-last-range";
    }

    private static void CheckStylesheet(Options o, CorpusItem item, byte[] bytes, GateReport report, CancellationToken ct)
    {
        string? charset = CharsetOf(item.ContentType);
        LinkedStylesheet linked = LinkedStylesheet.Create(item.Url, bytes, charset);
        var rng = new Random(o.Seed ^ Convert.ToInt32(item.Sha256[..7], 16));
        report.Count("stylesheets");
        var failed = new HashSet<string>(StringComparer.Ordinal);
        for (int round = 0; round < o.PerPage; round++)
        {
            ct.ThrowIfCancellationRequested();
            int seed = rng.Next();
            var r2 = new Random(seed);
            List<SheetEdit> edits = RandomSheetEdits(linked.Sheet, r2, out string label, 1 + r2.Next(3));
            if (edits.Count == 0)
            {
                break;
            }

            string? problem = null;
            try
            {
                BatchResult r = new EditBatch(null, null, edits, [linked]).Apply(new EditOptions { MaxVerifications = 64 }, ct);
                if (!r.Succeeded)
                {
                    CountSheet("stylesheet:" + label, applied: false, fallback: false, r.Outcomes.Any(x => x.Status == EditStatus.Failed));
                    CountRefusal(report, "stylesheet", r);
                    continue;
                }

                SheetUpdate su = r.Sheets[0];
                CountSheet("stylesheet:" + label, applied: true, su.Changes.Ranges.Any(x => x.IsFallback), false);
                problem = CheckRanges(linked.Sheet.Text, su.Changes);
                if (problem is null && !string.Equals(Stylesheet.Parse(su.NewSheet.Text, null, null, ct).ToSourceText(), su.NewSheet.Text, StringComparison.Ordinal))
                {
                    problem = "new-sheet-not-lossless";
                }

                if (problem is null && su.NewLinked is not null && Utf8RoundTrips(bytes, linked.Sheet.Text)
                    && !su.NewLinked.Bytes.Span.SequenceEqual(Encoding.UTF8.GetBytes(su.NewSheet.Text)))
                {
                    problem = "new-bytes-differ-from-new-text";
                }

                if (problem is null && su.NewLinked is not null)
                {
                    linked = su.NewLinked;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                problem = GateRunner.Classify(ex);
            }

            if (problem is not null && failed.Add(problem))
            {
                report.Fail("stylesheet:" + problem, item.Sha256 + " seed " + seed.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private static bool Utf8RoundTrips(byte[] bytes, string text) => bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(text));

    private static string? CheckRanges(string oldText, StylesheetChangeSet c)
    {
        string newText = c.NewSheet.Text;
        var sb = new StringBuilder();
        int pos = 0;
        int newPos = 0;
        foreach (StylesheetEditedRange r in c.Ranges)
        {
            if (r.OldRange.Start < pos || r.OldRange.End < r.OldRange.Start || r.NewRange.Start < newPos || r.NewRange.End > newText.Length || r.OldRange.End > oldText.Length)
            {
                return "ranges-out-of-order";
            }

            int gap = r.OldRange.Start - pos;
            if (r.NewRange.Start - newPos != gap || !string.Equals(oldText.Substring(pos, gap), newText.Substring(newPos, gap), StringComparison.Ordinal))
            {
                return "text-changed-outside-ranges";
            }

            sb.Append(oldText, pos, gap).Append(newText, r.NewRange.Start, r.NewRange.End - r.NewRange.Start);
            newPos = r.NewRange.End;
            pos = r.OldRange.End;
        }

        sb.Append(oldText, pos, oldText.Length - pos);
        return string.Equals(sb.ToString(), newText, StringComparison.Ordinal) ? null : "ranges-do-not-rebuild-new-text";
    }

    private static string? CharsetOf(string? contentType)
    {
        int i = contentType?.IndexOf("charset=", StringComparison.OrdinalIgnoreCase) ?? -1;
        if (contentType is null || i < 0)
        {
            return null;
        }

        string v = contentType[(i + 8)..].Trim().Trim('"', '\'');
        int semi = v.IndexOf(';', StringComparison.Ordinal);
        return semi >= 0 ? v[..semi].Trim() : v;
    }

    // ---- random sheet edits ----

    private static List<SheetEdit> RandomSheetEdits(Stylesheet s, Random rng, out string label, int count)
    {
        var items = new List<CssNode>();
        var stack = new Stack<CssNode>();
        List<CssNode> top = [.. s.Rules];
        for (int i = top.Count - 1; i >= 0 && i >= top.Count - 400; i--)
        {
            stack.Push(top[i]);
        }

        while (stack.Count > 0 && items.Count < 2000)
        {
            CssNode n = stack.Pop();
            items.Add(n);
            List<CssNode> kids = [.. n.Children];
            for (int i = kids.Count - 1; i >= 0; i--)
            {
                stack.Push(kids[i]);
            }
        }

        List<CssNode> rules = [.. items.Where(n => n.Kind is CssNodeKind.QualifiedRule or CssNodeKind.AtRule)];
        List<CssNode> blocks = [.. rules.Where(n => n.HasBlock)];
        var edits = new List<SheetEdit>();
        var labels = new SortedSet<string>(StringComparer.Ordinal);
        for (int k = 0; k < count; k++)
        {
            string l = string.Empty;
            SheetEdit? e = null;
            switch (rng.Next(7))
            {
                case 0 or 1 when blocks.Count > 0:
                    {
                        CssNode r = blocks[rng.Next(blocks.Count)];
                        List<CssNode> decls = [.. r.Declarations];
                        string prop = decls.Count > 0 && rng.Next(2) == 0 ? decls[rng.Next(decls.Count)].Name : s_props[rng.Next(s_props.Length)];
                        l = "setDeclaration";
                        e = SheetEdit.SetDeclaration(s, r, prop, s_values[rng.Next(s_values.Length)], rng.Next(4) == 0);
                        break;
                    }

                case 2 when blocks.Count > 0:
                    {
                        CssNode r = blocks[rng.Next(blocks.Count)];
                        List<CssNode> decls = [.. r.Declarations];
                        string prop = decls.Count > 0 && rng.Next(3) != 0 ? decls[rng.Next(decls.Count)].Name : s_props[rng.Next(s_props.Length)];
                        l = "removeDeclaration";
                        e = SheetEdit.RemoveDeclaration(s, r, prop);
                        break;
                    }

                case 3 when rules.Count > 0:
                    {
                        CssNode r = rules[rng.Next(rules.Count)];
                        l = "insertRule";
                        string css = s_rules[rng.Next(s_rules.Length)];
                        e = rng.Next(2) == 0 ? SheetEdit.InsertRuleBefore(s, r, css) : SheetEdit.InsertRuleAfter(s, r, css);
                        break;
                    }

                case 4:
                    {
                        l = "insertRule";
                        CssNode? c = blocks.Count > 0 && rng.Next(3) == 0 ? blocks[rng.Next(blocks.Count)] : null;
                        e = SheetEdit.AppendRule(s, s_rules[rng.Next(s_rules.Length)], c);
                        break;
                    }

                case 5 when rules.Count > 0:
                    l = "removeRule";
                    e = SheetEdit.RemoveRule(s, rules[rng.Next(rules.Count)]);
                    break;
                case 6 when rules.Count > 0:
                    l = "replaceRule";
                    e = SheetEdit.ReplaceRule(s, rules[rng.Next(rules.Count)], s_rules[rng.Next(s_rules.Length)]);
                    break;
                default:
                    break;
            }

            if (e is not null)
            {
                edits.Add(e);
                labels.Add(l);
            }
        }

        label = labels.Count == 1 ? labels.First() : "mixed";
        return edits;
    }

    /// <summary>Counts why a transaction was refused, by the reason's wording with numbers and quoted text removed (the reasons name the edit, never page content).</summary>
    private static void CountRefusal(GateReport report, string where, BatchResult r)
    {
        string why = System.Text.RegularExpressions.Regex.Replace(r.Failure ?? "unknown", "[0-9]+|'[^']*'|\"[^\"]*\"", "_");
        report.Count("sheet-refused:" + where + ":" + (r.Outcomes.Any(x => x.Status == EditStatus.Failed) ? "failed:" : "invalid:") + (why.Length > 70 ? why[..70] : why));
    }

    private static void CountSheet(string op, bool applied, bool fallback, bool failed)
    {
        lock (s_lock)
        {
            if (!s_sheetOps.TryGetValue(op, out long[]? row))
            {
                s_sheetOps[op] = row = new long[5];
            }

            row[0]++;
            if (applied)
            {
                row[1]++;
            }

            if (fallback)
            {
                row[2]++;
            }

            if (failed)
            {
                row[3]++;
            }

            if (!applied && !failed)
            {
                row[4]++;
            }
        }
    }

    /// <summary>Writes the per-operation table (attempted, applied, refused, fallback rate) next to the summary.</summary>
    private static void WriteOperations(Options o)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# editfuzz: operations").AppendLine();
        sb.AppendLine("Page edits (one row per operation; an operation in a transaction that stopped early is not counted):").AppendLine();
        sb.AppendLine("| operation | attempted | applied | invalid | failed | applied with fallback | fallback rate |").AppendLine("|---|---|---|---|---|---|---|");
        lock (s_lock)
        {
            foreach (KeyValuePair<Editing.EditOperation, long[]> kv in s_stats.PerOperation)
            {
                long[] r = kv.Value;
                sb.Append(CultureInfo.InvariantCulture, $"| {kv.Key} | {r[0]} | {r[1]} | {r[2]} | {r[3]} | {r[4]} | {Corpus.Pct(r[4], r[1])} |\n");
            }

            sb.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"Transactions {s_stats.Transactions}, applied {s_stats.Applied}, with a fallback {s_stats.WithFallback} ({Corpus.Pct(s_stats.WithFallback, s_stats.Applied)}), reverts checked {s_stats.Reverts}, re-parses {s_stats.Verifications}.").AppendLine();
            sb.AppendLine("Stylesheet edits (`<style>` element and linked sheets; a transaction is 1-3 edits, `mixed` when of several kinds):").AppendLine();
            sb.AppendLine("| kind | transactions | applied | applied with fallback | fallback rate | failed (verification) | refused |").AppendLine("|---|---|---|---|---|---|---|");
            foreach (KeyValuePair<string, long[]> kv in s_sheetOps)
            {
                long[] r = kv.Value;
                sb.Append(CultureInfo.InvariantCulture, $"| {kv.Key} | {r[0]} | {r[1]} | {r[2]} | {Corpus.Pct(r[2], r[1])} | {r[3]} | {r[4]} |\n");
            }
        }

        System.IO.File.WriteAllText(System.IO.Path.Combine(o.Out, "operations.md"), sb.ToString());
        Console.WriteLine(sb.ToString());
    }
}
