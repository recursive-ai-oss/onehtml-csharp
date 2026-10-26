using System;
using System.IO;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>
/// Byte-for-byte checks of both exports on a page that exercises the contrast, spacing, orientation and media rules. Set
/// <c>ONEHTML_UPDATE_GOLDEN=1</c> to rewrite the expected files after a deliberate change, and read the diff.
/// </summary>
public sealed class VisualGoldenTests
{
    private static string Dir { get; } = Path.Combine(AriaActTests.Repo, "tests", "OneHtml.Accessibility.Validation.Tests", "Golden");

    private static CheckReport Report()
    {
        Document doc = Document.Parse(File.ReadAllBytes(Path.Combine(Dir, "visual-page.html")));
        return new Checker().Check(AccessibilityTree.Build(doc, DocumentStyles.Create(doc)));
    }

    private static void Compare(string name, string actual)
    {
        string path = Path.Combine(Dir, name);
        if (Environment.GetEnvironmentVariable("ONEHTML_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual);
        }

        Assert.Equal(File.ReadAllText(path), actual);
    }

    [Fact]
    public void Native_json_matches_the_golden_file() =>
        Compare("visual-page.native.json", Report().ToNativeJson(new ExportOptions { Indented = true, Url = "https://example.test/visual" }));

    [Fact]
    public void Axe_compatible_json_matches_the_golden_file() =>
        Compare("visual-page.axe.json", Report().ToAxeJson(new ExportOptions { Indented = true, Url = "https://example.test/visual" }));

    [Fact]
    public void The_page_shows_each_outcome_the_rules_can_give()
    {
        CheckReport r = Report();
        Assert.Contains(r.Results, x => x.RuleId == "color-contrast" && x.Outcome == Outcome.Failed);
        Assert.Contains(r.Results, x => x.RuleId == "color-contrast" && x.Outcome == Outcome.NeedsRender);
        Assert.Contains(r.Results, x => x.RuleId == "color-contrast" && x.Outcome == Outcome.Passed);
        Assert.Contains(r.Results, x => x.RuleId == "link-in-text-block" && x.Outcome == Outcome.Failed);
        Assert.Contains(r.Results, x => x.RuleId == "avoid-inline-spacing" && x.Outcome == Outcome.Failed && x.SuggestedEdits.Count == 1);
        Assert.Contains(r.Results, x => x.RuleId == "css-orientation-lock" && x.Outcome == Outcome.Failed);
        Assert.Contains(r.Results, x => x.RuleId == "no-autoplay-audio" && x.Outcome == Outcome.NeedsReview);
        Assert.Contains(r.Results, x => x.RuleId == "no-autoplay-audio" && x.Outcome == Outcome.Passed);
    }
}
