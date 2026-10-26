using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>Deep, wide and self-referential input: the tree must build quickly and never recurse on the depth of the input.</summary>
public sealed class TreeHostileTests
{
    private static Document Parse(string html) =>
        Document.Parse(html, new ParseOptions { Limits = new ParseLimits { MaxOpenElements = 300_000, MaxNodes = 10_000_000 } });

    private static AccessibilityTree Build(Document doc, AccessibilityOptions? options = null) =>
        AccessibilityTree.Build(doc, DocumentStyles.Create(doc), null, options);

    private static void Within(int ms, Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        Assert.True(sw.ElapsedMilliseconds < ms, "took " + sw.ElapsedMilliseconds + " ms");
    }

    /// <summary>
    /// Runs <paramref name="work"/> on an input of <c>n</c> and of <c>4n</c> units (best of three after a warm-up, so JIT time is
    /// not counted) and requires the cost to grow far less than quadratically: linear work takes about 4 times as long, quadratic
    /// work 16 times. A ratio tells a slow machine from a regression where a fixed time cannot. The absolute bound only guards a hang.
    /// </summary>
    private static void ScalesLinearly(int n, Func<int, Action> work)
    {
        work(n / 8)();
        double Best(int size)
        {
            Action run = work(size);
            double best = double.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                var sw = Stopwatch.StartNew();
                run();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }

            return best;
        }

        double small = Best(n);
        double large = Best(4 * n);
        Assert.True(large < 150 + 8 * small, $"{n} units took {small:F0} ms, {4 * n} took {large:F0} ms");
        Assert.True(large < 120_000, $"{large:F0} ms");
    }

    [Fact]
    public void Deeply_nested_elements()
    {
        // Styling and building the tree once walked every element's ancestors, which made depth quadratic.
        ScalesLinearly(25_000, n =>
        {
            string html = string.Concat(Enumerable.Repeat("<div>", n)) + "<button id=b>deep</button>";
            Document doc = Parse(html);
            return () =>
            {
                AccessibilityTree tree = Build(doc);
                Assert.Equal("deep", tree.Find(doc.Root.QuerySelector("#b"))!.Name.Value);
                Assert.True(tree.Nodes.Count > n);
                Assert.NotEmpty(tree.ToChromeJson());
            };
        });
    }

    [Fact]
    public void Deep_chain_of_nested_headings_is_bounded()
    {
        string html = string.Concat(Enumerable.Repeat("<div role=heading>x", 20_000));
        Document doc = Parse(html);
        Within(30_000, () =>
        {
            AccessibilityTree tree = Build(doc, new AccessibilityOptions { MaxNameWork = 2_000_000 });
            Assert.True(tree.LimitReached, "nodes=" + tree.Nodes.Count + " name=" + tree.Nodes.First(n => n.Role == "heading").Name.Value.Length + " diag=" + tree.Diagnostics.Count);
            Assert.Contains(tree.Diagnostics, d => d.Kind == AxDiagnosticKind.LimitExceeded);
        });
    }

    [Fact]
    public void Long_labelledby_chain_and_cycles()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 20_000; i++)
        {
            sb.Append("<div id=n").Append(i).Append(" role=group aria-labelledby=n").Append((i + 1) % 20_000).Append(">t").Append(i).Append("</div>");
        }

        Document doc = Parse(sb.ToString());
        Within(20_000, () =>
        {
            AccessibilityTree tree = Build(doc);
            Assert.Equal(20_000, tree.Nodes.Count(n => n.Role == "group"));
        });
    }

    [Fact]
    public void One_attribute_naming_thousands_of_ids()
    {
        var ids = new StringBuilder();
        var els = new StringBuilder();
        for (int i = 0; i < 50_000; i++)
        {
            ids.Append('x').Append(i).Append(' ');
            els.Append("<i id=x").Append(i).Append(">a</i>");
        }

        Document doc = Parse("<button id=b aria-labelledby='" + ids + "' aria-describedby='" + ids + "' aria-owns='" + ids + "'>b</button>" + els);
        Within(20_000, () =>
        {
            AccessibilityTree tree = Build(doc, new AccessibilityOptions { MaxIdReferences = 1000 });
            Assert.True(tree.Find(doc.Root.QuerySelector("#b"))!.Name.Value.Length < 5000);
        });
    }

    [Fact]
    public void Owner_chain_and_ancestor_checks_stay_bounded()
    {
        // Each element owns the next, which is also its DOM child's cousin: many ancestor walks over a deep tree.
        var sb = new StringBuilder();
        const int N = 20_000;
        for (int i = 0; i < N; i++)
        {
            sb.Append("<div id=d").Append(i).Append(" aria-owns=e").Append(i).Append('>');
        }

        for (int i = 0; i < N; i++)
        {
            sb.Append("<p id=e").Append(i).Append(">p</p>");
        }

        Document doc = Parse(sb.ToString());
        Within(30_000, () =>
        {
            AccessibilityTree tree = Build(doc, new AccessibilityOptions { MaxOwnsWork = 5_000_000 });
            Assert.True(tree.Nodes.Count > N);
            // The result is still a tree: every node is reached once.
            Assert.Equal(tree.Nodes.Count, tree.Nodes.Select(n => n.Id).Distinct().Count());
        });
    }

    [Fact]
    public void A_hundred_thousand_nodes()
    {
        var sb = new StringBuilder("<main>");
        for (int i = 0; i < 20_000; i++)
        {
            sb.Append("<section aria-label=s").Append(i).Append("><h2>h").Append(i).Append("</h2><p>text <a href=#>link</a></p></section>");
        }

        sb.Append("</main>");
        Document doc = Parse(sb.ToString());
        Within(20_000, () =>
        {
            AccessibilityTree tree = Build(doc);
            Assert.True(tree.Nodes.Count > 100_000);
            Assert.Equal(20_000, tree.Nodes.Count(n => n.Role == "region"));
        });
    }

    [Fact]
    public void Huge_text_in_a_name_is_cut()
    {
        string html = "<button id=b>" + new string('a', 3_000_000) + "</button>";
        Document doc = Parse(html);
        AccessibilityTree tree = Build(doc, new AccessibilityOptions { MaxNameLength = 1000 });
        Assert.Equal(1000, tree.Find(doc.Root.QuerySelector("#b"))!.Name.Value.Length);
    }

    [Fact]
    public void Updating_a_page_of_nested_labels_stays_bounded_and_correct()
    {
        string html = string.Concat(Enumerable.Repeat("<label>x", 40_000)) + "<input id=i>";
        Document doc = Parse(html);
        DocumentStyles styles = DocumentStyles.Create(doc);
        AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
        Editing.EditResult edit = doc.Apply([Editing.Edit.SetAttribute(doc.Root.QuerySelector("#i"), "title", "t")]);
        Assert.True(edit.Succeeded, edit.Failure);
        Within(30_000, () =>
        {
            AccessibilityTree next = tree.Update(edit, styles.Restyle(edit));
            Assert.False(next.UpdateInfo!.Incremental);
            Assert.Contains("work bound", next.UpdateInfo.FullRebuildReason);
            Assert.Equal(AccessibilityTree.Build(edit.Document!, DocumentStyles.Create(edit.Document!)).Nodes.Count, next.Nodes.Count);
        });
    }

    [Fact]
    public void Reading_view_and_lists_of_a_huge_table_and_deep_nesting_are_bounded()
    {
        ScalesLinearly(5_000, n =>
        {
            string rows = string.Concat(Enumerable.Repeat("<tr><th>h<td>a<td>b<td>c</tr>", n / 2));
            Document doc = Parse("<table>" + rows + "</table>" + string.Concat(Enumerable.Repeat("<ul><li>", n)) + "end");
            AccessibilityTree tree = Build(doc);
            return () =>
            {
                ReadingView view = tree.CreateReadingView();
                Assert.True(view.Items.Count > 2 * n);
                NavigationLists nav = tree.CreateNavigationLists();
                Assert.Single(nav.Tables);
                Assert.Equal(n / 2, nav.Tables[0].Rows);
                Assert.True(tree.Diff(tree).IsEmpty);
            };
        });
    }
}
