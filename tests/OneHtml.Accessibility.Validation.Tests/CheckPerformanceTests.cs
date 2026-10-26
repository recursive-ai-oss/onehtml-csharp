using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>Writes check time and allocation for a 35,000-node page when ONEHTML_PERF_REPORT names a file.</summary>
public sealed class CheckPerformanceTests
{
    [Fact]
    public void Check_time_and_allocation_for_a_35k_node_page()
    {
        string? report = Environment.GetEnvironmentVariable("ONEHTML_PERF_REPORT");
        if (report is null)
        {
            return;
        }

        var sb = new StringBuilder("<!doctype html><title>Perf</title><style>.card{margin:1em} .hide{display:none}</style><body><main>");
        for (int i = 0; sb.Length < 570_000; i++)
        {
            sb.Append("<section class=card aria-label='Card ").Append(i).Append("'><h2>Heading ").Append(i).Append("</h2><p>Some <b>bold</b> text and a <a href='/p/").Append(i)
                .Append("'>link ").Append(i).Append("</a>.</p><ul role=list><li role=listitem>One</li><li>Two <span class=hide>x</span></li></ul><button type=button aria-describedby=d").Append(i).Append(">Go</button><span id=d").Append(i)
                .Append(" role=nope aria-foo=1>help</span><div role=tab aria-selected=maybe></div><div aria-hidden=true><a href=#>x</a></div>")
                .Append("<img src=i").Append(i).Append(".png alt='Picture ").Append(i).Append("'><label for=f").Append(i).Append(">Field</label><input id=f").Append(i).Append("><a href='/dup'>More</a><iframe title=t").Append(i % 7).Append(" src=/f").Append(i % 3).Append("></iframe></section>");
        }

        sb.Append("</main></body>");
        string html = sb.ToString();
        Document doc = Document.Parse(html);
        DocumentStyles styles = DocumentStyles.Create(doc);
        styles.ComputeAll();
        _ = AccessibilityTree.Build(doc, styles);
        var treeTimer = Stopwatch.StartNew();
        AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
        long treeMs = treeTimer.ElapsedMilliseconds;
        var checker = new Checker();
        _ = checker.Check(tree); // warm up
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        CheckReport r = checker.Check(tree);
        long ms = sw.ElapsedMilliseconds;
        long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        string[] nameRules = ["image-alt", "input-image-alt", "area-alt", "object-alt", "svg-img-alt", "role-img-alt", "image-redundant-alt", "button-name", "input-button-name", "link-name", "select-name", "summary-name", "label", "label-title-only", "form-field-multiple-labels", "frame-title", "frame-title-unique", "document-title", "label-content-name-mismatch", "empty-heading", "identical-links-same-purpose", "image-name-descriptive", "link-in-context-descriptive"];
        var only = new CheckOptions { RuleIds = nameRules };
        _ = checker.Check(tree, only);
        long nameBefore = GC.GetAllocatedBytesForCurrentThread();
        var nameTimer = Stopwatch.StartNew();
        CheckReport nr = checker.Check(tree, only);
        long nameMs = nameTimer.ElapsedMilliseconds;
        long nameAlloc = GC.GetAllocatedBytesForCurrentThread() - nameBefore;
        // The page-structure area alone.
        var areaOptions = new CheckOptions { RuleIds = StructureRuleTests.AxeIds };
        _ = checker.Check(tree, areaOptions);
        GC.Collect();
        before = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        CheckReport area = checker.Check(tree, areaOptions);
        long areaMs = sw.ElapsedMilliseconds;
        long areaAlloc = GC.GetAllocatedBytesForCurrentThread() - before;
        var focusOnly = new CheckOptions { RuleIds = ["focus-visible-indicator"] };
        _ = checker.Check(tree, focusOnly);
        sw.Restart();
        CheckReport focus = checker.Check(tree, focusOnly);
        long focusMs = sw.ElapsedMilliseconds;
        // The same page with the focus rules sites write: a rule on the focusable itself, :focus-within on a container, :has() on a container.
        Document focusDoc = Document.Parse(html.Replace("<style>", "<style>a:focus-visible,button:focus-visible{outline:2px solid blue} .card:focus-within h2{color:red} .card:has(:focus-visible){background-color:#eee} main > section:focus-within > ul > li{color:green} ", StringComparison.Ordinal));
        DocumentStyles focusStyles = DocumentStyles.Create(focusDoc);
        focusStyles.ComputeAll();
        AccessibilityTree focusTree = AccessibilityTree.Build(focusDoc, focusStyles);
        sw.Restart();
        CheckReport focusRules = checker.Check(focusTree, focusOnly);
        long focusRulesMs = sw.ElapsedMilliseconds;
        sw.Restart();
        int native = r.ToNativeJson().Length;
        long nativeMs = sw.ElapsedMilliseconds;
        sw.Restart();
        int axe = r.ToAxeJson().Length;
        long axeMs = sw.ElapsedMilliseconds;
        File.WriteAllText(report, $"dom nodes={doc.NodeCount} tree nodes={tree.Nodes.Count} tree_build_ms={treeMs} rules={r.RulesRun.Count} results={r.Results.Count} check_ms={ms} check_alloc_mb={alloc / 1048576.0:F1} names_rules={nr.RulesRun.Count} names_results={nr.Results.Count} names_ms={nameMs} names_alloc_mb={nameAlloc / 1048576.0:F1} native_json_ms={nativeMs} native_bytes={native} axe_json_ms={axeMs} axe_bytes={axe} structure_rules={area.RulesRun.Count} structure_results={area.Results.Count} focus_visible_ms={focusMs} focus_results={focus.Results.Count} focus_rules_ms={focusRulesMs} focus_rules_results={focusRules.Results.Count} structure_check_ms={areaMs} structure_check_alloc_mb={areaAlloc / 1048576.0:F1}\n");
    }
}
