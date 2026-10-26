using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

public class CatalogueTests
{
    private static readonly string[] Impacts = ["minor", "moderate", "serious", "critical"];

    private static string Repo { get; } = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", ".."));

    private static string ThisDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    [Fact]
    public void Criteria_counts_match_the_recommendations()
    {
        int Count(WcagVersion v, WcagLevel l) => WcagCriteria.ForVersion(v).Count(c => c.Level == l);
        Assert.Equal(25, Count(WcagVersion.Wcag20, WcagLevel.A));
        Assert.Equal(13, Count(WcagVersion.Wcag20, WcagLevel.AA));
        Assert.Equal(23, Count(WcagVersion.Wcag20, WcagLevel.AAA));
        Assert.Equal(30, Count(WcagVersion.Wcag21, WcagLevel.A));
        Assert.Equal(20, Count(WcagVersion.Wcag21, WcagLevel.AA));
        Assert.Equal(28, Count(WcagVersion.Wcag21, WcagLevel.AAA));
        Assert.Equal(31, Count(WcagVersion.Wcag22, WcagLevel.A));
        Assert.Equal(24, Count(WcagVersion.Wcag22, WcagLevel.AA));
        Assert.Equal(31, Count(WcagVersion.Wcag22, WcagLevel.AAA));
        Assert.Equal(87, WcagCriteria.All.Count);
    }

    [Fact]
    public void Criteria_are_in_number_order_with_published_handles()
    {
        Assert.Equal("Contrast (Minimum)", WcagCriteria.Get("1.4.3").Handle);
        Assert.Equal("Parsing", WcagCriteria.Get("4.1.1").Handle);
        Assert.Equal(WcagVersion.Wcag22, WcagCriteria.Get("4.1.1").RemovedIn);
        for (int i = 1; i < WcagCriteria.All.Count; i++)
        {
            Assert.True(Key(WcagCriteria.All[i - 1].Number).CompareTo(Key(WcagCriteria.All[i].Number)) < 0);
        }

        Assert.Throws<ArgumentException>(() => WcagCriteria.Get("9.9.9"));
    }

    private static long Key(string number)
    {
        long key = 0;
        foreach (string part in number.Split('.'))
        {
            key = (key * 100) + int.Parse(part, System.Globalization.CultureInfo.InvariantCulture);
        }

        return key;
    }

    [Fact]
    public void Every_act_rule_criterion_exists()
    {
        Assert.Equal(94, ActRules.All.Count);
        foreach (ActRule rule in ActRules.All)
        {
            foreach (string number in rule.Criteria)
            {
                Assert.True(WcagCriteria.Find(number) is not null, $"ACT {rule.Id} names unknown criterion {number}");
            }
        }

        Assert.Equal(37, ActRules.All.Count(r => r.Status == ActRuleStatus.Approved));
        Assert.Equal(50, ActRules.All.Count(r => r.Status == ActRuleStatus.Proposed));
        Assert.Equal(7, ActRules.All.Count(r => r.Status == ActRuleStatus.Deprecated));
        Assert.All(ActRules.All.Where(r => r.IsComposite), r => Assert.NotEmpty(r.InputRules));
        Assert.All(ActRules.All.Where(r => r.IsComposite), r => Assert.All(r.InputRules, i => Assert.NotNull(ActRules.Find(i))));
    }

    [Fact]
    public void Every_axe_rule_wcag_tag_maps_to_a_criterion()
    {
        Assert.Equal("4.14.0", AxeRules.AxeVersion);
        Assert.Equal(105, AxeRules.All.Count);
        foreach (AxeRule rule in AxeRules.All)
        {
            foreach (string number in rule.CriterionNumbers)
            {
                Assert.True(WcagCriteria.Find(number) is not null, $"axe {rule.Id} tag maps to unknown criterion {number}");
            }

            Assert.Contains(rule.Impact, Impacts);
            foreach (string act in rule.ActRules)
            {
                Assert.NotNull(ActRules.Find(act));
            }
        }

        Assert.Equal(["1.1.1"], AxeRules.Find("image-alt")!.CriterionNumbers);
        Assert.Contains((WcagVersion.Wcag21, WcagLevel.AA), AxeRules.Find("autocomplete-valid")!.LevelTags);
        Assert.True(AxeRules.Find("region")!.IsBestPractice);
    }

    [Theory]
    [InlineData("1.4.3", true, true, true, true)]
    [InlineData("1.4.11", false, true, true, true)]
    [InlineData("2.5.8", false, false, true, true)]
    [InlineData("4.1.1", true, true, false, true)]
    [InlineData("1.4.6", false, false, false, true)]
    [InlineData("2.4.11", false, false, true, true)]
    [InlineData("2.4.13", false, false, false, true)]
    public void Profile_membership(string number, bool in20, bool in21, bool in22, bool inAll)
    {
        SuccessCriterion c = WcagCriteria.Get(number);
        Assert.Equal(in20, ValidationProfile.Wcag20AA.Includes(c));
        Assert.Equal(in21, ValidationProfile.Wcag21AA.Includes(c));
        Assert.Equal(in22, ValidationProfile.Wcag22AA.Includes(c));
        Assert.Equal(inAll, ValidationProfile.All.Includes(c));
    }

    [Fact]
    public void Profile_sizes()
    {
        Assert.Equal(38, ValidationProfile.Wcag20AA.Criteria.Count);
        Assert.Equal(50, ValidationProfile.Wcag21AA.Criteria.Count);
        Assert.Equal(55, ValidationProfile.Wcag22AA.Criteria.Count);
        Assert.Equal(87, ValidationProfile.All.Criteria.Count);
        Assert.Same(ValidationProfile.Wcag21AA, ValidationProfile.Find("wcag-2.1-aa"));
    }

    [Fact]
    public void Profiles_select_rules_by_label()
    {
        var contrast = new RuleDescriptor("color-contrast", [RuleSource.Axe("color-contrast")], ["1.4.3"], false, RuleNeeds.RenderedFacts, null, "Colours are a design choice.");
        var practice = new RuleDescriptor("region", [RuleSource.Axe("region")], [], true, RuleNeeds.Static, null, "Needs a judgement.");
        var target = new RuleDescriptor("target-size", [RuleSource.Axe("target-size")], ["2.5.8"], false, RuleNeeds.RenderedFacts, null, "x");
        Assert.True(ValidationProfile.Wcag20AA.Includes(contrast));
        Assert.False(ValidationProfile.Wcag22AA.Includes(practice));
        Assert.True(ValidationProfile.All.Includes(practice));
        Assert.False(ValidationProfile.Wcag21AA.Includes(target));
        Assert.True(ValidationProfile.Wcag22AA.Includes(target));
    }

    [Fact]
    public void Descriptor_validates_its_input()
    {
        RuleSource[] own = [RuleSource.Own("x")];
        Assert.Throws<ArgumentException>(() => new RuleDescriptor("", own, [], true, RuleNeeds.Static, null, null));
        Assert.Throws<ArgumentException>(() => new RuleDescriptor("x", [], [], true, RuleNeeds.Static, null, null));
        Assert.Throws<ArgumentException>(() => new RuleDescriptor("x", own, ["0.0.0"], true, RuleNeeds.Static, null, null));
        Assert.Throws<ArgumentException>(() => new RuleDescriptor("x", own, [], true, RuleNeeds.Static, "a", "b"));
    }

    [Fact]
    public void Act_test_case_files_are_present_and_match_their_hashes()
    {
        string root = Path.Combine(Repo, "testdata");
        IReadOnlyList<ActTestCase> cases = ActTestCases.Parse(File.ReadAllText(Path.Combine(root, "act", "testcases.json")));
        Assert.Equal(1134, cases.Count);

        var sums = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(Path.Combine(root, "SHA256SUMS")))
        {
            int space = line.IndexOf("  ", StringComparison.Ordinal);
            if (space > 0 && line.AsSpan(space + 2).StartsWith("act/", StringComparison.Ordinal))
            {
                sums[line[(space + 2)..]] = line[..space];
            }
        }

        Assert.Equal(cases.Count + 1, sums.Count);
        foreach (ActTestCase testCase in cases)
        {
            Assert.NotNull(ActRules.Find(testCase.RuleId));
            string relative = "act/" + testCase.RelativePath;
            string actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, relative))));
            Assert.Equal(sums[relative], actual);
        }

        Assert.Equal(sums["act/testcases.json"], Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "act", "testcases.json")))));
    }

    [Fact]
    public void Every_criterion_has_an_automation_assessment_and_the_document_has_none_unassessed()
    {
        foreach (SuccessCriterion c in WcagCriteria.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(AutomationAssessments.Get(c.Number).Basis), c.Number);
        }

        Assert.Equal(WcagCriteria.All.Count, AutomationAssessments.All.Count);
        string markdown = CoverageMatrix.ToMarkdown(BuiltInRules.All.Select(r => r.Descriptor));
        Assert.DoesNotContain("unassessed", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("| wcag-2.1-aa |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Coverage_document_is_current()
    {
        string path = Path.Combine(Repo, "docs", "coverage.md");
        string expected = CoverageDocument();
        if (Environment.GetEnvironmentVariable("ONEHTML_UPDATE_DOCS") == "1")
        {
            File.WriteAllText(path, expected);
        }

        Assert.Equal(expected, File.ReadAllText(path));
    }

    private static string CoverageDocument() =>
        "# Coverage matrix\n\n" +
        "Generated by `CoverageMatrix.ToMarkdown()`; do not edit by hand. To refresh it after the data changes, run the\n" +
        "Validation tests with `ONEHTML_UPDATE_DOCS=1`. The sources and their pins are in `docs/catalogue.md`.\n\n" +
        "How to read it: *In WCAG* gives the first version with the criterion (`2.0+` means that version and every later one).\n" +
        "*ACT rules* are the W3C ACT rules that list the criterion as a requirement (any status; deprecated ones are marked).\n" +
        "*axe-core rules* are the rules whose `wcagNNN` tag names the criterion in axe-core 4.14.0. *Implemented* lists the\n" +
        "rules this package implements. *Automation* says how far a machine can decide the criterion: *fully* when every failure\n" +
        "condition can be decided mechanically, *partly* when some can and the rest need a person, *not* when it needs human\n" +
        "judgement or running behaviour a static check cannot see. *Basis* gives the reason. The assessments are data in\n" +
        "`Catalogue/Data/automation.json`.\n\n" +
        CoverageMatrix.ToMarkdown(BuiltInRules.All.Select(r => r.Descriptor));
}
