using System;
using System.Text;

namespace RecursiveAI.OneHtml.Css;

/// <summary>Character classes and escape decoding from CSS Syntax §4.2 and §4.3.7.</summary>
internal static class CssChars
{
    // CSS Syntax §3.3: CR, FF and CRLF become LF during preprocessing, so all three are newlines here.
    public static bool IsNewline(char c) => c is '\n' or '\r' or '\f';

    // CSS Syntax §4.2 "whitespace".
    public static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    public static bool IsDigit(char c) => (uint)(c - '0') <= 9;

    public static bool IsHexDigit(char c) => (uint)(c - '0') <= 9 || (uint)((c | 0x20) - 'a') <= 5;

    public static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    public static bool IsLetter(char c) => (uint)((c | 0x20) - 'a') <= 25;

    // CSS Syntax §4.2 "non-ASCII ident code point". Surrogates count: a pair is >= U+10000, and a lone surrogate
    // is replaced by U+FFFD (§3.3), which is also an ident code point. U+0000 is replaced by U+FFFD too.
    public static bool IsNonAsciiIdent(char c)
    {
        if (c < 0x80)
        {
            return c == 0;
        }

        return c == 0xB7
            || (c >= 0xC0 && c <= 0xD6)
            || (c >= 0xD8 && c <= 0xF6)
            || (c >= 0xF8 && c <= 0x37D)
            || (c >= 0x37F && c <= 0x1FFF)
            || c == 0x200C || c == 0x200D || c == 0x203F || c == 0x2040
            || (c >= 0x2070 && c <= 0x218F)
            || (c >= 0x2C00 && c <= 0x2FEF)
            || (c >= 0x3001 && c <= 0xD7FF)
            || (c >= 0xD800 && c <= 0xDFFF)
            || (c >= 0xF900 && c <= 0xFDCF)
            || (c >= 0xFDF0 && c <= 0xFFFD);
    }

    public static bool IsIdentStart(char c) => IsLetter(c) || c == '_' || IsNonAsciiIdent(c);

    public static bool IsIdentChar(char c) => IsIdentStart(c) || IsDigit(c) || c == '-';

    // CSS Syntax §4.2 "non-printable code point". U+0000 is not listed because preprocessing turned it into U+FFFD.
    public static bool IsNonPrintable(char c) => (c >= 1 && c <= 8) || c == 0xB || (c >= 0xE && c <= 0x1F) || c == 0x7F;

    /// <summary>
    /// Decodes the escapes in a raw ident, string or url body (CSS Syntax §4.3.5, §4.3.7). A backslash followed by a
    /// newline (only legal in strings) produces nothing; U+0000 and lone surrogates become U+FFFD.
    /// </summary>
    public static string Decode(ReadOnlySpan<char> raw, bool inString = false)
    {
        int i = 0;
        while (i < raw.Length && raw[i] != '\\' && raw[i] != '\0' && !char.IsSurrogate(raw[i]))
        {
            i++;
        }

        if (i == raw.Length)
        {
            return raw.ToString();
        }

        var sb = new StringBuilder(raw.Length);
        sb.Append(raw[..i]);
        while (i < raw.Length)
        {
            char c = raw[i];
            if (c == '\\')
            {
                i++;
                if (i >= raw.Length)
                {
                    // A backslash at the end of input: in a string it is dropped (CSS Syntax §4.3.5); elsewhere it is an
                    // escape of end-of-file, which stands for U+FFFD (§4.3.7).
                    if (!inString)
                    {
                        sb.Append('�');
                    }

                    break;
                }

                c = raw[i];
                if (IsHexDigit(c))
                {
                    int value = 0;
                    int digits = 0;
                    while (i < raw.Length && digits < 6 && IsHexDigit(raw[i]))
                    {
                        value = (value << 4) | HexValue(raw[i]);
                        i++;
                        digits++;
                    }

                    if (i < raw.Length && IsWhitespace(raw[i]))
                    {
                        if (raw[i] == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n')
                        {
                            i++;
                        }

                        i++;
                    }

                    if (value == 0 || (value >= 0xD800 && value <= 0xDFFF) || value > 0x10FFFF)
                    {
                        sb.Append('\uFFFD');
                    }
                    else
                    {
                        AppendCodePoint(sb, value);
                    }
                }
                else if (IsNewline(c))
                {
                    // Line continuation inside a string.
                    if (c == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n')
                    {
                        i++;
                    }

                    i++;
                }
                else
                {
                    i += AppendChar(sb, raw, i);
                }
            }
            else
            {
                i += AppendChar(sb, raw, i);
            }
        }

        return sb.ToString();
    }

    private static int AppendChar(StringBuilder sb, ReadOnlySpan<char> raw, int i)
    {
        char c = raw[i];
        if (c == '\0')
        {
            sb.Append('\uFFFD');
        }
        else if (char.IsHighSurrogate(c) && i + 1 < raw.Length && char.IsLowSurrogate(raw[i + 1]))
        {
            sb.Append(c).Append(raw[i + 1]);
            return 2;
        }
        else if (char.IsSurrogate(c))
        {
            sb.Append('\uFFFD');
        }
        else
        {
            sb.Append(c);
        }

        return 1;
    }

    private static void AppendCodePoint(StringBuilder sb, int cp)
    {
        if (cp < 0x10000)
        {
            sb.Append((char)cp);
        }
        else
        {
            cp -= 0x10000;
            sb.Append((char)(0xD800 + (cp >> 10))).Append((char)(0xDC00 + (cp & 0x3FF)));
        }
    }
}
