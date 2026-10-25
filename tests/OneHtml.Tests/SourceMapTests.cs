using System.Text;
using RecursiveAI.OneHtml.Parsing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests;

public class SourceMapTests
{
    private static Document Parse(string html) => Document.Parse(Encoding.UTF8.GetBytes(html));

    private static string Slice(Document doc, SourceRange r) => doc.Source.Text[r.Start..r.End];

    [Fact]
    public void Implied_elements_are_flagged_and_have_empty_start_ranges_at_the_token_that_made_them()
    {
        Document doc = Parse("<p>a</p>");
        Node html = doc.DocumentElement;
        Node head = html.FirstChild;
        Node body = html.LastChild;
        Node p = body.FirstChild;
        foreach (Node implied in new[] { html, head, body })
        {
            Assert.Equal(NodeOrigin.ImpliedStartTag, implied.Origin);
            Assert.Equal(new SourceRange(0, 0), implied.Source.Range);
            Assert.False(implied.Source.HasStartTag);
            Assert.False(implied.Source.HasEndTag);
        }

        Assert.Equal(NodeOrigin.None, p.Origin);
        Assert.Equal("<p>", Slice(doc, p.Source.Range));
        Assert.Equal("</p>", Slice(doc, p.Source.EndTag));
        Assert.True(p.Source.HasStartTag);
        Node text = p.FirstChild;
        Assert.Equal("a", Slice(doc, text.Source.Range));
        Assert.Equal(text.Source.Range, text.Source.DataRange);
    }

    [Fact]
    public void Unclosed_elements_have_no_end_tag_range()
    {
        Document doc = Parse("<div><p>a<p>b</div>");
        Node div = doc.DocumentElement.LastChild.FirstChild;
        Assert.Equal("</div>", Slice(doc, div.Source.EndTag));
        Assert.False(div.FirstChild.Source.HasEndTag);
        Assert.False(div.LastChild.Source.HasEndTag);
    }

    [Fact]
    public void Attributes_record_name_value_and_quote_style()
    {
        const string Html = "<a x y='1' z=\"2\" w=3 v=&amp;q>t</a>";
        Document doc = Parse(Html);
        Node a = doc.DocumentElement.LastChild.FirstChild;
        AttributeList attrs = a.Attributes;
        Assert.Equal(5, attrs.Count);

        Assert.Equal(AttributeQuote.NoValue, attrs[0].Quote);
        Assert.False(attrs[0].HasValue);
        Assert.Equal("x", Slice(doc, attrs[0].NameRange));
        Assert.Equal("x", Slice(doc, attrs[0].Range));
        Assert.Equal(0, attrs[0].ValueRange.Length);

        Assert.Equal(AttributeQuote.SingleQuoted, attrs[1].Quote);
        Assert.Equal("1", Slice(doc, attrs[1].ValueRange));
        Assert.Equal("y='1'", Slice(doc, attrs[1].Range));

        Assert.Equal(AttributeQuote.DoubleQuoted, attrs[2].Quote);
        Assert.Equal("z=\"2\"", Slice(doc, attrs[2].Range));

        Assert.Equal(AttributeQuote.Unquoted, attrs[3].Quote);
        Assert.Equal("w=3", Slice(doc, attrs[3].Range));
        Assert.Equal("3", attrs[3].Value.ToString());

        // The decoded value is not in the source, but its source range is.
        Assert.Equal("&q", attrs[4].Value.ToString());
        Assert.Equal("&amp;q", Slice(doc, attrs[4].ValueRange));
        Assert.Equal("v=&amp;q", Slice(doc, attrs[4].Range));
        Assert.Equal("<a x y='1' z=\"2\" w=3 v=&amp;q>", Slice(doc, a.Source.Range));
    }

    [Fact]
    public void Foster_parented_text_and_elements_are_flagged()
    {
        Document doc = Parse("<table>x<tr><td>y</td></tr></table>");
        Node body = doc.DocumentElement.LastChild;
        Node text = body.FirstChild;
        Node table = text.NextSibling;
        Assert.Equal(NodeKind.Text, text.Kind);
        Assert.Equal("x", text.Data.ToString());
        Assert.True((text.Origin & NodeOrigin.Reparented) != 0);
        Assert.True(text.Source.IsDisplaced);
        Assert.Equal("x", Slice(doc, text.Source.Range));
        Assert.Equal("table", table.LocalName);
        Assert.Equal(NodeOrigin.None, table.Origin);

        Node tbody = table.FirstChild;
        Assert.Equal("tbody", tbody.LocalName);
        Assert.Equal(NodeOrigin.ImpliedStartTag, tbody.Origin);
        Assert.Equal(new SourceRange(8, 8), tbody.Source.Range);
        Assert.Equal("<tr>", Slice(doc, tbody.FirstChild.Source.Range));
    }

    [Fact]
    public void The_adoption_agency_algorithm_marks_copies_and_moved_nodes()
    {
        Document doc = Parse("<b><p>x</b>y</p>");
        Node body = doc.DocumentElement.LastChild;
        Node b = body.FirstChild;
        Node p = b.NextSibling;
        Assert.Equal("b", b.LocalName);
        Assert.Equal("p", p.LocalName);
        Assert.False(b.HasChildNodes);
        Assert.Equal("</b>", Slice(doc, b.Source.EndTag));
        Assert.True((p.Origin & NodeOrigin.Reparented) != 0);

        Node copy = p.FirstChild;
        Assert.Equal("b", copy.LocalName);
        Assert.True((copy.Origin & NodeOrigin.Cloned) != 0);
        Assert.Equal("<b>", Slice(doc, copy.Source.Range));
        // The same end tag closes the copy too: the algorithm's second pass pops it for this token.
        Assert.Equal(b.Source.EndTag, copy.Source.EndTag);
        Assert.True((copy.FirstChild.Origin & NodeOrigin.Reparented) != 0);
        Assert.Equal("x", copy.FirstChild.Data.ToString());
        Assert.Equal("y", p.LastChild.Data.ToString());
    }

    [Fact]
    public void Reopened_formatting_elements_are_copies()
    {
        Document doc = Parse("<p><b>x</p>y");
        Node body = doc.DocumentElement.LastChild;
        Node second = body.LastChild;
        Assert.Equal("b", second.LocalName);
        Assert.True((second.Origin & NodeOrigin.Cloned) != 0);
        Assert.Equal("<b>", Slice(doc, second.Source.Range));
    }

    [Fact]
    public void End_tags_that_make_elements_are_their_end_range()
    {
        Document doc = Parse("<div></p></div></br>");
        Node body = doc.DocumentElement.LastChild;
        Node p = body.FirstChild.FirstChild;
        Assert.Equal("p", p.LocalName);
        Assert.Equal(NodeOrigin.ImpliedStartTag, p.Origin);
        Assert.Equal("</p>", Slice(doc, p.Source.EndTag));
        Node br = body.LastChild;
        Assert.Equal("br", br.LocalName);
        Assert.Equal(NodeOrigin.ImpliedStartTag, br.Origin);
        Assert.Equal("</br>", Slice(doc, br.Source.EndTag));
    }

    [Fact]
    public void Self_closing_syntax_is_recorded()
    {
        Document doc = Parse("<br/><svg/><div/>");
        Node body = doc.DocumentElement.LastChild;
        Assert.Equal(NodeOrigin.SelfClosingSyntax, body.FirstChild.Origin);
        Assert.Equal(NodeOrigin.SelfClosingSyntax, body.FirstChild.NextSibling.Origin);
        Assert.Equal(NodeOrigin.SelfClosingSyntax, body.LastChild.Origin);
        Assert.Equal("<br/>", Slice(doc, body.FirstChild.Source.Range));
    }

    [Fact]
    public void Comments_doctype_and_processing_instructions_have_ranges()
    {
        Document doc = Parse("<!DOCTYPE html><!--x--><?pi data?><p>");
        Node doctype = doc.Root.FirstChild;
        Assert.Equal("<!DOCTYPE html>", Slice(doc, doctype.Source.Range));
        Node comment = doctype.NextSibling;
        Assert.Equal("<!--x-->", Slice(doc, comment.Source.Range));
        Assert.Equal("x", Slice(doc, comment.Source.DataRange));
        Node pi = comment.NextSibling;
        Assert.Equal(NodeKind.ProcessingInstruction, pi.Kind);
        Assert.Equal("pi", pi.LocalName);
        Assert.Equal("data", pi.Data.ToString());
        Assert.Equal("<?pi data?>", Slice(doc, pi.Source.Range));
    }

    [Fact]
    public void Attributes_of_a_later_html_tag_are_merged_and_flagged()
    {
        Document doc = Parse("<html a=1><html b=2 a=3>");
        Node html = doc.DocumentElement;
        Assert.True((html.Origin & NodeOrigin.AttributesMerged) != 0);
        Assert.Equal(2, html.Attributes.Count);
        Assert.Equal("1", html.GetAttribute("a"));
        Assert.Equal("2", html.GetAttribute("b"));
        Assert.Equal("b", Slice(doc, html.Attributes[1].NameRange));
        Assert.True(html.Attributes[1].NameRange.Start > html.Source.Range.End);
    }

    [Fact]
    public void Text_split_by_the_body_end_tag_is_merged_and_flagged()
    {
        Document doc = Parse("<body>a</body>b");
        Node body = doc.DocumentElement.LastChild;
        Assert.Equal("</body>", Slice(doc, body.Source.EndTag));
        Node text = body.FirstChild;
        Assert.Equal("ab", text.Data.ToString());
        Assert.True((text.Origin & NodeOrigin.MergedText) != 0);
        Assert.Equal(new SourceRange(6, 15), text.Source.Range);
    }

    [Fact]
    public void Crlf_and_multibyte_sources_map_back_to_the_exact_bytes()
    {
        // "é" is two bytes and CR LF is two bytes for one newline character.
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("\u00E9\r\n<p>\u4E2D</p>\r\n")];
        Document doc = Document.Parse(bytes);
        Node body = doc.DocumentElement.LastChild;
        Node first = body.FirstChild;
        Assert.Equal("\u00E9\n", first.Data.ToString());
        Node p = first.NextSibling;
        SourceRange bytesRange = doc.ToByteRange(p.Source.Range);
        Assert.Equal("<p>", Encoding.UTF8.GetString(bytes[bytesRange.Start..bytesRange.End]));
        Assert.Equal(new SourceRange(7, 10), bytesRange);
        SourceRange end = doc.ToByteRange(p.Source.EndTag);
        Assert.Equal("</p>", Encoding.UTF8.GetString(bytes[end.Start..end.End]));
        SourceRange text = doc.ToByteRange(p.FirstChild.Source.Range);
        Assert.Equal("\u4E2D", Encoding.UTF8.GetString(bytes[text.Start..text.End]));
        SourceRange firstText = doc.ToByteRange(first.Source.Range);
        Assert.Equal("\u00E9\r\n", Encoding.UTF8.GetString(bytes[firstText.Start..firstText.End]));
    }

    [Fact]
    public void Character_references_keep_their_source_range()
    {
        Document doc = Parse("<p>a&amp;b</p>");
        Node text = doc.DocumentElement.LastChild.FirstChild.FirstChild;
        Assert.Equal("a&b", text.Data.ToString());
        Assert.Equal("a&amp;b", Slice(doc, text.Source.Range));
        Assert.False(text.Source.DataRange.IsPresent);
        Assert.Equal(NodeOrigin.None, text.Origin);
    }

    [Fact]
    public void Selectedcontent_copies_have_no_source_bytes_and_the_round_trip_is_unchanged()
    {
        const string Html = "<select><button><selectedcontent></button><option class=a>X<b>y</b><option selected>Z</select>";
        Document doc = Parse(Html);
        Node select = doc.DocumentElement.LastChild.FirstChild;
        Node content = select.FirstChild.FirstChild;
        Assert.Equal("selectedcontent", content.LocalName);
        Assert.Equal(NodeOrigin.None, content.Origin);
        Node copy = content.FirstChild;
        Assert.Equal("Z", copy.Data.ToString());
        Assert.Equal(NodeOrigin.Synthesized, copy.Origin & NodeOrigin.Synthesized);
        Assert.False(copy.Source.Range.IsPresent);
        Assert.True(copy.Source.IsDisplaced);
        Assert.Null(DocumentInvariants.Check(doc));
        Assert.Equal(Html, System.Text.Encoding.UTF8.GetString(doc.ToLosslessBytes()));

        // The first option is selected unless a later one says so, and its elements and attributes are copied too.
        Document first = Parse("<select><button><selectedcontent></button><option class=a>X<b>y</b></select>");
        Node c = first.DocumentElement.LastChild.FirstChild.FirstChild.FirstChild;
        Assert.Equal("X", c.FirstChild.Data.ToString());
        Node b = c.LastChild;
        Assert.Equal("b", b.LocalName);
        Assert.False(b.Source.HasStartTag);
        Assert.False(b.Source.Range.IsPresent);
        Assert.Null(DocumentInvariants.Check(first));
    }
}
