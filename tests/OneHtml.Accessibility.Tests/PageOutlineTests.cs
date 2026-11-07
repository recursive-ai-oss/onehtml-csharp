using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>The outline of a page (landmarks and headings found without the tree) equals the navigation lists of the full tree.</summary>
public sealed class PageOutlineTests
{
    private static string Describe(NavigationLists lists)
    {
        IEnumerable<string> landmarks = lists.Landmarks.Select(l => $"L {l.Node.DomNode.Index} {l.Role} '{l.Name}' {l.Depth}");
        IEnumerable<string> headings = lists.Headings.Select(h => $"H {h.Node.DomNode.Index} {h.Level} '{h.Name}' {h.Lang}");
        return string.Join('\n', landmarks.Concat(headings));
    }

    private static string Describe(PageOutline outline)
    {
        IEnumerable<string> landmarks = outline.Landmarks.Select(l => $"L {l.Node.Index} {l.Role} '{l.Name}' {l.Depth}");
        IEnumerable<string> headings = outline.Headings.Select(h => $"H {h.Node.Index} {h.Level} '{h.Name}' {h.Lang}");
        return string.Join('\n', landmarks.Concat(headings));
    }

    private static void AssertSame(string html, string label)
    {
        Document doc = Document.Parse(html);
        DocumentStyles styles = DocumentStyles.Create(doc);
        string expected = Describe(AccessibilityTree.Build(doc, styles).CreateNavigationLists());
        Assert.True(expected == Describe(PageOutline.Create(doc, styles)), label);

        // Without heading names the same elements are listed, with empty names.
        PageOutline bare = PageOutline.Create(doc, DocumentStyles.Create(doc), includeHeadingNames: false);
        Assert.False(bare.HasHeadingNames);
        Assert.All(bare.Headings, h => Assert.Equal(string.Empty, h.Name));
        PageOutline named = PageOutline.Create(doc, styles);
        Assert.True(named.HasHeadingNames);
        Assert.Equal(named.Headings.Select(h => (h.Node, h.Level, h.Lang)), bare.Headings.Select(h => (h.Node, h.Level, h.Lang)));
        Assert.Equal(named.Landmarks, bare.Landmarks);
    }

    [Fact]
    public void Lists_landmarks_and_headings_in_tree_order_with_depth_and_names()
    {
        Document doc = Document.Parse("<!doctype html><html lang=en><body><header><h1>Site</h1></header><nav aria-label='Main'><h2 lang=fr>Menu</h2></nav>"
            + "<main><section aria-label='News'><h3 aria-level=5>Today</h3></section><section><h3>Plain</h3></section></main><footer>x</footer></body></html>");
        PageOutline o = PageOutline.Create(doc, DocumentStyles.Create(doc));
        Assert.Equal(["banner", "navigation", "main", "region", "contentinfo"], o.Landmarks.Select(l => l.Role));
        Assert.Equal([0, 0, 0, 1, 0], o.Landmarks.Select(l => l.Depth));
        Assert.Equal(["Main", "News"], o.Landmarks.Where(l => l.Name.Length > 0).Select(l => l.Name));
        Assert.Equal([(1, "Site", "en"), (2, "Menu", "fr"), (5, "Today", "en"), (3, "Plain", "en")], o.Headings.Select(h => (h.Level, h.Name, h.Lang)));
    }

    [Theory]
    [InlineData("<main id=a aria-owns=b></main><div id=b><nav aria-label=x><h2>t</h2></nav></div>")]
    [InlineData("<div aria-owns=h><main></main></div><section><h2 id=h>Owned</h2></section>")]
    [InlineData("<div hidden id=h><h2>no</h2></div><main aria-owns=h><h1>yes</h1></main>")]
    [InlineData("<textarea><h2 id=h>no</h2></textarea><main aria-owns=h></main>")]
    [InlineData("<main aria-hidden=true><nav aria-label=n><h2>a</h2></nav></main><h2 style='visibility:hidden'>b</h2><h2 style='visibility:hidden'><span style='visibility:visible'>c</span></h2>")]
    [InlineData("<main style='visibility:hidden'><nav aria-label=n style='visibility:visible'><h2>a</h2></nav></main>")]
    [InlineData("<div role=listbox><h2 role=option>x</h2></div><h2 role=option>y</h2><div role=option><h3>z</h3></div>")]
    [InlineData("<button><h2>in button</h2></button><div role=button><nav aria-label=q></nav></div>")]
    [InlineData("<ul role=menu><li role=heading>x</li></ul><table role=none><tr><td><h2>cell</h2></td></tr></table>")]
    [InlineData("<section>unnamed</section><section aria-label=named><form></form><form name=f aria-label=F></form></section><aside><aside>nested</aside></aside>")]
    [InlineData("<h2 role=presentation>p</h2><h2 role=none>n</h2><div role=heading aria-level=0>d</div><div role=heading aria-level=-3>e</div><div role='heading main'>f</div>")]
    [InlineData("<main><main><main></main></main></main><div role=search><div role=search><h1 aria-labelledby=l>t</h1></div></div><p id=l>label</p>")]
    [InlineData("<details><summary><h2>s</h2></summary><nav aria-label=n></nav></details><details open><summary>x</summary><h3>open</h3></details>")]
    [InlineData("<svg><title><h2>x</h2></title><foreignObject><h2>in svg</h2></foreignObject></svg><math><mi>x</mi></math><template><h2>t</h2></template>")]
    [InlineData("<div aria-owns='a b c'></div><h2 id=a>A</h2><nav id=b aria-label=B><h3 id=c aria-owns=a>C</h3></nav>")]
    public void Matches_the_full_tree_on_hand_written_markup(string markup) => AssertSame("<!doctype html><title>t</title><body>" + markup, markup);

    [Fact]
    public void Matches_the_full_tree_on_every_realistic_page() => Assert.All(RealisticPages.All, p => AssertSame(p.Html, p.Name));

    [Fact]
    public void Matches_the_full_tree_on_every_test_data_page()
    {
        string[] files = [.. Directory.EnumerateFiles(TestPaths.TestData, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".html", StringComparison.Ordinal) || f.EndsWith(".htm", StringComparison.Ordinal) || f.EndsWith(".xhtml", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
        Assert.True(files.Length > 1000);
        int checkedPages = 0;
        foreach (string file in files)
        {
            if (new FileInfo(file).Length > 2_000_000)
            {
                continue;
            }

            AssertSame(File.ReadAllText(file), file);
            checkedPages++;
        }

        Assert.True(checkedPages > 1000);
    }

    [Fact]
    public void Can_be_made_and_read_from_many_threads_at_once()
    {
        string html = RealisticPages.All[0].Html;
        Document doc = Document.Parse(html);
        DocumentStyles styles = DocumentStyles.Create(doc);
        string expected = Describe(AccessibilityTree.Build(doc, styles).CreateNavigationLists());
        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            // Both the shared styles and fresh ones, which race on the shared user-agent rules.
            DocumentStyles s = i % 2 == 0 ? styles : DocumentStyles.Create(doc);
            Assert.Equal(expected, Describe(PageOutline.Create(doc, s)));
        });
    }

    [Fact]
    public void Rejects_bad_arguments()
    {
        Document doc = Document.Parse("<p>x");
        Document other = Document.Parse("<p>y");
        Assert.Throws<ArgumentNullException>(() => PageOutline.Create(null!, DocumentStyles.Create(doc)));
        Assert.Throws<ArgumentNullException>(() => PageOutline.Create(doc, null!));
        Assert.Throws<ArgumentException>(() => PageOutline.Create(doc, DocumentStyles.Create(other)));
        Assert.Throws<OperationCanceledException>(() => PageOutline.Create(doc, DocumentStyles.Create(doc), cancellationToken: new System.Threading.CancellationToken(true)));
    }

    [Fact]
    public void Deeply_nested_pages_do_not_overflow_the_stack()
    {
        string html = string.Concat(Enumerable.Repeat("<div>", 3000)) + "<h1>deep</h1><main></main>";
        Document doc = Document.Parse(html);
        PageOutline o = PageOutline.Create(doc, DocumentStyles.Create(doc));
        Assert.Single(o.Headings);
        Assert.Single(o.Landmarks);
    }
}
