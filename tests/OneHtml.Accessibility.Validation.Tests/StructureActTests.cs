using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>
/// Runs the vendored W3C ACT test cases of the page-structure rules, judged one verdict per page as the ARIA cases are.
/// Cases the static check cannot decide are listed with their reason in <c>testdata/act/structure-expectations.tsv</c>;
/// ACT rules the library does not implement as rules are in <see cref="NotImplemented"/> with the reason, and their cases
/// are counted as not applicable.
/// </summary>
public sealed class StructureActTests
{
    internal static readonly (string Act, string[] Rules)[] Map =
    [
        ("cf77f2", ["bypass"]),
        ("3ea0c8", ["duplicate-id", "duplicate-id-active", "duplicate-id-aria"]),
        ("akn7bn", ["frame-focusable-content"]),
        ("0ssw9k", ["scrollable-region-focusable"]),
        ("bc659a", ["meta-refresh"]),
        ("bisz58", ["meta-refresh-no-exceptions"]),
        ("b4f0c3", ["meta-viewport"]),
    ];

    /// <summary>ACT rules of this area that are not rules of the checker, and why.</summary>
    internal static readonly (string Act, string Reason)[] NotImplemented =
    [
        ("047fe0", "its verdict depends on which content repeats across the pages of a site; one page cannot say (bypass answers the page-level question of cf77f2)"),
        ("b40fd1", "its verdict depends on which content repeats across the pages of a site; one page cannot say"),
        ("3e12e1", "its verdict depends on which content repeats across pages and on what a script does to collapse it"),
        ("ye5d6e", "its verdict depends on which content repeats across the pages of a site; one page cannot say"),
        ("b49b2e", "whether a heading is descriptive is a judgement only a person can make"),
        ("e6952f", "duplicate attributes are dropped by the parser; only a document parsed with errors recorded could report them"),
    ];

    internal static string Run(string html, string[] rules)
    {
        CheckReport report = new Checker().Check(Document.Parse(html), new CheckOptions { RuleIds = rules });
        if (report.Results.Any(r => r.Outcome == Outcome.Failed))
        {
            return "failed";
        }

        if (report.Results.Any(r => r.Outcome == Outcome.Passed))
        {
            return "passed";
        }

        return report.Results.Any(r => r.Outcome is Outcome.NeedsReview or Outcome.NeedsRender) ? "incomplete" : "inapplicable";
    }

    [Fact]
    public void Structure_act_cases_match_their_expectations()
    {
        IReadOnlyList<Catalogue.ActTestCase> cases = Catalogue.ActTestCases.Parse(File.ReadAllText(Path.Combine(AriaActTests.Repo, "testdata", "act", "testcases.json")));
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        string tsv = Path.Combine(AriaActTests.Repo, "testdata", "act", "structure-expectations.tsv");
        if (File.Exists(tsv))
        {
            foreach (string line in File.ReadAllLines(tsv))
            {
                if (line.Length > 0 && line[0] != '#')
                {
                    string[] f = line.Split('\t');
                    known[f[0]] = f[2];
                }
            }
        }

        var sb = new StringBuilder();
        var problems = new List<string>();
        foreach ((string act, string[] rules) in Map)
        {
            int pass = 0, na = 0, fail = 0;
            foreach (Catalogue.ActTestCase c in cases.Where(c => c.RuleId == act))
            {
                string got = Run(File.ReadAllText(Path.Combine(AriaActTests.Repo, "testdata", "act", c.RelativePath)), rules);
                bool ok = got == c.Expected;
                if (known.ContainsKey(c.Id))
                {
                    if (ok)
                    {
                        problems.Add(c.Id + " " + act + " now matches; drop it from structure-expectations.tsv");
                    }

                    na++;
                }
                else if (ok)
                {
                    pass++;
                }
                else
                {
                    fail++;
                    problems.Add(act + "\t" + c.Id + "\t" + c.Title + "\texpected " + c.Expected + ", got " + got);
                }
            }

            sb.AppendLine(act + "\t" + string.Join(",", rules) + "\tpass " + pass + "\tn/a " + na + "\tfail " + fail);
        }

        foreach ((string act, string reason) in NotImplemented)
        {
            sb.AppendLine(act + "\tnot a rule\tn/a " + cases.Count(c => c.RuleId == act) + "\t" + reason);
        }

        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "act-structure-summary.tsv"), sb.ToString() + string.Join("\n", problems));
        Assert.True(problems.Count == 0, sb.ToString() + string.Join("\n", problems));
    }
}
