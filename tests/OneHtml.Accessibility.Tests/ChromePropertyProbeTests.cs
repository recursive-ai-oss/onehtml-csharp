using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Differential;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>
/// The properties of every element with an <c>id</c> on the property probe pages (<c>testdata/rendered/pages/axprops*.html</c>), ours against
/// what Chrome 156 reported for the same pages (the captures beside them). The probes were written to see one behaviour each; the list at
/// the end names the few elements where we know we differ, and why.
/// </summary>
public sealed class ChromePropertyProbeTests
{
    private static byte[] ReadGz(string dir, string name)
    {
        using FileStream f = File.OpenRead(Path.Combine(TestPaths.TestData, "rendered", "captures", dir, name + ".json.gz"));
        using var z = new GZipStream(f, CompressionMode.Decompress);
        using var ms = new MemoryStream();
        z.CopyTo(ms);
        return ms.ToArray();
    }

    // Elements where our value differs from Chrome's, and why (also written in docs/chrome-ax-differential.md).
    private static readonly HashSet<string> s_known = new(StringComparer.Ordinal)
    {
        // An editing host with block content: Chrome's value has the line breaks of the rendered blocks, which are not worked out here.
        "axprops2:ce1.value chrome=a\nx ours=(absent)",
        "axprops:e1.value chrome=x\n\nbs ours=(absent)",
        // aria-selected=false on a row: Chrome states nothing (a grid cell and an option state it).
        "axprops:s16.selected chrome=(absent) ours=false",
    };

    [Fact]
    public void Properties_of_the_probe_elements_equal_Chromes()
    {
        List<string> differences = [.. Differences("axprops"), .. Differences("axprops2")];
        Assert.Equal(s_known.OrderBy(x => x, StringComparer.Ordinal).ToArray(), differences.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    private static List<string> Differences(string capture)
    {
        using JsonDocument dom = JsonDocument.Parse(ReadGz(capture, "dom"));
        string doctype = dom.RootElement.TryGetProperty("doctype", out JsonElement dt) && dt.ValueKind == JsonValueKind.String ? dt.GetString()! : string.Empty;
        Document doc = Document.Parse(doctype + dom.RootElement.GetProperty("outerHTML").GetString()!);
        StyleContext context = StyleContext.Default.WithMedia(MediaFeatures.Default with { Width = 1280, Height = 800 });
        DocumentStyles styles = DocumentStyles.Create(doc, context, new StyleSourceOptions());
        DomSnapshotMapResult map = DomSnapshotAdapter.Map(DomSnapshot.Parse(ReadGz(capture, "snapshot")), doc, new DomSnapshotMapOptions { ViewportWidth = 1280, ViewportHeight = 800, Context = context });
        Assert.True(map.Succeeded, map.Refusal?.ToString());
        var backendToDom = new Dictionary<long, int>();
        for (int i = 0; i < doc.NodeCount; i++)
        {
            if (map.Facts!.TryGet(doc.GetNode(i), out NodeFacts nf) && nf.BackendNodeId >= 0)
            {
                backendToDom[nf.BackendNodeId] = i;
            }
        }

        using JsonDocument ax = JsonDocument.Parse(ReadGz(capture, "ax"));
        AxSide chrome = AxReader.Read(ax.RootElement.GetProperty("frames")[0].GetProperty("nodes"), b => backendToDom.TryGetValue(b, out int d) ? d : -1, null);
        AccessibilityTree tree = AccessibilityTree.Build(doc, styles, null, new AccessibilityOptions { Facts = map.Facts });
        using JsonDocument ours = JsonDocument.Parse(tree.ToChromeJson());
        AxSide mine = AxReader.Read(ours.RootElement.GetProperty("nodes"), b => b > int.MaxValue ? -1 : (int)b, tree);
        var byDom = new Dictionary<int, AxEntry>();
        foreach (AxEntry e in mine.Entries.Where(e => e.Dom >= 0 && !e.IsText))
        {
            byDom.TryAdd(e.Dom, e);
        }

        var differences = new List<string>();
        foreach (AxEntry c in chrome.Entries.Where(e => e.Dom >= 0 && !e.IsText && !e.Ignored))
        {
            Node el = doc.GetNode(c.Dom);
            if (!el.IsElement || el.Id is not { Length: > 0 } id || !byDom.TryGetValue(c.Dom, out AxEntry? o) || o.Ignored)
            {
                continue;
            }

            foreach (string key in c.Props.Keys.Union(o.Props.Keys).Where(k => k is not ("focusable" or "focused" or "url")))
            {
                c.Props.TryGetValue(key, out string? cv);
                o.Props.TryGetValue(key, out string? ov);
                if (cv != ov)
                {
                    differences.Add(id + "." + key + " chrome=" + (cv ?? "(absent)") + " ours=" + (ov ?? "(absent)"));
                }
            }

            if (c.Value != o.Value)
            {
                differences.Add(id + ".value chrome=" + (c.Value ?? "(absent)") + " ours=" + (o.Value ?? "(absent)"));
            }
        }

        return differences.Select(d => capture + ":" + d).ToList();
    }
}
