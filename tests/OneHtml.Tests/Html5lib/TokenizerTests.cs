using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RecursiveAI.OneHtml.Parsing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Html5lib;

/// <summary>Runs every case of the html5lib-tests tokenizer files, once per listed initial state.</summary>
public class TokenizerTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string file in TokenizerSuite.Files())
        {
            if (KnownDifferences.Files.ContainsKey(file))
            {
                continue;
            }

            foreach (TokenizerCase c in TokenizerSuite.Load(file))
            {
                yield return [file, c.Index];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Tokenizes_like_the_suite_expects(string file, int index)
    {
        TokenizerCase c = TokenizerSuite.Load(file)[index];
        foreach (string stateName in c.InitialStates)
        {
            (List<string> tokens, List<string> errors) = Run(c, stateName);
            string where = $"{file} #{index} ({c.Description}) in {stateName}, input {Escape(c.Input)}";
            Assert.True(c.ExpectedTokens.SequenceEqual(tokens), $"{where}\nexpected: {string.Join(" ", c.ExpectedTokens)}\nactual:   {string.Join(" ", tokens)}");
            if (c.ExpectedErrors is not null && !KnownDifferences.ErrorListsNotChecked.ContainsKey(file + "|" + c.Description))
            {
                Assert.True(KnownDifferences.ErrorsMatch(c.ExpectedErrors, errors), $"{where}\nexpected errors: {string.Join(" ", c.ExpectedErrors)}\nactual errors:   {string.Join(" ", errors)}");
            }
        }
    }

    private static (List<string> Tokens, List<string> Errors) Run(TokenizerCase c, string stateName)
    {
        var tokenizer = new HtmlTokenizer(c.Input, new HtmlTokenizerOptions { RecordErrors = true });
        tokenizer.SetState(stateName switch
        {
            "Data state" => TokenizerState.Data,
            "PLAINTEXT state" => TokenizerState.Plaintext,
            "RCDATA state" => TokenizerState.Rcdata,
            "RAWTEXT state" => TokenizerState.Rawtext,
            "Script data state" => TokenizerState.ScriptData,
            "CDATA section state" => TokenizerState.CdataSection,
            _ => throw new InvalidOperationException("Unknown state " + stateName),
        });
        tokenizer.LastStartTagName = c.LastStartTag;
        var tokens = new List<string>();
        int guard = 0;
        while (tokenizer.Next() != HtmlTokenKind.EndOfFile)
        {
            Assert.True(++guard < 1_000_000, "tokenizer does not finish");
            Assert.InRange(tokenizer.Start, 0, tokenizer.Source.Text.Length);
            Assert.InRange(tokenizer.End, tokenizer.Start, tokenizer.Source.Text.Length);
            switch (tokenizer.Kind)
            {
                case HtmlTokenKind.Character:
                    tokens.Add("Character:" + tokenizer.Data.ToString());
                    break;
                case HtmlTokenKind.Comment:
                    tokens.Add("Comment:" + tokenizer.Data.ToString());
                    break;
                case HtmlTokenKind.EndTag:
                    tokens.Add("EndTag:" + tokenizer.Name.ToString());
                    break;
                case HtmlTokenKind.StartTag:
                {
                    var attrs = new List<KeyValuePair<string, string>>();
                    for (int i = 0; i < tokenizer.Attributes.Count; i++)
                    {
                        attrs.Add(new(tokenizer.Attributes[i].Name.ToString(), tokenizer.Attributes[i].Value.ToString()));
                    }

                    tokens.Add(TokenizerSuite.FormatStartTag(tokenizer.Name.ToString(), attrs, tokenizer.SelfClosing));
                    break;
                }

                case HtmlTokenKind.Doctype:
                    tokens.Add(TokenizerSuite.FormatDoctype(
                        tokenizer.HasDoctypeName ? tokenizer.Name.ToString() : null,
                        tokenizer.PublicId,
                        tokenizer.SystemId,
                        !tokenizer.ForceQuirks));
                    break;
                case HtmlTokenKind.ProcessingInstruction:
                    tokens.Add("PI:" + tokenizer.Name.ToString() + "|" + tokenizer.Data.ToString());
                    break;
                default:
                    throw new InvalidOperationException("unexpected token kind " + tokenizer.Kind);
            }
        }

        string text = tokenizer.Source.Text;
        var errors = new List<string>();
        foreach (ParseError e in tokenizer.Errors!)
        {
            int line = 1;
            int lineStart = 0;
            for (int i = 0; i < e.Offset && i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            errors.Add(string.Create(CultureInfo.InvariantCulture, $"{HtmlParseErrors.GetCode(e.Error)}@{line}:{e.Offset - lineStart + 1}"));
        }

        return (TokenizerSuite.Coalesce(tokens), errors);
    }

    private static string Escape(string s) =>
        string.Concat(s.Select(ch => ch is >= ' ' and < '\u007f' ? ch.ToString() : string.Create(CultureInfo.InvariantCulture, $"\\u{(int)ch:X4}")));
}
