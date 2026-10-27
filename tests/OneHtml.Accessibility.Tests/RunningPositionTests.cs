using System.Linq;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>
/// <c>position: running(name)</c> (CSS Generated Content for Paged Media 3 section 1) takes an element out of the flow of paged media. The accessibility tree
/// is the screen tree Chrome exposes, and Chrome 156 does not know the value (it drops the declaration, so the element stays in the page), so a running
/// element is in the tree like any other: same role, name, hidden status and focus order as with <c>position: static</c>.
/// </summary>
public sealed class RunningPositionTests
{
    private const string Page = "<style>.r{position:%P%}.q{position:%P%;display:block}</style><header class=r id=h><h1>Title</h1><nav class=q aria-label=Main><a href=/x>Go</a></nav></header><main><img class=r id=i src=a.png alt='A picture'><button class=r>Press</button><p class=q hidden>gone</p></main>";

    private static string[] Dump(string position)
    {
        Document doc = Document.Parse(Page.Replace("%P%", position));
        AccessibilityTree tree = AccessibilityTree.Build(doc, DocumentStyles.Create(doc));
        return [.. doc.Root.DescendantElements.Select(e => tree.Find(e) is { } n ? e.LocalName + ":" + n.Role + ":" + n.Name.Value + ":" + n.Hidden.Reasons : e.LocalName + ":none")];
    }

    [Fact]
    public void A_running_element_is_in_the_tree_like_a_static_one()
    {
        Assert.Equal(Dump("static"), Dump("running(banner)"));
        Document doc = Document.Parse(Page.Replace("%P%", "running(banner)"));
        AxNode img = AccessibilityTree.Build(doc, DocumentStyles.Create(doc)).Find(doc.Root.QuerySelector("#i"))!;
        Assert.Equal("image", img.Role);
        Assert.Equal("A picture", img.Name.Value);
    }

    [Fact]
    public void The_style_still_says_running()
    {
        Document doc = Document.Parse(Page.Replace("%P%", "running(banner)"));
        ComputedStyle s = DocumentStyles.Create(doc).GetStyle(doc.Root.QuerySelector("#h"));
        Assert.Equal(PositionValue.Running, s.Position);
        Assert.Equal("banner", s.RunningName);
        Assert.Equal("running(banner)", s.GetPropertyValue("position"));
    }
}
