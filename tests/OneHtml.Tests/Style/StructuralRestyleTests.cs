using System;
using System.Globalization;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// Restyle after an edit that changes the tree shape, under selectors that read a position or a sibling and then reach below
/// the element ("li:last-child a"), or that sit in a rule set where a <c>:has()</c> rule follows a positional one. The
/// incremental result must equal a fresh computation on the edited document.
/// </summary>
public class StructuralRestyleTests
{
    private static void AssertSameAsFresh(string html, Func<Document, Edit[]> edits, bool expectIncremental)
    {
        Document doc = Document.Parse(html);
        DocumentStyles before = DocumentStyles.Create(doc);
        before.ComputeAll();
        EditResult er = doc.Apply(edits(doc));
        Assert.True(er.Succeeded);
        RestyleResult res = before.Restyle(er);
        Assert.Equal(!expectIncremental, res.FullRecompute);
        DocumentStyles fresh = DocumentStyles.Create(res.Styles.Document);
        foreach (Node e in fresh.Document.Root.DescendantElements)
        {
            string? diff = res.Styles.GetStyle(e).FirstDifference(fresh.GetStyle(e));
            Assert.True(diff is null, "<" + e.LocalName + "> #" + e.Index.ToString(CultureInfo.InvariantCulture) + " differs in " + diff + " after the edit: " + res.Styles.Document.Source.Text);
        }
    }

    private static Node ById(Document d, string id) => d.Root.QuerySelector("#" + id);

    [Fact]
    public void DescendantOfTheOldLastChildLosesItsMatch()
    {
        // Selectors 4 section 14.3: :last-child. Appending a sibling takes the match from the old last li and from its descendants.
        const string html = "<style>.m li:last-child a{margin-right:4px}</style><ul class=m><li><a href=/1>1</a></li><li id=z><a href=/2>2</a></li></ul>";
        AssertSameAsFresh(html, d => [Edit.InsertAfter(ById(d, "z"), "<li>3</li>")], true);
    }

    [Fact]
    public void DescendantOfASiblingGainsANthLastChildMatch()
    {
        // Removing the last li moves the nth-last-child positions of the others, and with them what lies below them.
        const string html = "<style>ul li:nth-last-child(2) b{color:red}</style><ul><li><b>1</b></li><li><b>2</b></li><li id=z>3</li></ul>";
        AssertSameAsFresh(html, d => [Edit.Remove(ById(d, "z"))], true);
    }

    [Fact]
    public void DescendantsOfLaterSiblingsFollowANextSiblingCombinator()
    {
        const string html = "<style>.a + .b span{color:red}</style><div><p class=b id=y><span>s</span></p><p class=a id=z></p><p class=b><span>t</span></p></div>";
        AssertSameAsFresh(html, d => [Edit.InsertBefore(ById(d, "y"), "<p class=a></p>")], true);
    }

    [Fact]
    public void EmptyParentReachesItsDescendants()
    {
        const string html = "<style>.box:empty > span{color:red}</style><div class=box id=z></div>";
        AssertSameAsFresh(html, d => [Edit.Append(ById(d, "z"), "<span>x</span>")], true);
    }

    [Fact]
    public void NestedSelectorArgumentsCount()
    {
        const string html = "<style>:is(ul li:last-child a){color:red}</style><ul><li><a href=/1>1</a></li><li id=z><a href=/2>2</a></li></ul>";
        AssertSameAsFresh(html, d => [Edit.InsertAfter(ById(d, "z"), "<li>3</li>")], true);
    }

    [Fact]
    public void HasRuleAfterAPositionalRuleRestylesTheAncestor()
    {
        // An inserted element that a :has() rule counts changes the ancestor's match even when a positional rule comes first.
        const string html = "<style>li:first-child{color:blue} .w:not(:has(.k)){color:red}</style><div class=w id=w><p id=z>x</p></div>";
        AssertSameAsFresh(html, d => [Edit.InsertAfter(ById(d, "z"), "<p class=k>y</p>")], true);
    }
}
