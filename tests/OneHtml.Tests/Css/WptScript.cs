using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RecursiveAI.OneHtml.Tests.Css;

/// <summary>Raised when a web-platform-tests call cannot be run here (it needs script, a DOM, layout or a browser).</summary>
internal sealed class WptNotApplicableException(string reason) : Exception(reason);

/// <summary>An argument expression of a call in a test file: a literal, a parameter, a row cell or an object.</summary>
internal sealed class JsExpr
{
    public List<object> Parts { get; } = [];

    public bool Undefined { get; init; }

    public bool Unknown { get; init; }

    public string Raw { get; init; } = string.Empty;

    public Dictionary<string, JsExpr>? Object { get; init; }
}

/// <summary>A cell of the <c>tests</c> table or a reference to one.</summary>
internal readonly record struct JsRowRef(int Index);

/// <summary>A reference to a parameter or variable by name.</summary>
internal readonly record struct JsVarRef(string Name);

/// <summary>One call found in script text.</summary>
internal sealed record JsCall(string Name, List<JsExpr> Args, int Position);

/// <summary>A <c>function</c> definition found in script text.</summary>
internal sealed record JsFunction(string Name, List<string> Parameters, int BodyStart, int BodyEnd);

/// <summary>
/// A small reader for the literal-argument calls that the web-platform-tests parsing files make. It understands
/// string, template and number literals, <c>undefined</c>, object literals, parameters of simple wrapper functions
/// and the <c>tests</c> table loops; anything else is reported as not applicable instead of being guessed.
/// </summary>
internal static class JsScript
{
    private static readonly Regex ScriptBlocks = new(@"<script(?<attrs>[^>]*)>(?<body>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Number = new(@"^[+-]?(\d+\.?\d*|\.\d+)(e[+-]?\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Identifier = new(@"^[A-Za-z_$][\w$]*$", RegexOptions.CultureInvariant);

    private static readonly Regex FunctionHeader = new(@"\bfunction\s+(?<name>[\w$]+)\s*\((?<params>[^)]*)\)\s*\{", RegexOptions.CultureInvariant);

    /// <summary>The inline script of an HTML test file with comments removed.</summary>
    public static string ExtractScript(string html)
    {
        var sb = new StringBuilder();
        foreach (Match m in ScriptBlocks.Matches(html))
        {
            if (!m.Groups["attrs"].Value.Contains("src=", StringComparison.Ordinal))
            {
                sb.Append(StripComments(m.Groups["body"].Value)).Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>Removes <c>//</c> and <c>/* */</c> comments outside string literals.</summary>
    public static string StripComments(string s)
    {
        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c is '\'' or '"' or '`')
            {
                int e = SkipString(s, i);
                sb.Append(s, i, e - i);
                i = e;
            }
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                sb.Append(' ');
                i = e < 0 ? s.Length : e + 2;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    /// <summary>The index just past the string literal that starts at <paramref name="i"/>.</summary>
    public static int SkipString(string s, int i)
    {
        char q = s[i++];
        while (i < s.Length && s[i] != q)
        {
            i += s[i] == '\\' ? 2 : 1;
        }

        return Math.Min(i + 1, s.Length);
    }

    /// <summary>The index of the bracket that closes the one at <paramref name="open"/>, or -1.</summary>
    public static int MatchClose(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            char c = s[i];
            if (c is '\'' or '"' or '`')
            {
                i = SkipString(s, i) - 1;
            }
            else if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>Splits the text between two brackets at its top-level commas.</summary>
    public static List<string> SplitArgs(string s, int from, int to)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = from;
        for (int i = from; i < to; i++)
        {
            char c = s[i];
            if (c is '\'' or '"' or '`')
            {
                i = SkipString(s, i) - 1;
            }
            else if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                parts.Add(s.Substring(start, i - start));
                start = i + 1;
            }
        }

        string last = s.Substring(start, to - start);
        if (last.Trim().Length > 0 || parts.Count > 0)
        {
            parts.Add(last);
        }

        return parts;
    }

    /// <summary>Finds every <c>function name(params) {</c> in the text.</summary>
    public static List<JsFunction> FindFunctions(string s)
    {
        var list = new List<JsFunction>();
        foreach (Match m in FunctionHeader.Matches(s))
        {
            int open = m.Index + m.Length - 1;
            int close = MatchClose(s, open);
            if (close < 0)
            {
                continue;
            }

            var ps = new List<string>();
            foreach (string p in m.Groups["params"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                ps.Add(p);
            }

            list.Add(new JsFunction(m.Groups["name"].Value, ps, open + 1, close));
        }

        return list;
    }

    /// <summary>Finds the calls to the named functions, in order, not descending into a call's arguments.</summary>
    public static List<JsCall> FindCalls(string s, int from, int to, Func<string, bool> isKnown)
    {
        var calls = new List<JsCall>();
        int i = from;
        while (i < to)
        {
            char c = s[i];
            if (c is '\'' or '"' or '`')
            {
                i = SkipString(s, i);
                continue;
            }

            if (!(char.IsAsciiLetter(c) || c is '_' or '$'))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < to && (char.IsAsciiLetterOrDigit(s[i]) || s[i] is '_' or '$'))
            {
                i++;
            }

            string name = s[start..i];
            int p = i;
            while (p < to && char.IsWhiteSpace(s[p]))
            {
                p++;
            }

            bool isCall = p < to && s[p] == '(' && (start == 0 || s[start - 1] != '.');
            if (!isCall || !isKnown(name) || IsFunctionHeader(s, start))
            {
                continue;
            }

            int close = MatchClose(s, p);
            if (close < 0 || close > to)
            {
                break;
            }

            var args = new List<JsExpr>();
            foreach (string a in SplitArgs(s, p + 1, close))
            {
                args.Add(ParseExpr(a));
            }

            calls.Add(new JsCall(name, args, start));
            i = close + 1;
        }

        return calls;
    }

    private static bool IsFunctionHeader(string s, int nameStart)
    {
        int k = nameStart - 1;
        while (k >= 0 && char.IsWhiteSpace(s[k]))
        {
            k--;
        }

        return k >= 7 && string.CompareOrdinal(s, k - 7, "function", 0, 8) == 0;
    }

    /// <summary>Parses one argument expression.</summary>
    public static JsExpr ParseExpr(string text)
    {
        string t = text.Trim();
        if (t == "undefined")
        {
            return new JsExpr { Undefined = true, Raw = t };
        }

        if (t.Length >= 2 && t[0] is '\'' or '"' or '`')
        {
            if (SkipString(t, 0) == t.Length)
            {
                return ParseString(t);
            }

            return new JsExpr { Unknown = true, Raw = t };
        }

        if (Number.IsMatch(t))
        {
            var e = new JsExpr { Raw = t };
            e.Parts.Add(t);
            return e;
        }

        if (Identifier.IsMatch(t))
        {
            var e = new JsExpr { Raw = t };
            e.Parts.Add(new JsVarRef(t));
            return e;
        }

        Match row = Regex.Match(t, @"^test\[(\d+)\]$", RegexOptions.CultureInvariant);
        if (row.Success)
        {
            var e = new JsExpr { Raw = t };
            e.Parts.Add(new JsRowRef(int.Parse(row.Groups[1].Value, CultureInfo.InvariantCulture)));
            return e;
        }

        if (t.Length >= 2 && t[0] == '{' && t[^1] == '}')
        {
            var dict = new Dictionary<string, JsExpr>(StringComparer.Ordinal);
            foreach (string field in SplitArgs(t, 1, t.Length - 1))
            {
                string f = field.Trim();
                int colon = f.IndexOf(':', StringComparison.Ordinal);
                if (colon < 0)
                {
                    if (Identifier.IsMatch(f))
                    {
                        dict[f] = ParseExpr(f);
                    }

                    continue;
                }

                string key = f[..colon].Trim().Trim('\'', '"');
                dict[key] = ParseExpr(f[(colon + 1)..]);
            }

            return new JsExpr { Object = dict, Raw = t };
        }

        return new JsExpr { Unknown = true, Raw = t };
    }

    private static JsExpr ParseString(string t)
    {
        var e = new JsExpr { Raw = t };
        char q = t[0];
        var sb = new StringBuilder();
        int i = 1;
        while (i < t.Length - 1)
        {
            char c = t[i];
            if (c == '\\' && i + 1 < t.Length - 1)
            {
                char n = t[i + 1];
                i += 2;
                switch (n)
                {
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    case 'u' when i + 4 <= t.Length && int.TryParse(t.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp):
                        sb.Append((char)cp);
                        i += 4;
                        break;
                    default:
                        sb.Append(n);
                        break;
                }

                continue;
            }

            if (q == '`' && c == '$' && i + 1 < t.Length - 1 && t[i + 1] == '{')
            {
                int close = MatchClose(t, i + 1);
                if (close < 0)
                {
                    return new JsExpr { Unknown = true, Raw = t };
                }

                string inner = t.Substring(i + 2, close - i - 2).Trim();
                JsExpr sub = ParseExpr(inner);
                if (sub.Unknown || sub.Undefined || sub.Parts.Count != 1 || sub.Parts[0] is string)
                {
                    if (sub.Parts.Count == 1 && sub.Parts[0] is string)
                    {
                        sb.Append((string)sub.Parts[0]);
                        i = close + 1;
                        continue;
                    }

                    return new JsExpr { Unknown = true, Raw = t };
                }

                if (sb.Length > 0)
                {
                    e.Parts.Add(sb.ToString());
                    sb.Clear();
                }

                e.Parts.Add(sub.Parts[0]);
                i = close + 1;
                continue;
            }

            sb.Append(c);
            i++;
        }

        if (sb.Length > 0 || e.Parts.Count == 0)
        {
            e.Parts.Add(sb.ToString());
        }

        return e;
    }
}
