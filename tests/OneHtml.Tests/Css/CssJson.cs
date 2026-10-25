using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Tests.Css;

/// <summary>Converts parse results to the JSON shapes used by the css-parsing-tests suite, and compares them.</summary>
internal static class CssJson
{
    public static string TestDataPath(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, "testdata", "css", .. parts]);

    public static JsonArray LoadCases(string relative)
    {
        string json = File.ReadAllText(TestDataPath(relative));
        return JsonNode.Parse(json)!.AsArray();
    }

    public static JsonNode Value(ComponentValue v)
    {
        CssToken t = v.Token;
        switch (v.Kind)
        {
            case ComponentValueKind.Function:
                var f = new JsonArray { "function", t.Value };
                AddValues(f, v.Children);

                return f;
            case ComponentValueKind.SimpleBlock:
                var b = new JsonArray { t.Kind switch { CssTokenKind.LeftBrace => "{}", CssTokenKind.LeftBracket => "[]", _ => "()" } };
                AddValues(b, v.Children);

                return b;
        }

        string repr = t.Raw.ToString();
        switch (t.Kind)
        {
            case CssTokenKind.Whitespace: return " ";
            case CssTokenKind.Ident: return new JsonArray { "ident", t.Value };
            case CssTokenKind.AtKeyword: return new JsonArray { "at-keyword", t.Value };
            case CssTokenKind.Hash: return new JsonArray { "hash", t.Value, t.IsHashId ? "id" : "unrestricted" };
            case CssTokenKind.QuotedString: return new JsonArray { "string", t.Value };
            case CssTokenKind.BadString: return new JsonArray { "error", "bad-string" };
            case CssTokenKind.Url: return new JsonArray { "url", t.Value };
            case CssTokenKind.BadUrl: return new JsonArray { "error", "bad-url" };
            case CssTokenKind.Delim: return t.Value;
            case CssTokenKind.Number: return new JsonArray { "number", NumberRepr(t), t.NumberValue, t.IsInteger ? "integer" : "number" };
            case CssTokenKind.Percentage: return new JsonArray { "percentage", NumberRepr(t), t.NumberValue, t.IsInteger ? "integer" : "number" };
            case CssTokenKind.Dimension: return new JsonArray { "dimension", NumberRepr(t), t.NumberValue, t.IsInteger ? "integer" : "number", t.Value };
            case CssTokenKind.UnicodeRange:
                t.GetUnicodeRange(out int s, out int e);
                return new JsonArray { "unicode-range", s, e };
            case CssTokenKind.Cdo: return "<!--";
            case CssTokenKind.Cdc: return "-->";
            case CssTokenKind.Colon: return ":";
            case CssTokenKind.Semicolon: return ";";
            case CssTokenKind.Comma: return ",";
            case CssTokenKind.RightBrace: return new JsonArray { "error", "}" };
            case CssTokenKind.RightBracket: return new JsonArray { "error", "]" };
            case CssTokenKind.RightParen: return new JsonArray { "error", ")" };
            default: throw new InvalidOperationException(repr);
        }
    }

    private static string NumberRepr(CssToken t) => t.NumberText.ToString();

    // The suite lists a parse error after a string or url that ran into the end of input.
    private static void AddValues(JsonArray into, ComponentValueList list)
    {
        foreach (ComponentValue v in list)
        {
            into.Add(Value(v));
            if (v.Kind == ComponentValueKind.Token && v.Token.Kind is CssTokenKind.QuotedString or CssTokenKind.Url && !v.Token.IsTerminated)
            {
                into.Add(new JsonArray { "error", v.Token.Kind == CssTokenKind.Url ? "eof-in-url" : "eof-in-string" });
            }
        }
    }

    public static JsonArray Values(ComponentValueList list)
    {
        var a = new JsonArray();
        AddValues(a, list);
        return a;
    }

    /// <summary>The 2021 suite's view of a block: the raw component values between the braces.</summary>
    public static JsonNode Node(CssNode n)
    {
        switch (n.Kind)
        {
            case CssNodeKind.AtRule:
                return new JsonArray { "at-rule", n.Name, Values(n.Prelude), n.HasBlock ? Values(n.BlockValues) : null };
            case CssNodeKind.QualifiedRule:
                return new JsonArray { "qualified rule", Values(n.Prelude), Values(n.BlockValues) };
            case CssNodeKind.Declaration:
                return new JsonArray { "declaration", n.Name, Values(n.Value), n.IsImportant };
            default:
                return new JsonArray { "error", "invalid" };
        }
    }

    /// <summary>A block's contents the way blocks_contents.json lists them: nodes, declarations and rules in order.</summary>
    public static JsonNode NodeWithContents(CssNode n)
    {
        switch (n.Kind)
        {
            case CssNodeKind.AtRule:
                return new JsonArray { "at-rule", n.Name, Values(n.Prelude), n.HasBlock ? Nodes(n.Children) : null };
            case CssNodeKind.QualifiedRule:
                return new JsonArray { "qualified rule", Values(n.Prelude), Nodes(n.Children) };
            default:
                return Node(n);
        }
    }

    public static JsonArray Nodes(CssNodeList list)
    {
        var a = new JsonArray();
        foreach (CssNode n in list)
        {
            a.Add(NodeWithContents(n));
        }

        return a;
    }

    public static bool Equal(JsonNode? a, JsonNode? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        JsonElement x = JsonSerializer.SerializeToElement(a);
        JsonElement y = JsonSerializer.SerializeToElement(b);
        return Equal(x, y);
    }

    private static bool Equal(JsonElement x, JsonElement y)
    {
        if (x.ValueKind != y.ValueKind)
        {
            return false;
        }

        switch (x.ValueKind)
        {
            case JsonValueKind.Array:
                if (x.GetArrayLength() != y.GetArrayLength())
                {
                    return false;
                }

                for (int i = 0; i < x.GetArrayLength(); i++)
                {
                    if (!Equal(x[i], y[i]))
                    {
                        return false;
                    }
                }

                return true;
            case JsonValueKind.Number:
                return x.GetDouble().Equals(y.GetDouble());
            case JsonValueKind.String:
                return x.GetString() == y.GetString();
            default:
                return true;
        }
    }
}
