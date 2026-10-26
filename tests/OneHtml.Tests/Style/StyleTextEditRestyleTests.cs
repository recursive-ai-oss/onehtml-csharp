using System.Linq;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>Edits to the text inside a style element, made as ordinary document edits, restyle like any other change to the sheet.</summary>
public class StyleTextEditRestyleTests
{
    private static void AssertSame(DocumentStyles actual, DocumentStyles fresh)
    {
        foreach (Node e in fresh.Document.Root.DescendantElements)
        {
            Assert.True(actual.GetStyle(e).FirstDifference(fresh.GetStyle(e)) is null, "<" + e.LocalName + "> differs in " + actual.GetStyle(e).FirstDifference(fresh.GetStyle(e)));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Removing_the_text_of_one_of_several_style_elements_keeps_the_later_sheets(int which)
    {
        string page = "<style>a div{display:inline}</style><style>p{color:red}</style><style>div{place-items:center}</style><style>span{transition:none}</style><a href=/x><div><span>s</span></div></a><p>t</p>";
        Document doc = Document.Parse(page);
        DocumentStyles styles = DocumentStyles.Create(doc);
        styles.ComputeAll();
        Node text = doc.Root.DescendantElements.Where(e => e.LocalName == "style").ElementAt(which).FirstChild;
        EditResult er = doc.Apply([Edit.Remove(text)]);
        Assert.True(er.Succeeded, er.Failure);
        AssertSame(styles.Restyle(er).Styles, DocumentStyles.Create(er.Document!));
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("replace")]
    public void Removing_or_replacing_the_text_of_a_style_element_restyles_the_elements_it_matched(string how)
    {
        Document doc = Document.Parse("<style>a div{display:inline}</style><style>div{place-items:center}</style><a href=/x><div id=d><span>s</span></div></a><p>t</p>");
        DocumentStyles styles = DocumentStyles.Create(doc);
        styles.ComputeAll();
        Node text = doc.Root.DescendantElements.First(e => e.LocalName == "style").FirstChild;
        EditResult er = doc.Apply([how == "remove" ? Edit.Remove(text) : Edit.ReplaceText(text, "p{color:red}")]);
        Assert.True(er.Succeeded, er.Failure);
        RestyleResult res = styles.Restyle(er);
        AssertSame(res.Styles, DocumentStyles.Create(er.Document!));
    }
}
