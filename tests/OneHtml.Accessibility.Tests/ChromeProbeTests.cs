using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>
/// Chrome's output on three probe pages written for this repository (<c>testdata/rendered/pages/pseudoprobe.html</c>,
/// <c>controlprobe.html</c>, <c>nameprobe.html</c>; captures beside them), against our tree. They pin what the Chromium profile entries
/// <c>generated-content-node</c>, <c>control-text-node</c> and <c>positioned-blockification</c> say.
/// </summary>
public sealed class ChromeProbeTests
{
    private sealed record Page(Document Doc, DocumentStyles Styles, RenderedFacts Facts, JsonElement AxNodes);

    private static byte[] ReadGz(string dir, string name)
    {
        using FileStream f = File.OpenRead(Path.Combine(TestPaths.TestData, "rendered", "captures", dir, name + ".json.gz"));
        using var z = new GZipStream(f, CompressionMode.Decompress);
        using var ms = new MemoryStream();
        z.CopyTo(ms);
        return ms.ToArray();
    }

    private static Page Load(string dir)
    {
        using JsonDocument dom = JsonDocument.Parse(ReadGz(dir, "dom"));
        string outer = dom.RootElement.GetProperty("outerHTML").GetString()!;
        string doctype = dom.RootElement.TryGetProperty("doctype", out JsonElement dt) && dt.ValueKind == JsonValueKind.String ? dt.GetString()! : string.Empty;
        Document doc = Document.Parse(doctype + outer);
        StyleContext context = StyleContext.Default.WithMedia(MediaFeatures.Default with { Width = 1280, Height = 800 });
        DocumentStyles styles = DocumentStyles.Create(doc, context, new StyleSourceOptions());
        DomSnapshot snapshot = DomSnapshot.Parse(ReadGz(dir, "snapshot"));
        DomSnapshotMapResult map = DomSnapshotAdapter.Map(snapshot, doc, new DomSnapshotMapOptions { ViewportWidth = 1280, ViewportHeight = 800, Context = context });
        Assert.True(map.Succeeded, map.Refusal?.ToString());
        using JsonDocument ax = JsonDocument.Parse(ReadGz(dir, "ax"));
        return new Page(doc, styles, map.Facts!, ax.RootElement.GetProperty("frames")[0].GetProperty("nodes").Clone());
    }

    private static List<string> OursOutline(AxNode root)
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

            for (int i = n.Children.Count - 1; i >= 0; i--)
            {
                stack.Push((n.Children[i], depth + (n.IsIgnored ? 0 : 1)));
            }
        }

        return lines;
    }

    private static List<string> ChromeOutline(JsonElement nodes)
    {
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

            JsonElement[] kids = [.. n.GetProperty("childIds").EnumerateArray().Select(c => byId[c.GetString()!])];
            for (int i = kids.Length - 1; i >= 0; i--)
            {
                stack.Push((kids[i], depth + (ignored ? 0 : 1)));
            }
        }

        return lines;
    }

    [Fact]
    public void Generated_content_boxes_make_the_exposed_structure_Chromes_on_the_pseudo_probe()
    {
        Page p = Load("pseudoprobe");
        AccessibilityTree tree = AccessibilityTree.Build(p.Doc, p.Styles, null, new AccessibilityOptions { Facts = p.Facts });
        Assert.Equal(ChromeOutline(p.AxNodes), OursOutline(tree.Root));
    }

    [Fact]
    public void Text_fields_and_input_buttons_have_the_inner_nodes_Chrome_gives_them()
    {
        Page p = Load("controlprobe");
        AccessibilityTree tree = AccessibilityTree.Build(p.Doc, p.Styles, null, new AccessibilityOptions { Facts = p.Facts });

        // A text field: a generic with the editable state, holding the value as static text.
        AxNode text = tree.Find(p.Doc.Root.QuerySelector("[aria-label=t1]"))!;
        AxNode editor = Assert.Single(text.Children);
        Assert.Equal("generic", editor.Role);
        Assert.Equal("plaintext", editor.States.Editable);
        AxNode value = Assert.Single(editor.Children);
        Assert.Equal("typed", value.Name.Value);

        // A password shows bullets, and the empty field has an editor without text.
        Assert.Equal("\u2022\u2022\u2022\u2022\u2022\u2022", Assert.Single(Assert.Single(tree.Find(p.Doc.Root.QuerySelector("[aria-label=p1]"))!.Children).Children).Name.Value);
        Assert.Empty(Assert.Single(tree.Find(p.Doc.Root.QuerySelector("[aria-label=t2]"))!.Children).Children);

        // A placeholder is an ignored leaf before the editor; a search field's editor sits inside an ignored node.
        AxNode placeholder = tree.Find(p.Doc.Root.QuerySelector("[aria-label=t3]"))!;
        Assert.Equal(2, placeholder.Children.Count);
        Assert.True(placeholder.Children[0].IsIgnored && placeholder.Children[0].Children.Count == 0);
        AxNode search = tree.Find(p.Doc.Root.QuerySelector("[aria-label=s1]"))!;
        AxNode wrapper = Assert.Single(search.Children);
        Assert.True(wrapper.IsIgnored);
        Assert.Equal("generic", Assert.Single(wrapper.Children).Role);

        // Input buttons: the value, or the default label, as static text.
        Assert.Equal("Go", Assert.Single(tree.Find(p.Doc.Root.QuerySelector("input[value=Go]"))!.Children).Name.Value);
        Assert.Equal("Submit", Assert.Single(tree.Find(p.Doc.Root.QuerySelector("input[type=submit]:not([value])"))!.Children).Name.Value);
    }

    [Fact]
    public void Text_search_and_number_fields_have_Chromes_inner_nodes_with_and_without_a_placeholder()
    {
        // The property probe for inner editors: every field type, with and without a value, a placeholder, disabled and read-only.
        Page p = Load("axprops3");
        AccessibilityTree tree = AccessibilityTree.Build(p.Doc, p.Styles, null, new AccessibilityOptions { Facts = p.Facts });
        // The date field's pickers are not built (profile entry internal-roles), so the outlines are compared up to it.
        Assert.Equal(ChromeOutline(p.AxNodes).TakeWhile(l => l.Trim() != "Date"), OursOutline(tree.Root).TakeWhile(l => l.Trim() != "Date"));
    }

    [Fact]
    public void Link_names_have_Chromes_spaces_for_every_parent_and_child_display_on_the_name_probe()
    {
        Page p = Load("nameprobe");
        AccessibilityTree tree = AccessibilityTree.Build(p.Doc, p.Styles, null, new AccessibilityOptions { Facts = p.Facts });
        int compared = 0;
        foreach (JsonElement n in p.AxNodes.EnumerateArray())
        {
            if (n.GetProperty("role").GetProperty("value").GetString() != "link" || n.TryGetProperty("ignored", out JsonElement ig) && ig.GetBoolean())
            {
                continue;
            }

            string url = n.GetProperty("properties").EnumerateArray().First(x => x.GetProperty("name").GetString() == "url").GetProperty("value").GetProperty("value").GetString()!;
            string href = url[(url.LastIndexOf('/'))..];
            AxNode ours = tree.Find(p.Doc.Root.QuerySelector("a[href='" + href + "']"))!;
            Assert.Equal(n.GetProperty("name").GetProperty("value").GetString(), ours.Name.Value);
            compared++;
        }

        Assert.Equal(170, compared);
    }
}
