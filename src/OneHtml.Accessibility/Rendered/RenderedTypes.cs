using System;
using System.Collections.Generic;
using System.Globalization;

namespace RecursiveAI.OneHtml.Accessibility.Rendered;

/// <summary>A rectangle in CSS pixels, as Chrome reports it: document coordinates, origin top left.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    /// <summary>The right edge.</summary>
    public double Right => X + Width;

    /// <summary>The bottom edge.</summary>
    public double Bottom => Y + Height;

    /// <summary>Whether the rectangle has no area.</summary>
    public bool IsEmpty => !(Width > 0 && Height > 0);

    /// <summary>Whether this rectangle and <paramref name="other"/> share any area.</summary>
    /// <param name="other">The other rectangle.</param>
    public bool Intersects(LayoutRect other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

/// <summary>One rendered fragment of a text node's text: a run on one line.</summary>
/// <param name="Start">Start of the run in the layout text, in UTF-16 code units.</param>
/// <param name="Length">Length of the run in UTF-16 code units.</param>
/// <param name="Bounds">The run's bounding box in document coordinates.</param>
public readonly record struct TextBox(int Start, int Length, LayoutRect Bounds);

/// <summary>A colour with 8-bit channels and a fractional alpha.</summary>
/// <param name="R">Red, 0 to 255.</param>
/// <param name="G">Green, 0 to 255.</param>
/// <param name="B">Blue, 0 to 255.</param>
/// <param name="A">Alpha, 0 to 1.</param>
public readonly record struct Rgba(byte R, byte G, byte B, double A)
{
    /// <summary>
    /// Parses <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>, and the <c>rgb(r, g, b)</c> / <c>rgba(r, g, b, a)</c> forms Chrome
    /// reports for computed colours (comma or space separated, a slash before alpha, percentages for alpha).
    /// </summary>
    /// <param name="text">The colour text.</param>
    /// <param name="color">The colour.</param>
    /// <returns><see langword="false"/> when the text is not one of those forms.</returns>
    public static bool TryParse(ReadOnlySpan<char> text, out Rgba color)
    {
        color = default;
        text = text.Trim();
        if (text.Length >= 4 && text[0] == '#')
        {
            return TryHex(text[1..], out color);
        }

        if (text.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(")", StringComparison.Ordinal))
        {
            return TryFunction(text[5..^1], out color);
        }

        if (text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(")", StringComparison.Ordinal))
        {
            return TryFunction(text[4..^1], out color);
        }

        return false;
    }

    /// <summary>The colour in <c>#rrggbb</c> or <c>#rrggbbaa</c> form, alpha rounded to a byte.</summary>
    public override string ToString() => A >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}")
        : string.Create(CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}{(int)Math.Round(Math.Clamp(A, 0, 1) * 255):x2}");

    private static bool TryHex(ReadOnlySpan<char> h, out Rgba color)
    {
        color = default;
        int n = h.Length;
        if (n is not (3 or 4 or 6 or 8))
        {
            return false;
        }

        Span<int> v = stackalloc int[8];
        for (int i = 0; i < n; i++)
        {
            int d = HexValue(h[i]);
            if (d < 0)
            {
                return false;
            }

            v[i] = d;
        }

        if (n <= 4)
        {
            byte r = (byte)(v[0] * 17), g = (byte)(v[1] * 17), b = (byte)(v[2] * 17);
            color = new Rgba(r, g, b, n == 4 ? v[3] * 17 / 255.0 : 1);
        }
        else
        {
            color = new Rgba((byte)(v[0] * 16 + v[1]), (byte)(v[2] * 16 + v[3]), (byte)(v[4] * 16 + v[5]), n == 8 ? (v[6] * 16 + v[7]) / 255.0 : 1);
        }

        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private static bool TryFunction(ReadOnlySpan<char> args, out Rgba color)
    {
        color = default;
        Span<double> v = stackalloc double[4];
        int count = 0;
        int i = 0;
        while (true)
        {
            while (i < args.Length && (args[i] is ' ' or ',' or '/' or '\t'))
            {
                i++;
            }

            if (i >= args.Length)
            {
                break;
            }

            int start = i;
            while (i < args.Length && args[i] is not (' ' or ',' or '/' or '\t'))
            {
                i++;
            }

            if (count == 4)
            {
                return false;
            }

            ReadOnlySpan<char> tok = args[start..i];
            bool pct = tok.EndsWith("%", StringComparison.Ordinal);
            if (pct)
            {
                tok = tok[..^1];
            }

            if (!double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d))
            {
                return false;
            }

            v[count++] = pct ? (count == 4 ? d / 100 : d * 2.55) : d;
        }

        if (count is not (3 or 4))
        {
            return false;
        }

        color = new Rgba(ToByte(v[0]), ToByte(v[1]), ToByte(v[2]), count == 4 ? Math.Clamp(v[3], 0, 1) : 1);
        return true;
    }

    private static byte ToByte(double d) => (byte)Math.Clamp((int)Math.Round(d), 0, 255);
}

/// <summary>
/// The colours of the pixels behind an element's text, summarised by the caller from a screenshot: the median and the
/// 5th and 95th percentiles per channel. Chrome's snapshot carries no pixels, so these come from the caller.
/// </summary>
/// <param name="Median">Median colour.</param>
/// <param name="P5">5th-percentile colour.</param>
/// <param name="P95">95th-percentile colour.</param>
/// <param name="PixelCount">Number of pixels sampled; 0 when not stated.</param>
public readonly record struct BackgroundSample(Rgba Median, Rgba P5, Rgba P95, int PixelCount);

/// <summary>A pseudo-element's box, attached to its originating element: it has no node of its own in a OneHtml document.</summary>
public readonly struct GeneratedBox
{
    private readonly RenderedFacts _facts;
    private readonly int _index;

    internal GeneratedBox(RenderedFacts facts, int index)
    {
        _facts = facts;
        _index = index;
    }

    /// <summary>The pseudo type as the protocol names it: <c>before</c>, <c>after</c>, <c>marker</c>, <c>first-letter</c>, <c>backdrop</c> and so on.</summary>
    public string PseudoType => _facts.GenType(_index);

    /// <summary>The pseudo identifier for functional pseudo-elements, or an empty string.</summary>
    public string Identifier => _facts.GenIdentifier(_index);

    /// <summary>Whether the pseudo-element generated a box.</summary>
    public bool HasLayout => Layout.IsValid;

    /// <summary>The pseudo-element's own box facts.</summary>
    public BoxFacts Layout => _facts.GenBox(_index);

    /// <summary>The boxes of the pseudo-element and of its generated children (the text of generated content has its own boxes and text runs), in layout order.</summary>
    public IReadOnlyList<BoxFacts> Boxes => _facts.GenAllBoxes(_index);

    /// <summary>The text of the generated content (the layout text of the pseudo-element and its descendants), or an empty string.</summary>
    public string Text => _facts.GenText(_index);

    /// <summary>The number of snapshot nodes the pseudo-element consists of, itself included.</summary>
    public int SnapshotNodeCount => _facts.GenNodeCount(_index);
}
