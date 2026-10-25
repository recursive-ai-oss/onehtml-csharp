using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneHtml.Css;

/// <summary>
/// Serializes a parse without using its source text: canonical CSS text built from the structure alone (CSSOM
/// "serialize a CSS rule / declaration block / identifier / string", CSS Syntax §9 for consecutive tokens). It is the
/// fallback for content that has no source, and it re-parses to the same structure. To reproduce the source
/// byte for byte, use <see cref="Stylesheet.ToSourceText"/> instead.
/// </summary>
public static class CssSerializer
{
    /// <summary>Serializes an identifier (CSSOM "serialize an identifier").</summary>
    /// <param name="value">The identifier's value (unescaped).</param>
    public static string SerializeIdentifier(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sb = new StringBuilder(value.Length + 2);
        AppendIdentifier(sb, value, identStart: true);
        return sb.ToString();
    }

    /// <summary>Serializes a string including its double quotes (CSSOM "serialize a string").</summary>
    /// <param name="value">The string's value (unescaped).</param>
    public static string SerializeString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sb = new StringBuilder(value.Length + 2);
        AppendString(sb, value);
        return sb.ToString();
    }

    /// <summary>Serializes a stylesheet's rules, one top-level rule per line.</summary>
    /// <param name="tree">A parse of a stylesheet.</param>
    public static string Serialize(CssTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var sb = new StringBuilder();
        WriteNodes(sb, tree, tree.Items[0].FirstChild);
        return sb.ToString();
    }

    /// <summary>Serializes a list of component values (CSS Syntax §9), collapsing whitespace and dropping comments.</summary>
    /// <param name="list">The values.</param>
    public static string Serialize(ComponentValueList list)
    {
        var sb = new StringBuilder();
        if (list.TryGetBounds(out CssTree? tree, out int from, out int to))
        {
            WriteTokens(sb, tree!, from, to);
        }

        return sb.ToString();
    }

    private struct Work
    {
        public int Item;
        public int Depth;
        public string? Text;
    }

    private static void WriteNodes(StringBuilder sb, CssTree tree, int first)
    {
        var stack = new Stack<Work>();
        PushSiblings(stack, tree, first, 0);
        bool firstOut = true;
        while (stack.Count > 0)
        {
            Work w = stack.Pop();
            if (w.Text is not null)
            {
                sb.Append(w.Text);
                continue;
            }

            ref readonly Item it = ref tree.Items[w.Item];
            if (it.Kind == CssNodeKind.Invalid)
            {
                continue;
            }

            if (w.Depth == 0)
            {
                if (!firstOut)
                {
                    sb.Append('\n');
                }

                firstOut = false;
            }
            else
            {
                AppendIndent(sb, w.Depth);
            }

            if (it.Kind == CssNodeKind.Declaration)
            {
                WriteDeclaration(sb, tree, w.Item);
                sb.Append(w.Depth == 0 ? "" : "\n");
                continue;
            }

            WriteHeader(sb, tree, w.Item);
            if ((it.Flags & Item.HasBlock) == 0)
            {
                sb.Append(w.Depth == 0 ? ";" : ";\n");
                continue;
            }

            bool hasRule = false;
            for (int c = it.FirstChild; c >= 0; c = tree.Items[c].NextSibling)
            {
                CssNodeKind ck = tree.Items[c].Kind;
                hasRule |= ck is CssNodeKind.QualifiedRule or CssNodeKind.AtRule;
            }

            string tail = w.Depth == 0 ? string.Empty : "\n";
            if (!hasRule)
            {
                sb.Append(" {");
                for (int c = it.FirstChild; c >= 0; c = tree.Items[c].NextSibling)
                {
                    if (tree.Items[c].Kind != CssNodeKind.Declaration)
                    {
                        continue;
                    }

                    sb.Append(' ');
                    WriteDeclaration(sb, tree, c);
                }

                sb.Append(" }").Append(tail);
                continue;
            }

            sb.Append(" {\n");
            stack.Push(new Work { Text = new string(' ', w.Depth * 2) + "}" + tail });
            PushSiblings(stack, tree, it.FirstChild, w.Depth + 1);
        }
    }

    // Pushes the siblings so that the first one pops first.
    private static void PushSiblings(Stack<Work> stack, CssTree tree, int first, int depth)
    {
        var items = new List<int>();
        for (int c = first; c >= 0; c = tree.Items[c].NextSibling)
        {
            items.Add(c);
        }

        for (int i = items.Count - 1; i >= 0; i--)
        {
            stack.Push(new Work { Item = items[i], Depth = depth });
        }
    }

    private static void AppendIndent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

    private static void WriteHeader(StringBuilder sb, CssTree tree, int idx)
    {
        ref readonly Item it = ref tree.Items[idx];
        if (it.Kind == CssNodeKind.AtRule)
        {
            sb.Append('@');
            AppendIdentifier(sb, AtName(tree, it.Name), identStart: true);
            ComponentValueList pre = new CssNode(tree, idx).Prelude.Trimmed();
            if (!pre.IsEmpty)
            {
                sb.Append(' ');
                WriteTokens(sb, pre);
            }
        }
        else
        {
            ComponentValueList pre = new CssNode(tree, idx).Prelude.Trimmed();
            WriteTokens(sb, pre);
        }
    }

    private static string AtName(CssTree tree, int tok) => new CssToken(tree, tok).Value;

    private static void WriteTokens(StringBuilder sb, ComponentValueList list)
    {
        if (list.TryGetBounds(out CssTree? t, out int from, out int to))
        {
            WriteTokens(sb, t!, from, to);
        }
    }

    private static void WriteDeclaration(StringBuilder sb, CssTree tree, int idx)
    {
        var node = new CssNode(tree, idx);
        string name = node.Name;
        if (node.IsCustomProperty)
        {
            AppendIdentifier(sb, name, identStart: true);
            sb.Append(':');
            ReadOnlySpan<char> raw = node.ValueText;
            if (!raw.IsEmpty)
            {
                sb.Append(' ').Append(raw);
            }
            else
            {
                sb.Append(' ');
            }
        }
        else
        {
            AppendIdentifier(sb, AsciiLower(name), identStart: true);
            sb.Append(": ");
            if (node.Value.TryGetBounds(out CssTree? t, out int from, out int to))
            {
                WriteTokens(sb, t!, from, to);
            }
        }

        if (node.IsImportant)
        {
            sb.Append(" !important");
        }

        sb.Append(';');
    }

    private static string AsciiLower(string s)
    {
        foreach (char c in s)
        {
            if (c is >= 'A' and <= 'Z')
            {
                var chars = s.ToCharArray();
                for (int i = 0; i < chars.Length; i++)
                {
                    if (chars[i] is >= 'A' and <= 'Z')
                    {
                        chars[i] = (char)(chars[i] | 0x20);
                    }
                }

                return new string(chars);
            }
        }

        return s;
    }

    // ---- tokens (CSS Syntax §9) ----

    private enum Cat
    {
        Other,
        Ident,
        Function,
        Url,
        BadUrl,
        Minus,
        Number,
        Percentage,
        Dimension,
        Cdc,
        LParen,
        Star,
        Percent,
        AtKeyword,
        Hash,
        DelimHash,
        DelimAt,
        DelimDot,
        DelimPlus,
        Slash,
    }

    private static Cat Categorize(CssTree tree, int i)
    {
        ref readonly Token t = ref tree.Tokens[i];
        switch (t.Kind)
        {
            case CssTokenKind.Ident: return Cat.Ident;
            case CssTokenKind.Function: return Cat.Function;
            case CssTokenKind.Url: return Cat.Url;
            case CssTokenKind.BadUrl: return Cat.BadUrl;
            case CssTokenKind.Number: return Cat.Number;
            case CssTokenKind.Percentage: return Cat.Percentage;
            case CssTokenKind.Dimension: return Cat.Dimension;
            case CssTokenKind.Cdc: return Cat.Cdc;
            case CssTokenKind.LeftParen: return Cat.LParen;
            case CssTokenKind.AtKeyword: return Cat.AtKeyword;
            case CssTokenKind.Hash: return Cat.Hash;
            case CssTokenKind.Delim:
                return tree.Text[t.Start] switch
                {
                    '-' => Cat.Minus,
                    '*' => Cat.Star,
                    '%' => Cat.Percent,
                    '#' => Cat.DelimHash,
                    '@' => Cat.DelimAt,
                    '.' => Cat.DelimDot,
                    '+' => Cat.DelimPlus,
                    '/' => Cat.Slash,
                    _ => Cat.Other,
                };
            default: return Cat.Other;
        }
    }

    // The table in CSS Syntax §9: pairs of tokens that would merge or change meaning without a comment between them.
    private static bool NeedsComment(Cat a, Cat b)
    {
        switch (a)
        {
            case Cat.Ident:
                return b is Cat.Ident or Cat.Function or Cat.Url or Cat.BadUrl or Cat.Minus or Cat.Number or Cat.Percentage
                    or Cat.Dimension or Cat.Cdc or Cat.LParen;
            case Cat.AtKeyword or Cat.Hash or Cat.Dimension or Cat.DelimHash or Cat.Minus:
                return b is Cat.Ident or Cat.Function or Cat.Url or Cat.BadUrl or Cat.Minus or Cat.Number or Cat.Percentage
                    or Cat.Dimension or Cat.Cdc;
            case Cat.Number:
                return b is Cat.Ident or Cat.Function or Cat.Url or Cat.BadUrl or Cat.Number or Cat.Percentage or Cat.Dimension
                    or Cat.Cdc or Cat.Percent;
            case Cat.DelimAt:
                return b is Cat.Ident or Cat.Function or Cat.Url or Cat.BadUrl or Cat.Minus or Cat.Cdc;
            case Cat.DelimDot or Cat.DelimPlus:
                return b is Cat.Number or Cat.Percentage or Cat.Dimension;
            case Cat.Slash:
                return b == Cat.Star;
            default:
                return false;
        }
    }

    private static void WriteTokens(StringBuilder sb, CssTree tree, int from, int to)
    {
        var pendingClosers = new Stack<char>();
        Cat prev = Cat.Other;
        bool havePrev = false;
        for (int i = from; i < to; i++)
        {
            ref readonly Token t = ref tree.Tokens[i];
            if (t.Kind == CssTokenKind.Whitespace)
            {
                sb.Append(' ');
                havePrev = false;
                continue;
            }

            Cat cat = Categorize(tree, i);
            if (havePrev && NeedsComment(prev, cat))
            {
                sb.Append("/**/");
            }

            WriteToken(sb, tree, i);
            prev = cat;
            havePrev = true;
            switch (t.Kind)
            {
                case CssTokenKind.Function or CssTokenKind.LeftParen:
                    if (tree.Match[i] < 0 || tree.Match[i] >= to)
                    {
                        pendingClosers.Push(')');
                    }

                    break;
                case CssTokenKind.LeftBracket:
                    if (tree.Match[i] < 0 || tree.Match[i] >= to)
                    {
                        pendingClosers.Push(']');
                    }

                    break;
                case CssTokenKind.LeftBrace:
                    if (tree.Match[i] < 0 || tree.Match[i] >= to)
                    {
                        pendingClosers.Push('}');
                    }

                    break;
            }
        }

        while (pendingClosers.Count > 0)
        {
            sb.Append(pendingClosers.Pop());
        }
    }

    private static void WriteToken(StringBuilder sb, CssTree tree, int i)
    {
        var tok = new CssToken(tree, i);
        ref readonly Token t = ref tree.Tokens[i];
        switch (t.Kind)
        {
            case CssTokenKind.Ident:
                AppendIdentifier(sb, tok.Value, identStart: true);
                break;
            case CssTokenKind.Function:
                AppendIdentifier(sb, tok.Value, identStart: true);
                sb.Append('(');
                break;
            case CssTokenKind.AtKeyword:
                sb.Append('@');
                AppendIdentifier(sb, tok.Value, identStart: true);
                break;
            case CssTokenKind.Hash:
                sb.Append('#');
                AppendIdentifier(sb, tok.Value, identStart: tok.IsHashId);
                break;
            case CssTokenKind.QuotedString:
                AppendString(sb, tok.Value);
                break;
            case CssTokenKind.Url:
                // Kept as a url token when the value can be written unquoted (so the token kinds survive a reparse);
                // otherwise the quoted function form, which is how CSSOM writes urls.
                string url = tok.Value;
                sb.Append("url(");
                if (IsSafeUnquotedUrl(url))
                {
                    sb.Append(url);
                }
                else
                {
                    AppendString(sb, url);
                }

                sb.Append(')');
                break;
            case CssTokenKind.Delim:
                string d = tok.Value;
                sb.Append(d);
                if (d == "\\")
                {
                    sb.Append('\n');
                }

                break;
            case CssTokenKind.Dimension:
                sb.Append(tree.Text, t.Start, t.Aux - t.Start);
                string unit = tok.Value;
                if (unit.Length > 1 && (unit[0] | 0x20) == 'e'
                    && (CssChars.IsDigit(unit[1]) || (unit[1] == '-' && unit.Length > 2 && CssChars.IsDigit(unit[2]))))
                {
                    // "1e3" would read as one number; escape the first letter.
                    sb.Append('\\').Append(((int)unit[0]).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
                    AppendIdentifier(sb, unit[1..], identStart: false);
                }
                else
                {
                    AppendIdentifier(sb, unit, identStart: false);
                }

                break;
            case CssTokenKind.Number:
            case CssTokenKind.UnicodeRange:
            case CssTokenKind.BadString:
            case CssTokenKind.BadUrl:
                sb.Append(tree.Text, t.Start, t.End - t.Start);
                break;
            case CssTokenKind.Percentage:
                sb.Append(tree.Text, t.Start, t.Aux - t.Start).Append('%');
                break;
            default:
                sb.Append(tree.Text, t.Start, t.End - t.Start);
                break;
        }
    }

    private static bool IsSafeUnquotedUrl(string v)
    {
        foreach (char c in v)
        {
            if (CssChars.IsWhitespace(c) || c is '"' or '\'' or '(' or ')' or '\\' || CssChars.IsNonPrintable(c) || c == '\0' || char.IsSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }

    // CSSOM "serialize an identifier". With identStart false, the first character is escaped only as in the middle of
    // an identifier (used for hash names and dimension units).
    private static void AppendIdentifier(StringBuilder sb, string value, bool identStart)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\0')
            {
                sb.Append('\uFFFD');
            }
            else if ((c >= '\u0001' && c <= '\u001F') || c == '\u007F')
            {
                AppendHexEscape(sb, c);
            }
            else if (identStart && i == 0 && c is >= '0' and <= '9')
            {
                AppendHexEscape(sb, c);
            }
            else if (identStart && i == 1 && c is >= '0' and <= '9' && value[0] == '-')
            {
                AppendHexEscape(sb, c);
            }
            else if (identStart && i == 0 && c == '-' && value.Length == 1)
            {
                sb.Append('\\').Append(c);
            }
            else if (c >= '\u0080' && !CssChars.IsNonAsciiIdent(c))
            {
                // CSSOM leaves every character from U+0080 up as is, but CSS Syntax 4.2 does not list the C1 controls and
                // some others as ident code points, so a bare one would not read back as part of the identifier.
                AppendHexEscape(sb, c);
            }
            else if (c >= '\u0080' || c is '-' or '_' || c is >= '0' and <= '9' || c is >= 'A' and <= 'Z' || c is >= 'a' and <= 'z')
            {
                if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    sb.Append(c).Append(value[++i]);
                }
                else if (char.IsSurrogate(c))
                {
                    sb.Append('\uFFFD');
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                sb.Append('\\').Append(c);
            }
        }
    }

    private static void AppendHexEscape(StringBuilder sb, char c) =>
        sb.Append('\\').Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(' ');

    // CSSOM "serialize a string".
    private static void AppendString(StringBuilder sb, string value)
    {
        sb.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\0')
            {
                sb.Append('\uFFFD');
            }
            else if ((c >= '\u0001' && c <= '\u001F') || c == '\u007F')
            {
                AppendHexEscape(sb, c);
            }
            else if (c is '"' or '\\')
            {
                sb.Append('\\').Append(c);
            }
            else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                sb.Append(c).Append(value[++i]);
            }
            else if (char.IsSurrogate(c))
            {
                sb.Append('\uFFFD');
            }
            else
            {
                sb.Append(c);
            }
        }

        sb.Append('"');
    }
}
