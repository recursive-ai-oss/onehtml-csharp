using System.Text;
using RecursiveAI.OneHtml.Encodings;
using Xunit;

namespace RecursiveAI.OneHtml.Tests;

public class SniffTests
{
    private static EncodingSniffResult Sniff(string ascii, string? contentType = null) =>
        EncodingSniffer.Sniff(Encoding.Latin1.GetBytes(ascii), contentType);

    [Fact]
    public void A_byte_order_mark_wins_over_everything()
    {
        EncodingSniffResult r = EncodingSniffer.Sniff([0xEF, 0xBB, 0xBF, .. "<meta charset=windows-1251>"u8], "text/html; charset=koi8-r");
        Assert.Equal(WhatwgEncoding.Utf8, r.Encoding);
        Assert.Equal(EncodingConfidence.Certain, r.Confidence);
        Assert.Equal(3, r.BomLength);
        Assert.Equal(WhatwgEncoding.Utf16Le, EncodingSniffer.Sniff([0xFF, 0xFE]).Encoding);
        Assert.Equal(WhatwgEncoding.Utf16Be, EncodingSniffer.Sniff([0xFE, 0xFF]).Encoding);
    }

    [Theory]
    [InlineData("text/html; charset=utf-8", "UTF-8")]
    [InlineData("text/html;charset=\"windows-1251\"", "windows-1251")]
    [InlineData("text/html; charset = koi8-r", null)]
    [InlineData("text/html; boundary=x; Charset=Latin1", "windows-1252")]
    [InlineData("text/html; charset=utf-8; charset=koi8-r", "UTF-8")]
    [InlineData("text/html; charset=\"utf\\-8\"", "UTF-8")]
    [InlineData("text/html; charset=nonsense", null)]
    [InlineData("text/html", null)]
    [InlineData("", null)]
    public void The_transport_charset_is_certain_when_it_names_an_encoding(string contentType, string? expected)
    {
        WhatwgEncoding? found = EncodingSniffer.ExtractFromContentType(contentType);
        Assert.Equal(expected, found?.Name);
        EncodingSniffResult r = Sniff("<p>", contentType);
        if (expected is not null)
        {
            Assert.Equal(EncodingConfidence.Certain, r.Confidence);
            Assert.Equal(expected, r.Encoding.Name);
        }
        else
        {
            Assert.Equal(EncodingConfidence.Tentative, r.Confidence);
        }
    }

    [Theory]
    [InlineData("<meta charset=utf-8>", "UTF-8")]
    [InlineData("<META CHARSET=\"ISO-8859-2\">", "ISO-8859-2")]
    [InlineData("<meta/charset='koi8-r'>", "KOI8-R")]
    [InlineData("<meta http-equiv=Content-Type content='text/html; charset=windows-1251'>", "windows-1251")]
    [InlineData("<meta content='text/html; charset=windows-1251'>", "windows-1252")]
    [InlineData("<meta http-equiv=refresh content='charset=koi8-r'>", "windows-1252")]
    [InlineData("<meta charset=utf-16>", "UTF-8")]
    [InlineData("<meta charset=utf-16be>", "UTF-8")]
    [InlineData("<meta charset=x-user-defined>", "windows-1252")]
    [InlineData("<!-- <meta charset=koi8-r> --><meta charset=utf-8>", "UTF-8")]
    [InlineData("<!--><meta charset=koi8-r>", "KOI8-R")]
    [InlineData("<p title='<meta charset=koi8-r>'>x", "windows-1252")]
    [InlineData("<meta charset=bogus><meta charset=utf-8>", "UTF-8")]
    [InlineData("<meta charset=koi8-r charset=utf-8>", "KOI8-R")]
    [InlineData("<?xml version=\"1.0\" encoding=\"iso-8859-2\"?><html>", "ISO-8859-2")]
    [InlineData("<?xml version=\"1.0\" encoding=\"utf-16\"?><html>", "UTF-8")]
    public void The_meta_prescan_finds_declarations(string markup, string expected)
    {
        EncodingSniffResult r = Sniff(markup);
        Assert.Equal(expected, r.Encoding.Name);
        Assert.Equal(EncodingConfidence.Tentative, r.Confidence);
    }

    [Fact]
    public void The_prescan_reads_only_the_first_1024_bytes_by_default()
    {
        string filler = new('x', 1100);
        Assert.Equal("windows-1252", Sniff(filler + "<meta charset=utf-8>").Encoding.Name);
        byte[] bytes = Encoding.Latin1.GetBytes(filler + "<meta charset=utf-8>");
        Assert.Equal(WhatwgEncoding.Utf8, EncodingSniffer.Sniff(bytes, prescanLimit: 4096).Encoding);
        Assert.Equal(WhatwgEncoding.Windows1252, EncodingSniffer.Sniff(Encoding.Latin1.GetBytes("<meta charset=utf-8>"), prescanLimit: 0).Encoding);
    }

    [Fact]
    public void Utf16_xml_declarations_are_recognised_without_a_byte_order_mark()
    {
        Assert.Equal(WhatwgEncoding.Utf16Le, EncodingSniffer.Prescan("<\0?\0x\0m\0l\0"u8));
        Assert.Equal(WhatwgEncoding.Utf16Be, EncodingSniffer.Prescan("\0<\0?\0x\0m\0l"u8));
    }

    [Fact]
    public void The_default_encoding_can_be_chosen()
    {
        EncodingSniffResult r = EncodingSniffer.Sniff("hello"u8, defaultEncoding: WhatwgEncoding.Utf8);
        Assert.Equal(WhatwgEncoding.Utf8, r.Encoding);
        Assert.Equal(EncodingConfidence.Tentative, r.Confidence);
    }

    [Theory]
    [InlineData("text/html; charset=utf-8", "UTF-8")]
    [InlineData("charset = 'koi8-r'", "KOI8-R")]
    [InlineData("charset=\"koi8-r", null)]
    [InlineData("xcharset=koi8-r;", "KOI8-R")]
    [InlineData("charset koi8-r charset=utf-8", "UTF-8")]
    [InlineData("charset=utf-8;x", "UTF-8")]
    [InlineData("nothing", null)]
    [InlineData("charset=", null)]
    public void Meta_content_values_are_searched_for_a_charset(string content, string? expected)
    {
        Assert.Equal(expected, EncodingSniffer.ExtractFromMetaContent(content)?.Name);
    }

    [Fact]
    public void Changing_the_encoding_re_decodes_only_when_the_new_one_differs()
    {
        byte[] bytes = Encoding.Latin1.GetBytes("<meta charset=koi8-r>\u00C1");
        SourceText first = SourceText.Decode(bytes);
        Assert.Equal(WhatwgEncoding.Koi8R, first.Encoding);
        Assert.Equal(EncodingConfidence.Tentative, first.Confidence);

        // The same encoding: nothing to redo, confidence becomes certain.
        EncodingChange same = first.ChangeEncoding(WhatwgEncoding.Koi8R);
        Assert.False(same.RestartRequired);
        Assert.Equal(EncodingConfidence.Certain, same.Text.Confidence);
        Assert.Same(first.Text, same.Text.Text);

        // Another encoding: a new decode that the parser must start over on.
        EncodingChange other = first.ChangeEncoding(WhatwgEncoding.Windows1252);
        Assert.True(other.RestartRequired);
        Assert.Equal(WhatwgEncoding.Windows1252, other.Text.Encoding);
        Assert.Equal(EncodingConfidence.Certain, other.Text.Confidence);
        Assert.EndsWith("\u00C1", other.Text.Text, System.StringComparison.Ordinal);
        Assert.NotEqual(first.Text, other.Text.Text);

        // Once certain, later declarations are ignored.
        EncodingChange ignored = other.Text.ChangeEncoding(WhatwgEncoding.Koi8R);
        Assert.False(ignored.RestartRequired);
        Assert.Equal(WhatwgEncoding.Windows1252, ignored.Text.Encoding);

        // UTF-16 declarations mean UTF-8; x-user-defined means windows-1252.
        SourceText tentative = SourceText.Decode(bytes);
        Assert.Equal(WhatwgEncoding.Utf8, tentative.ChangeEncoding(WhatwgEncoding.Utf16Le).Text.Encoding);
        Assert.Equal(WhatwgEncoding.Windows1252, tentative.ChangeEncoding(WhatwgEncoding.XUserDefined).Text.Encoding);

        // A UTF-16 document keeps its encoding whatever a meta says.
        SourceText utf16 = SourceText.Decode(new byte[] { 0x3C, 0x00 }, new DecodeOptions { DefaultEncoding = WhatwgEncoding.Utf16Le });
        EncodingChange c = utf16.ChangeEncoding(WhatwgEncoding.Windows1252);
        Assert.False(c.RestartRequired);
        Assert.Equal(WhatwgEncoding.Utf16Le, c.Text.Encoding);
    }

    [Fact]
    public void An_override_is_certain_but_a_byte_order_mark_still_wins()
    {
        SourceText forced = SourceText.Decode("\u00E9"u8.ToArray(), new DecodeOptions { OverrideEncoding = WhatwgEncoding.Windows1252 });
        Assert.Equal(EncodingConfidence.Certain, forced.Confidence);
        Assert.Equal("\u00C3\u00A9", forced.Text);
        SourceText bom = SourceText.Decode(new byte[] { 0xEF, 0xBB, 0xBF, 0x61 }, new DecodeOptions { OverrideEncoding = WhatwgEncoding.Windows1252 });
        Assert.Equal(WhatwgEncoding.Utf8, bom.Encoding);
    }
}
