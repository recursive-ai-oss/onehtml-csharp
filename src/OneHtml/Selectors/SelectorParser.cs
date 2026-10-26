using System;
using System.Collections.Generic;
using System.Globalization;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Selectors;

/// <summary>
/// Selectors Level 4 parser over CSS component values. Nesting of functional pseudo-classes is bounded by
/// <see cref="SelectorLimits.MaxNesting"/> (checked before descending), so the call depth never follows the input.
/// </summary>
internal static class SelectorParser
{
    internal enum Mode
    {
        List,
        Single,
    }

    private enum ListKind
    {
        /// <summary>Unforgiving complex selectors (top level, :not(), the "of S" of :nth-child()).</summary>
        Normal,

        /// <summary>Forgiving (:is(), :where()): invalid items are dropped (Selectors 4 §3.8).</summary>
        Forgiving,

        /// <summary>Relative selectors (:has()).</summary>
        Relative,
    }

    private sealed class State
    {
        public SelectorLimits Limits = SelectorLimits.Default;
        public IReadOnlyDictionary<string, string>? Namespaces;
        public int Complex;
        public int SimpleCount;
        public int NextId;
        public CssTree? Tree;

        /// <summary>Items a forgiving list kept only as text because they did not parse.</summary>
        public int Dropped;

        /// <summary>
        /// Set on the state of a validity probe (see <c>IsStableRaw</c>): only whether the item parses matters, so forgiving
        /// lists inside it skip building their own raw text, which keeps the probe linear in the item.
        /// </summary>
        public bool Probe;
    }

    public static SelectorList ParseText(string text, SelectorParseOptions? options, Mode mode)
    {
        SelectorLimits limits = options?.Limits ?? SelectorLimits.Default;
        if (text.Length > limits.MaxLength)
        {
            throw new SelectorLimitExceededException(nameof(SelectorLimits.MaxLength));
        }

        CssTree tree;
        try
        {
            tree = CssTree.ParseComponentValueList(text);
        }
        catch (CssLimitExceededException)
        {
            throw new SelectorLimitExceededException(nameof(SelectorLimits.MaxNesting));
        }

        return ParseValues(tree.Values, options, mode);
    }

    public static bool TryParseText(string text, SelectorParseOptions? options, Mode mode, out SelectorList? list)
    {
        try
        {
            list = ParseText(text, options, mode);
            return true;
        }
        catch (SelectorSyntaxException)
        {
            list = null;
            return false;
        }
    }

    public static bool TryParseValues(ComponentValueList values, SelectorParseOptions? options, Mode mode, out SelectorList? list)
    {
        try
        {
            list = ParseValues(values, options, mode);
            return true;
        }
        catch (SelectorSyntaxException)
        {
            list = null;
            return false;
        }
    }

    public static SelectorList ParseValues(ComponentValueList values, SelectorParseOptions? options, Mode mode)
    {
        var st = new State { Limits = options?.Limits ?? SelectorLimits.Default, Namespaces = options?.Namespaces };
        values.TryGetBounds(out st.Tree, out _, out _);
        ComponentValue[] arr = ToArray(values);
        if (arr.Length > st.Limits.MaxLength)
        {
            throw new SelectorLimitExceededException(nameof(SelectorLimits.MaxLength));
        }

        if (mode == Mode.Single)
        {
            if (HasTopLevelComma(arr, 0, arr.Length))
            {
                throw new SelectorSyntaxException("Not a valid complex selector.");
            }

            ComplexSelector? c = ParseComplex(st, arr, 0, arr.Length, false, 0, false, true);
            if (c is null)
            {
                throw new SelectorSyntaxException("Not a valid complex selector.");
            }

            if (st.Dropped > 0)
            {
                throw new SelectorSyntaxException("Contains an invalid forgiven item.");
            }

            return new SelectorList([c], false);
        }

        SelectorList? l = ParseList(st, arr, 0, arr.Length, ListKind.Normal, 0, false, true);
        if (l is null || l.Count == 0)
        {
            throw new SelectorSyntaxException("Not a valid selector list.");
        }

        return l;
    }

    // ---- lists and complex selectors ----

    private static SelectorList? ParseList(State st, ComponentValue[] v, int s, int e, ListKind kind, int depth, bool insideHas, bool allowPseudoElement)
    {
        if (depth > st.Limits.MaxNesting)
        {
            throw new SelectorLimitExceededException(nameof(SelectorLimits.MaxNesting));
        }

        var items = new List<ComplexSelector>();
        int segStart = s;
        for (int i = s; i <= e; i++)
        {
            if (i < e && !IsComma(v[i]))
            {
                continue;
            }

            int a = segStart;
            int b = i;
            segStart = i + 1;
            Trim(v, ref a, ref b);
            if (a >= b)
            {
                if (kind == ListKind.Forgiving)
                {
                    continue;
                }

                return null;
            }

            ComplexSelector? c = ParseComplex(st, v, a, b, kind == ListKind.Relative, depth, insideHas || kind == ListKind.Relative, allowPseudoElement);
            if (c is null)
            {
                if (kind == ListKind.Forgiving)
                {
                    // Selectors 4 §3.8: the item is dropped from matching, but kept as written so the list serializes unchanged.
                    st.Dropped++;
                    // Serialized (not copied) so that an unclosed bracket in the item is closed and the text parses back the same.
                    string raw = st.Probe || st.Tree is null ? string.Empty : CssSerializer.Serialize(new ComponentValueList(st.Tree, v[a].Token.Index, v[b - 1].Next));
                    if (!st.Probe && IsStableRaw(st, raw, depth, insideHas, allowPseudoElement))
                    {
                        items.Add(new ComplexSelector { Invalid = true, Raw = raw, Id = st.NextId++ });
                    }

                    continue;
                }

                return null;
            }

            items.Add(c);
        }

        return new SelectorList([.. items], kind == ListKind.Relative);
    }

    /// <summary>
    /// True when the text, written between the parentheses of a functional pseudo-class, reads back as exactly that one
    /// argument and serializes to the same text. Items for which it does not hold (a bad string, say) are dropped from the
    /// serialization rather than written in a form that would parse differently.
    /// </summary>
    private static bool IsStableRaw(State st, string raw, int depth, bool insideHas, bool allowPseudoElement)
    {
        if (raw.Length == 0)
        {
            return false;
        }

        try
        {
            CssTree t = CssTree.ParseComponentValueList(":is(" + raw + ")");
            int n = 0;
            ComponentValue only = default;
            foreach (ComponentValue cv in t.Values)
            {
                n++;
                only = cv;
            }

            if (n != 2 || only.Kind != ComponentValueKind.Function || !only.IsClosed)
            {
                return false;
            }

            // A bad-string token ends at a newline; once the newline is written as a space the text is a good string (or runs
            // into the next quote), so it never reads back as the same tokens.
            for (int i = 0; i < t.TokenCount; i++)
            {
                if (t.GetToken(i).Kind == CssTokenKind.BadString)
                {
                    return false;
                }
            }

            // The text must still be one list item: a comma at the top level would split it on the next parse.
            foreach (ComponentValue child in only.Children)
            {
                if (IsComma(child))
                {
                    return false;
                }
            }

            if (CssSerializer.Serialize(only.Children.Trimmed()) != raw)
            {
                return false;
            }

            // Writing the item can repair it (an unclosed bracket gets its close, an unterminated string its quote), and a
            // repaired item may be a valid selector, which would then match on the next parse. Keep it only if it is still invalid.
            var probe = new State { Limits = st.Limits, Namespaces = st.Namespaces, Tree = t, Probe = true };
            ComponentValue[] arr = ToArray(only.Children);
            try
            {
                return ParseComplex(probe, arr, 0, arr.Length, false, depth, insideHas, allowPseudoElement) is null;
            }
            catch (SelectorLimitExceededException)
            {
                return false;
            }
        }
        catch (CssLimitExceededException)
        {
            return false;
        }
    }

    private static bool HasTopLevelComma(ComponentValue[] v, int s, int e)
    {
        for (int i = s; i < e; i++)
        {
            if (IsComma(v[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static ComplexSelector? ParseComplex(State st, ComponentValue[] v, int s, int e, bool relative, int depth, bool insideHas, bool allowPe)
    {
        Trim(v, ref s, ref e);
        if (s >= e)
        {
            return null;
        }

        if (++st.Complex > st.Limits.MaxComplexSelectors)
        {
            throw new SelectorLimitExceededException(nameof(SelectorLimits.MaxComplexSelectors));
        }

        var compounds = new List<Compound>();
        var combs = new List<Combinator>();
        Combinator leading = Combinator.Descendant;
        int i = s;
        if (TryCombinator(v, i, e, out Combinator lead, out int used))
        {
            if (!relative)
            {
                return null;
            }

            leading = lead;
            i += used;
            while (i < e && v[i].IsWhitespace)
            {
                i++;
            }
        }

        while (true)
        {
            if (compounds.Count >= st.Limits.MaxCompounds)
            {
                throw new SelectorLimitExceededException(nameof(SelectorLimits.MaxCompounds));
            }

            Compound? k = ParseCompound(st, v, ref i, e, depth, insideHas, allowPe);
            if (k is null)
            {
                return null;
            }

            compounds.Add(k);
            int j = i;
            while (j < e && v[j].IsWhitespace)
            {
                j++;
            }

            if (j >= e)
            {
                break;
            }

            bool sawSpace = j > i;
            Combinator comb;
            if (TryCombinator(v, j, e, out comb, out int n))
            {
                j += n;
                while (j < e && v[j].IsWhitespace)
                {
                    j++;
                }

                if (j >= e)
                {
                    return null;
                }
            }
            else if (sawSpace)
            {
                comb = Combinator.Descendant;
            }
            else
            {
                return null;
            }

            combs.Add(comb);
            i = j;
        }

        // A pseudo-element must end the selector.
        for (int c = 0; c < compounds.Count - 1; c++)
        {
            if (compounds[c].HasPseudoElement)
            {
                return null;
            }
        }

        var cs = new ComplexSelector
        {
            Compounds = [.. compounds],
            Combs = [.. combs],
            Leading = leading,
            IsRelative = relative,
            HasPseudoElement = compounds[^1].HasPseudoElement,
            Id = st.NextId++,
        };
        Finish(cs);
        return cs;
    }

    private static bool TryCombinator(ComponentValue[] v, int i, int e, out Combinator comb, out int used)
    {
        comb = Combinator.Descendant;
        used = 0;
        if (i >= e)
        {
            return false;
        }

        if (IsDelim(v[i], '>'))
        {
            comb = Combinator.Child;
            used = 1;
        }
        else if (IsDelim(v[i], '+'))
        {
            comb = Combinator.NextSibling;
            used = 1;
        }
        else if (IsDelim(v[i], '~'))
        {
            comb = Combinator.SubsequentSibling;
            used = 1;
        }
        else if (IsDelim(v[i], '|') && i + 1 < e && IsDelim(v[i + 1], '|'))
        {
            comb = Combinator.Column;
            used = 2;
        }

        return used > 0;
    }

    private static void Finish(ComplexSelector cs)
    {
        Specificity spec = default;
        SelectorDependencies deps = SelectorDependencies.None;
        foreach (Compound k in cs.Compounds)
        {
            foreach (Simple p in k.Parts)
            {
                spec += p.Specificity;
                deps |= p.Deps;
            }
        }

        foreach (Combinator c in cs.Combs)
        {
            if (c is Combinator.NextSibling or Combinator.SubsequentSibling)
            {
                deps |= SelectorDependencies.SiblingCombinator;
            }
            else if (c == Combinator.Column)
            {
                deps |= SelectorDependencies.NeverMatches;
            }
        }

        if (cs.IsRelative && cs.Leading is Combinator.NextSibling or Combinator.SubsequentSibling)
        {
            deps |= SelectorDependencies.SiblingCombinator;
        }

        if (cs.HasPseudoElement)
        {
            deps |= SelectorDependencies.NeverMatches;
        }

        cs.Specificity = spec;
        cs.Deps = deps;

        // The bucket key of the rightmost compound: id, else class, else type.
        Compound last = cs.Compounds[^1];
        SelectorKey key = default;
        foreach (Simple p in last.Parts)
        {
            if (p.Kind == SimpleKind.Id)
            {
                key = new SelectorKey(SelectorKeyKind.Id, p.Name);
                break;
            }

            if (p.Kind == SimpleKind.Class && key.Kind != SelectorKeyKind.Class)
            {
                key = new SelectorKey(SelectorKeyKind.Class, p.Name);
            }
            else if (p.Kind == SimpleKind.Type && !p.IsUniversal && key.Kind == SelectorKeyKind.None)
            {
                key = new SelectorKey(SelectorKeyKind.Tag, p.LowerName);
            }
        }

        cs.Key = key;
    }

    // ---- compound selectors ----

    private static Compound? ParseCompound(State st, ComponentValue[] v, ref int i, int e, int depth, bool insideHas, bool allowPe)
    {
        var parts = new List<Simple>();
        string? lastPe = null;
        int pos = i;

        // Type selector with optional namespace.
        if (pos < e)
        {
            if (TryParseType(st, v, ref pos, e, out Simple? type, out bool typeBad))
            {
                parts.Add(type!);
            }
            else if (typeBad)
            {
                return null;
            }
        }

        while (pos < e)
        {
            ComponentValue c = v[pos];
            Simple? s;
            if (c.Kind == ComponentValueKind.Token)
            {
                CssToken t = c.Token;
                if (t.Kind == CssTokenKind.Hash)
                {
                    if (!t.IsHashId)
                    {
                        return null;
                    }

                    s = new Simple { Kind = SimpleKind.Id, Name = t.Value, Specificity = new Specificity(1, 0, 0) };
                    pos++;
                }
                else if (IsDelim(c, '.'))
                {
                    if (pos + 1 >= e || v[pos + 1].Kind != ComponentValueKind.Token || v[pos + 1].Token.Kind != CssTokenKind.Ident)
                    {
                        return null;
                    }

                    s = new Simple { Kind = SimpleKind.Class, Name = v[pos + 1].Token.Value, Specificity = new Specificity(0, 1, 0) };
                    pos += 2;
                }
                else if (t.Kind == CssTokenKind.Colon)
                {
                    s = ParseColon(st, v, ref pos, e, depth, insideHas, allowPe, lastPe);
                    if (s is null)
                    {
                        return null;
                    }

                    if (s.Kind == SimpleKind.PseudoElement)
                    {
                        lastPe = s.Name;
                    }
                }
                else
                {
                    break;
                }
            }
            else if (c.Kind == ComponentValueKind.SimpleBlock && c.Token.Kind == CssTokenKind.LeftBracket)
            {
                s = ParseAttribute(st, c);
                if (s is null)
                {
                    return null;
                }

                pos++;
            }
            else
            {
                break;
            }

            parts.Add(s);
        }

        if (parts.Count == 0)
        {
            return null;
        }

        st.SimpleCount += parts.Count;
        if (st.SimpleCount > st.Limits.MaxSimpleSelectors)
        {
            throw new SelectorLimitExceededException(nameof(SelectorLimits.MaxSimpleSelectors));
        }

        i = pos;
        return new Compound { Parts = [.. parts], HasPseudoElement = lastPe is not null };
    }

    private static bool TryParseType(State st, ComponentValue[] v, ref int pos, int e, out Simple? type, out bool bad)
    {
        type = null;
        bad = false;
        ComponentValue c = v[pos];
        if (c.Kind != ComponentValueKind.Token)
        {
            return false;
        }

        CssTokenKind k = c.Token.Kind;
        string? prefix = null;
        NsKind ns = NsKind.Unspecified;
        int p = pos;
        string? name;
        bool universal;

        if (k == CssTokenKind.Ident || IsDelim(c, '*'))
        {
            bool nsFollows = p + 1 < e && IsDelim(v[p + 1], '|') && !(p + 2 < e && IsDelim(v[p + 2], '='));
            if (nsFollows)
            {
                if (k == CssTokenKind.Ident)
                {
                    prefix = c.Token.Value;
                    ns = NsKind.Specific;
                }
                else
                {
                    ns = NsKind.Any;
                }

                p += 2;
                if (p >= e || !ReadLocalName(v[p], out name, out universal))
                {
                    bad = true;
                    return false;
                }

                p++;
            }
            else
            {
                ReadLocalName(c, out name, out universal);
                p++;
            }
        }
        else if (IsDelim(c, '|') && !(pos + 1 < e && IsDelim(v[pos + 1], '|')))
        {
            ns = NsKind.None;
            p++;
            if (p >= e || !ReadLocalName(v[p], out name, out universal))
            {
                bad = true;
                return false;
            }

            p++;
        }
        else
        {
            return false;
        }

        string? uri = null;
        if (ns == NsKind.Specific)
        {
            if (st.Namespaces is null || !st.Namespaces.TryGetValue(prefix!, out uri))
            {
                bad = true;
                return false;
            }
        }
        else if (ns == NsKind.Unspecified && st.Namespaces is not null && st.Namespaces.TryGetValue(string.Empty, out string? def) && def.Length > 0)
        {
            ns = NsKind.Specific;
            uri = def;
            prefix = null;
        }

        type = new Simple
        {
            Kind = SimpleKind.Type,
            Name = name!,
            LowerName = AsciiLower(name!),
            IsUniversal = universal,
            Ns = ns,
            NsUri = uri,
            NsPrefix = ns == NsKind.Specific ? prefix : null,
            Specificity = universal ? default : new Specificity(0, 0, 1),
        };
        pos = p;
        return true;
    }

    private static bool ReadLocalName(ComponentValue c, out string? name, out bool universal)
    {
        name = null;
        universal = false;
        if (c.Kind != ComponentValueKind.Token)
        {
            return false;
        }

        if (c.Token.Kind == CssTokenKind.Ident)
        {
            name = c.Token.Value;
            return true;
        }

        if (IsDelim(c, '*'))
        {
            name = "*";
            universal = true;
            return true;
        }

        return false;
    }

    // ---- attribute selectors ----

    private static Simple? ParseAttribute(State st, ComponentValue block)
    {
        // A block still open at the end of the input is closed there (CSS Syntax 5.4.8), so [a=b and ::slotted(foo are valid.
        ComponentValue[] v = ToArray(block.Children);
        int e = v.Length;
        int i = 0;
        while (i < e && v[i].IsWhitespace)
        {
            i++;
        }

        if (i >= e)
        {
            return null;
        }

        NsKind ns = NsKind.Unspecified;
        string? prefix = null;
        string name;
        ComponentValue c = v[i];
        if (IsDelim(c, '*'))
        {
            if (i + 2 < e && IsDelim(v[i + 1], '|') && IsIdent(v[i + 2]))
            {
                ns = NsKind.Any;
                name = v[i + 2].Token.Value;
                i += 3;
            }
            else
            {
                return null;
            }
        }
        else if (IsDelim(c, '|'))
        {
            if (i + 1 < e && IsIdent(v[i + 1]))
            {
                ns = NsKind.None;
                name = v[i + 1].Token.Value;
                i += 2;
            }
            else
            {
                return null;
            }
        }
        else if (IsIdent(c))
        {
            if (i + 2 < e && IsDelim(v[i + 1], '|') && !IsDelim(v[i + 2], '='))
            {
                if (!IsIdent(v[i + 2]))
                {
                    return null;
                }

                ns = NsKind.Specific;
                prefix = c.Token.Value;
                name = v[i + 2].Token.Value;
                i += 3;
            }
            else
            {
                name = c.Token.Value;
                i++;
            }
        }
        else
        {
            return null;
        }

        string? uri = null;
        if (ns == NsKind.Specific && (st.Namespaces is null || !st.Namespaces.TryGetValue(prefix!, out uri)))
        {
            return null;
        }

        var s = new Simple
        {
            Kind = SimpleKind.Attribute,
            Name = name,
            LowerName = AsciiLower(name),
            Ns = ns,
            NsPrefix = prefix,
            NsUri = uri,
            Op = AttrOp.Exists,
            Specificity = new Specificity(0, 1, 0),
        };

        while (i < e && v[i].IsWhitespace)
        {
            i++;
        }

        if (i >= e)
        {
            return s;
        }

        if (IsDelim(v[i], '='))
        {
            s.Op = AttrOp.Equals;
            i++;
        }
        else
        {
            AttrOp op;
            if (IsDelim(v[i], '~'))
            {
                op = AttrOp.Includes;
            }
            else if (IsDelim(v[i], '|'))
            {
                op = AttrOp.DashMatch;
            }
            else if (IsDelim(v[i], '^'))
            {
                op = AttrOp.Prefix;
            }
            else if (IsDelim(v[i], '$'))
            {
                op = AttrOp.Suffix;
            }
            else if (IsDelim(v[i], '*'))
            {
                op = AttrOp.Substring;
            }
            else
            {
                return null;
            }

            if (i + 1 >= e || !IsDelim(v[i + 1], '='))
            {
                return null;
            }

            s.Op = op;
            i += 2;
        }

        while (i < e && v[i].IsWhitespace)
        {
            i++;
        }

        if (i >= e || v[i].Kind != ComponentValueKind.Token)
        {
            return null;
        }

        CssToken vt = v[i].Token;
        if (vt.Kind == CssTokenKind.Ident || (vt.Kind == CssTokenKind.QuotedString && vt.IsTerminated))
        {
            s.Value = vt.Value;
        }
        else
        {
            return null;
        }

        i++;
        while (i < e && v[i].IsWhitespace)
        {
            i++;
        }

        if (i < e)
        {
            if (IsIdent(v[i]))
            {
                string f = v[i].Token.Value;
                if (f.Length == 1 && f[0] is 'i' or 'I')
                {
                    s.Flag = 1;
                }
                else if (f.Length == 1 && f[0] is 's' or 'S')
                {
                    s.Flag = 2;
                }
                else
                {
                    return null;
                }

                i++;
                while (i < e && v[i].IsWhitespace)
                {
                    i++;
                }
            }

            if (i < e)
            {
                return null;
            }
        }

        return s;
    }

    // ---- pseudo-classes and pseudo-elements ----

    private static readonly Dictionary<string, PseudoKind> s_simplePseudo = new(StringComparer.Ordinal)
    {
        ["root"] = PseudoKind.Root,
        ["empty"] = PseudoKind.Empty,
        ["first-child"] = PseudoKind.FirstChild,
        ["last-child"] = PseudoKind.LastChild,
        ["only-child"] = PseudoKind.OnlyChild,
        ["first-of-type"] = PseudoKind.FirstOfType,
        ["last-of-type"] = PseudoKind.LastOfType,
        ["only-of-type"] = PseudoKind.OnlyOfType,
        ["scope"] = PseudoKind.Scope,
        ["defined"] = PseudoKind.Defined,
        ["link"] = PseudoKind.Link,
        ["any-link"] = PseudoKind.AnyLink,
        ["hover"] = PseudoKind.Hover,
        ["active"] = PseudoKind.Active,
        ["focus"] = PseudoKind.Focus,
        ["focus-visible"] = PseudoKind.FocusVisible,
        ["focus-within"] = PseudoKind.FocusWithin,
        ["target"] = PseudoKind.Target,
        ["target-within"] = PseudoKind.TargetWithin,
        ["checked"] = PseudoKind.Checked,
        ["disabled"] = PseudoKind.Disabled,
        ["enabled"] = PseudoKind.Enabled,
        ["required"] = PseudoKind.Required,
        ["optional"] = PseudoKind.Optional,
        ["read-only"] = PseudoKind.ReadOnly,
        ["read-write"] = PseudoKind.ReadWrite,
        ["placeholder-shown"] = PseudoKind.PlaceholderShown,
        ["default"] = PseudoKind.Default,
        ["indeterminate"] = PseudoKind.Indeterminate,
        ["valid"] = PseudoKind.Valid,
        ["invalid"] = PseudoKind.Invalid,
        ["in-range"] = PseudoKind.InRange,
        ["out-of-range"] = PseudoKind.OutOfRange,
        ["open"] = PseudoKind.Open,
        ["heading"] = PseudoKind.HeadingAny,

        // Valid selectors that match nothing here: they need script, user interaction, a media element, a shadow tree,
        // a visited-link history, a document URL or a top layer.
        ["visited"] = PseudoKind.Never,
        ["local-link"] = PseudoKind.Never,
        ["autofill"] = PseudoKind.Never,
        ["popover-open"] = PseudoKind.Never,
        ["modal"] = PseudoKind.Never,
        ["fullscreen"] = PseudoKind.Never,
        ["picture-in-picture"] = PseudoKind.Never,
        ["user-valid"] = PseudoKind.Never,
        ["user-invalid"] = PseudoKind.Never,
        ["playing"] = PseudoKind.Never,
        ["paused"] = PseudoKind.Never,
        ["seeking"] = PseudoKind.Never,
        ["buffering"] = PseudoKind.Never,
        ["stalled"] = PseudoKind.Never,
        ["muted"] = PseudoKind.Never,
        ["volume-locked"] = PseudoKind.Never,
        ["current"] = PseudoKind.Never,
        ["past"] = PseudoKind.Never,
        ["future"] = PseudoKind.Never,
        ["target-current"] = PseudoKind.Never,
        ["host"] = PseudoKind.Never,
    };

    private static readonly HashSet<string> s_legacyPseudoElements = new(StringComparer.Ordinal) { "before", "after", "first-line", "first-letter" };

    private static readonly HashSet<string> s_pseudoElements = new(StringComparer.Ordinal)
    {
        "before", "after", "first-line", "first-letter", "marker", "placeholder", "selection", "backdrop", "file-selector-button",
        "cue", "cue-region", "slotted", "part", "highlight", "spelling-error", "grammar-error", "target-text", "details-content",
        "view-transition", "view-transition-group", "view-transition-image-pair", "view-transition-old", "view-transition-new",
        "picker-icon", "checkmark", "scroll-marker", "scroll-marker-group", "scroll-button", "picker", "column", "text-fragment",
        "search-text", "slider-thumb", "slider-track", "slider-fill",
    };

    private static readonly HashSet<string> s_afterPseudoElement = new(StringComparer.Ordinal)
    {
        "hover", "active", "focus", "focus-visible", "focus-within", "target", "target-within",
    };

    private static readonly HashSet<string> s_afterPart = new(StringComparer.Ordinal)
    {
        "hover", "active", "focus", "focus-visible", "focus-within", "target", "target-within", "disabled", "enabled", "checked",
        "indeterminate", "default", "required", "optional", "read-only", "read-write", "placeholder-shown", "valid", "invalid",
        "in-range", "out-of-range", "open", "lang", "dir", "state", "is", "where", "not", "any-link", "link", "defined",
    };

    /// <summary>After ::part() the arguments of :is(), :where() and :not() may only be state pseudo-classes; other items are dropped.</summary>
    private static Simple? RestrictToStatePseudos(State st, Simple s)
    {
        var items = new List<ComplexSelector>();
        foreach (ComplexSelector c in s.Sub!.Items)
        {
            bool ok = !c.Invalid && c.Compounds.Length == 1 && !c.Compounds[0].HasPseudoElement;
            if (ok)
            {
                foreach (Simple p in c.Compounds[0].Parts)
                {
                    ok &= (p.Kind == SimpleKind.PseudoClass && s_afterPart.Contains(p.Name)) || p.Kind is SimpleKind.Lang or SimpleKind.Dir || (p.Kind == SimpleKind.Logical && p.Logical != LogicalKind.Has);
                }
            }

            if (ok)
            {
                items.Add(c);
            }
            else if (s.Logical == LogicalKind.Not)
            {
                return null;
            }
            else
            {
                st.Dropped++;
                items.Add(new ComplexSelector { Invalid = true, Raw = SelectorSerializer.SerializeComplex(c), Id = c.Id });
            }
        }

        return new Simple
        {
            Kind = s.Kind,
            Name = s.Name,
            Logical = s.Logical,
            Sub = new SelectorList([.. items], false),
            Specificity = s.Specificity,
            Deps = s.Deps,
        };
    }

    private static Simple? ParseColon(State st, ComponentValue[] v, ref int pos, int e, int depth, bool insideHas, bool allowPe, string? lastPe)
    {
        int p = pos + 1;
        bool doubleColon = false;
        if (p < e && v[p].Kind == ComponentValueKind.Token && v[p].Token.Kind == CssTokenKind.Colon)
        {
            doubleColon = true;
            p++;
        }

        if (p >= e)
        {
            return null;
        }

        ComponentValue c = v[p];
        string name;
        bool isFn;
        if (c.Kind == ComponentValueKind.Function)
        {
            isFn = true;
            name = c.Token.Value;
        }
        else if (IsIdent(c))
        {
            isFn = false;
            name = c.Token.Value;
        }
        else
        {
            return null;
        }

        string lower = AsciiLower(name);
        Simple? result;
        if (doubleColon || (!isFn && s_legacyPseudoElements.Contains(lower)))
        {
            // Selectors 4 §3.6: a pseudo-element may follow another only when that one is a tree-abiding or shadow-part
            // pseudo-element (::part(), ::slotted()).
            if (!allowPe || (lastPe is not null and not ("part" or "slotted")))
            {
                return null;
            }

            result = ParsePseudoElement(st, c, lower, isFn, depth, insideHas);
        }
        else
        {
            if (lastPe is not null)
            {
                // After a pseudo-element only the pseudo-classes that do not depend on tree position may follow: user-action
                // ones, and after ::part() also the element-state ones. Nothing follows ::slotted().
                bool ok = lastPe == "part" ? s_afterPart.Contains(lower) : lastPe != "slotted" && s_afterPseudoElement.Contains(lower);
                if (!ok)
                {
                    return null;
                }
            }

            result = isFn ? ParseFunctionalPseudoClass(st, c, lower, depth, insideHas) : ParseSimplePseudoClass(lower);
            if (result is not null && lastPe == "part" && result.Sub is not null)
            {
                result = RestrictToStatePseudos(st, result);
            }
        }

        if (result is null)
        {
            return null;
        }

        pos = p + 1;
        return result;
    }

    private static Simple? ParseSimplePseudoClass(string lower)
    {
        if (!s_simplePseudo.TryGetValue(lower, out PseudoKind kind))
        {
            return null;
        }

        var s = new Simple { Kind = SimpleKind.PseudoClass, Pseudo = kind, Name = lower, Specificity = new Specificity(0, 1, 0) };
        s.Deps = kind switch
        {
            PseudoKind.Root or PseudoKind.Link or PseudoKind.AnyLink or PseudoKind.Defined or PseudoKind.HeadingAny => SelectorDependencies.None,
            PseudoKind.Empty => SelectorDependencies.Empty,
            PseudoKind.FirstChild or PseudoKind.LastChild or PseudoKind.OnlyChild or PseudoKind.FirstOfType or PseudoKind.LastOfType or PseudoKind.OnlyOfType => SelectorDependencies.Structural,
            PseudoKind.Scope => SelectorDependencies.Scope,
            PseudoKind.Hover => SelectorDependencies.Hover,
            PseudoKind.Active => SelectorDependencies.Active,
            PseudoKind.Focus or PseudoKind.FocusVisible => SelectorDependencies.Focus,
            PseudoKind.FocusWithin => SelectorDependencies.FocusWithin,
            PseudoKind.Target or PseudoKind.TargetWithin => SelectorDependencies.Target,
            PseudoKind.Never => SelectorDependencies.NeverMatches,
            _ => SelectorDependencies.FormState,
        };
        return s;
    }

    private static Simple? ParseFunctionalPseudoClass(State st, ComponentValue fn, string lower, int depth, bool insideHas)
    {
        ComponentValue[] args = ToArray(fn.Children);
        switch (lower)
        {
            case "not":
            case "is":
            case "where":
            case "has":
            {
                bool isHas = lower == "has";
                if (isHas && insideHas)
                {
                    return null;
                }

                ListKind kind = isHas ? ListKind.Relative : lower == "not" ? ListKind.Normal : ListKind.Forgiving;
                SelectorList? sub = ParseList(st, args, 0, args.Length, kind, depth + 1, insideHas, false);
                if (sub is null || (kind != ListKind.Forgiving && sub.Count == 0))
                {
                    return null;
                }

                return new Simple
                {
                    Kind = SimpleKind.Logical,
                    Name = lower,
                    Logical = lower switch { "not" => LogicalKind.Not, "is" => LogicalKind.Is, "where" => LogicalKind.Where, _ => LogicalKind.Has },
                    Sub = sub,
                    Specificity = lower == "where" ? default : sub.MaxSpecificity,
                    Deps = sub.Dependencies | (isHas ? SelectorDependencies.Has : SelectorDependencies.None),
                };
            }

            case "nth-child":
            case "nth-last-child":
            case "nth-of-type":
            case "nth-last-of-type":
                return ParseNthFunction(st, args, lower, depth, insideHas);

            case "lang":
                return ParseLang(args);

            case "dir":
            {
                int a = 0;
                int b = args.Length;
                Trim(args, ref a, ref b);
                if (b - a != 1 || !IsIdent(args[a]))
                {
                    return null;
                }

                string d = AsciiLower(args[a].Token.Value);
                if (d is not ("ltr" or "rtl"))
                {
                    return null;
                }

                return new Simple { Kind = SimpleKind.Dir, Name = "dir", Value = d, Specificity = new Specificity(0, 1, 0), Deps = SelectorDependencies.LangOrDir };
            }

            case "state":
            {
                int a = 0;
                int b = args.Length;
                Trim(args, ref a, ref b);
                if (b - a != 1 || !IsIdent(args[a]))
                {
                    return null;
                }

                return new Simple
                {
                    Kind = SimpleKind.PseudoClass,
                    Pseudo = PseudoKind.Never,
                    Name = "state",
                    ArgText = CssText.Ident(args[a].Token.Value),
                    Specificity = new Specificity(0, 1, 0),
                    Deps = SelectorDependencies.NeverMatches,
                };
            }

            case "heading":
                return ParseHeading(args);

            case "host":
            case "host-context":
            case "current":
            {
                SelectorList? sub = ParseList(st, args, 0, args.Length, ListKind.Normal, depth + 1, insideHas, false);
                if (sub is null || sub.Count == 0 || (lower != "current" && (sub.Count != 1 || sub.Items[0].Compounds.Length != 1 || !IsCompoundDeep(sub))))
                {
                    return null;
                }

                return new Simple
                {
                    Kind = SimpleKind.PseudoClass,
                    Pseudo = PseudoKind.Never,
                    Name = lower,
                    Sub = sub,
                    Specificity = new Specificity(0, 1, 0) + sub.MaxSpecificity,
                    Deps = SelectorDependencies.NeverMatches,
                };
            }

            default:
                return null;
        }
    }

    private static Simple? ParseLang(ComponentValue[] args)
    {
        var ranges = new List<string>();
        int segStart = 0;
        for (int i = 0; i <= args.Length; i++)
        {
            if (i < args.Length && !IsComma(args[i]))
            {
                continue;
            }

            int a = segStart;
            int b = i;
            segStart = i + 1;
            Trim(args, ref a, ref b);
            if (b - a != 1 || args[a].Kind != ComponentValueKind.Token)
            {
                return null;
            }

            CssToken t = args[a].Token;
            if (t.Kind == CssTokenKind.Ident || (t.Kind == CssTokenKind.QuotedString && t.IsTerminated))
            {
                ranges.Add(t.Value);
            }
            else
            {
                return null;
            }
        }

        return new Simple { Kind = SimpleKind.Lang, Name = "lang", Strings = [.. ranges], Specificity = new Specificity(0, 1, 0), Deps = SelectorDependencies.LangOrDir };
    }

    private static Simple? ParseHeading(ComponentValue[] args)
    {
        // :heading(<integer>#): h1-h6 whose level is one of the integers.
        var levels = new List<int>();
        int segStart = 0;
        for (int i = 0; i <= args.Length; i++)
        {
            if (i < args.Length && !IsComma(args[i]))
            {
                continue;
            }

            int a = segStart;
            int b = i;
            segStart = i + 1;
            Trim(args, ref a, ref b);
            if (b - a != 1 || args[a].Kind != ComponentValueKind.Token || args[a].Token.Kind != CssTokenKind.Number || !args[a].Token.IsInteger
                || !TryInt(args[a].Token.NumberValue, out int level))
            {
                return null;
            }

            levels.Add(0);
            levels.Add(level);
        }

        return new Simple { Kind = SimpleKind.Heading, Name = "heading", Pairs = [.. levels], Specificity = new Specificity(0, 1, 0) };
    }

    private static Simple? ParseNthFunction(State st, ComponentValue[] args, string lower, int depth, bool insideHas)
    {
        bool allowOf = lower is "nth-child" or "nth-last-child";
        int ofAt = -1;
        for (int i = 1; i < args.Length; i++)
        {
            if (IsIdent(args[i]) && args[i].Token.Value.Equals("of", StringComparison.OrdinalIgnoreCase) && args[i - 1].IsWhitespace)
            {
                ofAt = i;
                break;
            }
        }

        int a = 0;
        int b = ofAt >= 0 ? ofAt : args.Length;
        Trim(args, ref a, ref b);
        if (!TryParseAnPlusB(args, a, b, out int an, out int bn))
        {
            return null;
        }

        SelectorList? sub = null;
        if (ofAt >= 0)
        {
            if (!allowOf)
            {
                return null;
            }

            sub = ParseList(st, args, ofAt + 1, args.Length, ListKind.Normal, depth + 1, insideHas, false);
            if (sub is null || sub.Count == 0)
            {
                return null;
            }
        }

        return new Simple
        {
            Kind = SimpleKind.Nth,
            Name = lower,
            Nth = lower switch { "nth-child" => NthKind.Child, "nth-last-child" => NthKind.LastChild, "nth-of-type" => NthKind.OfType, _ => NthKind.LastOfType },
            A = an,
            B = bn,
            Sub = sub,
            Specificity = new Specificity(0, 1, 0) + (sub?.MaxSpecificity ?? default),
            Deps = SelectorDependencies.Structural | (sub?.Dependencies ?? SelectorDependencies.None),
        };
    }

    /// <summary>The &lt;an+b&gt; microsyntax of CSS Syntax §6 over v[a..b), with whitespace only where the grammar allows it.</summary>
    internal static bool TryParseAnPlusB(ComponentValue[] v, int a, int b, out int an, out int bn)
    {
        an = 0;
        bn = 0;
        var toks = new List<(CssToken T, bool Space)>();
        bool space = false;
        for (int i = a; i < b; i++)
        {
            if (v[i].IsWhitespace)
            {
                space = true;
                continue;
            }

            if (v[i].Kind != ComponentValueKind.Token)
            {
                return false;
            }

            toks.Add((v[i].Token, space));
            space = false;
        }

        int n = toks.Count;
        if (n == 0)
        {
            return false;
        }

        CssToken t0 = toks[0].T;
        if (n == 1 && t0.Kind == CssTokenKind.Ident)
        {
            string id = AsciiLower(t0.Value);
            if (id == "even")
            {
                an = 2;
                return true;
            }

            if (id == "odd")
            {
                an = 2;
                bn = 1;
                return true;
            }
        }

        if (n == 1 && t0.Kind == CssTokenKind.Number)
        {
            return t0.IsInteger && TryInt(t0.NumberValue, out bn);
        }

        string unit;
        int k;
        if (IsDelimTok(t0, '+'))
        {
            if (n < 2 || toks[1].Space || toks[1].T.Kind != CssTokenKind.Ident)
            {
                return false;
            }

            unit = AsciiLower(toks[1].T.Value);
            if (unit.Length == 0 || unit[0] != 'n')
            {
                return false;
            }

            an = 1;
            k = 2;
        }
        else if (t0.Kind == CssTokenKind.Ident)
        {
            string id = AsciiLower(t0.Value);
            if (id.StartsWith("-n", StringComparison.Ordinal))
            {
                an = -1;
                unit = id[1..];
            }
            else if (id.StartsWith('n'))
            {
                an = 1;
                unit = id;
            }
            else
            {
                return false;
            }

            k = 1;
        }
        else if (t0.Kind == CssTokenKind.Dimension)
        {
            if (!t0.IsInteger || !TryInt(t0.NumberValue, out an))
            {
                return false;
            }

            unit = AsciiLower(t0.Value);
            if (unit.Length == 0 || unit[0] != 'n')
            {
                return false;
            }

            k = 1;
        }
        else
        {
            return false;
        }

        if (unit == "n")
        {
            if (k == n)
            {
                return true;
            }

            CssToken t = toks[k].T;
            if (t.Kind == CssTokenKind.Delim && (IsDelimTok(t, '+') || IsDelimTok(t, '-')))
            {
                if (k + 2 != n)
                {
                    return false;
                }

                CssToken num = toks[k + 1].T;
                if (num.Kind != CssTokenKind.Number || !num.IsInteger || num.Sign != '\0' || !TryInt(num.NumberValue, out int m))
                {
                    return false;
                }

                bn = IsDelimTok(t, '-') ? -m : m;
                return true;
            }

            return k + 1 == n && t.Kind == CssTokenKind.Number && t.IsInteger && t.Sign != '\0' && TryInt(t.NumberValue, out bn);
        }

        if (unit == "n-")
        {
            if (k + 1 != n)
            {
                return false;
            }

            CssToken num = toks[k].T;
            if (num.Kind != CssTokenKind.Number || !num.IsInteger || num.Sign != '\0' || !TryInt(num.NumberValue, out int m))
            {
                return false;
            }

            bn = -m;
            return true;
        }

        if (unit.StartsWith("n-", StringComparison.Ordinal) && k == n)
        {
            ReadOnlySpan<char> digits = unit.AsSpan(2);
            foreach (char ch in digits)
            {
                if (ch is not (>= '0' and <= '9'))
                {
                    return false;
                }
            }

            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int m))
            {
                m = int.MaxValue / 2;
            }

            bn = -m;
            return true;
        }

        return false;
    }

    private static bool TryInt(double d, out int r)
    {
        if (double.IsNaN(d))
        {
            r = 0;
            return false;
        }

        // Beyond 32 bits the value is clamped: such a position is never reached in a document of fewer than 2^31 nodes.
        r = d >= int.MaxValue / 2 ? int.MaxValue / 2 : d <= int.MinValue / 2 ? int.MinValue / 2 : (int)d;
        return true;
    }

    private static Simple? ParsePseudoElement(State st, ComponentValue c, string lower, bool isFn, int depth, bool insideHas)
    {
        bool vendor = lower.StartsWith("-webkit-", StringComparison.Ordinal) || lower.StartsWith("-moz-", StringComparison.Ordinal);
        if (!vendor && !s_pseudoElements.Contains(lower))
        {
            return null;
        }

        var s = new Simple { Kind = SimpleKind.PseudoElement, Name = lower, Specificity = new Specificity(0, 0, 1), Deps = SelectorDependencies.NeverMatches };
        bool functionalOnly = lower is "slotted" or "part" or "highlight";
        bool plainOnly = lower is "before" or "after" or "first-line" or "first-letter" or "marker" or "placeholder" or "selection" or "backdrop"
            or "file-selector-button" or "spelling-error" or "grammar-error" or "target-text" or "details-content";
        if (!isFn)
        {
            return functionalOnly ? null : s;
        }

        if (plainOnly)
        {
            return null;
        }

        ComponentValue[] args = ToArray(c.Children);
        int a = 0;
        int b = args.Length;
        Trim(args, ref a, ref b);
        switch (lower)
        {
            case "slotted":
            {
                SelectorList? sub = ParseList(st, args, 0, args.Length, ListKind.Normal, depth + 1, insideHas, false);
                if (sub is null || sub.Count != 1 || sub.Items[0].Compounds.Length != 1 || !IsCompoundDeep(sub))
                {
                    return null;
                }

                s.Sub = sub;
                s.Specificity = new Specificity(0, 0, 1) + sub.MaxSpecificity;
                return s;
            }

            case "part":
            {
                var names = new List<string>();
                for (int i = a; i < b; i++)
                {
                    if (args[i].IsWhitespace)
                    {
                        continue;
                    }

                    if (!IsIdent(args[i]))
                    {
                        return null;
                    }

                    names.Add(args[i].Token.Value);
                }

                if (names.Count == 0)
                {
                    return null;
                }

                s.Strings = [.. names];
                return s;
            }

            case "highlight":
                if (b - a != 1 || !IsIdent(args[a]))
                {
                    return null;
                }

                s.Strings = [args[a].Token.Value];
                return s;

            default:
                if (a >= b && !vendor)
                {
                    return null;
                }

                s.ArgText = CssSerializer.Serialize(c.Children.Trimmed());
                return s;
        }
    }

    // ---- helpers ----

    /// <summary>True when every :not()/:is()/:where() inside holds only compound selectors (the argument of :host() and ::slotted() is a compound).</summary>
    private static bool IsCompoundDeep(SelectorList list)
    {
        var stack = new Stack<SelectorList>();
        stack.Push(list);
        while (stack.Count > 0)
        {
            foreach (ComplexSelector c in stack.Pop().Items)
            {
                if (c.Invalid)
                {
                    continue;
                }

                if (c.Compounds.Length != 1)
                {
                    return false;
                }

                foreach (Simple p in c.Compounds[0].Parts)
                {
                    if (p.Kind == SimpleKind.Logical && p.Logical != LogicalKind.Has)
                    {
                        stack.Push(p.Sub!);
                    }
                }
            }
        }

        return true;
    }

    private static void Trim(ComponentValue[] v, ref int a, ref int b)
    {
        while (a < b && v[a].IsWhitespace)
        {
            a++;
        }

        while (b > a && v[b - 1].IsWhitespace)
        {
            b--;
        }
    }

    private static ComponentValue[] ToArray(ComponentValueList list)
    {
        var l = new List<ComponentValue>();
        foreach (ComponentValue cv in list)
        {
            l.Add(cv);
        }

        return [.. l];
    }

    private static bool IsComma(ComponentValue c) => c.Kind == ComponentValueKind.Token && c.Token.Kind == CssTokenKind.Comma;

    private static bool IsIdent(ComponentValue c) => c.Kind == ComponentValueKind.Token && c.Token.Kind == CssTokenKind.Ident;

    private static bool IsDelim(ComponentValue c, char ch) => c.Kind == ComponentValueKind.Token && IsDelimTok(c.Token, ch);

    private static bool IsDelimTok(CssToken t, char ch)
    {
        if (t.Kind != CssTokenKind.Delim)
        {
            return false;
        }

        ReadOnlySpan<char> raw = t.Raw;
        return raw.Length == 1 && raw[0] == ch;
    }

    internal static string AsciiLower(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] is >= 'A' and <= 'Z')
            {
                return string.Create(s.Length, s, static (span, src) =>
                {
                    for (int k = 0; k < span.Length; k++)
                    {
                        char ch = src[k];
                        span[k] = ch is >= 'A' and <= 'Z' ? (char)(ch + 0x20) : ch;
                    }
                });
            }
        }

        return s;
    }
}
