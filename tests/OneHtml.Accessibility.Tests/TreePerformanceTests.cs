using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
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

        // One attribute edit near the middle of the page: restyle plus Update against a full Build of the new document.
        Node target = doc.Root.QuerySelector("section.card:nth-of-type(500) a");
        string edited = "";
        for (int round = 0; round < 5; round++)
        {
            EditResult edit = doc.Apply([Edit.SetAttribute(target, "aria-label", "Renamed " + round)]);
            Assert.True(edit.Succeeded);
            GC.Collect();
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            RestyleResult rs = styles.Restyle(edit);
            long restyleMs = sw.ElapsedMilliseconds;
            AccessibilityTree updated = tree.Update(edit, rs);
            long updateMs = sw.ElapsedMilliseconds - restyleMs;
            long updateAlloc = GC.GetAllocatedBytesForCurrentThread() - b0;
            DocumentStyles fresh = DocumentStyles.Create(edit.Document!);
            fresh.ComputeAll();
            sw.Restart();
            AccessibilityTree rebuilt = AccessibilityTree.Build(edit.Document!, fresh);
            long rebuildMs = sw.ElapsedMilliseconds;
            edited += $"edit: restyle_ms={restyleMs} update_ms={updateMs} update_alloc_mb={updateAlloc / 1048576.0:F1} full_build_ms={rebuildMs} names_recomputed={updated.UpdateInfo!.NamesRecomputed}/{updated.UpdateInfo.ElementNodes} equal={updated.ToChromeJson() == rebuilt.ToChromeJson()}\n";
        }

        File.WriteAllText(report, edited + $"dom nodes={doc.NodeCount} tree nodes={tree.Nodes.Count} cascade_ms={cascade} tree_build_ms={build} tree_alloc_mb={allocated / 1048576.0:F1} export_ms={export} export_bytes={exported}\n");
    }
}
