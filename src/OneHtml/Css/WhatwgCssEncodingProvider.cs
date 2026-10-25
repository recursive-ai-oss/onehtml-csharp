using System;
using RecursiveAI.OneHtml.Encodings;

namespace RecursiveAI.OneHtml.Css;

/// <summary>
/// <see cref="ICssEncodingProvider"/> over the library's WHATWG Encoding Standard decoders, so
/// <c>Stylesheet.Parse(bytes, WhatwgCssEncodingProvider.Instance, ...)</c> follows CSS Syntax §3.2 end to end.
/// </summary>
/// <remarks>
/// Encoding names are returned in lower case (<c>utf-8</c>, <c>windows-1252</c>, <c>utf-16le</c>) as
/// <see cref="CssEncoding.DetermineFallbackEncoding"/> expects. Decoding is the shared decoder's, so a carriage
/// return or CR LF pair becomes one line feed in the decoded text; CSS Syntax §3.3 treats those alike.
/// </remarks>
public sealed class WhatwgCssEncodingProvider : ICssEncodingProvider
{
    /// <summary>The shared instance; the provider has no state.</summary>
    public static WhatwgCssEncodingProvider Instance { get; } = new();

    /// <inheritdoc />
    public string? GetEncodingName(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        WhatwgEncoding? encoding = WhatwgEncoding.ForLabel(label);
        if (encoding is null)
        {
            return null;
        }

        string name = encoding.Name;
        return string.Create(name.Length, name, static (span, source) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                char c = source[i];
                span[i] = c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
            }
        });
    }

    /// <inheritdoc />
    public string Decode(ReadOnlySpan<byte> bytes, string fallbackEncodingName)
    {
        ArgumentNullException.ThrowIfNull(fallbackEncodingName);
        WhatwgEncoding encoding = WhatwgEncoding.ForLabel(fallbackEncodingName) ?? WhatwgEncoding.Utf8;
        return SourceText.Decode(bytes.ToArray(), encoding, EncodingConfidence.Certain).Text;
    }
}
