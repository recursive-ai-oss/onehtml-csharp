using System;
using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>The direct builder: same facts as the DOMSnapshot adapter for the same values, and the same refusals.</summary>
public sealed class RenderedFactsBuilderTests
{
    public static IEnumerable<TheoryDataRow<string>> Pages() => FactsCopy.Pages().Select(p => new TheoryDataRow<string>(p));

    [Theory]
    [MemberData(nameof(Pages))]
    public void Builder_from_the_adapters_values_gives_identical_facts_and_trees(string page)
    {
        (Document doc, RenderedFacts adapter) = FactsCopy.Load(page);
        RenderedFacts built = FactsCopy.Rebuild(doc, adapter);
        Assert.Equal(FactsCopy.Dump(doc, adapter), FactsCopy.Dump(doc, built));

        DocumentStyles styles = DocumentStyles.Create(doc);
        string a = AccessibilityTree.Build(doc, styles, null, new AccessibilityOptions { Facts = adapter }).ToChromeJson();
        string b = AccessibilityTree.Build(doc, styles, null, new AccessibilityOptions { Facts = built }).ToChromeJson();
        Assert.Equal(a, b);
    }

    /// <summary>The same comparison over a store of corpus captures, when <c>ONEHTML_CAPTURE_DIR</c> names one (skipped otherwise).</summary>
    [Fact]
    public void Builder_equals_adapter_on_stored_corpus_captures()
    {
        List<string> dirs = FactsCopy.StoreDirs();
        Assert.SkipWhen(dirs.Count == 0, "ONEHTML_CAPTURE_DIR is not set.");
        int compared = 0, refused = 0, boxes = 0, generated = 0;
        foreach (string dir in dirs)
        {
            if (FactsCopy.LoadDir(dir, requireSuccess: false) is not { } cap)
            {
                refused++;
                continue;
            }

            RenderedFacts built = FactsCopy.Rebuild(cap.Doc, cap.Facts);
            Assert.True(FactsCopy.Dump(cap.Doc, cap.Facts) == FactsCopy.Dump(cap.Doc, built), dir);
            DocumentStyles styles = DocumentStyles.Create(cap.Doc);
            string a = AccessibilityTree.Build(cap.Doc, styles, null, new AccessibilityOptions { Facts = cap.Facts }).ToChromeJson();
            string b = AccessibilityTree.Build(cap.Doc, styles, null, new AccessibilityOptions { Facts = built }).ToChromeJson();
            Assert.True(a == b, dir);
            compared++;
            boxes += cap.Facts.BoxCount;
            generated += cap.Facts.GeneratedBoxCount;
        }

        FactsCopy.Report($"facts-tree captures={dirs.Count} compared={compared} refused={refused} boxes={boxes} generated={generated}");
        Assert.True(compared > 0);
    }

    private static (Document Doc, RenderedFactsBuilder B) Small()
    {
        Document doc = Document.Parse("<!doctype html><div id=a><p id=b>x</p></div>");
        return (doc, new RenderedFactsBuilder(doc));
    }

    [Fact]
    public void Boxes_stacking_contexts_and_node_state_read_back()
    {
        (Document doc, RenderedFactsBuilder b) = Small();
        Node a = doc.GetElementById("a"), p = doc.GetElementById("b");
        b.SetComputedStyleNames(["display", "color"]);
        b.SetViewport(800, 600);
        b.AddBox(a, new BoxInput { BorderBox = new LayoutRect(1, 2, 3, 4), IsStackingContext = true, PaintOrder = 2, Styles = ["block", null] });
        b.AddBox(p, new BoxInput { BorderBox = new LayoutRect(5, 6, 7, 8), TextBoxes = [new TextBox(0, 1, new LayoutRect(5, 6, 7, 8))], LayoutText = "x", ClientRect = new LayoutRect(0, 0, 7, 8) });
        b.SetChecked(p, true);
        b.SetInputValue(p, "v");
        RenderedFacts f = b.Build();
        Assert.Equal(800, f.ViewportWidth);
        Assert.Equal(2, f.BoxCount);
        Assert.Equal(a.Index, f[p].StackingContextNodeIndex);
        Assert.Equal(a.Index, f[a].StackingContextNodeIndex);
        Assert.Equal(-1, f[doc.Root].StackingContextNodeIndex);
        Assert.Equal("block", f[a].GetStyle("display"));
        Assert.Null(f[a].GetStyle("color"));
        Assert.Null(f[a].Layout.ClientRect);
        Assert.Equal(new LayoutRect(0, 0, 7, 8), f[p].Layout.ClientRect);
        Assert.Equal("x", f[p].Layout.LayoutText);
        Assert.Single(f[p].Layout.TextBoxes.ToArray());
        Assert.True(f[p].InputChecked);
        Assert.False(f[a].InputChecked);
        Assert.Equal("v", f[p].InputValue);
        Assert.Null(f[a].Layout.TextColorOpacity);
        Assert.Null(f[a].Layout.OffsetRect);
    }

    [Fact]
    public void Nodes_of_another_document_and_null_handles_are_refused()
    {
        (Document doc, RenderedFactsBuilder b) = Small();
        Document other = Document.Parse("<!doctype html><div id=a><p id=b>x</p></div>");
        Assert.Throws<ArgumentException>(() => b.AddBox(other.GetElementById("a"), new BoxInput()));
        Assert.Throws<ArgumentException>(() => b.AddBox(default, new BoxInput()));
        Assert.Throws<ArgumentException>(() => b.SetChecked(other.Root, true));
        Assert.Throws<ArgumentException>(() => b.SetFrame(default, 0));
        Assert.Equal(0, b.BoxCount);
        Assert.Equal(doc.NodeCount, b.Build().NodeCount);
    }

    [Fact]
    public void Malformed_boxes_are_refused_and_leave_no_trace()
    {
        (Document doc, RenderedFactsBuilder b) = Small();
        Node a = doc.GetElementById("a");
        b.SetComputedStyleNames(["display"]);
        Assert.Throws<ArgumentException>(() => b.AddBox(a, new BoxInput { Styles = ["a", "b"] }));
        Assert.Throws<ArgumentException>(() => b.AddBox(a, new BoxInput { BorderBox = new LayoutRect(double.PositiveInfinity, 0, 1, 1) }));
        Assert.Throws<ArgumentException>(() => b.AddBox(a, new BoxInput { PaintOrder = -3 }));
        Assert.Throws<ArgumentException>(() => b.AddBox(a, new BoxInput { TextColorOpacity = double.PositiveInfinity }));
        Assert.Throws<ArgumentException>(() => b.AddBox(a, new BoxInput { TextBoxes = [new TextBox(-1, 1, default)] }));
        Assert.Throws<ArgumentException>(() => b.AddBox(a, new BoxInput { TextBoxes = [new TextBox(int.MaxValue, 5, default)] }));
        Assert.Equal(0, b.BoxCount);
        b.AddBox(a, new BoxInput { Styles = ["block"] });
        Assert.Throws<ArgumentException>(() => b.AddBox(a, new BoxInput { Styles = ["block"] }));
        Assert.Throws<InvalidOperationException>(() => b.SetComputedStyleNames(["x"]));
        Assert.Throws<ArgumentException>(() => b.AddGeneratedContent(a, new GeneratedBoxInput("before") { Boxes = [new BoxInput { Styles = ["x"] }], LayoutIndex = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.SetViewport(-1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.SetViewport(double.NaN, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => b.SetFrame(a, -1));
    }

    [Fact]
    public void Limits_are_checked_before_the_data_is_stored()
    {
        Document doc = Document.Parse("<!doctype html><div id=a><p id=b>x</p></div>");
        Node a = doc.GetElementById("a"), p = doc.GetElementById("b");

        var boxes = new RenderedFactsBuilder(doc, new RenderedFactsLimits { MaxBoxes = 1 });
        boxes.AddBox(a, new BoxInput());
        RenderedFactsLimitException e = Assert.Throws<RenderedFactsLimitException>(() => boxes.AddBox(p, new BoxInput()));
        Assert.Equal(nameof(RenderedFactsLimits.MaxBoxes), e.LimitName);
        Assert.Equal(1, boxes.BoxCount);
        Assert.Throws<RenderedFactsLimitException>(() => boxes.AddGeneratedContent(p, new GeneratedBoxInput("before") { Boxes = [new BoxInput(), new BoxInput()] }));

        var tb = new RenderedFactsBuilder(doc, new RenderedFactsLimits { MaxTextBoxes = 2 });
        TextBox t = new(0, 1, default);
        Assert.Throws<RenderedFactsLimitException>(() => tb.AddBox(a, new BoxInput { TextBoxes = [t, t, t] }));
        Assert.Equal(0, tb.BoxCount);
        tb.AddBox(a, new BoxInput { TextBoxes = [t, t] });

        var chars = new RenderedFactsBuilder(doc, new RenderedFactsLimits { MaxStringChars = 5 });
        chars.AddBox(a, new BoxInput { LayoutText = "12345" });
        Assert.Throws<RenderedFactsLimitException>(() => chars.AddBox(p, new BoxInput { LayoutText = "123456" }));
        chars.AddBox(p, new BoxInput { LayoutText = "12345" }); // a string already held costs nothing again

        var strings = new RenderedFactsBuilder(doc, new RenderedFactsLimits { MaxStrings = 1 });
        strings.SetInputValue(a, "x");
        Assert.Throws<RenderedFactsLimitException>(() => strings.SetInputValue(p, "y"));

        var names = new RenderedFactsBuilder(doc, new RenderedFactsLimits { MaxComputedStyleNames = 1 });
        Assert.Throws<RenderedFactsLimitException>(() => names.SetComputedStyleNames(["a", "b"]));
    }

    [Fact]
    public void Generated_content_is_ordered_by_host_then_by_the_order_added()
    {
        (Document doc, RenderedFactsBuilder b) = Small();
        Node a = doc.GetElementById("a"), p = doc.GetElementById("b");
        b.AddGeneratedContent(p, new GeneratedBoxInput("after") { Text = "2", Boxes = [new BoxInput()], LayoutIndex = 0 });
        b.AddGeneratedContent(a, new GeneratedBoxInput("before") { Text = "1", NodeCount = 3 });
        b.AddGeneratedContent(p, new GeneratedBoxInput("before") { Text = "0" });
        RenderedFacts f = b.Build();
        Assert.Equal(3, f.GeneratedBoxCount);
        Assert.Equal(["before"], Types(f[a]));
        Assert.Equal(["after", "before"], Types(f[p]));
        Assert.True(f[p].GeneratedBoxes[0].HasLayout);
        Assert.False(f[p].GeneratedBoxes[1].HasLayout);
        Assert.Equal(3, f[a].GeneratedBoxes[0].SnapshotNodeCount);
        static string[] Types(NodeFacts n) => [.. System.Linq.Enumerable.Select(n.GeneratedBoxes, g => g.PseudoType)];
    }

    [Fact]
    public void Build_can_be_repeated_and_results_are_independent()
    {
        (Document doc, RenderedFactsBuilder b) = Small();
        Node a = doc.GetElementById("a");
        b.AddBox(a, new BoxInput());
        RenderedFacts one = b.Build();
        b.SetChecked(a, true);
        RenderedFacts two = b.Build();
        Assert.False(one[a].InputChecked);
        Assert.True(two[a].InputChecked);
    }

    [Fact]
    public void Deep_documents_do_not_recurse()
    {
        Document doc = Document.Parse("<!doctype html>" + string.Concat(System.Linq.Enumerable.Repeat("<div>", 400)) + "x");
        var b = new RenderedFactsBuilder(doc);
        b.AddBox(doc.GetNode(doc.NodeCount - 1), new BoxInput { IsStackingContext = true });
        Assert.Equal(doc.NodeCount, b.Build().NodeCount);
    }
}
