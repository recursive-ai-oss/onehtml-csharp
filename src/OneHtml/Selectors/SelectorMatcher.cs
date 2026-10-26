using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Dom;

namespace RecursiveAI.OneHtml.Selectors;

/// <summary>Matching state for one query or one <c>Matches</c> call. A ref struct so a single match allocates nothing.</summary>
internal ref struct MatchCtx
{
    public Document Doc;
    public Arena A;

    /// <summary>Arena index of the element <c>:scope</c> matches, or -1 for none.</summary>
    public int Scope;

    public InteractionState? State;
    public bool Quirks;

    /// <summary>True for a query over many elements, which makes per-query caches worthwhile.</summary>
    public bool Bulk;

    /// <summary>Root of the tree being queried (for the <c>:has()</c> marks).</summary>
    public int TreeRoot;

    public int Steps;
    public bool UseMemo;
    public HashSet<(int Selector, int Compound, int Element)>? Failed;
    public Dictionary<ComplexSelector, ulong[]>? HasMarks;
    public Dictionary<Simple, NthOfCache>? NthCaches;

    /// <summary>Match the originating element of a selector that ends in a pseudo-element: the trailing pseudo-element itself is ignored (Selectors 4 section 3.6).</summary>
    public bool IgnoreTrailingPseudo;
}

internal sealed class NthOfCache
{
    public NthOfCache(int count)
    {
        FromStart = new int[count];
        FromEnd = new int[count];
        Done = new bool[count];
    }

    public int[] FromStart { get; }

    public int[] FromEnd { get; }

    /// <summary>Per parent: whether its children have been numbered.</summary>
    public bool[] Done { get; }
}

/// <summary>Right-to-left matching of compiled selectors (Selectors 4 §18) and the query entry points built on it.</summary>
internal static class SelectorMatcher
{
    // A candidate that needs more steps than this turns on the failure memo, which bounds the work by
    // (compounds x elements) however the selector backtracks.
    private const int MemoThreshold = 64;

    // ---- entry points ----

    public static bool Matches(Node element, SelectorList list, SelectorMatchOptions? options)
    {
        MatchCtx c = Create(element, options, false);
        return MatchList(ref c, list, element.Index);
    }

    /// <summary>
    /// Tests candidate complex selectors (rule, index) against one element and appends the ones that match. One match
    /// context serves all candidates, so the per-element setup is paid once. The cascade needs the individual selector
    /// that matched for its specificity.
    /// </summary>
    internal static void FilterMatching(Node element, List<Css.RuleSel> candidates, SelectorMatchOptions? options, List<Css.RuleSel> matched)
    {
        MatchCtx c = Create(element, options, options?.BulkCache is not null);
        if (options?.BulkCache is { } cache)
        {
            c.HasMarks = cache.HasMarks;
            c.NthCaches = cache.Nth;
        }

        int el = element.Index;
        foreach (Css.RuleSel rs in candidates)
        {
            ResetBacktracking(ref c);
            if (MatchComplex(ref c, rs.Rule.Selectors!.Items[rs.Sel], el))
            {
                matched.Add(rs);
            }
        }
    }

    /// <summary>
    /// Tests candidate selectors that end in a pseudo-element against the element the pseudo-element hangs from, ignoring
    /// the pseudo-element itself, and appends the ones that match.
    /// </summary>
    internal static void FilterMatchingPseudo(Node element, List<Css.RuleSel> candidates, SelectorMatchOptions? options, List<Css.RuleSel> matched)
    {
        MatchCtx c = Create(element, options, false);
        c.IgnoreTrailingPseudo = true;
        int el = element.Index;
        foreach (Css.RuleSel rs in candidates)
        {
            ComplexSelector sel = rs.Rule.Selectors!.Items[rs.Sel];
            ResetBacktracking(ref c);
            if (!sel.Invalid && MatchFrom(ref c, sel, sel.Compounds.Length - 1, el))
            {
                matched.Add(rs);
            }
        }
    }

    // Selector ids are only unique within the list they were parsed in, and candidates come from many lists (one per rule),
    // so a failure memo must never outlive the candidate that recorded it. The step budget is per candidate as well.
    private static void ResetBacktracking(ref MatchCtx c)
    {
        c.Steps = 0;
        if (c.UseMemo)
        {
            c.UseMemo = false;
            c.Failed?.Clear();
        }
    }

    public static Node QueryFirst(Node context, SelectorList list, SelectorMatchOptions? options)
    {
        Document doc = context.Document!;
        MatchCtx c = Create(context, options, false);
        Arena a = c.A;
        int idCandidate = IdFastPath(ref c, context, list);
        if (idCandidate != -2)
        {
            for (int e = idCandidate; e >= 0; e = doc.Index.NextWithSameId(e))
            {
                if (e != context.Index && context.Contains(new Node(doc, e)) && MatchList(ref c, list, e))
                {
                    return new Node(doc, e);
                }
            }

            return default;
        }

        for (int i = Node.NextPreorder(a, context.Index, context.Index); i >= 0; i = Node.NextPreorder(a, i, context.Index))
        {
            if (a.Kind[i] == (byte)NodeKind.Element && MatchList(ref c, list, i))
            {
                return new Node(doc, i);
            }
        }

        return default;
    }

    public static void QueryAll(Node context, SelectorList list, List<Node> results, SelectorMatchOptions? options)
    {
        Document doc = context.Document!;
        MatchCtx c = Create(context, options, true);
        Arena a = c.A;
        int idCandidate = IdFastPath(ref c, context, list);
        if (idCandidate != -2)
        {
            for (int e = idCandidate; e >= 0; e = doc.Index.NextWithSameId(e))
            {
                if (e != context.Index && context.Contains(new Node(doc, e)) && MatchList(ref c, list, e))
                {
                    results.Add(new Node(doc, e));
                }
            }

            return;
        }

        for (int i = Node.NextPreorder(a, context.Index, context.Index); i >= 0; i = Node.NextPreorder(a, i, context.Index))
        {
            if (a.Kind[i] == (byte)NodeKind.Element && MatchList(ref c, list, i))
            {
                results.Add(new Node(doc, i));
            }
        }
    }

    /// <summary>
    /// For a one-selector list whose rightmost compound has an id, the first element with that id (or -1 for none), when the
    /// id table can answer; -2 when the query must scan.
    /// </summary>
    private static int IdFastPath(ref MatchCtx c, Node context, SelectorList list)
    {
        if (list.Count != 1 || c.Quirks || list.Items[0].Key.Kind != SelectorKeyKind.Id || c.TreeRoot != 0)
        {
            return -2;
        }

        return c.Doc.Index.FirstWithId(list.Items[0].Key.Name);
    }

    private static MatchCtx Create(Node context, SelectorMatchOptions? options, bool bulk)
    {
        Document doc = context.Document!;
        Arena a = doc.Arena;
        Node scopeNode = options is not null && !options.Scope.IsNull ? options.Scope : context;
        int scope = -1;
        if (ReferenceEquals(scopeNode.Document, doc))
        {
            switch ((NodeKind)a.Kind[scopeNode.Index])
            {
                case NodeKind.Element:
                    scope = scopeNode.Index;
                    break;
                case NodeKind.Document:
                    scope = doc.DocumentElementIndex;
                    break;
            }
        }

        return new MatchCtx
        {
            Doc = doc,
            A = a,
            Scope = scope,
            State = options?.State,
            Quirks = doc.Mode == QuirksMode.Quirks,
            Bulk = bulk,
            TreeRoot = Node.RootIndex(a, context.Index),
        };
    }

    // ---- lists and complex selectors ----

    public static bool MatchList(ref MatchCtx c, SelectorList list, int el)
    {
        foreach (ComplexSelector s in list.Items)
        {
            if (MatchComplex(ref c, s, el))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchComplex(ref MatchCtx c, ComplexSelector s, int el)
    {
        if (s.Invalid || s.HasPseudoElement || (s.Deps & SelectorDependencies.NeverMatches) != 0 && HasColumn(s))
        {
            return false;
        }

        c.Steps = 0;
        return MatchFrom(ref c, s, s.Compounds.Length - 1, el);
    }

    private static bool HasColumn(ComplexSelector s)
    {
        foreach (Combinator k in s.Combs)
        {
            if (k == Combinator.Column)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchFrom(ref MatchCtx c, ComplexSelector s, int i, int el)
    {
        if (!MatchCompound(ref c, s.Compounds[i], el))
        {
            return false;
        }

        if (i == 0)
        {
            return true;
        }

        Arena a = c.A;
        switch (s.Combs[i - 1])
        {
            case Combinator.Child:
            {
                int p = a.Parent[el];
                return p >= 0 && a.Kind[p] == (byte)NodeKind.Element && Try(ref c, s, i - 1, p);
            }

            case Combinator.Descendant:
            {
                for (int p = a.Parent[el]; p >= 0 && a.Kind[p] == (byte)NodeKind.Element; p = a.Parent[p])
                {
                    if (Try(ref c, s, i - 1, p))
                    {
                        return true;
                    }
                }

                return false;
            }

            case Combinator.NextSibling:
            {
                int q = PrevElement(a, el);
                return q >= 0 && Try(ref c, s, i - 1, q);
            }

            case Combinator.SubsequentSibling:
            {
                for (int q = PrevElement(a, el); q >= 0; q = PrevElement(a, q))
                {
                    if (Try(ref c, s, i - 1, q))
                    {
                        return true;
                    }
                }

                return false;
            }

            default:
                return false;
        }
    }

    private static bool Try(ref MatchCtx c, ComplexSelector s, int i, int el)
    {
        (int, int, int) key = (s.Id, i, el);
        if (c.UseMemo && c.Failed is not null && c.Failed.Contains(key))
        {
            return false;
        }

        if (++c.Steps > MemoThreshold)
        {
            c.UseMemo = true;
            c.Failed ??= [];
        }

        bool r = MatchFrom(ref c, s, i, el);
        if (!r && c.UseMemo)
        {
            c.Failed!.Add(key);
        }

        return r;
    }

    internal static int PrevElement(Arena a, int el)
    {
        for (int q = a.Prev[el]; q >= 0; q = a.Prev[q])
        {
            if (a.Kind[q] == (byte)NodeKind.Element)
            {
                return q;
            }
        }

        return -1;
    }

    internal static int NextElement(Arena a, int el)
    {
        for (int q = a.Next[el]; q >= 0; q = a.Next[q])
        {
            if (a.Kind[q] == (byte)NodeKind.Element)
            {
                return q;
            }
        }

        return -1;
    }

    // ---- compounds and simple selectors ----

    private static bool MatchCompound(ref MatchCtx c, Compound k, int el)
    {
        if (k.HasPseudoElement && !c.IgnoreTrailingPseudo)
        {
            return false;
        }

        foreach (Simple s in k.Parts)
        {
            if (s.Kind == SimpleKind.PseudoElement)
            {
                continue;
            }

            if (!MatchSimple(ref c, s, el))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchSimple(ref MatchCtx c, Simple s, int el)
    {
        Arena a = c.A;
        switch (s.Kind)
        {
            case SimpleKind.Type:
                return MatchType(ref c, s, el);

            case SimpleKind.Id:
            {
                var n = new Node(c.Doc, el);
                int at = n.FindAttr(N.Id);
                if (at < 0)
                {
                    return false;
                }

                ReadOnlySpan<char> v = n.AttrValue(at);
                return c.Quirks ? ClassList.IsAsciiEqual(v, s.Name) : v.SequenceEqual(s.Name);
            }

            case SimpleKind.Class:
            {
                var n = new Node(c.Doc, el);
                int at = n.FindAttr(N.Class);
                if (at < 0)
                {
                    return false;
                }

                return new ClassList(n.AttrValue(at)).Contains(s.Name, c.Quirks);
            }

            case SimpleKind.Attribute:
                return MatchAttribute(ref c, s, el);

            case SimpleKind.PseudoClass:
                return MatchPseudo(ref c, s, el);

            case SimpleKind.Logical:
                switch (s.Logical)
                {
                    case LogicalKind.Not:
                        return !MatchList(ref c, s.Sub!, el);
                    case LogicalKind.Has:
                        return MatchHas(ref c, s, el);
                    default:
                        return MatchList(ref c, s.Sub!, el);
                }

            case SimpleKind.Nth:
                return MatchNth(ref c, s, el);

            case SimpleKind.Lang:
            {
                if (!ElementState.TryGetLanguage(new Node(c.Doc, el), out string lang))
                {
                    return false;
                }

                foreach (string range in s.Strings!)
                {
                    if (ElementState.LangMatches(range, lang))
                    {
                        return true;
                    }
                }

                return false;
            }

            case SimpleKind.Dir:
                return ElementState.IsRtl(new Node(c.Doc, el)) == (s.Value == "rtl");

            case SimpleKind.Heading:
            {
                int level = ElementState.HeadingLevel(new Node(c.Doc, el));
                if (level == 0)
                {
                    return false;
                }

                for (int i = 0; i < s.Pairs!.Length; i += 2)
                {
                    if (AnB(s.Pairs[i], s.Pairs[i + 1], level))
                    {
                        return true;
                    }
                }

                return false;
            }

            default:
                _ = a;
                return false;
        }
    }

    private static bool MatchType(ref MatchCtx c, Simple s, int el)
    {
        Arena a = c.A;
        switch (s.Ns)
        {
            case NsKind.None:
                return false;
            case NsKind.Specific:
                if (new Node(c.Doc, el).NamespaceURI != s.NsUri)
                {
                    return false;
                }

                break;
        }

        if (s.IsUniversal)
        {
            return true;
        }

        string local = c.Doc.Names.NameOf(a.Name[el]);
        return a.Ns[el] == (byte)ElementNamespace.Html
            ? string.Equals(local, s.LowerName, StringComparison.Ordinal)
            : string.Equals(local, s.Name, StringComparison.Ordinal);
    }

    // HTML 4.16 "Case-sensitivity of selectors": values of these attributes compare ASCII case-insensitively on HTML elements.
    private static readonly HashSet<string> s_caseInsensitiveValues = new(StringComparer.Ordinal)
    {
        "accept", "accept-charset", "align", "alink", "axis", "bgcolor", "charset", "checked", "clear", "codetype", "color", "compact",
        "declare", "defer", "dir", "direction", "disabled", "enctype", "face", "frame", "hreflang", "http-equiv", "lang", "language",
        "link", "media", "method", "multiple", "nohref", "noresize", "noshade", "nowrap", "readonly", "rel", "rev", "rules", "scope",
        "scrolling", "selected", "shape", "target", "text", "type", "valign", "valuetype", "vlink",
    };

    private static bool MatchAttribute(ref MatchCtx c, Simple s, int el)
    {
        Arena a = c.A;
        bool htmlEl = a.Ns[el] == (byte)ElementNamespace.Html;
        string wanted = htmlEl ? s.LowerName : s.Name;
        int start = a.AttrStart[el];
        int end = start + a.AttrCount[el];
        NameTable names = c.Doc.Names;
        for (int i = start; i < end; i++)
        {
            var ans = (AttributeNamespace)((a.AFlags[i] & AF.NsMask) >> AF.NsShift);
            switch (s.Ns)
            {
                case NsKind.Unspecified:
                    if (ans != AttributeNamespace.None)
                    {
                        continue;
                    }

                    break;
                case NsKind.None:
                    if (ans != AttributeNamespace.None)
                    {
                        continue;
                    }

                    break;
                case NsKind.Specific:
                    if (AttributeNamespaceUri(ans) != s.NsUri)
                    {
                        continue;
                    }

                    break;
            }

            if (!string.Equals(names.NameOf(a.AName[i]), wanted, StringComparison.Ordinal))
            {
                continue;
            }

            if (s.Op == AttrOp.Exists)
            {
                return true;
            }

            ReadOnlySpan<char> value = (a.AFlags[i] & AF.ValueSide) != 0
                ? a.Side.AsSpan(a.AValOff[i], a.AValLen[i])
                : c.Doc.Source.Text.AsSpan(a.AValOff[i], a.AValLen[i]);
            bool ci = s.Flag == 1 || (s.Flag == 0 && htmlEl && ans == AttributeNamespace.None && s_caseInsensitiveValues.Contains(s.LowerName));
            if (CompareValue(s.Op, value, s.Value, ci))
            {
                return true;
            }
        }

        return false;
    }

    private static string? AttributeNamespaceUri(AttributeNamespace ns) => ns switch
    {
        AttributeNamespace.XLink => "http://www.w3.org/1999/xlink",
        AttributeNamespace.Xml => "http://www.w3.org/XML/1998/namespace",
        AttributeNamespace.Xmlns => "http://www.w3.org/2000/xmlns/",
        _ => null,
    };

    private static bool CompareValue(AttrOp op, ReadOnlySpan<char> value, string wanted, bool ci)
    {
        StringComparison cmp = StringComparison.Ordinal;
        switch (op)
        {
            case AttrOp.Equals:
                return ci ? ClassList.IsAsciiEqual(value, wanted) : value.SequenceEqual(wanted);

            case AttrOp.Includes:
            {
                if (wanted.Length == 0 || ContainsWhitespace(wanted))
                {
                    return false;
                }

                foreach (ReadOnlySpan<char> t in new ClassList(value))
                {
                    if (ci ? ClassList.IsAsciiEqual(t, wanted) : t.SequenceEqual(wanted))
                    {
                        return true;
                    }
                }

                return false;
            }

            case AttrOp.DashMatch:
                if (ci ? ClassList.IsAsciiEqual(value, wanted) : value.SequenceEqual(wanted))
                {
                    return true;
                }

                return value.Length > wanted.Length && value[wanted.Length] == '-'
                       && (ci ? ClassList.IsAsciiEqual(value[..wanted.Length], wanted) : value[..wanted.Length].SequenceEqual(wanted));

            case AttrOp.Prefix:
                return wanted.Length > 0 && value.Length >= wanted.Length
                       && (ci ? ClassList.IsAsciiEqual(value[..wanted.Length], wanted) : value[..wanted.Length].SequenceEqual(wanted));

            case AttrOp.Suffix:
                return wanted.Length > 0 && value.Length >= wanted.Length
                       && (ci ? ClassList.IsAsciiEqual(value[^wanted.Length..], wanted) : value[^wanted.Length..].SequenceEqual(wanted));

            case AttrOp.Substring:
                if (wanted.Length == 0)
                {
                    return false;
                }

                if (!ci)
                {
                    return value.IndexOf(wanted.AsSpan(), cmp) >= 0;
                }

                for (int i = 0; i + wanted.Length <= value.Length; i++)
                {
                    if (ClassList.IsAsciiEqual(value.Slice(i, wanted.Length), wanted))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    private static bool ContainsWhitespace(string s)
    {
        foreach (char ch in s)
        {
            if (ch is ' ' or '\t' or '\n' or '\f' or '\r')
            {
                return true;
            }
        }

        return false;
    }

    // ---- pseudo-classes ----

    private static bool MatchPseudo(ref MatchCtx c, Simple s, int el)
    {
        Arena a = c.A;
        var n = new Node(c.Doc, el);
        switch (s.Pseudo)
        {
            case PseudoKind.Root:
            {
                int p = a.Parent[el];
                return p >= 0 && a.Kind[p] == (byte)NodeKind.Document;
            }

            case PseudoKind.Empty:
                for (int ch = a.First[el]; ch >= 0; ch = a.Next[ch])
                {
                    byte k = a.Kind[ch];
                    if (k == (byte)NodeKind.Element || (k == (byte)NodeKind.Text && new Node(c.Doc, ch).Data.Length > 0))
                    {
                        return false;
                    }
                }

                return true;

            case PseudoKind.FirstChild:
                return c.Doc.Index.Siblings.Position[el] <= 1;
            case PseudoKind.LastChild:
            {
                DocumentIndex.SiblingTable t = c.Doc.Index.Siblings;
                return t.Position[el] >= t.Total[el];
            }

            case PseudoKind.OnlyChild:
                return c.Doc.Index.Siblings.Total[el] <= 1;
            case PseudoKind.FirstOfType:
                return c.Doc.Index.Types.Position[el] <= 1;
            case PseudoKind.LastOfType:
            {
                DocumentIndex.SiblingTable t = c.Doc.Index.Types;
                return t.Position[el] >= t.Total[el];
            }

            case PseudoKind.OnlyOfType:
                return c.Doc.Index.Types.Total[el] <= 1;

            case PseudoKind.Scope:
                return c.Scope >= 0 && c.Scope == el;
            case PseudoKind.Defined:
                return ElementState.IsDefined(n);
            case PseudoKind.Link:
            case PseudoKind.AnyLink:
                return ElementState.IsLink(n);

            case PseudoKind.Hover:
            {
                InteractionState? st = c.State;
                return st is not null && (InChain(ref c, st.Hover, el, false) || InChain(ref c, st.HoverControl, el, false));
            }

            case PseudoKind.Active:
            {
                InteractionState? st = c.State;
                return st is not null && (InChain(ref c, st.Active, el, false) || InChain(ref c, st.ActiveControl, el, false));
            }

            case PseudoKind.Focus:
                return c.State is not null && InChain(ref c, c.State.Focus, el, true);
            case PseudoKind.FocusVisible:
                return c.State is { FocusVisible: true } && InChain(ref c, c.State.Focus, el, true);
            case PseudoKind.FocusWithin:
                return c.State is not null && InChain(ref c, c.State.Focus, el, false);
            case PseudoKind.Target:
                return c.State is not null && InChain(ref c, c.State.Target, el, true);
            case PseudoKind.TargetWithin:
                return c.State is not null && InChain(ref c, c.State.Target, el, false);

            case PseudoKind.Checked:
                return ElementState.IsChecked(n);
            case PseudoKind.Disabled:
                return ElementState.CanBeDisabled(n) && ElementState.IsDisabled(n);
            case PseudoKind.Enabled:
                return ElementState.CanBeDisabled(n) && !ElementState.IsDisabled(n);
            case PseudoKind.Required:
                return ElementState.RequiredOrOptionalApplies(n) && ElementState.Has(n, "required");
            case PseudoKind.Optional:
                return ElementState.RequiredOrOptionalApplies(n) && !ElementState.Has(n, "required");
            case PseudoKind.ReadOnly:
                return a.Ns[el] == (byte)ElementNamespace.Html && !ElementState.IsReadWrite(n);
            case PseudoKind.ReadWrite:
                return ElementState.IsReadWrite(n);
            case PseudoKind.PlaceholderShown:
                return ElementState.IsPlaceholderShown(n);
            case PseudoKind.Default:
                return ElementState.IsDefault(n);
            case PseudoKind.Indeterminate:
                return ElementState.IsIndeterminate(n);
            case PseudoKind.Valid:
                return ElementState.IsValidMatch(n, true);
            case PseudoKind.Invalid:
                return ElementState.IsValidMatch(n, false);
            case PseudoKind.InRange:
                return ElementState.IsInOutOfRange(n, false);
            case PseudoKind.OutOfRange:
                return ElementState.IsInOutOfRange(n, true);
            case PseudoKind.Open:
                return ElementState.IsOpen(n);
            case PseudoKind.HeadingAny:
                return ElementState.HeadingLevel(n) > 0;
            default:
                return false;
        }
    }

    /// <summary>True when <paramref name="target"/> is this element (<paramref name="selfOnly"/>) or this element or one of its descendants.</summary>
    private static bool InChain(ref MatchCtx c, Node target, int el, bool selfOnly)
    {
        if (target.IsNull || !ReferenceEquals(target.Document, c.Doc))
        {
            return false;
        }

        if (selfOnly)
        {
            return target.Index == el;
        }

        Arena a = c.A;
        for (int i = target.Index; i >= 0; i = a.Parent[i])
        {
            if (i == el)
            {
                return true;
            }
        }

        return false;
    }

    // ---- :nth-* ----

    internal static bool AnB(int a, int b, int pos)
    {
        long d = (long)pos - b;
        if (a == 0)
        {
            return d == 0;
        }

        return d % a == 0 && d / a >= 0;
    }

    private static bool MatchNth(ref MatchCtx c, Simple s, int el)
    {
        Arena a = c.A;
        bool fromEnd = s.Nth is NthKind.LastChild or NthKind.LastOfType;
        if (s.Sub is null)
        {
            DocumentIndex.SiblingTable t = s.Nth is NthKind.Child or NthKind.LastChild ? c.Doc.Index.Siblings : c.Doc.Index.Types;
            int pos = t.Position[el];
            if (pos == 0)
            {
                return false;
            }

            return AnB(s.A, s.B, fromEnd ? t.Total[el] - pos + 1 : pos);
        }

        // "of S": the element must match S and is counted among the siblings that do.
        if (!MatchList(ref c, s.Sub, el))
        {
            return false;
        }

        int parent = a.Parent[el];
        if (parent < 0)
        {
            return AnB(s.A, s.B, 1);
        }

        if (c.Bulk)
        {
            c.NthCaches ??= [];
            if (!c.NthCaches.TryGetValue(s, out NthOfCache? cache))
            {
                cache = new NthOfCache(a.Count);
                c.NthCaches[s] = cache;
            }

            if (!cache.Done[parent])
            {
                cache.Done[parent] = true;
                int k = 0;
                for (int x = a.First[parent]; x >= 0; x = a.Next[x])
                {
                    if (a.Kind[x] == (byte)NodeKind.Element && MatchList(ref c, s.Sub, x))
                    {
                        cache.FromStart[x] = ++k;
                    }
                }

                for (int x = a.First[parent]; x >= 0; x = a.Next[x])
                {
                    if (cache.FromStart[x] > 0)
                    {
                        cache.FromEnd[x] = k - cache.FromStart[x] + 1;
                    }
                }
            }

            return AnB(s.A, s.B, fromEnd ? cache.FromEnd[el] : cache.FromStart[el]);
        }

        int index = 0;
        if (fromEnd)
        {
            for (int x = el; x >= 0; x = a.Next[x])
            {
                if (a.Kind[x] == (byte)NodeKind.Element && MatchList(ref c, s.Sub, x))
                {
                    index++;
                }
            }
        }
        else
        {
            for (int x = el; x >= 0; x = a.Prev[x])
            {
                if (a.Kind[x] == (byte)NodeKind.Element && MatchList(ref c, s.Sub, x))
                {
                    index++;
                }
            }
        }

        return AnB(s.A, s.B, index);
    }

    // ---- :has() ----

    private static bool MatchHas(ref MatchCtx c, Simple s, int el)
    {
        foreach (ComplexSelector rel in s.Sub!.Items)
        {
            if (!rel.Invalid && !rel.HasPseudoElement && HasRelative(ref c, rel, el))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRelative(ref MatchCtx c, ComplexSelector rel, int anchor)
    {
        if (HasColumn(rel) || rel.Leading == Combinator.Column)
        {
            return false;
        }

        Arena a = c.A;
        if (rel.Compounds.Length == 1)
        {
            Compound k = rel.Compounds[0];
            if (c.Bulk)
            {
                // One pass over the tree marks every element that has a match; each :has() test is then a bit lookup,
                // so a deep tree costs linear time rather than a subtree walk per element.
                Dictionary<ComplexSelector, ulong[]> all = c.HasMarks ??= [];
                if (!all.TryGetValue(rel, out ulong[]? marks))
                {
                    marks = BuildHasMarks(ref c, rel, k);
                    all[rel] = marks;
                }

                return (marks[anchor >> 6] & (1UL << (anchor & 63))) != 0;
            }

            switch (rel.Leading)
            {
                case Combinator.Child:
                    for (int ch = a.First[anchor]; ch >= 0; ch = a.Next[ch])
                    {
                        if (a.Kind[ch] == (byte)NodeKind.Element && MatchCompound(ref c, k, ch))
                        {
                            return true;
                        }
                    }

                    return false;
                case Combinator.NextSibling:
                {
                    int q = NextElement(a, anchor);
                    return q >= 0 && MatchCompound(ref c, k, q);
                }

                case Combinator.SubsequentSibling:
                    for (int q = NextElement(a, anchor); q >= 0; q = NextElement(a, q))
                    {
                        if (MatchCompound(ref c, k, q))
                        {
                            return true;
                        }
                    }

                    return false;
                default:
                    for (int d = Node.NextPreorder(a, anchor, anchor); d >= 0; d = Node.NextPreorder(a, d, anchor))
                    {
                        if (a.Kind[d] == (byte)NodeKind.Element && MatchCompound(ref c, k, d))
                        {
                            return true;
                        }
                    }

                    return false;
            }
        }

        // General case: walk forward from the anchor one compound at a time, keeping the set of elements reached.
        var frontier = new List<int> { anchor };
        for (int step = 0; step < rel.Compounds.Length; step++)
        {
            Combinator comb = step == 0 ? rel.Leading : rel.Combs[step - 1];
            Compound k = rel.Compounds[step];
            var next = new List<int>();
            var seen = new HashSet<int>();
            foreach (int f in frontier)
            {
                switch (comb)
                {
                    case Combinator.Child:
                        for (int ch = a.First[f]; ch >= 0; ch = a.Next[ch])
                        {
                            if (a.Kind[ch] == (byte)NodeKind.Element && seen.Add(ch) && MatchCompound(ref c, k, ch))
                            {
                                next.Add(ch);
                            }
                        }

                        break;
                    case Combinator.NextSibling:
                    {
                        int q = NextElement(a, f);
                        if (q >= 0 && seen.Add(q) && MatchCompound(ref c, k, q))
                        {
                            next.Add(q);
                        }

                        break;
                    }

                    case Combinator.SubsequentSibling:
                        for (int q = NextElement(a, f); q >= 0; q = NextElement(a, q))
                        {
                            if (!seen.Add(q))
                            {
                                break; // Everything after q was reached from an earlier frontier element.
                            }

                            if (MatchCompound(ref c, k, q))
                            {
                                next.Add(q);
                            }
                        }

                        break;
                    default:
                    {
                        int d = Node.NextPreorder(a, f, f);
                        while (d >= 0)
                        {
                            if (a.Kind[d] == (byte)NodeKind.Element)
                            {
                                if (!seen.Add(d))
                                {
                                    d = NextSkippingChildren(a, d, f);
                                    continue;
                                }

                                if (MatchCompound(ref c, k, d))
                                {
                                    next.Add(d);
                                }
                            }

                            d = Node.NextPreorder(a, d, f);
                        }

                        break;
                    }
                }
            }

            if (next.Count == 0)
            {
                return false;
            }

            frontier = next;
        }

        return true;
    }

    private static int NextSkippingChildren(Arena a, int i, int scope)
    {
        while (i >= 0 && i != scope)
        {
            if (a.Next[i] >= 0)
            {
                return a.Next[i];
            }

            i = a.Parent[i];
        }

        return -1;
    }

    private static ulong[] BuildHasMarks(ref MatchCtx c, ComplexSelector rel, Compound k)
    {
        Arena a = c.A;
        var marks = new ulong[(a.Count >> 6) + 1];
        for (int x = Node.NextPreorder(a, c.TreeRoot, c.TreeRoot); x >= 0; x = Node.NextPreorder(a, x, c.TreeRoot))
        {
            if (a.Kind[x] != (byte)NodeKind.Element || !MatchCompound(ref c, k, x))
            {
                continue;
            }

            switch (rel.Leading)
            {
                case Combinator.Child:
                {
                    int p = a.Parent[x];
                    if (p >= 0)
                    {
                        marks[p >> 6] |= 1UL << (p & 63);
                    }

                    break;
                }

                case Combinator.NextSibling:
                {
                    int q = PrevElement(a, x);
                    if (q >= 0)
                    {
                        marks[q >> 6] |= 1UL << (q & 63);
                    }

                    break;
                }

                case Combinator.SubsequentSibling:
                    for (int q = PrevElement(a, x); q >= 0 && (marks[q >> 6] & (1UL << (q & 63))) == 0; q = PrevElement(a, q))
                    {
                        marks[q >> 6] |= 1UL << (q & 63);
                    }

                    break;
                default:
                    for (int p = a.Parent[x]; p >= 0 && (marks[p >> 6] & (1UL << (p & 63))) == 0; p = a.Parent[p])
                    {
                        marks[p >> 6] |= 1UL << (p & 63);
                    }

                    break;
            }
        }

        return marks;
    }
}
