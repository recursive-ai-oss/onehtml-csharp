using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RecursiveAI.OneHtml.Fuzz;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Css;

/// <summary>
/// A short coverage-blind mutation run of the value-parsing fuzz target, seeded with the value strings of the vendored
/// web-platform-tests files, plus the minimized inputs that earlier runs found. <c>ONEHTML_FUZZ_SECONDS</c> lengthens it.
/// </summary>
public partial class ValueFuzzTests
{
    [GeneratedRegex("""["'`]([^"'`\n]{3,160}\([^"'`\n]*)["'`]""", RegexOptions.CultureInvariant)]
    private static partial Regex Quoted();

    private static List<byte[]> Seeds()
    {
        var seeds = new List<byte[]>();
        foreach (string dir in new[] { "css-color/parsing", "css-values", "mediaqueries", "css-variables" })
        {
            foreach (string file in Directory.GetFiles(CssJson.TestDataPath("wpt", "css", dir), "*.html"))
            {
                int taken = 0;
                foreach (Match m in Quoted().Matches(File.ReadAllText(file)))
                {
                    if (taken++ % 7 == 0)
                    {
                        seeds.Add(Encoding.UTF8.GetBytes("\0" + m.Groups[1].Value));
                    }
                }
            }
        }

        foreach (string s in new[]
        {
            "var(--a, var(--b, red))\n--a: var(--b)\n--b: var(--a)", "calc(1px + calc(2px * (3 + 4)))", "(width >= 400px) and (prefers-color-scheme: dark), print",
            "color-mix(in oklch longer hue, hsl(none 50% 50%) 30%, lab(50 20 20 / .5))", "rgb(from hsl(from red h s l) r g b / alpha)",
            "supports(selector(a:has(b)))", "clamp(none, 1px, 2px)", "10px 2em 3% auto",
        })
        {
            seeds.Add(Encoding.UTF8.GetBytes("\0" + s));
        }

        return seeds;
    }

    [Fact]
    public void Value_target_survives_mutated_inputs()
    {
        double seconds = double.TryParse(Environment.GetEnvironmentVariable("ONEHTML_FUZZ_SECONDS"), out double s) ? s : 4;
        List<byte[]> seeds = Seeds();
        Assert.True(seeds.Count > 100);
        var rng = new Random(11);
        var sw = Stopwatch.StartNew();
        long runs = 0;
        byte[] splice = Encoding.UTF8.GetBytes("{}()[];:,\\\"'/*!@#-+.<>%\n \0eE0123456789");
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

            input[0] = (byte)rng.Next(256);
            try
            {
                ValueTarget.Run(input);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{ex.GetType().Name}: {ex.Message} on input {Convert.ToHexString(input)}", ex);
            }

            runs++;
        }

        Console.WriteLine($"value fuzz: {runs} executions in {sw.Elapsed.TotalSeconds:F0} s");
        Assert.True(runs > 100);
    }
}
