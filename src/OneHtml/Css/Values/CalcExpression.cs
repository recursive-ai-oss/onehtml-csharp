using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneHtml.Css;

/// <summary>
/// The context that resolves relative lengths (CSS Values 4 §6.1 font-relative, §6.1.2 viewport-percentage, CSS
/// Conditional/Containment container units). The cascade supplies the numbers; this library never measures fonts or
/// layout, so the metric ratios are inputs with the common fallbacks as defaults.
/// </summary>
public readonly record struct LengthContext
{
    /// <summary>The element's computed <c>font-size</c> in px (<c>em</c>). Default 16.</summary>
    public double FontSize { get; init; }

    /// <summary>The root element's computed <c>font-size</c> in px (<c>rem</c>). Default 16.</summary>
    public double RootFontSize { get; init; }

    /// <summary>The element's computed <c>line-height</c> in px (<c>lh</c>); 0 means "normal", taken as 1.2 × font size.</summary>
    public double LineHeight { get; init; }

    /// <summary>The root element's computed <c>line-height</c> in px (<c>rlh</c>); 0 means 1.2 × root font size.</summary>
    public double RootLineHeight { get; init; }

    /// <summary>x-height as a fraction of the font size (<c>ex</c>). Default 0.5, the fallback CSS Values 4 §6.1.1 allows when the font has no metric.</summary>
    public double ExRatio { get; init; }

    /// <summary>Cap height as a fraction of the font size (<c>cap</c>). Default 0.7.</summary>
    public double CapRatio { get; init; }

    /// <summary>Advance of "0" as a fraction of the font size (<c>ch</c>). Default 0.5, the fallback §6.1.1 gives for a font without a "0".</summary>
    public double ChRatio { get; init; }

    /// <summary>Advance of "水" (U+6C34) as a fraction of the font size (<c>ic</c>). Default 1, the fallback §6.1.1 gives.</summary>
    public double IcRatio { get; init; }

    /// <summary>
    /// x-height of the <em>root element's</em> font as a fraction of the root font size (<c>rex</c>, CSS Values 4 §6.1.1). 0 means unset:
    /// <see cref="ExRatio"/> is used, which is exact when the root and the element share a font. A layout engine that measures fonts sets it.
    /// </summary>
    public double RootExRatio { get; init; }

    /// <summary>Cap height of the root element's font as a fraction of the root font size (<c>rcap</c>). 0 means unset: <see cref="CapRatio"/> is used.</summary>
    public double RootCapRatio { get; init; }

    /// <summary>Advance of "0" in the root element's font as a fraction of the root font size (<c>rch</c>). 0 means unset: <see cref="ChRatio"/> is used.</summary>
    public double RootChRatio { get; init; }

    /// <summary>Advance of "水" in the root element's font as a fraction of the root font size (<c>ric</c>). 0 means unset: <see cref="IcRatio"/> is used.</summary>
    public double RootIcRatio { get; init; }

    /// <summary>Small viewport width in px. Default 0.</summary>
    public double ViewportWidth { get; init; }

    /// <summary>Small viewport height in px. Default 0.</summary>
    public double ViewportHeight { get; init; }

    /// <summary>Large viewport width in px; 0 means the same as <see cref="ViewportWidth"/>.</summary>
    public double LargeViewportWidth { get; init; }

    /// <summary>Large viewport height in px; 0 means the same as <see cref="ViewportHeight"/>.</summary>
    public double LargeViewportHeight { get; init; }

    /// <summary>Dynamic viewport width in px; 0 means the same as <see cref="ViewportWidth"/>.</summary>
    public double DynamicViewportWidth { get; init; }

    /// <summary>Dynamic viewport height in px; 0 means the same as <see cref="ViewportHeight"/>.</summary>
    public double DynamicViewportHeight { get; init; }

    /// <summary>The query container's content width in px for <c>cqw</c>; NaN or unset means "no container", and the small viewport is used (Containment §4.1).</summary>
    public double ContainerWidth { get; init; }

    /// <summary>The query container's content height in px for <c>cqh</c>; 0 means no container.</summary>
    public double ContainerHeight { get; init; }

    /// <summary>True when the writing mode is vertical, which swaps the inline and block axes for <c>vi</c>/<c>vb</c>/<c>cqi</c>/<c>cqb</c>.</summary>
    public bool VerticalWritingMode { get; init; }

    /// <summary>True when a query container exists (so <see cref="ContainerWidth"/> and <see cref="ContainerHeight"/> are meaningful).</summary>
    public bool HasContainer { get; init; }

    /// <summary>A context with every default filled in: 16px fonts, 0×0 viewport, no container.</summary>
    public static LengthContext Default { get; } = new()
    {
        FontSize = 16,
        RootFontSize = 16,
        ExRatio = 0.5,
        CapRatio = 0.7,
        ChRatio = 0.5,
        IcRatio = 1,
    };

    /// <summary>Converts a length to px. Absolute units use the CSS reference pixel (§6.2); percentages and non-length units return NaN.</summary>
    /// <param name="value">The number.</param>
    /// <param name="unit">A length unit.</param>
    public double ToPx(double value, CssUnit unit)
    {
        if (CssUnits.IsAbsoluteLength(unit))
        {
            return value * CssUnits.GetCanonicalFactor(unit);
        }

        double fs = FontSize;
        double rfs = RootFontSize;
        double vw = ViewportWidth;
        double vh = ViewportHeight;
        double lvw = LargeViewportWidth != 0 ? LargeViewportWidth : vw;
        double lvh = LargeViewportHeight != 0 ? LargeViewportHeight : vh;
        double dvw = DynamicViewportWidth != 0 ? DynamicViewportWidth : vw;
        double dvh = DynamicViewportHeight != 0 ? DynamicViewportHeight : vh;
        bool vert = VerticalWritingMode;
        double cw = HasContainer ? ContainerWidth : vw;
        double ch = HasContainer ? ContainerHeight : vh;
        double basis = unit switch
        {
            CssUnit.Em => fs,
            CssUnit.Rem => rfs,
            CssUnit.XHeight => fs * ExRatio,
            CssUnit.RootXHeight => rfs * (RootExRatio != 0 ? RootExRatio : ExRatio),
            CssUnit.Cap => fs * CapRatio,
            CssUnit.Rcap => rfs * (RootCapRatio != 0 ? RootCapRatio : CapRatio),
            CssUnit.Ch => fs * ChRatio,
            CssUnit.Rch => rfs * (RootChRatio != 0 ? RootChRatio : ChRatio),
            CssUnit.Ic => fs * IcRatio,
            CssUnit.Ric => rfs * (RootIcRatio != 0 ? RootIcRatio : IcRatio),
            CssUnit.Lh => LineHeight != 0 ? LineHeight : fs * 1.2,
            CssUnit.Rlh => RootLineHeight != 0 ? RootLineHeight : rfs * 1.2,
            CssUnit.Vw or CssUnit.Svw => vw / 100,
            CssUnit.Vh or CssUnit.Svh => vh / 100,
            CssUnit.Vi or CssUnit.Svi => (vert ? vh : vw) / 100,
            CssUnit.Vb or CssUnit.Svb => (vert ? vw : vh) / 100,
            CssUnit.Vmin or CssUnit.Svmin => Math.Min(vw, vh) / 100,
            CssUnit.Vmax or CssUnit.Svmax => Math.Max(vw, vh) / 100,
            CssUnit.Lvw => lvw / 100,
            CssUnit.Lvh => lvh / 100,
            CssUnit.Lvi => (vert ? lvh : lvw) / 100,
            CssUnit.Lvb => (vert ? lvw : lvh) / 100,
            CssUnit.Lvmin => Math.Min(lvw, lvh) / 100,
            CssUnit.Lvmax => Math.Max(lvw, lvh) / 100,
            CssUnit.Dvw => dvw / 100,
            CssUnit.Dvh => dvh / 100,
            CssUnit.Dvi => (vert ? dvh : dvw) / 100,
            CssUnit.Dvb => (vert ? dvw : dvh) / 100,
            CssUnit.Dvmin => Math.Min(dvw, dvh) / 100,
            CssUnit.Dvmax => Math.Max(dvw, dvh) / 100,
            CssUnit.Cqw => cw / 100,
            CssUnit.Cqh => ch / 100,
            CssUnit.Cqi => (vert ? ch : cw) / 100,
            CssUnit.Cqb => (vert ? cw : ch) / 100,
            CssUnit.Cqmin => Math.Min(cw, ch) / 100,
            CssUnit.Cqmax => Math.Max(cw, ch) / 100,
            _ => double.NaN,
        };
        return value * basis;
    }
}

/// <summary>The base types a calculation's type tracks (CSS Values 4 §10.3 "types").</summary>
internal enum CalcBase
{
    Length,
    Angle,
    Time,
    Frequency,
    Resolution,
    Flex,
    Percent,
}

/// <summary>
/// A calculation type: an exponent for each base type, plus whether a percentage is mixed in (the "percent hint",
/// CSS Values 4 §10.3). A plain number has all exponents zero.
/// </summary>
internal readonly struct CalcType : IEquatable<CalcType>
{
    private const int Bases = 7;
    private readonly long _packed; // 8 bits per base, signed exponent
    private readonly bool _hint;

    private CalcType(long packed, bool hint)
    {
        _packed = packed;
        _hint = hint;
    }

    public static CalcType Number => default;

    public static CalcType Of(CalcBase b, int exp = 1) => new((long)(byte)(sbyte)exp << (8 * (int)b), false);

    public static CalcType OfDimension(CssDimension d) => d switch
    {
        CssDimension.Number => Number,
        CssDimension.Length => Of(CalcBase.Length),
        CssDimension.Angle => Of(CalcBase.Angle),
        CssDimension.Time => Of(CalcBase.Time),
        CssDimension.Frequency => Of(CalcBase.Frequency),
        CssDimension.Resolution => Of(CalcBase.Resolution),
        CssDimension.Flex => Of(CalcBase.Flex),
        _ => Of(CalcBase.Percent),
    };

    public int Exp(CalcBase b) => (sbyte)(byte)(_packed >> (8 * (int)b));

    public bool IsNumber => _packed == 0;

    public bool IsPurePercent => _packed == ((long)1 << (8 * (int)CalcBase.Percent));

    public bool HasPercentHint => _hint;

    public CalcType WithHint() => new(_packed, true);

    public CalcType Mul(CalcType o)
    {
        long r = 0;
        for (int i = 0; i < Bases; i++)
        {
            int e = Exp((CalcBase)i) + o.Exp((CalcBase)i);
            if (e is < -100 or > 100)
            {
                e = 100;
            }

            r |= (long)(byte)(sbyte)e << (8 * i);
        }

        return new CalcType(r, _hint || o._hint);
    }

    public CalcType Invert()
    {
        long r = 0;
        for (int i = 0; i < Bases; i++)
        {
            r |= (long)(byte)(sbyte)(-Exp((CalcBase)i)) << (8 * i);
        }

        return new CalcType(r, _hint);
    }

    /// <summary>
    /// The type of a sum (§10.3 "add two types"): equal types add; a percentage adds to a dimension (the result has a
    /// percent hint) because it resolves to that dimension; anything else fails.
    /// </summary>
    public bool TryAdd(CalcType o, out CalcType result)
    {
        if (_packed == o._packed)
        {
            result = new CalcType(_packed, _hint || o._hint);
            return true;
        }

        if (o.IsPurePercent && IsSingleDimension)
        {
            result = WithHint();
            return true;
        }

        if (IsPurePercent && o.IsSingleDimension)
        {
            result = o.WithHint();
            return true;
        }

        result = default;
        return false;
    }

    private bool IsSingleDimension
    {
        get
        {
            int nonZero = 0;
            for (int i = 0; i < Bases; i++)
            {
                int e = Exp((CalcBase)i);
                if (e != 0)
                {
                    if (e != 1 || i == (int)CalcBase.Percent)
                    {
                        return false;
                    }

                    nonZero++;
                }
            }

            return nonZero == 1;
        }
    }

    /// <summary>The dimension of a single-dimension type (or a number); null for compound types such as length².</summary>
    public CssDimension? Dimension
    {
        get
        {
            if (IsNumber)
            {
                return CssDimension.Number;
            }

            if (IsPurePercent)
            {
                return CssDimension.Percent;
            }

            if (!IsSingleDimension)
            {
                return null;
            }

            for (int i = 0; i < Bases - 1; i++)
            {
                if (Exp((CalcBase)i) == 1)
                {
                    return (CssDimension)(i + 1);
                }
            }

            return null;
        }
    }

    public bool Equals(CalcType other) => _packed == other._packed && _hint == other._hint;

    public override bool Equals(object? obj) => obj is CalcType t && Equals(t);

    public override int GetHashCode() => HashCode.Combine(_packed, _hint);
}

internal enum CalcOp : byte
{
    Const,
    Sum,
    Product,
    Negate,
    Invert,
    Min,
    Max,
    Clamp,
    Round,
    Mod,
    Rem,
    Sin,
    Cos,
    Tan,
    Asin,
    Acos,
    Atan,
    Atan2,
    Pow,
    Sqrt,
    Hypot,
    Log,
    Exp,
    Abs,
    Sign,
}

/// <summary>One node of a flat expression. Children always have smaller indexes, so evaluation is a single forward pass.</summary>
internal struct CalcNode
{
    public CalcOp Op;
    public CssUnit Unit;     // Const: the unit (canonical for absolute units)
    public byte Strategy;    // Round: 0 nearest, 1 up, 2 down, 3 to-zero
    public bool Relative;    // contains a percentage, font-relative, viewport or container unit
    public bool Paren;       // wrote as calc()/parens; used by the serializer
    public double Value;
    public int ArgStart;
    public int ArgCount;
    public CalcType Type;
}

/// <summary>
/// A parsed <c>calc()</c> or math function (CSS Values 4 §10): a typed expression tree stored flat. An expression with
/// no relative units is folded to a single value at parse time and never reaches this class (see
/// <see cref="CssQuantity"/>); what remains needs a <see cref="LengthContext"/> and, for percentages, a basis.
/// </summary>
public sealed class CalcExpression
{
    internal readonly CalcNode[] Nodes;
    internal readonly int[] Args;
    internal readonly int Root;

    internal CalcExpression(CalcNode[] nodes, int[] args, int root)
    {
        Nodes = nodes;
        Args = args;
        Root = root;
    }

    internal CalcType Type => Nodes[Root].Type;

    /// <summary>The dimension of the result: number, length, angle, time, frequency, resolution or flex; percent only for a pure percentage.</summary>
    public CssDimension Dimension => Type.Dimension ?? CssDimension.Number;

    /// <summary>True when the result still contains a percentage that needs a basis.</summary>
    public bool ContainsPercentage
    {
        get
        {
            foreach (CalcNode n in Nodes)
            {
                if (n.Op == CalcOp.Const && n.Unit == CssUnit.Percent)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Evaluates the expression. Lengths come out in px, angles in degrees, times in seconds, frequencies in Hz,
    /// resolutions in dppx; a percentage is a fraction of <paramref name="percentBasis"/> in the same canonical
    /// unit (for a number-valued calculation the basis is 1).
    /// </summary>
    /// <param name="context">Resolves font-relative, viewport and container lengths.</param>
    /// <param name="percentBasis">What 100% means; in the canonical unit of the result.</param>
    public double Resolve(in LengthContext context, double percentBasis = 0)
    {
        double[] v = new double[Root + 1];
        for (int i = 0; i <= Root; i++)
        {
            v[i] = EvalNode(i, v, in context, percentBasis);
        }

        return v[Root];
    }

    private double EvalNode(int i, double[] v, in LengthContext ctx, double basis)
    {
        ref readonly CalcNode n = ref Nodes[i];
        switch (n.Op)
        {
            case CalcOp.Const:
                if (n.Unit == CssUnit.Percent)
                {
                    return n.Value / 100 * basis;
                }

                if (CssUnits.GetDimension(n.Unit) == CssDimension.Length && !CssUnits.IsAbsoluteLength(n.Unit) && n.Unit != CssUnit.Px)
                {
                    return ctx.ToPx(n.Value, n.Unit);
                }

                return n.Value;
            case CalcOp.Sum:
            {
                double s = 0;
                for (int k = 0; k < n.ArgCount; k++)
                {
                    s += v[Args[n.ArgStart + k]];
                }

                return s;
            }

            case CalcOp.Product:
            {
                double p = 1;
                for (int k = 0; k < n.ArgCount; k++)
                {
                    p *= v[Args[n.ArgStart + k]];
                }

                return p;
            }

            case CalcOp.Negate:
                return -v[Args[n.ArgStart]];
            case CalcOp.Invert:
                return 1 / v[Args[n.ArgStart]];
            default:
                return CalcMath.Apply(n.Op, n.Strategy, v, Args, n.ArgStart, n.ArgCount);
        }
    }

    /// <summary>The expression in canonical <c>calc(...)</c> form (§10.12).</summary>
    public string ToCss() => CalcSerializer.Serialize(this);

    /// <inheritdoc />
    public override string ToString() => ToCss();
}

internal static class CalcMath
{
    public static double Apply(CalcOp op, byte strategy, double[] v, int[] args, int start, int count)
    {
        double a = count > 0 ? v[args[start]] : 0;
        double b = count > 1 ? v[args[start + 1]] : 0;
        switch (op)
        {
            case CalcOp.Min:
            case CalcOp.Max:
            {
                double r = a;
                for (int k = 1; k < count; k++)
                {
                    double x = v[args[start + k]];
                    if (double.IsNaN(x) || double.IsNaN(r))
                    {
                        r = double.NaN;
                    }
                    else
                    {
                        r = op == CalcOp.Min ? Math.Min(r, x) : Math.Max(r, x);
                    }
                }

                return r;
            }

            case CalcOp.Clamp:
            {
                double lo = a;
                double val = b;
                double hi = v[args[start + 2]];
                // Values 4 §10.4.1: clamp(MIN, VAL, MAX) is max(MIN, min(VAL, MAX)); "none" bounds arrive as ∓infinity.
                if (double.IsNaN(lo) || double.IsNaN(val) || double.IsNaN(hi))
                {
                    return double.NaN;
                }

                return Math.Max(lo, Math.Min(val, hi));
            }

            case CalcOp.Round:
                return Round(strategy, a, b);
            case CalcOp.Mod:
                if (b == 0 || double.IsInfinity(a) || double.IsNaN(a) || double.IsNaN(b))
                {
                    return double.NaN;
                }

                if (double.IsInfinity(b))
                {
                    return double.IsNegative(a) == double.IsNegative(b) ? a : double.NaN;
                }

                return a - (b * Math.Floor(a / b));
            case CalcOp.Rem:
                if (b == 0 || double.IsInfinity(a) || double.IsNaN(a) || double.IsNaN(b))
                {
                    return double.NaN;
                }

                if (double.IsInfinity(b))
                {
                    return a;
                }

                return a % b;
            case CalcOp.Sin:
                return Math.Sin(strategy == 1 ? a : a * Math.PI / 180);
            case CalcOp.Cos:
                return Math.Cos(strategy == 1 ? a : a * Math.PI / 180);
            case CalcOp.Tan:
                return Math.Tan(strategy == 1 ? a : a * Math.PI / 180);
            case CalcOp.Asin:
                return Math.Asin(a) * 180 / Math.PI;
            case CalcOp.Acos:
                return Math.Acos(a) * 180 / Math.PI;
            case CalcOp.Atan:
                return Math.Atan(a) * 180 / Math.PI;
            case CalcOp.Atan2:
                return Math.Atan2(a, b) * 180 / Math.PI;
            case CalcOp.Pow:
                return Math.Pow(a, b);
            case CalcOp.Sqrt:
                return Math.Sqrt(a);
            case CalcOp.Hypot:
            {
                double s = 0;
                for (int k = 0; k < count; k++)
                {
                    double x = v[args[start + k]];
                    s += x * x;
                }

                return Math.Sqrt(s);
            }

            case CalcOp.Log:
                return count > 1 ? Math.Log(a) / Math.Log(b) : Math.Log(a);
            case CalcOp.Exp:
                return Math.Exp(a);
            case CalcOp.Abs:
                return Math.Abs(a);
            case CalcOp.Sign:
                return double.IsNaN(a) ? double.NaN : a > 0 ? 1 : a < 0 ? -1 : a;
            default:
                return double.NaN;
        }
    }

    // Values 4 §10.4.2 round(): the step B rounds A to a multiple.
    private static double Round(byte strategy, double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b) || b == 0 || double.IsInfinity(a))
        {
            return double.NaN;
        }

        if (double.IsInfinity(b))
        {
            // Values 4 §10.4.2: an infinite step rounds a finite A to zero, or to infinity when rounding away from it.
            bool neg = a < 0 || (a == 0 && double.IsNegative(a));
            return strategy switch
            {
                1 when a > 0 => double.PositiveInfinity,
                2 when a < 0 => double.NegativeInfinity,
                _ => neg ? -0.0 : 0.0,
            };
        }

        b = Math.Abs(b);
        double q = a / b;
        double r = strategy switch
        {
            1 => Math.Ceiling(q),
            2 => Math.Floor(q),
            3 => Math.Truncate(q),
            _ => Math.Floor(q + 0.5), // ties go toward +infinity
        };
        return r * b;
    }
}
