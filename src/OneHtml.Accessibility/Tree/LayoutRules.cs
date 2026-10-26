using System;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Tree;

/// <summary>
/// The choices Chromium makes once layout is known, read from <see cref="RenderedFacts"/>. Every rule here is an observation of
/// Chrome's <c>Accessibility.getFullAXTree</c> output and is written down in <c>docs/accessibility-chromium-profile.md</c>
/// (entries <c>layout-generic-pruning</c> and <c>layout-table</c>). Each answers only from the facts of the nodes it asks about and
/// falls back to "no opinion" when the capture lacks the computed style it needs.
/// </summary>
internal sealed class LayoutRules
{
    private const int MaxTableScan = 20_000;
    private const int DataTableRows = 20;

    private readonly RenderedFacts _facts;
    private readonly DocumentStyles _styles;
    private readonly bool _canJudgeBoxes;

    public LayoutRules(RenderedFacts facts, DocumentStyles styles)
    {
        _facts = facts;
        _styles = styles;
        var names = new System.Collections.Generic.HashSet<string>(facts.ComputedStyleNames, StringComparer.Ordinal);
        _canJudgeBoxes = names.Contains("display") && names.Contains("position") && names.Contains("float");
    }

    public RenderedFacts Facts => _facts;

    // ---- generic containers (profile entry layout-generic-pruning) ----

    /// <summary>
    /// True when Chromium leaves a generic container out of what assistive technology walks although nothing in the markup or
    /// roles says so. The container is kept when it is a <c>section</c>, <c>header</c>, <c>footer</c>, <c>aside</c> or <c>a</c>,
    /// has an <c>id</c>, <c>title</c>, <c>tabindex</c>, <c>role</c> or any <c>aria-*</c> attribute, is positioned, is
    /// inline-level but not a plain inline box, or is a block whose own children are inline content (text, inline boxes, floats or
    /// out-of-flow boxes) with no in-flow block child. A <c>span</c> and any inline box are left out. Flex, grid and table
    /// containers are left out. An element with no box returns false (it is hidden, or has no opinion).
    /// </summary>
    public bool IsUninterestingGeneric(Node el)
    {
        if (!_canJudgeBoxes || !_facts.TryGet(el, out NodeFacts f) || !f.HasLayout)
        {
            return false;
        }

        string tag = el.LocalName;
        if (tag is "section" or "header" or "footer" or "aside" or "a" || HasInterestingAttribute(el))
        {
            return false;
        }

        string display = f.GetStyle("display") ?? string.Empty;
        if (tag == "span" || display == "inline")
        {
            return true;
        }

        if (IsScrollContainer(f))
        {
            return false;
        }

        if (display.StartsWith("inline-", StringComparison.Ordinal) || display.StartsWith("-webkit-inline", StringComparison.Ordinal))
        {
            return false;
        }

        string? position = f.GetStyle("position");
        if (position is not null && position != "static")
        {
            return false;
        }

        if (display is not ("block" or "flow-root" or "list-item"))
        {
            return true;
        }

        return !HasInlineContent(el);
    }

    /// <summary>
    /// True when Chromium leaves a <c>p</c> out as uninteresting: a block with no inline content (empty, or holding only block
    /// children) and none of the attributes that keep a generic container.
    /// </summary>
    public bool IsUninterestingParagraph(Node p)
    {
        if (!_canJudgeBoxes || !_facts.TryGet(p, out NodeFacts f) || !f.HasLayout || HasInterestingAttribute(p))
        {
            return false;
        }

        return f.GetStyle("display") is "block" or "flow-root" or "list-item" && !HasInlineContent(p);
    }

    /// <summary>True when the node (an element or a text node) generated a box.</summary>
    public bool HasBox(Node el) => _canJudgeBoxes && _facts.TryGet(el, out NodeFacts f) && f.HasLayout;

    /// <summary>True when the element's box is a plain inline box (computed <c>display: inline</c>).</summary>
    public bool IsInlineBox(Node el) => _facts.TryGet(el, out NodeFacts f) && f.HasLayout && f.GetStyle("display") == "inline";

    /// <summary>True for a box that scrolls: overflow auto or scroll on an axis, and content larger than the padding box on that axis.</summary>
    private static bool IsScrollContainer(NodeFacts f)
    {
        BoxFacts box = f.Layout;
        if (box.ClientRect is not { } client || box.ScrollRect is not { } scroll)
        {
            return false;
        }

        bool x = f.GetStyle("overflow-x") is "auto" or "scroll" && scroll.Width > client.Width + 0.5;
        bool y = f.GetStyle("overflow-y") is "auto" or "scroll" && scroll.Height > client.Height + 0.5;
        return x || y;
    }

    private static bool HasInterestingAttribute(Node el)
    {
        foreach (Attr a in el.Attributes)
        {
            string n = a.LocalName;
            if (n is "id" or "title" or "tabindex" or "role" || n.StartsWith("aria-", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the element's own children make it a block flow with inline content: at least one child box, and no in-flow
    /// block-level child. Text counts when it has a layout box (collapsed white space has none); an element that generates no box
    /// counts for nothing; a <c>display: contents</c> element is replaced by its children.
    /// </summary>
    private bool HasInlineContent(Node el)
    {
        bool content = false;
        Node n = el.FirstChild;
        while (!n.IsNull)
        {
            bool descend = false;
            if (n.Kind == NodeKind.Text)
            {
                if (_facts.TryGet(n, out NodeFacts tf) && tf.HasLayout)
                {
                    content = true;
                }
            }
            else if (n.IsElement)
            {
                if (_facts.TryGet(n, out NodeFacts cf) && cf.HasLayout)
                {
                    string? pos = cf.GetStyle("position");
                    string? flt = cf.GetStyle("float");
                    if (pos is "absolute" or "fixed" || (flt is not null && flt != "none"))
                    {
                        content = true;
                    }
                    else if (IsInlineLevel(cf.GetStyle("display")))
                    {
                        content = true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (_styles.GetStyle(n).Display.Kind == DisplayKind.Contents)
                {
                    descend = true;
                }
            }

            if (descend && !n.FirstChild.IsNull)
            {
                n = n.FirstChild;
                continue;
            }

            while (!n.IsNull && n.NextSibling.IsNull)
            {
                n = n.Parent;
                if (n.IsNull || n.Index == el.Index)
                {
                    return content;
                }
            }

            n = n.NextSibling;
        }

        return content;
    }

    private static bool IsInlineLevel(string? display) =>
        display is not null && (display == "inline" || display.StartsWith("inline-", StringComparison.Ordinal) || display.StartsWith("ruby", StringComparison.Ordinal) || display.StartsWith("-webkit-inline", StringComparison.Ordinal));

    // ---- white space (profile entry layout-white-space) ----

    /// <summary>
    /// True for a white-space-only text node that was drawn (it has a text box) between two inline boxes (its previous and next
    /// siblings are elements with <c>display: inline</c>). Chromium has a static text for those only.
    /// </summary>
    public bool IsRenderedWhiteSpace(Node text)
    {
        if (!_facts.TryGet(text, out NodeFacts f) || !f.HasLayout || f.Layout.TextBoxes.Length == 0)
        {
            return false;
        }

        return IsInlineSibling(text.PreviousSibling) && IsInlineSibling(text.NextSibling);
    }

    private bool IsInlineSibling(Node n) => n.IsElement && _facts.TryGet(n, out NodeFacts f) && f.HasLayout && f.GetStyle("display") == "inline" && f.GetStyle("position") is not ("absolute" or "fixed") && f.GetStyle("float") is null or "none";

    // ---- list markers (profile entry list-marker-node) ----

    /// <summary>The text of the element's <c>::marker</c> box when it has one with text (not for <c>summary</c>, whose marker Chromium exposes as a disclosure triangle).</summary>
    public bool TryGetListMarker(Node el, out string text)
    {
        text = string.Empty;
        if (!_facts.TryGet(el, out NodeFacts f) || !f.HasLayout || (el.Namespace == ElementNamespace.Html && el.LocalName == "summary"))
        {
            return false;
        }

        if (f.GetStyle("visibility") is "hidden" or "collapse")
        {
            return false;
        }

        foreach (GeneratedBox g in f.GeneratedBoxes)
        {
            if (g.PseudoType == "marker" && g.HasLayout && g.Text.Length > 0)
            {
                text = g.Text;
                return true;
            }
        }

        return false;
    }

    // ---- tables (profile entry layout-table) ----

    /// <summary>
    /// True when Chromium treats an HTML <c>table</c> as a data table (role <c>table</c>), false when as a layout table
    /// (internal role <c>LayoutTable</c>). A table is a data table when it has a <c>summary</c> attribute or any <c>aria-*</c>
    /// attribute, a caption, or a cell with a <c>scope</c> attribute; or, when it has more than one cell, a header cell, a
    /// <c>thead</c>, or a visible border on the table or on its first cell; or, whatever else it has, when it has 20 rows or more.
    /// Cells, captions, headers and rows of a table nested inside this one belong to the nested table.
    /// </summary>
    public bool IsDataTable(Node table)
    {
        foreach (Attr a in table.Attributes)
        {
            if (a.LocalName == "summary" || a.LocalName.StartsWith("aria-", StringComparison.Ordinal))
            {
                return true;
            }
        }

        bool bordered = HasBorder(table);
        bool headers = false;
        int cells = 0;
        int rows = 0;
        bool firstCell = true;
        int scanned = 0;
        Node d = table.FirstChild;
        while (!d.IsNull)
        {
            // Nested tables would make a scan per table quadratic; evidence past this many nodes is not looked for.
            if (++scanned > MaxTableScan)
            {
                break;
            }

            bool skip = false;
            if (d.Kind == NodeKind.Element && d.Namespace == ElementNamespace.Html)
            {
                switch (d.LocalName)
                {
                    case "table":
                        skip = true;
                        break;
                    case "caption":
                        return true;
                    case "tr":
                        rows++;
                        break;
                    case "thead":
                        headers = true;
                        break;
                    case "th" or "td":
                        cells++;
                        headers |= d.LocalName == "th";
                        if (d.HasAttribute("scope"))
                        {
                            return true;
                        }

                        if (firstCell)
                        {
                            firstCell = false;
                            bordered |= HasBorder(d);
                        }

                        break;
                }
            }

            // Next node in document order within the table, not entering a nested table.
            Node next = skip ? default : d.FirstChild;
            if (next.IsNull)
            {
                Node up = d;
                while (true)
                {
                    if (!up.NextSibling.IsNull)
                    {
                        next = up.NextSibling;
                        break;
                    }

                    up = up.Parent;
                    if (up.IsNull || up.Index == table.Index)
                    {
                        break;
                    }
                }
            }

            d = next;
        }

        // A long table is a data table without any other evidence; a table of one cell is a layout table whatever its borders or
        // header cell.
        return rows >= DataTableRows || (cells > 1 && (headers || bordered));
    }

    /// <summary>
    /// The internal role Chromium gives an element that is not an HTML table part but is laid out as a CSS table (<c>display: table</c>),
    /// table row or table cell: <c>LayoutTable</c>, <c>LayoutTableRow</c> or <c>LayoutTableCell</c>; null for any other display.
    /// </summary>
    public string? CssTableKind(Node el) => !_canJudgeBoxes || !_facts.TryGet(el, out NodeFacts f) || !f.HasLayout
        ? null
        : f.GetStyle("display") switch
        {
            "table" => "LayoutTable",
            "table-row" => "LayoutTableRow",
            "table-cell" => "LayoutTableCell",
            _ => null,
        };

    private bool HasBorder(Node el)
    {
        if (!_facts.TryGet(el, out NodeFacts f) || !f.HasLayout)
        {
            return false;
        }

        string? w = f.GetStyle("border-top-width");
        return w is not null && w != "0px" && f.GetStyle("border-top-style") is not (null or "none" or "hidden");
    }
}
