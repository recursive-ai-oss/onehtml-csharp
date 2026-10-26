using System.Text;
using Xunit;

namespace RecursiveAI.OneHtml.Tests;

/// <summary>
/// A carriage return that comes from a character reference sits in the tree as U+000D. The serializer writes it as is
/// (HTML 13.3 "serializing HTML fragments" escapes only &amp;, no-break space, quotes and angle brackets), and the next
/// parse's input stream preprocessing (13.2.3.5) turns it into U+000A. So such a tree cannot survive
/// serialize, parse, serialize, in attribute values as well as in text, and the fuzz oracle must say so for both.
/// </summary>
public class SerializeRoundTripOracleTests
{
    private static Document Parse(string html) => Document.Parse(Encoding.UTF8.GetBytes(html));

    [Theory]
    [InlineData("<div a=\"x&#xD;y\">t</div>")]
    [InlineData("<div a=x&#xD;&#xA;y>t</div>")]
    [InlineData("<div>x&#xD;y</div>")]
    public void A_carriage_return_from_a_reference_is_not_expected_to_round_trip(string html)
    {
        Document doc = Parse(html);
        string first = doc.SerializeChildren(doc.Root, false);
        Assert.Contains('\r', first);
        Document again = Parse(first);
        Assert.NotEqual(first, again.SerializeChildren(again.Root, false));
        Assert.False(DocumentInvariants.ExpectsStableRoundTrip(doc));
    }

    [Fact]
    public void Plain_attribute_values_still_round_trip()
    {
        Document doc = Parse("<div a=\"x&amp;y\" b=\"p\nq\">t</div>");
        Assert.True(DocumentInvariants.ExpectsStableRoundTrip(doc));
        string first = doc.SerializeChildren(doc.Root, false);
        Document again = Parse(first);
        Assert.Equal(first, again.SerializeChildren(again.Root, false));
    }
}
