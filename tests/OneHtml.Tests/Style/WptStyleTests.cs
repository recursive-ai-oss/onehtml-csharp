using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RecursiveAI.OneHtml.Tests.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// Replays the static parts of the web-platform-tests files for the cascade (<c>css-cascade</c>, <c>css-variables</c>,
/// <c>css-nesting</c>, the specificity files of <c>selectors</c>) and the computed-value files of the typed properties
/// (see <see cref="StyleWptRunner"/>). Failures must be listed in <c>testdata/css/wpt-style-expectations.txt</c> with a
/// reason; anything else fails the test. Counts per directory go to the console and, when <c>ONEHTML_ARTIFACTS</c> is set,
/// to <c>wpt-style-report.txt</c>, with every result in <c>wpt-style-results.txt</c>.
/// </summary>
public class WptStyleTests
{
    private static readonly string[] Directories =
    [
        "css-cascade", "css-variables", "css-nesting", "selectors",
        "css-fonts/parsing", "css-display/parsing", "css-position/parsing", "css-overflow/parsing", "css-ui/parsing",
    ];

    private sealed record Expectation(Regex File, Regex Test, string Reason)
    {
        public int Matches { get; set; }
    }

    private static string DataRoot { get; } = Path.GetFullPath(Path.Combine(TestPaths.TestData, "css", "wpt-style"));

    private static List<Expectation> LoadExpectations()
    {
        var list = new List<Expectation>();
        foreach (string line in File.ReadAllLines(Path.Combine(TestPaths.TestData, "css", "wpt-style-expectations.txt")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('\t');
            Assert.True(f.Length == 3, "expectation line needs 3 tab-separated fields: " + line);
            list.Add(new Expectation(new Regex(f[0], RegexOptions.CultureInvariant), new Regex(f[1], RegexOptions.CultureInvariant | RegexOptions.Singleline), f[2]));
        }

        return list;
    }

    internal static List<StyleWptResult> RunAll()
    {
        var all = new List<StyleWptResult>();
        foreach (string dir in Directories)
        {
            string path = Path.Combine(DataRoot, "css", dir.Replace('/', Path.DirectorySeparatorChar));
            foreach (string file in Directory.EnumerateFiles(path).Where(f => f.EndsWith(".html", StringComparison.Ordinal) || f.EndsWith(".xht", StringComparison.Ordinal) || f.EndsWith(".xhtml", StringComparison.Ordinal)).OrderBy(f => f, StringComparer.Ordinal))
            {
                try
                {
                    all.AddRange(StyleWptRunner.RunFile(dir, DataRoot, file));
                }
                catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
                {
                    all.Add(new StyleWptResult(dir, Path.GetFileName(file), "(file)", WptOutcome.Fail, "exception: " + ex.GetType().Name + ": " + ex.Message));
                }
            }
        }

        return all;
    }

    [Fact]
    public void Wpt_cascade_matches_expectations()
    {
        List<Expectation> expectations = LoadExpectations();
        List<StyleWptResult> results = RunAll();
        var report = new StringBuilder();
        var detail = new StringBuilder();
        var unexpected = new List<string>();
        int tp = 0, tf = 0, tn = 0, tk = 0;
        foreach (IGrouping<string, StyleWptResult> g in results.GroupBy(r => r.Directory))
        {
            int pass = 0, fail = 0, na = 0, known = 0;
            var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (StyleWptResult r in g)
            {
                switch (r.Outcome)
                {
                    case WptOutcome.Pass:
                        pass++;
                        detail.AppendLine(CultureInfo.InvariantCulture, $"PASS\t{r.Directory}/{r.File}\t{r.Test}");
                        break;
                    case WptOutcome.NotApplicable:
                        na++;
                        string key = Regex.Replace(r.Detail, @"needs script: .*", "needs script", RegexOptions.CultureInvariant);
                        key = Regex.Replace(key, @": .*", string.Empty, RegexOptions.CultureInvariant);
                        reasons[key] = reasons.GetValueOrDefault(key) + 1;
                        detail.AppendLine(CultureInfo.InvariantCulture, $"N/A\t{r.Directory}/{r.File}\t{r.Test}\t{r.Detail}");
                        break;
                    default:
                        fail++;
                        Expectation? e = expectations.FirstOrDefault(x => x.File.IsMatch(r.File) && x.Test.IsMatch(r.Test));
                        if (e is null)
                        {
                            unexpected.Add($"{r.Directory}/{r.File} [{r.Test}]: {r.Detail}");
                            detail.AppendLine(CultureInfo.InvariantCulture, $"FAIL\t{r.Directory}/{r.File}\t{r.Test}\t{r.Detail}");
                        }
                        else
                        {
                            e.Matches++;
                            known++;
                            detail.AppendLine(CultureInfo.InvariantCulture, $"KNOWN\t{r.Directory}/{r.File}\t{r.Test}\t{r.Detail}\t{e.Reason}");
                        }

                        break;
                }
            }

            tp += pass;
            tf += fail;
            tn += na;
            tk += known;
            report.AppendLine(CultureInfo.InvariantCulture, $"{g.Key}: passed {pass}, failed {fail} (documented {known}), not applicable {na}");
            foreach ((string reason, int n) in reasons.OrderByDescending(kv => kv.Value).Take(10))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"    n/a {n,5}  {reason}");
            }
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"total: passed {tp}, failed {tf} (documented {tk}), not applicable {tn}");
        foreach (Expectation e in expectations.Where(x => x.Matches == 0))
        {
            report.AppendLine("unused expectation: " + e.File + " " + e.Test);
        }

        string? artifacts = Environment.GetEnvironmentVariable("ONEHTML_ARTIFACTS");
        if (!string.IsNullOrEmpty(artifacts))
        {
            Directory.CreateDirectory(artifacts);
            File.WriteAllText(Path.Combine(artifacts, "wpt-style-report.txt"), report.ToString());
            File.WriteAllText(Path.Combine(artifacts, "wpt-style-results.txt"), detail.ToString());
            File.WriteAllLines(Path.Combine(artifacts, "wpt-style-unexpected.txt"), unexpected);
        }

        Console.WriteLine(report.ToString());
        Assert.True(unexpected.Count == 0, unexpected.Count.ToString(CultureInfo.InvariantCulture) + " undocumented failures; first: " + string.Join("\n", unexpected.Take(15)));
    }
}
