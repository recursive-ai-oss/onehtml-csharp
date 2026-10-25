using System;
using System.Globalization;

namespace RecursiveAI.OneHtml.Css;

/// <summary>
/// A cursor over a range of a <see cref="CssTree"/>'s tokens, for the typed value parsers. Everything works on
/// token indexes; a function or block is skipped in one step through the tree's bracket pairing, so nothing here
/// recurses on input depth.
/// </summary>
internal readonly struct TokenRange
{
    public readonly CssTree Tree;
    public readonly int From;
    public readonly int To;

    public TokenRange(CssTree tree, int from, int to)
    {
        Tree = tree;
        From = from;
        To = to;
    }

    public static TokenRange Of(ComponentValueList list)
    {
        if (!list.TryGetBounds(out CssTree? tree, out int from, out int to))
        {
            throw new ArgumentException("The list is not initialized.", nameof(list));
        }

        return new TokenRange(tree!, from, to);
    }

    public ref readonly Token this[int i] => ref Tree.Tokens[i];

    public CssTokenKind Kind(int i) => Tree.Tokens[i].Kind;

    /// <summary>Index just past the component value that starts at <paramref name="i"/>.</summary>
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
    public int Close(int i)
    {
        int m = Tree.Match[i];
        return m >= 0 && m < To ? m : To;
    }

    public int SkipWs(int i)
    {
        while (i < To && Tree.Tokens[i].Kind == CssTokenKind.Whitespace)
        {
            i++;
        }

        return i;
    }

    public TokenRange Inner(int functionIndex) => new(Tree, functionIndex + 1, Close(functionIndex));

    public TokenRange Sub(int from, int to) => new(Tree, from, to);

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

    public bool IsEmpty => From >= To;

    public ReadOnlySpan<char> Text(int i) => Tree.Text.AsSpan(Tree.Tokens[i].Start, Tree.Tokens[i].End - Tree.Tokens[i].Start);

    /// <summary>The raw source text of this range.</summary>
    public string RawText() => From >= To ? string.Empty : Tree.Text.Substring(Tree.Tokens[From].Start, Tree.Tokens[To - 1].End - Tree.Tokens[From].Start);

    /// <summary>The name of an ident or function token, without allocation when it has no escapes.</summary>
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

    public bool IsIdent(int i, string keyword) =>
        i < To && Tree.Tokens[i].Kind == CssTokenKind.Ident && Name(i, out _).Equals(keyword, StringComparison.OrdinalIgnoreCase);

    public bool IsFunction(int i, string name) =>
        i < To && Tree.Tokens[i].Kind == CssTokenKind.Function && Name(i, out _).Equals(name, StringComparison.OrdinalIgnoreCase);

    public bool IsDelim(int i, char c) => i < To && Tree.Tokens[i].Kind == CssTokenKind.Delim && Tree.Text[Tree.Tokens[i].Start] == c;

    public string IdentString(int i)
    {
        ReadOnlySpan<char> n = Name(i, out string? d);
        return d ?? n.ToString();
    }

    public double Number(int i)
    {
        ref readonly Token t = ref Tree.Tokens[i];
        return double.Parse(Tree.Text.AsSpan(t.Start, t.Aux - t.Start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public bool IsInteger(int i) => (Tree.Tokens[i].Flags & Token.IsInteger) != 0;

    public ReadOnlySpan<char> UnitText(int i) => Tree.Text.AsSpan(Tree.Tokens[i].Aux, Tree.Tokens[i].End - Tree.Tokens[i].Aux);

    /// <summary>Reads the unit of a dimension token; false for an unknown unit.</summary>
    public bool TryUnit(int i, out CssUnit unit)
    {
        ReadOnlySpan<char> u = UnitText(i);
        if (u.IndexOf('\\') >= 0)
        {
            u = CssChars.Decode(u);
        }

        return CssUnits.TryParse(u, out unit);
    }

    public string StringValue(int i) => new CssToken(Tree, i).Value;
}
