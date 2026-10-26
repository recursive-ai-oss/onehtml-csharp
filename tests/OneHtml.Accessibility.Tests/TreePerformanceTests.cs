using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>Writes tree build time and allocation for a 35,000-node page when ONEHTML_PERF_REPORT names a file.</summary>
public sealed class TreePerformanceTests
{
    [Fact]
    public void Build_time_and_allocation_for_a_35k_node_page()
    {
        string? report = Environment.GetEnvironmentVariable("ONEHTML_PERF_REPORT");
        if (report is null)
        {
            return;
        }

        var sb = new StringBuilder("<!doctype html><title>Perf</title><style>.card{margin:1em} .card h2{font-size:1.2em} nav a{color:#036} .hide{display:none}</style><body><main>");
        for (int i = 0; sb.Length < 450_000; i++)
        {
            sb.Append("<section class=card aria-label='Card ").Append(i).Append("'><h2>Heading ").Append(i).Append("</h2><p>Some <b>bold</b> text and a <a href='/p/").Append(i)
                .Append("'>link ").Append(i).Append("</a>.</p><ul><li>One</li><li>Two <span class=hide>x</span></li></ul><button type=button aria-describedby=d").Append(i).Append(">Go</button><span id=d").Append(i).Append(">help</span></section>");
        }

        sb.Append("</main></body>");
        Document doc = Document.Parse(sb.ToString());
        DocumentStyles styles = DocumentStyles.Create(doc);
        var sw = Stopwatch.StartNew();
        styles.ComputeAll();
        long cascade = sw.ElapsedMilliseconds;

        // Warm up once, then measure.
        AccessibilityTree.Build(doc, styles);
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
        long build = sw.ElapsedMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        sw.Restart();
        int exported = tree.ToChromeJson().Length;
        long export = sw.ElapsedMilliseconds;
        File.WriteAllText(report, $"dom nodes={doc.NodeCount} tree nodes={tree.Nodes.Count} cascade_ms={cascade} tree_build_ms={build} tree_alloc_mb={allocated / 1048576.0:F1} export_ms={export} export_bytes={exported}\n");
    }
}
