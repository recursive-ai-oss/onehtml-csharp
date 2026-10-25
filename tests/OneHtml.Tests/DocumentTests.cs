using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Encodings;
using RecursiveAI.OneHtml.Tests.Html5lib;
using Xunit;

namespace RecursiveAI.OneHtml.Tests;

public class DocumentTests
{
    private static string[] ChildNames(Node parent)
    {
        var list = new List<string>();
        foreach (Node c in parent.Children)
        {
            list.Add(c.LocalName);
        }

        return [.. list];
    }

    private static Document Parse(string html, bool scripting = false) => Document.Parse(html, new ParseOptions { ScriptingEnabled = scripting });

    [Fact]
    public void A_simple_page_has_the_expected_shape()
    {
        Document doc = Parse("<!DOCTYPE html><title>T</title><p class=a>Hi <b>there</b></p>");
        Assert.Equal(QuirksMode.NoQuirks, doc.Mode);
        Assert.Equal("html", doc.Doctype.LocalName);
        Node html = doc.DocumentElement;
        Assert.Equal("html", html.LocalName);
        Assert.Equal(["head", "body"], ChildNames(html));
        Node p = html.LastChild.FirstChild;
        Assert.Equal("p", p.LocalName);
        Assert.Equal("a", p.GetAttribute("class"));
        Assert.Equal("a", p.GetAttribute("CLASS"));
        Assert.Equal("Hi ", p.FirstChild.Data.ToString());
        Assert.Equal("there", p.LastChild.FirstChild.Data.ToString());
        Assert.Equal("<p class=\"a\">Hi <b>there</b></p>", doc.SerializeNode(p));
        Assert.Null(DocumentInvariants.Check(doc));
    }

    [Fact]
    public void Handles_into_one_snapshot_stay_with_it()
    {
        Document a = Parse("<p>x");
        Document b = Parse("<p>x");
        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(a.DocumentElement, b.DocumentElement);
        Assert.Equal(a.DocumentElement, a.GetNode(a.DocumentElement.Index));
        Assert.True(default(Node).IsNull);
        Assert.True(a.Root.Parent.IsNull);
    }

    [Theory]
    [InlineData("<!DOCTYPE html>", QuirksMode.NoQuirks)]
    [InlineData("", QuirksMode.Quirks)]
    [InlineData("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"x\">", QuirksMode.LimitedQuirks)]
    [InlineData("<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\">", QuirksMode.Quirks)]
    [InlineData("<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\" \"u\">", QuirksMode.LimitedQuirks)]
    [InlineData("<!DOCTYPE foo>", QuirksMode.Quirks)]
    [InlineData("<!DOCTYPE html SYSTEM \"about:legacy-compat\">", QuirksMode.NoQuirks)]
    public void The_doctype_selects_the_mode(string doctype, QuirksMode expected) =>
        Assert.Equal(expected, Parse(doctype + "<p>").Mode);

    [Fact]
    public void Template_content_is_a_separate_root()
    {
        Document doc = Parse("<template><div>a</div></template>");
        Node template = doc.DocumentElement.FirstChild.FirstChild;
        Assert.Equal("template", template.LocalName);
        Assert.False(template.HasChildNodes);
        Assert.Equal(NodeKind.DocumentFragment, template.TemplateContent.Kind);
        Assert.Equal("div", template.TemplateContent.FirstChild.LocalName);
        Assert.Equal("<div>a</div>", doc.SerializeChildren(template));
        Assert.Null(DocumentInvariants.Check(doc));
    }

    [Fact]
    public void Foreign_content_gets_namespaces_and_repaired_names()
    {
        Document doc = Parse("<svg viewbox='0 0 1 1' xlink:href=u><foreignobject><p>x</p></foreignobject><clippath></svg><math definitionurl=d><mi>a</mi></math>");
        Node body = doc.DocumentElement.LastChild;
        Node svg = body.FirstChild;
        Assert.Equal(ElementNamespace.Svg, svg.Namespace);
        Assert.Equal("viewBox", svg.Attributes[0].LocalName);
        Assert.Equal("href", svg.Attributes[1].LocalName);
        Assert.Equal(AttributeNamespace.XLink, svg.Attributes[1].Namespace);
        Assert.Equal("xlink:href", svg.Attributes[1].QualifiedName);
        Node fo = svg.FirstChild;
        Assert.Equal("foreignObject", fo.LocalName);
        Assert.Equal(ElementNamespace.Html, fo.FirstChild.Namespace);
        Assert.Equal("clipPath", svg.LastChild.LocalName);
        Node math = svg.NextSibling;
        Assert.Equal(ElementNamespace.MathMl, math.Namespace);
        Assert.Equal("definitionURL", math.Attributes[0].LocalName);
    }

    [Fact]
    public void A_late_meta_restarts_the_parse_with_the_declared_encoding()
    {
        // The prescan only reads the first 1024 bytes, so this meta is met while parsing.
        byte[] head = Encoding.ASCII.GetBytes("<!DOCTYPE html><p>" + new string(' ', 1100) + "<meta charset=windows-1251><p>");
        byte[] bytes = [.. head, 0xC0, 0xC1];
        Document doc = Document.Parse(bytes);
        Assert.Equal("windows-1251", doc.Source.Encoding.Name);
        Assert.Equal(EncodingConfidence.Certain, doc.Source.Confidence);
        Node last = doc.DocumentElement.LastChild.LastChild;
        Assert.Equal("\u0410\u0411", last.FirstChild.Data.ToString());
        Assert.Equal(bytes, doc.ToLosslessBytes());
    }

    [Fact]
    public void A_meta_that_agrees_does_not_restart_and_makes_the_encoding_certain()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("<p>" + new string(' ', 1100) + "<meta charset=windows-1252>x");
        Document doc = Document.Parse(bytes);
        Assert.Equal(EncodingConfidence.Certain, doc.Source.Confidence);
        Assert.Equal("windows-1252", doc.Source.Encoding.Name);
    }

    [Fact]
    public void A_transport_charset_is_not_overridden_by_a_meta()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("<meta charset=windows-1251><p>\u00C0".Replace('\u00C0', 'x'));
        Document doc = Document.Parse(bytes, new ParseOptions { ContentType = "text/html; charset=utf-8" });
        Assert.Equal("UTF-8", doc.Source.Encoding.Name);
    }

    public static IEnumerable<object[]> LegacyInputs()
    {
        const string Page = "<!DOCTYPE html><meta charset={0}><title>t</title><p>caf\u00E9 <b>\u00FCber</b></p>\r\n<!-- c -->";
        yield return [Encoding.Latin1.GetBytes(string.Format(System.Globalization.CultureInfo.InvariantCulture, Page, "windows-1252")), "windows-1252"];
        yield return [Encoding.UTF8.GetBytes(string.Format(System.Globalization.CultureInfo.InvariantCulture, Page, "utf-8")), "UTF-8"];
        yield return [new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("<p>caf\u00E9 \u4E2D</p>")).ToArray(), "UTF-16LE"];
        yield return [new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("<p>\u4E2D\u6587</p>\n")).ToArray(), "UTF-8"];
        yield return [Encoding.ASCII.GetBytes("<meta charset=shift_jis><p>").Concat(new byte[] { 0x82, 0xA0, 0x82, 0xA2 }).Concat(Encoding.ASCII.GetBytes("</p>")).ToArray(), "Shift_JIS"];
        yield return [Encoding.ASCII.GetBytes("<meta charset=gbk><p>").Concat(new byte[] { 0xD6, 0xD0, 0xCE, 0xC4 }).Concat(Encoding.ASCII.GetBytes("</p>\r")).ToArray(), "GBK"];
        yield return [Encoding.ASCII.GetBytes("<meta charset=iso-2022-jp><p>\u001B$B$\"\u001B(B</p>"), "ISO-2022-JP"];
    }

    [Theory]
    [MemberData(nameof(LegacyInputs))]
    public void Legacy_encodings_round_trip_byte_for_byte(byte[] bytes, string encoding)
    {
        Document doc = Document.Parse(bytes);
        Assert.Equal(encoding, doc.Source.Encoding.Name);
        Assert.Equal(bytes, doc.ToLosslessBytes());
        Assert.Null(DocumentInvariants.Check(doc));
    }

    [Fact]
    public void Every_tree_construction_and_tokenizer_input_round_trips_as_utf8()
    {
        int count = 0;
        foreach (string file in TreeConstructionSuite.Files())
        {
            foreach (TreeCase c in TreeConstructionSuite.Load(file))
            {
                RoundTrip(c.Data);
                count++;
            }
        }

        foreach (string file in TokenizerSuite.Files())
        {
            foreach (TokenizerCase c in TokenizerSuite.Load(file))
            {
                RoundTrip(c.Input);
                count++;
            }
        }

        Assert.True(count > 8000);
    }

    private static void RoundTrip(string input)
    {
        // Lone surrogates cannot be encoded as UTF-8; the tests that contain them are about the tokenizer.
        byte[] bytes = new UTF8Encoding(false, false).GetBytes(input);
        Document doc = Document.Parse(bytes);
        Assert.Equal(bytes, doc.ToLosslessBytes());
        string? problem = DocumentInvariants.Check(doc);
        Assert.True(problem is null, $"{problem} for input: {input}");
        _ = doc.SerializeChildren(doc.Root);
    }

    [Fact]
    public void Serializing_follows_the_fragment_algorithm()
    {
        Document doc = Parse("<div id='a&quot;b'>x &amp; y\u00A0<br><img src=s><textarea>\n<b></textarea><script>if (a<b) {}</script><!--c--></div>");
        Node div = doc.DocumentElement.LastChild.FirstChild;
        Assert.Equal(
            "x &amp; y&nbsp;<br><img src=\"s\"><textarea>&lt;b&gt;</textarea><script>if (a<b) {}</script><!--c-->",
            doc.SerializeChildren(div));
        Assert.Equal("<div id=\"a&quot;b\">", doc.SerializeNode(div)[..19]);
        Assert.Equal(string.Empty, doc.SerializeChildren(div.FirstChild.NextSibling.NextSibling));
    }

    [Fact]
    public void Fragments_are_parsed_in_the_context_element()
    {
        Document td = Document.ParseFragment("<td>a<td>b", new FragmentContext(ElementNamespace.Html, "tr"));
        Assert.True(td.IsFragment);
        Assert.Equal(NodeKind.DocumentFragment, td.Root.Kind);
        Assert.Equal(["td", "td"], ChildNames(td.Root));
        Assert.Equal("<td>a</td><td>b</td>", td.SerializeChildren(td.Root));

        Document body = Document.ParseFragment("<p>a<p>b</i>", new FragmentContext(ElementNamespace.Html, "body"));
        Assert.Equal("<p>a</p><p>b</p>", body.SerializeChildren(body.Root));

        Document rc = Document.ParseFragment("a<b>&amp;</title>", new FragmentContext(ElementNamespace.Html, "title"));
        Assert.Equal("a&lt;b&gt;&amp;&lt;/title&gt;", rc.SerializeChildren(rc.Root));
    }

    [Fact]
    public void A_fragment_context_can_come_from_an_element_of_a_document()
    {
        Document page = Parse("<form><div id=d></div></form>");
        Node div = page.DocumentElement.LastChild.FirstChild.FirstChild;
        FragmentContext ctx = FragmentContext.FromElement(div);
        Assert.True(ctx.InsideForm);
        Assert.Equal("div", ctx.LocalName);
        Document frag = Document.ParseFragment("<form><input></form>", ctx);
        // The form pointer is set from the context, so the nested form start tag is ignored.
        Assert.Equal("<input>", frag.SerializeChildren(frag.Root));
    }

    [Fact]
    public void Scripting_changes_how_noscript_is_parsed()
    {
        Document off = Parse("<body><noscript><p>x</p></noscript>", scripting: false);
        Document on = Parse("<body><noscript><p>x</p></noscript>", scripting: true);
        Node noscriptOff = off.DocumentElement.LastChild.FirstChild;
        Node noscriptOn = on.DocumentElement.LastChild.FirstChild;
        Assert.Equal("noscript", noscriptOff.LocalName);
        Assert.Equal("p", noscriptOff.FirstChild.LocalName);
        Assert.Equal("<p>x</p>", noscriptOn.FirstChild.Data.ToString());
    }

    [Fact]
    public void Parse_errors_are_the_tokenizers_and_only_recorded_on_request()
    {
        Assert.Empty(Parse("<p a=1 a=2>").ParseErrors);
        Document doc = Document.Parse("<p a=1 a=2>", new ParseOptions { RecordErrors = true });
        Assert.NotEmpty(doc.ParseErrors);
    }

    [Fact]
    public void Cancellation_is_honoured()
    {
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => Document.Parse("<p>x", null, cts.Token));
    }
}
