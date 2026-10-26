using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// <c>styles</c>: on each page, with the stylesheets the corpus holds for it, compares incremental style results with a
/// full recompute value for value, for every property of every element. Two checks: (a) overlays under random
/// non-default <see cref="StyleContext"/>s (focus, hover, active, <c>:target</c>, viewport and media features) against
/// <see cref="DocumentStyles.Create(Document, StyleContext?, StyleSourceOptions?, CancellationToken)"/> under the same
/// context; (b) a chain of random edits (element edits, <c>&lt;style&gt;</c> and linked-sheet rule and declaration
/// edits) restyled through <c>Restyle</c> against fresh styles of the edited document and sheets.
/// </summary>
internal static class StylesCommand
{
    private static readonly string[] s_props = ["color", "background-color", "font-size", "display", "opacity", "visibility", "margin", "width", "position", "border-top-color", "outline-style", "--k", "font-weight", "text-decoration-line"];
    private static readonly string[] s_values = ["red", "blue", "10px", "none", "0.3", "hidden", "2em", "inherit", "green", "block", "absolute", "bold", "underline", "1px", "var(--k)", "calc(1px + 2px)", "initial", "unset"];
    private static readonly string[] s_attributes = ["hidden", "lang", "dir", "title", "bgcolor", "align", "disabled", "checked", "open", "tabindex"];
    private static readonly string[] s_removable = ["class", "id", "style", "lang", "title", "hidden", "dir"];

    /// <summary>Remembers the URLs the cascade asked for, so linked sheets can be handed to the editor under the exact URL the library resolved.</summary>
    private sealed class RecordingResolver(StylesheetStore store)
    {
        private readonly Lock _lock = new();

        public List<(string Url, StylesheetResource Resource)> Requested { get; } = [];

        public StylesheetResource? Resolve(string url)
        {
            StylesheetResource? r = store.Resolve(url);
            if (r is not null)
            {
                lock (_lock)
                {
                    if (!Requested.Any(x => x.Url == url))
                    {
                        Requested.Add((url, r));
                    }
                }
            }

            return r;
        }
    }

    public static int Run(Options o)
    {
        var store = StylesheetStore.Load(o.StyleCorpus is { Length: > 0 } sc ? sc : o.Corpus);
        List<CorpusItem> items = Corpus.Load(o.Corpus, pages: true, stylesheets: false);
        Console.WriteLine($"styles: {store.Count} stylesheets available");
        return GateRunner.Run(o, "styles", items, (item, bytes, report, ct) => CheckPage(o, store, item, bytes, report, ct));
    }

    private static void CheckPage(Options o, StylesheetStore store, CorpusItem item, byte[] bytes, GateReport report, CancellationToken ct)
    {
        Document doc = Document.Parse(bytes, new ParseOptions { ContentType = item.ContentType }, ct);
        var rng = new Random(o.Seed ^ Convert.ToInt32(item.Sha256[..7], 16));
        string? baseUrl = Uri.TryCreate(item.Url, UriKind.Absolute, out Uri? u) && u.Scheme is "http" or "https" ? item.Url : null;
        var recorder = new RecordingResolver(store);
        var options = new StyleSourceOptions { BaseUrl = baseUrl, Resolver = recorder.Resolve };
        report.Count("pages");
        report.Count("elements", doc.Root.DescendantElements.Count());

        DocumentStyles baseStyles = DocumentStyles.Create(doc, null, options, ct);
        baseStyles.ComputeAll(ct);
        report.Count("linked-sheets-resolved", recorder.Requested.Count);

        var failed = new HashSet<string>(StringComparer.Ordinal);
        OverlayChecks(o, item, doc, baseStyles, options, rng, report, failed, ct);
        RestyleChecks(o, item, doc, baseStyles, options, recorder, rng, report, failed, ct);
    }

    // ---- (a) overlays ----

    private static void OverlayChecks(Options o, CorpusItem item, Document doc, DocumentStyles baseStyles, StyleSourceOptions options, Random rng, GateReport report, HashSet<string> failed, CancellationToken ct)
    {
        List<Node> elements = [.. doc.Root.DescendantElements];
        if (elements.Count == 0)
        {
            return;
        }

        List<Node> focusable = [.. elements.Where(e => e.HasAttribute("tabindex") || e.LocalName is "a" or "button" or "input" or "select" or "textarea" or "summary")];
        for (int i = 0; i < o.PerPage; i++)
        {
            ct.ThrowIfCancellationRequested();
            (StyleContext ctx, string cause) = RandomContext(rng, elements, focusable);
            try
            {
                DocumentStyles overlay = baseStyles.WithContext(ctx, ct);
                DocumentStyles full = DocumentStyles.Create(doc, ctx, options, ct);
                report.Count("overlay-contexts");
                Compare(overlay, full, "overlay:" + cause, item, report, failed, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Failure("overlay:" + cause + ":" + GateRunner.Classify(ex), item, report, failed, null);
            }
        }
    }

    private static (StyleContext Context, string Cause) RandomContext(Random rng, List<Node> elements, List<Node> focusable)
    {
        StyleContext ctx = StyleContext.Default;
        MediaFeatures m = MediaFeatures.Default;
        var parts = new SortedSet<string>(StringComparer.Ordinal);
        int n = 1 + rng.Next(3);
        for (int i = 0; i < n; i++)
        {
            switch (rng.Next(9))
            {
                case 0 when focusable.Count > 0:
                    ctx = ctx.WithFocus(focusable[rng.Next(focusable.Count)], rng.Next(2) == 0 ? FocusSource.Keyboard : FocusSource.PointingDevice);
                    parts.Add("focus");
                    break;
                case 1:
                    ctx = ctx.WithHover(elements[rng.Next(elements.Count)]);
                    parts.Add("hover");
                    break;
                case 2:
                    ctx = ctx with { Target = elements[rng.Next(elements.Count)] };
                    parts.Add("target");
                    break;
                case 3:
                    ctx = ctx with { Active = elements[rng.Next(elements.Count)] };
                    parts.Add("active");
                    break;
                case 4:
                    m = m with { Width = new[] { 320.0, 375, 768, 1024, 1920 }[rng.Next(5)], Height = new[] { 480.0, 667, 900, 1080 }[rng.Next(4)] };
                    parts.Add("viewport");
                    break;
                case 5:
                    m = m with { ColorScheme = PrefersColorScheme.Dark };
                    parts.Add("dark");
                    break;
                case 6:
                    m = m with { ReducedMotion = PrefersReduced.Reduce, Contrast = PrefersContrast.More };
                    parts.Add("reduced-motion-contrast");
                    break;
                case 7:
                    m = m with { ForcedColors = true };
                    parts.Add("forced-colors");
                    break;
                default:
                    m = m with { Type = MediaType.Print };
                    parts.Add("print");
                    break;
            }
        }

        if (parts.Count == 0)
        {
            ctx = ctx.WithHover(elements[rng.Next(elements.Count)]);
            parts.Add("hover");
        }

        return (ctx.WithMedia(m), string.Join('+', parts));
    }

    // ---- (b) restyle after edits ----

    private static void RestyleChecks(Options o, CorpusItem item, Document startDoc, DocumentStyles startStyles, StyleSourceOptions startOptions, RecordingResolver recorder, Random rng, GateReport report, HashSet<string> failed, CancellationToken ct)
    {
        // Linked sheets the cascade used (links and imports alike): up to three the editor can be given by URL.
        var linkedList = new List<LinkedStylesheet>();
        var overrides = new Dictionary<string, StylesheetResource>(StringComparer.Ordinal);
        foreach ((string url, StylesheetResource res) in recorder.Requested.Take(3))
        {
            try
            {
                linkedList.Add(LinkedStylesheet.Create(url, res.Bytes, res.ProtocolEncodingLabel));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                report.Count("linked-unusable:" + GateRunner.Classify(ex));
            }
        }

        StylesheetResource? freshResolver(string url) => overrides.TryGetValue(url, out StylesheetResource? r) ? r : recorder.Resolve(url);

        Document doc = startDoc;
        DocumentStyles styles = startStyles;
        for (int step = 0; step < o.PerPage; step++)
        {
            ct.ThrowIfCancellationRequested();
            string kind;
            List<Edit> docEdits = [];
            List<SheetEdit> sheetEdits = [];
            try
            {
                IReadOnlyList<InlineStylesheet> inlines = InlineStylesheets.Of(doc);
                int pick = rng.Next(10);
                if (pick < 5 || (inlines.Count == 0 && linkedList.Count == 0))
                {
                    kind = "element";
                    docEdits = ElementEdits(rng, doc);
                }
                else if (pick < 7 && inlines.Count > 0)
                {
                    kind = "style-element";
                    sheetEdits = SheetEdits(rng, inlines[rng.Next(inlines.Count)].Sheet, doc);
                }
                else if (pick < 9 && linkedList.Count > 0)
                {
                    kind = "linked-sheet";
                    sheetEdits = SheetEdits(rng, linkedList[rng.Next(linkedList.Count)].Sheet, doc);
                }
                else
                {
                    kind = "mixed";
                    docEdits = ElementEdits(rng, doc);
                    if (inlines.Count > 0)
                    {
                        sheetEdits.AddRange(SheetEdits(rng, inlines[0].Sheet, doc));
                    }

                    if (linkedList.Count > 0)
                    {
                        sheetEdits.AddRange(SheetEdits(rng, linkedList[0].Sheet, doc));
                    }
                }

                if (docEdits.Count == 0 && sheetEdits.Count == 0)
                {
                    report.Count("restyle-no-edit");
                    continue;
                }

                if (GateRunner.Trace)
                {
                    foreach (Edit te in docEdits)
                    {
                        Console.Error.WriteLine("TRACE " + kind + " " + te.Operation + " #" + te.Target.Index + " <" + te.Target.LocalName + "> name=" + te.Name + " value=" + te.Value);
                    }

                    foreach (SheetEdit se in sheetEdits)
                    {
                        Console.Error.WriteLine("TRACE " + kind + " " + se.Operation + " prop=" + se.Property + " value=" + se.Value + " css=" + se.Css);
                    }
                }

                BatchResult batch = new EditBatch(doc, docEdits, sheetEdits, linkedList).Apply(null, ct);
                if (!batch.Succeeded)
                {
                    report.Count("restyle-refused:" + kind);
                    continue;
                }

                RestyleResult res;
                if (sheetEdits.Count == 0 && rng.Next(2) == 0)
                {
                    EditResult er = doc.Apply(docEdits, null, ct);
                    if (!er.Succeeded)
                    {
                        report.Count("restyle-refused:" + kind);
                        continue;
                    }

                    res = styles.Restyle(er, ct);
                    kind += "(EditResult)";
                }
                else
                {
                    res = styles.Restyle(batch, ct);
                }

                foreach (SheetUpdate su in batch.Sheets)
                {
                    if (su.NewLinked is not null)
                    {
                        overrides[su.NewLinked.Url] = new StylesheetResource(su.NewLinked.Bytes.ToArray(), recorder.Requested.FirstOrDefault(x => x.Url == su.NewLinked.Url).Resource?.ProtocolEncodingLabel);
                        int at = linkedList.FindIndex(l => l.Url == su.NewLinked.Url);
                        if (at >= 0)
                        {
                            linkedList[at] = su.NewLinked;
                        }
                    }
                }

                report.Count("restyle-steps:" + kind);
                report.Count(res.FullRecompute ? "restyle-full-recompute:" + kind : "restyle-incremental:" + kind);
                if (!res.FullRecompute)
                {
                    report.Count("restyle-elements-restyled", res.RestyledElements.Count);
                }

                var freshOptions = new StyleSourceOptions { BaseUrl = startOptions.BaseUrl, Resolver = freshResolver };
                DocumentStyles fresh = DocumentStyles.Create(res.Styles.Document, null, freshOptions, ct);
                bool equal = Compare(res.Styles, fresh, "restyle:" + kind + (res.FullRecompute ? ":full" : ":incremental"), item, report, failed, ct);
                doc = res.Styles.Document;
                styles = res.Styles;
                if (!equal)
                {
                    return; // the chain has diverged from the truth; further steps would only repeat the same difference
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Failure("restyle:step:" + GateRunner.Classify(ex), item, report, failed, null);
                return;
            }
        }
    }

    // ---- comparison ----

    private static bool Compare(DocumentStyles actual, DocumentStyles expected, string cause, CorpusItem item, GateReport report, HashSet<string> failed, CancellationToken ct)
    {
        bool equal = true;
        foreach (Node e in expected.Document.Root.DescendantElements)
        {
            ct.ThrowIfCancellationRequested();
            string? diff = actual.GetStyle(e).FirstDifference(expected.GetStyle(e));
            report.Count("elements-compared");
            if (diff is not null)
            {
                equal = false;
                report.Count("mismatch:" + cause + ":" + diff);
                Failure(cause + ":" + diff, item, report, failed, "<" + e.LocalName + "> #" + e.Index.ToString(CultureInfo.InvariantCulture));
                if (GateRunner.Trace)
                {
                    Console.Error.WriteLine("TRACE mismatch " + cause + " " + diff + " <" + e.LocalName + "> #" + e.Index + " actual=" + actual.GetStyle(e).GetPropertyValue(diff) + " expected=" + expected.GetStyle(e).GetPropertyValue(diff) + " class=" + e.GetAttribute("class") + " id=" + e.GetAttribute("id") + " style=" + e.GetAttribute("style") + " parent=" + e.Parent.LocalName + " expectedFrom=" + (expected.GetStyle(e).GetSource(diff)?.SelectorText ?? "-") + " in " + (expected.GetStyle(e).GetSource(diff)?.SheetUrl ?? "inline #" + expected.GetStyle(e).GetSource(diff)?.Owner.Index) + " actualFrom=" + (actual.GetStyle(e).GetSource(diff)?.SelectorText ?? "-") + " in " + (actual.GetStyle(e).GetSource(diff)?.SheetUrl ?? "inline #" + actual.GetStyle(e).GetSource(diff)?.Owner.Index));
                }
            }
        }

        return equal;
    }

    private static void Failure(string cls, CorpusItem item, GateReport report, HashSet<string> failed, string? example)
    {
        if (failed.Add(cls))
        {
            report.Fail(cls, item.Sha256, example);
        }
    }

    // ---- edit generation: the page's own vocabulary, so edits reach real rules ----

    private static List<Edit> ElementEdits(Random r, Document d)
    {
        List<Node> nodes = Preorder(d);
        List<string> classes = [.. nodes.Where(n => n.IsElement).SelectMany(n => (n.GetAttribute("class") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.Ordinal).Take(300)];
        List<string> ids = [.. nodes.Where(n => n.IsElement).Select(n => n.GetAttribute("id") ?? string.Empty).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).Take(100)];
        string Class() => classes.Count > 0 ? classes[r.Next(classes.Count)] : "c" + r.Next(5);
        string Id() => ids.Count > 0 ? ids[r.Next(ids.Count)] : "i" + r.Next(5);
        string[] fragments =
        [
            "<p class=\"" + Class() + "\">n</p>", "<span id=\"" + Id() + "\"></span>", "<div class=\"" + Class() + " " + Class() + "\"><b>x</b></div>", "text",
            "<li class=\"" + Class() + "\">q</li>", "<section><a href=/y class=\"" + Class() + "\">l</a></section>", "<ul><li>1</li><li class=\"" + Class() + "\">2</li></ul>",
        ];

        var edits = new List<Edit>();
        var used = new HashSet<int>();
        for (int e = 0, count = 1 + r.Next(3); e < count; e++)
        {
            Node n = nodes[r.Next(nodes.Count)];
            if (!used.Add(n.Index) || n.Kind is NodeKind.Document or NodeKind.DocumentType)
            {
                continue;
            }

            if (n.Kind == NodeKind.Text)
            {
                edits.Add(r.Next(3) switch
                {
                    0 => Edit.ReplaceText(n, "y" + r.Next(9)),
                    1 => Edit.InsertAfter(n, fragments[r.Next(fragments.Length)]),
                    _ => Edit.Remove(n),
                });
                continue;
            }

            if (!n.IsElement)
            {
                continue;
            }

            bool structural = n.LocalName is "html" or "head" or "body";
            switch (r.Next(structural ? 6 : 13))
            {
                case 0:
                    edits.Add(Edit.SetAttribute(n, "class", Class() + (r.Next(2) == 0 ? " " + Class() : string.Empty)));
                    break;
                case 1:
                    edits.Add(Edit.SetAttribute(n, "id", Id()));
                    break;
                case 2:
                    edits.Add(Edit.SetAttribute(n, s_attributes[r.Next(s_attributes.Length)], r.Next(2) == 0 ? "en" : "x"));
                    break;
                case 3:
                    edits.Add(Edit.RemoveAttribute(n, s_removable[r.Next(s_removable.Length)]));
                    break;
                case 4:
                    edits.Add(Edit.SetStyle(n, s_props[r.Next(s_props.Length)], s_values[r.Next(s_values.Length)], r.Next(5) == 0));
                    break;
                case 5:
                    edits.Add(r.Next(2) == 0 ? Edit.RemoveStyle(n, s_props[r.Next(s_props.Length)]) : Edit.Append(n, fragments[r.Next(fragments.Length)]));
                    break;
                case 6:
                    edits.Add(Edit.Prepend(n, fragments[r.Next(fragments.Length)]));
                    break;
                case 7:
                    edits.Add(Edit.InsertBefore(n, fragments[r.Next(fragments.Length)]));
                    break;
                case 8:
                    edits.Add(Edit.InsertAfter(n, fragments[r.Next(fragments.Length)]));
                    break;
                case 9:
                    edits.Add(Edit.Wrap(n, "<div class=\"" + Class() + "\"></div>"));
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

    private static List<SheetEdit> SheetEdits(Random r, Stylesheet sheet, Document d)
    {
        List<CssNode> rules = [.. sheet.Rules.Where(x => x.Kind == CssNodeKind.QualifiedRule)];
        string NewRule()
        {
            List<Node> els = [.. d.Root.DescendantElements.Take(2000)];
            Node e = els.Count > 0 ? els[r.Next(els.Count)] : default;
            string cls = !e.IsNull && (e.GetAttribute("class") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } cs ? "." + CssEscape(cs[r.Next(cs.Length)]) : "div";
            string tag = !e.IsNull ? e.LocalName : "div";
            string[] pool =
            [
                cls + "{" + s_props[r.Next(s_props.Length)] + ":" + s_values[r.Next(s_values.Length)] + "}",
                tag + ":hover{color:red}", cls + ":focus-within{background-color:blue}", tag + ":has(> " + tag + "){opacity:.5}", tag + " + " + tag + "{margin:1px}",
                "@media (max-width:600px){" + cls + "{display:none}}", tag + ":nth-child(2n){color:green !important}", "@layer x{" + cls + "{color:blue}}",
            ];
            return pool[r.Next(pool.Length)];
        }

        var edits = new List<SheetEdit>();
        if (rules.Count == 0 || r.Next(5) == 0)
        {
            edits.Add(SheetEdit.AppendRule(sheet, NewRule()));
            return edits;
        }

        CssNode rule = rules[r.Next(rules.Count)];
        switch (r.Next(7))
        {
            case 0:
            case 1:
                edits.Add(SheetEdit.SetDeclaration(sheet, rule, s_props[r.Next(s_props.Length)], s_values[r.Next(s_values.Length)], r.Next(6) == 0));
                break;
            case 2:
                edits.Add(SheetEdit.RemoveRule(sheet, rule));
                break;
            case 3:
                edits.Add(SheetEdit.InsertRuleBefore(sheet, rule, NewRule()));
                break;
            case 4:
                edits.Add(SheetEdit.InsertRuleAfter(sheet, rule, NewRule()));
                break;
            case 5:
                edits.Add(SheetEdit.ReplaceRule(sheet, rule, NewRule()));
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

    private static string CssEscape(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_') && c < 0x80)
            {
                sb.Append('\\');
            }

            sb.Append(c);
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
}
