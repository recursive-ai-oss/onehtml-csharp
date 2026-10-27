using System;
using System.Globalization;
using System.Threading;

namespace RecursiveAI.OneHtml.Css;

/// <summary>
/// A cursor over a range of a <see cref="CssTree"/>'s tokens, for value parsers: the typed parsers in this library are
/// written on it, and a declaration validator or a caller's own property grammar can use the same type. Everything works
/// on token indexes (<see cref="From"/> up to, not including, <see cref="To"/>); a function or block is skipped in one
/// step through the tree's bracket pairing, so nothing here recurses on input depth, and nothing allocates unless its
/// documentation says so. An index outside the tree's tokens throws <see cref="IndexOutOfRangeException"/>.
/// </summary>
public readonly struct TokenRange
{
    /// <summary>The tree that owns the tokens.</summary>
    public CssTree Tree { get; }

    /// <summary>Index of the first token of the range.</summary>
    public int From { get; }

    /// <summary>Index just past the last token of the range.</summary>
    public int To { get; }

    /// <summary>Creates a range over tokens <paramref name="from"/> up to, not including, <paramref name="to"/> of <paramref name="tree"/>.</summary>
    /// <param name="tree">The owning tree.</param>
    /// <param name="from">First token index.</param>
    /// <param name="to">Index just past the last token.</param>
    public TokenRange(CssTree tree, int from, int to)
    {
        Tree = tree;
        From = from;
        To = to;
    }

    /// <summary>The token range of a component value list (whitespace tokens included).</summary>
    /// <param name="list">A list from a parsed tree.</param>
    /// <exception cref="ArgumentException">The list is the default, uninitialized value.</exception>
    public static TokenRange Of(ComponentValueList list)
    {
        if (!list.TryGetBounds(out CssTree? tree, out int from, out int to))
        {
            throw new ArgumentException("The list is not initialized.", nameof(list));
        }

        return new TokenRange(tree!, from, to);
    }

    /// <summary>
    /// The tokens of this range as a <see cref="ComponentValueList"/> over the same tree, with nothing copied; the inverse of <see cref="Of"/>.
    /// A default range gives a default (empty) list, and bounds outside the tree are clamped to it.
    /// </summary>
    public ComponentValueList ToList()
    {
        if (Tree is null)
        {
            return default;
        }

        int from = Math.Clamp(From, 0, Tree.Tokens.Length);
        return new ComponentValueList(Tree, from, Math.Clamp(To, from, Tree.Tokens.Length));
    }

    /// <summary>Parses <paramref name="text"/> as a list of component values (CSS Syntax 3 section 5.4.9) and returns the range covering all of it, for a value given as text.</summary>
    /// <param name="text">Decoded value text, without the property name or <c>!important</c>.</param>
    /// <param name="options">Limits; <see cref="CssParseOptions.Default"/> when null.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    public static TokenRange Parse(string text, CssParseOptions? options = null, CancellationToken cancellationToken = default) =>
        Of(CssTree.ParseComponentValueList(text, options, cancellationToken).Values);

    internal ref readonly Token this[int i] => ref Tree.Tokens[i];

    /// <summary>The token at index <paramref name="i"/> as a public view (kind, source range, decoded value).</summary>
    /// <param name="i">A token index.</param>
    public CssToken TokenAt(int i) => new(Tree, i);

    /// <summary>The kind of the token at index <paramref name="i"/>.</summary>
    /// <param name="i">A token index.</param>
    public CssTokenKind Kind(int i) => Tree.Tokens[i].Kind;

    /// <summary>Index just past the component value that starts at <paramref name="i"/>.</summary>
    /// <param name="i">Index of a token or of the opener of a function or block.</param>
    public int Next(int i)
    {
        CssTokenKind k = Tree.Tokens[i].Kind;
        if (k is CssTokenKind.Function or CssTokenKind.LeftParen or CssTokenKind.LeftBracket or CssTokenKind.LeftBrace)
        {
            int m = Tree.Match[i];
            return m >= 0 && m < To ? m + 1 : To;
        }

        return i + 1;
    }

    /// <summary>For a function or block at <paramref name="i"/>, the index of its closer, or <see cref="To"/> when unclosed.</summary>
    /// <param name="i">Index of a function, <c>(</c>, <c>[</c> or <c>{</c> token.</param>
    public int Close(int i)
    {
        int m = Tree.Match[i];
        return m >= 0 && m < To ? m : To;
    }

    /// <summary>Index of the first token at or after <paramref name="i"/> that is not whitespace, or <see cref="To"/>.</summary>
    /// <param name="i">A token index.</param>
    public int SkipWs(int i)
    {
        while (i < To && Tree.Tokens[i].Kind == CssTokenKind.Whitespace)
        {
            i++;
        }

        return i;
    }

    /// <summary>The tokens inside the function or block that starts at <paramref name="functionIndex"/>, brackets excluded; an unclosed one runs to <see cref="To"/>.</summary>
    /// <param name="functionIndex">Index of a function, <c>(</c>, <c>[</c> or <c>{</c> token.</param>
    public TokenRange Inner(int functionIndex) => new(Tree, functionIndex + 1, Close(functionIndex));

    /// <summary>A range over the same tree with other bounds.</summary>
    /// <param name="from">First token index.</param>
    /// <param name="to">Index just past the last token.</param>
    public TokenRange Sub(int from, int to) => new(Tree, from, to);

    /// <summary>The same range without leading and trailing whitespace tokens.</summary>
    public TokenRange Trim()
    {
        int a = From;
        int b = To;
        while (a < b && Tree.Tokens[a].Kind == CssTokenKind.Whitespace)
        {
            a++;
        }

        while (b > a && Tree.Tokens[b - 1].Kind == CssTokenKind.Whitespace)
        {
            b--;
        }

        return new TokenRange(Tree, a, b);
    }

    /// <summary>True when the range holds no tokens.</summary>
    public bool IsEmpty => From >= To;

    /// <summary>The source text of token <paramref name="i"/>, verbatim (escapes not decoded).</summary>
    /// <param name="i">A token index.</param>
    public ReadOnlySpan<char> Text(int i) => Tree.Text.AsSpan(Tree.Tokens[i].Start, Tree.Tokens[i].End - Tree.Tokens[i].Start);

    /// <summary>The source text of this range, verbatim; allocates.</summary>
    public string RawText() => From >= To ? string.Empty : Tree.Text.Substring(Tree.Tokens[From].Start, Tree.Tokens[To - 1].End - Tree.Tokens[From].Start);

    /// <summary>The name of an ident or function token, without allocation when it has no escapes.</summary>
    /// <param name="i">Index of an ident or function token.</param>
    /// <param name="decoded">The decoded name when the source has escapes, else null (the span is then the source text).</param>
    public ReadOnlySpan<char> Name(int i, out string? decoded)
    {
        ref readonly Token t = ref Tree.Tokens[i];
        int end = t.Kind == CssTokenKind.Function ? t.Aux : t.End;
        ReadOnlySpan<char> raw = Tree.Text.AsSpan(t.Start, end - t.Start);
        if (raw.IndexOf('\\') < 0 && raw.IndexOf('\0') < 0)
        {
            decoded = null;
            return raw;
        }

        decoded = CssChars.Decode(raw);
        return decoded;
    }

    /// <summary>True when token <paramref name="i"/> is in the range, is an ident, and equals <paramref name="keyword"/> ignoring ASCII case (escapes decoded).</summary>
    /// <param name="i">A token index.</param>
    /// <param name="keyword">The ASCII keyword.</param>
    public bool IsIdent(int i, string keyword) =>
        i < To && Tree.Tokens[i].Kind == CssTokenKind.Ident && Name(i, out _).Equals(keyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when token <paramref name="i"/> is in the range, is a function, and its name equals <paramref name="name"/> ignoring ASCII case.</summary>
    /// <param name="i">A token index.</param>
    /// <param name="name">The ASCII function name without the parenthesis.</param>
    public bool IsFunction(int i, string name) =>
        i < To && Tree.Tokens[i].Kind == CssTokenKind.Function && Name(i, out _).Equals(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when token <paramref name="i"/> is in the range and is the single-character delimiter <paramref name="c"/>.</summary>
    /// <param name="i">A token index.</param>
    /// <param name="c">The delimiter.</param>
    public bool IsDelim(int i, char c) => i < To && Tree.Tokens[i].Kind == CssTokenKind.Delim && Tree.Text[Tree.Tokens[i].Start] == c;

    /// <summary>The decoded name of the ident or function token <paramref name="i"/>; allocates.</summary>
    /// <param name="i">A token index.</param>
    public string IdentString(int i)
    {
        ReadOnlySpan<char> n = Name(i, out string? d);
        return d ?? n.ToString();
    }

    /// <summary>The numeric value of the number, percentage or dimension token <paramref name="i"/> (a percentage's number before the <c>%</c>).</summary>
    /// <param name="i">A token index.</param>
    public double Number(int i)
    {
        ref readonly Token t = ref Tree.Tokens[i];
        return double.Parse(Tree.Text.AsSpan(t.Start, t.Aux - t.Start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>True when the number, percentage or dimension token <paramref name="i"/> has no fraction or exponent.</summary>
    /// <param name="i">A token index.</param>
    public bool IsInteger(int i) => (Tree.Tokens[i].Flags & Token.IsInteger) != 0;

    /// <summary>The unit text of the dimension token <paramref name="i"/>, verbatim.</summary>
    /// <param name="i">A token index.</param>
    public ReadOnlySpan<char> UnitText(int i) => Tree.Text.AsSpan(Tree.Tokens[i].Aux, Tree.Tokens[i].End - Tree.Tokens[i].Aux);

    /// <summary>Reads the unit of a dimension token; false for an unknown unit.</summary>
    /// <param name="i">Index of a dimension token.</param>
    /// <param name="unit">The unit.</param>
    public bool TryUnit(int i, out CssUnit unit)
    {
        ReadOnlySpan<char> u = UnitText(i);
        if (u.IndexOf('\\') >= 0)
        {
            u = CssChars.Decode(u);
        }

        return CssUnits.TryParse(u, out unit);
    }

    /// <summary>The decoded content of the string or url token <paramref name="i"/>; allocates.</summary>
    /// <param name="i">A token index.</param>
    public string StringValue(int i) => new CssToken(Tree, i).Value;
}
