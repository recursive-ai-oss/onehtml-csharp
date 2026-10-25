using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RecursiveAI.OneHtml.Css;

/// <summary>What <see cref="Shorthands.Expand"/> found.</summary>
public enum ShorthandStatus : byte
{
    /// <summary>The property is not a shorthand (it has no longhands in the property table).</summary>
    NotShorthand,

    /// <summary>The property is a shorthand this library has no grammar for; the cascade keeps its token list as is.</summary>
    NotExpanded,

    /// <summary>The value was split into longhands; see <see cref="ShorthandExpansion.Longhands"/>.</summary>
    Expanded,

    /// <summary>The value does not match the shorthand's grammar; the declaration is invalid and dropped.</summary>
    Invalid,

    /// <summary>The value contains <c>var()</c>. Substitute first, then expand; <see cref="ShorthandExpansion.Longhands"/> names the longhands that will be set.</summary>
    PendingSubstitution,
}

/// <summary>One longhand set by a shorthand: a value, or a CSS-wide keyword (<c>initial</c> for omitted components and reset-only longhands).</summary>
public readonly struct ExpandedLonghand
{
    internal ExpandedLonghand(string name, CssWideKeyword keyword, ComponentValueList value)
    {
        Name = name;
        Keyword = keyword;
        Value = value;
    }

    /// <summary>The longhand's property name.</summary>
    public string Name { get; }

    /// <summary>A CSS-wide keyword when the shorthand sets this longhand to <c>initial</c> (or whatever the shorthand was given); <see cref="CssWideKeyword.None"/> when <see cref="Value"/> applies.</summary>
    public CssWideKeyword Keyword { get; }

    /// <summary>The longhand's value as component values (a slice of the shorthand's value where one exists, otherwise synthesized from it).</summary>
    public ComponentValueList Value { get; }

    /// <inheritdoc />
    public override string ToString() => Keyword != CssWideKeyword.None ? $"{Name}: {CssWideKeywords.ToCss(Keyword)}" : $"{Name}: {Value.Raw}";
}

/// <summary>The result of expanding a shorthand.</summary>
public sealed class ShorthandExpansion
{
    internal ShorthandExpansion(ShorthandStatus status, List<ExpandedLonghand>? longhands = null)
    {
        Status = status;
        Longhands = longhands ?? [];
    }

    /// <summary>The outcome.</summary>
    public ShorthandStatus Status { get; }

    /// <summary>The leaf longhands in the order of the property table (nested shorthands such as <c>border-width</c> are expanded down to <c>border-top-width</c> and so on, reset-only longhands included).</summary>
    public IReadOnlyList<ExpandedLonghand> Longhands { get; }
}

/// <summary>
/// Shorthand expansion (CSS Cascade 5 §3.1 and the shorthands' own grammars). Real grammars exist for the
/// box-model shorthands (<c>margin</c>, <c>padding</c>, <c>inset</c>, <c>border-*</c>, <c>overflow</c>, <c>gap</c>), <c>outline</c>,
/// <c>text-decoration</c>, <c>list-style</c>, <c>font</c>, <c>background</c> (and <c>background-position</c>), <c>transition</c> and
/// <c>animation</c>. Every other shorthand reports <see cref="ShorthandStatus.NotExpanded"/>.
/// </summary>
public static partial class Shorthands
{
    [GeneratedRegex(@"\{1,([24])\}$", RegexOptions.CultureInvariant)]
    private static partial Regex SidesSyntaxRegex();
    private static readonly FrozenSet<string> PairShorthands = ValueKinds.Set("gap", "grid-gap");

    /// <summary>True when <see cref="Expand"/> can split this shorthand (it has a grammar here).</summary>
    /// <param name="name">A property name.</param>
    public static bool HasGrammar(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return PropertyTable.TryGet(name, out PropertyInfo? p) && p.IsShorthand && Classify(p) != Kind.None;
    }

    /// <summary>
    /// Splits a shorthand declaration's value into longhands. CSS-wide keywords apply to every longhand; a value
    /// containing <c>var()</c> is <see cref="ShorthandStatus.PendingSubstitution"/>.
    /// </summary>
    /// <param name="name">The shorthand's property name (any ASCII case).</param>
    /// <param name="value">The declaration's value.</param>
    public static ShorthandExpansion Expand(string name, ComponentValueList value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!PropertyTable.TryGet(name, out PropertyInfo? info) || !info.IsShorthand)
        {
            return new ShorthandExpansion(ShorthandStatus.NotShorthand);
        }

        info = PropertyTable.ResolveAlias(info);
        CssWideKeyword kw = CssWideKeywords.Detect(value);
        if (kw != CssWideKeyword.None)
        {
            var all = new List<ExpandedLonghand>();
            Leaves(info, kw, all, 0);
            return new ShorthandExpansion(ShorthandStatus.Expanded, all);
        }

        if (VarSubstitution.ContainsVar(value))
        {
            var pending = new List<ExpandedLonghand>();
            Leaves(info, CssWideKeyword.None, pending, 0);
            return new ShorthandExpansion(ShorthandStatus.PendingSubstitution, pending);
        }

        Kind kind = Classify(info);
        if (kind == Kind.None)
        {
            return new ShorthandExpansion(ShorthandStatus.NotExpanded);
        }

        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty)
        {
            return new ShorthandExpansion(ShorthandStatus.Invalid);
        }

        var slots = new Dictionary<string, Slot>(StringComparer.Ordinal);
        bool ok = kind switch
        {
            Kind.Sides => Sides(r, info, slots),
            Kind.LineLike => LineLike(r, info, slots),
            Kind.TextDecoration => TextDecoration(r, info, slots),
            Kind.ListStyle => ListStyle(r, info, slots),
            Kind.Font => Font(r, info, slots),
            Kind.Background => Background(r, info, slots),
            Kind.BackgroundPosition => BackgroundPosition(r, slots),
            Kind.Transition => Transition(r, slots),
            Kind.Animation => Animation(r, slots),
            _ => false,
        };
        if (!ok)
        {
            return new ShorthandExpansion(ShorthandStatus.Invalid);
        }

        var result = new List<ExpandedLonghand>();
        foreach (string l in info.Longhands)
        {
            AddLeaf(l, slots.TryGetValue(l, out Slot s) ? s : default, r.Tree, result, 0);
        }

        foreach (string l in info.ResetLonghands)
        {
            AddLeaf(l, default, r.Tree, result, 0);
        }

        // Slots for parts below a nested shorthand (background-position-x, font-variant-caps) replace the reset that the nested shorthand produced.
        foreach (KeyValuePair<string, Slot> kv in slots)
        {
            if (Array.IndexOf(info.Longhands, kv.Key) >= 0)
            {
                continue;
            }

            result.RemoveAll(e => e.Name == kv.Key);
            result.Add(new ExpandedLonghand(kv.Key, CssWideKeyword.None, SynthSlot(kv.Value, r.Tree)));
        }

        return new ShorthandExpansion(ShorthandStatus.Expanded, result);
    }

    private enum Kind : byte
    {
        None,
        Sides,
        LineLike,
        TextDecoration,
        ListStyle,
        Font,
        Background,
        BackgroundPosition,
        Transition,
        Animation,
    }

    private struct Slot
    {
        public int From;
        public int To;
        public string? Text;

        public readonly bool IsSet => Text is not null || To > From;
    }

    private static Kind Classify(PropertyInfo info)
    {
        switch (info.Name)
        {
            case "font": return Kind.Font;
            case "background": return Kind.Background;
            case "background-position": return Kind.BackgroundPosition;
            case "transition": return Kind.Transition;
            case "animation": return Kind.Animation;
            case "text-decoration": return Kind.TextDecoration;
            case "list-style": return Kind.ListStyle;
        }

        string[] l = info.Longhands;
        if (PairShorthands.Contains(info.Name) && l.Length == 2)
        {
            return Kind.Sides;
        }

        if (l.Length == 3 && l[0].EndsWith("-width", StringComparison.Ordinal) && l[1].EndsWith("-style", StringComparison.Ordinal) && l[2].EndsWith("-color", StringComparison.Ordinal))
        {
            return Kind.LineLike;
        }

        if (info.Syntax is { } syntax && SidesSyntaxRegex().Match(syntax) is { Success: true } m && int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) == l.Length)
        {
            return Kind.Sides;
        }

        return Kind.None;
    }

    // --- output assembly ------------------------------------------------------------------------------------------

    private static ComponentValueList SynthSlot(Slot s, CssTree tree) =>
        s.Text is not null ? CssTree.ParseComponentValueList(s.Text).Values.Trimmed() : new ComponentValueList(tree, s.From, s.To).Trimmed();

    // Adds a longhand; a nested shorthand is expanded down to its own leaves (border-width -> border-top-width ...).
    private static void AddLeaf(string name, Slot s, CssTree tree, List<ExpandedLonghand> output, int depth)
    {
        bool isShorthand = PropertyTable.TryGet(name, out PropertyInfo? p) && p.IsShorthand && depth < 4;
        if (!s.IsSet)
        {
            if (isShorthand)
            {
                Leaves(p!, CssWideKeyword.Initial, output, depth + 1);
            }
            else
            {
                output.Add(new ExpandedLonghand(name, CssWideKeyword.Initial, default));
            }

            return;
        }

        ComponentValueList v = SynthSlot(s, tree);
        if (isShorthand)
        {
            ShorthandExpansion nested = Expand(name, v);
            if (nested.Status == ShorthandStatus.Expanded)
            {
                output.AddRange(nested.Longhands);
                return;
            }
        }

        output.Add(new ExpandedLonghand(name, CssWideKeyword.None, v));
    }

    private static void Leaves(PropertyInfo info, CssWideKeyword keyword, List<ExpandedLonghand> output, int depth)
    {
        foreach (string l in info.Longhands.Concat2(info.ResetLonghands))
        {
            if (depth < 4 && PropertyTable.TryGet(l, out PropertyInfo? p) && p.IsShorthand)
            {
                Leaves(p, keyword, output, depth + 1);
            }
            else
            {
                output.Add(new ExpandedLonghand(l, keyword, default));
            }
        }
    }

    private static IEnumerable<string> Concat2(this string[] a, string[] b)
    {
        foreach (string s in a)
        {
            yield return s;
        }

        foreach (string s in b)
        {
            yield return s;
        }
    }

    // --- splitting helpers -----------------------------------------------------------------------------------------

    private static List<(int From, int To)> Components(TokenRange r)
    {
        var list = new List<(int, int)>();
        int i = r.SkipWs(r.From);
        while (i < r.To)
        {
            int next = r.Next(i);
            list.Add((i, next));
            i = r.SkipWs(next);
        }

        return list;
    }

    private static List<TokenRange> SplitCommas(TokenRange r)
    {
        var parts = new List<TokenRange>();
        int start = r.From;
        int i = r.From;
        while (i < r.To)
        {
            if (r.Kind(i) == CssTokenKind.Comma)
            {
                parts.Add(r.Sub(start, i).Trim());
                start = i + 1;
                i++;
            }
            else
            {
                i = r.Next(i);
            }
        }

        parts.Add(r.Sub(start, r.To).Trim());
        return parts;
    }

    // --- margin, padding, inset, border-width/style/color, overflow, gap ---------------------------------------------

    private static bool Sides(TokenRange r, PropertyInfo info, Dictionary<string, Slot> slots)
    {
        List<(int From, int To)> c = Components(r);
        int n = info.Longhands.Length;
        if (c.Count is < 1 || c.Count > n)
        {
            return false;
        }

        string name = info.Name;
        foreach ((int from, int to) in c)
        {
            int end = Validate(name, r, from);
            if (end != to)
            {
                return false;
            }
        }

        int[] map = n == 4
            ? c.Count switch { 1 => [0, 0, 0, 0], 2 => [0, 1, 0, 1], 3 => [0, 1, 2, 1], _ => [0, 1, 2, 3] }
            : c.Count == 1 ? [0, 0] : [0, 1];
        for (int k = 0; k < n; k++)
        {
            slots[info.Longhands[k]] = new Slot { From = c[map[k]].From, To = c[map[k]].To };
        }

        return true;
    }

    // The value check for each component of a "sides" shorthand; accepts anything for properties without a rule.
    private static int Validate(string shorthand, TokenRange r, int i)
    {
        int end = r.Next(i);
        if (r.Kind(i) == CssTokenKind.Comma || r.IsDelim(i, '/'))
        {
            return -1;
        }

        if (shorthand.StartsWith("margin", StringComparison.Ordinal) || shorthand.StartsWith("inset", StringComparison.Ordinal) || shorthand.StartsWith("scroll-padding", StringComparison.Ordinal))
        {
            return ValueKinds.LengthPercentage(r, i, allowAuto: true);
        }

        if (shorthand.StartsWith("padding", StringComparison.Ordinal))
        {
            return ValueKinds.LengthPercentage(r, i, nonNegative: true);
        }

        if (shorthand.StartsWith("scroll-margin", StringComparison.Ordinal))
        {
            return ValueKinds.Length(r, i);
        }

        if (shorthand.EndsWith("-width", StringComparison.Ordinal) && shorthand.StartsWith("border", StringComparison.Ordinal))
        {
            return ValueKinds.LineWidth(r, i);
        }

        if (shorthand.EndsWith("-style", StringComparison.Ordinal) && shorthand.StartsWith("border", StringComparison.Ordinal))
        {
            return ValueKinds.LineStyle(r, i);
        }

        if (shorthand.EndsWith("-color", StringComparison.Ordinal) && shorthand.StartsWith("border", StringComparison.Ordinal))
        {
            return ValueKinds.Color(r, i);
        }

        if (shorthand == "overflow")
        {
            return ValueKinds.Keyword(r, i, ValueKinds.OverflowKeywords);
        }

        if (shorthand is "gap" or "grid-gap")
        {
            return r.IsIdent(i, "normal") ? i + 1 : ValueKinds.LengthPercentage(r, i, nonNegative: true);
        }

        return end;
    }

    // --- border, border-top, outline, column-rule ... (width || style || color) -----------------------------------------------

    private static bool LineLike(TokenRange r, PropertyInfo info, Dictionary<string, Slot> slots)
    {
        bool outline = info.Name.StartsWith("outline", StringComparison.Ordinal);
        var parts = new (string Name, Func<TokenRange, int, int> Match)[]
        {
            (info.Longhands[0], ValueKinds.LineWidth),
            (info.Longhands[1], outline ? (rr, i) => rr.IsIdent(i, "auto") ? i + 1 : ValueKinds.LineStyle(rr, i) : ValueKinds.LineStyle),
            (info.Longhands[2], outline ? (rr, i) => rr.IsIdent(i, "invert") ? i + 1 : ValueKinds.Color(rr, i) : ValueKinds.Color),
        };
        return Unordered(r, parts, slots);
    }

    private static bool Unordered(TokenRange r, (string Name, Func<TokenRange, int, int> Match)[] parts, Dictionary<string, Slot> slots)
    {
        int i = r.SkipWs(r.From);
        while (i < r.To)
        {
            bool matched = false;
            foreach ((string name, Func<TokenRange, int, int> match) in parts)
            {
                if (slots.ContainsKey(name))
                {
                    continue;
                }

                int end = match(r, i);
                if (end > i)
                {
                    slots[name] = new Slot { From = i, To = end };
                    i = r.SkipWs(end);
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                return false;
            }
        }

        return slots.Count > 0;
    }

    // --- text-decoration -----------------------------------------------------------------------------------------------

    private static readonly FrozenSet<string> DecorationLines = ValueKinds.Set("underline", "overline", "line-through", "blink", "spelling-error", "grammar-error");
    private static readonly FrozenSet<string> DecorationStyles = ValueKinds.Set("solid", "double", "dotted", "dashed", "wavy");

    private static bool TextDecoration(TokenRange r, PropertyInfo info, Dictionary<string, Slot> slots)
    {
        string lineName = "text-decoration-line";
        string styleName = "text-decoration-style";
        string colorName = "text-decoration-color";
        string thickName = "text-decoration-thickness";
        var parts = new (string Name, Func<TokenRange, int, int> Match)[]
        {
            (lineName, (rr, i) =>
            {
                if (rr.IsIdent(i, "none"))
                {
                    return i + 1;
                }

                // One or more distinct line keywords, in any order, separated by whitespace.
                int end = i;
                int j = i;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (j < rr.To && rr.Kind(j) == CssTokenKind.Ident && ValueKinds.In(DecorationLines, rr.Name(j, out _)) && seen.Add(rr.IdentString(j)))
                {
                    end = j + 1;
                    j = rr.SkipWs(j + 1);
                }

                return end;
            }),
            (styleName, (rr, i) => ValueKinds.Keyword(rr, i, DecorationStyles)),
            (colorName, ValueKinds.Color),
            (thickName, (rr, i) => rr.IsIdent(i, "auto") || rr.IsIdent(i, "from-font") ? i + 1 : ValueKinds.LengthPercentage(rr, i)),
        };
        return Unordered(r, parts, slots);
    }

    // --- list-style ---------------------------------------------------------------------------------------------------------

    private static bool ListStyle(TokenRange r, PropertyInfo info, Dictionary<string, Slot> slots)
    {
        string position = "list-style-position";
        string image = "list-style-image";
        string type = "list-style-type";
        int noneCount = 0;
        int i = r.SkipWs(r.From);
        Slot noneSlot = default;
        while (i < r.To)
        {
            int next = r.Next(i);
            if (r.IsIdent(i, "none"))
            {
                noneCount++;
                noneSlot = new Slot { From = i, To = next };
            }
            else if ((r.IsIdent(i, "inside") || r.IsIdent(i, "outside")) && !slots.ContainsKey(position))
            {
                slots[position] = new Slot { From = i, To = next };
            }
            else if (ValueKinds.Image(r, i) is > 0 && !slots.ContainsKey(image))
            {
                slots[image] = new Slot { From = i, To = next };
            }
            else if ((r.Kind(i) == CssTokenKind.Ident || r.Kind(i) == CssTokenKind.QuotedString || r.IsFunction(i, "symbols")) && !slots.ContainsKey(type))
            {
                slots[type] = new Slot { From = i, To = next };
            }
            else
            {
                return false;
            }

            i = r.SkipWs(next);
        }

        if (noneCount > 2)
        {
            return false;
        }

        if (noneCount > 0)
        {
            // CSS Lists 3: a lone none sets whichever of image and type is still free (both when both are); two nones set both.
            bool imageFree = !slots.ContainsKey(image);
            bool typeFree = !slots.ContainsKey(type);
            int free = (imageFree ? 1 : 0) + (typeFree ? 1 : 0);
            if (noneCount > free || (noneCount == 2 && free != 2))
            {
                return false;
            }

            if (imageFree)
            {
                slots[image] = noneSlot;
            }

            if (typeFree)
            {
                slots[type] = noneSlot;
            }
        }

        return slots.Count > 0;
    }

    // --- font ----------------------------------------------------------------------------------------------------------------

    private static readonly FrozenSet<string> FontStyles = ValueKinds.Set("italic", "oblique");
    private static readonly FrozenSet<string> FontWeights = ValueKinds.Set("bold", "bolder", "lighter");
    private static readonly FrozenSet<string> FontStretches = ValueKinds.Set(
        "ultra-condensed", "extra-condensed", "condensed", "semi-condensed", "semi-expanded", "expanded", "extra-expanded", "ultra-expanded");
    private static readonly FrozenSet<string> FontSizes = ValueKinds.Set("xx-small", "x-small", "small", "medium", "large", "x-large", "xx-large", "xxx-large", "larger", "smaller", "math");
    private static readonly FrozenSet<string> SystemFonts = ValueKinds.Set("caption", "icon", "menu", "message-box", "small-caption", "status-bar");

    private static bool Font(TokenRange r, PropertyInfo info, Dictionary<string, Slot> slots)
    {
        int first = r.SkipWs(r.From);
        if (r.Kind(first) == CssTokenKind.Ident && ValueKinds.In(SystemFonts, r.Name(first, out _)) && r.SkipWs(first + 1) >= r.To)
        {
            return false; // system fonts set every longhand from the platform; not expandable here
        }

        string style = "font-style", variant = "font-variant", weight = "font-weight", stretch = info.Longhands.Length > 3 ? info.Longhands[3] : "font-stretch";
        int i = first;
        int normals = 0;
        while (i < r.To)
        {
            int next = r.Next(i);
            if (r.IsIdent(i, "normal"))
            {
                normals++;
                i = r.SkipWs(next);
                continue;
            }

            if (r.Kind(i) == CssTokenKind.Ident && ValueKinds.In(FontStyles, r.Name(i, out _)) && !slots.ContainsKey(style))
            {
                // oblique may take an angle
                int end = next;
                int j = r.SkipWs(next);
                if (r.IsIdent(i, "oblique") && j < r.To && CssQuantity.TryParse(r, j, QuantityKinds.Angle, null, out _, out int after))
                {
                    end = after;
                    next = after;
                }

                slots[style] = new Slot { From = i, To = end };
            }
            else if (r.IsIdent(i, "small-caps") && !slots.ContainsKey(variant))
            {
                slots[variant] = new Slot { From = i, To = next };
            }
            else if (!slots.ContainsKey(weight) && ((r.Kind(i) == CssTokenKind.Ident && ValueKinds.In(FontWeights, r.Name(i, out _))) || (r.Kind(i) == CssTokenKind.Number && r.Number(i) is >= 1 and <= 1000) || (r.Kind(i) == CssTokenKind.Function && CssQuantity.TryParse(r, i, QuantityKinds.Number, null, out _, out _))))
            {
                slots[weight] = new Slot { From = i, To = next };
            }
            else if (!slots.ContainsKey(stretch) && ((r.Kind(i) == CssTokenKind.Ident && ValueKinds.In(FontStretches, r.Name(i, out _))) || r.Kind(i) == CssTokenKind.Percentage))
            {
                slots[stretch] = new Slot { From = i, To = next };
            }
            else
            {
                break; // the size
            }

            i = r.SkipWs(next);
        }

        if (normals > 4 - slots.Count)
        {
            return false;
        }

        // font-size
        if (i >= r.To)
        {
            return false;
        }

        int sizeEnd = r.Kind(i) == CssTokenKind.Ident && ValueKinds.In(FontSizes, r.Name(i, out _)) ? i + 1 : ValueKinds.LengthPercentage(r, i, nonNegative: true);
        if (sizeEnd < 0)
        {
            return false;
        }

        slots["font-size"] = new Slot { From = i, To = sizeEnd };
        i = r.SkipWs(sizeEnd);
        if (ValueKinds.IsSlash(r, i))
        {
            i = r.SkipWs(i + 1);
            int lhEnd = r.IsIdent(i, "normal") ? i + 1 : -1;
            if (lhEnd < 0)
            {
                lhEnd = ValueKinds.Number(r, i);
            }

            if (lhEnd < 0)
            {
                lhEnd = ValueKinds.LengthPercentage(r, i, nonNegative: true);
            }

            if (lhEnd < 0)
            {
                return false;
            }

            slots["line-height"] = new Slot { From = i, To = lhEnd };
            i = r.SkipWs(lhEnd);
        }

        // font-family: a comma-separated list to the end; each family is a string or a run of identifiers.
        if (i >= r.To)
        {
            return false;
        }

        TokenRange fam = r.Sub(i, r.To).Trim();
        foreach (TokenRange part in SplitCommas(fam))
        {
            if (part.IsEmpty)
            {
                return false;
            }

            int j = part.From;
            if (part.Kind(j) == CssTokenKind.QuotedString)
            {
                if (part.SkipWs(j + 1) < part.To)
                {
                    return false;
                }

                continue;
            }

            while (j < part.To)
            {
                if (part.Kind(j) != CssTokenKind.Ident)
                {
                    return false;
                }

                j = part.SkipWs(j + 1);
            }
        }

        slots["font-family"] = new Slot { From = i, To = r.To };

        // font-variant: only "normal" and "small-caps" are possible here; expand it ourselves.
        if (slots.TryGetValue(variant, out Slot v))
        {
            slots.Remove(variant);
            slots["font-variant-caps"] = v;
        }

        return true;
    }

    // --- background ----------------------------------------------------------------------------------------------------------

    private static readonly FrozenSet<string> RepeatWords = ValueKinds.Set("repeat", "space", "round", "no-repeat");
    private static readonly FrozenSet<string> Attachments = ValueKinds.Set("scroll", "fixed", "local");
    private static readonly FrozenSet<string> Boxes = ValueKinds.Set("border-box", "padding-box", "content-box", "text");
    private static readonly FrozenSet<string> PositionWords = ValueKinds.Set("left", "right", "top", "bottom", "center");

    private static bool Background(TokenRange r, PropertyInfo info, Dictionary<string, Slot> slots)
    {
        List<TokenRange> layers = SplitCommas(r);
        string[] names = ["background-image", "background-position-x", "background-position-y", "background-size", "background-repeat", "background-attachment", "background-origin", "background-clip"];
        string[] defaults = ["none", "0%", "0%", "auto", "repeat", "scroll", "padding-box", "border-box"];
        var cells = new string?[names.Length][];
        for (int k = 0; k < names.Length; k++)
        {
            cells[k] = new string?[layers.Count];
        }

        Slot colorSlot = default;
        for (int li = 0; li < layers.Count; li++)
        {
            TokenRange lr = layers[li];
            if (lr.IsEmpty)
            {
                return false;
            }

            bool last = li == layers.Count - 1;
            string? image = null, size = null, rep = null, att = null, box1 = null, box2 = null, px = null, py = null;
            bool haveColor = false;
            int i = lr.From;
            while (i < lr.To)
            {
                int next = lr.Next(i);
                string compText = lr.Tree.Text.Substring(lr.Tree.Tokens[i].Start, lr.Tree.Tokens[next - 1].End - lr.Tree.Tokens[i].Start);
                if (image is null && ValueKinds.Image(lr, i) is > 0)
                {
                    image = compText;
                }
                else if (rep is null && lr.Kind(i) == CssTokenKind.Ident && (lr.IsIdent(i, "repeat-x") || lr.IsIdent(i, "repeat-y") || ValueKinds.In(RepeatWords, lr.Name(i, out _))))
                {
                    rep = compText;
                    int j = lr.SkipWs(next);
                    if (!lr.IsIdent(i, "repeat-x") && !lr.IsIdent(i, "repeat-y") && j < lr.To && lr.Kind(j) == CssTokenKind.Ident && ValueKinds.In(RepeatWords, lr.Name(j, out _)))
                    {
                        rep += " " + lr.IdentString(j);
                        next = j + 1;
                    }
                }
                else if (att is null && lr.Kind(i) == CssTokenKind.Ident && ValueKinds.In(Attachments, lr.Name(i, out _)))
                {
                    att = compText;
                }
                else if (lr.Kind(i) == CssTokenKind.Ident && ValueKinds.In(Boxes, lr.Name(i, out _)) && box2 is null)
                {
                    if (box1 is null)
                    {
                        box1 = compText;
                    }
                    else
                    {
                        box2 = compText;
                    }
                }
                else if (px is null && (PositionAt(lr, i)))
                {
                    var comps = new List<(int From, int To)>();
                    int j = i;
                    while (j < lr.To && comps.Count < 4 && PositionAt(lr, j))
                    {
                        int e = lr.Next(j);
                        comps.Add((j, e));
                        j = lr.SkipWs(e);
                    }

                    if (!PositionPair(lr, comps, out px, out py))
                    {
                        return false;
                    }

                    next = comps[^1].To;
                    int s = lr.SkipWs(next);
                    if (ValueKinds.IsSlash(lr, s))
                    {
                        s = lr.SkipWs(s + 1);
                        int se = SizeAt(lr, s);
                        if (se < 0)
                        {
                            return false;
                        }

                        size = lr.Tree.Text.Substring(lr.Tree.Tokens[s].Start, lr.Tree.Tokens[se - 1].End - lr.Tree.Tokens[s].Start);
                        next = se;
                    }
                }
                else if (last && !haveColor && ValueKinds.Color(lr, i) is > 0)
                {
                    haveColor = true;
                    colorSlot = new Slot { From = i, To = next };
                }
                else
                {
                    return false;
                }

                i = lr.SkipWs(next);
            }

            cells[0][li] = image;
            cells[1][li] = px;
            cells[2][li] = py;
            cells[3][li] = size;
            cells[4][li] = rep;
            cells[5][li] = att;
            cells[6][li] = box1;
            cells[7][li] = box2 ?? box1;
        }

        for (int k = 0; k < names.Length; k++)
        {
            bool any = false;
            foreach (string? c in cells[k])
            {
                any |= c is not null;
            }

            if (!any)
            {
                continue;
            }

            var sb = new StringBuilder();
            for (int li = 0; li < layers.Count; li++)
            {
                if (li > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(cells[k][li] ?? defaults[k]);
            }

            slots[names[k]] = new Slot { Text = sb.ToString() };
        }

        if (colorSlot.To > colorSlot.From)
        {
            slots["background-color"] = colorSlot;
        }

        return true;
    }

    private static bool PositionAt(TokenRange r, int i)
    {
        if (i >= r.To)
        {
            return false;
        }

        if (r.Kind(i) == CssTokenKind.Ident)
        {
            return ValueKinds.In(PositionWords, r.Name(i, out _));
        }

        return ValueKinds.LengthPercentage(r, i) > 0;
    }

    private static int SizeAt(TokenRange r, int i)
    {
        if (i >= r.To)
        {
            return -1;
        }

        if (r.IsIdent(i, "cover") || r.IsIdent(i, "contain"))
        {
            return i + 1;
        }

        int a = r.IsIdent(i, "auto") ? i + 1 : ValueKinds.LengthPercentage(r, i, nonNegative: true);
        if (a < 0)
        {
            return -1;
        }

        int j = r.SkipWs(a);
        int b = j < r.To ? (r.IsIdent(j, "auto") ? j + 1 : ValueKinds.LengthPercentage(r, j, nonNegative: true)) : -1;
        return b > 0 ? b : a;
    }

    // CSS Backgrounds 3 §3.6 <bg-position>: one to four components giving x and y, with edge keywords and offsets.
    private static bool PositionPair(TokenRange r, List<(int From, int To)> c, out string x, out string y)
    {
        x = y = "center";
        string T(int k) => r.Tree.Text.Substring(r.Tree.Tokens[c[k].From].Start, r.Tree.Tokens[c[k].To - 1].End - r.Tree.Tokens[c[k].From].Start);
        bool Kw(int k, string w) => r.IsIdent(c[k].From, w);
        bool IsLp(int k) => r.Kind(c[k].From) != CssTokenKind.Ident;
        bool IsX(int k) => Kw(k, "left") || Kw(k, "right");
        bool IsY(int k) => Kw(k, "top") || Kw(k, "bottom");
        switch (c.Count)
        {
            case 1:
                if (IsLp(0) || IsX(0))
                {
                    x = T(0);
                }
                else if (IsY(0))
                {
                    y = T(0);
                }

                return true;
            case 2:
            {
                bool CanX(int k) => IsX(k) || Kw(k, "center") || IsLp(k);
                bool CanY(int k) => IsY(k) || Kw(k, "center") || IsLp(k);
                if (IsY(0) || IsX(1))
                {
                    // Keyword order swapped: a vertical (or centre) keyword, then a horizontal (or centre) keyword.
                    if (!(IsY(0) || Kw(0, "center")) || !(IsX(1) || Kw(1, "center")))
                    {
                        return false;
                    }

                    y = T(0);
                    x = T(1);
                    return true;
                }

                if (!CanX(0) || !CanY(1))
                {
                    return false;
                }

                x = T(0);
                y = T(1);
                return true;
            }

            default:
            {
                // edge [offset] edge [offset]
                int k = 0;
                string? ex = null, ey = null;
                while (k < c.Count)
                {
                    if (r.Kind(c[k].From) != CssTokenKind.Ident)
                    {
                        return false;
                    }

                    string edge = T(k);
                    string full = edge;
                    int used = 1;
                    if (k + 1 < c.Count && IsLp(k + 1) && !Kw(k, "center"))
                    {
                        full = edge + " " + T(k + 1);
                        used = 2;
                    }

                    if (IsX(k))
                    {
                        if (ex is not null)
                        {
                            return false;
                        }

                        ex = full;
                    }
                    else if (IsY(k))
                    {
                        if (ey is not null)
                        {
                            return false;
                        }

                        ey = full;
                    }
                    else
                    {
                        // center takes the axis not yet filled
                        if (ex is null)
                        {
                            ex = "center";
                        }
                        else if (ey is null)
                        {
                            ey = "center";
                        }
                        else
                        {
                            return false;
                        }
                    }

                    k += used;
                }

                if (ex is null || ey is null)
                {
                    return false;
                }

                x = ex;
                y = ey;
                return true;
            }
        }
    }

    private static bool BackgroundPosition(TokenRange r, Dictionary<string, Slot> slots)
    {
        var xs = new List<string>();
        var ys = new List<string>();
        foreach (TokenRange part in SplitCommas(r))
        {
            var comps = new List<(int From, int To)>();
            int j = part.From;
            while (j < part.To)
            {
                if (!PositionAt(part, j) || comps.Count >= 4)
                {
                    return false;
                }

                int e = part.Next(j);
                comps.Add((j, e));
                j = part.SkipWs(e);
            }

            if (comps.Count == 0 || !PositionPair(part, comps, out string x, out string y))
            {
                return false;
            }

            xs.Add(x);
            ys.Add(y);
        }

        slots["background-position-x"] = new Slot { Text = string.Join(", ", xs) };
        slots["background-position-y"] = new Slot { Text = string.Join(", ", ys) };
        return true;
    }

    // --- transition and animation --------------------------------------------------------------------------------------------------

    private static readonly FrozenSet<string> Easings = ValueKinds.Set("ease", "linear", "ease-in", "ease-out", "ease-in-out", "step-start", "step-end");
    private static readonly FrozenSet<string> EasingFunctions = ValueKinds.Set("cubic-bezier", "steps", "linear");
    private static readonly FrozenSet<string> Directions = ValueKinds.Set("normal", "reverse", "alternate", "alternate-reverse");
    private static readonly FrozenSet<string> FillModes = ValueKinds.Set("none", "forwards", "backwards", "both");
    private static readonly FrozenSet<string> PlayStates = ValueKinds.Set("running", "paused");

    private static bool IsEasing(TokenRange r, int i) =>
        r.Kind(i) == CssTokenKind.Ident ? ValueKinds.In(Easings, r.Name(i, out _)) : r.Kind(i) == CssTokenKind.Function && ValueKinds.In(EasingFunctions, r.Name(i, out _));

    private static bool Transition(TokenRange r, Dictionary<string, Slot> slots)
    {
        string[] names = ["transition-property", "transition-duration", "transition-timing-function", "transition-delay", "transition-behavior"];
        string[] defaults = ["all", "0s", "ease", "0s", "normal"];
        var layers = SplitCommas(r);
        var cells = new string?[5][];
        for (int k = 0; k < 5; k++)
        {
            cells[k] = new string?[layers.Count];
        }

        for (int li = 0; li < layers.Count; li++)
        {
            TokenRange lr = layers[li];
            if (lr.IsEmpty)
            {
                return false;
            }

            int times = 0;
            int i = lr.From;
            while (i < lr.To)
            {
                int next = lr.Next(i);
                string t = lr.Tree.Text.Substring(lr.Tree.Tokens[i].Start, lr.Tree.Tokens[next - 1].End - lr.Tree.Tokens[i].Start);
                if (ValueKinds.Time(lr, i) > 0 && times < 2)
                {
                    cells[times == 0 ? 1 : 3][li] = t;
                    times++;
                }
                else if (cells[2][li] is null && IsEasing(lr, i))
                {
                    cells[2][li] = t;
                }
                else if (cells[4][li] is null && (lr.IsIdent(i, "normal") || lr.IsIdent(i, "allow-discrete")))
                {
                    cells[4][li] = t;
                }
                else if (cells[0][li] is null && lr.Kind(i) == CssTokenKind.Ident)
                {
                    cells[0][li] = t;
                }
                else
                {
                    return false;
                }

                i = lr.SkipWs(next);
            }
        }

        EmitLayers(names, defaults, cells, layers.Count, slots);
        return true;
    }

    private static bool Animation(TokenRange r, Dictionary<string, Slot> slots)
    {
        string[] names = ["animation-duration", "animation-timing-function", "animation-delay", "animation-iteration-count", "animation-direction", "animation-fill-mode", "animation-play-state", "animation-name"];
        string[] defaults = ["0s", "ease", "0s", "1", "normal", "none", "running", "none"];
        var layers = SplitCommas(r);
        var cells = new string?[8][];
        for (int k = 0; k < 8; k++)
        {
            cells[k] = new string?[layers.Count];
        }

        for (int li = 0; li < layers.Count; li++)
        {
            TokenRange lr = layers[li];
            if (lr.IsEmpty)
            {
                return false;
            }

            int times = 0;
            int i = lr.From;
            while (i < lr.To)
            {
                int next = lr.Next(i);
                string t = lr.Tree.Text.Substring(lr.Tree.Tokens[i].Start, lr.Tree.Tokens[next - 1].End - lr.Tree.Tokens[i].Start);
                bool isIdent = lr.Kind(i) == CssTokenKind.Ident;
                if (ValueKinds.Time(lr, i) > 0 && times < 2)
                {
                    cells[times == 0 ? 0 : 2][li] = t;
                    times++;
                }
                else if (cells[1][li] is null && IsEasing(lr, i))
                {
                    cells[1][li] = t;
                }
                else if (cells[3][li] is null && (lr.IsIdent(i, "infinite") || ValueKinds.Number(lr, i) > 0))
                {
                    cells[3][li] = t;
                }
                else if (cells[4][li] is null && isIdent && ValueKinds.In(Directions, lr.Name(i, out _)))
                {
                    cells[4][li] = t;
                }
                else if (cells[5][li] is null && isIdent && ValueKinds.In(FillModes, lr.Name(i, out _)))
                {
                    cells[5][li] = t;
                }
                else if (cells[6][li] is null && isIdent && ValueKinds.In(PlayStates, lr.Name(i, out _)))
                {
                    cells[6][li] = t;
                }
                else if (cells[7][li] is null && (isIdent || lr.Kind(i) == CssTokenKind.QuotedString))
                {
                    cells[7][li] = t;
                }
                else
                {
                    return false;
                }

                i = lr.SkipWs(next);
            }
        }

        EmitLayers(names, defaults, cells, layers.Count, slots);
        return true;
    }

    private static void EmitLayers(string[] names, string[] defaults, string?[][] cells, int layers, Dictionary<string, Slot> slots)
    {
        for (int k = 0; k < names.Length; k++)
        {
            // A longhand no layer mentions is simply reset to its initial value (the caller adds that).
            bool any = false;
            foreach (string? c in cells[k])
            {
                any |= c is not null;
            }

            if (!any && layers == 1)
            {
                // The only layer relies on the initial value, which is also what a CSS-wide reset gives.
                slots[names[k]] = new Slot { Text = defaults[k] };
                continue;
            }

            var parts = new string[layers];
            for (int li = 0; li < layers; li++)
            {
                parts[li] = cells[k][li] ?? defaults[k];
            }

            slots[names[k]] = new Slot { Text = string.Join(", ", parts) };
        }
    }
}
