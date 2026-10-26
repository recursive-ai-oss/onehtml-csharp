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
    private readonly StateHelpers _stateHelpers;
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
        _stateHelpers = new StateHelpers(doc, a => _roles[a].Role is { Name: "listbox" or "tablist" or "tree" or "treegrid" });
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
        ClearDisabledEditors();
        ResolveSvgRoots();
        ResolveLabelFor();
        _tree.UsedCounters = _counters.Walked;
        ComputeFocusOrder();
        ComputeChromiumFocusableOptions();
        MarkAutofocus();
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
        _layout is not null && el.Namespace == ElementNamespace.Html && _roles[el].Role is { Name: "generic" } && _layout.IsUninterestingGeneric(el) && _layout.IsInlineBox(el) && !_layout.HasPseudoBox(el);

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

    /// <summary>The URL a link or image refers to: <paramref name="value"/> resolved against the base URL when there is one (profile entry url-serialization).</summary>
    private string ResolveUrl(string value)
    {
        if (!_baseUrlKnown)
        {
            _baseUrlKnown = true;
            if (_opt.BaseUrl is { Length: > 0 } b)
            {
                Node baseEl = _doc.Root.QuerySelector("base[href]");
                _baseUrl = baseEl.IsNull ? b : UrlResolver.Resolve(b, baseEl.GetAttribute("href") ?? string.Empty);
            }
        }

        return UrlResolver.Resolve(_baseUrl, value);
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
                AddGeneratedText(host, false);
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
            AddGeneratedText(host, true);
        }
    }

    /// <summary>
    /// Profile entry <c>generated-content-node</c>: each <c>::before</c> and <c>::after</c> box is a node of its own under the element,
    /// first and last (after a list marker): an ignored node (uninteresting) for an inline box, a generic node for any other display,
    /// holding the static text of the generated text when there is some. Profile entry <c>control-text-node</c> does the same for the
    /// text a form control draws inside itself.
    /// </summary>
    private void AddGeneratedText(AxNode host, bool afterMarker)
    {
        bool hasBefore = _layout!.TryGetPseudo(host.DomNode, true, out string beforeText, out bool beforeInline, out bool beforeOut, out bool hostInline, out bool beforeIb);
        bool hasAfter = _layout.TryGetPseudo(host.DomNode, false, out string afterText, out bool afterInline, out bool afterOut, out _, out bool afterIb);
        // An empty block-level box (out of flow or not) gets a node only inside an inline-level host; an empty inline-block box always does (observed).
        hasBefore &= beforeInline || beforeText.Length > 0 || beforeIb || hostInline;
        hasAfter &= afterInline || afterText.Length > 0 || afterIb || hostInline;
        List<AxNode> control = ControlNodes(host);
        if (!hasBefore && !hasAfter && control.Count == 0)
        {
            return;
        }

        var children = new List<AxNode>(host.Children.Count + 3);
        int keep = afterMarker && host.Children.Count > 0 ? 1 : 0;
        for (int i = 0; i < keep; i++)
        {
            children.Add(host.Children[i]);
        }

        children.AddRange(control);

        if (hasBefore)
        {
            children.Add(PseudoNode(host, beforeText, beforeInline));
        }

        for (int i = keep; i < host.Children.Count; i++)
        {
            children.Add(host.Children[i]);
        }

        if (hasAfter)
        {
            children.Add(PseudoNode(host, afterText, afterInline));
        }

        host.Children = children;
    }

    private AxNode PseudoNode(AxNode host, string text, bool inlineBox)
    {
        var node = new AxNode(_tree, -1, AxNodeKind.Generated, host.DomNode)
        {
            Role = inlineBox ? "none" : "generic",
            IsIgnored = inlineBox,
            IgnoredReasons = inlineBox ? ["uninteresting"] : [],
            Parent = host,
            States = new AxStates(),
        };
        node.Children = text.Length == 0 ? [] : [GeneratedStaticText(node, text)];
        return node;
    }

    /// <summary>
    /// Profile entry <c>control-text-node</c>: what a rendered form control shows inside itself. A text-entry <c>input</c> and a
    /// <c>textarea</c> have a generic child with the editable state (the inner editor) holding the static text of the value (a password's
    /// is bullets; a placeholder is not a value); an <c>input</c> button has the static text of its <c>value</c>, or of the default label
    /// of a submit or reset button. Null when the host has none.
    /// </summary>
    private List<AxNode> ControlNodes(AxNode host)
    {
        Node el = host.DomNode;
        List<AxNode> none = [];
        if (el.Namespace != ElementNamespace.Html || host.Kind != AxNodeKind.Element || !_layout!.HasBox(el))
        {
            return none;
        }

        if (el.LocalName == "textarea")
        {
            return _roles[el].Role?.Name == "textbox" ? [EditorNode(host, el.TextContent ?? string.Empty)] : none;
        }

        if (el.LocalName != "input")
        {
            return none;
        }

        string type = TreeText.AsciiLower(el.GetAttribute("type") ?? string.Empty);
        string? value = el.GetAttribute("value");
        switch (_roles[el].Role?.Name)
        {
            case "textbox" or "searchbox" or "spinbutton" or "combobox" when type is "text" or "search" or "tel" or "url" or "email" or "password" or "number" or "":
                AxNode editor = EditorNode(host, type == "password" ? new string('\u2022', value?.Length ?? 0) : value ?? string.Empty);
                AxNode content = editor;
                if (type is "search" or "number")
                {
                    // The inner editor of these controls sits in an ignored node with no reason (a search field's cancel button, a
                    // number field's spin button).
                    var wrapper = new AxNode(_tree, -1, AxNodeKind.Generated, host.DomNode) { Role = "none", IsIgnored = true, IgnoredReasons = [], Parent = host, States = new AxStates() };
                    editor.Parent = wrapper;
                    wrapper.Children = [editor];
                    content = wrapper;
                }

                if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(el.GetAttribute("placeholder")))
                {
                    // The placeholder text is an ignored leaf before the inner editor (or before the node that holds it).
                    return [new AxNode(_tree, -1, AxNodeKind.Generated, host.DomNode) { Role = "none", IsIgnored = true, IgnoredReasons = [], Parent = host, States = new AxStates(), Children = [] }, content];
                }

                return [content];
            case "button" when type is "submit" or "button" or "reset":
                string label = !string.IsNullOrEmpty(value) ? value : type == "submit" ? "Submit" : type == "reset" ? "Reset" : string.Empty;
                return label.Length == 0 ? none : [GeneratedStaticText(host, label)];
            default:
                return none;
        }
    }

    private readonly List<AxNode> _editors = [];

    /// <summary>
    /// The editing host inside a control (and its text) is editable only while the control is enabled and not read-only (profile entry
    /// inner-editor-editable); a disabled or read-only field keeps the nodes and loses the state. Run after the states are known.
    /// </summary>
    private void ClearDisabledEditors()
    {
        foreach (AxNode e in _editors)
        {
            AxNode? host = e.Parent;
            while (host is { Kind: AxNodeKind.Generated })
            {
                host = host.Parent;
            }

            if (host is { Kind: AxNodeKind.Element } h && (h.States.Disabled || h.DomNode.HasAttribute("readonly")))
            {
                e.States.Editable = null;
            }
        }
    }

    private AxNode EditorNode(AxNode host, string text)
    {
        var editor = new AxNode(_tree, -1, AxNodeKind.Generated, host.DomNode)
        {
            Role = "generic",
            Parent = host,
            States = new AxStates { Editable = "plaintext" },
        };
        _editors.Add(editor);
        if (text.Length > 0 && !text.Contains('\n', StringComparison.Ordinal))
        {
            AxNode t = GeneratedStaticText(editor, text);
            t.States.Editable = "plaintext";
            _editors.Add(t);
            editor.Children = [t];
        }
        else
        {
            editor.Children = [];
        }

        return editor;
    }

    private AxNode GeneratedStaticText(AxNode parent, string text) => new(_tree, -1, AxNodeKind.Generated, parent.DomNode)
    {
        Role = "StaticText",
        InternalRole = "StaticText",
        Name = new AxText(text, AxTextSource.TextNode, null, []),
        Parent = parent,
        States = new AxStates(),
    };

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

    /// <summary>
    /// Profile entry label-for: a label that holds nothing but text and labels a checkbox or a radio button is ignored (reason labelFor) and
    /// so is its text, because the control's name already carries the text.
    /// </summary>
    private void ResolveLabelFor()
    {
        foreach (AxNode n in _tree.Nodes)
        {
            if (n.Kind != AxNodeKind.Element || n.IsIgnored || n.Role is not ("checkbox" or "radio") || n.NativeLabels.Count == 0 || n.DomNode.LocalName != "input")
            {
                continue;
            }

            foreach (Node label in n.NativeLabels)
            {
                if (_byDom[label.Index] is not { } l || l.IsIgnored || l.InternalRole != "LabelText" || !HoldsOnlyText(l, n))
                {
                    continue;
                }

                l.IsIgnored = true;
                l.IgnoredReasons = ["labelFor"];
                foreach (AxNode c in l.Children)
                {
                    if (c == n)
                    {
                        continue;
                    }

                    c.IsIgnored = true;
                    c.IgnoredReasons = ["presentationalRole"];
                }
            }
        }
    }

    /// <summary>True when the label's children are text (and the labelled control itself, for a label that wraps it) with some text among them.</summary>
    private static bool HoldsOnlyText(AxNode label, AxNode control)
    {
        // An element child other than the control (a b whose node was left out, say) makes it a label with content.
        for (Node d = label.DomNode.FirstChild; !d.IsNull; d = d.NextSibling)
        {
            if (d.IsElement && d.Index != control.DomNode.Index)
            {
                return false;
            }
        }

        int texts = 0;
        foreach (AxNode c in label.Children)
        {
            if (c == control)
            {
                continue;
            }

            if (c.Kind != AxNodeKind.Text || c.IsIgnored)
            {
                return false;
            }

            texts++;
        }

        return texts > 0;
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
        // The static text is the rendered text, so text-transform applies (profile entry text-transform-static-text).
        string transform = n.DomNode.Parent.IsElement ? _styles.GetStyle(n.DomNode.Parent).GetPropertyValue("text-transform") : string.Empty;
        string text = TreeText.ApplyTextTransform(TreeText.Collapse(n.DomNode.Data), transform);
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
        n.States = new AxStates { Editable = n.Parent is { Kind: AxNodeKind.Element } pe ? pe.States.Editable : null };
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
            // An element that has aria-hidden=true itself is ariaHiddenElement even when an ancestor is aria-hidden too (Chrome 156 output on
            // 104 of the 2,901 own aria-hidden nodes in 400 corpus pages were nested, and none was the subtree); the rest is the subtree.
            bool own = n.Kind == AxNodeKind.Element && _hidden.Own(n.DomNode.Index).HasFlag(HiddenReasons.AriaHidden);
            reasons.Add(own ? "ariaHiddenElement" : "ariaHiddenSubtree");
        }

        if ((h.Reasons & HiddenReasons.Inert) != 0)
        {
            bool own = n.Kind == AxNodeKind.Element && _hidden.Own(n.DomNode.Index).HasFlag(HiddenReasons.Inert);
            reasons.Add(own ? "inertElement" : "inertSubtree");
        }
    }

    /// <summary>
    /// True when a listbox or combobox holds the element: the first ancestor that is not a generic, a group or presentational is one, or an
    /// <c>aria-owns</c> gives the element to one. A list between them (an <c>ul</c> inside the listbox) is not.
    /// </summary>
    private bool HasListboxContext(Node el)
    {
        if (_owns.OwnerOf(el.Index) >= 0)
        {
            return true;
        }

        int steps = 0;
        for (Node a = el.ParentElement; !a.IsNull && a.IsElement && steps++ < 100_000; a = a.ParentElement)
        {
            string? name = _roles[a].Role?.Name;
            if (name is null or "generic" or "group" or "none" or "presentation")
            {
                continue;
            }

            return name is "listbox" or "combobox";
        }

        return false;
    }

    private void ComputeElement(AxNode n)
    {
        Node el = n.DomNode;
        NodeRole nr = _roles[el];
        AriaRole? role = nr.Role;
        RoleOrigin origin = nr.Origin;
        bool presentationalTableRow = false;
        if (origin == RoleOrigin.InheritedPresentational && el.Namespace == ElementNamespace.Html)
        {
            // Inside a table whose role is none, Chromium exposes each cell as a generic and leaves the rows and row groups out as
            // uninteresting, where WAI-ARIA 5.2.7 would make all of them presentational (profile entry presentational-table).
            if (el.LocalName is "td" or "th")
            {
                role = AriaTaxonomy.FindRole("generic");
                origin = RoleOrigin.Implicit;
            }
            else if (el.LocalName is "tr" or "tbody" or "thead" or "tfoot")
            {
                presentationalTableRow = true;
            }
        }

        if (origin == RoleOrigin.ChildrenPresentational)
        {
            // Chromium does not make the descendants of a button, tab or image role presentational: an svg, heading or span inside a button
            // keeps its own role (profile entry children-presentational).
            role = nr.ExplicitRole ?? nr.ImplicitRole;
            origin = nr.ExplicitRole is not null ? RoleOrigin.Explicit : role is null ? RoleOrigin.None : RoleOrigin.Implicit;
        }

        if (origin == RoleOrigin.Explicit && role is { Name: "option" } && !HasListboxContext(el))
        {
            // Chromium does not give the role option to an element that no listbox (or combobox) holds: a button, link or list item keeps its
            // own role, a div is generic (profile entry option-needs-listbox).
            role = nr.ImplicitRole;
            origin = role is null ? RoleOrigin.None : RoleOrigin.Implicit;
        }

        if (origin == RoleOrigin.Implicit && role is { Name: "listitem" or "generic" } && el.Namespace == ElementNamespace.Html && el.LocalName == "li"
            && el.ParentElement is { IsNull: false } listParent && listParent.Namespace == ElementNamespace.Html && listParent.LocalName is "ul" or "ol" or "menu"
            && _roles[listParent].ExplicitRole is { Name: not ("list" or "none" or "presentation" or "generic") })
        {
            // An li whose list element was given another role (menu, menubar, tablist) is not a list item: Chromium ignores it as presentational
            // (profile entry list-item-in-widget).
            role = AriaTaxonomy.FindRole("none");
        }

        n.AriaRole = role;
        n.RoleOrigin = origin;
        n.Role = role is null ? string.Empty : RoleComputer.ComputedRoleName(role);
        n.Hidden = _hidden.Get(el);
        // A disabled fieldset or optgroup passes nothing on: only the controls and options it holds are disabled (profile entry disabled-inheritance).
        n.AriaDisabledChain = n.Parent is { Kind: AxNodeKind.Element } p && (p.AriaDisabledChain || (p.States.Disabled && !(p.DomNode.Namespace == ElementNamespace.Html && p.DomNode.LocalName is "fieldset" or "optgroup")));

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

        if (n.Name.Source == AxTextSource.Title && role is { Name: "generic" } && el.Namespace == ElementNamespace.Html)
        {
            // Chromium does not name a generic container from its title: the title is its description (profile entry generic-title).
            if (n.Description.IsEmpty)
            {
                n.Description = new AxText(n.Name.Value, AxTextSource.Title, "title", []);
            }

            n.Name = AxText.Empty;
        }
        else if (n.Description.Source == AxTextSource.Title && n.Name.Source == AxTextSource.Contents && string.Equals(n.Description.Value, n.Name.Value, StringComparison.Ordinal) && !HasImageContent(el))
        {
            // A title that repeats a name made of text alone is not also the description (profile entry description-repeats-name).
            n.Description = AxText.Empty;
        }

        n.States = StateBuilder.Build(el, role, nr.IsDisabled, n.AriaDisabledChain, nr.IsFocusable, () => TreeText.ApplyTextTransform(NameComputer.SelectedOptionsText(el), _styles.GetStyle(el).GetPropertyValue("text-transform")), OptionSelected, _stateHelpers);
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

        n.InternalRole = InternalRoleOf(el, role, origin, IsFirstSummary(el)) ?? LayoutTableRole(el, role, origin) ?? ProfileRole(n, el, role, origin);
        if (el.Namespace == ElementNamespace.Html && LabelIndex.IsLabelable(el))
        {
            n.NativeLabels = _names.LabelsOf(el);
        }
        ComputeIgnored(n, nr);
        if (presentationalTableRow && n.IgnoredReasons.Count == 1 && n.IgnoredReasons[0] == "presentationalRole")
        {
            n.IgnoredReasons = ["uninteresting"];
        }
    }

    private readonly Dictionary<int, int> _selectedOption = [];

    /// <summary>
    /// HTML section 4.10.7 selectedness of an option of a select: for a multiple select its selected attribute; for a single-choice one the
    /// last option with the attribute, else, when the select shows one row, the first option that is not disabled. Null when the option
    /// belongs to no select (a datalist).
    /// </summary>
    private bool? OptionSelected(Node option)
    {
        Node select = option.ParentElement;
        if (!select.IsNull && select.Namespace == ElementNamespace.Html && select.LocalName == "optgroup")
        {
            select = select.ParentElement;
        }

        if (select.IsNull || select.Namespace != ElementNamespace.Html || select.LocalName != "select")
        {
            return null;
        }

        if (select.HasAttribute("multiple"))
        {
            return option.HasAttribute("selected");
        }

        if (!_selectedOption.TryGetValue(select.Index, out int chosen))
        {
            chosen = -1;
            int firstEnabled = -1;
            int visited = 0;
            for (Node d = select.NextInDocumentOrder(select); !d.IsNull && visited++ < 100_000; d = d.NextInDocumentOrder(select))
            {
                if (!d.IsElement || d.Namespace != ElementNamespace.Html || d.LocalName != "option")
                {
                    continue;
                }

                if (d.HasAttribute("selected"))
                {
                    chosen = d.Index;
                }

                if (firstEnabled < 0 && !d.HasAttribute("disabled") && !(d.ParentElement is { IsNull: false } g && g.LocalName == "optgroup" && g.HasAttribute("disabled")))
                {
                    firstEnabled = d.Index;
                }
            }

            bool oneRow = !(select.GetAttribute("size") is { } sz && int.TryParse(sz, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int rows) && rows > 1);
            if (chosen < 0 && oneRow)
            {
                chosen = firstEnabled;
            }

            _selectedOption[select.Index] = chosen;
        }

        return chosen == option.Index;
    }

    /// <summary>
    /// With rendered facts, the internal role Chromium gives a table it judges to be for layout, and its rows and cells (profile entry
    /// <c>layout-table</c>); null otherwise. Only elements whose role is the implicit one of the HTML table, row or cell qualify.
    /// </summary>
    private string? LayoutTableRole(Node el, AriaRole? role, RoleOrigin origin)
    {
        if (_layout is null || origin != RoleOrigin.Implicit || role is null || el.Namespace != ElementNamespace.Html)
        {
            return null;
        }

        // A generic element laid out as a CSS table, row or cell.
        if (role.Name == "generic")
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

    /// <summary>True when the element's contents include an image, an SVG or an image button (looked at up to a bounded number of nodes).</summary>
    private static bool HasImageContent(Node el)
    {
        int budget = 500;
        for (Node d = el.NextInDocumentOrder(el); !d.IsNull && budget-- > 0; d = d.NextInDocumentOrder(el))
        {
            if (d.IsElement && (d.LocalName is "img" or "svg" or "area" || d.LocalName == "input" && HtmlSemantics.GetInputType(d) == HtmlInputType.Image))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Roles Chromium exposes in place of the HTML-AAM mapping (profile entries <c>description-list-role</c>, <c>figcaption-role</c>,
    /// <c>orphan-list-item</c>, <c>aside-in-sectioning</c>); null when the mapping stands.
    /// </summary>
    private static string? ProfileRole(AxNode n, Node el, AriaRole? role, RoleOrigin origin)
    {
        if (origin != RoleOrigin.Implicit || role is null || el.Namespace != ElementNamespace.Html)
        {
            return null;
        }

        switch (el.LocalName)
        {
            case "dl" when role.Name == "list":
                return "DescriptionList";
            case "figcaption" when role.Name == "caption":
                return "Figcaption";
            case "li" when role.Name == "generic":
                return "listitem";
            case "aside" when role.Name == "complementary" && n.Name.IsEmpty:
                // An unnamed aside nested in sectioning content or in another complementary landmark is not a landmark of its own.
                for (Node p = el.ParentElement; !p.IsNull; p = p.ParentElement)
                {
                    if (p.Namespace == ElementNamespace.Html && p.LocalName is "article" or "aside" or "nav" or "section"
                        || TreeText.EqualsIgnoreCase(p.GetAttribute("role"), "complementary"))
                    {
                        return "generic";
                    }
                }

                return null;
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
                // Chrome 156 lists a wbr as ignored with the reason notRendered (5,021 nodes on 8 corpus pages; profile entry wbr-not-rendered).
                reasons.Add(n.DomNode.Namespace == ElementNamespace.Svg && n.DomNode.LocalName == "foreignObject" ? "presentationalRole"
                    : n.DomNode.Namespace == ElementNamespace.Html && n.DomNode.LocalName == "wbr" ? "notRendered" : "uninteresting");
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

    /// <summary>The first element with an autofocus attribute that is focusable and exposed has focus after load (HTML section 6.6.4, flush autofocus candidates).</summary>
    private void MarkAutofocus()
    {
        foreach (AxNode n in _domOrder)
        {
            if (n.Kind == AxNodeKind.Element && !n.IsIgnored && n.Focus.IsFocusable && n.DomNode.HasAttribute("autofocus"))
            {
                n.IsAutofocus = true;
                return;
            }
        }
    }

    /// <summary>
    /// Chromium reports the options of a select as focusable although they are not stops in the sequential order (profile entry
    /// option-focusable). The focus order has already been computed, so this changes only <see cref="AxFocus.IsFocusable"/>.
    /// </summary>
    private void ComputeChromiumFocusableOptions()
    {
        foreach (AxNode n in _domOrder)
        {
            if (n.Kind == AxNodeKind.Element && !n.Focus.IsFocusable && n.Role == "option" && n.DomNode.Namespace == ElementNamespace.Html
                && n.DomNode.LocalName == "option" && !n.DomNode.HasAttribute("disabled") && !n.IsIgnored && n.Parent is { } p && p.Role is "combobox" or "listbox" or "group" or "MenuListPopup")
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
            if (n.Kind == AxNodeKind.Element && n.Role == "treeitem" && n.States.Level is null)
            {
                // A tree item is one level deeper for each group it sits in (profile entry treeitem-level).
                int groups = 0;
                for (AxNode? p = n.Parent; p is not null; p = p.Parent)
                {
                    if (p.Role == "group")
                    {
                        groups++;
                    }
                }

                n.States.Level = groups + 1;
                continue;
            }

            if (n.Kind != AxNodeKind.Element || !(n.Role == "listitem" || n.InternalRole == "listitem") || n.States.Level is not null)
            {
                continue;
            }

            // A description list is not a list for the level (it is DescriptionList in Chromium), and an item outside any list is level 1.
            int depth = 0;
            for (AxNode? p = n.Parent; p is not null; p = p.Parent)
            {
                if (p.Role == "list" && p.InternalRole is null)
                {
                    depth++;
                }
            }

            n.States.Level = Math.Max(1, depth);
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
