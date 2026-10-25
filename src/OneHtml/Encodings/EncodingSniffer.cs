using System;
using System.Text;

namespace RecursiveAI.OneHtml.Encodings;

/// <summary>How sure the parser is of the encoding it decoded with (HTML 13.2.3.2).</summary>
public enum EncodingConfidence
{
    /// <summary>The encoding is a guess. A later <c>&lt;meta&gt;</c> declaration may change it; see <see cref="SourceText.ChangeEncoding"/>.</summary>
    Tentative,

    /// <summary>The encoding is definite: a byte order mark, the transport layer, or an earlier change of encoding.</summary>
    Certain,
}

/// <summary>The outcome of <see cref="EncodingSniffer.Sniff"/>.</summary>
/// <param name="Encoding">The encoding to decode with.</param>
/// <param name="Confidence">Whether it is certain or tentative.</param>
/// <param name="BomLength">The length in bytes of the byte order mark that decided it, or 0.</param>
public readonly record struct EncodingSniffResult(WhatwgEncoding Encoding, EncodingConfidence Confidence, int BomLength);

/// <summary>
/// The HTML encoding sniffing algorithm (HTML 13.2.3.2): byte order mark, then the transport layer's
/// <c>Content-Type</c> charset, then a prescan for a <c>&lt;meta&gt;</c> declaration, then a default.
/// </summary>
public static class EncodingSniffer
{
    /// <summary>The number of bytes the prescan reads by default; the spec encourages 1024.</summary>
    public const int DefaultPrescanLimit = 1024;

    /// <summary>
    /// Runs the encoding sniffing algorithm.
    /// </summary>
    /// <param name="bytes">The start of the document; any length, including empty.</param>
    /// <param name="contentType">
    /// The transport <c>Content-Type</c> header value, or <see langword="null"/>. Only a <c>charset</c> parameter
    /// that names a known encoding counts; anything else is ignored.
    /// </param>
    /// <param name="defaultEncoding">The encoding to use when nothing else decides; <c>windows-1252</c> when <see langword="null"/>.</param>
    /// <param name="prescanLimit">How many bytes the prescan may read. Default 1024; values below 0 are treated as 0.</param>
    public static EncodingSniffResult Sniff(
        ReadOnlySpan<byte> bytes,
        string? contentType = null,
        WhatwgEncoding? defaultEncoding = null,
        int prescanLimit = DefaultPrescanLimit)
    {
        WhatwgEncoding? bom = SniffBom(bytes, out int bomLength);
        if (bom is not null)
        {
            return new EncodingSniffResult(bom, EncodingConfidence.Certain, bomLength);
        }

        WhatwgEncoding? transport = ExtractFromContentType(contentType);
        if (transport is not null)
        {
            return new EncodingSniffResult(transport, EncodingConfidence.Certain, 0);
        }

        WhatwgEncoding? scanned = Prescan(bytes, prescanLimit);
        if (scanned is not null)
        {
            return new EncodingSniffResult(scanned, EncodingConfidence.Tentative, 0);
        }

        return new EncodingSniffResult(defaultEncoding ?? WhatwgEncoding.Windows1252, EncodingConfidence.Tentative, 0);
    }

    /// <summary>BOM sniffing (Encoding Standard 4.3 "BOM sniff"). Returns <see langword="null"/> when there is no byte order mark.</summary>
    /// <param name="bytes">The start of the document.</param>
    /// <param name="bomLength">The length of the mark: 3 for UTF-8, 2 for UTF-16, 0 for none.</param>
    public static WhatwgEncoding? SniffBom(ReadOnlySpan<byte> bytes, out int bomLength)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bomLength = 3;
            return WhatwgEncoding.Utf8;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            bomLength = 2;
            return WhatwgEncoding.Utf16Be;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            bomLength = 2;
            return WhatwgEncoding.Utf16Le;
        }

        bomLength = 0;
        return null;
    }

    /// <summary>
    /// Extracts the encoding named by the <c>charset</c> parameter of a <c>Content-Type</c> header value.
    /// The first <c>charset</c> parameter counts; its value may be a quoted string. Returns
    /// <see langword="null"/> when there is none or it names no known encoding.
    /// </summary>
    /// <param name="contentType">The header value, or <see langword="null"/>.</param>
    public static WhatwgEncoding? ExtractFromContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return null;
        }

        ReadOnlySpan<char> s = contentType;
        int pos = s.IndexOf(';');
        if (pos < 0)
        {
            return null;
        }

        // Parameters, as in the MIME Sniffing Standard's "parse a MIME type": name=value pairs separated by
        // semicolons; the value is a token or a quoted string; the first parameter of a given name wins.
        while (pos < s.Length)
        {
            pos++; // the semicolon
            while (pos < s.Length && IsHttpWhitespace(s[pos]))
            {
                pos++;
            }

            int nameStart = pos;
            while (pos < s.Length && s[pos] != ';' && s[pos] != '=')
            {
                pos++;
            }

            ReadOnlySpan<char> name = s[nameStart..pos];
            if (pos >= s.Length || s[pos] == ';')
            {
                continue;
            }

            pos++; // the equals sign
            string? quoted = null;
            ReadOnlySpan<char> value;
            if (pos < s.Length && s[pos] == '"')
            {
                var sb = new StringBuilder();
                pos++;
                while (pos < s.Length && s[pos] != '"')
                {
                    if (s[pos] == '\\' && pos + 1 < s.Length)
                    {
                        pos++;
                    }

                    sb.Append(s[pos]);
                    pos++;
                }

                if (pos < s.Length)
                {
                    pos++; // the closing quote
                }

                while (pos < s.Length && s[pos] != ';')
                {
                    pos++;
                }

                quoted = sb.ToString();
                value = quoted;
            }
            else
            {
                int valueStart = pos;
                while (pos < s.Length && s[pos] != ';')
                {
                    pos++;
                }

                value = s[valueStart..pos];
            }

            if (name.Equals("charset", StringComparison.OrdinalIgnoreCase))
            {
                ReadOnlySpan<char> trimmed = value.Trim(" \t\r\n");
                if (!trimmed.IsEmpty)
                {
                    return WhatwgEncoding.ForLabel(trimmed);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The algorithm for extracting a character encoding from a <c>meta</c> element (HTML 2.5.3): finds
    /// <c>charset=</c> in the value of a <c>content</c> attribute, such as <c>text/html; charset=utf-8</c>.
    /// </summary>
    /// <param name="value">The attribute value.</param>
    /// <returns>The encoding, or <see langword="null"/> for "nothing".</returns>
    public static WhatwgEncoding? ExtractFromMetaContent(ReadOnlySpan<char> value)
    {
        int position = 0;
        while (true)
        {
            // "Loop": the first seven characters after position that match "charset", ASCII case-insensitively.
            int found = value[position..].IndexOf("charset", StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return null;
            }

            position += found + 7;
            while (position < value.Length && IsAsciiWhitespace(value[position]))
            {
                position++;
            }

            if (position >= value.Length || value[position] != '=')
            {
                // Point just before the next character and search again.
                continue;
            }

            position++;
            while (position < value.Length && IsAsciiWhitespace(value[position]))
            {
                position++;
            }

            if (position >= value.Length)
            {
                return null;
            }

            char c = value[position];
            if (c is '"' or '\'')
            {
                int close = value[(position + 1)..].IndexOf(c);
                if (close < 0)
                {
                    return null;
                }

                return WhatwgEncoding.ForLabel(value.Slice(position + 1, close));
            }

            int end = position;
            while (end < value.Length && !IsAsciiWhitespace(value[end]) && value[end] != ';')
            {
                end++;
            }

            return WhatwgEncoding.ForLabel(value[position..end]);
        }
    }

    /// <summary>
    /// Prescans the start of a byte stream for an encoding declaration (HTML 13.2.3.2 "prescan a byte stream to
    /// determine its encoding"), including the UTF-16 and <c>&lt;?xml encoding=...?&gt;</c> fallbacks.
    /// </summary>
    /// <param name="input">The document bytes.</param>
    /// <param name="limit">How many bytes to read at most; the spec's "end condition". Default 1024.</param>
    /// <returns>The declared encoding, or <see langword="null"/> when the prescan finds none.</returns>
    public static WhatwgEncoding? Prescan(ReadOnlySpan<byte> input, int limit = DefaultPrescanLimit)
    {
        ReadOnlySpan<byte> b = input.Length > limit ? input[..Math.Max(limit, 0)] : input;

        // Prescan for UTF-16 XML declarations.
        if (b.StartsWith("<\0?\0x\0"u8))
        {
            return WhatwgEncoding.Utf16Le;
        }

        if (b.StartsWith("\0<\0?\0x"u8))
        {
            return WhatwgEncoding.Utf16Be;
        }

        int pos = 0;
        while (pos < b.Length)
        {
            if (b[pos] != 0x3C)
            {
                pos++;
                continue;
            }

            if (b[pos..].StartsWith("<!--"u8))
            {
                // Up to the first '>' preceded by two hyphens that comes after the '<'; the hyphens of "<!--" may be shared.
                int end = b[(pos + 2)..].IndexOf("-->"u8);
                if (end < 0)
                {
                    break;
                }

                pos += 2 + end + 3;
                continue;
            }

            if (pos + 5 < b.Length && IsMetaStart(b[pos..]))
            {
                pos += 5;
                bool gotPragma = false;
                bool needPragmaSet = false;
                bool needPragma = false;
                bool charsetSet = false;
                WhatwgEncoding? charset = null;
                bool seenHttpEquiv = false;
                bool seenContent = false;
                bool seenCharset = false;
                while (true)
                {
                    AttributeResult result = GetAttribute(b, ref pos, out Attribute attribute);
                    if (result == AttributeResult.OutOfBytes)
                    {
                        return GetXmlEncoding(b);
                    }

                    if (result == AttributeResult.None)
                    {
                        break;
                    }

                    ReadOnlySpan<byte> name = b[attribute.NameStart..attribute.NameEnd];
                    ReadOnlySpan<byte> value = b[attribute.ValueStart..attribute.ValueEnd];
                    if (Ascii.EqualsIgnoreCase(name, "http-equiv"u8))
                    {
                        if (!seenHttpEquiv)
                        {
                            seenHttpEquiv = true;
                            if (Ascii.EqualsIgnoreCase(value, "content-type"u8))
                            {
                                gotPragma = true;
                            }
                        }
                    }
                    else if (Ascii.EqualsIgnoreCase(name, "content"u8))
                    {
                        if (!seenContent)
                        {
                            seenContent = true;
                            if (!charsetSet)
                            {
                                WhatwgEncoding? fromContent = ExtractFromMetaContentBytes(value);
                                if (fromContent is not null)
                                {
                                    charset = fromContent;
                                    charsetSet = true;
                                    needPragma = true;
                                    needPragmaSet = true;
                                }
                            }
                        }
                    }
                    else if (Ascii.EqualsIgnoreCase(name, "charset"u8))
                    {
                        if (!seenCharset)
                        {
                            seenCharset = true;
                            charset = ForLabelBytes(value);
                            charsetSet = true;
                            needPragma = false;
                            needPragmaSet = true;
                        }
                    }
                }

                if (needPragmaSet && (!needPragma || gotPragma) && charset is not null)
                {
                    if (charset.IsUtf16)
                    {
                        return WhatwgEncoding.Utf8;
                    }

                    return charset == WhatwgEncoding.XUserDefined ? WhatwgEncoding.Windows1252 : charset;
                }

                pos++;
                continue;
            }

            if (pos + 1 < b.Length && (IsAsciiLetter(b[pos + 1]) || (b[pos + 1] == 0x2F && pos + 2 < b.Length && IsAsciiLetter(b[pos + 2]))))
            {
                pos += b[pos + 1] == 0x2F ? 2 : 1;
                while (pos < b.Length && !IsWhitespace(b[pos]) && b[pos] != 0x3E)
                {
                    pos++;
                }

                bool outOfBytes = false;
                while (true)
                {
                    AttributeResult result = GetAttribute(b, ref pos, out _);
                    if (result == AttributeResult.OutOfBytes)
                    {
                        outOfBytes = true;
                        break;
                    }

                    if (result == AttributeResult.None)
                    {
                        break;
                    }
                }

                if (outOfBytes)
                {
                    break;
                }

                pos++;
                continue;
            }

            if (pos + 1 < b.Length && b[pos + 1] is 0x21 or 0x2F or 0x3F)
            {
                int end = b[pos..].IndexOf((byte)0x3E);
                if (end < 0)
                {
                    break;
                }

                pos += end + 1;
                continue;
            }

            pos++;
        }

        return GetXmlEncoding(b);
    }

    // Bytes above 0x7F cannot be part of a label, so widening each byte to the char of the same value is enough.
    private static WhatwgEncoding? ForLabelBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > 64)
        {
            return null;
        }

        Span<char> buffer = stackalloc char[64];
        for (int i = 0; i < bytes.Length; i++)
        {
            buffer[i] = (char)bytes[i];
        }

        return WhatwgEncoding.ForLabel(buffer[..bytes.Length]);
    }

    private static WhatwgEncoding? ExtractFromMetaContentBytes(ReadOnlySpan<byte> bytes)
    {
        // The prescan reads at most its limit (1024 bytes by default), so a small stack buffer covers real input.
        Span<char> buffer = bytes.Length <= 512 ? stackalloc char[512] : new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            buffer[i] = (char)bytes[i];
        }

        return ExtractFromMetaContent(buffer[..bytes.Length]);
    }

    private static bool IsMetaStart(ReadOnlySpan<byte> b) =>
        b[0] == 0x3C
        && (b[1] | 0x20) == 'm' && (b[2] | 0x20) == 'e' && (b[3] | 0x20) == 't' && (b[4] | 0x20) == 'a'
        && (IsWhitespace(b[5]) || b[5] == 0x2F);

    private static bool IsAsciiLetter(byte c) => (uint)((c | 0x20) - 'a') <= 'z' - 'a';

    private static bool IsWhitespace(byte c) => c is 0x09 or 0x0A or 0x0C or 0x0D or 0x20;

    private static bool IsAsciiWhitespace(char c) => c is '\t' or '\n' or '\f' or '\r' or ' ';

    private static bool IsHttpWhitespace(char c) => c is '\t' or '\n' or '\r' or ' ';

    // HTML 13.2.3.2 "get an XML encoding"
    private static WhatwgEncoding? GetXmlEncoding(ReadOnlySpan<byte> b)
    {
        if (!b.StartsWith("<?xml"u8))
        {
            return null;
        }

        int declarationEnd = b.IndexOf((byte)0x3E);
        if (declarationEnd < 0)
        {
            return null;
        }

        ReadOnlySpan<byte> declaration = b[..declarationEnd];
        int at = declaration.IndexOf("encoding"u8);
        if (at < 0)
        {
            return null;
        }

        int pos = at + 8;
        while (pos < declaration.Length && declaration[pos] <= 0x20)
        {
            pos++;
        }

        if (pos >= declaration.Length || declaration[pos] != 0x3D)
        {
            return null;
        }

        pos++;
        while (pos < declaration.Length && declaration[pos] <= 0x20)
        {
            pos++;
        }

        if (pos >= declaration.Length || declaration[pos] is not (0x22 or 0x27))
        {
            return null;
        }

        byte quote = declaration[pos];
        pos++;
        int close = declaration[pos..].IndexOf(quote);
        if (close < 0)
        {
            return null;
        }

        ReadOnlySpan<byte> potential = declaration.Slice(pos, close);
        foreach (byte c in potential)
        {
            if (c <= 0x20)
            {
                return null;
            }
        }

        WhatwgEncoding? encoding = ForLabelBytes(potential);
        return encoding is not null && encoding.IsUtf16 ? WhatwgEncoding.Utf8 : encoding;
    }

    private enum AttributeResult
    {
        Found,
        None,
        OutOfBytes,
    }

    private struct Attribute
    {
        public int NameStart;
        public int NameEnd;
        public int ValueStart;
        public int ValueEnd;
    }

    // HTML 13.2.3.2 "get an attribute". The name and value are ranges of the input; the algorithm's only
    // transformation is ASCII lowercasing, which callers do by comparing ignoring case.
    private static AttributeResult GetAttribute(ReadOnlySpan<byte> b, ref int pos, out Attribute attribute)
    {
        attribute = default;
        while (true)
        {
            if (pos >= b.Length)
            {
                return AttributeResult.OutOfBytes;
            }

            if (IsWhitespace(b[pos]) || b[pos] == 0x2F)
            {
                pos++;
                continue;
            }

            break;
        }

        if (b[pos] == 0x3E)
        {
            return AttributeResult.None;
        }

        attribute.NameStart = pos;
        bool hasValue = false;
        while (true)
        {
            if (pos >= b.Length)
            {
                return AttributeResult.OutOfBytes;
            }

            byte c = b[pos];
            if (c == 0x3D && pos > attribute.NameStart)
            {
                attribute.NameEnd = pos;
                pos++;
                hasValue = true;
                break;
            }

            if (IsWhitespace(c))
            {
                attribute.NameEnd = pos;
                while (pos < b.Length && IsWhitespace(b[pos]))
                {
                    pos++;
                }

                if (pos >= b.Length)
                {
                    return AttributeResult.OutOfBytes;
                }

                if (b[pos] != 0x3D)
                {
                    attribute.ValueStart = attribute.ValueEnd = pos;
                    return AttributeResult.Found;
                }

                pos++;
                hasValue = true;
                break;
            }

            if (c is 0x2F or 0x3E)
            {
                attribute.NameEnd = pos;
                attribute.ValueStart = attribute.ValueEnd = pos;
                return AttributeResult.Found;
            }

            pos++;
        }

        _ = hasValue;

        // "Value"
        while (true)
        {
            if (pos >= b.Length)
            {
                return AttributeResult.OutOfBytes;
            }

            if (IsWhitespace(b[pos]))
            {
                pos++;
                continue;
            }

            break;
        }

        byte first = b[pos];
        if (first is 0x22 or 0x27)
        {
            pos++;
            attribute.ValueStart = pos;
            while (true)
            {
                if (pos >= b.Length)
                {
                    return AttributeResult.OutOfBytes;
                }

                if (b[pos] == first)
                {
                    attribute.ValueEnd = pos;
                    pos++;
                    return AttributeResult.Found;
                }

                pos++;
            }
        }

        if (first == 0x3E)
        {
            attribute.ValueStart = attribute.ValueEnd = pos;
            return AttributeResult.Found;
        }

        attribute.ValueStart = pos;
        while (true)
        {
            if (pos >= b.Length)
            {
                return AttributeResult.OutOfBytes;
            }

            if (IsWhitespace(b[pos]) || b[pos] == 0x3E)
            {
                attribute.ValueEnd = pos;
                return AttributeResult.Found;
            }

            pos++;
        }
    }
}
