using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>
/// Runs the vendored W3C ACT test cases of the rules that close the gaps in the ACT coverage: the review-only rules about
/// descriptive text and decorative or text-bearing images, the identical-links-in-context rule and the visible focus
/// indicator. For the rules that ask a person, an applicable case expected to pass or fail may come out as needs-review
/// (judgement); an inapplicable case must come out inapplicable. Cases the cascade cannot judge are listed with a reason in
/// <c>testdata/act/gaps-expectations.tsv</c>; each listed case must still be wrong, or the test says the entry is stale.
/// </summary>
public sealed class ActGapActTests
{
    internal static readonly (string Act, string[] Rules)[] Map =
    [
        ("0va7u6", ["image-of-text"]),
        ("c4a8a4", ["document-title-descriptive"]),
        ("oj04fd", ["focus-visible-indicator"]),
        ("aizyf1", ["link-descriptive"]),
        ("b49b2e", ["heading-descriptive"]),
        ("cc0f0a", ["label-descriptive"]),
        ("e88epe", ["image-decorative"]),
        ("fd3a94", ["identical-links-same-context"]),
    ];

    internal static (string Rows, List<string> Problems) Evaluate()
    {
        IReadOnlyList<Catalogue.ActTestCase> cases = Catalogue.ActTestCases.Parse(File.ReadAllText(Path.Combine(AriaActTests.Repo, "testdata", "act", "testcases.json")));
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        string tsv = Path.Combine(AriaActTests.Repo, "testdata", "act", "gaps-expectations.tsv");
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
            int pass = 0, judged = 0, na = 0, fail = 0;
            foreach (Catalogue.ActTestCase c in cases.Where(c => c.RuleId == act))
            {
                string got = VisualActTests.Run(File.ReadAllText(Path.Combine(AriaActTests.Repo, "testdata", "act", c.RelativePath)), rules, false);
                bool ok = got == c.Expected;
                bool judgement = !ok && got == "incomplete" && c.Expected is "failed" or "passed";
                if (known.ContainsKey(c.Id))
                {
                    if (ok || judgement)
                    {
                        problems.Add(c.Id + " " + act + " now matches; drop it from gaps-expectations.tsv");
                    }

                    na++;
                }
                else if (ok)
                {
                    pass++;
                }
                else if (judgement)
                {
                    judged++;
                }
                else
                {
                    fail++;
                    problems.Add(act + "\t" + c.Id + "\t" + c.Title + "\texpected " + c.Expected + ", got " + got);
                }
            }

            sb.AppendLine(act + "\t" + string.Join(",", rules) + "\tpass " + pass + "\tjudgement " + judged + "\tn/a " + na + "\tfail " + fail);
        }

        return (sb.ToString(), problems);
    }

    [Fact]
    public void Gap_act_cases_match_their_expectations()
    {
        (string rows, List<string> problems) = Evaluate();
        Assert.True(problems.Count == 0, rows + "\n" + string.Join("\n", problems));
    }
}
