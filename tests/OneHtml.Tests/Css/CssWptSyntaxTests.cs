using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Css;

/// <summary>
/// The tests of web-platform-tests <c>css/css-syntax/</c> that can run without a browser, rewritten against the
/// parser's own API. Each test names the WPT file it comes from. The files themselves are vendored under
/// <c>testdata/css/wpt</c>; where a test needs the CSSOM, selector matching or computed style, the part that is
/// about syntax is checked and the rest is not applicable.
/// </summary>
public class CssWptSyntaxTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CssTree Values(string css) => CssTree.ParseComponentValueList(css, cancellationToken: Ct);

    private static List<ComponentValue> List(ComponentValueList l) => [.. l];

    private static string WptFile(string name) => File.ReadAllText(CssJson.TestDataPath("wpt", "css", "css-syntax", name));

    private static List<CssNode> Top(Stylesheet s) => [.. s.Rules];

    private static Stylesheet Sheet(string css) => Stylesheet.Parse(css, cancellationToken: Ct);

    // ---- trailing-braces.html ----
    [Fact]
    public void Trailing_braces_are_not_declarations()
    {
        Stylesheet s = Sheet("#target1 {\n color:green;\n color:red{};\n color:red {};\n}");
        CssNode rule = Assert.Single(Top(s));
        CssNode decl = Assert.Single(rule.Declarations);
        Assert.Equal("green", decl.ValueText.ToString());
    }

    // ---- charset-is-not-a-rule.html ----
    [Fact]
    public void Charset_is_an_unrecognized_at_rule()
    {
        Stylesheet s = Sheet("@charset \"utf-8\";\n@charset \"utf-8\";\nfoo { color: blue; }\n@charset \"utf-8\";");
        Assert.Equal(1, Top(s).Count(n => n.Kind == CssNodeKind.QualifiedRule));
        Assert.Equal(3, Top(s).Count(n => n.AtRuleKind == AtRuleKind.Charset));
    }

    // ---- invalid-shape-block-balancing.html, invalid-margin-trim-block-balancing.html ----
    [Theory]
    [InlineData(".foo { clip-path: shape(from 0 0, foo()); }")]
    [InlineData(".foo { margin-trim: block-start block(); }")]
    public void Unbalanced_looking_values_do_not_swallow_the_next_rule(string foo)
    {
        Stylesheet s = Sheet("div { color: red; }\n" + foo + "\n.green { color: green; }");
        List<CssNode> rules = Top(s);
        Assert.Equal(3, rules.Count);
        Assert.Equal(".green", rules[2].PreludeText.ToString());
        Assert.Equal("green", Assert.Single(rules[2].Declarations).ValueText.ToString());
    }

    // ---- cdc-vs-ident-tokens.html ----
    [Fact]
    public void Cdc_is_not_swallowed_by_an_ident()
    {
        Stylesheet s = Sheet("-->\n--foo { color: blue; }\n");
        Assert.Equal("--foo", Top(s)[0].PreludeText.ToString());
    }

    // ---- unicode-range-selector.html ----
    [Fact]
    public void Unicode_range_is_not_a_token_outside_its_descriptor()
    {
        List<ComponentValue> v = List(Values("u+a").Values);
        Assert.Equal([CssTokenKind.Ident, CssTokenKind.Delim, CssTokenKind.Ident], v.Select(x => x.Token.Kind));
    }

    // ---- invalid-nested-rules.html ----
    [Fact]
    public void Parsing_continues_after_a_nested_rule_with_an_invalid_prelude()
    {
        Stylesheet s = Sheet(".a {\n .b <::::invalid::::> {}\n & .c {}\n}");
        CssNode a = Assert.Single(Top(s));
        List<CssNode> children = [.. a.Rules];
        Assert.Equal(2, children.Count);
        Assert.Equal("& .c", children[1].PreludeText.ToString());
    }

    // ---- unclosed-constructs.html (the grammar-matching part: an unclosed block is still one block) ----
    [Theory]
    [InlineData("[foo", ComponentValueKind.SimpleBlock)]
    [InlineData(":nth-child(1", ComponentValueKind.Function)]
    public void Unclosed_constructs_are_single_values(string css, ComponentValueKind last)
    {
        List<ComponentValue> v = List(Values(css).Values);
        ComponentValue final = v[^1];
        Assert.Equal(last, final.Kind);
        Assert.False(final.IsClosed);
        Assert.Equal(css.Length, final.Range.End);
    }

    // ---- url-whitespace-consumption.html ----
    [Theory]
    [InlineData("url(\"foo\")")]
    [InlineData("url( \"foo\")")]
    [InlineData("url(\"foo\" )")]
    public void Quoted_urls_are_functions_whatever_the_whitespace(string css)
    {
        ComponentValue v = Assert.Single(List(Values(css).Values));
        Assert.Equal(ComponentValueKind.Function, v.Kind);
        ComponentValue str = v.Children.First(c => !c.IsWhitespace);
        Assert.Equal(CssTokenKind.QuotedString, str.Token.Kind);
        Assert.Equal("foo", str.Token.Value);
    }

    // ---- parse-invalid-input.html (declaration level) ----
    [Theory]
    [InlineData("color: red; }", "color=red")]
    [InlineData("color: \"red", "color=\"red")]
    [InlineData("color: red;' ", "color=red")]
    [InlineData("color: red; /*", "color=red")]
    [InlineData("color: attr(", "color=attr(")]
    [InlineData("color: ", "color=")]
    [InlineData("color: /*red", "color=")]
    [InlineData("color:: red", "color=: red")]
    public void Invalid_style_attribute_text_parses_as_the_specification_says(string input, string expected)
    {
        CssTree t = CssTree.ParseDeclarationList(input, cancellationToken: Ct);
        string got = string.Join(";", t.Root.Declarations.Select(d => d.Name + "=" + d.ValueText.ToString()));
        Assert.Equal(expected, got);
    }

    // ---- unclosed-url-at-eof.html ----
    [Theory]
    [InlineData("background-image:url(foo", "foo")]
    [InlineData("background-image:url(", "")]
    public void Unclosed_url_at_eof_is_a_url_token(string input, string value)
    {
        CssTree t = CssTree.ParseDeclarationList(input, cancellationToken: Ct);
        CssNode d = Assert.Single(t.Root.Declarations);
        ComponentValue v = Assert.Single(List(d.Value));
        Assert.Equal(CssTokenKind.Url, v.Token.Kind);
        Assert.Equal(value, v.Token.Value);
        Assert.False(v.Token.IsTerminated);
    }

    // ---- input-preprocessing.html ----
    [Fact]
    public void Nul_and_surrogates_become_replacement_characters()
    {
        // Lone surrogates are built in code: test-case serialization would mangle them.
        foreach (char bad in new[] { '\0', '\ud800' })
        {
            foreach ((string input, string expected) in new[]
            {
                ("foo" + bad, "foo\uFFFD"),
                ("f" + bad + "oo", "f\uFFFDoo"),
                (bad + "foo", "\uFFFDfoo"),
                (bad.ToString(), "\uFFFD"),
                (new string(bad, 3), "\uFFFD\uFFFD\uFFFD"),
            })
            {
                ComponentValue v = Assert.Single(List(Values(input).Values));
                Assert.Equal(CssTokenKind.Ident, v.Token.Kind);
                Assert.Equal(expected, v.Token.Value);
            }
        }
    }

    [Fact]
    public void Carriage_returns_and_form_feeds_are_newlines()
    {
        // input-preprocessing.html notes that newline normalization is not testable through the CSSOM. Here: CR, FF
        // and CRLF each end a string with a bad-string, and CRLF counts as one newline in an escape.
        Assert.Equal(CssTokenKind.BadString, List(Values("'a\rb'").Values)[0].Token.Kind);
        Assert.Equal(CssTokenKind.BadString, List(Values("'a\fb'").Values)[0].Token.Kind);
        ComponentValue s = Assert.Single(List(Values("'a\\\r\nb'").Values));
        Assert.Equal("ab", s.Token.Value);
        Assert.Equal(CssTokenKind.Whitespace, List(Values("\r\n\f\n").Values).Single().Token.Kind);
    }

    // ---- inclusive-ranges.html ----
    [Fact]
    public void Digits_and_non_printables_have_inclusive_ranges()
    {
        for (char c = '0'; c <= '9'; c++)
        {
            ComponentValue v = Assert.Single(List(Values(c.ToString()).Values));
            Assert.Equal(CssTokenKind.Number, v.Token.Kind);
        }

        var nonPrintable = Enumerable.Range(1, 8).Concat([0xB]).Concat(Enumerable.Range(0xE, 0x12)).Concat([0x7F]);
        foreach (int cp in nonPrintable)
        {
            // "foo" followed by a non-printable code point does not extend the identifier.
            List<ComponentValue> v = List(Values("foo" + (char)cp).Values);
            Assert.Equal(2, v.Count);
            Assert.Equal("foo", v[0].Token.Value);

            // And inside an unquoted url it is a bad url.
            Assert.Equal(CssTokenKind.BadUrl, List(Values("url(a" + (char)cp + "b)").Values)[0].Token.Kind);
        }
    }

    // ---- ident-three-code-points.html ----
    [Theory]
    [InlineData("#1", false)]
    [InlineData("#-2", false)]
    [InlineData("#--3", true)]
    [InlineData("#---4", true)]
    [InlineData("#a", true)]
    [InlineData("#-b", true)]
    [InlineData("#--c", true)]
    [InlineData("#---d", true)]
    public void Hash_tokens_are_id_only_when_they_would_start_an_ident(string css, bool isId)
    {
        ComponentValue v = Assert.Single(List(Values(css).Values));
        Assert.Equal(CssTokenKind.Hash, v.Token.Kind);
        Assert.Equal(isId, v.Token.IsHashId);
    }

    // ---- non-ascii-codepoints.html ----
    [Fact]
    public void Non_ascii_ident_code_points_match_the_ranges()
    {
        int[][] valid =
        [
            [0xB7], [0xC0, 0xD6], [0xD8, 0xF6], [0xF8, 0x37D], [0x37F, 0x1FFF], [0x200C, 0x200D], [0x203F, 0x2040],
            [0x2070, 0x218F], [0x2C00, 0x2FEF], [0x3001, 0xD7FF], [0xF900, 0xFDCF], [0xFDF0, 0xFFFD], [0x10000, 0x1FFFF],
        ];

        bool InRanges(int cp) => valid.Any(r => cp >= r[0] && cp <= (r.Length > 1 ? r[1] : r[0]));

        bool IsIdent(int cp)
        {
            string s = "f" + char.ConvertFromUtf32(cp) + "oo";
            List<ComponentValue> v = List(Values(s).Values);
            return v.Count == 1 && v[0].Token.Kind == CssTokenKind.Ident && v[0].Token.Value == s;
        }

        foreach (int[] range in valid)
        {
            int lo = range[0];
            int hi = range.Length > 1 ? range[1] : range[0];
            Assert.True(IsIdent(lo), $"U+{lo:X}");
            Assert.True(IsIdent(hi), $"U+{hi:X}");
            Assert.True(IsIdent((lo + hi) / 2), $"U+{(lo + hi) / 2:X}");
            if (lo > 0x80 && !InRanges(lo - 1))
            {
                Assert.False(IsIdent(lo - 1), $"U+{lo - 1:X}");
            }

            // U+D800 and above in the surrogate block cannot be written as a scalar value.
            if (!InRanges(hi + 1) && hi + 1 < 0x20000 && !(hi + 1 >= 0xD800 && hi + 1 <= 0xDFFF))
            {
                Assert.False(IsIdent(hi + 1), $"U+{hi + 1:X}");
            }
        }

        Assert.False(IsIdent(0x80));
    }

    // ---- whitespace.html ----
    [Fact]
    public void Css_whitespace_is_exactly_five_ascii_characters()
    {
        foreach (int cp in new[] { 0x9, 0xA, 0xC, 0xD, 0x20 })
        {
            List<ComponentValue> v = List(Values("a" + (char)cp + "b").Values);
            Assert.Equal(3, v.Count);
            Assert.True(v[1].IsWhitespace);
        }

        foreach (int cp in new[]
        {
            0xB, 0x85, 0xA0, 0x1680, 0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009,
            0x200A, 0x2928, 0x2029, 0x202F, 0x205F, 0x3000, 0x180E, 0x200B, 0x200C, 0x200D, 0x2060, 0xFEFF,
        })
        {
            Assert.DoesNotContain(List(Values("a" + (char)cp + "b").Values), x => x.IsWhitespace);
        }
    }

    // ---- decimal-points-in-numbers.html ----
    [Fact]
    public void Decimal_points_belong_to_a_number_only_between_or_before_digits()
    {
        ComponentValue one = Assert.Single(List(Values("1.0").Values));
        Assert.Equal(1.0, one.Token.NumberValue);
        Assert.False(one.Token.IsInteger);
        Assert.Equal(0.1, Assert.Single(List(Values(".1").Values)).Token.NumberValue);
        List<ComponentValue> trailing = List(Values("1.").Values);
        Assert.Equal([CssTokenKind.Number, CssTokenKind.Delim], trailing.Select(x => x.Token.Kind));
        Assert.Equal(CssTokenKind.Dimension, Assert.Single(List(Values("1.0px").Values)).Token.Kind);
        Assert.Equal(0.1, Assert.Single(List(Values(".1px").Values)).Token.NumberValue);
        Assert.Equal([CssTokenKind.Number, CssTokenKind.Delim, CssTokenKind.Ident], List(Values("1.px").Values).Select(x => x.Token.Kind));
    }

    // ---- escaped-eof.html ----
    [Theory]
    [InlineData("foo { --foo:foo\\", CssTokenKind.Ident, "foo\uFFFD")]
    [InlineData("foo { --foo:1foo\\", CssTokenKind.Dimension, "foo\uFFFD")]
    [InlineData("foo { --foo:url(foo\\", CssTokenKind.Url, "foo\uFFFD")]
    [InlineData("foo { --foo:\"foo\\", CssTokenKind.QuotedString, "foo")]
    public void Escaped_eof_is_a_replacement_character_except_in_strings(string css, CssTokenKind kind, string value)
    {
        CssNode decl = Assert.Single(Assert.Single(Top(Sheet(css))).Declarations);
        ComponentValue v = Assert.Single(List(decl.Value));
        Assert.Equal(kind, v.Token.Kind);
        Assert.Equal(value, v.Token.Value);
    }

    [Fact]
    public void Escaped_eof_in_a_hash_makes_an_id_hash()
    {
        ComponentValue h = Assert.Single(List(Values("#foo\\").Values));
        Assert.True(h.Token.IsHashId);
        Assert.Equal("foo\uFFFD", h.Token.Value);
        Assert.False(Assert.Single(List(Values("#123").Values)).Token.IsHashId);
    }

    // ---- declarations-trim-whitespace.html ----
    [Fact]
    public void Custom_property_values_are_trimmed()
    {
        const string css = "#foo {\n --foo-1:bar;\n --foo-2: bar;\n --foo-3:bar ;\n --foo-4: bar ;\n --foo-5: bar !important;\n"
            + " --foo-6: bar !important ;\n --foo-7:bar!important;\n --foo-8:bar!important ;\n --foo-9:bar\n }";
        List<CssNode> decls = [.. Assert.Single(Top(Sheet(css))).Declarations];
        Assert.Equal(9, decls.Count);
        foreach (CssNode d in decls)
        {
            Assert.Equal("bar", d.ValueText.ToString());
        }

        Assert.Equal([false, false, false, false, true, true, true, true, false], decls.Select(d => d.IsImportant));
    }

    // ---- at-rule-in-declaration-list.html ----
    [Theory]
    [InlineData("div { @at {} color: green; }", "color", "green")]
    [InlineData("div { @at at; color: green; }", "color", "green")]
    [InlineData("@page { @at {} margin-top: 20px; }", "margin-top", "20px")]
    [InlineData("@page { @at at; margin-top: 20px; }", "margin-top", "20px")]
    [InlineData("@font-face { @at {} font-family: myfont; }", "font-family", "myfont")]
    [InlineData("@font-face { @at at; font-family: myfont; }", "font-family", "myfont")]
    public void An_at_rule_inside_a_block_does_not_hide_the_declarations(string css, string name, string value)
    {
        CssNode rule = Assert.Single(Top(Sheet(css)));
        CssNode decl = Assert.Single(rule.Declarations);
        Assert.Equal(name, decl.Name);
        Assert.Equal(value, decl.ValueText.ToString());
        Assert.Equal("at", Assert.Single(rule.Rules).Name);
    }

    // ---- custom-property-rule-ambiguity.html ----
    [Fact]
    public void A_rule_that_looks_like_a_custom_property_is_dropped_at_the_top_level()
    {
        List<CssNode> rules = Top(Sheet(".a { }\n --x:hover { color: blue; } \n .b { }"));
        Assert.Equal([".a", ".b"], rules.Where(n => n.Kind == CssNodeKind.QualifiedRule).Select(n => n.PreludeText.ToString()));
        rules = Top(Sheet(".a { }\n --x:hover { ] } /* Ambiguous \"rule\" */\n .b { }"));
        Assert.Equal([".a", ".b"], rules.Where(n => n.Kind == CssNodeKind.QualifiedRule).Select(n => n.PreludeText.ToString()));
    }

    [Fact]
    public void A_nested_custom_property_takes_everything_to_the_end_of_the_block()
    {
        CssNode div = Assert.Single(Top(Sheet("div {\n .a { }\n --x:hover { }\n .b { }\n}")));
        CssNode a = Assert.Single(div.Rules);
        Assert.Equal(".a", a.PreludeText.ToString());
        CssNode x = Assert.Single(div.Declarations);
        Assert.Equal("--x", x.Name);
        Assert.Equal("hover { }\n .b { }", x.ValueText.ToString());

        // With a stray "]" the declaration is not a valid custom property value, but syntactically it is still one
        // declaration that runs to the end of the block, so no ".b" rule appears.
        div = Assert.Single(Top(Sheet("div {\n .a { }\n --x:hover { ] }\n .b { }\n}")));
        Assert.Single(div.Rules);
        Assert.Equal("--x", Assert.Single(div.Declarations).Name);
    }

    // ---- var-with-blocks.html (the declaration-versus-rule decision) ----
    [Theory]
    [InlineData("color:var(--x);", true)]
    [InlineData("color:{var(--x)};", true)]
    [InlineData("color: { var(--x) };", true)]
    [InlineData("color:var(--x) { };", false)]
    [InlineData("color:{ } var(--x);", false)]
    [InlineData("color:{ var(--x) } A;", false)]
    [InlineData("color:A { var(--x) };", false)]
    [InlineData("--y:var(--x);", true)]
    [InlineData("--y:{var(--x)};", true)]
    [InlineData("--y: { var(--x) };", true)]
    [InlineData("--y:var(--x) { };", true)]
    [InlineData("--y:{ } var(--x);", true)]
    [InlineData("--y:{ var(--x) } A;", true)]
    [InlineData("--y:A { var(--x) };", true)]
    public void A_block_in_a_value_makes_a_declaration_only_when_it_is_the_whole_value(string body, bool isDeclaration)
    {
        CssNode rule = Assert.Single(Top(Sheet(".a {\n color: rgb(2, 2, 2);\n " + body + "\n background-color:rgb(1, 1, 1);\n}")));
        string[] names = [.. rule.Declarations.Select(d => d.Name)];
        string name = body[..body.IndexOf(':', StringComparison.Ordinal)];
        int expected = name == "color" ? (isDeclaration ? 2 : 1) : (isDeclaration ? 1 : 0);
        Assert.Equal(expected, names.Count(n => n == name));
        Assert.Contains("background-color", names);
        if (!isDeclaration)
        {
            Assert.Single(rule.Rules);
        }
    }

    // ---- missing-semicolon.html ----
    [Fact]
    public void A_last_declaration_without_a_semicolon_is_kept()
    {
        Stylesheet s = Sheet(WptFile("support/missing-semicolon.css"));
        CssNode rule = Assert.Single(Top(s));
        List<CssNode> d = [.. rule.Declarations];
        Assert.Equal(2, d.Count);
        Assert.Equal("green", d[1].ValueText.ToString());
    }

    // ---- crashtests/ ----
    [Fact]
    public void Crashtests_do_not_crash()
    {
        Sheet("* {\n  @\\aaa\n}");
        CssTree.ParseDeclarationList("content: attr(test) / attr(", cancellationToken: Ct);

        // 60000 unclosed brackets pass the nesting limit and degrade to unclosed blocks, never a crash.
        CssTree deep = CssTree.ParseDeclarationList("content: " + new string('(', 60000) + "\";", cancellationToken: Ct);
        Assert.True(deep.NestingLimitReached);
    }

    // ---- serialize-escape-identifiers.html ----
    [Fact]
    public void Escaped_identifiers_survive_serialization()
    {
        const string css = "@import 'abc' layer(\\{\\});\n@counter-style abc\\{\\}oops {}\n@font-feature-values abc\\{\\}oops {}\n"
            + "@font-palette-values --abc\\{\\}oops {}\n@keyframes abc\\{\\}oops {}\n@layer abc\\;oops\\!;\n@media \\( {}\n"
            + "@position-try abc\\{\\}oops {}\n@namespace abc\\{\\}oops \"test\";\n@page abc\\{\\}def {}\n";
        Stylesheet first = Sheet(css);
        string once = first.Serialize();
        Stylesheet second = Sheet(once);
        Assert.Equal(once, second.Serialize());
        Assert.Equal(first.Rules.Count, second.Rules.Count);
        Assert.Equal(first.Rules.Select(r => r.Name), second.Rules.Select(r => r.Name));
    }

    // ---- serialize-consecutive-tokens.html ----
    [Fact]
    public void Consecutive_tokens_that_would_merge_get_a_comment_between_them()
    {
        string[][] cases =
        [
            ["foo", "bar", "bar()", "url(bar)", "-", "123", "123%", "123em", "-->", "()"],
            ["@foo", "bar", "bar()", "url(bar)", "-", "123", "123%", "123em", "-->"],
            ["#foo", "bar", "bar()", "url(bar)", "-", "123", "123%", "123em", "-->"],
            ["123foo", "bar", "bar()", "url(bar)", "-", "123", "123%", "123em", "-->"],
            ["#", "bar", "bar()", "url(bar)", "-", "123", "123%", "123em"],
            ["-", "bar", "bar()", "url(bar)", "-", "123", "123%", "123em"],
            ["123", "bar", "bar()", "url(bar)", "123", "123%", "123em", "%"],
            ["@", "bar", "bar()", "url(bar)", "-"],
            [".", "123", "123%", "123em"],
            ["+", "123", "123%", "123em"],
            ["/", "*"],
        ];
        int n = 0;
        foreach (string[] row in cases)
        {
            for (int i = 1; i < row.Length; i++)
            {
                string t1 = row[0];
                string t2 = row[i];
                CssTree tree = Values(t1 + "/**/" + t2);
                string s = CssSerializer.Serialize(tree.Values);
                Assert.NotEqual(t1 + t2, s);
                Assert.StartsWith(t1, s, StringComparison.Ordinal);
                if (t2 != "url(bar)")
                {
                    // A url token is written back in its canonical quoted form.
                    Assert.EndsWith(t2, s, StringComparison.Ordinal);
                }

                CssTree again = Values(s);
                Assert.Equal(Kinds(tree), Kinds(again));
                n++;
            }
        }

        Assert.True(n > 60);
    }

    private static List<string> Kinds(CssTree t)
    {
        var r = new List<string>();
        for (int i = 0; i < t.TokenCount; i++)
        {
            r.Add(t.GetToken(i).Kind + ":" + t.GetToken(i).Value);
        }

        return r;
    }

    // ---- urange-parsing.html (the unicode-range descriptor, without the CSSOM) ----
    [Fact]
    public void Unicode_range_descriptor_values()
    {
        string html = WptFile("urange-parsing.html");
        int valid = 0;
        int invalid = 0;
        int notApplicable = 0;
        foreach (Match m in Regex.Matches(html, "test(Invalid)?Urange\\(\"([^\"]*)\"(?:, \"([^\"]*)\")?\\);"))
        {
            bool expectInvalid = m.Groups[1].Success;
            string input = m.Groups[2].Value;
            if (input.Contains("/**/", StringComparison.Ordinal))
            {
                // Comments between the pieces of a range are accepted by the <urange> value grammar of CSS Values,
                // which is the typed-value parser's job; the tokenizer only joins adjacent characters.
                notApplicable++;
                continue;
            }

            CssTree t = CssTree.ParseDeclaration("unicode-range: " + input, cancellationToken: Ct);
            bool ok = t.Error == CssParseError.None;
            CssNode d = ok ? t.Nodes.First() : default;
            List<ComponentValue> v = ok ? List(d.Value) : [];
            bool isRange = v.Count == 1 && v[0].Token.Kind == CssTokenKind.UnicodeRange;
            int start = 0;
            int end = -1;
            if (isRange)
            {
                v[0].Token.GetUnicodeRange(out start, out end);
                isRange = start <= end && end <= 0x10FFFF;
            }

            if (expectInvalid)
            {
                Assert.False(isRange, input);
                invalid++;
            }
            else
            {
                Assert.True(isRange, input);
                string expected = m.Groups[3].Value.ToUpperInvariant()[2..];
                int dash = expected.IndexOf('-', StringComparison.Ordinal);
                int es = Convert.ToInt32(dash < 0 ? expected : expected[..dash], 16);
                int ee = dash < 0 ? es : Convert.ToInt32(expected[(dash + 1)..], 16);
                Assert.Equal((es, ee), (start, end));
                valid++;
            }
        }

        Assert.True(valid > 30 && invalid > 40, $"{valid} valid, {invalid} invalid, {notApplicable} not applicable");
    }
}
