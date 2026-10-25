using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneHtml.Tests.Selectors;

/// <summary>A JavaScript identifier or other expression the reader does not evaluate; kept as source text.</summary>
internal sealed record JsExpr(string Text);

/// <summary>The value <c>undefined</c>.</summary>
internal sealed class JsUndefined
{
    public static JsUndefined Instance { get; } = new();
}

/// <summary>
/// Reads the literal subset of JavaScript that the vendored web-platform tests use for their data: strings, numbers,
/// booleans, arrays, objects, comments, and calls split into statements. It evaluates nothing; whatever it cannot read as
/// a literal comes back as a <see cref="JsExpr"/> with its source text.
/// </summary>
internal sealed class JsReader
{
    private readonly string _s;
    private int _i;

    public JsReader(string source, int start = 0)
    {
        _s = source;
        _i = start;
    }

    public int Position => _i;

    public bool AtEnd
    {
        get
        {
            SkipTrivia();
            return _i >= _s.Length;
        }
    }

    public void SkipTrivia()
    {
        while (_i < _s.Length)
        {
            char c = _s[_i];
            if (char.IsWhiteSpace(c))
            {
                _i++;
            }
            else if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
            {
                while (_i < _s.Length && _s[_i] != '\n')
                {
                    _i++;
                }
            }
            else if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
            {
                int end = _s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                _i = end < 0 ? _s.Length : end + 2;
            }
            else
            {
                break;
            }
        }
    }

    public char Peek()
    {
        SkipTrivia();
        return _i < _s.Length ? _s[_i] : '\0';
    }

    public bool TryConsume(char c)
    {
        if (Peek() == c)
        {
            _i++;
            return true;
        }

        return false;
    }

    /// <summary>Reads one value: a literal, or an expression kept as text up to the next top-level comma, closing bracket or semicolon.</summary>
    public object? ReadValue()
    {
        char c = Peek();
        object? v;
        switch (c)
        {
            case '"':
            case '\'':
                v = ReadString();
                break;
            case '[':
                v = ReadArray();
                break;
            case '{':
                v = ReadObject();
                break;
            default:
                v = ReadScalarOrExpr();
                break;
        }

        // A binary + joins string pieces ("a" + "b"); anything else makes the whole thing an expression.
        while (Peek() == '+' && v is string left)
        {
            int save = _i;
            _i++;
            if (Peek() is '"' or '\'')
            {
                v = left + ReadString();
            }
            else
            {
                _i = save;
                break;
            }
        }

        return v;
    }

    private string ReadString()
    {
        char quote = _s[_i++];
        var sb = new StringBuilder();
        while (_i < _s.Length && _s[_i] != quote)
        {
            char c = _s[_i++];
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            char e = _s[_i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'f': sb.Append('\f'); break;
                case 'b': sb.Append('\b'); break;
                case '0': sb.Append('\0'); break;
                case 'u':
                    if (_s[_i] == '{')
                    {
                        int close = _s.IndexOf('}', _i);
                        sb.Append(char.ConvertFromUtf32(int.Parse(_s.AsSpan(_i + 1, close - _i - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                        _i = close + 1;
                    }
                    else
                    {
                        sb.Append((char)int.Parse(_s.AsSpan(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _i += 4;
                    }

                    break;
                case 'x':
                    sb.Append((char)int.Parse(_s.AsSpan(_i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    _i += 2;
                    break;
                case '\n':
                    break;
                default: sb.Append(e); break;
            }
        }

        _i++;
        return sb.ToString();
    }

    private List<object?> ReadArray()
    {
        _i++;
        var list = new List<object?>();
        while (Peek() != ']')
        {
            list.Add(ReadValue());
            if (!TryConsume(','))
            {
                break;
            }
        }

        TryConsume(']');
        return list;
    }

    private Dictionary<string, object?> ReadObject()
    {
        _i++;
        var d = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (Peek() != '}')
        {
            char c = Peek();
            string key;
            if (c is '"' or '\'')
            {
                key = ReadString();
            }
            else
            {
                int st = _i;
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] is '_' or '$'))
                {
                    _i++;
                }

                key = _s[st.._i];
            }

            if (!TryConsume(':'))
            {
                break;
            }

            d[key] = ReadValue();
            if (!TryConsume(','))
            {
                break;
            }
        }

        TryConsume('}');
        return d;
    }

    private object? ReadScalarOrExpr()
    {
        SkipTrivia();
        int start = _i;
        int depth = 0;
        while (_i < _s.Length)
        {
            char c = _s[_i];
            if (c is '"' or '\'')
            {
                ReadString();
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0)
                {
                    break;
                }

                depth--;
            }
            else if (depth == 0 && c is ',' or ';')
            {
                break;
            }
            else if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] is '/' or '*')
            {
                SkipTrivia();
                continue;
            }

            _i++;
        }

        string text = _s[start.._i].Trim();
        if (text.Length == 0)
        {
            return new JsExpr(string.Empty);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
        {
            return d;
        }

        if (text.StartsWith("0x", StringComparison.Ordinal) && int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int h))
        {
            return (double)h;
        }

        return text switch
        {
            "true" => true,
            "false" => false,
            "null" => null,
            "undefined" => JsUndefined.Instance,
            _ => new JsExpr(text),
        };
    }

    /// <summary>The array literal assigned to <c>var name = [...]</c>, or null.</summary>
    public static List<object?>? ReadVarArray(string source, string name)
    {
        int at = source.IndexOf("var " + name + " =", StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var r = new JsReader(source, at + ("var " + name + " =").Length);
        return r.ReadValue() as List<object?>;
    }

    /// <summary>Splits a script into top-level statements (text), skipping comments. Function declarations end at their closing brace.</summary>
    public static List<string> Statements(string js)
    {
        var result = new List<string>();
        var r = new JsReader(js);
        while (!r.AtEnd)
        {
            int start = r.Position;
            int depth = 0;
            bool isFunction = js.AsSpan(start).StartsWith("function") || js.AsSpan(start).StartsWith("async function");
            while (r._i < js.Length)
            {
                char c = js[r._i];
                if (c is '"' or '\'' or '`')
                {
                    if (c == '`')
                    {
                        int end = js.IndexOf('`', r._i + 1);
                        r._i = end < 0 ? js.Length : end + 1;
                    }
                    else
                    {
                        r.ReadString();
                    }

                    continue;
                }

                if (c == '/' && r._i + 1 < js.Length && js[r._i + 1] is '/' or '*')
                {
                    r.SkipTrivia();
                    continue;
                }

                if (c is '(' or '[' or '{')
                {
                    depth++;
                }
                else if (c is ')' or ']' or '}')
                {
                    depth--;
                    if (depth == 0 && c == '}' && isFunction)
                    {
                        r._i++;
                        break;
                    }
                }
                else if (c == ';' && depth == 0)
                {
                    break;
                }

                r._i++;
            }

            string text = js[start..Math.Min(r._i, js.Length)].Trim();
            if (r._i < js.Length && js[r._i] == ';')
            {
                r._i++;
            }

            if (text.Length > 0)
            {
                result.Add(text);
            }
        }

        return result;
    }

    /// <summary>Reads <c>name(args...)</c> from a whole statement; null when the statement is not a plain call.</summary>
    public static (string Name, List<object?> Args)? ReadCall(string statement)
    {
        int open = statement.IndexOf('(', StringComparison.Ordinal);
        if (open <= 0)
        {
            return null;
        }

        string name = statement[..open].Trim();
        foreach (char c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '$' or '.'))
            {
                return null;
            }
        }

        var r = new JsReader(statement, open + 1);
        var args = new List<object?>();
        while (r.Peek() != ')' && !r.AtEnd)
        {
            args.Add(r.ReadValue());
            if (!r.TryConsume(','))
            {
                break;
            }
        }

        if (!r.TryConsume(')'))
        {
            return null;
        }

        return r.AtEnd ? (name, args) : null;
    }

    /// <summary>The inline <c>&lt;script&gt;</c> bodies of an HTML test file (those without a <c>src</c>).</summary>
    public static List<string> InlineScripts(string html)
    {
        var list = new List<string>();
        int i = 0;
        while (true)
        {
            int open = html.IndexOf("<script", i, StringComparison.OrdinalIgnoreCase);
            if (open < 0)
            {
                break;
            }

            int gt = html.IndexOf('>', open);
            int close = html.IndexOf("</script>", gt, StringComparison.OrdinalIgnoreCase);
            if (gt < 0 || close < 0)
            {
                break;
            }

            string tag = html[open..gt];
            if (!tag.Contains("src=", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(html[(gt + 1)..close]);
            }

            i = close + 9;
        }

        return list;
    }
}
