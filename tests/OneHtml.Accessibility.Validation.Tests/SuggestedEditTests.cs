using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>
/// Every suggested edit must apply through <c>Apply</c> and through replay of its saved form, clear the finding it
/// answers, and leave no failure that was not there before.
/// </summary>
public sealed class SuggestedEditTests
{
    private static readonly string[] OwnCases =
    [
        "<body aria-hidden=\"true\"><p>x</p></body>",
        "<div role=\"directory\"><div role=\"listitem\">a</div></div>",
        "<div role=\"doc-endnote\">a</div>",
        "<input type=\"checkbox\" aria-checked=\"true\"><input type=\"checkbox\" checked aria-checked=\"false\">",
        "<div aria-label=\"x\">no role</div><span aria-labelledby=\"a\">y</span><p id=a>z</p>",
        "<div aria-hidden=\"yes\" aria-live=\"loud\" aria-colspan=\"two\">x</div>",
        "<div role=\"slider\" aria-valuenow=\"high\" tabindex=\"0\">x</div>",
        "<p aria-foo=\"1\" aria-bar>x</p><svg aria-baz=\"1\"></svg>",
    ];

    private static IEnumerable<string> ActFailedPages()
    {
        var cases = Catalogue.ActTestCases.Parse(File.ReadAllText(Path.Combine(AriaActTests.Repo, "testdata", "act", "testcases.json")));
        string[] acts = AriaActTests.Map.Select(m => m.Act).ToArray();
        foreach (Catalogue.ActTestCase c in cases.Where(c => acts.Contains(c.RuleId)))
        {
            yield return File.ReadAllText(Path.Combine(AriaActTests.Repo, "testdata", "act", c.RelativePath));
        }
    }

    // The region rule is left out: removing aria-hidden from the body exposes its content, which the region rule then
    // judges for the first time; that is not a failure the edit introduced.
    private static HashSet<(string Rule, int Index)> Failures(CheckReport r) =>
        [.. r.WithOutcome(Outcome.Failed).Where(f => f.RuleId != "region").Select(f => (f.RuleId, f.Target.Index))];

    private static int Verify(string html)
    {
        Document doc = Document.Parse(html);
        CheckReport before = new Checker().Check(doc);
        IReadOnlyList<Edit> edits = before.SuggestedEditList();
        if (edits.Count == 0)
        {
            return 0;
        }

        // Every edit names its rule and criteria.
        Assert.All(edits, e => Assert.True(((IDictionary<string, object?>)e.Meta!).ContainsKey("rule")));

        EditResult applied = doc.Apply(edits);
        Assert.True(applied.Succeeded, applied.Failure);
        Document after = applied.Document!;
        Assert.False(after.NodeCount != doc.NodeCount, "attribute and role edits keep the node count");

        // The same edits, saved and replayed on the original document, give the same bytes.
        ReplayResult replayed = before.SuggestedEdits().Replay(doc);
        Assert.True(replayed.Succeeded, replayed.Failure);
        Assert.Equal(after.Source.Text, replayed.Batch!.Document!.Source.Text);

        HashSet<(string, int)> failedBefore = Failures(before);
        HashSet<(string, int)> failedAfter = Failures(new Checker().Check(after));
        // Findings that had a suggestion are cleared (by rule and node), and nothing new fails.
        foreach (RuleResult r in before.WithOutcome(Outcome.Failed).Where(r => r.SuggestedEdits.Count > 0))
        {
            Assert.DoesNotContain((r.RuleId, r.Target.Index), failedAfter);
        }

        Assert.Empty(failedAfter.Except(failedBefore));
        return edits.Count;
    }

    [Fact]
    public void Suggested_edits_clear_their_findings_on_the_act_cases()
    {
        int total = 0;
        foreach (string html in ActFailedPages())
        {
            total += Verify(html);
        }

        Assert.True(total > 0);
    }

    [Fact]
    public void Suggested_edits_clear_their_findings_on_hand_written_pages()
    {
        int total = 0;
        foreach (string html in OwnCases)
        {
            total += Verify(html);
        }

        Assert.True(total >= 7, "edits suggested: " + total);
    }

    [Fact]
    public void No_suggestion_for_a_required_attribute_content_or_a_judgement()
    {
        // Removing a required, invalid attribute would trade one failure for another; names and labels are content.
        CheckReport r = new Checker().Check(Document.Parse("<div role=\"slider\" aria-valuenow=\"high\">x</div><div role=\"button\"></div><div role=\"tab\" aria-checked=\"true\">t</div><button aria-hidden=\"true\">b</button>"));
        foreach (RuleResult f in r.WithOutcome(Outcome.Failed).Where(f => f.RuleId is "aria-valid-attr-value" or "aria-command-name" or "aria-allowed-attr" or "aria-hidden-focus" or "aria-tab-name"))
        {
            Assert.Empty(f.SuggestedEdits);
        }
    }

    // Which fix is right depends on what the author meant, so these three rules report the problem and offer no edit.
    [Theory]
    [InlineData("aria-valid-attr-value", "<div aria-hidden=\"yes\" aria-live=\"loud\" aria-colspan=\"two\">x</div>")]
    [InlineData("aria-valid-attr-value", "<p aria-expanded=\"maybe\">x</p>")]
    [InlineData("aria-prohibited-attr", "<div aria-label=\"x\">no role</div>")]
    [InlineData("aria-prohibited-attr", "<span aria-labelledby=\"a\">y</span><p id=a>z</p>")]
    [InlineData("listitem", "<div><li>a</li><li>b</li></div>")]
    [InlineData("listitem", "<body><li>one</li></body>")]
    public void Rules_with_no_public_fix_fail_without_a_suggestion(string rule, string html)
    {
        CheckReport r = new Checker().Check(Document.Parse(html));
        Assert.Contains(r.WithOutcome(Outcome.Failed), f => f.RuleId == rule);
        Assert.All(r.Results.Where(f => f.RuleId == rule), f => Assert.Empty(f.SuggestedEdits));
    }
}
