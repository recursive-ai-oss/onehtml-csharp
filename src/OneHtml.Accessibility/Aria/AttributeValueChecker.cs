using System;
using System.Text;
using System.Collections.Generic;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneHtml.Accessibility.Aria;

/// <summary>Checks an <c>aria-*</c> attribute value against the type the WAI-ARIA 1.2 section 6.2 "Value Types" give it.</summary>
internal static class AttributeValueChecker
{
    /// <summary>
    /// Adds a diagnostic when the value is invalid or names no element. An empty value is treated as not specified
    /// and is never reported, since browsers ignore it.
    /// </summary>
    public static void Check(Document document, AriaProperty prop, string name, ReadOnlySpan<char> value, int maxIdReferences, List<RoleDiagnostic> into)
    {
        if (value.IsEmpty)
        {
            return;
        }

        switch (prop.ValueType)
        {
            case AriaValueType.Boolean:
            case AriaValueType.BooleanOrUndefined:
            case AriaValueType.Tristate:
            case AriaValueType.Token:
                if (!prop.TokenSet.GetAlternateLookup<ReadOnlySpan<char>>().Contains(value))
                {
                    Invalid(into, name, value);
                }

                break;
            case AriaValueType.TokenList:
                {
                    int pos = 0;
                    bool any = false;
                    while (AsciiText.NextToken(value, ref pos, out ReadOnlySpan<char> token))
                    {
                        any = true;
                        if (token.Contains(' ') || !prop.TokenSet.GetAlternateLookup<ReadOnlySpan<char>>().Contains(token))
                        {
                            Invalid(into, name, token);
                            return;
                        }
                    }

                    if (!any)
                    {
                        Invalid(into, name, value);
                    }

                    break;
                }

            case AriaValueType.IntegerValue:
                if (!TryParseAriaInteger(value, out long v) || v < MinimumOf(name))
                {
                    Invalid(into, name, value);
                }

                break;
            case AriaValueType.Number:
                if (!IsRealNumber(value))
                {
                    Invalid(into, name, value);
                }

                break;
            case AriaValueType.IdReference:
                {
                    int pos = 0;
                    AsciiText.NextToken(value, ref pos, out ReadOnlySpan<char> id);
                    if (AsciiText.NextToken(value, ref pos, out _) || id.IsEmpty)
                    {
                        // An ID reference is exactly one ID; ids contain no whitespace.
                        Invalid(into, name, value);
                    }
                    else
                    {
                        CheckId(document, name, id, into);
                    }

                    break;
                }

            case AriaValueType.IdReferenceList:
                {
                    int pos = 0;
                    int count = 0;
                    while (AsciiText.NextToken(value, ref pos, out ReadOnlySpan<char> id))
                    {
                        if (++count > maxIdReferences)
                        {
                            into.Add(new RoleDiagnostic(RoleDiagnosticKind.LimitExceeded, name, null, "Only the first ID references were examined."));
                            return;
                        }

                        CheckId(document, name, id, into);
                    }

                    break;
                }
        }
    }

    private static void CheckId(Document document, string name, ReadOnlySpan<char> id, List<RoleDiagnostic> into)
    {
        if (document.GetElementById(id.ToString()).IsNull)
        {
            into.Add(new RoleDiagnostic(RoleDiagnosticKind.BrokenIdReference, name, RoleComputer.Clip(id), "No element has this id."));
        }
    }

    private static void Invalid(List<RoleDiagnostic> into, string name, ReadOnlySpan<char> value) =>
        into.Add(new RoleDiagnostic(RoleDiagnosticKind.InvalidAttributeValue, name, RoleComputer.Clip(value), "Not a valid value for this attribute."));

    /// <summary>Lower bounds the specification states in prose: WAI-ARIA 1.2 aria-level, aria-posinset, aria-colspan and the index and count properties.</summary>
    private static long MinimumOf(string name)
    {
        if (Ascii.EqualsIgnoreCase(name, "aria-setsize") || Ascii.EqualsIgnoreCase(name, "aria-colcount") || Ascii.EqualsIgnoreCase(name, "aria-rowcount"))
        {
            return -1;
        }

        if (Ascii.EqualsIgnoreCase(name, "aria-rowspan"))
        {
            return 0;
        }

        return 1;
    }

    private static bool TryParseAriaInteger(ReadOnlySpan<char> s, out long value)
    {
        value = 0;
        int i = 0;
        bool negative = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
        {
            negative = s[i] == '-';
            i++;
        }

        if (i >= s.Length)
        {
            return false;
        }

        long v = 0;
        for (; i < s.Length; i++)
        {
            if (s[i] is < '0' or > '9')
            {
                return false;
            }

            v = v < (long.MaxValue - 9) / 10 ? (v * 10) + (s[i] - '0') : v;
        }

        value = negative ? -v : v;
        return true;
    }

    /// <summary>A real number: optional sign, digits with an optional fraction, optional exponent. No whitespace, no NaN or infinity.</summary>
    internal static bool IsRealNumber(ReadOnlySpan<char> s)
    {
        int i = 0;
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
        {
            i++;
        }

        int digits = 0;
        while (i < s.Length && s[i] is >= '0' and <= '9')
        {
            i++;
            digits++;
        }

        if (i < s.Length && s[i] == '.')
        {
            i++;
            while (i < s.Length && s[i] is >= '0' and <= '9')
            {
                i++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return false;
        }

        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            if (i < s.Length && (s[i] == '-' || s[i] == '+'))
            {
                i++;
            }

            int exp = 0;
            while (i < s.Length && s[i] is >= '0' and <= '9')
            {
                i++;
                exp++;
            }

            if (exp == 0)
            {
                return false;
            }
        }

        return i == s.Length;
    }
}
