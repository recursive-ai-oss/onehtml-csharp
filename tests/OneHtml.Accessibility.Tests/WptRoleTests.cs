using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RecursiveAI.OneHtml.Accessibility.Aria;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>
/// Runs the role assertions of web-platform-tests' <c>html-aam/</c> and <c>wai-aria/role/</c> pages against
/// <see cref="RoleComputer"/>. The pages call <c>AriaUtils.verify*</c> helpers with selectors, expecting
/// <c>data-expectedrole</c> values; those calls are translated here. Cases that need a script, a name beyond
/// the simple attribute probe, or an expectation the page itself marks ambiguous are not counted as run.
/// Known failures are listed with their reason in <c>testdata/wpt/aria/expectations.tsv</c>.
/// </summary>
public sealed partial class WptRoleTests
{
    private static readonly string Root = Path.Combine(TestPaths.TestData, "wpt", "aria");

    public static IEnumerable<object[]> Files() =>
        Directory.EnumerateFiles(Root, "*.html", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new object[] { f });

    [GeneratedRegex(@"AriaUtils\.(?<fn>\w+)\((?<args>[^;]*)\);", RegexOptions.None, 2000)]
    private static partial Regex CallPattern();

    [GeneratedRegex("\"(?<s>(?:[^\"\\\\]|\\\\.)*)\"|'(?<s>(?:[^'\\\\]|\\\\.)*)'", RegexOptions.None, 2000)]
    private static partial Regex StringPattern();

    internal sealed record Case(string Name, string Expected, Func<string, bool> Accept, NodeRole Result, bool NotApplicable, string? NaReason);

    /// <summary>The probe the pages assume: a name from aria-label, a resolvable aria-labelledby, or a title.</summary>
    internal static bool SimpleName(OneHtml.Node n)
    {
        string? label = n.GetAttribute("aria-label");
        if (label is not null && label.Trim().Length > 0)
        {
            return true;
        }

        string? by = n.GetAttribute("aria-labelledby");
        if (by is not null)
        {
            foreach (string id in by.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!n.Document!.GetElementById(id).IsNull)
                {
                    return true;
                }
            }
        }

        string? title = n.GetAttribute("title");
        return title is not null && title.Trim().Length > 0;
    }

    internal static List<Case> Collect(string relative, out int scriptBuilt)
    {
        string html = File.ReadAllText(Path.Combine(Root, relative), Encoding.UTF8);
        OneHtml.Document doc = OneHtml.Document.Parse(html);
        RoleMap map = RoleComputer.Compute(doc, new RoleOptions { HasAccessibleName = SimpleName });
        var cases = new List<Case>();
        scriptBuilt = 0;
        foreach (OneHtml.Node script in doc.Root.QuerySelectorAll("script:not([src])"))
        {
            string text = script.TextContent ?? string.Empty;
            if (text.Contains("createElement", StringComparison.Ordinal) || text.Contains("innerHTML", StringComparison.Ordinal)
                || text.Contains("appendChild", StringComparison.Ordinal) || text.Contains("insertAdjacent", StringComparison.Ordinal))
            {
                scriptBuilt++;
            }

            foreach (Match bm in ComputedRoleAssertPattern().Matches(text))
            {
                // wai-aria/role/basic.html: promise_test(get_computed_role(getElementById(id)); assert_equals(role, expected)).
                OneHtml.Node target = doc.GetElementById(bm.Groups["id"].Value);
                string want = bm.Groups["want"].Value;
                cases.Add(new Case(bm.Groups["name"].Value, want, got => got == want, map[target], false, null));
            }

            Match arr = RoleArrayPattern().Match(text);
            if (arr.Success)
            {
                // wai-aria/role/roles.html: a list of role names, assigned as written, upper-cased and sentence-cased.
                string body = string.Join('\n', arr.Groups["a"].Value.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
                foreach (Match sm in StringPattern().Matches(body))
                {
                    string r = sm.Groups["s"].Value;
                    foreach (string variant in new[] { r, r.ToUpperInvariant(), char.ToUpperInvariant(r[0]) + r[1..].ToLowerInvariant() })
                    {
                        AddAssigned(doc, cases, variant);
                    }
                }
            }

            foreach (Match m in CallPattern().Matches(text))
            {
                string fn = m.Groups["fn"].Value;
                string args = m.Groups["args"].Value;
                var strings = StringPattern().Matches(args).Select(s => s.Groups["s"].Value).ToList();
                switch (fn)
                {
                    case "verifyRolesBySelector":
                    case "verifyGenericRolesBySelector":
                    case "verifyRoleOrVariantRolesBySelector":
                        foreach (OneHtml.Node el in doc.Root.QuerySelectorAll(strings[0]))
                        {
                            string testName = el.GetAttribute("data-testname") ?? el.GetAttribute("data-expectedrole") ?? el.LocalName;
                            Func<string, bool> accept;
                            string expected;
                            string? na = null;
                            if (fn == "verifyRolesBySelector")
                            {
                                expected = el.GetAttribute("data-expectedrole") ?? string.Empty;
                                accept = got => got == expected;
                                na = NotApplicableReason(expected);
                            }
                            else
                            {
                                string[] variants = fn == "verifyGenericRolesBySelector" ? ["generic", "", "none"] : strings.Skip(1).ToArray();
                                expected = string.Join('|', variants);
                                accept = got => Array.IndexOf(variants, got) >= 0;
                            }

                            cases.Add(new Case(testName, expected, accept, map[el], na is not null, na));
                        }

                        break;
                    case "assignAndVerifyRolesByRoleNames" when strings.Count > 0:
                        foreach (string role in strings)
                        {
                            AddAssigned(doc, cases, role);
                        }

                        break;
                }
            }
        }

        return cases;
    }

    private static void AddAssigned(OneHtml.Document context, List<Case> cases, string role)
    {
        OneHtml.Document d2 = OneHtml.Document.ParseFragment("<div role=\"" + role + "\">x</div>", OneHtml.FragmentContext.FromElement(context.Root.QuerySelector("body")));
        OneHtml.Node div = d2.Root.QuerySelector("div");
        string expected = role.ToLowerInvariant();
        cases.Add(new Case("role: " + role, expected, got => got == expected, RoleComputer.Compute(d2, new RoleOptions { HasAccessibleName = SimpleName })[div], false, null));
    }

    [GeneratedRegex("get_computed_role\\(document\\.getElementById\\('(?<id>\\w+)'\\)\\);\\s*assert_equals\\(role, \"(?<want>[^\"]+)\"\\);\\s*\\}, \"(?<name>[^\"]+)\"", RegexOptions.None, 2000)]
    private static partial Regex ComputedRoleAssertPattern();

    [GeneratedRegex(@"let ariaRoles = \[(?<a>.*?)\];", RegexOptions.Singleline, 2000)]
    private static partial Regex RoleArrayPattern();

    private static string? NotApplicableReason(string expected) => expected switch
    {
        "SPEC_AMBIGUOUS_LOG_VALUE" => "the page marks the expectation as ambiguous in the specification",
        "not defined in spec?" => "the page marks the expectation as undefined",
        "html-cite" => "an implementation-specific role name",
        _ => null,
    };

    private static Dictionary<string, string> LoadExpectations()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        string path = Path.Combine(Root, "expectations.tsv");
        if (File.Exists(path))
        {
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] parts = line.Split('\t');
                map[parts[0] + "\t" + parts[1]] = parts.Length > 2 ? parts[2] : string.Empty;
            }
        }

        return map;
    }

    [Fact]
    public void Report_and_known_failures()
    {
        Dictionary<string, string> expected = LoadExpectations();
        var unexpected = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var report = new StringBuilder();
        int totalPass = 0, totalFail = 0, totalNa = 0;
        foreach (object[] f in Files())
        {
            string file = (string)f[0];
            List<Case> cases = Collect(file, out int scriptBuilt);
            int pass = 0, fail = 0, na = 0;
            foreach (Case c in cases)
            {
                if (c.NotApplicable)
                {
                    na++;
                    continue;
                }

                string key = file + "\t" + c.Name;
                if (c.Accept(c.Result.ComputedRole))
                {
                    pass++;
                    if (expected.ContainsKey(key))
                    {
                        unexpected.Add("now passes, remove from expectations: " + key);
                    }
                }
                else
                {
                    fail++;
                    used.Add(key);
                    if (!expected.ContainsKey(key))
                    {
                        unexpected.Add("FAIL " + key + " expected " + c.Expected + " got '" + c.Result.ComputedRole + "' origin " + c.Result.Origin);
                    }
                }
            }

            totalPass += pass;
            totalFail += fail;
            totalNa += na;
            report.Append(CultureInfo.InvariantCulture, $"{file}\tpass={pass}\tfail={fail}\tn/a={na}\tscript-built-blocks={scriptBuilt}\n");
        }

        foreach (string key in expected.Keys)
        {
            if (!used.Contains(key) && !unexpected.Any(u => u.EndsWith(key, StringComparison.Ordinal)))
            {
                unexpected.Add("stale expectation: " + key);
            }
        }

        report.Append(CultureInfo.InvariantCulture, $"TOTAL\tpass={totalPass}\tfail={totalFail}\tn/a={totalNa}\n");
        string? outPath = Environment.GetEnvironmentVariable("ONEHTML_WPT_REPORT");
        if (outPath is not null)
        {
            File.WriteAllText(outPath, report.ToString());
        }

        Assert.True(unexpected.Count == 0, string.Join('\n', unexpected));
    }
}
