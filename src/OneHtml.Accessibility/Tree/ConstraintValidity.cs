using System;
using System.Globalization;
using System.Text.RegularExpressions;
using RecursiveAI.OneHtml.Accessibility.Aria;

namespace RecursiveAI.OneHtml.Accessibility.Tree;

/// <summary>
/// The constraint-validation failures Chromium reports as <c>invalid</c> on a form control (HTML section 4.10.20.3), from the markup alone:
/// no script has run, so a value is the <c>value</c> attribute and nothing was edited by a user. What Chromium reports and what it leaves
/// out is in the Chromium profile entry <c>constraint-invalid</c>.
/// </summary>
internal static class ConstraintValidity
{
    private const int MaxPatternLength = 256;
    private const int MaxValueLength = 4096;

    /// <summary>True when the control fails a constraint Chromium counts.</summary>
    /// <param name="el">The HTML element.</param>
    /// <param name="nativeDisabled">The control is disabled by its own attribute or a disabled fieldset.</param>
    /// <param name="helpers">Whole-tree questions (radio groups).</param>
    public static bool Fails(Node el, bool nativeDisabled, StateHelpers? helpers)
    {
        if (el.LocalName is not ("input" or "select" or "textarea"))
        {
            return false;
        }

        // An explicit aria-invalid=false is what the author says, and Chromium reports it in place of the constraint.
        if (el.GetAttribute("aria-invalid") is { } ai && TreeText.EqualsIgnoreCase(TreeText.Trim(ai), "false"))
        {
            return false;
        }

        // HTML 4.10.20.2: a disabled control, or one with the readonly attribute, is barred from constraint validation (Chromium applies
        // the attribute to every control that has it, a checkbox included).
        if (nativeDisabled || HtmlSemantics.Has(el, "readonly"))
        {
            return false;
        }

        switch (el.LocalName)
        {
            case "textarea":
                // An empty required text area is not reported (observed in Chromium).
                return false;
            case "select":
                return HtmlSemantics.Has(el, "required") && SelectValueMissing(el);
        }

        HtmlInputType type = HtmlSemantics.GetInputType(el);
        string? value = el.GetAttribute("value");
        bool required = HtmlSemantics.Has(el, "required");
        switch (type)
        {
            case HtmlInputType.Checkbox:
                return required && !HtmlSemantics.Has(el, "checked");
            case HtmlInputType.Radio:
                return helpers?.RadioGroupMissing(el) ?? (required && !HtmlSemantics.Has(el, "checked"));
            case HtmlInputType.File:
                // Static markup holds no chosen file.
                return required;
            case HtmlInputType.Date or HtmlInputType.Month or HtmlInputType.Week or HtmlInputType.Time or HtmlInputType.DateTimeLocal:
                bool valid = value is not null && IsValidTemporal(type, value);
                if (!valid)
                {
                    // A value that is not a valid date and time string is sanitized to the empty string.
                    return required;
                }

                return type is HtmlInputType.Date or HtmlInputType.Month && OutOfTemporalRange(type, value!, el);
            case HtmlInputType.Number:
                return Num(value) is { } v && (Num(el.GetAttribute("min")) is { } min && v < min || Num(el.GetAttribute("max")) is { } max && v > max || StepMismatch(el, value!));
            case HtmlInputType.Email:
                return value is { Length: > 0 and <= MaxValueLength } && (!ValidEmail(value, HtmlSemantics.Has(el, "multiple")) || PatternMismatch(el, value));
            case HtmlInputType.Url:
                return value is { Length: > 0 and <= MaxValueLength } && (!ValidUrl(Strip(value)) || PatternMismatch(el, value));
            case HtmlInputType.Text or HtmlInputType.Search or HtmlInputType.Telephone or HtmlInputType.Password:
                return value is { Length: > 0 and <= MaxValueLength } && PatternMismatch(el, value);
            default:
                return false;
        }
    }

    /// <summary>
    /// HTML 4.10.5.1.12 step mismatch of a number: the value is not a whole number of steps from the step base, which is the <c>min</c>, else the
    /// <c>value</c> attribute itself (so a field with no min never mismatches). The step is the <c>step</c> attribute, 1 when missing or invalid.
    /// </summary>
    private static bool StepMismatch(Node el, string value)
    {
        if (el.GetAttribute("step") is { } stepText && TreeText.EqualsIgnoreCase(TreeText.Trim(stepText), "any"))
        {
            return false;
        }

        if (!decimal.TryParse(el.GetAttribute("min"), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal min)
            || !decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal v))
        {
            return false;
        }

        decimal step = decimal.TryParse(el.GetAttribute("step"), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal st) && st > 0 ? st : 1;
        return (v - min) % step != 0;
    }

    private static double? Num(string? v) =>
        v is not null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d) ? d : null;

    /// <summary>The value sanitization algorithm of the text types: no newlines, and a url or email value is stripped of surrounding space.</summary>
    private static string Strip(string value) => value.Trim(' ', '\t', '\n', '\r', '\f');

    private static bool PatternMismatch(Node el, string value)
    {
        if (el.GetAttribute("pattern") is not { Length: > 0 and <= MaxPatternLength } pattern)
        {
            return false;
        }

        string text = value.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
        try
        {
            // The pattern must match the whole value (HTML 4.10.5.3.6). NonBacktracking keeps the time linear in the value whatever the
            // author wrote; a pattern it cannot compile (lookaround, back-reference) is treated as no pattern, as an invalid one is.
            var regex = new Regex("^(?:" + pattern + ")\\z", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            return !regex.IsMatch(text);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>HTML 4.10.5.1.5: the valid e-mail address grammar (ASCII local part and a dotted host), for one address or a comma-separated list.</summary>
    private static bool ValidEmail(string value, bool multiple)
    {
        if (!multiple)
        {
            return ValidAddress(Strip(value));
        }

        foreach (string part in value.Split(','))
        {
            if (!ValidAddress(Strip(part)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidAddress(string s)
    {
        int at = s.IndexOf('@', StringComparison.Ordinal);
        if (at < 1 || at == s.Length - 1 || s.IndexOf('@', at + 1) >= 0)
        {
            return false;
        }

        for (int i = 0; i < at; i++)
        {
            char c = s[i];
            if (!(char.IsAsciiLetterOrDigit(c) || ".!#$%&'*+/=?^_`{|}~-".Contains(c, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        foreach (string label in s[(at + 1)..].Split('.'))
        {
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-')
            {
                return false;
            }

            foreach (char c in label)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c == '-'))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>An absolute URL: a scheme, and for the special schemes an authority with a host (URL Standard, absolute-URL string).</summary>
    private static bool ValidUrl(string s)
    {
        int colon = s.IndexOf(':', StringComparison.Ordinal);
        if (colon < 1 || !char.IsAsciiLetter(s[0]))
        {
            return false;
        }

        for (int i = 1; i < colon; i++)
        {
            if (!(char.IsAsciiLetterOrDigit(s[i]) || s[i] is '+' or '-' or '.'))
            {
                return false;
            }
        }

        foreach (char c in s)
        {
            if (c <= ' ')
            {
                return false;
            }
        }

        string scheme = TreeText.AsciiLower(s[..colon]);
        if (scheme is "http" or "https" or "ftp" or "ws" or "wss")
        {
            string rest = s[(colon + 1)..].TrimStart('/', '\\');
            return rest.Length > 0 && rest[0] is not ('/' or '?' or '#' or '\\');
        }

        return true;
    }

    private static bool SelectValueMissing(Node select)
    {
        bool multiple = HtmlSemantics.Has(select, "multiple");
        bool oneRow = !multiple && !(select.GetAttribute("size") is { } sz && int.TryParse(sz, NumberStyles.None, CultureInfo.InvariantCulture, out int rows) && rows > 1);
        Node first = default;
        Node lastSelected = default;
        Node firstEnabled = default;
        int visited = 0;
        for (Node d = select.NextInDocumentOrder(select); !d.IsNull && visited++ < 100_000; d = d.NextInDocumentOrder(select))
        {
            if (!d.IsElement || d.Namespace != ElementNamespace.Html || d.LocalName != "option")
            {
                continue;
            }

            if (first.IsNull)
            {
                first = d;
            }

            if (d.HasAttribute("selected"))
            {
                lastSelected = d;
            }

            if (firstEnabled.IsNull && !d.HasAttribute("disabled") && !(d.ParentElement is { IsNull: false } g && g.LocalName == "optgroup" && g.HasAttribute("disabled")))
            {
                firstEnabled = d;
            }
        }

        if (!oneRow)
        {
            // A list box starts with nothing chosen (HTML 4.10.7): a required one with no selected option lacks a value.
            return lastSelected.IsNull;
        }

        // HTML 4.10.7: a drop-down chooses the last option marked selected, else the first that is not disabled; when that is the first
        // option and its value is empty, it is the placeholder label option and the required select lacks a value (HTML 4.10.7, valueMissing).
        Node chosen = !lastSelected.IsNull ? lastSelected : firstEnabled;
        return chosen.IsNull || (chosen.Index == first.Index && (first.GetAttribute("value") ?? first.TextContent ?? string.Empty).Length == 0);
    }

    internal static bool IsValidTemporal(HtmlInputType type, string v)
    {
        switch (type)
        {
            case HtmlInputType.Date:
                return TryDate(v, out _, out _, out _);
            case HtmlInputType.Month:
                return TryMonth(v, out _, out _);
            case HtmlInputType.Time:
                return TryTime(v);
            case HtmlInputType.DateTimeLocal:
                int t = v.IndexOfAny(['T', ' ']);
                return t > 0 && TryDate(v[..t], out _, out _, out _) && TryTime(v[(t + 1)..]);
            case HtmlInputType.Week:
                // YYYY-Www, week 01 to 53.
                int w = v.IndexOf("-W", StringComparison.Ordinal);
                return w >= 4 && v.Length == w + 4 && int.TryParse(v.AsSpan(0, w), NumberStyles.None, CultureInfo.InvariantCulture, out int y) && y > 0
                    && int.TryParse(v.AsSpan(w + 2), NumberStyles.None, CultureInfo.InvariantCulture, out int wk) && wk is >= 1 and <= 53;
            default:
                return false;
        }
    }

    private static bool TryMonth(string v, out int year, out int month)
    {
        year = month = 0;
        int dash = v.IndexOf('-', StringComparison.Ordinal);
        return dash >= 4 && v.Length == dash + 3 && int.TryParse(v.AsSpan(0, dash), NumberStyles.None, CultureInfo.InvariantCulture, out year) && year > 0
            && int.TryParse(v.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out month) && month is >= 1 and <= 12;
    }

    private static bool TryDate(string v, out int year, out int month, out int day)
    {
        year = month = day = 0;
        if (v.Length < 10 || v[^3] != '-')
        {
            return false;
        }

        return TryMonth(v[..^3], out year, out month) && int.TryParse(v.AsSpan(v.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out day)
            && day >= 1 && day <= DateTime.DaysInMonth(Math.Min(year, 9999), month);
    }

    private static bool TryTime(string v)
    {
        // HH:MM, optionally :SS and .fraction.
        if (v.Length < 5 || v[2] != ':' || !int.TryParse(v.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int h) || h > 23
            || !int.TryParse(v.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int m) || m > 59)
        {
            return false;
        }

        if (v.Length == 5)
        {
            return true;
        }

        return v.Length >= 8 && v[5] == ':' && int.TryParse(v.AsSpan(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int s) && s <= 59
            && (v.Length == 8 || v[8] == '.' && v.Length > 9 && v.AsSpan(9).IndexOfAnyExceptInRange('0', '9') < 0);
    }

    /// <summary>A date or month below its <c>min</c> or above its <c>max</c>; the strings compare in order when both are valid and the years have the same length.</summary>
    private static bool OutOfTemporalRange(HtmlInputType type, string value, Node el)
    {
        string? min = el.GetAttribute("min");
        string? max = el.GetAttribute("max");
        return min is not null && IsValidTemporal(type, min) && min.Length == value.Length && string.CompareOrdinal(value, min) < 0
            || max is not null && IsValidTemporal(type, max) && max.Length == value.Length && string.CompareOrdinal(value, max) > 0;
    }
}
