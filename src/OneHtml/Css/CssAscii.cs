using System;

namespace RecursiveAI.OneHtml.Css;

/// <summary>ASCII case folding, the only kind CSS uses (CSS Syntax 2.1: keywords match ASCII case-insensitively).</summary>
internal static class CssAscii
{
    /// <summary>Lower-cases A to Z and leaves every other character alone, so that U+212A (Kelvin sign) does not become <c>k</c>.</summary>
    public static string AsciiLower(this string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        int i = 0;
        while (i < s.Length && !(s[i] >= 'A' && s[i] <= 'Z'))
        {
            i++;
        }

        if (i == s.Length)
        {
            return s;
        }

        return string.Create(s.Length, s, static (span, source) =>
        {
            for (int k = 0; k < span.Length; k++)
            {
                char c = source[k];
                span[k] = c >= 'A' && c <= 'Z' ? (char)(c + 32) : c;
            }
        });
    }
}
