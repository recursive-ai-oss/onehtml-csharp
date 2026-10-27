using System.Collections.Generic;
using System.Linq;
using System;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>The keyframes API beyond what the Chrome table checks: the rule view, the live path, reading again for free, change reporting after sheet edits, and hostile input.</summary>
public class KeyframesApiTests
{
    private const string Sheet = "@keyframes a { from { opacity: 0 } to { opacity: 1 } } @keyframes b { from { color: red } to { color: blue } }";

    private static string[] Frames(DocumentStyles ds, Node el, int animation = 0) =>
        [.. ds.GetAnimationKeyframes(ds.GetStyle(el))[animation]!.Keyframes.Select(KeyframesChromeTests.Format)];

    private static string[] Frames(LiveDocumentStyles ds, Node el) =>
        [.. ds.GetAnimationKeyframes(ds.GetStyle(el))[0]!.Keyframes.Select(KeyframesChromeTests.Format)];

    [Fact]
    public void The_winning_rule_carries_its_origin_layer_and_blocks()
    {
        (DocumentStyles ds, _) = KeyframesChromeTests.Page(
            ["@layer l1, l2; @layer l2 { @keyframes k { 10%, 20% { opacity: .1; margin: 1px; width: var(--w); animation-duration: 3s } to { opacity: 1 !important; color: red } } } @-webkit-keyframes k { from { opacity: 0 } } @keyframes \"s p\" { from { opacity: 0 } }"],
            "", "", "k");
        KeyframesRule rule = ds.GetKeyframesRule("k")!;
        Assert.False(rule.IsPrefixed);
        Assert.Equal("l2", rule.LayerPath);
        Assert.Equal(StyleOrigin.Author, rule.Origin);
        Assert.Equal(3, rule.Blocks.Count);
        Assert.Equal([0.1, 0.2, 1.0], rule.Blocks.Select(b => b.Offset));
        // Shorthands expand, the animation-duration and the !important declaration are gone, and var() stays for the element.
        Assert.Equal(["opacity=.1", "margin-top=1px", "margin-right=1px", "margin-bottom=1px", "margin-left=1px", "width=var(--w)"], rule.Blocks[0].Declarations.Select(d => d.Property + "=" + d.Value));
        Assert.True(rule.Blocks[0].Declarations[^1].UsesVar);
        Assert.Equal(["color=red"], rule.Blocks[2].Declarations.Select(d => d.Property + "=" + d.Value));
        Assert.Equal(["k", "s p"], ds.KeyframesRules.Select(r => r.Name));
        Assert.Null(ds.GetKeyframesRule("K"));
        Assert.Null(ds.GetKeyframesRule("nope"));
    }

    [Fact]
    public void A_prefixed_rule_is_used_only_when_nothing_else_has_the_name()
    {
        (DocumentStyles ds, _) = KeyframesChromeTests.Page(["@-webkit-keyframes p { from { opacity: 0 } } @keyframes q { from { opacity: 0 } } @-webkit-keyframes q { from { opacity: 1 } }"], "", "", "p");
        Assert.True(ds.GetKeyframesRule("p")!.IsPrefixed);
        Assert.False(ds.GetKeyframesRule("q")!.IsPrefixed);
    }

    [Fact]
    public void Var_reads_the_custom_properties_of_the_element_it_is_resolved_for()
    {
        Document doc = Document.Parse("<style>@keyframes k { from { width: var(--w); margin: var(--m) 2px; color: var(--bad) } to { width: 9px } } div { animation: k 1s }</style><div id=a style='--w: 1px; --m: 5px; --bad: 7px'></div><div id=b style='--w: 2px'></div>");
        DocumentStyles ds = DocumentStyles.Create(doc);
        Node a = doc.Root.DescendantElements.First(e => e.GetAttribute("id") == "a");
        Node b = doc.Root.DescendantElements.First(e => e.GetAttribute("id") == "b");
        Assert.Contains("width=1px", Frames(ds, a)[0].Split('|')[3].Split('~'));
        Assert.Contains("margin-top=5px", Frames(ds, a)[0].Split('|')[3].Split('~'));
        Assert.Contains("width=2px", Frames(ds, b)[0].Split('|')[3].Split('~'));
        // A shorthand whose var() is not defined is invalid at computed-value time: every longhand is unset.
        Assert.Contains("margin-top=unset!", Frames(ds, b)[0].Split('|')[3].Split('~'));
        // The implicit end keyframe carries the element's own value for what only the first keyframe sets.
        Assert.StartsWith("1|", Frames(ds, a)[1]);
        Assert.Contains("margin-top=", Frames(ds, a)[1]);
    }

    [Fact]
    public void Logical_properties_are_mapped_under_the_elements_writing_mode()
    {
        // Left to right, margin-inline-start and margin-left are one property and the later declaration wins; right to left, they are two.
        (DocumentStyles ds, Node el) = KeyframesChromeTests.Page(["@keyframes k { from { margin-inline-start: 1px; margin-left: 2px } to { margin-left: 3px } }"], "", "", "k");
        Assert.Equal("0|ease|-|margin-left=2px", Frames(ds, el)[0]);
        (ds, el) = KeyframesChromeTests.Page(["@keyframes k { from { margin-left: 2px; margin-inline-start: 1px } to { margin-left: 3px } }"], "", "", "k");
        Assert.Equal("0|ease|-|margin-left=1px", Frames(ds, el)[0]);
        (ds, el) = KeyframesChromeTests.Page(["@keyframes k { from { margin-inline-start: 1px; margin-left: 2px } to { margin-left: 3px } }"], "direction: rtl", "", "k");
        Assert.Equal("0|ease|-|margin-left=2px~margin-right=1px", Frames(ds, el)[0]);
    }

    [Fact]
    public void An_animation_without_a_rule_or_named_none_has_no_keyframes()
    {
        (DocumentStyles ds, Node el) = KeyframesChromeTests.Page([Sheet], "", "", "none, nope, a, \"b\"");
        IReadOnlyList<ResolvedKeyframes?> all = ds.GetAnimationKeyframes(ds.GetStyle(el));
        Assert.Equal(4, all.Count);
        Assert.Null(all[0]);
        Assert.Null(all[1]);
        Assert.Equal("a", all[2]!.Name);
        Assert.Equal("b", all[3]!.Name);
        Assert.Equal(["color"], all[3]!.AnimatedProperties);
        Assert.Null(ds.ResolveKeyframes(ds.GetStyle(el), "nope"));
        Assert.Equal(2, ds.ResolveKeyframes(ds.GetStyle(el), "a")!.Keyframes.Count);
    }

    [Fact]
    public void The_live_styles_give_the_same_keyframes_and_state_changes_do_not_disturb_them()
    {
        Document doc = Document.Parse("<style>@keyframes k { from { opacity: 0 } to { opacity: var(--o) } } a { animation: k 1s; --o: .5 } a:hover { --o: .9 }</style><a id=x href=/>t</a>");
        DocumentStyles ds = DocumentStyles.Create(doc);
        Node a = doc.Root.DescendantElements.First(e => e.GetAttribute("id") == "x");
        LiveDocumentStyles live = LiveDocumentStyles.Create(ds);
        Assert.Equal(Frames(ds, a), Frames(live, a));
        Assert.Same(ds.GetKeyframesRule("k")!.Name, live.GetKeyframesRule("k")!.Name);
        live.ApplyState(ElementStateChange.Hover(a));
        Assert.Equal("1|ease|-|opacity=.9", Frames(live, a)[1]);
        live.ApplyState(default(ElementStateChange).WithHover(default));
        Assert.Equal(Frames(ds, a), Frames(live, a));
    }

    [Fact]
    public void Reading_the_same_style_again_allocates_nothing()
    {
        Document doc = Document.Parse("<style>@keyframes k { from { opacity: 0 } to { opacity: var(--o) } } a { animation: k 1s; --o: .5 }</style><a id=x href=/>t</a>");
        DocumentStyles ds = DocumentStyles.Create(doc);
        Node a = doc.Root.DescendantElements.First(e => e.GetAttribute("id") == "x");
        LiveDocumentStyles live = LiveDocumentStyles.Create(ds);
        ComputedStyle style = ds.GetStyle(a);
        ComputedStyle liveStyle = live.GetStyle(a);
        int sink = 0;
        for (int i = 0; i < 100; i++)
        {
            sink += ds.GetAnimationKeyframes(style).Count + live.GetAnimationKeyframes(liveStyle).Count + ds.GetStyle(a).GetHashCode() * 0;
        }

        // Several windows, each measured inside a fully optimized method: a window that straddles the runtime's tier-up or on-stack replacement of a
        // loop (a one-off cost of several KB on the thread that runs it, seen when other tests keep the JIT busy) is not steady state. A read that
        // allocated every time would allocate in every window, so the smallest one must still be 0.
        long used = long.MaxValue;
        for (int window = 0; window < 5 && used != 0; window++)
        {
            used = Math.Min(used, Window(ds, live, style, liveStyle, ref sink));
        }

        Assert.True(sink > 0);
        Assert.Equal(0, used);
        Assert.Same(ds.GetAnimationKeyframes(style), ds.GetAnimationKeyframes(style));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private static long Window(DocumentStyles ds, LiveDocumentStyles live, ComputedStyle style, ComputedStyle liveStyle, ref int sink)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            sink += ds.GetAnimationKeyframes(style).Count + live.GetAnimationKeyframes(liveStyle).Count;
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static Node In(RestyleResult r) => r.Styles.Document.Root.DescendantElements.First(e => e.GetAttribute("id") == "x");

    private static BatchResult Edit(Document doc, params SheetEdit[] edits)
    {
        BatchResult r = new EditBatch(doc, [], edits, []).Apply();
        Assert.True(r.Succeeded, r.Failure);
        return r;
    }

    [Fact]
    public void Sheet_edits_to_keyframes_are_reported_by_name_after_a_restyle()
    {
        Document doc = Document.Parse("<style>" + Sheet + " div { animation: a 1s, b 1s }</style><div id=x></div>");
        DocumentStyles ds = DocumentStyles.Create(doc);
        Node x = doc.Root.DescendantElements.First(e => e.GetAttribute("id") == "x");
        Stylesheet sheet = InlineStylesheets.Of(doc)[0].Sheet;
        CssNode ruleA = sheet.Rules.First(r => r.Kind == CssNodeKind.AtRule && r.PreludeText.ToString().Trim() == "a");
        CssNode ruleB = sheet.Rules.First(r => r.Kind == CssNodeKind.AtRule && r.PreludeText.ToString().Trim() == "b");
        string beforeA = string.Join(";", Frames(ds, x, 0));

        RestyleResult r1 = ds.Restyle(Edit(doc, SheetEdit.ReplaceRule(sheet, ruleA, "@keyframes a { from { opacity: .2 } to { opacity: .8 } }")));
        Assert.Equal(["a"], r1.ChangedKeyframes);
        Assert.Equal("0|ease|-|opacity=.2", Frames(r1.Styles, In(r1), 0)[0]);
        Assert.Equal(beforeA, string.Join(";", Frames(ds, x, 0)));

        RestyleResult r2 = ds.Restyle(Edit(doc, SheetEdit.RemoveRule(sheet, ruleB)));
        Assert.Equal(["b"], r2.ChangedKeyframes);
        Assert.Null(r2.Styles.GetAnimationKeyframes(r2.Styles.GetStyle(In(r2)))[1]);

        RestyleResult r3 = ds.Restyle(Edit(doc, SheetEdit.InsertRuleAfter(sheet, ruleB, "@keyframes c { from { opacity: 0 } }"), SheetEdit.InsertRuleAfter(sheet, ruleB, "@keyframes a { from { opacity: 0 } }")));
        Assert.Equal(["a", "c"], r3.ChangedKeyframes);

        // The same text moved, or an edit elsewhere in the sheet, changes no name.
        RestyleResult r4 = ds.Restyle(Edit(doc, SheetEdit.InsertRuleAfter(sheet, ruleB, "p { color: red }")));
        Assert.Empty(r4.ChangedKeyframes);
        RestyleResult r5 = ds.Restyle(Edit(doc, SheetEdit.InsertRuleBefore(sheet, ruleA, "@keyframes z { from { opacity: 0 } }"), SheetEdit.RemoveRule(sheet, sheet.Rules.First(r => r.Kind == CssNodeKind.QualifiedRule))));
        Assert.Equal(["z"], r5.ChangedKeyframes);
    }

    [Fact]
    public void A_document_edit_that_changes_the_style_text_is_reported_too()
    {
        Document doc = Document.Parse("<style>@keyframes a { from { opacity: 0 } }</style><div style='animation: a 1s'></div>");
        DocumentStyles ds = DocumentStyles.Create(doc);
        Node text = doc.Root.DescendantElements.First(e => e.LocalName == "style").FirstChild;
        EditResult er = doc.Apply([RecursiveAI.OneHtml.Editing.Edit.ReplaceText(text, "@keyframes a { from { opacity: 1 } } @keyframes b { }")]);
        Assert.True(er.Succeeded, er.Failure);
        Assert.Equal(["a", "b"], ds.Restyle(er).ChangedKeyframes);
    }

    [Fact]
    public void Many_blocks_stay_cheap()
    {
        var css = new System.Text.StringBuilder("@keyframes k {");
        for (int i = 0; i < 20_000; i++)
        {
            css.Append((i / 201.0).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append("% { opacity: .5; margin-left: ").Append(i).Append("px; color: var(--c) } ");
        }

        css.Append('}');
        (DocumentStyles ds, Node el) = KeyframesChromeTests.Page([css.ToString()], "--c: red", "", "k");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ResolvedKeyframes k = ds.GetAnimationKeyframes(ds.GetStyle(el))[0]!;
        Assert.True(k.Keyframes.Count >= 20_000);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), sw.Elapsed.ToString());
        Assert.Equal(k.Keyframes.OrderBy(f => f.Offset).Select(f => f.Offset), k.Keyframes.Select(f => f.Offset));
    }
}
