using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>A page with state rules of every reach (hover, active, focus, focus-within, :has(), sibling and descendant combinators, inherited changes, a label) and a scripted sequence of state changes.</summary>
internal static class LiveFixture
{
    public const string Css = """
        :root{--k:1px}
        body{font:16px/1.2 sans-serif;color:#222}
        nav ul{list-style:none;margin:0;padding:0}
        nav li{position:relative;color:#333}
        nav li > ul{display:none}
        nav li:hover > ul{display:block}
        nav li:hover > a{color:red;background:yellow}
        nav li:focus-within > ul{display:block}
        nav a:focus-visible{outline:2px solid blue}
        nav a:focus{color:green}
        li:has(> a:hover){font-weight:bold}
        li:has(:focus-visible){border-left:3px solid orange}
        a:hover + span{opacity:.5}
        a:hover ~ em{color:navy}
        a:active{color:purple}
        button:hover{font-size:20px}
        .card:hover{--k:3px;font-size:18px}
        .card:hover .title{color:teal;margin:var(--k)}
        .card:not(:hover) .badge{display:none}
        li:is(:hover, :focus-within) > a{text-decoration:underline}
        label:hover + input{outline:1px solid red}
        form:has(input:focus) label{color:maroon}
        :target{background:pink}
        .card:hover::before{content:"hover"}
        """;

    public static string Page(bool quirks, int menus = 12, int items = 5, int cards = 12)
    {
        var r = new Random(53);
        var sb = new StringBuilder((quirks ? "" : "<!DOCTYPE html>") + "<html><head><style>").Append(Css).Append("</style></head><body><nav><ul>");
        for (int m = 0; m < menus; m++)
        {
            sb.Append("<li class=\"top t").Append(m % 3).Append("\"><a href=\"#m").Append(m).Append("\" id=\"m").Append(m).Append("\">Menu ").Append(m).Append("</a><span>x</span><em>y</em><ul>");
            for (int i = 0; i < items; i++)
            {
                sb.Append("<li><a href=\"#i").Append(m).Append('_').Append(i).Append("\" id=\"i").Append(m).Append('_').Append(i).Append("\">Item</a>");
                if (r.Next(3) == 0)
                {
                    sb.Append("<ul><li><a href=\"#\">Deep</a></li><li><a href=\"#\" style=\"color:#123\">Deeper</a></li></ul>");
                }

                sb.Append("</li>");
            }

            sb.Append("</ul></li>");
        }

        sb.Append("</ul></nav><main>");
        for (int c = 0; c < cards; c++)
        {
            sb.Append("<div class=\"card\" id=\"c").Append(c).Append("\"><h3 class=\"title\">Card ").Append(c).Append("</h3><p>Text <b>bold</b> <a href=\"#c").Append(c).Append("\">link</a></p><span class=\"badge\">new</span><button>Go <i>now</i></button></div>");
        }

        sb.Append("<form><label for=\"f1\">Name</label><input id=\"f1\"><label>Other <input id=\"f2\" type=\"checkbox\"></label><textarea></textarea></form>");
        sb.Append("<table bgcolor=\"#eeeeee\"><tr><td bgcolor=\"#ff0000\">a</td><td>b</td></tr></table></main></body></html>");
        return sb.ToString();
    }

    /// <summary>A reproducible sequence of changes over the page's elements: hover moves along paths and out, focus by keyboard and pointer, active on and off, <c>:target</c>.</summary>
    public static List<ElementStateChange> Script(Document doc, int count, int seed)
    {
        var r = new Random(seed);
        List<Node> all = [.. doc.Root.DescendantElements];
        List<Node> focusable = [.. all.Where(e => e.LocalName is "a" or "button" or "input" or "textarea")];
        var script = new List<ElementStateChange>(count);
        Node at = all[r.Next(all.Count)];
        for (int i = 0; i < count; i++)
        {
            int kind = r.Next(10);
            if (kind < 5)
            {
                // Walk the pointer: to a neighbour (parent, child or sibling) or somewhere new, or out of the page.
                int pick = r.Next(6);
                Node next = pick switch
                {
                    0 => at.ParentElement,
                    1 => at.FirstElementChild,
                    2 => at.NextElementSibling,
                    3 => at.PreviousElementSibling,
                    4 => default,
                    _ => all[r.Next(all.Count)],
                };
                at = next.IsNull ? all[r.Next(all.Count)] : next;
                script.Add(ElementStateChange.Hover(pick == 4 ? default : at));
            }
            else if (kind < 7)
            {
                script.Add(ElementStateChange.Focus(r.Next(4) == 0 ? default : focusable[r.Next(focusable.Count)], r.Next(2) == 0 ? FocusSource.Keyboard : FocusSource.PointingDevice));
            }
            else if (kind < 9)
            {
                script.Add(ElementStateChange.Active(r.Next(3) == 0 ? default : at));
            }
            else
            {
                script.Add(ElementStateChange.Hover(at).WithActive(at).WithFocus(focusable[r.Next(focusable.Count)]).WithTarget(r.Next(2) == 0 ? default : all[r.Next(all.Count)]));
            }
        }

        return script;
    }
}

public class LiveDocumentStylesTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void Live_styles_equal_a_fresh_computation_after_every_change(int seed, bool quirks)
    {
        Document doc = Document.Parse(LiveFixture.Page(quirks));
        DocumentStyles start = DocumentStyles.Create(doc);
        LiveDocumentStyles live = LiveDocumentStyles.Create(start);
        List<ElementStateChange> script = LiveFixture.Script(doc, 120, seed);
        List<Node> elements = [.. doc.Root.DescendantElements];
        DocumentStyles previous = start;
        StyleContext ctx = StyleContext.Default;
        int mismatches = 0;
        int changedTotal = 0;
        foreach (ElementStateChange change in script)
        {
            live.ApplyState(change);
            ctx = live.GetContext();
            DocumentStyles fresh = DocumentStyles.Create(doc, ctx);
            DocumentStyles overlay = start.WithContext(ctx);
            var changed = new HashSet<int>(live.ChangedElementIndices.ToArray());
            Assert.Equal(changed.Count, live.ChangedElementCount);
            Assert.True(live.ChangedElementIndices.ToArray().SequenceEqual(live.ChangedElementIndices.ToArray().Order()));
            foreach (Node e in elements)
            {
                int i = e.Index;
                string? diff = live.GetStyle(i).FirstDifference(fresh.GetStyle(e));
                if (diff is not null)
                {
                    mismatches++;
                }

                Assert.Null(diff);
                Assert.Null(live.GetStyle(e).FirstDifference(overlay.GetStyle(e)));
                if (previous.GetStyle(e).FirstDifference(fresh.GetStyle(e)) is not null)
                {
                    Assert.Contains(i, changed);
                }
            }

            changedTotal += changed.Count;
            previous = fresh;
        }

        Assert.Equal(0, mismatches);
        Assert.True(changedTotal > 100);
    }

    [Fact]
    public void Hover_through_a_label_reaches_its_control_and_snapshots_stay_unchanged()
    {
        Document doc = Document.Parse(LiveFixture.Page(false));
        DocumentStyles start = DocumentStyles.Create(doc);
        LiveDocumentStyles live = LiveDocumentStyles.Create(start);
        Node label = doc.Root.QuerySelector("label");
        Node input = doc.GetElementById("f1");
        string before = start.GetStyle(input).GetPropertyValue("outline-style");
        live.ApplyState(ElementStateChange.Hover(label));
        Assert.Contains(input.Index, live.ChangedElementIndices.ToArray());
        Assert.NotEqual(before, live.GetStyle(input).GetPropertyValue("outline-style"));
        Assert.Equal(before, start.GetStyle(input).GetPropertyValue("outline-style"));
        DocumentStyles snap = live.ToSnapshot();
        live.ApplyState(ElementStateChange.Hover(default));
        Assert.Equal(before, live.GetStyle(input).GetPropertyValue("outline-style"));
        Assert.NotEqual(before, snap.GetStyle(input).GetPropertyValue("outline-style"));
    }

    [Fact]
    public void A_small_buffer_receives_the_lowest_indices()
    {
        Document doc = Document.Parse(LiveFixture.Page(false));
        LiveDocumentStyles live = LiveDocumentStyles.Create(DocumentStyles.Create(doc));
        live.ApplyState(ElementStateChange.Hover(doc.GetElementById("c1")));
        int total = live.ChangedElementCount;
        Assert.True(total > 2);
        Span<int> two = stackalloc int[2];
        Assert.Equal(2, live.ChangedElementsInto(two));
        Assert.Equal(live.ChangedElementIndices[0], two[0]);
        Assert.Equal(live.ChangedElementIndices[1], two[1]);
        Span<int> big = stackalloc int[total + 5];
        Assert.Equal(total, live.ChangedElementsInto(big));
        Assert.Equal(0, live.ChangedElementsInto(default));
    }

    [Fact]
    public void A_node_of_another_document_is_refused()
    {
        Document doc = Document.Parse(LiveFixture.Page(false));
        Document other = Document.Parse(LiveFixture.Page(false));
        LiveDocumentStyles live = LiveDocumentStyles.Create(DocumentStyles.Create(doc));
        Assert.Throws<ArgumentException>(() => live.ApplyState(ElementStateChange.Hover(other.GetElementById("c1"))));
        Assert.Throws<ArgumentOutOfRangeException>(() => live.GetStyle(0));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(5, true)]
    public void State_changes_allocate_nothing_in_steady_state(int seed, bool quirks)
    {
        Document doc = Document.Parse(LiveFixture.Page(quirks));
        DocumentStyles start = DocumentStyles.Create(doc);
        LiveDocumentStyles live = LiveDocumentStyles.Create(start);
        List<ElementStateChange> script = LiveFixture.Script(doc, 1000, seed);
        int color = PropertyTable.IdOf("color");
        int display = PropertyTable.IdOf("display");
        List<int> elements = [.. doc.Root.DescendantElements.Select(e => e.Index)];
        long sink = 0;

        long Pass()
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Span<int> buffer = stackalloc int[2048];
            foreach (ElementStateChange change in script)
            {
                live.ApplyState(change);
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

        // Warm-up: the same script reaches each element and state combination once.
        Pass();
        Pass();
        long bytes = Pass();
        Assert.True(sink != 0);
        Assert.Equal(0, bytes);
    }
}
