using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Accessibility.Tests;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>Contrast, link-in-text-block, text spacing, orientation, target size and media rules on small pages.</summary>
public sealed class VisualRuleTests
{
    private static CheckReport Run(string html, string rule, RenderedFacts? facts = null, Document? doc = null)
    {
        doc ??= Document.Parse(html);
        return new Checker().Check(AccessibilityTree.Build(doc, DocumentStyles.Create(doc)), new CheckOptions { RuleIds = [rule], Facts = facts });
    }

    private static string Verdicts(string html, string rule) =>
        string.Join(",", Run(html, rule).Results.Where(r => r.Outcome != Outcome.Inapplicable).Select(r => r.Outcome.ToString().ToLowerInvariant()));

    // ---- color-contrast ----

    [Theory]
    // Text over an image that is not in its ancestor chain: the cascade alone would take the page background for it.
    [InlineData("<div style='position:relative'><img src='a.png' style='position:absolute;top:0;left:0;width:300px;height:100px'><p style='color:#eee'>text</p></div>")]
    [InlineData("<img src='a.png' style='position:fixed;top:0;left:0;width:300px;height:100px'><p style='color:#eee'>text</p>")]
    [InlineData("<div><p style='color:#eee'>text</p><div style='position:absolute;inset:0;background:url(a.png)'></div></div>")]
    public void Text_over_an_image_the_cascade_cannot_see_is_never_judged_against_the_page_colour(string html)
    {
        Assert.Equal("needsrender", Verdicts(html, "color-contrast"));
    }

    [Fact]
    public void An_image_that_cannot_reach_the_text_leaves_the_judgement_to_the_cascade()
    {
        // The image is in another positioned block: its containing block does not hold the text.
        Assert.Equal("failed", Verdicts("<div style='position:relative'><img src='a.png' style='position:absolute;top:0'></div><p style='color:#eee'>text</p>", "color-contrast"));
        Assert.Equal("failed", Verdicts("<p style='color:#eee'>text</p><img src='a.png'>", "color-contrast"));
    }

    [Fact]
    public void Ratio_is_not_rounded_up_to_meet_the_threshold()
    {
        // #777 on white is 4.47:1, #767676 is 4.54:1 (the usual boundary pair).
        Assert.Equal("failed", Verdicts("<p style='color:#777;background:#fff'>text</p>", "color-contrast"));
        Assert.Equal("passed", Verdicts("<p style='color:#767676;background:#fff'>text</p>", "color-contrast"));
        RuleResult r = Run("<p style='color:#777;background:#fff'>text</p>", "color-contrast").WithOutcome(Outcome.Failed).Single();
        Assert.Contains("4.47:1", r.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Large_text_needs_three_to_one_and_the_boundaries_follow_points()
    {
        const string c = "color:#777;background:#fff;";
        Assert.Equal("failed", Verdicts($"<p style='{c}font-size:23.9px'>text</p>", "color-contrast"));
        Assert.Equal("passed", Verdicts($"<p style='{c}font-size:24px'>text</p>", "color-contrast"));
        // 14pt is 18.67px and must be bold.
        Assert.Equal("passed", Verdicts($"<p style='{c}font-size:14pt;font-weight:700'>text</p>", "color-contrast"));
        Assert.Equal("failed", Verdicts($"<p style='{c}font-size:14pt;font-weight:600'>text</p>", "color-contrast"));
        Assert.Equal("failed", Verdicts($"<p style='{c}font-size:18px;font-weight:700'>text</p>", "color-contrast"));
        // The enhanced rule asks 4.5:1 of large text and 7:1 of the rest.
        Assert.Equal("failed", Verdicts($"<p style='color:#767676;background:#fff;font-size:16px'>text</p>", "color-contrast-enhanced"));
        Assert.Equal("passed", Verdicts($"<p style='color:#767676;background:#fff;font-size:24px'>text</p>", "color-contrast-enhanced"));
    }

    [Fact]
    public void Opacity_chains_multiply_and_weaken_the_text_and_the_backgrounds_under_them()
    {
        Assert.Equal("failed", Verdicts("<div style='background:#fff'><p style='color:#000;opacity:.3'>text</p></div>", "color-contrast"));
        // 0.8 * 0.8 = 0.64 of black over white is #5c5c5c, 6.9:1.
        Assert.Equal("passed", Verdicts("<div style='background:#fff;opacity:1'><div style='opacity:.8'><p style='color:#000;opacity:.8'>text</p></div></div>", "color-contrast"));
        // 0.5 * 0.5 = 0.25: #bfbfbf on white.
        Assert.Equal("failed", Verdicts("<div style='opacity:.5'><p style='color:#000;opacity:.5;background:#fff'>text</p></div>", "color-contrast"));
        // A background that is itself half transparent lets the page show through: white text on #000 at .5 over white is on #808080.
        Assert.Equal("failed", Verdicts("<p style='color:#fff;background:rgba(0,0,0,.5)'>text</p>", "color-contrast"));
        // Fully transparent text is not drawn.
        Assert.Equal(string.Empty, Verdicts("<p style='color:rgba(0,0,0,0)'>text</p>", "color-contrast"));
    }

    [Fact]
    public void Nested_and_transparent_backgrounds_resolve_through_the_chain()
    {
        const string page = "<body style='margin:0'><div style='background:#000'><div><section style='background:transparent'><p style='color:#fff'>{0}</p></section></div></div></body>";
        Assert.Equal("passed", Verdicts(string.Format(System.Globalization.CultureInfo.InvariantCulture, page, "light on dark"), "color-contrast"));
        // An opaque inner background replaces the dark one.
        Assert.Equal("failed", Verdicts("<div style='background:#000'><div style='background:#fff'><p style='color:#eee'>text</p></div></div>", "color-contrast"));
        // A translucent layer over a dark one: white at .1 over black is #1a1a1a, so grey text on it reads.
        Assert.Equal("passed", Verdicts("<div style='background:#000'><div style='background:rgba(255,255,255,.1)'><p style='color:#fff'>text</p></div></div>", "color-contrast"));
    }

    [Fact]
    public void Gradients_use_the_most_contrasting_stop_and_a_dark_to_light_gradient_can_fail_or_pass()
    {
        Assert.Equal("passed", Verdicts("<p style='color:#333;background:linear-gradient(to right,#fff,#00f);width:500px'>text</p>", "color-contrast"));
        Assert.Equal("failed", Verdicts("<p style='color:#aaa;background:linear-gradient(to right,#fff,#00f);width:300px'>text</p>", "color-contrast"));
        // Hard stops, transparent stops and positions are read.
        Assert.Equal("passed", Verdicts("<p style='color:#000;background:linear-gradient(90deg,#fff 0%,#fff 50%,#ccc 50%)'>text</p>", "color-contrast"));
    }

    [Fact]
    public void Text_shadows_count_among_the_colours_behind_the_text()
    {
        Assert.Equal("passed", Verdicts("<p style='color:#000;background:#737373;text-shadow:white 0 0 3px'>text</p>", "color-contrast"));
        Assert.Equal("failed", Verdicts("<p style='color:#000;background:#737373'>text</p>", "color-contrast"));
    }

    [Fact]
    public void Disabled_controls_their_labels_and_disabled_groups_are_incidental()
    {
        Assert.Equal(string.Empty, Verdicts("<button disabled style='color:#ccc;background:#fff'>Save</button>", "color-contrast"));
        Assert.Equal(string.Empty, Verdicts("<label style='color:#ccc;background:#fff'>Name <input disabled></label>", "color-contrast"));
        Assert.Equal(string.Empty, Verdicts("<label for=a style='color:#ccc;background:#fff'>Name</label><input id=a disabled>", "color-contrast"));
        Assert.Equal(string.Empty, Verdicts("<fieldset disabled style='color:#ccc;background:#fff'><legend>Group</legend><label>A <input></label></fieldset>", "color-contrast"));
        Assert.Equal(string.Empty, Verdicts("<div role=group aria-disabled=true style='color:#ccc;background:#fff'><label>A <input></label></div>", "color-contrast"));
        Assert.Equal("failed", Verdicts("<button style='color:#ccc;background:#fff'>Save</button>", "color-contrast"));
    }

    [Fact]
    public void Text_that_is_hidden_or_moved_off_the_page_is_not_judged()
    {
        Assert.Equal(string.Empty, Verdicts("<p style='display:none;color:#ccc'>x</p>", "color-contrast"));
        Assert.Equal(string.Empty, Verdicts("<p style='visibility:hidden;color:#ccc'>x</p>", "color-contrast"));
        Assert.Equal(string.Empty, Verdicts("<p style='position:absolute;left:-9999px;color:#ccc'>x</p>", "color-contrast"));
        Assert.Equal(string.Empty, Verdicts("<p style='text-indent:-9999px;color:#ccc'>x</p>", "color-contrast"));
        Assert.Equal(string.Empty, Verdicts("<p style='color:#fff;background:#fff'>same colour</p>", "color-contrast"));
        Assert.Equal("passed", Verdicts("<p style='color:#000;background:#666'>=== ---</p>", "color-contrast"));
    }

    [Fact]
    public void Pseudo_element_text_is_checked_against_the_host_background()
    {
        const string page = "<style>p::before{content:\"Note: \";color:#ccc}</style><p style='color:#000;background:#fff'>Body</p>";
        CheckReport r = Run(page, "color-contrast");
        Assert.Equal(["passed", "failed"], r.Results.Select(x => x.Outcome.ToString().ToLowerInvariant()).OrderByDescending(x => x == "passed").ToArray());
        RuleResult failed = r.WithOutcome(Outcome.Failed).Single();
        Assert.Contains(failed.Findings, f => f.Message.Contains("::before", StringComparison.Ordinal));
        Assert.Equal(string.Empty, Verdicts("<style>p::before{content:\"\";color:#ccc}</style><p style='color:#000'>Body</p>", "color-contrast").Replace("passed", string.Empty));
    }

    [Fact]
    public void An_image_background_needs_samples_and_a_positioned_element_needs_the_browsers_blend()
    {
        CheckReport image = Run("<p style=\"color:#000;background:url(a.png)\">text</p>", "color-contrast");
        RuleResult nr = image.WithOutcome(Outcome.NeedsRender).Single();
        Assert.Equal(TextSupportMissing, nr.FactsMissing);
        Assert.Empty(nr.FactsUsed);
        CheckReport positioned = Run("<div style='position:absolute;top:0'><p style='color:#fff'>text</p></div>", "color-contrast");
        Assert.Equal(["blended background colors of the element", "paint order and stacking of the element"], positioned.WithOutcome(Outcome.NeedsRender).Single().FactsMissing);
        // An opaque background of its own makes the position irrelevant.
        Assert.Equal("passed", Verdicts("<div style='position:absolute;top:0;background:#000'><p style='color:#fff'>text</p></div>", "color-contrast"));
    }

    private static readonly string[] TextSupportMissing = ["background samples (median and 5th/95th percentile colours) behind the text"];

    // ---- facts: Chrome captures of the fixture pages ----

    private static (Document Doc, RenderedFacts Facts)? Capture(string page)
    {
        string dir = Path.Combine(AriaActTests.Repo, "testdata", "rendered", "captures", page);
        if (!Directory.Exists(dir))
        {
            return null;
        }

        static byte[] Read(string d, string name)
        {
            using var f = File.OpenRead(Path.Combine(d, name + ".json.gz"));
            using var z = new GZipStream(f, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            z.CopyTo(ms);
            return ms.ToArray();
        }

        using JsonDocument dom = JsonDocument.Parse(Read(dir, "dom"));
        string doctype = dom.RootElement.TryGetProperty("doctype", out JsonElement dt) && dt.ValueKind == JsonValueKind.String ? dt.GetString()! : string.Empty;
        Document doc = Document.Parse(doctype + dom.RootElement.GetProperty("outerHTML").GetString());
        DomSnapshotMapResult map = DomSnapshotAdapter.Map(DomSnapshot.Parse(Read(dir, "snapshot")), doc);
        Assert.True(map.Succeeded, map.Refusal?.ToString());
        return (doc, map.Facts!);
    }

    [Fact]
    public void Chrome_capture_of_text_over_backgrounds_decides_with_facts_and_asks_where_it_cannot()
    {
        if (Capture("background") is not { } cap)
        {
            Assert.Skip("no capture of background.html");
            return;
        }

        (Document doc, RenderedFacts facts) = cap;
        CheckReport r = Run(string.Empty, "color-contrast", facts, doc);
        RuleResult Of(string id) => r.Results.SingleOrDefault(x => !x.Target.IsNull && x.Target.GetAttribute("id") == id)
            ?? throw new InvalidOperationException(id + " in " + string.Join(";", r.Results.Select(x => x.Outcome + ":" + x.Message)));
        Assert.Equal(Outcome.Passed, Of("solid").Outcome);
        // White text on a gradient from black to white: the black end has the best contrast.
        Assert.Equal(Outcome.Passed, Of("gradient").Outcome);
        // Half-transparent black over white leaves #808080 behind white text: 3.95:1.
        Assert.Equal(Outcome.Failed, Of("translucent").Outcome);
        // The image's pixels are not in the snapshot.
        Assert.Equal(Outcome.NeedsRender, Of("image").Outcome);
        Assert.Equal(TextSupportMissing, Of("image").FactsMissing);
        // A positioned pseudo-element behind the text paints #900, which neither the cascade chain nor the browser's blended colour (white) includes.
        Assert.Equal(Outcome.NeedsRender, Of("behind").Outcome);
        Assert.Empty(Of("behind").FactsUsed);
        // A translucent white panel positioned over the blue block: the browser's blended colour is what the text sits on.
        RuleResult overlay = Of("overlay");
        Assert.Equal(Outcome.Passed, overlay.Outcome);
        Assert.Contains("blended background color", overlay.FactsUsed);
    }

    [Fact]
    public void Background_samples_decide_text_over_an_image_with_the_median_and_the_extremes()
    {
        if (Capture("background") is not { } cap)
        {
            Assert.Skip("no capture of background.html");
            return;
        }

        (Document doc, RenderedFacts facts) = cap;
        Node image = doc.Root.QuerySelector("#image");
        long backend = facts[image].BackendNodeId;

        CheckReport With(string median, string p5, string p95)
        {
            string json = "{\"format\":\"onehtml-background-samples\",\"version\":1,\"samples\":[{\"backendNodeId\":" + backend + ",\"median\":\"" + median + "\",\"p5\":\"" + p5 + "\",\"p95\":\"" + p95 + "\",\"pixels\":4800}]}";
            BackgroundSampleApplication applied = facts.WithBackgroundSamples(BackgroundSamples.Parse(System.Text.Encoding.UTF8.GetBytes(json)));
            Assert.Equal(1, applied.Applied);
            return Run(string.Empty, "color-contrast", applied.Facts, doc);
        }

        RuleResult dark = With("#202020", "#101010", "#303030").Results.Single(x => x.Target.GetAttribute("id") == "image");
        Assert.Equal(Outcome.Passed, dark.Outcome);
        Assert.Contains("background samples", dark.FactsUsed);
        Assert.Equal(Outcome.Failed, With("#c0c0c0", "#b0b0b0", "#d0d0d0").Results.Single(x => x.Target.GetAttribute("id") == "image").Outcome);
        // Enough on average, not at the bright end: a person decides.
        Assert.Equal(Outcome.NeedsReview, With("#202020", "#101010", "#e0e0e0").Results.Single(x => x.Target.GetAttribute("id") == "image").Outcome);
    }

    [Fact]
    public void Pseudo_elements_without_a_box_in_the_capture_are_not_judged()
    {
        if (Capture("pseudo") is not { } cap)
        {
            Assert.Skip("no capture of pseudo.html");
            return;
        }

        (Document doc, RenderedFacts facts) = cap;
        CheckReport r = Run(string.Empty, "color-contrast", facts, doc);
        // The hidden host and its generated text never reach the rules.
        Assert.DoesNotContain(r.Results, x => x.Target.GetAttribute("id") == "hidden-pseudo");
        Assert.All(r.Results, x => Assert.NotEqual(Outcome.NeedsRender, x.Outcome));
    }

    [Fact]
    public void Text_not_laid_out_in_the_capture_is_not_judged()
    {
        if (Capture("positioned") is not { } cap)
        {
            Assert.Skip("no capture of positioned.html");
            return;
        }

        (Document doc, RenderedFacts facts) = cap;
        CheckReport r = Run(string.Empty, "color-contrast", facts, doc);
        Assert.Contains(r.Results, x => x.Outcome != Outcome.Inapplicable);
        // Boxes with their own opaque backgrounds are decided from the cascade even though they are positioned.
        Assert.Equal(Outcome.Passed, r.Results.Single(x => !x.Target.IsNull && x.Target.GetAttribute("id") == "a").Outcome);
    }

    // ---- link-in-text-block ----

    [Fact]
    public void Links_in_text_need_a_cue_or_three_to_one_against_the_text()
    {
        const string text = "<p style='color:#333;background:#fff'>Read <a href=x style='{0}'>the report</a> today.</p>";
        string P(string css) => string.Format(System.Globalization.CultureInfo.InvariantCulture, text, css);
        Assert.Equal("passed", Verdicts(P("text-decoration:underline"), "link-in-text-block"));
        Assert.Equal("passed", Verdicts(P("text-decoration:none;font-weight:700"), "link-in-text-block"));
        Assert.Equal("passed", Verdicts(P("text-decoration:none;color:#0000ee;border-bottom:1px solid"), "link-in-text-block"));
        // #333 and #0000ee are 1.2:1 apart in luminance terms: colour alone is not enough.
        Assert.Equal("failed", Verdicts(P("text-decoration:none;color:#0000ee"), "link-in-text-block"));
        // Black text and a light grey link are over 3:1 apart.
        Assert.Equal("passed", Verdicts("<p style='color:#000;background:#fff'>Read <a href=x style='text-decoration:none;color:#757575'>the report</a> today.</p>", "link-in-text-block"));
        // A link on its own, in a navigation list, is not in a block of text.
        Assert.Equal(string.Empty, Verdicts("<ul><li><a href=x style='text-decoration:none;color:#333'>Home</a></li></ul>", "link-in-text-block"));
    }

    [Fact]
    public void Push_buttons_made_with_input_are_judged_on_their_value_and_native_face()
    {
        // The label of <input type=submit> is its value; the button face is the browser's light grey even when the author adds a border.
        Assert.Equal("failed", Verdicts("<input type=submit value=Go style='color:#aaa;background:#fff'>", "color-contrast"));
        Assert.Equal("passed", Verdicts("<input type=submit value=Go>", "color-contrast"));
        Assert.Equal("failed", Verdicts("<button style='color:#fff;border:2px solid red'>Go</button>", "color-contrast"));
        Assert.Equal("passed", Verdicts("<button style='border:2px solid red'>Go</button>", "color-contrast"));
        // A disabled control is incidental.
        Assert.Equal(string.Empty, Verdicts("<input type=submit value=Go disabled style='color:#aaa;background:#fff'>", "color-contrast"));
    }

    [Fact]
    public void A_link_with_a_background_image_of_its_own_is_set_apart()
    {
        const string text = "<p style='color:#333;background:#fff'>Read <a href=x style='text-decoration:none;color:#333;{0}'>the report</a> today.</p>";
        Assert.Equal("failed", Verdicts(string.Format(System.Globalization.CultureInfo.InvariantCulture, text, ""), "link-in-text-block"));
        Assert.Equal("passed", Verdicts(string.Format(System.Globalization.CultureInfo.InvariantCulture, text, "background-image:linear-gradient(#333,#333)"), "link-in-text-block"));
    }

    [Fact]
    public void List_items_inside_aria_hidden_content_are_not_judged()
    {
        Assert.Equal("failed", Verdicts("<div><li>a</li></div>", "listitem"));
        Assert.Equal(string.Empty, Verdicts("<div aria-hidden=true><li>a</li></div>", "listitem"));
    }

    [Fact]
    public void Target_size_ignores_covered_targets_counts_hidden_neighbours_and_places_overflow_after_the_spacing_test()
    {
        const string html = "<main><a id=a href=a>a</a><a id=b href=b>b</a><a id=c href=# aria-hidden=true>c</a><a id=d href=#>d</a><a id=e href=# >e</a></main>";
        Document doc = Document.Parse(html);
        var boxes = new Dictionary<string, (double X, double Y, double W, double H)>
        {
            ["a"] = (0, 0, 16, 16), ["b"] = (20, 0, 16, 16), ["c"] = (400, 0, 16, 16), ["d"] = (410, 0, 16, 16), ["e"] = (700, 0, 16, 16),
        };
        FakeNode root = FakeSnapshot.FromDocument(doc, n =>
        {
            if (n.Kind == NodeKind.Element)
            {
                string id = n.GetAttribute("id") ?? string.Empty;
                (double x, double y, double w, double h) = boxes.GetValueOrDefault(id, (0, 0, 800, 800));
                return new FakeLayout { X = x, Y = y, W = w, H = h, Styles = ["inline", "rgb(0, 0, 0)", "visible"] };
            }

            return null;
        });
        DomSnapshotMapResult map = DomSnapshotAdapter.Map(DomSnapshot.Parse(FakeSnapshot.ToJson(root), FakeSnapshot.StyleNames), doc);
        Assert.True(map.Succeeded, map.Refusal?.ToString());
        CheckReport r = Run(html, "target-size", map.Facts, doc);
        Outcome? Of(string id) => r.Results.SingleOrDefault(x => !x.Target.IsNull && x.Target.GetAttribute("id") == id)?.Outcome;
        // a and b are 4 pixels apart: too close.
        Assert.Equal(Outcome.Failed, Of("a"));
        Assert.Equal(Outcome.Failed, Of("b"));
        // d has a hidden-from-assistive-technology neighbour two pixels away that still takes up room; the hidden control itself gets no verdict.
        Assert.Null(Of("c"));
        Assert.Equal(Outcome.Failed, Of("d"));
        // Two links to "#" are two controls, not one target.
        Assert.Equal(Outcome.Passed, Of("e"));
    }

    // ---- avoid-inline-spacing ----

    [Fact]
    public void Important_inline_spacing_is_judged_against_the_font_size_and_offers_one_edit_when_it_can()
    {
        const string failing = "<p style=\"letter-spacing: 0.1em !important; line-height: 1.2 !important\">x</p>";
        Document doc = Document.Parse(failing);
        CheckReport r = new Checker().Check(doc, new CheckOptions { RuleIds = ["avoid-inline-spacing"] });
        // Letter spacing fails; line height cannot be judged without the text boxes, so it stays out of the verdict.
        RuleResult f = r.WithOutcome(Outcome.Failed).Single();
        Assert.Single(f.Findings);
        Assert.Single(f.SuggestedEdits);
        EditResult applied = doc.Apply(r.SuggestedEditList());
        Assert.True(applied.Succeeded, applied.Failure);
        Assert.Equal("<p style=\"letter-spacing: 0.1em; line-height: 1.2 !important\">x</p>", applied.Document!.Source.Text);
        Assert.Empty(new Checker().Check(applied.Document, new CheckOptions { RuleIds = ["avoid-inline-spacing"] }).WithOutcome(Outcome.Failed));

        // Declared twice: dropping !important from the last would let the first one win, so no edit is offered.
        CheckReport twice = new Checker().Check(Document.Parse("<p style=\"word-spacing:0.1em !important; word-spacing:0.05em !important\">x</p>"), new CheckOptions { RuleIds = ["avoid-inline-spacing"] });
        Assert.Empty(twice.WithOutcome(Outcome.Failed).Single().SuggestedEdits);
        Assert.Equal("passed", Verdicts("<p style=\"line-height:1.5 !important\">x</p>", "avoid-inline-spacing").Replace("needsrender", "passed"));
    }

    [Fact]
    public void Line_height_applies_only_to_text_that_wraps_and_facts_say_which()
    {
        const string html = "<p id=a style=\"line-height:1 !important\">one two three</p><p id=b style=\"line-height:1 !important\">one</p>";
        Document doc = Document.Parse(html);
        FakeNode root = FakeSnapshot.FromDocument(doc, n =>
        {
            if (n.Kind == NodeKind.Element)
            {
                return new FakeLayout { W = 100, H = 18, Styles = ["block", "rgb(0, 0, 0)", "visible"] };
            }

            if (n.Kind == NodeKind.Text && n.Data.Trim().Length > 0)
            {
                var l = new FakeLayout { W = 60, H = 18, Text = n.Data.ToString(), Styles = ["block", "rgb(0, 0, 0)", "visible"] };
                l.Boxes.Add((0, 3, 0, 0, 30, 18));
                if (n.Data.Length > 5)
                {
                    l.Boxes.Add((4, n.Data.Length - 4, 0, 20, 30, 18));
                }

                return l;
            }

            return null;
        });
        DomSnapshotMapResult map = DomSnapshotAdapter.Map(DomSnapshot.Parse(FakeSnapshot.ToJson(root), FakeSnapshot.StyleNames), doc);
        Assert.True(map.Succeeded, map.Refusal?.ToString());
        CheckReport r = Run(html, "avoid-inline-spacing", map.Facts, doc);
        Assert.Equal(Outcome.Failed, r.Results.Single(x => !x.Target.IsNull && x.Target.GetAttribute("id") == "a").Outcome);
        Assert.Equal(Outcome.Passed, r.Results.Single(x => !x.Target.IsNull && x.Target.GetAttribute("id") == "b").Outcome);
        Assert.Contains("text boxes", r.Results.Single(x => !x.Target.IsNull && x.Target.GetAttribute("id") == "a").FactsUsed);
        CheckReport without = Run(html, "avoid-inline-spacing");
        Assert.All(without.Results, x => Assert.Equal(Outcome.NeedsRender, x.Outcome));
        Assert.All(without.Results, x => Assert.Equal(AvoidSpacingMissing, x.FactsMissing));
    }

    private static readonly string[] AvoidSpacingMissing = ["text boxes of the element (to see whether its text wraps onto more than one line)"];

    [Fact]
    public void Every_suggested_spacing_edit_applies_losslessly_and_clears_its_finding()
    {
        var cases = Catalogue.ActTestCases.Parse(File.ReadAllText(Path.Combine(AriaActTests.Repo, "testdata", "act", "testcases.json")));
        int suggestions = 0;
        foreach (Catalogue.ActTestCase c in cases.Where(c => c.RuleId is "24afc2" or "9e45ec" or "78fd32"))
        {
            Document doc = Document.Parse(File.ReadAllText(Path.Combine(AriaActTests.Repo, "testdata", "act", c.RelativePath)));
            CheckReport before = new Checker().Check(doc, new CheckOptions { RuleIds = ["avoid-inline-spacing"] });
            IReadOnlyList<Edit> edits = before.SuggestedEditList();
            if (edits.Count == 0)
            {
                continue;
            }

            suggestions += edits.Count;
            EditResult applied = doc.Apply(edits);
            Assert.True(applied.Succeeded, applied.Failure);
            ReplayResult replayed = before.SuggestedEdits().Replay(doc);
            Assert.True(replayed.Succeeded, replayed.Failure);
            Assert.Equal(applied.Document!.Source.Text, replayed.Batch!.Document!.Source.Text);
            CheckReport after = new Checker().Check(applied.Document, new CheckOptions { RuleIds = ["avoid-inline-spacing"] });
            Assert.Empty(after.WithOutcome(Outcome.Failed));
            Assert.Empty(after.SuggestedEditList());
        }

        Assert.True(suggestions >= 6, "the failed ACT cases carry suggestions: " + suggestions);
    }

    // ---- css-orientation-lock ----

    [Fact]
    public void Orientation_lock_compares_the_rotation_in_both_orientations()
    {
        Assert.Equal("failed", Verdicts("<style>@media (orientation: portrait){body{transform:rotate(90deg)}}</style><body>x</body>", "css-orientation-lock"));
        Assert.Equal("failed", Verdicts("<style>@media (orientation: landscape){body{rotate:270deg}}</style><body>x</body>", "css-orientation-lock"));
        Assert.Equal("failed", Verdicts("<style>@media (orientation: portrait){body{transform:rotate3d(0,0,1,.25turn)}}</style><body>x</body>", "css-orientation-lock"));
        // A small tilt, a full turn, and a move are not a lock.
        Assert.Equal("passed", Verdicts("<style>@media (orientation: portrait){body{transform:rotate(10deg)}}</style><body>x</body>", "css-orientation-lock"));
        Assert.Equal("passed", Verdicts("<style>@media (orientation: portrait){body{transform:rotate(360deg)}}</style><body>x</body>", "css-orientation-lock"));
        Assert.Equal(string.Empty, Verdicts("<style>@media (orientation: portrait){body{transform:translateX(9px)}}</style><body>x</body>", "css-orientation-lock"));
        // The same rotation in both orientations turns nothing.
        Assert.Equal(string.Empty, Verdicts("<style>body{transform:rotate(90deg)}</style><body>x</body>", "css-orientation-lock"));
    }

    // ---- target-size ----

    [Fact]
    public void Target_size_reads_boxes_applies_the_spacing_exception_and_asks_for_facts_without_them()
    {
        const string html = "<main><a id=big href=a>big</a> <a id=small1 href=b>s1</a> <a id=small2 href=c>s2</a> <a id=lone href=d>lone</a> <input id=cb type=checkbox></main>";
        Document doc = Document.Parse(html);
        var boxes = new Dictionary<string, (double X, double Y, double W, double H)>
        {
            ["big"] = (0, 0, 40, 30), ["small1"] = (100, 0, 16, 16), ["small2"] = (110, 0, 16, 16), ["lone"] = (400, 400, 16, 16), ["cb"] = (700, 700, 13, 13),
        };
        FakeNode root = FakeSnapshot.FromDocument(doc, n =>
        {
            if (n.Kind == NodeKind.Element)
            {
                string id = n.GetAttribute("id") ?? string.Empty;
                (double x, double y, double w, double h) = boxes.GetValueOrDefault(id, (0, 0, 800, 800));
                return new FakeLayout { X = x, Y = y, W = w, H = h, Styles = ["inline", "rgb(0, 0, 0)", "visible"] };
            }

            return null;
        });
        DomSnapshotMapResult map = DomSnapshotAdapter.Map(DomSnapshot.Parse(FakeSnapshot.ToJson(root), FakeSnapshot.StyleNames), doc);
        Assert.True(map.Succeeded, map.Refusal?.ToString());
        CheckReport r = Run(html, "target-size", map.Facts, doc);
        Outcome Of(string id) => r.Results.Single(x => !x.Target.IsNull && x.Target.GetAttribute("id") == id).Outcome;
        Assert.Equal(Outcome.Passed, Of("big"));
        Assert.Equal(Outcome.Failed, Of("small1"));
        Assert.Equal(Outcome.Failed, Of("small2"));
        Assert.Equal(Outcome.Passed, Of("lone"));
        // An unstyled checkbox is sized by the browser.
        Assert.Equal(Outcome.Passed, Of("cb"));
        Assert.Contains("border boxes of the targets", r.Results.Single(x => !x.Target.IsNull && x.Target.GetAttribute("id") == "small1").FactsUsed);

        CheckReport without = Run(html, "target-size");
        Assert.Equal(5, without.WithOutcome(Outcome.NeedsRender).Count());
        Assert.All(without.WithOutcome(Outcome.NeedsRender), x => Assert.Equal(["border box of the target", "border boxes of the targets near it"], x.FactsMissing));
    }

    // ---- media ----

    [Fact]
    public void Media_that_starts_by_itself_passes_with_controls_and_otherwise_asks()
    {
        Assert.Equal("passed", Verdicts("<audio src=a.mp3 autoplay controls></audio>", "no-autoplay-audio"));
        Assert.Equal("needsreview", Verdicts("<audio src=a.mp3 autoplay></audio>", "no-autoplay-audio"));
        Assert.Equal(string.Empty, Verdicts("<audio src=a.mp3 autoplay muted></audio>", "no-autoplay-audio"));
        Assert.Equal(string.Empty, Verdicts("<audio src=a.mp3 controls></audio>", "no-autoplay-audio"));
        Assert.Equal(string.Empty, Verdicts("<video src=a.mp4 autoplay style='display:none'></video>", "no-autoplay-audio"));
    }

    [Fact]
    public void Captions_are_never_failed_because_markup_cannot_show_burned_in_captions()
    {
        CheckReport none = Run("<video src=a.mp4 controls></video>", "video-caption");
        RuleResult nr = none.Results.Single(x => x.Outcome == Outcome.NeedsReview);
        Assert.Contains("no captions track", nr.Findings.Single().Message, StringComparison.Ordinal);
        CheckReport with = Run("<video src=a.mp4 controls><track kind=captions src=a.vtt></video>", "video-caption");
        Assert.Contains("captions or subtitles track", with.Results.Single(x => x.Outcome == Outcome.NeedsReview).Findings.Single().Message, StringComparison.Ordinal);
        Assert.Equal("needsreview", Verdicts("<audio src=a.mp3 controls></audio>", "audio-caption"));
    }

    [Fact]
    public void A_large_page_is_checked_in_linear_time()
    {
        var sb = new System.Text.StringBuilder("<style>.a{color:#555;background:#eee}.b{background:linear-gradient(#fff,#ddd)}</style><body>");
        for (int i = 0; i < 4000; i++)
        {
            sb.Append("<div class=a><div class=b><p style='letter-spacing:.2em !important'>Paragraph ").Append(i).Append(" <a href=x>link</a> text</p></div></div>");
        }

        Document doc = Document.Parse(sb.ToString());
        AccessibilityTree tree = AccessibilityTree.Build(doc, DocumentStyles.Create(doc));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        CheckReport r = new Checker().Check(tree, new CheckOptions { RuleIds = ["color-contrast", "color-contrast-enhanced", "link-in-text-block", "avoid-inline-spacing"] });
        clock.Stop();
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "visual-timing.txt"), $"{doc.NodeCount} nodes, {r.Results.Count} results, {clock.ElapsedMilliseconds} ms");
        Assert.True(clock.ElapsedMilliseconds < 20_000, clock.ElapsedMilliseconds + " ms");
    }
}
