using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// Where <c>::selection</c> and <c>::placeholder</c> exist, how they inherit, and how they follow state and edits. What each computes to
/// is checked against Chrome in <see cref="ChromeFormAndPseudoTests"/>.
/// </summary>
public class SelectionPlaceholderTests
{
    private static ComputedStyle? Pseudo(string html, string id, PseudoElementKind kind)
    {
        Document doc = Document.Parse(html);
        return DocumentStyles.Create(doc).GetPseudoStyle(doc.GetElementById(id), kind);
    }

    [Fact]
    public void A_selection_style_exists_for_an_element_with_a_matching_rule_and_for_its_descendants()
    {
        const string h = "<style>div::selection{color:blue}</style><section id=s><div id=d><p id=p>t <b id=b>x</b></p></div><i id=i>y</i></section>";
        Assert.Null(Pseudo(h, "s", PseudoElementKind.Selection));
        Assert.Null(Pseudo(h, "i", PseudoElementKind.Selection));
        Assert.Equal("blue", Pseudo(h, "d", PseudoElementKind.Selection)!.GetPropertyValue("color"));
        Assert.Equal("blue", Pseudo(h, "p", PseudoElementKind.Selection)!.GetPropertyValue("color"));
        Assert.Equal("blue", Pseudo(h, "b", PseudoElementKind.Selection)!.GetPropertyValue("color"));
        Assert.Null(Pseudo("<p id=p>t</p>", "p", PseudoElementKind.Selection));
    }

    [Fact]
    public void A_selection_style_ignores_properties_that_do_not_apply_to_highlights_and_keeps_custom_properties()
    {
        ComputedStyle s = Pseudo("<style>p{font-size:20px;margin-top:4px} p::selection{--k:7px;font-size:50px;margin-top:9px;color:red;text-decoration-line:underline}</style><p id=p>t</p>", "p", PseudoElementKind.Selection)!;
        Assert.Equal(20, s.FontSizePx);
        Assert.Equal("0", s.GetPropertyValue("margin-top"));
        Assert.Equal("red", s.GetPropertyValue("color"));
        Assert.Equal("underline", s.GetPropertyValue("text-decoration-line"));
        Assert.True(s.TryGetCustomProperty("--k", out string? k));
        Assert.Equal("7px", k);
    }

    [Fact]
    public void A_placeholder_style_exists_only_for_text_controls_with_a_placeholder()
    {
        const string h = "<input id=a placeholder=x><input id=b><input id=c placeholder=\"\"><input id=d type=checkbox placeholder=x><textarea id=e placeholder=x></textarea><textarea id=f></textarea><div id=g placeholder=x></div><input id=h type=hidden placeholder=x>";
        Assert.NotNull(Pseudo(h, "a", PseudoElementKind.Placeholder));
        Assert.NotNull(Pseudo(h, "e", PseudoElementKind.Placeholder));
        foreach (string id in new[] { "b", "c", "d", "f", "g", "h" })
        {
            Assert.Null(Pseudo(h, id, PseudoElementKind.Placeholder));
        }
    }

    [Fact]
    public void The_placeholder_box_is_not_displayed_while_a_value_hides_it()
    {
        const string h = "<input id=a placeholder=x><input id=b placeholder=x value=v><textarea id=c placeholder=x>t</textarea>";
        Assert.Equal("block", Pseudo(h, "a", PseudoElementKind.Placeholder)!.GetPropertyValue("display"));
        Assert.Equal("none", Pseudo(h, "b", PseudoElementKind.Placeholder)!.GetPropertyValue("display"));
        Assert.Equal("none", Pseudo(h, "c", PseudoElementKind.Placeholder)!.GetPropertyValue("display"));
    }

    [Fact]
    public void Every_kind_has_its_own_cache_entry_on_the_same_element()
    {
        Document doc = Document.Parse("<style>input::before{content:'b'} input::after{content:'a'} input::selection{color:red} input::placeholder{color:blue} input::marker{color:green}</style><input id=i placeholder=x>");
        DocumentStyles styles = DocumentStyles.Create(doc);
        Node i = doc.GetElementById("i");
        for (int round = 0; round < 2; round++)
        {
            Assert.Contains("b", styles.GetPseudoStyle(i, PseudoElementKind.Before)!.GetPropertyValue("content"));
            Assert.Contains("a", styles.GetPseudoStyle(i, PseudoElementKind.After)!.GetPropertyValue("content"));
            Assert.Equal("green", styles.GetPseudoStyle(i, PseudoElementKind.Marker)!.GetPropertyValue("color"));
            Assert.Equal("red", styles.GetPseudoStyle(i, PseudoElementKind.Selection)!.GetPropertyValue("color"));
            Assert.Equal("blue", styles.GetPseudoStyle(i, PseudoElementKind.Placeholder)!.GetPropertyValue("color"));
        }
    }

    [Fact]
    public void Selection_styles_follow_interaction_and_form_state_on_both_paths()
    {
        Document doc = Document.Parse("<style>p:hover::selection{color:blue} input:checked::selection{color:red} input:focus::placeholder{color:green}</style><p id=p>t</p><input id=c type=checkbox><input id=t placeholder=x>");
        DocumentStyles start = DocumentStyles.Create(doc);
        LiveDocumentStyles live = LiveDocumentStyles.Create(start);
        Node p = doc.GetElementById("p");
        Node c = doc.GetElementById("c");
        Node t = doc.GetElementById("t");
        Assert.True(live.HasInteractivePseudoElementRules);
        Assert.Null(live.GetPseudoStyle(p, PseudoElementKind.Selection));
        live.ApplyState(ElementStateChange.Hover(p));
        Assert.Equal("blue", live.GetPseudoStyle(p, PseudoElementKind.Selection)!.GetPropertyValue("color"));
        Assert.Equal("blue", start.WithContext(live.GetContext()).GetPseudoStyle(p, PseudoElementKind.Selection)!.GetPropertyValue("color"));
        live.ApplyState(ElementStateChange.Hover(default));
        Assert.Null(live.GetPseudoStyle(p, PseudoElementKind.Selection));
        live.ApplyFormState([RecursiveAI.OneHtml.Selectors.FormStateChange.Checked(c, true)]);
        Assert.Equal("red", live.GetPseudoStyle(c, PseudoElementKind.Selection)!.GetPropertyValue("color"));
        live.ApplyState(ElementStateChange.Focus(t));
        Assert.Equal("green", live.GetPseudoStyle(t, PseudoElementKind.Placeholder)!.GetPropertyValue("color"));
        live.ApplyState(ElementStateChange.Focus(default));
        Assert.Equal("#757575", live.GetPseudoStyle(t, PseudoElementKind.Placeholder)!.GetPropertyValue("color"));
    }

    [Fact]
    public void An_edit_that_reaches_a_selection_rule_names_the_element_and_everything_below_it()
    {
        Document doc = Document.Parse("<style>.on::selection{color:red}</style><div id=a><p id=b>t <b id=c>x</b></p></div><div id=z><p>u</p></div>");
        DocumentStyles styles = DocumentStyles.Create(doc);
        EditResult r = doc.Apply([Edit.SetAttribute(doc.GetElementById("a"), "class", "on")]);
        Assert.True(r.Succeeded, r.Failure);
        RestyleResult rs = styles.Restyle(r);
        Document next = r.Document!;
        int[] may = rs.PseudoElementsMayDiffer!.ToArray();
        foreach (string id in new[] { "a", "b", "c" })
        {
            Assert.Contains(next.GetElementById(id).Index, may);
        }

        Assert.DoesNotContain(next.GetElementById("z").Index, may);
        Assert.Equal("red", rs.Styles.GetPseudoStyle(next.GetElementById("c"), PseudoElementKind.Selection)!.GetPropertyValue("color"));
    }

    [Fact]
    public void A_placeholder_attribute_edit_is_reported_for_the_placeholder_rules()
    {
        Document doc = Document.Parse("<input id=a><input id=b placeholder=x>");
        DocumentStyles styles = DocumentStyles.Create(doc);
        Assert.Null(styles.GetPseudoStyle(doc.GetElementById("a"), PseudoElementKind.Placeholder));
        EditResult r = doc.Apply([Edit.SetAttribute(doc.GetElementById("a"), "placeholder", "hi")]);
        Assert.True(r.Succeeded, r.Failure);
        RestyleResult rs = styles.Restyle(r);
        Assert.Contains(r.Document!.GetElementById("a").Index, rs.PseudoElementsMayDiffer!);
        Assert.NotNull(rs.Styles.GetPseudoStyle(r.Document.GetElementById("a"), PseudoElementKind.Placeholder));
    }

    [Fact]
    public void Deep_nesting_does_not_recurse()
    {
        var sb = new StringBuilder("<style>div::selection{color:red}</style>");
        const int depth = 400;
        for (int i = 0; i < depth; i++)
        {
            sb.Append("<div>");
        }

        sb.Append("<p id=p>t</p>");
        Document doc = Document.Parse(sb.ToString());
        Assert.Equal("red", DocumentStyles.Create(doc).GetPseudoStyle(doc.GetElementById("p"), PseudoElementKind.Selection)!.GetPropertyValue("color"));
    }
}
