using System;
using RecursiveAI.OneHtml.Selectors;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;

namespace RecursiveAI.OneHtml.Css;

/// <summary>Dense integer ids for the rows of the property table, used by the cascade for array-indexed lookups.</summary>
internal static class PropertyIds
{
    public static readonly PropertyInfo[] Info = [.. PropertyTable.All];

    public static readonly int Count = Info.Length;

    private static readonly FrozenDictionary<string, int> ByName = BuildNames();

    private static readonly FrozenDictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> Lookup = ByName.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>True for properties whose computed value is inherited by default (aliases and shorthands: false).</summary>
    public static readonly bool[] Inherited = BuildInherited();

    /// <summary>The ids of the inherited properties, ascending.</summary>
    public static readonly int[] InheritedIds = BuildInheritedIds();

    /// <summary>For each id, its position in <see cref="InheritedIds"/>, or -1.</summary>
    public static readonly int[] InheritedSlot = BuildInheritedSlots();

    public static readonly int Direction = Get("direction");
    public static readonly int WritingMode = Get("writing-mode");
    public static readonly int Color = Get("color");
    public static readonly int FontSize = Get("font-size");
    public static readonly int FontWeight = Get("font-weight");
    public static readonly int Display = Get("display");
    public static readonly int Visibility = Get("visibility");
    public static readonly int Opacity = Get("opacity");
    public static readonly int BackgroundColor = Get("background-color");
    public static readonly int All = Get("all");
    public static readonly int UnicodeBidi = Get("unicode-bidi");

    /// <summary>The id of a property name (any ASCII case, aliases resolved to their target), or -1 for an unknown name.</summary>
    public static int Get(ReadOnlySpan<char> name) => Lookup.TryGetValue(name, out int id) ? id : -1;

    private static FrozenDictionary<string, int> BuildNames()
    {
        var index = new Dictionary<string, int>(Info.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Info.Length; i++)
        {
            index[Info[i].Name] = i;
        }

        var d = new Dictionary<string, int>(Info.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Info.Length; i++)
        {
            PropertyInfo target = PropertyTable.ResolveAlias(Info[i]);
            d[Info[i].Name] = index[target.Name];
        }

        return d.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static bool[] BuildInherited()
    {
        var r = new bool[Info.Length];
        for (int i = 0; i < Info.Length; i++)
        {
            PropertyInfo p = Info[i];
            r[i] = p.Inherited == PropertyInheritance.Yes && p.LegacyAliasOf is null;
        }

        return r;
    }

    private static int[] BuildInheritedIds()
    {
        var l = new List<int>();
        for (int i = 0; i < Info.Length; i++)
        {
            if (Inherited[i])
            {
                l.Add(i);
            }
        }

        return [.. l];
    }

    private static int[] BuildInheritedSlots()
    {
        var r = new int[Info.Length];
        Array.Fill(r, -1);
        for (int k = 0; k < InheritedIds.Length; k++)
        {
            r[InheritedIds[k]] = k;
        }

        return r;
    }
}

/// <summary>The cascade layers of the three origins: a tree per origin, ranked by CSS Cascade 5 §6.4.1.</summary>
internal sealed class LayerTree
{
    private readonly List<int> _parent = [];
    private readonly List<string> _name = [];
    private readonly List<Dictionary<string, int>?> _byName = [];
    private readonly List<List<int>> _children = [];
    private int[] _rank = [];
    private readonly int[] _max = new int[3];
    private int _anon;

    public LayerTree()
    {
        for (int o = 0; o < 3; o++)
        {
            Add(-1, string.Empty);
        }
    }

    public int Count => _parent.Count;

    /// <summary>The unlayered "layer" of an origin: the root, which sorts after every named layer.</summary>
    public static int RootOf(StyleOrigin origin) => (int)origin;

    public int Parent(int layer) => _parent[layer];

    private int Add(int parent, string name)
    {
        _parent.Add(parent);
        _name.Add(name);
        _byName.Add(null);
        _children.Add([]);
        int id = _parent.Count - 1;
        if (parent >= 0)
        {
            _children[parent].Add(id);
        }

        return id;
    }

    /// <summary>Finds or creates the named (possibly dotted, <c>a.b</c>) layer below <paramref name="parent"/>. Returns -1 when the name is invalid or <paramref name="limit"/> layers exist.</summary>
    public int Resolve(int parent, string dottedName, int limit)
    {
        int cur = parent;
        int start = 0;
        while (true)
        {
            int dot = dottedName.IndexOf('.', start);
            string part = dot < 0 ? dottedName[start..] : dottedName[start..dot];
            if (part.Length == 0)
            {
                return -1;
            }

            Dictionary<string, int> map = _byName[cur] ??= new Dictionary<string, int>(StringComparer.Ordinal);
            if (!map.TryGetValue(part, out int child))
            {
                if (Count >= limit)
                {
                    return -1;
                }

                child = Add(cur, part);
                map[part] = child;
            }

            cur = child;
            if (dot < 0)
            {
                return cur;
            }

            start = dot + 1;
        }
    }

    /// <summary>Creates an anonymous layer below <paramref name="parent"/> (never referable by name).</summary>
    public int CreateAnonymous(int parent, int limit) => Count >= limit ? -1 : Add(parent, "(anonymous " + (++_anon).ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");

    /// <summary>
    /// Assigns each layer its rank within its origin: a layer's sub-layers in order of first appearance, then the layer
    /// itself (its own unlayered declarations form an implicit last sub-layer), so the root of an origin ranks highest.
    /// </summary>
    public void Rank()
    {
        _rank = new int[Count];
        for (int o = 0; o < 3; o++)
        {
            int counter = 0;
            var stack = new Stack<(int Node, int Next)>();
            stack.Push((o, 0));
            while (stack.Count > 0)
            {
                (int node, int next) = stack.Pop();
                if (next < _children[node].Count)
                {
                    stack.Push((node, next + 1));
                    stack.Push((_children[node][next], 0));
                    continue;
                }

                _rank[node] = counter++;
            }

            _max[o] = counter - 1;
        }
    }

    /// <summary>The layer's position in its origin's order, ascending; used as is for normal declarations.</summary>
    public int RankOf(int layer) => _rank[layer];

    /// <summary>The rank of the origin's unlayered root.</summary>
    public int MaxRank(StyleOrigin origin) => _max[(int)origin];

    /// <summary>The path of a layer, <c>a.b</c>; empty for a root.</summary>
    public string PathOf(int layer)
    {
        if (layer < 3)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        for (int l = layer; l >= 3; l = _parent[l])
        {
            parts.Add(_name[l]);
        }

        parts.Reverse();
        return string.Join('.', parts);
    }

    /// <summary>A signature of the structure for comparing two gathers: layer paths in rank order.</summary>
    public string Signature()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 3; i < Count; i++)
        {
            sb.Append(PathOf(i)).Append('|');
        }

        return sb.ToString();
    }
}

/// <summary>One style rule, or one run of declarations of a rule with nested rules, or an element's inline style or hints: the unit the cascade orders.</summary>
internal sealed class StyleRule
{
    private ParsedDeclaration[]? _parsed;

    public int Id;
    public DeclarationKind Kind;
    public StyleOrigin Origin;
    public Stylesheet? Sheet;
    public Node Owner;
    public string? Url;
    public CssNode RuleNode;
    public List<CssNode> DeclNodes = [];
    public CssTree? DeclTree;
    public SelectorList? Selectors;
    public string SelectorText = string.Empty;
    public int Layer;
    public int Order;
    public LayerTree? Layers;
    public bool Dynamic;
    public DeclarationValidator? Validator;

    /// <summary>The declarations, validated and expanded to longhands; built on first use.</summary>
    public ParsedDeclaration[] Parsed
    {
        get
        {
            ParsedDeclaration[]? p = Volatile.Read(ref _parsed);
            if (p is null)
            {
                var list = new List<ParsedDeclaration>(DeclNodes.Count);
                for (int i = 0; i < DeclNodes.Count; i++)
                {
                    ParsedDeclaration? d = ParsedDeclaration.Create(DeclNodes[i], this, i);
                    if (d is not null)
                    {
                        list.Add(d);
                    }
                }

                p = [.. list];
                p = Interlocked.CompareExchange(ref _parsed, p, null) ?? p;
            }

            return p;
        }
    }

    public DeclarationSource MakeSource(ParsedDeclaration d) =>
        new(Kind, Origin, d.Node, RuleNode, Sheet, Owner, d.Important, Layers is null ? string.Empty : Layers.PathOf(Layer), Kind == DeclarationKind.Sheet ? SelectorText : null, Url);
}

/// <summary>A declaration parsed for the cascade: validated, shorthands expanded to longhands (or kept pending for <c>var()</c>).</summary>
internal sealed class ParsedDeclaration
{
    private DeclarationSource? _source;

    private ParsedDeclaration(CssNode node, StyleRule rule, int index, bool important)
    {
        Node = node;
        Rule = rule;
        Index = index;
        Important = important;
    }

    public CssNode Node { get; }

    public StyleRule Rule { get; }

    /// <summary>The position among the rule's declarations.</summary>
    public int Index { get; }

    public bool Important { get; }

    /// <summary>The shorthand's property name when this declaration expands (or is pending expansion), else null.</summary>
    public string? ShorthandName { get; private set; }

    /// <summary>The whole declaration value (for a shorthand pending <c>var()</c>).</summary>
    public ComponentValueList FullValue { get; private set; }

    public StyleValue[] Values { get; private set; } = [];

    public DeclarationSource Source => _source ??= Rule.MakeSource(this);

    public static ParsedDeclaration? Create(CssNode node, StyleRule rule, int index)
    {
        var d = new ParsedDeclaration(node, rule, index, node.IsImportant);
        ComponentValueList value = node.Value;
        d.FullValue = value;
        if (node.IsCustomProperty)
        {
            string name = node.Name;
            CssWideKeyword kw = CssWideKeywords.Detect(value);
            d.Values = [new StyleValue(-1, name, value, kw, d, false, null)];
            return d;
        }

        string prop = node.Name;
        int id = PropertyIds.Get(prop);
        if (id < 0)
        {
            return null;
        }

        PropertyInfo info = PropertyIds.Info[id];
        if (id == PropertyIds.All)
        {
            // CSS Cascade 5 §3.2: `all` accepts only a CSS-wide keyword and sets every property but direction, unicode-bidi
            // and custom properties to it. A shorthand without an expansion grammar is stored under its own name too (see
            // below), so it is set as well; one with a grammar is covered by its longhands.
            CssWideKeyword all = CssWideKeywords.Detect(value);
            if (all == CssWideKeyword.None)
            {
                return null;
            }

            var list = new List<StyleValue>(PropertyIds.Count);
            for (int i = 0; i < PropertyIds.Count; i++)
            {
                PropertyInfo p = PropertyIds.Info[i];
                if (p.LegacyAliasOf is null && (!p.IsShorthand || !Shorthands.HasGrammar(p.Name)) && i != PropertyIds.Direction && i != PropertyIds.UnicodeBidi && i != PropertyIds.All)
                {
                    list.Add(new StyleValue(i, null, value, all, d, false, null));
                }
            }

            d.Values = [.. list];
            return d;
        }

        bool hasVar = VarSubstitution.ContainsVar(value);
        if (hasVar && !VarSubstitution.IsValidSyntax(value))
        {
            return null;
        }

        // The caller's grammar sees only what the author or user wrote; the UA sheet and presentational hints are ours.
        DeclarationValidator? validator = rule.Kind != DeclarationKind.PresentationalHint && rule.Origin != StyleOrigin.UserAgent ? rule.Validator : null;

        CssWideKeyword keyword = hasVar ? CssWideKeyword.None : CssWideKeywords.Detect(value);
        if (info.IsShorthand)
        {
            d.ShorthandName = info.Name;
            ShorthandExpansion ex = Shorthands.Expand(info.Name, value);
            switch (ex.Status)
            {
                case ShorthandStatus.Invalid:
                    return null;
                case ShorthandStatus.PendingSubstitution when Shorthands.HasGrammar(info.Name):
                {
                    var list = new List<StyleValue>(ex.Longhands.Count);
                    foreach (ExpandedLonghand l in ex.Longhands)
                    {
                        int lid = PropertyIds.Get(l.Name);
                        if (lid >= 0)
                        {
                            list.Add(new StyleValue(lid, null, value, CssWideKeyword.None, d, true, null));
                        }
                    }

                    d.Values = [.. list];
                    return d;
                }

                case ShorthandStatus.Expanded:
                {
                    var list = new List<StyleValue>(ex.Longhands.Count);
                    foreach (ExpandedLonghand l in ex.Longhands)
                    {
                        if (validator is not null && !hasVar && !Accepts(validator, rule.Origin, l.Name, l.Keyword, l.Value))
                        {
                            return null;
                        }

                        int lid = PropertyIds.Get(l.Name);
                        if (lid >= 0)
                        {
                            list.Add(new StyleValue(lid, null, l.Value, l.Keyword, d, false, null));
                        }
                    }

                    if (keyword != CssWideKeyword.None && !Shorthands.HasGrammar(info.Name))
                    {
                        // A shorthand without an expansion grammar is also stored under its own name when written with a value
                        // (see the NotExpanded case below), so a CSS-wide keyword must reach that slot as well as the longhands:
                        // `vertical-align: inherit` is a shorthand in CSS Inline 3 and would otherwise read as its initial value.
                        list.Add(new StyleValue(id, null, value, keyword, d, false, null));
                    }

                    d.Values = [.. list];
                    return d;
                }

                default:
                    // NotExpanded: the shorthand's own token list is kept; there is no grammar to check it against.
                    if (hasVar)
                    {
                        d.ShorthandName = null;
                        d.Values = [new StyleValue(id, null, value, CssWideKeyword.None, d, true, null)];
                        return d;
                    }

                    d.ShorthandName = null;
                    if (validator is not null && !Accepts(validator, rule.Origin, info.Name, keyword, value))
                    {
                        return null;
                    }

                    d.Values = [new StyleValue(id, null, value, keyword, d, false, null)];
                    return d;
            }
        }

        if (hasVar)
        {
            d.Values = [new StyleValue(id, null, value, CssWideKeyword.None, d, true, null)];
            return d;
        }

        if (keyword == CssWideKeyword.None && !IsValidLonghand(info.Name, value))
        {
            return null;
        }

        if (validator is not null && !Accepts(validator, rule.Origin, info.Name, keyword, value))
        {
            return null;
        }

        d.Values = [new StyleValue(id, null, value, keyword, d, false, null)];
        return d;
    }

    /// <summary>Asks the caller's validator about an untyped property's value; keywords, typed properties and values with <c>var()</c> are never asked.</summary>
    private static bool Accepts(DeclarationValidator validator, StyleOrigin origin, string name, CssWideKeyword keyword, ComponentValueList value)
    {
        if (keyword != CssWideKeyword.None || TypedProperties.IsTyped(name) || VarSubstitution.ContainsVar(value))
        {
            return true;
        }

        TokenRange range = TokenRange.Of(value).Trim();
        return range.IsEmpty || validator(name, range, origin);
    }

    /// <summary>Whether a longhand's value is acceptable at parse time; an invalid declaration is dropped before the cascade (CSS Cascade 5 §4.3 "invalid at parse time").</summary>
    public static bool IsValidLonghand(string name, ComponentValueList value) =>
        TypedProperties.IsTyped(name) ? TypedProperties.IsValid(name, value) : DeclarationSupport.IsSupported(name, value);
}
