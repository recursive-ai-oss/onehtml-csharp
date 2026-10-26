using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>
/// The tree built with rendered facts, on the Chrome capture of <c>testdata/rendered/pages/layout.html</c> (kept with Chrome's own
/// <c>Accessibility.getFullAXTree</c> output). The tree must expose the same structure as Chrome there; without facts it must stay
/// what it was before facts existed.
/// </summary>
public sealed class LayoutFactsTreeTests
{
    private static readonly string Dir = Path.Combine(TestPaths.TestData, "rendered", "captures", "layout");

    private sealed record Page(Document Doc, DomSnapshot Snapshot, DocumentStyles Styles, RenderedFacts Facts, StyleContext Context);

    private static byte[] ReadGz(string name)
    {
        using FileStream f = File.OpenRead(Path.Combine(Dir, name + ".json.gz"));
        using var z = new GZipStream(f, CompressionMode.Decompress);
        using var ms = new MemoryStream();
        z.CopyTo(ms);
        return ms.ToArray();
    }

    private static Page Load()
    {
        using JsonDocument dom = JsonDocument.Parse(ReadGz("dom"));
        string outer = dom.RootElement.GetProperty("outerHTML").GetString()!;
        string doctype = dom.RootElement.TryGetProperty("doctype", out JsonElement dt) && dt.ValueKind == JsonValueKind.String ? dt.GetString()! : string.Empty;
        Document doc = Document.Parse(doctype + outer);
        StyleContext context = StyleContext.Default.WithMedia(MediaFeatures.Default with { Width = 1280, Height = 800 });
        DocumentStyles styles = DocumentStyles.Create(doc, context, new StyleSourceOptions());
        DomSnapshot snapshot = DomSnapshot.Parse(ReadGz("snapshot"));
        DomSnapshotMapResult map = DomSnapshotAdapter.Map(snapshot, doc, new DomSnapshotMapOptions { ViewportWidth = 1280, ViewportHeight = 800, Context = context });
        Assert.True(map.Succeeded, map.Refusal?.ToString());
        return new Page(doc, snapshot, styles, map.Facts!, context);
    }

    private static AccessibilityTree Build(Page p, bool facts) =>
        AccessibilityTree.Build(p.Doc, p.Styles, null, new AccessibilityOptions { Facts = facts ? p.Facts : null });

    /// <summary>The exposed tree as lines of depth and role: ignored nodes dissolve into their parent.</summary>
    private static List<string> Outline(AxNode root)
    {
        var lines = new List<string>();
        var stack = new Stack<(AxNode Node, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            (AxNode n, int depth) = stack.Pop();
            if (!n.IsIgnored)
            {
                lines.Add(new string(' ', depth) + (n.InternalRole ?? n.Role));
            }

            if (n.Role == "textbox")
            {
                continue; // the inner editor is shadow content, which the Chrome outline leaves out too
            }

            for (int i = n.Children.Count - 1; i >= 0; i--)
            {
                stack.Push((n.Children[i], depth + (n.IsIgnored ? 0 : 1)));
            }
        }

        return lines;
    }

    /// <summary>The same outline from Chrome's output. The shadow content of a text field has no node in the document and is left out.</summary>
    private static List<string> ChromeOutline()
    {
        using JsonDocument ax = JsonDocument.Parse(ReadGz("ax"));
        JsonElement nodes = ax.RootElement.GetProperty("frames")[0].GetProperty("nodes");
        var byId = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement n in nodes.EnumerateArray())
        {
            byId[n.GetProperty("nodeId").GetString()!] = n;
        }

        var lines = new List<string>();
        var stack = new Stack<(JsonElement Node, int Depth)>();
        stack.Push((nodes[0], 0));
        while (stack.Count > 0)
        {
            (JsonElement n, int depth) = stack.Pop();
            string role = n.GetProperty("role").GetProperty("value").GetString()!;
            if (role == "InlineTextBox")
            {
                continue;
            }

            bool ignored = n.TryGetProperty("ignored", out JsonElement ig) && ig.GetBoolean();
            if (!ignored)
            {
                lines.Add(new string(' ', depth) + role);
            }

            if (role == "textbox")
            {
                continue;
            }

            JsonElement[] kids = [.. n.GetProperty("childIds").EnumerateArray().Select(c => byId[c.GetString()!])];
            for (int i = kids.Length - 1; i >= 0; i--)
            {
                stack.Push((kids[i], depth + (ignored ? 0 : 1)));
            }
        }

        return lines;
    }

    [Fact]
    public void The_exposed_structure_with_facts_equals_Chromes_on_the_layout_page()
    {
        Page page = Load();
        List<string> chrome = ChromeOutline();
        List<string> ours = Outline(Build(page, true).Root);
        Assert.Equal(chrome, ours);
    }

    [Fact]
    public void Without_facts_every_container_is_exposed_and_no_layout_nodes_exist()
    {
        Page page = Load();
        AccessibilityTree tree = Build(page, false);
        Assert.Null(tree.Facts);
        Assert.DoesNotContain(tree.Nodes, n => n.Kind == AxNodeKind.Generated);
        Assert.DoesNotContain(tree.Nodes, n => n.InternalRole is "LayoutTable" or "LayoutTableRow" or "LayoutTableCell" or "ListMarker" or "MenuListPopup");

        // The block that only holds blocks is generic and exposed, as it always was.
        AxNode blocks = tree.Find(page.Doc.Root.QuerySelector(".blocks"))!;
        Assert.False(blocks.IsIgnored);
        Assert.Equal("generic", blocks.Role);
    }

    [Fact]
    public void Containers_are_pruned_by_what_layout_made_of_them()
    {
        Page page = Load();
        AccessibilityTree tree = Build(page, true);
        bool Ignored(string selector) => tree.Find(page.Doc.Root.QuerySelector(selector))!.IsIgnored;

        Assert.False(Ignored(".text-only"));
        Assert.False(Ignored(".inline-child"));
        Assert.False(Ignored(".positioned"));
        Assert.False(Ignored(".with-id"));
        Assert.False(Ignored(".with-title"));
        Assert.False(Ignored(".inline-block"));
        Assert.False(Ignored(".para"));
        Assert.True(Ignored(".blocks"));
        Assert.True(Ignored(".mixed"));
        Assert.True(Ignored(".empty"));
        Assert.True(Ignored(".flexbox"));
        Assert.True(Ignored(".gridbox"));
        Assert.True(Ignored(".para-empty"));
        Assert.Equal(["uninteresting"], tree.Find(page.Doc.Root.QuerySelector(".blocks"))!.IgnoredReasons);

        // Inline boxes that Chromium leaves out have no node at all; their text belongs to the paragraph.
        Assert.Null(tree.Find(page.Doc.Root.QuerySelector(".formatting b")));
        Assert.Null(tree.Find(page.Doc.Root.QuerySelector(".formatting span")));
        Assert.Same(tree.Find(page.Doc.Root.QuerySelector(".formatting"))!, tree.Find(page.Doc.Root.QuerySelector(".formatting b").FirstChild)!.Parent);
    }

    [Fact]
    public void Tables_are_data_or_layout_tables_by_the_evidence_in_them()
    {
        Page page = Load();
        AccessibilityTree tree = Build(page, true);
        string? Internal(string selector) => tree.Find(page.Doc.Root.QuerySelector(selector))!.InternalRole;

        Assert.Equal("LayoutTable", Internal("table.one"));
        Assert.Equal("LayoutTable", Internal("table.plain"));
        Assert.Equal("LayoutTableRow", Internal("table.plain tr"));
        Assert.Equal("LayoutTableCell", Internal("table.plain td"));
        Assert.Null(Internal("table.bordered"));
        Assert.Null(Internal("table.captioned"));
        Assert.Null(Internal("table.headed"));
        Assert.Equal("table", tree.Find(page.Doc.Root.QuerySelector("table.headed"))!.Role);
        Assert.Equal("columnheader", tree.Find(page.Doc.Root.QuerySelector("table.headed th"))!.Role);
    }

    [Fact]
    public void List_markers_and_select_popups_are_nodes_of_their_own()
    {
        Page page = Load();
        AccessibilityTree tree = Build(page, true);

        AxNode first = tree.Find(page.Doc.Root.QuerySelector(".bullets > li"))!;
        AxNode marker = first.Children[0];
        Assert.Equal(AxNodeKind.Generated, marker.Kind);
        Assert.Equal("ListMarker", marker.InternalRole);
        Assert.Equal("• ", marker.Name.Value);
        Assert.False(marker.IsIgnored);
        Assert.Same(first, marker.Parent);
        Assert.Single(marker.Children);
        Assert.True(marker.Children[0].IsIgnored);
        Assert.Equal("1. ", tree.Find(page.Doc.Root.QuerySelector(".numbers > li"))!.Children[0].Name.Value);
        Assert.DoesNotContain(tree.Find(page.Doc.Root.QuerySelector(".unmarked > li"))!.Children, c => c.Kind == AxNodeKind.Generated);

        AxNode select = tree.Find(page.Doc.Root.QuerySelector("select"))!;
        AxNode popup = Assert.Single(select.Children);
        Assert.Equal("MenuListPopup", popup.InternalRole);
        Assert.Equal(2, popup.Children.Count);
        Assert.All(popup.Children, o => Assert.Same(popup, o.Parent));
        Assert.All(popup.Children, o => Assert.True(o.Focus.IsFocusable));

        // Ids are consecutive in tree order, generated nodes included, and the export leaves them without a DOM node.
        Assert.All(tree.Nodes.Select((n, i) => (n, i)), x => Assert.Equal(x.i, x.n.Id));
        using JsonDocument export = JsonDocument.Parse(tree.ToChromeJson());
        JsonElement[] nodes = [.. export.RootElement.GetProperty("nodes").EnumerateArray()];
        Assert.Equal(tree.Nodes.Count, nodes.Length);
        JsonElement markerJson = nodes[marker.Id];
        Assert.Equal("ListMarker", markerJson.GetProperty("role").GetProperty("value").GetString());
        Assert.False(markerJson.TryGetProperty("backendDOMNodeId", out _));
    }

    [Fact]
    public void The_same_page_gives_the_same_tree_every_time()
    {
        Page page = Load();
        Assert.Equal(Build(page, true).ToChromeJson(), Build(page, true).ToChromeJson());
    }

    [Fact]
    public void Facts_of_another_document_or_context_are_refused()
    {
        Page page = Load();
        Document other = Document.Parse("<p>other</p>");
        DocumentStyles otherStyles = DocumentStyles.Create(other, page.Context, new StyleSourceOptions());
        Assert.Throws<ArgumentException>(() => AccessibilityTree.Build(other, otherStyles, null, new AccessibilityOptions { Facts = page.Facts }));

        StyleContext narrow = page.Context.WithMedia(MediaFeatures.Default with { Width = 400, Height = 800 });
        Assert.Throws<ArgumentException>(() => AccessibilityTree.Build(page.Doc, page.Styles, narrow, new AccessibilityOptions { Facts = page.Facts }));
    }

    private static (EditResult Edit, RestyleResult Restyle) EditTitle(Page page)
    {
        Node target = page.Doc.Root.QuerySelector(".with-title");
        EditResult edit = page.Doc.Apply([Edit.SetAttribute(target, "title", "Renamed")]);
        Assert.True(edit.Succeeded);
        return (edit, page.Styles.Restyle(edit));
    }

    [Fact]
    public void An_update_never_keeps_facts_and_equals_a_fresh_build()
    {
        Page page = Load();
        AccessibilityTree before = Build(page, true);
        (EditResult edit, RestyleResult restyle) = EditTitle(page);

        // Without new facts: the result has none, and equals a build without facts.
        AccessibilityTree updated = before.Update(edit, restyle);
        Assert.Null(updated.Facts);
        Assert.False(updated.UpdateInfo!.Incremental);
        Assert.Contains("facts", updated.UpdateInfo.FullRebuildReason, StringComparison.Ordinal);
        AccessibilityTree fresh = AccessibilityTree.Build(edit.Document!, restyle.Styles);
        Assert.Equal(fresh.ToChromeJson(), updated.ToChromeJson());

        // With facts of the edited document (the same layout; only a title changed): equal to a build with those facts.
        DomSnapshotMapResult map = DomSnapshotAdapter.Map(page.Snapshot, edit.Document!, new DomSnapshotMapOptions { ViewportWidth = 1280, ViewportHeight = 800, Context = page.Context, Attributes = AttributeCheck.None });
        Assert.True(map.Succeeded, map.Refusal?.ToString());
        AccessibilityTree withFacts = before.Update(edit, restyle, map.Facts);
        Assert.Same(map.Facts, withFacts.Facts);
        AccessibilityTree freshWith = AccessibilityTree.Build(edit.Document!, restyle.Styles, null, new AccessibilityOptions { Facts = map.Facts });
        Assert.Equal(freshWith.ToChromeJson(), withFacts.ToChromeJson());
        Assert.Equal("Renamed", withFacts.Find(edit.Document!.Root.QuerySelector(".with-title"))!.Description.Value);

        // The old facts do not describe the edited document.
        Assert.Throws<ArgumentException>(() => before.Update(edit, restyle, page.Facts));
    }

    [Fact]
    public void A_tree_without_facts_still_updates_incrementally()
    {
        Page page = Load();
        AccessibilityTree before = Build(page, false);
        (EditResult edit, RestyleResult restyle) = EditTitle(page);
        AccessibilityTree updated = before.Update(edit, restyle);
        Assert.True(updated.UpdateInfo!.Incremental, updated.UpdateInfo.FullRebuildReason);
        Assert.Equal(AccessibilityTree.Build(edit.Document!, restyle.Styles).ToChromeJson(), updated.ToChromeJson());
    }

    [Fact]
    public void Changing_the_context_drops_the_facts()
    {
        Page page = Load();
        AccessibilityTree tree = Build(page, true);
        AccessibilityTree other = tree.WithContext(page.Context.WithFocus(page.Doc.Root.QuerySelector("input")));
        Assert.Null(other.Facts);
    }

    [Fact]
    public void Generated_nodes_are_paired_by_their_host_in_a_diff()
    {
        Page page = Load();
        AccessibilityTree a = Build(page, true);
        AccessibilityTree b = Build(page, true);
        Assert.Contains(a.Nodes, n => n.Kind == AxNodeKind.Generated);
        Assert.True(TreeDiff.Compute(a, b).IsEmpty);

        // Without facts the generated nodes are removed and nothing generated is added.
        TreeDiff plain = TreeDiff.Compute(a, Build(page, false));
        Assert.Contains(plain.Removed, n => n.Kind == AxNodeKind.Generated);
        Assert.DoesNotContain(plain.Added, n => n.Kind == AxNodeKind.Generated);
    }
}
