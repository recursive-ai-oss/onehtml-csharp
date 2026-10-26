using System;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Editing;

public class EditJsonTests
{
    private const string Page =
        "<!DOCTYPE html>\n<html><head><style>\n.a { color: red }\n.b { margin: 0 }\n</style></head>\n" +
        "<body><nav id=\"main-nav\"><a href=\"/x\">X</a><a href=\"/y\">Y</a></nav>\n<p class=\"intro\">Hello <b>there</b></p><!-- note --><ul><li>one<li>two</ul></body></html>\n";

    private static CssNode Rule(Stylesheet s, int n) => SheetEditTests.Rule(s, n);

    private static (Document Doc, EditEnvelope Env) Saved()
    {
        Document doc = Document.Parse(Encoding.UTF8.GetBytes(Page));
        InlineStylesheet inl = InlineStylesheets.Of(doc).Single();
        EditEnvelope env = EditJson.Create(
            doc,
            [
                Edit.SetAttribute(EditTestHelpers.Find(doc, "p"), "title", "Greeting"),
                Edit.ReplaceText(EditTestHelpers.FindText(doc, "one"), "uno"),
            ],
            [SheetEdit.SetDeclaration(inl.Sheet, Rule(inl.Sheet, 1), "padding", "1px", important: true)]);
        return (doc, env);
    }

    [Fact]
    public void Round_trip_is_stable_and_replays_like_a_direct_apply()
    {
        (Document doc, EditEnvelope env) = Saved();
        string json = env.ToJson();
        EditEnvelope back = EditJson.Parse(json);
        Assert.Equal(json, back.ToJson());
        Assert.Equal(env.Edits.Count, back.Edits.Count);

        ReplayResult r = back.Replay(Document.Parse(Encoding.UTF8.GetBytes(Page)));
        Assert.True(r.Succeeded, r.Failure);
        Assert.All(r.Outcomes, o => Assert.Equal(EditStatus.Applied, o.Status));

        InlineStylesheet inl = InlineStylesheets.Of(doc).Single();
        BatchResult direct = new EditBatch(
            doc,
            [Edit.SetAttribute(EditTestHelpers.Find(doc, "p"), "title", "Greeting"), Edit.ReplaceText(EditTestHelpers.FindText(doc, "one"), "uno")],
            [SheetEdit.SetDeclaration(inl.Sheet, Rule(inl.Sheet, 1), "padding", "1px", important: true)]).Apply();
        Assert.True(direct.Succeeded, direct.Failure);
        Assert.Equal(direct.Document!.Source.Text, r.Batch!.Document!.Source.Text);
        Assert.Contains("padding: 1px !important", r.Batch.Document!.Source.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Indented_output_parses_to_the_same_edits()
    {
        (_, EditEnvelope env) = Saved();
        Assert.Equal(env.ToJson(), EditJson.Parse(env.ToJson(indented: true)).ToJson());
    }

    [Fact]
    public void Unknown_members_are_ignored_and_meta_round_trips()
    {
        string json = """
            {"format":"onehtml-edits","version":1,"future":{"a":[1,2,{"b":null}]},
             "edits":[{"op":"setAttribute","unknown":[[[]]],"name":"title","value":"v",
               "target":{"locator":"#main-nav","other":1,"fingerprint":{"tag":"nav","attrs":{"id":"main-nav"},"text":"sha256:00","path":"sha256:11","x":2}},
               "meta":{"criterion":"4.1.2","n":[1,2.5,true,null],"nested":{"k":"v"}}}]}
            """;
        EditEnvelope env = EditJson.Parse(json);
        string again = env.ToJson();
        EditEnvelope env2 = EditJson.Parse(again);
        Assert.Equal(again, env2.ToJson());
        Assert.Equal("""{"criterion":"4.1.2","n":[1,2.5,true,null],"nested":{"k":"v"}}""", env2.Edits[0].MetaJson);
        Assert.DoesNotContain("future", again, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"format":"other","version":1,"edits":[]}""")]
    [InlineData("""{"format":"onehtml-edits","version":2,"edits":[]}""")]
    [InlineData("""{"format":"onehtml-edits","edits":[]}""")]
    [InlineData("""{"format":"onehtml-edits","version":1,"edits":{}}""")]
    [InlineData("""{"format":"onehtml-edits","version":1,"edits":[{"op":"setAttribute","target":3}]}""")]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{"format":"onehtml-edits","version":1,"edits":[]} trailing""")]
    [InlineData("""{"format":"onehtml-edits","version":1,"edits":[{"op":"setAttribute","value":"\ud800"}]}""")]
    public void Malformed_envelopes_fail_with_the_typed_exception(string json)
    {
        Assert.Throws<EditJsonException>(() => EditJson.Parse(json));
    }

    [Fact]
    public void Hostile_inputs_fail_fast_and_cleanly()
    {
        string prefix = """{"format":"onehtml-edits","version":1,"edits":[],"x":""";
        Assert.Throws<EditJsonException>(() => EditJson.Parse(prefix + new string('[', 10_000) + new string(']', 10_000) + "}"));
        Assert.Throws<EditJsonException>(() => EditJson.Parse(prefix + new string('[', 10_000)));
        Assert.Throws<EditJsonException>(() => EditJson.Parse(prefix + new string('{', 5_000)));

        var small = new EditJsonLimits { MaxStringBytes = 1000, MaxBytes = 100_000, MaxEdits = 50, MaxMetaBytes = 200 };
        string huge = "{\"format\":\"onehtml-edits\",\"version\":1,\"edits\":[{\"op\":\"setAttribute\",\"value\":\"" + new string('a', 5000) + "\"}]}";
        Assert.Throws<EditJsonException>(() => EditJson.Parse(huge, small));
        Assert.Throws<EditJsonException>(() => EditJson.Parse(new string(' ', 200_000) + "{}", small));

        var sb = new StringBuilder("""{"format":"onehtml-edits","version":1,"edits":[""");
        for (int i = 0; i < 60; i++)
        {
            sb.Append(i == 0 ? string.Empty : ",").Append("""{"op":"remove"}""");
        }

        sb.Append("]}");
        Assert.Throws<EditJsonException>(() => EditJson.Parse(sb.ToString(), small));

        // A declared edit count in the millions never allocates: the bytes limit stops it first.
        var many = new StringBuilder("""{"format":"onehtml-edits","version":1,"edits":[""");
        many.Append(string.Join(',', Enumerable.Repeat("{}", 2_000_000))).Append("]}");
        Assert.Throws<EditJsonException>(() => EditJson.Parse(many.ToString(), new EditJsonLimits { MaxBytes = 1 << 20 }));
        Assert.Throws<EditJsonException>(() => EditJson.Parse(many.ToString()));

        string bigMeta = "{\"format\":\"onehtml-edits\",\"version\":1,\"edits\":[{\"op\":\"remove\",\"meta\":{\"k\":\"" + new string('m', 1000) + "\"}}]}";
        Assert.Throws<EditJsonException>(() => EditJson.Parse(bigMeta, small));
    }

    private static EditEnvelope Hand(string edit) =>
        EditJson.Parse("""{"format":"onehtml-edits","version":1,"edits":[""" + edit + "]}");

    [Fact]
    public void Stale_when_the_target_changed_or_is_gone()
    {
        (_, EditEnvelope env) = Saved();
        Document changed = Document.Parse(Page.Replace("<p class=\"intro\">Hello", "<p class=\"intro\">Goodbye", StringComparison.Ordinal));
        ReplayResult r = env.Replay(changed);
        Assert.False(r.Succeeded);
        Assert.Equal(EditStatus.Stale, r.Outcomes[0].Status);
        Assert.All(r.Outcomes.Skip(1), o => Assert.Equal(EditStatus.NotAttempted, o.Status));
        Assert.Null(r.Batch?.Document);

        ReplayResult gone = env.Replay(Document.Parse("<p>nothing</p>"));
        Assert.False(gone.Succeeded);
        Assert.Contains(gone.Outcomes, o => o.Status == EditStatus.Stale);
    }

    [Fact]
    public void Ambiguous_when_the_locator_matches_several_elements_and_the_anchor_is_gone()
    {
        Document doc = Document.Parse("<div><p>same</p></div>");
        EditEnvelope env = EditJson.Create(doc, [Edit.SetAttribute(EditTestHelpers.Find(doc, "p"), "id", "z")]);
        // The page gains a second identical paragraph before the first and a prefix that moves the anchor.
        Document other = Document.Parse("<h1>hi</h1><div><p>same</p><p>same</p></div>");
        ReplayResult r = env.Replay(other);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Outcomes[0].Status, new[] { EditStatus.Ambiguous, EditStatus.Stale });
        Assert.NotEqual(EditStatus.Applied, r.Outcomes[0].Status);
    }

    [Fact]
    public void Anchor_resolves_an_unchanged_document_and_hand_written_locator_only_targets_are_invalid()
    {
        (Document doc, EditEnvelope env) = Saved();
        ReplayResult r = env.Replay(doc);
        Assert.True(r.Succeeded, r.Failure);
        Assert.True(r.Outcomes[0].UsedAnchor);

        EditEnvelope hand = Hand("""{"op":"setAttribute","name":"title","value":"v","target":{"locator":"#main-nav"}}""");
        ReplayResult h = hand.Replay(Document.Parse(Page));
        Assert.False(h.Succeeded);
        Assert.Equal(EditStatus.Invalid, h.Outcomes[0].Status);
    }

    [Fact]
    public void Invalid_when_the_operation_does_not_fit_the_target()
    {
        Document doc = Document.Parse("<p>hello</p>");
        Node text = EditTestHelpers.FindText(doc, "hello");
        EditEnvelope env = EditJson.Create(doc, [Edit.ReplaceText(text, "x")]);
        string json = env.ToJson().Replace("\"replaceText\"", "\"setAttribute\"", StringComparison.Ordinal);
        ReplayResult r = EditJson.Parse(json).Replay(doc);
        Assert.False(r.Succeeded);
        Assert.Equal(EditStatus.Invalid, r.Outcomes[0].Status);

        Assert.Equal(EditStatus.Invalid, Hand("""{"name":"x"}""").Replay(doc).Outcomes[0].Status);
        EditEnvelope unknownOp = Hand("""{"op":"explode","target":{"locator":"p"}}""");
        Assert.Equal(EditStatus.Invalid, unknownOp.Replay(doc).Outcomes[0].Status);
    }

    [Fact]
    public void Text_and_comment_targets_replay()
    {
        Document doc = Document.Parse(Encoding.UTF8.GetBytes(Page));
        Node comment = EditTestHelpers.Find(doc, "p").NextSibling;
        Assert.Equal(NodeKind.Comment, comment.Kind);
        EditEnvelope env = EditJson.Create(doc, [Edit.ReplaceText(EditTestHelpers.FindText(doc, "two"), "dos"), Edit.Remove(comment)]);
        string json = env.ToJson();
        Assert.Contains("\"kind\":\"text\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"comment\"", json, StringComparison.Ordinal);
        // A copy with extra whitespace in front still resolves by parent target and child index.
        ReplayResult r = EditJson.Parse(json).Replay(Document.Parse(Encoding.UTF8.GetBytes("\n\n" + Page)));
        Assert.True(r.Succeeded, r.Failure + " " + string.Join(";", r.Outcomes.Select(o => o.Status + ":" + o.Reason)));
        string text = r.Batch!.Document!.Source.Text;
        Assert.Contains("dos", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<!-- note -->", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Rule_targets_resolve_by_selector_and_index_and_inline_sheet_names()
    {
        Document doc = Document.Parse(Encoding.UTF8.GetBytes(Page));
        InlineStylesheet inl = InlineStylesheets.Of(doc).Single();
        EditEnvelope env = EditJson.Create(doc, null, [SheetEdit.RemoveRule(inl.Sheet, Rule(inl.Sheet, 0))]);
        string json = env.ToJson();
        Assert.Contains("inline:0", json, StringComparison.Ordinal);

        // Rules reordered: selector text still finds the rule, the declarations fingerprint agrees.
        string reordered = Page.Replace(".a { color: red }\n.b { margin: 0 }", ".b { margin: 0 }\n.a { color: red }", StringComparison.Ordinal);
        ReplayResult r = EditJson.Parse(json).Replay(Document.Parse(Encoding.UTF8.GetBytes(reordered)));
        Assert.True(r.Succeeded, r.Failure);
        Assert.DoesNotContain(".a {", r.Batch!.Document!.Source.Text, StringComparison.Ordinal);
        Assert.Contains(".b {", r.Batch.Document.Source.Text, StringComparison.Ordinal);

        // The rule's declarations changed: stale.
        ReplayResult stale = EditJson.Parse(json).Replay(Document.Parse(Page.Replace("color: red", "color: blue", StringComparison.Ordinal)));
        Assert.False(stale.Succeeded);
        Assert.Equal(EditStatus.Stale, stale.Outcomes[0].Status);
    }

    [Fact]
    public void Linked_sheets_are_named_by_url()
    {
        byte[] css = Encoding.UTF8.GetBytes("a { b: c }\n.x { d: e }\n");
        LinkedStylesheet sheet = LinkedStylesheet.Create("https://example.test/site.css", css);
        EditEnvelope env = EditJson.Create(null, null, [SheetEdit.SetDeclaration(sheet.Sheet, Rule(sheet.Sheet, 1), "d", "f")], [sheet]);
        string json = env.ToJson();
        Assert.Contains("https://example.test/site.css", json, StringComparison.Ordinal);
        ReplayResult r = EditJson.Parse(json).Replay(null, [LinkedStylesheet.Create("https://example.test/site.css", css)]);
        Assert.True(r.Succeeded, r.Failure);
        SheetUpdate u = Assert.Single(r.Batch!.Sheets);
        Assert.Equal("a { b: c }\n.x { d: f }\n", Encoding.UTF8.GetString(u.NewLinked!.Bytes.Span));

        // Without the sheet supplied the edit is stale, never a guess.
        ReplayResult missing = EditJson.Parse(json).Replay(null, []);
        Assert.False(missing.Succeeded);
        Assert.Equal(EditStatus.Stale, missing.Outcomes[0].Status);
    }

    [Fact]
    public void One_failing_edit_applies_nothing()
    {
        (_, EditEnvelope env) = Saved();
        Document changed = Document.Parse(Page.Replace("margin: 0", "margin: 9px", StringComparison.Ordinal));
        ReplayResult r = env.Replay(changed);
        Assert.False(r.Succeeded);
        Assert.Null(r.Batch?.Document);
        Assert.Equal(EditStatus.Stale, r.Outcomes[2].Status);
    }

    [Fact]
    public void Fuzz_target_survives_mutated_envelopes()
    {
        (_, EditEnvelope env) = Saved();
        byte[] seed = Encoding.UTF8.GetBytes(env.ToJson());
        if (Environment.GetEnvironmentVariable("ONEHTML_EDITJSON_SEED_DIR") is { Length: > 0 } dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "saved.json"), seed);
        }

        var rng = new Random(77);
        for (int i = 0; i < 4000; i++)
        {
            byte[] b = (byte[])seed.Clone();
            for (int k = rng.Next(1, 6); k > 0; k--)
            {
                b[rng.Next(b.Length)] = (byte)rng.Next(32, 127);
            }

            RecursiveAI.OneHtml.Fuzz.EditJsonTarget.Run(b);
        }
    }
}
