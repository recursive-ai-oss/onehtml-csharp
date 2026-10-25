using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Css;

/// <summary>The css-parsing-tests <c>color_*.json</c> files: input text to the serialization of the parsed color, or null for invalid.</summary>
public class CssColorSuiteTests
{
    public static TheoryData<string> Files =>
    [
        "color_keywords_3", "color_keywords_4", "color_hexadecimal_3", "color_hexadecimal_4", "color_hsl_3", "color_hsl_4",
        "color_hwb_4", "color_function_4", "color_lab_4", "color_lch_4", "color_oklab_4", "color_oklch_4",
    ];

    [Theory]
    [MemberData(nameof(Files))]
    public void Suite(string file)
    {
        JsonArray cases = CssJson.LoadCases($"css-parsing-tests/{file}.json");
        var failures = new List<string>();
        for (int i = 0; i + 1 < cases.Count; i += 2)
        {
            string input = cases[i]!.GetValue<string>();
            string? expected = cases[i + 1]?.GetValue<string>();
            bool ok = CssColor.TryParse(input, out CssColor c);
            string? actual = ok ? c.ToCss() : null;
            if (ok && actual!.Contains("none", StringComparison.Ordinal) && expected is not null && expected.StartsWith("rgb", StringComparison.Ordinal))
            {
                // The suite predates missing components: it reads none as 0. Compare the color with each none taken as 0.
                (double r, double g, double b, double al) = c.ToSrgb();
                actual = al >= 1
                    ? FormattableString.Invariant($"rgb({r * 255}, {g * 255}, {b * 255})")
                    : FormattableString.Invariant($"rgba({r * 255}, {g * 255}, {b * 255}, {al})");
            }

            if (actual != expected && !SameUpToEightBits(actual, expected))
            {
                failures.Add($"{input} => {actual ?? "null"}, expected {expected ?? "null"}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {cases.Count / 2} failed:\n" + string.Join("\n", failures.GetRange(0, System.Math.Min(failures.Count, 25))));
    }

    /// <summary>
    /// The suite was written before CSS Color 4 fixed the legacy form at 8-bit channels (§16.2.2) and an 8-bit alpha
    /// (§16.1.1), so it expects unrounded channels. Legacy rgb() results are compared up to that rounding.
    /// </summary>
    private static bool SameUpToEightBits(string? actual, string? expected)
    {
        if (actual is not null && expected is not null && (actual.StartsWith("lab(", StringComparison.Ordinal) || actual.StartsWith("lch(", StringComparison.Ordinal) || actual.StartsWith("oklab(", StringComparison.Ordinal) || actual.StartsWith("oklch(", StringComparison.Ordinal)))
        {
            // The suite keeps out-of-range lightness and negative chroma; CSS Color 4 clamps them when the value is parsed
            // (the sections on lab(), lch(), oklab() and oklch()), so the expectation is read through the same clamp.
            return CssColor.TryParse(expected, out CssColor clamped) && clamped.ToCss() == actual;
        }

        if (actual is null || expected is null || !actual.StartsWith("rgb", StringComparison.Ordinal) || !expected.StartsWith("rgb", StringComparison.Ordinal))
        {
            return false;
        }

        double[] a = Numbers(actual);
        double[] e = Numbers(expected);
        if (a.Length != e.Length || actual.StartsWith("rgba", StringComparison.Ordinal) != expected.StartsWith("rgba", StringComparison.Ordinal))
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (Math.Abs(a[i] - e[i]) > (i < 3 ? 0.5 : 0.005))
            {
                return false;
            }
        }

        return true;
    }

    private static double[] Numbers(string s) =>
        [.. s[(s.IndexOf('(', StringComparison.Ordinal) + 1)..^1].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture))];
}
