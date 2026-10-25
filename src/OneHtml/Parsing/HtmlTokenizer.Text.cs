using System;
using System.Buffers;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Parsing;

public sealed partial class HtmlTokenizer
{
    private static readonly SearchValues<char> s_dataStops = SearchValues.Create("<&");
    private static readonly SearchValues<char> s_dataStopsNul = SearchValues.Create("<&\0");
    private static readonly SearchValues<char> s_rawStopsNul = SearchValues.Create("<\0");
    private static readonly SearchValues<char> s_scriptEscapedStops = SearchValues.Create("-<\0");

    // Runs the text-family states (HTML 13.2.5.1 to 13.2.5.5, 13.2.5.15 to 13.2.5.31, 13.2.5.69). They share one
    // idea: the characters between runStart and p are an unbroken run of text that will become one character token.
    // Markup that starts at p flushes the run first and is parsed when the loop is entered again at p, so no
    // error is reported or state changed before the flush.
    private bool TextStep()
    {
        string t = _text;
        int len = t.Length;
        int runStart = _pos;
        int p = _pos;
        while (true)
        {
            switch (_state)
            {
                case State.Data:
                {
                    int i = t.AsSpan(p).IndexOfAny(_errors is null ? s_dataStops : s_dataStopsNul);
                    if (i < 0)
                    {
                        return EndText(runStart, len);
                    }

                    p += i;
                    char c = t[p];
                    if (c == '\0')
                    {
                        // HTML 13.2.5.1: a NUL in the data state is an error and stays in the text.
                        ReportError(HtmlParseError.UnexpectedNullCharacter, p);
                        p++;
                        continue;
                    }

                    if (c == '&')
                    {
                        if (p > runStart)
                        {
                            return Flush(runStart, p);
                        }

                        int n = ParseCharacterReference(p, inAttribute: false, out int end);
                        if (n == 0)
                        {
                            p = end;
                            continue;
                        }

                        return EmitReference(p, end, n);
                    }

                    // c is '<'. HTML 13.2.5.6 "tag open", 13.2.5.7 "end tag open".
                    int c1 = p + 1 < len ? t[p + 1] : -1;
                    if (c1 == '!' || c1 == '?' || IsAsciiAlpha(c1) || (c1 == '/' && p + 2 < len))
                    {
                        if (p > runStart)
                        {
                            return Flush(runStart, p);
                        }

                        return MarkupAt(p);
                    }

                    if (c1 == -1 || c1 == '/')
                    {
                        // The input ends right after "<" or "</": both characters are text, then the end of file.
                        ReportError(HtmlParseError.EofBeforeTagName, len);
                        p = len;
                    }
                    else
                    {
                        ReportError(HtmlParseError.InvalidFirstCharacterOfTagName, p + 1);
                        p++;
                    }

                    continue;
                }

                case State.Rcdata:
                {
                    int i = t.AsSpan(p).IndexOfAny(s_dataStopsNul);
                    if (i < 0)
                    {
                        return EndText(runStart, len);
                    }

                    p += i;
                    char c = t[p];
                    if (c == '\0')
                    {
                        if (p > runStart)
                        {
                            return Flush(runStart, p);
                        }

                        return EmitReplacement(p);
                    }

                    if (c == '&')
                    {
                        if (p > runStart)
                        {
                            return Flush(runStart, p);
                        }

                        int n = ParseCharacterReference(p, inAttribute: false, out int end);
                        if (n == 0)
                        {
                            p = end;
                            continue;
                        }

                        return EmitReference(p, end, n);
                    }

                    // HTML 13.2.5.9 to 13.2.5.11: "</" and a name that matches the last start tag ends the text.
                    if (p + 1 < len && t[p + 1] == '/' && MatchesAppropriateEndTag(p))
                    {
                        if (p > runStart)
                        {
                            return Flush(runStart, p);
                        }

                        return ParseTag(p, isEnd: true, p + 2);
                    }

                    p++;
                    continue;
                }

                case State.Rawtext:
                {
                    int i = t.AsSpan(p).IndexOfAny(s_rawStopsNul);
                    if (i < 0)
                    {
                        return EndText(runStart, len);
                    }

                    p += i;
                    if (t[p] == '\0')
                    {
                        if (p > runStart)
                        {
                            return Flush(runStart, p);
                        }

                        return EmitReplacement(p);
                    }

                    // HTML 13.2.5.12 to 13.2.5.14
                    if (p + 1 < len && t[p + 1] == '/' && MatchesAppropriateEndTag(p))
                    {
                        if (p > runStart)
                        {
                            return Flush(runStart, p);
                        }

                        return ParseTag(p, isEnd: true, p + 2);
                    }

                    p++;
                    continue;
                }

                case State.Plaintext:
                {
                    int i = t.AsSpan(p).IndexOf('\0');
                    if (i < 0)
                    {
                        return EndText(runStart, len);
                    }

                    p += i;
                    if (p > runStart)
                    {
                        return Flush(runStart, p);
                    }

                    return EmitReplacement(p);
                }

                case State.CdataSection:
                {
                    // HTML 13.2.5.69 to 13.2.5.71: everything up to "]]>" is text; NUL is left to the tree builder.
                    int i = t.AsSpan(p).IndexOf("]]>", StringComparison.Ordinal);
                    if (i < 0)
                    {
                        ReportError(HtmlParseError.EofInCdata, len);
                        return EndText(runStart, len);
                    }

                    int end = p + i;
                    _state = State.Data;
                    _pos = end + 3;
                    if (end > runStart)
                    {
                        EmitCharacters(runStart, end);
                        return true;
                    }

                    return false;
                }

                case State.ScriptData:
                {
                    int i = t.AsSpan(p).IndexOfAny(s_rawStopsNul);
                    if (i < 0)
                    {
                        return EndText(runStart, len);
                    }

                    p += i;
                    if (t[p] == '\0')
                    {
                        if (p > runStart)
                        {
                            return Flush(runStart, p);
                        }

                        return EmitReplacement(p);
                    }

                    int c1 = p + 1 < len ? t[p + 1] : -1;
                    if (c1 == '/')
                    {
                        if (MatchesAppropriateEndTag(p))
                        {
                            if (p > runStart)
                            {
                                return Flush(runStart, p);
                            }

                            return ParseTag(p, isEnd: true, p + 2);
                        }

                        p++;
                    }
                    else if (c1 == '!')
                    {
                        // HTML 13.2.5.15, 13.2.5.18, 13.2.5.19: "<!--" starts the escaped states; "<!" with
                        // anything else goes back to script data at the character after it.
                        if (p + 2 < len && t[p + 2] == '-')
                        {
                            if (p + 3 < len && t[p + 3] == '-')
                            {
                                _state = State.ScriptDataEscapedDashDash;
                                p += 4;
                            }
                            else
                            {
                                p += 3;
                            }
                        }
                        else
                        {
                            p += 2;
                        }
                    }
                    else
                    {
                        p++;
                    }

                    continue;
                }

                case State.ScriptDataEscaped:
                {
                    int i = t.AsSpan(p).IndexOfAny(s_scriptEscapedStops);
                    if (i < 0)
                    {
                        return EndScriptComment(runStart);
                    }

                    p += i;
                    char c = t[p];
                    if (c == '-')
                    {
                        _state = State.ScriptDataEscapedDash;
                        p++;
                        continue;
                    }

                    if (c == '\0')
                    {
                        if (p > runStart)
                        {
                            return Flush(runStart, p);
                        }

                        return EmitReplacement(p);
                    }

                    // '<': handled with the dash states, which treat it the same way.
                    if (EscapedLessThan(runStart, ref p, out bool done))
                    {
                        return done;
                    }

                    continue;
                }

                case State.ScriptDataEscapedDash:
                case State.ScriptDataEscapedDashDash:
                {
                    if (p >= len)
                    {
                        return EndScriptComment(runStart);
                    }

                    char c = t[p];
                    if (c == '<')
                    {
                        if (EscapedLessThan(runStart, ref p, out bool done))
                        {
                            return done;
                        }

                        continue;
                    }

                    if (c == '\0')
                    {
                        if (p > runStart)
                        {
                            return Flush(runStart, p);
                        }

                        _state = State.ScriptDataEscaped;
                        return EmitReplacement(p);
                    }

                    if (_state == State.ScriptDataEscapedDash)
                    {
                        // HTML 13.2.5.21
                        _state = c == '-' ? State.ScriptDataEscapedDashDash : State.ScriptDataEscaped;
                    }
                    else if (c == '>')
                    {
                        // HTML 13.2.5.22: more hyphens keep the state; ">" ends the comment-like text.
                        _state = State.ScriptData;
                    }
                    else if (c != '-')
                    {
                        _state = State.ScriptDataEscaped;
                    }

                    p++;
                    continue;
                }

                case State.ScriptDataDoubleEscaped:
                case State.ScriptDataDoubleEscapedDash:
                case State.ScriptDataDoubleEscapedDashDash:
                {
                    int c;
                    if (_state == State.ScriptDataDoubleEscaped)
                    {
                        int i = t.AsSpan(p).IndexOfAny(s_scriptEscapedStops);
                        if (i < 0)
                        {
                            return EndScriptComment(runStart);
                        }

                        p += i;
                        c = t[p];
                    }
                    else
                    {
                        if (p >= len)
                        {
                            return EndScriptComment(runStart);
                        }

                        c = t[p];
                    }

                    if (c == '\0')
                    {
                        if (p > runStart)
                        {
                            return Flush(runStart, p);
                        }

                        _state = State.ScriptDataDoubleEscaped;
                        return EmitReplacement(p);
                    }

                    if (c == '<')
                    {
                        // HTML 13.2.5.30 and 13.2.5.31: "</" followed by letters and a terminator.
                        p++;
                        _state = State.ScriptDataDoubleEscaped;
                        if (p < len && t[p] == '/')
                        {
                            int q = p + 1;
                            while (q < len && IsAsciiAlpha(t[q]))
                            {
                                q++;
                            }

                            if (q < len && t[q] is '\t' or '\n' or '\f' or ' ' or '/' or '>')
                            {
                                if (t.AsSpan(p + 1, q - p - 1).Equals("script", StringComparison.OrdinalIgnoreCase))
                                {
                                    _state = State.ScriptDataEscaped;
                                }

                                p = q + 1;
                            }
                            else
                            {
                                p = q;
                            }
                        }

                        continue;
                    }

                    if (_state == State.ScriptDataDoubleEscaped)
                    {
                        // c is '-'
                        _state = State.ScriptDataDoubleEscapedDash;
                    }
                    else if (_state == State.ScriptDataDoubleEscapedDash)
                    {
                        _state = c == '-' ? State.ScriptDataDoubleEscapedDashDash : State.ScriptDataDoubleEscaped;
                    }
                    else if (c == '>')
                    {
                        _state = State.ScriptData;
                    }
                    else if (c != '-')
                    {
                        _state = State.ScriptDataDoubleEscaped;
                    }

                    p++;
                    continue;
                }

                default:
                    throw new InvalidOperationException("Unexpected tokenizer state.");
            }
        }
    }

    // HTML 13.2.5.23 "script data escaped less-than sign state", for a '<' at p. Returns true when TextStep must
    // return `done` now (a token was produced, or the run was flushed); false to go on with p moved.
    private bool EscapedLessThan(int runStart, ref int p, out bool done)
    {
        string t = _text;
        int len = t.Length;
        done = false;
        int c1 = p + 1 < len ? t[p + 1] : -1;
        if (c1 == '/')
        {
            _state = State.ScriptDataEscaped;
            if (MatchesAppropriateEndTag(p))
            {
                done = p > runStart ? Flush(runStart, p) : ParseTag(p, isEnd: true, p + 2);
                return true;
            }

            p += 2;
        }
        else if (IsAsciiAlpha(c1))
        {
            // HTML 13.2.5.26 "script data double escape start state"
            int q = p + 1;
            while (q < len && IsAsciiAlpha(t[q]))
            {
                q++;
            }

            if (q < len && t[q] is '\t' or '\n' or '\f' or ' ' or '/' or '>')
            {
                _state = t.AsSpan(p + 1, q - p - 1).Equals("script", StringComparison.OrdinalIgnoreCase)
                    ? State.ScriptDataDoubleEscaped
                    : State.ScriptDataEscaped;
                p = q + 1;
            }
            else
            {
                _state = State.ScriptDataEscaped;
                p = q;
            }
        }
        else
        {
            _state = State.ScriptDataEscaped;
            p++;
        }

        return false;
    }

    private static bool IsAsciiAlpha(int c) => (uint)((c | 0x20) - 'a') <= 'z' - 'a';

    private static bool IsAsciiAlnum(int c) => IsAsciiAlpha(c) || (uint)(c - '0') <= 9;

    private static bool IsTagWhitespace(char c) => c is '\t' or '\n' or '\f' or ' ';

    // HTML 13.2.5: an end tag token is appropriate when its name equals the last start tag's name. The text at p is
    // "</"; the name that follows must be exactly that name, in either case, and be followed by whitespace, "/" or ">".
    private bool MatchesAppropriateEndTag(int p)
    {
        if (_lastStartLength == 0 || !_lastStartIsLetters)
        {
            return false;
        }

        string t = _text;
        int q = p + 2;
        if (q + _lastStartLength >= t.Length)
        {
            return false;
        }

        for (int k = 0; k < _lastStartLength; k++)
        {
            if ((t[q + k] | 0x20) != _lastStart[k])
            {
                return false;
            }
        }

        return t[q + _lastStartLength] is '\t' or '\n' or '\f' or ' ' or '/' or '>';
    }

    // The input ended inside the script's HTML-comment-like text.
    private bool EndScriptComment(int runStart)
    {
        ReportError(HtmlParseError.EofInScriptHtmlCommentLikeText, _text.Length);
        return EndText(runStart, _text.Length);
    }

    private bool EndText(int runStart, int end)
    {
        _state = State.Eof;
        _pos = _text.Length;
        if (end > runStart)
        {
            EmitCharacters(runStart, end);
            return true;
        }

        return false;
    }

    private bool Flush(int runStart, int end)
    {
        _pos = end;
        EmitCharacters(runStart, end);
        return true;
    }

    private void EmitCharacters(int start, int end)
    {
        _kind = HtmlTokenKind.Character;
        _start = start;
        _end = end;
        _data = new TextRef { Start = start, Length = end - start, SourceStart = start, SourceEnd = end };
    }

    // A U+0000 that the standard replaces with U+FFFD: its own token.
    private bool EmitReplacement(int p)
    {
        ReportError(HtmlParseError.UnexpectedNullCharacter, p);
        _bufLen = 0;
        AppendBuffer('\uFFFD');
        _kind = HtmlTokenKind.Character;
        _start = p;
        _end = p + 1;
        _data = new TextRef { Start = 0, Length = 1, InBuffer = true, SourceStart = p, SourceEnd = p + 1 };
        _pos = p + 1;
        return true;
    }

    // A character reference that produced text: its own token, holding the decoded characters.
    private bool EmitReference(int start, int end, int count)
    {
        _bufLen = 0;
        AppendBuffer(_reference.AsSpan(0, count));
        _kind = HtmlTokenKind.Character;
        _start = start;
        _end = end;
        _data = new TextRef { Start = 0, Length = count, InBuffer = true, SourceStart = start, SourceEnd = end };
        _pos = end;
        return true;
    }

    private static readonly SearchValues<char> s_inputStreamSuspects = CreateInputStreamSuspects();

    // Errors HTML 13.2.3.5 reports for the input stream: surrogates, noncharacters and controls.
    private static List<ParseError>? FindInputStreamErrors(string text)
    {
        var list = new List<ParseError>();
        ReadOnlySpan<char> span = text;
        int i = 0;
        while (i < span.Length)
        {
            int j = span[i..].IndexOfAny(s_inputStreamSuspects);
            if (j < 0)
            {
                break;
            }

            i += j;
            char c = span[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < span.Length && char.IsLowSurrogate(span[i + 1]))
                {
                    // A supplementary-plane noncharacter ends in FFFE or FFFF.
                    int cp = char.ConvertToUtf32(c, span[i + 1]);
                    if ((cp & 0xFFFE) == 0xFFFE)
                    {
                        list.Add(new ParseError(HtmlParseError.NoncharacterInInputStream, i));
                    }

                    i += 2;
                    continue;
                }

                list.Add(new ParseError(HtmlParseError.SurrogateInInputStream, i));
            }
            else if (char.IsLowSurrogate(c))
            {
                list.Add(new ParseError(HtmlParseError.SurrogateInInputStream, i));
            }
            else if (c >= '\uFDD0')
            {
                list.Add(new ParseError(HtmlParseError.NoncharacterInInputStream, i));
            }
            else
            {
                list.Add(new ParseError(HtmlParseError.ControlCharacterInInputStream, i));
            }

            i++;
        }

        return list.Count == 0 ? null : list;
    }

    private static SearchValues<char> CreateInputStreamSuspects()
    {
        var chars = new List<char>();
        for (int c = 0x01; c <= 0x08; c++)
        {
            chars.Add((char)c);
        }

        chars.Add('\u000B');
        for (int c = 0x0E; c <= 0x1F; c++)
        {
            chars.Add((char)c);
        }

        for (int c = 0x7F; c <= 0x9F; c++)
        {
            chars.Add((char)c);
        }

        for (int c = 0xD800; c <= 0xDFFF; c++)
        {
            chars.Add((char)c);
        }

        for (int c = 0xFDD0; c <= 0xFDEF; c++)
        {
            chars.Add((char)c);
        }

        chars.Add('\uFFFE');
        chars.Add('\uFFFF');
        return SearchValues.Create(chars.ToArray());
    }
}
