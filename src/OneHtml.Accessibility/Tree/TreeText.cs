using System;
using System.Collections.Generic;
using System.Text;

namespace RecursiveAI.OneHtml.Accessibility.Tree;

/// <summary>ASCII-only text helpers: HTML's whitespace and case rules, never the current culture.</summary>
internal static class TreeText
{
    public static bool IsWs(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';

    public static bool IsBlank(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
        {
            if (!IsWs(c))
            {
                return false;
            }
        }

        return true;
    }

    public static string AsciiLower(string s)
    {
        int i = 0;
        while (i < s.Length && s[i] is not (>= 'A' and <= 'Z'))
        {
            i++;
        }

        if (i == s.Length)
        {
            return s;
        }

        return string.Create(s.Length, s, static (span, src) =>
        {
            for (int k = 0; k < span.Length; k++)
            {
                char c = src[k];
                span[k] = c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
            }
        });
    }

    public static bool EqualsIgnoreCase(string? a, string b) => a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase) && IsAscii(a);

    private static bool IsAscii(string s)
    {
        foreach (char c in s)
        {
            if (c > 127)
            {
                return false;
            }
        }

        return true;
    }

    public static string Trim(string s) => s.Trim(' ', '\t', '\n', '\f', '\r');

    /// <summary>Collapses runs of ASCII whitespace to one space and trims both ends: the flattening the accessible name gets.</summary>
    public static string Collapse(ReadOnlySpan<char> s)
    {
        var sb = new StringBuilder(s.Length);
        bool pendingSpace = false;
        foreach (char c in s)
        {
            if (IsWs(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Applies a computed <c>text-transform</c> keyword to text (CSS Text 3 section 2.1): <c>uppercase</c>, <c>lowercase</c> and <c>capitalize</c> (the first letter of each word); anything else leaves it unchanged.</summary>
    public static string ApplyTextTransform(string data, string transform)
    {
        if (transform.Length == 0 || transform == "none")
        {
            return data;
        }

        if (transform.StartsWith("uppercase", StringComparison.Ordinal))
        {
            return data.ToUpperInvariant();
        }

        if (transform.StartsWith("lowercase", StringComparison.Ordinal))
        {
            return data.ToLowerInvariant();
        }

        if (transform.StartsWith("capitalize", StringComparison.Ordinal))
        {
            var sb = new StringBuilder(data.Length);
            bool start = true;
            foreach (char c in data)
            {
                sb.Append(start && char.IsLetter(c) ? char.ToUpperInvariant(c) : c);
                start = char.IsWhiteSpace(c) || (start && !char.IsLetter(c));
            }

            return sb.ToString();
        }

        return data;
    }

    public static bool HasNonWhitespace(StringBuilder sb, int from)
    {
        for (int i = from; i < sb.Length; i++)
        {
            if (!IsWs(sb[i]))
            {
                return true;
            }
        }

        return false;
    }
}
