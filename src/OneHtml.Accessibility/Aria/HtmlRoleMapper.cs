using System;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneHtml.Accessibility.Aria;

/// <summary>The kind of ancestor that scopes <c>header</c>, <c>footer</c> and <c>aside</c> (HTML-AAM 3.5).</summary>
internal enum LandmarkScope : byte
{
    /// <summary>No sectioning ancestor: the element is scoped to the body.</summary>
    Body,

    /// <summary>The nearest of the scoping ancestors is a main element.</summary>
    Main,

    /// <summary>The nearest of the scoping ancestors is article, aside, nav or section.</summary>
    Sectioning,
}

/// <summary>What the traversal knows about an element's surroundings; the mapper needs nothing else from its ancestors.</summary>
internal readonly struct MapContext
{
    public MapContext(LandmarkScope scope, bool tableIsGrid, bool isFirstSummary, bool rowHasDataCell, bool inList)
    {
        InList = inList;
        Scope = scope;
        TableIsGrid = tableIsGrid;
        IsFirstSummary = isFirstSummary;
        RowHasDataCell = rowHasDataCell;
    }

    public LandmarkScope Scope { get; }

    /// <summary>True when the nearest ancestor with a role other than generic or presentational has the list role.</summary>
    public bool InList { get; }

    /// <summary>True when the nearest table ancestor has an explicit grid or treegrid role.</summary>
    public bool TableIsGrid { get; }

    /// <summary>True for a summary that is the first summary child of its details parent.</summary>
    public bool IsFirstSummary { get; }

    /// <summary>For a th, true when its row has a td cell; computed once per row by the traversal.</summary>
    public bool RowHasDataCell { get; }
}

/// <summary>The implicit role of an element, with a condition on the accessible name the later name computation resolves.</summary>
internal readonly struct ImplicitRole
{
    public ImplicitRole(AriaRole? role, AriaRole? roleWhenNamed = null)
    {
        Role = role;
        RoleWhenNamed = roleWhenNamed;
    }

    /// <summary>The role when the condition fails or is not evaluated; null when HTML-AAM gives the element no role.</summary>
    public AriaRole? Role { get; }

    /// <summary>When set, the role instead of <see cref="Role"/> if the element has an accessible name.</summary>
    public AriaRole? RoleWhenNamed { get; }
}

/// <summary>HTML-AAM section 3.5, "HTML Element Role Mappings", with the conditions the tables state.</summary>
internal static class HtmlRoleMapper
{
    private static AriaRole R(string name) => AriaTaxonomy.FindRole(name) ?? throw new InvalidOperationException("Unknown role " + name);

    private static readonly AriaRole s_generic = R("generic");
    private static readonly AriaRole s_none = R("none");

    public static ImplicitRole Map(Node n, in MapContext ctx)
    {
        switch (n.Namespace)
        {
            case ElementNamespace.Html:
                return MapHtml(n, ctx);
            case ElementNamespace.MathMl:
                // HTML-AAM 3.5 "math": the mapping is defined by MathML-AAM; the root is the math role.
                return n.LocalName == "math" ? new ImplicitRole(R("math")) : default;
            case ElementNamespace.Svg:
                // HTML-AAM 3.5 "svg" defers to SVG-AAM; only the root is mapped here (graphics-document, Graphics-ARIA).
                return n.LocalName == "svg" ? new ImplicitRole(R("graphics-document")) : default;
            default:
                return default;
        }
    }

    private static ImplicitRole Of(string role) => new(AriaTaxonomy.FindRole(role));

    private static ImplicitRole MapHtml(Node n, in MapContext ctx)
    {
        string name = n.LocalName;
        switch (name)
        {
            case "a":
            case "area":
                // el-a, el-a-no-href, el-area, el-area-no-href.
                return HtmlSemantics.Has(n, "href") ? Of("link") : new ImplicitRole(s_generic);
            case "address":
            case "details":
            case "fieldset":
            case "hgroup":
            case "optgroup":
                return Of("group");
            case "article":
                return Of("article");
            case "aside":
                // el-aside-ancestorbodymain, el-aside.
                return ctx.Scope == LandmarkScope.Sectioning ? new ImplicitRole(s_generic, R("complementary")) : Of("complementary");
            case "blockquote":
                return Of("blockquote");
            case "body":
            case "html":
            case "b":
            case "bdi":
            case "bdo":
            case "data":
            case "div":
            case "i":
            case "pre":
            case "q":
            case "samp":
            case "small":
            case "span":
            case "u":
                return new ImplicitRole(s_generic);
            case "button":
                return Of("button");
            case "caption":
            case "figcaption":
                return Of("caption");
            case "code":
                return Of("code");
            case "datalist":
                return Of("listbox");
            case "dd":
                return Of("definition");
            case "del":
            case "s":
                return Of("deletion");
            case "dfn":
            case "dt":
                return Of("term");
            case "dialog":
                return Of("dialog");
            case "dir":
            case "dl":
            case "menu":
            case "ol":
            case "ul":
                return Of("list");
            case "em":
                return Of("emphasis");
            case "figure":
                return Of("figure");
            case "footer":
                // el-footer-ancestorbody, el-footer.
                return ctx.Scope == LandmarkScope.Body ? Of("contentinfo") : new ImplicitRole(s_generic);
            case "header":
                // el-header-ancestorbody, el-header.
                return ctx.Scope == LandmarkScope.Body ? Of("banner") : new ImplicitRole(s_generic);
            case "form":
                // el-form: HTML-AAM maps form to the form role. Chromium exposes it with or without a name (profile entry
                // unnamed-form), although the role is a landmark only when named.
                return Of("form");
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                return Of("heading");
            case "hr":
                return Of("separator");
            case "img":
                return MapImg(n);
            case "input":
                return MapInput(n);
            case "ins":
                return Of("insertion");
            case "li":
                // el-li: listitem when its accessibility parent is a list; otherwise the generic role.
                return ctx.InList ? Of("listitem") : new ImplicitRole(s_generic);
            case "main":
                return Of("main");
            case "mark":
                return Of("mark");
            case "meter":
                return Of("meter");
            case "nav":
                return Of("navigation");
            case "option":
                return MapOption(n);
            case "output":
                return Of("status");
            case "p":
                return Of("paragraph");
            case "progress":
                return Of("progressbar");
            case "search":
                return Of("search");
            case "section":
                // el-section: region only with an accessible name.
                return new ImplicitRole(s_generic, R("region"));
            case "select":
                return MapSelect(n);
            case "strong":
                return Of("strong");
            case "sub":
                return Of("subscript");
            case "sup":
                return Of("superscript");
            case "summary":
                // el-summary: a summary that is not the first summary child of a details has the generic role;
                // the first has no mapping of its own (the disclosure widget is exposed by the platform).
                return ctx.IsFirstSummary ? default : new ImplicitRole(s_generic);
            case "table":
                return Of("table");
            case "tbody":
            case "tfoot":
            case "thead":
                return Of("rowgroup");
            case "td":
                return ctx.TableIsGrid ? Of("gridcell") : Of("cell");
            case "th":
                return MapTh(n, ctx);
            case "textarea":
                return Of("textbox");
            case "time":
                return Of("time");
            case "tr":
                return Of("row");
            case "abbr":
            case "audio":
            case "base":
            case "br":
            case "canvas":
            case "cite":
            case "col":
            case "colgroup":
            case "embed":
            case "head":
            case "iframe":
            case "kbd":
            case "label":
            case "legend":
            case "link":
            case "map":
            case "meta":
            case "noscript":
            case "object":
            case "param":
            case "picture":
            case "rp":
            case "rt":
            case "ruby":
            case "script":
            case "slot":
            case "source":
            case "style":
            case "template":
            case "title":
            case "track":
            case "var":
            case "video":
            case "wbr":
                // "No corresponding role".
                return default;
            default:
                // el-autonomous-custom-element, el-form-associated-custom-element, and any element HTML-AAM does
                // not list (font, center, marquee, ...): the generic role.
                return new ImplicitRole(s_generic);
        }
    }

    private static ImplicitRole MapImg(Node n)
    {
        // el-img, el-img-empty-alt: an alt that is empty after trimming whitespace makes the image presentational
        // unless the author named it with aria-label or aria-labelledby; a title does not count.
        if (HtmlSemantics.TryGetAttribute(n, "alt", out ReadOnlySpan<char> alt) && AsciiText.Trim(alt).IsEmpty)
        {
            return HasAriaName(n) ? Of("img") : new ImplicitRole(s_none);
        }

        return Of("img");
    }

    /// <summary>True when aria-label has text or aria-labelledby names at least one element of the document.</summary>
    private static bool HasAriaName(Node n)
    {
        if (HtmlSemantics.TryGetAttribute(n, "aria-label", out ReadOnlySpan<char> label) && !AsciiText.Trim(label).IsEmpty)
        {
            return true;
        }

        if (HtmlSemantics.TryGetAttribute(n, "aria-labelledby", out ReadOnlySpan<char> ids))
        {
            int pos = 0;
            while (AsciiText.NextToken(ids, ref pos, out ReadOnlySpan<char> id))
            {
                if (!n.Document!.GetElementById(id.ToString()).IsNull)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ImplicitRole MapInput(Node n)
    {
        switch (HtmlSemantics.GetInputType(n))
        {
            case HtmlInputType.Button:
            case HtmlInputType.Image:
            case HtmlInputType.Reset:
            case HtmlInputType.Submit:
                return Of("button");
            case HtmlInputType.Checkbox:
                // The `switch` attribute is the HTML-AAM draft's switch mapping.
                return HtmlSemantics.Has(n, "switch") ? Of("switch") : Of("checkbox");
            case HtmlInputType.Radio:
                return Of("radio");
            case HtmlInputType.Number:
                return Of("spinbutton");
            case HtmlInputType.Range:
                return Of("slider");
            case HtmlInputType.Password:
                // HTML-AAM lists no role for a password field; Chromium exposes a text box (profile entry input-password-file).
                return Of("textbox");
            case HtmlInputType.File:
                // Likewise a file picker is exposed as a button.
                return Of("button");
            case HtmlInputType.Search:
                return HasSuggestions(n) ? Of("combobox") : Of("searchbox");
            case HtmlInputType.Text:
            case HtmlInputType.Telephone:
            case HtmlInputType.Url:
            case HtmlInputType.Email:
                // el-input-textetc-autocomplete: a list attribute naming a datalist makes it a combobox.
                return HasSuggestions(n) ? Of("combobox") : Of("textbox");
            default:
                // color, date, datetime-local, hidden, month, time, week.
                return default;
        }
    }

    /// <summary>HTML 4.10.5.1.? "suggestions source element": the list attribute names a datalist in the same tree.</summary>
    private static bool HasSuggestions(Node n)
    {
        if (!HtmlSemantics.TryGetAttribute(n, "list", out ReadOnlySpan<char> id) || id.IsEmpty)
        {
            return false;
        }

        Node target = n.Document!.GetElementById(id.ToString());
        return !target.IsNull && HtmlSemantics.IsHtml(target, "datalist");
    }

    private static ImplicitRole MapOption(Node n)
    {
        // el-option: an option in a list of options (select, optgroup in select, datalist).
        Node p = n.ParentElement;
        if (!p.IsNull && HtmlSemantics.IsHtml(p, "optgroup"))
        {
            p = p.ParentElement;
        }

        return !p.IsNull && HtmlSemantics.IsHtml(p) && p.LocalName is "select" or "datalist" ? Of("option") : default;
    }

    private static ImplicitRole MapSelect(Node n)
    {
        // el-select-listbox, el-select-combobox: rendered as a list box when multiple or size > 1.
        if (HtmlSemantics.Has(n, "multiple"))
        {
            return Of("listbox");
        }

        return HtmlSemantics.TryGetAttribute(n, "size", out ReadOnlySpan<char> size)
            && HtmlSemantics.TryParseInteger(size, out long v) && v > 1
            ? Of("listbox")
            : Of("combobox");
    }

    private static ImplicitRole MapTh(Node n, in MapContext ctx)
    {
        // el-th, el-th-gridcell, el-th-columnheader, el-th-rowheader.
        HtmlSemantics.TryGetAttribute(n, "scope", out ReadOnlySpan<char> scope);
        if (HtmlSemantics.EqualsToken(scope, "col") || HtmlSemantics.EqualsToken(scope, "colgroup"))
        {
            return Of("columnheader");
        }

        if (HtmlSemantics.EqualsToken(scope, "row") || HtmlSemantics.EqualsToken(scope, "rowgroup"))
        {
            return Of("rowheader");
        }

        // Auto state: the header-cell kind of HTML 4.9.11 is approximated; see the Chromium profile.
        Node row = n.ParentElement;
        if (!row.IsNull && HtmlSemantics.IsHtml(row, "tr"))
        {
            Node group = row.ParentElement;
            if (!group.IsNull && HtmlSemantics.IsHtml(group, "thead"))
            {
                return Of("columnheader");
            }

            if (ctx.RowHasDataCell)
            {
                return Of("rowheader");
            }
        }

        return Of("columnheader");
    }
}
