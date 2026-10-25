using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Css;

/// <summary>A tiny encoding provider for the byte-level tests, built on the platform's decoders.</summary>
internal sealed class TestEncodingProvider : ICssEncodingProvider
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["utf-8"] = "utf-8",
        ["utf8"] = "utf-8",
        ["utf-16"] = "utf-16le",
        ["utf-16le"] = "utf-16le",
        ["utf-16be"] = "utf-16be",
        ["iso-8859-2"] = "iso-8859-2",
        ["iso-8859-5"] = "iso-8859-5",
        ["windows-1250"] = "windows-1250",
        ["windows-1251"] = "windows-1251",
        ["windows-1253"] = "windows-1253",
    };

    static TestEncodingProvider() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public string? GetEncodingName(string label) =>
        Labels.TryGetValue(label.Trim(' ', '\t', '\n', '\f', '\r').ToLowerInvariant(), out string? name) ? name : null;

    public string Decode(ReadOnlySpan<byte> bytes, string fallbackEncodingName)
    {
        if (bytes.StartsWith("\uFEFF"u8))
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        Encoding enc = fallbackEncodingName switch
        {
            "utf-8" => new UTF8Encoding(false),
            "utf-16le" => Encoding.Unicode,
            "utf-16be" => Encoding.BigEndianUnicode,
            "iso-8859-2" => Encoding.GetEncoding(28592),
            "iso-8859-5" => Encoding.GetEncoding(28595),
            "windows-1250" => Encoding.GetEncoding(1250),
            "windows-1251" => Encoding.GetEncoding(1251),
            "windows-1253" => Encoding.GetEncoding(1253),
            _ => throw new InvalidOperationException(fallbackEncodingName),
        };
        return enc.GetString(bytes);
    }
}

public class CssEncodingTests
{
    [Fact]
    public void Stylesheet_bytes_suite()
    {
        var provider = new TestEncodingProvider();
        JsonArray cases = CssJson.LoadCases("css-parsing-tests/stylesheet_bytes.json");
        var failures = new List<string>();
        for (int i = 0; i + 1 < cases.Count; i += 2)
        {
            JsonObject input = cases[i]!.AsObject();
            string cssBytes = input["css_bytes"]!.GetValue<string>();
            var bytes = new byte[cssBytes.Length];
            for (int k = 0; k < bytes.Length; k++)
            {
                bytes[k] = (byte)cssBytes[k];
            }

            string? protocol = input["protocol_encoding"]?.GetValue<string>();
            string? env = input["environment_encoding"]?.GetValue<string>();
            JsonArray expected = cases[i + 1]!.AsArray();
            string expectedEncoding = expected[1]!.GetValue<string>();

            string fallback = CssEncoding.DetermineFallbackEncoding(bytes, protocol, env, provider);
            Stylesheet sheet = Stylesheet.Parse(bytes, provider, protocol, env, cancellationToken: TestContext.Current.CancellationToken);
            var rules = new JsonArray();
            foreach (CssNode n in sheet.Rules)
            {
                rules.Add(CssJson.Node(n));
            }

            // The suite names the encoding actually used, which is the BOM's when there is one.
            string used = bytes.AsSpan().StartsWith("\uFEFF"u8) ? "utf-8"
                : bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE ? "utf-16le"
                : bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF ? "utf-16be"
                : fallback;
            if (!CssJson.Equal(expected[0], rules) || used != expectedEncoding)
            {
                failures.Add($"#{i / 2}: expected {expected.ToJsonString()} got {rules.ToJsonString()} / {used}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
