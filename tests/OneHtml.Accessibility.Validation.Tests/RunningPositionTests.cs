using System.Linq;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>
/// A running element (<c>position: running(name)</c>, CSS GCPM 3 section 1) is checked as Chrome renders it: Chrome 156 drops the declaration, so
/// the element is in flow and not positioned. Every rule's outcome is what it is for <c>position: static</c>.
/// </summary>
public sealed class RunningPositionTests
{
    private const string Page = "<style>.r{position:%P%;background:#fff;color:#999}.s{position:%P%;background:#000}</style>"
        + "<header class=r id=h><h1>Title</h1><nav aria-label=Main><a href=/x class=r>Go</a></nav></header>"
        + "<main><img class=r src=a.png><button class=r>Press</button><div class=s><span class=r>Text on dark</span></div>"
        + "<input class=r type=text><label class=r>L</label></main>";

    private static string[] Results(string position)
    {
        Document doc = Document.Parse(Page.Replace("%P%", position));
        return [.. new Checker().Check(doc).Results.Select(r => r.Rule.Descriptor.Id + ":" + r.Outcome + ":" + r.Target.Index + ":" + r.TaskImpact + ":" + string.Join("/", r.FactsMissing))];
    }

    [Fact]
    public void Every_outcome_equals_the_static_one()
    {
        string[] expected = Results("static");
        Assert.NotEmpty(expected);
        // "running(x)" is longer than "static", so the node indexes still match: the style text is a single text node.
        Assert.Equal(expected, Results("running(banner)"));
    }

    [Fact]
    public void A_later_declaration_that_is_not_valid_leaves_the_earlier_position()
    {
        Document doc = Document.Parse("<style>.r{position:relative;position:running()}</style><p class=r>x</p>");
        Css.ComputedStyle s = Css.DocumentStyles.Create(doc).GetStyle(doc.Root.QuerySelector("p"));
        Assert.Equal(Css.PositionValue.Relative, s.Position);
        Assert.Null(s.RunningName);
    }
}
