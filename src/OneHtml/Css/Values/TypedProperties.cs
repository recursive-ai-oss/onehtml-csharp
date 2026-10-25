using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text;

namespace RecursiveAI.OneHtml.Css;

/// <summary><c>position</c> (CSS Positioned Layout 3 §2).</summary>
public enum PositionValue : byte
{
    /// <summary><c>static</c>.</summary>
    Static,

    /// <summary><c>relative</c>.</summary>
    Relative,

    /// <summary><c>absolute</c>.</summary>
    Absolute,

    /// <summary><c>fixed</c>.</summary>
    Fixed,

    /// <summary><c>sticky</c> (also written <c>-webkit-sticky</c>).</summary>
    Sticky,
}

/// <summary>An <c>overflow-x</c>/<c>overflow-y</c> keyword (CSS Overflow 3 §3.1).</summary>
public enum OverflowValue : byte
{
    /// <summary><c>visible</c>.</summary>
    Visible,

    /// <summary><c>hidden</c>.</summary>
    Hidden,

    /// <summary><c>clip</c>.</summary>
    Clip,

    /// <summary><c>scroll</c>.</summary>
    Scroll,

    /// <summary><c>auto</c>.</summary>
    Auto,
}

/// <summary>A <c>pointer-events</c> keyword (SVG 2 §... and CSS UI 4 §6.1).</summary>
public enum PointerEventsValue : byte
{
    /// <summary><c>auto</c>.</summary>
    Auto,

    /// <summary><c>none</c>.</summary>
    None,

    /// <summary><c>visiblePainted</c>.</summary>
    VisiblePainted,

    /// <summary><c>visibleFill</c>.</summary>
    VisibleFill,

    /// <summary><c>visibleStroke</c>.</summary>
    VisibleStroke,

    /// <summary><c>visible</c>.</summary>
    Visible,

    /// <summary><c>painted</c>.</summary>
    Painted,

    /// <summary><c>fill</c>.</summary>
    Fill,

    /// <summary><c>stroke</c>.</summary>
    Stroke,

    /// <summary><c>all</c>.</summary>
    All,
}

/// <summary>What one layer of <c>background-image</c> is.</summary>
public enum ImageKind : byte
{
    /// <summary><c>none</c>.</summary>
    None,

    /// <summary>A <c>url()</c> (or <c>src()</c>) image.</summary>
    Url,

    /// <summary>A linear, radial or conic gradient, repeating or not.</summary>
    Gradient,

    /// <summary><c>image-set()</c>.</summary>
    ImageSet,

    /// <summary>Another image function (<c>image()</c>, <c>cross-fade()</c>, <c>element()</c>, <c>paint()</c>).</summary>
    Other,
}

/// <summary><c>clip-path</c> in three kinds (CSS Masking 1 §5.1): none, a reference, or a shape/box.</summary>
public enum ClipPathKind : byte
{
    /// <summary><c>none</c>: nothing is clipped.</summary>
    None,

    /// <summary>A <c>url()</c> reference to an SVG clipPath.</summary>
    Reference,

    /// <summary>A basic shape, a geometry box, or both: the element is clipped.</summary>
    Shape,
}

/// <summary>A <c>&lt;length-percentage&gt;</c> or <c>auto</c>: the type of <c>top</c>, <c>left</c>, <c>inset-*</c>.</summary>
/// <param name="IsAuto">True for <c>auto</c>.</param>
/// <param name="Value">The length or percentage when not auto.</param>
public readonly record struct LengthPercentageOrAuto(bool IsAuto, CssQuantity Value)
{
    /// <summary>The value as CSS text.</summary>
    public string ToCss() => IsAuto ? "auto" : Value.ToCss();
}

/// <summary>The <c>clip</c> property (CSS Masking 1 §5.2 legacy): <c>auto</c> or <c>rect()</c> with four edges, each a length or <c>auto</c>.</summary>
/// <param name="IsAuto">True for <c>auto</c>.</param>
/// <param name="Top">Top edge (meaningful when not auto).</param>
/// <param name="Right">Right edge.</param>
/// <param name="Bottom">Bottom edge.</param>
/// <param name="Left">Left edge.</param>
public readonly record struct ClipValue(bool IsAuto, LengthPercentageOrAuto Top, LengthPercentageOrAuto Right, LengthPercentageOrAuto Bottom, LengthPercentageOrAuto Left)
{
    /// <summary>The value as CSS text (<c>rect(...)</c> is written with commas).</summary>
    public string ToCss() => IsAuto ? "auto" : $"rect({Top.ToCss()}, {Right.ToCss()}, {Bottom.ToCss()}, {Left.ToCss()})";
}

/// <summary>A <c>font-size</c>: a keyword or a length/percentage (CSS Fonts 4 §2.5).</summary>
public sealed class FontSizeValue
{
    private static readonly FrozenDictionary<string, double> Table = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
    {
        ["xx-small"] = 9, ["x-small"] = 10, ["small"] = 13, ["medium"] = 16, ["large"] = 18, ["x-large"] = 24, ["xx-large"] = 32, ["xxx-large"] = 48,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    internal FontSizeValue(string? keyword, CssQuantity length)
    {
        Keyword = keyword;
        Length = length;
    }

    /// <summary>The keyword (<c>medium</c>, <c>larger</c>, <c>math</c>, ...), lower case, or null for a length.</summary>
    public string? Keyword { get; }

    /// <summary>The length or percentage when <see cref="Keyword"/> is null.</summary>
    public CssQuantity Length { get; }

    /// <summary>
    /// Computes the font size in px. <paramref name="context"/> describes the <em>parent</em> (its <see cref="LengthContext.FontSize"/>
    /// is the inherited size, which <c>em</c> and <c>%</c> refer to here, CSS Fonts 4 §2.5). Absolute-size keywords use the
    /// table 9, 10, 13, 16, 18, 24, 32, 48 px for a 16px <c>medium</c> and scale with <paramref name="mediumPx"/>;
    /// <c>larger</c> and <c>smaller</c> multiply by 1.2 and 1/1.2. These ratios are this library's documented defaults (the specification leaves the table to the user agent).
    /// </summary>
    /// <param name="context">The parent's length context.</param>
    /// <param name="mediumPx">The size of <c>medium</c>; 16 by default.</param>
    public double ResolvePx(in LengthContext context, double mediumPx = 16)
    {
        if (Keyword is null)
        {
            return Length.Resolve(in context, context.FontSize);
        }

        if (Table.TryGetValue(Keyword, out double px))
        {
            return px * mediumPx / 16;
        }

        return Keyword switch
        {
            "larger" => context.FontSize * 1.2,
            "smaller" => context.FontSize / 1.2,
            _ => context.FontSize,
        };
    }

    /// <summary>The value as CSS text.</summary>
    public string ToCss() => Keyword ?? Length.ToCss();
}

/// <summary>A <c>font-weight</c>: a number 1..1000 or <c>bolder</c>/<c>lighter</c> (CSS Fonts 4 §2.2).</summary>
public sealed class FontWeightValue
{
    internal FontWeightValue(double number, string? relative, string? calcText)
    {
        Number = number;
        Relative = relative;
        CalcText = calcText;
    }

    /// <summary>The numeric weight (<c>normal</c> = 400, <c>bold</c> = 700). Unused when <see cref="Relative"/> is set.</summary>
    public double Number { get; }

    /// <summary><c>bolder</c> or <c>lighter</c>, or null.</summary>
    public string? Relative { get; }

    internal string? CalcText { get; }

    /// <summary>Resolves the weight given the inherited one; <c>bolder</c>/<c>lighter</c> follow the table of CSS Fonts 4 §2.2.</summary>
    /// <param name="inherited">The parent's computed weight.</param>
    public double Resolve(double inherited)
    {
        if (Relative is null)
        {
            return Math.Clamp(Number, 1, 1000);
        }

        bool bolder = Relative == "bolder";
        return inherited switch
        {
            < 100 => bolder ? 400 : inherited,
            < 350 => bolder ? 400 : 100,
            < 550 => bolder ? 700 : 100,
            < 750 => bolder ? 900 : 400,
            < 900 => bolder ? 900 : 700,
            _ => bolder ? inherited : 700,
        };
    }

    /// <summary>The value as CSS text; the keywords <c>normal</c> and <c>bold</c> serialize as written.</summary>
    public string ToCss() => Relative ?? CalcText ?? CssUnits.FormatNumber(Number);
}

/// <summary>A <c>font-style</c>: <c>normal</c>, <c>italic</c>, or <c>oblique</c> with an optional angle.</summary>
/// <param name="Keyword">normal, italic or oblique.</param>
/// <param name="ObliqueAngleDegrees">The angle for oblique (14 when not written).</param>
public readonly record struct FontStyleValue(string Keyword, double ObliqueAngleDegrees)
{
    /// <summary>The value as CSS text (<c>oblique 14deg</c> serializes as <c>oblique</c>).</summary>
    public string ToCss() => Keyword == "oblique" && Math.Abs(ObliqueAngleDegrees - 14) > 1e-9 ? $"oblique {CssUnits.FormatNumber(ObliqueAngleDegrees)}deg" : Keyword;
}

/// <summary>A <c>font-family</c> list.</summary>
public sealed class FontFamilyValue
{
    private static readonly FrozenSet<string> Generic = ValueKinds.Set(
        "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif", "ui-sans-serif", "ui-monospace", "ui-rounded", "emoji", "math", "fangsong");

    internal FontFamilyValue(string[] names, bool[] generic)
    {
        Names = names;
        IsGeneric = generic;
    }

    /// <summary>The families in order: family names with their string content, generic keywords lower case.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>For each entry, whether it is a generic family keyword.</summary>
    public IReadOnlyList<bool> IsGeneric { get; }

    /// <summary>Whether <c>name</c> is a generic family keyword.</summary>
    /// <param name="name">A name.</param>
    public static bool IsGenericFamily(string name) => ValueKinds.In(Generic, name);

    /// <summary>The value as CSS text: quoted names that need it, generic keywords bare.</summary>
    public string ToCss()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Names.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(IsGeneric[i] ? Names[i] : Quote(Names[i]));
        }

        return sb.ToString();
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

/// <summary>A parsed <c>speak-as</c> value (CSS Speech 1 §... ).</summary>
/// <param name="Normal">True for <c>normal</c>.</param>
/// <param name="SpellOut">True when <c>spell-out</c> is set.</param>
/// <param name="Digits">True when <c>digits</c> is set.</param>
/// <param name="Punctuation">null, <c>literal-punctuation</c> or <c>no-punctuation</c>.</param>
public readonly record struct SpeakAsValue(bool Normal, bool SpellOut, bool Digits, string? Punctuation);

/// <summary>An outline width: a keyword's length or a length (CSS UI 4 §4.1 gives <c>thin</c>, <c>medium</c>, <c>thick</c> user-agent values; 1, 3 and 5 px are used).</summary>
public sealed class LineWidthValue
{
    internal LineWidthValue(string? keyword, CssQuantity length)
    {
        Keyword = keyword;
        Length = length;
    }

    /// <summary><c>thin</c>, <c>medium</c> or <c>thick</c>; null for a length.</summary>
    public string? Keyword { get; }

    /// <summary>The length when <see cref="Keyword"/> is null.</summary>
    public CssQuantity Length { get; }

    /// <summary>The width in px.</summary>
    /// <param name="context">Resolves relative lengths.</param>
    public double ResolvePx(in LengthContext context) => Keyword switch
    {
        "thin" => 1,
        "medium" => 3,
        "thick" => 5,
        _ => Length.Resolve(in context),
    };
}

/// <summary>Typed parsers for the properties the accessibility tree and the checks read. Each takes the declaration's value after <c>var()</c> substitution and returns false for a value that is not valid for the property.</summary>
public static class TypedProperties
{
    private static readonly FrozenSet<string> Visibilities = ValueKinds.Set("visible", "hidden", "collapse");
    private static readonly FrozenSet<string> Positions = ValueKinds.Set("static", "relative", "absolute", "fixed", "sticky", "-webkit-sticky");
    private static readonly FrozenSet<string> Pointers = ValueKinds.Set("auto", "none", "visiblepainted", "visiblefill", "visiblestroke", "visible", "painted", "fill", "stroke", "all");
    private static readonly FrozenSet<string> FontSizeKeywords = ValueKinds.Set("xx-small", "x-small", "small", "medium", "large", "x-large", "xx-large", "xxx-large", "larger", "smaller", "math");
    private static readonly FrozenSet<string> ShapeFunctions = ValueKinds.Set("inset", "circle", "ellipse", "polygon", "path", "rect", "xywh", "shape");
    private static readonly FrozenSet<string> GeometryBoxes = ValueKinds.Set("margin-box", "border-box", "padding-box", "content-box", "fill-box", "stroke-box", "view-box");
    private static readonly FrozenSet<string> SpeakKeywords = ValueKinds.Set("auto", "never", "always", "normal", "none", "spell-out");

    private static bool OneIdent(ComponentValueList value, out TokenRange r, out string word)
    {
        r = TokenRange.Of(value).Trim();
        word = string.Empty;
        if (r.IsEmpty || r.Next(r.From) != r.To || r.Kind(r.From) != CssTokenKind.Ident)
        {
            return false;
        }

        word = r.IdentString(r.From).AsciiLower();
        return true;
    }

    /// <summary>Parses <c>visibility</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The keyword.</param>
    public static bool TryParseVisibility(ComponentValueList value, out VisibilityValue result)
    {
        result = default;
        if (!OneIdent(value, out _, out string w) || !ValueKinds.In(Visibilities, w))
        {
            return false;
        }

        result = w == "visible" ? VisibilityValue.Visible : w == "hidden" ? VisibilityValue.Hidden : VisibilityValue.Collapse;
        return true;
    }

    /// <summary>Parses <c>opacity</c> (CSS Color 4 §3.2): a number or percentage; the result is the computed value, clamped to 0..1 (a NaN becomes 0).</summary>
    /// <param name="value">The value.</param>
    /// <param name="opacity">The number (a percentage is divided by 100).</param>
    public static bool TryParseOpacity(ComponentValueList value, out double opacity)
    {
        opacity = 1;
        if (!CssQuantity.TryParse(value, QuantityKinds.Number | QuantityKinds.Percentage, out CssQuantity q))
        {
            return false;
        }

        double v = q.Calc is not null ? q.Calc.Resolve(LengthContext.Default, 1) : q.Unit == CssUnit.Percent ? q.Value / 100 : q.Value;
        opacity = double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1);
        return true;
    }

    /// <summary>Parses a color-valued property (<c>color</c>, <c>background-color</c>, <c>outline-color</c>, ...). <c>currentcolor</c> is accepted.</summary>
    /// <param name="value">The value.</param>
    /// <param name="color">The color.</param>
    public static bool TryParseColor(ComponentValueList value, out CssColor color) => CssColor.TryParse(value, out color);

    /// <summary>Parses <c>position</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The keyword.</param>
    public static bool TryParsePosition(ComponentValueList value, out PositionValue result)
    {
        result = default;
        if (!OneIdent(value, out _, out string w) || !ValueKinds.In(Positions, w))
        {
            return false;
        }

        result = w switch { "static" => PositionValue.Static, "relative" => PositionValue.Relative, "absolute" => PositionValue.Absolute, "fixed" => PositionValue.Fixed, _ => PositionValue.Sticky };
        return true;
    }

    /// <summary>Parses an offset (<c>top</c>, <c>right</c>, <c>bottom</c>, <c>left</c>, <c>inset-*</c>): <c>auto</c> or a length-percentage.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The offset.</param>
    public static bool TryParseOffset(ComponentValueList value, out LengthPercentageOrAuto result)
    {
        result = default;
        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty || r.Next(r.From) != r.To)
        {
            return false;
        }

        if (r.IsIdent(r.From, "auto"))
        {
            result = new LengthPercentageOrAuto(true, default);
            return true;
        }

        if (!CssQuantity.TryParse(r, r.From, QuantityKinds.LengthPercentage | QuantityKinds.UnitlessZeroLength, null, out CssQuantity q, out _))
        {
            return false;
        }

        result = new LengthPercentageOrAuto(false, q);
        return true;
    }

    /// <summary>Parses <c>overflow-x</c>, <c>overflow-y</c>, <c>overflow-block</c> or <c>overflow-inline</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The keyword.</param>
    public static bool TryParseOverflow(ComponentValueList value, out OverflowValue result)
    {
        result = default;
        if (!OneIdent(value, out _, out string w) || !ValueKinds.In(ValueKinds.OverflowKeywords, w))
        {
            return false;
        }

        result = w switch { "visible" => OverflowValue.Visible, "hidden" => OverflowValue.Hidden, "clip" => OverflowValue.Clip, "scroll" => OverflowValue.Scroll, _ => OverflowValue.Auto };
        return true;
    }

    /// <summary>Parses <c>pointer-events</c>; keyword case is ignored.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The keyword.</param>
    public static bool TryParsePointerEvents(ComponentValueList value, out PointerEventsValue result)
    {
        result = default;
        if (!OneIdent(value, out _, out string w) || !ValueKinds.In(Pointers, w))
        {
            return false;
        }

        result = w switch
        {
            "auto" => PointerEventsValue.Auto,
            "none" => PointerEventsValue.None,
            "visiblepainted" => PointerEventsValue.VisiblePainted,
            "visiblefill" => PointerEventsValue.VisibleFill,
            "visiblestroke" => PointerEventsValue.VisibleStroke,
            "visible" => PointerEventsValue.Visible,
            "painted" => PointerEventsValue.Painted,
            "fill" => PointerEventsValue.Fill,
            "stroke" => PointerEventsValue.Stroke,
            _ => PointerEventsValue.All,
        };
        return true;
    }

    /// <summary>Parses <c>speak</c>: <c>auto</c>, <c>never</c>, <c>always</c> (CSS Speech 1) and the CSS 2.1 keywords <c>normal</c>, <c>none</c>, <c>spell-out</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="keyword">The keyword, lower case.</param>
    public static bool TryParseSpeak(ComponentValueList value, out string keyword)
    {
        keyword = string.Empty;
        if (!OneIdent(value, out _, out string w) || !ValueKinds.In(SpeakKeywords, w))
        {
            return false;
        }

        keyword = w;
        return true;
    }

    /// <summary>Parses <c>speak-as</c>: <c>normal | spell-out || digits || [ literal-punctuation | no-punctuation ]</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The parsed value.</param>
    public static bool TryParseSpeakAs(ComponentValueList value, out SpeakAsValue result)
    {
        result = default;
        TokenRange r = TokenRange.Of(value).Trim();
        bool spell = false, digits = false, normal = false;
        string? punct = null;
        int n = 0;
        int i = r.From;
        while (i < r.To)
        {
            if (r.Kind(i) != CssTokenKind.Ident)
            {
                return false;
            }

            switch (r.IdentString(i).AsciiLower())
            {
                case "normal" when n == 0: normal = true; break;
                case "spell-out" when !spell: spell = true; break;
                case "digits" when !digits: digits = true; break;
                case "literal-punctuation" when punct is null: punct = "literal-punctuation"; break;
                case "no-punctuation" when punct is null: punct = "no-punctuation"; break;
                default: return false;
            }

            n++;
            i = r.SkipWs(i + 1);
        }

        if (n == 0 || (normal && n > 1))
        {
            return false;
        }

        result = new SpeakAsValue(normal, spell, digits, punct);
        return true;
    }

    /// <summary>Parses <c>font-size</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The size.</param>
    public static bool TryParseFontSize(ComponentValueList value, out FontSizeValue? result)
    {
        result = null;
        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty || r.Next(r.From) != r.To)
        {
            return false;
        }

        if (r.Kind(r.From) == CssTokenKind.Ident)
        {
            string w = r.IdentString(r.From).AsciiLower();
            if (!ValueKinds.In(FontSizeKeywords, w))
            {
                return false;
            }

            result = new FontSizeValue(w, default);
            return true;
        }

        if (!CssQuantity.TryParse(r, r.From, QuantityKinds.LengthPercentage | QuantityKinds.NonNegative, null, out CssQuantity q, out _))
        {
            return false;
        }

        result = new FontSizeValue(null, q);
        return true;
    }

    /// <summary>Parses <c>font-weight</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The weight.</param>
    public static bool TryParseFontWeight(ComponentValueList value, out FontWeightValue? result)
    {
        result = null;
        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty || r.Next(r.From) != r.To)
        {
            return false;
        }

        if (r.Kind(r.From) == CssTokenKind.Ident)
        {
            switch (r.IdentString(r.From).AsciiLower())
            {
                case "normal": result = new FontWeightValue(400, null, "normal"); return true;
                case "bold": result = new FontWeightValue(700, null, "bold"); return true;
                case "bolder": result = new FontWeightValue(0, "bolder", null); return true;
                case "lighter": result = new FontWeightValue(0, "lighter", null); return true;
                default: return false;
            }
        }

        if (!CssQuantity.TryParse(r, r.From, QuantityKinds.Number, null, out CssQuantity q, out _) || q.Calc is not null)
        {
            return false;
        }

        if (!q.FromMath && q.Value is < 1 or > 1000)
        {
            return false;
        }

        result = new FontWeightValue(q.Value, null, q.FromMath ? q.ToCss() : null);
        return true;
    }

    /// <summary>Parses <c>font-style</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The style.</param>
    public static bool TryParseFontStyle(ComponentValueList value, out FontStyleValue result)
    {
        result = default;
        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty || r.Kind(r.From) != CssTokenKind.Ident)
        {
            return false;
        }

        string w = r.IdentString(r.From).AsciiLower();
        int next = r.SkipWs(r.From + 1);
        if (w is "normal" or "italic")
        {
            if (next < r.To)
            {
                return false;
            }

            result = new FontStyleValue(w, 0);
            return true;
        }

        if (w != "oblique")
        {
            return false;
        }

        if (next >= r.To)
        {
            result = new FontStyleValue("oblique", 14);
            return true;
        }

        if (!CssQuantity.TryParse(r, next, QuantityKinds.Angle, null, out CssQuantity q, out int after) || r.SkipWs(after) < r.To || q.Calc is not null)
        {
            return false;
        }

        double deg = q.Value * CssUnits.GetCanonicalFactor(q.Unit);
        if (deg is < -90 or > 90)
        {
            return false;
        }

        result = new FontStyleValue("oblique", deg);
        return true;
    }

    /// <summary>Parses <c>font-family</c>: a comma-separated list of quoted names, unquoted name runs (their identifiers joined by single spaces) and generic keywords.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The families.</param>
    public static bool TryParseFontFamily(ComponentValueList value, out FontFamilyValue? result)
    {
        result = null;
        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty)
        {
            return false;
        }

        var names = new List<string>();
        var generic = new List<bool>();
        int start = r.From;
        int i = r.From;
        while (true)
        {
            bool end = i >= r.To;
            if (end || r.Kind(i) == CssTokenKind.Comma)
            {
                TokenRange part = r.Sub(start, i).Trim();
                if (part.IsEmpty)
                {
                    return false;
                }

                if (part.Kind(part.From) == CssTokenKind.QuotedString)
                {
                    if (part.Next(part.From) != part.To)
                    {
                        return false;
                    }

                    names.Add(part.StringValue(part.From));
                    generic.Add(false);
                }
                else
                {
                    var words = new List<string>();
                    int j = part.From;
                    while (j < part.To)
                    {
                        if (part.Kind(j) != CssTokenKind.Ident)
                        {
                            return false;
                        }

                        words.Add(part.IdentString(j));
                        j = part.SkipWs(j + 1);
                    }

                    if (words.Count == 1 && CssWideKeywords.FromName(words[0]) != CssWideKeyword.None)
                    {
                        return false;
                    }

                    if (words.Count == 1 && words[0].Equals("default", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    string joined = string.Join(' ', words);
                    bool isGeneric = words.Count == 1 && FontFamilyValue.IsGenericFamily(joined);
                    names.Add(isGeneric ? joined.AsciiLower() : joined);
                    generic.Add(isGeneric);
                }

                if (end)
                {
                    break;
                }

                start = i + 1;
                i++;
                continue;
            }

            i = r.Next(i);
        }

        result = new FontFamilyValue(names.ToArray(), generic.ToArray());
        return true;
    }

    /// <summary>Parses <c>background-image</c> into one <see cref="ImageKind"/> per layer.</summary>
    /// <param name="value">The value.</param>
    /// <param name="layers">The kinds in layer order.</param>
    public static bool TryParseBackgroundImage(ComponentValueList value, out ImageKind[] layers)
    {
        layers = [];
        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty)
        {
            return false;
        }

        var list = new List<ImageKind>();
        int start = r.From;
        int i = r.From;
        while (true)
        {
            if (i >= r.To || r.Kind(i) == CssTokenKind.Comma)
            {
                TokenRange part = r.Sub(start, i).Trim();
                if (part.IsEmpty || part.Next(part.From) != part.To)
                {
                    return false;
                }

                int p = part.From;
                switch (part.Kind(p))
                {
                    case CssTokenKind.Url:
                        list.Add(ImageKind.Url);
                        break;
                    case CssTokenKind.Ident when part.IsIdent(p, "none"):
                        list.Add(ImageKind.None);
                        break;
                    case CssTokenKind.Function:
                    {
                        ReadOnlySpan<char> fn = part.Name(p, out _);
                        if (fn.Equals("url", StringComparison.OrdinalIgnoreCase) || fn.Equals("src", StringComparison.OrdinalIgnoreCase))
                        {
                            list.Add(ImageKind.Url);
                        }
                        else if (ValueKinds.In(ValueKinds.GradientFunctions, fn))
                        {
                            list.Add(ImageKind.Gradient);
                        }
                        else if (fn.Equals("image-set", StringComparison.OrdinalIgnoreCase) || fn.Equals("-webkit-image-set", StringComparison.OrdinalIgnoreCase))
                        {
                            list.Add(ImageKind.ImageSet);
                        }
                        else if (ValueKinds.In(ValueKinds.ImageFunctions, fn))
                        {
                            list.Add(ImageKind.Other);
                        }
                        else
                        {
                            return false;
                        }

                        break;
                    }

                    default:
                        return false;
                }

                if (i >= r.To)
                {
                    break;
                }

                start = i + 1;
                i++;
                continue;
            }

            i = r.Next(i);
        }

        layers = list.ToArray();
        return true;
    }

    /// <summary>Parses <c>clip</c>: <c>auto</c> or <c>rect(top, right, bottom, left)</c> (commas optional).</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The clip.</param>
    public static bool TryParseClip(ComponentValueList value, out ClipValue result)
    {
        result = default;
        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty || r.Next(r.From) != r.To)
        {
            return false;
        }

        if (r.IsIdent(r.From, "auto"))
        {
            result = new ClipValue(true, default, default, default, default);
            return true;
        }

        if (!r.IsFunction(r.From, "rect"))
        {
            return false;
        }

        TokenRange inner = r.Inner(r.From);
        var edges = new List<LengthPercentageOrAuto>();
        int i = inner.SkipWs(inner.From);
        bool commas = false;
        while (i < inner.To)
        {
            if (edges.Count >= 4)
            {
                return false;
            }

            if (inner.IsIdent(i, "auto"))
            {
                edges.Add(new LengthPercentageOrAuto(true, default));
                i++;
            }
            else if (CssQuantity.TryParse(inner, i, QuantityKinds.Length | QuantityKinds.UnitlessZeroLength, null, out CssQuantity q, out int next))
            {
                edges.Add(new LengthPercentageOrAuto(false, q));
                i = next;
            }
            else
            {
                return false;
            }

            i = inner.SkipWs(i);
            if (i < inner.To && inner.Kind(i) == CssTokenKind.Comma)
            {
                commas = true;
                i = inner.SkipWs(i + 1);
                if (i >= inner.To)
                {
                    return false;
                }
            }
            else if (commas && i < inner.To)
            {
                return false; // mixing commas and spaces
            }
        }

        if (edges.Count != 4)
        {
            return false;
        }

        result = new ClipValue(false, edges[0], edges[1], edges[2], edges[3]);
        return true;
    }

    /// <summary>Parses <c>clip-path</c> into none / reference / shape.</summary>
    /// <param name="value">The value.</param>
    /// <param name="kind">The kind.</param>
    public static bool TryParseClipPath(ComponentValueList value, out ClipPathKind kind)
    {
        kind = ClipPathKind.None;
        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty)
        {
            return false;
        }

        if (r.Next(r.From) == r.To)
        {
            if (r.IsIdent(r.From, "none"))
            {
                return true;
            }

            if (r.Kind(r.From) == CssTokenKind.Url || r.IsFunction(r.From, "url"))
            {
                kind = ClipPathKind.Reference;
                return true;
            }
        }

        bool shape = false, box = false;
        int i = r.From;
        while (i < r.To)
        {
            if (r.Kind(i) == CssTokenKind.Function && ValueKinds.In(ShapeFunctions, r.Name(i, out _)) && !shape)
            {
                shape = true;
            }
            else if (r.Kind(i) == CssTokenKind.Ident && ValueKinds.In(GeometryBoxes, r.Name(i, out _)) && !box)
            {
                box = true;
            }
            else
            {
                return false;
            }

            i = r.SkipWs(r.Next(i));
        }

        kind = ClipPathKind.Shape;
        return shape || box;
    }

    /// <summary>Parses an outline width: <c>thin</c>, <c>medium</c>, <c>thick</c> or a non-negative length.</summary>
    /// <param name="value">The value.</param>
    /// <param name="result">The width.</param>
    public static bool TryParseLineWidth(ComponentValueList value, out LineWidthValue? result)
    {
        result = null;
        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty || r.Next(r.From) != r.To)
        {
            return false;
        }

        if (r.Kind(r.From) == CssTokenKind.Ident)
        {
            string w = r.IdentString(r.From).AsciiLower();
            if (!ValueKinds.In(ValueKinds.LineWidths, w))
            {
                return false;
            }

            result = new LineWidthValue(w, default);
            return true;
        }

        if (!CssQuantity.TryParse(r, r.From, QuantityKinds.Length | QuantityKinds.NonNegative | QuantityKinds.UnitlessZeroLength, null, out CssQuantity q, out _))
        {
            return false;
        }

        result = new LineWidthValue(null, q);
        return true;
    }

    /// <summary>Parses <c>outline-style</c>: <c>auto</c> or a line style other than <c>hidden</c> (CSS UI 4 §4.2).</summary>
    /// <param name="value">The value.</param>
    /// <param name="style">The keyword, lower case.</param>
    public static bool TryParseOutlineStyle(ComponentValueList value, out string style)
    {
        style = string.Empty;
        if (!OneIdent(value, out _, out string w))
        {
            return false;
        }

        if (w == "auto" || (w != "hidden" && ValueKinds.In(ValueKinds.LineStyles, w)))
        {
            style = w;
            return true;
        }

        return false;
    }

    /// <summary>Parses <c>outline-offset</c> (a length).</summary>
    /// <param name="value">The value.</param>
    /// <param name="offset">The length.</param>
    public static bool TryParseOutlineOffset(ComponentValueList value, out CssQuantity offset) =>
        CssQuantity.TryParse(value, QuantityKinds.Length | QuantityKinds.UnitlessZeroLength, out offset);

    /// <summary>Which typed parser, if any, a property has.</summary>
    /// <param name="property">A property name.</param>
    public static bool IsTyped(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return TypedNames.Contains(property);
    }

    private static readonly FrozenSet<string> TypedNames = ValueKinds.Set(
        "display", "visibility", "content", "color", "background-color", "background-image", "opacity", "font-size", "font-weight",
        "font-style", "font-family", "outline-width", "outline-style", "outline-color", "outline-offset", "clip", "clip-path", "position",
        "top", "right", "bottom", "left", "inset-block-start", "inset-block-end", "inset-inline-start", "inset-inline-end",
        "overflow-x", "overflow-y", "overflow-block", "overflow-inline", "speak", "speak-as", "pointer-events");

    /// <summary>
    /// Parses a value for a typed property and returns its serialization. Returns false when the property has no typed
    /// parser here or the value is not valid for it; use <see cref="IsTyped"/> to tell the two apart.
    /// </summary>
    /// <param name="property">The property name.</param>
    /// <param name="value">The value (CSS-wide keywords and <c>var()</c> handled by the caller).</param>
    /// <param name="css">The serialized specified value.</param>
    public static bool TrySerialize(string property, ComponentValueList value, out string css)
    {
        css = string.Empty;
        ArgumentNullException.ThrowIfNull(property);
        string p = property.AsciiLower();
        switch (p)
        {
            case "display" when DisplayValue.TryParse(value, out DisplayValue d):
                css = d.ToCss();
                return true;
            case "visibility" when TryParseVisibility(value, out VisibilityValue v):
                css = v.ToString().AsciiLower();
                return true;
            case "color" or "background-color" or "outline-color" when TryParseColor(value, out CssColor c):
                css = c.ToCss();
                return true;
            case "opacity" when TryParseOpacity(value, out double o):
                css = CssUnits.FormatNumber(o);
                return true;
            case "position" when TryParsePosition(value, out PositionValue pos):
                css = pos == PositionValue.Sticky ? "sticky" : pos.ToString().AsciiLower();
                return true;
            case "top" or "right" or "bottom" or "left" or "inset-block-start" or "inset-block-end" or "inset-inline-start" or "inset-inline-end" when TryParseOffset(value, out LengthPercentageOrAuto off):
                css = off.ToCss();
                return true;
            case "overflow-x" or "overflow-y" or "overflow-block" or "overflow-inline" when TryParseOverflow(value, out OverflowValue ov):
                css = ov.ToString().AsciiLower();
                return true;
            case "font-size" when TryParseFontSize(value, out FontSizeValue? fs):
                css = fs!.ToCss();
                return true;
            case "font-weight" when TryParseFontWeight(value, out FontWeightValue? fw):
                css = fw!.ToCss();
                return true;
            case "font-style" when TryParseFontStyle(value, out FontStyleValue st):
                css = st.ToCss();
                return true;
            case "font-family" when TryParseFontFamily(value, out FontFamilyValue? ff):
                css = ff!.ToCss();
                return true;
            case "clip" when TryParseClip(value, out ClipValue clip):
                css = clip.ToCss();
                return true;
            case "outline-style" when TryParseOutlineStyle(value, out string os):
                css = os;
                return true;
            case "outline-width" when TryParseLineWidth(value, out LineWidthValue? lw):
                css = lw!.Keyword ?? lw.Length.ToCss();
                return true;
            case "outline-offset" when TryParseOutlineOffset(value, out CssQuantity oo):
                css = oo.ToCss();
                return true;
            case "speak" when TryParseSpeak(value, out string sp):
                css = sp;
                return true;
            case "pointer-events" when TryParsePointerEvents(value, out PointerEventsValue pe):
                css = pe switch
                {
                    PointerEventsValue.VisiblePainted => "visiblePainted",
                    PointerEventsValue.VisibleFill => "visibleFill",
                    PointerEventsValue.VisibleStroke => "visibleStroke",
                    _ => pe.ToString().AsciiLower(),
                };
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Whether a value is valid for a typed property (the typed parsers above, plus <c>content</c>, <c>clip-path</c>,
    /// <c>background-image</c> and <c>speak-as</c>). Returns false for a property without a typed parser.
    /// </summary>
    /// <param name="property">The property name.</param>
    /// <param name="value">The value.</param>
    public static bool IsValid(string property, ComponentValueList value)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (TrySerialize(property, value, out _))
        {
            return true;
        }

        return property.AsciiLower() switch
        {
            "content" => ContentValue.TryParse(value, out _),
            "clip-path" => TryParseClipPath(value, out _),
            "background-image" => TryParseBackgroundImage(value, out _),
            "speak-as" => TryParseSpeakAs(value, out _),
            _ => false,
        };
    }
}
