using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneHtml.Selectors;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Selectors;

public class SelectorBasicTests
{
    private static Document Doc(string html) => Document.Parse("<!DOCTYPE html>" + html);

    private static string Ids(IEnumerable<Node> nodes) => string.Join(",", nodes.Select(n => n.Id));

    [Theory]
    [InlineData("div > p.a", "div > p.a")]
    [InlineData("A[HREF=x i]", "A[HREF=\"x\" i]")]
    [InlineData(":is(a,b)>c", ":is(a, b) > c")]
    [InlineData("li:nth-child(2n+1 of .x)", "li:nth-child(2n+1 of .x)")]
    [InlineData("*.a", ".a")]
    [InlineData("#\\31 23", "#\\31 23")]
    public void Serializes(string input, string expected)
    {
        Assert.Equal(expected, SelectorList.Parse(input).ToString());
    }

    [Theory]
    [InlineData("#a", "a")]
    [InlineData("p", "p1,p2")]
    [InlineData("div p:first-child", "p1")]
    [InlineData("p + p", "p2")]
    [InlineData("p:not(.x)", "p1")]
    [InlineData("div:has(> p.x)", "a")]
    [InlineData("[class~=x]", "p2")]
    [InlineData(":nth-child(2 of p)", "p2")]
    [InlineData("p:nth-last-of-type(1)", "p2")]
    public void Queries(string selector, string expected)
    {
        Document d = Doc("<div id=a><p id=p1>1</p><p id=p2 class='x y'>2</p></div>");
        Assert.Equal(expected, Ids(d.Root.QuerySelectorAll(selector)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a,")]
    [InlineData("[")]
    [InlineData(".5cm")]
    [InlineData("div:example")]
    [InlineData(":nth-child(n- 1 2)")]
    [InlineData(":not()")]
    public void Rejects(string selector)
    {
        Assert.False(SelectorList.TryParse(selector, out _));
        Assert.Throws<SelectorSyntaxException>(() => SelectorList.Parse(selector));
    }
}
