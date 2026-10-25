using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Encodings;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Html5lib;

/// <summary>Runs the html5lib-tests <c>encoding/*.dat</c> files (the <c>&lt;meta&gt;</c> prescan and the sniffing algorithm).</summary>
public class EncodingSuiteTests
{
    private static readonly string[] s_files = ["tests1.dat", "tests2.dat", "test-yahoo-jp.dat"];

    public static IEnumerable<object[]> Cases()
    {
        foreach (string file in s_files)
        {
            int count = Load(file).Count;
            for (int i = 0; i < count; i++)
            {
                yield return [file, i];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Sniffs_the_expected_encoding(string file, int index)
    {
        (byte[] data, string expected) = Load(file)[index];
        WhatwgEncoding want = WhatwgEncoding.ForLabel(expected)!;
        Assert.NotNull(want);

        // The suite's cases have no transport charset and no byte order mark; windows-1252 is the default.
        // The prescan looks at the first 1024 bytes only. A <meta> past that point is met while parsing, where
        // a tentative encoding is changed (HTML 13.2.3.5), so the case is completed by the same step here.
        SourceText text = SourceText.Decode(data);
        if (text.Encoding != want && text.Confidence == EncodingConfidence.Tentative)
        {
            WhatwgEncoding? late = FindMeta(text);
            if (late is not null)
            {
                text = text.ChangeEncoding(late).Text;
            }
        }

        Assert.Equal(want, text.Encoding);
    }

    private static WhatwgEncoding? FindMeta(SourceText text)
    {
        var tokenizer = new Parsing.HtmlTokenizer(text);
        Parsing.HtmlTokenKind kind;
        while ((kind = tokenizer.Next()) != Parsing.HtmlTokenKind.EndOfFile)
        {
            if (kind != Parsing.HtmlTokenKind.StartTag || !tokenizer.Name.SequenceEqual("meta"))
            {
                continue;
            }

            Parsing.AttributeViewList attrs = tokenizer.Attributes;
            if (attrs.TryGet("charset", out Parsing.AttributeView charset))
            {
                return WhatwgEncoding.ForLabel(charset.Value.ToString());
            }

            if (attrs.TryGet("http-equiv", out Parsing.AttributeView equiv) && equiv.Value.Equals("content-type", StringComparison.OrdinalIgnoreCase)
                && attrs.TryGet("content", out Parsing.AttributeView content))
            {
                return EncodingSniffer.ExtractFromMetaContent(content.Value);
            }
        }

        return null;
    }

    private static List<(byte[] Data, string Encoding)> Load(string file)
    {
        // Bytes are read as Latin-1 so that each byte is one char, then cut into cases.
        string all = Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(TestPaths.TestData, "html5lib-tests", "encoding", file)));
        var list = new List<(byte[], string)>();
        foreach (string block in all.Split("#data\n", StringSplitOptions.RemoveEmptyEntries))
        {
            int marker = block.LastIndexOf("\n#encoding\n", StringComparison.Ordinal);
            string data = block[..marker];
            string encoding = block[(marker + "\n#encoding\n".Length)..].Trim();
            list.Add((Encoding.Latin1.GetBytes(data), encoding));
        }

        return list;
    }
}
