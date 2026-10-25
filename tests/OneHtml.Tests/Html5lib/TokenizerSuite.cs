using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Tests.Html5lib;

/// <summary>One case of an html5lib-tests tokenizer file.</summary>
internal sealed class TokenizerCase
{
    public required string File { get; init; }

    public required int Index { get; init; }

    public required string Description { get; init; }

    public required string Input { get; init; }

    public required IReadOnlyList<string> ExpectedTokens { get; init; }

    public required IReadOnlyList<string> InitialStates { get; init; }

    public string? LastStartTag { get; init; }

    /// <summary>Null when the case lists no errors; the suite then does not check them.</summary>
    public IReadOnlyList<string>? ExpectedErrors { get; init; }
}

/// <summary>Reads the html5lib-tests tokenizer files.</summary>
internal static class TokenizerSuite
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<TokenizerCase>> s_files = new();

    public static string Directory { get; } = Path.Combine(TestPaths.TestData, "html5lib-tests", "tokenizer");

    public static IEnumerable<string> Files() =>
        System.IO.Directory.GetFiles(Directory, "*.test").Select(Path.GetFileName).Order(StringComparer.Ordinal)!;

    public static IReadOnlyList<TokenizerCase> Load(string file) => s_files.GetOrAdd(file, Parse);

    private static IReadOnlyList<TokenizerCase> Parse(string file)
    {
        using JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllBytes(Path.Combine(Directory, file)));
        JsonElement tests = doc.RootElement.EnumerateObject().First().Value;
        var list = new List<TokenizerCase>();
        int index = 0;
        foreach (JsonElement t in tests.EnumerateArray())
        {
            bool doubleEscaped = t.TryGetProperty("doubleEscaped", out JsonElement de) && de.GetBoolean();
            string Unescape(string s) => doubleEscaped ? UnescapeUnicode(s) : s;
            var expected = new List<string>();
            foreach (JsonElement token in t.GetProperty("output").EnumerateArray())
            {
                expected.Add(Canonical(token, Unescape));
            }

            List<string>? errors = null;
            if (t.TryGetProperty("errors", out JsonElement errs))
            {
                errors = new List<string>();
                foreach (JsonElement e in errs.EnumerateArray())
                {
                    errors.Add(string.Create(CultureInfo.InvariantCulture, $"{e.GetProperty("code").GetString()}@{e.GetProperty("line").GetInt32()}:{e.GetProperty("col").GetInt32()}"));
                }
            }

            var states = new List<string>();
            if (t.TryGetProperty("initialStates", out JsonElement st))
            {
                states.AddRange(st.EnumerateArray().Select(x => x.GetString()!));
            }
            else
            {
                states.Add("Data state");
            }

            list.Add(new TokenizerCase
            {
                File = file,
                Index = index++,
                Description = t.GetProperty("description").GetString()!,
                Input = Unescape(t.GetProperty("input").GetString()!),
                ExpectedTokens = Coalesce(expected),
                InitialStates = states,
                LastStartTag = t.TryGetProperty("lastStartTag", out JsonElement ls) ? ls.GetString() : null,
                ExpectedErrors = errors,
            });
        }

        return list;
    }

    // Canonical, comparable text for one expected token.
    private static string Canonical(JsonElement token, Func<string, string> unescape)
    {
        string kind = token[0].GetString()!;
        switch (kind)
        {
            case "Character":
                return "Character:" + unescape(token[1].GetString()!);
            case "Comment":
                return "Comment:" + unescape(token[1].GetString()!);
            case "EndTag":
                return "EndTag:" + unescape(token[1].GetString()!);
            case "StartTag":
            {
                var attrs = new List<KeyValuePair<string, string>>();
                foreach (JsonProperty p in token[2].EnumerateObject())
                {
                    attrs.Add(new(unescape(p.Name), unescape(p.Value.GetString()!)));
                }

                bool selfClosing = token.GetArrayLength() > 3 && token[3].GetBoolean();
                return FormatStartTag(unescape(token[1].GetString()!), attrs, selfClosing);
            }

            case "DOCTYPE":
                return FormatDoctype(
                    token[1].ValueKind == JsonValueKind.Null ? null : unescape(token[1].GetString()!),
                    token[2].ValueKind == JsonValueKind.Null ? null : unescape(token[2].GetString()!),
                    token[3].ValueKind == JsonValueKind.Null ? null : unescape(token[3].GetString()!),
                    token[4].GetBoolean());
            case "ProcessingInstruction":
                return "PI:" + unescape(token[1].GetString()!) + "|" + unescape(token[2].GetString()!);
            default:
                throw new InvalidOperationException("Unknown token kind " + kind);
        }
    }

    public static string FormatStartTag(string name, IEnumerable<KeyValuePair<string, string>> attributes, bool selfClosing) =>
        "StartTag:" + name + "|" + string.Join(",", attributes.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => a.Key + "=" + a.Value)) + (selfClosing ? "|/" : string.Empty);

    public static string FormatDoctype(string? name, string? publicId, string? systemId, bool correct) =>
        $"DOCTYPE:{name ?? "<null>"}|{publicId ?? "<null>"}|{systemId ?? "<null>"}|{(correct ? "correct" : "quirks")}";

    public static List<string> Coalesce(IEnumerable<string> tokens)
    {
        var result = new List<string>();
        foreach (string t in tokens)
        {
            if (t.StartsWith("Character:", StringComparison.Ordinal) && result.Count > 0 && result[^1].StartsWith("Character:", StringComparison.Ordinal))
            {
                result[^1] += t["Character:".Length..];
            }
            else
            {
                result.Add(t);
            }
        }

        return result;
    }

    // The suite's "double escaped" strings spell non-ASCII code units as \\uXXXX.
    private static string UnescapeUnicode(string s)
    {
        if (!s.Contains("\\u", StringComparison.Ordinal))
        {
            return s;
        }

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 5 < s.Length + 0 && s[i + 1] == 'u' && int.TryParse(s.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
            {
                sb.Append((char)code);
                i += 5;
            }
            else
            {
                sb.Append(s[i]);
            }
        }

        return sb.ToString();
    }
}
