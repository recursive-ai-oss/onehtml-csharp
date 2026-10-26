using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>The body element's link attribute is a presentational hint for the unvisited links of the document (HTML Standard, Rendering, "Page").</summary>
public class BodyLinkAttributeTests
{
    [Fact]
    public void Link_attribute_colours_links_with_an_href_but_loses_to_author_rules()
    {
        DocumentStyles s = CascadeTests.Styles("<style>.o{color:#00f}</style><body link='#840000'><a id=a href=x>t</a><a id=n>t</a><a id=o class=o href=x>t</a></body>");
        Assert.Equal("#840000", s.GetStyle(s.Document.GetElementById("a")).GetPropertyValue("color"));
        Assert.NotEqual("#840000", s.GetStyle(s.Document.GetElementById("n")).GetPropertyValue("color"));
        Assert.NotEqual("#840000", s.GetStyle(s.Document.GetElementById("o")).GetPropertyValue("color"));
    }
}
