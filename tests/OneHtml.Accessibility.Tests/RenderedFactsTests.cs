using System;
using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

public sealed class RenderedFactsTests
{
    private static FakeLayout Box(double x, double y, double w, double h, int paint = 1, bool stacking = false, string? text = null) =>
        new() { X = x, Y = y, W = w, H = h, Paint = paint, Stacking = stacking, Text = text, Styles = ["block", "rgb(0, 0, 0)", "visible"] };

    private static (Document Doc, FakeNode Root) Fake(string html, Func<Node, FakeLayout?>? layout = null)
    {
        Document doc = Document.Parse(html);
        return (doc, FakeSnapshot.FromDocument(doc, layout));
    }

    private static DomSnapshotMapResult Map(FakeNode root, Document doc, DomSnapshotMapOptions? options = null) =>
        DomSnapshotAdapter.Map(DomSnapshot.Parse(FakeSnapshot.ToJson(root), FakeSnapshot.StyleNames), doc, options);

    private static RenderedFacts Ok(DomSnapshotMapResult r)
    {
        Assert.True(r.Succeeded, r.Refusal?.ToString());
        Assert.True(r.Accounting!.IsComplete, r.Accounting.ToString());
        return r.Facts!;
    }

    private static string Id(Node n) => n.Id;

    private static Func<Node, FakeLayout?> LayoutVisible(params string[] hiddenIds)
    {
        double y = 0;
        return n =>
        {
            for (Node c = n; !c.IsNull; c = c.Parent)
            {
                if (c.IsElement && hiddenIds.Contains(c.Id))
                {
                    return null;
                }
            }

            if (n.Kind == NodeKind.Element)
            {
                return Box(0, y += 20, 100, 18);
            }

            if (n.Kind == NodeKind.Text && n.Data.Trim().Length > 0)
            {
                var l = Box(0, y, 100, 18, text: n.Data.ToString().Trim());
                l.Boxes.Add((0, n.Data.Length, 0, y, 60, 18));
                return l;
            }

            return null;
        };
    }

    [Fact]
    public void Hidden_nodes_have_no_layout_and_visible_ones_have_boxes()
    {
        (Document doc, FakeNode root) = Fake("<!DOCTYPE html><title>t</title><div id=a><p id=in>x</p></div><p id=b>hello world</p>", LayoutVisible("a"));
        RenderedFacts facts = Ok(Map(root, doc));

        Assert.False(facts[doc.Root.QuerySelector("#a")].HasLayout);
        Assert.False(facts[doc.Root.QuerySelector("#in")].HasLayout);
        NodeFacts b = facts[doc.Root.QuerySelector("#b")];
        Assert.True(b.HasLayout);
        Assert.True(b.Layout.BorderBox.Width == 100);
        Assert.Equal("block", b.GetStyle("display"));
        Assert.Null(b.GetStyle("no-such-property"));
        Assert.Equal(1, b.Layout.PaintOrder);

        NodeFacts text = facts[doc.Root.QuerySelector("#b").FirstChild];
        Assert.Equal("hello world", text.Layout.LayoutText);
        Assert.Equal(1, text.Layout.TextBoxes.Length);
        Assert.Equal(11, text.Layout.TextBoxes[0].Length);
        Assert.Equal(60, text.Layout.TextBoxes[0].Bounds.Width);
        Assert.Equal("rgb(255, 255, 255)", text.Layout.BlendedBackgroundColor);
        Assert.Equal(1.0, text.Layout.TextColorOpacity);
    }

    [Fact]
    public void Mapping_is_complete_and_keyed_by_backend_id()
    {
        (Document doc, FakeNode root) = Fake("<!DOCTYPE html><ul><li>a<li>b</ul>", LayoutVisible());
        DomSnapshotMapResult r = Map(root, doc);
        RenderedFacts facts = Ok(r);
        Assert.Equal(DomSnapshot.Parse(FakeSnapshot.ToJson(root), FakeSnapshot.StyleNames).TotalNodeCount, r.Accounting!.TotalNodes);
        Node li = doc.Root.QuerySelector("li");
        Assert.True(facts[li].BackendNodeId >= 100);
        Assert.True(facts[li].SnapshotNodeIndex > 0);
        Assert.Equal(DomSnapshotAdapter.StructureFingerprint(doc), facts.StructureFingerprint);
        Assert.Equal(facts.StructureFingerprint, DomSnapshotAdapter.StructureFingerprint(DomSnapshot.Parse(FakeSnapshot.ToJson(root), FakeSnapshot.StyleNames)));
    }

    [Fact]
    public void Whitespace_only_text_nodes_are_mapped_without_layout()
    {
        string html = "<!DOCTYPE html>\n<html>\n<body>\n  <div id=a>\n    <span>x</span>\n    <span>y</span>\n  </div>\n</body>\n</html>\n";
        (Document doc, FakeNode root) = Fake(html, LayoutVisible());
        RenderedFacts facts = Ok(Map(root, doc));
        Node a = doc.Root.QuerySelector("#a");
        Node ws = a.FirstChild;
        Assert.Equal(NodeKind.Text, ws.Kind);
        Assert.False(facts[ws].HasLayout);
        Assert.True(facts[ws].SnapshotNodeIndex >= 0);
    }

    [Fact]
    public void Pseudo_elements_attach_to_their_host()
    {
        (Document doc, FakeNode root) = Fake("<!DOCTYPE html><p id=b>text</p><p id=c>none</p>", LayoutVisible());
        FakeNode p = FakeSnapshot.Find(root, "P");
        var before = new FakeNode { Type = 1, Name = "::before", Pseudo = "before", Layout = Box(0, 5, 40, 18), Backend = 9001 };
        before.Kids.Add(new FakeNode { Type = 3, Name = "#text", Value = "New: ", Layout = Box(0, 5, 40, 18, text: "New: "), Backend = 9002 });
        before.Kids[0].Layout!.Boxes.Add((0, 5, 0, 5, 40, 18));
        p.Kids.Insert(0, before);
        p.Kids.Add(new FakeNode { Type = 1, Name = "::marker", Pseudo = "marker", Backend = 9003 });

        DomSnapshotMapResult r = Map(root, doc);
        RenderedFacts facts = Ok(r);
        Assert.Equal(3, r.Accounting!.Pseudo);
        IReadOnlyList<GeneratedBox> gens = facts[doc.Root.QuerySelector("#b")].GeneratedBoxes;
        Assert.Equal(2, gens.Count);
        Assert.Equal("before", gens[0].PseudoType);
        Assert.True(gens[0].HasLayout);
        Assert.Equal("New: ", gens[0].Text);
        Assert.Equal(2, gens[0].Boxes.Count);
        Assert.Equal(5, gens[0].Boxes[1].TextBoxes[0].Length);
        Assert.Equal("marker", gens[1].PseudoType);
        Assert.False(gens[1].HasLayout);
        Assert.Empty(facts[doc.Root.QuerySelector("#c")].GeneratedBoxes);
        Assert.Equal(2, facts.GeneratedBoxCount);
    }

    [Fact]
    public void User_agent_shadow_trees_do_not_break_the_mapping()
    {
        (Document doc, FakeNode root) = Fake("<!DOCTYPE html><label>n <input id=i value=v></label><p>after</p>", LayoutVisible());
        FakeNode input = FakeSnapshot.Find(root, "INPUT");
        var shadow = new FakeNode { Type = 11, Name = "#document-fragment", ShadowRoot = "user-agent", Backend = 7000 };
        var inner = new FakeNode { Type = 1, Name = "DIV", Layout = Box(1, 1, 5, 5), Backend = 7001 };
        inner.Kids.Add(new FakeNode { Type = 3, Name = "#text", Value = "v", Layout = Box(1, 1, 5, 5, text: "v"), Backend = 7002 });
        shadow.Kids.Add(inner);
        input.Kids.Add(shadow);
        input.InputValue = "v";
        input.InputChecked = true;

        DomSnapshotMapResult r = Map(root, doc);
        RenderedFacts facts = Ok(r);
        Assert.Equal(3, r.Accounting!.Shadow);
        Assert.Equal(2, r.Accounting.LayoutShadow);
        Assert.Equal("v", facts[doc.Root.QuerySelector("#i")].InputValue);
        Assert.True(facts[doc.Root.QuerySelector("#i")].HasLayout);
        Assert.True(facts[doc.Root.QuerySelector("#i")].InputChecked);
        Assert.False(facts[doc.Root.QuerySelector("p")].InputChecked);
    }

    [Fact]
    public void Template_content_is_mapped_when_the_snapshot_has_it_and_left_alone_when_not()
    {
        string html = "<!DOCTYPE html><p>a</p><template id=t><div id=in><b>x</b></div></template><p>b</p>";
        (Document doc, FakeNode root) = Fake(html, LayoutVisible());
        RenderedFacts facts = Ok(Map(root, doc));
        Node inner = doc.Root.QuerySelector("template").TemplateContent.FirstChild;
        Assert.Equal("in", inner.Id);
        Assert.True(facts[inner].SnapshotNodeIndex > 0);

        FakeNode template = FakeSnapshot.Find(root, "TEMPLATE");
        template.Kids.Clear();
        RenderedFacts without = Ok(Map(root, doc));
        Assert.Equal(-1, without[inner].SnapshotNodeIndex);
        Assert.False(without[inner].HasLayout);
    }

    [Fact]
    public void Frames_are_other_documents_and_can_be_mapped_separately()
    {
        (Document doc, FakeNode root) = Fake("<!DOCTYPE html><iframe id=f></iframe><p>x</p>", LayoutVisible());
        (Document inner, FakeNode innerRoot) = Fake("<!DOCTYPE html><h2 id=h>in frame</h2>", LayoutVisible());
        FakeSnapshot.Find(root, "IFRAME").ContentDocument = 1;
        DomSnapshot snap = DomSnapshot.Parse(FakeSnapshot.ToJson(root, innerRoot), FakeSnapshot.StyleNames);

        DomSnapshotMapResult page = DomSnapshotAdapter.Map(snap, doc);
        RenderedFacts pageFacts = Ok(page);
        Assert.Equal(snap.Documents[1].NodeCount, page.Accounting!.OtherDocuments);
        FrameLink link = Assert.Single(pageFacts.Frames);
        Assert.Equal(doc.Root.QuerySelector("#f").Index, link.NodeIndex);
        Assert.Equal(1, link.SnapshotDocumentIndex);

        DomSnapshotMapResult frame = DomSnapshotAdapter.Map(snap, inner, new DomSnapshotMapOptions { DocumentIndex = link.SnapshotDocumentIndex });
        RenderedFacts frameFacts = Ok(frame);
        Assert.True(frameFacts[inner.Root.QuerySelector("#h")].HasLayout);
        Assert.Equal(snap.Documents[0].NodeCount, frame.Accounting!.OtherDocuments);
    }

    [Fact]
    public void A_document_parsed_from_the_serialized_dom_maps_when_the_snapshot_has_a_doctype_and_comments()
    {
        (Document original, FakeNode root) = Fake("<!DOCTYPE html><!--c--><html><body><p>x</p></body></html><!--after-->", LayoutVisible());
        Document fromOuter = Document.Parse(original.SerializeNode(original.DocumentElement));
        DomSnapshotMapResult r = Map(root, fromOuter);
        RenderedFacts facts = Ok(r);
        Assert.True(r.Accounting!.Prolog >= 2);
        Assert.True(facts[fromOuter.Root.QuerySelector("p")].HasLayout);
        Ok(Map(root, original));
    }

    [Fact]
    public void Stacking_context_membership_and_paint_order()
    {
        (Document doc, FakeNode root) = Fake("<!DOCTYPE html><div id=a><span id=in>x</span></div><div id=b></div>", n =>
            n.Kind != NodeKind.Element ? null : n.Id switch
            {
                "a" => Box(0, 0, 50, 50, paint: 3, stacking: true),
                "b" => Box(10, 10, 50, 50, paint: 7, stacking: true),
                "in" => Box(0, 0, 10, 10, paint: 4),
                _ => Box(0, 0, 1, 1, paint: 0),
            });
        RenderedFacts facts = Ok(Map(root, doc));
        Node a = doc.Root.QuerySelector("#a"), b = doc.Root.QuerySelector("#b"), inner = doc.Root.QuerySelector("#in");
        Assert.True(facts[a].IsStackingContext);
        Assert.False(facts[inner].IsStackingContext);
        Assert.Equal(a, facts.GetStackingContext(inner));
        Assert.Equal(a, facts.GetStackingContext(a));
        Assert.True(facts[b].Layout.PaintOrder > facts[a].Layout.PaintOrder);
        Assert.True(facts[a].Layout.BorderBox.Intersects(facts[b].Layout.BorderBox));
        Assert.True(facts.GetStackingContext(doc.Root.QuerySelector("body")).IsNull);
    }

    [Fact]
    public void A_node_of_another_document_is_rejected()
    {
        (Document doc, FakeNode root) = Fake("<!DOCTYPE html><p>x</p>", LayoutVisible());
        RenderedFacts facts = Ok(Map(root, doc));
        Document other = Document.Parse("<!DOCTYPE html><p>x</p>");
        Assert.False(facts.TryGet(other.Root.QuerySelector("p"), out _));
        Assert.Throws<ArgumentException>(() => facts[other.Root.QuerySelector("p")]);
        Assert.False(facts.TryGet(default, out _));
    }

    // ---- refusals ----

    private static SnapshotRefusal Refused(string snapshotHtml, string documentHtml, AttributeCheck attributes = AttributeCheck.Values)
    {
        (_, FakeNode root) = Fake(snapshotHtml, LayoutVisible());
        DomSnapshotMapResult r = Map(root, Document.Parse(documentHtml), new DomSnapshotMapOptions { Attributes = attributes });
        Assert.False(r.Succeeded);
        Assert.NotNull(r.Refusal);
        return r.Refusal!;
    }

    [Fact]
    public void A_removed_or_added_node_is_a_count_mismatch()
    {
        SnapshotRefusal r = Refused("<!DOCTYPE html><div><p>a</p><p>b</p></div>", "<!DOCTYPE html><div><p>a</p></div>");
        Assert.Equal(RefusalReason.NodeCountMismatch, r.Reason);
        Assert.Contains("elements", r.Message, StringComparison.Ordinal);
        Assert.Equal(RefusalReason.NodeCountMismatch, Refused("<!DOCTYPE html><div><p>a</p></div>", "<!DOCTYPE html><div><p>a</p><p>b</p></div>").Reason);
    }

    [Fact]
    public void A_renamed_element_is_a_structure_mismatch_with_a_path()
    {
        SnapshotRefusal r = Refused("<!DOCTYPE html><div><p>a</p></div>", "<!DOCTYPE html><div><span>a</span></div>");
        Assert.Equal(RefusalReason.StructureMismatch, r.Reason);
        Assert.Contains("<p>", r.Message, StringComparison.Ordinal);
        Assert.Contains("<span>", r.Message, StringComparison.Ordinal);
        Assert.StartsWith("html[1] > body[2] > div[1]", r.Path, StringComparison.Ordinal);
        Assert.True(r.DocumentNode > 0 && r.SnapshotNode > 0);
    }

    [Fact]
    public void Moved_nodes_with_equal_counts_are_refused()
    {
        SnapshotRefusal r = Refused("<!DOCTYPE html><div><p>a</p></div><section></section>", "<!DOCTYPE html><div></div><section><p>a</p></section>");
        Assert.Equal(RefusalReason.StructureMismatch, r.Reason);
    }

    [Fact]
    public void Changed_text_is_refused()
    {
        SnapshotRefusal r = Refused("<!DOCTYPE html><p>old text</p>", "<!DOCTYPE html><p>new text</p>");
        Assert.Equal(RefusalReason.TextMismatch, r.Reason);
        Assert.Contains("old text", r.Message, StringComparison.Ordinal);
        Assert.Contains("new text", r.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Attribute_changes_are_refused_or_allowed_by_the_documented_rule()
    {
        string a = "<!DOCTYPE html><p id=x class=one>t</p>";
        SnapshotRefusal valueChanged = Refused(a, "<!DOCTYPE html><p id=x class=two>t</p>");
        Assert.Equal(RefusalReason.AttributeMismatch, valueChanged.Reason);
        Assert.Contains("class", valueChanged.Message, StringComparison.Ordinal);

        (Document doc, FakeNode root) = Fake(a, LayoutVisible());
        Document edited = Document.Parse("<!DOCTYPE html><p id=x class=two>t</p>");
        Assert.True(Map(root, edited, new DomSnapshotMapOptions { Attributes = AttributeCheck.Names }).Succeeded);
        Assert.True(Map(root, edited, new DomSnapshotMapOptions { Attributes = AttributeCheck.None }).Succeeded);
        Assert.True(Map(root, doc).Succeeded);

        Assert.Equal(RefusalReason.AttributeMismatch, Refused(a, "<!DOCTYPE html><p id=x class=one title=t>t</p>", AttributeCheck.Names).Reason);
        Assert.Equal(RefusalReason.AttributeMismatch, Refused(a, "<!DOCTYPE html><p id=x>t</p>", AttributeCheck.Names).Reason);
        Assert.Equal(RefusalReason.AttributeMismatch, Refused(a, "<!DOCTYPE html><p id=x class=one lang=en>t</p>").Reason);
    }

    [Fact]
    public void Two_different_pages_are_refused()
    {
        SnapshotRefusal r = Refused("<!DOCTYPE html><h1>One</h1><p>a</p>", "<!DOCTYPE html><ul><li>x<li>y</ul>");
        Assert.Equal(RefusalReason.NodeCountMismatch, r.Reason);
    }

    [Fact]
    public void Bad_document_arguments_are_refused_not_thrown()
    {
        (Document doc, FakeNode root) = Fake("<!DOCTYPE html><p>x</p>", LayoutVisible());
        DomSnapshot snap = DomSnapshot.Parse(FakeSnapshot.ToJson(root));
        Assert.Equal(RefusalReason.DocumentIndexOutOfRange, DomSnapshotAdapter.Map(snap, doc, new DomSnapshotMapOptions { DocumentIndex = 3 }).Refusal!.Reason);
        Assert.Equal(RefusalReason.DocumentIndexOutOfRange, DomSnapshotAdapter.Map(snap, doc, new DomSnapshotMapOptions { DocumentIndex = -1 }).Refusal!.Reason);
        var fragment = Document.ParseFragment("<p>x</p>", new FragmentContext(ElementNamespace.Html, "body"));
        Assert.Equal(RefusalReason.DocumentIsFragment, DomSnapshotAdapter.Map(snap, fragment).Refusal!.Reason);
    }

    [Fact]
    public void Mapping_can_be_cancelled()
    {
        (Document doc, FakeNode root) = Fake("<!DOCTYPE html>" + string.Concat(Enumerable.Repeat("<p>x</p>", 5000)), LayoutVisible());
        DomSnapshot snap = DomSnapshot.Parse(FakeSnapshot.ToJson(root));
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => DomSnapshotAdapter.Map(snap, doc, null, cts.Token));
    }

    [Fact]
    public void Wide_documents_map_in_linear_time()
    {
        var limits = new ParseOptions { Limits = new ParseLimits { MaxOpenElements = 100_000, MaxNodes = 10_000_000 } };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Document wide = Document.Parse("<!DOCTYPE html><body>" + string.Concat(Enumerable.Repeat("<p class=c data-a=1 data-b=2 data-c=3 data-d=4 data-e=5 data-f=6 data-g=7 data-h=8 data-i=9>t", 60_000)), limits);
        long tParse = sw.ElapsedMilliseconds;
        FakeNode wideRoot = FakeSnapshot.FromDocument(wide);
        long tFake = sw.ElapsedMilliseconds;
        byte[] json = FakeSnapshot.ToJson(wideRoot);
        long tJson = sw.ElapsedMilliseconds;
        DomSnapshot wideSnap = DomSnapshot.Parse(json);
        long tRead = sw.ElapsedMilliseconds;
        Assert.True(DomSnapshotAdapter.Map(wideSnap, wide).Succeeded);
        long tMap = sw.ElapsedMilliseconds;
        Assert.True(tMap - tRead < 5000, $"parse {tParse} fake {tFake} json {tJson} read {tRead} map {tMap}");
    }

    [Fact]
    public void Deep_documents_map_without_recursion()
    {
        var limits = new ParseOptions { Limits = new ParseLimits { MaxOpenElements = 100_000, MaxNodes = 10_000_000 } };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Document deep = Document.Parse("<!DOCTYPE html>" + string.Concat(Enumerable.Repeat("<div>", 3000)) + "x", limits);
        long tParse = sw.ElapsedMilliseconds;
        FakeNode deepRoot = FakeSnapshot.FromDocument(deep);
        long tFake = sw.ElapsedMilliseconds;
        byte[] json = FakeSnapshot.ToJson(deepRoot);
        long tJson = sw.ElapsedMilliseconds;
        DomSnapshot deepSnap = DomSnapshot.Parse(json);
        long tRead = sw.ElapsedMilliseconds;
        DomSnapshotMapResult r = DomSnapshotAdapter.Map(deepSnap, deep);
        long tMap = sw.ElapsedMilliseconds;
        Assert.True(r.Succeeded, r.Refusal?.ToString());
        Assert.True(tMap - tRead < 5000, $"parse {tParse} fake {tFake} json {tJson} read {tRead} map {tMap}");
    }

    // ---- background samples ----

    [Fact]
    public void Background_samples_attach_by_backend_id_or_snapshot_node()
    {
        (Document doc, FakeNode root) = Fake("<!DOCTYPE html><div id=a>x</div><div id=b>y</div>", LayoutVisible());
        RenderedFacts facts = Ok(Map(root, doc, new DomSnapshotMapOptions { ViewportWidth = 1280, ViewportHeight = 800 }));
        long backendA = facts[doc.Root.QuerySelector("#a")].BackendNodeId;
        int snapB = facts[doc.Root.QuerySelector("#b")].SnapshotNodeIndex;
        string json = $$"""
            {"format":"onehtml-background-samples","version":1,"viewport":{"width":1280,"height":800},
             "samples":[
               {"backendNodeId":{{backendA}},"median":"#003366","p5":"rgb(0, 30, 80)","p95":"#114477","pixels":4800},
               {"snapshotNode":{{snapB}},"median":"rgba(10, 20, 30, 0.5)"},
               {"backendNodeId":999999,"median":"#fff"}]}
            """;
        BackgroundSamples samples = BackgroundSamples.Parse(System.Text.Encoding.UTF8.GetBytes(json));
        BackgroundSampleApplication applied = facts.WithBackgroundSamples(samples);
        Assert.Equal(2, applied.Applied);
        Assert.Equal([2], applied.Unmatched);
        Assert.Null(facts[doc.Root.QuerySelector("#a")].Background);
        BackgroundSample a = applied.Facts[doc.Root.QuerySelector("#a")].Background!.Value;
        Assert.Equal(new Rgba(0, 0x33, 0x66, 1), a.Median);
        Assert.Equal(new Rgba(0, 30, 80, 1), a.P5);
        Assert.Equal(4800, a.PixelCount);
        BackgroundSample b = applied.Facts[doc.Root.QuerySelector("#b")].Background!.Value;
        Assert.Equal(0.5, b.Median.A);
        Assert.Equal(b.Median, b.P95);

        var wrongViewport = new BackgroundSamples(samples.Entries, 800, 600);
        Assert.Throws<ArgumentException>(() => facts.WithBackgroundSamples(wrongViewport));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"format\":\"onehtml-background-samples\",\"version\":2,\"samples\":[]}")]
    [InlineData("{\"format\":\"onehtml-background-samples\",\"version\":1,\"samples\":[{\"median\":\"#fff\"}]}")]
    [InlineData("{\"format\":\"onehtml-background-samples\",\"version\":1,\"samples\":[{\"backendNodeId\":1,\"median\":\"nope\"}]}")]
    [InlineData("{\"format\":\"onehtml-background-samples\",\"version\":1,\"samples\":[{\"backendNodeId\":1}]}")]
    [InlineData("[1,2")]
    public void Malformed_sample_files_fail_cleanly(string json)
    {
        Assert.Throws<BackgroundSamplesFormatException>(() => BackgroundSamples.Parse(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData("#fff", 255, 255, 255, 1.0)]
    [InlineData("#0f08", 0, 255, 0, 0.5333333333333333)]
    [InlineData("rgb(1, 2, 3)", 1, 2, 3, 1.0)]
    [InlineData("rgba(1,2,3,.25)", 1, 2, 3, 0.25)]
    [InlineData("rgb(1 2 3 / 50%)", 1, 2, 3, 0.5)]
    public void Colours_parse(string text, int r, int g, int b, double a)
    {
        Assert.True(Rgba.TryParse(text, out Rgba c));
        Assert.Equal(r, c.R);
        Assert.Equal(g, c.G);
        Assert.Equal(b, c.B);
        Assert.Equal(a, c.A, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("#gggggg")]
    [InlineData("rgb(1, 2)")]
    [InlineData("rgb(1, 2, 3, 4, 5)")]
    [InlineData("red")]
    public void Bad_colours_do_not_parse(string text)
    {
        Assert.False(Rgba.TryParse(text, out _));
    }
}
