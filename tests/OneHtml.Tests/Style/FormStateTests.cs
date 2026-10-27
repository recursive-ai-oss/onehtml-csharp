using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Selectors;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>A page with form state rules of every reach and a scripted sequence of form-state changes.</summary>
internal static class FormFixture
{
    public const string Css = """
        body{font:16px/1.2 sans-serif;color:#222}
        .hint{display:none}
        input:checked{outline:2px solid green}
        input:checked + label{color:red;font-weight:bold}
        input:checked ~ .hint{display:block}
        li:has(input:checked){background:yellow}
        li:has(> label > :checked) .x{margin-left:3px}
        label:not(:has(:checked)){text-decoration:underline}
        input:disabled{opacity:.5}
        input:enabled{cursor:text}
        fieldset:disabled{color:gray}
        fieldset:disabled .x{font-size:20px}
        fieldset:has(:disabled) legend{font-style:italic}
        input:indeterminate{background:blue}
        input[type=radio]:indeterminate + label{border:1px dotted red}
        input:read-write{border:1px solid black}
        input:read-only{border:1px solid silver}
        option:checked{background:lime}
        optgroup:disabled{color:silver}
        select:has(option:checked) + span{color:maroon}
        .box:not(input:checked ~ .box){padding:4px}
        input:checked::after{content:"x"}
        input:is(:checked, :disabled) + label{letter-spacing:1px}
        :not(:disabled) > .x{word-spacing:2px}
        """;

    public static string Page(bool quirks, int rows = 14)
    {
        var sb = new StringBuilder((quirks ? "" : "<!DOCTYPE html>") + "<html><head><style>").Append(Css).Append("</style></head><body><form>");
        for (int r = 0; r < rows; r++)
        {
            sb.Append("<fieldset id=\"fs").Append(r).Append('"').Append(r % 5 == 0 ? " disabled" : "").Append("><legend>Group ").Append(r).Append("<input type=\"checkbox\" id=\"lg").Append(r).Append("\"></legend><ul>");
            sb.Append("<li><label><input type=\"checkbox\" id=\"cb").Append(r).Append('"').Append(r % 3 == 0 ? " checked" : "").Append("> Check</label><span class=\"x\">x</span></li>");
            sb.Append("<li><input type=\"checkbox\" id=\"ck").Append(r).Append("\"><label for=\"ck").Append(r).Append("\">Two</label><span class=\"hint\">hint</span><div class=\"box\">b</div><div class=\"box\">c</div></li>");
            sb.Append("<li><input type=\"radio\" name=\"g").Append(r % 4).Append("\" id=\"ra").Append(r).Append('"').Append(r % 4 == 1 ? " checked" : "").Append("><label for=\"ra").Append(r).Append("\">Ra</label>");
            sb.Append("<input type=\"radio\" name=\"g").Append(r % 4).Append("\" id=\"rb").Append(r).Append("\"><label for=\"rb").Append(r).Append("\">Rb</label>");
            sb.Append("<input type=\"radio\" id=\"rn").Append(r).Append("\"><label>Rn</label></li>");
            sb.Append("<li><input type=\"text\" id=\"t").Append(r).Append('"').Append(r % 2 == 0 ? " readonly" : "").Append("><textarea id=\"ta").Append(r).Append("\"></textarea><button id=\"b").Append(r).Append("\">Go</button></li>");
            sb.Append("<li><select id=\"s").Append(r).Append("\"><optgroup label=\"A\" id=\"og").Append(r).Append("\"><option id=\"o").Append(r).Append("a\">1</option><option id=\"o").Append(r).Append("b\" selected>2</option></optgroup><option id=\"o").Append(r).Append("c\">3</option></select><span>s</span></li>");
            sb.Append("</ul></fieldset>");
        }

        sb.Append("</form><div id=\"plain\">no form</div></body></html>");
        return sb.ToString();
    }

    public static List<FormStateChange[]> Script(Document doc, int count, int seed)
    {
        var r = new Random(seed);
        List<Node> all = [.. doc.Root.DescendantElements];
        List<Node> forms = [.. all.Where(e => e.LocalName is "input" or "option" or "fieldset" or "optgroup" or "select" or "textarea" or "button" or "div")];
        var script = new List<FormStateChange[]>(count);
        for (int i = 0; i < count; i++)
        {
            int n = r.Next(5) == 0 ? r.Next(2, 6) : 1;
            var batch = new FormStateChange[n];
            for (int k = 0; k < n; k++)
            {
                Node e = forms[r.Next(forms.Count)];
                bool? v = r.Next(4) switch { 0 => null, 1 or 2 => true, _ => false };
                batch[k] = r.Next(3) switch
                {
                    0 => FormStateChange.Checked(e, v),
                    1 => FormStateChange.Disabled(e, v),
                    _ => FormStateChange.Indeterminate(e, v),
                };
            }

            script.Add(batch);
        }

        return script;
    }
}

public class FormStateTests
{
    private static string Prop(DocumentStyles s, Document d, string id, string prop) => s.GetStyle(d.GetElementById(id)).GetPropertyValue(prop);

    private static DocumentStyles With(Document doc, Func<FormStateOverrides, FormStateOverrides> f) =>
        DocumentStyles.Create(doc, StyleContext.Default.WithFormState(f(FormStateOverrides.Empty)));

    [Fact]
    public void Checked_override_toggles_a_checkbox_and_what_selectors_reach_from_it()
    {
        Document doc = Document.Parse(FormFixture.Page(false));
        Node ck = doc.GetElementById("ck1");
        DocumentStyles plain = DocumentStyles.Create(doc);
        Assert.Equal("none", Prop(plain, doc, "ck1", "outline-style"));
        DocumentStyles on = With(doc, f => f.WithChecked(ck, true));
        Assert.Equal("solid", Prop(on, doc, "ck1", "outline-style"));
        Assert.Equal("bold", on.GetStyle(doc.Root.QuerySelector("label[for=ck1]")).GetPropertyValue("font-weight"));
        Assert.Equal("block", on.GetStyle(doc.Root.QuerySelector("#ck1 ~ .hint")).GetPropertyValue("display"));
        Assert.Equal("yellow", on.GetStyle(ck.ParentElement).GetPropertyValue("background-color"));
        Assert.Equal("normal", plain.GetStyle(doc.Root.QuerySelector("label[for=ck1]")).GetPropertyValue("font-weight"));

        // An attribute-checked box can be unchecked.
        Node cb0 = doc.GetElementById("cb0");
        Assert.Equal("solid", Prop(plain, doc, "cb0", "outline-style"));
        Assert.Equal("none", Prop(With(doc, f => f.WithChecked(cb0, false)), doc, "cb0", "outline-style"));
        Assert.Equal("solid", Prop(With(doc, f => f.WithChecked(cb0, false).WithChecked(cb0, null)), doc, "cb0", "outline-style"));
    }

    [Fact]
    public void A_disabled_fieldset_disables_its_controls_and_enabling_it_reverses_that()
    {
        Document doc = Document.Parse(FormFixture.Page(false));
        Node fs = doc.GetElementById("fs1");
        DocumentStyles plain = DocumentStyles.Create(doc);
        Assert.Equal("1", Prop(plain, doc, "cb1", "opacity"));
        DocumentStyles off = With(doc, f => f.WithDisabled(fs, true));
        Assert.Equal(".5", Prop(off, doc, "cb1", "opacity"));
        Assert.Equal("gray", Prop(off, doc, "fs1", "color"));
        Assert.Equal(".5", Prop(off, doc, "t1", "opacity"));
        // The first legend's controls are not disabled by the fieldset.
        Assert.Equal("1", Prop(off, doc, "lg1", "opacity"));

        Node fs0 = doc.GetElementById("fs0");
        Assert.Equal(".5", Prop(plain, doc, "cb0", "opacity"));
        DocumentStyles on = With(doc, f => f.WithDisabled(fs0, false));
        Assert.Equal("1", Prop(on, doc, "cb0", "opacity"));
        Assert.Equal("text", Prop(on, doc, "cb0", "cursor"));
    }

    [Fact]
    public void Read_write_follows_disabled_and_a_named_radio_group_follows_its_members()
    {
        Document doc = Document.Parse(FormFixture.Page(false));
        DocumentStyles plain = DocumentStyles.Create(doc);
        Assert.Equal("solid", Prop(plain, doc, "t1", "border-top-style"));
        Assert.Equal("silver", Prop(With(doc, f => f.WithDisabled(doc.GetElementById("t1"), true)), doc, "t1", "border-top-color"));
        Assert.Equal("silver", Prop(With(doc, f => f.WithDisabled(doc.GetElementById("t0"), false)), doc, "t0", "border-top-color"));

        // Group g0 (rows 0, 4, 8, 12) has no checked member; g1 has one in each of rows 1, 5, 9, 13.
        Node ra0 = doc.GetElementById("ra0");
        string indeterminate = "dotted";
        Assert.Equal(indeterminate, plain.GetStyle(doc.Root.QuerySelector("label[for=ra0]")).GetPropertyValue("border-top-style"));
        DocumentStyles checkedOne = With(doc, f => f.WithChecked(ra0, true));
        foreach (string row in new[] { "ra0", "rb0", "ra4", "rb8" })
        {
            Assert.Equal("none", checkedOne.GetStyle(doc.Root.QuerySelector("label[for=" + row + "]")).GetPropertyValue("border-top-style"));
        }

        // A group becomes indeterminate when its checked members (rows 1, 5, 9 and 13) are all unchecked, and not before.
        Assert.Equal("none", plain.GetStyle(doc.Root.QuerySelector("label[for=rb1]")).GetPropertyValue("border-top-style"));
        Assert.Equal("none", With(doc, f => f.WithChecked(doc.GetElementById("ra1"), false)).GetStyle(doc.Root.QuerySelector("label[for=rb5]")).GetPropertyValue("border-top-style"));
        DocumentStyles allOff = With(doc, f => f.WithChecked(doc.GetElementById("ra1"), false).WithChecked(doc.GetElementById("ra5"), false).WithChecked(doc.GetElementById("ra9"), false).WithChecked(doc.GetElementById("ra13"), false));
        Assert.Equal("dotted", allOff.GetStyle(doc.Root.QuerySelector("label[for=rb5]")).GetPropertyValue("border-top-style"));
        Assert.Equal("dotted", allOff.GetStyle(doc.Root.QuerySelector("label[for=ra1]")).GetPropertyValue("border-top-style"));

        // A radio without a name is indeterminate unless it is checked itself.
        Assert.Equal("dotted", plain.GetStyle(doc.Root.QuerySelector("#rn0 + label")).GetPropertyValue("border-top-style"));
        Assert.Equal("none", With(doc, f => f.WithChecked(doc.GetElementById("rn0"), true)).GetStyle(doc.Root.QuerySelector("#rn0 + label")).GetPropertyValue("border-top-style"));
    }

    [Fact]
    public void Indeterminate_applies_to_checkboxes_and_option_selectedness_is_literal()
    {
        Document doc = Document.Parse(FormFixture.Page(false));
        Assert.Equal("blue", Prop(With(doc, f => f.WithIndeterminate(doc.GetElementById("ck2"), true)), doc, "ck2", "background-color"));
        Assert.Equal("transparent", Prop(With(doc, f => f.WithIndeterminate(doc.GetElementById("ck2"), false)), doc, "ck2", "background-color"));
        // Not a text field: no effect.
        Assert.Equal("transparent", Prop(With(doc, f => f.WithIndeterminate(doc.GetElementById("t2"), true)), doc, "t2", "background-color"));

        DocumentStyles plain = DocumentStyles.Create(doc);
        Assert.Equal("lime", Prop(plain, doc, "o3b", "background-color"));
        Assert.Equal("transparent", Prop(plain, doc, "o3a", "background-color"));
        DocumentStyles moved = With(doc, f => f.WithChecked(doc.GetElementById("o3a"), true).WithChecked(doc.GetElementById("o3b"), false));
        Assert.Equal("lime", Prop(moved, doc, "o3a", "background-color"));
        Assert.Equal("transparent", Prop(moved, doc, "o3b", "background-color"));
        Assert.Equal("maroon", moved.GetStyle(doc.GetElementById("s3").NextElementSibling).GetPropertyValue("color"));
    }

    [Fact]
    public void Form_state_in_a_context_matches_the_selector_api_and_is_checked_against_its_document()
    {
        Document doc = Document.Parse(FormFixture.Page(false));
        Node ck = doc.GetElementById("ck3");
        var state = new InteractionState { FormState = FormStateOverrides.Empty.WithChecked(ck, true) };
        var options = new SelectorMatchOptions { State = state };
        Assert.True(ck.Matches(SelectorList.Parse(":checked"), options));
        Assert.False(ck.Matches(":checked"));
        Assert.Equal(1, FormStateOverrides.Empty.WithChecked(ck, true).Count);
        Assert.True(FormStateOverrides.Empty.WithChecked(ck, true).Equals(FormStateOverrides.Empty.WithChecked(ck, true)));
        Assert.True(FormStateOverrides.Empty.WithChecked(ck, true).WithChecked(ck, null).IsEmpty);
        Assert.Equal(true, FormStateOverrides.Empty.WithDisabled(ck, true).GetDisabled(ck));
        Assert.Null(FormStateOverrides.Empty.WithDisabled(ck, true).GetChecked(ck));

        Document other = Document.Parse(FormFixture.Page(false));
        Assert.Throws<ArgumentException>(() => DocumentStyles.Create(other, StyleContext.Default.WithFormState(FormStateOverrides.Empty.WithChecked(ck, true))));
        Assert.Throws<ArgumentException>(() => FormStateOverrides.Empty.WithChecked(ck, true).WithChecked(other.GetElementById("ck3"), true));
        Assert.Throws<ArgumentException>(() => FormStateOverrides.Empty.WithChecked(doc.Root, true));
    }

    [Fact]
    public void WithContext_overlays_equal_fresh_styles_and_restyle_carries_the_overrides()
    {
        Document doc = Document.Parse(FormFixture.Page(false));
        DocumentStyles start = DocumentStyles.Create(doc);
        FormStateOverrides f = FormStateOverrides.Empty.WithChecked(doc.GetElementById("ck1"), true).WithDisabled(doc.GetElementById("fs2"), true);
        StyleContext ctx = StyleContext.Default.WithFormState(f);
        DocumentStyles overlay = start.WithContext(ctx);
        DocumentStyles fresh = DocumentStyles.Create(doc, ctx);
        Assert.NotEmpty(overlay.ChangedElements);
        foreach (Node e in doc.Root.DescendantElements)
        {
            Assert.Null(overlay.GetStyle(e).FirstDifference(fresh.GetStyle(e)));
        }

        // Back to no form state: equal to the plain styles again.
        DocumentStyles back = overlay.WithContext(StyleContext.Default);
        foreach (Node e in doc.Root.DescendantElements)
        {
            Assert.Null(back.GetStyle(e).FirstDifference(start.GetStyle(e)));
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void Live_styles_equal_a_fresh_computation_after_every_form_change(int seed, bool quirks)
    {
        Document doc = Document.Parse(FormFixture.Page(quirks));
        DocumentStyles start = DocumentStyles.Create(doc);
        LiveDocumentStyles live = LiveDocumentStyles.Create(start);
        List<FormStateChange[]> script = FormFixture.Script(doc, 150, seed);
        List<Node> elements = [.. doc.Root.DescendantElements];
        DocumentStyles previous = start;
        int changedTotal = 0;
        foreach (FormStateChange[] batch in script)
        {
            live.ApplyFormState(batch);
            StyleContext ctx = live.GetContext();
            DocumentStyles fresh = DocumentStyles.Create(doc, ctx);
            DocumentStyles overlay = start.WithContext(ctx);
            var changed = new HashSet<int>(live.ChangedElementIndices.ToArray());
            Assert.Equal(changed.Count, live.ChangedElementCount);
            foreach (Node e in elements)
            {
                Assert.Null(live.GetStyle(e).FirstDifference(fresh.GetStyle(e)));
                Assert.Null(live.GetStyle(e).FirstDifference(overlay.GetStyle(e)));
                if (previous.GetStyle(e).FirstDifference(fresh.GetStyle(e)) is not null)
                {
                    Assert.Contains(e.Index, changed);
                }
            }

            changedTotal += changed.Count;
            previous = fresh;
        }

        Assert.True(changedTotal > 100);
    }

    [Fact]
    public void Live_form_changes_mix_with_interaction_changes_and_snapshots_stay_unchanged()
    {
        Document doc = Document.Parse(FormFixture.Page(false));
        DocumentStyles start = DocumentStyles.Create(doc);
        LiveDocumentStyles live = LiveDocumentStyles.Create(start);
        Node ck = doc.GetElementById("ck1");
        live.ApplyFormState([FormStateChange.Checked(ck, true)]);
        live.ApplyState(ElementStateChange.Hover(doc.GetElementById("t1")));
        Assert.Equal("solid", live.GetStyle(ck).GetPropertyValue("outline-style"));
        Assert.Equal("none", start.GetStyle(ck).GetPropertyValue("outline-style"));
        DocumentStyles snap = live.ToSnapshot();
        Assert.True(snap.Context.FormState.GetChecked(ck));
        live.ApplyFormState([FormStateChange.Checked(ck, null)]);
        Assert.Equal("none", live.GetStyle(ck).GetPropertyValue("outline-style"));
        Assert.Equal("solid", snap.GetStyle(ck).GetPropertyValue("outline-style"));

        // A live object made from a snapshot with form state starts there.
        LiveDocumentStyles again = LiveDocumentStyles.Create(snap);
        Assert.Equal("solid", again.GetStyle(ck).GetPropertyValue("outline-style"));
        again.ApplyFormState([FormStateChange.Checked(ck, false)]);
        Assert.Equal("none", again.GetStyle(ck).GetPropertyValue("outline-style"));
        Assert.Throws<ArgumentException>(() => again.ApplyFormState([FormStateChange.Checked(Document.Parse(FormFixture.Page(false)).GetElementById("ck1"), true)]));
    }

    [Fact]
    public void Generated_box_styles_follow_form_state()
    {
        Document doc = Document.Parse(FormFixture.Page(false));
        LiveDocumentStyles live = LiveDocumentStyles.Create(DocumentStyles.Create(doc));
        Node ck = doc.GetElementById("ck2");
        Assert.True(live.HasInteractivePseudoElementRules);
        Assert.Null(live.GetPseudoStyle(ck, PseudoElementKind.After));
        live.ApplyFormState([FormStateChange.Checked(ck, true)]);
        Assert.Equal("\"x\"", live.GetPseudoStyle(ck, PseudoElementKind.After)!.GetPropertyValue("content"));
        live.ApplyFormState([FormStateChange.Checked(ck, false)]);
        Assert.Null(live.GetPseudoStyle(ck, PseudoElementKind.After));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(5, true)]
    public void Form_changes_allocate_nothing_in_steady_state(int seed, bool quirks)
    {
        Document doc = Document.Parse(FormFixture.Page(quirks));
        LiveDocumentStyles live = LiveDocumentStyles.Create(DocumentStyles.Create(doc));
        List<FormStateChange[]> script = FormFixture.Script(doc, 600, seed);
        int color = PropertyTable.IdOf("color");
        int display = PropertyTable.IdOf("display");
        List<int> elements = [.. doc.Root.DescendantElements.Select(e => e.Index)];
        long sink = 0;

        long Pass()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Span<int> buffer = stackalloc int[2048];
            foreach (FormStateChange[] batch in script)
            {
                live.ApplyFormState(batch);
                sink += live.ChangedElementsInto(buffer);
                foreach (int i in live.ChangedElementIndices)
                {
                    sink += live.GetValue(i, color)!.Text.Length + live.GetStyle(i).Id;
                }
            }

            foreach (int i in elements)
            {
                sink += live.GetValue(i, display)!.Text.Length;
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Pass();
        Pass();
        long bytes = Pass();
        Assert.True(sink != 0);
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void Rolled_back_declarations_allocate_nothing_in_steady_state()
    {
        // revert and revert-layer send the cascade down its rescan path, which must reuse its buffers too.
        Document doc = Document.Parse("<!doctype html><style>@layer a{ul{color:red;margin-top:3px}}@layer b{ul{color:revert-layer;margin-top:revert}ul:has(:checked){color:blue;--x:revert}}</style><ul><li><input id=c type=checkbox></ul>");
        LiveDocumentStyles live = LiveDocumentStyles.Create(DocumentStyles.Create(doc));
        Node c = doc.GetElementById("c");
        int color = PropertyTable.IdOf("color");
        int ul = doc.Root.QuerySelector("ul").Index;
        long sink = 0;

        long Pass()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
            {
                live.ApplyFormState([FormStateChange.Checked(c, i % 2 == 0)]);
                sink += live.GetValue(ul, color)!.Text.Length;
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Pass();
        Pass();
        long bytes = Pass();
        Assert.True(sink != 0);
        Assert.Equal(0, bytes);
    }
}
