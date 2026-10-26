using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using RecursiveAI.OneHtml.Dom;
using RecursiveAI.OneHtml.Selectors;

namespace RecursiveAI.OneHtml.Css;

/// <summary>A declaration competing for a property, with the cascade keys that order it (CSS Cascade 5 §6.1).</summary>
internal struct Cand
{
    public StyleValue? V;
    public StyleRule? Rule;

    /// <summary>Origin and importance tier, element-attached flag, layer: the part of the sort that precedes specificity.</summary>
    public long K1;

    /// <summary>Specificity packed as A, B, C of 20 bits each.</summary>
    public long K2;

    /// <summary>Order of appearance: rule order, then position in the rule.</summary>
    public long K3;

    public readonly int Tier => (int)(K1 >> 34);

    public readonly bool IsElementAttached => (K1 & (1L << 33)) != 0;

    public readonly int LayerKey => (int)(K1 & 0x7FFFFFFF);

    public static int Compare(in Cand a, in Cand b)
    {
        if (a.K1 != b.K1)
        {
            return a.K1 < b.K1 ? -1 : 1;
        }

        if (a.K2 != b.K2)
        {
            return a.K2 < b.K2 ? -1 : 1;
        }

        return a.K3 < b.K3 ? -1 : a.K3 > b.K3 ? 1 : 0;
    }
}

/// <summary>Per-thread working storage for one element's cascade.</summary>
internal sealed class CascadeScratch
{
    public readonly Cand[] Best = new Cand[PropertyIds.Count];
    public readonly int[] Stamp = new int[PropertyIds.Count];
    public int Epoch;
    public readonly List<int> Touched = [];
    public readonly Dictionary<string, Cand> Custom = new(StringComparer.Ordinal);
    public readonly List<RuleSel> Candidates = [];
    public readonly List<RuleSel> Matched = [];
    public readonly List<(StyleRule Rule, long Spec)> Sources = [];
    public int[] RuleStamp = [];
    public int[] RuleSlot = [];
    public int RuleEpoch;
    public StyleEngine? Owner;

    public bool Has(int id) => Stamp[id] == Epoch;
}

/// <summary>Which rules are set aside when a rolled-back cascade is evaluated again (CSS Cascade 5 §7.3 and §7.4).</summary>
internal sealed class Exclusions
{
    public int OriginClasses;
    public readonly List<(int Tier, int LayerKey)> Layers = [];
    public readonly List<(int Class, int NormalRank)> LayerCuts = [];
    public LayerTree? Tree;
    public readonly List<StyleRule> Rules = [];

    public int NormalRank(in Cand c)
    {
        bool important = c.Tier >= 4;
        StyleOrigin o = ClassOf(c.Tier) == 0 ? StyleOrigin.UserAgent : ClassOf(c.Tier) == 1 ? StyleOrigin.User : StyleOrigin.Author;
        return important ? Tree!.MaxRank(o) - c.LayerKey : c.LayerKey;
    }

    public static int ClassOf(int tier) => tier is 0 or 6 ? 0 : tier is 1 or 5 ? 1 : 2;

    public bool Excludes(in Cand c)
    {
        if ((OriginClasses & (1 << ClassOf(c.Tier))) != 0)
        {
            return true;
        }

        foreach ((int tier, int layerKey) in Layers)
        {
            if (c.Tier == tier && c.LayerKey == layerKey)
            {
                return true;
            }
        }

        // revert-layer in a layered origin: the current layer and every later layer go, at both importances
        // (CSS Cascade 5 §7.3.5: "or between its normal and important levels").
        foreach ((int cls, int cut) in LayerCuts)
        {
            if (!c.IsElementAttached && c.Tier is >= 0 and <= 6 && c.Tier != 2 && ClassOf(c.Tier) == cls && NormalRank(c) >= cut)
            {
                return true;
            }
        }

        foreach (StyleRule r in Rules)
        {
            if (ReferenceEquals(c.Rule, r))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Computes and shares element styles for one document, rule database and media state.</summary>
internal sealed class StyleEngine
{
    private const int MaxInlineRules = 1 << 16;

    [ThreadStatic]
    private static CascadeScratch? _scratch;

    private static readonly StyleValue?[] NoInherited = new StyleValue?[PropertyIds.InheritedIds.Length];

    private readonly Dictionary<int, ComputedStyle> _cache = [];
    private readonly ConcurrentDictionary<string, StyleRule?> _inlineRules = new(StringComparer.Ordinal);
    private readonly CssParseOptions _parse;
    private int _styleCounter;

    public StyleEngine(Document doc, StyleContext context, RuleDatabase db, StyleSourceOptions options)
    {
        Doc = doc;
        Context = context;
        Db = db;
        _parse = options.ParseOptions ?? CssParseOptions.Default;
        BaseLength = context.CreateLengthContext();
        ColorOptions = new ColorParseOptions { DarkScheme = context.Media.ColorScheme == PrefersColorScheme.Dark };
        UsesBulkCaches = false;
        foreach (StyleRule r in db.Rules)
        {
            SelectorDependencies deps = r.Selectors!.Dependencies;
            UsesHas |= (deps & SelectorDependencies.Has) != 0;
            UsesStructure |= (deps & (SelectorDependencies.Structural | SelectorDependencies.SiblingCombinator | SelectorDependencies.Empty | SelectorDependencies.Has)) != 0;
            if ((r.Selectors!.Dependencies & (SelectorDependencies.Has | SelectorDependencies.Structural)) != 0)
            {
                UsesBulkCaches = true;
                break;
            }
        }
    }

    public Document Doc { get; }

    public StyleContext Context { get; }

    public RuleDatabase Db { get; }

    public LengthContext BaseLength { get; }

    public ColorParseOptions ColorOptions { get; }

    /// <summary>True when some selector uses <c>:has()</c> or position pseudo-classes, so matching shares bulk caches under a lock.</summary>
    public bool UsesBulkCaches { get; }

    /// <summary>True when some selector uses <c>:has()</c>.</summary>
    public bool UsesHas { get; }

    /// <summary>True when some selector reads siblings, positions, emptiness or descendants, so a change of the tree shape reaches neighbours.</summary>
    public bool UsesStructure { get; }

    public int StyleCount => _cache.Count;

    public int NextStyleId() => Interlocked.Increment(ref _styleCounter);

    private CascadeScratch GetScratch()
    {
        CascadeScratch s = _scratch ??= new CascadeScratch();
        if (!ReferenceEquals(s.Owner, this))
        {
            s.Owner = this;
            if (s.RuleStamp.Length < Db.Rules.Count)
            {
                s.RuleStamp = new int[Db.Rules.Count];
                s.RuleSlot = new int[Db.Rules.Count];
                s.RuleEpoch = 0;
            }
        }

        return s;
    }

    // ---- matching ----

    /// <summary>Appends the rules whose selectors match the element (each once, with its highest matching specificity) to <paramref name="s"/>.</summary>
    private void MatchRules(Node el, SelectorMatchOptions? options, CascadeScratch s)
    {
        s.Candidates.Clear();
        s.Matched.Clear();
        Db.Index.Collect(el, s.Candidates);
        if (s.Candidates.Count == 0)
        {
            return;
        }

        SelectorBulkCache? bulk = options?.BulkCache;
        if (bulk is not null)
        {
            lock (bulk)
            {
                SelectorMatcher.FilterMatching(el, s.Candidates, options, s.Matched);
            }
        }
        else
        {
            SelectorMatcher.FilterMatching(el, s.Candidates, options, s.Matched);
        }

        if (++s.RuleEpoch == int.MaxValue)
        {
            Array.Clear(s.RuleStamp);
            s.RuleEpoch = 1;
        }

        foreach (RuleSel m in s.Matched)
        {
            long spec = Pack(m.Spec);
            int rid = m.Rule.Id;
            if (s.RuleStamp[rid] == s.RuleEpoch)
            {
                int slot = s.RuleSlot[rid];
                if (spec > s.Sources[slot].Spec)
                {
                    s.Sources[slot] = (m.Rule, spec);
                }
            }
            else
            {
                s.RuleStamp[rid] = s.RuleEpoch;
                s.RuleSlot[rid] = s.Sources.Count;
                s.Sources.Add((m.Rule, spec));
            }
        }
    }

    /// <summary>The selectors of a rule set that match an element, as (rule id, selector index) pairs; used by overlay and restyle to see which rules a state change reaches.</summary>
    public static void MatchSet(Node el, RuleIndex index, SelectorMatchOptions? options, List<RuleSel> candidates, List<RuleSel> matched)
    {
        candidates.Clear();
        matched.Clear();
        index.Collect(el, candidates);
        if (candidates.Count == 0)
        {
            return;
        }

        if (options?.BulkCache is { } bulk)
        {
            lock (bulk)
            {
                SelectorMatcher.FilterMatching(el, candidates, options, matched);
            }
        }
        else
        {
            SelectorMatcher.FilterMatching(el, candidates, options, matched);
        }
    }

    internal static long Pack(Specificity sp) =>
        ((long)Math.Min(sp.A, 0xFFFFF) << 40) | ((long)Math.Min(sp.B, 0xFFFFF) << 20) | (long)Math.Min(sp.C, 0xFFFFF);

    // ---- ranking ----

    private long KeyOne(StyleRule r, bool important)
    {
        int tier;
        int layerKey;
        long styleFlag = 0;
        switch (r.Kind)
        {
            case DeclarationKind.InlineStyle:
                tier = important ? 4 : 3;
                layerKey = important ? 0 : Db.Layers.MaxRank(StyleOrigin.Author);
                styleFlag = 1;
                break;
            case DeclarationKind.PresentationalHint:
                tier = 2;
                layerKey = 0;
                break;
            default:
            {
                StyleOrigin o = r.Origin;
                tier = important ? (o == StyleOrigin.UserAgent ? 6 : o == StyleOrigin.User ? 5 : 4) : (o == StyleOrigin.UserAgent ? 0 : o == StyleOrigin.User ? 1 : 3);
                int rank = Db.Layers.RankOf(r.Layer);
                layerKey = important ? Db.Layers.MaxRank(o) - rank : rank;
                break;
            }
        }

        return ((long)tier << 34) | (styleFlag << 33) | (uint)layerKey;
    }

    private Cand MakeCand(StyleRule rule, long spec, ParsedDeclaration d, StyleValue v) => new()
    {
        V = v,
        Rule = rule,
        K1 = KeyOne(rule, d.Important),
        K2 = spec,
        K3 = ((long)rule.Order << 24) | (long)Math.Min(d.Index, 0xFFFFFF),
    };

    private static int SlotOf(int id, int mode)
    {
        int p = LogicalMap.Physical(id, mode);
        return p >= 0 ? p : id;
    }

    // ---- inline style and presentational hints ----

    private StyleRule? InlineRule(string text, DeclarationKind kind)
    {
        string key = (kind == DeclarationKind.InlineStyle ? "s" : "h") + text;
        if (_inlineRules.TryGetValue(key, out StyleRule? cached))
        {
            return cached;
        }

        StyleRule? rule = null;
        try
        {
            CssTree tree = CssTree.ParseDeclarationList(text, _parse);
            var decls = new List<CssNode>();
            foreach (CssNode n in tree.Nodes)
            {
                if (n.Kind == CssNodeKind.Declaration)
                {
                    decls.Add(n);
                }
            }

            if (decls.Count > 0)
            {
                rule = new StyleRule
                {
                    Id = -1,
                    Kind = kind,
                    Origin = StyleOrigin.Author,
                    DeclNodes = decls,
                    DeclTree = tree,
                    Layer = LayerTree.RootOf(StyleOrigin.Author),
                    Layers = null,
                    Order = kind == DeclarationKind.InlineStyle ? int.MaxValue >> 8 : 0,
                };
            }
        }
        catch (CssLimitExceededException)
        {
            rule = null;
        }

        if (_inlineRules.Count < MaxInlineRules)
        {
            _inlineRules[key] = rule;
        }

        return rule;
    }

    // ---- the cascade for one element ----

    /// <summary>Computes (or finds the shared) style of an element, given its parent element's style.</summary>
    public ComputedStyle Compute(Node el, ComputedStyle? parent, SelectorMatchOptions? options)
    {
        CascadeScratch s = GetScratch();
        if (++s.Epoch == int.MaxValue)
        {
            Array.Clear(s.Stamp);
            s.Epoch = 1;
        }

        s.Touched.Clear();
        s.Custom.Clear();
        s.Sources.Clear();

        // Sources of declarations: presentational hints, matching rules, then the style attribute.
        if (PresentationalHints.For(el) is { } hints && InlineRule(hints, DeclarationKind.PresentationalHint) is { } hr)
        {
            s.Sources.Add((hr, 0));
        }

        MatchRules(el, options, s);

        if (el.GetAttribute("style") is { Length: > 0 } styleText && InlineRule(styleText, DeclarationKind.InlineStyle) is { } ir)
        {
            s.Sources.Add((ir, 0));
        }

        // 1. Offer every declaration to its property.
        foreach ((StyleRule rule, long spec) in s.Sources)
        {
            foreach (ParsedDeclaration d in rule.Parsed)
            {
                foreach (StyleValue v in d.Values)
                {
                    Offer(s, MakeCand(rule, spec, d, v));
                }
            }
        }

        // 2. Writing mode and direction decide how logical properties map to physical ones.
        int mode = ComputeMode(s, parent);

        // 3. A logical declaration and its physical partner compete as one property (Cascade 5 §6.4).
        MergeLogical(s, mode);

        // 4. Roll back where a winner is revert, revert-layer or revert-rule.
        ResolveRollbacks(s, mode);

        s.Touched.Sort();
        return Finish(el, s, parent, mode);
    }

    /// <summary>
    /// Computes the style of a generated box (<c>::before</c>, <c>::after</c>, <c>::marker</c>) of an element: the declarations
    /// of the rules whose selector ends in that pseudo-element and whose rest matches the element, inheriting from the
    /// element's own style (CSS Pseudo-Elements 4 section 3). Returns null when no rule applies.
    /// </summary>
    public ComputedStyle? ComputePseudo(Node el, string pseudo, ComputedStyle elementStyle, SelectorMatchOptions? options)
    {
        if (!Db.PseudoIndex.TryGetValue(pseudo, out RuleIndex? index))
        {
            return null;
        }

        CascadeScratch s = GetScratch();
        s.Candidates.Clear();
        s.Matched.Clear();
        index.Collect(el, s.Candidates);
        if (s.Candidates.Count == 0)
        {
            return null;
        }

        SelectorBulkCache? bulk = options?.BulkCache;
        if (bulk is not null)
        {
            lock (bulk)
            {
                SelectorMatcher.FilterMatchingPseudo(el, s.Candidates, options, s.Matched);
            }
        }
        else
        {
            SelectorMatcher.FilterMatchingPseudo(el, s.Candidates, options, s.Matched);
        }

        if (s.Matched.Count == 0)
        {
            return null;
        }

        if (++s.Epoch == int.MaxValue)
        {
            Array.Clear(s.Stamp);
            s.Epoch = 1;
        }

        s.Touched.Clear();
        s.Custom.Clear();
        s.Sources.Clear();
        if (++s.RuleEpoch == int.MaxValue)
        {
            Array.Clear(s.RuleStamp);
            s.RuleEpoch = 1;
        }

        foreach (RuleSel m in s.Matched)
        {
            long spec = Pack(m.Spec);
            int rid = m.Rule.Id;
            if (s.RuleStamp[rid] == s.RuleEpoch)
            {
                int slot = s.RuleSlot[rid];
                if (spec > s.Sources[slot].Spec)
                {
                    s.Sources[slot] = (m.Rule, spec);
                }
            }
            else
            {
                s.RuleStamp[rid] = s.RuleEpoch;
                s.RuleSlot[rid] = s.Sources.Count;
                s.Sources.Add((m.Rule, spec));
            }
        }

        foreach ((StyleRule rule, long spec) in s.Sources)
        {
            foreach (ParsedDeclaration d in rule.Parsed)
            {
                foreach (StyleValue v in d.Values)
                {
                    Offer(s, MakeCand(rule, spec, d, v));
                }
            }
        }

        int mode = ComputeMode(s, elementStyle);
        MergeLogical(s, mode);
        ResolveRollbacks(s, mode);
        s.Touched.Sort();
        return Finish(el, s, elementStyle, mode);
    }

    private static void Offer(CascadeScratch s, in Cand c)
    {
        StyleValue v = c.V!;
        if (v.CustomName is { } name)
        {
            if (!s.Custom.TryGetValue(name, out Cand cur) || Cand.Compare(c, cur) > 0)
            {
                s.Custom[name] = c;
            }

            return;
        }

        int id = v.PropertyId;
        if (s.Stamp[id] != s.Epoch)
        {
            s.Stamp[id] = s.Epoch;
            s.Best[id] = c;
            s.Touched.Add(id);
        }
        else if (Cand.Compare(c, s.Best[id]) > 0)
        {
            s.Best[id] = c;
        }
    }

    private int ComputeMode(CascadeScratch s, ComputedStyle? parent)
    {
        string wm = ModeKeyword(s, PropertyIds.WritingMode, parent);
        string dir = ModeKeyword(s, PropertyIds.Direction, parent);
        return LogicalMap.ModeIndex(wm, dir == "rtl");
    }

    /// <summary>The keyword of a small keyword property (inherited) at this point of the cascade, with inheritance applied.</summary>
    private string ModeKeyword(CascadeScratch s, int id, ComputedStyle? parent)
    {
        Cand? best = s.Has(id) ? s.Best[id] : null;
        if (best is { } b && IsRollback(b.V!.Keyword))
        {
            best = Rescan(s, id, id, null, 0, b);
        }

        string fallback = parent is null ? ComputedStyle.InitialOf(id).Text : parent.Get(id).Text;
        if (best is not { } w)
        {
            return fallback;
        }

        StyleValue v = w.V!;
        if (v.Keyword is CssWideKeyword.Inherit or CssWideKeyword.Unset || v.Pending)
        {
            return fallback;
        }

        if (v.Keyword == CssWideKeyword.Initial)
        {
            return ComputedStyle.InitialOf(id).Text;
        }

        return v.Text.AsciiLower();
    }

    private static bool IsRollback(CssWideKeyword k) => k is CssWideKeyword.Revert or CssWideKeyword.RevertLayer or CssWideKeyword.RevertRule;

    private static void MergeLogical(CascadeScratch s, int mode)
    {
        int count = s.Touched.Count;
        for (int i = 0; i < count; i++)
        {
            int id = s.Touched[i];
            int phys = LogicalMap.Physical(id, mode);
            if (phys < 0)
            {
                continue;
            }

            Cand c = s.Best[id];
            if (s.Stamp[phys] != s.Epoch)
            {
                s.Stamp[phys] = s.Epoch;
                s.Best[phys] = c;
                s.Touched.Add(phys);
            }
            else if (Cand.Compare(c, s.Best[phys]) > 0)
            {
                s.Best[phys] = c;
            }

            s.Stamp[id] = 0;
        }

        // Drop the logical ids that were merged away.
        s.Touched.RemoveAll(id => s.Stamp[id] != s.Epoch);
    }

    private void ResolveRollbacks(CascadeScratch s, int mode)
    {
        for (int i = 0; i < s.Touched.Count; i++)
        {
            int id = s.Touched[i];
            Cand c = s.Best[id];
            if (IsRollback(c.V!.Keyword))
            {
                Cand? r = Rescan(s, id, id, null, mode, c);
                if (r is { } rc)
                {
                    s.Best[id] = rc;
                }
                else
                {
                    s.Stamp[id] = 0;
                }
            }
        }

        s.Touched.RemoveAll(id => s.Stamp[id] != s.Epoch);

        List<string>? drop = null;
        List<KeyValuePair<string, Cand>>? replace = null;
        foreach (KeyValuePair<string, Cand> kv in s.Custom)
        {
            if (IsRollback(kv.Value.V!.Keyword))
            {
                Cand? r = Rescan(s, -1, -1, kv.Key, mode, kv.Value);
                if (r is { } rc)
                {
                    (replace ??= []).Add(new KeyValuePair<string, Cand>(kv.Key, rc));
                }
                else
                {
                    (drop ??= []).Add(kv.Key);
                }
            }
        }

        if (drop is not null)
        {
            foreach (string k in drop)
            {
                s.Custom.Remove(k);
            }
        }

        if (replace is not null)
        {
            foreach (KeyValuePair<string, Cand> kv in replace)
            {
                s.Custom[kv.Key] = kv.Value;
            }
        }
    }

    /// <summary>
    /// Evaluates the cascade for one property again with the declarations a rollback sets aside left out, repeating while
    /// the new winner is a rollback too. Null means nothing is left: the property then takes its default.
    /// </summary>
    private Cand? Rescan(CascadeScratch s, int slot, int idForSlot, string? custom, int mode, Cand first)
    {
        _ = idForSlot;
        var ex = new Exclusions();
        Cand cur = first;
        for (int guard = 0; guard < 64; guard++)
        {
            switch (cur.V!.Keyword)
            {
                case CssWideKeyword.Revert:
                    ex.OriginClasses |= 1 << Exclusions.ClassOf(cur.Tier);
                    break;
                case CssWideKeyword.RevertLayer:
                    if (cur.IsElementAttached || cur.Tier == 2)
                    {
                        ex.Layers.Add((cur.Tier, cur.LayerKey));
                    }
                    else
                    {
                        ex.Tree = Db.Layers;
                        ex.LayerCuts.Add((Exclusions.ClassOf(cur.Tier), ex.NormalRank(cur)));
                    }

                    break;
                case CssWideKeyword.RevertRule:
                    ex.Rules.Add(cur.Rule!);
                    break;
                default:
                    return cur;
            }

            bool found = false;
            Cand best = default;
            foreach ((StyleRule rule, long spec) in s.Sources)
            {
                foreach (ParsedDeclaration d in rule.Parsed)
                {
                    foreach (StyleValue v in d.Values)
                    {
                        bool same = custom is not null ? v.CustomName == custom : v.CustomName is null && SlotOf(v.PropertyId, mode) == slot;
                        if (!same)
                        {
                            continue;
                        }

                        Cand c = MakeCand(rule, spec, d, v);
                        if (ex.Excludes(c))
                        {
                            continue;
                        }

                        if (!found || Cand.Compare(c, best) > 0)
                        {
                            best = c;
                            found = true;
                        }
                    }
                }
            }

            if (!found)
            {
                return null;
            }

            cur = best;
        }

        return null;
    }

    // ---- building the style ----

    private ComputedStyle Finish(Node el, CascadeScratch s, ComputedStyle? parent, int mode)
    {
        _ = el;
        // The key: everything the resolved style is a function of (the parent style and the winning declarations).
        int n = s.Touched.Count;
        var ids = new int[n];
        var vals = new StyleValue[n];
        for (int i = 0; i < n; i++)
        {
            ids[i] = s.Touched[i];
            vals[i] = s.Best[ids[i]].V!;
        }

        string[] cNames = [];
        StyleValue[] cVals = [];
        if (s.Custom.Count > 0)
        {
            cNames = [.. s.Custom.Keys];
            Array.Sort(cNames, StringComparer.Ordinal);
            cVals = new StyleValue[cNames.Length];
            for (int i = 0; i < cNames.Length; i++)
            {
                cVals[i] = s.Custom[cNames[i]].V!;
            }
        }

        int hash = parent?.Id ?? 0;
        for (int i = 0; i < n; i++)
        {
            hash = HashCode.Combine(hash, ids[i], RuntimeHelpers.GetHashCode(vals[i]));
        }

        for (int i = 0; i < cNames.Length; i++)
        {
            hash = HashCode.Combine(hash, cNames[i].GetHashCode(StringComparison.Ordinal), RuntimeHelpers.GetHashCode(cVals[i]));
        }

        lock (_cache)
        {
            if (_cache.TryGetValue(hash, out ComputedStyle? chain))
            {
                for (ComputedStyle? c = chain; c is not null; c = c.CacheNext)
                {
                    if (c.KeyMatches(parent, ids, vals, cNames, cVals))
                    {
                        return c;
                    }
                }
            }
        }

        ComputedStyle built = Build(parent, ids, vals, cNames, cVals, mode);
        lock (_cache)
        {
            if (_cache.TryGetValue(hash, out ComputedStyle? chain))
            {
                for (ComputedStyle? c = chain; c is not null; c = c.CacheNext)
                {
                    if (c.KeyMatches(parent, ids, vals, cNames, cVals))
                    {
                        return c;
                    }
                }

                built.CacheNext = chain;
            }

            _cache[hash] = built;
        }

        return built;
    }

    private ComputedStyle Build(ComputedStyle? parent, int[] ids, StyleValue[] vals, string[] cNames, StyleValue[] cVals, int mode)
    {
        // Custom properties first: var() in ordinary properties reads them.
        CustomProps custom = BuildCustom(parent, cNames, cVals);

        var ownIds = new List<int>(ids.Length);
        var ownVals = new List<StyleValue>(ids.Length);
        StyleValue?[]? inherited = null;
        StyleValue?[] parentInherited = parent?.InheritedValues ?? NoInherited;
        Dictionary<ParsedDeclaration, ShorthandExpansion?>? expansions = null;
        int declared = 0;

        for (int i = 0; i < ids.Length; i++)
        {
            int slot = ids[i];
            StyleValue v = vals[i];
            bool isInherited = PropertyIds.Inherited[slot];
            StyleValue? resolved = ResolveValue(slot, v, parent, custom, ref expansions);
            // resolved == null means "unset by default": inherit if the property inherits, else the initial value.
            if (resolved is null)
            {
                if (isInherited)
                {
                    SetInherited(ref inherited, parentInherited, slot, parentInherited[PropertyIds.InheritedSlot[slot]]);
                }

                continue;
            }

            if (v.Keyword is not (CssWideKeyword.Inherit or CssWideKeyword.Unset))
            {
                if (slot == PropertyIds.Color)
                {
                    declared |= ComputedStyle.DeclColor;
                }
                else if (slot == PropertyIds.FontSize)
                {
                    declared |= ComputedStyle.DeclFontSize;
                }
                else if (slot == PropertyIds.FontWeight)
                {
                    declared |= ComputedStyle.DeclFontWeight;
                }
            }

            if (isInherited)
            {
                SetInherited(ref inherited, parentInherited, slot, resolved);
            }
            else if (!resolved.IsInitial)
            {
                ownIds.Add(slot);
                ownVals.Add(resolved);
            }
        }

        var style = new ComputedStyle(this, parent, [.. ownIds], [.. ownVals], inherited ?? parentInherited, custom, mode);
        style.SetKey(ids, vals, cNames, cVals);
        style.Declared = declared;
        return style;
    }

    private static void SetInherited(ref StyleValue?[]? table, StyleValue?[] parentTable, int id, StyleValue? value)
    {
        int k = PropertyIds.InheritedSlot[id];
        if (ReferenceEquals(parentTable[k], value) && table is null)
        {
            return;
        }

        if (table is null)
        {
            table = new StyleValue?[parentTable.Length];
            Array.Copy(parentTable, table, parentTable.Length);
        }

        table[k] = value;
    }

    /// <summary>Applies CSS-wide keywords and <c>var()</c>; null means the property takes its default (inherit or initial).</summary>
    private static StyleValue? ResolveValue(int slot, StyleValue v, ComputedStyle? parent, CustomProps custom, ref Dictionary<ParsedDeclaration, ShorthandExpansion?>? expansions)
    {
        switch (v.Keyword)
        {
            case CssWideKeyword.Initial:
                return ComputedStyle.InitialOf(slot);
            case CssWideKeyword.Inherit:
                return parent is null ? ComputedStyle.InitialOf(slot) : parent.Get(slot);
            case CssWideKeyword.Unset:
            case CssWideKeyword.Revert:
            case CssWideKeyword.RevertLayer:
            case CssWideKeyword.RevertRule:
                return null;
        }

        if (!v.Pending)
        {
            return v;
        }

        // CSS Variables 1 §3.2: substitute at computed-value time; a value that is invalid then behaves as unset.
        ParsedDeclaration d = v.Declaration!;
        if (d.ShorthandName is { } shorthand)
        {
            expansions ??= [];
            if (!expansions.TryGetValue(d, out ShorthandExpansion? ex))
            {
                SubstitutionResult sub = VarSubstitution.Substitute(d.FullValue, custom.Values);
                ex = sub.IsValid && !VarSubstitution.ContainsVar(sub.Value) ? Shorthands.Expand(shorthand, sub.Value) : null;
                if (ex is not null && ex.Status is ShorthandStatus.Invalid or ShorthandStatus.PendingSubstitution or ShorthandStatus.NotShorthand)
                {
                    ex = null;
                }

                expansions[d] = ex;
            }

            if (ex is null)
            {
                return null;
            }

            string target = PropertyIds.Info[slot].Name;
            foreach (ExpandedLonghand l in ex.Longhands)
            {
                if (string.Equals(l.Name, target, StringComparison.OrdinalIgnoreCase))
                {
                    return l.Keyword switch
                    {
                        CssWideKeyword.Initial => ComputedStyle.InitialOf(slot),
                        CssWideKeyword.Inherit => parent is null ? ComputedStyle.InitialOf(slot) : parent.Get(slot),
                        CssWideKeyword.None => new StyleValue(slot, null, l.Value, CssWideKeyword.None, d, false, null),
                        _ => null,
                    };
                }
            }

            return null;
        }

        SubstitutionResult r = VarSubstitution.Substitute(v.Tokens, custom.Values);
        if (!r.IsValid)
        {
            return null;
        }

        ComponentValueList value = r.Value;
        CssWideKeyword kw = CssWideKeywords.Detect(value);
        if (kw != CssWideKeyword.None)
        {
            return null;
        }

        if (!ParsedDeclaration.IsValidLonghand(PropertyIds.Info[slot].Name, value) && TypedOrChecked(slot))
        {
            return null;
        }

        return new StyleValue(slot, null, value, CssWideKeyword.None, d, false, r.Text);
    }

    private static bool TypedOrChecked(int slot) => !PropertyIds.Info[slot].IsShorthand;

    private static CustomProps BuildCustom(ComputedStyle? parent, string[] names, StyleValue[] values)
    {
        CustomProps inherited = parent?.Custom ?? CustomProps.Empty;
        if (names.Length == 0)
        {
            return inherited;
        }

        var specified = new Dictionary<string, ComponentValueList>(names.Length, StringComparer.Ordinal);
        var removed = new List<string>();
        var winners = new Dictionary<string, StyleValue>(StringComparer.Ordinal);
        for (int i = 0; i < names.Length; i++)
        {
            StyleValue v = values[i];
            switch (v.Keyword)
            {
                case CssWideKeyword.Initial:
                    removed.Add(names[i]);
                    break;
                case CssWideKeyword.None:
                    specified[names[i]] = v.Tokens;
                    winners[names[i]] = v;
                    break;
                default:
                    // inherit, unset and unresolved rollbacks: the inherited value stays.
                    break;
            }
        }

        if (specified.Count == 0 && removed.Count == 0)
        {
            return inherited;
        }

        Dictionary<string, string> resolved = specified.Count == 0 ? [] : VarSubstitution.ResolveCustomProperties(specified, inherited.Values);
        var all = new Dictionary<string, string>(inherited.Values, StringComparer.Ordinal);
        foreach (string r in removed)
        {
            all.Remove(r);
        }

        foreach (string name in specified.Keys)
        {
            all.Remove(name);
        }

        foreach (KeyValuePair<string, string> kv in resolved)
        {
            all[kv.Key] = kv.Value;
        }

        return new CustomProps(all, winners.Count == 0 ? null : winners);
    }
}
