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

    private HiddenMap _hidden = null!;
    private OwnsMap _owns = null!;
    private RoleMap _roles = null!;
    private NameComputer _names = null!;
    private AccessibilityTree _tree = null!;
    private AxNode?[] _byDom = null!;
    private readonly List<AxNode> _domOrder = [];
    private readonly List<AxDiagnostic> _diags = [];
    private readonly Dictionary<AxNode, List<AxDiagnostic>> _nodeDiags = [];

    public TreeBuilder(Document doc, DocumentStyles styles, AccessibilityOptions options, CancellationToken ct)
    {
        _doc = doc;
        _styles = styles;
        _opt = options;
        _ct = ct;
    }

    public AccessibilityTree Run()
    {
        _hidden = HiddenMap.Compute(_doc, _styles, _ct);
        _owns = OwnsMap.Compute(_doc, _hidden, _opt, _ct);

        // Roles that depend on a name (a section is a region only when named, a role=region token only when named) need
        // names, and names need roles for a few decisions (embedded controls, contents). A first pass with the
        // name-dependent roles unresolved gives names good enough to decide them; the second pass fixes the roles.
        var counters = new CounterResolver(_doc, _styles, _hidden);
        RoleMap roles0 = RoleComputer.Compute(_doc, null, _ct);
        var probeNames = new NameComputer(_doc, _styles, _hidden, roles0, _owns, _opt, counters, _ct);
        _roles = RoleComputer.Compute(_doc, new RoleOptions { HasAccessibleName = n => !probeNames.ComputeName(n).IsEmpty }, _ct);
        _names = new NameComputer(_doc, _styles, _hidden, _roles, _owns, _opt, counters, _ct);

        _tree = new AccessibilityTree(_doc, _styles, _opt, _hidden, _roles, _owns);
        _byDom = new AxNode?[_doc.NodeCount];
        CreateNodes();
        LinkChildren();
        AssignIds();
        ComputeNodes();
        ComputeFocusOrder();
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
                    Create(AxNodeKind.Element, cur);
                    descend = !cur.FirstChild.IsNull;
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
                parent = _byDom[n.DomNode.Parent.Index];
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
                default:
                    ComputeElement(n);
                    break;
            }
        }
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
        if (TreeText.Collapse(d).Length == 0 && reasons.Count == 0)
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
        n.Name = _names.ComputeName(el);

        n.Description = _names.ComputeDescription(el, n.Name.Source);

        n.States = StateBuilder.Build(el, role, nr.IsDisabled, n.AriaDisabledChain, () => NameComputer.SelectedOptionsText(el));
        n.Relations = BuildRelations(n);
        n.Focus = new AxFocus(IsFocusableNow(n, nr), TabIndexOf(el), -1);

        // Role diagnostics of the role layer.
        foreach (RoleDiagnostic d in nr.Diagnostics)
        {
            AddDiagnostic(n, new AxDiagnostic(AxDiagnosticKind.Role, el, d.Attribute, d.Value, d.Message, d.Kind));
        }

        ComputeIgnored(n, nr);
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
            reasons.Add("uninteresting");
        }
        else if (role.Name == "generic" && !IsInterestingGeneric(n, nr))
        {
            reasons.Add("uninteresting");
        }

        n.IsIgnored = reasons.Count > 0;
        n.IgnoredReasons = reasons;
    }

    private static bool IsInterestingGeneric(AxNode n, NodeRole nr)
    {
        if (!n.Name.IsEmpty || !n.Description.IsEmpty || nr.IsFocusable)
        {
            return true;
        }

        foreach (Attr a in n.DomNode.Attributes)
        {
            string name = a.LocalName;
            if (name is "role" or "tabindex" or "contenteditable" or "title" || name.StartsWith("aria-", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

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
                m.States.PosInSet ??= pos;
                m.States.SetSize ??= size;
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
