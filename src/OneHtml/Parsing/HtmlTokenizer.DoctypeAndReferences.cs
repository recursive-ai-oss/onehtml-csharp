using System;
using System.Text;

namespace RecursiveAI.OneHtml.Parsing;

public sealed partial class HtmlTokenizer
{
    private enum DoctypeState
    {
        Doctype,
        BeforeName,
        AfterName,
        AfterPublicKeyword,
        BeforePublicIdentifier,
        PublicIdentifier,
        AfterPublicIdentifier,
        BetweenIdentifiers,
        AfterSystemKeyword,
        BeforeSystemIdentifier,
        SystemIdentifier,
        AfterSystemIdentifier,
        Bogus,
    }

    // HTML 13.2.5.53 to 13.2.5.68: "<!DOCTYPE" ends at p. The name and identifiers are ranges of the source.
    private bool ParseDoctype(int tagStart, int p)
    {
        string t = _text;
        int len = t.Length;
        bool force = false;
        int nameStart = -1;
        int nameEnd = -1;
        int publicStart = -1;
        int publicEnd = -1;
        int systemStart = -1;
        int systemEnd = -1;
        char quote = '"';
        DoctypeState state = DoctypeState.Doctype;
        while (true)
        {
            // The states with nothing to read at the end of the input all end the same way.
            bool atEnd = p >= len;
            switch (state)
            {
                case DoctypeState.Doctype:
                    if (atEnd)
                    {
                        goto EofInDoctype;
                    }

                    if (IsTagWhitespace(t[p]))
                    {
                        p++;
                    }
                    else if (t[p] != '>')
                    {
                        ReportError(HtmlParseError.MissingWhitespaceBeforeDoctypeName, p);
                    }

                    state = DoctypeState.BeforeName;
                    break;

                case DoctypeState.BeforeName:
                {
                    while (p < len && IsTagWhitespace(t[p]))
                    {
                        p++;
                    }

                    if (p >= len)
                    {
                        goto EofInDoctype;
                    }

                    if (t[p] == '>')
                    {
                        ReportError(HtmlParseError.MissingDoctypeName, p);
                        force = true;
                        return EmitDoctype(tagStart, p + 1, false, force, nameStart, nameEnd, publicStart, publicEnd, systemStart, systemEnd);
                    }

                    // DOCTYPE name state (13.2.5.55): up to whitespace or ">".
                    nameStart = p;
                    int stop = t.AsSpan(p).IndexOfAny("\t\n\f >");
                    nameEnd = stop < 0 ? len : p + stop;
                    ReportNuls(nameStart, nameEnd);
                    p = nameEnd;
                    if (stop < 0)
                    {
                        goto EofInDoctype;
                    }

                    state = DoctypeState.AfterName;
                    break;
                }

                case DoctypeState.AfterName:
                {
                    while (p < len && IsTagWhitespace(t[p]))
                    {
                        p++;
                    }

                    if (p >= len)
                    {
                        goto EofInDoctype;
                    }

                    if (t[p] == '>')
                    {
                        return EmitDoctype(tagStart, p + 1, false, force, nameStart, nameEnd, publicStart, publicEnd, systemStart, systemEnd);
                    }

                    ReadOnlySpan<char> rest = t.AsSpan(p);
                    if (rest.StartsWith("PUBLIC", StringComparison.OrdinalIgnoreCase))
                    {
                        p += 6;
                        state = DoctypeState.AfterPublicKeyword;
                    }
                    else if (rest.StartsWith("SYSTEM", StringComparison.OrdinalIgnoreCase))
                    {
                        p += 6;
                        state = DoctypeState.AfterSystemKeyword;
                    }
                    else
                    {
                        ReportError(HtmlParseError.InvalidCharacterSequenceAfterDoctypeName, p);
                        force = true;
                        state = DoctypeState.Bogus;
                    }

                    break;
                }

                case DoctypeState.AfterPublicKeyword:
                    if (atEnd)
                    {
                        goto EofInDoctype;
                    }

                    if (IsTagWhitespace(t[p]))
                    {
                        p++;
                        state = DoctypeState.BeforePublicIdentifier;
                    }
                    else if (t[p] is '"' or '\'')
                    {
                        ReportError(HtmlParseError.MissingWhitespaceAfterDoctypePublicKeyword, p);
                        quote = t[p];
                        publicStart = ++p;
                        state = DoctypeState.PublicIdentifier;
                    }
                    else if (t[p] == '>')
                    {
                        ReportError(HtmlParseError.MissingDoctypePublicIdentifier, p);
                        force = true;
                        return EmitDoctype(tagStart, p + 1, false, force, nameStart, nameEnd, publicStart, publicEnd, systemStart, systemEnd);
                    }
                    else
                    {
                        ReportError(HtmlParseError.MissingQuoteBeforeDoctypePublicIdentifier, p);
                        force = true;
                        state = DoctypeState.Bogus;
                    }

                    break;

                case DoctypeState.BeforePublicIdentifier:
                    while (p < len && IsTagWhitespace(t[p]))
                    {
                        p++;
                    }

                    if (p >= len)
                    {
                        goto EofInDoctype;
                    }

                    if (t[p] is '"' or '\'')
                    {
                        quote = t[p];
                        publicStart = ++p;
                        state = DoctypeState.PublicIdentifier;
                    }
                    else if (t[p] == '>')
                    {
                        ReportError(HtmlParseError.MissingDoctypePublicIdentifier, p);
                        force = true;
                        return EmitDoctype(tagStart, p + 1, false, force, nameStart, nameEnd, publicStart, publicEnd, systemStart, systemEnd);
                    }
                    else
                    {
                        ReportError(HtmlParseError.MissingQuoteBeforeDoctypePublicIdentifier, p);
                        force = true;
                        state = DoctypeState.Bogus;
                    }

                    break;

                case DoctypeState.PublicIdentifier:
                {
                    int stop = t.AsSpan(p).IndexOfAny(quote, '>');
                    publicEnd = stop < 0 ? len : p + stop;
                    ReportNuls(publicStart, publicEnd);
                    p = publicEnd;
                    if (stop < 0)
                    {
                        goto EofInDoctype;
                    }

                    if (t[p] == '>')
                    {
                        ReportError(HtmlParseError.AbruptDoctypePublicIdentifier, p);
                        force = true;
                        return EmitDoctype(tagStart, p + 1, false, force, nameStart, nameEnd, publicStart, publicEnd, systemStart, systemEnd);
                    }

                    p++;
                    state = DoctypeState.AfterPublicIdentifier;
                    break;
                }

                case DoctypeState.AfterPublicIdentifier:
                    if (atEnd)
                    {
                        goto EofInDoctype;
                    }

                    if (IsTagWhitespace(t[p]))
                    {
                        p++;
                        state = DoctypeState.BetweenIdentifiers;
                    }
                    else if (t[p] == '>')
                    {
                        return EmitDoctype(tagStart, p + 1, false, force, nameStart, nameEnd, publicStart, publicEnd, systemStart, systemEnd);
                    }
                    else if (t[p] is '"' or '\'')
                    {
                        ReportError(HtmlParseError.MissingWhitespaceBetweenDoctypePublicAndSystemIdentifiers, p);
                        quote = t[p];
                        systemStart = ++p;
                        state = DoctypeState.SystemIdentifier;
                    }
                    else
                    {
                        ReportError(HtmlParseError.MissingQuoteBeforeDoctypeSystemIdentifier, p);
                        force = true;
                        state = DoctypeState.Bogus;
                    }

                    break;

                case DoctypeState.BetweenIdentifiers:
                    while (p < len && IsTagWhitespace(t[p]))
                    {
                        p++;
                    }

                    if (p >= len)
                    {
                        goto EofInDoctype;
                    }

                    if (t[p] == '>')
                    {
                        return EmitDoctype(tagStart, p + 1, false, force, nameStart, nameEnd, publicStart, publicEnd, systemStart, systemEnd);
                    }

                    if (t[p] is '"' or '\'')
                    {
                        quote = t[p];
                        systemStart = ++p;
                        state = DoctypeState.SystemIdentifier;
                    }
                    else
                    {
                        ReportError(HtmlParseError.MissingQuoteBeforeDoctypeSystemIdentifier, p);
                        force = true;
                        state = DoctypeState.Bogus;
                    }

                    break;

                case DoctypeState.AfterSystemKeyword:
                    if (atEnd)
                    {
                        goto EofInDoctype;
                    }

                    if (IsTagWhitespace(t[p]))
                    {
                        p++;
                        state = DoctypeState.BeforeSystemIdentifier;
                    }
                    else if (t[p] is '"' or '\'')
                    {
                        ReportError(HtmlParseError.MissingWhitespaceAfterDoctypeSystemKeyword, p);
                        quote = t[p];
                        systemStart = ++p;
                        state = DoctypeState.SystemIdentifier;
                    }
                    else if (t[p] == '>')
                    {
                        ReportError(HtmlParseError.MissingDoctypeSystemIdentifier, p);
                        force = true;
                        return EmitDoctype(tagStart, p + 1, false, force, nameStart, nameEnd, publicStart, publicEnd, systemStart, systemEnd);
                    }
                    else
                    {
                        ReportError(HtmlParseError.MissingQuoteBeforeDoctypeSystemIdentifier, p);
                        force = true;
                        state = DoctypeState.Bogus;
                    }

                    break;

                case DoctypeState.BeforeSystemIdentifier:
                    while (p < len && IsTagWhitespace(t[p]))
                    {
                        p++;
                    }

                    if (p >= len)
                    {
                        goto EofInDoctype;
                    }

                    if (t[p] is '"' or '\'')
                    {
                        quote = t[p];
                        systemStart = ++p;
                        state = DoctypeState.SystemIdentifier;
                    }
                    else if (t[p] == '>')
                    {
                        ReportError(HtmlParseError.MissingDoctypeSystemIdentifier, p);
                        force = true;
                        return EmitDoctype(tagStart, p + 1, false, force, nameStart, nameEnd, publicStart, publicEnd, systemStart, systemEnd);
                    }
                    else
                    {
                        ReportError(HtmlParseError.MissingQuoteBeforeDoctypeSystemIdentifier, p);
                        force = true;
                        state = DoctypeState.Bogus;
                    }

                    break;

                case DoctypeState.SystemIdentifier:
                {
                    int stop = t.AsSpan(p).IndexOfAny(quote, '>');
                    systemEnd = stop < 0 ? len : p + stop;
                    ReportNuls(systemStart, systemEnd);
                    p = systemEnd;
                    if (stop < 0)
                    {
                        goto EofInDoctype;
                    }

                    if (t[p] == '>')
                    {
                        ReportError(HtmlParseError.AbruptDoctypeSystemIdentifier, p);
                        force = true;
                        return EmitDoctype(tagStart, p + 1, false, force, nameStart, nameEnd, publicStart, publicEnd, systemStart, systemEnd);
                    }

                    p++;
                    state = DoctypeState.AfterSystemIdentifier;
                    break;
                }

                case DoctypeState.AfterSystemIdentifier:
                    while (p < len && IsTagWhitespace(t[p]))
                    {
                        p++;
                    }

                    if (p >= len)
                    {
                        goto EofInDoctype;
                    }

                    if (t[p] == '>')
                    {
                        return EmitDoctype(tagStart, p + 1, false, force, nameStart, nameEnd, publicStart, publicEnd, systemStart, systemEnd);
                    }

                    // This error does not set the force-quirks flag.
                    ReportError(HtmlParseError.UnexpectedCharacterAfterDoctypeSystemIdentifier, p);
                    state = DoctypeState.Bogus;
                    break;

                default:
                {
                    // Bogus DOCTYPE state (13.2.5.68): everything up to ">" is ignored.
                    int stop = t.AsSpan(p).IndexOf('>');
                    int end = stop < 0 ? len : p + stop;
                    ReportNuls(p, end);
                    if (stop < 0)
                    {
                        return EmitDoctype(tagStart, len, true, force, nameStart, nameEnd, publicStart, publicEnd, systemStart, systemEnd);
                    }

                    return EmitDoctype(tagStart, end + 1, false, force, nameStart, nameEnd, publicStart, publicEnd, systemStart, systemEnd);
                }
            }
        }

    EofInDoctype:
        ReportError(HtmlParseError.EofInDoctype, len);
        return EmitDoctype(tagStart, len, true, true, nameStart, nameEnd, publicStart, publicEnd, systemStart, systemEnd);
    }

    private bool EmitDoctype(
        int tagStart,
        int end,
        bool atEof,
        bool force,
        int nameStart,
        int nameEnd,
        int publicStart,
        int publicEnd,
        int systemStart,
        int systemEnd)
    {
        _kind = HtmlTokenKind.Doctype;
        _start = tagStart;
        _end = end;
        _pos = end;
        _state = atEof ? State.Eof : State.Data;
        _forceQuirks = force;
        _hasName = nameStart >= 0;
        _bufLen = 0;
        if (_hasName)
        {
            // The name is lowercased; U+0000 becomes U+FFFD (the error was reported when it was read).
            EnsureBuffer(nameEnd - nameStart);
            for (int i = nameStart; i < nameEnd; i++)
            {
                char c = _text[i];
                _buf[_bufLen++] = c is >= 'A' and <= 'Z' ? (char)(c + 0x20) : c == '\0' ? '\uFFFD' : c;
            }

            _name = new TextRef { Start = 0, Length = nameEnd - nameStart, InBuffer = true, SourceStart = nameStart, SourceEnd = nameEnd };
        }

        _publicId = publicStart >= 0 ? MakeIdentifier(publicStart, publicEnd < 0 ? _text.Length : publicEnd) : null;
        _systemId = systemStart >= 0 ? MakeIdentifier(systemStart, systemEnd < 0 ? _text.Length : systemEnd) : null;
        return true;
    }

    private string MakeIdentifier(int start, int end)
    {
        ReadOnlySpan<char> span = _text.AsSpan(start, end - start);
        return span.IndexOf('\0') < 0 ? span.ToString() : span.ToString().Replace('\0', '\uFFFD');
    }

    // HTML 13.2.5.72 to 13.2.5.76: "<?" at tagStart.
    private bool ProcessingInstruction(int tagStart)
    {
        string t = _text;
        int len = t.Length;
        int p = tagStart + 2;
        if (p >= len)
        {
            return PiEof();
        }

        if (!(IsAsciiAlpha(t[p]) || t[p] == '_'))
        {
            ReportError(HtmlParseError.InvalidFirstCharacterOfProcessingInstructionTarget, p);
            return BogusComment(tagStart, tagStart + 1);
        }

        // Processing instruction target state: letters, digits, hyphens and underscores.
        int q = p;
        while (q < len && (IsAsciiAlnum(t[q]) || t[q] == '-' || t[q] == '_'))
        {
            q++;
        }

        if (q >= len)
        {
            return PiEof();
        }

        char c = t[q];
        if (!(IsTagWhitespace(c) || c == '?' || c == '>'))
        {
            ReportError(HtmlParseError.InvalidProcessingInstructionTarget, q);
            return BogusComment(tagStart, tagStart + 1);
        }

        ReadOnlySpan<char> target = t.AsSpan(p, q - p);
        if (target.Equals("xml", StringComparison.OrdinalIgnoreCase) || target.Equals("xml-stylesheet", StringComparison.OrdinalIgnoreCase))
        {
            ReportError(HtmlParseError.DisallowedProcessingInstructionTarget, q);
            return BogusComment(tagStart, tagStart + 1);
        }

        // After processing instruction target state, then the data state.
        while (q < len && IsTagWhitespace(t[q]))
        {
            q++;
        }

        int dataStart = q;
        while (true)
        {
            int i = t.AsSpan(q).IndexOfAny('?', '>');
            if (i < 0)
            {
                return PiEof();
            }

            q += i;
            if (t[q] == '>')
            {
                return EmitProcessingInstruction(tagStart, p, p + target.Length, dataStart, q, q + 1);
            }

            // '?': a "?>" ends the instruction; any other "?" is data.
            if (q + 1 >= len)
            {
                return PiEof();
            }

            if (t[q + 1] == '>')
            {
                return EmitProcessingInstruction(tagStart, p, p + target.Length, dataStart, q, q + 2);
            }

            q++;
        }
    }

    private bool PiEof()
    {
        ReportError(HtmlParseError.EofInProcessingInstruction, _text.Length);
        _state = State.Eof;
        _pos = _text.Length;
        return false;
    }

    private bool EmitProcessingInstruction(int tagStart, int targetStart, int targetEnd, int dataStart, int dataEnd, int end)
    {
        _kind = HtmlTokenKind.ProcessingInstruction;
        _start = tagStart;
        _end = end;
        _pos = end;
        _state = State.Data;
        _name = new TextRef { Start = targetStart, Length = targetEnd - targetStart, SourceStart = targetStart, SourceEnd = targetEnd };
        _data = new TextRef { Start = dataStart, Length = dataEnd - dataStart, SourceStart = dataStart, SourceEnd = dataEnd };
        return true;
    }

    // ---- Character references (HTML 13.2.5.77 to 13.2.5.84) ----

    private readonly char[] _reference = new char[4];

    // For the '&' at p. Returns how many characters of decoded text are in _reference (end is then the offset after the
    // reference), or 0 when the '&' is not a reference and stays as it is; end is then p + 1, because whatever
    // the standard says to flush as text is ordinary text.
    private int ParseCharacterReference(int p, bool inAttribute, out int end)
    {
        string t = _text;
        int len = t.Length;
        end = p + 1;
        int c = p + 1 < len ? t[p + 1] : -1;
        if (IsAsciiAlnum(c))
        {
            return ParseNamedReference(p, inAttribute, out end);
        }

        if (c == '#')
        {
            return ParseNumericReference(p, out end);
        }

        return 0;
    }

    // HTML 13.2.5.78 and 13.2.5.79
    private int ParseNamedReference(int p, bool inAttribute, out int end)
    {
        string t = _text;
        int len = t.Length;
        end = p + 1;
        int window = Math.Min(len - p - 1, NamedCharacterReferences.MaxNameLength);
        if (NamedCharacterReferences.TryMatch(t.AsSpan(p + 1, window), out int matched, out ReadOnlySpan<char> value))
        {
            int after = p + 1 + matched;
            bool semicolon = t[after - 1] == ';';
            if (inAttribute && !semicolon && after < len && (t[after] == '=' || IsAsciiAlnum(t[after])))
            {
                // For historical reasons, "&amp=" and "&ampx" in an attribute value are left alone.
                return 0;
            }

            if (!semicolon)
            {
                ReportError(HtmlParseError.MissingSemicolonAfterCharacterReference, after);
            }

            value.CopyTo(_reference);
            end = after;
            return value.Length;
        }

        // Ambiguous ampersand state: the letters and digits are text; a semicolon after them is an error.
        int q = p + 1;
        while (q < len && IsAsciiAlnum(t[q]))
        {
            q++;
        }

        if (q < len && t[q] == ';')
        {
            ReportError(HtmlParseError.UnknownNamedCharacterReference, q);
        }

        return 0;
    }

    // HTML 13.2.5.80 to 13.2.5.84
    private int ParseNumericReference(int p, out int end)
    {
        string t = _text;
        int len = t.Length;
        end = p + 1;
        int q = p + 2;
        bool hex = false;
        if (q < len && (t[q] == 'x' || t[q] == 'X'))
        {
            hex = true;
            q++;
        }

        int digits = q;
        int code = 0;
        while (q < len)
        {
            int digit = HexValue(t[q]);
            if (digit < 0 || (!hex && digit > 9))
            {
                break;
            }

            // Anything above U+10FFFF is the same error, so the value stops growing there.
            code = code > 0x10FFFF ? code : (code * (hex ? 16 : 10)) + digit;
            q++;
        }

        if (q == digits)
        {
            ReportError(HtmlParseError.AbsenceOfDigitsInNumericCharacterReference, q);
            return 0;
        }

        if (q < len && t[q] == ';')
        {
            q++;
        }
        else
        {
            ReportError(HtmlParseError.MissingSemicolonAfterCharacterReference, q);
        }

        // Numeric character reference end state (13.2.5.84). Errors are reported at the offset after the reference.
        if (code == 0)
        {
            ReportError(HtmlParseError.NullCharacterReference, q);
            code = 0xFFFD;
        }
        else if (code > 0x10FFFF)
        {
            ReportError(HtmlParseError.CharacterReferenceOutsideUnicodeRange, q);
            code = 0xFFFD;
        }
        else if (code is >= 0xD800 and <= 0xDFFF)
        {
            ReportError(HtmlParseError.SurrogateCharacterReference, q);
            code = 0xFFFD;
        }
        else if (code is (>= 0xFDD0 and <= 0xFDEF) || (code & 0xFFFE) == 0xFFFE)
        {
            ReportError(HtmlParseError.NoncharacterCharacterReference, q);
        }

        bool control = code is (>= 0x01 and <= 0x1F) or (>= 0x7F and <= 0x9F);
        if (code == 0x0D || (control && code is not (0x09 or 0x0A or 0x0C)))
        {
            ReportError(HtmlParseError.ControlCharacterReference, q);
            code = Windows1252Replacement(code);
        }

        end = q;
        if (code < 0x10000)
        {
            _reference[0] = (char)code;
            return 1;
        }

        _reference[0] = (char)(0xD7C0 + (code >> 10));
        _reference[1] = (char)(0xDC00 + (code & 0x3FF));
        return 2;
    }

    private static int HexValue(char c) =>
        c is >= '0' and <= '9' ? c - '0'
        : c is >= 'a' and <= 'f' ? c - 'a' + 10
        : c is >= 'A' and <= 'F' ? c - 'A' + 10
        : -1;

    // The table in 13.2.5.84: the C1 controls that browsers read as windows-1252.
    private static int Windows1252Replacement(int code) => code switch
    {
        0x80 => 0x20AC,
        0x82 => 0x201A,
        0x83 => 0x0192,
        0x84 => 0x201E,
        0x85 => 0x2026,
        0x86 => 0x2020,
        0x87 => 0x2021,
        0x88 => 0x02C6,
        0x89 => 0x2030,
        0x8A => 0x0160,
        0x8B => 0x2039,
        0x8C => 0x0152,
        0x8E => 0x017D,
        0x91 => 0x2018,
        0x92 => 0x2019,
        0x93 => 0x201C,
        0x94 => 0x201D,
        0x95 => 0x2022,
        0x96 => 0x2013,
        0x97 => 0x2014,
        0x98 => 0x02DC,
        0x99 => 0x2122,
        0x9A => 0x0161,
        0x9B => 0x203A,
        0x9C => 0x0153,
        0x9E => 0x017E,
        0x9F => 0x0178,
        _ => code,
    };
}
