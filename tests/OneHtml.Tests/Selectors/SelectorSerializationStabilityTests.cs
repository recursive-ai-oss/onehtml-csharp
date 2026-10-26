using RecursiveAI.OneHtml.Selectors;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Selectors;

/// <summary>parse, serialize, parse, serialize must be a fixed point for forgiving lists that hold unparseable items.</summary>
public class SelectorSerializationStabilityTests
{
    private static string Stable(string selector)
    {
        string once = SelectorList.Parse(selector).ToString();
        Assert.Equal(once, SelectorList.Parse(once).ToString());
        return once;
    }

    [Theory]
    [InlineData(":where(\"\n\",\n")]
    [InlineData(":is(\"a\nb\", div)")]
    [InlineData(":is(\"\n\"\"\",\n")]
    [InlineData("/**/:where(\"\n\",\n")]
    [InlineData(":is(a, \"x\n, b)")]
    public void A_bad_string_in_a_forgiving_item_does_not_change_the_list_on_reparse(string selector)
    {
        Stable(selector);
    }

    [Theory]
    [InlineData(":where([ \uFFFD='")]
    [InlineData(":is([a='x")]
    [InlineData(":is(a, [b=\"c")]
    [InlineData(":is(:has(:is([x=')")]
    public void Repairing_an_item_while_writing_it_does_not_make_it_a_valid_selector(string selector)
    {
        Stable(selector);
    }

    [Fact]
    public void An_item_that_stays_invalid_when_written_out_is_kept()
    {
        // The unclosed bracket is invalid because of the "!" token inside it, so closing it keeps it invalid.
        Assert.Equal(":is(div, [a!b])", Stable(":is(div, [a!b"));
    }

    [Fact]
    public void An_invalid_but_well_formed_item_is_kept_as_written()
    {
        Assert.Equal(":is(div, :nope)", Stable(":is(div, :nope)"));
        Assert.Equal(":is(div, \"s\")", Stable(":is(div, \"s\")"));
    }
}
