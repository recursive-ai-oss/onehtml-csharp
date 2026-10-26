using System;
using System.Collections.Generic;
using System.Globalization;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

/// <summary>
/// WCAG 2.x colour arithmetic. Relative luminance and the contrast ratio are the definitions in WCAG 2.2 section 1.4.3
/// ("relative luminance", "contrast ratio") applied to 8-bit sRGB channels, because that is what a display shows.
/// </summary>
internal static class ContrastMath
{
    /// <summary>The linearised value of one 8-bit sRGB channel (WCAG relative-luminance definition, threshold 0.04045 as in CSS Color 4).</summary>
    private static double Linear(byte channel)
    {
        double v = channel / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    internal static double Luminance(byte r, byte g, byte b) => (0.2126 * Linear(r)) + (0.7152 * Linear(g)) + (0.0722 * Linear(b));

    /// <summary>(L1 + 0.05) / (L2 + 0.05) with L1 the lighter; 1 to 21. Not rounded: WCAG says 4.499 does not meet 4.5.</summary>
    internal static double Ratio(Rgb8 a, Rgb8 b)
    {
        double la = Luminance(a.R, a.G, a.B), lb = Luminance(b.R, b.G, b.B);
        return la >= lb ? (la + 0.05) / (lb + 0.05) : (lb + 0.05) / (la + 0.05);
    }

    /// <summary>Source-over compositing of a colour with alpha onto an opaque colour, rounded to 8 bits per channel as a browser does.</summary>
    internal static Rgb8 Over(Rgb8 top, double alpha, Rgb8 below)
    {
        if (alpha >= 1)
        {
            return top;
        }

        if (alpha <= 0)
        {
            return below;
        }

        return new Rgb8(Mix(top.R, below.R, alpha), Mix(top.G, below.G, alpha), Mix(top.B, below.B, alpha));
    }

    private static byte Mix(byte t, byte b, double a) => (byte)Math.Clamp((int)Math.Round((t * a) + (b * (1 - a))), 0, 255);

    /// <summary>
    /// Large-scale text (WCAG 1.4.3 definition): at least 18 point, or 14 point and bold (700 or more). One point is 4/3 CSS px.
    /// </summary>
    internal static bool IsLarge(double fontPx, double weight) => fontPx >= 24 - 1e-6 || (weight >= 700 && fontPx >= (14 * 4.0 / 3.0) - 1e-6);

    /// <summary>A ratio for messages: truncated, never rounded up, to two decimals, so "4.5" is only ever written for a ratio that is at least 4.5.</summary>
    internal static string Format(double ratio) => (Math.Floor(ratio * 100 + 1e-9) / 100).ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>An opaque sRGB colour.</summary>
internal readonly record struct Rgb8(byte R, byte G, byte B)
{
    public override string ToString() => "#" + R.ToString("x2", CultureInfo.InvariantCulture) + G.ToString("x2", CultureInfo.InvariantCulture) + B.ToString("x2", CultureInfo.InvariantCulture);
}

/// <summary>A colour that may be showing behind text: a known opaque colour, or something only pixels can tell (an image).</summary>
internal readonly record struct Backing(Rgb8 Color, bool Unknown);

/// <summary>What the cascade says is painted behind an element's content: the possible backdrop colours and why they might be wrong.</summary>
internal sealed class PaintState
{
    internal PaintState(Backing[] backings, double opacity, string? hazard, bool gradient, bool offscreen)
    {
        Backings = backings;
        Opacity = opacity;
        Hazard = hazard;
        Gradient = gradient;
        Offscreen = offscreen;
    }

    /// <summary>The distinct colours that can be behind the content (a gradient has several; an image makes one entry unknown).</summary>
    internal Backing[] Backings { get; }

    /// <summary>The product of the opacity of the element and its ancestors: every colour painted in the subtree is weakened by it.</summary>
    internal double Opacity { get; }

    /// <summary>When set, something out of flow or blending may put other content behind this subtree; the cascade cannot say what.</summary>
    internal string? Hazard { get; }

    /// <summary>True when a gradient contributed to <see cref="Backings"/>.</summary>
    internal bool Gradient { get; }

    /// <summary>True when the element or an ancestor is moved far off the page or reduced to a clipped one-pixel box.</summary>
    internal bool Offscreen { get; }

    internal bool HasUnknown
    {
        get
        {
            foreach (Backing b in Backings)
            {
                if (b.Unknown)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>
/// Walks the cascade from the root to an element and says what is painted behind it (CSS 2 appendix E painting order,
/// reduced to the ancestor chain). Results are cached per element, so a page costs one step per element.
/// </summary>
internal sealed class PaintCalculator
{
    private const int MaxBackings = 64;
    private readonly CheckContext _context;
    private readonly Dictionary<int, PaintState> _memo = [];
    private readonly PaintState _canvas;

    internal PaintCalculator(CheckContext context)
    {
        _context = context;
        _canvas = new PaintState([new Backing(CanvasColor(context), false)], 1, null, false, false);
    }

    // The canvas is white unless the page opted into a dark colour scheme and the context prefers one (CSS Color Adjustment 1).
    private static Rgb8 CanvasColor(CheckContext context)
    {
        Node root = context.Document.DocumentElement;
        if (!root.IsNull && context.StyleContext.Media.ColorScheme == PrefersColorScheme.Dark
            && context.Styles.GetStyle(root).GetPropertyValue("color-scheme").Contains("dark", StringComparison.OrdinalIgnoreCase)
            && CssColor.TryParse("Canvas", out CssColor c, new ColorParseOptions { DarkScheme = true }))
        {
            (byte r, byte g, byte b, _) = c.ToRgba8();
            return new Rgb8(r, g, b);
        }

        return new Rgb8(255, 255, 255);
    }

    /// <summary>The state after painting the element itself (its own background included).</summary>
    internal PaintState Of(Node element)
    {
        if (_memo.TryGetValue(element.Index, out PaintState? known))
        {
            return known;
        }

        var chain = new List<Node>();
        PaintState below = _canvas;
        for (Node e = element; !e.IsNull; e = e.ParentElement)
        {
            if (_memo.TryGetValue(e.Index, out PaintState? m))
            {
                below = m;
                break;
            }

            chain.Add(e);
        }

        for (int i = chain.Count - 1; i >= 0; i--)
        {
            below = Apply(below, _context.Styles.GetStyle(chain[i]), _context.Styles.GetPseudoStyle(chain[i], PseudoElementKind.Before), _context.Styles.GetPseudoStyle(chain[i], PseudoElementKind.After));
            _memo[chain[i].Index] = below;
        }

        return below;
    }

    /// <summary>The state after painting a generated box (<c>::before</c>, <c>::after</c>) of the element, which sits inside the element.</summary>
    internal PaintState OfPseudo(Node host, ComputedStyle pseudo) => Apply(Of(host), pseudo, null, null);

    private static PaintState Apply(PaintState parent, ComputedStyle s, ComputedStyle? before, ComputedStyle? after)
    {
        double opacity = parent.Opacity * Math.Clamp(s.Opacity, 0, 1);
        string? hazard = parent.Hazard ?? HazardOf(s);
        bool offscreen = parent.Offscreen || IsOffscreen(s);
        bool gradient = parent.Gradient;
        Backing[] backings = parent.Backings;

        if (PaintsFromGeneratedBox(before) || PaintsFromGeneratedBox(after))
        {
            // A panel or overlay made by a positioned generated box is not in the ancestor chain, and the browser's blended colour does not include it either.
            backings = Add(backings, new Backing(default, true));
        }

        if (s.GetPropertyValue("background-clip").Contains("text", StringComparison.OrdinalIgnoreCase))
        {
            // The background is painted inside the glyphs only, so what is behind the text is the page behind this element and the glyph fill is the background.
            return new PaintState([.. backings, new Backing(default, true)], opacity, hazard, gradient, offscreen);
        }

        (byte r, byte g, byte b, double a) = s.BackgroundColor.ToRgba8();
        double effective = a * opacity;
        if (effective > 0)
        {
            backings = OverEach(backings, new Rgb8(r, g, b), effective);
            if (effective >= 1 - 1e-9)
            {
                hazard = null;
            }
        }

        string images = s.GetPropertyValue("background-image");
        if (images.Length > 0 && !images.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            List<string> layers = CssText.SplitTopLevel(images, ',');
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                string layer = layers[i].Trim();
                if (layer.Length == 0 || layer.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (layer.Contains("-gradient(", StringComparison.OrdinalIgnoreCase) && TryGradientStops(layer, s, out List<(Rgb8 Color, double Alpha)> stops))
                {
                    gradient = true;
                    backings = Gradient(backings, stops, opacity, ref hazard);
                }
                else
                {
                    // An image (or a gradient this reader cannot take apart): only its pixels know what the backdrop is.
                    backings = Add(backings, new Backing(default, true));
                }
            }
        }

        if (backings.Length > MaxBackings)
        {
            backings = [new Backing(default, true)];
        }

        return new PaintState(backings, opacity, hazard, gradient, offscreen);
    }

    private static Backing[] OverEach(Backing[] below, Rgb8 color, double alpha)
    {
        var list = new List<Backing>(below.Length);
        foreach (Backing b in below)
        {
            Backing n = alpha >= 1 - 1e-9 ? new Backing(color, false) : b.Unknown ? b : new Backing(ContrastMath.Over(color, alpha, b.Color), false);
            if (!list.Contains(n))
            {
                list.Add(n);
            }
        }

        return [.. list];
    }

    private static Backing[] Add(Backing[] backings, Backing extra)
    {
        foreach (Backing b in backings)
        {
            if (b == extra)
            {
                return backings;
            }
        }

        return [.. backings, extra];
    }

    // Each stop colour can be what shows; a stop that is not opaque lets the layer below show through.
    private static Backing[] Gradient(Backing[] below, List<(Rgb8 Color, double Alpha)> stops, double opacity, ref string? hazard)
    {
        var list = new List<Backing>();
        bool allOpaque = true;
        foreach ((Rgb8 color, double alpha) in stops)
        {
            double a = alpha * opacity;
            allOpaque &= a >= 1 - 1e-9;
            foreach (Backing n in OverEach(below, color, a))
            {
                if (!list.Contains(n))
                {
                    list.Add(n);
                }
            }
        }

        if (allOpaque)
        {
            hazard = null;
        }

        return [.. list];
    }

    // Positioning out of flow, transforms and blending put content from elsewhere behind this subtree (CSS 2 appendix E, Compositing 1).
    private static string? HazardOf(ComputedStyle s)
    {
        string pos = s.GetPropertyValue("position");
        if (pos.Equals("absolute", StringComparison.OrdinalIgnoreCase) || pos.Equals("fixed", StringComparison.OrdinalIgnoreCase))
        {
            return "positioned out of flow";
        }

        if (NotNone(s, "transform") || NotNone(s, "translate") || NotNone(s, "rotate") || NotNone(s, "scale"))
        {
            return "transformed";
        }

        if (NotNone(s, "filter") || NotNone(s, "backdrop-filter"))
        {
            return "filtered";
        }

        string mix = s.GetPropertyValue("mix-blend-mode");
        if (mix.Length > 0 && !mix.Equals("normal", StringComparison.OrdinalIgnoreCase))
        {
            return "blended";
        }

        string bgMix = s.GetPropertyValue("background-blend-mode");
        if (bgMix.Length > 0 && !bgMix.Equals("normal", StringComparison.OrdinalIgnoreCase))
        {
            return "blended";
        }

        return null;
    }

    // A generated box taken out of flow that paints a background (an overlay or a panel behind the content).
    private static bool PaintsFromGeneratedBox(ComputedStyle? p)
    {
        if (p is null || p.Content is not { IsNone: false, IsNormal: false })
        {
            return false;
        }

        string pos = p.GetPropertyValue("position");
        return (pos.Equals("absolute", StringComparison.OrdinalIgnoreCase) || pos.Equals("fixed", StringComparison.OrdinalIgnoreCase))
            && (p.BackgroundColor.ToRgba8().A > 0 || NotNone(p, "background-image"));
    }

    private static bool NotNone(ComputedStyle s, string property)
    {
        string v = s.GetPropertyValue(property).Trim();
        return v.Length > 0 && !v.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    // Text moved far off the page, or cut down to a clipped one-pixel box, is not visible text (the usual "visually hidden" patterns).
    private static bool IsOffscreen(ComputedStyle s)
    {
        string pos = s.GetPropertyValue("position");
        bool outOfFlow = pos.Equals("absolute", StringComparison.OrdinalIgnoreCase) || pos.Equals("fixed", StringComparison.OrdinalIgnoreCase);
        double font = s.FontSizePx;
        if (outOfFlow)
        {
            foreach (string side in new[] { "top", "left" })
            {
                if (CssLength.TryPx(s.GetPropertyValue(side), font, out double px) && px <= -1000)
                {
                    return true;
                }
            }

            if (!s.GetPropertyValue("clip").Trim().Equals("auto", StringComparison.OrdinalIgnoreCase) || NotNone(s, "clip-path"))
            {
                return true;
            }

            if (CssLength.TryPx(s.GetPropertyValue("width"), font, out double w) && CssLength.TryPx(s.GetPropertyValue("height"), font, out double h) && w <= 1 && h <= 1
                && !s.GetPropertyValue("overflow").Trim().Equals("visible", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return CssLength.TryPx(s.GetPropertyValue("text-indent"), font, out double indent) && indent <= -1000;
    }

    // Colour stops of a gradient function (CSS Images 4 section 3). Direction, shape, interpolation hints and positions are skipped.
    private static bool TryGradientStops(string layer, ComputedStyle s, out List<(Rgb8 Color, double Alpha)> stops)
    {
        stops = [];
        int open = layer.IndexOf('(', StringComparison.Ordinal);
        int close = layer.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return false;
        }

        List<string> parts = CssText.SplitTopLevel(layer[(open + 1)..close], ',');
        for (int i = 0; i < parts.Count; i++)
        {
            string part = parts[i].Trim();
            if (part.Length == 0)
            {
                return false;
            }

            List<string> words = CssText.SplitTopLevel(part, ' ');
            Rgb8 color = default;
            double alpha = 0;
            bool found = false;
            foreach (string w in new[] { words[0], words[^1] })
            {
                if (CssColor.TryParse(w, out CssColor c))
                {
                    (byte r, byte g, byte b, double a) = c.Resolve(s.Color).ToRgba8();
                    color = new Rgb8(r, g, b);
                    alpha = a;
                    found = true;
                    break;
                }
            }

            if (found)
            {
                stops.Add((color, alpha));
            }
            else if (i == 0 || (part[0] is >= '0' and <= '9' or '.' or '-' or '+'))
            {
                // The line direction, the shape and position, or an interpolation hint: not a colour.
                continue;
            }
            else
            {
                return false;
            }
        }

        return stops.Count > 0;
    }
}

/// <summary>Small helpers over CSS value text. Computed values come back from the cascade as text.</summary>
internal static class CssText
{
    /// <summary>Splits at <paramref name="separator"/> outside parentheses and strings; empty parts are dropped for a space separator.</summary>
    internal static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quote != '\0')
            {
                if (ch == '\\')
                {
                    i++;
                }
                else if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0 && (separator == ' ' ? ch is ' ' or '\t' or '\n' or '\r' or '\f' : ch == separator))
            {
                if (separator != ' ' || i > start)
                {
                    parts.Add(text[start..i]);
                }

                start = i + 1;
            }
        }

        if (separator != ' ' || start < text.Length)
        {
            parts.Add(text[start..]);
        }

        if (parts.Count == 0)
        {
            parts.Add(string.Empty);
        }

        return parts;
    }
}

/// <summary>Lengths in the units that need no layout.</summary>
internal static class CssLength
{
    /// <summary>Parses <c>0</c> or a number with an absolute or font-relative unit (the root font size is taken as 16px). Percentages, <c>calc()</c> and keywords give <see langword="false"/>.</summary>
    internal static bool TryPx(string text, double fontPx, out double px)
    {
        px = 0;
        string t = text.Trim();
        if (t.Length == 0)
        {
            return false;
        }

        int i = 0;
        if (t[i] is '+' or '-')
        {
            i++;
        }

        while (i < t.Length && (char.IsAsciiDigit(t[i]) || t[i] == '.'))
        {
            i++;
        }

        // An exponent only when digits follow, so that the unit "em" is not read as one.
        if (i < t.Length && t[i] is 'e' or 'E')
        {
            int j = i + 1;
            if (j < t.Length && t[j] is '+' or '-')
            {
                j++;
            }

            if (j < t.Length && char.IsAsciiDigit(t[j]))
            {
                while (j < t.Length && char.IsAsciiDigit(t[j]))
                {
                    j++;
                }

                i = j;
            }
        }

        if (i == 0 || !double.TryParse(t.AsSpan(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
        {
            return false;
        }

        string unit = t[i..].ToLowerInvariantAscii();
        double? factor = unit switch
        {
            "" => n == 0 ? 1 : null,
            "px" => 1,
            "em" => fontPx,
            "rem" => 16,
            "ex" => fontPx / 2,
            "ch" => fontPx / 2,
            "pt" => 4.0 / 3,
            "pc" => 16,
            "in" => 96,
            "cm" => 96 / 2.54,
            "mm" => 96 / 25.4,
            "q" => 96 / 101.6,
            _ => null,
        };
        if (factor is null)
        {
            return false;
        }

        px = n * factor.Value;
        return true;
    }
}
