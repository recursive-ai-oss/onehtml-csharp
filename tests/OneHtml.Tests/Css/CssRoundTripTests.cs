using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Css;

/// <summary>
/// Source round trip and structural invariants of the CSS parse, over every input of the vendored suites, our own
/// stylesheets, and seeded random CSS-shaped text.
/// </summary>
public class CssRoundTripTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Checks a parse: the tokens and comments rebuild the text exactly, and nodes nest and stay in order.</summary>
    internal static void Validate(CssTree t)
    {
        string text = t.Text;
        Assert.Equal(text, CssSourceWriter.Write(t));

        int prev = 0;
        for (int i = 0; i < t.TokenCount; i++)
        {
            CssRange r = t.GetToken(i).Range;
            Assert.True(r.Start >= prev && r.End > r.Start && r.End <= text.Length, $"token {i}");
            prev = r.End;
        }

        var stack = new Stack<(CssNode Parent, CssRange Within)>();
        stack.Push((t.Root, new CssRange(0, text.Length)));
        while (stack.Count > 0)
        {
            (CssNode parent, CssRange within) = stack.Pop();
            int last = within.Start;
            foreach (CssNode n in parent.Children)
            {
                CssRange r = n.Range;
                Assert.True(r.Start >= last && r.End >= r.Start && r.End <= within.End, $"node {n.Kind} {r} within {within}");
                last = r.End;
                if (n.Kind == CssNodeKind.Declaration)
                {
                    Assert.Equal(r.Start, n.NameRange.Start);
                    Assert.True(n.ColonRange.Start >= n.NameRange.End);
                    Assert.True(n.ValueRange.Start >= n.ColonRange.End && n.ValueRange.End <= r.End);
                    Assert.True(n.ImportantRange.End <= r.End && n.SemicolonRange.End == r.End || n.SemicolonRange.IsEmpty);
                }
                else if (n.Kind is CssNodeKind.QualifiedRule or CssNodeKind.AtRule)
                {
                    Assert.True(n.PreludeRange.Start >= r.Start && n.PreludeRange.End <= r.End);
                    if (n.HasBlock)
                    {
                        Assert.True(n.BlockRange.End == r.End && n.ContentRange.Start <= n.ContentRange.End);
                        stack.Push((n, n.ContentRange));
                    }
                }
            }
        }

        // The canonical serializer must be total.
        _ = CssSerializer.Serialize(t);
    }

    private static void CheckAllEntryPoints(string css)
    {
        Validate(CssTree.ParseStylesheet(css, cancellationToken: Ct));
        Validate(CssTree.ParseDeclarationList(css, cancellationToken: Ct));
        Validate(CssTree.ParseBlockContents(css, cancellationToken: Ct));
        Validate(CssTree.ParseRule(css, cancellationToken: Ct));
        Validate(CssTree.ParseDeclaration(css, cancellationToken: Ct));
        Validate(CssTree.ParseComponentValue(css, cancellationToken: Ct));
        Validate(CssTree.ParseComponentValueList(css, cancellationToken: Ct));
        Validate(CssTree.ParseCommaSeparatedList(css, cancellationToken: Ct));
    }

    private static IEnumerable<string> SuiteInputs()
    {
        foreach (string file in Directory.GetFiles(CssJson.TestDataPath("css-parsing-tests"), "*.json"))
        {
            JsonArray cases = JsonNode.Parse(File.ReadAllText(file))!.AsArray();
            for (int i = 0; i + 1 < cases.Count; i += 2)
            {
                if (cases[i] is JsonValue v && v.TryGetValue(out string? s))
                {
                    yield return s;
                }
                else if (cases[i] is JsonObject o)
                {
                    string raw = o["css_bytes"]!.GetValue<string>();
                    yield return raw;
                }
            }
        }
    }

    private static IEnumerable<string> WptInputs()
    {
        string dir = CssJson.TestDataPath("wpt", "css", "css-syntax");
        foreach (string file in Directory.GetFiles(dir, "*.html", SearchOption.AllDirectories).Concat(Directory.GetFiles(dir, "*.css", SearchOption.AllDirectories)))
        {
            string text = File.ReadAllText(file);
            if (file.EndsWith(".css", StringComparison.Ordinal))
            {
                yield return text;
                continue;
            }

            foreach (Match m in Regex.Matches(text, "<style[^>]*>(.*?)</style>", RegexOptions.Singleline))
            {
                yield return m.Groups[1].Value;
            }

            foreach (Match m in Regex.Matches(text, "style=\"([^\"]*)\""))
            {
                yield return m.Groups[1].Value;
            }

            foreach (Match m in Regex.Matches(text, "(?:style\\s*=|setAttribute\\(\"style\",)\\s*['\"`]([^'\"`]*)['\"`]"))
            {
                yield return m.Groups[1].Value;
            }

            // Strings handed to the test helpers (selectors, values, ranges).
            foreach (Match m in Regex.Matches(text, "\"((?:[^\"\\\\\\n]|\\\\.)*)\""))
            {
                yield return Regex.Unescape(m.Groups[1].Value.Replace("\\u", "\\u", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void Suite_inputs_round_trip()
    {
        int n = 0;
        foreach (string css in SuiteInputs())
        {
            CheckAllEntryPoints(css);
            n++;
        }

        Assert.True(n > 300, n.ToString());
    }

    [Fact]
    public void Wpt_inputs_round_trip()
    {
        int n = 0;
        foreach (string css in WptInputs())
        {
            string text;
            try
            {
                text = css;
                CheckAllEntryPoints(text);
            }
            catch (RegexParseException)
            {
                continue;
            }

            n++;
        }

        Assert.True(n > 100, n.ToString());
    }

    [Fact]
    public void Own_stylesheets_round_trip_in_every_newline_style()
    {
        foreach (string file in Directory.GetFiles(CssJson.TestDataPath("own"), "*.css"))
        {
            string css = File.ReadAllText(file);
            foreach (string variant in new[] { css, css.Replace("\n", "\r\n", StringComparison.Ordinal), css.Replace("\n", "\r", StringComparison.Ordinal), css.Replace("\n", "\f", StringComparison.Ordinal) })
            {
                Stylesheet s = Stylesheet.Parse(variant, cancellationToken: Ct);
                Validate(s.Tree);
                Assert.Equal(variant, s.ToSourceText());
                CheckAllEntryPoints(variant);
            }
        }
    }

    [Fact]
    public void Canonical_serialization_is_stable_for_well_formed_sheets()
    {
        foreach (string name in new[] { "minified.css", "nested.css", "escapes.css", "unicode-range.css" })
        {
            string css = File.ReadAllText(CssJson.TestDataPath("own", name));
            string once = Stylesheet.Parse(css, cancellationToken: Ct).Serialize();
            string twice = Stylesheet.Parse(once, cancellationToken: Ct).Serialize();
            Assert.Equal(once, twice);
        }
    }

    [Fact]
    public void Generated_stylesheets_round_trip()
    {
        // Huge single line, deep (but legal) nesting, many rules.
        var sb = new StringBuilder();
        for (int i = 0; i < 20000; i++)
        {
            sb.Append(".c").Append(i).Append("{color:red;margin:0 auto}");
        }

        CheckRoundTrip(sb.ToString());

        sb.Clear();
        for (int i = 0; i < 100; i++)
        {
            sb.Append(".n").Append(i).Append('{');
        }

        sb.Append("color:red;");
        sb.Append('}', 100);
        CheckRoundTrip(sb.ToString());
        CheckRoundTrip(sb.ToString()[..^50]); // unclosed at EOF

        sb.Clear();
        sb.Append("a{b:c(");
        for (int i = 0; i < 200; i++)
        {
            sb.Append("d(");
        }

        CheckRoundTrip(sb.ToString());
        CheckRoundTrip("a{b:" + new string('x', 300000) + "}");
        CheckRoundTrip("/*" + new string('*', 100000));
        CheckRoundTrip("\"" + new string('\\', 100001));
    }

    private static void CheckRoundTrip(string css)
    {
        Stylesheet s = Stylesheet.Parse(css, cancellationToken: Ct);
        Assert.Equal(css, s.ToSourceText());
        Validate(s.Tree);
    }

    [Fact]
    public void Random_css_shaped_text_round_trips()
    {
        string[] pieces =
        [
            "a", "b-c", "--x", "\\", "\\31 ", "\\\n", "0", "1.5e3", "-.5", "+7", "10px", "50%", "#fff", "#1", "@media", "@import", "@",
            "url(", "url(x)", "url( \"y\" )", "u+1?", "U+0-1", "\"", "'", "\"unterminated", "/*", "*/", "/**/", "<!--", "-->", "!", "!important",
            ":", ";", ",", "{", "}", "(", ")", "[", "]", " ", "\n", "\r\n", "\t", "\f", ".", "&", ">", "~", "=", "|", "\0", "\ud83d\ude00", "\ud800", "\u00e9",
            "calc(", "var(--a, ", "{}", "()", "a:b", "a{b:c}", "@media screen{", "@layer x;", "--y:{", "color:red;",
        ];
        var rng = new Random(20261025);
        for (int iter = 0; iter < 6000; iter++)
        {
            var sb = new StringBuilder();
            int count = rng.Next(0, 40);
            for (int k = 0; k < count; k++)
            {
                sb.Append(pieces[rng.Next(pieces.Length)]);
            }

            string css = sb.ToString();
            try
            {
                CheckAllEntryPoints(css);
            }
            catch (Exception e) when (e is not Xunit.Sdk.XunitException)
            {
                throw new InvalidOperationException($"iteration {iter}: {css.ReplaceLineEndings("\\n")}", e);
            }
            catch (Xunit.Sdk.XunitException e)
            {
                throw new InvalidOperationException($"iteration {iter}: {css.ReplaceLineEndings("\\n")}\n{e.Message}", e);
            }
        }
    }
}
