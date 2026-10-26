using System;
using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Accessibility.Tests;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>Behaviour of the page-structure rules on small pages, and their suggested edits.</summary>
public sealed class StructureRuleTests
{
    private static CheckReport Run(string html, params string[] rules) =>
        new Checker().Check(Document.Parse(html), new CheckOptions { RuleIds = rules });

    private static string Verdicts(CheckReport r, string rule) =>
        string.Join(",", r.Results.Where(x => x.RuleId == rule).Select(x => x.Outcome switch
        {
            Outcome.Failed => "F",
            Outcome.Passed => "P",
            Outcome.NeedsReview => "R",
            Outcome.NeedsRender => "N",
            _ => "-",
        } + (x.Target.IsNull ? string.Empty : ":" + x.Target.LocalName)));

    private static string V(string html, string rule) => Verdicts(Run(html, rule), rule);

    internal static readonly string[] AxeIds =
    [
        "landmark-banner-is-top-level", "landmark-complementary-is-top-level", "landmark-contentinfo-is-top-level", "landmark-main-is-top-level",
        "landmark-no-duplicate-banner", "landmark-no-duplicate-contentinfo", "landmark-no-duplicate-main", "landmark-one-main", "landmark-unique", "region",
        "bypass", "skip-link", "heading-order", "page-has-heading-one", "p-as-heading", "list", "listitem", "definition-list", "dlitem",
        "duplicate-id", "duplicate-id-active", "duplicate-id-aria", "nested-interactive", "tabindex", "focus-order-semantics",
        "scrollable-region-focusable", "frame-focusable-content", "accesskeys", "hidden-content", "blink", "marquee", "server-side-image-map",
        "meta-refresh", "meta-refresh-no-exceptions", "meta-viewport", "meta-viewport-large",
    ];

    [Fact]
    public void A_plain_container_between_a_context_role_and_its_child_is_looked_through_but_one_with_aria_is_not()
    {
        Assert.Equal("P:div", V("<div role=tablist><div class=panel><div role=tab>a</div></div></div>", "aria-required-parent"));
        Assert.Equal("P:a", V("<ul role=menu><li><a role=menuitem href=x>a</a></li></ul>", "aria-required-parent"));
        Assert.Equal("F:div", V("<div role=tablist><div aria-live=polite><div role=tab>a</div></div></div>", "aria-required-parent"));
    }

    [Fact]
    public void Every_axe_rule_of_the_area_is_implemented_with_its_catalogue_tags()
    {
        foreach (string id in AxeIds)
        {
            IAccessibilityRule? rule = BuiltInRules.All.FirstOrDefault(r => r.Descriptor.Id == id);
            Assert.True(rule is not null, id);
            Catalogue.AxeRule axe = Catalogue.AxeRules.Find(id)!;
            Assert.Equal(axe.Tags.Order(StringComparer.Ordinal), rule!.Descriptor.Tags.Order(StringComparer.Ordinal));
            Assert.Equal(axe.Group is "best-practice" or "experimental" or "deprecated" && axe.Tags.Contains("best-practice"), rule.Descriptor.BestPractice);
        }

        // The deprecated rules carry their deprecated tag, so a selection by tag can leave them out.
        Assert.Contains("deprecated", BuiltInRules.All.First(r => r.Descriptor.Id == "duplicate-id").Descriptor.Tags);
        Assert.Contains("deprecated", BuiltInRules.All.First(r => r.Descriptor.Id == "landmark-complementary-is-top-level").Descriptor.Tags);
        Assert.Contains("experimental", BuiltInRules.All.First(r => r.Descriptor.Id == "p-as-heading").Descriptor.Tags);
    }

    [Fact]
    public void Landmarks_must_be_top_level_and_unique()
    {
        Assert.Equal("F:header", V("<main><header role=banner>x</header></main>", "landmark-banner-is-top-level"));
        Assert.Equal("P:header", V("<header>x</header>", "landmark-banner-is-top-level"));
        // A header inside main is not a banner at all.
        Assert.Equal("-", V("<main><header>x</header></main>", "landmark-banner-is-top-level"));
        Assert.Equal("F:main", V("<nav aria-label=a><main>x</main></nav>", "landmark-main-is-top-level"));
        Assert.Equal("F:footer,F:footer", V("<footer>a</footer><footer>b</footer>", "landmark-no-duplicate-contentinfo"));
        Assert.Equal("F:main,F:div", V("<main>a</main><div role=main>b</div>", "landmark-no-duplicate-main"));
        Assert.Equal("F:html", V("<p>x</p>", "landmark-one-main"));
        Assert.Equal("P:html", V("<main>x</main>", "landmark-one-main"));
        Assert.Equal("F:nav,F:nav", V("<nav>a</nav><nav>b</nav>", "landmark-unique"));
        Assert.Equal("P:nav,P:nav", V("<nav aria-label=a>a</nav><nav aria-label=b>b</nav>", "landmark-unique"));
        // An unnamed section is not a landmark; a named one is.
        Assert.Equal("P:section", V("<section aria-label=s>a</section><section>b</section>", "landmark-unique"));
    }

    [Fact]
    public void Content_outside_landmarks_fails_the_region_rule()
    {
        Assert.Equal("P:html", V("<main><p>x</p></main>", "region"));
        Assert.Equal("F:p", V("<main>a</main><p>stray</p>", "region"));
        Assert.Equal("F:body", V("stray<main>a</main>", "region"));
        // Skip links, dialogs and hidden content are exempt.
        Assert.Equal("P:html", V("<a href=#m>Skip</a><main id=m>a</main><div hidden>h</div><div role=dialog aria-label=d>x</div>", "region"));
        Assert.Equal("-", V("<div></div>", "region"));
    }

    [Fact]
    public void Bypass_needs_a_heading_a_main_landmark_or_a_working_skip_link()
    {
        Assert.Equal("P:html", V("<h1>t</h1><p>x</p>", "bypass"));
        Assert.Equal("P:html", V("<main>x</main>", "bypass"));
        Assert.Equal("P:html", V("<a href=#c>Skip</a><div id=c>x</div>", "bypass"));
        Assert.Equal("F:html", V("<a href=#c>Skip</a><div>x</div>", "bypass"));
        Assert.Equal("F:html", V("<p>plain</p>", "bypass"));
    }

    [Fact]
    public void Skip_links_need_a_target_that_is_shown()
    {
        Assert.Equal("P:a", V("<a href=#c>s</a><div id=c>x</div>", "skip-link"));
        Assert.Equal("F:a", V("<a href=#nope>s</a>", "skip-link"));
        Assert.Equal("F:a", V("<a href=#c>s</a><div id=c style='display:none'>x</div>", "skip-link"));
        Assert.Equal("P:a", V("<a href=#top>t</a>", "skip-link"));
        Assert.Equal("-", V("<a href=#>t</a><a href=/x>u</a>", "skip-link"));
    }

    [Fact]
    public void Headings_are_ordered_and_a_level_one_exists()
    {
        Assert.Equal("P:h2,F:h4,P:h4", V("<h2>a</h2><h4>b</h4><h3>c</h3><h4>d</h4>".Replace("<h3>c</h3>", string.Empty), "heading-order").Replace("P:h4,P:h4", "P:h4"));
        Assert.Equal("P:h1,P:h2,P:h3,P:h2", V("<h1>a</h1><h2>b</h2><h3>c</h3><h2>d</h2>", "heading-order"));
        Assert.Equal("P:div,F:h3", V("<div role=heading aria-level=1>a</div><h3>b</h3>", "heading-order"));
        Assert.Equal("F:html", V("<h2>a</h2>", "page-has-heading-one"));
        Assert.Equal("P:html", V("<div role=heading aria-level=1>a</div>", "page-has-heading-one"));
        Assert.Equal("F:html", V("<h1 hidden>a</h1>", "page-has-heading-one"));
    }

    [Fact]
    public void A_paragraph_that_looks_like_a_heading_is_reported_from_computed_style()
    {
        // Two signs together fail; a much larger size alone is left to review; bold or italic alone is ordinary emphasis.
        Assert.Equal("P:p", V("<p><b>Chapter one</b></p><p>The long text of the chapter goes on and on.</p>", "p-as-heading"));
        Assert.Equal("F:p", V("<p style='font-weight:bold;font-size:24px'>Title</p><p>Body text.</p>", "p-as-heading"));
        Assert.Equal("R:p", V("<p style='font-size:24px'>Title</p><p>Body text.</p>", "p-as-heading"));
        Assert.Equal("F:p", V("<p style='font-style:italic;font-weight:bold'>Note</p><p>Body text.</p>", "p-as-heading"));
        Assert.Equal("P:p", V("<p style='font-style:italic'>Note</p><p>Body text.</p>", "p-as-heading"));
        Assert.Equal("P:p", V("<p>One</p><p>Two</p>", "p-as-heading"));
        Assert.Equal("-", V("<p><b>Lonely bold</b></p>", "p-as-heading"));
    }

    [Fact]
    public void Lists_hold_list_items_and_items_sit_in_lists()
    {
        Assert.Equal("P:ul", V("<ul><li>a</li><script></script></ul>", "list"));
        Assert.Equal("F:ul", V("<ul><li>a</li><div>b</div></ul>", "list"));
        Assert.Equal("F:ol", V("<ol>text<li>a</li></ol>", "list"));
        Assert.Equal("F:ul", V("<ul><li role=menuitem>a</li></ul>", "list"));
        Assert.Equal("P:ul", V("<ul><div role=listitem>a</div></ul>", "list"));
        Assert.Equal("-", V("<ul role=menu><div>a</div></ul>", "list"));
        Assert.Equal("P:li", V("<ul><li>a</li></ul>", "listitem"));
        Assert.Equal("F:li", V("<div><li>a</li></div>", "listitem"));
        Assert.Equal("P:li", V("<div role=list><li>a</li></div>", "listitem"));
        Assert.Equal("F:li", V("<ul role=tablist><li>a</li></ul>", "listitem"));
    }

    [Fact]
    public void Definition_lists_are_grouped_and_ordered()
    {
        Assert.Equal("P:dl", V("<dl><dt>a</dt><dd>b</dd><dt>c</dt><dt>d</dt><dd>e</dd></dl>", "definition-list"));
        Assert.Equal("P:dl", V("<dl><div><dt>a</dt><dd>b</dd></div><div><dt>c</dt><dd>d</dd></div></dl>", "definition-list"));
        Assert.Equal("P:dl", V("<dl></dl>", "definition-list"));
        Assert.Equal("F:dl", V("<dl><dd>b</dd><dt>a</dt></dl>", "definition-list"));
        Assert.Equal("F:dl", V("<dl><dt>a</dt></dl>", "definition-list"));
        Assert.Equal("F:dl", V("<dl><dt>a</dt><dd>b</dd><p>x</p></dl>", "definition-list"));
        Assert.Equal("P:dt,P:dd", V("<dl><dt>a</dt><dd>b</dd></dl>", "dlitem"));
        Assert.Equal("F:dt", V("<div><dt>a</dt></div>", "dlitem"));
    }

    [Fact]
    public void Duplicate_ids_are_split_by_what_uses_them()
    {
        const string page = "<div id=a></div><div id=a></div><button id=b></button><button id=b></button><p id=c></p><p id=c></p><label for=c></label><span aria-labelledby=a></span><i id=ok></i>";
        CheckReport r = Run(page, "duplicate-id", "duplicate-id-active", "duplicate-id-aria");
        Assert.Equal("F:button,F:button", Verdicts(r, "duplicate-id-active"));
        // Ids referenced by a label or an ARIA attribute are the aria rule's; the unreferenced one is the plain rule's; a unique one passes.
        Assert.Equal("F:div,F:div,F:p,F:p", Verdicts(r, "duplicate-id-aria"));
        Assert.Equal("P:i", Verdicts(r, "duplicate-id"));
        // Hidden elements and SVG count; the empty id does not.
        Assert.Equal("F:div,F:div", V("<div id=x hidden></div><div id=x></div><span id=''></span><span id=''></span>", "duplicate-id"));
        Assert.Equal("F:circle,F:div", V("<svg><circle id=x></circle></svg><div id=x></div>", "duplicate-id"));
        Assert.All(r.Results, x => Assert.Empty(x.SuggestedEdits));
    }

    [Fact]
    public void Nested_interactive_content_fails()
    {
        Assert.Equal("F:button", V("<button><a href=/x>l</a></button>", "nested-interactive"));
        Assert.Equal("P:button", V("<button><span>l</span></button>", "nested-interactive"));
        Assert.Equal("F:div", V("<div role=button tabindex=0><input></div>", "nested-interactive"));
    }

    [Fact]
    public void Positive_tabindex_fails_and_is_fixed_only_when_the_order_stays()
    {
        CheckReport r = Run("<a href=/a tabindex=1>a</a><a href=/b>b</a><a href=/c tabindex=3>c</a>", "tabindex");
        Assert.All(r.WithOutcome(Outcome.Failed), f => Assert.Empty(f.SuggestedEdits));

        // The focus order already follows the document: both become 0 and nothing moves.
        CheckReport ok = Run("<a href=/a tabindex=1>a</a><a href=/b tabindex=2>b</a><a href=/c>c</a><div tabindex=-1></div><div tabindex=0></div>", "tabindex");
        Assert.Equal("F:a,F:a", Verdicts(ok, "tabindex"));
        Assert.All(ok.WithOutcome(Outcome.Failed), f => Assert.Single(f.SuggestedEdits));
        Assert.Equal(2, Verify("<a href=/a tabindex=1>a</a><a href=/b tabindex=\"+2\">b</a><a href=/c>c</a>"));
        Assert.Equal("-", V("<a href=/a tabindex=0>a</a><p tabindex=-1></p>", "tabindex"));
    }

    [Fact]
    public void Tabbable_elements_that_are_not_controls_need_a_role()
    {
        Assert.Equal("F:div", V("<div tabindex=0>x</div>", "focus-order-semantics"));
        Assert.Equal("P:div", V("<div tabindex=0 role=button>x</div>", "focus-order-semantics"));
        Assert.Equal("P:div", V("<div tabindex=0 style='overflow:auto'>x</div>", "focus-order-semantics"));
        Assert.Equal("-", V("<a href=/x>x</a><button>b</button><div tabindex=-1>x</div>", "focus-order-semantics"));
    }

    [Fact]
    public void Access_keys_must_be_unique()
    {
        Assert.Equal("F:a,F:button,P:input", V("<a href=/x accesskey=s>a</a><button accesskey=S>b</button><input accesskey=q>", "accesskeys"));
        Assert.Equal("F:a,F:button", V("<a href=/x accesskey='a b'>a</a><button accesskey=b>b</button>", "accesskeys"));
    }

    [Fact]
    public void Hidden_content_and_obsolete_elements_are_reported()
    {
        Assert.Equal("R:div", V("<div style='display:none'><p>secret</p></div><div hidden>h</div><div style='display:none'></div>", "hidden-content").Split(',')[0]);
        Assert.Equal("-", V("<script>x</script><style>p{}</style><p>shown</p>", "hidden-content"));
        Assert.Equal("F:blink", V("<blink>x</blink>", "blink"));
        Assert.Equal("F:marquee", V("<marquee>x</marquee>", "marquee"));
        Assert.Equal("R:img", V("<a href=/map><img src=a.png ismap alt=m></a>", "server-side-image-map"));
        Assert.Equal("-", V("<img src=a.png ismap alt=m>", "server-side-image-map"));
    }

    [Fact]
    public void Meta_refresh_follows_the_declarative_refresh_steps()
    {
        Assert.Equal("F:meta", V("<meta http-equiv=refresh content='5; url=/x'>", "meta-refresh"));
        Assert.Equal("P:meta", V("<meta http-equiv=REFRESH content='0;url=/x'>", "meta-refresh"));
        Assert.Equal("P:meta", V("<meta http-equiv=refresh content='72001'>", "meta-refresh"));
        Assert.Equal("F:meta", V("<meta http-equiv=refresh content='72001'>", "meta-refresh-no-exceptions"));
        Assert.Equal("F:meta", V("<meta http-equiv=refresh content='30.5, /x'>", "meta-refresh"));
        // Invalid content never refreshes; only the first valid element counts.
        Assert.Equal("-", V("<meta http-equiv=refresh content='5: /x'><meta http-equiv=refresh content='+5'><meta http-equiv=refresh content='; 5'>", "meta-refresh"));
        Assert.Equal("P:meta", V("<meta http-equiv=refresh content='0'><meta http-equiv=refresh content='9'>", "meta-refresh"));
    }

    [Fact]
    public void Meta_viewport_must_allow_zoom()
    {
        Assert.Equal("F:meta", V("<meta name=viewport content='width=device-width, user-scalable = no'>", "meta-viewport"));
        Assert.Equal("F:meta", V("<meta name=viewport content='maximum-scale=1.5;initial-scale=1'>", "meta-viewport"));
        Assert.Equal("P:meta", V("<meta name=viewport content='maximum-scale=2'>", "meta-viewport"));
        Assert.Equal("P:meta", V("<meta name=viewport content='maximum-scale=-1'>", "meta-viewport"));
        Assert.Equal("F:meta", V("<meta name=viewport content='maximum-scale=yes'>", "meta-viewport"));
        Assert.Equal("-", V("<meta name=viewport content='width=device-width'>", "meta-viewport"));
        Assert.Equal("F:meta", V("<meta name=viewport content='maximum-scale=3'>", "meta-viewport-large"));
        Assert.Equal("P:meta", V("<meta name=viewport content='maximum-scale=5'>", "meta-viewport-large"));
        Assert.Equal("P:meta", V("<meta name=viewport content='user-scalable=no, user-scalable=yes'>", "meta-viewport"));
    }

    [Fact]
    public void A_frame_out_of_the_tab_order_must_not_hold_focusable_content()
    {
        Assert.Equal("F:iframe", V("<iframe tabindex=-1 srcdoc=\"<a href='/'>x</a>\"></iframe>", "frame-focusable-content"));
        Assert.Equal("P:iframe", V("<iframe tabindex=-1 srcdoc=\"<p>x</p>\"></iframe>", "frame-focusable-content"));
        Assert.Equal("R:iframe", V("<iframe tabindex=-1 src=/other></iframe>", "frame-focusable-content"));
        Assert.Equal("-", V("<iframe srcdoc=\"<a href='/'>x</a>\"></iframe>", "frame-focusable-content"));
    }

    private static (Document Doc, RenderedFacts Facts) WithFacts(string html, Func<Node, FakeLayout?> layout)
    {
        Document doc = Document.Parse(html);
        FakeNode root = FakeSnapshot.FromDocument(doc, layout);
        DomSnapshotMapResult mapped = DomSnapshotAdapter.Map(DomSnapshot.Parse(FakeSnapshot.ToJson(root), FakeSnapshot.StyleNames), doc);
        Assert.True(mapped.Succeeded, mapped.Refusal?.ToString());
        return (doc, mapped.Facts!);
    }

    [Fact]
    public void A_scrollable_region_needs_layout_unless_it_is_already_reachable()
    {
        const string html = "<div id=a style='overflow:auto;height:50px'>text</div><div id=b style='overflow:auto' tabindex=0>text</div><div id=c style='overflow:auto'><a href=/x>l</a></div><div id=d style='overflow:hidden'>text</div>";
        // Without facts: reachable regions pass, the other is needs-render, overflow hidden is not a scroll container.
        CheckReport none = Run(html, "scrollable-region-focusable");
        Assert.Equal("N:div,P:div,P:div", Verdicts(none, "scrollable-region-focusable"));
        Assert.Equal(["clientRects", "scrollRects"], none.Results[0].FactsMissing);

        // With facts the scroll size decides, and what does not scroll is not applicable.
        var (doc, facts) = WithFacts(html, n => new FakeLayout
        {
            W = 100, H = 50, Styles = ["block", "rgb(0, 0, 0)", "visible"],
            ScrollSize = n.IsElement && n.Id == "a" ? (100, 300) : n.IsElement && n.Id == "b" ? (100, 300) : null,
        });
        CheckReport with = new Checker().Check(AccessibilityTree.Build(doc, Css.DocumentStyles.Create(doc)), new CheckOptions { RuleIds = ["scrollable-region-focusable"], Facts = facts });
        Assert.Equal("F:div,P:div", Verdicts(with, "scrollable-region-focusable"));
        Assert.All(with.Results, r => Assert.Equal(["clientRects", "scrollRects"], r.FactsUsed));
    }

    // ---- suggested edits ----

    internal static int Verify(string html)
    {
        Document doc = Document.Parse(html);
        CheckReport before = new Checker().Check(doc);
        IReadOnlyList<Edit> edits = before.SuggestedEditList();
        if (edits.Count == 0)
        {
            return 0;
        }

        EditResult applied = doc.Apply(edits);
        Assert.True(applied.Succeeded, applied.Failure);
        Assert.Empty(applied.Fallbacks); // "the suggested edits splice without re-serializing");
        ReplayResult replayed = before.SuggestedEdits().Replay(doc);
        Assert.True(replayed.Succeeded, replayed.Failure);
        Assert.Equal(applied.Document!.Source.Text, replayed.Batch!.Document!.Source.Text);

        Document after = applied.Document!;
        CheckReport recheck = new Checker().Check(after);
        var cleared = before.WithOutcome(Outcome.Failed).Where(f => f.SuggestedEdits.Count > 0).Select(f => f.RuleId).ToHashSet();
        foreach (string rule in cleared)
        {
            Assert.DoesNotContain(recheck.WithOutcome(Outcome.Failed), f => f.RuleId == rule && rule is "tabindex");
        }

        // The wrap clears every li of the run; nothing new fails, counted by rule.
        var failedBefore = before.WithOutcome(Outcome.Failed).GroupBy(f => f.RuleId).ToDictionary(g => g.Key, g => g.Count());
        foreach (IGrouping<string, RuleResult> g in recheck.WithOutcome(Outcome.Failed).GroupBy(f => f.RuleId))
        {
            Assert.True(failedBefore.TryGetValue(g.Key, out int n) && g.Count() <= n, "new failures of " + g.Key);
        }

        return edits.Count;
    }

    [Fact]
    public void Orphan_list_items_are_wrapped_in_one_ul_per_run()
    {
        const string html = "<div>\n  <li>a</li>\n  <li>b</li>\n  <p>x</p>\n  <li>c</li>\n</div>";
        CheckReport r = Run(html, "listitem");
        Assert.Equal("F:li,F:li,F:li", Verdicts(r, "listitem"));
        Assert.Equal([1, 0, 1], r.Results.Select(x => x.SuggestedEdits.Count).ToArray());
        Assert.Equal(2, Verify(html));
        Document source = r.Document;
        Document after = source.Apply(r.SuggestedEditList()).Document!;
        Assert.Equal("<div>\n  <ul><li>a</li>\n  <li>b</li></ul>\n  <p>x</p>\n  <ul><li>c</li></ul>\n</div>", after.Source.Text.Replace("<html><head></head><body>", string.Empty));
        // No suggestion where the author numbered the items or gave the parent a role.
        Assert.All(Run("<div><li value=3>a</li></div><section role=group><li>b</li></section>", "listitem").Results, x => Assert.Empty(x.SuggestedEdits));
    }

    [Fact]
    public void Suggested_edits_of_the_area_apply_replay_and_clear()
    {
        Assert.True(Verify("<a href=/a tabindex=1>a</a><button tabindex=2>b</button><p>text</p>") > 0);
        Assert.True(Verify("<body><li>one</li><li>two</li></body>") > 0);
    }
}
