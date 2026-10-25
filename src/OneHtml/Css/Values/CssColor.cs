using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneHtml.Css;

/// <summary>A color space of CSS Color 4 (§4 to §10).</summary>
public enum ColorSpace : byte
{
    /// <summary>sRGB, gamma encoded; components are 0..1.</summary>
    Srgb,

    /// <summary>sRGB with linear light.</summary>
    SrgbLinear,

    /// <summary>Display P3.</summary>
    DisplayP3,

    /// <summary>Adobe RGB (1998).</summary>
    A98Rgb,

    /// <summary>ProPhoto RGB.</summary>
    ProphotoRgb,

    /// <summary>ITU-R BT.2020.</summary>
    Rec2020,

    /// <summary>CIE XYZ with the D50 white point.</summary>
    XyzD50,

    /// <summary>CIE XYZ with the D65 white point.</summary>
    XyzD65,

    /// <summary>HSL: hue in degrees, saturation and lightness in 0..100.</summary>
    Hsl,

    /// <summary>HWB: hue in degrees, whiteness and blackness in 0..100.</summary>
    Hwb,

    /// <summary>CIE Lab (D50): lightness 0..100, a and b about ±125.</summary>
    Lab,

    /// <summary>CIE LCH (D50): lightness, chroma, hue in degrees.</summary>
    Lch,

    /// <summary>Oklab: lightness 0..1, a and b about ±0.4.</summary>
    Oklab,

    /// <summary>Oklch: lightness, chroma, hue in degrees.</summary>
    Oklch,
}

/// <summary>What a parsed <c>&lt;color&gt;</c> stands for.</summary>
public enum ColorKind : byte
{
    /// <summary>A color with components.</summary>
    Value,

    /// <summary>The <c>currentcolor</c> keyword; resolve it with <see cref="CssColor.Resolve"/>.</summary>
    CurrentColor,
}

/// <summary>
/// A CSS color (CSS Color 4): components in a <see cref="ColorSpace"/>, alpha, and the per-component "missing"
/// (<c>none</c>) flags. Colors written as hex, a named color, <c>rgb()</c>, <c>hsl()</c> or <c>hwb()</c> are stored as
/// sRGB and serialize in the legacy <c>rgb()</c> form (§15.2).
/// </summary>
public readonly struct CssColor : IEquatable<CssColor>
{
    internal CssColor(ColorSpace space, double c1, double c2, double c3, double alpha, byte missing, bool legacy, ColorKind kind = ColorKind.Value, string? systemName = null)
    {
        SystemName = systemName;
        Space = space;
        C1 = c1;
        C2 = c2;
        C3 = c3;
        Alpha = alpha;
        MissingMask = missing;
        IsLegacy = legacy;
        Kind = kind;
    }

    /// <summary>The space the components are in.</summary>
    public ColorSpace Space { get; }

    /// <summary>First component (red, hue, L, X, ...).</summary>
    public double C1 { get; }

    /// <summary>Second component.</summary>
    public double C2 { get; }

    /// <summary>Third component.</summary>
    public double C3 { get; }

    /// <summary>Alpha, 0..1.</summary>
    public double Alpha { get; }

    /// <summary>Bit 0..2: component 1..3 is <c>none</c>; bit 3: alpha is <c>none</c>.</summary>
    public byte MissingMask { get; }

    /// <summary>True for colors that serialize in the legacy <c>rgb()</c> form.</summary>
    public bool IsLegacy { get; }

    /// <summary>Value or currentcolor.</summary>
    public ColorKind Kind { get; }

    /// <summary>For a system color keyword (<c>Canvas</c>, <c>LinkText</c>, ...), its lower-case name; the components are the light-scheme default. Null otherwise.</summary>
    public string? SystemName { get; }

    /// <summary>True for the <c>currentcolor</c> keyword.</summary>
    public bool IsCurrentColor => Kind == ColorKind.CurrentColor;

    /// <summary>The <c>currentcolor</c> keyword.</summary>
    public static CssColor CurrentColor { get; } = new(ColorSpace.Srgb, 0, 0, 0, 1, 0, true, ColorKind.CurrentColor);

    /// <summary>Transparent black.</summary>
    public static CssColor Transparent { get; } = new(ColorSpace.Srgb, 0, 0, 0, 0, 0, true);

    /// <summary>Builds a color in a given space.</summary>
    /// <param name="space">The space.</param>
    /// <param name="c1">First component.</param>
    /// <param name="c2">Second component.</param>
    /// <param name="c3">Third component.</param>
    /// <param name="alpha">Alpha, clamped to 0..1.</param>
    public static CssColor Create(ColorSpace space, double c1, double c2, double c3, double alpha = 1) =>
        new(space, c1, c2, c3, Math.Clamp(alpha, 0, 1), 0, false);

    /// <summary>An opaque or translucent sRGB color from 8-bit channels, in the legacy serialization form.</summary>
    /// <param name="r">Red 0..255.</param>
    /// <param name="g">Green 0..255.</param>
    /// <param name="b">Blue 0..255.</param>
    /// <param name="alpha">Alpha 0..1.</param>
    public static CssColor FromRgb(int r, int g, int b, double alpha = 1) =>
        new(ColorSpace.Srgb, Math.Clamp(r, 0, 255) / 255.0, Math.Clamp(g, 0, 255) / 255.0, Math.Clamp(b, 0, 255) / 255.0, Math.Clamp(alpha, 0, 1), 0, true);

    /// <summary>Replaces <c>currentcolor</c> by <paramref name="current"/>; any other color is returned unchanged.</summary>
    /// <param name="current">The color to use for <c>currentcolor</c>.</param>
    public CssColor Resolve(CssColor current) => IsCurrentColor ? current : this;

    /// <summary>
    /// The color as gamma-encoded sRGB with alpha, each in 0..1. Out-of-gamut colors are mapped into the sRGB gamut by
    /// reducing Oklch chroma (CSS Color 4 §13.2); <c>none</c> components count as 0.
    /// </summary>
    public (double R, double G, double B, double A) ToSrgb()
    {
        CssColor c = Space == ColorSpace.Srgb ? this : ColorMath.Convert(this, ColorSpace.Srgb);
        double r = c.Comp(0), g = c.Comp(1), b = c.Comp(2);
        double a = (c.MissingMask & 8) != 0 ? 0 : c.Alpha;
        if (r is < -1e-9 or > 1 + 1e-9 || g is < -1e-9 or > 1 + 1e-9 || b is < -1e-9 or > 1 + 1e-9)
        {
            (r, g, b) = ColorMath.GamutMap(this);
        }

        return (Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1), a);
    }

    /// <summary>The color as 8-bit sRGB channels (rounded) and alpha 0..1.</summary>
    public (byte R, byte G, byte B, double A) ToRgba8()
    {
        (double r, double g, double b, double a) = ToSrgb();
        return ((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255), a);
    }

    /// <summary>Converts the color to another space (through XYZ). <c>none</c> flags survive only when the space is unchanged.</summary>
    /// <param name="target">The target space.</param>
    public CssColor ConvertTo(ColorSpace target) => ColorMath.Convert(this, target);

    internal double Comp(int i) => (MissingMask & (1 << i)) != 0 ? 0 : i switch { 0 => C1, 1 => C2, _ => C3 };

    /// <summary>Parses a single <c>&lt;color&gt;</c> value (named, hex, <c>rgb()</c>, <c>hsl()</c>, <c>hwb()</c>, <c>lab()</c>, <c>lch()</c>, <c>oklab()</c>, <c>oklch()</c>, <c>color()</c>, <c>color-mix()</c>, <c>light-dark()</c>, relative colors, system colors, <c>currentcolor</c>).</summary>
    /// <param name="value">One component value, optionally surrounded by whitespace.</param>
    /// <param name="color">The color.</param>
    /// <param name="options">Parse options; null for defaults.</param>
    public static bool TryParse(ComponentValueList value, out CssColor color, ColorParseOptions? options = null)
    {
        color = default;
        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty)
        {
            return false;
        }

        if (r.Next(r.From) != r.To)
        {
            return false;
        }

        return CssColorParser.TryParseAt(r, r.From, options ?? ColorParseOptions.Default, 0, out color, out _);
    }

    /// <summary>Parses a color from text, for example a test value or an attribute.</summary>
    /// <param name="text">The text.</param>
    /// <param name="color">The color.</param>
    /// <param name="options">Parse options; null for defaults.</param>
    public static bool TryParse(string text, out CssColor color, ColorParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        CssTree tree = CssTree.ParseComponentValueList(text);
        return TryParse(tree.Values, out color, options);
    }

    /// <summary>The specified-value serialization (CSS Color 4 §15): legacy colors as <c>rgb()</c>/<c>rgba()</c>, the rest in their own function form.</summary>
    public string ToCss() => ColorSerializer.Serialize(this);

    /// <inheritdoc />
    public override string ToString() => ToCss();

    /// <inheritdoc />
    public bool Equals(CssColor other) => ToCss() == other.ToCss();

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CssColor c && Equals(c);

    /// <inheritdoc />
    public override int GetHashCode() => ToCss().GetHashCode(StringComparison.Ordinal);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(CssColor left, CssColor right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(CssColor left, CssColor right) => !left.Equals(right);
}

/// <summary>Inputs to color parsing that depend on the environment.</summary>
public sealed class ColorParseOptions
{
    /// <summary>The defaults: light color scheme.</summary>
    public static ColorParseOptions Default { get; } = new();

    /// <summary>When true, <c>light-dark(a, b)</c> yields <c>b</c>; the default is the light scheme.</summary>
    public bool DarkScheme { get; init; }
}

internal static class ColorSerializer
{
    public static string Serialize(in CssColor c)
    {
        if (c.IsCurrentColor)
        {
            return "currentcolor";
        }

        if (c.SystemName is { } system)
        {
            return system;
        }

        var sb = new StringBuilder();
        bool alphaNone = (c.MissingMask & 8) != 0;
        double a = alphaNone ? 0 : Math.Clamp(c.Alpha, 0, 1);
        if (c.IsLegacy && c.Space == ColorSpace.Srgb)
        {
            // §16.2.2 and §16.1.1: the legacy form holds 8-bit channels and an 8-bit alpha.
            int alpha8 = (int)Math.Floor((a * 255) + 0.5 + 1e-9);
            bool opaque = alpha8 >= 255;
            sb.Append(opaque ? "rgb(" : "rgba(");
            sb.Append(Channel8(c.Comp(0))).Append(", ");
            sb.Append(Channel8(c.Comp(1))).Append(", ");
            sb.Append(Channel8(c.Comp(2)));
            if (!opaque)
            {
                sb.Append(", ").Append(LegacyAlpha(alpha8));
            }

            return sb.Append(')').ToString();
        }

        switch (c.Space)
        {
            case ColorSpace.Hsl:
                sb.Append("hsl(").Append(Comp(c, 0)).Append(' ').Append(Pct(c, 1)).Append(' ').Append(Pct(c, 2));
                break;
            case ColorSpace.Hwb:
                sb.Append("hwb(").Append(Comp(c, 0)).Append(' ').Append(Pct(c, 1)).Append(' ').Append(Pct(c, 2));
                break;
            case ColorSpace.Lab:
                sb.Append("lab(").Append(Comp(c, 0)).Append(' ').Append(Comp(c, 1)).Append(' ').Append(Comp(c, 2));
                break;
            case ColorSpace.Lch:
                sb.Append("lch(").Append(Comp(c, 0)).Append(' ').Append(Comp(c, 1)).Append(' ').Append(Comp(c, 2));
                break;
            case ColorSpace.Oklab:
                sb.Append("oklab(").Append(Comp(c, 0)).Append(' ').Append(Comp(c, 1)).Append(' ').Append(Comp(c, 2));
                break;
            case ColorSpace.Oklch:
                sb.Append("oklch(").Append(Comp(c, 0)).Append(' ').Append(Comp(c, 1)).Append(' ').Append(Comp(c, 2));
                break;
            default:
                sb.Append("color(").Append(ColorMath.SpaceName(c.Space)).Append(' ').Append(Comp(c, 0)).Append(' ').Append(Comp(c, 1)).Append(' ').Append(Comp(c, 2));
                break;
        }

        if (alphaNone)
        {
            sb.Append(" / none");
        }
        else if (a < 1)
        {
            sb.Append(" / ").Append(Num(a));
        }

        return sb.Append(')').ToString();
    }

    private static string Channel8(double unit) =>
        ((int)Math.Floor((Math.Clamp(unit, 0, 1) * 255) + 0.5 + 1e-9)).ToString(CultureInfo.InvariantCulture);

    /// <summary>CSS Color 4 §16.1.1: the two-decimal percentage that maps back to the 8-bit alpha, else the value over 0.255 to three decimals.</summary>
    private static string LegacyAlpha(int alpha8)
    {
        for (int n = 0; n <= 100; n++)
        {
            if ((n * 255 + 50) / 100 == alpha8)
            {
                return Num(n / 100.0);
            }
        }

        return Num((alpha8 * 2000 + 255) / 510 / 1000.0);
    }

    private static string Comp(in CssColor c, int i) => (c.MissingMask & (1 << i)) != 0 ? "none" : Num(i switch { 0 => c.C1, 1 => c.C2, _ => c.C3 });

    private static string Pct(in CssColor c, int i)
    {
        if ((c.MissingMask & (1 << i)) != 0)
        {
            return "none";
        }

        double v = i == 1 ? c.C2 : c.C3;
        return double.IsFinite(v) ? Num(v) + "%" : "calc(" + (double.IsNaN(v) ? "NaN" : v > 0 ? "infinity" : "-infinity") + " * 1%)";
    }

    /// <summary>Numbers are rounded to six decimals, then written in the shortest form (CSS Color 4 §15.1).</summary>
    public static string Num(double v)
    {
        if (double.IsNaN(v))
        {
            return "calc(NaN)";
        }

        if (double.IsInfinity(v))
        {
            return v > 0 ? "calc(infinity)" : "calc(-infinity)";
        }

        // The 1e-7 slack makes a value that is a tie in exact arithmetic round up despite floating-point error.
        if (Math.Abs(v) >= 1e15)
        {
            // Spelled with an exponent, so the text stays short and below the largest double when read back.
            return v.ToString("0.#####E+0", CultureInfo.InvariantCulture);
        }

        double r = Math.Round(v + 1e-7, 6, MidpointRounding.AwayFromZero);
        if (r == 0)
        {
            r = 0;
        }

        return r.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
