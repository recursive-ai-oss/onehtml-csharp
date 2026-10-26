using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using RecursiveAI.OneHtml.Locators;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>Counts of replay outcomes for one kind of mutation.</summary>
internal sealed class MutationRow
{
    public int Tried;
    public int Skipped;
    public int Applied;
    public int Stale;
    public int Ambiguous;
    public int Invalid;

    /// <summary>Applied to a node that is neither the target nor indistinguishable from it: a bug.</summary>
    public int Wrong;

    /// <summary>Applied to a copy that has the target's exact bytes and path: the format cannot tell them apart.</summary>
    public int Twin;

    /// <summary>Applied to a different node whose saved-fingerprint parts (tag, key attributes, text) are equal to the target's: the saved format cannot tell it from the target.</summary>
    public int Lookalike;

    /// <summary>Outcomes the mutation says should not happen, wrong elements aside (a stale that should have applied, an apply that should be stale).</summary>
    public int Unexpected;
}

/// <summary>
/// Replay checks on mutated copies of a page. For each page, edits are saved from the original document, then
/// replayed onto copies changed in one known way. The truth about which element a saved edit should find comes from
/// the mutation itself (element order in the page), never from the library's answer.
/// </summary>
internal sealed class ReplayMutations
{
    public static readonly string[] Names =
    [
        "whitespace", "attribute-order", "sibling-inserted-before", "target-removed", "target-duplicated-after", "target-duplicated-before",
        "text-changed", "id-changed", "class-changed", "unrelated-text-changed", "unrelated-length-preserved",
        "style-whitespace", "style-rule-removed", "style-rule-duplicated-after", "style-rule-duplicated-before", "style-declaration-changed", "style-rule-inserted", "style-element-inserted-before",
    ];

    private readonly Dictionary<string, MutationRow> _rows = Names.ToDictionary(n => n, _ => new MutationRow(), StringComparer.Ordinal);

    public IReadOnlyDictionary<string, MutationRow> Rows => _rows;

    private ElementFingerprint? _fingerprint;

    public List<string> Findings { get; } = [];

    public int Pages { get; private set; }

    public int Targets { get; private set; }

    /// <summary>Targets skipped because the saved edit does not apply even to the unmutated page.</summary>
    public int BaselineFailed { get; private set; }

    /// <summary>Runs every mutation for up to <paramref name="perPage"/> targets of one page.</summary>
    public void CheckPage(string name, string html, int perPage, Random rng)
    {
        Pages++;
        // Source ranges are offsets into the newline-normalized text (HTML 13.2.3.5), so the mutations work on that.
        html = html.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        Document doc;
        try
        {
            doc = Document.Parse(html);
        }
        catch (HtmlLimitExceededException)
        {
            return;
        }

        List<Node> elements = Elements(doc);
        var candidates = new List<int>();
        for (int i = 0; i < elements.Count; i++)
        {
            Node e = elements[i];
            if (e.Source.IsDisplaced || e.Parent.Kind == NodeKind.Document || e.LocalName is "html" or "head" or "body" or "template")
            {
                continue;
            }

            candidates.Add(i);
        }

        for (int k = 0; k < perPage && candidates.Count > 0; k++)
        {
            int ordinal = candidates[rng.Next(candidates.Count)];
            Targets++;
            ElementMutations(name, html, doc, elements, ordinal, rng);
        }

        StyleMutations(name, html, doc, rng);
    }

    // ---- element edits ----

    private void ElementMutations(string page, string html, Document doc, List<Node> elements, int ordinal, Random rng)
    {
        Node t = elements[ordinal];
        if (!Extent(t, out int s, out int e))
        {
            return;
        }

        EditEnvelope env = Roundtrip(EditJson.Create(doc, [Edit.SetAttribute(t, "data-replayed", "1")]));
        // An element the editor itself cannot edit in the unmutated page (some parser-reparented positions) says nothing about replay.
        if (env.Replay(doc).Outcomes[0].Status != EditStatus.Applied)
        {
            BaselineFailed++;
            return;
        }

        string tag = t.LocalName;
        _fingerprint = ElementFingerprint.Create(t);
        string originalOuter = html[s..e];
        string pathText = ElementFingerprint.PathText(t);
        int descendants = CountElements(t);

        // whitespace: extra blanks inside the start tag and between the tags of the page
        {
            Node start = t;
            int gt = start.Source.Range.End - 1;
            if (gt > start.Source.Range.Start && html[gt] == '>' && html[gt - 1] != '/' && html[gt - 1] != '"' && html[gt - 1] != '\'')
            {
                string m = html[..gt] + "  " + html[gt..];
                Check("whitespace", env, m, doc, elements, ordinal, truth: ordinal, delta: 0, tag, originalOuter, pathText, mustApply: true);
            }
            else
            {
                Skip("whitespace");
            }
        }

        // attribute order: swap the first two attributes of the target's start tag
        {
            if (t.Attributes.Count >= 2 && t.Attributes[0].Range.End <= t.Attributes[1].Range.Start && t.Attributes[0].Namespace == AttributeNamespace.None)
            {
                SourceRange a = t.Attributes[0].Range;
                SourceRange b = t.Attributes[1].Range;
                string m = html[..a.Start] + html[b.Start..b.End] + html[a.End..b.Start] + html[a.Start..a.End] + html[b.End..];
                Check("attribute-order", env, m, doc, elements, ordinal, ordinal, 0, tag, originalOuter, pathText, mustApply: true);
            }
            else
            {
                Skip("attribute-order");
            }
        }

        // a sibling inserted before the target
        {
            string? fill = SiblingFor(t.Parent.LocalName);
            if (fill is not null)
            {
                string m = html[..s] + fill + html[s..];
                Check("sibling-inserted-before", env, m, doc, elements, ordinal, ordinal + 1, 1, tag, originalOuter, pathText, mustApply: false);
            }
            else
            {
                Skip("sibling-inserted-before");
            }
        }

        // the target removed
        Check("target-removed", env, html[..s] + html[e..], doc, elements, ordinal, truth: -1, delta: -(1 + descendants), tag, originalOuter, pathText, mustApply: false);

        // the target duplicated
        Check("target-duplicated-after", env, html[..e] + originalOuter + html[e..], doc, elements, ordinal, ordinal, 1 + descendants, tag, originalOuter, pathText, mustApply: false);
        Check("target-duplicated-before", env, html[..s] + originalOuter + html[s..], doc, elements, ordinal, ordinal + 1 + descendants, 1 + descendants, tag, originalOuter, pathText, mustApply: false);

        // the target's text changed
        {
            Node? text = FirstTextWithLetter(t);
            if (text is { } tn && !tn.Source.IsDisplaced && tn.Source.DataRange is { } dr && dr.End > dr.Start && !IsRawTextParent(tn))
            {
                int at = html.IndexOfAny(['a', 'e', 'i', 'o', 'u', 'A', 'E', 'O'], dr.Start, dr.End - dr.Start);
                if (at >= 0)
                {
                    string m = html[..at] + "Q" + html[(at + 1)..];
                    Check("text-changed", env, m, doc, elements, ordinal, ordinal, 0, tag, originalOuter, pathText, mustApply: false, mustNotApply: true);
                }
                else
                {
                    Skip("text-changed");
                }
            }
            else
            {
                Skip("text-changed");
            }
        }

        // id / class changed
        {
            string? id = t.GetAttribute("id");
            Attr? idAttr = FindAttr(t, "id");
            if (id is not null && idAttr is { } ia && ia.ValueRange.End > ia.ValueRange.Start && !Hashy(id))
            {
                string m = html[..ia.ValueRange.End] + "x" + html[ia.ValueRange.End..];
                Check("id-changed", env, m, doc, elements, ordinal, ordinal, 0, tag, originalOuter, pathText, mustApply: false, mustNotApply: true);
            }
            else
            {
                Skip("id-changed");
            }

            Attr? ca = FindAttr(t, "class");
            if (ca is { } cl && cl.ValueRange.End > cl.ValueRange.Start && t.GetAttribute("class") is { } cv && cv.Any(char.IsLetter) && !Hashy(cv.Split(' ')[0]))
            {
                string m = html[..cl.ValueRange.Start] + "zz-" + html[cl.ValueRange.Start..];
                Check("class-changed", env, m, doc, elements, ordinal, ordinal, 0, tag, originalOuter, pathText, mustApply: false, mustNotApply: true);
            }
            else
            {
                Skip("class-changed");
            }
        }

        // an unrelated text node changed (longer, then same length)
        {
            Node? other = UnrelatedText(elements, ordinal, rng);
            if (other is { } o && o.Source.DataRange is { } dr && dr.End > dr.Start && !IsRawTextParent(o) && html.IndexOfAny(['a', 'e', 'i', 'o', 'u'], dr.Start, dr.End - dr.Start) is int at and >= 0 && (at < s || at >= e))
            {
                Check("unrelated-text-changed", env, html[..at] + "UNRELATED " + html[at..], doc, elements, ordinal, ordinal, 0, tag, originalOuter, pathText, mustApply: true);
                Check("unrelated-length-preserved", env, html[..at] + "Q" + html[(at + 1)..], doc, elements, ordinal, ordinal, 0, tag, originalOuter, pathText, mustApply: true);
            }
            else
            {
                Skip("unrelated-text-changed");
                Skip("unrelated-length-preserved");
            }
        }

        _ = page;
    }

    private void Check(string mutation, EditEnvelope env, string mutated, Document original, List<Node> originalElements, int ordinal, int truth, int delta, string tag, string originalOuter, string pathText, bool mustApply, bool mustNotApply = false)
    {
        MutationRow row = _rows[mutation];
        Document m = Document.Parse(mutated);
        List<Node> after = Elements(m);
        // The mutation must have changed the tree the way it was meant to, or the truth is unknown: skip it.
        if (after.Count != originalElements.Count + delta || (truth >= 0 && (truth >= after.Count || after[truth].LocalName != tag)))
        {
            row.Skipped++;
            return;
        }

        row.Tried++;
        ReplayResult r = env.Replay(m);
        ReplayOutcome o = r.Outcomes[0];
        switch (o.Status)
        {
            case EditStatus.Applied:
                {
                    row.Applied++;
                    Node got = o.Node;
                    int gotOrdinal = after.FindIndex(x => x == got);
                    if (gotOrdinal == truth && truth >= 0)
                    {
                        if (mustNotApply)
                        {
                            row.Unexpected++;
                            Findings.Add($"{mutation}: applied although the target changed: {env.Edits[0].Target!.Locator}\n  original outer: {Short(originalOuter)}\n  mutated outer:  {(Extent(got, out int ds, out int de) ? Short(mutated[ds..de]) : "?")}");
                        }

                        break;
                    }

                    bool twin = Extent(got, out int gs, out int ge) && string.Equals(mutated[gs..ge], originalOuter, StringComparison.Ordinal) && string.Equals(ElementFingerprint.PathText(got), pathText, StringComparison.Ordinal);
                    if (twin)
                    {
                        row.Twin++;
                    }
                    else if (_fingerprint is { } fp && fp.Compare(ElementFingerprint.Create(got)) is { TagAndAttributes: true, Text: true })
                    {
                        row.Lookalike++;
                    }
                    else
                    {
                        row.Wrong++;
                        Findings.Add($"WRONG ELEMENT {mutation}: locator {env.Edits[0].Target!.Locator} resolved to {got.LocalName} (element {gotOrdinal}), the target is element {truth}\n  page: {Short(mutated.Replace('\n', ' '))}\n  original outer: {Short(originalOuter)}");
                    }

                    break;
                }

            case EditStatus.Stale:
                row.Stale++;
                if (mustApply)
                {
                    row.Unexpected++;
                    Findings.Add($"{mutation}: stale, expected applied: {o.Reason} [{env.Edits[0].Target!.Locator}]\n  page: {Short(mutated.Replace('\n', ' '))}\n  original outer: {Short(originalOuter)}");
                }

                break;
            case EditStatus.Ambiguous:
                row.Ambiguous++;
                if (mustApply)
                {
                    row.Unexpected++;
                    Findings.Add($"{mutation}: ambiguous, expected applied: {o.Reason}");
                }

                break;
            default:
                row.Invalid++;
                row.Unexpected++;
                Findings.Add($"{mutation}: {o.Status}: {o.Reason}");
                break;
        }
    }

    private void Skip(string mutation) => _rows[mutation].Skipped++;

    // ---- stylesheet edits ----

    private void StyleMutations(string page, string html, Document doc, Random rng)
    {
        _ = page;
        IReadOnlyList<InlineStylesheet> sheets;
        try
        {
            sheets = InlineStylesheets.Of(doc);
        }
        catch (CssLimitExceededException)
        {
            return;
        }

        var options = new List<(InlineStylesheet Sheet, CssNode Rule)>();
        foreach (InlineStylesheet s in sheets)
        {
            if (s.TextNodeStart(doc) < 0)
            {
                continue;
            }

            foreach (CssNode r in s.Sheet.Rules)
            {
                if (r.Kind == CssNodeKind.QualifiedRule && r.HasBlock && r.IsBlockClosed && r.Declarations.Any())
                {
                    options.Add((s, r));
                }
            }
        }

        if (options.Count == 0)
        {
            return;
        }

        (InlineStylesheet sheet, CssNode rule) = options[rng.Next(options.Count)];
        int baseOffset = sheet.TextNodeStart(doc);
        EditEnvelope env = Roundtrip(EditJson.Create(doc, null, [SheetEdit.SetDeclaration(sheet.Sheet, rule, "color", "red")]));
        string css = sheet.Sheet.Text;
        CssRange rr = rule.Range;
        string original = SheetLayout_Norm(css[rr.Start..rr.End]);
        int abs(int x) => baseOffset + x;
        string Splice(int from, int to, string with) => html[..abs(from)] + with + html[abs(to)..];

        StyleCheck("style-whitespace", env, Splice(rr.Start, rr.Start, "  "), original, mustApply: true);
        StyleCheck("style-rule-removed", env, Splice(rr.Start, rr.End, string.Empty), original, mustApply: false);
        StyleCheck("style-rule-duplicated-after", env, Splice(rr.End, rr.End, "\n" + css[rr.Start..rr.End]), original, mustApply: true);
        StyleCheck("style-rule-duplicated-before", env, Splice(rr.Start, rr.Start, css[rr.Start..rr.End] + "\n"), original, mustApply: false);
        StyleCheck("style-rule-inserted", env, Splice(rr.Start, rr.Start, "zz-unrelated { top: 0 }\n"), original, mustApply: true);
        CssNode first = rule.Declarations.First();
        StyleCheck("style-declaration-changed", env, Splice(first.ValueRange.Start, first.ValueRange.End, "changed-" + css[first.ValueRange.Start..first.ValueRange.End]), original, mustApply: false, mustNotApply: true);
        Node styleEl = sheet.Element;
        if (styleEl.Source.Range.Start >= 0)
        {
            int at = styleEl.Source.Range.Start;
            StyleCheck("style-element-inserted-before", env, html[..at] + "<style>zz-other { top: 0 }</style>" + html[at..], original, mustApply: false);
        }
    }

    private void StyleCheck(string mutation, EditEnvelope env, string mutated, string originalRule, bool mustApply, bool mustNotApply = false)
    {
        MutationRow row = _rows[mutation];
        Document m = Document.Parse(mutated);
        row.Tried++;
        ReplayResult r = env.Replay(m);
        ReplayOutcome o = r.Outcomes[0];
        switch (o.Status)
        {
            case EditStatus.Applied:
                {
                    row.Applied++;
                    CssNode got = o.Rule!.Value;
                    string gotNorm = SheetLayout_Norm(got.Raw.ToString());
                    if (!string.Equals(gotNorm, originalRule, StringComparison.Ordinal))
                    {
                        row.Wrong++;
                        Findings.Add($"WRONG RULE {mutation}: resolved '{Short(gotNorm)}' for '{Short(originalRule)}'");
                    }
                    else if (mustNotApply)
                    {
                        row.Unexpected++;
                        Findings.Add($"{mutation}: applied although the rule changed");
                    }
                    else if (!mustApply)
                    {
                        row.Twin += mutation.Contains("duplicated-before", StringComparison.Ordinal) ? 1 : 0;
                    }

                    break;
                }

            case EditStatus.Stale:
                row.Stale++;
                if (mustApply)
                {
                    row.Unexpected++;
                    Findings.Add($"{mutation}: stale, expected applied: {o.Reason}");
                }

                break;
            case EditStatus.Ambiguous:
                row.Ambiguous++;
                if (mustApply)
                {
                    row.Unexpected++;
                    Findings.Add($"{mutation}: ambiguous, expected applied: {o.Reason}");
                }

                break;
            default:
                row.Invalid++;
                row.Unexpected++;
                Findings.Add($"{mutation}: {o.Status}: {o.Reason}");
                break;
        }
    }

    // ---- helpers ----

    private static string Short(string s) => s.Length <= 300 ? s : s[..300] + "...";

    private static string SheetLayout_Norm(string s) => string.Join(' ', s.Split([' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Saves and reads back, so replay always runs from parsed JSON.</summary>
    private static EditEnvelope Roundtrip(EditEnvelope e) => EditJson.Parse(e.ToJson());

    private static bool Hashy(string s) => s.Length >= 8 && s.Any(char.IsDigit);

    private static Attr? FindAttr(Node e, string name)
    {
        foreach (Attr a in e.Attributes)
        {
            if (a.Namespace == AttributeNamespace.None && a.LocalName == name)
            {
                return a;
            }
        }

        return null;
    }

    private static bool IsRawTextParent(Node text) => text.Parent.IsElement && text.Parent.LocalName is "script" or "style" or "textarea" or "title" or "xmp" or "iframe" or "noembed" or "noframes" or "noscript" or "plaintext";

    private static bool Extent(Node n, out int start, out int end)
    {
        start = end = -1;
        if (n.Source.IsDisplaced || !n.Source.HasStartTag)
        {
            return false;
        }

        start = n.Source.Range.Start;
        Node last = n;
        while (last.IsElement)
        {
            if (last.Source.HasEndTag)
            {
                end = last.Source.EndTag.End;
                return end >= start;
            }

            Node c = last.LastChild;
            if (c.IsNull)
            {
                end = last.Source.Range.End;
                return end >= start;
            }

            if (!c.IsElement)
            {
                end = c.Source.Range.End;
                return end >= start;
            }

            last = c;
        }

        return false;
    }

    private static string? SiblingFor(string parent) => parent switch
    {
        "div" or "section" or "article" or "main" or "nav" or "aside" or "header" or "footer" or "body" or "li" or "td" or "th" or "blockquote" or "form" or "fieldset" or "figure" or "span" or "a" or "p" or "ul" or "ol" => parent is "ul" or "ol" ? "<li>x</li>" : "<i>x</i>",
        "tr" => "<td>x</td>",
        "tbody" or "thead" or "tfoot" => "<tr><td>x</td></tr>",
        "dl" => "<dd>x</dd>",
        "select" => "<option>x</option>",
        _ => null,
    };

    private static List<Node> Elements(Document doc)
    {
        var list = new List<Node>();
        var stack = new Stack<Node>();
        stack.Push(doc.Root);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            if (n.IsElement)
            {
                list.Add(n);
            }

            var kids = new List<Node>();
            for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.IsElement)
                {
                    kids.Add(c);
                }
            }

            for (int i = kids.Count - 1; i >= 0; i--)
            {
                stack.Push(kids[i]);
            }
        }

        return list;
    }

    private static int CountElements(Node n)
    {
        int count = 0;
        var stack = new Stack<Node>();
        for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            stack.Push(c);
        }

        while (stack.Count > 0)
        {
            Node x = stack.Pop();
            if (x.IsElement)
            {
                count++;
                for (Node c = x.FirstChild; !c.IsNull; c = c.NextSibling)
                {
                    stack.Push(c);
                }
            }
        }

        return count;
    }

    private static Node? FirstTextWithLetter(Node e)
    {
        var stack = new Stack<Node>();
        stack.Push(e);
        Node? best = null;
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind == NodeKind.Text && best is null && c.Data.ToString().Any(ch => ch is 'a' or 'e' or 'i' or 'o' or 'u' or 'A' or 'E' or 'O') && c.Source.DataRange.End > c.Source.DataRange.Start)
                {
                    best = c;
                }
                else if (c.IsElement)
                {
                    stack.Push(c);
                }
            }
        }

        return best;
    }

    private static Node? UnrelatedText(List<Node> elements, int ordinal, Random rng)
    {
        Node t = elements[ordinal];
        for (int tries = 0; tries < 30; tries++)
        {
            Node e = elements[rng.Next(elements.Count)];
            bool related = false;
            for (Node x = e; x.IsElement; x = x.Parent)
            {
                if (x == t)
                {
                    related = true;
                    break;
                }
            }

            for (Node x = t; x.IsElement && !related; x = x.Parent)
            {
                if (x == e)
                {
                    related = true;
                }
            }

            if (related)
            {
                continue;
            }

            for (Node c = e.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind == NodeKind.Text && c.Data.ToString().Any(ch => ch is 'a' or 'e' or 'i' or 'o' or 'u'))
                {
                    return c;
                }
            }
        }

        return null;
    }

    public string Table()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-32} {1,7} {2,7} {3,8} {4,7} {5,9} {6,7} {7,6} {8,5} {9,9} {10,11}", "mutation", "tried", "skipped", "applied", "stale", "ambiguous", "invalid", "wrong", "twin", "lookalike", "unexpected"));
        foreach (string n in Names)
        {
            MutationRow r = _rows[n];
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-32} {1,7} {2,7} {3,8} {4,7} {5,9} {6,7} {7,6} {8,5} {9,9} {10,11}", n, r.Tried, r.Skipped, r.Applied, r.Stale, r.Ambiguous, r.Invalid, r.Wrong, r.Twin, r.Lookalike, r.Unexpected));
        }

        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "targets {0}, not editable even unmutated (skipped) {1}", Targets, BaselineFailed));
        return sb.ToString();
    }

    public int WrongTotal => _rows.Values.Sum(r => r.Wrong);
}

internal static class InlineStylesheetExtensions
{
    /// <summary>The offset in the document text where the style element's text starts, or -1 when the sheet is not one text node of the source.</summary>
    public static int TextNodeStart(this InlineStylesheet s, Document doc)
    {
        Node c = s.Element.FirstChild;
        if (c.IsNull || c.Kind != NodeKind.Text || !c.NextSibling.IsNull || c.Source.IsDisplaced)
        {
            return -1;
        }

        SourceRange r = c.Source.Range;
        return r.End - r.Start == s.Sheet.Text.Length && string.Equals(doc.Source.Text.Substring(r.Start, r.End - r.Start), s.Sheet.Text, StringComparison.Ordinal) ? r.Start : -1;
    }
}
