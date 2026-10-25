using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Css;

/// <summary>
/// Replays the web-platform-tests parsing, serialization and evaluation calls of <c>css/css-color/parsing</c>,
/// <c>css/css-values</c>, <c>css/css-variables</c> and <c>css/mediaqueries</c> against the library. Failures must be
/// listed in <c>testdata/css/wpt-expectations.txt</c> with a reason; anything else fails the test. The counts per
/// directory are written to the console and, when <c>ONEHTML_ARTIFACTS</c> is set, to <c>wpt-values-report.txt</c> there.
/// </summary>
public class CssWptValueTests
{
    private static readonly string[] Directories = ["css-color/parsing", "css-values", "css-variables", "mediaqueries"];

    private sealed record Expectation(Regex File, Regex Call, Regex Input, string Reason)
    {
        public int Matches { get; set; }
    }

    private static List<Expectation> LoadExpectations()
    {
        var list = new List<Expectation>();
        foreach (string line in File.ReadAllLines(CssJson.TestDataPath("wpt-expectations.txt")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('\t');
            Assert.True(f.Length == 4, "expectation line needs 4 tab-separated fields: " + line);
            list.Add(new Expectation(new Regex(f[0], RegexOptions.CultureInvariant), new Regex(f[1], RegexOptions.CultureInvariant), new Regex(f[2], RegexOptions.CultureInvariant | RegexOptions.Singleline), f[3]));
        }

        return list;
    }

    [Fact]
    public void Wpt_value_parsing_matches_expectations()
    {
        List<Expectation> expectations = LoadExpectations();
        var report = new StringBuilder();
        var unexpected = new List<string>();
        int totalPass = 0, totalFail = 0, totalNa = 0, known = 0;
        foreach (string dir in Directories)
        {
            string root = CssJson.TestDataPath("wpt", "css", dir);
            int pass = 0, fail = 0, na = 0, expectedFail = 0;
            var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(root, "*.html").OrderBy(p => p, StringComparer.Ordinal))
            {
                foreach (WptResult r in WptValueRunner.RunFile(dir, path))
                {
                    switch (r.Outcome)
                    {
                        case WptOutcome.Pass:
                            pass++;
                            break;
                        case WptOutcome.NotApplicable:
                            na++;
                            reasons[r.Detail] = reasons.GetValueOrDefault(r.Detail) + 1;
                            break;
                        default:
                            fail++;
                            Expectation? e = expectations.FirstOrDefault(x => x.File.IsMatch(r.File) && x.Call.IsMatch(r.Call) && x.Input.IsMatch(r.Input));
                            if (e is null)
                            {
                                unexpected.Add($"{r.File} {r.Call}({r.Input}): {r.Detail}");
                            }
                            else
                            {
                                e.Matches++;
                                expectedFail++;
                            }

                            break;
                    }
                }
            }

            totalPass += pass;
            totalFail += fail;
            totalNa += na;
            known += expectedFail;
            report.AppendLine(CultureInfo.InvariantCulture, $"{dir}: passed {pass}, failed {fail} (documented {expectedFail}), not applicable {na}");
            foreach ((string reason, int n) in reasons.OrderByDescending(kv => kv.Value).Take(8))
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"    n/a {n,5}  {reason}");
            }
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"total: passed {totalPass}, failed {totalFail} (documented {known}), not applicable {totalNa}");
        foreach (Expectation e in expectations.Where(x => x.Matches == 0))
        {
            report.AppendLine("unused expectation: " + e.Reason);
        }

        string? artifacts = Environment.GetEnvironmentVariable("ONEHTML_ARTIFACTS");
        if (!string.IsNullOrEmpty(artifacts))
        {
            Directory.CreateDirectory(artifacts);
            File.WriteAllText(Path.Combine(artifacts, "wpt-values-report.txt"), report.ToString());
            File.WriteAllLines(Path.Combine(artifacts, "wpt-values-unexpected.txt"), unexpected);
        }

        Console.WriteLine(report.ToString());
        Assert.True(unexpected.Count == 0, unexpected.Count.ToString(CultureInfo.InvariantCulture) + " undocumented failures; first: " + string.Join("\n", unexpected.Take(15)));
    }
}
