using System.Collections.Generic;
using System.Text.Json.Nodes;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Css;

public class AnPlusBTests
{
    [Fact]
    public void Suite()
    {
        JsonArray cases = CssJson.LoadCases("css-parsing-tests/An+B.json");
        var failures = new List<string>();
        for (int i = 0; i + 1 < cases.Count; i += 2)
        {
            string input = cases[i]!.GetValue<string>();
            JsonArray? expected = cases[i + 1]?.AsArray();
            bool ok = AnPlusB.TryParse(input, out AnPlusB r);
            string actual = ok ? $"[{r.A}, {r.B}]" : "null";
            string want = expected is null ? "null" : $"[{expected[0]}, {expected[1]}]";
            if (actual != want)
            {
                failures.Add($"'{input}' => {actual}, expected {want}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {cases.Count / 2} failed:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData(2, 1, 1, true)]
    [InlineData(2, 1, 2, false)]
    [InlineData(-1, 3, 3, true)]
    [InlineData(-1, 3, 4, false)]
    [InlineData(0, 4, 4, true)]
    [InlineData(3, 0, 0, true)]
    public void Matches(int a, int b, int position, bool expected) => Assert.Equal(expected, new AnPlusB(a, b).Matches(position));
}
