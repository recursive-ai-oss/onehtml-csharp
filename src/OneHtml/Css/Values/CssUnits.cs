using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;

namespace RecursiveAI.OneHtml.Css;

/// <summary>The dimension a unit measures (CSS Values 4 §5, §6, §7, §8).</summary>
public enum CssDimension : byte
{
    /// <summary>A plain number, or a percentage that is not (yet) tied to a dimension.</summary>
    Number,

    /// <summary>A length (§6).</summary>
    Length,

    /// <summary>An angle (§7.1).</summary>
    Angle,

    /// <summary>A duration (§7.2).</summary>
    Time,

    /// <summary>A frequency (§7.3).</summary>
    Frequency,

    /// <summary>A resolution (§7.4).</summary>
    Resolution,

    /// <summary>A flexible length, <c>fr</c> (CSS Grid).</summary>
    Flex,

    /// <summary>A percentage.</summary>
    Percent,
}

/// <summary>A unit of a dimension token. <see cref="None"/> is a unitless number.</summary>
public enum CssUnit : byte
{
    /// <summary>No unit.</summary>
    None,

    /// <summary><c>%</c>.</summary>
    Percent,

    /// <summary><c>px</c>, the canonical length unit.</summary>
    Px,

    /// <summary><c>cm</c>.</summary>
    Cm,

    /// <summary><c>mm</c>.</summary>
    Mm,

    /// <summary><c>Q</c>, a quarter of a millimetre.</summary>
    Q,

    /// <summary><c>in</c>.</summary>
    In,

    /// <summary><c>pt</c>.</summary>
    Pt,

    /// <summary><c>pc</c>.</summary>
    Pc,

    /// <summary><c>em</c>.</summary>
    Em,

    /// <summary><c>rem</c>.</summary>
    Rem,

    /// <summary><c>ex</c>.</summary>
    XHeight,

    /// <summary><c>rex</c>.</summary>
    RootXHeight,

    /// <summary><c>cap</c>.</summary>
    Cap,

    /// <summary><c>rcap</c>.</summary>
    Rcap,

    /// <summary><c>ch</c>.</summary>
    Ch,

    /// <summary><c>rch</c>.</summary>
    Rch,

    /// <summary><c>ic</c>.</summary>
    Ic,

    /// <summary><c>ric</c>.</summary>
    Ric,

    /// <summary><c>lh</c>.</summary>
    Lh,

    /// <summary><c>rlh</c>.</summary>
    Rlh,

    /// <summary><c>vw</c>.</summary>
    Vw,

    /// <summary><c>vh</c>.</summary>
    Vh,

    /// <summary><c>vi</c>.</summary>
    Vi,

    /// <summary><c>vb</c>.</summary>
    Vb,

    /// <summary><c>vmin</c>.</summary>
    Vmin,

    /// <summary><c>vmax</c>.</summary>
    Vmax,

    /// <summary><c>svw</c>.</summary>
    Svw,

    /// <summary><c>svh</c>.</summary>
    Svh,

    /// <summary><c>svi</c>.</summary>
    Svi,

    /// <summary><c>svb</c>.</summary>
    Svb,

    /// <summary><c>svmin</c>.</summary>
    Svmin,

    /// <summary><c>svmax</c>.</summary>
    Svmax,

    /// <summary><c>lvw</c>.</summary>
    Lvw,

    /// <summary><c>lvh</c>.</summary>
    Lvh,

    /// <summary><c>lvi</c>.</summary>
    Lvi,

    /// <summary><c>lvb</c>.</summary>
    Lvb,

    /// <summary><c>lvmin</c>.</summary>
    Lvmin,

    /// <summary><c>lvmax</c>.</summary>
    Lvmax,

    /// <summary><c>dvw</c>.</summary>
    Dvw,

    /// <summary><c>dvh</c>.</summary>
    Dvh,

    /// <summary><c>dvi</c>.</summary>
    Dvi,

    /// <summary><c>dvb</c>.</summary>
    Dvb,

    /// <summary><c>dvmin</c>.</summary>
    Dvmin,

    /// <summary><c>dvmax</c>.</summary>
    Dvmax,

    /// <summary><c>cqw</c>.</summary>
    Cqw,

    /// <summary><c>cqh</c>.</summary>
    Cqh,

    /// <summary><c>cqi</c>.</summary>
    Cqi,

    /// <summary><c>cqb</c>.</summary>
    Cqb,

    /// <summary><c>cqmin</c>.</summary>
    Cqmin,

    /// <summary><c>cqmax</c>.</summary>
    Cqmax,

    /// <summary><c>deg</c>, the canonical angle unit.</summary>
    Deg,

    /// <summary><c>grad</c>.</summary>
    Grad,

    /// <summary><c>rad</c>.</summary>
    Rad,

    /// <summary><c>turn</c>.</summary>
    Turn,

    /// <summary><c>s</c>, the canonical time unit.</summary>
    S,

    /// <summary><c>ms</c>.</summary>
    Ms,

    /// <summary><c>Hz</c>, the canonical frequency unit.</summary>
    Hz,

    /// <summary><c>kHz</c>.</summary>
    KHz,

    /// <summary><c>dppx</c>, the canonical resolution unit.</summary>
    Dppx,

    /// <summary><c>x</c>, an alias of <c>dppx</c>.</summary>
    X,

    /// <summary><c>dpi</c>.</summary>
    Dpi,

    /// <summary><c>dpcm</c>.</summary>
    Dpcm,

    /// <summary><c>fr</c>.</summary>
    Fr,
}

/// <summary>Facts about units: names, dimensions, and the factors that convert absolute units to the canonical unit.</summary>
public static class CssUnits
{
    private static readonly string[] Names = BuildNameArray();

    private static string[] BuildNameArray()
    {
        string[] n = new string[(int)CssUnit.Fr + 1];
        foreach (CssUnit u in Enum.GetValues<CssUnit>())
        {
            n[(int)u] = u.ToString().AsciiLower();
        }

        n[(int)CssUnit.XHeight] = "ex";
        n[(int)CssUnit.RootXHeight] = "rex";

        return n;
    }

    private static readonly FrozenDictionary<string, CssUnit> ByName = BuildNames();

    private static readonly FrozenDictionary<string, CssUnit>.AlternateLookup<ReadOnlySpan<char>> Lookup =
        ByName.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>Finds a unit by name, ignoring ASCII case (CSS Values 4 §5: units are ASCII case-insensitive).</summary>
    /// <param name="name">The unit as written.</param>
    /// <param name="unit">The unit, when known.</param>
    public static bool TryParse(ReadOnlySpan<char> name, out CssUnit unit) => Lookup.TryGetValue(name, out unit);

    /// <summary>The dimension of a unit.</summary>
    /// <param name="unit">A unit.</param>
    public static CssDimension GetDimension(CssUnit unit) => unit switch
    {
        CssUnit.None => CssDimension.Number,
        CssUnit.Percent => CssDimension.Percent,
        >= CssUnit.Px and <= CssUnit.Cqmax => CssDimension.Length,
        >= CssUnit.Deg and <= CssUnit.Turn => CssDimension.Angle,
        CssUnit.S or CssUnit.Ms => CssDimension.Time,
        CssUnit.Hz or CssUnit.KHz => CssDimension.Frequency,
        >= CssUnit.Dppx and <= CssUnit.Dpcm => CssDimension.Resolution,
        _ => CssDimension.Flex,
    };

    /// <summary>True for lengths that need no context to become pixels: <c>px cm mm Q in pt pc</c> (CSS Values 4 §6.2).</summary>
    /// <param name="unit">A unit.</param>
    public static bool IsAbsoluteLength(CssUnit unit) => unit is >= CssUnit.Px and <= CssUnit.Pc;

    /// <summary>True for font-relative lengths (<c>em rem ex rex cap rcap ch rch ic ric lh rlh</c>).</summary>
    /// <param name="unit">A unit.</param>
    public static bool IsFontRelative(CssUnit unit) => unit is >= CssUnit.Em and <= CssUnit.Rlh;

    /// <summary>True for viewport-percentage lengths (<c>vw vh vi vb vmin vmax</c> with the <c>s</c>, <c>l</c> and <c>d</c> variants).</summary>
    /// <param name="unit">A unit.</param>
    public static bool IsViewportRelative(CssUnit unit) => unit is >= CssUnit.Vw and <= CssUnit.Dvmax;

    /// <summary>True for container query lengths (<c>cqw cqh cqi cqb cqmin cqmax</c>).</summary>
    /// <param name="unit">A unit.</param>
    public static bool IsContainerRelative(CssUnit unit) => unit is >= CssUnit.Cqw and <= CssUnit.Cqmax;

    /// <summary>
    /// The factor that converts a value in an absolute-length, angle, time, frequency or resolution unit to the
    /// canonical unit of its dimension (px, deg, s, Hz, dppx). Relative units and percentages return 1.
    /// </summary>
    /// <param name="unit">A unit.</param>
    public static double GetCanonicalFactor(CssUnit unit) => unit switch
    {
        CssUnit.Cm => 96.0 / 2.54,
        CssUnit.Mm => 96.0 / 25.4,
        CssUnit.Q => 96.0 / 101.6,
        CssUnit.In => 96.0,
        CssUnit.Pt => 96.0 / 72.0,
        CssUnit.Pc => 16.0,
        CssUnit.Grad => 0.9,
        CssUnit.Rad => 180.0 / Math.PI,
        CssUnit.Turn => 360.0,
        CssUnit.Ms => 0.001,
        CssUnit.KHz => 1000.0,
        CssUnit.Dpi => 1.0 / 96.0,
        CssUnit.Dpcm => 2.54 / 96.0,
        _ => 1.0,
    };

    /// <summary>The canonical unit of a dimension: px, deg, s, Hz, dppx, fr, or none.</summary>
    /// <param name="dimension">A dimension.</param>
    public static CssUnit GetCanonicalUnit(CssDimension dimension) => dimension switch
    {
        CssDimension.Length => CssUnit.Px,
        CssDimension.Angle => CssUnit.Deg,
        CssDimension.Time => CssUnit.S,
        CssDimension.Frequency => CssUnit.Hz,
        CssDimension.Resolution => CssUnit.Dppx,
        CssDimension.Flex => CssUnit.Fr,
        CssDimension.Percent => CssUnit.Percent,
        _ => CssUnit.None,
    };

    /// <summary>The unit's name as serialized: lower case, except <c>Q</c>, <c>Hz</c> and <c>kHz</c> (CSS Values 4 §5.1 serialization).</summary>
    /// <param name="unit">A unit.</param>
    public static string GetName(CssUnit unit) => unit switch
    {
        CssUnit.None => string.Empty,
        CssUnit.Percent => "%",
        CssUnit.Q => "Q",
        CssUnit.Hz => "Hz",
        CssUnit.KHz => "kHz",
        _ => Names[(int)unit],
    };

    private static FrozenDictionary<string, CssUnit> BuildNames()
    {
        var d = new Dictionary<string, CssUnit>(StringComparer.OrdinalIgnoreCase);
        foreach (CssUnit u in Enum.GetValues<CssUnit>())
        {
            if (u is CssUnit.None or CssUnit.Percent)
            {
                continue;
            }

            d[GetName(u)] = u;
        }

        return d.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    internal static string FormatNumber(double v)
    {
        // CSSOM "serialize a CSS component value" for numbers: shortest round-trip decimal, no exponent for the
        // magnitudes that matter here, "-0" kept as 0.
        if (double.IsNaN(v))
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(v))
        {
            return "infinity";
        }

        if (double.IsNegativeInfinity(v))
        {
            return "-infinity";
        }

        if (v == 0)
        {
            return 1 / v < 0 ? "-0" : "0";
        }

        string s = v.ToString("R", CultureInfo.InvariantCulture);
        if (s.Contains('E', StringComparison.Ordinal))
        {
            s = v.ToString("0.####################", CultureInfo.InvariantCulture);
        }

        return s;
    }
}
