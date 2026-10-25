using System;

namespace RecursiveAI.OneHtml.Encodings;

/// <summary>
/// Maps offsets in the decoded UTF-16 text to byte offsets in the original input, and back.
/// </summary>
/// <remarks>
/// <para>
/// The decoder emits the text in <i>units</i>: the bytes of one decoded character (or error), producing one or two
/// UTF-16 code units. A carriage return followed by a line feed is one unit, because newlines are normalized
/// while decoding. The map stores runs of consecutive units with the same shape, so ordinary pages need a few
/// hundred entries and pure-ASCII input needs one.
/// </para>
/// <para>
/// Mapping rules: a text offset inside a unit (the low half of a surrogate pair) maps to the first byte of the unit;
/// a byte offset inside a unit maps to the first text offset of the unit. A text offset equal to the text length
/// maps to the byte length of the input. Bytes that produce no text (a byte order mark, an ISO-2022-JP escape
/// sequence) belong to the unit before them, except a byte order mark, which is outside the map: offsets before
/// <see cref="FirstByte"/> map to text offset 0.
/// </para>
/// </remarks>
public sealed class OffsetMap
{
    private Run[] _runs;
    private int _runCount;
    private readonly int _maxRuns;

    internal OffsetMap(int firstByte, int maxRuns)
    {
        FirstByte = firstByte;
        ByteLength = firstByte;
        _maxRuns = maxRuns;
        _runs = new Run[16];
    }

    /// <summary>The number of UTF-16 code units in the decoded text.</summary>
    public int TextLength { get; private set; }

    /// <summary>The number of bytes in the original input.</summary>
    public int ByteLength { get; private set; }

    /// <summary>The offset of the first byte that is decoded to text: the length of the byte order mark, or 0.</summary>
    public int FirstByte { get; }

    /// <summary>True when every text offset equals its byte offset, so that no table is needed.</summary>
    public bool IsIdentity =>
        FirstByte == 0 && ByteLength == TextLength
        && (_runCount == 0 || (_runCount == 1 && _runs[0].Bytes == 1 && _runs[0].Chars == 1));

    /// <summary>The number of stored runs (see the remarks); a measure of the map's memory use, 1 for ASCII-only text.</summary>
    public int RunCount => _runCount;

    /// <summary>Creates a map in which text offset <c>i</c> is byte offset <c>i</c> (text that did not come from bytes).</summary>
    internal static OffsetMap CreateIdentity(int length)
    {
        var map = new OffsetMap(0, int.MaxValue);
        map.Add(1, 1, length);
        return map;
    }

    /// <summary>Converts a UTF-16 offset in the decoded text to a byte offset in the original input.</summary>
    /// <param name="textOffset">A value from 0 to <see cref="TextLength"/> inclusive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="textOffset"/> is outside the text.</exception>
    public int ToByteOffset(int textOffset)
    {
        if ((uint)textOffset > (uint)TextLength)
        {
            throw new ArgumentOutOfRangeException(nameof(textOffset));
        }

        if (textOffset == TextLength)
        {
            return ByteLength;
        }

        // The last run that starts at or before the offset. Zero-width runs share a start with the run after them,
        // and the later one is the one that holds the character.
        int lo = 0;
        int hi = _runCount - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >>> 1;
            if (_runs[mid].CharStart <= textOffset)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        ref readonly Run run = ref _runs[lo];
        int unit = (textOffset - run.CharStart) / run.Chars;
        return run.ByteStart + unit * run.Bytes;
    }

    /// <summary>Converts a byte offset in the original input to a UTF-16 offset in the decoded text.</summary>
    /// <param name="byteOffset">A value from 0 to <see cref="ByteLength"/> inclusive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="byteOffset"/> is outside the input.</exception>
    public int ToTextOffset(int byteOffset)
    {
        if ((uint)byteOffset > (uint)ByteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset));
        }

        if (byteOffset >= ByteLength)
        {
            return TextLength;
        }

        if (byteOffset <= FirstByte || _runCount == 0)
        {
            return 0;
        }

        int lo = 0;
        int hi = _runCount - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >>> 1;
            if (_runs[mid].ByteStart <= byteOffset)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        ref readonly Run run = ref _runs[lo];
        if (run.Chars == 0)
        {
            return run.CharStart;
        }

        int unit = (byteOffset - run.ByteStart) / run.Bytes;
        return run.CharStart + unit * run.Chars;
    }

    /// <summary>Appends <paramref name="count"/> units of <paramref name="bytes"/> bytes and <paramref name="chars"/> code units each.</summary>
    internal void Add(int bytes, int chars, int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (_runCount > 0 && chars != 0)
        {
            ref Run last = ref _runs[_runCount - 1];
            if (last.Bytes == bytes && last.Chars == chars)
            {
                last.Count += count;
                TextLength += chars * count;
                ByteLength += bytes * count;
                return;
            }
        }

        if (_runCount == _maxRuns)
        {
            throw new HtmlLimitExceededException(nameof(ParseLimits.MaxOffsetMapRuns), _maxRuns, (long)_maxRuns + 1);
        }

        if (_runCount == _runs.Length)
        {
            Array.Resize(ref _runs, (int)Math.Min((long)_runs.Length * 2, Array.MaxLength));
        }

        _runs[_runCount++] = new Run { CharStart = TextLength, ByteStart = ByteLength, Bytes = bytes, Chars = chars, Count = count };
        TextLength += chars * count;
        ByteLength += bytes * count;
    }

    /// <summary>Adds bytes to the unit most recently added (used when a line feed follows a carriage return).</summary>
    internal void ExtendLast(int extraBytes)
    {
        ref Run last = ref _runs[_runCount - 1];
        if (last.Count == 1)
        {
            last.Bytes += extraBytes;
            ByteLength += extraBytes;
            return;
        }

        // Split the final unit off its run.
        last.Count--;
        int bytes = last.Bytes + extraBytes;
        int chars = last.Chars;
        TextLength -= chars;
        ByteLength -= last.Bytes;
        Add(bytes, chars, 1);
    }

    private struct Run
    {
        public int CharStart;
        public int ByteStart;
        public int Bytes;
        public int Chars;
        public int Count;
    }
}
