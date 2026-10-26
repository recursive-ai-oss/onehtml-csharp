using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Selectors;

/// <summary>
/// One-off queries (<c>Matches</c>, <c>QuerySelector(All)</c>, <c>Closest</c>) over a deep tree with :is()/:where()/:not()
/// nested inside each other. Each level asks the same question of every ancestor again, so without a bound the cost is the
/// depth raised to the nesting (Selectors 4 section 4.3).
/// </summary>
public class NestedIsQueryCostTests
{
    private const int Depth = 150;

    private static Document Deep(bool withTarget)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Depth; i++)
        {
            sb.Append(i == 0 && withTarget ? "<div class=x>" : "<div>");
        }

        sb.Append("<span id=leaf></span>");
        return Document.Parse(sb.ToString());
    }

    // Never matches when the document has no .x, so every ancestor is tried at every level.
    private const string Nested = ":is(:is(:is(:is(.x) *) *) *) *";

    // The bound catches exponential matching, which takes minutes; these queries take about 0.1 s alone,
    // so the headroom is for a busy machine running every test assembly at once.
    private static void Within(TimeSpan limit, Action work)
    {
        var sw = Stopwatch.StartNew();
        work();
        Assert.True(sw.Elapsed < limit, sw.Elapsed.ToString());
    }

    [Fact]
    public void Matches_with_nested_is_over_deep_ancestors_is_bounded()
    {
        Document doc = Deep(false);
        Node leaf = doc.Root.QuerySelector("#leaf");
        Within(TimeSpan.FromSeconds(5), () => Assert.False(leaf.Matches(Nested)));
    }

    [Fact]
    public void Nested_not_and_where_are_bounded()
    {
        Document doc = Deep(true);
        Node leaf = doc.Root.QuerySelector("#leaf");
        Within(TimeSpan.FromSeconds(5), () =>
        {
            _ = leaf.Matches(":not(:not(:not(:not(:is(*) *) *) *) *) *");
            Assert.True(leaf.Matches(":where(:where(:where(.x) *) *) *"));
            Assert.False(leaf.Matches(":is(:is(:is(:is(.y) *) *) *) *"));
        });
    }

    [Fact]
    public void QuerySelectorAll_and_Closest_with_nested_is_are_bounded()
    {
        Document doc = Deep(false);
        Node leaf = doc.Root.QuerySelector("#leaf");
        Within(TimeSpan.FromSeconds(5), () =>
        {
            Assert.Empty(doc.Root.QuerySelectorAll(Nested));
            Assert.True(leaf.Closest(Nested).IsNull);
        });
    }

    [Fact]
    public void Nested_is_answers_agree_with_the_flat_selector()
    {
        // The nested form and the flattened descendant chain select the same elements.
        Document doc = Deep(true);
        string[] nested = doc.Root.QuerySelectorAll(":is(:is(:is(.x *) *) *) *").Select(n => n.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        string[] flat = doc.Root.QuerySelectorAll(".x * * * *").Select(n => n.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(flat, nested);
        Assert.NotEmpty(nested);
    }
}
