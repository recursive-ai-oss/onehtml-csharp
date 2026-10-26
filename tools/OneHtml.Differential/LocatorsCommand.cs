using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using RecursiveAI.OneHtml.Locators;
using RecursiveAI.OneHtml.Selectors;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// <c>locators</c>: for every element of every page, the generated locator must parse, must match its element, and
/// (checked by querying the whole document, for every element up to <c>--sample</c> per page and for a seeded sample of
/// the rest) must match that element and nothing else. Every locator must also be distinct from every other element's.
/// </summary>
internal static class LocatorsCommand
{
    /// <summary>The generator gives up this many steps below an anchor (docs/locators.md), so a deeper element legitimately has no locator.</summary>
    private const int DepthWithoutLocator = 121;

    public static int Run(Options o)
    {
        List<CorpusItem> items = Corpus.Load(o.Corpus, pages: true, stylesheets: false);
        return GateRunner.Run(o, "locators", items, (item, bytes, report, ct) => CheckPage(o, item, bytes, report, ct));
    }

    private static void CheckPage(Options o, CorpusItem item, byte[] bytes, GateReport report, CancellationToken ct)
    {
        Document doc = Document.Parse(bytes, new ParseOptions { ContentType = item.ContentType }, ct);
        var generator = new LocatorGenerator(doc);
        List<Node> elements = [.. doc.Root.DescendantElements];
        report.Count("pages");
        report.Count("elements", elements.Count);

        // Which elements get the whole-document query: all of them on a small page, else a seeded sample.
        HashSet<int> queried = [];
        if (elements.Count <= o.Sample)
        {
            foreach (Node e in elements)
            {
                queried.Add(e.Index);
            }
        }
        else
        {
            var rng = new Random(o.Seed ^ Convert.ToInt32(item.Sha256[..7], 16));
            while (queried.Count < o.Sample)
            {
                queried.Add(elements[rng.Next(elements.Count)].Index);
            }
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var failedClasses = new HashSet<string>(StringComparer.Ordinal);
        void Fail(string cls, Node e)
        {
            report.Count("failing-elements:" + cls);
            if (failedClasses.Add(cls))
            {
                report.Fail(cls, item.Sha256, "<" + e.LocalName + ">");
            }
        }

        foreach (Node e in elements)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!generator.TryGenerate(e, out string? locator))
                {
                    int depth = 0;
                    for (Node p = e; p.IsElement; p = p.Parent)
                    {
                        depth++;
                    }

                    if (!e.IsConnected)
                    {
                        report.Count("not-in-tree");
                    }
                    else if (depth >= DepthWithoutLocator)
                    {
                        report.Count("no-locator-too-deep");
                    }
                    else
                    {
                        Fail("no-locator", e);
                    }

                    continue;
                }

                report.Count("locators");
                report.Observe("locator-length", locator!.Length);
                if (!SelectorList.TryParse(locator, out SelectorList? list))
                {
                    Fail("selector-parse-failure", e);
                    continue;
                }

                if (!seen.TryAdd(locator, e.Index))
                {
                    Fail("duplicate-locator", e);
                }

                if (!e.Matches(list!))
                {
                    Fail("does-not-match-its-element", e);
                    continue;
                }

                if (queried.Contains(e.Index))
                {
                    report.Count("queried");
                    List<Node> found = doc.Root.QuerySelectorAll(list!);
                    if (found.Count == 0)
                    {
                        Fail("query-finds-nothing", e);
                    }
                    else if (found.Count > 1)
                    {
                        Fail(found.Contains(e) ? "non-unique" : "matches-others-not-target", e);
                    }
                    else if (found[0] != e)
                    {
                        Fail("matches-another-element", e);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Fail(GateRunner.Classify(ex), e);
            }
        }
    }
}
