using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Selectors;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// <c>liveform</c>: on each page, scripts form-state changes (checkedness, disabledness and the indeterminate state of random controls, options,
/// fieldsets and optgroups, with some other elements mixed in, set, flipped and cleared, one to four at a time) and checks
/// <see cref="LiveDocumentStyles.ApplyFormState"/> against a fresh <see cref="DocumentStyles"/> and a <see cref="DocumentStyles.WithContext"/>
/// overlay for the equivalent context after every change: every property of every element must be equal, and every element whose fresh
/// style changed must be in the changed list. A replay of the script measures the bytes allocated in steady state.
/// </summary>
internal static class LiveFormCommand
{
    public static int Run(Options o)
    {
        var store = StylesheetStore.Load(o.StyleCorpus is { Length: > 0 } sc ? sc : o.Corpus);
        List<CorpusItem> items = Corpus.Load(o.Corpus, pages: true, stylesheets: false);
        if (o.Stride > 1)
        {
            items = [.. items.OrderBy(i => i.Sha256, StringComparer.Ordinal).Where((_, n) => n % o.Stride == 0)];
        }

        if (o.Pages is { Length: > 0 } only)
        {
            items = [.. items.Where(i => only.Any(p => i.Sha256.StartsWith(p, StringComparison.Ordinal)))];
        }

        Console.WriteLine($"liveform: {store.Count} stylesheets available, {items.Count} pages");
        return GateRunner.Run(o, "liveform", items, (item, bytes, report, ct) => CheckPage(o, store, item, bytes, report, ct));
    }

    private static void CheckPage(Options o, StylesheetStore store, CorpusItem item, byte[] bytes, GateReport report, CancellationToken ct)
    {
        Document doc = Document.Parse(bytes, new ParseOptions { ContentType = item.ContentType }, ct);
        var rng = new Random(o.Seed ^ Convert.ToInt32(item.Sha256[..7], 16));
        string? baseUrl = Uri.TryCreate(item.Url, UriKind.Absolute, out Uri? u) && u.Scheme is "http" or "https" ? item.Url : null;
        var options = new StyleSourceOptions { BaseUrl = baseUrl, Resolver = store.Resolve };
        List<Node> elements = [.. doc.Root.DescendantElements];
        report.Count("pages");
        report.Count("elements", elements.Count);
        List<Node> forms = [.. elements.Where(e => e.LocalName is "input" or "option" or "fieldset" or "optgroup" or "select" or "textarea" or "button" or "label" or "form")];
        report.Count("form-elements", forms.Count);
        if (forms.Count == 0)
        {
            report.Count("pages-without-form-elements");
            return;
        }

        DocumentStyles start = DocumentStyles.Create(doc, null, options, ct);
        start.ComputeAll(ct);
        LiveDocumentStyles live = LiveDocumentStyles.Create(start);
        List<FormStateChange[]> script = Script(rng, elements, forms, o.PerPage);
        var failed = new HashSet<string>(StringComparer.Ordinal);
        DocumentStyles previous = start;
        foreach (FormStateChange[] batch in script)
        {
            ct.ThrowIfCancellationRequested();
            live.ApplyFormState(batch);
            StyleContext ctx = live.GetContext();
            DocumentStyles overlay = start.WithContext(ctx, ct);
            DocumentStyles fresh = DocumentStyles.Create(doc, ctx, options, ct);
            fresh.ComputeAll(ct);
            report.Count("changes");
            report.Count("changed-elements", live.ChangedElementCount);
            var changed = new HashSet<int>();
            int last = -1;
            foreach (int idx in live.ChangedElementIndices)
            {
                if (idx <= last || !changed.Add(idx))
                {
                    report.Count("mismatch:changed-list-not-ascending-unique");
                    Fail("changed-list-order", item, report, failed, null);
                }

                last = idx;
            }

            foreach (Node e in elements)
            {
                string? diff = live.GetStyle(e.Index).FirstDifference(fresh.GetStyle(e));
                report.Count("elements-compared");
                if (diff is not null)
                {
                    report.Count("mismatch:live:" + diff);
                    Fail("live:" + diff, item, report, failed, "<" + e.LocalName + "> #" + e.Index.ToString(CultureInfo.InvariantCulture));
                }

                if (overlay.GetStyle(e).FirstDifference(fresh.GetStyle(e)) is { } odiff)
                {
                    report.Count("mismatch:overlay:" + odiff);
                    Fail("overlay:" + odiff, item, report, failed, "<" + e.LocalName + "> #" + e.Index.ToString(CultureInfo.InvariantCulture));
                }

                if (previous.GetStyle(e).FirstDifference(fresh.GetStyle(e)) is not null)
                {
                    report.Count("elements-truly-changed");
                    if (!changed.Contains(e.Index))
                    {
                        report.Count("mismatch:changed-list-misses-element");
                        Fail("changed-list-misses", item, report, failed, "<" + e.LocalName + "> #" + e.Index.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            previous = fresh;
        }

        // Steady state: replay from the default state twice (the second pass is measured).
        ResetAll(live, script);
        foreach (FormStateChange[] b in script)
        {
            live.ApplyFormState(b);
        }

        ResetAll(live, script);
        long before = GC.GetAllocatedBytesForCurrentThread();
        long ticks = Stopwatch.GetTimestamp();
        foreach (FormStateChange[] b in script)
        {
            live.ApplyFormState(b);
        }

        ticks = Stopwatch.GetTimestamp() - ticks;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        report.Count("steady-bytes", allocated);
        report.Count("steady-changes", script.Count);
        report.Count("steady-live-ns", ticks * 1_000_000_000L / Stopwatch.Frequency);
        if (allocated != 0)
        {
            report.Count("steady-pages-allocating");
            Fail("steady-state-allocation", item, report, failed, allocated.ToString(CultureInfo.InvariantCulture) + " bytes over " + script.Count + " changes");
        }
    }

    private static void Fail(string cls, CorpusItem item, GateReport report, HashSet<string> failed, string? example)
    {
        if (failed.Add(cls))
        {
            report.Fail(cls, item.Sha256, example);
        }
    }

    private static void ResetAll(LiveDocumentStyles live, List<FormStateChange[]> script)
    {
        var clear = new List<FormStateChange>();
        foreach (FormStateChange[] b in script)
        {
            foreach (FormStateChange c in b)
            {
                clear.Add(new FormStateChange(c.Element, c.Kind, null));
            }
        }

        live.ApplyFormState(clear.ToArray());
    }

    private static List<FormStateChange[]> Script(Random r, List<Node> all, List<Node> forms, int count)
    {
        var script = new List<FormStateChange[]>(count);
        for (int i = 0; i < count; i++)
        {
            var batch = new FormStateChange[r.Next(5) == 0 ? r.Next(2, 5) : 1];
            for (int k = 0; k < batch.Length; k++)
            {
                Node e = r.Next(8) == 0 ? all[r.Next(all.Count)] : forms[r.Next(forms.Count)];
                bool? v = r.Next(4) switch { 0 => null, 1 or 2 => true, _ => false };
                batch[k] = r.Next(3) switch
                {
                    0 => FormStateChange.Checked(e, v),
                    1 => FormStateChange.Disabled(e, v),
                    _ => FormStateChange.Indeterminate(e, v),
                };
            }

            script.Add(batch);
        }

        return script;
    }
}
