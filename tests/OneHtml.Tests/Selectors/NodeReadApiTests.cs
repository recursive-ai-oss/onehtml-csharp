using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using RecursiveAI.OneHtml.Selectors;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Selectors;

public class NodeReadApiTests
{
    private static int s_sink;

    private const string Page = "<!DOCTYPE html><html lang=en><head><title>T</title></head><body><div id=a class='x  y'><p id=p1>one <b>two</b></p><!--c--><p id=p2 class=Y>three</p></div><div id=a><template id=t><i id=ti>in</i></template></div></body></html>";

    [Fact]
    public void Navigation_and_text()
    {
        Document d = Document.Parse(Page);
        Node a = d.GetElementById("a");
        Assert.Equal("DIV", a.TagName);
        Assert.Equal("div", a.LocalName);
        Assert.Equal("http://www.w3.org/1999/xhtml", a.NamespaceURI);
        Assert.Equal("x  y", a.ClassName);
        Assert.Equal(2, a.ChildElementCount);
        Assert.Equal(3, a.ChildNodes.Count());
        Assert.Equal("p1,p2", string.Join(",", a.Children.Select(c => c.Id)));
        Assert.Equal("p2", a.LastElementChild.Id);
        Assert.Equal("p2", d.GetElementById("p1").NextElementSibling.Id);
        Assert.True(d.GetElementById("p1").PreviousElementSibling.IsNull);
        Assert.Equal("body", a.ParentElement.LocalName);
        Assert.True(d.DocumentElement.ParentElement.IsNull);
        Assert.Equal("one twothree", a.TextContent);
        Assert.Null(d.Root.TextContent);
        Assert.Equal("c", a.ChildNodes.Skip(1).First().TextContent);
        Assert.Equal("en", d.DocumentElement.GetAttribute("LANG"));
        Assert.Equal("en", d.DocumentElement.GetAttribute(AttributeNamespace.None, "lang"));
        Assert.False(a.HasAttribute("nope"));
        Assert.Equal(["x", "y"], ClassTokens(a));
        Assert.True(a.ClassList.Contains("x"));
        Assert.False(a.ClassList.Contains("X"));
        Assert.Equal(2, a.ClassList.Count);
        Assert.Equal("y", a.ClassList[1]);
        Assert.NotNull(a.AsElement());
        Assert.Null(d.Root.AsElement());
        Assert.Equal("DIV", a.AsElement()!.Value.TagName);
    }

    private static string[] ClassTokens(Node n)
    {
        var l = new List<string>();
        foreach (ReadOnlySpan<char> t in n.ClassList)
        {
            l.Add(t.ToString());
        }

        return [.. l];
    }

    [Fact]
    public void Tree_relations()
    {
        Document d = Document.Parse(Page);
        Node a = d.GetElementById("a");
        Node p1 = d.GetElementById("p1");
        Node p2 = d.GetElementById("p2");
        Node inTemplate = d.GetElementById("ti");
        Assert.True(inTemplate.IsNull); // Template content is a separate tree.
        Node content = d.Root.QuerySelector("template").TemplateContent;
        Assert.False(content.IsConnected);
        Assert.True(content.FirstChild.GetRootNode() == content);
        Assert.True(p1.IsConnected);
        Assert.True(p1.GetRootNode() == d.Root);
        Assert.True(a.Contains(p1) && a.Contains(a) && !p1.Contains(a));
        Assert.False(a.Contains(content.FirstChild));
        Assert.Equal(DocumentPosition.None, p1.CompareDocumentPosition(p1));
        Assert.Equal(DocumentPosition.Following, p1.CompareDocumentPosition(p2));
        Assert.Equal(DocumentPosition.Preceding, p2.CompareDocumentPosition(p1));
        Assert.Equal(DocumentPosition.Contains | DocumentPosition.Preceding, p1.CompareDocumentPosition(a));
        Assert.Equal(DocumentPosition.ContainedBy | DocumentPosition.Following, a.CompareDocumentPosition(p1));
        Assert.True((p1.CompareDocumentPosition(content.FirstChild) & DocumentPosition.Disconnected) != 0);
        Document other = Document.Parse(Page);
        Assert.False(a.Contains(other.GetElementById("p1")));
        Assert.True((a.CompareDocumentPosition(other.GetElementById("a")) & DocumentPosition.Disconnected) != 0);
    }

    [Fact]
    public void Document_order_walks_and_lookups()
    {
        Document d = Document.Parse(Page);
        Assert.Equal("html head title body div p b p div template".Split(' '), d.Root.DescendantElements.Select(e => e.LocalName).ToArray());
        Assert.Equal("a", d.GetElementById("a").Id);
        Assert.True(d.GetElementById("a").Children.First() == d.GetElementById("p1"));
        Assert.True(d.GetElementById("").IsNull);
        Assert.True(d.GetElementById("A").IsNull);
        Assert.Equal(2, d.Root.GetElementsByTagName("DIV").Count);
        Assert.Equal(d.Root.DescendantElements.Count(), d.Root.GetElementsByTagName("*").Count);
        Assert.Equal("a", d.Root.GetElementsByClassName("y x").Single().Id);
        Assert.Empty(d.Root.GetElementsByClassName("  "));
        Assert.Single(d.Root.GetElementsByTagNameNS("http://www.w3.org/1999/xhtml", "title"));

        Document q = Document.Parse("<p class=Foo>1</p><p class=foo>2</p>");
        Assert.Equal(2, q.Root.GetElementsByClassName("FOO").Count); // quirks mode: ASCII case-insensitive
        Assert.Equal(2, q.Root.QuerySelectorAll(".fOo").Count);
        Document s = Document.Parse("<!DOCTYPE html><p class=Foo>1</p><p class=foo>2</p>");
        Assert.Single(s.Root.GetElementsByClassName("foo"));
    }

    [Fact]
    public void Closest_scope_and_sibling_selectors()
    {
        Document d = Document.Parse(Page);
        Node b = d.Root.QuerySelector("b");
        Assert.Equal("p1", b.Closest("p").Id);
        Assert.Equal("a", b.Closest("div > :scope").IsNull ? "a" : "x");
        Assert.Equal("p1", b.Closest(":has(> :scope)").Id);
        Assert.Equal("a", d.GetElementById("p1").QuerySelector(":scope").IsNull ? "a" : "x");
        Assert.True(d.Root.QuerySelector(":scope") == d.DocumentElement);
        Assert.Equal("p2", d.GetElementById("p1").Parent.QuerySelector(":scope > p + p").Id);
    }

    [Fact]
    public void Interaction_state()
    {
        Document d = Document.Parse(Page);
        var state = new InteractionState { Hover = d.Root.QuerySelector("b"), Focus = d.GetElementById("p2"), FocusVisible = true };
        var o = new SelectorMatchOptions { State = state };
        Assert.Equal("p1", string.Join(",", d.Root.QuerySelectorAll(SelectorList.Parse("p:hover"), o).Select(n => n.Id)));
        Assert.Equal("a", d.Root.QuerySelectorAll(SelectorList.Parse("div:hover, div:focus-within"), o).First().Id);
        Assert.Equal("p2", d.Root.QuerySelector(SelectorList.Parse(":focus-visible"), o).Id);
        Assert.True(d.Root.QuerySelector(":hover:focus").IsNull);
        Assert.True(d.Root.QuerySelector(":hover").IsNull);
    }

    [Fact]
    public void Navigation_and_matching_allocate_nothing()
    {
        Document d = Document.Parse(Page + string.Concat(Enumerable.Repeat("<p class=z>x</p>", 50)));
        SelectorList list = SelectorList.Parse("body > p.z:nth-child(odd), div p + p");
        var results = new List<Node>(200);
        Node body = d.Root.QuerySelector("body");

        long Measure(Action act)
        {
            act(); // warm: builds the lazy tables
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
            {
                act();
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Assert.Equal(0, Measure(() =>
        {
            int n = 0;
            foreach (Node c in body.Children)
            {
                n += c.FirstElementChild.IsNull ? 0 : 1;
                n += c.NextElementSibling.IsNull ? 0 : 1;
                n += c.ParentElement.IsNull ? 0 : 1;
            }

            foreach (Node e in d.Root.DescendantElements)
            {
                n += e.ClassList.Contains("z") ? 1 : 0;
            }

            s_sink = n;
        }));
        Assert.Equal(0, Measure(() => body.Matches(list)));
        Assert.Equal(0, Measure(() =>
        {
            results.Clear();
            d.Root.QuerySelectorAll(list, results);
        }));
        Assert.Equal(0, Measure(() => d.Root.QuerySelector(list)));
    }

    [Fact]
    public void A_snapshot_can_be_queried_from_many_threads_while_its_tables_are_built()
    {
        Document d = Document.Parse("<!DOCTYPE html>" + string.Concat(Enumerable.Range(0, 400).Select(i => $"<div id=i{i} class=c{i % 5}><p>{i}<p>x</div>")));
        string[] selectors = ["#i77", "div:nth-child(3n+1) > p:last-child", "p:nth-of-type(2)", "div:has(> p)", ".c3 + .c4", "input:valid, :lang(en)"];
        int[] expected = selectors.Select(s => d.Root.QuerySelectorAll(s).Count).ToArray();
        Document fresh = Document.Parse("<!DOCTYPE html>" + string.Concat(Enumerable.Range(0, 400).Select(i => $"<div id=i{i} class=c{i % 5}><p>{i}<p>x</div>")));
        Parallel.For(0, 64, i =>
        {
            string s = selectors[i % selectors.Length];
            Assert.Equal(expected[i % selectors.Length], fresh.Root.QuerySelectorAll(s).Count);
            Assert.Equal("i77", fresh.GetElementById("i77").Id);
        });
    }

    [Fact]
    public void Bad_selector_input_throws_the_typed_exception()
    {
        Document d = Document.Parse(Page);
        Assert.Throws<SelectorSyntaxException>(() => d.Root.QuerySelector("p >"));
        Assert.Throws<ArgumentNullException>(() => d.Root.QuerySelector((string)null!));
        Assert.False(d.Root.Matches("p"));
    }

    // ---- limits and complexity ----

    [Fact]
    public void Selector_limits_are_checked_before_the_work_they_guard()
    {
        Assert.Throws<SelectorLimitExceededException>(() => SelectorList.Parse(new string('a', 70_000)));
        Assert.Throws<SelectorLimitExceededException>(() => SelectorList.Parse(string.Concat(Enumerable.Repeat(":is(", 17)) + "a" + new string(')', 17)));
        SelectorList.Parse(string.Concat(Enumerable.Repeat(":is(", 16)) + "a" + new string(')', 16));
        Assert.Throws<SelectorLimitExceededException>(() => SelectorList.Parse(string.Join(" ", Enumerable.Repeat("a", 129))));
        SelectorList.Parse(string.Join(" ", Enumerable.Repeat("a", 128)));
        Assert.Throws<SelectorLimitExceededException>(() => SelectorList.Parse(string.Join(",", Enumerable.Repeat("a", 4097))));
        Assert.Throws<SelectorLimitExceededException>(() => SelectorList.Parse(string.Concat(Enumerable.Repeat(".a", 17_000))));
        // Brackets nested far past the CSS parser's own bound.
        Assert.False(SelectorList.TryParse(new string('(', 60_000), out _));
        Assert.False(SelectorList.TryParse(string.Concat(Enumerable.Repeat(":not(", 50_000)), out _));
        Assert.False(SelectorList.IsValid(string.Concat(Enumerable.Repeat("[", 50_000))));
        var tight = new SelectorLimits { MaxLength = 10 };
        Assert.Throws<SelectorLimitExceededException>(() => SelectorList.Parse("abcdefghijk", new SelectorParseOptions { Limits = tight }));
    }

    [Fact]
    public void Backtracking_combinators_stay_polynomial()
    {
        // A chain of descendant combinators that fails at the far left would try every way of picking ancestors.
        string deep = "<!DOCTYPE html>" + string.Concat(Enumerable.Repeat("<div>", 1500)) + "<p>x</p>";
        Document d = Document.Parse(deep);
        var sw = Stopwatch.StartNew();
        Assert.Empty(d.Root.QuerySelectorAll("x div div div div div div div div div div p"));
        Assert.Empty(d.Root.QuerySelectorAll("section div > div div > div div div p:not(:first-child)"));
        Assert.Single(d.Root.QuerySelectorAll("div div div div div div div div div div p"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), sw.Elapsed.ToString());

        string flat = "<!DOCTYPE html><ul>" + string.Concat(Enumerable.Repeat("<li>x", 5000)) + "</ul>";
        Document f = Document.Parse(flat);
        sw.Restart();
        Assert.Empty(f.Root.QuerySelectorAll("em ~ li ~ li ~ li ~ li ~ li"));
        Assert.Equal(4996, f.Root.QuerySelectorAll("li ~ li ~ li ~ li ~ li").Count);
        Assert.Equal(5000, f.Root.QuerySelectorAll("li:nth-child(n)").Count);
        Assert.Equal(2500, f.Root.QuerySelectorAll("li:nth-child(2n of li)").Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), sw.Elapsed.ToString());
    }

    [Fact]
    public void Has_is_linear_on_deep_trees()
    {
        Document d = Document.Parse("<!DOCTYPE html>" + string.Concat(Enumerable.Repeat("<div>", 3000)) + "<p>x</p>");
        var sw = Stopwatch.StartNew();
        Assert.Equal(3000, d.Root.QuerySelectorAll("div:has(p)").Count);
        Assert.Equal(3000, d.Root.QuerySelectorAll("div:has(> div, > p)").Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), sw.Elapsed.ToString());
    }
}
