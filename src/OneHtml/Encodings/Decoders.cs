using System;

namespace RecursiveAI.OneHtml.Encodings;

/// <summary>
/// The decoders of the Encoding Standard, written as loops over the whole input instead of one-byte handlers.
/// A byte the standard says to "restore to the queue" is simply not consumed. Every decoder reports each unit it
/// decodes, with the number of input bytes the unit used, to a <see cref="DecodeSink"/>.
/// </summary>
internal static class Decoders
{
    public static void Decode(WhatwgEncoding encoding, ReadOnlySpan<byte> source, DecodeSink sink)
    {
        switch (encoding.Kind)
        {
            case EncodingKind.Utf8:
                Utf8(source, sink);
                break;
            case EncodingKind.SingleByte:
                SingleByte(source, sink, EncodingTables.SingleByte(encoding.Table));
                break;
            case EncodingKind.Gbk:
            case EncodingKind.Gb18030:
                // Encoding Standard 10.1: GBK's decoder is gb18030's.
                Gb18030(source, sink);
                break;
            case EncodingKind.Big5:
                Big5(source, sink);
                break;
            case EncodingKind.EucJp:
                EucJp(source, sink);
                break;
            case EncodingKind.Iso2022Jp:
                Iso2022Jp(source, sink);
                break;
            case EncodingKind.ShiftJis:
                ShiftJis(source, sink);
                break;
            case EncodingKind.EucKr:
                EucKr(source, sink);
                break;
            case EncodingKind.Replacement:
                // Encoding Standard 14.1: one error for a non-empty input, then finished.
                if (!source.IsEmpty)
                {
                    sink.Error(source.Length);
                }

                break;
            case EncodingKind.Utf16Be:
                Utf16(source, sink, bigEndian: true);
                break;
            case EncodingKind.Utf16Le:
                Utf16(source, sink, bigEndian: false);
                break;
            case EncodingKind.XUserDefined:
                XUserDefined(source, sink);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(encoding));
        }
    }

    // Encoding Standard 8.1.1 UTF-8 decoder
    private static void Utf8(ReadOnlySpan<byte> s, DecodeSink k)
    {
        int n = s.Length;
        int i = 0;
        while (i < n)
        {
            i += k.AppendAscii(s[i..]);
            if (i >= n)
            {
                break;
            }

            int b = s[i];
            if (b < 0x80)
            {
                k.Emit(b, 1);
                i++;
                continue;
            }

            int need;
            int cp;
            int lower = 0x80;
            int upper = 0xBF;
            if (b is >= 0xC2 and <= 0xDF)
            {
                need = 1;
                cp = b & 0x1F;
            }
            else if (b is >= 0xE0 and <= 0xEF)
            {
                if (b == 0xE0)
                {
                    lower = 0xA0;
                }
                else if (b == 0xED)
                {
                    upper = 0x9F;
                }

                need = 2;
                cp = b & 0xF;
            }
            else if (b is >= 0xF0 and <= 0xF4)
            {
                if (b == 0xF0)
                {
                    lower = 0x90;
                }
                else if (b == 0xF4)
                {
                    upper = 0x8F;
                }

                need = 3;
                cp = b & 7;
            }
            else
            {
                k.Error(1);
                i++;
                continue;
            }

            int j = i + 1;
            bool ok = true;
            for (int seen = 0; seen < need; seen++)
            {
                // A truncated or invalid sequence is one error covering the bytes seen so far; the byte that broke
                // it is decoded again from scratch.
                if (j >= n || s[j] < lower || s[j] > upper)
                {
                    k.Error(j - i);
                    i = j;
                    ok = false;
                    break;
                }

                lower = 0x80;
                upper = 0xBF;
                cp = (cp << 6) | (s[j] & 0x3F);
                j++;
            }

            if (ok)
            {
                k.Emit(cp, need + 1);
                i = j;
            }
        }
    }

    // Encoding Standard 9.1 single-byte decoder
    private static void SingleByte(ReadOnlySpan<byte> s, DecodeSink k, ReadOnlySpan<ushort> index)
    {
        int n = s.Length;
        int i = 0;
        while (i < n)
        {
            i += k.AppendAscii(s[i..]);
            if (i >= n)
            {
                break;
            }

            int b = s[i++];
            if (b < 0x80)
            {
                k.Emit(b, 1);
                continue;
            }

            ushort cp = index[b - 0x80];
            if (cp == EncodingTables.None16)
            {
                k.Error(1);
            }
            else
            {
                k.Emit(cp, 1);
            }
        }
    }

    // Encoding Standard 14.5 x-user-defined decoder
    private static void XUserDefined(ReadOnlySpan<byte> s, DecodeSink k)
    {
        int n = s.Length;
        int i = 0;
        while (i < n)
        {
            i += k.AppendAscii(s[i..]);
            if (i >= n)
            {
                break;
            }

            int b = s[i++];
            k.Emit(b < 0x80 ? b : 0xF780 + b - 0x80, 1);
        }
    }

    // Encoding Standard 10.2.1 gb18030 decoder (also GBK's)
    private static void Gb18030(ReadOnlySpan<byte> s, DecodeSink k)
    {
        int n = s.Length;
        int i = 0;
        ReadOnlySpan<ushort> index = EncodingTables.Gb18030;
        while (i < n)
        {
            i += k.AppendAscii(s[i..]);
            if (i >= n)
            {
                break;
            }

            int b = s[i];
            if (b < 0x80)
            {
                k.Emit(b, 1);
                i++;
                continue;
            }

            if (b == 0x80)
            {
                k.Emit(0x20AC, 1);
                i++;
                continue;
            }

            if (b == 0xFF)
            {
                k.Error(1);
                i++;
                continue;
            }

            // A lead byte 0x81 to 0xFE. The input ending anywhere in a sequence is one error for the pending bytes.
            if (i + 1 >= n)
            {
                k.Error(n - i);
                i = n;
                continue;
            }

            int b2 = s[i + 1];
            if (b2 is >= 0x30 and <= 0x39)
            {
                if (i + 2 >= n)
                {
                    k.Error(n - i);
                    i = n;
                    continue;
                }

                int b3 = s[i + 2];
                if (b3 is < 0x81 or > 0xFE)
                {
                    k.Error(1);
                    i++;
                    continue;
                }

                if (i + 3 >= n)
                {
                    k.Error(n - i);
                    i = n;
                    continue;
                }

                int b4 = s[i + 3];
                if (b4 is < 0x30 or > 0x39)
                {
                    k.Error(1);
                    i++;
                    continue;
                }

                int pointer = ((b - 0x81) * 10 * 126 * 10) + ((b2 - 0x30) * 10 * 126) + ((b3 - 0x81) * 10) + (b4 - 0x30);
                int cp = RangesCodePoint(pointer);
                if (cp < 0)
                {
                    k.Error(4);
                }
                else
                {
                    k.Emit(cp, 4);
                }

                i += 4;
                continue;
            }

            int offset = b2 < 0x7F ? 0x40 : 0x41;
            if (b2 is (>= 0x40 and <= 0x7E) or (>= 0x80 and <= 0xFE))
            {
                ushort cp = index[((b - 0x81) * 190) + (b2 - offset)];
                if (cp != EncodingTables.None16)
                {
                    k.Emit(cp, 2);
                    i += 2;
                    continue;
                }
            }

            if (b2 < 0x80)
            {
                k.Error(1);
                i++;
            }
            else
            {
                k.Error(2);
                i += 2;
            }
        }
    }

    // Encoding Standard 5 "index gb18030 ranges code point"
    private static int RangesCodePoint(int pointer)
    {
        if ((pointer > 39419 && pointer < 189000) || pointer > 1237575)
        {
            return -1;
        }

        if (pointer == 7457)
        {
            return 0xE7C7;
        }

        ReadOnlySpan<uint> pointers = EncodingTables.Gb18030RangePointers;
        int lo = 0;
        int hi = pointers.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >>> 1;
            if (pointers[mid] <= (uint)pointer)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return (int)(EncodingTables.Gb18030RangeCodePoints[lo] + (uint)pointer - pointers[lo]);
    }

    // Encoding Standard 11.1.1 Big5 decoder
    private static void Big5(ReadOnlySpan<byte> s, DecodeSink k)
    {
        int n = s.Length;
        int i = 0;
        ReadOnlySpan<uint> index = EncodingTables.Big5;
        while (i < n)
        {
            i += k.AppendAscii(s[i..]);
            if (i >= n)
            {
                break;
            }

            int b = s[i];
            if (b < 0x80)
            {
                k.Emit(b, 1);
                i++;
                continue;
            }

            if (b is < 0x81 or > 0xFE)
            {
                k.Error(1);
                i++;
                continue;
            }

            if (i + 1 >= n)
            {
                k.Error(1);
                i++;
                continue;
            }

            int b2 = s[i + 1];
            if (b2 is (>= 0x40 and <= 0x7E) or (>= 0xA1 and <= 0xFE))
            {
                int offset = b2 < 0x7F ? 0x40 : 0x62;
                int pointer = ((b - 0x81) * 157) + (b2 - offset);
                switch (pointer)
                {
                    case 1133:
                        k.EmitPair(0x00CA, 0x0304, 2);
                        i += 2;
                        continue;
                    case 1135:
                        k.EmitPair(0x00CA, 0x030C, 2);
                        i += 2;
                        continue;
                    case 1164:
                        k.EmitPair(0x00EA, 0x0304, 2);
                        i += 2;
                        continue;
                    case 1166:
                        k.EmitPair(0x00EA, 0x030C, 2);
                        i += 2;
                        continue;
                }

                uint cp = index[pointer];
                if (cp != EncodingTables.None32)
                {
                    k.Emit((int)cp, 2);
                    i += 2;
                    continue;
                }
            }

            if (b2 < 0x80)
            {
                k.Error(1);
                i++;
            }
            else
            {
                k.Error(2);
                i += 2;
            }
        }
    }

    // Encoding Standard 12.1.1 EUC-JP decoder
    private static void EucJp(ReadOnlySpan<byte> s, DecodeSink k)
    {
        int n = s.Length;
        int i = 0;
        ReadOnlySpan<ushort> jis0208 = EncodingTables.Jis0208;
        ReadOnlySpan<ushort> jis0212 = EncodingTables.Jis0212;
        while (i < n)
        {
            i += k.AppendAscii(s[i..]);
            if (i >= n)
            {
                break;
            }

            int b = s[i];
            if (b < 0x80)
            {
                k.Emit(b, 1);
                i++;
                continue;
            }

            if (b != 0x8E && b != 0x8F && b is < 0xA1 or > 0xFE)
            {
                k.Error(1);
                i++;
                continue;
            }

            if (i + 1 >= n)
            {
                k.Error(1);
                i++;
                continue;
            }

            int b2 = s[i + 1];
            if (b == 0x8E && b2 is >= 0xA1 and <= 0xDF)
            {
                k.Emit(0xFF61 - 0xA1 + b2, 2);
                i += 2;
                continue;
            }

            if (b == 0x8F && b2 is >= 0xA1 and <= 0xFE)
            {
                // A three-byte JIS X 0212 sequence; the input ending after two bytes is one error.
                if (i + 2 >= n)
                {
                    k.Error(n - i);
                    i = n;
                    continue;
                }

                int b3 = s[i + 2];
                if (b3 is >= 0xA1 and <= 0xFE)
                {
                    ushort cp = jis0212[((b2 - 0xA1) * 94) + b3 - 0xA1];
                    if (cp != EncodingTables.None16)
                    {
                        k.Emit(cp, 3);
                        i += 3;
                        continue;
                    }
                }

                if (b3 < 0x80)
                {
                    k.Error(2);
                    i += 2;
                }
                else
                {
                    k.Error(3);
                    i += 3;
                }

                continue;
            }

            if (b is >= 0xA1 and <= 0xFE && b2 is >= 0xA1 and <= 0xFE)
            {
                ushort cp = jis0208[((b - 0xA1) * 94) + b2 - 0xA1];
                if (cp != EncodingTables.None16)
                {
                    k.Emit(cp, 2);
                    i += 2;
                    continue;
                }
            }

            if (b2 < 0x80)
            {
                k.Error(1);
                i++;
            }
            else
            {
                k.Error(2);
                i += 2;
            }
        }
    }

    // Encoding Standard 12.3.1 Shift_JIS decoder
    private static void ShiftJis(ReadOnlySpan<byte> s, DecodeSink k)
    {
        int n = s.Length;
        int i = 0;
        ReadOnlySpan<ushort> jis0208 = EncodingTables.Jis0208;
        while (i < n)
        {
            i += k.AppendAscii(s[i..]);
            if (i >= n)
            {
                break;
            }

            int b = s[i];
            if (b <= 0x80)
            {
                k.Emit(b, 1);
                i++;
                continue;
            }

            if (b is >= 0xA1 and <= 0xDF)
            {
                k.Emit(0xFF61 - 0xA1 + b, 1);
                i++;
                continue;
            }

            if (b is not ((>= 0x81 and <= 0x9F) or (>= 0xE0 and <= 0xFC)))
            {
                k.Error(1);
                i++;
                continue;
            }

            if (i + 1 >= n)
            {
                k.Error(1);
                i++;
                continue;
            }

            int b2 = s[i + 1];
            if (b2 is (>= 0x40 and <= 0x7E) or (>= 0x80 and <= 0xFC))
            {
                int offset = b2 < 0x7F ? 0x40 : 0x41;
                int leadOffset = b < 0xA0 ? 0x81 : 0xC1;
                int pointer = ((b - leadOffset) * 188) + b2 - offset;
                if (pointer is >= 8836 and <= 10715)
                {
                    k.Emit(0xE000 - 8836 + pointer, 2);
                    i += 2;
                    continue;
                }

                ushort cp = jis0208[pointer];
                if (cp != EncodingTables.None16)
                {
                    k.Emit(cp, 2);
                    i += 2;
                    continue;
                }
            }

            if (b2 < 0x80)
            {
                k.Error(1);
                i++;
            }
            else
            {
                k.Error(2);
                i += 2;
            }
        }
    }

    // Encoding Standard 13.1.1 EUC-KR decoder
    private static void EucKr(ReadOnlySpan<byte> s, DecodeSink k)
    {
        int n = s.Length;
        int i = 0;
        ReadOnlySpan<ushort> index = EncodingTables.EucKr;
        while (i < n)
        {
            i += k.AppendAscii(s[i..]);
            if (i >= n)
            {
                break;
            }

            int b = s[i];
            if (b < 0x80)
            {
                k.Emit(b, 1);
                i++;
                continue;
            }

            if (b is < 0x81 or > 0xFE || i + 1 >= n)
            {
                k.Error(1);
                i++;
                continue;
            }

            int b2 = s[i + 1];
            if (b2 is >= 0x41 and <= 0xFE)
            {
                ushort cp = index[((b - 0x81) * 190) + (b2 - 0x41)];
                if (cp != EncodingTables.None16)
                {
                    k.Emit(cp, 2);
                    i += 2;
                    continue;
                }
            }

            if (b2 < 0x80)
            {
                k.Error(1);
                i++;
            }
            else
            {
                k.Error(2);
                i += 2;
            }
        }
    }

    private const int StateAscii = 0;
    private const int StateRoman = 1;
    private const int StateKatakana = 2;
    private const int StateLead = 3;
    private const int StateTrail = 4;
    private const int StateEscapeStart = 5;
    private const int StateEscape = 6;

    // Encoding Standard 12.2.1 ISO-2022-JP decoder. "unit" is the first input byte not yet reported to the sink.
    private static void Iso2022Jp(ReadOnlySpan<byte> s, DecodeSink k)
    {
        int n = s.Length;
        int i = 0;
        int unit = 0;
        int state = StateAscii;
        int outputState = StateAscii;
        int lead = 0;
        bool output = false;
        ReadOnlySpan<ushort> jis0208 = EncodingTables.Jis0208;
        while (true)
        {
            if (i >= n)
            {
                // The end of the input: only the states holding unfinished bytes still have something to report.
                if (state == StateTrail)
                {
                    state = StateLead;
                    k.Error(i - unit);
                    unit = i;
                }
                else if (state == StateEscapeStart)
                {
                    output = false;
                    state = outputState;
                    k.Error(i - unit);
                    unit = i;
                }
                else if (state == StateEscape)
                {
                    i--;
                    output = false;
                    state = outputState;
                    k.Error(i - unit);
                    unit = i;
                }
                else
                {
                    return;
                }

                continue;
            }

            int b = s[i++];
            switch (state)
            {
                case StateAscii:
                    if (b == 0x1B)
                    {
                        state = StateEscapeStart;
                    }
                    else if (b <= 0x7F && b != 0x0E && b != 0x0F)
                    {
                        output = false;
                        k.Emit(b, i - unit);
                        unit = i;
                    }
                    else
                    {
                        output = false;
                        k.Error(i - unit);
                        unit = i;
                    }

                    break;
                case StateRoman:
                    if (b == 0x1B)
                    {
                        state = StateEscapeStart;
                    }
                    else if (b <= 0x7F && b != 0x0E && b != 0x0F)
                    {
                        output = false;
                        k.Emit(b == 0x5C ? 0xA5 : b == 0x7E ? 0x203E : b, i - unit);
                        unit = i;
                    }
                    else
                    {
                        output = false;
                        k.Error(i - unit);
                        unit = i;
                    }

                    break;
                case StateKatakana:
                    if (b == 0x1B)
                    {
                        state = StateEscapeStart;
                    }
                    else if (b is >= 0x21 and <= 0x5F)
                    {
                        output = false;
                        k.Emit(0xFF61 - 0x21 + b, i - unit);
                        unit = i;
                    }
                    else
                    {
                        output = false;
                        k.Error(i - unit);
                        unit = i;
                    }

                    break;
                case StateLead:
                    if (b == 0x1B)
                    {
                        state = StateEscapeStart;
                    }
                    else if (b is >= 0x21 and <= 0x7E)
                    {
                        output = false;
                        lead = b;
                        state = StateTrail;
                    }
                    else
                    {
                        output = false;
                        k.Error(i - unit);
                        unit = i;
                    }

                    break;
                case StateTrail:
                    if (b == 0x1B)
                    {
                        state = StateEscapeStart;
                        k.Error(i - unit);
                        unit = i;
                    }
                    else if (b is >= 0x21 and <= 0x7E)
                    {
                        state = StateLead;
                        ushort cp = jis0208[((lead - 0x21) * 94) + b - 0x21];
                        if (cp == EncodingTables.None16)
                        {
                            k.Error(i - unit);
                        }
                        else
                        {
                            k.Emit(cp, i - unit);
                        }

                        unit = i;
                    }
                    else
                    {
                        state = StateLead;
                        k.Error(i - unit);
                        unit = i;
                    }

                    break;
                case StateEscapeStart:
                    if (b is 0x24 or 0x28)
                    {
                        lead = b;
                        state = StateEscape;
                    }
                    else
                    {
                        i--;
                        output = false;
                        state = outputState;
                        k.Error(i - unit);
                        unit = i;
                    }

                    break;
                default:
                    {
                        int l = lead;
                        lead = 0;
                        int next = -1;
                        if (l == 0x28)
                        {
                            next = b switch { 0x42 => StateAscii, 0x4A => StateRoman, 0x49 => StateKatakana, _ => -1 };
                        }
                        else if (b is 0x40 or 0x42)
                        {
                            next = StateLead;
                        }

                        if (next >= 0)
                        {
                            state = next;
                            outputState = next;
                            bool hadOutput = output;
                            output = true;
                            if (hadOutput)
                            {
                                k.Error(i - unit);
                            }
                            else
                            {
                                k.Skip(i - unit);
                            }

                            unit = i;
                        }
                        else
                        {
                            // Not an escape sequence: the escape byte is an error and the two bytes after it are read again.
                            i -= 2;
                            output = false;
                            state = outputState;
                            k.Error(i - unit);
                            unit = i;
                        }

                        break;
                    }
            }
        }
    }

    // Encoding Standard 14.2 / 14.3 / 14.4 UTF-16BE and UTF-16LE decoders
    private static void Utf16(ReadOnlySpan<byte> s, DecodeSink k, bool bigEndian)
    {
        int n = s.Length;
        int i = 0;
        while (i < n)
        {
            if (i + 1 >= n)
            {
                k.Error(1);
                break;
            }

            int unit = bigEndian ? (s[i] << 8) | s[i + 1] : (s[i + 1] << 8) | s[i];
            if (unit is >= 0xD800 and <= 0xDBFF)
            {
                if (i + 3 >= n)
                {
                    // The input ends after the lead surrogate (and perhaps half of the next unit): one error.
                    k.Error(n - i);
                    break;
                }

                int next = bigEndian ? (s[i + 2] << 8) | s[i + 3] : (s[i + 3] << 8) | s[i + 2];
                if (next is >= 0xDC00 and <= 0xDFFF)
                {
                    k.Emit(0x10000 + ((unit - 0xD800) << 10) + (next - 0xDC00), 4);
                    i += 4;
                }
                else
                {
                    k.Error(2);
                    i += 2;
                }
            }
            else if (unit is >= 0xDC00 and <= 0xDFFF)
            {
                k.Error(2);
                i += 2;
            }
            else
            {
                k.Emit(unit, 2);
                i += 2;
            }
        }
    }
}
