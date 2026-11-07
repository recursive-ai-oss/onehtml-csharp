using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>CSS Values 4 §6.1: the number 0 is a valid <c>&lt;length&gt;</c> without a unit, so <c>font-size: 0</c> is the same declaration as <c>font-size: 0px</c>.</summary>
public class UnitlessZeroLengthTests
{
    [Theory]
    [InlineData("font-size:0")]
    [InlineData("font-size:0px")]
    [InlineData("font-size: 0 !important")]
    public void Font_size_accepts_a_unitless_zero(string declaration)
    {
        DocumentStyles s = CascadeTests.Styles("<p>visible <span id=a style='" + declaration + "'>hidden <b id=b>x</b></span></p>");
        Assert.Equal("0px", s.GetStyle(s.Document.GetElementById("a")).GetComputedValue("font-size"));
        Assert.Equal("0px", s.GetStyle(s.Document.GetElementById("b")).GetComputedValue("font-size"));
    }

    [Fact]
    public void Font_size_zero_in_a_style_sheet_and_in_the_font_shorthand()
    {
        DocumentStyles s = CascadeTests.Styles("<style>.z{font-size:0}.f{font:0 serif}</style><i id=z class=z>a</i><i id=f class=f>b</i>");
        Assert.Equal("0px", s.GetStyle(s.Document.GetElementById("z")).GetComputedValue("font-size"));
        Assert.Equal("0px", s.GetStyle(s.Document.GetElementById("f")).GetComputedValue("font-size"));
    }

    [Fact]
    public void Font_size_still_rejects_a_unitless_nonzero_number_and_a_negative_length()
    {
        DocumentStyles s = CascadeTests.Styles("<div style='font-size:20px'><span id=a style='font-size:5'>x</span><span id=b style='font-size:-0.0001px'>y</span></div>");
        Assert.Equal(20, s.GetStyle(s.Document.GetElementById("a")).FontSizePx);
        Assert.Equal(20, s.GetStyle(s.Document.GetElementById("b")).FontSizePx);
    }
}
