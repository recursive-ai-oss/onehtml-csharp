using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Css;

/// <summary>
/// The value grammars of the <c>transition-*</c> and <c>animation-*</c> longhands and the pieces the two shorthands share: easing
/// functions, times, keyframes names and the rule that a CSS-wide keyword is not allowed as an item of a list (CSS Cascade 5 §7.3).
/// </summary>
internal static class AnimationGrammar
{
    private static readonly FrozenSet<string> EasingKeywords = ValueKinds.Set("linear", "ease", "ease-in", "ease-out", "ease-in-out", "step-start", "step-end");
    private static readonly FrozenSet<string> StepPositions = ValueKinds.Set("jump-start", "jump-end", "jump-none", "jump-both", "start", "end");
    private static readonly FrozenSet<string> Directions = ValueKinds.Set("normal", "reverse", "alternate", "alternate-reverse");
    private static readonly FrozenSet<string> FillModes = ValueKinds.Set("none", "forwards", "backwards", "both");
    private static readonly FrozenSet<string> PlayStates = ValueKinds.Set("running", "paused");
    private static readonly FrozenSet<string> Compositions = ValueKinds.Set("replace", "add", "accumulate");
    private static readonly FrozenSet<string> Behaviors = ValueKinds.Set("normal", "allow-discrete");
    private static readonly FrozenSet<string> Scrollers = ValueKinds.Set("root", "nearest", "self");
    private static readonly FrozenSet<string> Axes = ValueKinds.Set("block", "inline", "x", "y");

    /// <summary>The longhands this class validates (animation-delay is a shorthand of <c>-start</c> and <c>-end</c> in the property table, but one list of times in browsers).</summary>
    private static readonly FrozenSet<string> Longhands = ValueKinds.Set(
        "transition-property", "transition-duration", "transition-delay", "transition-timing-function", "transition-behavior",
        "animation-name", "animation-duration", "animation-delay", "animation-timing-function", "animation-iteration-count",
        "animation-direction", "animation-fill-mode", "animation-play-state", "animation-timeline", "animation-composition");

    public static bool Handles(string name) => Longhands.Contains(name);

    /// <summary>Whether the token at <paramref name="i"/> is a CSS-wide keyword.</summary>
    public static bool IsCssWide(TokenRange r, int i) => i < r.To && r.Kind(i) == CssTokenKind.Ident && CssWideKeywords.FromName(r.Name(i, out _)) != CssWideKeyword.None;

    /// <summary>
    /// A <c>&lt;custom-ident&gt;</c> (CSS Values 4 §5.1): an identifier that is not a CSS-wide keyword and not <c>default</c>. The caller
    /// excludes the other keywords its property reserves.
    /// </summary>
    public static bool IsCustomIdent(TokenRange r, int i) =>
        i < r.To && r.Kind(i) == CssTokenKind.Ident && !IsCssWide(r, i) && !r.IsIdent(i, "default");

    /// <summary>A <c>&lt;keyframes-name&gt;</c> or <c>none</c> (CSS Animations 1 §3.2): a custom identifier, <c>none</c>, or a non-empty string.</summary>
    public static bool IsAnimationName(TokenRange r, int i)
    {
        if (i >= r.To)
        {
            return false;
        }

        if (r.Kind(i) == CssTokenKind.QuotedString)
        {
            return r.StringValue(i).Length > 0;
        }

        return IsCustomIdent(r, i);
    }

    /// <summary>
    /// A <c>&lt;time&gt;</c> at <paramref name="i"/>: the index after it, or -1. <paramref name="negative"/> is true for a literal
    /// below zero, which cannot be a duration.
    /// </summary>
    public static int Time(TokenRange r, int i, out bool negative)
    {
        negative = false;
        if (i < r.To && CssQuantity.TryParse(r, i, QuantityKinds.Time, null, out CssQuantity q, out int next))
        {
            negative = q.Calc is null && q.Value < 0;
            return next;
        }

        return -1;
    }

    /// <summary>An <c>&lt;easing-function&gt;</c> (CSS Easing 2 §2): the index after it, or -1. Function arguments are checked.</summary>
    public static int Easing(TokenRange r, int i)
    {
        if (i >= r.To)
        {
            return -1;
        }

        if (r.Kind(i) == CssTokenKind.Ident)
        {
            return ValueKinds.In(EasingKeywords, r.Name(i, out _)) ? i + 1 : -1;
        }

        if (r.Kind(i) != CssTokenKind.Function)
        {
            return -1;
        }

        ReadOnlySpan<char> name = r.Name(i, out _);
        TokenRange inner = r.Inner(i);
        bool ok;
        if (name.Equals("cubic-bezier", StringComparison.OrdinalIgnoreCase))
        {
            ok = CubicBezier(inner);
        }
        else if (name.Equals("steps", StringComparison.OrdinalIgnoreCase))
        {
            ok = Steps(inner);
        }
        else if (name.Equals("linear", StringComparison.OrdinalIgnoreCase))
        {
            ok = LinearStops(inner);
        }
        else
        {
            ok = false;
        }

        return ok ? r.Next(i) : -1;
    }

    // CSS Easing 2 §2.2: cubic-bezier(<number [0,1]>, <number>, <number [0,1]>, <number>).
    private static bool CubicBezier(TokenRange inner)
    {
        List<TokenRange> args = SplitCommas(inner);
        if (args.Count != 4)
        {
            return false;
        }

        for (int k = 0; k < 4; k++)
        {
            TokenRange a = args[k];
            if (a.IsEmpty || !CssQuantity.TryParse(a, a.From, QuantityKinds.Number, null, out CssQuantity q, out int next) || a.SkipWs(next) < a.To)
            {
                return false;
            }

            if ((k == 0 || k == 2) && q.Calc is null && (q.Value < 0 || q.Value > 1))
            {
                return false;
            }
        }

        return true;
    }

    // CSS Easing 2 §2.3: steps(<integer>, <step-position>?) with at least one step, and two for jump-none.
    private static bool Steps(TokenRange inner)
    {
        List<TokenRange> args = SplitCommas(inner);
        if (args.Count is < 1 or > 2)
        {
            return false;
        }

        TokenRange a = args[0];
        if (a.IsEmpty || !CssQuantity.TryParse(a, a.From, QuantityKinds.Number | QuantityKinds.IntegerOnly, null, out CssQuantity q, out int next) || a.SkipWs(next) < a.To)
        {
            return false;
        }

        double steps = q.Value;
        bool none = false;
        if (args.Count == 2)
        {
            TokenRange p = args[1];
            if (p.IsEmpty || p.Kind(p.From) != CssTokenKind.Ident || p.SkipWs(p.From + 1) < p.To || !ValueKinds.In(StepPositions, p.Name(p.From, out _)))
            {
                return false;
            }

            none = p.IsIdent(p.From, "jump-none");
        }

        return q.Calc is not null || steps >= (none ? 2 : 1);
    }

    // CSS Easing 2 §2.1: linear(<linear-easing-point>#) with at least two points; a point is a number and up to two percentages.
    private static bool LinearStops(TokenRange inner)
    {
        List<TokenRange> stops = SplitCommas(inner);
        if (stops.Count < 2)
        {
            return false;
        }

        foreach (TokenRange s in stops)
        {
            int numbers = 0, percents = 0, numberAt = -1, count = 0;
            int i = s.From;
            while (i < s.To)
            {
                if (CssQuantity.TryParse(s, i, QuantityKinds.Number, null, out _, out int next))
                {
                    numbers++;
                    numberAt = count;
                    i = next;
                }
                else if (CssQuantity.TryParse(s, i, QuantityKinds.Percentage, null, out _, out next))
                {
                    percents++;
                    i = next;
                }
                else
                {
                    return false;
                }

                count++;
                i = s.SkipWs(i);
            }

            if (numbers != 1 || percents > 2 || (numberAt != 0 && numberAt != count - 1))
            {
                return false;
            }
        }

        return true;
    }

    internal static List<TokenRange> SplitCommas(TokenRange r)
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

    /// <summary>
    /// Whether <paramref name="value"/> (CSS-wide keywords and <c>var()</c> already handled by the caller) is valid for one of the
    /// longhands in <see cref="Handles"/>. Every list item is checked on its own, and a CSS-wide keyword as an item invalidates the whole
    /// declaration (CSS Cascade 5 §7.3).
    /// </summary>
    public static bool IsValid(string name, TokenRange value)
    {
        List<TokenRange> items = SplitCommas(value.Trim());
        if (name.Equals("transition-property", StringComparison.OrdinalIgnoreCase) && items.Count > 1)
        {
            // `none` stands alone (CSS Transitions 1 §2.1).
            foreach (TokenRange it in items)
            {
                if (it.IsIdent(it.From, "none"))
                {
                    return false;
                }
            }
        }

        foreach (TokenRange it in items)
        {
            if (it.IsEmpty || !ItemValid(name, it))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ItemValid(string name, TokenRange it)
    {
        int end;
        switch (name.AsciiLower())
        {
            case "transition-property":
                return it.Next(it.From) == it.To && (it.IsIdent(it.From, "none") || IsCustomIdent(it, it.From));
            case "transition-duration":
                end = Time(it, it.From, out bool neg);
                return end > 0 && !neg && it.SkipWs(end) >= it.To;
            case "transition-delay" or "animation-delay":
                end = Time(it, it.From, out _);
                return end > 0 && it.SkipWs(end) >= it.To;
            case "transition-timing-function" or "animation-timing-function":
                end = Easing(it, it.From);
                return end > 0 && it.SkipWs(end) >= it.To;
            case "transition-behavior":
                return ValueKinds.Keyword(it, it.From, Behaviors) > 0 && it.Next(it.From) == it.To;
            case "animation-name":
                return it.Next(it.From) == it.To && (it.IsIdent(it.From, "none") || IsAnimationName(it, it.From));
            case "animation-duration":
                if (it.IsIdent(it.From, "auto"))
                {
                    return it.Next(it.From) == it.To;
                }

                end = Time(it, it.From, out neg);
                return end > 0 && !neg && it.SkipWs(end) >= it.To;
            case "animation-iteration-count":
                if (it.IsIdent(it.From, "infinite"))
                {
                    return it.Next(it.From) == it.To;
                }

                end = ValueKinds.Number(it, it.From);
                return end > 0 && it.SkipWs(end) >= it.To;
            case "animation-direction":
                return SingleKeyword(it, Directions);
            case "animation-fill-mode":
                return SingleKeyword(it, FillModes);
            case "animation-play-state":
                return SingleKeyword(it, PlayStates);
            case "animation-composition":
                return SingleKeyword(it, Compositions);
            case "animation-timeline":
                return Timeline(it);
            default:
                return true;
        }
    }

    private static bool SingleKeyword(TokenRange it, FrozenSet<string> set) => ValueKinds.Keyword(it, it.From, set) > 0 && it.Next(it.From) == it.To;

    // CSS Animations 2 §4.1 (animation-timeline): auto | none | <dashed-ident> | <scroll()> | <view()>.
    private static bool Timeline(TokenRange it)
    {
        if (it.Next(it.From) != it.To)
        {
            return false;
        }

        int i = it.From;
        if (it.Kind(i) == CssTokenKind.Ident)
        {
            if (it.IsIdent(i, "auto") || it.IsIdent(i, "none"))
            {
                return true;
            }

            ReadOnlySpan<char> w = it.Name(i, out _);
            return w.Length > 2 && w[0] == '-' && w[1] == '-';
        }

        if (it.Kind(i) != CssTokenKind.Function)
        {
            return false;
        }

        TokenRange inner = it.Inner(i);
        if (it.IsFunction(i, "scroll"))
        {
            // [ <scroller> || <axis> ]?
            bool scroller = false, axis = false;
            int j = inner.SkipWs(inner.From);
            while (j < inner.To)
            {
                if (inner.Kind(j) != CssTokenKind.Ident)
                {
                    return false;
                }

                if (!scroller && ValueKinds.In(Scrollers, inner.Name(j, out _)))
                {
                    scroller = true;
                }
                else if (!axis && ValueKinds.In(Axes, inner.Name(j, out _)))
                {
                    axis = true;
                }
                else
                {
                    return false;
                }

                j = inner.SkipWs(j + 1);
            }

            return true;
        }

        if (it.IsFunction(i, "view"))
        {
            // [ <axis> || <'view-timeline-inset'> ]?: an optional axis and up to two insets, each auto or a length-percentage.
            bool axis = false;
            int insets = 0;
            int j = inner.SkipWs(inner.From);
            while (j < inner.To)
            {
                if (inner.Kind(j) == CssTokenKind.Ident && !axis && ValueKinds.In(Axes, inner.Name(j, out _)))
                {
                    axis = true;
                    j = inner.SkipWs(j + 1);
                    continue;
                }

                int next = ValueKinds.LengthPercentage(inner, j, allowAuto: true);
                if (next < 0 || ++insets > 2)
                {
                    return false;
                }

                j = inner.SkipWs(next);
            }

            return true;
        }

        return false;
    }
}
