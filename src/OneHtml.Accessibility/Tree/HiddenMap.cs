using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Tree;

/// <summary>
/// Hidden status of every element and text node: what the cascade and the markup hide, computed in one walk with no
/// recursion. Layout-dependent hiding is part of it only when rendered facts are given (see <see cref="LayoutHiding"/>).
/// </summary>
internal sealed class HiddenMap
{
    internal const HiddenReasons NotRenderedMask = HiddenReasons.DisplayNone | HiddenReasons.HiddenAttribute | HiddenReasons.Template | HiddenReasons.ClosedDetails | HiddenReasons.ContentVisibilityHidden;

    private readonly Document _doc;
    private readonly HiddenReasons[] _all;
    private readonly HiddenReasons[] _own;
    private readonly int[] _hardOrigin;
    private readonly int[] _ariaOrigin;
    private readonly int[] _inertOrigin;
    private Rendered.RenderedFacts? _facts;

    private HiddenMap(Document doc)
    {
        _doc = doc;
        int n = doc.NodeCount;
        _all = new HiddenReasons[n];
        _own = new HiddenReasons[n];
        _hardOrigin = new int[n];
        _ariaOrigin = new int[n];
        _inertOrigin = new int[n];
    }

    public static HiddenMap Compute(Document doc, DocumentStyles styles, System.Threading.CancellationToken ct, Rendered.RenderedFacts? facts = null)
    {
        var map = new HiddenMap(doc);
        map._facts = facts is not null && new System.Collections.Generic.HashSet<string>(facts.ComputedStyleNames, StringComparer.Ordinal).Contains("display") ? facts : null;
        Node cur = doc.Root.FirstChild;
        int visited = 0;
        while (!cur.IsNull)
        {
            if ((++visited & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            if (cur.IsElement || cur.Kind == NodeKind.Text)
            {
                map.Visit(cur, styles);
            }

            Node next = cur.FirstChild;
            if (next.IsNull)
            {
                // The next sibling, climbing while there is none.
                Node c = cur;
                while (!c.IsNull)
                {
                    next = c.NextSibling;
                    if (!next.IsNull)
                    {
                        break;
                    }

                    c = c.Parent;
                    if (c.Index == 0)
                    {
                        break;
                    }
                }
            }

            cur = next;
        }

        return map;
    }

    private void Visit(Node n, DocumentStyles styles)
    {
        int i = n.Index;
        Node parent = n.Parent;
        int p = parent.Index;
        HiddenReasons inherited = parent.IsElement ? _all[p] & ~HiddenReasons.VisibilityHidden : HiddenReasons.None;
        HiddenReasons own = HiddenReasons.None;

        if (n.IsElement)
        {
            ComputedStyle style = styles.GetStyle(n);
            if (!style.Display.IsRendered)
            {
                bool html = n.Namespace == ElementNamespace.Html;
                string? hidden = html ? n.GetAttribute("hidden") : null;
                if (n.LocalName == "template" && html)
                {
                    own |= HiddenReasons.Template;
                }
                else if (hidden is not null && !string.Equals(hidden, "until-found", StringComparison.OrdinalIgnoreCase)
                    && style.GetSource("display") is { Origin: StyleOrigin.UserAgent })
                {
                    own |= HiddenReasons.HiddenAttribute;
                }
                else if (!(html && n.LocalName == "area" && style.GetSource("display") is { Origin: StyleOrigin.UserAgent } && HasMapAncestor(n)))
                {
                    // An area of an image map is exposed although the rendering rules give it display: none.
                    own |= HiddenReasons.DisplayNone;
                }
            }

            if (n.Namespace == ElementNamespace.Html && n.LocalName == "noscript" && (own & NotRenderedMask) == 0)
            {
                // HTML section 15.3.1: noscript is display: none when scripting is enabled, as it is in a browser (profile entry noscript-not-rendered).
                own |= HiddenReasons.DisplayNone;
            }

            if ((own & NotRenderedMask) == 0 && (inherited & NotRenderedMask) == 0 && NoBoxDespiteStyle(n, style))
            {
                // Profile entry layout-hidden: the capture says the element has no box although the cascade renders it.
                own |= HiddenReasons.DisplayNone;
            }

            if (style.Visibility != VisibilityValue.Visible)
            {
                own |= HiddenReasons.VisibilityHidden;
            }

            if (HasToken(n.GetAttribute("aria-hidden"), "true"))
            {
                own |= HiddenReasons.AriaHidden;
            }

            if (n.Namespace == ElementNamespace.Html && n.HasAttribute("inert"))
            {
                own |= HiddenReasons.Inert;
            }
        }

        if (parent.IsElement)
        {
            // HTML section 4.11.1: the contents of a details element that is not open are not rendered, but its first summary is.
            if (parent.Namespace == ElementNamespace.Html && parent.LocalName == "details" && !parent.HasAttribute("open") && !IsFirstSummary(n))
            {
                own |= HiddenReasons.ClosedDetails;
            }

            // CSS Containment 2 section 4.2: content-visibility: hidden skips the contents of the element.
            if (inherited == HiddenReasons.None || (inherited & NotRenderedMask) == 0)
            {
                if (styles.GetStyle(parent).GetPropertyValue("content-visibility").Trim() == "hidden")
                {
                    own |= HiddenReasons.ContentVisibilityHidden;
                }
            }
        }

        _own[i] = own;
        _all[i] = own | inherited;
        HiddenReasons all = _all[i];
        if ((all & NotRenderedMask) != 0)
        {
            _hardOrigin[i] = (own & NotRenderedMask) != 0 ? OriginForOwnHard(n, own, p) : _hardOrigin[p];
        }

        if ((all & HiddenReasons.AriaHidden) != 0)
        {
            _ariaOrigin[i] = (own & HiddenReasons.AriaHidden) != 0 ? i : _ariaOrigin[p];
        }

        if ((all & HiddenReasons.Inert) != 0)
        {
            _inertOrigin[i] = (own & HiddenReasons.Inert) != 0 ? i : _inertOrigin[p];
        }

        if (n.Kind == NodeKind.Text)
        {
            // A text node is as visible as its parent element.
            if (parent.IsElement && (_all[p] & HiddenReasons.VisibilityHidden) != 0)
            {
                _all[i] |= HiddenReasons.VisibilityHidden;
            }
        }
    }

    /// <summary>
    /// True when rendered facts say the element generated no box although its style renders it. Exempt: <c>display: contents</c>
    /// (it never has one), the options of a select (they live in the control's user-agent tree), image-map areas, and anything
    /// that is not an HTML element (SVG and MathML internals have no boxes of their own).
    /// </summary>
    private bool NoBoxDespiteStyle(Node n, ComputedStyle style)
    {
        if (_facts is null || n.Namespace != ElementNamespace.Html || style.Display.Kind == DisplayKind.Contents || !style.Display.IsRendered)
        {
            return false;
        }

        if (n.LocalName is "option" or "optgroup" or "area" or "map" or "slot" or "html" or "body" || !_facts.TryGet(n, out Rendered.NodeFacts f))
        {
            return false;
        }

        return !f.HasLayout;
    }

    private static bool HasMapAncestor(Node n)
    {
        for (Node p = n.Parent; !p.IsNull; p = p.Parent)
        {
            if (p.IsElement && p.Namespace == ElementNamespace.Html && p.LocalName == "map")
            {
                return true;
            }
        }

        return false;
    }

    private static int OriginForOwnHard(Node n, HiddenReasons own, int parentIndex)
    {
        // Closed details and content-visibility are set by the parent; the others by the node itself.
        return (own & (HiddenReasons.ClosedDetails | HiddenReasons.ContentVisibilityHidden)) != 0 && (own & (HiddenReasons.DisplayNone | HiddenReasons.HiddenAttribute | HiddenReasons.Template)) == 0
            ? parentIndex
            : n.Index;
    }

    private static bool IsFirstSummary(Node n)
    {
        if (!n.IsElement || n.Namespace != ElementNamespace.Html || n.LocalName != "summary")
        {
            return false;
        }

        for (Node s = n.PreviousSibling; !s.IsNull; s = s.PreviousSibling)
        {
            if (s.IsElement && s.Namespace == ElementNamespace.Html && s.LocalName == "summary")
            {
                return false;
            }
        }

        return true;
    }

    internal static bool HasToken(string? value, string token) => value is not null && string.Equals(value.Trim(' ', '\t', '\n', '\f', '\r'), token, StringComparison.OrdinalIgnoreCase);

    public HiddenReasons Reasons(int index) => _all[index];

    public HiddenReasons Own(int index) => _own[index];

    /// <summary>True when the node generates no object in the tree.</summary>
    public bool NotRendered(int index) => (_all[index] & NotRenderedMask) != 0;

    public HiddenInfo Get(Node n)
    {
        int i = n.Index;
        HiddenReasons r = _all[i];
        if (r == HiddenReasons.None)
        {
            return new HiddenInfo(HiddenReasons.None, default, false);
        }

        var tmp = new HiddenInfo(r, default, false);
        int origin = tmp.Primary switch
        {
            HiddenReasons.AriaHidden => _ariaOrigin[i],
            HiddenReasons.Inert => _inertOrigin[i],
            HiddenReasons.VisibilityHidden => n.Kind == NodeKind.Text ? n.Parent.Index : i,
            _ => _hardOrigin[i],
        };
        return new HiddenInfo(r, _doc.GetNode(origin), origin != i);
    }
}
