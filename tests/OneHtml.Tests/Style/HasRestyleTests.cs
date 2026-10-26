using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// Restyle after tree-shape edits under <c>:has()</c> rules (Selectors 4 section 4.5): descendant, child and sibling relative
/// selectors, <c>:has()</c> inside <c>:is()</c>/<c>:not()</c>, and rules that read another element's <c>:has()</c> result
/// through a combinator. The incremental result must equal a fresh computation, and rules with the <c>:has()</c> in the
/// subject compound must not force a full recompute.
/// </summary>
public class HasRestyleTests
{
    // Each rule sets a property of its own so that a missed match shows up as a difference.
    private static readonly string[] Bounded =
    [
        ".a:has(.b){color:red}",
        "div:has(> .b){background-color:blue}",
        ".a:has(+ .b){margin-top:1px}",
        ".a:has(~ .c){margin-left:2px}",
        "div:has(.b + .c){padding-top:3px}",
        ":is(div,section):has(.c){padding-left:4px}",
        ".a:not(:has(.b)){opacity:.5}",
        ":is(.a:has(.b), .c){font-size:20px}",
        "ul:has(li:nth-child(2)){margin-bottom:5px}",
        ".a:has(> .b ~ .c){margin-right:6px}",
        ".a:has(+ .b .c){padding-bottom:7px}",
        ".a:has(~ div > .b){border-top-width:8px}",
        "section:has(:empty){border-left-width:9px}",
        "li:has(+ li:last-child){letter-spacing:1px}",
        ".c:is(:has(span), .b){word-spacing:2px}",
    ];

    // A :has() before a combinator, or inside :nth-child(of S): the result is read by other elements.
    private static readonly string[] Unbounded =
    [
        ".a:has(.b) + .c{outline-width:1px}",
        ".a:has(.b) .c{text-indent:3px}",
        ".a:has(~ .b) span{line-height:2}",
        ":is(:has(.b) .c){border-bottom-width:4px}",
        "li:nth-child(2 of :has(.b)){border-right-width:5px}",
    ];

    private static readonly string[] Tags = ["div", "ul", "li", "section", "p", "span", "a", "div"];
    private static readonly string[] Classes = ["", "a", "b", "c", "a b", "b c"];

    private static void Build(Random r, StringBuilder sb, int depth)
    {
        string tag = Tags[r.Next(Tags.Length)];
        string cls = Classes[r.Next(Classes.Length)];
        sb.Append('<').Append(tag);
        if (cls.Length > 0)
        {
            sb.Append(" class=\"").Append(cls).Append('"');
        }

        sb.Append('>');
        if (depth > 0)
        {
            for (int i = 0, n = r.Next(4); i < n; i++)
            {
                if (r.Next(6) == 0)
                {
                    sb.Append('t');
                }
                else
                {
                    Build(r, sb, depth - 1);
                }
            }
        }

        sb.Append("</").Append(tag).Append('>');
    }

    private static readonly string[] Fragments =
    [
        "<p class=b>n</p>", "<span class=c></span>", "<div class=\"a\"><b class=b>x</b></div>", "text", "<li class=b>q</li>",
        "<section><a class=c>l</a></section>", "<ul><li>1</li><li class=c>2</li></ul>", "<i></i>", "<div class=c></div>",
    ];

    private static string Page(Random r, string[] pool)
    {
        var sb = new StringBuilder("<!DOCTYPE html><html><head><style>");
        for (int i = 0, n = 2 + r.Next(5); i < n; i++)
        {
            sb.Append(pool[r.Next(pool.Length)]).Append('\n');
        }

        sb.Append("</style></head><body>");
        for (int i = 0, n = 1 + r.Next(3); i < n; i++)
        {
            Build(r, sb, 4);
        }

        return sb.Append("</body></html>").ToString();
    }

    private static List<Node> BodyNodes(Document d)
    {
        var list = new List<Node>();
        Node body = d.Root.QuerySelector("body");
        var stack = new Stack<Node>();
        for (Node c = body.LastChild; !c.IsNull; c = c.PreviousSibling)
        {
            stack.Push(c);
        }

        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            list.Add(n);
            for (Node c = n.LastChild; !c.IsNull; c = c.PreviousSibling)
            {
                stack.Push(c);
            }
        }

        return list;
    }

    private static Edit[] RandomEdits(Random r, Document d)
    {
        List<Node> nodes = BodyNodes(d);
        var edits = new List<Edit>();
        var used = new HashSet<int>();
        for (int e = 0, count = 1 + r.Next(3); e < count && nodes.Count > 0; e++)
        {
            Node n = nodes[r.Next(nodes.Count)];
            if (!used.Add(n.Index))
            {
                continue;
            }

            string frag = Fragments[r.Next(Fragments.Length)];
            if (n.Kind == NodeKind.Text)
            {
                edits.Add(r.Next(3) switch { 0 => Edit.ReplaceText(n, "z"), 1 => Edit.InsertAfter(n, frag), _ => Edit.Remove(n) });
                continue;
            }

            if (!n.IsElement)
            {
                continue;
            }

            edits.Add(r.Next(9) switch
            {
                0 => Edit.Remove(n),
                1 => Edit.Remove(n),
                2 => Edit.InsertBefore(n, frag),
                3 => Edit.InsertAfter(n, frag),
                4 => Edit.Append(n, frag),
                5 => Edit.Prepend(n, frag),
                6 => Edit.SetAttribute(n, "class", Classes[r.Next(Classes.Length)]),
                7 => Edit.Unwrap(n),
                _ => Edit.Replace(n, frag),
            });
        }

        return [.. edits];
    }

    private static void AssertSameAsFresh(RestyleResult res, string tag)
    {
        DocumentStyles fresh = DocumentStyles.Create(res.Styles.Document);
        foreach (Node e in fresh.Document.Root.DescendantElements)
        {
            string? diff = res.Styles.GetStyle(e).FirstDifference(fresh.GetStyle(e));
            Assert.True(diff is null, $"{tag}: <{e.LocalName}> #{e.Index.ToString(CultureInfo.InvariantCulture)} differs in {diff}: {res.Styles.Document.Source.Text}");
        }
    }

    private static (int Steps, int Full) Run(string[] pool, int seeds, int stepsPerSeed, int seedBase)
    {
        int steps = 0, full = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int seed = 0; seed < seeds; seed++)
        {
            var r = new Random(seedBase + seed);
            Document doc = Document.Parse(Page(r, pool));
            DocumentStyles styles = DocumentStyles.Create(doc);
            styles.ComputeAll();
            for (int s = 0; s < stepsPerSeed; s++)
            {
                Edit[] edits = RandomEdits(r, doc);
                if (edits.Length == 0)
                {
                    continue;
                }

                EditResult er = doc.Apply(edits);
                if (!er.Succeeded)
                {
                    continue;
                }

                RestyleResult res = styles.Restyle(er);
                steps++;
                full += res.FullRecompute ? 1 : 0;
                AssertSameAsFresh(res, $"seed {seed} step {s}");
                doc = er.Document!;
                styles = res.Styles;
            }
        }

        Console.WriteLine($"  {clock.Elapsed.TotalMilliseconds / Math.Max(1, steps):F3} ms per step including the fresh comparison");
        return (steps, full);
    }

    private static int Seeds(int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable("ONEHTML_STYLE_SEEDS"), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : fallback;

    [Fact]
    public void Subject_has_rules_restyle_incrementally_and_equal_a_fresh_computation()
    {
        (int steps, int full) = Run(Bounded, Seeds(300), 6, 1000);
        Console.WriteLine($"has subject rules: {steps} steps, {full} full recomputes");
        Assert.True(steps > 400, steps.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(0, full);
    }

    [Fact]
    public void Has_rules_read_through_a_combinator_equal_a_fresh_computation()
    {
        (int steps, int full) = Run([.. Bounded, .. Unbounded], Seeds(300), 6, 5000);
        Console.WriteLine($"has mixed rules: {steps} steps, {full} full recomputes");
        Assert.True(steps > 400, steps.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(0, full);
    }

    [Fact]
    public void Unbounded_rules_only_equal_a_fresh_computation()
    {
        (int steps, _) = Run(Unbounded, Seeds(200), 6, 9000);
        Assert.True(steps > 200, steps.ToString(CultureInfo.InvariantCulture));
    }

    private static void One(string html, Func<Document, Edit[]> edits, string expectedColorOf, string expected)
    {
        Document doc = Document.Parse(html);
        DocumentStyles before = DocumentStyles.Create(doc);
        before.ComputeAll();
        EditResult er = doc.Apply(edits(doc));
        Assert.True(er.Succeeded);
        RestyleResult res = before.Restyle(er);
        Assert.False(res.FullRecompute);
        Assert.Equal(expected, res.Styles.GetStyle(res.Styles.Document.Root.QuerySelector(expectedColorOf)).GetPropertyValue("color"));
        AssertSameAsFresh(res, "case");
    }

    [Fact]
    public void A_deep_insert_restyles_the_ancestor_that_now_has_a_match()
    {
        One("<style>#x:has(.k){color:red}</style><div id=x><p><span><i id=z></i></span></p></div>",
            d => [Edit.InsertAfter(d.Root.QuerySelector("#z"), "<b class=k></b>")], "#x", "red");
    }

    [Fact]
    public void A_removal_restyles_the_ancestor_that_lost_its_match()
    {
        One("<style>#x:has(.k){color:red}</style><div id=x><p><span><i class=k id=z></i></span></p></div>",
            d => [Edit.Remove(d.Root.QuerySelector("#z"))], "#x", "CanvasText");
    }

    [Fact]
    public void A_sibling_relative_selector_reaches_the_earlier_sibling_of_an_ancestor()
    {
        // #x:has(+ .p .k): the .k inside the next sibling.
        One("<style>#x:has(+ .p .k){color:red}</style><div><b id=x></b><p class=p><span id=z></span></p></div>",
            d => [Edit.Append(d.Root.QuerySelector("#z"), "<i class=k></i>")], "#x", "red");
    }

    [Fact]
    public void A_has_in_front_of_a_combinator_restyles_what_follows_it()
    {
        One("<style>#x:has(.k) ~ .t{color:red}</style><div><b id=x><i id=z></i></b><u class=t></u></div>",
            d => [Edit.Append(d.Root.QuerySelector("#z"), "<i class=k></i>")], ".t", "red");
    }

    [Fact]
    public void Edits_on_a_large_page_with_a_has_rule_stay_incremental()
    {
        var sb = new StringBuilder("<style>.a:has(.b){color:red} li:has(+ li.b){margin-top:1px}</style><body>");
        for (int i = 0; i < 600; i++)
        {
            sb.Append("<div class=\"w\"><ul>");
            for (int j = 0; j < 4; j++)
            {
                sb.Append("<li class=\"").Append(j == 2 ? "b" : "a").Append("\"><p>x</p></li>");
            }

            sb.Append("</ul></div>");
        }

        Document doc = Document.Parse(sb.ToString());
        DocumentStyles styles = DocumentStyles.Create(doc);
        styles.ComputeAll();
        var clock = new System.Diagnostics.Stopwatch();
        int full = 0, restyled = 0;
        const int Steps = 40;
        for (int s = 0; s < Steps; s++)
        {
            List<Node> uls = doc.Root.QuerySelectorAll("ul").ToList();
            Node target = uls[(s * 37) % uls.Count];
            EditResult er = doc.Apply([s % 2 == 0 ? Edit.Append(target, "<li class=b><p>n</p></li>") : Edit.Remove(target.FirstChild)]);
            Assert.True(er.Succeeded);
            clock.Start();
            RestyleResult res = styles.Restyle(er);
            res.Styles.ComputeAll();
            clock.Stop();
            full += res.FullRecompute ? 1 : 0;
            restyled += res.RestyledElements.Count;
            doc = er.Document!;
            styles = res.Styles;
        }

        Console.WriteLine($"large page: {clock.Elapsed.TotalMilliseconds / Steps:F2} ms per restyle step, every style read after it, {full} full of {Steps}, {restyled / Steps} elements restyled per step");
        AssertSameAsFresh(styles.Restyle(doc.Apply([Edit.Append(doc.Root.QuerySelector("ul"), "<li class=b></li>")])), "final");
        Assert.Equal(0, full);
    }
}
