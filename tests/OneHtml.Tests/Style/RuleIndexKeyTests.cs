using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// The rule index files a selector under a key every element it can match has (an attribute, a list of types), and drops selectors that
/// never match. These check that the elements such selectors are meant for still get them, and no others.
/// </summary>
public class RuleIndexKeyTests
{
    private static string Value(DocumentStyles s, string id, string property) => s.GetStyle(s.Document.GetElementById(id)).GetPropertyValue(property);

    [Fact]
    public void Attribute_keyed_selectors_reach_only_elements_with_the_attribute()
    {
        DocumentStyles s = CascadeTests.Styles("<style>p[align=left]{color:red} [data-X]{color:blue} svg [viewBox]{fill:green} [VIEWBOX]{stroke:red}</style><body>"
            + "<p id=a align=LEFT>t</p><p id=b>t</p><p id=c align=right>t</p><div id=d DATA-x=1>t</div><svg id=e><g id=f viewBox='0 0 1 1'/></svg>");
        Assert.Equal("red", Value(s, "a", "color"));
        Assert.NotEqual("red", Value(s, "b", "color"));
        Assert.NotEqual("red", Value(s, "c", "color"));
        Assert.Equal("blue", Value(s, "d", "color"));
        Assert.Equal("green", Value(s, "f", "fill"));
        Assert.NotEqual("green", Value(s, "e", "fill"));
    }

    [Fact]
    public void Link_and_heading_selectors_keep_their_matches()
    {
        DocumentStyles s = CascadeTests.Styles("<style>:link{color:red} :any-link{margin-top:3px} :visited{color:green} :heading{padding-top:4px} :heading(2){padding-left:5px} "
            + ":is(dl, span){margin-left:7px} :where(b, i) u{margin-right:8px}</style><body>"
            + "<a id=a href=x>t</a><a id=b>t</a><h2 id=c>t</h2><h3 id=d>t</h3><p id=e>t</p><dl id=f></dl><span id=g></span><b><u id=h>t</u></b><u id=i>t</u><svg><a id=j href=y></a></svg>");
        Assert.Equal("red", Value(s, "a", "color"));
        Assert.Equal("3px", Value(s, "a", "margin-top"));
        Assert.NotEqual("red", Value(s, "b", "color"));
        Assert.NotEqual("3px", Value(s, "b", "margin-top"));
        Assert.NotEqual("red", Value(s, "j", "color"));
        Assert.Equal("4px", Value(s, "c", "padding-top"));
        Assert.Equal("5px", Value(s, "c", "padding-left"));
        Assert.Equal("4px", Value(s, "d", "padding-top"));
        Assert.NotEqual("5px", Value(s, "d", "padding-left"));
        Assert.NotEqual("4px", Value(s, "e", "padding-top"));
        Assert.Equal("7px", Value(s, "f", "margin-left"));
        Assert.Equal("7px", Value(s, "g", "margin-left"));
        Assert.NotEqual("7px", Value(s, "e", "margin-left"));
        Assert.Equal("8px", Value(s, "h", "margin-right"));
        Assert.NotEqual("8px", Value(s, "i", "margin-right"));
    }

    [Fact]
    public void Writing_mode_and_direction_pass_down_unless_set()
    {
        DocumentStyles s = CascadeTests.Styles("<body style='direction:rtl'><div id=a style='writing-mode:vertical-rl'><p id=b>t</p><p id=c style='direction:ltr'>t</p></div><div id=d dir=ltr><span id=e>t</span></div></body>");
        ComputedStyle b = s.GetStyle(s.Document.GetElementById("b"));
        Assert.True(b.IsRightToLeft);
        Assert.Equal("vertical-rl", b.WritingMode);
        ComputedStyle c = s.GetStyle(s.Document.GetElementById("c"));
        Assert.False(c.IsRightToLeft);
        Assert.Equal("vertical-rl", c.WritingMode);
        Assert.True(s.GetStyle(s.Document.GetElementById("e")).IsRightToLeft == s.GetStyle(s.Document.GetElementById("d")).IsRightToLeft);
        Assert.Equal("horizontal-tb", s.GetStyle(s.Document.GetElementById("d")).WritingMode);
    }
}
