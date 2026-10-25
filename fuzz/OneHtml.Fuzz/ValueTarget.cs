using System;
using System.Collections.Generic;
using System.Text;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Fuzz;

/// <summary>
/// Fuzz target for value parsing: colours, quantities and math functions, media queries, <c>@supports</c>,
/// <c>var()</c> substitution, shorthand expansion and the typed property parsers. The first byte picks the text
/// encoding (odd: UTF-16) and a property from the table; the first line of the text is the value under test and
/// every later line is read as <c>--name: value</c>, a custom property for substitution. Only the typed limit
/// exception may escape the library; a serialization that does not parse back to itself is a finding too.
/// </summary>
internal static class ValueTarget
{
    private static readonly CssParseOptions Options = new() { MaxInputLength = 1 << 16, MaxTokens = 1 << 16, MaxNestingDepth = 64 };

    private static readonly VarLimits Limits = new() { MaxOutputLength = 1 << 16, MaxSteps = 1 << 16, MaxTotalLength = 1 << 18 };

    private static readonly QuantityKinds[] Kinds =
    [
        QuantityKinds.Number,
        QuantityKinds.Number | QuantityKinds.IntegerOnly,
        QuantityKinds.LengthPercentage | QuantityKinds.UnitlessZeroLength,
        QuantityKinds.Length | QuantityKinds.NonNegative,
        QuantityKinds.Angle,
        QuantityKinds.Time,
        QuantityKinds.Resolution,
        QuantityKinds.Frequency,
        QuantityKinds.Number | QuantityKinds.Percentage,
    ];

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return;
        }

        int selector = data[0];
        string text = Decode(data);
        string[] lines = text.Split('\n');
        try
        {
            ComponentValueList value = CssTree.ParseComponentValueList(lines[0], Options).Values;
            Colors(value);
            Quantities(value);
            Media(lines[0]);
            Supports(lines[0], value);
            Variables(value, lines);
            Properties(selector, value);
        }
        catch (CssLimitExceededException)
        {
        }
    }

    private static string Decode(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> body = data[1..];
        if ((data[0] & 1) == 1)
        {
            var chars = new char[body.Length / 2];
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = (char)(body[2 * i] | (body[(2 * i) + 1] << 8));
            }

            return new string(chars);
        }

        return Encoding.UTF8.GetString(body);
    }

    private static void Colors(ComponentValueList value)
    {
        foreach (bool dark in new[] { false, true })
        {
            if (!CssColor.TryParse(value, out CssColor c, new ColorParseOptions { DarkScheme = dark }))
            {
                continue;
            }

            string css = c.ToCss();
            _ = c.ToSrgb();
            _ = c.ToRgba8();
            _ = c.Resolve(CssColor.FromRgb(1, 2, 3));
            foreach (ColorSpace space in Enum.GetValues<ColorSpace>())
            {
                _ = c.ConvertTo(space).ToCss();
            }

            // A color serialized from a parsed color parses again, and after that one step it is stable. (Not at the first
            // step: a mix result may lie outside the range that parsing clamps to. System and currentcolor keywords and
            // calc() forms keep their own spelling and are left out.)
            if (!c.IsCurrentColor && c.SystemName is null && !css.Contains("calc(", StringComparison.Ordinal))
            {
                var options = new ColorParseOptions { DarkScheme = dark };
                if (!CssColor.TryParse(css, out CssColor again, options))
                {
                    throw new InvalidOperationException("serialized color does not parse: " + css);
                }

                string css2 = again.ToCss();
                if (!CssColor.TryParse(css2, out CssColor third, options) || third.ToCss() != css2)
                {
                    throw new InvalidOperationException("color does not settle: " + css + " -> " + css2);
                }
            }
        }
    }

    private static void Quantities(ComponentValueList value)
    {
        LengthContext context = LengthContext.Default with { ViewportWidth = 800, ViewportHeight = 600, HasContainer = true, ContainerWidth = 300, ContainerHeight = 200 };
        foreach (QuantityKinds kinds in Kinds)
        {
            if (!CssQuantity.TryParse(value, kinds, out CssQuantity q))
            {
                continue;
            }

            string css = q.ToCss();
            _ = q.Resolve(context, 100);
            _ = q.Resolve(LengthContext.Default);
            _ = q.IsPercentage;
            _ = q.Dimension;

            // The serialization parses again, and after that one step it is stable (a NaN that still carried a relative
            // unit folds only on the second read).
            if (!CssQuantity.TryParse(CssTree.ParseComponentValueList(css, Options).Values, kinds, out CssQuantity again))
            {
                throw new InvalidOperationException("serialized quantity does not parse: " + css);
            }

            // Products and quotients of dimensions are kept as written and not simplified yet, so their text can still
            // change on the next read; only the rest is required to settle.
            string css2 = again.ToCss();
            if (!css2.Contains('*', StringComparison.Ordinal) && !css2.Contains('/', StringComparison.Ordinal)
                && (!CssQuantity.TryParse(CssTree.ParseComponentValueList(css2, Options).Values, kinds, out CssQuantity third) || third.ToCss() != css2))
            {
                throw new InvalidOperationException("quantity does not settle: " + css + " -> " + css2);
            }
        }
    }

    private static void Media(string text)
    {
        MediaQueryList list = MediaQueryList.Parse(text);
        string css = list.ToCss();
        _ = list.Matches(MediaFeatures.Default);
        _ = list.Matches(MediaFeatures.Default with { Width = 320, Height = 480, ColorScheme = PrefersColorScheme.Dark, ForcedColors = true, Type = MediaType.Print });
        if (MediaQueryList.Parse(css).ToCss() != css)
        {
            throw new InvalidOperationException("media query list does not round-trip: " + css);
        }
    }

    private static void Supports(string text, ComponentValueList value)
    {
        _ = SupportsCondition.Parse(text).Evaluate();
        _ = SupportsCondition.Parse(value, new SupportsOptions { SelectorSupported = static s => s.Length % 2 == 0 }).Evaluate();
        _ = DeclarationSupport.IsSupported("color", value);
        _ = DeclarationSupport.IsSupported("margin", value);
        _ = DeclarationSupport.IsSupported("--x", value);
    }

    private static void Variables(ComponentValueList value, string[] lines)
    {
        var specified = new Dictionary<string, ComponentValueList>(StringComparer.Ordinal);
        for (int i = 1; i < lines.Length && i < 40; i++)
        {
            int colon = lines[i].IndexOf(':', StringComparison.Ordinal);
            if (colon > 2 && lines[i].StartsWith("--", StringComparison.Ordinal))
            {
                specified[lines[i][..colon].Trim()] = CssTree.ParseComponentValueList(lines[i][(colon + 1)..], Options).Values;
            }
        }

        Dictionary<string, string> computed = VarSubstitution.ResolveCustomProperties(specified, Limits);
        _ = VarSubstitution.ContainsVar(value);
        _ = VarSubstitution.IsValidSyntax(value);
        SubstitutionResult result = VarSubstitution.Substitute(value, computed, Limits);
        if (result.IsValid)
        {
            _ = result.Value;
            _ = result.Text.Length;
        }
    }

    private static void Properties(int selector, ComponentValueList value)
    {
        _ = CssWideKeywords.Detect(value);
        _ = DisplayValue.TryParse(value, out _);
        _ = ContentValue.TryParse(value, out _);
        _ = TypedProperties.TryParseVisibility(value, out _);
        _ = TypedProperties.TryParseOpacity(value, out _);
        _ = TypedProperties.TryParsePosition(value, out _);
        _ = TypedProperties.TryParseOffset(value, out _);
        _ = TypedProperties.TryParseOverflow(value, out _);
        _ = TypedProperties.TryParsePointerEvents(value, out _);
        _ = TypedProperties.TryParseSpeak(value, out _);
        _ = TypedProperties.TryParseSpeakAs(value, out _);
        _ = TypedProperties.TryParseFontSize(value, out _);
        _ = TypedProperties.TryParseFontWeight(value, out _);
        _ = TypedProperties.TryParseFontStyle(value, out _);
        _ = TypedProperties.TryParseFontFamily(value, out _);
        _ = TypedProperties.TryParseBackgroundImage(value, out _);
        _ = TypedProperties.TryParseClip(value, out _);
        _ = TypedProperties.TryParseClipPath(value, out _);
        _ = TypedProperties.TryParseLineWidth(value, out _);
        _ = TypedProperties.TryParseOutlineStyle(value, out _);
        _ = TypedProperties.TryParseOutlineOffset(value, out _);
        _ = AnPlusB.TryParse(value, out _);

        // A property from the table, chosen by the first byte; shorthands expand, typed properties parse and serialize.
        string name = PropertyTable.All[selector * 7 % PropertyTable.Count].Name;
        ShorthandExpansion expansion = Shorthands.Expand(name, value);
        foreach (ExpandedLonghand l in expansion.Longhands)
        {
            _ = l.Name.Length;
            _ = l.Value.IsEmpty;
        }

        _ = TypedProperties.IsValid(name, value);
        _ = TypedProperties.TrySerialize(name, value, out _);
    }
}
