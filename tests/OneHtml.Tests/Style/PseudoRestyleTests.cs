using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// An element's own style does not include its generated boxes, so a change that only reaches a <c>::before</c> or
/// <c>::after</c> rule leaves the element's style as it was. The restyle names those elements apart.
/// </summary>
public class PseudoRestyleTests
{
    private static RestyleResult Restyle(string html, string selector, string attribute, string? value, out Document next, out DocumentStyles styles)
    {
        Document doc = Document.Parse(html);
        styles = DocumentStyles.Create(doc);
        Node target = doc.Root.QuerySelector(selector);
        EditResult r = doc.Apply([value is null ? Edit.RemoveAttribute(target, attribute) : Edit.SetAttribute(target, attribute, value)]);
        Assert.True(r.Succeeded, r.Failure);
        next = r.Document!;
        return styles.Restyle(r);
    }

    [Fact]
    public void A_class_on_an_ancestor_reaches_a_descendants_pseudo_element_rule()
    {
        RestyleResult rs = Restyle("<style>.on .b::before{content:'x'}</style><div id=a><p class=b>t</p></div>", "#a", "class", "on", out Document next, out _);
        int b = next.Root.QuerySelector(".b").Index;
        Assert.DoesNotContain(b, rs.RestyledElements);
        Assert.Contains(b, rs.PseudoElementsMayDiffer!);
        Assert.NotNull(rs.Styles.GetPseudoStyle(next.Root.QuerySelector(".b"), PseudoElementKind.Before));
    }

    [Fact]
    public void Editing_a_pseudo_element_rule_names_the_elements_it_matches()
    {
        Document doc = Document.Parse("<style id=s>p::after{content:'a'}</style><p id=p>t</p><div id=d>u</div>");
        DocumentStyles styles = DocumentStyles.Create(doc);
        EditResult r = doc.Apply([Edit.ReplaceText(doc.Root.QuerySelector("#s"), "p::after{content:'b'}")]);
        RestyleResult rs = styles.Restyle(r);
        Assert.Contains(r.Document!.Root.QuerySelector("#p").Index, rs.PseudoElementsMayDiffer!);
        Assert.DoesNotContain(r.Document.Root.QuerySelector("#d").Index, rs.PseudoElementsMayDiffer!);
        Assert.Contains("b", rs.Styles.GetPseudoStyle(r.Document.Root.QuerySelector("#p"), PseudoElementKind.After)!.GetPropertyValue("content"));
    }

    [Fact]
    public void Without_author_pseudo_element_rules_the_list_is_the_changed_element()
    {
        RestyleResult rs = Restyle("<p id=a class=x>t</p><p>u</p>", "#a", "class", "y", out Document next, out _);
        Assert.Equal([next.Root.QuerySelector("#a").Index], rs.PseudoElementsMayDiffer!.ToArray());
    }

    [Fact]
    public void A_pseudo_element_style_is_computed_under_the_restyled_parent_style()
    {
        // em resolves through the parent chain: a font-size edit on the element changes the ::before's computed size.
        Document doc = Document.Parse("<style>p::before{content:'x';font-size:2em} #p{font-size:10px}</style><p id=p>t</p>");
        DocumentStyles styles = DocumentStyles.Create(doc);
        Assert.Equal(20, styles.GetPseudoStyle(doc.Root.QuerySelector("#p"), PseudoElementKind.Before)!.FontSizePx);
        EditResult r = doc.Apply([Edit.SetStyle(doc.Root.QuerySelector("#p"), "font-size", "30px")]);
        RestyleResult rs = styles.Restyle(r);
        Assert.Equal(60, rs.Styles.GetPseudoStyle(r.Document!.Root.QuerySelector("#p"), PseudoElementKind.Before)!.FontSizePx);
        Assert.Contains(r.Document.Root.QuerySelector("#p").Index, rs.PseudoElementsMayDiffer!);
    }

    [Fact]
    public void A_restyled_value_keeps_the_origin_of_its_winning_declaration()
    {
        // The same display: none, first from an author rule, then from the user-agent rule for [hidden].
        Document doc = Document.Parse("<style id=s>[hidden]{display:none}</style><div hidden id=d>x</div>");
        DocumentStyles styles = DocumentStyles.Create(doc);
        Assert.Equal(StyleOrigin.Author, styles.GetStyle(doc.Root.QuerySelector("#d")).GetSource("display")!.Origin);
        EditResult r = doc.Apply([Edit.ReplaceText(doc.Root.QuerySelector("#s"), "p{color:red}")]);
        RestyleResult rs = styles.Restyle(r);
        Node d = r.Document!.Root.QuerySelector("#d");
        Assert.Equal(StyleOrigin.UserAgent, rs.Styles.GetStyle(d).GetSource("display")!.Origin);
        Assert.Equal(StyleOrigin.UserAgent, DocumentStyles.Create(r.Document).GetStyle(d).GetSource("display")!.Origin);
    }
}
