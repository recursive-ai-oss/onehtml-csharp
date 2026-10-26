using System;
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
    [InlineData("(url(")]
    [InlineData("(url(a")]
    [InlineData("(a, (url( ")]
    [InlineData("f(g(url(x")]
    [InlineData("(url(\u0001")]
    [InlineData("(url(p /")]
    [InlineData("(url(o\"")]
    [InlineData("(url(a b) (url(a b")]
    [InlineData("(url(a\\)")]
    [InlineData("(url(\\")]
    [InlineData("(url(a\\")]
    [InlineData("(a\\ ")]
    [InlineData("f(a\\ ")]
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
    [Theory]
    [InlineData("")]
    [InlineData(")\u0001")]
    [InlineData("not \0\\f(not )<a:b,)f(")]
    public void Media_query_nested_past_the_depth_limit_is_an_error_that_is_stable(string tail)
    {
        // Past the nesting limit the blocks stay unmatched and run to the end of the input, so a closer inside them closes
        // nothing; written back out with fewer levels it would close something. Such a query is `not all` (Media Queries 4 2.3).
        string css = MediaQueryList.Parse(new string('(', 257) + tail).ToCss();
        Assert.Equal("not all", css);
        Assert.Equal(css, MediaQueryList.Parse(css).ToCss());
        Assert.Equal("(a), not all", MediaQueryList.Parse("(a), " + new string('(', 257) + tail).ToCss());
    }

    [Fact]
    public void Media_query_at_the_depth_limit_still_parses()
    {
        string css = MediaQueryList.Parse(new string('(', 200) + new string(')', 200)).ToCss();
        Assert.NotEqual("not all", css);
        Assert.Equal(css, MediaQueryList.Parse(css).ToCss());
    }

    [Theory]
    [InlineData("(foo(x))")]
    [InlineData("((foo(x)) or (y))")]
    [InlineData("((a) b)")]
    [InlineData("((()) x)")]
    [InlineData("(a /* open")]
    [InlineData("(f(\"a\\\n")]
    [InlineData("(url(\"a")]
    public void Media_query_general_enclosed_keeps_its_parentheses(string query)
    {
        string css = MediaQueryList.Parse(query).ToCss();
        Assert.Equal(css, MediaQueryList.Parse(css).ToCss());
    }

    [Fact]
    public void Media_query_serialization_is_stable_on_random_token_soup()
    {
        string[] pieces = ["(", ")", "(", ")", "[", "]", "{", "}", "'", "\"", "\\", "\\\n", "/*", "*/", " ", "\n", "url(", "url(\"", "f(", "not ", "and ", "or ", "only ", "screen ", "width", ":", "<", ">=", "=", "1px", "1e3", "\0", ",", "/", "2", "min-", "x", "\\31 ", "\\ ", "!", ";", "@", "#a", "u+1", "\u00e9"];
        var rnd = new Random(7);
        var sb = new System.Text.StringBuilder();
        for (int it = 0; it < 200_000; it++)
        {
            sb.Clear();
            int n = rnd.Next(1, 40);
            for (int k = 0; k < n; k++)
            {
                sb.Append(pieces[rnd.Next(pieces.Length)]);
            }

            string q = sb.ToString();
            string css = MediaQueryList.Parse(q).ToCss();
            Assert.True(css == MediaQueryList.Parse(css).ToCss(), "unstable for: " + q);
        }
    }

    [Fact]
    public void Supports_condition_text_reads_back_as_the_same_condition_on_random_token_soup()
    {
        string[] pieces = ["(", ")", "(", ")", "[", "]", "{", "}", "'", "\"", "\\", "\\\n", "/*", "*/", " ", "\n", "url(", "url(\"", "f(", "not ", "and ", "or ", "selector(", "font-format(", "font-tech(", "at-rule(", "@media", "woff2", "display", ":", "grid", "!important", "!", "1px", "\0", ",", "/", "x", "\\31 ", "\\ ", ";", "#a", "u+1", "\u00e9"];
        var rnd = new Random(11);
        var sb = new System.Text.StringBuilder();
        for (int it = 0; it < 200_000; it++)
        {
            sb.Clear();
            int n = rnd.Next(1, 40);
            for (int k = 0; k < n; k++)
            {
                sb.Append(pieces[rnd.Next(pieces.Length)]);
            }

            AssertSupportsStable(sb.ToString());
        }
    }

    [Theory]
    [InlineData(257, "")]
    [InlineData(257, ")\u0001")]
    [InlineData(300, "display: grid")]
    [InlineData(260, ") and (display: grid)")]
    public void Supports_condition_nested_past_the_depth_limit_reads_back_as_the_same_condition(int depth, string tail)
    {
        AssertSupportsStable(new string('(', depth) + tail);
        AssertSupportsStable("(display: grid) and " + new string('(', depth) + tail);
    }

    [Theory]
    [InlineData("(url(")]
    [InlineData("(display: url(")]
    [InlineData("(display: \"a")]
    [InlineData("selector(a\\")]
    [InlineData("not (foo(x))")]
    [InlineData("((foo(x)) or (y))")]
    [InlineData("(display: grid) and (a\\ ")]
    [InlineData("(a /* open")]
    [InlineData("font-tech(")]
    [InlineData("(display: grid) '\f")]
    [InlineData("(display: grid) \"a\n")]
    [InlineData("not (display: grid) \"a\r\n  ")]
    [InlineData("'\n'\f")]
    public void Supports_condition_with_a_block_cut_off_by_the_end_of_input_reads_back_as_the_same_condition(string condition)
    {
        AssertSupportsStable(condition);
    }

    private static void AssertSupportsStable(string text)
    {
        SupportsCondition first = SupportsCondition.Parse(text);
        SupportsCondition again = SupportsCondition.Parse(first.Text);
        Assert.True(again.Text == first.Text && again.IsValid == first.IsValid && again.Evaluate() == first.Evaluate(), "unstable for: " + text);
    }

    // Everything the end of input can cut off, at the end of a query: strings, urls, identifiers and escapes, a backslash
    // by itself, a comment, and the newline forms (CR, FF, CRLF) that a string or an escape can end on.
    private static readonly string[] EofTails =
    [
        "\"a\\", "'a\\", "\"a", "'a", "url(a\\", "url(\"a\\", "url(a", "url(a b", "a\\", "\\", "\\31", "\\3", "/*c",
        "\"\\\r", "\"\\\r\r", "\"\\\r\n", "\"\\\n\n", "\"a\n", "url(a\\\n", "a\\ ", "\\\f", "\"\\\\", "\"\\\\\\",
        "\\7(\"\\^1-\\\r\r", "'\\\r\r\r",
    ];

    private static readonly string[] EofHeads =
    [
        "(", "((", "(x ", "f(", new string('(', 60), new string('(', 255), "screen and (", "not (", "(a) and (", "x, (", "(\"\\(\"\\(\"\\",
    ];

    [Fact]
    public void Media_query_cut_off_by_the_end_of_input_is_kept_and_reads_back_as_itself()
    {
        // A string, url, escape, comment or backslash cut off at the end of input inside a general-enclosed term, at several
        // depths: written back with its closers, the text must read as the same query, and the query must not degrade.
        foreach (string head in EofHeads)
        {
            foreach (string tail in EofTails)
            {
                string query = head + tail;
                string css = MediaQueryList.Parse(query).ToCss();
                Assert.True(css == MediaQueryList.Parse(css).ToCss(), "unstable for: " + query);
                Assert.True(!css.Contains("not all", StringComparison.Ordinal) || query.StartsWith("not", StringComparison.Ordinal), "degraded: " + query);
                Assert.DoesNotContain('\r', css);
            }
        }
    }

    [Theory]
    [InlineData("(\"\\\r\r", "(\"\\\n\n)")]
    [InlineData("((\"\\\r\n", "((\"\\\n\"))")]
    public void Media_query_with_a_bad_string_ending_on_a_carriage_return_is_written_with_line_feeds(string query, string expected)
    {
        // CR LF is one newline and CR CR is two, so the writer cannot add a line feed after a CR; it writes LF only.
        string css = MediaQueryList.Parse(query).ToCss();
        Assert.Equal(expected, css);
        Assert.Equal(css, MediaQueryList.Parse(css).ToCss());
        Assert.DoesNotContain('\r', css);
    }

    [Fact]
    public void Supports_condition_cut_off_by_the_end_of_input_reads_back_as_the_same_condition()
    {
        foreach (string head in EofHeads)
        {
            foreach (string tail in EofTails)
            {
                AssertSupportsStable(head + tail);
                AssertSupportsStable("(display: grid) and " + head + tail);
                AssertSupportsStable("selector(" + head + tail);
            }
        }
    }
}
