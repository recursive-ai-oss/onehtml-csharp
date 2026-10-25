using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// Shrinks a page to a small snippet that still shows a given divergence: first a window around the divergence,
/// then delta debugging over lines and over characters, within a call and time budget.
/// </summary>
internal static class Reducer
{
    private const int MaxCalls = 6000;
    private static readonly TimeSpan s_maxTime = TimeSpan.FromSeconds(20);

    public static string Reduce(string text, int hint, Func<string, bool> stillDiverges)
    {
        var clock = Stopwatch.StartNew();
        int calls = 0;
        bool Test(string s)
        {
            calls++;
            return stillDiverges(s);
        }

        bool Budget() => calls < MaxCalls && clock.Elapsed < s_maxTime;

        if (hint >= 0 && text.Length > 4000)
        {
            foreach (int radius in new[] { 1500, 6000, 24000 })
            {
                int from = Math.Max(0, hint - radius);
                int to = Math.Min(text.Length, hint + radius);
                string window = text[from..to];
                if (window.Length < text.Length && Test(window))
                {
                    text = window;
                    break;
                }
            }
        }

        List<string> lines = SplitLines(text);
        lines = Ddmin(lines, parts => Test(string.Concat(parts)), Budget);
        string joined = string.Concat(lines);

        var chars = new List<string>(joined.Length);
        for (int i = 0; i < joined.Length; i++)
        {
            if (char.IsHighSurrogate(joined[i]) && i + 1 < joined.Length)
            {
                chars.Add(joined.Substring(i++, 2));
            }
            else
            {
                chars.Add(joined[i].ToString());
            }
        }

        chars = Ddmin(chars, parts => Test(string.Concat(parts)), Budget);
        return string.Concat(chars);
    }

    private static List<string> SplitLines(string s)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\n')
            {
                lines.Add(s[start..(i + 1)]);
                start = i + 1;
            }
        }

        if (start < s.Length)
        {
            lines.Add(s[start..]);
        }

        return lines;
    }

    /// <summary>Zeller's ddmin, complement form only: drop one of n chunks at a time while the test still holds.</summary>
    private static List<string> Ddmin(List<string> units, Func<List<string>, bool> test, Func<bool> budget)
    {
        int n = 2;
        while (units.Count >= 2 && budget())
        {
            int size = (units.Count + n - 1) / n;
            bool reduced = false;
            for (int start = 0; start < units.Count && budget(); start += size)
            {
                var complement = new List<string>(units.Count);
                for (int i = 0; i < units.Count; i++)
                {
                    if (i < start || i >= start + size)
                    {
                        complement.Add(units[i]);
                    }
                }

                if (complement.Count > 0 && test(complement))
                {
                    units = complement;
                    n = Math.Max(n - 1, 2);
                    reduced = true;
                    break;
                }
            }

            if (!reduced)
            {
                if (n >= units.Count)
                {
                    break;
                }

                n = Math.Min(units.Count, n * 2);
            }
        }

        // A single unit that is the whole input could still be empty-able, but an empty input never diverges here.
        return units;
    }
}
