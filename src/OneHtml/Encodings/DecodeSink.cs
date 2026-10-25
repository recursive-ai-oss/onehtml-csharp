using System;
using System.Text;
using System.Threading;

namespace RecursiveAI.OneHtml.Encodings;

/// <summary>
/// Receives the code points a decoder produces, writes them as UTF-16, normalizes newlines and builds the
/// <see cref="OffsetMap"/>. Every decoder reports each unit with the number of input bytes it consumed.
/// </summary>
internal sealed class DecodeSink
{
    private readonly CancellationToken _cancellation;
    private int _ticks;
    private bool _afterCarriageReturn;

    public DecodeSink(char[] buffer, OffsetMap map, CancellationToken cancellation)
    {
        Chars = buffer;
        Map = map;
        _cancellation = cancellation;
    }

    public char[] Chars { get; }

    public int Length { get; private set; }

    public OffsetMap Map { get; }

    public int ErrorCount { get; private set; }

    /// <summary>Emits one code point decoded from <paramref name="bytes"/> input bytes.</summary>
    public void Emit(int codePoint, int bytes)
    {
        Tick();

        // HTML 13.2.3.5 "Preprocessing the input stream": CR LF and a lone CR become one LF. The line feed after a
        // carriage return joins the carriage return's unit so the byte map stays exact.
        if (codePoint == '\r')
        {
            Chars[Length++] = '\n';
            Map.Add(bytes, 1, 1);
            _afterCarriageReturn = true;
            return;
        }

        if (codePoint == '\n' && _afterCarriageReturn)
        {
            _afterCarriageReturn = false;
            Map.ExtendLast(bytes);
            return;
        }

        _afterCarriageReturn = false;
        if (codePoint < 0x10000)
        {
            Chars[Length++] = (char)codePoint;
            Map.Add(bytes, 1, 1);
        }
        else
        {
            Chars[Length++] = (char)(0xD7C0 + (codePoint >> 10));
            Chars[Length++] = (char)(0xDC00 + (codePoint & 0x3FF));
            Map.Add(bytes, 2, 1);
        }
    }

    /// <summary>Emits two code points decoded together from <paramref name="bytes"/> input bytes (a Big5 pointer that maps to a pair).</summary>
    public void EmitPair(int first, int second, int bytes)
    {
        Tick();
        _afterCarriageReturn = false;
        Chars[Length++] = (char)first;
        Chars[Length++] = (char)second;
        Map.Add(bytes, 2, 1);
    }

    /// <summary>Emits U+FFFD for a decoding error that consumed <paramref name="bytes"/> input bytes.</summary>
    public void Error(int bytes)
    {
        Tick();
        ErrorCount++;
        _afterCarriageReturn = false;
        Chars[Length++] = '\uFFFD';
        Map.Add(bytes, 1, 1);
    }

    /// <summary>Records input bytes that produce no text (an ISO-2022-JP escape sequence).</summary>
    public void Skip(int bytes) => Map.Add(bytes, 0, 1);

    /// <summary>
    /// Copies the leading run of ASCII bytes that need no newline handling and returns how many were copied.
    /// Stops before a carriage return, which <see cref="Emit"/> must see.
    /// </summary>
    public int AppendAscii(ReadOnlySpan<byte> source)
    {
        if (_afterCarriageReturn || source.IsEmpty)
        {
            return 0;
        }

        Tick();
        Span<char> destination = Chars.AsSpan(Length);
        Ascii.ToUtf16(source, destination, out int written);
        if (written == 0)
        {
            return 0;
        }

        int cr = destination[..written].IndexOf('\r');
        if (cr >= 0)
        {
            written = cr;
            if (written == 0)
            {
                return 0;
            }
        }

        Length += written;
        Map.Add(1, 1, written);
        return written;
    }

    private void Tick()
    {
        if ((++_ticks & 0xFFFF) == 0)
        {
            _cancellation.ThrowIfCancellationRequested();
        }
    }
}
