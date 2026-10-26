using System.Linq;
using System.Text;
using BenchmarkDotNet.Attributes;
using RecursiveAI.OneHtml.Accessibility;
using RecursiveAI.OneHtml.Accessibility.Validation;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Benchmarks;

/// <summary>
/// A full check against an incremental re-check after one edit, on a synthetic page of about 35,000 DOM nodes (a repeated card of
/// headings, links, lists, form controls, images and frames). The edit, its restyle and the updated tree are made once in setup, so
/// the "Re-check" rows time <see cref="Checker.Recheck(CheckReport, EditResult, RestyleResult, AccessibilityTree, RecursiveAI.OneHtml.Accessibility.Rendered.RenderedFacts?, System.Threading.CancellationToken)"/>
/// alone; the "Edit and re-check" rows add <c>Apply</c>, <c>Restyle</c> and <c>AccessibilityTree.Update</c>. Both checks run every
/// rule except <c>focus-visible-indicator</c>, which compares every element's style under each focusable element and takes
/// tens of seconds on this page; <see cref="RecheckAllRulesBenchmarks"/> has the full catalogue in a short job.
/// </summary>
[MemoryDiagnoser]
public class RecheckBenchmarks
{
    private const string SlowRule = "focus-visible-indicator";

    private readonly Checker _checker = new();
    private Document _doc = null!;
    private DocumentStyles _styles = null!;
    private AccessibilityTree _tree = null!;
    private CheckOptions _options = null!;
    private CheckReport _report = null!;
    private EditResult _attrEdit = null!;
    private RestyleResult _attrRestyle = null!;
    private AccessibilityTree _attrTree = null!;
    private EditResult _textEdit = null!;
    private RestyleResult _textRestyle = null!;
    private AccessibilityTree _textTree = null!;
    private Edit _attr = null!;
    private Edit _text = null!;

    /// <summary>Every rule (default) or every rule but <c>focus-visible-indicator</c>.</summary>
    protected virtual bool SkipSlowRule => true;

    [GlobalSetup]
    public void Setup()
    {
        _doc = Document.Parse(PageGenerator.Html());
        _styles = DocumentStyles.Create(_doc);
        _styles.ComputeAll();
        _tree = AccessibilityTree.Build(_doc, _styles);
        _options = SkipSlowRule ? new CheckOptions { RuleIds = [.. _checker.Rules.Select(r => r.Descriptor.Id).Where(id => id != SlowRule)] } : new CheckOptions();
        _report = _checker.Check(_tree, _options);
        int middle = _doc.Root.DescendantElements.Count(e => e.LocalName == "p") / 2;
        Node p = _doc.Root.DescendantElements.Where(e => e.LocalName == "p").ElementAt(middle);
        _attr = Edit.SetAttribute(p, "data-bench", "1");
        Node bold = p.QuerySelector("b");
        _text = Edit.ReplaceText(bold.FirstChild, "changed");
        (_attrEdit, _attrRestyle, _attrTree) = Prepare(_attr);
        (_textEdit, _textRestyle, _textTree) = Prepare(_text);
    }

    private (EditResult, RestyleResult, AccessibilityTree) Prepare(Edit edit)
    {
        EditResult result = _doc.Apply([edit]);
        RestyleResult restyle = _styles.Restyle(result);
        return (result, restyle, _tree.Update(result, restyle));
    }

    [Benchmark(Baseline = true, Description = "Full check")]
    public int FullCheck() => _checker.Check(_attrTree, _options).Results.Count;

    [Benchmark(Description = "Re-check after an attribute edit")]
    public int RecheckAttribute() => _checker.Recheck(_report, _attrEdit, _attrRestyle, _attrTree).Results.Count;

    [Benchmark(Description = "Re-check after a text edit")]
    public int RecheckText() => _checker.Recheck(_report, _textEdit, _textRestyle, _textTree).Results.Count;

    [Benchmark(Description = "Edit and re-check, attribute")]
    public int EditAndRecheckAttribute()
    {
        (EditResult e, RestyleResult r, AccessibilityTree t) = Prepare(_attr);
        return _checker.Recheck(_report, e, r, t).Results.Count;
    }

    [Benchmark(Description = "Edit and re-check, text")]
    public int EditAndRecheckText()
    {
        (EditResult e, RestyleResult r, AccessibilityTree t) = Prepare(_text);
        return _checker.Recheck(_report, e, r, t).Results.Count;
    }
}

/// <summary>The same rows with every rule, including the one that dominates a full check of this page. A short job: one full check takes tens of seconds.</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class RecheckAllRulesBenchmarks : RecheckBenchmarks
{
    /// <inheritdoc />
    protected override bool SkipSlowRule => false;
}

/// <summary>The synthetic page of the re-check benchmarks.</summary>
internal static class PageGenerator
{
    public static string Html()
    {
        var sb = new StringBuilder("<!doctype html><title>Perf</title><style>.card{margin:1em} .hide{display:none}</style><body><main>");
        for (int i = 0; sb.Length < 570_000; i++)
        {
            sb.Append("<section class=card aria-label='Card ").Append(i).Append("'><h2>Heading ").Append(i).Append("</h2><p>Some <b>bold</b> text and a <a href='/p/").Append(i)
                .Append("'>link ").Append(i).Append("</a>.</p><ul role=list><li role=listitem>One</li><li>Two <span class=hide>x</span></li></ul><button type=button aria-describedby=d").Append(i).Append(">Go</button><span id=d").Append(i)
                .Append(" role=nope aria-foo=1>help</span><div role=tab aria-selected=maybe></div><div aria-hidden=true><a href=#>x</a></div>")
                .Append("<img src=i").Append(i).Append(".png alt='Picture ").Append(i).Append("'><label for=f").Append(i).Append(">Field</label><input id=f").Append(i).Append("><a href='/dup'>More</a><iframe title=t").Append(i % 7).Append(" src=/f").Append(i % 3).Append("></iframe></section>");
        }

        sb.Append("</main></body>");
        return sb.ToString();
    }
}
