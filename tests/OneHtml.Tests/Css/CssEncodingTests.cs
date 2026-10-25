using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Css;

public class CssEncodingTests
{
    [Fact]
    public void Stylesheet_bytes_suite()
    {
        var provider = WhatwgCssEncodingProvider.Instance;
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
