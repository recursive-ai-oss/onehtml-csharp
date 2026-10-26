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
        for (int i = 0; sb.Length < 450_000; i++)
        {
            sb.Append("<section class=card aria-label='Card ").Append(i).Append("'><h2>Heading ").Append(i).Append("</h2><p>Some <b>bold</b> text and a <a href='/p/").Append(i)
                .Append("'>link ").Append(i).Append("</a>.</p><ul role=list><li role=listitem>One</li><li>Two <span class=hide>x</span></li></ul><button type=button aria-describedby=d").Append(i).Append(">Go</button><span id=d").Append(i)
                .Append(" role=nope aria-foo=1>help</span><div role=tab aria-selected=maybe></div><div aria-hidden=true><a href=#>x</a></div></section>");
        }

        sb.Append("</main></body>");
        Document doc = Document.Parse(sb.ToString());
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
        sw.Restart();
        int native = r.ToNativeJson().Length;
        long nativeMs = sw.ElapsedMilliseconds;
        sw.Restart();
        int axe = r.ToAxeJson().Length;
        long axeMs = sw.ElapsedMilliseconds;
        File.WriteAllText(report, $"dom nodes={doc.NodeCount} tree nodes={tree.Nodes.Count} tree_build_ms={treeMs} rules={r.RulesRun.Count} results={r.Results.Count} check_ms={ms} check_alloc_mb={alloc / 1048576.0:F1} native_json_ms={nativeMs} native_bytes={native} axe_json_ms={axeMs} axe_bytes={axe}\n");
    }
}
