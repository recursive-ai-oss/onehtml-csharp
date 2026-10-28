using System;
using System.Globalization;
using System.Text;
using System.Threading;

namespace RecursiveAI.OneHtml.Css;

/// <summary>The user-agent sheets, parsed once: the CSS of the HTML Standard's Rendering section (see <c>docs/cascade.md</c>).</summary>
internal static class UserAgentSheets
{
    private static readonly Lazy<Stylesheet> StandardSheet = new(() => Stylesheet.Parse(UserAgentStylesheetData.Standard + PseudoElementDefaults, "about:user-agent", null, default), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<Stylesheet> QuirksSheet = new(() => Stylesheet.Parse(UserAgentStylesheetData.Quirks, "about:user-agent-quirks", null, default), LazyThreadSafetyMode.ExecutionAndPublication);

    // What Chrome's computed style of ::placeholder shows (black-box): grey text, no pointer events, a block box, the line height an input sets
    // (a textarea takes the author's), and no box while a value hides the placeholder; the author cannot change the !important ones. The HTML Standard has no rule for it.
    //
    // The parts of a range slider are real elements inside Chrome's shadow tree. Its DevTools protocol reports their computed style: block boxes
    // with border-box sizing, an isolated bidi context and the initial values of every property that does not inherit (the input's border, margin,
    // padding, background and opacity do not reach them). The track also grows to fill the flex container around it, centres itself and has no
    // theme appearance. Sizes are used values from layout and are not declared here. The fill has no part in Chrome, so it has no defaults.
    private const string PseudoElementDefaults = """

        input[placeholder]::placeholder, textarea[placeholder]::placeholder { color: #757575; display: block !important; pointer-events: none !important; }
        input[placeholder]::placeholder { line-height: normal !important; }
        input[placeholder]:not(:placeholder-shown)::placeholder, textarea[placeholder]:not(:placeholder-shown)::placeholder { display: none !important; }

        input[type=range i]::slider-track { display: block; box-sizing: border-box; flex-grow: 1; flex-basis: 0%; align-self: center; appearance: none; unicode-bidi: isolate; }
        input[type=range i]::slider-thumb { display: block; box-sizing: border-box; appearance: auto; unicode-bidi: isolate; }
        """;

    public static Stylesheet Standard => StandardSheet.Value;

    public static Stylesheet Quirks => QuirksSheet.Value;
}

/// <summary>
/// HTML presentational attributes as the declarations they stand for (HTML 15.3 "Rendering: non-replaced elements" and
/// 15.4 "Rendering: replaced elements", "maps to the pixel length property"). They cascade as author declarations of
/// zero specificity that lose to every author rule (CSS Cascade 5 §6.3 "presentational hints").
/// </summary>
internal static class PresentationalHints
{
    /// <summary>The declarations for an element, as CSS text, or null when it has none.</summary>
    public static string? For(Node element)
    {
        if (element.Namespace != ElementNamespace.Html || !element.HasAttributes)
        {
            return null;
        }

        string name = element.LocalName;
        StringBuilder? sb = null;

        switch (name)
        {
            case "body" or "table" or "thead" or "tbody" or "tfoot" or "tr" or "td" or "th" or "marquee":
                if (element.GetAttribute("bgcolor") is { } bg && TryLegacyColor(bg, out string bgc))
                {
                    Append(ref sb, "background-color", bgc);
                }

                if (name == "body" && element.GetAttribute("text") is { } text && TryLegacyColor(text, out string tc))
                {
                    Append(ref sb, "color", tc);
                }

                break;
            case "a" or "area":
                // HTML 15.3.1 "Page": the body element's link attribute colours the unvisited links of the document. (:visited never matches here.)
                if (element.HasAttribute("href") && BodyOf(element) is { IsNull: false } body && body.GetAttribute("link") is { } link && TryLegacyColor(link, out string lc))
                {
                    Append(ref sb, "color", lc);
                }

                break;
            case "font":
                if (element.GetAttribute("color") is { } fc && TryLegacyColor(fc, out string fcc))
                {
                    Append(ref sb, "color", fcc);
                }

                if (element.GetAttribute("face") is { Length: > 0 } face)
                {
                    Append(ref sb, "font-family", face);
                }

                if (element.GetAttribute("size") is { } size && TryFontSize(size, out string keyword))
                {
                    Append(ref sb, "font-size", keyword);
                }

                break;
            case "div" or "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                if (element.GetAttribute("align") is { } align)
                {
                    string a = align.Trim();
                    foreach (string v in new[] { "left", "right", "center", "justify" })
                    {
                        if (string.Equals(a, v, StringComparison.OrdinalIgnoreCase))
                        {
                            Append(ref sb, "text-align", v);
                            break;
                        }
                    }
                }

                break;
        }

        if (name is "img" or "video" or "canvas" or "iframe" or "embed" or "object" or "table" or "td" or "th")
        {
            if (element.GetAttribute("width") is { } w && TryDimension(w, out string wv))
            {
                Append(ref sb, "width", wv);
            }

            if (element.GetAttribute("height") is { } h && TryDimension(h, out string hv))
            {
                Append(ref sb, "height", hv);
            }
        }

        if (name is "td" or "th" && element.HasAttribute("nowrap"))
        {
            Append(ref sb, "white-space", "nowrap");
        }

        return sb?.ToString();
    }

    // The body element of the document (HTML 3.1.3): the first body child of the root element.
    private static Node BodyOf(Node element)
    {
        Node root = element.Document!.DocumentElement;
        if (root.IsNull)
        {
            return default;
        }

        for (Node c = root.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.IsElement && c.Namespace == ElementNamespace.Html && c.LocalName == "body")
            {
                return c;
            }
        }

        return default;
    }

    private static void Append(ref StringBuilder? sb, string property, string value)
    {
        sb ??= new StringBuilder();
        sb.Append(property).Append(": ").Append(value).Append("; ");
    }

    /// <summary>HTML 2.3.5 "rules for parsing dimension values", reduced to a CSS length or percentage.</summary>
    private static bool TryDimension(string input, out string css)
    {
        css = string.Empty;
        int i = 0;
        while (i < input.Length && input[i] is ' ' or '\t' or '\n' or '\r' or '\f')
        {
            i++;
        }

        int start = i;
        while (i < input.Length && input[i] is >= '0' and <= '9')
        {
            i++;
        }

        if (i == start)
        {
            return false;
        }

        string number = input[start..i];
        if (i < input.Length && input[i] == '.')
        {
            int f = ++i;
            while (i < input.Length && input[i] is >= '0' and <= '9')
            {
                i++;
            }

            if (i > f)
            {
                number += "." + input[f..i];
            }
        }

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsInfinity(value))
        {
            return false;
        }

        css = (i < input.Length && input[i] == '%')
            ? number + "%"
            : number + "px";
        return true;
    }

    private static bool TryFontSize(string input, out string keyword)
    {
        keyword = string.Empty;
        string s = input.Trim();
        if (s.Length == 0)
        {
            return false;
        }

        int sign = 0;
        if (s[0] == '+')
        {
            sign = 1;
            s = s[1..];
        }
        else if (s[0] == '-')
        {
            sign = -1;
            s = s[1..];
        }

        int i = 0;
        while (i < s.Length && s[i] is >= '0' and <= '9' && i < 9)
        {
            i++;
        }

        if (i == 0)
        {
            return false;
        }

        int value = int.Parse(s[..i], CultureInfo.InvariantCulture);
        if (sign == 1)
        {
            value = 3 + value;
        }
        else if (sign == -1)
        {
            value = 3 - value;
        }

        value = Math.Clamp(value, 1, 7);
        keyword = value switch
        {
            1 => "x-small",
            2 => "small",
            3 => "medium",
            4 => "large",
            5 => "x-large",
            6 => "xx-large",
            _ => "xxx-large",
        };
        return true;
    }

    /// <summary>HTML 2.3.4.4 "rules for parsing a legacy colour value", giving a CSS colour.</summary>
    internal static bool TryLegacyColor(string input, out string css)
    {
        css = string.Empty;
        string s = input.Trim(' ', '\t', '\n', '\r', '\f');
        if (s.Length == 0 || string.Equals(s, "transparent", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool letters = true;
        foreach (char c in s)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z'))
            {
                letters = false;
                break;
            }
        }

        if (letters && CssColor.TryParse(s, out CssColor named) && !named.IsCurrentColor && named.SystemName is null)
        {
            css = s.AsciiLower();
            return true;
        }

        if (s.Length == 4 && s[0] == '#' && IsHex(s[1]) && IsHex(s[2]) && IsHex(s[3]))
        {
            css = "#" + s[1] + s[1] + s[2] + s[2] + s[3] + s[3];
            return true;
        }

        var sb = new StringBuilder(s.Length);
        foreach (Rune r in s.EnumerateRunes())
        {
            if (r.Value > 0xFFFF)
            {
                sb.Append("00");
            }
            else
            {
                sb.Append((char)r.Value);
            }
        }

        s = sb.ToString();
        if (s.Length > 128)
        {
            s = s[..128];
        }

        if (s.Length > 0 && s[0] == '#')
        {
            s = s[1..];
        }

        var hex = new StringBuilder(s.Length + 2);
        foreach (char c in s)
        {
            hex.Append(IsHex(c) ? c : '0');
        }

        while (hex.Length == 0 || hex.Length % 3 != 0)
        {
            hex.Append('0');
        }

        int len = hex.Length / 3;
        string r1 = hex.ToString(0, len);
        string g1 = hex.ToString(len, len);
        string b1 = hex.ToString(2 * len, len);
        if (len > 8)
        {
            r1 = r1[(len - 8)..];
            g1 = g1[(len - 8)..];
            b1 = b1[(len - 8)..];
            len = 8;
        }

        while (len > 2 && r1[0] == '0' && g1[0] == '0' && b1[0] == '0')
        {
            r1 = r1[1..];
            g1 = g1[1..];
            b1 = b1[1..];
            len--;
        }

        if (len > 2)
        {
            r1 = r1[..2];
            g1 = g1[..2];
            b1 = b1[..2];
        }

        css = "#" + Pad(r1) + Pad(g1) + Pad(b1);
        return true;

        static string Pad(string x) => x.Length == 1 ? "0" + x : x;
    }

    private static bool IsHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
