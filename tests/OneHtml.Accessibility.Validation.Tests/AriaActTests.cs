using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>
/// Runs the vendored W3C ACT test cases against the ARIA rules. A case's expectation is for the whole page: failed when
/// some target fails, passed when targets exist and all pass, inapplicable when none applies. Cases our static check
/// cannot judge are listed with their reason in <c>testdata/act/aria-expectations.tsv</c> and reported as not applicable.
/// </summary>
public sealed class AriaActTests
{
    /// <summary>ACT rule id → the rules whose combined results answer it.</summary>
    internal static readonly (string Act, string[] Rules)[] Map =
    [
        ("5f99a7", ["aria-valid-attr"]),
        ("674b10", ["aria-roles"]),
        ("6a7281", ["aria-valid-attr-value"]),
        ("5c01ea", ["aria-allowed-attr", "aria-prohibited-attr", "aria-conditional-attr"]),
        ("4e8ab6", ["aria-required-attr"]),
        ("bc4a75", ["aria-required-children"]),
        ("ff89c9", ["aria-required-parent"]),
        ("307n5z", ["nested-interactive"]),
        ("6cfa84", ["aria-hidden-focus"]),
        ("46ca7f", ["presentation-role-conflict"]),
        ("m6b1q3", ["aria-command-name"]),
    ];

    internal static string Repo { get; } = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", ".."));

    private static string ThisDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    internal static string Run(string html, string[] ruleIds)
    {
        Document doc = Document.Parse(html);
        var checker = new Checker();
        CheckReport report = checker.Check(doc, new CheckOptions { RuleIds = ruleIds });
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

    internal static Dictionary<string, (string Status, string Reason)> LoadExpectations()
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        string path = Path.Combine(Repo, "testdata", "act", "aria-expectations.tsv");
        if (!File.Exists(path))
        {
            return map;
        }

        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('\t');
            map[f[0]] = (f[1], f[2]);
        }

        return map;
    }

    internal static (string Rows, List<string> Problems) Evaluate()
    {
        IReadOnlyList<ActTestCase> cases = ActTestCases.Parse(File.ReadAllText(Path.Combine(Repo, "testdata", "act", "testcases.json")));
        Dictionary<string, (string Status, string Reason)> known = LoadExpectations();
        var sb = new StringBuilder();
        var mismatches = new List<string>();
        var stale = new List<string>();
        var perRule = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        foreach ((string act, string[] rules) in Map)
        {
            int pass = 0, na = 0, fail = 0;
            foreach (ActTestCase c in cases.Where(c => c.RuleId == act))
            {
                string html = File.ReadAllText(Path.Combine(Repo, "testdata", "act", c.RelativePath));
                string got = Run(html, rules);
                bool ok = got == c.Expected;
                if (known.TryGetValue(c.Id, out (string Status, string Reason) k))
                {
                    if (ok)
                    {
                        stale.Add(c.Id + " " + act + " now matches");
                    }

                    na++;
                    continue;
                }

                if (ok)
                {
                    pass++;
                }
                else
                {
                    fail++;
                    mismatches.Add(act + "\t" + c.Id + "\t" + c.Title + "\texpected " + c.Expected + ", got " + got);
                }
            }

            perRule[act] = [pass, na, fail];
        }

        foreach (KeyValuePair<string, int[]> kv in perRule)
        {
            sb.AppendLine(kv.Key + "\tpass " + kv.Value[0] + "\tn/a " + kv.Value[1] + "\tfail " + kv.Value[2]);
        }

        mismatches.AddRange(stale);
        return (sb.ToString(), mismatches);
    }

    [Fact]
    public void Aria_act_cases_match_their_expectations()
    {
        (string rows, List<string> problems) = Evaluate();
        Assert.True(problems.Count == 0, rows + "\n" + string.Join("\n", problems));
    }
}
