using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>
/// The axe-core 4.14.0 outcomes recorded in <c>testdata/axe/expectations.tsv</c> (captured in Chrome for Testing on the pages beside it),
/// checked against our verdicts on the same pages. A fifth column holds our own verdict where it deliberately differs from axe's; the
/// reason for each such row is in <c>docs/axe-differential.md</c>. Rows are keyed by element id, so they survive reformatting of the pages.
/// </summary>
public sealed class AxeExpectationsTests
{
    private static string Root { get; } = Path.Combine(AriaActTests.Repo, "testdata", "axe");

    private static string Group(Outcome o) => o switch
    {
        Outcome.Failed => "violation",
        Outcome.Passed => "pass",
        _ => "incomplete",
    };

    private static bool IsInside(Node n, Node ancestor)
    {
        for (Node p = n.Parent; !p.IsNull; p = p.Parent)
        {
            if (p.Equals(ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static int Severity(string g) => g switch { "violation" => 3, "incomplete" => 2, "pass" => 1, _ => 0 };

    [Fact]
    public void Our_verdicts_match_the_recorded_axe_outcomes_or_the_documented_difference()
    {
        var failures = new List<string>();
        int rows = 0;
        var checker = new Checker();
        foreach (IGrouping<(string Page, string Rule), string[]> g in File.ReadAllLines(Path.Combine(Root, "expectations.tsv"))
                     .Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).GroupBy(c => (c[0], c[1])))
        {
            Document doc = Document.Parse(File.ReadAllBytes(Path.Combine(Root, g.Key.Page)));
            CheckReport report = checker.Check(doc, new CheckOptions { RuleIds = [g.Key.Rule] });
            foreach (string[] row in g)
            {
                rows++;
                Node el = doc.GetElementById(row[2]);
                string actual = "none";
                // Table rules report on cells where axe reports the table: take the cells' verdicts.
                bool container = g.Key.Rule is "td-has-header" or "table-fake-caption" or "td-headers-attr" or "th-has-data-cells";
                foreach (RuleResult r in report.Results.Where(r => !r.Target.IsNull && (r.Target.Equals(el) || (container && IsInside(r.Target, el)))))
                {
                    string grp = Group(r.Outcome);
                    if (Severity(grp) > Severity(actual))
                    {
                        actual = grp;
                    }
                }

                string expected = row.Length > 4 && row[4].Length > 0 ? row[4] : row[3];
                // A pass axe reports for a candidate we do not report on is coverage, not disagreement (docs/axe-differential.md).
                if (actual != expected && !(expected == "pass" && actual == "none"))
                {
                    failures.Add($"{row[0]} {row[1]} #{row[2]}: expected {expected}, ours {actual} (axe {row[3]})");
                }
            }
        }

        Assert.True(rows > 50, "the expectations file is missing or empty");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
