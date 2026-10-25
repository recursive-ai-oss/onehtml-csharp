using System;
using System.Text;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Accessibility.Aria;

/// <summary>
/// ASCII case-insensitive string comparison. Role tokens, attribute names and token values are matched this way (HTML
/// "ASCII case-insensitive"); an ordinal-ignore-case comparison would also fold characters such as U+017F, so it is
/// not used.
/// </summary>
internal sealed class AsciiIgnoreCaseComparer : IEqualityComparer<string>, IAlternateEqualityComparer<ReadOnlySpan<char>, string>
{
    public static readonly AsciiIgnoreCaseComparer Instance = new();

    public bool Equals(string? x, string? y) => x is not null && y is not null && Ascii.EqualsIgnoreCase(x, y);

    public int GetHashCode(string obj) => GetHashCode(obj.AsSpan());

    public bool Equals(ReadOnlySpan<char> alternate, string other) => Ascii.EqualsIgnoreCase(alternate, other);

    public int GetHashCode(ReadOnlySpan<char> alternate)
    {
        uint h = 2166136261;
        foreach (char c in alternate)
        {
            char l = c is >= 'A' and <= 'Z' ? (char)(c + 0x20) : c;
            h = (h ^ l) * 16777619;
        }

        return (int)h;
    }

    public string Create(ReadOnlySpan<char> alternate) => alternate.ToString();
}

internal static class AsciiText
{
    /// <summary>ASCII whitespace as the HTML Standard defines it: tab, line feed, form feed, carriage return, space.</summary>
    public static bool IsWhitespace(char c) => c is '\t' or '\n' or '\f' or '\r' or ' ';

    public static ReadOnlySpan<char> Trim(ReadOnlySpan<char> s)
    {
        int start = 0;
        int end = s.Length;
        while (start < end && IsWhitespace(s[start]))
        {
            start++;
        }

        while (end > start && IsWhitespace(s[end - 1]))
        {
            end--;
        }

        return s[start..end];
    }

    /// <summary>Reads the next ASCII-whitespace-separated token starting at <paramref name="pos"/> and advances past it.</summary>
    public static bool NextToken(ReadOnlySpan<char> s, ref int pos, out ReadOnlySpan<char> token)
    {
        while (pos < s.Length && IsWhitespace(s[pos]))
        {
            pos++;
        }

        if (pos >= s.Length)
        {
            token = default;
            return false;
        }

        int start = pos;
        while (pos < s.Length && !IsWhitespace(s[pos]))
        {
            pos++;
        }

        token = s[start..pos];
        return true;
    }
}
