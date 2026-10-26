using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Fuzz;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Css;

public class CssLimitsAndFuzzTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Input_longer_than_the_limit_is_refused_before_tokenizing()
    {
        var o = new CssParseOptions { MaxInputLength = 10 };
        CssLimitExceededException e = Assert.Throws<CssLimitExceededException>(() => CssTree.ParseStylesheet(new string('a', 11), o, Ct));
        Assert.Equal(CssLimit.InputLength, e.Limit);
        Assert.Equal(10, e.Maximum);
        CssTree.ParseStylesheet(new string('a', 10), o, Ct);
    }

    [Fact]
    public void Byte_input_much_longer_than_the_limit_is_refused_before_decoding()
    {
        var o = new CssParseOptions { MaxInputLength = 10 };
        var provider = WhatwgCssEncodingProvider.Instance;
        Assert.Throws<CssLimitExceededException>(() => Stylesheet.Parse(new byte[41], provider, options: o, cancellationToken: Ct));
    }

    [Fact]
    public void Token_count_is_limited()
    {
        var o = new CssParseOptions { MaxTokens = 3 };
        Assert.Equal(3, CssTree.ParseComponentValueList("a b", o, Ct).TokenCount);
        CssLimitExceededException e = Assert.Throws<CssLimitExceededException>(() => CssTree.ParseComponentValueList("a b c", o, Ct));
        Assert.Equal(CssLimit.TokenCount, e.Limit);
    }

    [Fact]
    public void Nesting_past_the_limit_degrades_to_unclosed_blocks_and_stays_lossless()
    {
        var o = new CssParseOptions { MaxNestingDepth = 3 };
        Assert.False(CssTree.ParseComponentValueList("((()))", o, Ct).NestingLimitReached);
        Assert.False(CssTree.ParseComponentValueList("a(b[c{}])", o, Ct).NestingLimitReached);

        // The fourth opener is not matched; like a block still open at EOF it swallows the rest of the input.
        CssTree values = CssTree.ParseComponentValueList("(((()))) x", o, Ct);
        Assert.True(values.NestingLimitReached);
        Assert.Equal(1, values.Values.Count);

        foreach (string css in new[] { "a{b{c{d{}}}} e{}", "a{b:c(d(e(f(", "a{b{c{d{e:f}}}}}}" })
        {
            CssTree t = CssTree.ParseStylesheet(css, o, Ct);
            Assert.True(t.NestingLimitReached);
            Assert.Equal(css, t.Text);
        }
    }

    [Fact]
    public void Random_bracket_soup_with_a_tiny_depth_limit_never_throws_and_round_trips()
    {
        string[] pieces = ["{", "}", "(", ")", "[", "]", "a", "b:c", ";", " ", "x(", "@m ", "'s'", "\n"];
        var rng = new Random(12345);
        var o = new CssParseOptions { MaxNestingDepth = 4 };
        for (int n = 0; n < 3000; n++)
        {
            string css = string.Concat(Enumerable.Range(0, rng.Next(1, 40)).Select(_ => pieces[rng.Next(pieces.Length)]));
            Stylesheet sheet = Stylesheet.Parse(css, null, o);
            Assert.Equal(css, sheet.ToSourceText());
            CssTree.ParseDeclarationList(css, o, Ct);
            CssTree.ParseComponentValueList(css, o, Ct);
        }
    }

    [Fact]
    public void Very_deep_unclosed_blocks_are_a_stylesheet_not_an_exception()
    {
        // Unclosed blocks are closed at end of input (CSS Syntax 5.4.8), so 5000 of them are ordinary, if odd, input.
        string css = "@media screen{" + string.Concat(Enumerable.Repeat("a{", 5000)) + "b:c";
        Stylesheet sheet = Stylesheet.Parse(css);
        Assert.Equal(css, sheet.ToSourceText());
        Assert.Single(sheet.Rules);
    }

    [Fact]
    public void Depth_at_the_default_limit_does_not_overflow_the_stack()
    {
        // Legal at the default limit of 256; the parser keeps its own stack, so even a thread with a tiny stack copes.
        string deep = string.Concat(Enumerable.Repeat("a{", 250)) + "b:c;" + new string('}', 250);
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                CssRoundTripTests.Validate(CssTree.ParseStylesheet(deep, cancellationToken: Ct));
                CssRoundTripTests.Validate(CssTree.ParseComponentValueList(string.Concat(Enumerable.Repeat("f(", 250)), cancellationToken: Ct));
            }
            catch (Exception e)
            {
                failure = e;
            }
        }, 256 * 1024);
        t.Start();
        t.Join();
        Assert.Null(failure);
    }

    [Fact]
    public void Cancellation_is_honoured()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        string big = string.Concat(Enumerable.Repeat("a{b:c} ", 20000));
        Assert.ThrowsAny<OperationCanceledException>(() => CssTree.ParseStylesheet(big, cancellationToken: cts.Token));
    }

    [Theory]
    [InlineData("a:b{}", "x{", "}")]
    [InlineData("a:b ", "x{", "}")]
    [InlineData("a:b(", "x{", "}")]
    [InlineData(";", "x{", "}")]
    [InlineData("a;", "", "")]
    [InlineData("@a ", "", "")]
    [InlineData("a ", "", "")]
    [InlineData("{", "", "")]
    [InlineData("}", "", "")]
    [InlineData("--a:{", "x{", "}")]
    [InlineData("a<!--", "", "")]
    public void Repeated_constructs_parse_in_linear_time(string unit, string prefix, string suffix)
    {
        // 200k repetitions: anything quadratic in the repetition count would take minutes, not milliseconds.
        string css = prefix + string.Concat(Enumerable.Repeat(unit, 200_000)) + suffix;
        var sw = Stopwatch.StartNew();
        CssTree t = CssTree.ParseStylesheet(css, new CssParseOptions { MaxNestingDepth = 1_000_000, MaxInputLength = 1 << 26, MaxTokens = 1 << 24 }, Ct);
        CssTree.ParseDeclarationList(css, new CssParseOptions { MaxNestingDepth = 1_000_000, MaxInputLength = 1 << 26, MaxTokens = 1 << 24 }, Ct);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), sw.Elapsed.ToString());
        Assert.Equal(css, CssSourceWriter.Write(t));
    }

    // Found by the mutation smoke: members that do not apply to a node's kind must be empty, not throw.
    [Fact]
    public void Members_that_do_not_apply_to_a_node_kind_are_empty()
    {
        CssTree t = CssTree.ParseStylesheet("a { b: c } @d e; f", cancellationToken: Ct);
        foreach (CssNode n in t.Nodes)
        {
            Assert.Equal(n.Kind == CssNodeKind.Declaration, !n.Value.IsEmpty);
            _ = n.ValueText.Length + n.PreludeText.Length + n.Prelude.Raw.Length + n.BlockValues.Raw.Length;
        }

        CssNode rule = t.Nodes.First();
        Assert.True(rule.Value.IsEmpty);
        Assert.Equal(0, rule.ValueText.Length);
        CssNode decl = rule.Children.First();
        Assert.True(decl.Prelude.IsEmpty);
        Assert.Equal(0, decl.PreludeText.Length);
    }

    // ---- mutation smoke for the fuzz target ----

    [Fact]
    public void Fuzz_target_survives_mutated_inputs()
    {
        double seconds = double.TryParse(Environment.GetEnvironmentVariable("ONEHTML_FUZZ_SECONDS"), out double s) ? s : 3;
        var seeds = new List<byte[]>();
        foreach (string file in Directory.GetFiles(CssJson.TestDataPath("own"), "*.css"))
        {
            seeds.Add(Encoding.UTF8.GetBytes("\0" + File.ReadAllText(file)));
        }

        foreach (string css in new[]
        {
            "a{b:c}", "@media x{y{z:w}}", "a:b;c:d!important", "url(\"x\") u+1? 1e3 \\\n", "--a:{;}", "<!-- a --> /* c */ 'x\n",
        })
        {
            seeds.Add(Encoding.UTF8.GetBytes("\0" + css));
        }

        var rng = new Random(7);
        var sw = Stopwatch.StartNew();
        long runs = 0;
        byte[] splice = Encoding.UTF8.GetBytes("{}()[];:,\\\"'/*!@#-+.<>\n \0");
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            byte[] input = [.. seeds[rng.Next(seeds.Count)]];
            int edits = rng.Next(1, 8);
            for (int e = 0; e < edits && input.Length > 1; e++)
            {
                int pos = rng.Next(1, input.Length);
                switch (rng.Next(5))
                {
                    case 0: input[pos] = (byte)rng.Next(256); break;
                    case 1: input[pos] = splice[rng.Next(splice.Length)]; break;
                    case 2: input = [.. input[..pos], .. input[(pos + 1)..]]; break;
                    case 3: input = [.. input[..pos], splice[rng.Next(splice.Length)], .. input[pos..]]; break;
                    default:
                        byte[] donor = seeds[rng.Next(seeds.Count)];
                        int from = rng.Next(1, donor.Length);
                        input = [.. input[..pos], .. donor.AsSpan(from, Math.Min(32, donor.Length - from)), .. input[pos..]];
                        break;
                }
            }

            if (rng.Next(8) == 0)
            {
                input[0] = 1; // UTF-16 interpretation of the rest
            }

            CssTarget.Run(input);
            runs++;
        }

        string? log = Environment.GetEnvironmentVariable("ONEHTML_FUZZ_LOG");
        if (log is not null)
        {
            File.WriteAllText(log, $"{runs} executions in {sw.Elapsed.TotalSeconds:F0} s\n");
        }

        Assert.True(runs > 0);
    }
}
