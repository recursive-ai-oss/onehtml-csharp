using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;

namespace RecursiveAI.OneHtml.Css;

/// <summary>Parses CSS Color 4 <c>&lt;color&gt;</c> values on a token range, with explicit argument handling and a depth counter for nested color functions.</summary>
internal static class CssColorParser
{
    // Color functions nest only through color-mix(), light-dark() and relative colors; a hostile input could nest
    // them as deep as the tree allows, so the nesting of color arguments is bounded.
    private const int MaxDepth = 16;

    private static readonly FrozenDictionary<string, int> Named = BuildNamed();
    private static readonly FrozenDictionary<string, int> System = BuildSystem();

    private static FrozenDictionary<string, int> BuildNamed()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, int rgb) in NamedColorData.All)
        {
            d[name] = rgb;
        }

        return d.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    // The light-scheme defaults this library uses for system colors (CSS Color 4 §6.2 leaves the values to the
    // user agent). They are stable, documented choices, not measurements of any browser.
    private static FrozenDictionary<string, int> BuildSystem()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["AccentColor"] = 0x3584E4, ["AccentColorText"] = 0xFFFFFF, ["ActiveText"] = 0xFF0000, ["ButtonBorder"] = 0x767676,
            ["ButtonFace"] = 0xEFEFEF, ["ButtonText"] = 0x000000, ["Canvas"] = 0xFFFFFF, ["CanvasText"] = 0x000000,
            ["Field"] = 0xFFFFFF, ["FieldText"] = 0x000000, ["GrayText"] = 0x6D6D6D, ["Highlight"] = 0x3584E4,
            ["HighlightText"] = 0xFFFFFF, ["LinkText"] = 0x0000EE, ["Mark"] = 0xFFFF00, ["MarkText"] = 0x000000,
            ["SelectedItem"] = 0x3584E4, ["SelectedItemText"] = 0xFFFFFF, ["VisitedText"] = 0x551A8B,
            ["ActiveBorder"] = 0xFFFFFF, ["ActiveCaption"] = 0xCCCCCC, ["AppWorkspace"] = 0xAAAAAA, ["Background"] = 0x6363CE,
            ["ButtonHighlight"] = 0xFFFFFF, ["ButtonShadow"] = 0x888888, ["CaptionText"] = 0x000000, ["InactiveBorder"] = 0xFFFFFF,
            ["InactiveCaption"] = 0xFFFFFF, ["InactiveCaptionText"] = 0x434343, ["InfoBackground"] = 0xFBFCC5, ["InfoText"] = 0x000000,
            ["Menu"] = 0xF7F7F7, ["MenuText"] = 0x000000, ["Scrollbar"] = 0xFFFFFF, ["ThreeDDarkShadow"] = 0x666666,
            ["ThreeDFace"] = 0xC0C0C0, ["ThreeDHighlight"] = 0xDDDDDD, ["ThreeDLightShadow"] = 0xC0C0C0, ["ThreeDShadow"] = 0x888888,
            ["Window"] = 0xFFFFFF, ["WindowFrame"] = 0xCCCCCC, ["WindowText"] = 0x000000,
        };
        return d.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static CssColor FromRgb24(int rgb, string? system = null) =>
        new(ColorSpace.Srgb, ((rgb >> 16) & 255) / 255.0, ((rgb >> 8) & 255) / 255.0, (rgb & 255) / 255.0, 1, 0, true, ColorKind.Value, system);

    public static bool TryParseAt(TokenRange r, int i, ColorParseOptions opt, int depth, out CssColor color, out int next)
    {
        color = default;
        next = r.Next(i);
        if (depth > MaxDepth)
        {
            return false;
        }

        switch (r.Kind(i))
        {
            case CssTokenKind.Hash:
                return TryHex(r.Text(i)[1..], out color);
            case CssTokenKind.Ident:
            {
                string name = r.IdentString(i);
                if (name.Equals("currentcolor", StringComparison.OrdinalIgnoreCase))
                {
                    color = CssColor.CurrentColor;
                    return true;
                }

                if (name.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                {
                    color = CssColor.Transparent;
                    return true;
                }

                if (Named.TryGetValue(name, out int rgb))
                {
                    color = FromRgb24(rgb);
                    return true;
                }

                if (System.TryGetValue(name, out rgb))
                {
                    color = FromRgb24(rgb, name.AsciiLower());
                    return true;
                }

                return false;
            }

            case CssTokenKind.Function:
                return TryFunction(r, i, opt, depth, out color);
            default:
                return false;
        }
    }

    private static bool TryHex(ReadOnlySpan<char> h, out CssColor color)
    {
        color = default;
        if (h.Length is not (3 or 4 or 6 or 8))
        {
            return false;
        }

        int[] d = new int[8];
        for (int k = 0; k < h.Length; k++)
        {
            int v = HexValue(h[k]);
            if (v < 0)
            {
                return false;
            }

            d[k] = v;
        }

        int r, g, b, a = 255;
        if (h.Length <= 4)
        {
            r = d[0] * 17;
            g = d[1] * 17;
            b = d[2] * 17;
            if (h.Length == 4)
            {
                a = d[3] * 17;
            }
        }
        else
        {
            r = (d[0] * 16) + d[1];
            g = (d[2] * 16) + d[3];
            b = (d[4] * 16) + d[5];
            if (h.Length == 8)
            {
                a = (d[6] * 16) + d[7];
            }
        }

        color = new CssColor(ColorSpace.Srgb, r / 255.0, g / 255.0, b / 255.0, a / 255.0, 0, true);
        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    // Argument items: >= 0 is the index of a component value; -1 is a comma; -2 is a slash.
    private static bool Split(TokenRange inner, List<int> items)
    {
        int i = inner.From;
        while (true)
        {
            i = inner.SkipWs(i);
            if (i >= inner.To)
            {
                return true;
            }

            CssTokenKind k = inner.Kind(i);
            if (k == CssTokenKind.Comma)
            {
                items.Add(-1);
                i++;
            }
            else if (inner.IsDelim(i, '/'))
            {
                items.Add(-2);
                i++;
            }
            else
            {
                items.Add(i);
                i = inner.Next(i);
            }

            if (items.Count > 64)
            {
                return false;
            }
        }
    }

    private static bool TryFunction(TokenRange r, int fi, ColorParseOptions opt, int depth, out CssColor color)
    {
        color = default;
        string name = r.IdentString(fi).AsciiLower();
        TokenRange inner = r.Inner(fi);
        switch (name)
        {
            case "rgb":
            case "rgba":
            case "hsl":
            case "hsla":
            case "hwb":
            case "lab":
            case "lch":
            case "oklab":
            case "oklch":
            case "color":
                return TryComponents(r, inner, name, opt, depth, out color);
            case "color-mix":
                return TryMix(inner, opt, depth, out color);
            case "light-dark":
                return TryLightDark(inner, opt, depth, out color);
            default:
                return false;
        }
    }

    private static bool TryLightDark(TokenRange inner, ColorParseOptions opt, int depth, out CssColor color)
    {
        color = default;
        var items = new List<int>();
        if (!Split(inner, items) || items.Count != 3 || items[1] != -1 || items[0] < 0 || items[2] < 0)
        {
            return false;
        }

        if (!TryParseAt(inner, items[0], opt, depth + 1, out CssColor light, out int n0) || inner.SkipWs(n0) != items[1] - 0 && inner.SkipWs(n0) < inner.To && inner.Kind(inner.SkipWs(n0)) != CssTokenKind.Comma)
        {
            return false;
        }

        if (!TryParseAt(inner, items[2], opt, depth + 1, out CssColor dark, out int n2) || inner.SkipWs(n2) != inner.To)
        {
            return false;
        }

        color = opt.DarkScheme ? dark : light;
        return true;
    }

    private readonly record struct ChannelSpec(double PercentRef, bool IsHue);

    private static bool TryComponents(TokenRange r, TokenRange inner, string fn, ColorParseOptions opt, int depth, out CssColor color)
    {
        color = default;
        var items = new List<int>();
        if (!Split(inner, items) || items.Count == 0)
        {
            return false;
        }

        bool legacySyntax = items.Contains(-1);
        bool isColorFn = fn == "color";
        bool relative = items[0] >= 0 && inner.IsIdent(items[0], "from");
        int p = 0;
        CssColor origin = default;
        if (relative)
        {
            if (items.Count < 2 || items[1] < 0 || legacySyntax)
            {
                return false;
            }

            if (!TryParseAt(inner, items[1], opt, depth + 1, out origin, out _))
            {
                return false;
            }

            p = 2;
        }

        ColorSpace space;
        ChannelSpec[] specs;
        string[] names;
        if (isColorFn)
        {
            if (p >= items.Count || items[p] < 0 || inner.Kind(items[p]) != CssTokenKind.Ident)
            {
                return false;
            }

            if (!TryColorSpace(inner.IdentString(items[p]), out space) || space is ColorSpace.Hsl or ColorSpace.Hwb or ColorSpace.Lab or ColorSpace.Lch or ColorSpace.Oklab or ColorSpace.Oklch)
            {
                return false;
            }

            p++;
            specs = [new(1, false), new(1, false), new(1, false)];
            names = space is ColorSpace.XyzD50 or ColorSpace.XyzD65 ? ["x", "y", "z"] : ["r", "g", "b"];
        }
        else
        {
            switch (fn)
            {
                case "rgb":
                case "rgba":
                    space = ColorSpace.Srgb;
                    specs = [new(255, false), new(255, false), new(255, false)];
                    names = ["r", "g", "b"];
                    break;
                case "hsl":
                case "hsla":
                    space = ColorSpace.Hsl;
                    specs = [new(0, true), new(100, false), new(100, false)];
                    names = ["h", "s", "l"];
                    break;
                case "hwb":
                    space = ColorSpace.Hwb;
                    specs = [new(0, true), new(100, false), new(100, false)];
                    names = ["h", "w", "b"];
                    break;
                case "lab":
                    space = ColorSpace.Lab;
                    specs = [new(100, false), new(125, false), new(125, false)];
                    names = ["l", "a", "b"];
                    break;
                case "lch":
                    space = ColorSpace.Lch;
                    specs = [new(100, false), new(150, false), new(0, true)];
                    names = ["l", "c", "h"];
                    break;
                case "oklab":
                    space = ColorSpace.Oklab;
                    specs = [new(1, false), new(0.4, false), new(0.4, false)];
                    names = ["l", "a", "b"];
                    break;
                default:
                    space = ColorSpace.Oklch;
                    specs = [new(1, false), new(0.4, false), new(0, true)];
                    names = ["l", "c", "h"];
                    break;
            }
        }

        Dictionary<string, double>? symbols = null;
        if (relative)
        {
            CssColor o = origin.IsCurrentColor ? CssColor.Transparent : origin;
            CssColor conv = o.ConvertTo(space);
            double scale = space == ColorSpace.Srgb && !isColorFn ? 255 : 1;
            symbols = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                [names[0]] = conv.Comp(0) * scale,
                [names[1]] = conv.Comp(1) * scale,
                [names[2]] = conv.Comp(2) * scale,
                ["alpha"] = (conv.MissingMask & 8) != 0 ? 0 : conv.Alpha,
            };

            // Color 5 §4.1: a channel keyword whose origin component is missing is 0 in a calculation, but used on its own
            // it keeps the component missing. The "?" entries record which ones.
            for (int k = 0; k < 3; k++)
            {
                if ((conv.MissingMask & (1 << k)) != 0)
                {
                    symbols["?" + names[k]] = 1;
                }
            }

            if ((conv.MissingMask & 8) != 0)
            {
                symbols["?alpha"] = 1;
            }
        }

        // Channels and alpha
        int rest = items.Count - p;
        bool hasSlash = false;
        int alphaItem = -1;
        int[] ch = new int[3];
        if (legacySyntax)
        {
            if (isColorFn || fn is not ("rgb" or "rgba" or "hsl" or "hsla") || items.Contains(-2))
            {
                return false;
            }

            // a, b, c [, alpha]
            if (rest is not (5 or 7) || items[p + 1] != -1 || items[p + 3] != -1 || items[p] < 0 || items[p + 2] < 0 || items[p + 4] < 0)
            {
                return false;
            }

            ch[0] = items[p];
            ch[1] = items[p + 2];
            ch[2] = items[p + 4];
            if (rest == 7)
            {
                if (items[p + 5] != -1 || items[p + 6] < 0)
                {
                    return false;
                }

                alphaItem = items[p + 6];
            }
        }
        else
        {
            if (rest is not (3 or 5) || items[p] < 0 || items[p + 1] < 0 || items[p + 2] < 0)
            {
                return false;
            }

            ch[0] = items[p];
            ch[1] = items[p + 1];
            ch[2] = items[p + 2];
            if (rest == 5)
            {
                if (items[p + 3] != -2 || items[p + 4] < 0)
                {
                    return false;
                }

                hasSlash = true;
                alphaItem = items[p + 4];
            }
        }

        _ = hasSlash;
        double[] v = new double[3];
        byte missing = 0;
        bool[] isPercent = new bool[3];
        for (int k = 0; k < 3; k++)
        {
            if (!ReadChannel(inner, ch[k], specs[k], symbols, legacySyntax, out v[k], out bool none, out isPercent[k]))
            {
                return false;
            }

            if (none)
            {
                if (legacySyntax)
                {
                    return false;
                }

                missing |= (byte)(1 << k);
            }
        }

        double alpha = 1;
        if (alphaItem >= 0)
        {
            if (!ReadAlpha(inner, alphaItem, symbols, legacySyntax, out alpha, out bool alphaNone))
            {
                return false;
            }

            if (alphaNone)
            {
                missing |= 8;
            }
        }
        else if (relative && symbols is not null)
        {
            alpha = symbols["alpha"];
        }

        if (legacySyntax)
        {
            // Legacy rgb(): the three channels are all numbers or all percentages. Legacy hsl(): saturation and lightness are percentages.
            if (space == ColorSpace.Srgb && (isPercent[0] != isPercent[1] || isPercent[1] != isPercent[2]))
            {
                return false;
            }

            if (space == ColorSpace.Hsl && (!isPercent[1] || !isPercent[2] || isPercent[0]))
            {
                return false;
            }
        }

        if (relative && !legacySyntax)
        {
            // keep as parsed
        }

        if (space is ColorSpace.Hsl or ColorSpace.Hwb && (double.IsNaN(v[0]) || double.IsInfinity(v[0])))
        {
            v[0] = 0;
        }

        // Build the result
        switch (space)
        {
            case ColorSpace.Srgb when !isColorFn && relative:
            {
                // CSS Color 5 §11.2: a relative rgb() result serializes as color(srgb ...), like the other non-legacy forms.
                color = new CssColor(ColorSpace.Srgb, Math.Clamp(v[0], 0, 255) / 255, Math.Clamp(v[1], 0, 255) / 255, Math.Clamp(v[2], 0, 255) / 255, Math.Clamp(alpha, 0, 1), missing, false);
                return true;
            }

            case ColorSpace.Hsl or ColorSpace.Hwb when relative && missing != 0:
            {
                color = new CssColor(space, v[0], Math.Max(0, v[1]), Math.Max(0, v[2]), Math.Clamp(alpha, 0, 1), missing, false);
                return true;
            }

            case ColorSpace.Hsl or ColorSpace.Hwb when relative:
            {
                var polar = new CssColor(space, v[0], Math.Clamp(v[1], 0, 100), Math.Clamp(v[2], 0, 100), Math.Clamp(alpha, 0, 1), missing, false);
                CssColor srgb = polar.ConvertTo(ColorSpace.Srgb);
                color = new CssColor(ColorSpace.Srgb, srgb.C1, srgb.C2, srgb.C3, srgb.Alpha, srgb.MissingMask, false);
                return true;
            }

            case ColorSpace.Srgb when !isColorFn && missing != 0:
            {
                // CSS Color 4 §16.2.2: rgb() with a missing component serializes as color(srgb ...) so none survives.
                color = new CssColor(ColorSpace.Srgb, Math.Clamp(v[0], 0, 255) / 255, Math.Clamp(v[1], 0, 255) / 255, Math.Clamp(v[2], 0, 255) / 255, Math.Clamp(alpha, 0, 1), missing, false);
                return true;
            }

            case ColorSpace.Srgb when !isColorFn:
            {
                color = new CssColor(ColorSpace.Srgb, Math.Clamp(v[0], 0, 255) / 255, Math.Clamp(v[1], 0, 255) / 255, Math.Clamp(v[2], 0, 255) / 255, Math.Clamp(alpha, 0, 1), (byte)(missing & 8), true);
                return true;
            }

            case ColorSpace.Hsl when missing != 0:
            case ColorSpace.Hwb when missing != 0:
            {
                // §16.2.2: hsl() and hwb() with a missing component keep their own function so none survives.
                color = new CssColor(space, v[0], Math.Max(0, v[1]), Math.Max(0, v[2]), Math.Clamp(alpha, 0, 1), missing, false);
                return true;
            }

            case ColorSpace.Hsl:
            {
                double s = Math.Clamp(v[1], 0, 100);
                double l = Math.Clamp(v[2], 0, 100);
                double[] rgb = ToRgbFromPolar(space, v[0], s, l);
                color = new CssColor(ColorSpace.Srgb, Math.Clamp(rgb[0], 0, 1), Math.Clamp(rgb[1], 0, 1), Math.Clamp(rgb[2], 0, 1), Math.Clamp(alpha, 0, 1), (byte)(missing & 8), true);
                return true;
            }

            case ColorSpace.Hwb:
            {
                // §7: whiteness and blackness are only clamped below at 0; a sum over 100% is normalized to a gray.
                double w = Math.Max(0, v[1]);
                double b = Math.Max(0, v[2]);
                double[] rgb = ToRgbFromPolar(space, v[0], w, b);
                color = new CssColor(ColorSpace.Srgb, Math.Clamp(rgb[0], 0, 1), Math.Clamp(rgb[1], 0, 1), Math.Clamp(rgb[2], 0, 1), Math.Clamp(alpha, 0, 1), (byte)(missing & 8), true);
                return true;
            }

            case ColorSpace.Lch:
            case ColorSpace.Oklch:
                v[2] = ((v[2] % 360) + 360) % 360;
                break;
        }

        if (space is ColorSpace.Lab or ColorSpace.Lch or ColorSpace.Oklab or ColorSpace.Oklch)
        {
            // §9 to §12: lightness is clamped to its range and chroma to be non-negative at parsed-value time; a NaN
            // from math functions becomes 0 and an infinite hue becomes 0 (§4.2, "NaN" and "infinity" in calculations).
            double maxL = space is ColorSpace.Lab or ColorSpace.Lch ? 100 : 1;
            for (int k = 0; k < 3; k++)
            {
                if (double.IsNaN(v[k]))
                {
                    v[k] = 0;
                }
            }

            v[0] = Math.Clamp(v[0], 0, maxL);
            if (space is ColorSpace.Lch or ColorSpace.Oklch)
            {
                v[1] = Math.Max(0, v[1]);
                if (double.IsInfinity(v[2]))
                {
                    v[2] = 0;
                }
            }
        }

        color = new CssColor(space, v[0], v[1], v[2], Math.Clamp(alpha, 0, 1), missing, false);
        return true;
    }

    private static double[] ToRgbFromPolar(ColorSpace space, double h, double a, double b) =>
        space == ColorSpace.Hsl ? ColorMath.HslToSrgb(h, a, b) : ColorMath.HwbToSrgb(h, a, b);

    private static bool TryColorSpace(string s, out ColorSpace space)
    {
        switch (s.AsciiLower())
        {
            case "srgb": space = ColorSpace.Srgb; return true;
            case "srgb-linear": space = ColorSpace.SrgbLinear; return true;
            case "display-p3": space = ColorSpace.DisplayP3; return true;
            case "a98-rgb": space = ColorSpace.A98Rgb; return true;
            case "prophoto-rgb": space = ColorSpace.ProphotoRgb; return true;
            case "rec2020": space = ColorSpace.Rec2020; return true;
            case "xyz":
            case "xyz-d65": space = ColorSpace.XyzD65; return true;
            case "xyz-d50": space = ColorSpace.XyzD50; return true;
            case "hsl": space = ColorSpace.Hsl; return true;
            case "hwb": space = ColorSpace.Hwb; return true;
            case "lab": space = ColorSpace.Lab; return true;
            case "lch": space = ColorSpace.Lch; return true;
            case "oklab": space = ColorSpace.Oklab; return true;
            case "oklch": space = ColorSpace.Oklch; return true;
            default: space = ColorSpace.Srgb; return false;
        }
    }

    private static bool ReadChannel(TokenRange r, int i, ChannelSpec spec, Dictionary<string, double>? symbols, bool legacy, out double value, out bool none, out bool percent)
    {
        value = 0;
        none = false;
        percent = false;
        if (r.Kind(i) == CssTokenKind.Ident)
        {
            string id = r.IdentString(i);
            if (id.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                none = !legacy;
                return !legacy;
            }

            if (symbols is not null && symbols.TryGetValue(id.AsciiLower(), out double sv) && id != "alpha")
            {
                value = sv;
                none = symbols.ContainsKey("?" + id.AsciiLower());
                return true;
            }

            return false;
        }

        QuantityKinds kinds = QuantityKinds.Number | (spec.IsHue ? QuantityKinds.Angle : QuantityKinds.Percentage);
        if (!CssQuantity.TryParse(r, i, kinds, symbols, out CssQuantity q, out int next) || q.Calc is not null || next != r.Next(i))
        {
            return false;
        }

        switch (q.Unit)
        {
            case CssUnit.Percent:
                percent = true;
                value = q.Value * spec.PercentRef / 100;
                return true;
            case CssUnit.None:
                value = q.Value;
                return true;
            default:
                value = q.Value * CssUnits.GetCanonicalFactor(q.Unit);
                return true;
        }
    }

    private static bool ReadAlpha(TokenRange r, int i, Dictionary<string, double>? symbols, bool legacy, out double alpha, out bool none)
    {
        alpha = 1;
        none = false;
        if (r.Kind(i) == CssTokenKind.Ident)
        {
            string id = r.IdentString(i);
            if (id.Equals("none", StringComparison.OrdinalIgnoreCase) && !legacy)
            {
                none = true;
                return true;
            }

            if (symbols is not null && id.Equals("alpha", StringComparison.OrdinalIgnoreCase))
            {
                alpha = symbols["alpha"];
                none = symbols.ContainsKey("?alpha");
                return true;
            }

            return false;
        }

        if (!CssQuantity.TryParse(r, i, QuantityKinds.Number | QuantityKinds.Percentage, symbols, out CssQuantity q, out _) || q.Calc is not null)
        {
            return false;
        }

        alpha = Math.Clamp(q.Unit == CssUnit.Percent ? q.Value / 100 : q.Value, 0, 1);
        return true;
    }

    private static bool TryMix(TokenRange inner, ColorParseOptions opt, int depth, out CssColor color)
    {
        color = default;
        var items = new List<int>();
        if (!Split(inner, items))
        {
            return false;
        }

        // Three comma-separated groups: "in <space> [<hue> hue]?", then two "<color> <percentage>?" groups.
        var groups = new List<List<int>> { new() };
        foreach (int it in items)
        {
            if (it == -1)
            {
                groups.Add([]);
            }
            else if (it == -2)
            {
                return false;
            }
            else
            {
                groups[^1].Add(it);
            }
        }

        if (groups.Count != 3 || groups[0].Count is not (2 or 4) || !inner.IsIdent(groups[0][0], "in") || inner.Kind(groups[0][1]) != CssTokenKind.Ident)
        {
            return false;
        }

        if (!TryColorSpace(inner.IdentString(groups[0][1]), out ColorSpace space) || space is ColorSpace.Hsl && false)
        {
            return false;
        }

        int hueMethod = 0;
        if (groups[0].Count == 4)
        {
            if (space is not (ColorSpace.Hsl or ColorSpace.Hwb or ColorSpace.Lch or ColorSpace.Oklch) || !inner.IsIdent(groups[0][3], "hue") || inner.Kind(groups[0][2]) != CssTokenKind.Ident)
            {
                return false;
            }

            switch (inner.IdentString(groups[0][2]).AsciiLower())
            {
                case "shorter": hueMethod = 0; break;
                case "longer": hueMethod = 1; break;
                case "increasing": hueMethod = 2; break;
                case "decreasing": hueMethod = 3; break;
                default: return false;
            }
        }

        if (!TryMixOperand(inner, groups[1], opt, depth, out CssColor c1, out double? p1) || !TryMixOperand(inner, groups[2], opt, depth, out CssColor c2, out double? p2))
        {
            return false;
        }

        double a1, a2;
        if (p1 is null && p2 is null)
        {
            a1 = a2 = 50;
        }
        else if (p1 is null)
        {
            a2 = p2!.Value;
            a1 = 100 - a2;
        }
        else if (p2 is null)
        {
            a1 = p1.Value;
            a2 = 100 - a1;
        }
        else
        {
            a1 = p1.Value;
            a2 = p2.Value;
        }

        double sum = a1 + a2;
        if (sum <= 0)
        {
            return false;
        }

        double alphaMult = sum < 100 ? sum / 100 : 1;
        double t = a2 / sum;
        CssColor x = c1.IsCurrentColor ? CssColor.Transparent : c1;
        CssColor y = c2.IsCurrentColor ? CssColor.Transparent : c2;
        CssColor m = ColorMath.Mix(x, y, t, space, hueMethod);
        if (m.Space is ColorSpace.Hsl or ColorSpace.Hwb)
        {
            // Color 5 §3.4: a mix in a legacy polar space is returned in sRGB.
            m = m.ConvertTo(ColorSpace.Srgb);
        }

        color = new CssColor(m.Space, m.C1, m.C2, m.C3, Math.Clamp(m.Alpha * alphaMult, 0, 1), m.MissingMask, false);
        return true;
    }

    private static bool TryMixOperand(TokenRange inner, List<int> group, ColorParseOptions opt, int depth, out CssColor color, out double? percent)
    {
        color = default;
        percent = null;
        if (group.Count is < 1 or > 2)
        {
            return false;
        }

        int colorIdx = -1;
        foreach (int g in group)
        {
            if (inner.Kind(g) == CssTokenKind.Percentage)
            {
                if (percent is not null)
                {
                    return false;
                }

                double pv = inner.Number(g);
                if (pv is < 0 or > 100)
                {
                    return false;
                }

                percent = pv;
            }
            else if (colorIdx < 0)
            {
                colorIdx = g;
            }
            else
            {
                return false;
            }
        }

        if (colorIdx < 0)
        {
            return false;
        }

        return TryParseAt(inner, colorIdx, opt, depth + 1, out color, out _);
    }
}
