using System;
using System.Buffers;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Encodings;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>
/// A linked stylesheet the caller fetched: its bytes, decoded with the CSS Syntax encoding determination, and the
/// parsed <see cref="Stylesheet"/>. Edits to it produce new bytes in the same encoding with every byte outside the
/// edited ranges unchanged. The library never fetches anything; the caller supplies the bytes.
/// </summary>
public sealed class LinkedStylesheet
{
    private LinkedStylesheet(string url, ReadOnlyMemory<byte> bytes, SourceText source, Stylesheet sheet)
    {
        Url = url;
        Bytes = bytes;
        Source = source;
        Sheet = sheet;
    }

    /// <summary>The URL the caller names the sheet by; the saved-edit format's <c>sheet</c> value.</summary>
    public string Url { get; }

    /// <summary>The sheet's bytes exactly as supplied (or as produced by an edit).</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>The parsed sheet. Its <see cref="Stylesheet.Location"/> is <see cref="Url"/>.</summary>
    public Stylesheet Sheet { get; }

    /// <summary>The name of the encoding the bytes were decoded with.</summary>
    public string EncodingName => Source.Encoding.Name;

    internal SourceText Source { get; }

    /// <summary>Decodes and parses a stylesheet.</summary>
    /// <param name="url">The URL (any non-empty string) that names the sheet in saved edits.</param>
    /// <param name="bytes">The stylesheet's bytes.</param>
    /// <param name="protocolEncodingLabel">The charset of the <c>Content-Type</c> header, if any.</param>
    /// <param name="environmentEncodingLabel">The encoding of the referencing document, if any.</param>
    /// <param name="options">CSS limits; <see langword="null"/> means the defaults.</param>
    /// <exception cref="ArgumentException"><paramref name="url"/> is empty.</exception>
    /// <exception cref="CssLimitExceededException">The input exceeds a limit.</exception>
    public static LinkedStylesheet Create(string url, ReadOnlyMemory<byte> bytes, string? protocolEncodingLabel = null, string? environmentEncodingLabel = null, CssParseOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);
        CssParseOptions o = options ?? CssParseOptions.Default;
        if (bytes.Length > o.MaxInputLength * 4L)
        {
            throw new CssLimitExceededException(CssLimit.InputLength, o.MaxInputLength);
        }

        string name = CssEncoding.DetermineFallbackEncoding(bytes.Span, protocolEncodingLabel, environmentEncodingLabel, WhatwgCssEncodingProvider.Instance);
        WhatwgEncoding encoding = WhatwgEncoding.ForLabel(name) ?? WhatwgEncoding.Utf8;
        SourceText source = SourceText.Decode(bytes, encoding, EncodingConfidence.Certain);
        return new LinkedStylesheet(url, bytes, source, Stylesheet.Parse(source.Text, url, o));
    }

    // The decoded text has every newline as one line feed; the bytes keep what the author wrote. New text follows the
    // first line break of the file: two code units wide for a CRLF pair, one otherwise.
    private bool UsesCrLf()
    {
        int i = Source.Text.IndexOf('\n');
        if (i < 0)
        {
            return false;
        }

        int width = Source.ToByteOffset(i + 1) - Source.ToByteOffset(i);
        return width == (Source.Encoding.IsUtf16 ? 4 : 2);
    }

    /// <summary>
    /// Builds the sheet that results from replacing text ranges of this one. The pieces are in order and do not overlap.
    /// Returns null with an error when the encoding cannot hold the new text or the bytes do not decode back to it.
    /// </summary>
    internal LinkedStylesheet? WithPieces(List<SheetPiece> pieces, string newText, out string? error)
    {
        error = null;
        DocumentEncoder? encoder = DocumentEncoder.Create(Source.Encoding);
        if (encoder is null || Source.Encoding.Kind == EncodingKind.Iso2022Jp)
        {
            error = "stylesheets in the " + Source.Encoding.Name + " encoding cannot be edited";
            return null;
        }

        ReadOnlySpan<byte> original = Bytes.Span;
        bool crlf = UsesCrLf();
        var output = new ArrayBufferWriter<byte>(original.Length + 64);
        int bytePos = 0;
        foreach (SheetPiece p in pieces)
        {
            int bs = Source.ToByteOffset(p.Start);
            int be = Source.ToByteOffset(p.End);
            output.Write(original[bytePos..bs]);
            for (int i = 0; i < p.Text.Length; i++)
            {
                char c = p.Text[i];
                int cp = c;
                if (char.IsHighSurrogate(c) && i + 1 < p.Text.Length && char.IsLowSurrogate(p.Text[i + 1]))
                {
                    cp = char.ConvertToUtf32(c, p.Text[i + 1]);
                    i++;
                }
                else if (char.IsSurrogate(c))
                {
                    error = "the new text has an unpaired surrogate";
                    return null;
                }

                if (!encoder.CanEncode(cp))
                {
                    error = "the new text has a character the stylesheet's encoding (" + Source.Encoding.Name + ") cannot hold";
                    return null;
                }
            }

            encoder.Encode(crlf ? p.Text.Replace("\n", "\r\n", StringComparison.Ordinal) : p.Text, output);
            bytePos = be;
        }

        output.Write(original[bytePos..]);
        byte[] result = output.WrittenSpan.ToArray();

        // The proof of a lossless write: the new bytes decode to exactly the text the edits produced.
        SourceText check = SourceText.Decode(result, Source.Encoding, EncodingConfidence.Certain);
        if (!string.Equals(check.Text, newText, StringComparison.Ordinal) || !ReferenceEquals(check.Encoding, Source.Encoding) && check.Encoding.Kind != Source.Encoding.Kind)
        {
            error = "the new bytes do not decode to the edited stylesheet text";
            return null;
        }

        return new LinkedStylesheet(Url, result, check, Stylesheet.Parse(check.Text, Url));
    }
}
