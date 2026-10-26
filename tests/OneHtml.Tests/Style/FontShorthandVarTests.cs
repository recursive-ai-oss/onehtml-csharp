using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>A later <c>font</c> shorthand that holds a <c>var()</c> still sets the font longhands over an earlier rule of equal specificity.</summary>
public class FontShorthandVarTests
{
    [Fact]
    public void Later_font_shorthand_with_var_beats_earlier_inherit()
    {
        const string h = "<style>:root :where(.b){font-size:inherit;font-weight:inherit;color:#fff}</style>"
            + "<style>:root{--s: 'Source Sans 3', sans-serif; --f: var(--s)} .b{background:red;padding:1px;font:700 1.188rem/1.25rem var(--f)}</style>"
            + "<div id=d><a id=a class=b href=x>t</a></div>";
        DocumentStyles s = CascadeTests.Styles(h);
        ComputedStyle a = s.GetStyle(s.Document.GetElementById("a"));
        Assert.Equal("700", a.GetPropertyValue("font-weight"));
        Assert.Equal("1.188rem", a.GetPropertyValue("font-size"));
        Assert.Equal("1.25rem", a.GetPropertyValue("line-height"));
    }
}
