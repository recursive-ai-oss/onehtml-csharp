using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>
/// Runs the vendored W3C ACT test cases against the table, form-autocomplete and language rules, judged as
/// <see cref="AriaActTests"/> does. Two rules only ever ask a person (the language of the text cannot be detected): for them
/// an applicable case, expected to pass or fail, must come out as "incomplete", and an inapplicable case as inapplicable.
/// Cases the static check cannot judge are listed with a reason in <c>testdata/act/tables-forms-language-expectations.tsv</c>.
/// </summary>
public sealed class TablesFormsLanguageActTests
{
    private static readonly (string Act, string[] Rules, bool ReviewOnly)[] s_map =
    [
        ("a25f45", ["td-headers-attr"], false),
        ("d0f69e", ["th-has-data-cells"], false),
        ("73f2c2", ["autocomplete-valid"], false),
        ("b5c3f8", ["html-has-lang"], false),
        ("bf051a", ["html-lang-valid"], false),
        ("5b7ae0", ["html-xml-lang-mismatch"], false),
        ("de46e4", ["valid-lang"], false),
        ("ucwvc8", ["html-lang-matches-content"], true),
        ("off6ek", ["element-lang-matches-content"], true),
    ];

    private static Dictionary<string, (string Status, string Reason)> LoadExpectations()
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        string path = Path.Combine(AriaActTests.Repo, "testdata", "act", "tables-forms-language-expectations.tsv");
        if (File.Exists(path))
        {
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length > 0 && line[0] != '#')
                {
                    string[] f = line.Split('\t');
                    map[f[0]] = (f[1], f[2]);
                }
            }
        }

        return map;
    }

    internal static (string Rows, List<string> Problems) Evaluate()
    {
        IReadOnlyList<ActTestCase> cases = ActTestCases.Parse(File.ReadAllText(Path.Combine(AriaActTests.Repo, "testdata", "act", "testcases.json")));
        Dictionary<string, (string Status, string Reason)> known = LoadExpectations();
        var sb = new StringBuilder();
        var mismatches = new List<string>();
        var stale = new List<string>();
        foreach ((string act, string[] rules, bool reviewOnly) in s_map)
        {
            int pass = 0, na = 0, fail = 0;
            foreach (ActTestCase c in cases.Where(c => c.RuleId == act))
            {
                string html = File.ReadAllText(Path.Combine(AriaActTests.Repo, "testdata", "act", c.RelativePath));
                string got = AriaActTests.Run(html, rules);
                string expected = reviewOnly && c.Expected != "inapplicable" ? "incomplete" : c.Expected;
                bool ok = got == expected;
                if (known.ContainsKey(c.Id))
                {
                    if (ok)
                    {
                        stale.Add(c.Id + " " + act + " now matches");
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
                    mismatches.Add(act + "\t" + c.Id + "\t" + c.Title + "\texpected " + expected + ", got " + got);
                }
            }

            sb.AppendLine(act + "\t" + string.Join(",", rules) + "\tpass " + pass + "\tn/a " + na + "\tfail " + fail);
        }

        mismatches.AddRange(stale);
        return (sb.ToString(), mismatches);
    }

    [Fact]
    public void Act_cases_match_their_expectations()
    {
        (string rows, List<string> problems) = Evaluate();
        Assert.True(problems.Count == 0, rows + "\n" + string.Join("\n", problems));
    }
}
