using System;
using System.Diagnostics;
using System.Text;
using RecursiveAI.OneHtml.Encodings;
using Xunit;

namespace RecursiveAI.OneHtml.Tests;

/// <summary>
/// Hostile shapes: nothing may hang, nothing may be quadratic, and the limits fail with the typed exception before
/// the work they guard. Timing checks compare a run with one eight times its size, so a quadratic algorithm (64x)
/// is told from a linear one (8x) even on a busy machine.
/// </summary>
public class LimitsTests
{
    private static Document Parse(string html, ParseLimits? limits = null) =>
        Document.Parse(Encoding.UTF8.GetBytes(html), new ParseOptions { Limits = limits });

    private static string Repeat(string unit, int n)
    {
        var sb = new StringBuilder(unit.Length * n);
        for (int i = 0; i < n; i++)
        {
            sb.Append(unit);
        }

        return sb.ToString();
    }

    private static double Time(string html)
    {
        double best = double.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                Document doc = Parse(html);
                Assert.Null(DocumentInvariants.Check(doc));
            }
            catch (HtmlLimitExceededException)
            {
                // A typed refusal is an allowed outcome of a hostile input.
            }

            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }

        return best;
    }

    public static TheoryData<string, string> Shapes => new()
    {
        { "formatting element closed inside a paragraph", "<p><b x=1></p>" },
        { "adoption agency with a furthest block", "<b><p>x</b>y</p>" },
        { "misnested formatting", "<a><b><i>x</a>y</b>z</i>" },
        { "foster parented text", "<table>x</table>" },
        { "foster parented elements", "<table><b>x</b></table>" },
        { "implied table parts", "<table><td>x" },
        { "same formatting element over and over", "<b><b><b><b>x" },
        { "unclosed paragraphs", "<p>x" },
        { "table cell reset", "<table><tr><td><b>x</td></tr>" },
        { "selected options with a selectedcontent", "<select><button><selectedcontent></button><option selected>x" },
        { "template nesting", "<template><p>x</template>" },
        { "foreign content breakout", "<svg><p><math><b>" },
        { "character references", "a&amp;b&#x41;&notit;" },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Hostile_shapes_take_time_linear_in_the_input(string name, string unit)
    {
        const int Small = 4000;
        Time(Repeat(unit, 500)); // warm up
        double small = Time(Repeat(unit, Small));
        double large = Time(Repeat(unit, Small * 8));
        Assert.True(large < 200 + 24 * small, $"{name}: {Small} units took {small:F1} ms, {Small * 8} took {large:F1} ms");
        Assert.True(large < 20_000, $"{name}: {large:F0} ms");
    }

    // HTML 13.2.3.5: a CR LF pair decodes to one LF. The decoder once converted all the input behind each line
    // before cutting it at the line's carriage return, so a document with CRLF line ends cost quadratic time
    // (a 2 MB page of short lines took ten seconds).
    [Theory]
    [InlineData("text\r\n")]
    [InlineData("<p>a\r\n")]
    [InlineData("<!-- c\r\n -->\r\n")]
    [InlineData("<pre>a\r\nb</pre>\r\n")]
    [InlineData("\u00e9 line\r\n")]
    [InlineData("a\rb\r\r\n")]
    public void Crlf_line_ends_decode_in_linear_time(string unit)
    {
        const int Small = 20_000;
        Time(Repeat(unit, 500)); // warm up
        double small = Time(Repeat(unit, Small));
        double large = Time(Repeat(unit, Small * 8));
        Assert.True(large < 200 + 24 * small, $"{Small} units took {small:F1} ms, {Small * 8} took {large:F1} ms");
    }

    [Fact]
    public void Crlf_decodes_to_lf_and_maps_back_to_the_bytes()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Repeat("ab\r\ncd\re\r\n", 50));
        SourceText source = SourceText.Decode(bytes);
        Assert.Equal(Repeat("ab\ncd\ne\n", 50), source.Text);
        Assert.Equal(bytes.Length, source.Map.ToByteOffset(source.Text.Length));
        Assert.Equal(4, source.Map.ToByteOffset(3));
        Assert.Equal(7, source.Map.ToByteOffset(6));
    }

    [Fact]
    public void Nesting_deeper_than_the_open_element_limit_fails_cleanly()
    {
        Document ok = Parse(Repeat("<div>", 4000));
        Assert.Null(DocumentInvariants.Check(ok));
        var e = Assert.Throws<HtmlLimitExceededException>(() => Parse(Repeat("<div>", 5000)));
        Assert.Equal(nameof(ParseLimits.MaxOpenElements), e.LimitName);
    }

    [Fact]
    public void Deep_nesting_does_not_recurse_in_the_serializer_or_the_checks()
    {
        var limits = new ParseLimits { MaxOpenElements = 200_000 };
        Document doc = Parse(Repeat("<div>", 150_000), limits);
        Assert.Null(DocumentInvariants.Check(doc));
        Assert.True(doc.SerializeChildren(doc.Root).Length > 150_000 * 5);
        Assert.Equal(150_000 * 5, doc.ToLosslessBytes().Length);
    }

    [Fact]
    public void The_node_limit_is_checked_before_the_arena_grows()
    {
        var limits = new ParseLimits { MaxNodes = 1000 };
        var e = Assert.Throws<HtmlLimitExceededException>(() => Parse(Repeat("<p>x", 5000), limits));
        Assert.Equal(nameof(ParseLimits.MaxNodes), e.LimitName);
    }

    [Fact]
    public void The_total_attribute_limit_fails_cleanly()
    {
        var limits = new ParseLimits { MaxTotalAttributes = 1000 };
        var e = Assert.Throws<HtmlLimitExceededException>(() => Parse(Repeat("<i a=1 b=2 c=3></i>", 1000), limits));
        Assert.Equal(nameof(ParseLimits.MaxTotalAttributes), e.LimitName);
    }

    [Fact]
    public void A_tag_with_too_many_attributes_fails_before_reading_them_all()
    {
        var sb = new StringBuilder("<a");
        for (int i = 0; i < 100_000; i++)
        {
            sb.Append(" x").Append(i);
        }

        var e = Assert.Throws<HtmlLimitExceededException>(() => Parse(sb.ToString()));
        Assert.Equal(nameof(ParseLimits.MaxAttributesPerTag), e.LimitName);
    }

    [Fact]
    public void Long_formatting_chains_stay_within_the_active_formatting_limit()
    {
        // Each different attribute value is a different element for the Noah's Ark clause, so none are dropped.
        var sb = new StringBuilder();
        for (int i = 0; i < 5000; i++)
        {
            sb.Append("<b x=").Append(i).Append('>');
        }

        sb.Append("text</b>");
        try
        {
            Document doc = Parse(sb.ToString());
            Assert.Null(DocumentInvariants.Check(doc));
        }
        catch (HtmlLimitExceededException e)
        {
            Assert.True(e.LimitName is nameof(ParseLimits.MaxActiveFormattingElements) or nameof(ParseLimits.MaxOpenElements));
        }
    }

    [Fact]
    public void Many_selectedcontent_updates_are_bounded()
    {
        var limits = new ParseLimits { MaxNodes = 200_000 };
        string html = "<select>" + Repeat("<button><selectedcontent></button>", 500) + Repeat("<option selected>x", 2000);
        var e = Assert.Throws<HtmlLimitExceededException>(() => Parse(html, limits));
        Assert.Equal(nameof(ParseLimits.MaxNodes), e.LimitName);
    }

    [Fact]
    public void Large_inputs_parse_without_per_node_allocation_blowup()
    {
        string html = Repeat("<div class=a><p>text &amp; more</p><br></div>\n", 100_000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Document doc = Parse(html);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(doc.NodeCount > 400_000);
        // Columns only: well under 200 bytes per node, plus the input copies.
        Assert.True(bytes < (long)doc.NodeCount * 200 + (long)html.Length * 16, $"{bytes} bytes for {doc.NodeCount} nodes");
    }
}
