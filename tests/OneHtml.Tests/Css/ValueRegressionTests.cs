using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Css;

/// <summary>Minimized inputs that the value-parsing fuzz target found.</summary>
public class ValueRegressionTests
{
    [Theory]
    [InlineData("\\a no, ()")]
    [InlineData("(w(dth) or (b")]
    [InlineData("(a or (b: ((c)")]
    [InlineData("not ((wdthE) or (heig[t))")]
    [InlineData("ten(1deg /# alph')g\f")]
    [InlineData("rgb0\\96, 84, 108)")]
    [InlineData("color-mix(in' srg7, re! 30%, blue 40%))")]
    [InlineData("f(\"a")]
    [InlineData("f(a, \\")]
    [InlineData("f(\"a\\")]
    [InlineData("f(a}{, 1)\\")]
    public void Media_query_list_serializes_to_text_that_parses_back_to_itself(string query)
    {
        string css = MediaQueryList.Parse(query).ToCss();
        Assert.Equal(css, MediaQueryList.Parse(css).ToCss());
    }

    [Fact]
    public void Media_type_with_an_escaped_newline_keeps_its_escape()
    {
        Assert.Equal("\\a no, ()", MediaQueryList.Parse("\\a no, ()").ToCss());
    }

    [Theory]
    [InlineData("rgb(0, 0, calc(NaN))")]
    [InlineData("color(a98-rgb calc(NaN) 0 0)")]
    [InlineData("color(prophoto-rgb 0 calc(NaN) 0)")]
    [InlineData("color(rec2020 0 0 calc(NaN))")]
    public void Not_a_number_in_a_color_channel_does_not_throw(string text)
    {
        Assert.True(CssColor.TryParse(text, out CssColor c));
        foreach (ColorSpace space in System.Enum.GetValues<ColorSpace>())
        {
            _ = c.ConvertTo(space).ToCss();
        }
    }

    [Theory]
    [InlineData("98459453589700000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000px")]
    [InlineData("1e999")]
    public void A_literal_too_large_for_a_double_serializes_to_something_that_parses(string text)
    {
        Assert.True(CssQuantity.TryParse(CssTree.ParseComponentValueList(text).Values, QuantityKinds.Number | QuantityKinds.Length, out CssQuantity q));
        string css = q.ToCss();
        Assert.True(CssQuantity.TryParse(CssTree.ParseComponentValueList(css).Values, QuantityKinds.Number | QuantityKinds.Length, out CssQuantity again));
        Assert.Equal(css, again.ToCss());
    }

    [Theory]
    [InlineData("round(7*up, -0, infinity)")]
    [InlineData("calc(1px *)")]
    [InlineData("calc(* 2)")]
    [InlineData("min(1px +, 2px)")]
    public void Operators_missing_an_operand_make_the_math_function_invalid(string text)
    {
        foreach (QuantityKinds kinds in new[] { QuantityKinds.Number, QuantityKinds.LengthPercentage, QuantityKinds.Angle })
        {
            Assert.False(CssQuantity.TryParse(CssTree.ParseComponentValueList(text).Values, kinds, out _));
        }
    }

    [Theory]
    [InlineData("hsl(none 0% 18.9e3757%)")]
    [InlineData("hsl(none 0% calc(infinity))")]
    [InlineData("lab(1e999 1e999 -1e999)")]
    public void Huge_color_components_serialize_to_text_that_parses(string text)
    {
        Assert.True(CssColor.TryParse(text, out CssColor c));
        string css = c.ToCss();
        Assert.True(CssColor.TryParse(css, out CssColor again));
        Assert.Equal(css, again.ToCss());
    }

    [Fact]
    public void Keywords_fold_ASCII_case_only()
    {
        Assert.True(CssColor.TryParse("BLACK", out _));
        Assert.False(CssColor.TryParse("blac\u212A", out _));
        Assert.False(CssColor.TryParse("RGB(0 0 0)".Replace("RGB", "rgb\u212A"), out _));
        Assert.Equal("not all", MediaQueryList.Parse("(\u212Aolor)").Matches(MediaFeatures.Default) ? "match" : "not all");
    }
}
