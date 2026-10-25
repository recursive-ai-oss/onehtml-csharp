using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Css;

/// <summary>Which numeric forms <see cref="CssQuantity.TryParse(ComponentValueList, QuantityKinds, out CssQuantity)"/> accepts.</summary>
[Flags]
public enum QuantityKinds
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>A <c>&lt;number&gt;</c>.</summary>
    Number = 1,

    /// <summary>Restricts numbers to integers when combined with <see cref="Number"/>; math functions are accepted and rounded at use.</summary>
    IntegerOnly = 2,

    /// <summary>A <c>&lt;percentage&gt;</c>.</summary>
    Percentage = 4,

    /// <summary>A <c>&lt;length&gt;</c>.</summary>
    Length = 8,

    /// <summary>An <c>&lt;angle&gt;</c>.</summary>
    Angle = 16,

    /// <summary>A <c>&lt;time&gt;</c>.</summary>
    Time = 32,

    /// <summary>A <c>&lt;frequency&gt;</c>.</summary>
    Frequency = 64,

    /// <summary>A <c>&lt;resolution&gt;</c>.</summary>
    Resolution = 128,

    /// <summary>A <c>&lt;flex&gt;</c> (<c>fr</c>).</summary>
    Flex = 256,

    /// <summary>With <see cref="Length"/>, the number 0 counts as a length (CSS Values 4 §6: "unitless zero").</summary>
    UnitlessZeroLength = 512,

    /// <summary>Negative literal values are rejected (the property's range is <c>[0,∞]</c>). Math functions clamp at use time instead.</summary>
    NonNegative = 1024,

    /// <summary><c>&lt;length-percentage&gt;</c>.</summary>
    LengthPercentage = Length | Percentage,
}

/// <summary>
/// A parsed numeric value: a number, percentage or dimension as written, or the result of a math function. Cheap
/// to copy; the expression of an unfoldable math function is shared.
/// </summary>
public readonly struct CssQuantity : IEquatable<CssQuantity>
{
    internal CssQuantity(double value, CssUnit unit, CalcExpression? calc, bool fromMath)
    {
        // A literal too large for a double (1e999) is held at the largest finite value, since only a math function can
        // write infinity (Values 4 section 10.9); this keeps ToCss output parseable.
        Value = !fromMath && calc is null && double.IsInfinity(value) ? Math.CopySign(double.MaxValue, value) : value;
        Unit = unit;
        Calc = calc;
        FromMath = fromMath;
    }

    /// <summary>The number as written (or the folded result of a math function, in the canonical unit). Unused when <see cref="Calc"/> is set.</summary>
    public double Value { get; }

    /// <summary>The unit as written; <see cref="CssUnit.None"/> for a number and <see cref="CssUnit.Percent"/> for a percentage. A folded math function carries the canonical unit.</summary>
    public CssUnit Unit { get; }

    /// <summary>The expression of a math function that still needs context (relative units or percentages); null otherwise.</summary>
    public CalcExpression? Calc { get; }

    /// <summary>True when the value came from <c>calc()</c> or another math function (it serializes as <c>calc(...)</c>).</summary>
    public bool FromMath { get; }

    /// <summary>The dimension.</summary>
    public CssDimension Dimension => Calc is not null ? Calc.Dimension : CssUnits.GetDimension(Unit);

    /// <summary>True when the value is a length or percentage that needs no context to resolve (an absolute length).</summary>
    public bool IsAbsolute => Calc is null && Unit != CssUnit.Percent && !(CssUnits.GetDimension(Unit) == CssDimension.Length && !CssUnits.IsAbsoluteLength(Unit) && Unit != CssUnit.Px);

    /// <summary>True when the value is, or contains, a percentage.</summary>
    public bool IsPercentage => Calc is not null ? Calc.ContainsPercentage : Unit == CssUnit.Percent;

    /// <summary>
    /// The value in the canonical unit of its dimension (px, deg, s, Hz, dppx). A percentage is that fraction of
    /// <paramref name="percentBasis"/> (already in the canonical unit; for a number-percentage use 1).
    /// </summary>
    /// <param name="context">Resolves font-relative, viewport and container lengths.</param>
    /// <param name="percentBasis">What 100% is.</param>
    public double Resolve(in LengthContext context, double percentBasis = 0)
    {
        if (Calc is not null)
        {
            return Calc.Resolve(in context, percentBasis);
        }

        if (Unit == CssUnit.Percent)
        {
            return Value / 100 * percentBasis;
        }

        if (CssUnits.GetDimension(Unit) == CssDimension.Length)
        {
            return FromMath && Unit == CssUnit.Px ? Value : context.ToPx(Value, Unit);
        }

        return Value * CssUnits.GetCanonicalFactor(Unit);
    }

    /// <summary>The value as CSS text: <c>12px</c>, <c>50%</c>, or <c>calc(...)</c> for a math function (CSS Values 4 §10.13).</summary>
    public string ToCss()
    {
        if (Calc is not null)
        {
            return Calc.ToCss();
        }

        if (FromMath)
        {
            if (double.IsNaN(Value) || double.IsInfinity(Value))
            {
                var n = new CalcNode { Op = CalcOp.Const, Value = Value, Unit = Unit, Type = CalcType.OfDimension(CssUnits.GetDimension(Unit)) };
                return "calc(" + CalcSerializer.InfinityText(n) + ")";
            }

            return "calc(" + CssUnits.FormatNumber(Value) + CssUnits.GetName(Unit) + ")";
        }

        return CssUnits.FormatNumber(Value) + CssUnits.GetName(Unit);
    }

    /// <inheritdoc />
    public override string ToString() => ToCss();

    /// <inheritdoc />
    public bool Equals(CssQuantity other) => ToCss() == other.ToCss();

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CssQuantity q && Equals(q);

    /// <inheritdoc />
    public override int GetHashCode() => ToCss().GetHashCode(StringComparison.Ordinal);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(CssQuantity left, CssQuantity right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(CssQuantity left, CssQuantity right) => !left.Equals(right);

    /// <summary>
    /// Parses a list holding exactly one number, percentage, dimension or math function (whitespace around it is
    /// ignored). A <c>var()</c> must have been substituted already.
    /// </summary>
    /// <param name="value">The component values.</param>
    /// <param name="accept">The forms to accept.</param>
    /// <param name="quantity">The parsed value.</param>
    public static bool TryParse(ComponentValueList value, QuantityKinds accept, out CssQuantity quantity)
    {
        quantity = default;
        TokenRange r = TokenRange.Of(value).Trim();
        if (r.IsEmpty || r.Next(r.From) != r.To)
        {
            return false;
        }

        return TryParse(r, r.From, accept, null, out quantity, out _);
    }

    internal static bool TryParse(TokenRange r, int i, QuantityKinds accept, Dictionary<string, double>? symbols, out CssQuantity q, out int next)
    {
        q = default;
        next = r.Next(i);
        bool nonNeg = (accept & QuantityKinds.NonNegative) != 0;
        switch (r.Kind(i))
        {
            case CssTokenKind.Number:
            {
                double v = r.Number(i);
                if ((accept & QuantityKinds.Number) != 0)
                {
                    if ((accept & QuantityKinds.IntegerOnly) != 0 && !r.IsInteger(i))
                    {
                        return false;
                    }

                    if (nonNeg && v < 0)
                    {
                        return false;
                    }

                    q = new CssQuantity(v, CssUnit.None, null, false);
                    return true;
                }

                if (v == 0 && (accept & QuantityKinds.Length) != 0 && (accept & QuantityKinds.UnitlessZeroLength) != 0)
                {
                    q = new CssQuantity(0, CssUnit.Px, null, false);
                    return true;
                }

                return false;
            }

            case CssTokenKind.Percentage:
            {
                double v = r.Number(i);
                if ((accept & QuantityKinds.Percentage) == 0 || (nonNeg && v < 0))
                {
                    return false;
                }

                q = new CssQuantity(v, CssUnit.Percent, null, false);
                return true;
            }

            case CssTokenKind.Dimension:
            {
                if (!r.TryUnit(i, out CssUnit unit))
                {
                    return false;
                }

                double v = r.Number(i);
                if (!AcceptsDimension(accept, CssUnits.GetDimension(unit)) || (nonNeg && v < 0))
                {
                    return false;
                }

                q = new CssQuantity(v, unit, null, false);
                return true;
            }

            case CssTokenKind.Function:
            {
                if (!CalcParser.TryParse(r, i, symbols, out CalcParser.Result res))
                {
                    return false;
                }

                if (!AcceptsType(accept, res.Type))
                {
                    return false;
                }

                if ((accept & QuantityKinds.Percentage) == 0)
                {
                    // Values 4 §10.8: a percentage anywhere in a calculation is invalid where the property takes no percentage,
                    // even when it cancels out, as in sign(50%).
                    for (int k = i + 1; k < r.Close(i); k++)
                    {
                        if (r.Kind(k) == CssTokenKind.Percentage)
                        {
                            return false;
                        }
                    }
                }

                q = res.IsConstant
                    ? new CssQuantity(res.Value, res.Unit, null, true)
                    : new CssQuantity(0, CssUnit.None, res.Expression, true);
                return true;
            }

            default:
                return false;
        }
    }

    private static bool AcceptsDimension(QuantityKinds k, CssDimension d) => d switch
    {
        CssDimension.Length => (k & QuantityKinds.Length) != 0,
        CssDimension.Angle => (k & QuantityKinds.Angle) != 0,
        CssDimension.Time => (k & QuantityKinds.Time) != 0,
        CssDimension.Frequency => (k & QuantityKinds.Frequency) != 0,
        CssDimension.Resolution => (k & QuantityKinds.Resolution) != 0,
        CssDimension.Flex => (k & QuantityKinds.Flex) != 0,
        _ => false,
    };

    private static bool AcceptsType(QuantityKinds k, CalcType t)
    {
        CssDimension? d = t.Dimension;
        if (d is null)
        {
            return false;
        }

        bool pct = (k & QuantityKinds.Percentage) != 0;
        if (t.HasPercentHint && !pct)
        {
            return false;
        }

        return d.Value switch
        {
            CssDimension.Number => (k & QuantityKinds.Number) != 0,
            CssDimension.Percent => pct,
            _ => AcceptsDimension(k, d.Value),
        };
    }
}
