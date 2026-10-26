using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// Random edits (document, inline-style, <c>&lt;style&gt;</c> and linked-sheet edits, alone and mixed) followed by the
/// one-call restyle: the incrementally restyled styles must equal a fresh <see cref="DocumentStyles"/> on the new
/// document and sheets, for every element and every property.
/// </summary>
public class EditRestyleTests
{
    private const string LinkUrl = "https://t.test/a.css";
    private const string BaseUrl = "https://t.test/";

    private sealed class Counts
    {
        public int Steps, Skipped;
        public readonly Dictionary<string, int[]> ByKind = [];
        public long Restyled, Elements;

        public void Add(string kind, bool full)
        {
            if (!ByKind.TryGetValue(kind, out int[]? a))
            {
                ByKind[kind] = a = new int[2];
            }

            a[full ? 1 : 0]++;
        }

        public override string ToString() =>
            $"steps {Steps}, skipped {Skipped}; " + string.Join("; ", ByKind.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}: {k.Value[0]} incremental / {k.Value[1]} full")) +
            $"; incremental steps restyled {Restyled} of {Elements} elements";
    }

    private static readonly string[] Rules =
    [
        ".c1{color:red}", ".c2 .c3{background-color:blue}", ".c1>.c2{color:green}", ".c1+.c2{background-color:red}",
        ".c2~.c3{color:blue}", "li:nth-child(2n){color:red}", ":first-child{background-color:green}", ":last-child{color:green}",
        "div:empty{background-color:blue}", ".c1:not(.c2){opacity:.5}", "#i1{color:blue}", "div span{color:red}",
        "section>b{color:red}", "ul li+li{color:blue}", "span:only-child{color:green}", ".c4{font-size:20px}", ".c5{font-size:2em}",
        ":is(.c0,.c1) .c4{color:red !important}", "@media (min-width:600px){.c3{color:green}}", "@layer x{.c0{color:red}} .c0{color:blue}",
        "[title]{display:inline-block}", "[hidden]{visibility:hidden}", "p{margin:1px 2px}", ".c3{--k:7px;width:var(--k)}", "td{color:blue}",
        "a:hover{color:red}", "div:focus-within{background-color:blue}", "[lang=en] .c2{font-weight:bold}",
        "li:last-child span{color:green}", ".c2:nth-last-child(2) .c3{color:red}", "div:empty ~ p b{color:blue}",
    ];

    private static readonly string[] Colors = ["red", "blue", "green"];
    private static readonly string[] AttrNames = ["title", "hidden", "lang", "dir", "bgcolor", "align"];
    private static readonly string[] RemovableAttrs = ["class", "id", "style", "lang", "title"];
    private static readonly string[] HasRules = [".c1:has(.c2){color:red}", "ul:has(li:last-child){color:green}", "section:has(> a){color:red}"];

    private static string Sheet(Random r, bool has)
    {
        string[] pool = has ? [.. Rules, .. HasRules] : Rules;
        var sb = new StringBuilder();
        int n = 3 + r.Next(8);
        for (int i = 0; i < n; i++)
        {
            sb.Append(pool[r.Next(pool.Length)]).Append(r.Next(2) == 0 ? "\n" : " ");
        }

        return sb.ToString();
    }

    private static void Element(Random r, StringBuilder sb, int depth, string indent)
    {
        string[] tags = ["div", "span", "section", "p", "ul", "a", "table", "b", "div"];
        string tag = depth <= 0 ? "span" : tags[r.Next(tags.Length)];
        sb.Append(indent).Append('<').Append(tag);
        if (tag == "a")
        {
            sb.Append(" href=/x");
        }

        int nc = r.Next(3);
        if (nc > 0)
        {
            sb.Append(" class=\"");
            for (int i = 0; i < nc; i++)
            {
                sb.Append(i > 0 ? " " : "").Append('c').Append(r.Next(6));
            }

            sb.Append('"');
        }

        if (r.Next(6) == 0)
        {
            sb.Append(" id=i").Append(r.Next(4));
        }

        if (r.Next(7) == 0)
        {
            sb.Append(" style=\"color:").Append(Colors[r.Next(3)]).Append('"');
        }

        if (r.Next(10) == 0)
        {
            sb.Append(" lang=en");
        }

        sb.Append('>');
        if (tag == "table")
        {
            sb.Append("<tr><td>c</td><td class=c").Append(r.Next(6)).Append(">d</td></tr></table>\n");
            return;
        }

        if (tag == "ul")
        {
            for (int i = 0, n = r.Next(4); i < n; i++)
            {
                sb.Append("<li").Append(r.Next(2) == 0 ? " class=c" + r.Next(6) : "").Append('>').Append(r.Next(2) == 0 ? "t" : "").Append("</li>");
            }
        }
        else if (tag is "a" or "p" or "b")
        {
            sb.Append("text ").Append(r.Next(9));
        }
        else if (depth > 0)
        {
            sb.Append('\n');
            for (int i = 0, n = r.Next(4); i < n; i++)
            {
                if (r.Next(4) == 0)
                {
                    sb.Append(indent).Append("  x").Append(r.Next(9)).Append('\n');
                }
                else
                {
                    Element(r, sb, depth - 1, indent + "  ");
                }
            }

            sb.Append(indent);
        }

        sb.Append("</").Append(tag).Append(">\n");
    }

    private static string Page(Random r, bool has, string linkedHref)
    {
        var sb = new StringBuilder("<!DOCTYPE html>\n<html>\n<head>\n");
        if (linkedHref.Length > 0)
        {
            sb.Append("<link rel=stylesheet href=").Append(linkedHref).Append(">\n");
        }

        sb.Append("<style>").Append(Sheet(r, has)).Append("</style>\n</head>\n<body>\n");
        for (int i = 0, n = 1 + r.Next(4); i < n; i++)
        {
            Element(r, sb, 3, "  ");
        }

        return sb.Append("</body>\n</html>\n").ToString();
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

    private static readonly string[] Fragments =
    [
        "<p class=c1>n</p>", "<span id=i2></span>", "<div class=\"c2 c3\"><b>x</b></div>", "text", "<li class=c2>q</li>",
        "<section><a href=/y>l</a></section>", "<ul><li>1</li><li class=c1>2</li></ul>",
    ];

    private static readonly string[] Props = ["color", "background-color", "font-size", "display", "opacity", "visibility", "margin"];
    private static readonly string[] Values = ["red", "blue", "10px", "none", "0.3", "hidden", "2em", "inherit", "green"];

    private static List<Edit> DocumentEdits(Random r, Document d)
    {
        List<Node> nodes = Preorder(d);
        var edits = new List<Edit>();
        int count = 1 + r.Next(3);
        var used = new HashSet<int>();
        for (int e = 0; e < count; e++)
        {
            Node n = nodes[r.Next(nodes.Count)];
            if (!used.Add(n.Index) || n.Kind is NodeKind.Document or NodeKind.DocumentType)
            {
                continue;
            }

            bool structural = n.IsElement && n.LocalName is "html" or "head" or "body";
            if (n.Kind == NodeKind.Text)
            {
                edits.Add(r.Next(3) switch
                {
                    0 => Edit.ReplaceText(n, "y" + r.Next(9)),
                    1 => Edit.InsertAfter(n, Fragments[r.Next(Fragments.Length)]),
                    _ => Edit.Remove(n),
                });
                continue;
            }

            if (!n.IsElement)
            {
                continue;
            }

            switch (r.Next(structural ? 6 : 13))
            {
                case 0:
                    edits.Add(Edit.SetAttribute(n, "class", "c" + r.Next(6) + (r.Next(2) == 0 ? " c" + r.Next(6) : "")));
                    break;
                case 1:
                    edits.Add(Edit.SetAttribute(n, "id", "i" + r.Next(4)));
                    break;
                case 2:
                    edits.Add(Edit.SetAttribute(n, AttrNames[r.Next(6)], r.Next(2) == 0 ? "en" : "x"));
                    break;
                case 3:
                    edits.Add(Edit.RemoveAttribute(n, RemovableAttrs[r.Next(5)]));
                    break;
                case 4:
                    edits.Add(Edit.SetStyle(n, Props[r.Next(Props.Length)], Values[r.Next(Values.Length)], r.Next(5) == 0));
                    break;
                case 5:
                    edits.Add(r.Next(2) == 0 ? Edit.RemoveStyle(n, Props[r.Next(Props.Length)]) : Edit.Append(n, Fragments[r.Next(Fragments.Length)]));
                    break;
                case 6:
                    edits.Add(Edit.Prepend(n, Fragments[r.Next(Fragments.Length)]));
                    break;
                case 7:
                    edits.Add(Edit.InsertBefore(n, Fragments[r.Next(Fragments.Length)]));
                    break;
                case 8:
                    edits.Add(Edit.InsertAfter(n, Fragments[r.Next(Fragments.Length)]));
                    break;
                case 9:
                    edits.Add(Edit.Wrap(n, r.Next(2) == 0 ? "<div class=c1></div>" : "<section class=c2></section>"));
                    break;
                case 10:
                    edits.Add(Edit.Unwrap(n));
                    break;
                case 11:
                    edits.Add(Edit.Remove(n));
                    break;
                default:
                    edits.Add(Edit.ReplaceText(n, "z"));
                    break;
            }
        }

        return edits;
    }

    private static List<SheetEdit> SheetEdits(Random r, Stylesheet sheet)
    {
        var rules = sheet.Rules.Where(x => x.Kind == CssNodeKind.QualifiedRule).ToList();
        var edits = new List<SheetEdit>();
        if (rules.Count == 0 || r.Next(5) == 0)
        {
            edits.Add(SheetEdit.AppendRule(sheet, Rules[r.Next(Rules.Length)]));
            return edits;
        }

        CssNode rule = rules[r.Next(rules.Count)];
        switch (r.Next(7))
        {
            case 0:
            case 1:
                edits.Add(SheetEdit.SetDeclaration(sheet, rule, Props[r.Next(Props.Length)], Values[r.Next(Values.Length)], r.Next(6) == 0));
                break;
            case 2:
                edits.Add(SheetEdit.RemoveRule(sheet, rule));
                break;
            case 3:
                edits.Add(SheetEdit.InsertRuleBefore(sheet, rule, Rules[r.Next(Rules.Length)]));
                break;
            case 4:
                edits.Add(SheetEdit.InsertRuleAfter(sheet, rule, Rules[r.Next(Rules.Length)]));
                break;
            case 5:
                edits.Add(SheetEdit.ReplaceRule(sheet, rule, Rules[r.Next(Rules.Length)]));
                break;
            default:
                foreach (CssNode decl in rule.Declarations)
                {
                    if (decl.Kind == CssNodeKind.Declaration)
                    {
                        edits.Add(SheetEdit.RemoveDeclaration(sheet, rule, decl.Name));
                        break;
                    }
                }

                break;
        }

        return edits;
    }

    private static StylesheetResolver Resolver(LinkedStylesheet? linked) =>
        url => linked is not null && url == linked.Url ? new StylesheetResource(linked.Bytes.ToArray()) : null;

    private static void AssertEqual(DocumentStyles actual, DocumentStyles expected, string what)
    {
        string[] typed = ["display", "visibility", "color", "background-color", "font-size", "opacity", "font-weight", "outline-style"];
        foreach (Node e in expected.Document.Root.DescendantElements)
        {
            ComputedStyle a = actual.GetStyle(e);
            ComputedStyle b = expected.GetStyle(e);
            string? diff = a.FirstDifference(b);
            Assert.True(diff is null, what + ": <" + e.LocalName + "> #" + e.Index.ToString(CultureInfo.InvariantCulture) + " differs in " + diff);
            foreach (string p in typed)
            {
                Assert.True(a.GetComputedValue(p) == b.GetComputedValue(p), what + ": computed " + p + " of <" + e.LocalName + "> #" + e.Index.ToString(CultureInfo.InvariantCulture) + ": " + a.GetComputedValue(p) + " vs " + b.GetComputedValue(p) + " in " + expected.Document.Source.Text);
            }
        }
    }

    private static void Run(Random r, Document start, LinkedStylesheet? startLinked, bool has, int steps, Counts counts, string tag)
    {
        Document doc = start;
        LinkedStylesheet? linked = startLinked;
        var options = new StyleSourceOptions { BaseUrl = BaseUrl, Resolver = Resolver(linked) };
        DocumentStyles styles = DocumentStyles.Create(doc, null, options);
        if (r.Next(2) == 0)
        {
            styles.ComputeAll();
        }

        for (int s = 0; s < steps; s++)
        {
            counts.Steps++;
            int kind = r.Next(6);
            IReadOnlyList<InlineStylesheet> inlines = InlineStylesheets.Of(doc);
            var docEdits = new List<Edit>();
            var sheetEdits = new List<SheetEdit>();
            string label;
            if (kind == 0 || kind == 5)
            {
                docEdits = DocumentEdits(r, doc);
                label = "document";
            }
            else if (kind == 1 && inlines.Count > 0)
            {
                sheetEdits = SheetEdits(r, inlines[r.Next(inlines.Count)].Sheet);
                label = "style element";
            }
            else if (kind == 2 && linked is not null)
            {
                sheetEdits = SheetEdits(r, linked.Sheet);
                label = "linked sheet";
            }
            else if (kind == 3 && inlines.Count > 0 && linked is not null)
            {
                docEdits = DocumentEdits(r, doc);
                sheetEdits = [.. SheetEdits(r, inlines[0].Sheet), .. SheetEdits(r, linked.Sheet)];
                label = "mixed";
            }
            else
            {
                // Head edits: a style element or link added, changed or removed.
                Node head = doc.Root.DescendantElements.FirstOrDefault(x => x.LocalName == "head");
                Node link = doc.Root.DescendantElements.FirstOrDefault(x => x.LocalName == "link");
                Node style = doc.Root.DescendantElements.FirstOrDefault(x => x.LocalName == "style");
                switch (r.Next(5))
                {
                    case 0 when !head.IsNull:
                        docEdits.Add(Edit.Append(head, "<style>" + Rules[r.Next(Rules.Length)] + "</style>"));
                        break;
                    case 1 when !link.IsNull:
                        docEdits.Add(r.Next(2) == 0 ? Edit.SetAttribute(link, "media", "print") : Edit.SetAttribute(link, "disabled", ""));
                        break;
                    case 2 when !link.IsNull:
                        docEdits.Add(Edit.Remove(link));
                        break;
                    case 3 when !style.IsNull:
                        docEdits.Add(Edit.ReplaceText(style, Rules[r.Next(Rules.Length)] + Rules[r.Next(Rules.Length)]));
                        break;
                    case 4 when !style.IsNull:
                        docEdits.Add(Edit.Remove(style));
                        break;
                }

                label = "head elements";
            }

            if (docEdits.Count == 0 && sheetEdits.Count == 0)
            {
                counts.Skipped++;
                continue;
            }

            BatchResult batch = new EditBatch(doc, docEdits, sheetEdits, linked is null ? [] : [linked]).Apply();
            if (!batch.Succeeded)
            {
                counts.Skipped++;
                continue;
            }

            RestyleResult res;
            if (sheetEdits.Count == 0 && r.Next(2) == 0)
            {
                EditResult er = doc.Apply(docEdits);
                Assert.True(er.Succeeded, er.Failure);
                res = styles.Restyle(er);
                label += " (EditResult)";
            }
            else
            {
                res = styles.Restyle(batch);
            }

            Document newDoc = res.Styles.Document;
            LinkedStylesheet? newLinked = linked;
            foreach (SheetUpdate u in batch.Sheets)
            {
                if (u.NewLinked is not null)
                {
                    newLinked = u.NewLinked;
                }
            }

            counts.Add(label, res.FullRecompute);
            if (!res.FullRecompute)
            {
                counts.Restyled += res.RestyledElements.Count;
                counts.Elements += newDoc.Root.DescendantElements.Count();
            }

            var freshOptions = new StyleSourceOptions { BaseUrl = BaseUrl, Resolver = Resolver(newLinked) };
            DocumentStyles fresh = DocumentStyles.Create(newDoc, null, freshOptions);
            AssertEqual(res.Styles, fresh, $"{tag} step {s} ({label}, full={res.FullRecompute}, has={has})");
            doc = newDoc;
            linked = newLinked;
            styles = res.Styles;
        }
    }

    private static int Seeds(int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable("ONEHTML_RESTYLE_SEEDS"), out int n) && n > 0 ? n : fallback;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Random_edits_restyle_equals_fresh_styles(bool has)
    {
        var counts = new Counts();
        for (int seed = 0; seed < Seeds(150); seed++)
        {
            var r = new Random(seed * 2 + (has ? 1 : 0));
            string css = Sheet(r, has);
            LinkedStylesheet linked = LinkedStylesheet.Create(LinkUrl, Encoding.UTF8.GetBytes(css));
            Document doc = Document.Parse(Page(r, has, "a.css"));
            Run(r, doc, linked, has, 4, counts, "seed " + seed);
        }

        Console.WriteLine("restyle equivalence (has=" + has + "): " + counts);
        Assert.True(counts.Steps - counts.Skipped > counts.Steps / 2, counts.ToString());
        Assert.True(counts.ByKind.Values.Sum(a => a[0]) > 0, "no incremental restyle happened: " + counts);
    }

    [Fact]
    public void Vendored_pages()
    {
        var counts = new Counts();
        foreach (string file in Directory.GetFiles(Path.Combine(TestPaths.TestData, "edit"), "*.html").OrderBy(f => f, StringComparer.Ordinal))
        {
            byte[] bytes = File.ReadAllBytes(file);
            for (int seed = 0; seed < Seeds(40); seed++)
            {
                var r = new Random(seed);
                Document doc = Document.Parse(bytes);
                Run(r, doc, null, false, 3, counts, Path.GetFileName(file) + " seed " + seed);
            }
        }

        Console.WriteLine("restyle equivalence (vendored pages): " + counts);
        Assert.True(counts.ByKind.Values.Sum(a => a[0]) > 0, counts.ToString());
    }

    [Fact]
    public void A_linked_sheet_edit_reaches_the_cascade_through_the_resolver()
    {
        LinkedStylesheet linked = LinkedStylesheet.Create(LinkUrl, "p { color: red }\n"u8.ToArray());
        Document doc = Document.Parse("<link rel=stylesheet href=a.css><p>x</p>");
        DocumentStyles styles = DocumentStyles.Create(doc, null, new StyleSourceOptions { BaseUrl = BaseUrl, Resolver = Resolver(linked) });
        Node p = doc.Root.DescendantElements.First(x => x.LocalName == "p");
        Assert.Equal("red", styles.GetStyle(p).GetPropertyValue("color"));
        CssNode rule = linked.Sheet.Rules.First();
        BatchResult b = new EditBatch(null, null, [SheetEdit.SetDeclaration(linked.Sheet, rule, "color", "blue")], [linked]).Apply();
        Assert.True(b.Succeeded, b.Failure);
        RestyleResult res = styles.Restyle(b);
        Assert.False(res.FullRecompute);
        Assert.Equal("blue", res.Styles.GetStyle(res.Styles.Document.Root.DescendantElements.First(x => x.LocalName == "p")).GetPropertyValue("color"));
        Assert.Equal([p.Index], res.RestyledElements);
        // The old view still sees the old sheet.
        Assert.Equal("red", styles.GetStyle(p).GetPropertyValue("color"));
    }

    [Fact]
    public void An_attribute_edit_names_the_attribute_and_restyles_only_its_reach()
    {
        Document doc = Document.Parse("<style>.a{color:red} .b{color:blue}</style><p class=a>1</p><p class=b>2</p><p>3</p>");
        DocumentStyles styles = DocumentStyles.Create(doc);
        styles.ComputeAll();
        Node first = doc.Root.DescendantElements.First(x => x.LocalName == "p");
        EditResult er = doc.Apply([Edit.SetAttribute(first, "class", "b")]);
        RestyleResult res = styles.Restyle(er);
        Assert.False(res.FullRecompute);
        Assert.Equal([er.Changes!.MapToNew(first).Index], res.RestyledElements);
        Assert.Throws<ArgumentException>(() => DocumentStyles.Create(er.Document!).Restyle(er));
    }
}
