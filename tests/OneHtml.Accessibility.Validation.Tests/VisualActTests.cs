using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Accessibility.Aria;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>
/// Runs the vendored W3C ACT test cases of the contrast, text spacing, orientation and media rules, judged as the other ACT
/// tests are (one verdict per page). The media rules ask a person, so their passed and failed cases may come out as
/// needs-review (<see cref="Judgement"/>). Cases that need pixels or layout the cascade cannot give are listed with their reason in
/// <c>testdata/act/visual-expectations.tsv</c>; each listed case must still be wrong, or the test says the entry is stale.
/// </summary>
public sealed class VisualActTests
{
    internal static readonly (string Act, string[] Rules, bool ExcludeWidgets)[] Map =
    [
        ("afw4f7", ["color-contrast"], true),
        ("09o5cg", ["color-contrast-enhanced"], true),
        ("24afc2", ["avoid-inline-spacing"], false),
        ("78fd32", ["avoid-inline-spacing"], false),
        ("9e45ec", ["avoid-inline-spacing"], false),
        ("b33eff", ["css-orientation-lock"], false),
        ("80f0bf", ["no-autoplay-audio"], false),
        ("4c31df", ["autoplay-audio-control"], false),
        ("aaa1bf", ["autoplay-audio-duration"], false),
        ("eac66b", ["video-caption"], false),
        ("2eb176", ["audio-caption"], false),
        ("afb423", ["audio-caption"], false),
        ("f51b46", ["video-captions"], false),
        ("c3232f", ["video-only-alternative"], false),
        ("ee13b5", ["video-only-transcript"], false),
        ("fd26cf", ["video-only-media-alternative"], false),
        ("d7ba54", ["video-only-audio-track"], false),
        ("ac7dc6", ["video-only-description-track"], false),
        ("f196ce", ["video-description-track"], false),
        ("1ea59c", ["video-audio-description"], false),
        ("ab4d13", ["video-media-alternative"], false),
        ("1a02b0", ["video-transcript"], false),
        ("1ec09b", ["video-strict-alternative"], false),
        ("c5a4ea", ["video-visual-alternative"], false),
        ("e7aa44", ["audio-alternative"], false),
    ];

    /// <summary>ACT rules whose outcome depends on what a recording contains.</summary>
    internal static readonly HashSet<string> Judgement = new(StringComparer.Ordinal)
    {
        "80f0bf", "4c31df", "aaa1bf", "eac66b", "2eb176", "afb423", "f51b46", "c3232f", "ee13b5", "fd26cf", "d7ba54", "ac7dc6", "f196ce", "1ea59c", "ab4d13", "1a02b0", "1ec09b", "c5a4ea", "e7aa44",
    };

    private static bool IsWidget(AriaRole? role)
    {
        var stack = new Stack<AriaRole>();
        if (role is not null)
        {
            stack.Push(role);
        }

        while (stack.Count > 0)
        {
            AriaRole r = stack.Pop();
            if (r.Name == "widget")
            {
                return true;
            }

            foreach (string s in r.Superclasses)
            {
                if (AriaTaxonomy.FindRole(s) is { } sup)
                {
                    stack.Push(sup);
                }
            }
        }

        return false;
    }

    internal static string Run(string html, string[] rules, bool excludeWidgets)
    {
        CheckReport report = new Checker().Check(Document.Parse(html), new CheckOptions { RuleIds = rules });
        var kept = report.Results.Where(r =>
        {
            if (!excludeWidgets || r.Target.IsNull)
            {
                return true;
            }

            // ACT afw4f7 and 09o5cg leave text inside widgets to other rules; the library checks it too.
            for (AxNode? n = report.Tree.Find(r.Target); n is not null; n = n.Parent)
            {
                if (IsWidget(n.AriaRole))
                {
                    return false;
                }
            }

            return true;
        }).ToList();
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
    public void Visual_act_cases_match_their_expectations()
    {
        IReadOnlyList<Catalogue.ActTestCase> cases = Catalogue.ActTestCases.Parse(File.ReadAllText(Path.Combine(AriaActTests.Repo, "testdata", "act", "testcases.json")));
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        string tsv = Path.Combine(AriaActTests.Repo, "testdata", "act", "visual-expectations.tsv");
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
        foreach ((string act, string[] rules, bool widgets) in Map)
        {
            int pass = 0, judged = 0, na = 0, fail = 0;
            foreach (Catalogue.ActTestCase c in cases.Where(c => c.RuleId == act))
            {
                string got = Run(File.ReadAllText(Path.Combine(AriaActTests.Repo, "testdata", "act", c.RelativePath)), rules, widgets);
                bool ok = got == c.Expected;
                bool judgement = !ok && Judgement.Contains(act) && got == "incomplete";
                if (known.ContainsKey(c.Id))
                {
                    if (ok)
                    {
                        problems.Add(c.Id + " " + act + " now matches; drop it from visual-expectations.tsv");
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

        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "act-visual-summary.tsv"), sb.ToString() + string.Join("\n", problems));
        Assert.True(problems.Count == 0, sb.ToString() + string.Join("\n", problems));
    }
}
