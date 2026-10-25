using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneHtml.Encodings;
using Xunit;

namespace RecursiveAI.OneHtml.Tests;

/// <summary>
/// Decoder checks against the Encoding Standard's index files, read from a separate copy of <c>indexes.json</c>
/// (not the generated tables), and the edge cases of each decoding algorithm.
/// </summary>
public class DecoderTests
{
    private static readonly Lazy<JsonDocument> s_indexes = new(() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(TestPaths.TestData, "whatwg", "indexes.json"))));

    private static readonly Lazy<JsonDocument> s_encodings = new(() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(TestPaths.TestData, "whatwg", "encodings.json"))));

    private static string Decode(WhatwgEncoding encoding, params byte[] bytes) =>
        SourceText.Decode(bytes, encoding, EncodingConfidence.Certain).Text;

    private static List<int?> Index(string name) =>
        s_indexes.Value.RootElement.GetProperty(name).EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.Null ? (int?)null : e.GetInt32()).ToList();

    private static string Expect(int? codePoint, byte[] bytes, int asciiTrailAt = -1)
    {
        // A pointer without a code point decodes to U+FFFD; if the byte that broke the pair is ASCII it is decoded on its own.
        if (codePoint is { } cp)
        {
            return char.ConvertFromUtf32(cp);
        }

        return asciiTrailAt >= 0 && bytes[asciiTrailAt] < 0x80 ? "\uFFFD" + (char)bytes[asciiTrailAt] : "\uFFFD";
    }

    // ---- Every encoding and label of the standard ----

    [Fact]
    public void Every_encoding_and_label_in_encodings_json_resolves()
    {
        int encodings = 0;
        foreach (JsonElement group in s_encodings.Value.RootElement.EnumerateArray())
        {
            foreach (JsonElement e in group.GetProperty("encodings").EnumerateArray())
            {
                encodings++;
                string name = e.GetProperty("name").GetString()!;
                foreach (JsonElement label in e.GetProperty("labels").EnumerateArray())
                {
                    WhatwgEncoding? found = WhatwgEncoding.ForLabel(label.GetString());
                    Assert.True(found is not null, label.GetString());
                    Assert.Equal(name, found!.Name);
                    Assert.Same(found, WhatwgEncoding.ForLabel(" \t" + label.GetString()!.ToUpperInvariant() + "\n"));
                }
            }
        }

        Assert.Equal(encodings, WhatwgEncoding.All.Count);
        Assert.Equal(WhatwgEncoding.All.Count, WhatwgEncoding.All.Select(e => e.Name).Distinct().Count());
        Assert.Null(WhatwgEncoding.ForLabel("utf-7"));
        Assert.Null(WhatwgEncoding.ForLabel(""));
        Assert.Null(WhatwgEncoding.ForLabel("utf-8\u00A0"));
    }

    [Fact]
    public void Single_byte_encodings_match_their_indexes()
    {
        var byIndexName = new Dictionary<string, string> { ["ISO-8859-8-I"] = "iso-8859-8" };
        int checkedEncodings = 0;
        foreach (WhatwgEncoding enc in WhatwgEncoding.All)
        {
            string key = byIndexName.GetValueOrDefault(enc.Name, enc.Name.ToLowerInvariant());
            if (!s_indexes.Value.RootElement.TryGetProperty(key, out JsonElement _) || key is "big5" or "gb18030" or "euc-kr" or "jis0208" or "jis0212")
            {
                continue;
            }

            List<int?> index = Index(key);
            Assert.Equal(128, index.Count);
            for (int b = 0; b < 256; b++)
            {
                if (b == 13) { continue; } // CR is normalized to LF when decoding
                string expected = b < 0x80 ? ((char)b).ToString() : index[b - 0x80] is { } cp ? char.ConvertFromUtf32(cp) : "\uFFFD";
                Assert.Equal(expected, Decode(enc, (byte)b));
            }

            checkedEncodings++;
        }

        Assert.Equal(28, checkedEncodings);
    }

    [Fact]
    public void X_user_defined_maps_high_bytes_to_the_private_use_area()
    {
        for (int b = 0; b < 256; b++)
        {
            if (b == 13) { continue; } // CR is normalized to LF when decoding
            Assert.Equal(b < 0x80 ? b : 0xF780 + b - 0x80, Decode(WhatwgEncoding.XUserDefined, (byte)b)[0]);
        }
    }

    [Fact]
    public void Replacement_decodes_any_input_to_one_replacement_character()
    {
        Assert.Equal(string.Empty, Decode(WhatwgEncoding.Replacement));
        Assert.Equal("\uFFFD", Decode(WhatwgEncoding.Replacement, 0x61, 0x62, 0x63));
        Assert.Equal("\uFFFD", Decode(WhatwgEncoding.Replacement, 0xFF));
    }

    // ---- Multi-byte encodings against their indexes ----

    [Fact]
    public void Euc_kr_matches_index_euc_kr()
    {
        List<int?> index = Index("euc-kr");
        for (int p = 0; p < index.Count; p++)
        {
            byte[] bytes = [(byte)((p / 190) + 0x81), (byte)((p % 190) + 0x41)];
            Assert.Equal(Expect(index[p], bytes, 1), Decode(WhatwgEncoding.EucKr, bytes));
        }
    }

    [Theory]
    [InlineData("GBK")]
    [InlineData("gb18030")]
    public void Gb18030_and_gbk_match_index_gb18030(string name)
    {
        WhatwgEncoding enc = WhatwgEncoding.ForLabel(name)!;
        List<int?> index = Index("gb18030");
        for (int p = 0; p < index.Count; p++)
        {
            int t = p % 190;
            byte[] bytes = [(byte)((p / 190) + 0x81), (byte)(t < 0x3F ? 0x40 + t : 0x41 + t)];
            Assert.Equal(Expect(index[p], bytes, 1), Decode(enc, bytes));
        }

        Assert.Equal("\u20AC", Decode(enc, 0x80));
    }

    [Fact]
    public void Gb18030_four_byte_sequences_match_the_ranges_index()
    {
        JsonElement[] ranges = s_indexes.Value.RootElement.GetProperty("gb18030-ranges").EnumerateArray().ToArray();
        Assert.Equal(207, ranges.Length);
        static byte[] Bytes(int p) =>
        [
            (byte)((p / 12600) + 0x81),
            (byte)((p % 12600 / 1260) + 0x30),
            (byte)((p % 1260 / 10) + 0x81),
            (byte)((p % 10) + 0x30),
        ];

        for (int i = 0; i < ranges.Length; i++)
        {
            int pointer = ranges[i][0].GetInt32();
            int codePoint = ranges[i][1].GetInt32();
            Assert.Equal(char.ConvertFromUtf32(codePoint), Decode(WhatwgEncoding.Gb18030, Bytes(pointer)));
            int last = i + 1 < ranges.Length ? ranges[i + 1][0].GetInt32() - 1 : 1237575;
            if (last != 7457 && last <= 39419 || last >= 189000)
            {
                Assert.Equal(char.ConvertFromUtf32(codePoint + last - pointer), Decode(WhatwgEncoding.Gb18030, Bytes(last)));
            }
        }

        // Pointers between the BMP ranges and the supplementary ones, and past the end, have no code point.
        Assert.Equal("\uFFFD", Decode(WhatwgEncoding.Gb18030, Bytes(39420)));
        Assert.Equal("\uFFFD", Decode(WhatwgEncoding.Gb18030, Bytes(1237576)));
        Assert.Equal("\uE7C7", Decode(WhatwgEncoding.Gb18030, Bytes(7457)));
        Assert.Equal("\U00010000", Decode(WhatwgEncoding.Gb18030, 0x90, 0x30, 0x81, 0x30));
        Assert.Equal("\U0010FFFF", Decode(WhatwgEncoding.Gb18030, 0xE3, 0x32, 0x9A, 0x35));
    }

    [Fact]
    public void Big5_matches_index_big5()
    {
        List<int?> index = Index("big5");
        for (int p = 0; p < index.Count; p++)
        {
            int t = p % 157;
            byte[] bytes = [(byte)((p / 157) + 0x81), (byte)(t < 0x3F ? 0x40 + t : 0x62 + t)];
            string expected = p switch
            {
                1133 => "\u00CA\u0304",
                1135 => "\u00CA\u030C",
                1164 => "\u00EA\u0304",
                1166 => "\u00EA\u030C",
                _ => Expect(index[p], bytes, 1),
            };
            Assert.Equal(expected, Decode(WhatwgEncoding.Big5, bytes));
        }
    }

    [Fact]
    public void Euc_jp_matches_jis0208_jis0212_and_halfwidth_katakana()
    {
        List<int?> jis0208 = Index("jis0208");
        for (int p = 0; p < 94 * 94; p++)
        {
            byte[] bytes = [(byte)((p / 94) + 0xA1), (byte)((p % 94) + 0xA1)];
            int? expected = p < jis0208.Count ? jis0208[p] : null;
            Assert.Equal(Expect(expected, bytes), Decode(WhatwgEncoding.EucJp, bytes));
        }

        List<int?> jis0212 = Index("jis0212");
        for (int p = 0; p < jis0212.Count; p++)
        {
            byte[] bytes = [0x8F, (byte)((p / 94) + 0xA1), (byte)((p % 94) + 0xA1)];
            Assert.Equal(Expect(jis0212[p], bytes), Decode(WhatwgEncoding.EucJp, bytes));
        }

        for (int b = 0xA1; b <= 0xDF; b++)
        {
            Assert.Equal(char.ConvertFromUtf32(0xFF61 - 0xA1 + b), Decode(WhatwgEncoding.EucJp, 0x8E, (byte)b));
        }
    }

    [Fact]
    public void Shift_jis_matches_jis0208_and_single_byte_ranges()
    {
        List<int?> jis0208 = Index("jis0208");
        for (int p = 0; p < 188 * 60; p++)
        {
            int lead = p / 188;
            lead += lead < 0x1F ? 0x81 : 0xC1;
            int t = p % 188;
            byte[] bytes = [(byte)lead, (byte)(t < 0x3F ? 0x40 + t : 0x41 + t)];
            string expected = p is >= 8836 and <= 10715
                ? char.ConvertFromUtf32(0xE000 - 8836 + p)
                : Expect(p < jis0208.Count ? jis0208[p] : null, bytes, 1);
            Assert.Equal(expected, Decode(WhatwgEncoding.ShiftJis, bytes));
        }

        for (int b = 0xA1; b <= 0xDF; b++)
        {
            Assert.Equal(char.ConvertFromUtf32(0xFF61 - 0xA1 + b), Decode(WhatwgEncoding.ShiftJis, (byte)b));
        }

        Assert.Equal("\u0080", Decode(WhatwgEncoding.ShiftJis, 0x80));
        Assert.Equal("\uFFFD", Decode(WhatwgEncoding.ShiftJis, 0xA0));
        Assert.Equal("\uFFFD", Decode(WhatwgEncoding.ShiftJis, 0xFD));
    }

    [Fact]
    public void Iso_2022_jp_decodes_jis0208_roman_and_katakana_with_escapes()
    {
        List<int?> jis0208 = Index("jis0208");
        for (int p = 0; p < 94 * 94; p++)
        {
            byte[] bytes = [0x1B, 0x24, 0x42, (byte)((p / 94) + 0x21), (byte)((p % 94) + 0x21)];
            string expected = p < jis0208.Count && jis0208[p] is { } cp ? char.ConvertFromUtf32(cp) : "\uFFFD";
            Assert.Equal(expected, Decode(WhatwgEncoding.Iso2022Jp, bytes));
        }

        // ESC ( J: JIS X 0201 Roman, where 0x5C is the yen sign and 0x7E the overline.
        Assert.Equal("a\u00A5\u203E", Decode(WhatwgEncoding.Iso2022Jp, 0x1B, 0x28, 0x4A, 0x61, 0x5C, 0x7E));

        // ESC ( I: katakana; ESC ( B back to ASCII.
        Assert.Equal("\uFF61\uFF9Fa", Decode(WhatwgEncoding.Iso2022Jp, 0x1B, 0x28, 0x49, 0x21, 0x5F, 0x1B, 0x28, 0x42, 0x61));

        // Two escapes in a row without text between them: the second is an error.
        Assert.Equal("\uFFFDa", Decode(WhatwgEncoding.Iso2022Jp, 0x1B, 0x28, 0x42, 0x1B, 0x28, 0x42, 0x61));

        // A malformed escape, a stray lead byte at the end, and bytes outside the 7-bit range.
        Assert.Equal("\uFFFD(Xx", Decode(WhatwgEncoding.Iso2022Jp, 0x1B, 0x28, 0x58, 0x78));
        Assert.Equal("\uFFFD", Decode(WhatwgEncoding.Iso2022Jp, 0x1B, 0x24, 0x42, 0x30));
        Assert.Equal("\uFFFD", Decode(WhatwgEncoding.Iso2022Jp, 0x0E));
        Assert.Equal("\uFFFD", Decode(WhatwgEncoding.Iso2022Jp, 0x80));
    }

    [Fact]
    public void Utf16_decodes_surrogate_pairs_and_reports_lone_surrogates_and_odd_bytes()
    {
        Assert.Equal("a\U0001F600b", Decode(WhatwgEncoding.Utf16Le, 0x61, 0x00, 0x3D, 0xD8, 0x00, 0xDE, 0x62, 0x00));
        Assert.Equal("a\U0001F600b", Decode(WhatwgEncoding.Utf16Be, 0x00, 0x61, 0xD8, 0x3D, 0xDE, 0x00, 0x00, 0x62));
        Assert.Equal("\uFFFDa", Decode(WhatwgEncoding.Utf16Le, 0x3D, 0xD8, 0x61, 0x00));
        Assert.Equal("\uFFFD", Decode(WhatwgEncoding.Utf16Le, 0x00, 0xDE));
        Assert.Equal("\uFFFD", Decode(WhatwgEncoding.Utf16Le, 0x3D, 0xD8));
        Assert.Equal("a\uFFFD", Decode(WhatwgEncoding.Utf16Le, 0x61, 0x00, 0x62));
        Assert.Equal("\uFFFD", Decode(WhatwgEncoding.Utf16Le, 0x3D, 0xD8, 0x00));
    }

    [Fact]
    public void Utf8_follows_the_replacement_rules_of_the_standard()
    {
        Assert.Equal("a\u00E9\u20AC\U0001F600", Decode(WhatwgEncoding.Utf8, 0x61, 0xC3, 0xA9, 0xE2, 0x82, 0xAC, 0xF0, 0x9F, 0x98, 0x80));

        // One replacement per maximal invalid subpart (Unicode "best practice").
        Assert.Equal("\uFFFD\uFFFD", Decode(WhatwgEncoding.Utf8, 0xC0, 0xAF));          // overlong: two errors
        Assert.Equal("\uFFFD\uFFFD\uFFFD", Decode(WhatwgEncoding.Utf8, 0xED, 0xA0, 0x80)); // encoded surrogate
        Assert.Equal("\uFFFD", Decode(WhatwgEncoding.Utf8, 0xE2, 0x82));                  // truncated at the end
        Assert.Equal("\uFFFDA", Decode(WhatwgEncoding.Utf8, 0xE2, 0x82, 0x41));           // truncated, then ASCII
        Assert.Equal("\uFFFD\uFFFD\uFFFD\uFFFD", Decode(WhatwgEncoding.Utf8, 0xF4, 0x90, 0x80, 0x80)); // above U+10FFFF
        Assert.Equal("\uFFFD", Decode(WhatwgEncoding.Utf8, 0xFF));
        Assert.Equal("\uFFFD\uFFFD", Decode(WhatwgEncoding.Utf8, 0x80, 0xBF));
    }

    // ---- Newlines and byte order marks ----

    [Fact]
    public void Newlines_are_normalized_while_decoding()
    {
        Assert.Equal("a\nb\nc\n\nd", Decode(WhatwgEncoding.Utf8, (byte)'a', 13, 10, (byte)'b', 13, (byte)'c', 10, 13, (byte)'d'));
        Assert.Equal("\n\n", Decode(WhatwgEncoding.Utf8, 13, 13));
        Assert.Equal("\n", Decode(WhatwgEncoding.Utf8, 13, 10));
        Assert.Equal("\n\n", Decode(WhatwgEncoding.Utf8, 10, 13));
        Assert.Equal("a\nb", Decode(WhatwgEncoding.Utf16Le, 0x61, 0, 13, 0, 10, 0, 0x62, 0));
        Assert.Equal("a\nb", Decode(WhatwgEncoding.Windows1252, (byte)'a', 13, 10, (byte)'b'));
    }

    [Fact]
    public void A_byte_order_mark_overrides_the_encoding_and_is_not_part_of_the_text()
    {
        SourceText utf8 = SourceText.Decode(new byte[] { 0xEF, 0xBB, 0xBF, 0x61 }, WhatwgEncoding.Windows1252, EncodingConfidence.Tentative);
        Assert.Equal("a", utf8.Text);
        Assert.Equal(WhatwgEncoding.Utf8, utf8.Encoding);
        Assert.Equal(EncodingConfidence.Certain, utf8.Confidence);
        Assert.Equal(3, utf8.BomLength);
        Assert.Equal(3, utf8.ToByteOffset(0));
        Assert.Equal(4, utf8.ToByteOffset(1));
        Assert.Equal(0, utf8.ToTextOffset(2));

        SourceText le = SourceText.Decode(new byte[] { 0xFF, 0xFE, 0x61, 0x00 });
        Assert.Equal("a", le.Text);
        Assert.Equal(WhatwgEncoding.Utf16Le, le.Encoding);
        SourceText be = SourceText.Decode(new byte[] { 0xFE, 0xFF, 0x00, 0x61 });
        Assert.Equal("a", be.Text);
        Assert.Equal(WhatwgEncoding.Utf16Be, be.Encoding);

        // Without the mark, the bytes of a mark are ordinary characters in a legacy encoding.
        Assert.Equal("\u00EF\u00BB\u00BE", Decode(WhatwgEncoding.Windows1252, 0xEF, 0xBB, 0xBE));
    }
}
