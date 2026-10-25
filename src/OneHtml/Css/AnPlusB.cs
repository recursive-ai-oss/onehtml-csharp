using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Css;

/// <summary>
/// The <c>&lt;an+b&gt;</c> microsyntax of CSS Syntax §6: the pair (A, B) in <c>An+B</c>. Selectors use it for
/// <c>:nth-child()</c> and friends; it lives with the CSS syntax code because it is defined over tokens.
/// </summary>
/// <param name="A">The step.</param>
/// <param name="B">The offset.</param>
public readonly record struct AnPlusB(int A, int B)
{
    /// <summary>True when the element at 1-based <paramref name="position"/> is matched: some integer n ≥ 0 gives <c>A·n + B == position</c>.</summary>
    /// <param name="position">A 1-based index.</param>
    public bool Matches(long position)
    {
        long d = position - B;
        if (A == 0)
        {
            return d == 0;
        }

        return d % A == 0 && d / A >= 0;
    }

    /// <summary>Parses the whole of <paramref name="text"/> as an <c>&lt;an+b&gt;</c>; whitespace and comments around it are ignored.</summary>
    /// <param name="text">The text, for example <c>2n+1</c> or <c>odd</c>.</param>
    /// <param name="result">The parsed pair.</param>
    public static bool TryParse(string text, out AnPlusB result)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryParse(CssTree.ParseComponentValueList(text).Values, out result);
    }

    /// <summary>Parses a component value list as an <c>&lt;an+b&gt;</c> (CSS Syntax §6.2).</summary>
    /// <param name="list">The values; the whole list must be one <c>&lt;an+b&gt;</c>.</param>
    /// <param name="result">The parsed pair.</param>
    public static bool TryParse(ComponentValueList list, out AnPlusB result)
    {
        result = default;
        TokenRange r = TokenRange.Of(list);
        // Collect significant tokens with whether whitespace came before each.
        var toks = new List<(int Index, bool Space)>();
        bool space = false;
        for (int i = r.From; i < r.To; i++)
        {
            if (r.Kind(i) == CssTokenKind.Whitespace)
            {
                space = true;
                continue;
            }

            CssTokenKind k = r.Kind(i);
            if (k is CssTokenKind.Function or CssTokenKind.LeftParen or CssTokenKind.LeftBracket or CssTokenKind.LeftBrace)
            {
                return false;
            }

            toks.Add((i, space));
            space = false;
            if (toks.Count > 6)
            {
                return false;
            }
        }

        if (toks.Count == 0)
        {
            return false;
        }

        int p = 0;
        (int idx, _) = toks[0];
        CssTokenKind kind = r.Kind(idx);
        long a;
        long b = 0;
        bool needTail = false; // an "n" form that may take a B part
        bool dashForm = false; // ends with "n-": B must follow as a signless integer

        if (kind == CssTokenKind.Ident)
        {
            string name = r.IdentString(idx).AsciiLower();
            if (name == "odd")
            {
                result = new AnPlusB(2, 1);
                return toks.Count == 1;
            }

            if (name == "even")
            {
                result = new AnPlusB(2, 0);
                return toks.Count == 1;
            }

            if (!ParseNIdent(name, allowMinus: true, out a, out long digits, out dashForm, out needTail))
            {
                return false;
            }

            if (digits != long.MinValue)
            {
                b = digits;
            }

            p = 1;
        }
        else if (kind == CssTokenKind.Delim && r.IsDelim(idx, '+'))
        {
            if (toks.Count < 2 || r.Kind(toks[1].Index) != CssTokenKind.Ident || toks[1].Space || r.Tree.Tokens[idx].End != r.Tree.Tokens[toks[1].Index].Start)
            {
                return false;
            }

            string name = r.IdentString(toks[1].Index).AsciiLower();
            if (!ParseNIdent(name, allowMinus: false, out a, out long digits, out dashForm, out needTail))
            {
                return false;
            }

            if (digits != long.MinValue)
            {
                b = digits;
            }

            p = 2;
        }
        else if (kind == CssTokenKind.Number)
        {
            if (!r.IsInteger(idx) || toks.Count != 1)
            {
                return false;
            }

            result = Clamp(0, (long)r.Number(idx));
            return true;
        }
        else if (kind == CssTokenKind.Dimension)
        {
            if (!r.IsInteger(idx))
            {
                return false;
            }

            string unit = r.UnitText(idx).ToString().AsciiLower();
            if (unit.Contains('\\', StringComparison.Ordinal))
            {
                unit = CssChars.Decode(r.UnitText(idx)).AsciiLower();
            }

            if (!ParseNIdent(unit, allowMinus: false, out _, out long digits, out dashForm, out needTail))
            {
                return false;
            }

            a = (long)r.Number(idx);
            if (digits != long.MinValue)
            {
                b = digits;
            }

            p = 1;
        }
        else
        {
            return false;
        }

        if (dashForm)
        {
            // "n-" followed by a signless integer
            if (toks.Count != p + 1 || !IsSignlessInteger(r, toks[p].Index))
            {
                return false;
            }

            result = Clamp(a, -(long)r.Number(toks[p].Index));
            return true;
        }

        if (p == toks.Count)
        {
            result = Clamp(a, b);
            return true;
        }

        if (!needTail || b != 0)
        {
            return false;
        }

        // signed integer: "n +5", "n -5"
        int t1 = toks[p].Index;
        if (r.Kind(t1) == CssTokenKind.Number && r.IsInteger(t1) && r.Tree.Text[r.Tree.Tokens[t1].Start] is '+' or '-' && toks.Count == p + 1)
        {
            result = Clamp(a, (long)r.Number(t1));
            return true;
        }

        // "n + 5", "n - 5"
        if ((r.IsDelim(t1, '+') || r.IsDelim(t1, '-')) && toks.Count == p + 2 && IsSignlessInteger(r, toks[p + 1].Index))
        {
            long v = (long)r.Number(toks[p + 1].Index);
            result = Clamp(a, r.IsDelim(t1, '-') ? -v : v);
            return true;
        }

        return false;
    }

    private static bool IsSignlessInteger(TokenRange r, int i) =>
        r.Kind(i) == CssTokenKind.Number && r.IsInteger(i) && r.Tree.Text[r.Tree.Tokens[i].Start] is not ('+' or '-');

    // Parses the identifier (or dimension unit) forms n, -n, n-, -n-, n-<digits>, -n-<digits>. For a dimension the
    // leading minus is not allowed because the number carries the sign.
    private static bool ParseNIdent(string s, bool allowMinus, out long a, out long digits, out bool dashForm, out bool needTail)
    {
        a = 1;
        digits = long.MinValue;
        dashForm = false;
        needTail = false;
        int i = 0;
        if (s.StartsWith('-'))
        {
            if (!allowMinus)
            {
                return false;
            }

            a = -1;
            i = 1;
        }

        if (i >= s.Length || s[i] != 'n')
        {
            return false;
        }

        i++;
        if (i == s.Length)
        {
            needTail = true;
            return true;
        }

        if (s[i] != '-')
        {
            return false;
        }

        i++;
        if (i == s.Length)
        {
            dashForm = true;
            return true;
        }

        long v = 0;
        for (; i < s.Length; i++)
        {
            if (s[i] is < '0' or > '9')
            {
                return false;
            }

            v = Math.Min((v * 10) + (s[i] - '0'), 1L << 40);
        }

        digits = -v;
        return true;
    }

    private static AnPlusB Clamp(long a, long b) =>
        new((int)Math.Clamp(a, int.MinValue, int.MaxValue), (int)Math.Clamp(b, int.MinValue, int.MaxValue));
}
