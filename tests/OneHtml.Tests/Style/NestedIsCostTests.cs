using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// CSS nesting desugars to <c>:is()</c> of the parent selector, so a descendant combinator at each of several nesting levels
/// asks the same question of the same ancestors again from every element below (Selectors 4 section 4.3; CSS Nesting 1
/// section 3.2). The answers are kept per element, so a deep tree costs a quadratic bound rather than depth to the nesting.
/// </summary>
public class NestedIsCostTests
{
    [Fact]
    public void Nested_descendant_rules_over_a_deep_tree_are_cheap_and_correct()
    {
        const int depth = 80;
        // The shape CSS nesting produces: the parent selector reappears inside :is() at each level.
        var html = new StringBuilder("<style>* { & * { & :nth-child(3n of foo, bar, target) { & * { & * { & * { background-color: red } } } } }</style>");
        for (int i = 0; i < depth; i++)
        {
            html.Append("<foo></foo><foo></foo><foo></foo><foo></foo><foo></foo><foo></foo><foo></foo><foo></foo><foo></foo><target>");
        }

        Document doc = Document.Parse(html.ToString());
        var sw = Stopwatch.StartNew();
        DocumentStyles styles = DocumentStyles.Create(doc);
        int n = styles.ComputeAll();
        Assert.True(n > depth * 10);
        // About 0.1 s on a development machine; the bound leaves a slow shared runner room and still fails a per-level blow-up.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), sw.Elapsed.ToString());

        // Spot checks against direct matching of the flattened selector.
        const string Flat = ":is(:is(:is(:is(:is(*) *) :nth-child(3n of foo, bar, target)) *) *) *";
        int seen = 0, hits = 0;
        foreach (Node e in doc.Root.DescendantElements.Where(x => x.LocalName is "foo" or "target"))
        {
            if (seen++ % 25 != 0)
            {
                continue;
            }

            bool direct = e.Matches(Flat);
            hits += direct ? 1 : 0;
            Assert.Equal(direct, styles.GetStyle(e).GetPropertyValue("background-color") == "red");
        }

        Assert.True(seen > 0);
        _ = hits;
    }

    [Fact]
    public void Memoized_logical_selectors_agree_with_direct_matching()
    {
        // Same tree under the memoizing cascade and under direct selector matching, for :is, :where and :not nested with descendant combinators.
        var html = new StringBuilder("<style>:is(.a, .b) :not(.c) { background-color: red } :where(:is(.a *) .b) { margin-top: 1px } .a :is(.b :not(.a)) { opacity: 0.5 } :nth-child(2n of :not(.c)) { width: 3px }</style>");
        for (int i = 0; i < 40; i++)
        {
            html.Append("<div class=\"").Append((i % 3) switch { 0 => "a", 1 => "b", _ => "c" }).Append("\"><span class=\"").Append(i % 2 == 0 ? "a" : "b").Append("\">");
        }

        Document doc = Document.Parse(html.ToString());
        DocumentStyles styles = DocumentStyles.Create(doc);
        foreach (Node e in doc.Root.DescendantElements.Where(x => x.LocalName is "div" or "span"))
        {
            bool red = e.Matches(":is(.a, .b) :not(.c)");
            Assert.Equal(red, styles.GetStyle(e).GetPropertyValue("background-color") == "red");
            Assert.Equal(e.Matches(":where(:is(.a *) .b)"), styles.GetStyle(e).GetPropertyValue("margin-top") == "1px");
            Assert.Equal(e.Matches(".a :is(.b :not(.a))"), styles.GetStyle(e).GetPropertyValue("opacity") == "0.5");
            Assert.Equal(e.Matches(":nth-child(2n of :not(.c))"), styles.GetStyle(e).GetPropertyValue("width") == "3px");
        }
    }
}
