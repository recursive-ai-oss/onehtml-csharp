using System;
using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using RecursiveAI.OneHtml.Selectors;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// The parts of a range slider (<c>::slider-thumb</c>, <c>::slider-track</c>, <c>::slider-fill</c> and the browsers' names for them): which
/// elements have them, how they inherit, how they follow state and edits. What the user-agent defaults compute to is checked against Chrome in
/// <see cref="ChromeRangePartsTests"/>.
/// </summary>
public class RangeSliderPartTests
{
    private static readonly (string Name, PseudoElementKind Kind)[] Names =
    [
        ("slider-thumb", PseudoElementKind.SliderThumb),
        ("-webkit-slider-thumb", PseudoElementKind.SliderThumb),
        ("-moz-range-thumb", PseudoElementKind.SliderThumb),
        ("slider-track", PseudoElementKind.SliderTrack),
        ("-webkit-slider-runnable-track", PseudoElementKind.SliderTrack),
        ("-moz-range-track", PseudoElementKind.SliderTrack),
        ("slider-fill", PseudoElementKind.SliderFill),
        ("-moz-range-progress", PseudoElementKind.SliderFill),
    ];

    public static IEnumerable<object[]> EveryName() => Names.Select(n => new object[] { n.Name, n.Kind });

    private static ComputedStyle? Part(string html, string id, PseudoElementKind kind)
    {
        Document doc = Document.Parse(html);
        return DocumentStyles.Create(doc).GetPseudoStyle(doc.GetElementById(id), kind);
    }

    [Theory]
    [MemberData(nameof(EveryName))]
    public void Every_name_styles_its_part_on_a_range_input_and_no_other_part(string name, PseudoElementKind kind)
    {
        string h = $"<style>input::{name}{{outline-width:7px;color:red}}</style><input id=r type=range>";
        Assert.Equal("7px", Part(h, "r", kind)!.GetPropertyValue("outline-width"));
        foreach (PseudoElementKind other in Enum.GetValues<PseudoElementKind>().Where(k => k >= PseudoElementKind.SliderThumb && k != kind))
        {
            // The other parts exist for the input (or not, for the fill) but this rule is not theirs.
            ComputedStyle? s = Part(h, "r", other);
            Assert.True(s is null || s.GetPropertyValue("outline-width") != "7px");
        }
    }

    [Theory]
    [MemberData(nameof(EveryName))]
    public void A_part_exists_only_for_an_input_of_the_range_type(string name, PseudoElementKind kind)
    {
        string h = $"<style>*::{name}{{outline-width:7px}}</style><input id=a type=range><input id=b type=RANGE><input id=c type=' range'><input id=d type=text><input id=e><input id=f type=rang><div id=g type=range></div><span id=h type=range></span><input id=i type=RaNgE>"
            + "<select id=j type=range></select><textarea id=k type=range></textarea><button id=l type=range></button><svg><input id=m type=range></input></svg>";
        foreach (string yes in new[] { "a", "b", "i" })
        {
            Assert.Equal("7px", Part(h, yes, kind)!.GetPropertyValue("outline-width"));
        }

        foreach (string no in new[] { "c", "d", "e", "f", "g", "h", "j", "k", "l" })
        {
            Assert.Null(Part(h, no, kind));
        }
    }

    [Fact]
    public void A_type_attribute_edit_adds_and_removes_the_parts_and_the_restyle_names_the_element()
    {
        Document doc = Document.Parse("<style>input::-webkit-slider-thumb{outline-width:7px}</style><input id=i type=text><input id=j type=range>");
        DocumentStyles styles = DocumentStyles.Create(doc);
        Assert.Null(styles.GetPseudoStyle(doc.GetElementById("i"), PseudoElementKind.SliderThumb));
        EditResult r = doc.Apply([Edit.SetAttribute(doc.GetElementById("i"), "type", "range"), Edit.SetAttribute(doc.GetElementById("j"), "type", "number")]);
        Assert.True(r.Succeeded, r.Failure);
        RestyleResult rs = styles.Restyle(r);
        Node i = r.Document!.GetElementById("i");
        Node j = r.Document.GetElementById("j");
        Assert.Contains(i.Index, rs.PseudoElementsMayDiffer!);
        Assert.Contains(j.Index, rs.PseudoElementsMayDiffer!);
        Assert.Equal("7px", rs.Styles.GetPseudoStyle(i, PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-width"));
        Assert.Null(rs.Styles.GetPseudoStyle(j, PseudoElementKind.SliderThumb));
        Assert.Equal(rs.Styles.GetPseudoStyle(i, PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-width"),
            DocumentStyles.Create(r.Document).GetPseudoStyle(i, PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-width"));
    }

    [Fact]
    public void A_stylesheet_edit_reaches_the_parts_of_the_inputs_it_matches()
    {
        Document doc = Document.Parse("<style id=s>input::slider-thumb{outline-width:3px}</style><input id=i type=range><input id=t>");
        DocumentStyles styles = DocumentStyles.Create(doc);
        EditResult r = doc.Apply([Edit.ReplaceText(doc.GetElementById("s"), "input::slider-thumb{outline-width:4px}")]);
        RestyleResult rs = styles.Restyle(r);
        Node i = r.Document!.GetElementById("i");
        Assert.Contains(i.Index, rs.PseudoElementsMayDiffer!);
        Assert.Equal("4px", rs.Styles.GetPseudoStyle(i, PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-width"));
    }

    [Fact]
    public void The_user_agent_gives_the_thumb_and_track_a_style_and_the_fill_none()
    {
        const string h = "<input id=r type=range>";
        ComputedStyle thumb = Part(h, "r", PseudoElementKind.SliderThumb)!;
        ComputedStyle track = Part(h, "r", PseudoElementKind.SliderTrack)!;
        Assert.Equal("block", thumb.GetPropertyValue("display"));
        Assert.Equal("border-box", thumb.GetPropertyValue("box-sizing"));
        Assert.Equal("block", track.GetPropertyValue("display"));
        Assert.Equal("1", track.GetPropertyValue("flex-grow"));
        Assert.Equal("center", track.GetPropertyValue("align-self"));
        Assert.Null(Part(h, "r", PseudoElementKind.SliderFill));
        Assert.NotNull(Part("<style>input::-moz-range-progress{color:red}</style>" + h, "r", PseudoElementKind.SliderFill));
    }

    [Fact]
    public void Parts_inherit_text_properties_but_not_box_properties_from_the_input()
    {
        const string h = "<style>input{color:red;cursor:help;font-size:20px;border:3px solid blue;margin:5px;padding:2px;opacity:.5;background-color:yellow}</style><input id=r type=range>";
        foreach (PseudoElementKind kind in new[] { PseudoElementKind.SliderThumb, PseudoElementKind.SliderTrack })
        {
            ComputedStyle s = Part(h, "r", kind)!;
            Assert.Equal("red", s.GetPropertyValue("color"));
            Assert.Equal("help", s.GetPropertyValue("cursor"));
            Assert.Equal(20, s.FontSizePx);
            Assert.Equal("0", s.GetPropertyValue("margin-top"));
            Assert.Equal("0", s.GetPropertyValue("padding-left"));
            Assert.Equal("1", s.GetPropertyValue("opacity"));
            Assert.Equal("none", s.GetPropertyValue("border-top-style"));
        }
    }

    [Fact]
    public void The_thumb_and_fill_inherit_from_the_track_and_the_track_from_the_input()
    {
        const string h = "<style>input{color:red;font-size:10px} input::slider-track{color:blue;cursor:help} input::slider-thumb{outline-width:2px}</style><input id=r type=range>";
        Assert.Equal("blue", Part(h, "r", PseudoElementKind.SliderThumb)!.GetPropertyValue("color"));
        Assert.Equal("help", Part(h, "r", PseudoElementKind.SliderThumb)!.GetPropertyValue("cursor"));
        Assert.Equal("blue", Part(h + "<style>input::slider-fill{outline-width:1px}</style>", "r", PseudoElementKind.SliderFill)!.GetPropertyValue("color"));
        Assert.Equal("red", Part("<style>input{color:red} input::slider-thumb{outline-width:2px}</style><input id=r type=range>", "r", PseudoElementKind.SliderThumb)!.GetPropertyValue("color"));
    }

    [Fact]
    public void Author_rules_cascade_with_importance_layers_and_specificity_and_beat_the_user_agent()
    {
        const string h = "<style>@layer a{input::slider-thumb{background-color:red !important;color:red}} input::slider-thumb{background-color:blue;color:blue;display:inline-block}"
            + " input#r::-webkit-slider-thumb{box-sizing:content-box} input::-moz-range-thumb{box-sizing:padding-box;margin-top:4px}</style><input id=r type=range>";
        ComputedStyle s = Part(h, "r", PseudoElementKind.SliderThumb)!;
        Assert.Equal("red", s.GetPropertyValue("background-color"));
        Assert.Equal("blue", s.GetPropertyValue("color"));
        Assert.Equal("inline-block", s.GetPropertyValue("display"));
        Assert.Equal("content-box", s.GetPropertyValue("box-sizing"));
        Assert.Equal("4px", s.GetPropertyValue("margin-top"));
    }

    [Fact]
    public void A_selector_list_may_mix_names_and_a_universal_part_rule_matches_range_inputs_only()
    {
        const string h = "<style>::-webkit-slider-thumb, p::before{outline-width:6px}</style><input id=r type=range><input id=t>";
        Assert.Equal("6px", Part(h, "r", PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-width"));
        Assert.Null(Part(h, "t", PseudoElementKind.SliderThumb));
    }

    [Fact]
    public void Rules_a_browser_rejects_stay_rejected()
    {
        // Functional notation, a pseudo-element after the part, and a state pseudo-class that is not allowed after a pseudo-element.
        const string h = "<style>input::slider-thumb(x){outline-width:1px} input::slider-thumb::before{outline-width:2px} input::slider-thumb:disabled{outline-width:3px}"
            + " input::-webkit-slider-thumb(){outline-width:4px}</style><input id=r type=range>";
        Assert.NotEqual("1px", Part(h, "r", PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-width"));
        Assert.NotEqual("2px", Part(h, "r", PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-width"));
        Assert.NotEqual("3px", Part(h, "r", PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-width"));
        Assert.NotEqual("4px", Part(h, "r", PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-width"));
    }

    [Fact]
    public void A_state_pseudo_class_on_the_part_itself_is_not_supported()
    {
        // A part has no hover or focus state of its own here: the rule is dropped, not applied to the input's state.
        Document doc = Document.Parse("<style>input::slider-thumb:hover{outline-width:5px}</style><input id=r type=range>");
        LiveDocumentStyles live = LiveDocumentStyles.Create(DocumentStyles.Create(doc));
        Node r = doc.GetElementById("r");
        live.ApplyState(ElementStateChange.Hover(r));
        Assert.NotEqual("5px", live.GetPseudoStyle(r, PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-width"));
    }

    private const string StateRules =
        "<style>input:hover::slider-thumb{outline-width:1px} input:active::-webkit-slider-runnable-track{outline-width:2px} input:focus::-moz-range-progress{outline-width:3px}"
        + " input:focus-visible::slider-thumb{outline-style:dashed} input:disabled::slider-track{outline-width:4px} input:checked::slider-thumb{outline-width:9px}</style>"
        + "<input id=a type=range><input id=b type=range disabled><input id=c type=checkbox>";

    [Fact]
    public void Parts_follow_the_style_context_on_the_snapshot_path()
    {
        Document doc = Document.Parse(StateRules);
        Node a = doc.GetElementById("a");
        Node b = doc.GetElementById("b");
        DocumentStyles none = DocumentStyles.Create(doc);
        Assert.NotEqual("1px", none.GetPseudoStyle(a, PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-width"));
        Assert.Equal("4px", none.GetPseudoStyle(b, PseudoElementKind.SliderTrack)!.GetPropertyValue("outline-width"));
        Assert.NotEqual("4px", none.GetPseudoStyle(a, PseudoElementKind.SliderTrack)!.GetPropertyValue("outline-width"));
        DocumentStyles hovered = DocumentStyles.Create(doc, StyleContext.Default with { Hover = a });
        Assert.Equal("1px", hovered.GetPseudoStyle(a, PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-width"));
        DocumentStyles active = none.WithContext(StyleContext.Default with { Active = a });
        Assert.Equal("2px", active.GetPseudoStyle(a, PseudoElementKind.SliderTrack)!.GetPropertyValue("outline-width"));
        DocumentStyles focused = none.WithContext(StyleContext.Default with { Focus = a, FocusSource = FocusSource.Keyboard });
        Assert.Equal("3px", focused.GetPseudoStyle(a, PseudoElementKind.SliderFill)!.GetPropertyValue("outline-width"));
        Assert.Equal("dashed", focused.GetPseudoStyle(a, PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-style"));
        DocumentStyles enabled = none.WithContext(StyleContext.Default.WithFormState(FormStateOverrides.Empty.WithDisabled(b, false)));
        Assert.NotEqual("4px", enabled.GetPseudoStyle(b, PseudoElementKind.SliderTrack)!.GetPropertyValue("outline-width"));
        DocumentStyles disabled = none.WithContext(StyleContext.Default.WithFormState(FormStateOverrides.Empty.WithDisabled(a, true)));
        Assert.Equal("4px", disabled.GetPseudoStyle(a, PseudoElementKind.SliderTrack)!.GetPropertyValue("outline-width"));
    }

    [Fact]
    public void Parts_follow_state_changes_on_the_live_path_and_equal_the_snapshot_path()
    {
        Document doc = Document.Parse(StateRules);
        DocumentStyles start = DocumentStyles.Create(doc);
        LiveDocumentStyles live = LiveDocumentStyles.Create(start);
        Node a = doc.GetElementById("a");
        Node b = doc.GetElementById("b");
        Assert.True(live.HasInteractivePseudoElementRules);
        PseudoElementKind[] kinds = [PseudoElementKind.SliderThumb, PseudoElementKind.SliderTrack, PseudoElementKind.SliderFill];

        void SameAsSnapshot()
        {
            DocumentStyles snap = live.ToSnapshot();
            foreach (Node n in new[] { a, b })
            {
                foreach (PseudoElementKind k in kinds)
                {
                    ComputedStyle? x = live.GetPseudoStyle(n, k);
                    ComputedStyle? y = snap.GetPseudoStyle(n, k);
                    Assert.Equal(y is null, x is null);
                    if (x is not null)
                    {
                        Assert.Equal(y!.GetPropertyValue("outline-width"), x.GetPropertyValue("outline-width"));
                        Assert.Equal(y.GetPropertyValue("outline-style"), x.GetPropertyValue("outline-style"));
                    }
                }
            }
        }

        SameAsSnapshot();
        live.ApplyState(ElementStateChange.Hover(a));
        Assert.Equal("1px", live.GetPseudoStyle(a, PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-width"));
        SameAsSnapshot();
        live.ApplyState(ElementStateChange.Hover(default));
        Assert.NotEqual("1px", live.GetPseudoStyle(a, PseudoElementKind.SliderThumb)!.GetPropertyValue("outline-width"));
        live.ApplyState(ElementStateChange.Focus(a));
        Assert.Equal("3px", live.GetPseudoStyle(a, PseudoElementKind.SliderFill)!.GetPropertyValue("outline-width"));
        SameAsSnapshot();
        live.ApplyState(ElementStateChange.Focus(default));
        Assert.Null(live.GetPseudoStyle(a, PseudoElementKind.SliderFill));
        live.ApplyFormState([FormStateChange.Disabled(b, false)]);
        Assert.NotEqual("4px", live.GetPseudoStyle(b, PseudoElementKind.SliderTrack)!.GetPropertyValue("outline-width"));
        live.ApplyFormState([FormStateChange.Disabled(a, true)]);
        Assert.Equal("4px", live.GetPseudoStyle(a, PseudoElementKind.SliderTrack)!.GetPropertyValue("outline-width"));
        SameAsSnapshot();
    }

    [Fact]
    public void A_cached_part_follows_a_change_of_the_inputs_own_style_even_without_a_rule_that_reads_state()
    {
        Document doc = Document.Parse("<style>input:hover{color:red;font-size:30px} input::slider-thumb{outline-width:1px}</style><input id=a type=range>");
        LiveDocumentStyles live = LiveDocumentStyles.Create(DocumentStyles.Create(doc));
        Node a = doc.GetElementById("a");
        Assert.False(live.HasInteractivePseudoElementRules);
        Assert.NotEqual("red", live.GetPseudoStyle(a, PseudoElementKind.SliderThumb)!.GetPropertyValue("color"));
        live.ApplyState(ElementStateChange.Hover(a));
        Assert.Equal("red", live.GetPseudoStyle(a, PseudoElementKind.SliderThumb)!.GetPropertyValue("color"));
        Assert.Equal(30, live.GetPseudoStyle(a, PseudoElementKind.SliderTrack)!.FontSizePx);
        live.ApplyState(ElementStateChange.Hover(default));
        Assert.NotEqual("red", live.GetPseudoStyle(a, PseudoElementKind.SliderThumb)!.GetPropertyValue("color"));
    }

    [Fact]
    public void Live_state_changes_that_reach_the_parts_allocate_nothing_in_steady_state()
    {
        Document doc = Document.Parse(StateRules + "<p id=p>text</p>");
        LiveDocumentStyles live = LiveDocumentStyles.Create(DocumentStyles.Create(doc));
        Node a = doc.GetElementById("a");
        Node b = doc.GetElementById("b");
        ElementStateChange[] states = [ElementStateChange.Hover(a), ElementStateChange.Focus(a), ElementStateChange.Hover(default), ElementStateChange.Focus(b), ElementStateChange.Focus(default)];
        FormStateChange[][] forms = [[FormStateChange.Disabled(a, true)], [FormStateChange.Disabled(a, false), FormStateChange.Disabled(b, false)], [FormStateChange.Disabled(b, true)]];
        long sink = 0;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        long Pass()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; i++)
            {
                live.ApplyState(states[i % states.Length]);
                live.ApplyFormState(forms[i % forms.Length]);
                sink += live.ChangedElementCount + 1;
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Pass();
        Pass();
        long bytes = Pass();
        Assert.True(sink > 0);
        Assert.Equal(0, bytes);
        // The parts are still readable and current afterwards.
        Assert.NotNull(live.GetPseudoStyle(b, PseudoElementKind.SliderTrack));
    }
}
