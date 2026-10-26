using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using RecursiveAI.OneHtml.Accessibility;
using RecursiveAI.OneHtml.Accessibility.Validation;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Differential;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Benchmarks;

/// <summary>
/// One sampled page prepared for the checker benchmarks: parsed, styled and with its accessibility tree built, plus the
/// single edit the re-check measurement applies. The linked stylesheets are not supplied (the sample is read from the
/// page's own bytes), so the cascade sees the page's inline styles and the user-agent sheet only.
/// </summary>
internal sealed class CheckPage
{
    public CheckPage(CorpusItem item, Document doc)
    {
        Item = item;
        Doc = doc;
        Styles = DocumentStyles.Create(doc);
        Tree = AccessibilityTree.Build(doc, Styles);
        // The same edit is made in Chrome: the first element inside the body gets a data attribute.
        Node first = doc.Root.QuerySelector("body *");
        Edit = first.IsNull ? null : Editing.Edit.SetAttribute(first, "data-bench", "1");
    }

    public CorpusItem Item { get; }

    public Document Doc { get; }

    public DocumentStyles Styles { get; }

    public AccessibilityTree Tree { get; }

    public Edit? Edit { get; }
}

/// <summary>
/// The three checker operations the axe-core comparison measures, shared by the BenchmarkDotNet rows and the per-page
/// timing mode so both do exactly the same work. Each returns a number the caller adds up, so nothing is optimised away.
/// </summary>
internal static class CheckWork
{
    /// <summary>The focus measurement re-checks this rule under each focused element: contrast is what a focus style changes.</summary>
    public const string FocusRule = "color-contrast";

    private static readonly Checker s_checker = new();

    /// <summary>Focus overlays measured per page.</summary>
    public static int FocusCap { get; } = int.TryParse(Environment.GetEnvironmentVariable("ONEHTML_BENCH_FOCUS_CAP"), NumberStyles.None, CultureInfo.InvariantCulture, out int c) && c > 0 ? c : 20;

    /// <summary>Full check: cascade, tree and every rule, from a parsed document.</summary>
    public static int Full(CheckPage page) => s_checker.Check(page.Doc).Results.Count;

    /// <summary>One edit, then the restyle, the incremental tree update and a full rule run over the new tree. -1 when the page has no edit target.</summary>
    public static int AfterEdit(CheckPage page)
    {
        if (page.Edit is null)
        {
            return -1;
        }

        EditResult edit = page.Doc.Apply(new[] { page.Edit });
        if (!edit.Succeeded)
        {
            return -2;
        }

        RestyleResult restyle = page.Styles.Restyle(edit);
        AccessibilityTree updated = page.Tree.Update(edit, restyle);
        return s_checker.Check(updated).Results.Count;
    }

    /// <summary>Under each of the first focusable nodes (in focus order), the tree with that node focused and the contrast rule re-run.</summary>
    public static int Focus(CheckPage page)
    {
        int total = 0;
        var options = new CheckOptions { RuleIds = [FocusRule], IncludePasses = false };
        foreach (AxNode node in page.Tree.FocusOrder.Take(FocusCap))
        {
            AccessibilityTree focused = page.Tree.WithContext(StyleContext.Default.WithFocus(node.DomNode));
            total += s_checker.Check(focused, options).Results.Count;
        }

        return total;
    }

    public static int FocusCount(CheckPage page) => Math.Min(page.Tree.FocusOrder.Count, FocusCap);
}

/// <summary>
/// The checker against axe-core in Chrome: full check, re-check after one edit, and the focus-overlay check, over the
/// shared page sample. This is the BenchmarkDotNet side (time and allocation per sweep of the sample); the per-page
/// timings that line up with the Chrome side are written by <see cref="CheckPerPage"/>. See bench/README.md.
/// </summary>
[MemoryDiagnoser]
public class CheckBenchmarks
{
    private CheckPage[] _pages = [];

    [GlobalSetup]
    public void Setup() => _pages = CheckPerPage.Prepare();

    [Benchmark(Baseline = true, Description = "Full check")]
    public int FullCheck()
    {
        int n = 0;
        foreach (CheckPage p in _pages)
        {
            n += CheckWork.Full(p);
        }

        return n;
    }

    [Benchmark(Description = "Re-check after one edit")]
    public int RecheckAfterEdit()
    {
        int n = 0;
        foreach (CheckPage p in _pages)
        {
            n += CheckWork.AfterEdit(p);
        }

        return n;
    }

    [Benchmark(Description = "Focus overlays")]
    public int FocusOverlays()
    {
        int n = 0;
        foreach (CheckPage p in _pages)
        {
            n += CheckWork.Focus(p);
        }

        return n;
    }
}

/// <summary>
/// Per-page timings of the same three operations with a stopwatch (one warm-up, then the median of the repeats), as JSON
/// lines in the shape tools/axe-bench writes for Chrome, so the two can be compared page by page. Started with
/// <c>--check-per-page &lt;out.jsonl&gt;</c>.
/// </summary>
internal static class CheckPerPage
{
    public static CheckPage[] Prepare()
    {
        var pages = new List<CheckPage>();
        foreach (CorpusItem item in Sample.Items)
        {
            try
            {
                pages.Add(new CheckPage(item, Document.Parse(File.ReadAllBytes(item.Path))));
            }
            catch (Exception e) when (e is HtmlLimitExceededException or IOException)
            {
                Console.Error.WriteLine($"skipped {item.Sha256[..12]}: {e.Message}");
            }
        }

        return pages.ToArray();
    }

    public static int Run(string outPath, int repeats)
    {
        CheckPage[] pages = Prepare();
        using var writer = new StreamWriter(outPath, append: false, new UTF8Encoding(false));
        foreach (CheckPage page in pages)
        {
            double[] full = Time(page, CheckWork.Full, repeats);
            double[] edit = Time(page, CheckWork.AfterEdit, repeats);
            double[] focus = Time(page, CheckWork.Focus, repeats);
            var line = new Dictionary<string, object?>
            {
                ["side"] = "onehtml",
                ["sha256"] = page.Item.Sha256,
                ["url"] = page.Item.Url,
                ["nodes"] = page.Doc.NodeCount,
                ["full_ms"] = full,
                ["edit_ms"] = page.Edit is null ? null : edit,
                ["focus_n"] = CheckWork.FocusCount(page),
                ["focus_ms"] = focus,
            };
            writer.WriteLine(JsonSerializer.Serialize(line, JsonSerializerOptions.Default));
        }

        Console.WriteLine($"wrote {pages.Length} pages to {outPath}");
        return 0;
    }

    private static double[] Time(CheckPage page, Func<CheckPage, int> work, int repeats)
    {
        _ = work(page);
        var times = new double[repeats];
        for (int i = 0; i < repeats; i++)
        {
            long t0 = Stopwatch.GetTimestamp();
            _ = work(page);
            times[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }

        return times;
    }
}
