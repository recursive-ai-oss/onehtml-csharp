using System;
using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Accessibility.Tests;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

public sealed class NameRuleTests
{
    private static CheckReport Run(string html, string rule, RenderedFacts? facts = null) => Run(Document.Parse(html), rule, facts);

    private static CheckReport Run(Document doc, string rule, RenderedFacts? facts = null)
    {
        return new Checker().Check(AccessibilityTree.Build(doc, DocumentStyles.Create(doc)), new CheckOptions { RuleIds = [rule], Facts = facts });
    }

    private static string Verdicts(string html, string rule) =>
        string.Join(",", Run(html, rule).Results.Select(r => r.Outcome.ToString().ToLowerInvariant()));

    [Theory]
    [InlineData("<img src=a>", "image-alt", "failed")]
    [InlineData("<img src=a alt=x>", "image-alt", "passed")]
    [InlineData("<img src=a alt=''>", "image-alt", "passed")]
    [InlineData("<img src=a alt=' '>", "image-alt", "failed")]
    [InlineData("<img src=a alt=' ' aria-label=Logo>", "image-alt", "passed")]
    [InlineData("<img src=a hidden>", "image-alt", "inapplicable")]
    [InlineData("<input type=image src=a>", "input-image-alt", "failed")]
    [InlineData("<input type=image src=a alt=Go>", "input-image-alt", "passed")]
    [InlineData("<input type=submit>", "input-button-name", "passed")]
    [InlineData("<input type=button>", "input-button-name", "failed")]
    [InlineData("<button></button>", "button-name", "failed")]
    [InlineData("<button><img alt=Save src=a></button>", "button-name", "passed")]
    [InlineData("<a href=x></a>", "link-name", "failed")]
    [InlineData("<a href=x aria-label=Home></a>", "link-name", "passed")]
    [InlineData("<a>no href</a>", "link-name", "inapplicable")]
    [InlineData("<map name=m><area href=x></map>", "area-alt", "failed")]
    [InlineData("<map name=m><area href=x alt=Go></map>", "area-alt", "passed")]
    [InlineData("<select></select>", "select-name", "failed")]
    [InlineData("<label>Colour <select></select></label>", "select-name", "passed")]
    [InlineData("<details><summary></summary></details>", "summary-name", "failed")]
    [InlineData("<details><summary>More</summary></details>", "summary-name", "passed")]
    [InlineData("<input>", "label", "failed")]
    [InlineData("<input placeholder=Name>", "label", "passed")]
    [InlineData("<input disabled>", "label", "failed")]
    [InlineData("<input type=hidden>", "label", "inapplicable")]
    [InlineData("<label>Name <input></label>", "label-title-only", "passed")]
    [InlineData("<input title=Name>", "label-title-only", "failed")]
    [InlineData("<input id=a><label for=a>One</label><label for=a>Two</label>", "form-field-multiple-labels", "needsreview")]
    [InlineData("<input id=a><label for=a>One</label>", "form-field-multiple-labels", "passed")]
    [InlineData("<iframe></iframe>", "frame-title", "failed")]
    [InlineData("<iframe title=t></iframe>", "frame-title", "passed")]
    [InlineData("<iframe tabindex=-1></iframe>", "frame-title", "inapplicable")]
    [InlineData("<iframe title=t src=a></iframe><iframe title=T src=a></iframe>", "frame-title-unique", "passed,passed")]
    [InlineData("<iframe title=t src=a></iframe><iframe title=t src=b></iframe>", "frame-title-unique", "needsreview,needsreview")]
    [InlineData("<iframe title=t src=a></iframe><iframe title=u src=b></iframe>", "frame-title-unique", "inapplicable")]
    [InlineData("<title>Hi</title>", "document-title", "passed")]
    [InlineData("<title> </title>", "document-title", "failed")]
    [InlineData("<p>no title", "document-title", "failed")]
    [InlineData("<svg><title>in svg</title></svg>", "document-title", "failed")]
    [InlineData("<h2></h2>", "empty-heading", "failed")]
    [InlineData("<h2>Hi</h2>", "empty-heading", "passed")]
    [InlineData("<div role=heading aria-level=2></div>", "empty-heading", "failed")]
    [InlineData("<div role=img></div>", "role-img-alt", "failed")]
    [InlineData("<div role=img aria-label=Chart></div>", "role-img-alt", "passed")]
    [InlineData("<svg role=img></svg>", "svg-img-alt", "failed")]
    [InlineData("<svg role=img><title>Dot</title></svg>", "svg-img-alt", "passed")]
    [InlineData("<object data=a.png></object>", "object-alt", "failed")]
    [InlineData("<object data=a.png title=Photo></object>", "object-alt", "passed")]
    [InlineData("<object data=a.html></object>", "object-alt", "inapplicable")]
    [InlineData("<a href=a>Home</a><a href=a>home</a>", "identical-links-same-purpose", "passed,passed")]
    [InlineData("<a href=a>Home</a><a href=b>Home</a>", "identical-links-same-purpose", "needsreview,needsreview")]
    [InlineData("<a href=a>Home</a><a href=b>Away</a>", "identical-links-same-purpose", "inapplicable")]
    [InlineData("<a href=a>Sale <img alt=Sale src=x></a>", "image-redundant-alt", "failed")]
    [InlineData("<a href=a>Sale <img alt=Cart src=x></a>", "image-redundant-alt", "passed")]
    public void Rule_verdicts(string html, string rule, string expected) =>
        Assert.Equal(expected, Verdicts(html, rule));

    [Fact]
    public void Names_rules_suggest_no_edits()
    {
        // A name, label, alt text or title is content: no rule of the area has exactly one judgement-free fix.
        foreach (IAccessibilityRule r in BuiltInRules.All.Where(r => r.Descriptor.Tags.Contains("cat.text-alternatives") || r.Descriptor.Tags.Contains("cat.name-role-value") || r.Descriptor.Tags.Contains("cat.forms")))
        {
            if (r.Descriptor.Id.StartsWith("aria-", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.Null(r.Descriptor.Fix);
            Assert.NotNull(r.Descriptor.NoFixReason);
        }
    }

    [Fact]
    public void A_list_may_own_its_items_through_a_group()
    {
        const string ok = "<div role=list><div role=group><div role=listitem>a</div></div><div role=listitem>b</div></div>";
        Assert.Equal("passed", Verdicts(ok, "aria-required-children"));
        Assert.Equal("failed", Verdicts("<div role=list><div role=group>x</div></div>", "aria-required-children"));
    }

    [Fact]
    public void Presentational_conflict_is_reported_for_a_focusable_empty_alt_image()
    {
        Assert.Equal("failed", Verdicts("<img src=a alt='' tabindex=0>", "presentation-role-conflict"));
        Assert.Equal("failed", Verdicts("<img src=a alt='' aria-describedby=x><p id=x>d</p>", "presentation-role-conflict"));
        Assert.Equal("passed", Verdicts("<img src=a alt=''>", "presentation-role-conflict"));
    }

    // ---- rendered facts ----

    private static FakeLayout Box(double w = 100, string? text = null) =>
        new() { X = 0, Y = 0, W = w, H = 18, Text = text, Styles = ["block", "rgb(0, 0, 0)", "visible"] };

    private static RenderedFacts Facts(Document doc, Func<Node, bool> textLaidOut)
    {
        FakeNode root = FakeSnapshot.FromDocument(doc, n =>
        {
            if (n.Kind == NodeKind.Element)
            {
                return Box();
            }

            if (n.Kind == NodeKind.Text && n.Data.Trim().Length > 0 && textLaidOut(n))
            {
                FakeLayout l = Box(60, n.Data.ToString());
                l.Boxes.Add((0, n.Data.Length, 0, 0, 60, 18));
                return l;
            }

            return null;
        });
        DomSnapshotMapResult r = DomSnapshotAdapter.Map(DomSnapshot.Parse(FakeSnapshot.ToJson(root), FakeSnapshot.StyleNames), doc);
        Assert.True(r.Succeeded, r.Refusal?.ToString());
        return r.Facts!;
    }

    private const string Offscreen = "<button style='position:absolute;left:-9999px' aria-label='Close'>Open</button>";

    [Fact]
    public void Label_in_name_asks_for_layout_when_style_can_move_the_text()
    {
        CheckReport report = Run(Offscreen, "label-content-name-mismatch");
        RuleResult r = Assert.Single(report.Results);
        Assert.Equal(Outcome.NeedsRender, r.Outcome);
        Assert.Equal(LabelFacts, r.FactsMissing);
        Assert.Empty(r.FactsUsed);
    }

    private static readonly string[] LabelFacts = ["layout text of the visible label", "text boxes of the visible label"];

    [Fact]
    public void Label_in_name_reads_the_laid_out_text_when_facts_are_given()
    {
        Document doc = Document.Parse(Offscreen);
        RenderedFacts facts = Facts(doc, _ => true);
        RuleResult shown = Assert.Single(Run(doc, "label-content-name-mismatch", facts).Results);
        Assert.Equal(Outcome.Failed, shown.Outcome);
        Assert.Equal(["layout text", "text boxes"], shown.FactsUsed);
        Assert.Empty(shown.FactsMissing);

        // Text that was not laid out is not visible, so there is no visible label to compare.
        Document doc2 = Document.Parse(Offscreen);
        RenderedFacts none = Facts(doc2, _ => false);
        Assert.Equal(Outcome.Inapplicable, Assert.Single(Run(doc2, "label-content-name-mismatch", none).Results).Outcome);
    }

    [Fact]
    public void Facts_of_another_document_are_refused()
    {
        RenderedFacts facts = Facts(Document.Parse(Offscreen), _ => true);
        Assert.Throws<ArgumentException>(() => Run(Offscreen, "label-content-name-mismatch", facts));
    }

    [Fact]
    public void Label_in_name_decides_without_facts_when_nothing_can_move_the_text()
    {
        Assert.Equal("passed", Verdicts("<a href=x aria-label='Read the news'>News</a>", "label-content-name-mismatch"));
        Assert.Equal("failed", Verdicts("<a href=x aria-label='Home'>News</a>", "label-content-name-mismatch"));
        Assert.Equal("passed", Verdicts("<button aria-label='close'>X</button>", "label-content-name-mismatch"));
    }

    // ---- limits ----

    [Fact]
    public void Many_links_with_one_name_and_deep_nesting_stay_fast()
    {
        var sb = new System.Text.StringBuilder("<title>t</title>");
        for (int i = 0; i < 20_000; i++)
        {
            sb.Append("<a href=/p").Append(i).Append(">Read more</a><label><input></label>");
        }

        for (int i = 0; i < 300; i++)
        {
            sb.Append("<div role=button aria-label=x><label>");
        }

        sb.Append("deep <input>");
        Document doc = Document.Parse(sb.ToString());
        AccessibilityTree tree = AccessibilityTree.Build(doc, DocumentStyles.Create(doc));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        CheckReport report = new Checker().Check(tree, new CheckOptions
        {
            RuleIds = ["identical-links-same-purpose", "form-field-multiple-labels", "label-content-name-mismatch", "image-redundant-alt", "frame-title-unique", "label"],
        });
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), clock.Elapsed.ToString());
        Assert.Equal(20_000, report.Results.Count(r => r.RuleId == "identical-links-same-purpose" && r.Outcome == Outcome.NeedsReview));
    }
}
