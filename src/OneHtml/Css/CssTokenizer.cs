using System;
using System.Threading;

namespace RecursiveAI.OneHtml.Css;

/// <summary>One token: a source range plus a few facts. Values are decoded lazily from the source text.</summary>
internal struct Token
{
    public const byte HashIsId = 1;
    public const byte IsInteger = 2;
    public const byte HasEscape = 4;
    public const byte Terminated = 8;

    public int Start;
    public int End;

    /// <summary>
    /// Kind-specific offset into the text: for a function the index of its "("; for a url the start of the value;
    /// for a number, percentage or dimension the end of the numeric part.
    /// </summary>
    public int Aux;

    public CssTokenKind Kind;
    public byte Flags;
}

/// <summary>
/// The tokenizer (CSS Syntax §4). It works directly on the original text: the preprocessing of §3.3 is applied as
/// the characters are read (CR, FF and CRLF are newlines, U+0000 and lone surrogates act as U+FFFD), so token
/// offsets are offsets into the original text. Comments are recorded in a separate list.
/// </summary>
internal sealed class CssTokenizer
{
    private readonly string _text;
    private readonly int _end;
    private readonly int _maxTokens;
    private readonly bool _unicodeRanges;
    private readonly bool _recordComments;
    private readonly CancellationToken _cancel;
    private int _pos;
    private bool _sawEscape;

    public Token[] Tokens;
    public int Count;
    public int[] Comments; // pairs: start, end
    public int CommentCount; // number of ints used

    public CssTokenizer(string text, int start, int end, int maxTokens, bool unicodeRanges, bool recordComments,
        Token[]? tokens, int count, CancellationToken cancel)
    {
        _text = text;
        _pos = start;
        _end = end;
        _maxTokens = maxTokens;
        _unicodeRanges = unicodeRanges;
        _recordComments = recordComments;
        _cancel = cancel;
        // Real stylesheets run about one token per three characters; the array doubles from there if needed.
        Tokens = tokens ?? new Token[Math.Min(maxTokens, ((end - start) / 3) + 16)];
        Count = count;
        Comments = recordComments ? new int[16] : [];
    }

    public void Run()
    {
        while (NextToken())
        {
            if ((Count & 0xFFF) == 0)
            {
                _cancel.ThrowIfCancellationRequested();
            }
        }
    }

    private void Add(CssTokenKind kind, int start, int aux = 0, byte flags = 0)
    {
        if (Count == _maxTokens)
        {
            throw new CssLimitExceededException(CssLimit.TokenCount, _maxTokens);
        }

        if (Count == Tokens.Length)
        {
            int newSize = (int)Math.Min((long)Tokens.Length * 2, _maxTokens);
            Array.Resize(ref Tokens, newSize);
        }

        Tokens[Count++] = new Token { Start = start, End = _pos, Aux = aux, Kind = kind, Flags = flags };
    }

    private char At(int i) => _text[i];

    // CSS Syntax §4.3.8 "check if two code points are a valid escape" for a backslash at i. A backslash before the
    // end of input counts as valid (the escape then stands for U+FFFD).
    private bool ValidEscapeAt(int i) => i < _end && At(i) == '\\' && (i + 1 >= _end || !CssChars.IsNewline(At(i + 1)));

    // §4.3.9 "check if three code points would start an ident sequence".
    private bool WouldStartIdent(int i)
    {
        if (i >= _end)
        {
            return false;
        }

        char c = At(i);
        if (c == '-')
        {
            if (i + 1 >= _end)
            {
                return false;
            }

            char c1 = At(i + 1);
            return CssChars.IsIdentStart(c1) || c1 == '-' || ValidEscapeAt(i + 1);
        }

        if (CssChars.IsIdentStart(c))
        {
            return true;
        }

        return c == '\\' && ValidEscapeAt(i);
    }

    // §4.3.10 "check if three code points would start a number".
    private bool WouldStartNumber(int i)
    {
        if (i >= _end)
        {
            return false;
        }

        char c = At(i);
        if (c is '+' or '-')
        {
            if (i + 1 >= _end)
            {
                return false;
            }

            char c1 = At(i + 1);
            if (CssChars.IsDigit(c1))
            {
                return true;
            }

            return c1 == '.' && i + 2 < _end && CssChars.IsDigit(At(i + 2));
        }

        if (c == '.')
        {
            return i + 1 < _end && CssChars.IsDigit(At(i + 1));
        }

        return CssChars.IsDigit(c);
    }

    // §4.3.2 "consume comments".
    private void ConsumeComments()
    {
        while (_pos + 1 < _end && At(_pos) == '/' && At(_pos + 1) == '*')
        {
            int start = _pos;
            int close = _text.AsSpan(_pos + 2, _end - _pos - 2).IndexOf("*/", StringComparison.Ordinal);
            _pos = close < 0 ? _end : _pos + 2 + close + 2;
            if (_recordComments)
            {
                if (CommentCount + 2 > Comments.Length)
                {
                    Array.Resize(ref Comments, Comments.Length * 2);
                }

                Comments[CommentCount++] = start;
                Comments[CommentCount++] = _pos;
            }
        }
    }

    // §4.3.1 "consume a token". Returns false at the end of input.
    private bool NextToken()
    {
        ConsumeComments();
        if (_pos >= _end)
        {
            return false;
        }

        int s = _pos;
        char c = At(_pos);
        switch (c)
        {
            case ' ' or '\t' or '\n' or '\r' or '\f':
                while (_pos < _end && CssChars.IsWhitespace(At(_pos)))
                {
                    _pos++;
                }

                Add(CssTokenKind.Whitespace, s);
                return true;
            case '"' or '\'':
                ConsumeString(c);
                return true;
            case '#':
                _pos++;
                if (_pos < _end && (CssChars.IsIdentChar(At(_pos)) || ValidEscapeAt(_pos)))
                {
                    byte flags = WouldStartIdent(_pos) ? Token.HashIsId : (byte)0;
                    _sawEscape = false;
                    ConsumeIdentSequence();
                    Add(CssTokenKind.Hash, s, 0, (byte)(flags | (_sawEscape ? Token.HasEscape : 0)));
                }
                else
                {
                    Add(CssTokenKind.Delim, s);
                }

                return true;
            case '(':
                _pos++;
                Add(CssTokenKind.LeftParen, s);
                return true;
            case ')':
                _pos++;
                Add(CssTokenKind.RightParen, s);
                return true;
            case '+':
                if (WouldStartNumber(_pos))
                {
                    ConsumeNumeric();
                }
                else
                {
                    _pos++;
                    Add(CssTokenKind.Delim, s);
                }

                return true;
            case ',':
                _pos++;
                Add(CssTokenKind.Comma, s);
                return true;
            case '-':
                if (WouldStartNumber(_pos))
                {
                    ConsumeNumeric();
                }
                else if (_pos + 2 < _end && At(_pos + 1) == '-' && At(_pos + 2) == '>')
                {
                    _pos += 3;
                    Add(CssTokenKind.Cdc, s);
                }
                else if (WouldStartIdent(_pos))
                {
                    ConsumeIdentLike();
                }
                else
                {
                    _pos++;
                    Add(CssTokenKind.Delim, s);
                }

                return true;
            case '.':
                if (WouldStartNumber(_pos))
                {
                    ConsumeNumeric();
                }
                else
                {
                    _pos++;
                    Add(CssTokenKind.Delim, s);
                }

                return true;
            case ':':
                _pos++;
                Add(CssTokenKind.Colon, s);
                return true;
            case ';':
                _pos++;
                Add(CssTokenKind.Semicolon, s);
                return true;
            case '<':
                if (_pos + 3 < _end && At(_pos + 1) == '!' && At(_pos + 2) == '-' && At(_pos + 3) == '-')
                {
                    _pos += 4;
                    Add(CssTokenKind.Cdo, s);
                }
                else
                {
                    _pos++;
                    Add(CssTokenKind.Delim, s);
                }

                return true;
            case '@':
                _pos++;
                if (WouldStartIdent(_pos))
                {
                    _sawEscape = false;
                    ConsumeIdentSequence();
                    Add(CssTokenKind.AtKeyword, s, 0, _sawEscape ? Token.HasEscape : (byte)0);
                }
                else
                {
                    Add(CssTokenKind.Delim, s);
                }

                return true;
            case '[':
                _pos++;
                Add(CssTokenKind.LeftBracket, s);
                return true;
            case '\\':
                if (ValidEscapeAt(_pos))
                {
                    ConsumeIdentLike();
                }
                else
                {
                    _pos++;
                    Add(CssTokenKind.Delim, s);
                }

                return true;
            case ']':
                _pos++;
                Add(CssTokenKind.RightBracket, s);
                return true;
            case '{':
                _pos++;
                Add(CssTokenKind.LeftBrace, s);
                return true;
            case '}':
                _pos++;
                Add(CssTokenKind.RightBrace, s);
                return true;
            case 'U' or 'u':
                if (_unicodeRanges && WouldStartUnicodeRange(_pos))
                {
                    ConsumeUnicodeRange();
                }
                else
                {
                    ConsumeIdentLike();
                }

                return true;
            default:
                if (CssChars.IsDigit(c))
                {
                    ConsumeNumeric();
                }
                else if (CssChars.IsIdentStart(c))
                {
                    ConsumeIdentLike();
                }
                else
                {
                    _pos++;
                    Add(CssTokenKind.Delim, s);
                }

                return true;
        }
    }

    // §4.3.5 "consume a string token".
    private void ConsumeString(char quote)
    {
        int s = _pos;
        _pos++;
        byte flags = 0;
        while (true)
        {
            if (_pos >= _end)
            {
                break; // parse error; the string ends at the end of input
            }

            char c = At(_pos);
            if (c == quote)
            {
                _pos++;
                flags |= Token.Terminated;
                break;
            }

            if (CssChars.IsNewline(c))
            {
                Add(CssTokenKind.BadString, s);
                return;
            }

            if (c == '\\')
            {
                if (_pos + 1 >= _end)
                {
                    _pos++;
                }
                else if (CssChars.IsNewline(At(_pos + 1)))
                {
                    ConsumeNewlineAt(_pos + 1);
                }
                else
                {
                    ConsumeEscape();
                }

                continue;
            }

            _pos++;
        }

        Add(CssTokenKind.QuotedString, s, 0, flags);
    }

    // Consumes a backslash at _pos followed by the newline at nl (CRLF counts as one newline).
    private void ConsumeNewlineAt(int nl)
    {
        _pos = nl + 1;
        if (At(nl) == '\r' && _pos < _end && At(_pos) == '\n')
        {
            _pos++;
        }
    }

    // §4.3.7 "consume an escaped code point"; _pos is at the backslash, which is verified to start a valid escape.
    private void ConsumeEscape()
    {
        _sawEscape = true;
        _pos++; // the backslash
        if (_pos >= _end)
        {
            return;
        }

        char c = At(_pos);
        if (CssChars.IsHexDigit(c))
        {
            int n = 0;
            while (_pos < _end && n < 6 && CssChars.IsHexDigit(At(_pos)))
            {
                _pos++;
                n++;
            }

            if (_pos < _end && CssChars.IsWhitespace(At(_pos)))
            {
                if (At(_pos) == '\r' && _pos + 1 < _end && At(_pos + 1) == '\n')
                {
                    _pos++;
                }

                _pos++;
            }
        }
        else
        {
            _pos++;
        }
    }

    // §4.3.12 "consume an ident sequence".
    private void ConsumeIdentSequence()
    {
        while (_pos < _end)
        {
            char c = At(_pos);
            if (CssChars.IsIdentChar(c))
            {
                _pos++;
            }
            else if (c == '\\' && ValidEscapeAt(_pos))
            {
                ConsumeEscape();
            }
            else
            {
                break;
            }
        }
    }

    // §4.3.3 "consume a numeric token" with §4.3.13 "consume a number".
    private void ConsumeNumeric()
    {
        int s = _pos;
        byte flags = Token.IsInteger;
        if (At(_pos) is '+' or '-')
        {
            _pos++;
        }

        while (_pos < _end && CssChars.IsDigit(At(_pos)))
        {
            _pos++;
        }

        if (_pos + 1 < _end && At(_pos) == '.' && CssChars.IsDigit(At(_pos + 1)))
        {
            _pos++;
            while (_pos < _end && CssChars.IsDigit(At(_pos)))
            {
                _pos++;
            }

            flags = 0;
        }

        if (_pos < _end && (At(_pos) | 0x20) == 'e')
        {
            int p = _pos + 1;
            if (p < _end && At(p) is '+' or '-')
            {
                p++;
            }

            if (p < _end && CssChars.IsDigit(At(p)))
            {
                while (p < _end && CssChars.IsDigit(At(p)))
                {
                    p++;
                }

                _pos = p;
                flags = 0;
            }
        }

        int numberEnd = _pos;
        if (WouldStartIdent(_pos))
        {
            _sawEscape = false;
            ConsumeIdentSequence();
            Add(CssTokenKind.Dimension, s, numberEnd, (byte)(flags | (_sawEscape ? Token.HasEscape : 0)));
        }
        else if (_pos < _end && At(_pos) == '%')
        {
            _pos++;
            Add(CssTokenKind.Percentage, s, numberEnd, flags);
        }
        else
        {
            Add(CssTokenKind.Number, s, numberEnd, flags);
        }
    }

    // §4.3.4 "consume an ident-like token".
    private void ConsumeIdentLike()
    {
        int s = _pos;
        _sawEscape = false;
        ConsumeIdentSequence();
        byte flags = _sawEscape ? Token.HasEscape : (byte)0;
        int nameEnd = _pos;
        if (_pos < _end && At(_pos) == '(')
        {
            if (IsUrlName(s, nameEnd, _sawEscape))
            {
                _pos++;
                while (_pos + 1 < _end && CssChars.IsWhitespace(At(_pos)) && CssChars.IsWhitespace(At(_pos + 1)))
                {
                    _pos++;
                }

                bool quoted = _pos < _end
                    && (At(_pos) is '"' or '\''
                        || (CssChars.IsWhitespace(At(_pos)) && _pos + 1 < _end && At(_pos + 1) is '"' or '\''));
                if (quoted)
                {
                    Add(CssTokenKind.Function, s, nameEnd, flags);
                }
                else
                {
                    ConsumeUrl(s, flags);
                }
            }
            else
            {
                _pos++;
                Add(CssTokenKind.Function, s, nameEnd, flags);
            }
        }
        else
        {
            Add(CssTokenKind.Ident, s, 0, flags);
        }
    }

    private bool IsUrlName(int s, int e, bool hasEscape)
    {
        if (!hasEscape)
        {
            return e - s == 3 && (At(s) | 0x20) == 'u' && (At(s + 1) | 0x20) == 'r' && (At(s + 2) | 0x20) == 'l';
        }

        return CssChars.Decode(_text.AsSpan(s, e - s)).Equals("url", StringComparison.OrdinalIgnoreCase);
    }

    // §4.3.6 "consume a url token"; "url(" and any extra leading whitespace are already consumed.
    private void ConsumeUrl(int s, byte flags)
    {
        while (_pos < _end && CssChars.IsWhitespace(At(_pos)))
        {
            _pos++;
        }

        int valueStart = _pos;
        while (true)
        {
            if (_pos >= _end)
            {
                Add(CssTokenKind.Url, s, valueStart, flags);
                return;
            }

            char c = At(_pos);
            if (c == ')')
            {
                _pos++;
                Add(CssTokenKind.Url, s, valueStart, (byte)(flags | Token.Terminated));
                return;
            }

            if (CssChars.IsWhitespace(c))
            {
                while (_pos < _end && CssChars.IsWhitespace(At(_pos)))
                {
                    _pos++;
                }

                if (_pos >= _end)
                {
                    Add(CssTokenKind.Url, s, valueStart, flags);
                }
                else if (At(_pos) == ')')
                {
                    _pos++;
                    Add(CssTokenKind.Url, s, valueStart, (byte)(flags | Token.Terminated));
                }
                else
                {
                    ConsumeBadUrlRemnants();
                    Add(CssTokenKind.BadUrl, s);
                }

                return;
            }

            if (c is '"' or '\'' or '(' || CssChars.IsNonPrintable(c))
            {
                _pos++;
                ConsumeBadUrlRemnants();
                Add(CssTokenKind.BadUrl, s);
                return;
            }

            if (c == '\\')
            {
                if (ValidEscapeAt(_pos))
                {
                    ConsumeEscape();
                    flags |= Token.HasEscape;
                    continue;
                }

                _pos++;
                ConsumeBadUrlRemnants();
                Add(CssTokenKind.BadUrl, s);
                return;
            }

            _pos++;
        }
    }

    // §4.3.15 "consume the remnants of a bad url".
    private void ConsumeBadUrlRemnants()
    {
        while (_pos < _end)
        {
            char c = At(_pos);
            if (c == ')')
            {
                _pos++;
                return;
            }

            if (c == '\\' && ValidEscapeAt(_pos))
            {
                ConsumeEscape();
            }
            else
            {
                _pos++;
            }
        }
    }

    // §4.3.11 "check if three code points would start a unicode-range".
    private bool WouldStartUnicodeRange(int i) =>
        i + 2 < _end && (At(i) | 0x20) == 'u' && At(i + 1) == '+' && (At(i + 2) == '?' || CssChars.IsHexDigit(At(i + 2)));

    // §4.3.14 "consume a unicode-range token".
    private void ConsumeUnicodeRange()
    {
        int s = _pos;
        _pos += 2;
        int digits = 0;
        while (_pos < _end && digits < 6 && CssChars.IsHexDigit(At(_pos)))
        {
            _pos++;
            digits++;
        }

        bool question = false;
        while (_pos < _end && digits < 6 && At(_pos) == '?')
        {
            _pos++;
            digits++;
            question = true;
        }

        if (!question && _pos + 1 < _end && At(_pos) == '-' && CssChars.IsHexDigit(At(_pos + 1)))
        {
            _pos++;
            int n = 0;
            while (_pos < _end && n < 6 && CssChars.IsHexDigit(At(_pos)))
            {
                _pos++;
                n++;
            }
        }

        Add(CssTokenKind.UnicodeRange, s);
    }
}
