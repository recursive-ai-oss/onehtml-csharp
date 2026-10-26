using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

public sealed class CheckerTests
{
    private const string Page = "<!DOCTYPE html><html><head><title>t</title></head><body>" +
        "<div role=\"lnik\" id=\"a\">x</div>" +
        "<div aria-labelled=\"y\">z</div>" +
        "<button aria-hidden=\"true\">hidden but tabbable</button>" +
        "<ul><li>fine</li></ul>" +
        "</body></html>";

    private static CheckReport Check(string html, CheckOptions? options = null, IEnumerable<IAccessibilityRule>? privateRules = null) =>
        new Checker(privateRules).Check(Document.Parse(html), options);

    [Fact]
    public void Built_in_rules_have_unique_ids_sources_and_fix_decisions()
    {
        IReadOnlyList<IAccessibilityRule> rules = BuiltInRules.All;
        Assert.Equal(rules.Count, rules.Select(r => r.Descriptor.Id).Distinct(StringComparer.Ordinal).Count());
        foreach (IAccessibilityRule r in rules)
        {
            Assert.NotEmpty(r.Description);
            Assert.NotEmpty(r.Help);
            Assert.True((r.Descriptor.Fix is null) != (r.Descriptor.NoFixReason is null), r.Descriptor.Id);
            // Only rules that read layout (text laid out on the page, the scroll size of a region) need rendered facts.
            Assert.Equal(r.Descriptor.Id is "label-content-name-mismatch" or "scrollable-region-focusable" ? RuleNeeds.RenderedFacts : RuleNeeds.Static, r.Descriptor.Needs);
        }

        // Every non-deprecated axe rule of the ARIA area, and presentation-role-conflict, is implemented.
        string[] expected = [.. AxeRules.All.Where(a => (a.Id.StartsWith("aria-", StringComparison.Ordinal) || a.Id == "presentation-role-conflict") && a.Group != "deprecated").Select(a => a.Id)];
        Assert.Equal(26 - 1, expected.Length);
        foreach (string id in expected)
        {
            Assert.Contains(rules, r => r.Descriptor.Id == id);
        }

        // ACT ids in the descriptors exist in the catalogue.
        foreach (RuleSource s in rules.SelectMany(r => r.Descriptor.Sources).Where(s => s.Kind == RuleSourceKind.Act))
        {
            Assert.NotNull(ActRules.Find(s.Id));
        }
    }

    [Fact]
    public void Results_are_ordered_by_rule_then_document_order_and_reproducible()
    {
        CheckReport a = Check(Page);
        CheckReport b = Checker.WithRules(BuiltInRules.All.Reverse()).Check(Document.Parse(Page));
        Assert.Equal(a.ToNativeJson(), b.ToNativeJson());
        string[] ids = [.. a.Results.Select(r => r.RuleId)];
        Assert.Equal(ids.OrderBy(i => i, StringComparer.Ordinal), ids);
        foreach (IGrouping<string, RuleResult> g in a.Results.Where(r => !r.Target.IsNull).GroupBy(r => r.RuleId))
        {
            int[] idx = [.. g.Select(r => r.Target.Index)];
            Assert.Equal(idx.OrderBy(i => i), idx);
        }
    }

    [Fact]
    public void Selection_by_rule_id_tag_criterion_and_profile_intersects()
    {
        CheckReport one = Check(Page, new CheckOptions { RuleIds = ["aria-roles"] });
        Assert.Equal(["aria-roles"], one.RulesRun.Select(r => r.Descriptor.Id));
        Assert.Single(one.WithOutcome(Outcome.Failed));

        CheckReport tag = Check(Page, new CheckOptions { Tags = ["best-practice"] });
        Assert.All(tag.RulesRun, r => Assert.True(r.Descriptor.BestPractice));
        Assert.Contains(tag.RulesRun, r => r.Descriptor.Id == "aria-allowed-role");

        CheckReport crit = Check(Page, new CheckOptions { Criteria = ["1.3.1"] });
        Assert.Contains(crit.RulesRun, r => r.Descriptor.Id == "aria-required-children");
        Assert.DoesNotContain(crit.RulesRun, r => r.Descriptor.Id == "aria-roles");

        CheckReport profile = Check(Page, new CheckOptions { Profile = ValidationProfile.Wcag21AA });
        Assert.DoesNotContain(profile.RulesRun, r => r.Descriptor.BestPractice && r.Descriptor.Criteria.Count == 0);
        CheckReport both = Check(Page, new CheckOptions { Profile = ValidationProfile.Wcag21AA, RuleIds = ["aria-allowed-role", "aria-roles"] });
        Assert.Equal(["aria-roles"], both.RulesRun.Select(r => r.Descriptor.Id));

        Assert.Throws<ArgumentException>(() => Check(Page, new CheckOptions { RuleIds = ["no-such-rule"] }));
    }

    [Fact]
    public void A_rule_with_nothing_to_check_is_inapplicable_and_passes_can_be_dropped()
    {
        CheckReport r = Check("<p>hi</p>", new CheckOptions { RuleIds = ["aria-roles"] });
        RuleResult only = Assert.Single(r.Results);
        Assert.Equal(Outcome.Inapplicable, only.Outcome);
        Assert.True(only.Target.IsNull);

        CheckReport noPasses = Check("<div role=button aria-label=x></div><div role=nope></div>", new CheckOptions { RuleIds = ["aria-roles"], IncludePasses = false });
        Assert.DoesNotContain(noPasses.Results, x => x.Outcome == Outcome.Passed);
        Assert.Single(noPasses.Results);
    }

    [Fact]
    public void Task_impact_follows_the_tree()
    {
        CheckReport r = Check("<form><div role=\"nope\">x</div></form><p><span role=\"nope\">y</span></p><button role=\"nope\">z</button>", new CheckOptions { RuleIds = ["aria-roles"] });
        RuleResult[] f = [.. r.WithOutcome(Outcome.Failed)];
        Assert.Equal(3, f.Length);
        Assert.Equal(("form", TaskImpact.BlocksTask), (f[0].TaskImpactReason, f[0].TaskImpact));
        Assert.Equal(TaskImpact.Cosmetic, f[1].TaskImpact);
        Assert.Null(f[1].TaskImpactReason);
        Assert.Equal("interactive", f[2].TaskImpactReason);
    }

    [Fact]
    public void Results_under_an_interaction_state_are_labelled_css_only()
    {
        Document doc = Document.Parse("<button id=b role=\"nope\">z</button>");
        var styles = Css.DocumentStyles.Create(doc);
        var plain = new Checker().Check(AccessibilityTree.Build(doc, styles), new CheckOptions { RuleIds = ["aria-roles"] });
        Assert.All(plain.Results, r => Assert.False(r.CssOnly));
        var focused = new Checker().Check(AccessibilityTree.Build(doc, styles, Css.StyleContext.Default.WithFocus(doc.Root.QuerySelector("#b"))), new CheckOptions { RuleIds = ["aria-roles"] });
        Assert.All(focused.Results, r => Assert.True(r.CssOnly));
        Assert.Contains("\"cssOnly\":true", focused.ToNativeJson());
    }

    private sealed class MyRule : IAccessibilityRule
    {
        public Catalogue.RuleDescriptor Descriptor { get; } = new("private-no-h1", [RuleSource.Own("private-no-h1")], [], true, RuleNeeds.Static, null, "Needs the author's judgement.", ["private"]);

        public string Description => "The page needs an h1.";

        public string Help => "Add one.";

        public Impact Impact => Impact.Minor;

        public void Evaluate(CheckContext context, RuleReporter reporter)
        {
            Node h1 = context.Document.Root.QuerySelector("h1");
            if (h1.IsNull)
            {
                reporter.Failed(context.Document.DocumentElement, "No h1.");
            }
            else
            {
                reporter.Passed(h1);
            }
        }
    }

    [Fact]
    public void Private_rules_run_alongside_the_built_in_ones()
    {
        CheckReport r = Check("<p>x</p>", new CheckOptions { Tags = ["private"] }, [new MyRule()]);
        RuleResult f = Assert.Single(r.Results);
        Assert.Equal("private-no-h1", f.RuleId);
        Assert.Equal(Outcome.Failed, f.Outcome);
        Assert.Throws<ArgumentException>(() => new Checker([new MyRule(), new MyRule()]));
        Assert.Contains("\"private-no-h1\"", r.ToAxeJson());
    }

    [Fact]
    public void Cancellation_is_observed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => new Checker().Check(Document.Parse("<p>x</p>"), null, cts.Token));
    }

    [Fact]
    public void Result_limit_is_enforced_before_the_report_grows()
    {
        string html = string.Concat(Enumerable.Repeat("<div role=\"nope\"></div>", 500));
        CheckReport r = Check(html, new CheckOptions { RuleIds = ["aria-roles"], MaxResultsPerRule = 10 });
        Assert.True(r.LimitReached);
        Assert.Equal(10, r.Results.Count);
    }

    [Fact]
    public void A_large_hostile_page_is_checked_without_trouble()
    {
        string many = string.Concat(Enumerable.Repeat("<div role=\"tab nope\" aria-checked=\"yes\" aria-x=\"1\" aria-owns=\"a b c\"><span aria-hidden=\"true\"><a href=\"#\">l</a></span></div>", 4000));
        CheckReport r = Check(many);
        Assert.True(r.Results.Count > 4000);
        string deep = string.Concat(Enumerable.Repeat("<div aria-hidden=\"true\">", 3000)) + "<a href=\"#\">x</a>";
        CheckReport d = Check(deep, new CheckOptions { RuleIds = ["aria-hidden-focus"] });
        Assert.NotEmpty(d.Results);
    }

    [Fact]
    public void Exports_are_valid_json_with_the_documented_top_level_shape()
    {
        CheckReport r = Check(Page);
        using JsonDocument native = JsonDocument.Parse(r.ToNativeJson());
        Assert.Equal("onehtml-results", native.RootElement.GetProperty("format").GetString());
        Assert.Equal(1, native.RootElement.GetProperty("version").GetInt32());
        using JsonDocument axe = JsonDocument.Parse(r.ToAxeJson());
        foreach (string key in new[] { "violations", "passes", "incomplete", "inapplicable" })
        {
            Assert.Equal(JsonValueKind.Array, axe.RootElement.GetProperty(key).ValueKind);
        }

        Assert.NotEmpty(axe.RootElement.GetProperty("violations").EnumerateArray());
    }
}
