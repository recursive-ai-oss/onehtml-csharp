using System;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Accessibility.Aria;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Tree;

/// <summary>Builds an <see cref="AccessibilityTree"/>; every walk uses an explicit stack or a cursor.</summary>
internal sealed class TreeBuilder
{
    private readonly Document _doc;
    private readonly DocumentStyles _styles;
    private readonly AccessibilityOptions _opt;
    private readonly CancellationToken _ct;
    private readonly UpdatePlan? _plan;
    private CounterResolver _counters = null!;
    private readonly LayoutRules? _layout;
    private readonly Dictionary<int, bool> _dataTables = [];

    private HiddenMap _hidden = null!;
    private OwnsMap _owns = null!;
    private RoleMap _roles = null!;
    private NameComputer _names = null!;
    private AccessibilityTree _tree = null!;
    private AxNode?[] _byDom = null!;
    private readonly List<AxNode> _domOrder = [];
    private readonly List<AxDiagnostic> _diags = [];
    private readonly Dictionary<AxNode, List<AxDiagnostic>> _nodeDiags = [];

    public TreeBuilder(Document doc, DocumentStyles styles, AccessibilityOptions options, UpdatePlan? plan, CancellationToken ct)
    {
        _plan = plan;
        _doc = doc;
        _styles = styles;
        _opt = options;
        _ct = ct;
        _layout = options.Facts is { } facts ? new LayoutRules(facts, styles) : null;
    }

    public AccessibilityTree Run()
    {
        _hidden = HiddenMap.Compute(_doc, _styles, _ct, _opt.Facts);
        _owns = OwnsMap.Compute(_doc, _hidden, _opt, _ct);

        // Roles that depend on a name (a section is a region only when named, a role=region token only when named) need
        // names, and names need roles for a few decisions (embedded controls, contents). A first pass with the
        // name-dependent roles unresolved gives names good enough to decide them; the second pass fixes the roles.
        var counters = _counters = new CounterResolver(_doc, _styles, _hidden);
        RoleMap roles0 = RoleComputer.Compute(_doc, null, _ct);
        var probeNames = new NameComputer(_doc, _styles, _hidden, roles0, _owns, _opt, counters, _ct);
        _roles = RoleComputer.Compute(_doc, new RoleOptions { HasAccessibleName = n => !probeNames.ComputeName(n).IsEmpty }, _ct);
        _names = new NameComputer(_doc, _styles, _hidden, _roles, _owns, _opt, counters, _ct);

        _tree = new AccessibilityTree(_doc, _styles, _opt, _hidden, _roles, _owns);
        _byDom = new AxNode?[_doc.NodeCount];
        CreateNodes();
        LinkChildren();
        AddGeneratedNodes();
        AssignIds();
        _plan?.Prepare(_hidden, _roles, _owns, _opt.MaxIdReferences, _ct);
        ComputeNodes();
        ResolveSvgRoots();
        _tree.UsedCounters = _counters.Walked;
        ComputeFocusOrder();
        ComputeChromiumFocusableOptions();
        ComputeListItemLevels();
        ComputePositions();
        CheckContextAndOwned();
        Collect();
        return _tree;
    }

    // ---- nodes ----

    private AxNode Create(AxNodeKind kind, Node dom)
    {
        var n = new AxNode(_tree, -1, kind, dom);
        _byDom[dom.Index] = n;
        _tree.Register(dom, n);
        _domOrder.Add(n);
        return n;
    }

    private void CreateNodes()
    {
        Create(AxNodeKind.Document, _doc.Root);
        Node cur = _doc.Root.FirstChild;
        int visited = 0;
        while (!cur.IsNull)
        {
            if ((++visited & 1023) == 0)
            {
                _ct.ThrowIfCancellationRequested();
            }

            bool descend = false;
            if (cur.IsElement)
            {
                if (!_hidden.NotRendered(cur.Index))
                {
                    if (!OmittedByLayout(cur))
                    {
                        Create(AxNodeKind.Element, cur);
                    }

                    // The text of a textarea or option is the control's value or label, not content a user reads on its own:
                    // Chromium exposes no children for either (profile entry text-control-leaves).
                    descend = !cur.FirstChild.IsNull && !IsLeafControl(cur);
                }
            }
            else if (cur.Kind == NodeKind.Text && !_hidden.NotRendered(cur.Index) && KeepText(cur))
            {
                Create(AxNodeKind.Text, cur);
            }

            if (descend)
            {
                cur = cur.FirstChild;
                continue;
            }

            while (!cur.IsNull)
            {
                Node next = cur.NextSibling;
                if (!next.IsNull)
                {
                    cur = next;
                    break;
                }

                cur = cur.Parent;
                if (cur.IsNull || cur.Index == 0)
                {
                    cur = default;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// With rendered facts, an inline generic container that Chromium leaves out as uninteresting has no node at all in
    /// Chromium's tree, as opposed to a block one, which is there but ignored (profile entry <c>layout-generic-pruning</c>). Its
    /// children belong to its nearest ancestor that has a node.
    /// </summary>
    private bool OmittedByLayout(Node el) =>
        _layout is not null && el.Namespace == ElementNamespace.Html && _roles[el].Role is { Name: "generic" } && _layout.IsUninterestingGeneric(el) && _layout.IsInlineBox(el);

    /// <summary>HTML section 4.11.1: the summary element that is the first summary child of a details element.</summary>
    private static bool IsFirstSummary(Node el)
    {
        if (el.LocalName != "summary" || el.ParentElement is not { IsNull: false } p || p.Namespace != ElementNamespace.Html || p.LocalName != "details")
        {
            return false;
        }

        for (Node s = el.PreviousElementSibling; !s.IsNull; s = s.PreviousElementSibling)
        {
            if (s.Namespace == ElementNamespace.Html && s.LocalName == "summary")
            {
                return false;
            }
        }

        return true;
    }

    private string? _baseUrl;
    private bool _baseUrlKnown;

    /// <summary>The URL a link or image refers to: <paramref name="value"/> resolved against the base URL when there is one.</summary>
    private string ResolveUrl(string value)
    {
        if (!_baseUrlKnown)
        {
            _baseUrlKnown = true;
            if (_opt.BaseUrl is { Length: > 0 } b && Uri.TryCreate(b, UriKind.Absolute, out Uri? baseUri))
            {
                Node baseEl = _doc.Root.QuerySelector("base[href]");
                if (!baseEl.IsNull && Uri.TryCreate(baseUri, baseEl.GetAttribute("href"), out Uri? withBase))
                {
                    baseUri = withBase;
                }

                _baseUrl = baseUri.AbsoluteUri;
            }
        }

        if (_baseUrl is not null && Uri.TryCreate(new Uri(_baseUrl), TreeText.Trim(value), out Uri? resolved))
        {
            return resolved.AbsoluteUri;
        }

        return value;
    }

    private static bool IsLeafControl(Node el) =>
        el.Namespace == ElementNamespace.Html ? el.LocalName is "textarea" or "option" : el.Namespace == ElementNamespace.Svg && el.LocalName is "title" or "desc" or "metadata";

    /// <summary>Text that is only white space is kept only between inline neighbours, where it is a visible space.</summary>
    private bool KeepText(Node text)
    {
        ReadOnlySpan<char> d = text.Data;
        if (d.IsEmpty)
        {
            return false;
        }

        foreach (char c in d)
        {
            if (!TreeText.IsWs(c))
            {
                return true;
            }
        }

        // With rendered facts, only white space that is drawn between two inline boxes has a static text (profile entry layout-white-space).
        if (_layout is not null)
        {
            return _layout.IsRenderedWhiteSpace(text);
        }

        Node parent = text.Parent;
        return parent.IsElement && IsInline(parent);
    }

    private bool IsInline(Node el)
    {
        DisplayValue d = _styles.GetStyle(el).Display;
        return d.Kind == DisplayKind.Contents || (d.Kind == DisplayKind.Box && d.Outside == DisplayOutside.Inline && d.Inside == DisplayInside.Flow && !d.IsListItem);
    }

    private void LinkChildren()
    {
        var lists = new Dictionary<AxNode, List<AxNode>>();
        AxNode root = _domOrder[0];
        foreach (AxNode n in _domOrder)
        {
            if (n == root)
            {
                continue;
            }

            AxNode? parent = null;
            int owner = n.Kind == AxNodeKind.Element ? _owns.OwnerOf(n.DomNode.Index) : -1;
            if (owner < 0)
            {
                // The nearest ancestor with a node: the parent unless it was left out (OmittedByLayout).
                Node up = n.DomNode.Parent;
                while (!up.IsNull && _byDom[up.Index] is null)
                {
                    up = up.Parent;
                }

                parent = up.IsNull ? null : _byDom[up.Index];
                if (parent is null)
                {
                    continue; // cannot happen: a node is only created under a created parent
                }

                Add(lists, parent, n);
            }
        }

        // Then the owned elements, after the DOM children of their owner, in the order of aria-owns.
        foreach (AxNode n in _domOrder)
        {
            if (n.Kind != AxNodeKind.Element || _owns.OwnedBy(n.DomNode.Index) is not { } owned)
            {
                continue;
            }

            foreach (int idx in owned)
            {
                AxNode? child = _byDom[idx];
                if (child is not null)
                {
                    Add(lists, n, child);
                }
            }
        }

        foreach ((AxNode p, List<AxNode> l) in lists)
        {
            p.Children = l;
        }

        _tree.Root = root;
    }

    /// <summary>
    /// With rendered facts, gives each element whose layout has a list marker a ListMarker node as its first child, holding the
    /// marker's text, with the ignored text child Chromium puts under it (profile entry <c>list-marker-node</c>; plain static text when
    /// the element is not a list item), and gives a drop-down
    /// select its popup node.
    /// </summary>
    private void AddGeneratedNodes()
    {
        if (_layout is null)
        {
            return;
        }

        foreach (AxNode host in _domOrder)
        {
            if (host.Kind != AxNodeKind.Element || _hidden.Reasons(host.DomNode.Index) != HiddenReasons.None)
            {
                continue;
            }

            if (_roles[host.DomNode].Role is { Name: "combobox" } && host.DomNode.Namespace == ElementNamespace.Html && host.DomNode.LocalName == "select" && _layout.HasBox(host.DomNode))
            {
                // Profile entry layout-select-popup: a drop-down select has one MenuListPopup child that holds the options.
                var popup = new AxNode(_tree, -1, AxNodeKind.Generated, host.DomNode)
                {
                    Role = "MenuListPopup",
                    InternalRole = "MenuListPopup",
                    Parent = host,
                    Children = host.Children,
                    States = new AxStates(),
                };
                foreach (AxNode option in popup.Children)
                {
                    option.Parent = popup;
                }

                host.Children = [popup];
                continue;
            }

            if (!_layout.TryGetListMarker(host.DomNode, out string text))
            {
                continue;
            }

            AxNode marker;
            if (_roles[host.DomNode].Role is { Name: "listitem" })
            {
                marker = new AxNode(_tree, -1, AxNodeKind.Generated, host.DomNode)
                {
                    Role = "ListMarker",
                    InternalRole = "ListMarker",
                    Name = new AxText(text, AxTextSource.Contents, null, []),
                    Parent = host,
                    States = new AxStates(),
                };
                marker.Children =
                [
                    new AxNode(_tree, -1, AxNodeKind.Generated, host.DomNode)
                    {
                        Role = "none",
                        IsIgnored = true,
                        IgnoredReasons = ["presentationalRole"],
                        Parent = marker,
                        States = new AxStates(),
                    },
                ];
            }
            else
            {
                // An item that is not a list item for assistive technology keeps the marker as plain static text.
                marker = new AxNode(_tree, -1, AxNodeKind.Generated, host.DomNode)
                {
                    Role = "StaticText",
                    InternalRole = "StaticText",
                    Name = new AxText(text, AxTextSource.TextNode, null, []),
                    Parent = host,
                    States = new AxStates(),
                };
            }

            var children = new List<AxNode>(host.Children.Count + 1) { marker };
            children.AddRange(host.Children);
            host.Children = children;
        }
    }

    private static void Add(Dictionary<AxNode, List<AxNode>> lists, AxNode parent, AxNode child)
    {
        if (!lists.TryGetValue(parent, out List<AxNode>? l))
        {
            lists[parent] = l = [];
        }

        l.Add(child);
        child.Parent = parent;
    }

    private void AssignIds()
    {
        var order = new List<AxNode>(_domOrder.Count);
        var stack = new Stack<AxNode>();
        stack.Push(_tree.Root);
        while (stack.Count > 0)
        {
            AxNode n = stack.Pop();
            n.Id = order.Count;
            order.Add(n);
            IReadOnlyList<AxNode> ch = n.Children;
            for (int i = ch.Count - 1; i >= 0; i--)
            {
                stack.Push(ch[i]);
            }
        }

        _tree.Nodes = order;
    }

    // ---- per node ----

    private void ComputeNodes()
    {
        int visited = 0;
        foreach (AxNode n in _tree.Nodes)
        {
            if ((++visited & 255) == 0)
            {
                _ct.ThrowIfCancellationRequested();
            }

            switch (n.Kind)
            {
                case AxNodeKind.Document:
                    ComputeDocument(n);
                    break;
                case AxNodeKind.Text:
                    ComputeText(n);
                    break;
                case AxNodeKind.Generated:
                    break; // fully set when created (AddGeneratedNodes)
                default:
                    ComputeElement(n);
                    break;
            }
        }
    }

    /// <summary>
    /// Profile entry svg-root-role: Chromium exposes an svg as an image, and as SvgRoot (a container) when something inside it is
    /// exposed: rendered text, a link, a foreignObject, a role. Graphics elements and nested svg elements do not count. An svg with no
    /// children and no name is ignored.
    /// </summary>
    private void ResolveSvgRoots()
    {
        foreach (AxNode n in _tree.Nodes)
        {
            if (n.Kind != AxNodeKind.Element || n.InternalRole != "image" || n.IsIgnored)
            {
                continue;
            }

            bool nested = n.DomNode.ParentElement is { IsNull: false } parent && parent.Namespace == ElementNamespace.Svg;
            if ((n.DomNode.FirstChild.IsNull || nested) && n.Name.IsEmpty)
            {
                // An svg with nothing in it, or one nested in another svg, and no name has no node in Chromium's tree.
                n.InternalRole = null;
                n.IsIgnored = true;
                n.IgnoredReasons = ["uninteresting"];
            }
            else if (HasExposedContent(n))
            {
                n.InternalRole = "SvgRoot";
            }
        }
    }

    private static bool HasExposedContent(AxNode root)
    {
        var stack = new Stack<AxNode>();
        foreach (AxNode c in root.Children)
        {
            stack.Push(c);
        }

        while (stack.Count > 0)
        {
            AxNode c = stack.Pop();
            bool nestedSvg = c.Kind == AxNodeKind.Element && c.DomNode.Namespace == ElementNamespace.Svg && c.DomNode.LocalName == "svg";
            if (!c.IsIgnored && !nestedSvg)
            {
                return true;
            }

            foreach (AxNode g in c.Children)
            {
                stack.Push(g);
            }
        }

        return false;
    }

    private void ComputeDocument(AxNode n)
    {
        n.Role = "RootWebArea";
        n.IsIgnored = false;
        Node title = FindTitle();
        if (!title.IsNull)
        {
            n.Name = new AxText(TreeText.Collapse(title.TextContent ?? string.Empty), AxTextSource.DocumentTitle, null, [title]);
        }
    }

    private Node FindTitle()
    {
        foreach (Node e in _doc.Root.DescendantElements)
        {
            if (e.Namespace == ElementNamespace.Html && e.LocalName == "title")
            {
                return e;
            }

            if (e.Namespace == ElementNamespace.Html && e.LocalName == "body")
            {
                break;
            }
        }

        return default;
    }

    private void ComputeText(AxNode n)
    {
        n.Role = "StaticText";
        n.Hidden = _hidden.Get(n.DomNode);
        string text = TreeText.Collapse(n.DomNode.Data);
        // Collapsed white space keeps one space at an end only where an inline neighbour follows or precedes.
        ReadOnlySpan<char> d = n.DomNode.Data;
        bool startSpace = d.Length > 0 && TreeText.IsWs(d[0]);
        bool endSpace = d.Length > 0 && TreeText.IsWs(d[^1]);
        Node parent = n.DomNode.Parent;
        bool blockParent = parent.IsElement && !IsInline(parent);
        if (text.Length == 0)
        {
            text = " ";
        }
        else
        {
            if (startSpace && !(blockParent && n.DomNode.PreviousSibling.IsNull))
            {
                text = " " + text;
            }

            if (endSpace && !(blockParent && n.DomNode.NextSibling.IsNull))
            {
                text += " ";
            }
        }

        n.Name = new AxText(text, AxTextSource.TextNode, null, []);
        var reasons = new List<string>();
        AddHiddenReasons(n, reasons);
        if (TreeText.Collapse(d).Length == 0 && reasons.Count == 0 && _layout is null)
        {
            reasons.Add("uninteresting");
        }

        n.IsIgnored = reasons.Count > 0;
        n.IgnoredReasons = reasons;
        n.AriaDisabledChain = n.Parent is { } p && (p.AriaDisabledChain || p.States.Disabled);
        n.States = new AxStates();
    }

    private void AddHiddenReasons(AxNode n, List<string> reasons)
    {
        HiddenInfo h = n.Hidden;
        if ((h.Reasons & HiddenReasons.VisibilityHidden) != 0)
        {
            reasons.Add("notVisible");
        }

        if ((h.Reasons & HiddenReasons.AriaHidden) != 0)
        {
            bool own = n.Kind == AxNodeKind.Element && _hidden.Own(n.DomNode.Index).HasFlag(HiddenReasons.AriaHidden);
            reasons.Add(own ? "ariaHiddenElement" : "ariaHiddenSubtree");
        }

        if ((h.Reasons & HiddenReasons.Inert) != 0)
        {
            bool own = n.Kind == AxNodeKind.Element && _hidden.Own(n.DomNode.Index).HasFlag(HiddenReasons.Inert);
            reasons.Add(own ? "inertElement" : "inertSubtree");
        }
    }

    private void ComputeElement(AxNode n)
    {
        Node el = n.DomNode;
        NodeRole nr = _roles[el];
        AriaRole? role = nr.Role;
        n.AriaRole = role;
        n.RoleOrigin = nr.Origin;
        n.Role = role is null ? string.Empty : RoleComputer.ComputedRoleName(role);
        n.Hidden = _hidden.Get(el);
        n.AriaDisabledChain = n.Parent is { Kind: AxNodeKind.Element } p && (p.AriaDisabledChain || TreeText.EqualsIgnoreCase(p.DomNode.GetAttribute("aria-disabled"), "true"));

        // Name and description. A role that prohibits author naming is still named from aria-label, aria-labelledby and the
        // title attribute, as the html-aam name pages expect of generic elements.
        if (_plan is null || !_plan.TryReuse(el.Index, out AxText reusedName, out AxText reusedDescription))
        {
            n.Name = _names.ComputeName(el);
            n.Description = _names.ComputeDescription(el, n.Name.Source);
        }
        else
        {
            n.Name = reusedName;
            n.Description = reusedDescription;
        }

        n.States = StateBuilder.Build(el, role, nr.IsDisabled, n.AriaDisabledChain, () => NameComputer.SelectedOptionsText(el));
        n.Relations = BuildRelations(n);
        if (n.States.Url is { } rawUrl)
        {
            n.States.Url = ResolveUrl(rawUrl);
        }
        n.Focus = new AxFocus(IsFocusableNow(n, nr), TabIndexOf(el), -1);

        // Role diagnostics of the role layer.
        foreach (RoleDiagnostic d in nr.Diagnostics)
        {
            AddDiagnostic(n, new AxDiagnostic(AxDiagnosticKind.Role, el, d.Attribute, d.Value, d.Message, d.Kind));
        }

        n.InternalRole = InternalRoleOf(el, role, nr.Origin, IsFirstSummary(el)) ?? LayoutTableRole(el, nr);
        ComputeIgnored(n, nr);
    }

    /// <summary>
    /// With rendered facts, the internal role Chromium gives a table it judges to be for layout, and its rows and cells (profile entry
    /// <c>layout-table</c>); null otherwise. Only elements whose role is the implicit one of the HTML table, row or cell qualify.
    /// </summary>
    private string? LayoutTableRole(Node el, NodeRole nr)
    {
        if (_layout is null || nr.Origin != RoleOrigin.Implicit || nr.Role is null || el.Namespace != ElementNamespace.Html)
        {
            return null;
        }

        // A generic element laid out as a CSS table, row or cell.
        if (nr.Role.Name == "generic")
        {
            return _layout.CssTableKind(el);
        }

        string? kind = el.LocalName switch
        {
            "table" => "LayoutTable",
            "tr" => "LayoutTableRow",
            "td" or "th" => "LayoutTableCell",
            _ => null,
        };
        if (kind is null)
        {
            return null;
        }

        Node table = el;
        while (!table.IsNull && !(table.IsElement && table.Namespace == ElementNamespace.Html && table.LocalName == "table"))
        {
            table = table.Parent;
        }

        if (table.IsNull)
        {
            return null;
        }

        if (!_dataTables.TryGetValue(table.Index, out bool data))
        {
            _dataTables[table.Index] = data = _layout.IsDataTable(table);
        }

        return data ? null : kind;
    }

    /// <summary>
    /// The role Chromium exposes for elements WAI-ARIA has no role for (profile entry internal-roles). Elements that have a WAI-ARIA
    /// role keep it, except the SVG and MathML elements Chromium names after the element.
    /// </summary>
    private static string? InternalRoleOf(Node el, AriaRole? role, RoleOrigin origin, bool firstSummary)
    {
        switch (el.Namespace)
        {
            case ElementNamespace.Svg:
                // Profile entry svg-root-role: image until ResolveSvgRoots finds exposed content inside.
                return el.LocalName == "svg" && origin == RoleOrigin.Implicit ? "image" : null;
            case ElementNamespace.MathMl:
                return el.LocalName switch
                {
                    "math" => "MathMLMath",
                    "mi" => "MathMLIdentifier",
                    "mo" => "MathMLOperator",
                    "mn" => "MathMLNumber",
                    _ => null,
                };
        }

        if (origin == RoleOrigin.Implicit && role is { Name: "generic" } && el.Namespace == ElementNamespace.Html)
        {
            // Profile entry section-header-footer: a header or footer outside the body scope is not a landmark.
            if (el.LocalName == "header")
            {
                return "sectionheader";
            }

            if (el.LocalName == "footer")
            {
                return "sectionfooter";
            }
        }

        if (role is not null)
        {
            return null;
        }

        switch (el.LocalName)
        {
            case "label":
                return "LabelText";
            case "legend":
                return "Legend";
            case "br":
                return "LineBreak";
            case "iframe":
                return "Iframe";
            case "summary":
                return firstSummary ? "DisclosureTriangle" : null;
            case "input":
                return HtmlSemantics.GetInputType(el) switch
                {
                    HtmlInputType.Date => "Date",
                    HtmlInputType.Color => "ColorWell",
                    _ => null,
                };
            default:
                return null;
        }
    }

    private static int? TabIndexOf(Node el)
    {
        string? v = el.GetAttribute("tabindex");
        return v is not null && HtmlSemantics.TryParseInteger(v, out long t) ? (int)Math.Clamp(t, int.MinValue, int.MaxValue) : null;
    }

    private static bool IsFocusableNow(AxNode n, NodeRole nr)
    {
        HiddenReasons r = n.Hidden.Reasons;
        // HTML section 6.6.3: a focusable area must be rendered, not inert, and (for form controls) not disabled; visibility: hidden removes it too.
        return nr.IsFocusable && !nr.IsDisabled && !nr.IsInert && (r & (HiddenMap.NotRenderedMask | HiddenReasons.VisibilityHidden | HiddenReasons.Inert)) == 0;
    }

    private void ComputeIgnored(AxNode n, NodeRole nr)
    {
        var reasons = new List<string>();
        AddHiddenReasons(n, reasons);
        AriaRole? role = n.AriaRole;
        if (role is { Name: "none" or "presentation" })
        {
            reasons.Add("presentationalRole");
        }
        else if (role is null)
        {
            if (n.InternalRole is null)
            {
                // SVG's foreignObject only hosts HTML content (profile entry foreign-object).
                reasons.Add(n.DomNode.Namespace == ElementNamespace.Svg && n.DomNode.LocalName == "foreignObject" ? "presentationalRole" : "uninteresting");
            }
        }
        else if (role.Name == "generic" && IsRootWrapper(n.DomNode) && !(_layout is not null && n.DomNode.LocalName is "b" or "i" or "body" && _layout.HasBox(n.DomNode)))
        {
            reasons.Add("uninteresting");
        }
        else if (role.Name == "generic" && _layout is not null && n.DomNode.Namespace == ElementNamespace.Html && n.InternalRole is null && _layout.IsUninterestingGeneric(n.DomNode))
        {
            // Profile entry layout-generic-pruning.
            reasons.Add("uninteresting");
        }
        else if (role.Name == "paragraph" && _layout is not null && n.DomNode.Namespace == ElementNamespace.Html && n.DomNode.LocalName == "p" && _layout.IsUninterestingParagraph(n.DomNode))
        {
            // Profile entry layout-generic-pruning: a paragraph with no inline content.
            reasons.Add("uninteresting");
        }
        else if (role.Name == "rowgroup" && n.RoleOrigin == RoleOrigin.Implicit && n.DomNode.LocalName == "tbody")
        {
            reasons.Add("uninteresting");
        }

        n.IsIgnored = reasons.Count > 0;
        n.IgnoredReasons = reasons;
    }

    /// <summary>The elements Chromium leaves out of the exposed tree although they have the generic role: the document element, the body and the bold and italic elements (profile entry generic-pruning).</summary>
    private static bool IsRootWrapper(Node el) => el.Namespace == ElementNamespace.Html && el.LocalName is "html" or "body" or "b" or "i";

    // ---- relations ----

    private List<AxRelation> BuildRelations(AxNode n)
    {
        Node el = n.DomNode;
        List<AxRelation>? list = null;

        if (_owns.OwnedBy(el.Index) is { } owned)
        {
            var elements = new List<Node>(owned.Count);
            var targets = new List<AxNode>(owned.Count);
            foreach (int i in owned)
            {
                elements.Add(_doc.GetNode(i));
                if (_byDom[i] is { } t)
                {
                    targets.Add(t);
                }
            }

            (list ??= []).Add(new AxRelation(AxRelationKind.Owns, "aria-owns", elements, targets, []));
        }

        AddRelation(ref list, n, "aria-controls", AxRelationKind.Controls, true);
        if (el.Namespace == ElementNamespace.Html && el.LocalName == "input" && el.GetAttribute("aria-controls") is null && el.GetAttribute("list") is not null)
        {
            AddRelation(ref list, n, "list", AxRelationKind.Controls, true);
        }

        AddRelation(ref list, n, "aria-labelledby", AxRelationKind.LabelledBy, false);
        AddRelation(ref list, n, "aria-describedby", AxRelationKind.DescribedBy, false);
        AddRelation(ref list, n, "aria-details", AxRelationKind.Details, true);
        AddRelation(ref list, n, "aria-errormessage", AxRelationKind.ErrorMessage, true);
        AddRelation(ref list, n, "aria-flowto", AxRelationKind.FlowTo, true);
        AddRelation(ref list, n, "aria-activedescendant", AxRelationKind.ActiveDescendant, true);
        return list ?? [];
    }

    private void AddRelation(ref List<AxRelation>? list, AxNode n, string attribute, AxRelationKind kind, bool needsTree)
    {
        string? value = n.DomNode.GetAttribute(attribute);
        if (value is null)
        {
            return;
        }

        var elements = new List<Node>();
        var targets = new List<AxNode>();
        var broken = new List<string>();
        int pos = 0;
        int count = 0;
        while (OwnsMap.NextToken(value, ref pos, out string id) && count++ < _opt.MaxIdReferences)
        {
            Node t = _doc.GetElementById(id);
            if (t.IsNull)
            {
                if (!broken.Contains(id))
                {
                    broken.Add(id);
                }

                continue;
            }

            if (elements.Contains(t))
            {
                continue;
            }

            elements.Add(t);
            if (_byDom[t.Index] is { } ax)
            {
                targets.Add(ax);
            }
            else if (needsTree)
            {
                AddDiagnostic(n, new AxDiagnostic(AxDiagnosticKind.BrokenIdReference, n.DomNode, attribute, id, "The element \"" + id + "\" named by " + attribute + " is not rendered, so it is not in the accessibility tree."));
            }
        }

        if (elements.Count > 0 || broken.Count > 0)
        {
            (list ??= []).Add(new AxRelation(kind, attribute, elements, targets, broken));
        }
    }

    private void AddDiagnostic(AxNode n, AxDiagnostic d)
    {
        if (!_nodeDiags.TryGetValue(n, out List<AxDiagnostic>? l))
        {
            _nodeDiags[n] = l = [];
        }

        l.Add(d);
    }

    // ---- focus ----

    private void ComputeFocusOrder()
    {
        // HTML section 6.6.3 sequential focus navigation order: positive tabindex values ascending (document order breaks
        // ties), then elements with tabindex 0 or none in document order. Negative values are focusable but not tabbable.
        var positive = new List<(int Tab, int Order, AxNode Node)>();
        var rest = new List<AxNode>();
        int order = 0;
        foreach (AxNode n in _domOrder)
        {
            if (n.Kind != AxNodeKind.Element || !n.Focus.IsFocusable)
            {
                continue;
            }

            order++;
            int? tab = n.Focus.TabIndex;
            if (tab is > 0)
            {
                positive.Add((tab.Value, order, n));
            }
            else if (tab is null or 0)
            {
                rest.Add(n);
            }
        }

        positive.Sort((a, b) => a.Tab != b.Tab ? a.Tab.CompareTo(b.Tab) : a.Order.CompareTo(b.Order));
        var all = new List<AxNode>(positive.Count + rest.Count);
        foreach ((int _, int _, AxNode node) in positive)
        {
            all.Add(node);
        }

        all.AddRange(rest);
        for (int i = 0; i < all.Count; i++)
        {
            all[i].Focus = all[i].Focus with { SequentialIndex = i };
        }

        _tree.FocusOrder = all;
    }

    // ---- position in set (Core-AAM section 5.4) ----

    private static string? SetFamily(string role) => role switch
    {
        "listitem" => "listitem",
        "menuitem" or "menuitemcheckbox" or "menuitemradio" => "menuitem",
        "option" => "option",
        "radio" => "radio",
        "tab" => "tab",
        "treeitem" => "treeitem",
        "article" => "article",
        _ => null,
    };

    /// <summary>
    /// Chromium reports the options of a select as focusable although they are not stops in the sequential order (profile entry
    /// option-focusable). The focus order has already been computed, so this changes only <see cref="AxFocus.IsFocusable"/>.
    /// </summary>
    private void ComputeChromiumFocusableOptions()
    {
        foreach (AxNode n in _domOrder)
        {
            if (n.Kind == AxNodeKind.Element && !n.Focus.IsFocusable && n.Role == "option" && n.DomNode.Namespace == ElementNamespace.Html
                && n.DomNode.LocalName == "option" && !n.States.Disabled && !n.IsIgnored && n.Parent is { } p && p.Role is "combobox" or "listbox" or "group" or "MenuListPopup")
            {
                n.Focus = n.Focus with { IsFocusable = true };
            }
        }
    }

    /// <summary>
    /// Chromium gives each list item the number of list ancestors as its level (profile entry list-item-level). An explicit
    /// <c>aria-level</c> is kept.
    /// </summary>
    private void ComputeListItemLevels()
    {
        foreach (AxNode n in _domOrder)
        {
            if (n.Kind != AxNodeKind.Element || n.Role != "listitem" || n.States.Level is not null)
            {
                continue;
            }

            int depth = 0;
            for (AxNode? p = n.Parent; p is not null; p = p.Parent)
            {
                if (p.Role == "list")
                {
                    depth++;
                }
            }

            if (depth > 0)
            {
                n.States.Level = depth;
            }
        }
    }

    private void ComputePositions()
    {
        var groups = new Dictionary<(int Container, string Family, string? Extra), List<AxNode>>();
        foreach (AxNode n in _tree.Nodes)
        {
            if (n.Kind != AxNodeKind.Element || n.IsIgnored || SetFamily(n.Role) is not { } family)
            {
                continue;
            }

            if (family == "article" && n.Parent?.Role != "feed")
            {
                continue;
            }

            int container;
            string? extra = null;
            Node el = n.DomNode;
            if (family == "radio" && el.Namespace == ElementNamespace.Html && el.LocalName == "input" && el.GetAttribute("name") is { Length: > 0 } rname)
            {
                // A native radio button belongs to the group of the same name in its form (HTML section 4.10.5.1.18).
                container = FormOwnerIndex(el);
                extra = rname;
            }
            else
            {
                AxNode? p = n.Parent;
                while (p is not null && (p.IsIgnored || p.Role is "generic" or "none" || (p.Role == "group" && family is "treeitem" or "menuitem")))
                {
                    p = p.Parent;
                }

                container = p?.Id ?? -1;
            }

            var key = (container, family, extra);
            if (!groups.TryGetValue(key, out List<AxNode>? l))
            {
                groups[key] = l = [];
            }

            l.Add(n);
        }

        foreach (List<AxNode> members in groups.Values)
        {
            int size = members.Count;
            foreach (AxNode m in members)
            {
                if (m.States.SetSize is { } given && given >= 0)
                {
                    size = given;
                    break;
                }
            }

            int prev = 0;
            foreach (AxNode m in members)
            {
                int pos = m.States.PosInSet ?? prev + 1;
                prev = pos;
                if (m.States.PosInSet is null)
                {
                    m.States.PosInSet = pos;
                    m.States.PosInSetComputed = true;
                }

                if (m.States.SetSize is null)
                {
                    m.States.SetSize = size;
                    m.States.SetSizeComputed = true;
                }

                if (m.States.SetSize == -1)
                {
                    m.States.SetSize = null;
                }
            }
        }
    }

    private int FormOwnerIndex(Node el)
    {
        string? form = el.GetAttribute("form");
        if (form is not null)
        {
            Node f = _doc.GetElementById(form);
            return f.IsNull ? -1 : f.Index;
        }

        for (Node p = el.Parent; !p.IsNull; p = p.Parent)
        {
            if (p.IsElement && p.Namespace == ElementNamespace.Html && p.LocalName == "form")
            {
                return p.Index;
            }
        }

        return -1;
    }

    // ---- required context and owned elements (WAI-ARIA 1.2 sections 5.2.3 and 5.2.4) ----

    private void CheckContextAndOwned()
    {
        // One depth-first walk keeps the count of each role along the path (for required context) and, per open node,
        // the set of roles below it (for required owned elements).
        var roleIds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (AriaRole r in AriaTaxonomy.Roles)
        {
            roleIds[r.Name] = roleIds.Count;
        }

        const int Words = 3;
        var path = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new List<(AxNode Node, int Next)>();
        var bits = new List<ulong>();
        stack.Add((_tree.Root, 0));
        bits.AddRange(new ulong[Words]);
        while (stack.Count > 0)
        {
            (AxNode node, int next) = stack[^1];
            if (next == 0)
            {
                _ct.ThrowIfCancellationRequested();
                if (node.AriaRole is { } r && !node.IsIgnored)
                {
                    CheckContext(node, r, path);
                }

                if (node.AriaRole is { } rr)
                {
                    path[rr.Name] = path.GetValueOrDefault(rr.Name) + 1;
                }
            }

            if (next < node.Children.Count)
            {
                stack[^1] = (node, next + 1);
                stack.Add((node.Children[next], 0));
                bits.AddRange(new ulong[Words]);
                continue;
            }

            // Leave the node: check what it owns, then fold its roles into its parent's set.
            int baseIdx = (stack.Count - 1) * Words;
            if (node.AriaRole is { } role)
            {
                if (!node.IsIgnored)
                {
                    CheckOwned(node, role, bits, baseIdx, roleIds);
                }

                path[role.Name]--;
            }

            if (stack.Count > 1)
            {
                int parentIdx = (stack.Count - 2) * Words;
                for (int w = 0; w < Words; w++)
                {
                    bits[parentIdx + w] |= bits[baseIdx + w];
                }

                if (node.AriaRole is { } own && roleIds.TryGetValue(own.Name, out int id))
                {
                    bits[parentIdx + (id / 64)] |= 1UL << (id % 64);
                }
            }

            stack.RemoveAt(stack.Count - 1);
            bits.RemoveRange(bits.Count - Words, Words);
        }
    }

    private void CheckContext(AxNode node, AriaRole role, Dictionary<string, int> path)
    {
        if (role.RequiredContext.Count == 0)
        {
            return;
        }

        foreach (string ctx in role.RequiredContext)
        {
            if (path.GetValueOrDefault(ctx) > 0)
            {
                return;
            }
        }

        AddDiagnostic(node, new AxDiagnostic(AxDiagnosticKind.MissingRequiredContext, node.DomNode, null, role.Name, "The role " + role.Name + " requires a parent with the role " + string.Join(" or ", role.RequiredContext) + "."));
    }

    private void CheckOwned(AxNode node, AriaRole role, List<ulong> bits, int baseIdx, Dictionary<string, int> roleIds)
    {
        if (role.RequiredOwned.Count == 0 || role.Name == "combobox" || node.States.Busy)
        {
            return;
        }

        foreach (IReadOnlyList<string> chain in role.RequiredOwned)
        {
            if (chain.Count == 0 || !roleIds.TryGetValue(chain[^1], out int id))
            {
                continue;
            }

            if ((bits[baseIdx + (id / 64)] & (1UL << (id % 64))) != 0)
            {
                return;
            }
        }

        var wanted = new List<string>();
        foreach (IReadOnlyList<string> chain in role.RequiredOwned)
        {
            if (chain.Count > 0)
            {
                wanted.Add(chain[^1]);
            }
        }

        AddDiagnostic(node, new AxDiagnostic(AxDiagnosticKind.MissingRequiredOwnedElements, node.DomNode, null, role.Name, "The role " + role.Name + " requires owned elements with the role " + string.Join(" or ", wanted) + "."));
    }

    // ---- diagnostics ----

    private static bool IsInteractive(string role) => role is "button" or "link" or "checkbox" or "radio" or "switch" or "textbox" or "searchbox"
        or "combobox" or "listbox" or "slider" or "spinbutton" or "menuitem" or "menuitemcheckbox" or "menuitemradio" or "tab" or "treeitem" or "option";

    private void Collect()
    {
        foreach (AxNode n in _tree.Nodes)
        {
            if (n.Kind != AxNodeKind.Element)
            {
                continue;
            }

            HiddenReasons r = n.Hidden.Reasons;
            NodeRole nr = _roles[n.DomNode];
            if ((r & HiddenReasons.AriaHidden) != 0 && nr.IsFocusable && !nr.IsDisabled && (r & (HiddenMap.NotRenderedMask | HiddenReasons.VisibilityHidden | HiddenReasons.Inert)) == 0)
            {
                AddDiagnostic(n, new AxDiagnostic(AxDiagnosticKind.FocusableUnderAriaHidden, n.DomNode, "aria-hidden", null, "A focusable element is hidden from assistive technology by aria-hidden, so a keyboard user can reach something that is not announced."));
            }

            if (!n.IsIgnored && n.Name.IsEmpty && n.AriaRole is { } role)
            {
                if (IsInteractive(role.Name))
                {
                    AddDiagnostic(n, new AxDiagnostic(AxDiagnosticKind.NamelessInteractive, n.DomNode, null, role.Name, "This " + role.Name + " has no accessible name."));
                }
                else if (role.NameRequired)
                {
                    AddDiagnostic(n, new AxDiagnostic(AxDiagnosticKind.EmptyNameRequired, n.DomNode, null, role.Name, "The role " + role.Name + " requires an accessible name."));
                }
            }
        }

        foreach (AxDiagnostic d in _owns.Diagnostics)
        {
            if (_byDom[d.Node.Index] is { } owner)
            {
                AddDiagnostic(owner, d);
            }
            else
            {
                _diags.Add(d);
            }
        }

        if (_names.LimitReached)
        {
            _tree.LimitReached = true;
            _diags.Add(new AxDiagnostic(AxDiagnosticKind.LimitExceeded, _doc.Root, null, null, "The name computation work limit was reached; some names are incomplete."));
        }

        var all = new List<AxDiagnostic>();
        foreach (AxNode n in _tree.Nodes)
        {
            if (_nodeDiags.TryGetValue(n, out List<AxDiagnostic>? l))
            {
                n.Diagnostics = l;
                all.AddRange(l);
            }
        }

        all.AddRange(_diags);
        _tree.Diagnostics = all;
    }
}
