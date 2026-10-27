using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// <c>live</c>: on each page, scripts interaction-state changes (the hover walking random element paths, keyboard and pointer focus,
/// active on and off, <c>:target</c>) and checks <see cref="LiveDocumentStyles"/> against a fresh <see cref="DocumentStyles"/>
/// for the equivalent context after every change: every property of every element must be equal, and every element whose fresh
/// style changed must be in the changed list. A second and third pass replay the script to measure, in steady state, the bytes
/// allocated and the time of <see cref="LiveDocumentStyles.ApplyState"/> against
/// <see cref="DocumentStyles.WithContext"/> on the same changes.
/// </summary>
internal static class LiveCommand
{
    public static int Run(Options o)
    {
        var store = StylesheetStore.Load(o.StyleCorpus is { Length: > 0 } sc ? sc : o.Corpus);
        List<CorpusItem> items = Corpus.Load(o.Corpus, pages: true, stylesheets: false);
        if (o.Stride > 1)
        {
            items = [.. items.OrderBy(i => i.Sha256, StringComparer.Ordinal).Where((_, n) => n % o.Stride == 0)];
        }

        Console.WriteLine($"live: {store.Count} stylesheets available, {items.Count} pages");
        return GateRunner.Run(o, "live", items, (item, bytes, report, ct) => CheckPage(o, store, item, bytes, report, ct));
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
        if (elements.Count == 0)
        {
            return;
        }

        DocumentStyles start = DocumentStyles.Create(doc, null, options, ct);
        start.ComputeAll(ct);
        var live = LiveDocumentStyles.Create(start);
        List<Node> focusable = [.. elements.Where(e => e.HasAttribute("tabindex") || e.LocalName is "a" or "button" or "input" or "select" or "textarea" or "summary")];
        List<ElementStateChange> script = Script(rng, elements, focusable, o.PerPage);
        var failed = new HashSet<string>(StringComparer.Ordinal);

        // Pass 1: equality with a fresh computation after each change, and the time of the snapshot path on the same change.
        DocumentStyles previous = start;
        var contexts = new List<StyleContext>(script.Count);
        for (int i = 0; i < script.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            long t0 = Stopwatch.GetTimestamp();
            live.ApplyState(script[i]);
            long liveTicks = Stopwatch.GetTimestamp() - t0;
            StyleContext ctx = live.GetContext();
            contexts.Add(ctx);
            long t1 = Stopwatch.GetTimestamp();
            DocumentStyles overlay = start.WithContext(ctx, ct);
            long overlayTicks = Stopwatch.GetTimestamp() - t1;
            report.Count("changes");
            report.Count("changed-elements", live.ChangedElementCount);
            report.Observe("pass1-live-us", Micros(liveTicks));
            report.Observe("pass1-withcontext-us", Micros(overlayTicks));
            DocumentStyles fresh = DocumentStyles.Create(doc, ctx, options, ct);
            fresh.ComputeAll(ct);
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
                ct.ThrowIfCancellationRequested();
                string? diff = live.GetStyle(e.Index).FirstDifference(fresh.GetStyle(e));
                report.Count("elements-compared");
                if (diff is not null)
                {
                    report.Count("mismatch:live:" + diff);
                    Fail("live:" + diff, item, report, failed, "<" + e.LocalName + "> #" + e.Index.ToString(CultureInfo.InvariantCulture));
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

            _ = overlay;
            previous = fresh;
        }

        // Passes 2 and 3 replay the script from the default state: the second warms, the third is the steady state that is measured.
        Reset(live);
        foreach (ElementStateChange c in script)
        {
            live.ApplyState(c);
        }

        Reset(live);
        long[] stepTicks = new long[script.Count];
        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < script.Count; i++)
        {
            long t = Stopwatch.GetTimestamp();
            live.ApplyState(script[i]);
            stepTicks[i] = Stopwatch.GetTimestamp() - t;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
        report.Count("steady-bytes", allocated);
        if (allocated != 0)
        {
            report.Count("steady-pages-allocating");
            Fail("steady-state-allocation", item, report, failed, allocated.ToString(CultureInfo.InvariantCulture) + " bytes over " + script.Count + " changes");
        }

        long ticks = 0;
        // Pass 4: the snapshot path on the same changes, warm (the style table already holds every style), for time and allocation.
        foreach (StyleContext c in contexts)
        {
            _ = start.WithContext(c, ct);
        }

        long[] overlayTicks4 = new long[contexts.Count];
        long overlayBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < contexts.Count; i++)
        {
            long t = Stopwatch.GetTimestamp();
            _ = start.WithContext(contexts[i], ct);
            overlayTicks4[i] = Stopwatch.GetTimestamp() - t;
        }

        long overlayBytes = GC.GetAllocatedBytesForCurrentThread() - overlayBefore;
        report.Count("steady-withcontext-bytes", overlayBytes);
        foreach (long dt in overlayTicks4)
        {
            report.Observe("steady-withcontext-us", Micros(dt));
        }

        long[] sortedLive = [.. stepTicks];
        long[] sortedOverlay = [.. overlayTicks4];
        Array.Sort(sortedLive);
        Array.Sort(sortedOverlay);

        // The large pages, and any page where a change took a millisecond or more, get a line of their own.
        if (elements.Count >= 2000 || Micros(sortedLive[^1]) >= 1000)
        {
            Console.Error.WriteLine("PAGE " + item.Sha256[..12] + " elements=" + elements.Count + " rules=" + start.RuleCount + " changes=" + script.Count + " live-median-us=" + Micros(sortedLive[sortedLive.Length / 2]) + " live-max-us=" + Micros(sortedLive[^1]) + " withcontext-median-us=" + Micros(sortedOverlay[sortedOverlay.Length / 2]) + " withcontext-max-us=" + Micros(sortedOverlay[^1]) + " withcontext-bytes-per-change=" + (overlayBytes / Math.Max(1, contexts.Count)) + " live-bytes=" + allocated);
        }

        foreach (long dt in stepTicks)
        {
            ticks += dt;
            report.Observe("steady-live-us", Micros(dt));
            report.Count("steady-changes");
        }

        report.Count("steady-live-ns", ticks * 1_000_000_000L / Stopwatch.Frequency);
    }

    private static void Reset(LiveDocumentStyles live) =>
        live.ApplyState(ElementStateChange.Hover(default).WithActive(default).WithFocus(default).WithTarget(default));

    private static int Micros(long ticks) => (int)Math.Min(int.MaxValue, ticks * 1_000_000L / Stopwatch.Frequency);

    private static void Fail(string cls, CorpusItem item, GateReport report, HashSet<string> failed, string? example)
    {
        if (failed.Add(cls))
        {
            report.Fail(cls, item.Sha256, example);
        }
    }

    // A pointer that walks: to the parent, a child or a sibling of where it was, now and then to a random element, or out of the page;
    // keyboard and pointer focus; active on and off; occasionally everything at once.
    private static List<ElementStateChange> Script(Random r, List<Node> all, List<Node> focusable, int count)
    {
        var script = new List<ElementStateChange>(count);
        Node at = all[r.Next(all.Count)];
        for (int i = 0; i < count; i++)
        {
            int kind = r.Next(10);
            if (kind < 5)
            {
                int pick = r.Next(7);
                Node next = pick switch
                {
                    0 => at.ParentElement,
                    1 => at.FirstElementChild,
                    2 => at.NextElementSibling,
                    3 => at.PreviousElementSibling,
                    4 => default,
                    _ => all[r.Next(all.Count)],
                };
                if (!next.IsNull)
                {
                    at = next;
                }

                script.Add(ElementStateChange.Hover(pick == 4 ? default : at));
            }
            else if (kind < 7 && focusable.Count > 0)
            {
                script.Add(ElementStateChange.Focus(r.Next(4) == 0 ? default : focusable[r.Next(focusable.Count)], r.Next(2) == 0 ? FocusSource.Keyboard : FocusSource.PointingDevice));
            }
            else if (kind < 9)
            {
                script.Add(ElementStateChange.Active(r.Next(3) == 0 ? default : at));
            }
            else
            {
                script.Add(ElementStateChange.Hover(at).WithActive(at).WithTarget(r.Next(2) == 0 ? default : all[r.Next(all.Count)]));
            }
        }

        return script;
    }
}
