using System;
using System.Text;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneHtml.Accessibility.Aria;

/// <summary>The input types of HTML 4.10.5.1, as far as the accessibility mappings tell them apart.</summary>
internal enum HtmlInputType : byte
{
    NotInput,
    Text,
    Search,
    Telephone,
    Url,
    Email,
    Password,
    Date,
    Month,
    Week,
    Time,
    DateTimeLocal,
    Number,
    Range,
    Color,
    Checkbox,
    Radio,
    File,
    Submit,
    Image,
    Reset,
    Button,
    Hidden,
}

/// <summary>Attribute and element helpers that read the DOM without allocating.</summary>
internal static class HtmlSemantics
{
    /// <summary>Finds an attribute with no namespace, ASCII case-insensitively. Returns false when absent.</summary>
    public static bool TryGetAttribute(Node element, string lowerName, out ReadOnlySpan<char> value)
    {
        AttributeList list = element.Attributes;
        for (int i = 0; i < list.Count; i++)
        {
            Attr a = list[i];
            if (a.Namespace == AttributeNamespace.None && Ascii.EqualsIgnoreCase(a.LocalName, lowerName))
            {
                value = a.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public static bool Has(Node element, string lowerName) => TryGetAttribute(element, lowerName, out _);

    public static bool IsHtml(Node n) => n.IsElement && n.Namespace == ElementNamespace.Html;

    public static bool IsHtml(Node n, string localName) => IsHtml(n) && string.Equals(n.LocalName, localName, StringComparison.Ordinal);

    public static bool EqualsToken(ReadOnlySpan<char> value, string lower) => Ascii.EqualsIgnoreCase(value, lower);

    public static HtmlInputType GetInputType(Node n)
    {
        if (!IsHtml(n, "input"))
        {
            return HtmlInputType.NotInput;
        }

        if (!TryGetAttribute(n, "type", out ReadOnlySpan<char> t))
        {
            return HtmlInputType.Text;
        }

        // HTML 4.10.5.1: an invalid or missing value is the Text state.
        return t.Length switch
        {
            3 when EqualsToken(t, "tel") => HtmlInputType.Telephone,
            3 when EqualsToken(t, "url") => HtmlInputType.Url,
            4 when EqualsToken(t, "text") => HtmlInputType.Text,
            4 when EqualsToken(t, "date") => HtmlInputType.Date,
            4 when EqualsToken(t, "time") => HtmlInputType.Time,
            4 when EqualsToken(t, "week") => HtmlInputType.Week,
            5 when EqualsToken(t, "email") => HtmlInputType.Email,
            5 when EqualsToken(t, "month") => HtmlInputType.Month,
            5 when EqualsToken(t, "range") => HtmlInputType.Range,
            5 when EqualsToken(t, "color") => HtmlInputType.Color,
            5 when EqualsToken(t, "radio") => HtmlInputType.Radio,
            5 when EqualsToken(t, "image") => HtmlInputType.Image,
            5 when EqualsToken(t, "reset") => HtmlInputType.Reset,
            6 when EqualsToken(t, "search") => HtmlInputType.Search,
            6 when EqualsToken(t, "number") => HtmlInputType.Number,
            6 when EqualsToken(t, "submit") => HtmlInputType.Submit,
            6 when EqualsToken(t, "button") => HtmlInputType.Button,
            6 when EqualsToken(t, "hidden") => HtmlInputType.Hidden,
            4 when EqualsToken(t, "file") => HtmlInputType.File,
            8 when EqualsToken(t, "checkbox") => HtmlInputType.Checkbox,
            8 when EqualsToken(t, "password") => HtmlInputType.Password,
            14 when EqualsToken(t, "datetime-local") => HtmlInputType.DateTimeLocal,
            _ => HtmlInputType.Text,
        };
    }

    /// <summary>HTML 2.3.4.3 "rules for parsing integers": leading whitespace, optional sign, digits, anything after is ignored.</summary>
    public static bool TryParseInteger(ReadOnlySpan<char> s, out long value)
    {
        value = 0;
        int i = 0;
        while (i < s.Length && AsciiText.IsWhitespace(s[i]))
        {
            i++;
        }

        bool negative = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
        {
            negative = s[i] == '-';
            i++;
        }

        if (i >= s.Length || s[i] is < '0' or > '9')
        {
            return false;
        }

        long v = 0;
        while (i < s.Length && s[i] is >= '0' and <= '9')
        {
            // Saturate instead of overflowing: only the sign and rough size matter to callers.
            v = v < (long.MaxValue - 9) / 10 ? (v * 10) + (s[i] - '0') : v;
            i++;
        }

        value = negative ? -v : v;
        return true;
    }

    /// <summary>
    /// True when the element is a form control that the HTML Standard lets be disabled with the <c>disabled</c>
    /// attribute (HTML 4.10.18.5: button, input, select, textarea, fieldset; optgroup and option as well).
    /// </summary>
    public static bool CanBeDisabled(Node n) => IsHtml(n) && n.LocalName is "button" or "input" or "select" or "textarea" or "fieldset" or "optgroup" or "option";

    /// <summary>True for the elements that a fieldset's disabled state reaches (not fieldset itself, optgroup or option).</summary>
    public static bool IsFieldsetDisableable(Node n) => IsHtml(n) && n.LocalName is "button" or "input" or "select" or "textarea";
}
