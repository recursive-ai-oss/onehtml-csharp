using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Css;

/// <summary>Small value classifiers shared by the shorthand grammars and the typed parsers: keyword sets, lengths, times, colors, images.</summary>
internal static class ValueKinds
{
    public static FrozenSet<string> Set(params string[] words) => words.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool In(FrozenSet<string> set, ReadOnlySpan<char> word) => set.GetAlternateLookup<ReadOnlySpan<char>>().Contains(word);

    public static readonly FrozenSet<string> LineStyles = Set("none", "hidden", "dotted", "dashed", "solid", "double", "groove", "ridge", "inset", "outset");
    public static readonly FrozenSet<string> LineWidths = Set("thin", "medium", "thick");
    public static readonly FrozenSet<string> OverflowKeywords = Set("visible", "hidden", "clip", "scroll", "auto");
    public static readonly FrozenSet<string> ImageFunctions = Set(
        "linear-gradient", "radial-gradient", "conic-gradient", "repeating-linear-gradient", "repeating-radial-gradient",
        "repeating-conic-gradient", "-webkit-linear-gradient", "-webkit-radial-gradient", "-webkit-repeating-linear-gradient",
        "-webkit-repeating-radial-gradient", "-webkit-gradient", "image-set", "-webkit-image-set", "image", "cross-fade",
        "-webkit-cross-fade", "element", "-moz-element", "paint", "url", "src");
    public static readonly FrozenSet<string> GradientFunctions = Set(
        "linear-gradient", "radial-gradient", "conic-gradient", "repeating-linear-gradient", "repeating-radial-gradient",
        "repeating-conic-gradient", "-webkit-linear-gradient", "-webkit-radial-gradient", "-webkit-repeating-linear-gradient",
        "-webkit-repeating-radial-gradient", "-webkit-gradient");

    /// <summary>Index after a component value at <paramref name="i"/> when it is the keyword set's member, else -1.</summary>
    public static int Keyword(TokenRange r, int i, FrozenSet<string> set) =>
        i < r.To && r.Kind(i) == CssTokenKind.Ident && In(set, r.Name(i, out _)) ? i + 1 : -1;

    public static int LengthPercentage(TokenRange r, int i, bool nonNegative = false, bool allowAuto = false, bool allowKeywordNone = false)
    {
        if (i >= r.To)
        {
            return -1;
        }

        if (allowAuto && r.IsIdent(i, "auto"))
        {
            return i + 1;
        }

        if (allowKeywordNone && r.IsIdent(i, "none"))
        {
            return i + 1;
        }

        QuantityKinds k = QuantityKinds.LengthPercentage | QuantityKinds.UnitlessZeroLength | (nonNegative ? QuantityKinds.NonNegative : 0);
        return CssQuantity.TryParse(r, i, k, null, out _, out int next) ? next : -1;
    }

    public static int Length(TokenRange r, int i, bool nonNegative = false)
    {
        if (i >= r.To)
        {
            return -1;
        }

        QuantityKinds k = QuantityKinds.Length | QuantityKinds.UnitlessZeroLength | (nonNegative ? QuantityKinds.NonNegative : 0);
        return CssQuantity.TryParse(r, i, k, null, out _, out int next) ? next : -1;
    }

    public static int LineWidth(TokenRange r, int i)
    {
        int k = Keyword(r, i, LineWidths);
        return k >= 0 ? k : Length(r, i, nonNegative: true);
    }

    public static int LineStyle(TokenRange r, int i) => Keyword(r, i, LineStyles);

    public static int Color(TokenRange r, int i) =>
        i < r.To && CssColorParser.TryParseAt(r, i, ColorParseOptions.Default, 0, out _, out int next) ? next : -1;

    public static int Time(TokenRange r, int i) =>
        i < r.To && CssQuantity.TryParse(r, i, QuantityKinds.Time, null, out _, out int next) ? next : -1;

    public static int Number(TokenRange r, int i, bool nonNegative = true) =>
        i < r.To && CssQuantity.TryParse(r, i, QuantityKinds.Number | (nonNegative ? QuantityKinds.NonNegative : 0), null, out _, out int next) ? next : -1;

    public static int Image(TokenRange r, int i)
    {
        if (i >= r.To)
        {
            return -1;
        }

        switch (r.Kind(i))
        {
            case CssTokenKind.Url:
                return i + 1;
            case CssTokenKind.Function:
                return In(ImageFunctions, r.Name(i, out _)) ? r.Next(i) : -1;
            case CssTokenKind.Ident:
                return r.IsIdent(i, "none") ? i + 1 : -1;
            default:
                return -1;
        }
    }

    public static bool IsComma(TokenRange r, int i) => i < r.To && r.Kind(i) == CssTokenKind.Comma;

    public static bool IsSlash(TokenRange r, int i) => i < r.To && r.IsDelim(i, '/');
}
