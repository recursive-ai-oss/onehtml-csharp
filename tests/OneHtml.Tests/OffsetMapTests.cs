using System;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Encodings;
using Xunit;

namespace RecursiveAI.OneHtml.Tests;

public class OffsetMapTests
{
    private static string RandomText(Random random, int length)
    {
        string[] pieces = ["a", "b", " ", "<", ">", "\n", "\r\n", "\r", "\u00E9", "\u20AC", "\u4E2D", "\U0001F600", "\u0100"];
        var sb = new StringBuilder();
        for (int i = 0; i < length; i++)
        {
            sb.Append(pieces[random.Next(pieces.Length)]);
        }

        return sb.ToString();
    }

    private static bool IsMidPair(string text, int i) => i > 0 && i < text.Length && char.IsLowSurrogate(text[i]) && char.IsHighSurrogate(text[i - 1]);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Utf8_offsets_round_trip_and_cut_the_bytes_where_the_text_is_cut(int seed)
    {
        var random = new Random(seed);
        string original = RandomText(random, 400);
        byte[] bytes = Encoding.UTF8.GetBytes(original);
        SourceText source = SourceText.Decode(bytes, WhatwgEncoding.Utf8, EncodingConfidence.Certain);
        string text = source.Text;
        Assert.Equal(SourceText.NormalizeNewlines(original), text);
        Assert.Equal(bytes.Length, source.ToByteOffset(text.Length));
        Assert.Equal(text.Length, source.ToTextOffset(bytes.Length));

        int previous = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            int b = source.ToByteOffset(i);
            Assert.True(b >= previous);
            previous = b;
            if (IsMidPair(text, i))
            {
                Assert.Equal(source.ToByteOffset(i - 1), b);
                continue;
            }

            // The bytes before the offset decode to the text before it.
            Assert.Equal(text[..i], SourceText.NormalizeNewlines(Encoding.UTF8.GetString(bytes, 0, b)));
            Assert.Equal(i, source.ToTextOffset(b));
        }

        // Byte offsets inside a character map to the start of that character's text.
        for (int b = 0; b <= bytes.Length; b++)
        {
            int i = source.ToTextOffset(b);
            Assert.True(source.ToByteOffset(i) <= b);
        }
    }

    [Fact]
    public void Ascii_input_needs_one_run_and_the_identity_map()
    {
        SourceText source = SourceText.Decode(Encoding.ASCII.GetBytes(new string('x', 10_000)), WhatwgEncoding.Utf8, EncodingConfidence.Certain);
        Assert.True(source.Map.IsIdentity);
        Assert.Equal(1, source.Map.RunCount);
        Assert.Equal(1234, source.ToByteOffset(1234));
        Assert.Equal(1234, source.ToTextOffset(1234));
    }

    [Fact]
    public void A_page_with_a_few_multibyte_characters_needs_few_runs()
    {
        string page = string.Concat(Enumerable.Repeat(new string('a', 500) + "\u00E9", 20));
        SourceText source = SourceText.Decode(Encoding.UTF8.GetBytes(page), WhatwgEncoding.Utf8, EncodingConfidence.Certain);
        Assert.True(source.Map.RunCount <= 41, source.Map.RunCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.False(source.Map.IsIdentity);
    }

    [Fact]
    public void Utf16_and_legacy_encodings_map_offsets()
    {
        byte[] utf16 = [0x61, 0x00, 0x3D, 0xD8, 0x00, 0xDE, 0x62, 0x00];
        SourceText s = SourceText.Decode(utf16, WhatwgEncoding.Utf16Le, EncodingConfidence.Certain);
        Assert.Equal("a\U0001F600b", s.Text);
        Assert.Equal([0, 2, 2, 6, 8], Enumerable.Range(0, 5).Select(s.ToByteOffset));
        Assert.Equal(3, s.ToTextOffset(6));

        // Shift_JIS: a, HIRAGANA A (0x82 0xA0), b.
        SourceText sjis = SourceText.Decode(new byte[] { 0x61, 0x82, 0xA0, 0x62 }, WhatwgEncoding.ShiftJis, EncodingConfidence.Certain);
        Assert.Equal("a\u3042b", sjis.Text);
        Assert.Equal([0, 1, 3, 4], Enumerable.Range(0, 4).Select(sjis.ToByteOffset));
        Assert.Equal(1, sjis.ToTextOffset(2));
    }

    [Fact]
    public void Iso_2022_jp_escape_sequences_belong_to_the_character_before_them()
    {
        // ESC $ B, HIRAGANA A (0x24 0x22), ESC ( B, a
        byte[] bytes = [0x1B, 0x24, 0x42, 0x24, 0x22, 0x1B, 0x28, 0x42, 0x61];
        SourceText s = SourceText.Decode(bytes, WhatwgEncoding.Iso2022Jp, EncodingConfidence.Certain);
        Assert.Equal("\u3042a", s.Text);
        Assert.Equal(3, s.ToByteOffset(0));
        Assert.Equal(8, s.ToByteOffset(1));
        Assert.Equal(9, s.ToByteOffset(2));
        Assert.Equal(0, s.ToTextOffset(1));
        Assert.Equal(1, s.ToTextOffset(5));
        Assert.Equal(1, s.ToTextOffset(8));
    }

    [Fact]
    public void Offsets_outside_the_text_are_rejected()
    {
        SourceText s = SourceText.Decode(new byte[] { 0x61 }, WhatwgEncoding.Utf8, EncodingConfidence.Certain);
        Assert.Throws<ArgumentOutOfRangeException>(() => s.ToByteOffset(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.ToByteOffset(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.ToTextOffset(2));
    }

    [Fact]
    public void Text_made_from_a_string_has_an_identity_map_and_normalized_newlines()
    {
        SourceText s = SourceText.FromString("a\r\nb\rc");
        Assert.Equal("a\nb\nc", s.Text);
        Assert.Equal(3, s.ToByteOffset(3));
        Assert.Same("plain", SourceText.NormalizeNewlines("plain"));
    }

    [Fact]
    public void Limits_are_checked_before_the_work_they_guard()
    {
        var small = new ParseLimits { MaxInputLength = 100 };
        HtmlLimitExceededException ex = Assert.Throws<HtmlLimitExceededException>(
            () => SourceText.Decode(new byte[101], new DecodeOptions { Limits = small }));
        Assert.Equal(nameof(ParseLimits.MaxInputLength), ex.LimitName);
        Assert.Equal(100, ex.Limit);
        Assert.Throws<HtmlLimitExceededException>(() => SourceText.FromString(new string('x', 101), small));

        // Alternating one-byte and two-byte characters give a run per character; the map has its own bound.
        byte[] alternating = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("a\u00E9", 5_000)));
        HtmlLimitExceededException runs = Assert.Throws<HtmlLimitExceededException>(
            () => SourceText.Decode(alternating, new DecodeOptions { OverrideEncoding = WhatwgEncoding.Utf8, Limits = new ParseLimits { MaxOffsetMapRuns = 1_000 } }));
        Assert.Equal(nameof(ParseLimits.MaxOffsetMapRuns), runs.LimitName);
    }

    [Fact]
    public void Cancellation_stops_decoding()
    {
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => SourceText.Decode(new byte[10], cancellationToken: cts.Token));
    }
}
