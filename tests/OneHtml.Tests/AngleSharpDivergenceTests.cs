using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Encodings;
using Xunit;

namespace RecursiveAI.OneHtml.Tests;

/// <summary>
/// The places where our trees differ from AngleSharp's on real pages, each written by hand from the standard's text.
/// <c>docs/anglesharp-differential.md</c> explains every case with its clause; these tests keep our side of it fixed.
/// </summary>
public sealed class AngleSharpDivergenceTests
{
    private static string Dump(string html)
    {
        Document doc = Document.Parse(html);
        var sb = new StringBuilder();
        Node body = doc.Root.QuerySelector("body");
        Write(sb, body.ChildNodes);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, IEnumerable<Node> nodes)
    {
        bool first = true;
        foreach (Node n in nodes)
        {
            if (!first)
            {
                sb.Append(',');
            }

            first = false;
            switch (n.Kind)
            {
                case NodeKind.Element:
                    sb.Append(n.LocalName);
                    if (n.ChildNodes.Any())
                    {
                        sb.Append('(');
                        Write(sb, n.ChildNodes);
                        sb.Append(')');
                    }

                    if (n.LocalName == "template" && !n.TemplateContent.IsNull)
                    {
                        sb.Append('{');
                        Write(sb, n.TemplateContent.ChildNodes);
                        sb.Append('}');
                    }

                    break;
                case NodeKind.Text:
                    sb.Append('"').Append(n.Data.ToString().Replace("\n", "\\n", StringComparison.Ordinal)).Append('"');
                    break;
                default:
                    sb.Append('#').Append(n.Kind);
                    break;
            }
        }
    }

    // HTML 13.2.6.4.7 "in body": "search" is in the lists of elements that close a p and whose end tag pops to it.
    [Fact]
    public void Search_is_a_known_block_element()
    {
        Assert.Equal("div(search(div),\"x\")", Dump("<div><search><div></search>x"));
        Assert.Equal("p(\"a\"),search(\"b\")", Dump("<p>a<search>b"));
    }

    // menuitem has no entry in the parsing algorithm any more, so it is an ordinary element that stays open.
    [Fact]
    public void Menuitem_is_an_ordinary_element()
    {
        Assert.Equal("div(menuitem(\"<\"))", Dump("<div><menuitem><"));
        Assert.Equal("div(ul(legend(menuitem(\">\"))))", Dump("<div><ul><legend><menuitem>>"));
    }

    // The "in select" insertion mode is gone: a select's content is parsed as in body, and unknown elements stay in it.
    [Fact]
    public void Select_keeps_elements_that_are_not_options()
    {
        Assert.Equal("select(n)", Dump("<select><n>"));
        Assert.Equal("select(option(n)),\"\\n\"", Dump("<select><option><n></select>\n"));
    }

    // 13.2.6.4.15 "in cell": an end tag td/th with no such element in table scope is ignored, so a th is not closed by </td>.
    [Fact]
    public void A_td_end_tag_does_not_close_a_th()
    {
        Assert.Equal("table(tbody(tr(th(\"x\"))))", Dump("<table><tr><th></td>x"));
    }

    // 13.2.6.4.7 "in body" template start tag: shadowrootmode only creates a shadow root when the parser allows
    // declarative shadow roots, which a document parsed from bytes does not.
    [Fact]
    public void Declarative_shadow_root_is_not_attached_when_parsing_a_document()
    {
        Assert.Equal("div(template{b})", Dump("<div><template shadowrootmode=open><b></b></template></div>"));
    }

    // 13.2.5.72ff: "<?" starts a processing instruction state; a target that is valid gives a processing instruction.
    [Fact]
    public void Processing_instructions_are_not_comments()
    {
        Document doc = Document.Parse("<script></script><?p q?>");
        Node pi = doc.Root.QuerySelector("head").ChildNodes.Last();
        Assert.Equal(NodeKind.ProcessingInstruction, pi.Kind);
    }

    // 13.2.5.77 "character reference state": a "&" not followed by an alphanumeric or "#" is flushed as itself and the
    // following ";" is ordinary text or attribute value.
    [Fact]
    public void A_bare_ampersand_keeps_the_semicolon()
    {
        Assert.Equal("\"a&;b\"", Dump("a&;b"));
        Document doc = Document.Parse("<p title=\"&;\" onclick=&;>");
        Node p = doc.Root.QuerySelector("p");
        Assert.Equal("&;", p.GetAttribute("title"));
        Assert.Equal("&;", p.GetAttribute("onclick"));
    }

    // 13.2.5.14 "RAWTEXT end tag name state": letters of an end tag that never completes are emitted as typed, while
    // only the tag name token is lowercased.
    [Fact]
    public void An_unfinished_end_tag_in_raw_text_keeps_its_case()
    {
        Assert.Equal("</C", Document.Parse("<style></C").Root.QuerySelector("style").TextContent);
    }

    // 13.2.6.5 "adjust SVG tag names" lists feDropShadow; "adjust foreign attributes" keeps the xlink prefix.
    [Fact]
    public void Svg_names_and_xlink_attributes_follow_the_foreign_content_rules()
    {
        Document doc = Document.Parse("<svg><feDropShadow></feDropShadow><use xlink:href=a href=b></svg>");
        Assert.Equal("feDropShadow", doc.Root.QuerySelector("svg").FirstChild.LocalName);
        Node use = doc.Root.QuerySelector("use");
        var names = new List<string>();
        foreach (Attr a in use.Attributes)
        {
            names.Add(a.QualifiedName + "=" + a.Value.ToString() + "/" + a.Namespace);
        }

        Assert.Equal(["xlink:href=a/XLink", "href=b/None"], names);
    }

    // 13.2.6.4.7 adoption agency algorithm: with no special element below the formatting element, everything above it
    // is popped. "time" is not special, so it closes with the a or b.
    [Fact]
    public void A_non_special_element_does_not_act_as_a_furthest_block()
    {
        Assert.Equal("ul(li(a(time),\"<\"))", Dump("<ul><li><a><time></a><"));
        Assert.Equal("p(b(\"1\",time(\"2\")),\"3\")", Dump("<p><b>1<time>2</b>3"));
        Assert.Equal("ul(li(a(time),a))", Dump("<ul><li><a><time><a>"));
    }

    // 13.2.6.4.7 "in body" form end tag: inside a template the form is out of scope, so the token is ignored and the
    // form element pointer still ends the outer form later.
    [Fact]
    public void A_form_end_tag_inside_a_template_is_ignored()
    {
        Assert.Equal("form(div(template{o(\"x\")},\"y\"),\"z\"),\"w\"", Dump("<form><div><template><o></form>x</template>y</div>z</form>w"));
    }

    // 13.2.3.2 "encoding sniffing algorithm": the transport charset wins, parameter names are case-insensitive, the
    // prescan looks for an XML declaration, and GBK is the encoding named by the gb2312 label (Encoding Standard).
    [Fact]
    public void Encoding_sniffing_follows_the_standard()
    {
        SourceText a = SourceText.Decode("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=windows-1252\"><p>"u8.ToArray(), new DecodeOptions { ContentType = "text/html; Charset=utf-8" });
        Assert.Equal("UTF-8", a.Encoding.Name);
        SourceText b = SourceText.Decode("<?xml version=\"1.0\" encoding=\"Shift_JIS\"?><p>"u8.ToArray());
        Assert.Equal("Shift_JIS", b.Encoding.Name);
        SourceText c = SourceText.Decode("<meta charset=gb2312><p>"u8.ToArray());
        Assert.Equal("GBK", c.Encoding.Name);
        SourceText d = SourceText.Decode("<p>no declaration"u8.ToArray());
        Assert.Equal("windows-1252", d.Encoding.Name);
    }

    // The standard has no limit on nesting. Ours is ParseLimits.MaxOpenElements (4096), so 600 nested elements all nest.
    [Fact]
    public void Nesting_is_not_capped_at_512()
    {
        Document doc = Document.Parse("<p>" + string.Concat(Enumerable.Repeat("<x>", 600)) + "a");
        int depth = 0;
        for (Node n = doc.Root.QuerySelector("p"); !n.IsNull; n = n.FirstChild.IsElement ? n.FirstChild : default)
        {
            depth++;
        }

        Assert.Equal(601, depth);
    }
}
