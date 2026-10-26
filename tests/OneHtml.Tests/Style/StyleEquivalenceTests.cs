using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// Overlay and restyle results against a full recompute: random pages and edits from a seeded generator, plus the
/// vendored test pages.
/// </summary>
public class StyleEquivalenceTests
{
    // ---- a small page model ----

    private sealed class M
    {
        public int Uid;
        public string? Tag; // null: a text node
        public string Text = "t";
        public List<string> Classes = [];
        public string? Id;
        public string? Style;
        public List<M> Kids = [];
    }

    private sealed class Gen
    {
        private readonly Random _r;
        private int _uid;

        public Gen(int seed) => _r = new Random(seed);

        public Random R => _r;

        public M Element(int depth)
        {
            string[] tags = ["div", "span", "section", "b", "ul", "a", "input", "div", "span"];
            string tag = depth <= 0 ? (_r.Next(3) == 0 ? "a" : "span") : tags[_r.Next(tags.Length)];
            var m = new M { Uid = ++_uid, Tag = tag };
            int nc = _r.Next(3);
            for (int i = 0; i < nc; i++)
            {
                m.Classes.Add("c" + _r.Next(6));
            }

            m.Classes = [.. m.Classes.Distinct()];
            if (_r.Next(6) == 0)
            {
                m.Id = "i" + _r.Next(5);
            }

            if (_r.Next(8) == 0)
            {
                m.Style = "color:" + new[] { "red", "blue", "green" }[_r.Next(3)];
            }

            if (tag == "input")
            {
                return m;
            }

            if (tag == "a")
            {
                m.Kids.Add(Text());
                return m;
            }

            int kids = depth <= 0 ? 0 : _r.Next(4);
            for (int i = 0; i < kids; i++)
            {
                if (tag == "ul")
                {
                    var li = new M { Uid = ++_uid, Tag = "li" };
                    if (_r.Next(2) == 0)
                    {
                        li.Classes.Add("c" + _r.Next(6));
                    }

                    if (_r.Next(2) == 0)
                    {
                        li.Kids.Add(Text());
                    }

                    m.Kids.Add(li);
                }
                else if (_r.Next(4) == 0)
                {
                    m.Kids.Add(Text());
                }
                else
                {
                    m.Kids.Add(Element(depth - 1));
                }
            }

            Normalize(m);
            return m;
        }

        public M Text() => new() { Uid = ++_uid, Text = "x" + _r.Next(9).ToString(CultureInfo.InvariantCulture) };

        public int NextUid() => ++_uid;
    }

    private static void Normalize(M m)
    {
        for (int i = m.Kids.Count - 1; i > 0; i--)
        {
            if (m.Kids[i].Tag is null && m.Kids[i - 1].Tag is null)
            {
                m.Kids[i - 1].Text += m.Kids[i].Text;
                m.Kids.RemoveAt(i);
            }
        }

        foreach (M k in m.Kids)
        {
            Normalize(k);
        }
    }

    private static void Write(M m, StringBuilder sb)
    {
        if (m.Tag is null)
        {
            sb.Append(m.Text);
            return;
        }

        sb.Append('<').Append(m.Tag);
        if (m.Tag == "a")
        {
            sb.Append(" href=/x");
        }

        if (m.Classes.Count > 0)
        {
            sb.Append(" class=\"").Append(string.Join(' ', m.Classes)).Append('"');
        }

        if (m.Id is not null)
        {
            sb.Append(" id=").Append(m.Id);
        }

        if (m.Style is not null)
        {
            sb.Append(" style=\"").Append(m.Style).Append('"');
        }

        sb.Append('>');
        if (m.Tag == "input")
        {
            return;
        }

        foreach (M k in m.Kids)
        {
            Write(k, sb);
        }

        sb.Append("</").Append(m.Tag).Append('>');
    }

    private static string Page(M root, string css)
    {
        var sb = new StringBuilder("<!DOCTYPE html><html><head><style>").Append(css).Append("</style></head><body>");
        foreach (M k in root.Kids)
        {
            Write(k, sb);
        }

        return sb.Append("</body></html>").ToString();
    }

    private static List<M> Flatten(M root)
    {
        var l = new List<M>();
        void Walk(M m)
        {
            foreach (M k in m.Kids)
            {
                l.Add(k);
                Walk(k);
            }
        }

        Walk(root);
        return l;
    }

    private static M Clone(M m) => new()
    {
        Uid = m.Uid,
        Tag = m.Tag,
        Text = m.Text,
        Classes = [.. m.Classes],
        Id = m.Id,
        Style = m.Style,
        Kids = [.. m.Kids.Select(Clone)],
    };

    // Rules: structural and sibling selectors, the dynamic pseudo-classes, :has().
    private static readonly string[] Rules =
    [
        ".c1{color:red}", ".c2 .c3{background-color:blue}", ".c1>.c2{color:green}", ".c1+.c2{background-color:red}",
        ".c2~.c3{color:blue}", "li:nth-child(2n){color:red}", ":first-child{background-color:green}", ":last-child{color:green}",
        "div:empty{background-color:blue}", ".c1:not(.c2){opacity:.5}", "#i1{color:blue}", "div span{color:red}",
        "a:hover{color:red}", "a:focus{background-color:green}", "div:focus-within{background-color:blue}", "input:focus-visible{color:green}",
        "section>b{color:red}", "ul li+li{color:blue}", "span:only-child{color:green}", ".c4{font-size:20px}", ".c5{font-size:2em}",
        ":is(.c0,.c1) .c4{color:red !important}", "@media (min-width:600px){.c3{color:green}}", "@layer x{.c0{color:red}} .c0{color:blue}",
    ];

    private static readonly string[] HasRules =
    [
        ".c1:has(.c2){color:red}", "div:has(+ .c3){background-color:blue}", "ul:has(li:last-child){color:green}", "section:has(> a){color:red}",
        "li:has(:focus){background-color:green}",
    ];

    private static string Sheet(Random r, bool useHas)
    {
        var sb = new StringBuilder();
        string[] pool = useHas ? [.. Rules, .. HasRules] : Rules;
        int n = 4 + r.Next(10);
        for (int i = 0; i < n; i++)
        {
            sb.Append(pool[r.Next(pool.Length)]);
        }

        return sb.ToString();
    }

    private static List<Node> Preorder(Document d)
    {
        var l = new List<Node>();
        var stack = new Stack<Node>();
        stack.Push(d.Root);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            l.Add(n);
            var kids = new List<Node>();
            for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                kids.Add(c);
            }

            for (int i = kids.Count - 1; i >= 0; i--)
            {
                stack.Push(kids[i]);
            }
        }

        return l;
    }

    // The old-to-new node index map of two pages built from models: nodes before the body's content match by position, the
    // model's nodes by identity.
    private static int[] MapNodes(Document oldDoc, M oldRoot, Document newDoc, M newRoot)
    {
        List<Node> po = Preorder(oldDoc);
        List<Node> pn = Preorder(newDoc);
        List<M> mo = Flatten(oldRoot);
        List<M> mn = Flatten(newRoot);
        int prefix = po.Count - mo.Count;
        Assert.Equal(prefix, pn.Count - mn.Count);
        var map = new int[oldDoc.NodeCount];
        Array.Fill(map, -1);
        for (int i = 0; i < prefix; i++)
        {
            map[po[i].Index] = pn[i].Index;
        }

        var byUid = new Dictionary<int, int>();
        for (int i = 0; i < mn.Count; i++)
        {
            byUid[mn[i].Uid] = pn[prefix + i].Index;
        }

        for (int i = 0; i < mo.Count; i++)
        {
            if (byUid.TryGetValue(mo[i].Uid, out int ni))
            {
                map[po[prefix + i].Index] = ni;
            }
        }

        return map;
    }

    private static void AssertEqual(DocumentStyles actual, DocumentStyles expected, string what)
    {
        foreach (Node e in expected.Document.Root.DescendantElements)
        {
            string? diff = actual.GetStyle(e).FirstDifference(expected.GetStyle(e));
            Assert.True(diff is null, what + ": <" + e.LocalName + "> #" + e.Index.ToString(CultureInfo.InvariantCulture) + " differs in " + diff);
        }
    }

    // ---- restyle ----

    private static (int Full, int Incremental, int Elements) RestyleRun(int seed, bool useHas, int iterations)
    {
        int full = 0, incremental = 0, elementsRestyled = 0;
        var g = new Gen(seed);
        Random r = g.R;
        for (int it = 0; it < iterations; it++)
        {
            var root = new M { Uid = 0, Tag = "body" };
            int top = 1 + r.Next(4);
            for (int i = 0; i < top; i++)
            {
                root.Kids.Add(g.Element(3));
            }

            Normalize(root);
            string css = Sheet(r, useHas);
            M next = Clone(root);
            string css2 = css;
            List<ElementChangeSpec> changes = [];
            int edits = 1 + r.Next(3);
            bool sheetChange = false;
            for (int e = 0; e < edits; e++)
            {
                List<M> all = Flatten(next);
                if (all.Count == 0)
                {
                    break;
                }

                M target = all[r.Next(all.Count)];
                switch (r.Next(7))
                {
                    case 0 when target.Tag is not null:
                        target.Classes = [.. target.Classes.Append("c" + r.Next(6)).Distinct()];
                        changes.Add(new ElementChangeSpec(target.Uid, ["class"]));
                        break;
                    case 1 when target.Tag is not null && target.Classes.Count > 0:
                        target.Classes.RemoveAt(r.Next(target.Classes.Count));
                        changes.Add(new ElementChangeSpec(target.Uid, ["class"]));
                        break;
                    case 2 when target.Tag is not null:
                        target.Id = r.Next(2) == 0 ? null : "i" + r.Next(5);
                        changes.Add(new ElementChangeSpec(target.Uid, ["id"]));
                        break;
                    case 3 when target.Tag is not null:
                        target.Style = r.Next(2) == 0 ? null : "background-color:" + new[] { "red", "blue" }[r.Next(2)];
                        changes.Add(new ElementChangeSpec(target.Uid, ["style"]));
                        break;
                    case 4 when target.Tag is not (null or "input" or "a" or "ul"):
                        target.Kids.Insert(r.Next(target.Kids.Count + 1), g.Element(2));
                        break;
                    case 5:
                    {
                        M? parent = FindParent(next, target);
                        if (parent is not null)
                        {
                            parent.Kids.Remove(target);
                        }

                        break;
                    }

                    case 6 when !sheetChange:
                        css2 = Sheet(r, useHas);
                        sheetChange = true;
                        break;
                }

                Normalize(next);
            }

            Document oldDoc = Document.Parse(Page(root, css));
            Document newDoc = Document.Parse(Page(next, css2));
            DocumentStyles oldStyles = DocumentStyles.Create(oldDoc);
            oldStyles.ComputeAll();
            int[] map = MapNodes(oldDoc, root, newDoc, next);
            var byUid = new Dictionary<int, int>();
            List<Node> pn = Preorder(newDoc);
            List<M> mn = Flatten(next);
            int prefix = pn.Count - mn.Count;
            for (int i = 0; i < mn.Count; i++)
            {
                byUid[mn[i].Uid] = pn[prefix + i].Index;
            }

            var elems = new List<ElementChange>();
            foreach (ElementChangeSpec c in changes)
            {
                if (byUid.TryGetValue(c.Uid, out int ni))
                {
                    elems.Add(new ElementChange(ni, c.Attrs));
                }
            }

            var change = new StyleChange { Elements = elems, SheetsChanged = sheetChange, OldToNew = map };
            RestyleResult res = oldStyles.Restyle(newDoc, change);
            if (res.FullRecompute)
            {
                full++;
            }
            else
            {
                incremental++;
                elementsRestyled += res.RestyledElements.Count;
            }

            DocumentStyles fresh = DocumentStyles.Create(newDoc);
            AssertEqual(res.Styles, fresh, $"seed {seed} iteration {it} (full={res.FullRecompute})");
        }

        return (full, incremental, elementsRestyled);
    }

    private readonly record struct ElementChangeSpec(int Uid, string[] Attrs);

    private static M? FindParent(M root, M target)
    {
        foreach (M m in Flatten(root).Prepend(root))
        {
            if (m.Kids.Contains(target))
            {
                return m;
            }
        }

        return null;
    }

    [Fact]
    public void Restyle_after_random_edits_equals_full_recompute()
    {
        var a = RestyleRun(11, useHas: false, iterations: 150);
        var b = RestyleRun(12, useHas: false, iterations: 150);
        // Most edits are handled incrementally and restyle a few elements, not the page.
        Assert.True(a.Incremental + b.Incremental > 5 * (a.Full + b.Full), $"incremental {a.Incremental + b.Incremental}, full {a.Full + b.Full}");
        Assert.True(a.Elements + b.Elements > 0);
    }

    [Fact]
    public void Restyle_with_has_selectors_equals_full_recompute()
    {
        RestyleRun(21, useHas: true, iterations: 100);
    }

    [Fact]
    public void Restyle_touches_only_what_the_change_reaches()
    {
        const string css = "<style>.hot{color:red} p{margin-top:1px}</style>";
        Document a = Document.Parse(css + "<div><p id=a class=hot>x</p><p id=b>y</p><p id=c>z</p></div>");
        Document b = Document.Parse(css + "<div><p id=a>x</p><p id=b>y</p><p id=c>z</p></div>");
        DocumentStyles s = DocumentStyles.Create(a);
        s.ComputeAll();
        RestyleResult res = s.Restyle(b, new StyleChange { Elements = [new ElementChange(a.GetElementById("a").Index, ["class"])] });
        Assert.False(res.FullRecompute);
        Assert.Equal([a.GetElementById("a").Index], res.RestyledElements);
        Assert.Equal("canvastext", res.Styles.GetStyle(b.GetElementById("a")).GetPropertyValue("color").ToLowerInvariant());
    }

    // ---- overlays ----

    private static void OverlayEquivalence(string html, StyleSourceOptions? options, Random r, int hoverSamples, string label)
    {
        Document doc = Document.Parse(html);
        DocumentStyles baseStyles = DocumentStyles.Create(doc, null, options);
        baseStyles.ComputeAll();
        var elements = doc.Root.DescendantElements.ToList();
        var focusable = elements.Where(e => e.LocalName is "button" or "select" or "textarea" or "summary"
            || (e.LocalName is "a" or "area" && e.HasAttribute("href")) || (e.LocalName == "input" && !string.Equals(e.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase))
            || e.HasAttribute("tabindex") || string.Equals(e.GetAttribute("contenteditable") ?? "x", "true", StringComparison.OrdinalIgnoreCase)).ToList();
        var targets = new List<(string Kind, StyleContext Ctx)>();
        foreach (Node f in focusable)
        {
            targets.Add(("focus", StyleContext.Default.WithFocus(f)));
            if (f.LocalName is "input" or "textarea")
            {
                targets.Add(("pointer focus", StyleContext.Default.WithFocus(f, FocusSource.PointingDevice)));
            }
        }

        for (int i = 0; i < hoverSamples && elements.Count > 0; i++)
        {
            Node h = elements[r.Next(elements.Count)];
            targets.Add(("hover", StyleContext.Default.WithHover(h)));
        }

        for (int i = 0; i < Math.Min(3, elements.Count); i++)
        {
            Node t = elements[r.Next(elements.Count)];
            targets.Add(("focus+hover", StyleContext.Default.WithFocus(t).WithHover(elements[r.Next(elements.Count)])));
        }

        foreach ((string kind, StyleContext ctx) in targets)
        {
            DocumentStyles overlay = baseStyles.WithContext(ctx);
            DocumentStyles full = DocumentStyles.Create(doc, ctx, options);
            AssertEqual(overlay, full, label + " " + kind);
        }
    }

    [Fact]
    public void Overlays_on_random_pages_equal_full_recompute()
    {
        var g = new Gen(5);
        for (int it = 0; it < 60; it++)
        {
            var root = new M { Uid = 0, Tag = "body" };
            for (int i = 0; i < 3; i++)
            {
                root.Kids.Add(g.Element(3));
            }

            Normalize(root);
            OverlayEquivalence(Page(root, Sheet(g.R, useHas: it % 2 == 0)), null, g.R, 6, "page " + it.ToString(CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void Overlays_on_written_pages_equal_full_recompute()
    {
        const string page = """
            <style>
            a:hover, button:hover { color: red } a:focus-visible { outline: 2px solid blue }
            li:hover > a { background-color: yellow } label:hover + input { color: green }
            form:focus-within { background-color: #eee } nav:has(a:focus) { border-top-color: red }
            li:has(:hover) { color: blue } :target { color: purple } input:focus:not(:focus-visible) { color: orange }
            @media (hover: hover) { button:hover { background-color: pink } }
            </style>
            <nav id=n><ul><li id=l1><a id=a1 href=/a>A</a></li><li id=l2><a id=a2 href=/b>B</a></li></ul></nav>
            <form id=f><label id=lab for=i1>L</label><input id=i1 type=text><input id=i2 type=checkbox><button id=b1>Go</button>
            <textarea id=t1></textarea><select id=s1><option>1</option></select></form>
            <div tabindex=0 id=d1 contenteditable=true>E</div><details><summary id=sm>S</summary>x</details>
            """;
        OverlayEquivalence(page, null, new Random(3), 20, "written page");
        // :target comes from the context.
        Document doc = Document.Parse(page);
        DocumentStyles b = DocumentStyles.Create(doc);
        StyleContext t = StyleContext.Default with { Target = doc.GetElementById("n") };
        AssertEqual(b.WithContext(t), DocumentStyles.Create(doc, t), "target");
    }

    [Fact]
    public void Overlays_on_vendored_pages_equal_full_recompute()
    {
        string? dir = FindVendored();
        Assert.NotNull(dir);

        var r = new Random(9);
        int pages = 0;
        foreach (string file in Directory.EnumerateFiles(dir, "*.html", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            string html = File.ReadAllText(file);
            if (html.Length > 60000 || !html.Contains("<style", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (pages++ % 3 != 0)
            {
                continue;
            }

            OverlayEquivalence(html, null, r, 4, Path.GetFileName(file));
        }

        Assert.True(pages > 20);
    }

    private static string? FindVendored()
    {
        string c = Path.Combine(TestPaths.TestData, "css", "wpt-style");
        return Directory.Exists(c) ? c : null;
    }
}
