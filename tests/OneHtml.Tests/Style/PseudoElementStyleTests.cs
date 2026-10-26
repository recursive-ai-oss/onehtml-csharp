using System.Linq;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

public class PseudoElementStyleTests
{
    private static ComputedStyle? Pseudo(string html, string id, PseudoElementKind kind)
    {
        DocumentStyles s = DocumentStyles.Create(Document.Parse(html));
        return s.GetPseudoStyle(s.Document.GetElementById(id), kind);
    }

    [Fact]
    public void Before_and_after_get_their_own_content_and_display()
    {
        const string h = "<style>#a::before{content:'x' attr(title); display:block} #a::after{content:none} #a::marker{color:red}</style><p id=a title=T>y</p><p id=b>z</p>";
        ComputedStyle before = Pseudo(h, "a", PseudoElementKind.Before)!;
        Assert.Equal("block", before.Display.ToString());
        ContentValue content = before.Content!;
        Assert.Equal(2, content.Items.Count);
        Assert.Equal(ContentItemKind.Attr, content.Items[1].Kind);
        Assert.True(Pseudo(h, "a", PseudoElementKind.After)!.Content!.IsNone);
        Assert.Equal("red", Pseudo(h, "a", PseudoElementKind.Marker)!.GetPropertyValue("color"));
        Assert.Null(Pseudo(h, "b", PseudoElementKind.Before));
    }

    [Fact]
    public void The_pseudo_element_inherits_from_its_element_and_follows_the_cascade()
    {
        const string h = "<style>p{color:green;visibility:hidden} p::before{content:'a'} .x::before{content:'b';visibility:visible} p.x::before{content:'c' !important}</style><p id=a>1</p><p id=b class=x>2</p>";
        ComputedStyle a = Pseudo(h, "a", PseudoElementKind.Before)!;
        Assert.Equal("green", a.GetPropertyValue("color"));
        Assert.Equal(VisibilityValue.Hidden, a.Visibility);
        ComputedStyle b = Pseudo(h, "b", PseudoElementKind.Before)!;
        Assert.Equal(VisibilityValue.Visible, b.Visibility);
        Assert.Equal("c", b.Content!.Items.Single().Text);
    }

    [Fact]
    public void Ancestor_conditions_and_the_single_colon_form_apply()
    {
        const string h = "<style>div > p:first-child:before{content:'f'} section p::after{content:'s'}</style><div><p id=a>1</p><p id=b>2</p></div>";
        Assert.NotNull(Pseudo(h, "a", PseudoElementKind.Before));
        Assert.Null(Pseudo(h, "b", PseudoElementKind.Before));
        Assert.Null(Pseudo(h, "a", PseudoElementKind.After));
    }

    [Fact]
    public void The_user_agent_quotes_come_with_the_q_element()
    {
        ComputedStyle before = Pseudo("<q id=a>x</q>", "a", PseudoElementKind.Before)!;
        Assert.Equal(ContentItemKind.OpenQuote, before.Content!.Items.Single().Kind);
    }

    [Fact]
    public void Wrong_arguments()
    {
        DocumentStyles s = DocumentStyles.Create(Document.Parse("<p id=a>x</p>"));
        Assert.Throws<System.ArgumentException>(() => s.GetPseudoStyle(s.Document.Root, PseudoElementKind.Before));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => s.GetPseudoStyle(s.Document.GetElementById("a"), (PseudoElementKind)9));
    }

    [Fact]
    public void Rules_with_a_pseudo_element_still_match_no_element()
    {
        Document doc = Document.Parse("<style>p::before{color:red}</style><p id=a>x</p>");
        DocumentStyles s = DocumentStyles.Create(doc);
        Assert.NotEqual("red", s.GetStyle(doc.GetElementById("a")).GetPropertyValue("color"));
        Assert.Empty(doc.Root.QuerySelectorAll("p::before"));
    }
}
