using System;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// The cascade matches every candidate rule of an element with one match context. A selector that backtracks a lot turns
/// on a failure memo, and the memo must not carry one rule's failures over to another rule whose selector has the same
/// shape: selector ids are only unique within the list they were parsed in.
/// </summary>
public class CandidateMemoTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_rule_that_fails_after_much_backtracking_does_not_hide_a_later_rule_of_the_same_shape(bool differentSheets)
    {
        const string failing = ".q div div div div .c{color:red}";
        const string matching = ".p div div div div .c{color:blue}";
        string css = differentSheets ? "<style>" + failing + "</style><style>" + matching + "</style>" : "<style>" + failing + matching + "</style>";
        string body = "<div class=p>" + new string('x', 0) + string.Concat(System.Linq.Enumerable.Repeat("<div>", 70)) + "<span class=c id=t>t</span>" + string.Concat(System.Linq.Enumerable.Repeat("</div>", 70)) + "</div>";
        Document doc = Document.Parse("<!DOCTYPE html>" + css + body);
        Assert.Equal("blue", DocumentStyles.Create(doc).GetStyle(doc.GetElementById("t")).GetPropertyValue("color"));
    }
}
