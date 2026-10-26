using System;
using System.Diagnostics;
using System.Threading;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Css;

/// <summary>
/// A <c>unicode-range</c> value is tokenized a second time with unicode ranges allowed (CSS Syntax 3 §5.5.6), and the second
/// pass can see blocks the first pass swallowed (the <c>)</c> that ends a bad url in the first pass). Nesting past the limit
/// in that second pass must not move the marker of the first pass's overflowing rule block.
/// </summary>
public class CssUnicodeRangeNestingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // First pass: U, +1, and one bad-url that ends at the ")" after the line break, so no brace opens. Second pass: a
    // unicode-range token, an ident "ur\6c" with a "(" and twenty-three braces that overflow a depth limit of 4.
    private static readonly string Hostile = "unicode-range:U+1ur\\6c(\u00ac\")" + new string('{', 20) + "(\n);" + new string('{', 20) + "}";

    [Theory]
    [InlineData(4)]
    [InlineData(64)]
    public void Overflow_in_a_retokenized_unicode_range_value_stays_lossless_and_fast(int depth)
    {
        var o = new CssParseOptions { MaxNestingDepth = depth };
        var sw = Stopwatch.StartNew();
        foreach (string text in new[] { Hostile, "@font-face{" + Hostile + "}", "a{" + Hostile + "}b{}" })
        {
            Assert.Equal(text, CssSourceWriter.Write(CssTree.ParseStylesheet(text, o, Ct)));
            Assert.Equal(text, CssSourceWriter.Write(CssTree.ParseDeclarationList(text, o, Ct)));
            Assert.Equal(text, CssSourceWriter.Write(CssTree.ParseBlockContents(text, o, Ct)));
        }

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), sw.Elapsed.ToString());
    }
}
