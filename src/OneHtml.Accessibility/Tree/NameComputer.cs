using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RecursiveAI.OneHtml.Accessibility.Aria;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Tree;

/// <summary>Which label elements name which form controls (HTML section 4.10.4 "labeled control").</summary>
internal sealed class LabelIndex
{
    private readonly Dictionary<int, List<Node>> _labels = new();

    public static bool IsLabelable(Node n)
    {
        if (!n.IsElement || n.Namespace != ElementNamespace.Html)
        {
            return false;
        }

        switch (n.LocalName)
        {
            case "button":
            case "meter":
            case "output":
            case "progress":
            case "select":
            case "textarea":
                return true;
            case "input":
                return HtmlSemantics.GetInputType(n) != HtmlInputType.Hidden;
            default:
                return false;
        }
    }

    public IReadOnlyList<Node> For(Node control) => _labels.TryGetValue(control.Index, out List<Node>? l) ? l : [];

    public static LabelIndex Build(Document doc, System.Threading.CancellationToken ct)
    {
        var index = new LabelIndex();
        long budget = 2_000_000;
        int visited = 0;
        foreach (Node e in doc.Root.DescendantElements)
        {
            if ((++visited & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            if (e.Namespace != ElementNamespace.Html || e.LocalName != "label")
            {
                continue;
            }

            Node target = default;
            string? f = e.GetAttribute("for");
            if (f is not null)
            {
                Node t = f.Length == 0 ? default : doc.GetElementById(f);
                if (!t.IsNull && IsLabelable(t))
                {
                    target = t;
                }
            }
            else
            {
                // The first labelable descendant in tree order.
                for (Node d = e.NextInDocumentOrder(e); !d.IsNull && budget-- > 0; d = d.NextInDocumentOrder(e))
                {
                    if (IsLabelable(d))
                    {
                        target = d;
                        break;
                    }
                }
            }

            if (!target.IsNull)
            {
                if (!index._labels.TryGetValue(target.Index, out List<Node>? list))
                {
                    index._labels[target.Index] = list = [];
                }

                list.Add(e);
            }
        }

        return index;
    }
}

/// <summary>
/// The accessible name and description computation (Accessible Name and Description Computation 1.2 section 4.3). The
/// algorithm is recursive by definition; here it is a loop over an explicit stack of frames, each frame a position in the
/// steps of one node, so depth of input never reaches the call stack. A node already being computed in the same mode is
/// skipped, which ends reference cycles.
/// </summary>
internal sealed class NameComputer
{
    private enum Stage : byte
    {
        Start,
        Refs,
        AriaLabel,
        Embedded,
        NativePre,
        Contents,
        Title,
        NativePost,
    }

    private enum StepKind : byte
    {
        Attr,
        Elements,
        Const,
    }

    private readonly record struct NativeStep(StepKind Kind, string? Name, string? Text, IReadOnlyList<Node>? Nodes);

    private sealed class Frame
    {
        public required Node Node;
        public bool Top;
        public bool Direct;
        public bool InLabelledBy;
        public bool InNative;
        public HiddenReasons BaseMask;
        public bool SkipSelf;
        public bool Descendant;
        public bool FigureTried;
        public bool ForceContents;
        public Stage Stage;
        public Stage AfterRefs;
        public int OutStart;
        public List<Node>? Refs;
        public int RefIdx;
        public int RefMark = -1;
        public AxTextSource RefsSource;
        public bool RefsInNative;
        public List<NativeStep>? Native;
        public int NativeIdx;
        public int Phase;
        public Node Cursor;
        public List<int>? Owned;
        public int OwnedIdx;
        public bool PendingSpace;
        public AxTextSource Source;
        public string? Attribute;
        public List<Node>? Related;
        public bool Synthetic;
        public int VisitKey;
        public bool Visiting;
    }

    private readonly Document _doc;
    private readonly DocumentStyles _styles;
    private readonly HiddenMap _hidden;
    private readonly RoleMap _roles;
    private readonly OwnsMap _owns;
    private readonly AccessibilityOptions _options;
    private readonly CounterResolver _counters;
    private readonly System.Threading.CancellationToken _ct;
    private LabelIndex? _labels;
    private long _work;
    private int _topIndex = -1;

    private readonly StringBuilder _sb = new();
    private readonly List<Frame> _stack = [];
    private readonly HashSet<int> _visiting = [];
    private readonly HashSet<int> _referenced = [];

    public NameComputer(Document doc, DocumentStyles styles, HiddenMap hidden, RoleMap roles, OwnsMap owns, AccessibilityOptions options, CounterResolver counters, System.Threading.CancellationToken ct)
    {
        _doc = doc;
        _styles = styles;
        _hidden = hidden;
        _roles = roles;
        _owns = owns;
        _options = options;
        _counters = counters;
        _ct = ct;
    }

    /// <summary>True once the work limit stopped a computation.</summary>
    public bool LimitReached { get; private set; }

    /// <summary>The label elements of a control; built on first use.</summary>
    public IReadOnlyList<Node> LabelsOf(Node control) => (_labels ??= LabelIndex.Build(_doc, _ct)).For(control);

    /// <summary>The accessible name of an element (AccName 1.2 section 4.3.2 with the element as the root of the computation).</summary>
    public AxText ComputeName(Node element) =>
        Run(new Frame { Node = element, Top = true, Stage = Stage.Start });

    /// <summary>The accessible description (AccName 1.2 section 4.3.3 as HTML-AAM and Core-AAM apply it).</summary>
    /// <param name="element">The element.</param>
    /// <param name="nameSource">Where the element's name came from; a <c>title</c> already used there is not the description.</param>
    public AxText ComputeDescription(Node element, AxTextSource nameSource)
    {
        List<Node> refs = ResolveRefs(element, "aria-describedby");
        if (refs.Count > 0)
        {
            AxText t = Run(new Frame
            {
                Node = element,
                Synthetic = true,
                Stage = Stage.Refs,
                Refs = refs,
                RefsSource = AxTextSource.DescribedBy,
                AfterRefs = Stage.NativePost,
                Related = refs,
            });
            if (!t.IsEmpty)
            {
                return t;
            }
        }

        string? d = element.GetAttribute("aria-description");
        if (d is not null && TreeText.Trim(d).Length > 0)
        {
            return new AxText(Limit(TreeText.Collapse(d)), AxTextSource.AriaDescription, "aria-description", []);
        }

        if (nameSource != AxTextSource.Title && element.GetAttribute("title") is { } title && TreeText.Trim(title).Length > 0)
        {
            return new AxText(Limit(TreeText.Collapse(title)), AxTextSource.Title, "title", []);
        }

        return AxText.Empty;
    }

    private string Limit(string s) => s.Length <= _options.MaxNameLength ? s : s[.._options.MaxNameLength];

    // The role the tree gives the element: the descendants of a button, tab or image keep their own role, as Chromium exposes them
    // (profile entry children-presentational), so a heading inside a button is still named from its contents.
    private AriaRole? RoleOf(Node n)
    {
        if (!n.IsElement)
        {
            return null;
        }

        NodeRole nr = _roles[n];
        return nr.Origin == RoleOrigin.ChildrenPresentational ? nr.ExplicitRole ?? nr.ImplicitRole : nr.Role;
    }

    private List<Node> ResolveRefs(Node el, string attribute)
    {
        var list = new List<Node>();
        string? v = el.GetAttribute(attribute);
        if (v is null)
        {
            return list;
        }

        int pos = 0;
        int count = 0;
        while (OwnsMap.NextToken(v, ref pos, out string token) && count++ < _options.MaxIdReferences)
        {
            Node t = _doc.GetElementById(token);
            if (!t.IsNull && !list.Contains(t))
            {
                list.Add(t);
            }
        }

        return list;
    }

    // ---- the loop ----

    private AxText Run(Frame top)
    {
        _sb.Clear();
        _stack.Clear();
        _visiting.Clear();
        _referenced.Clear();
        top.OutStart = 0;
        _topIndex = top.Node.Index;
        _stack.Add(top);
        int steps = 0;
        while (_stack.Count > 0)
        {
            if ((++steps & 1023) == 0)
            {
                _ct.ThrowIfCancellationRequested();
            }

            Step(_stack[^1]);
        }

        string text = TreeText.Collapse(_sb.ToString());
        _sb.Clear();
        if (text.Length == 0)
        {
            return AxText.Empty;
        }

        return new AxText(Limit(text), top.Source, top.Attribute, top.Related ?? []);
    }

    private void Finish(Frame f, AxTextSource source, string? attribute = null, List<Node>? related = null)
    {
        f.Source = source;
        f.Attribute = attribute;
        f.Related = related ?? f.Related;
        Pop(f);
    }

    private void Pop(Frame f)
    {
        if (f.Visiting)
        {
            _visiting.Remove(f.VisitKey);
        }

        _stack.RemoveAt(_stack.Count - 1);
    }

    private void Fail(Frame f)
    {
        _sb.Length = f.OutStart;
        f.Source = AxTextSource.None;
        Pop(f);
    }

    private void Push(Frame parent, Node node, bool direct, bool inLabelledBy, bool inNative, bool descendant)
    {
        var f = new Frame
        {
            Node = node,
            Direct = direct,
            InLabelledBy = inLabelledBy,
            InNative = inNative,
            Stage = Stage.Start,
            OutStart = _sb.Length,
            BaseMask = direct ? HiddenReasons.None : parent.BaseMask,
        };
        f.Descendant = descendant;
        _stack.Add(f);
    }

    private const HiddenReasons Considered = HiddenMap.NotRenderedMask | HiddenReasons.AriaHidden | HiddenReasons.VisibilityHidden;

    /// <summary>
    /// AccName 1.2 step 2A with the note on step 2B: a node hidden for a reason its referenced root does not share is left
    /// out; a root that is itself hidden brings its whole subtree in, whatever hides it.
    /// </summary>
    private HiddenReasons HiddenFor(Node n, HiddenReasons baseMask)
    {
        HiddenReasons r = n.Kind == NodeKind.Text
            ? (_hidden.Own(n.Index) & HiddenMap.NotRenderedMask) | (_hidden.Reasons(n.Index) & HiddenReasons.VisibilityHidden)
            : _hidden.Own(n.Index) & Considered;
        return r & ~baseMask;
    }

    private void Step(Frame f)
    {
        switch (f.Stage)
        {
            case Stage.Start:
                DoStart(f);
                break;
            case Stage.Refs:
                DoRefs(f);
                break;
            case Stage.AriaLabel:
                DoAriaLabel(f);
                break;
            case Stage.Embedded:
                DoEmbedded(f);
                break;
            case Stage.NativePre:
                DoNativePre(f);
                break;
            case Stage.Contents:
                DoContents(f);
                break;
            case Stage.Title:
                DoTitle(f);
                break;
            default:
                DoNativePost(f);
                break;
        }
    }

    private void DoStart(Frame f)
    {
        Node n = f.Node;
        if (++_work > _options.MaxNameWork)
        {
            LimitReached = true;
            Fail(f);
            return;
        }

        // AccName 1.2 step 2A: a hidden node contributes nothing unless it is the root or directly referenced.
        if (f.Top || f.Direct)
        {
            f.BaseMask |= n.IsElement ? _hidden.Reasons(n.Index) & Considered : HiddenReasons.None;
        }
        else
        {
            HiddenReasons hidden = HiddenFor(n, f.BaseMask);
            if (hidden != HiddenReasons.None)
            {
                // A node that is only visibility: hidden still lets visible descendants through.
                if (hidden == HiddenReasons.VisibilityHidden && n.IsElement)
                {
                    f.SkipSelf = true;
                }
                else
                {
                    Fail(f);
                    return;
                }
            }

            if (n.Index == _topIndex)
            {
                // The element being named is not part of its own label.
                Fail(f);
                return;
            }

            if (_referenced.Contains(n.Index))
            {
                // Already used as the target of an aria-labelledby or aria-describedby in this computation: not counted twice.
                Fail(f);
                return;
            }
        }

        f.VisitKey = (n.Index << 1) | (f.InLabelledBy ? 1 : 0);
        if (!_visiting.Add(f.VisitKey))
        {
            Fail(f);
            return;
        }

        f.Visiting = true;

        if (n.Kind == NodeKind.Text)
        {
            // Step 2G.
            _sb.Append(n.Data);
            Finish(f, AxTextSource.TextNode);
            return;
        }

        if (!n.IsElement)
        {
            Fail(f);
            return;
        }

        if (f.SkipSelf)
        {
            f.ForceContents = true;
            f.Stage = Stage.Contents;
            return;
        }

        // Step 2B: aria-labelledby, unless already inside one.
        if (!f.InLabelledBy)
        {
            List<Node> refs = ResolveRefs(n, "aria-labelledby");
            if (refs.Count > 0)
            {
                f.Refs = refs;
                f.RefIdx = 0;
                f.RefsSource = AxTextSource.LabelledBy;
                f.AfterRefs = Stage.AriaLabel;
                f.RefsInNative = false;
                f.Stage = Stage.Refs;
                return;
            }
        }

        f.Stage = Stage.AriaLabel;
    }

    private void DoRefs(Frame f)
    {
        if (f.RefMark >= 0)
        {
            // A referenced node finished: separate it from the text before it with one space.
            if (_sb.Length > f.RefMark && f.RefMark > f.OutStart)
            {
                _sb.Insert(f.RefMark, ' ');
            }

            f.RefMark = -1;
        }

        if (f.RefIdx < f.Refs!.Count)
        {
            Node r = f.Refs[f.RefIdx++];
            if (f.RefsSource is AxTextSource.LabelledBy or AxTextSource.DescribedBy)
            {
                _referenced.Add(r.Index);
            }

            f.RefMark = _sb.Length;
            Push(f, r, direct: !f.RefsInNative, inLabelledBy: !f.RefsInNative, inNative: f.RefsInNative, descendant: false);
            return;
        }

        if (TreeText.HasNonWhitespace(_sb, f.OutStart))
        {
            Finish(f, f.RefsSource, null, f.Refs);
            return;
        }

        _sb.Length = f.OutStart;
        if (f.Synthetic)
        {
            Fail(f);
            return;
        }

        f.Related = null;
        f.Stage = f.AfterRefs;
    }

    private static bool IsEmbeddedControl(AriaRole? role) =>
        role is { Name: "textbox" or "combobox" or "listbox" or "slider" or "spinbutton" or "progressbar" or "meter" or "scrollbar" };

    /// <summary>AccName 1.2 step 2E: a control inside a label, a referenced element or the contents being read contributes its value.</summary>
    private bool IsEmbedded(Frame f, AriaRole? role) =>
        !f.Top && f.Node.Index != _topIndex && (f.InLabelledBy || f.InNative || f.Descendant) && IsEmbeddedControl(role);

    private void DoAriaLabel(Frame f)
    {
        Node n = f.Node;
        string? label = n.GetAttribute("aria-label");
        bool embedded = IsEmbedded(f, RoleOf(n));
        if (label is not null && TreeText.Trim(label).Length > 0 && !embedded)
        {
            // Step 2C.
            _sb.Append(label);
            Finish(f, AxTextSource.AriaLabel, "aria-label");
            return;
        }

        f.Stage = Stage.Embedded;
    }

    private void DoEmbedded(Frame f)
    {
        Node n = f.Node;
        AriaRole? role = RoleOf(n);
        f.Stage = Stage.NativePre;
        if (!IsEmbedded(f, role))
        {
            return;
        }

        // Step 2E: a control inside a label contributes its value.
        string? text = EmbeddedValue(n, role!);
        if (text is null)
        {
            f.ForceContents = true;
            return;
        }

        _sb.Append(text);
        Finish(f, AxTextSource.Contents);
    }

    private static string? EmbeddedValue(Node n, AriaRole role)
    {
        bool html = n.Namespace == ElementNamespace.Html;
        switch (role.Name)
        {
            case "textbox":
                if (html && n.LocalName == "input")
                {
                    return n.GetAttribute("value") ?? string.Empty;
                }

                if (html && n.LocalName == "textarea")
                {
                    string t = n.TextContent ?? string.Empty;
                    return t.StartsWith('\n') ? t[1..] : t;
                }

                return null;
            case "combobox":
            case "listbox":
                if (html && n.LocalName == "select")
                {
                    return SelectedOptionsText(n);
                }

                if (html && n.LocalName == "input")
                {
                    return n.GetAttribute("value") ?? string.Empty;
                }

                return AriaSelectedText(n) is { Length: > 0 } picked ? picked : null;
            default:
            {
                string? vt = n.GetAttribute("aria-valuetext");
                if (vt is not null && TreeText.Trim(vt).Length > 0)
                {
                    return vt;
                }

                string? vn = n.GetAttribute("aria-valuenow");
                if (vn is not null && TreeText.Trim(vn).Length > 0)
                {
                    return vn;
                }

                if (html && n.LocalName == "input")
                {
                    return n.GetAttribute("value") ?? string.Empty;
                }

                if (html && n.LocalName is "progress" or "meter")
                {
                    return n.GetAttribute("value") ?? string.Empty;
                }

                return string.Empty;
            }
        }
    }

    private static string OptionText(Node option)
    {
        string? label = option.GetAttribute("label");
        return label is { Length: > 0 } ? label : TreeText.Collapse(option.TextContent ?? string.Empty);
    }

    internal static string SelectedOptionsText(Node select)
    {
        var sb = new StringBuilder();
        Node first = default;
        bool multiple = select.HasAttribute("multiple");
        int visited = 0;
        for (Node d = select.NextInDocumentOrder(select); !d.IsNull && visited++ < 100_000; d = d.NextInDocumentOrder(select))
        {
            if (!d.IsElement || d.Namespace != ElementNamespace.Html || d.LocalName != "option")
            {
                continue;
            }

            if (first.IsNull)
            {
                first = d;
            }

            if (d.HasAttribute("selected"))
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(OptionText(d));
                if (!multiple)
                {
                    return sb.ToString();
                }
            }
        }

        // A single-selection select with no selected option shows its first option.
        if (sb.Length == 0 && !multiple && !first.IsNull)
        {
            return OptionText(first);
        }

        return sb.ToString();
    }

    private static string AriaSelectedText(Node widget)
    {
        var sb = new StringBuilder();
        int visited = 0;
        for (Node d = widget.NextInDocumentOrder(widget); !d.IsNull && visited++ < 100_000; d = d.NextInDocumentOrder(widget))
        {
            if (d.IsElement && TreeText.EqualsIgnoreCase(d.GetAttribute("aria-selected"), "true"))
            {
                if (sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(TreeText.Collapse(d.TextContent ?? string.Empty));
            }
        }

        return sb.ToString();
    }

    private void DoNativePre(Frame f)
    {
        f.Native ??= BuildNative(f.Node);
        while (f.NativeIdx < f.Native.Count)
        {
            NativeStep st = f.Native[f.NativeIdx++];
            switch (st.Kind)
            {
                case StepKind.Attr:
                {
                    string? v = f.Node.GetAttribute(st.Name!);
                    if (v is not null && st.Name == "alt" && !f.Direct && TreeText.Trim(v).Length == 0)
                    {
                        // HTML-AAM: an image with an empty alt is decorative; no other attribute names it.
                        Fail(f);
                        return;
                    }

                    if (v is not null && TreeText.Trim(v).Length > 0)
                    {
                        _sb.Append(v);
                        Finish(f, AxTextSource.NativeAttribute, st.Name);
                        return;
                    }

                    break;
                }

                case StepKind.Elements:
                    if (st.Nodes is { Count: > 0 })
                    {
                        f.Refs = [.. st.Nodes];
                        f.RefIdx = 0;
                        f.RefsSource = AxTextSource.NativeLabelElement;
                        f.RefsInNative = true;
                        f.AfterRefs = Stage.NativePre;
                        f.Stage = Stage.Refs;
                        return;
                    }

                    break;
                default:
                    break;
            }
        }

        f.Stage = Stage.Contents;
    }

    private List<NativeStep> BuildNative(Node n)
    {
        var steps = new List<NativeStep>();
        if (n.Namespace == ElementNamespace.Svg)
        {
            // SVG-AAM section 5.2: a title child element names the element.
            for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.IsElement && c.Namespace == ElementNamespace.Svg && c.LocalName == "title")
                {
                    steps.Add(new NativeStep(StepKind.Elements, null, null, [c]));
                    break;
                }
            }

            return steps;
        }

        if (n.Namespace != ElementNamespace.Html)
        {
            return steps;
        }

        switch (n.LocalName)
        {
            case "input":
                switch (HtmlSemantics.GetInputType(n))
                {
                    case HtmlInputType.Hidden:
                        break;
                    case HtmlInputType.Image:
                        steps.Add(new NativeStep(StepKind.Elements, null, null, LabelsOf(n)));
                        steps.Add(new NativeStep(StepKind.Attr, "alt", null, null));
                        break;
                    case HtmlInputType.Button:
                    case HtmlInputType.Submit:
                    case HtmlInputType.Reset:
                        steps.Add(new NativeStep(StepKind.Elements, null, null, LabelsOf(n)));
                        steps.Add(new NativeStep(StepKind.Attr, "value", null, null));
                        break;
                    default:
                        steps.Add(new NativeStep(StepKind.Elements, null, null, LabelsOf(n)));
                        break;
                }

                break;
            case "button":
            case "select":
            case "textarea":
            case "meter":
            case "progress":
            case "output":
                steps.Add(new NativeStep(StepKind.Elements, null, null, LabelsOf(n)));
                break;
            case "img":
            case "area":
                steps.Add(new NativeStep(StepKind.Attr, "alt", null, null));
                break;
            case "fieldset":
                AddChild(steps, n, "legend");
                break;
            case "table":
                AddChild(steps, n, "caption");
                break;
            case "figure":
                AddChild(steps, n, "figcaption");
                break;
            case "optgroup":
            case "track":
                steps.Add(new NativeStep(StepKind.Attr, "label", null, null));
                break;
            case "option":
                steps.Add(new NativeStep(StepKind.Attr, "label", null, null));
                break;
            default:
                break;
        }

        return steps;
    }

    private void AddChild(List<NativeStep> steps, Node n, string local)
    {
        for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.IsElement && c.Namespace == ElementNamespace.Html && c.LocalName == local && !_hidden.NotRendered(c.Index))
            {
                steps.Add(new NativeStep(StepKind.Elements, null, null, [c]));
                return;
            }
        }
    }

    private bool ContentsAllowed(Frame f)
    {
        if (f.Node.Namespace == ElementNamespace.Html && f.Node.LocalName == "select")
        {
            // The options of a select are shown only in its popup: they are not its contents.
            return false;
        }

        if (!f.Top || f.ForceContents)
        {
            return true;
        }

        AriaRole? role = RoleOf(f.Node);
        if (role is not null)
        {
            // A row is not named from its cells: the cells carry the text (profile entry row-name).
            // Profile entry name-from-contents-roles: Chromium names a dt (a term) from its contents, which WPT html-aam does not expect of a dfn, and a time not at all.
            return (role.Name == "term" && f.Node.LocalName == "dt") || (role.NameFromContents && role.Name is not ("row" or "time"));
        }

        // HTML-AAM: summary has no role of its own but is named from its contents.
        return f.Node.Namespace == ElementNamespace.Html && f.Node.LocalName == "summary";
    }

    private void DoContents(Frame f)
    {
        if (!ContentsAllowed(f))
        {
            f.Stage = Stage.Title;
            return;
        }

        if (f.PendingSpace)
        {
            _sb.Append(' ');
            f.PendingSpace = false;
        }

        Node el = f.Node;
        while (true)
        {
            switch (f.Phase)
            {
                case 0:
                    f.Phase = 1;
                    if (!f.SkipSelf)
                    {
                        AppendPseudo(el, PseudoElementKind.Before);
                    }

                    f.Cursor = el.FirstChild;
                    break;
                case 1:
                {
                    if (f.Cursor.IsNull)
                    {
                        f.Phase = 2;
                        f.Owned = _owns.OwnedBy(el.Index);
                        f.OwnedIdx = 0;
                        break;
                    }

                    Node c = f.Cursor;
                    f.Cursor = c.NextSibling;
                    if (c.IsElement && _owns.OwnerOf(c.Index) >= 0)
                    {
                        break;
                    }

                    if (VisitChild(f, c))
                    {
                        return;
                    }

                    break;
                }

                case 2:
                {
                    if (f.Owned is null || f.OwnedIdx >= f.Owned.Count)
                    {
                        f.Phase = 3;
                        break;
                    }

                    Node oc = _doc.GetNode(f.Owned[f.OwnedIdx++]);
                    if (VisitChild(f, oc))
                    {
                        return;
                    }

                    break;
                }

                case 3:
                    f.Phase = 4;
                    if (!f.SkipSelf)
                    {
                        AppendPseudo(el, PseudoElementKind.After);
                    }

                    break;
                default:
                    if (TreeText.HasNonWhitespace(_sb, f.OutStart))
                    {
                        Finish(f, AxTextSource.Contents);
                        return;
                    }

                    // A descendant that holds only white space keeps it (it separates its neighbours), unless a title can name it.
                    if (!f.Top && !f.Direct && _sb.Length > f.OutStart && f.Node.GetAttribute("title") is null)
                    {
                        Finish(f, AxTextSource.Contents);
                        return;
                    }

                    _sb.Length = f.OutStart;
                    f.Stage = Stage.Title;
                    return;
            }
        }
    }

    /// <summary>Handles one child of a contents traversal; true when a frame was pushed for it.</summary>
    private bool VisitChild(Frame f, Node c)
    {
        if (++_work > _options.MaxNameWork)
        {
            LimitReached = true;
            return false;
        }

        if (c.Kind == NodeKind.Text)
        {
            if (HiddenFor(c, f.BaseMask) == HiddenReasons.None)
            {
                _sb.Append(TransformText(c));
            }

            return false;
        }

        if (!c.IsElement)
        {
            return false;
        }

        HiddenReasons hiddenReasons = HiddenFor(c, f.BaseMask);
        if (hiddenReasons != HiddenReasons.None && hiddenReasons != HiddenReasons.VisibilityHidden)
        {
            return false;
        }

        // SVG 2 (Rendering): style and script elements are never rendered, so their text is no content (profile entry svg-unrendered-elements).
        if (c.Namespace == ElementNamespace.Svg && c.LocalName is "style" or "script")
        {
            return false;
        }

        if (c.Namespace == ElementNamespace.Html && c.LocalName == "br")
        {
            _sb.Append(' ');
            return false;
        }

        // Block-level children are set off by a space on each side; inline ones run together (step 2F.ii).
        if (!IsInline(c))
        {
            _sb.Append(' ');
            f.PendingSpace = true;
        }

        Push(f, c, direct: false, inLabelledBy: f.InLabelledBy, inNative: f.InNative, descendant: true);
        return true;
    }

    /// <summary>The text of a text node as rendered: <c>text-transform</c> of its parent applied (CSS Text 3 section 2.1).</summary>
    private string TransformText(Node text)
    {
        string data = text.Data.ToString();
        Node parent = text.Parent;
        return parent.IsElement ? TreeText.ApplyTextTransform(data, _styles.GetStyle(parent).GetPropertyValue("text-transform")) : data;
    }

    /// <summary>
    /// True when the element's box is a plain inline one, whose text runs on with its neighbours. Profile entry
    /// <c>name-block-spacing</c>: a <c>display: contents</c> element and a child of a flex or grid container (blockified, CSS Display 3
    /// section 2.7) are set off by spaces like blocks.
    /// </summary>
    private bool IsInline(Node el)
    {
        DisplayValue d = _styles.GetStyle(el).Display;
        if (d.Kind != DisplayKind.Box || d.Outside != DisplayOutside.Inline || d.Inside != DisplayInside.Flow || d.IsListItem)
        {
            return false;
        }

        Node parent = el.Parent;
        if (parent.IsElement)
        {
            DisplayValue pd = _styles.GetStyle(parent).Display;
            if (pd.Kind == DisplayKind.Box && pd.Inside is DisplayInside.Flex or DisplayInside.Grid)
            {
                return false;
            }
        }

        return true;
    }

    private void AppendPseudo(Node el, PseudoElementKind kind)
    {
        ComputedStyle? ps = _styles.GetPseudoStyle(el, kind);
        if (ps is null || !ps.Display.IsRendered)
        {
            return;
        }

        ContentValue? content = ps.Content;
        if (content is null || content.IsNone || content.IsNormal)
        {
            return;
        }

        IReadOnlyList<ContentItem> items = content.HasAlternativeText ? content.AlternativeText : content.Items;
        var sb = new StringBuilder();
        foreach (ContentItem item in items)
        {
            switch (item.Kind)
            {
                case ContentItemKind.Text:
                    sb.Append(item.Text);
                    break;
                case ContentItemKind.Attr:
                    sb.Append(item.Text is null ? null : el.GetAttribute(item.Text) ?? item.Fallback);
                    break;
                case ContentItemKind.Counter:
                    sb.Append(_counters.Resolve(el, kind, item));
                    break;
                case ContentItemKind.OpenQuote:
                    sb.Append('\u201C');
                    break;
                case ContentItemKind.CloseQuote:
                    sb.Append('\u201D');
                    break;
                default:
                    break;
            }
        }

        bool inline = ps.Display.Kind == DisplayKind.Contents || (ps.Display.Kind == DisplayKind.Box && ps.Display.Outside == DisplayOutside.Inline && ps.Display.Inside == DisplayInside.Flow);
        if (!inline || content.HasAlternativeText)
        {
            _sb.Append(' ').Append(sb).Append(' ');
        }
        else
        {
            _sb.Append(sb);
        }
    }

    private void DoTitle(Frame f)
    {
        // Step 2I: the tooltip attribute.
        string? title = f.Node.GetAttribute("title");
        if (title is not null && TreeText.Trim(title).Length > 0)
        {
            _sb.Append(title);
            Finish(f, AxTextSource.Title, "title");
            return;
        }

        f.Stage = Stage.NativePost;
    }

    private Node FigureCaption(Node img)
    {
        Node figure = img.Parent;
        if (!figure.IsElement || figure.Namespace != ElementNamespace.Html || figure.LocalName != "figure")
        {
            return default;
        }

        Node caption = default;
        for (Node c = figure.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == NodeKind.Text)
            {
                foreach (char ch in c.Data)
                {
                    if (!TreeText.IsWs(ch))
                    {
                        return default;
                    }
                }
            }
            else if (c.IsElement && c.Namespace == ElementNamespace.Html && c.LocalName == "figcaption")
            {
                if (caption.IsNull && !_hidden.NotRendered(c.Index))
                {
                    caption = c;
                }
            }
            else if (c != img)
            {
                return default;
            }
        }

        return caption;
    }

    private void DoNativePost(Frame f)
    {
        Node n = f.Node;
        if (!f.Synthetic && n.Namespace == ElementNamespace.Html)
        {
            string local = n.LocalName;
            bool placeholderField = local == "textarea"
                || (local == "input" && HtmlSemantics.GetInputType(n) is HtmlInputType.Text or HtmlInputType.Search or HtmlInputType.Telephone or HtmlInputType.Url or HtmlInputType.Email or HtmlInputType.Password or HtmlInputType.Number);
            if (placeholderField && n.GetAttribute("placeholder") is { } ph && TreeText.Trim(ph).Length > 0)
            {
                _sb.Append(ph);
                Finish(f, AxTextSource.Placeholder, "placeholder");
                return;
            }

            if (placeholderField && n.GetAttribute("aria-placeholder") is { } aph && TreeText.Trim(aph).Length > 0)
            {
                _sb.Append(aph);
                Finish(f, AxTextSource.Placeholder, "aria-placeholder");
                return;
            }

            if (local == "img" && !f.FigureTried && n.GetAttribute("alt") is null && FigureCaption(n) is { IsNull: false } caption)
            {
                // HTML-AAM: an img with no alt that fills its figure is named by the figure's figcaption.
                f.FigureTried = true;
                f.Refs = [caption];
                f.RefIdx = 0;
                f.RefsSource = AxTextSource.NativeLabelElement;
                f.RefsInNative = true;
                f.AfterRefs = Stage.NativePost;
                f.Stage = Stage.Refs;
                return;
            }

            if (local == "input")
            {
                // The text a browser shows on a button with no value of its own (HTML section 4.10.5.1).
                string? text = HtmlSemantics.GetInputType(n) switch
                {
                    HtmlInputType.Submit or HtmlInputType.Image => "Submit",
                    HtmlInputType.Reset => "Reset",
                    _ => null,
                };
                if (text is not null)
                {
                    _sb.Append(text);
                    Finish(f, AxTextSource.NativeAttribute);
                    return;
                }
            }
        }

        Fail(f);
    }
}
