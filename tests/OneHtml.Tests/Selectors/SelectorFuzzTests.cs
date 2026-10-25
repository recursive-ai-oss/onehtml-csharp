using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Fuzz;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Selectors;

public class SelectorFuzzTests
{
    /// <summary>Minimized inputs (selector, a zero byte, an optional page) that once broke an invariant, or sit on a limit.</summary>
    public static IEnumerable<object[]> Regressions()
    {
        yield return [":is(u\n,\n/[!/ol,.lisu) > [hidden]\0"];
        yield return [".foo\\ba #\\ba\0<p class='foo\u00ba' id='\u00ba'>"];
        yield return [".a:is(\u00be\">\u0019\u00f5\n>b\u00f6!\t\u00ce:has(.)\0"];
        yield return ["a:has(:is(b, c))\0<div><b>"];
        yield return [":not(:has(> a ~ b)) + :nth-last-child(2n+1 of :is(a, b))\0<a><b><a><b>"];
        yield return ["[a|=b i], [*|a], [|a], *|*, |a\0<p a=B>"];
        yield return ["::part(a):is(:hover, x)\0"];
        yield return [":is(, ,a,,)\0<a>"];
        yield return [":lang(\"*-US\", en)\0<p lang=en-us>"];
        yield return [":dir(rtl):heading(1, 3)\0<h1 dir=auto>\u05d0</h1>"];
        yield return ["input:valid:in-range:default\0<form><input type=number min=1 max=3 value=2><button>"];
        yield return [":nth-child(n+1 of :has(+ :nth-of-type(2)))\0<p><p><p>"];
        yield return [".a:not(.a)\0<div class=a>"];
    }

    [Theory]
    [MemberData(nameof(Regressions))]
    public void Regression_inputs_pass_the_invariants(string input)
    {
        SelectorTarget.Run(Encoding.UTF8.GetBytes("\0" + input));
    }

    [Fact]
    public void Fuzz_target_survives_mutated_inputs()
    {
        double seconds = double.TryParse(Environment.GetEnvironmentVariable("ONEHTML_FUZZ_SECONDS"), out double s) ? s : 3;
        string js = File.ReadAllText(Path.Combine(TestPaths.TestData, "wpt", "selectors", "dom", "nodes", "selectors.js"));
        var seeds = new List<byte[]>();
        foreach (object? o in JsReader.ReadVarArray(js, "validSelectors")!.Concat(JsReader.ReadVarArray(js, "invalidSelectors")!))
        {
            var d = (Dictionary<string, object?>)o!;
            seeds.Add(Encoding.UTF8.GetBytes("\0" + d["selector"] + "\0"));
        }

        string content = File.ReadAllText(Path.Combine(TestPaths.TestData, "wpt", "selectors", "dom", "nodes", "ParentNode-querySelector-All-content.html"));
        seeds.Add(Encoding.UTF8.GetBytes("\0div:has(> p) ~ :is(div, .x):not(:empty)\0" + content[..Math.Min(1500, content.Length)]));

        var rng = new Random(11);
        var sw = Stopwatch.StartNew();
        long runs = 0;
        byte[] splice = Encoding.UTF8.GetBytes("{}()[];:,\\\"'*|~+>^$=# .\n-0n\0<>/");
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
                        input = [.. input[..pos], .. donor.AsSpan(from, Math.Min(24, donor.Length - from)), .. input[pos..]];
                        break;
                }
            }

            try
            {
                SelectorTarget.Run(input);
            }
            catch (Exception ex)
            {
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "selector-crash.bin"), input);
                throw new InvalidOperationException("finding for input " + Convert.ToHexString(input), ex);
            }

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
