using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace RecursiveAI.OneHtml.Css;

/// <summary>A half-open range of UTF-16 offsets into the parsed text.</summary>
/// <param name="Start">First offset.</param>
/// <param name="End">Offset after the last character.</param>
public readonly record struct CssRange(int Start, int End)
{
    /// <summary>The number of characters in the range.</summary>
    public int Length => End - Start;

    /// <summary>True when the range is empty.</summary>
    public bool IsEmpty => End <= Start;
}

/// <summary>A token of a <see cref="CssTree"/>. A view over the tree's token array; creating one allocates nothing.</summary>
public readonly struct CssToken : IEquatable<CssToken>
{
    private readonly CssTree _tree;
    private readonly int _index;

    internal CssToken(CssTree tree, int index)
    {
        _tree = tree;
        _index = index;
    }

    private ref readonly Token T => ref _tree.Tokens[_index];

    /// <summary>The token's index in the tree's token array.</summary>
    public int Index => _index;

    /// <summary>The token kind.</summary>
    public CssTokenKind Kind => T.Kind;

    /// <summary>Source range of the token.</summary>
    public CssRange Range => new(T.Start, T.End);

    /// <summary>The token's source text, verbatim.</summary>
    public ReadOnlySpan<char> Raw => _tree.Text.AsSpan(T.Start, T.End - T.Start);

    /// <summary>
    /// The token's value with escapes decoded: the name of an ident, function, at-keyword or hash; the content of a
    /// string or url; the code point of a delim; the unit of a dimension. For other kinds, the source text.
    /// </summary>
    public string Value
    {
        get
        {
            ref readonly Token t = ref T;
            string text = _tree.Text;
            switch (t.Kind)
            {
                case CssTokenKind.Ident:
                    return CssChars.Decode(text.AsSpan(t.Start, t.End - t.Start));
                case CssTokenKind.Function:
                    return CssChars.Decode(text.AsSpan(t.Start, t.Aux - t.Start));
                case CssTokenKind.AtKeyword:
                case CssTokenKind.Hash:
                    return CssChars.Decode(text.AsSpan(t.Start + 1, t.End - t.Start - 1));
                case CssTokenKind.QuotedString:
                    return CssChars.Decode(text.AsSpan(t.Start + 1, t.End - t.Start - 1 - ((t.Flags & Token.Terminated) != 0 ? 1 : 0)), inString: true);
                case CssTokenKind.Url:
                    return CssChars.Decode(text.AsSpan(t.Aux, UrlValueEnd(text, t) - t.Aux));
                case CssTokenKind.Dimension:
                    return CssChars.Decode(text.AsSpan(t.Aux, t.End - t.Aux));
                case CssTokenKind.Delim:
                    char c = text[t.Start];
                    return c == '\0' || char.IsSurrogate(c) ? "\uFFFD" : c.ToString();
                default:
                    return text.Substring(t.Start, t.End - t.Start);
            }
        }
    }

    /// <summary>
    /// For hash tokens, true when the hash would start an identifier (the "id" type flag of CSS Syntax §4); only
    /// those are valid ID selectors.
    /// </summary>
    public bool IsHashId => (T.Flags & Token.HashIsId) != 0;

    /// <summary>For string and url tokens, true when the closing quote or parenthesis is in the source (false at end of input).</summary>
    public bool IsTerminated => (T.Flags & Token.Terminated) != 0;

    /// <summary>For number, percentage and dimension tokens, true when the number has no fraction or exponent.</summary>
    public bool IsInteger => (T.Flags & Token.IsInteger) != 0;

    /// <summary>For number, percentage and dimension tokens, the sign written in the source, or <c>'\0'</c> when none.</summary>
    public char Sign => T.Kind is CssTokenKind.Number or CssTokenKind.Percentage or CssTokenKind.Dimension && _tree.Text[T.Start] is '+' or '-'
        ? _tree.Text[T.Start]
        : '\0';

    /// <summary>
    /// For number, percentage and dimension tokens, the numeric value (a percentage's value is the number before
    /// the <c>%</c>). Values too large for a double are infinite. Zero for other kinds.
    /// </summary>
    public double NumberValue
    {
        get
        {
            ref readonly Token t = ref T;
            if (t.Kind is not (CssTokenKind.Number or CssTokenKind.Percentage or CssTokenKind.Dimension))
            {
                return 0;
            }

            return double.Parse(_tree.Text.AsSpan(t.Start, t.Aux - t.Start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>For number, percentage and dimension tokens, the source text of the number (no <c>%</c>, no unit); empty for other kinds.</summary>
    public ReadOnlySpan<char> NumberText =>
        T.Kind is CssTokenKind.Number or CssTokenKind.Percentage or CssTokenKind.Dimension
            ? _tree.Text.AsSpan(T.Start, T.Aux - T.Start)
            : default;

    /// <summary>For a <see cref="CssTokenKind.UnicodeRange"/> token, the inclusive start and end code points (CSS Syntax §4.3.14).</summary>
    /// <param name="start">The first code point.</param>
    /// <param name="end">The last code point; less than <paramref name="start"/> for an empty range.</param>
    public void GetUnicodeRange(out int start, out int end)
    {
        start = end = 0;
        if (T.Kind != CssTokenKind.UnicodeRange)
        {
            return;
        }

        ReadOnlySpan<char> r = Raw[2..];
        int dash = r.IndexOf('-');
        if (r.IndexOf('?') >= 0)
        {
            start = ParseHex(r, '0');
            end = ParseHex(r, 'F');
        }
        else if (dash >= 0)
        {
            start = ParseHex(r[..dash], '0');
            end = ParseHex(r[(dash + 1)..], '0');
        }
        else
        {
            start = end = ParseHex(r, '0');
        }
    }

    private static int ParseHex(ReadOnlySpan<char> s, char questionAs)
    {
        int v = 0;
        foreach (char c in s)
        {
            v = (v << 4) | CssChars.HexValue(c == '?' ? questionAs : c);
        }

        return v;
    }

    /// <summary>True when the decoded <see cref="Value"/> equals <paramref name="value"/> ignoring ASCII case.</summary>
    /// <param name="value">The ASCII text to compare with.</param>
    public bool ValueEqualsIgnoreCase(string value) => Value.Equals(value, StringComparison.OrdinalIgnoreCase);

    // Escape-aware end of an unquoted url's value: the whitespace before ")" is not part of it, but an escaped
    // whitespace character is.
    internal static int UrlValueEnd(string text, in Token t)
    {
        int limit = t.End;
        if ((t.Flags & Token.Terminated) != 0)
        {
            limit--; // the ")"
        }

        int i = t.Aux;
        int last = i;
        while (i < limit)
        {
            char c = text[i];
            if (c == '\\')
            {
                i++;
                if (i < limit)
                {
                    if (CssChars.IsHexDigit(text[i]))
                    {
                        int n = 0;
                        while (i < limit && n < 6 && CssChars.IsHexDigit(text[i]))
                        {
                            i++;
                            n++;
                        }

                        if (i < limit && CssChars.IsWhitespace(text[i]))
                        {
                            if (text[i] == '\r' && i + 1 < limit && text[i + 1] == '\n')
                            {
                                i++;
                            }

                            i++;
                        }
                    }
                    else
                    {
                        i++;
                    }
                }

                last = i;
            }
            else if (CssChars.IsWhitespace(c))
            {
                i++;
            }
            else
            {
                i++;
                last = i;
            }
        }

        return last;
    }

    /// <inheritdoc/>
    public bool Equals(CssToken other) => ReferenceEquals(_tree, other._tree) && _index == other._index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CssToken o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode() => _index;

    /// <summary>Equality of handles.</summary>
    /// <param name="left">A handle.</param>
    /// <param name="right">Another handle.</param>
    public static bool operator ==(CssToken left, CssToken right) => left.Equals(right);

    /// <summary>Inequality of handles.</summary>
    /// <param name="left">A handle.</param>
    /// <param name="right">Another handle.</param>
    public static bool operator !=(CssToken left, CssToken right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => Raw.ToString();
}

/// <summary>The shape of a <see cref="ComponentValue"/>.</summary>
public enum ComponentValueKind : byte
{
    /// <summary>A preserved token (CSS Syntax §5.2).</summary>
    Token,

    /// <summary>A function: a function token and its arguments up to the matching <c>)</c>.</summary>
    Function,

    /// <summary>A <c>()</c>, <c>[]</c> or <c>{}</c> block.</summary>
    SimpleBlock,
}

/// <summary>
/// A component value (CSS Syntax §5.2): a token, a function with its arguments, or a simple block with its contents.
/// A view over the token array; nothing is allocated per value.
/// </summary>
public readonly struct ComponentValue : IEquatable<ComponentValue>
{
    private readonly CssTree _tree;
    private readonly int _index;
    private readonly int _limit;

    internal ComponentValue(CssTree tree, int index, int limit)
    {
        _tree = tree;
        _index = index;
        _limit = limit;
    }

    /// <summary>The first token: the token itself, the function token, or the opening bracket.</summary>
    public CssToken Token => new(_tree, _index);

    /// <summary>Whether this is a token, a function or a simple block.</summary>
    public ComponentValueKind Kind => _tree.Tokens[_index].Kind switch
    {
        CssTokenKind.Function => ComponentValueKind.Function,
        CssTokenKind.LeftBrace or CssTokenKind.LeftBracket or CssTokenKind.LeftParen => ComponentValueKind.SimpleBlock,
        _ => ComponentValueKind.Token,
    };

    /// <summary>True for a whitespace token.</summary>
    public bool IsWhitespace => _tree.Tokens[_index].Kind == CssTokenKind.Whitespace;

    /// <summary>True for a function, and its name equals <paramref name="name"/> ignoring ASCII case.</summary>
    /// <param name="name">The ASCII function name without the parenthesis.</param>
    public bool IsFunctionNamed(string name) => Kind == ComponentValueKind.Function && Token.ValueEqualsIgnoreCase(name);

    /// <summary>True for an ident token whose value equals <paramref name="value"/> ignoring ASCII case.</summary>
    /// <param name="value">The ASCII text.</param>
    public bool IsIdent(string value) => _tree.Tokens[_index].Kind == CssTokenKind.Ident && Token.ValueEqualsIgnoreCase(value);

    /// <summary>For a function or block, its contents; empty for a token.</summary>
    public ComponentValueList Children
    {
        get
        {
            if (Kind == ComponentValueKind.Token)
            {
                return new ComponentValueList(_tree, _index + 1, _index + 1);
            }

            int close = _tree.Match[_index];
            return new ComponentValueList(_tree, _index + 1, close >= 0 ? close : _limit);
        }
    }

    /// <summary>True when a function or block has its closing bracket in the source (false at end of input).</summary>
    public bool IsClosed => Kind == ComponentValueKind.Token || _tree.Match[_index] >= 0;

    /// <summary>The value's source range, closing bracket included.</summary>
    public CssRange Range
    {
        get
        {
            ref readonly Token first = ref _tree.Tokens[_index];
            if (Kind == ComponentValueKind.Token)
            {
                return new CssRange(first.Start, first.End);
            }

            int close = _tree.Match[_index];
            return new CssRange(first.Start, _tree.Tokens[close >= 0 ? close : _limit - 1].End);
        }
    }

    internal int Next => Kind == ComponentValueKind.Token ? _index + 1 : _tree.Match[_index] is int m and >= 0 ? m + 1 : _limit;

    /// <summary>The source text of the value, verbatim.</summary>
    public ReadOnlySpan<char> Raw
    {
        get
        {
            CssRange r = Range;
            return _tree.Text.AsSpan(r.Start, r.Length);
        }
    }

    /// <inheritdoc/>
    public bool Equals(ComponentValue other) => ReferenceEquals(_tree, other._tree) && _index == other._index;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ComponentValue o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode() => _index;

    /// <summary>Equality of handles.</summary>
    /// <param name="left">A handle.</param>
    /// <param name="right">Another handle.</param>
    public static bool operator ==(ComponentValue left, ComponentValue right) => left.Equals(right);

    /// <summary>Inequality of handles.</summary>
    /// <param name="left">A handle.</param>
    /// <param name="right">Another handle.</param>
    public static bool operator !=(ComponentValue left, ComponentValue right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => Raw.ToString();
}

/// <summary>A sequence of component values: a token range of a <see cref="CssTree"/>. Whitespace tokens are included.</summary>
public readonly struct ComponentValueList : IEnumerable<ComponentValue>
{
    private readonly CssTree _tree;
    private readonly int _from;
    private readonly int _to;

    internal ComponentValueList(CssTree tree, int from, int to)
    {
        _tree = tree;
        _from = from;
        _to = to;
    }

    /// <summary>True when the list has no values.</summary>
    public bool IsEmpty => _from >= _to;

    /// <summary>The number of values (counted by walking the list).</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (ComponentValue unused in this)
            {
                n++;
            }

            return n;
        }
    }

    /// <summary>The source range from the first value's start to the last value's end; empty when the list is empty.</summary>
    public CssRange Range => _from >= _to
        ? new CssRange(0, 0)
        : new CssRange(_tree.Tokens[_from].Start, _tree.Tokens[_to - 1].End);

    /// <summary>The source text of the list, verbatim (comments between values included).</summary>
    public ReadOnlySpan<char> Raw
    {
        get
        {
            if (_from >= _to)
            {
                return default;
            }

            CssRange r = Range;
            return _tree.Text.AsSpan(r.Start, r.Length);
        }
    }

    /// <summary>The same list without leading and trailing whitespace values.</summary>
    public ComponentValueList Trimmed()
    {
        int a = _from;
        int b = _to;
        while (a < b && _tree.Tokens[a].Kind == CssTokenKind.Whitespace)
        {
            a++;
        }

        while (b > a && _tree.Tokens[b - 1].Kind == CssTokenKind.Whitespace)
        {
            b--;
        }

        return new ComponentValueList(_tree, a, b);
    }

    internal bool TryGetBounds(out CssTree? tree, out int from, out int to)
    {
        tree = _tree;
        from = _from;
        to = _to;
        return _tree is not null;
    }

    /// <summary>Gets an enumerator over the values.</summary>
    public Enumerator GetEnumerator() => new(_tree, _from, _to);

    IEnumerator<ComponentValue> IEnumerable<ComponentValue>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public override string ToString() => Raw.ToString();

    /// <summary>Enumerator over a <see cref="ComponentValueList"/>.</summary>
    public struct Enumerator : IEnumerator<ComponentValue>
    {
        private readonly CssTree _tree;
        private readonly int _to;
        private int _next;
        private ComponentValue _current;

        internal Enumerator(CssTree tree, int from, int to)
        {
            _tree = tree;
            _to = to;
            _next = from;
            _current = default;
        }

        /// <inheritdoc/>
        public readonly ComponentValue Current => _current;

        readonly object IEnumerator.Current => _current;

        /// <inheritdoc/>
        public bool MoveNext()
        {
            if (_next >= _to)
            {
                return false;
            }

            _current = new ComponentValue(_tree, _next, _to);
            _next = _current.Next;
            return true;
        }

        /// <inheritdoc/>
        public readonly void Reset() => throw new NotSupportedException();

        /// <inheritdoc/>
        public readonly void Dispose()
        {
        }
    }
}
