using System;
using System.Buffers;
using System.Threading;

namespace RecursiveAI.OneHtml.Encodings;

/// <summary>Options for <see cref="SourceText.Decode(ReadOnlyMemory{byte}, DecodeOptions?, CancellationToken)"/>.</summary>
public sealed class DecodeOptions
{
    /// <summary>The transport <c>Content-Type</c> header value, if the caller has one. Default <see langword="null"/>.</summary>
    public string? ContentType { get; init; }

    /// <summary>The encoding used when nothing else decides. Default <see cref="WhatwgEncoding.Windows1252"/>.</summary>
    public WhatwgEncoding? DefaultEncoding { get; init; }

    /// <summary>
    /// An encoding the caller insists on (a user override). It is used with <see cref="EncodingConfidence.Certain"/>
    /// unless the input starts with a byte order mark. Default <see langword="null"/>.
    /// </summary>
    public WhatwgEncoding? OverrideEncoding { get; init; }

    /// <summary>How many bytes the <c>&lt;meta&gt;</c> prescan reads. Default 1024.</summary>
    public int PrescanLimit { get; init; } = EncodingSniffer.DefaultPrescanLimit;

    /// <summary>The input bounds. Default <see cref="ParseLimits.Default"/>.</summary>
    public ParseLimits? Limits { get; init; }
}

/// <summary>The result of <see cref="SourceText.ChangeEncoding"/>.</summary>
/// <param name="Text">The source text to use from now on.</param>
/// <param name="RestartRequired">
/// True when <paramref name="Text"/> was decoded again with a different encoding, so everything built from the old
/// text (tokens, tree, offsets) must be thrown away and parsing started over on it. False when the old text stays valid.
/// </param>
public readonly record struct EncodingChange(SourceText Text, bool RestartRequired);

/// <summary>
/// An HTML input: the original bytes, the text decoded from them once, the encoding used and how sure the parser
/// is of it, and the map between text offsets and byte offsets.
/// </summary>
/// <remarks>
/// <para>
/// The text is the input stream the tokenizer reads, after the preprocessing step of HTML 13.2.3.5 that can be
/// done without looking at markup: newlines are normalized (CR LF and lone CR become LF). Decoding errors are
/// U+FFFD. A byte order mark is not part of the text.
/// </para>
/// <para>Instances are immutable and safe to read from any number of threads.</para>
/// </remarks>
public sealed class SourceText
{
    private SourceText(
        ReadOnlyMemory<byte> bytes,
        string text,
        WhatwgEncoding encoding,
        EncodingConfidence confidence,
        OffsetMap map,
        int errorCount,
        ParseLimits limits)
    {
        Bytes = bytes;
        Text = text;
        Encoding = encoding;
        Confidence = confidence;
        Map = map;
        DecodeErrorCount = errorCount;
        Limits = limits;
    }

    /// <summary>The original input bytes, exactly as given. Empty for text made by <see cref="FromString"/>.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>The decoded, newline-normalized text.</summary>
    public string Text { get; }

    /// <summary>The encoding the text was decoded with.</summary>
    public WhatwgEncoding Encoding { get; }

    /// <summary>
    /// Whether <see cref="Encoding"/> is certain. While it is <see cref="EncodingConfidence.Tentative"/>, a
    /// <c>&lt;meta&gt;</c> declaration the tree builder meets may call <see cref="ChangeEncoding"/>.
    /// </summary>
    public EncodingConfidence Confidence { get; }

    /// <summary>The map between text offsets and byte offsets.</summary>
    public OffsetMap Map { get; }

    /// <summary>How many times the decoder replaced bad input with U+FFFD.</summary>
    public int DecodeErrorCount { get; }

    /// <summary>The length in bytes of the byte order mark that was skipped, or 0.</summary>
    public int BomLength => Map.FirstByte;

    /// <summary>The limits this text was decoded under; <see cref="ChangeEncoding"/> reuses them.</summary>
    public ParseLimits Limits { get; }

    /// <summary>
    /// Sniffs the encoding (BOM, <paramref name="options"/>' content type, prescan, default) and decodes the input.
    /// </summary>
    /// <param name="bytes">The document bytes; kept by reference, not copied, so the caller must not change them.</param>
    /// <param name="options">Sniffing inputs and limits; <see langword="null"/> means all defaults.</param>
    /// <param name="cancellationToken">Checked periodically while decoding.</param>
    /// <exception cref="HtmlLimitExceededException">The input is longer than <see cref="ParseLimits.MaxInputLength"/>, or its offset map would be larger than <see cref="ParseLimits.MaxOffsetMapRuns"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static SourceText Decode(ReadOnlyMemory<byte> bytes, DecodeOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new DecodeOptions();
        ParseLimits limits = options.Limits ?? ParseLimits.Default;
        CheckLength(bytes.Length, limits);
        EncodingSniffResult sniffed;
        if (options.OverrideEncoding is { } forced)
        {
            // HTML 13.2.3.2: a byte order mark still wins over an override.
            WhatwgEncoding? bom = EncodingSniffer.SniffBom(bytes.Span, out int bomLength);
            sniffed = bom is not null
                ? new EncodingSniffResult(bom, EncodingConfidence.Certain, bomLength)
                : new EncodingSniffResult(forced, EncodingConfidence.Certain, 0);
        }
        else
        {
            sniffed = EncodingSniffer.Sniff(bytes.Span, options.ContentType, options.DefaultEncoding, options.PrescanLimit);
        }

        return DecodeCore(bytes, sniffed.Encoding, sniffed.Confidence, sniffed.BomLength, limits, cancellationToken);
    }

    /// <summary>
    /// Decodes the input with a known encoding. As in the Encoding Standard's "decode", a byte order mark overrides
    /// <paramref name="encoding"/> and is skipped.
    /// </summary>
    /// <param name="bytes">The document bytes; kept by reference.</param>
    /// <param name="encoding">The encoding to use if there is no byte order mark.</param>
    /// <param name="confidence">The confidence to record. A byte order mark makes it <see cref="EncodingConfidence.Certain"/>.</param>
    /// <param name="limits">The input bounds; <see langword="null"/> means <see cref="ParseLimits.Default"/>.</param>
    /// <param name="cancellationToken">Checked periodically while decoding.</param>
    /// <exception cref="HtmlLimitExceededException">A limit was exceeded.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static SourceText Decode(
        ReadOnlyMemory<byte> bytes,
        WhatwgEncoding encoding,
        EncodingConfidence confidence,
        ParseLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        limits ??= ParseLimits.Default;
        CheckLength(bytes.Length, limits);
        WhatwgEncoding? bom = EncodingSniffer.SniffBom(bytes.Span, out int bomLength);
        if (bom is not null)
        {
            return DecodeCore(bytes, bom, EncodingConfidence.Certain, bomLength, limits, cancellationToken);
        }

        return DecodeCore(bytes, encoding, confidence, 0, limits, cancellationToken);
    }

    /// <summary>
    /// Wraps text that did not come from bytes (a fragment given as a string). Newlines are normalized; there are no
    /// bytes, and the map is the identity, so a byte offset equals the text offset.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="limits">The input bounds; <see langword="null"/> means <see cref="ParseLimits.Default"/>.</param>
    /// <exception cref="HtmlLimitExceededException">The text is longer than <see cref="ParseLimits.MaxInputLength"/>.</exception>
    public static SourceText FromString(string text, ParseLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        limits ??= ParseLimits.Default;
        CheckLength(text.Length, limits);
        string normalized = NormalizeNewlines(text);
        return new SourceText(
            ReadOnlyMemory<byte>.Empty,
            normalized,
            WhatwgEncoding.Utf8,
            EncodingConfidence.Certain,
            OffsetMap.CreateIdentity(normalized.Length),
            0,
            limits);
    }

    /// <summary>
    /// Replaces CR LF and lone CR with LF (HTML 13.2.3.5). Returns the same instance when there is no CR.
    /// </summary>
    /// <param name="text">The text to normalize.</param>
    public static string NormalizeNewlines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int first = text.AsSpan().IndexOf('\r');
        if (first < 0)
        {
            return text;
        }

        int pairs = 0;
        for (int r = first; r < text.Length - 1; r++)
        {
            if (text[r] == '\r' && text[r + 1] == '\n')
            {
                pairs++;
            }
        }

        return string.Create(text.Length - pairs, (text, first), static (dest, state) =>
        {
            ReadOnlySpan<char> src = state.text;
            src[..state.first].CopyTo(dest);
            int w = state.first;
            for (int r = state.first; r < src.Length; r++)
            {
                char c = src[r];
                if (c == '\r')
                {
                    dest[w++] = '\n';
                    if (r + 1 < src.Length && src[r + 1] == '\n')
                    {
                        r++;
                    }
                }
                else
                {
                    dest[w++] = c;
                }
            }
        });
    }

    /// <summary>Converts a text offset to a byte offset in <see cref="Bytes"/>; see <see cref="OffsetMap.ToByteOffset"/>.</summary>
    /// <param name="textOffset">A value from 0 to the text length inclusive.</param>
    public int ToByteOffset(int textOffset) => Map.ToByteOffset(textOffset);

    /// <summary>Converts a byte offset to a text offset; see <see cref="OffsetMap.ToTextOffset"/>.</summary>
    /// <param name="byteOffset">A value from 0 to the byte length inclusive.</param>
    public int ToTextOffset(int byteOffset) => Map.ToTextOffset(byteOffset);

    /// <summary>
    /// Applies HTML 13.2.3.4 "changing the encoding while parsing", for a <c>&lt;meta&gt;</c> that declares
    /// <paramref name="newEncoding"/>. The tree builder calls this only for a meta element that carries a usable
    /// charset, and the answer tells it what to do:
    /// <list type="bullet">
    /// <item>The current confidence is already <see cref="EncodingConfidence.Certain"/>, the current encoding is UTF-16, or
    /// the declared encoding is the same one: nothing to redo. The result keeps the same text with confidence certain
    /// (<see cref="EncodingChange.RestartRequired"/> false).</item>
    /// <item>Otherwise the bytes are decoded again with the declared encoding (UTF-16 declarations become UTF-8,
    /// <c>x-user-defined</c> becomes <c>windows-1252</c>) at confidence certain, and the result asks for a restart:
    /// the parser discards its work and tokenizes <see cref="EncodingChange.Text"/> from the beginning. The parser
    /// never switches decoders in the middle of the input.</item>
    /// </list>
    /// </summary>
    /// <param name="newEncoding">The declared encoding.</param>
    /// <param name="cancellationToken">Checked periodically while decoding.</param>
    /// <exception cref="HtmlLimitExceededException">A limit was exceeded while decoding again.</exception>
    public EncodingChange ChangeEncoding(WhatwgEncoding newEncoding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newEncoding);
        if (Confidence == EncodingConfidence.Certain || Encoding.IsUtf16)
        {
            return new EncodingChange(WithCertainty(), false);
        }

        if (newEncoding.IsUtf16)
        {
            newEncoding = WhatwgEncoding.Utf8;
        }
        else if (newEncoding == WhatwgEncoding.XUserDefined)
        {
            newEncoding = WhatwgEncoding.Windows1252;
        }

        if (newEncoding == Encoding)
        {
            return new EncodingChange(WithCertainty(), false);
        }

        return new EncodingChange(DecodeCore(Bytes, newEncoding, EncodingConfidence.Certain, 0, Limits, cancellationToken), true);
    }

    private SourceText WithCertainty() =>
        Confidence == EncodingConfidence.Certain
            ? this
            : new SourceText(Bytes, Text, Encoding, EncodingConfidence.Certain, Map, DecodeErrorCount, Limits);

    private static void CheckLength(int length, ParseLimits limits)
    {
        if (length > limits.MaxInputLength)
        {
            throw new HtmlLimitExceededException(nameof(ParseLimits.MaxInputLength), limits.MaxInputLength, length);
        }
    }

    private static SourceText DecodeCore(
        ReadOnlyMemory<byte> bytes,
        WhatwgEncoding encoding,
        EncodingConfidence confidence,
        int bomLength,
        ParseLimits limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> body = bytes.Span[bomLength..];

        // No decoder produces more UTF-16 code units than it consumes bytes (the replacement encoding produces one
        // for any length), so the byte count bounds the buffer. The size was checked against the limit above.
        char[] buffer = ArrayPool<char>.Shared.Rent(Math.Max(body.Length, 1));
        try
        {
            var map = new OffsetMap(bomLength, limits.MaxOffsetMapRuns);
            var sink = new DecodeSink(buffer, map, cancellationToken);
            Decoders.Decode(encoding, body, sink);
            string text = new(buffer, 0, sink.Length);
            return new SourceText(bytes, text, encoding, confidence, map, sink.ErrorCount, limits);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }
}
