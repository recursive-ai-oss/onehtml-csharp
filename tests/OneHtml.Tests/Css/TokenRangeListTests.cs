using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Css;

public class TokenRangeListTests
{
    [Fact]
    public void ToList_covers_the_same_tokens_as_the_range()
    {
        TokenRange r = TokenRange.Parse("1px calc(2px + 3px) red");
        ComponentValueList list = r.ToList();
        Assert.Equal(r.RawText(), new string(list.Raw));
        Assert.Equal(5, list.Count); // whitespace counts as a value

        TokenRange sub = r.Sub(r.SkipWs(r.Next(r.From)), r.To).Trim();
        Assert.Equal(sub.RawText(), new string(sub.ToList().Raw));
        Assert.Equal(3, sub.ToList().Count);
    }

    [Fact]
    public void ToList_of_default_or_out_of_range_is_empty()
    {
        Assert.True(default(TokenRange).ToList().IsEmpty);
        TokenRange r = TokenRange.Parse("a b");
        Assert.True(new TokenRange(r.Tree, 5000, 6000).ToList().IsEmpty);
        Assert.True(new TokenRange(r.Tree, -4, 2).ToList().Count <= 2);
    }
}
