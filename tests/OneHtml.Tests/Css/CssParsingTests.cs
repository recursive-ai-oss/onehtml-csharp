using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using RecursiveAI.OneHtml.Css;
using Xunit;
using Xunit.Sdk;

namespace RecursiveAI.OneHtml.Tests.Css;

/// <summary>
/// Runs the css-parsing-tests suite (the implementation-independent CSS Syntax suite) against the parser. The suite
/// follows the 2021 draft; cases whose expectation conflicts with the current Editor's Draft are listed in
/// <see cref="Deviations"/> with the sentence of the current specification that decides them.
/// </summary>
public class CssParsingTests
{
    // Key: "file#index". Value: the sentence of the current CSS Syntax that makes the 2021 expectation obsolete.
    private const string NonAscii = "CSS Syntax 4.2 'non-ASCII ident code point': a code point whose value is any of U+00B7, between U+00C0 and U+00D6, ...; U+0080-U+00B6 are not ident code points, so they tokenize as delim tokens.";
    private const string NoUrange = "CSS Syntax 4.3.1 'consume a token': a unicode-range token is produced only 'If unicode ranges allowed is true', and 4.3.14 says the token 'is not produced by the tokenizer under normal circumstances'; only a unicode-range descriptor's value is tokenized that way.";
    private const string NoMatchTokens = "CSS Syntax 4 lists the tokens the tokenizer produces; there are no include-match, dash-match, prefix-match, suffix-match, substring-match or column tokens, so '~=', '|=', '^=', '$=', '*=' and '||' are pairs of delim tokens.";
    private const string DeclValue = "CSS Syntax 5.5.6 'consume a declaration': 'Discard whitespace from input' after the colon, then 'consume a list of component values ... with <semicolon-token> as the stop token', then 'While the last item in decl's value is a <whitespace-token>, remove that token'.";
    private const string DeclStop = "CSS Syntax 5.5.6 'consume a declaration': the value is consumed 'with <semicolon-token> as the stop token', so a semicolon ends the declaration and 5.4.7 'parse a declaration' reports any input after it as a syntax error.";
    private const string CdoCdc = "CSS Syntax 5.5.1 'consume a stylesheet's contents': '<CDO-token> <CDC-token>: Discard a token from input'. The 'parse a list of rules' entry point of the 2021 draft, which kept them, no longer exists.";

    private static readonly Dictionary<string, string> Deviations = new(StringComparer.Ordinal)
    {
        ["component_value_list.json#6"] = NonAscii,
        ["component_value_list.json#38"] = NoUrange,
        ["component_value_list.json#39"] = NoUrange,
        ["component_value_list.json#40"] = NoUrange,
        ["component_value_list.json#41"] = NoUrange,
        ["component_value_list.json#42"] = NoUrange,
        ["component_value_list.json#43"] = NoUrange,
        ["component_value_list.json#44"] = NoUrange,
        ["component_value_list.json#45"] = NoUrange,
        ["component_value_list.json#46"] = NoUrange,
        ["component_value_list.json#47"] = NoMatchTokens,
        ["component_value_list.json#48"] = NoMatchTokens,
        ["one_declaration.json#11"] = DeclValue,
        ["one_declaration.json#12"] = DeclStop,
        ["one_declaration.json#14"] = DeclStop,
        ["one_declaration.json#15"] = DeclValue,
        ["one_declaration.json#16"] = DeclValue,
        ["one_declaration.json#17"] = DeclValue,
        ["one_declaration.json#18"] = DeclValue,
        ["one_declaration.json#19"] = DeclValue,
        ["rule_list.json#10"] = CdoCdc,
        ["rule_list.json#12"] = CdoCdc,
    };

    /// <summary>Every deviation is exercised: a listed case that starts passing fails the run, so the list cannot go stale.</summary>
    public static int DeviationCount => Deviations.Count;

    private static void RunFile(string file, Func<string, JsonNode> actual)
    {
        JsonArray cases = CssJson.LoadCases("css-parsing-tests/" + file);
        var failures = new List<string>();
        int passed = 0;
        int deviations = 0;
        for (int i = 0; i + 1 < cases.Count; i += 2)
        {
            string input = cases[i]!.GetValue<string>();
            JsonNode expected = cases[i + 1]!;
            JsonNode got = actual(input);
            string key = $"{file}#{i / 2}";
            if (CssJson.Equal(expected, got))
            {
                passed++;
                Assert.DoesNotContain(key, Deviations.Keys);
            }
            else if (Deviations.ContainsKey(key))
            {
                deviations++;
            }
            else
            {
                failures.Add($"{key}: input {input.ReplaceLineEndings("\\n")}\n  expected {expected.ToJsonString()}\n  actual   {got.ToJsonString()}");
            }
        }

        if (failures.Count > 0)
        {
            throw new XunitException($"{failures.Count} failed ({passed} passed, {deviations} deviations):\n" + string.Join("\n", failures));
        }
    }

    private static JsonNode ErrorOr(CssTree tree, Func<JsonNode> ok) => tree.Error switch
    {
        CssParseError.None => ok(),
        CssParseError.Empty => new JsonArray { "error", "empty" },
        CssParseError.ExtraInput => new JsonArray { "error", "extra-input" },
        _ => new JsonArray { "error", "invalid" },
    };

    [Fact]
    public void Component_value_list() =>
        RunFile("component_value_list.json", s => CssJson.Values(CssTree.ParseComponentValueList(s, cancellationToken: TestContext.Current.CancellationToken).Values));

    [Fact]
    public void One_component_value() =>
        RunFile("one_component_value.json", s =>
        {
            CssTree t = CssTree.ParseComponentValue(s, cancellationToken: TestContext.Current.CancellationToken);
            return ErrorOr(t, () => CssJson.Value(t.Values.GetEnumerator() is var e && e.MoveNext() ? e.Current : throw new InvalidOperationException()));
        });

    [Fact]
    public void One_declaration() =>
        RunFile("one_declaration.json", s =>
        {
            CssTree t = CssTree.ParseDeclaration(s, cancellationToken: TestContext.Current.CancellationToken);
            return ErrorOr(t, () => CssJson.Node(t.Nodes.GetEnumerator() is var e && e.MoveNext() ? e.Current : throw new InvalidOperationException()));
        });

    [Fact]
    public void One_rule() =>
        RunFile("one_rule.json", s =>
        {
            CssTree t = CssTree.ParseRule(s, cancellationToken: TestContext.Current.CancellationToken);
            return ErrorOr(t, () => CssJson.Node(t.Nodes.GetEnumerator() is var e && e.MoveNext() ? e.Current : throw new InvalidOperationException()));
        });

    [Fact]
    public void Declaration_list() =>
        RunFile("declaration_list.json", s => NodeList(CssTree.ParseDeclarationList(s, cancellationToken: TestContext.Current.CancellationToken)));

    [Fact]
    public void Blocks_contents() =>
        RunFile("blocks_contents.json", s =>
        {
            CssTree t = CssTree.ParseBlockContents(s, cancellationToken: TestContext.Current.CancellationToken);
            var a = new JsonArray();
            foreach (CssNode n in t.Nodes)
            {
                a.Add(CssJson.Node(n));
            }

            return a;
        });

    [Fact]
    public void Stylesheet() =>
        RunFile("stylesheet.json", s => NodeList(CssTree.ParseStylesheet(s, cancellationToken: TestContext.Current.CancellationToken)));

    [Fact]
    public void Rule_list() =>
        RunFile("rule_list.json", s => NodeList(CssTree.ParseStylesheet(s, cancellationToken: TestContext.Current.CancellationToken)));

    private static JsonArray NodeList(CssTree tree)
    {
        var a = new JsonArray();
        foreach (CssNode n in tree.Nodes)
        {
            a.Add(CssJson.Node(n));
        }

        return a;
    }
}
