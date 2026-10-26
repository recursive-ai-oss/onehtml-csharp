using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>
/// Runs the vendored W3C ACT test cases of the rules about names and labels. Judged as the ARIA cases are (one verdict per
/// page). Rules that need a person to judge (<see cref="Judgement"/>) answer needs-review where ACT expects failed, which
/// counts as not applicable; cases the static check cannot decide are listed with their reason in
/// <c>testdata/act/names-expectations.tsv</c>.
/// </summary>
public sealed class NamesActTests
{
    internal static readonly (string Act, string[] Rules, string[]? Roles)[] Map =
    [
        ("97a4e1", ["button-name", "input-button-name", "aria-command-name"], ["button"]),
        ("c487ae", ["link-name", "area-alt", "aria-command-name"], ["link", "doc-biblioref", "doc-glossref", "doc-noteref"]),
        ("e086e5", ["label", "select-name", "aria-input-field-name", "aria-toggle-field-name"], null),
        ("23a2a8", ["image-alt", "role-img-alt", "svg-img-alt"], null),
        ("59796f", ["input-image-alt"], null),
        ("cae760", ["frame-title"], null),
        ("4b1c6c", ["frame-title-unique"], null),
        ("2779a5", ["document-title"], null),
        ("2ee8b8", ["label-content-name-mismatch"], null),
        ("5effbb", ["link-in-context-descriptive"], null),
        ("b20e66", ["identical-links-same-purpose"], null),
        ("ffd0e9", ["empty-heading"], null),
        ("m6b1q3", ["aria-command-name"], null),
        ("qt1vmo", ["image-name-descriptive"], null),
        ("7d6734", ["svg-img-alt"], null),
        ("8fc3b6", ["object-alt"], null),
    ];

    /// <summary>ACT rules whose passed and failed outcomes depend on a person's judgement.</summary>
    internal static readonly HashSet<string> Judgement = new(StringComparer.Ordinal) { "4b1c6c", "5effbb", "b20e66", "qt1vmo" };

    /// <summary>The page verdict over the rules' results; with <paramref name="roles"/>, only targets whose role is listed count (an ACT rule about buttons ignores the links a shared rule also checks).</summary>
    internal static string Run(string html, string[] rules, string[]? roles)
    {
        CheckReport report = new Checker().Check(Document.Parse(html), new CheckOptions { RuleIds = rules });
        var kept = report.Results.Where(r => roles is null || r.Target.IsNull || (report.Tree.Find(r.Target)?.AriaRole is { } ar && roles.Contains(ar.Name))).ToList();
        if (kept.Any(r => r.Outcome == Outcome.Failed))
        {
            return "failed";
        }

        if (kept.Any(r => r.Outcome == Outcome.Passed))
        {
            return "passed";
        }

        return kept.Any(r => r.Outcome is Outcome.NeedsReview or Outcome.NeedsRender) ? "incomplete" : "inapplicable";
    }

    [Fact]
    public void Names_act_cases_match_their_expectations()
    {
        IReadOnlyList<Catalogue.ActTestCase> cases = Catalogue.ActTestCases.Parse(File.ReadAllText(Path.Combine(AriaActTests.Repo, "testdata", "act", "testcases.json")));
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        string tsv = Path.Combine(AriaActTests.Repo, "testdata", "act", "names-expectations.tsv");
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
        foreach ((string act, string[] rules, string[]? roles) in Map)
        {
            int pass = 0, judged = 0, na = 0, fail = 0;
            foreach (Catalogue.ActTestCase c in cases.Where(c => c.RuleId == act))
            {
                string got = Run(File.ReadAllText(Path.Combine(AriaActTests.Repo, "testdata", "act", c.RelativePath)), rules, roles);
                bool ok = got == c.Expected;
                bool judgement = !ok && Judgement.Contains(act) && got == "incomplete" && c.Expected is "failed" or "passed";
                if (known.ContainsKey(c.Id))
                {
                    if (ok)
                    {
                        problems.Add(c.Id + " " + act + " now matches; drop it from names-expectations.tsv");
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

        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "act-names-summary.tsv"), sb.ToString() + string.Join("\n", problems));
        Assert.True(problems.Count == 0, sb.ToString() + string.Join("\n", problems));
    }
}
