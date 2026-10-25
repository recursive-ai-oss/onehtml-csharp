using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Encodings;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>
/// Turns the characters an edit writes into bytes of the document's encoding. The library decodes with the Encoding
/// Standard's decoders only, so the reverse tables are built by running those decoders over every one- and two-byte
/// sequence once per encoding. A character with no byte form is reported by <see cref="CanEncode"/> so the markup
/// writer can use a numeric character reference instead.
/// </summary>
internal sealed class DocumentEncoder
{
    private static readonly ConcurrentDictionary<WhatwgEncoding, Dictionary<int, byte[]>> s_reverse = new();

    private readonly WhatwgEncoding _encoding;
    private readonly Dictionary<int, byte[]>? _map;

    private DocumentEncoder(WhatwgEncoding encoding, Dictionary<int, byte[]>? map)
    {
        _encoding = encoding;
        _map = map;
    }

    /// <summary>The encoding the bytes use.</summary>
    public WhatwgEncoding Encoding => _encoding;

    /// <summary>Creates an encoder, or returns null when the encoding cannot be written (the <c>replacement</c> encoding).</summary>
    public static DocumentEncoder? Create(WhatwgEncoding encoding)
    {
        switch (encoding.Kind)
        {
            case EncodingKind.Replacement:
                return null;
            case EncodingKind.Utf8:
            case EncodingKind.Utf16Be:
            case EncodingKind.Utf16Le:
            case EncodingKind.Iso2022Jp:
                return new DocumentEncoder(encoding, null);
            default:
                return new DocumentEncoder(encoding, s_reverse.GetOrAdd(encoding, BuildReverse));
        }
    }

    /// <summary>True when the code point has a byte form. ASCII always does (the markup itself is ASCII).</summary>
    public bool CanEncode(int codePoint)
    {
        if (codePoint < 0x80)
        {
            return true;
        }

        if (codePoint is >= 0xD800 and <= 0xDFFF)
        {
            return false;
        }

        return _encoding.Kind switch
        {
            EncodingKind.Utf8 or EncodingKind.Utf16Be or EncodingKind.Utf16Le => true,
            EncodingKind.Iso2022Jp => false,
            _ => _map!.ContainsKey(codePoint),
        };
    }

    /// <summary>Appends the bytes of <paramref name="text"/>, which must contain only encodable characters.</summary>
    public void Encode(ReadOnlySpan<char> text, IBufferWriter<byte> output)
    {
        switch (_encoding.Kind)
        {
            case EncodingKind.Utf8:
                {
                    Span<byte> span = output.GetSpan(System.Text.Encoding.UTF8.GetMaxByteCount(text.Length));
                    int n = System.Text.Encoding.UTF8.GetBytes(text, span);
                    output.Advance(n);
                    return;
                }

            case EncodingKind.Utf16Le:
            case EncodingKind.Utf16Be:
                {
                    Span<byte> span = output.GetSpan(checked(text.Length * 2));
                    bool be = _encoding.Kind == EncodingKind.Utf16Be;
                    for (int i = 0; i < text.Length; i++)
                    {
                        char c = text[i];
                        span[2 * i] = (byte)(be ? c >> 8 : c);
                        span[(2 * i) + 1] = (byte)(be ? c : c >> 8);
                    }

                    output.Advance(text.Length * 2);
                    return;
                }

            default:
                for (int i = 0; i < text.Length; i++)
                {
                    int cp = text[i];
                    if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        cp = char.ConvertToUtf32(text[i], text[i + 1]);
                        i++;
                    }

                    if (cp < 0x80)
                    {
                        output.GetSpan(1)[0] = (byte)cp;
                        output.Advance(1);
                    }
                    else if (_map is not null && _map.TryGetValue(cp, out byte[]? bytes))
                    {
                        bytes.CopyTo(output.GetSpan(bytes.Length));
                        output.Advance(bytes.Length);
                    }
                    else
                    {
                        throw new InvalidOperationException("A character with no byte form reached the encoder.");
                    }
                }

                return;
        }
    }

    // Decode every sequence of one or two bytes (what the single-byte and double-byte tables can hold) and invert the
    // result. Duplicates keep the first sequence found; any of them decodes back to the character.
    private static Dictionary<int, byte[]> BuildReverse(WhatwgEncoding encoding)
    {
        var map = new Dictionary<int, byte[]>();
        var one = new byte[1];
        for (int b = 0x80; b <= 0xFF; b++)
        {
            one[0] = (byte)b;
            if (TryDecodeOne(encoding, one, out int cp))
            {
                map.TryAdd(cp, [(byte)b]);
            }
        }

        if (encoding.Kind != EncodingKind.SingleByte && encoding.Kind != EncodingKind.XUserDefined)
        {
            var two = new byte[2];
            for (int lead = 0x81; lead <= 0xFE; lead++)
            {
                for (int trail = 0x40; trail <= 0xFE; trail++)
                {
                    two[0] = (byte)lead;
                    two[1] = (byte)trail;
                    if (TryDecodeOne(encoding, two, out int cp))
                    {
                        map.TryAdd(cp, [(byte)lead, (byte)trail]);
                    }
                }
            }
        }

        return map;
    }

    private static bool TryDecodeOne(WhatwgEncoding encoding, byte[] bytes, out int codePoint)
    {
        var buffer = new char[4];
        var map = new OffsetMap(0, 64);
        var sink = new DecodeSink(buffer, map, CancellationToken.None);
        Decoders.Decode(encoding, bytes, sink);
        codePoint = 0;
        if (sink.ErrorCount != 0 || sink.Length == 0)
        {
            return false;
        }

        if (sink.Length == 1)
        {
            codePoint = buffer[0];
        }
        else if (sink.Length == 2 && char.IsHighSurrogate(buffer[0]) && char.IsLowSurrogate(buffer[1]))
        {
            codePoint = char.ConvertToUtf32(buffer[0], buffer[1]);
        }
        else
        {
            return false;
        }

        // ASCII has its own byte; a second spelling of it would be wrong, and U+FFFD is the decoder's error mark.
        return codePoint >= 0x80 && codePoint != 0xFFFD && !(codePoint is >= 0xD800 and <= 0xDFFF);
    }
}
