using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace RecursiveAI.OneHtml.Css;

/// <summary>
/// The <c>@keyframes</c> rule that wins for a name (CSS Animations 1 §3, CSS Cascade 5 §6.4 for layers): the last one in document
/// order within the same origin and cascade layer, an unlayered rule over a layered one, a later layer over an earlier one, an author rule over a
/// user rule. A rule written <c>@-webkit-keyframes</c> only counts when no <c>@keyframes</c> of that name exists, as in Chrome. The rule is
/// the specified rule: <c>var()</c> is not yet substituted; see <see cref="DocumentStyles.ResolveKeyframes"/>.
/// </summary>
public sealed class KeyframesRule
{
    private readonly RuleDatabase _db;
    private readonly KeyframeEntry _entry;
    private IReadOnlyList<KeyframeBlock>? _blocks;
    private KeyframeBlockData[]? _data;

    internal KeyframesRule(RuleDatabase db, KeyframeEntry entry)
    {
        _db = db;
        _entry = entry;
    }

    /// <summary>The name, case-sensitive; the identifier or string that follows <c>@keyframes</c>.</summary>
    public string Name => _entry.Name;

    /// <summary>True for <c>@-webkit-keyframes</c>.</summary>
    public bool IsPrefixed => _entry.Prefixed;

    /// <summary>The origin of the sheet that holds the rule.</summary>
    public StyleOrigin Origin => _entry.Origin;

    /// <summary>The cascade layer the rule is in, <c>a.b</c>; empty when unlayered.</summary>
    public string LayerPath => _db.Layers.PathOf(_entry.Layer);

    /// <summary>The rule in its style sheet.</summary>
    public CssNode Node => _entry.Node;

    /// <summary>The sheet's <c>style</c> or <c>link</c> element; the null node for user sheets and imports.</summary>
    public Node Owner => _entry.Owner;

    /// <summary>The URL of the sheet, or null for <c>style</c> text.</summary>
    public string? Url => _entry.Url;

    /// <summary>
    /// The keyframe blocks in source order, one per selector (<c>0%, 100% { }</c> is two). A block with an invalid selector (outside 0% to 100%,
    /// a keyword other than <c>from</c> and <c>to</c>) is dropped with its declarations, as CSS Animations 1 §3 requires. In a block, a declaration with
    /// <c>!important</c>, an invalid declaration, a custom property, an <c>animation-*</c> or <c>transition-*</c> property (but for
    /// <c>animation-timing-function</c> and <c>animation-composition</c>, which are kept apart) and a property Chrome does not animate
    /// (<c>direction</c>, <c>unicode-bidi</c>, <c>writing-mode</c>, <c>text-orientation</c>, <c>will-change</c>, <c>contain</c>, <c>container*</c>, <c>all</c>)
    /// is dropped. Shorthands are expanded to longhands, except a shorthand with <c>var()</c>, which stays one declaration until the element is known.
    /// </summary>
    public IReadOnlyList<KeyframeBlock> Blocks
    {
        get
        {
            _blocks ??= [.. Data.Select(d => d.ToPublic())];
            return _blocks;
        }
    }

    internal KeyframeBlockData[] Data => _data ??= KeyframeParser.Parse(_db, _entry);

    // Two rules with the same text and name are the same rule for change reporting.
    internal string Signature => _entry.Name + "\u0001" + _entry.Node.Raw.ToString();

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>One keyframe of a <see cref="KeyframesRule"/>, before the cascade of keyframes with the same offset (see <see cref="ResolvedKeyframes"/>).</summary>
public sealed class KeyframeBlock
{
    internal KeyframeBlock(double offset, string? timingFunction, string? composition, IReadOnlyList<KeyframeDeclaration> declarations)
    {
        Offset = offset;
        TimingFunction = timingFunction;
        Composition = composition;
        Declarations = declarations;
    }

    /// <summary>The offset from 0 to 1: <c>from</c> is 0, <c>to</c> is 1, a percentage is divided by 100.</summary>
    public double Offset { get; }

    /// <summary>The block's own <c>animation-timing-function</c> value as written (a list, possibly; a resolved keyframe uses its first item for every animation), or null when it has none or an invalid one.</summary>
    public string? TimingFunction { get; }

    /// <summary>The block's own <c>animation-composition</c> value as written, or null.</summary>
    public string? Composition { get; }

    /// <summary>The block's declarations, in source order.</summary>
    public IReadOnlyList<KeyframeDeclaration> Declarations { get; }
}

/// <summary>A declaration in a keyframe block.</summary>
/// <param name="Property">The longhand's name; the shorthand's name when <paramref name="UsesVar"/> is true and the declaration is a shorthand.</param>
/// <param name="Value">The value as written, or a CSS-wide keyword.</param>
/// <param name="UsesVar">True when the value contains <c>var()</c> and is substituted per element.</param>
public readonly record struct KeyframeDeclaration(string Property, string Value, bool UsesVar);

/// <summary>
/// The keyframes of one animation of one element: the rule's blocks cascaded and ordered the way Chrome's <c>getKeyframes()</c> reports them
/// (CSS Animations 2 §3.2), with <c>var()</c> substituted against the element's custom properties. The library runs no clock and interpolates nothing.
/// </summary>
public sealed class ResolvedKeyframes
{
    internal ResolvedKeyframes(string name, string timingFunction, string composition, IReadOnlyList<ResolvedKeyframe> keyframes, IReadOnlyList<string> animated)
    {
        Name = name;
        TimingFunction = timingFunction;
        Composition = composition;
        Keyframes = keyframes;
        AnimatedProperties = animated;
    }

    /// <summary>The animation name.</summary>
    public string Name { get; }

    /// <summary>The animation's own <c>animation-timing-function</c> item, which keyframes without one use: the item at the animation's index in the coordinated list (the list repeats), <c>ease</c> by default.</summary>
    public string TimingFunction { get; }

    /// <summary>The animation's own <c>animation-composition</c> item, which keyframes without one use; <c>replace</c> by default.</summary>
    public string Composition { get; }

    /// <summary>The keyframes in computed order: by offset, and in source order among equal offsets.</summary>
    public IReadOnlyList<ResolvedKeyframe> Keyframes { get; }

    /// <summary>The longhands (physical names) some keyframe sets, including one whose value was invalid at computed-value time, ascending.</summary>
    public IReadOnlyList<string> AnimatedProperties { get; }
}

/// <summary>One keyframe of a <see cref="ResolvedKeyframes"/>.</summary>
public sealed class ResolvedKeyframe
{
    internal ResolvedKeyframe(double offset, string timingFunction, string? composition, bool isImplicit, IReadOnlyList<KeyframeValue> values)
    {
        Offset = offset;
        TimingFunction = timingFunction;
        Composition = composition;
        IsImplicit = isImplicit;
        Values = values;
    }

    /// <summary>The offset from 0 to 1.</summary>
    public double Offset { get; }

    /// <summary>The timing function used from this keyframe to the next: the block's own, else the animation's.</summary>
    public string TimingFunction { get; }

    /// <summary>The composite operation this keyframe declares, or null for the animation's own (<see cref="ResolvedKeyframes.Composition"/>); <c>replace</c> on a keyframe the library added.</summary>
    public string? Composition { get; }

    /// <summary>True for a keyframe added at 0% or 100% to hold the element's own computed values of properties the other keyframes animate.</summary>
    public bool IsImplicit { get; }

    /// <summary>The values, by property name ascending (ordinal).</summary>
    public IReadOnlyList<KeyframeValue> Values { get; }
}

/// <summary>A longhand value in a resolved keyframe.</summary>
/// <param name="Property">The longhand's physical name; flow-relative properties are mapped under the element's writing mode and direction, so <c>margin-inline-start</c> becomes <c>margin-left</c> or <c>margin-right</c>.</param>
/// <param name="Value">The value as text with <c>var()</c> substituted: not converted to computed values (<c>1em</c>, <c>red</c> stay as written). A CSS-wide keyword stays a keyword. For an implicit keyframe it is the element's own value of the property.</param>
/// <param name="InvalidAtComputedValueTime">True when <c>var()</c> substitution failed or gave a value invalid for the property; <paramref name="Value"/> is then <c>unset</c> (CSS Variables 1 §3.2).</param>
public readonly record struct KeyframeValue(string Property, string Value, bool InvalidAtComputedValueTime = false);

internal sealed class KeyframeEntry
{
    public string Name = string.Empty;
    public bool Prefixed;
    public StyleOrigin Origin;
    public int Layer;
    public int Order;
    public CssNode Node;
    public Node Owner;
    public string? Url;
    public Stylesheet Sheet = null!;
    public DeclarationValidator? Validator;
}

internal sealed class KeyframeBlockData
{
    public double Offset;
    public string? TimingFunction;
    public string? Composition;
    public List<StyleValue> Values = [];

    public KeyframeBlock ToPublic()
    {
        var list = new List<KeyframeDeclaration>(Values.Count);
        foreach (StyleValue v in Values)
        {
            ParsedDeclaration d = v.Declaration!;
            if (v.Pending && d.ShorthandName is { } sh)
            {
                if (!list.Exists(x => x.Property == sh && x.UsesVar))
                {
                    list.Add(new KeyframeDeclaration(sh, d.FullValue.Trimmed().Raw.ToString(), true));
                }

                continue;
            }

            string name = PropertyIds.Info[v.PropertyId].Name;
            list.Add(new KeyframeDeclaration(name, v.Keyword != CssWideKeyword.None ? CssWideKeywords.ToCss(v.Keyword) : v.Text, v.Pending));
        }

        return new KeyframeBlock(Offset, TimingFunction, Composition, list);
    }
}

internal static class KeyframeParser
{
    // Properties that CSS Animations 1 §3 and Chrome leave out of keyframes. The animation-* and transition-* families are matched by prefix.
    private static readonly System.Collections.Frozen.FrozenSet<string> Denied = ValueKinds.Set(
        "direction", "unicode-bidi", "writing-mode", "text-orientation", "will-change", "contain", "container", "container-type", "container-name", "all");

    private static readonly int TimingId = PropertyIds.Get("animation-timing-function");
    private static readonly int CompositionId = PropertyIds.Get("animation-composition");

    public static bool IsAnimated(string name) =>
        !name.StartsWith("animation", StringComparison.Ordinal) && !name.StartsWith("transition", StringComparison.Ordinal) && !name.StartsWith("--", StringComparison.Ordinal) && !Denied.Contains(name);

    public static KeyframeBlockData[] Parse(RuleDatabase db, KeyframeEntry entry)
    {
        var blocks = new List<KeyframeBlockData>();
        foreach (CssNode child in entry.Node.Children)
        {
            if (child.Kind != CssNodeKind.QualifiedRule)
            {
                continue;
            }

            List<double>? offsets = Selectors(TokenRange.Of(child.Prelude).Trim());
            if (offsets is null)
            {
                continue;
            }

            var decls = new List<CssNode>();
            foreach (CssNode d in child.Children)
            {
                if (d.Kind == CssNodeKind.Declaration)
                {
                    decls.Add(d);
                }
            }

            var rule = new StyleRule
            {
                Id = -1,
                Kind = DeclarationKind.Sheet,
                Origin = entry.Origin,
                Sheet = entry.Sheet,
                Owner = entry.Owner,
                Url = entry.Url,
                RuleNode = child,
                DeclNodes = decls,
                Layer = entry.Layer,
                Layers = db.Layers,
                Validator = entry.Validator,
            };
            string? tf = null, comp = null;
            var values = new List<StyleValue>();
            foreach (ParsedDeclaration pd in rule.Parsed)
            {
                if (pd.Important || pd.Node.IsCustomProperty || pd.Node.NameEqualsIgnoreCase("all"))
                {
                    continue;
                }

                foreach (StyleValue v in pd.Values)
                {
                    if (v.PropertyId < 0)
                    {
                        continue;
                    }

                    if (pd.ShorthandName is null && (v.PropertyId == TimingId || v.PropertyId == CompositionId))
                    {
                        // The two animation properties keyframes accept. A var(), a CSS-wide keyword or an invalid value leaves the animation's own.
                        if (!v.Pending && v.Keyword == CssWideKeyword.None)
                        {
                            if (v.PropertyId == TimingId)
                            {
                                tf = v.Text;
                            }
                            else
                            {
                                comp = v.Text;
                            }
                        }

                        continue;
                    }

                    if (IsAnimated(PropertyIds.Info[v.PropertyId].Name))
                    {
                        values.Add(v);
                    }
                }
            }

            foreach (double o in offsets)
            {
                blocks.Add(new KeyframeBlockData { Offset = o, TimingFunction = tf, Composition = comp, Values = values });
            }
        }

        return [.. blocks];
    }

    // <keyframe-selector># = from | to | <percentage [0,100]>; one bad selector drops the block.
    private static List<double>? Selectors(TokenRange r)
    {
        if (r.IsEmpty)
        {
            return null;
        }

        var offsets = new List<double>();
        int start = r.From;
        for (int i = r.From; i <= r.To; i++)
        {
            if (i < r.To && r.Kind(i) != CssTokenKind.Comma)
            {
                i = r.Next(i) - 1;
                continue;
            }

            TokenRange one = r.Sub(start, i).Trim();
            if (one.IsEmpty || one.Next(one.From) != one.To)
            {
                return null;
            }

            if (one.IsIdent(one.From, "from"))
            {
                offsets.Add(0);
            }
            else if (one.IsIdent(one.From, "to"))
            {
                offsets.Add(1);
            }
            else if (one.Kind(one.From) == CssTokenKind.Percentage && one.Number(one.From) is >= 0 and <= 100 and double p)
            {
                offsets.Add(p / 100);
            }
            else
            {
                return null;
            }

            start = i + 1;
        }

        return offsets;
    }
}

internal static class KeyframeResolver
{
    private sealed class Block
    {
        public double Offset;
        public string? Tf;
        public string? Comp;
        public Dictionary<int, KeyframeValue> Values = [];
    }

    /// <summary>The names of the animations in <c>animation-name</c> (null for <c>none</c>), in list order.</summary>
    public static List<string?> AnimationNames(ComputedStyle style)
    {
        var names = new List<string?>();
        string text = style.GetPropertyValue("animation-name");
        if (string.IsNullOrWhiteSpace(text))
        {
            return names;
        }

        TokenRange r = TokenRange.Of(CssTree.ParseComponentValueList(text).Values).Trim();
        foreach (TokenRange item in AnimationGrammar.SplitCommas(r))
        {
            if (item.IsEmpty || item.Next(item.From) != item.To)
            {
                names.Add(null);
            }
            else if (item.Kind(item.From) == CssTokenKind.QuotedString)
            {
                names.Add(item.StringValue(item.From));
            }
            else if (item.Kind(item.From) == CssTokenKind.Ident && !item.IsIdent(item.From, "none"))
            {
                names.Add(item.IdentString(item.From));
            }
            else
            {
                names.Add(null);
            }
        }

        return names;
    }

    private static string ListItem(ComputedStyle style, string property, int index, string fallback)
    {
        string text = style.GetPropertyValue(property);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        List<TokenRange> items = AnimationGrammar.SplitCommas(TokenRange.Of(CssTree.ParseComponentValueList(text).Values).Trim());
        TokenRange item = items[index % items.Count];
        return item.IsEmpty ? fallback : item.RawText();
    }

    public static ResolvedKeyframes?[] ForStyle(RuleDatabase db, ComputedStyle style)
    {
        List<string?> names = AnimationNames(style);
        var result = new ResolvedKeyframes?[names.Count];
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i] is { } n)
            {
                result[i] = Resolve(db, style, n, i);
            }
        }

        return result;
    }

    private static string NormalizeText(string s) => string.Join(' ', s.AsciiLower().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static ResolvedKeyframes? Resolve(RuleDatabase db, ComputedStyle style, string name, int animationIndex)
    {
        if (!db.KeyframeWinners.TryGetValue(name, out KeyframesRule? rule))
        {
            return null;
        }

        string defaultTf = ListItem(style, "animation-timing-function", animationIndex, "ease");
        string defaultComp = ListItem(style, "animation-composition", animationIndex, "replace");
        string defaultKey = NormalizeText(defaultTf);
        int mode = LogicalMap.ModeIndex(style.GetPropertyValue("writing-mode").Trim().AsciiLower(), style.GetPropertyValue("direction").Trim().AsciiLower() == "rtl");

        KeyframeBlockData[] data = rule.Data;
        var expansions = new Dictionary<ParsedDeclaration, ShorthandExpansion?>();
        var blocks = new List<Block>(data.Length);
        foreach (KeyframeBlockData d in data)
        {
            var b = new Block
            {
                Offset = d.Offset,
                Tf = d.TimingFunction is null ? null : FirstItem(d.TimingFunction),
                Comp = d.Composition is null ? null : FirstItem(d.Composition).AsciiLower(),
            };
            foreach (StyleValue v in d.Values)
            {
                Apply(v, style, mode, b.Values, expansions);
            }

            blocks.Add(b);
        }

        // Chrome groups the blocks that share an offset, a timing function (the animation's counts when a block has none) and a declared composite into
        // one keyframe, later declarations winning, and places it where the group's last block stood. Animations 2 §3.2 groups by offset alone.
        var lastOf = new Dictionary<string, int>(StringComparer.Ordinal);
        string KeyOf(Block b) => b.Offset.ToString("R", CultureInfo.InvariantCulture) + "|" + NormalizeText(b.Tf ?? defaultTf) + "|" + (b.Comp ?? string.Empty);
        for (int i = 0; i < blocks.Count; i++)
        {
            lastOf[KeyOf(blocks[i])] = i;
        }

        var merged = new Dictionary<string, Block>(StringComparer.Ordinal);
        var frames = new List<(double Offset, Block B, string Key)>();
        for (int i = 0; i < blocks.Count; i++)
        {
            Block b = blocks[i];
            string key = KeyOf(b);
            if (!merged.TryGetValue(key, out Block? acc))
            {
                merged[key] = acc = new Block { Offset = b.Offset, Tf = b.Tf, Comp = b.Comp };
            }

            foreach (KeyValuePair<int, KeyframeValue> kv in b.Values)
            {
                acc.Values[kv.Key] = kv.Value;
            }

            if (lastOf[key] == i)
            {
                frames.Add((b.Offset, acc, key));
            }
        }

        // Stable by offset.
        var order = Enumerable.Range(0, frames.Count).OrderBy(i => frames[i].Offset).ThenBy(i => i).ToList();
        var sorted = order.Select(i => frames[i]).ToList();

        var animated = new SortedSet<int>();
        foreach ((double _, Block b, string _) in sorted)
        {
            animated.UnionWith(b.Values.Keys);
        }

        // Initial and final keyframes (Animations 2 §3.2): the element's own values of properties the first or last keyframe leaves out.
        var implicitFrames = new List<(double Offset, Block B, string Key, bool Implicit)>(sorted.Select(f => (f.Offset, f.B, f.Key, false)));
        Fill(0, atEnd: false);
        Fill(1, atEnd: true);

        var outFrames = new List<ResolvedKeyframe>(implicitFrames.Count);
        foreach ((double offset, Block b, string _, bool isImplicit) in implicitFrames)
        {
            var values = b.Values.Values.ToList();
            values.Sort((x, y) => string.CompareOrdinal(x.Property, y.Property));
            outFrames.Add(new ResolvedKeyframe(offset, b.Tf ?? defaultTf, isImplicit ? "replace" : b.Comp, isImplicit, values));
        }

        var animatedNames = animated.Select(id => PropertyIds.Info[id].Name).ToList();
        animatedNames.Sort(StringComparer.Ordinal);
        return new ResolvedKeyframes(name, defaultTf, defaultComp, outFrames, animatedNames);

        void Fill(double offset, bool atEnd)
        {
            var missing = new List<int>();
            foreach (int id in animated)
            {
                bool present = false;
                foreach ((double o, Block b, string _, bool _) in implicitFrames)
                {
                    if (o == offset && b.Values.ContainsKey(id))
                    {
                        present = true;
                        break;
                    }
                }

                if (!present)
                {
                    missing.Add(id);
                }
            }

            if (missing.Count == 0)
            {
                return;
            }

            string key = offset.ToString("R", CultureInfo.InvariantCulture) + "|" + defaultKey + "|";
            int at = implicitFrames.FindIndex(f => f.Offset == offset && f.Key == key);
            Block target;
            if (at >= 0)
            {
                target = implicitFrames[at].B;
            }
            else
            {
                target = new Block { Offset = offset };
                int pos = atEnd ? implicitFrames.Count : implicitFrames.FindLastIndex(f => f.Offset <= offset) + 1;
                implicitFrames.Insert(pos, (offset, target, key, true));
            }

            foreach (int id in missing)
            {
                string pname = PropertyIds.Info[id].Name;
                string own = style.GetComputedValue(pname);
                if (!string.IsNullOrWhiteSpace(own))
                {
                    target.Values[id] = new KeyframeValue(pname, own.Trim());
                }
            }
        }
    }

    // A list in a keyframe is not coordinated with the animations: Chrome takes its first item for every animation.
    private static string FirstItem(string text) => AnimationGrammar.SplitCommas(TokenRange.Of(CssTree.ParseComponentValueList(text).Values).Trim())[0].RawText();

    private static int Slot(int id, int mode)
    {
        int phys = LogicalMap.Physical(id, mode);
        return phys >= 0 ? phys : id;
    }

    private static void Apply(StyleValue v, ComputedStyle style, int mode, Dictionary<int, KeyframeValue> into, Dictionary<ParsedDeclaration, ShorthandExpansion?> expansions)
    {
        ParsedDeclaration d = v.Declaration!;
        string name = PropertyIds.Info[v.PropertyId].Name;
        int slot = Slot(v.PropertyId, mode);
        string slotName = PropertyIds.Info[slot].Name;
        if (!v.Pending)
        {
            into[slot] = new KeyframeValue(slotName, v.Keyword != CssWideKeyword.None ? CssWideKeywords.ToCss(v.Keyword) : v.Text);
            return;
        }

        // CSS Variables 1 §3.2: substitution happens when the element is known; a value that fails is invalid at computed-value time and behaves as unset.
        var unset = new KeyframeValue(slotName, "unset", true);
        if (d.ShorthandName is { } shorthand)
        {
            if (!expansions.TryGetValue(d, out ShorthandExpansion? ex))
            {
                SubstitutionResult sub = VarSubstitution.Substitute(d.FullValue, style.CustomProperties);
                ex = sub.IsValid && !VarSubstitution.ContainsVar(sub.Value) ? Shorthands.Expand(shorthand, sub.Value) : null;
                if (ex is not null && ex.Status != ShorthandStatus.Expanded)
                {
                    ex = null;
                }

                expansions[d] = ex;
            }

            if (ex is null)
            {
                into[slot] = unset;
                return;
            }

            foreach (ExpandedLonghand l in ex.Longhands)
            {
                if (PropertyIds.Get(l.Name) == v.PropertyId)
                {
                    into[slot] = new KeyframeValue(slotName, l.Keyword != CssWideKeyword.None ? CssWideKeywords.ToCss(l.Keyword) : l.Value.Trimmed().Raw.ToString());
                    return;
                }
            }

            into[slot] = unset;
            return;
        }

        SubstitutionResult r = VarSubstitution.Substitute(v.Tokens, style.CustomProperties);
        if (!r.IsValid)
        {
            into[slot] = unset;
            return;
        }

        ComponentValueList value = r.Value;
        CssWideKeyword kw = CssWideKeywords.Detect(value);
        if (kw != CssWideKeyword.None)
        {
            into[slot] = new KeyframeValue(slotName, CssWideKeywords.ToCss(kw));
            return;
        }

        if (!ParsedDeclaration.IsValidLonghand(name, value))
        {
            into[slot] = unset;
            return;
        }

        into[slot] = new KeyframeValue(slotName, r.Text.Trim());
    }
}
