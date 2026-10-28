using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Selectors;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// Generated-box styles on the live path survive state changes that do not reach them, and a change followed by reads of the boxes it
/// reached allocates nothing. Equality with a fresh computation under the same context is checked over random state sequences.
/// </summary>
public class LiveGeneratedBoxTests
{
    private const string Rules =
        "input:hover::-webkit-slider-thumb{background-color:red;outline-width:2px}"
        + " input:active::slider-track{outline-width:3px}"
        + " input:focus::placeholder{color:green}"
        + " input:disabled::-moz-range-progress{color:blue}"
        + " input:hover{color:purple;font-size:22px}"
        + " div:hover p::selection{color:orange}"
        + " p:hover::before{content:'h'}"
        + " div:has(input:checked) p::after{content:'c'}"
        + " input:focus + p::before{content:'f';color:red}"
        + " input::slider-thumb{box-shadow:0 0 1px red}"
        + " p::selection{background-color:yellow}"
        + " p::before{color:teal}"
        + " input::placeholder{opacity:.5}"
        + " li:hover::marker{color:red}";

    private const string Body =
        "<div id=d1><input id=r1 type=range><p id=p1>a <b id=b1>b</b></p><input id=t1 placeholder=x><span id=s1>s</span></div>"
        + "<div id=d2><div id=d3><input id=r2 type=range disabled><p id=p2>c</p></div><input id=c1 type=checkbox><p id=p3>d</p></div>"
        + "<ul><li id=l1>x</li></ul><p id=p4>e</p>";

    private static readonly string[] Ids = ["d1", "r1", "p1", "b1", "t1", "s1", "d2", "d3", "r2", "p2", "c1", "p3", "l1", "p4"];

    private static readonly string[] ControlIds = ["r1", "t1", "r2", "c1"];

    private static readonly PseudoElementKind[] Kinds = Enum.GetValues<PseudoElementKind>();

    private static readonly string[] Props = ["color", "background-color", "content", "font-size", "display", "outline-width", "opacity", "box-shadow", "box-sizing", "flex-grow"];

    [Fact]
    public void A_state_change_and_the_reads_of_the_boxes_it_reached_allocate_nothing_in_steady_state()
    {
        Document doc = Document.Parse("<style>" + Rules + "</style>" + Body);
        LiveDocumentStyles live = LiveDocumentStyles.Create(DocumentStyles.Create(doc));
        Node r1 = doc.GetElementById("r1");
        Node t1 = doc.GetElementById("t1");
        Node p1 = doc.GetElementById("p1");
        Node b1 = doc.GetElementById("b1");
        Node r2 = doc.GetElementById("r2");
        Node[] read = [r1, t1, p1, b1, r2, doc.GetElementById("p3")];
        ElementStateChange[] changes =
        [
            ElementStateChange.Hover(r1), ElementStateChange.Hover(default), ElementStateChange.Focus(t1), ElementStateChange.Focus(default),
            ElementStateChange.Hover(b1), ElementStateChange.Active(r1), ElementStateChange.Active(default), ElementStateChange.Hover(default),
        ];
        long sink = 0;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        long Pass()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 40; i++)
            {
                live.ApplyState(changes[i % changes.Length]);
                foreach (Node n in read)
                {
                    for (int k = 0; k < Kinds.Length; k++)
                    {
                        sink += live.GetPseudoStyle(n, Kinds[k])?.Id ?? 1;
                    }
                }
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Pass();
        Pass();
        Assert.Equal(0, Pass());
        Assert.True(sink > 0);
    }

    [Fact]
    public void Form_changes_and_the_reads_after_them_allocate_nothing_in_steady_state()
    {
        Document doc = Document.Parse("<style>" + Rules + "</style>" + Body);
        LiveDocumentStyles live = LiveDocumentStyles.Create(DocumentStyles.Create(doc));
        Node r2 = doc.GetElementById("r2");
        Node c1 = doc.GetElementById("c1");
        Node p2 = doc.GetElementById("p2");
        Node p3 = doc.GetElementById("p3");
        FormStateChange[][] changes = [[FormStateChange.Disabled(r2, false)], [FormStateChange.Checked(c1, true)], [FormStateChange.Disabled(r2, true)], [FormStateChange.Checked(c1, false)]];
        long sink = 0;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        long Pass()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 40; i++)
            {
                live.ApplyFormState(changes[i % changes.Length]);
                sink += live.GetPseudoStyle(r2, PseudoElementKind.SliderFill)?.Id ?? 1;
                sink += live.GetPseudoStyle(r2, PseudoElementKind.SliderThumb)?.Id ?? 1;
                sink += live.GetPseudoStyle(p3, PseudoElementKind.After)?.Id ?? 1;
                sink += live.GetPseudoStyle(p2, PseudoElementKind.After)?.Id ?? 1;
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Pass();
        Pass();
        Assert.Equal(0, Pass());
        Assert.True(sink > 0);
    }

    [Fact]
    public void Without_state_rules_for_boxes_a_change_allocates_nothing_either()
    {
        Document doc = Document.Parse("<style>input::slider-thumb{color:red} p::before{content:'x'} input:hover{color:blue} p::selection{color:red}</style>" + Body);
        LiveDocumentStyles live = LiveDocumentStyles.Create(DocumentStyles.Create(doc));
        Node r1 = doc.GetElementById("r1");
        Node p1 = doc.GetElementById("p1");
        long sink = 0;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        long Pass()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 40; i++)
            {
                live.ApplyState(ElementStateChange.Hover(i % 2 == 0 ? r1 : default));
                sink += live.GetPseudoStyle(r1, PseudoElementKind.SliderThumb)!.Id + live.GetPseudoStyle(p1, PseudoElementKind.Before)!.Id + live.GetPseudoStyle(p1, PseudoElementKind.Selection)!.Id;
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Pass();
        Pass();
        Assert.Equal(0, Pass());
        Assert.True(sink > 0);
    }

    [Fact]
    public void A_cached_box_of_an_element_the_change_does_not_reach_is_kept_and_the_inherited_ones_follow()
    {
        Document doc = Document.Parse("<style>input:hover{color:purple} input::slider-track{outline-width:1px} p::before{content:'x'}</style>" + Body);
        LiveDocumentStyles live = LiveDocumentStyles.Create(DocumentStyles.Create(doc));
        Node r1 = doc.GetElementById("r1");
        Node p1 = doc.GetElementById("p1");
        ComputedStyle? farBefore = live.GetPseudoStyle(p1, PseudoElementKind.Before);
        Assert.Same(farBefore, live.GetPseudoStyle(p1, PseudoElementKind.Before));
        Assert.NotEqual("purple", live.GetPseudoStyle(r1, PseudoElementKind.SliderThumb)!.GetPropertyValue("color"));
        live.ApplyState(ElementStateChange.Hover(r1));
        Assert.Same(farBefore, live.GetPseudoStyle(p1, PseudoElementKind.Before));
        Assert.Equal("purple", live.GetPseudoStyle(r1, PseudoElementKind.SliderThumb)!.GetPropertyValue("color"));
        live.ApplyState(ElementStateChange.Hover(default));
        Assert.NotEqual("purple", live.GetPseudoStyle(r1, PseudoElementKind.SliderThumb)!.GetPropertyValue("color"));
    }

    [Fact]
    public void Random_state_sequences_give_the_boxes_a_fresh_computation_gives()
    {
        int steps = 0, comparisons = 0;
        for (int seed = 0; seed < 24; seed++)
        {
            var rnd = new Random(seed * 7919 + 1);
            Document doc = Document.Parse("<style>" + Rules + (seed % 3 == 0 ? " div:focus-within p::before{content:'w'}" : "") + (seed % 4 == 0 ? " .none::before{content:'n'}" : "") + "</style>" + Body);
            LiveDocumentStyles live = LiveDocumentStyles.Create(DocumentStyles.Create(doc));
            Node[] nodes = [.. Ids.Select(doc.GetElementById)];
            Node[] controls = [.. ControlIds.Select(doc.GetElementById)];
            Node hover = default, active = default, focus = default, target = default;
            FocusSource source = FocusSource.Keyboard;
            FormStateOverrides form = FormStateOverrides.Empty;

            Node Pick(Node[] from) => rnd.Next(4) == 0 ? default : from[rnd.Next(from.Length)];

            for (int step = 0; step < 50; step++)
            {
                // Read some boxes first so that they are cached from before the change.
                for (int i = 0; i < 10; i++)
                {
                    live.GetPseudoStyle(nodes[rnd.Next(nodes.Length)], Kinds[rnd.Next(Kinds.Length)]);
                }

                switch (rnd.Next(7))
                {
                    case 0: hover = Pick(nodes); live.ApplyState(ElementStateChange.Hover(hover)); break;
                    case 1: active = Pick(nodes); live.ApplyState(ElementStateChange.Active(active)); break;
                    case 2:
                        focus = Pick(controls);
                        source = rnd.Next(2) == 0 ? FocusSource.Keyboard : FocusSource.PointingDevice;
                        live.ApplyState(ElementStateChange.Focus(focus, source));
                        if (focus.IsNull)
                        {
                            source = FocusSource.Keyboard;
                        }

                        break;
                    case 3: target = Pick(nodes); live.ApplyState(ElementStateChange.Target(target)); break;
                    case 4:
                    {
                        Node n = rnd.Next(2) == 0 ? doc.GetElementById("r2") : doc.GetElementById("r1");
                        bool v = rnd.Next(2) == 0;
                        FormStateChange c = FormStateChange.Disabled(n, v);
                        form = form.With(c);
                        live.ApplyFormState([c]);
                        break;
                    }

                    case 5:
                    {
                        Node n = doc.GetElementById("c1");
                        bool v = rnd.Next(2) == 0;
                        FormStateChange c = FormStateChange.Checked(n, v);
                        form = form.With(c);
                        live.ApplyFormState([c]);
                        break;
                    }

                    default:
                        hover = Pick(nodes);
                        focus = Pick(controls);
                        source = rnd.Next(2) == 0 ? FocusSource.Keyboard : FocusSource.PointingDevice;
                        live.ApplyState(ElementStateChange.Hover(hover).WithFocus(focus, source));
                        if (focus.IsNull)
                        {
                            source = FocusSource.Keyboard;
                        }

                        break;
                }

                steps++;
                StyleContext ctx = StyleContext.Default with { Hover = hover, Active = active, Focus = focus, FocusSource = source, Target = target };
                DocumentStyles fresh = DocumentStyles.Create(doc, ctx.WithFormState(form));
                var order = new List<(Node, PseudoElementKind)>();
                foreach (Node n in nodes)
                {
                    foreach (PseudoElementKind k in Kinds)
                    {
                        order.Add((n, k));
                    }
                }

                for (int i = order.Count - 1; i > 0; i--)
                {
                    int j = rnd.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }

                foreach ((Node n, PseudoElementKind k) in order)
                {
                    ComputedStyle? a = live.GetPseudoStyle(n, k);
                    ComputedStyle? b = fresh.GetPseudoStyle(n, k);
                    comparisons++;
                    Assert.True(a is null == b is null, $"seed {seed} step {step}: {n.GetAttribute("id")} {k}: existence differs");
                    if (a is null)
                    {
                        continue;
                    }

                    foreach (string prop in Props)
                    {
                        string x = a.GetPropertyValue(prop);
                        string y = b!.GetPropertyValue(prop);
                        Assert.True(x == y, $"seed {seed} step {step}: {n.GetAttribute("id")} {k} {prop}: live '{x}' fresh '{y}'");
                    }
                }
            }
        }

        Assert.True(steps >= 1200 && comparisons >= 1200 * Ids.Length * Kinds.Length);
    }
}
