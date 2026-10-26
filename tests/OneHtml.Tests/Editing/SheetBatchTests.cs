using System;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Editing;

public class SheetBatchTests
{
    private static CssNode Rule(Stylesheet s, int n) => SheetEditTests.Rule(s, n);

    [Fact]
    public void Inline_sheet_edit_splices_only_the_style_text_and_keeps_crlf()
    {
        string html = "<!DOCTYPE html>\r\n<html><head><style>\r\na { b: c }\r\n\r\nd { e: f }\r\n</style></head>\r\n<body><p class=x>t</p></body></html>\r\n";
        Document doc = Document.Parse(Encoding.UTF8.GetBytes(html));
        InlineStylesheet inl = InlineStylesheets.Of(doc).Single();
        Assert.Equal("inline:0", inl.Name);
        Assert.DoesNotContain("\r", inl.Sheet.Text, StringComparison.Ordinal);
        Node p = EditTestHelpers.Find(doc, "p");
        var batch = new EditBatch(doc, [Edit.SetAttribute(p, "id", "q")], [SheetEdit.SetDeclaration(inl.Sheet, Rule(inl.Sheet, 1), "g", "h"), SheetEdit.RemoveRule(inl.Sheet, Rule(inl.Sheet, 0))]);
        BatchResult r = batch.Apply();
        Assert.True(r.Succeeded, r.Failure);
        string expected = html.Replace("a { b: c }\r\n\r\n", string.Empty, StringComparison.Ordinal).Replace("e: f }", "e: f; g: h }", StringComparison.Ordinal).Replace("<p class=x>", "<p class=x id=\"q\">", StringComparison.Ordinal);
        Assert.Equal(expected, Encoding.UTF8.GetString(r.Document!.ToLosslessBytes()));
        SheetUpdate u = Assert.Single(r.Sheets);
        Assert.Equal("inline:0", u.Name);
        Assert.Equal("\nd { e: f; g: h }\n", u.NewSheet.Text);
        Assert.Equal(2, u.Changes.Removed.Count);
    }

    [Fact]
    public void Batch_is_all_or_nothing()
    {
        Document doc = Document.Parse("<style>a{b:c}</style><p>x</p>");
        InlineStylesheet inl = InlineStylesheets.Of(doc).Single();
        Node t = EditTestHelpers.FindText(doc, "x");
        var batch = new EditBatch(doc, [Edit.SetAttribute(t, "id", "z")], [SheetEdit.SetDeclaration(inl.Sheet, Rule(inl.Sheet, 0), "d", "e")]);
        BatchResult r = batch.Apply();
        Assert.False(r.Succeeded);
        Assert.Null(r.Document);
        Assert.Equal(EditStatus.Invalid, r.Outcomes[0].Status);
        // A sheet edit that cannot apply stops the document edit too.
        var batch2 = new EditBatch(doc, [Edit.SetAttribute(EditTestHelpers.Find(doc, "p"), "id", "z")], [SheetEdit.SetDeclaration(inl.Sheet, Rule(inl.Sheet, 0), "d", "e;f")]);
        BatchResult r2 = batch2.Apply();
        Assert.False(r2.Succeeded);
        Assert.Equal(EditStatus.NotAttempted, r2.Outcomes[0].Status);
        Assert.Equal(EditStatus.Invalid, r2.Outcomes[1].Status);
    }

    [Fact]
    public void Inline_edit_in_empty_style_element()
    {
        Document doc = Document.Parse("<style></style><p>x</p>");
        InlineStylesheet inl = InlineStylesheets.Of(doc).Single();
        BatchResult r = new EditBatch(doc, null, [SheetEdit.AppendRule(inl.Sheet, "a { b: c }")]).Apply();
        Assert.True(r.Succeeded, r.Failure);
        Assert.Equal("<style>a { b: c }\n</style><p>x</p>", r.Document!.Source.Text);
    }

    [Fact]
    public void Style_elements_are_numbered_in_tree_order()
    {
        Document doc = Document.Parse("<style>a{}</style><div><style>b{}</style></div><template><style>t{}</style></template><svg><style>s{}</style></svg>");
        var sheets = InlineStylesheets.Of(doc);
        Assert.Equal(2, sheets.Count);
        Assert.Equal("b{}", sheets[1].Sheet.Text);
    }

    [Theory]
    [InlineData("utf-8", false)]
    [InlineData("windows-1252", false)]
    [InlineData("utf-16le", true)]
    [InlineData("utf-8", true)]
    public void Linked_sheet_edit_keeps_bytes_outside_the_edit(string label, bool bom)
    {
        string css = "/* caf\u00e9 */\r\na { b: c }\r\n\r\nd { e: \"\u00e9\" }\r\n";
        Encoding enc = label switch { "utf-16le" => Encoding.Unicode, "windows-1252" => Encoding.Latin1, _ => new UTF8Encoding(false) };
        byte[] body = enc.GetBytes(css);
        byte[] bytes = bom ? [.. (label == "utf-16le" ? new byte[] { 0xFF, 0xFE } : new byte[] { 0xEF, 0xBB, 0xBF }), .. body] : body;
        LinkedStylesheet l = LinkedStylesheet.Create("https://example.test/a.css", bytes, protocolEncodingLabel: label);
        Assert.Equal(css.Replace("\r\n", "\n", StringComparison.Ordinal), l.Sheet.Text);
        BatchResult r = new EditBatch(null, null, [SheetEdit.SetDeclaration(l.Sheet, Rule(l.Sheet, 0), "x", "y"), SheetEdit.RemoveRule(l.Sheet, Rule(l.Sheet, 1))], [l]).Apply();
        Assert.True(r.Succeeded, r.Failure);
        SheetUpdate u = r.Sheets.Single();
        Assert.NotNull(u.NewLinked);
        string expectedText = "/* caf\u00e9 */\r\na { b: c; x: y }\r\n";
        Assert.Equal(expectedText.Replace("\r\n", "\n", StringComparison.Ordinal), u.NewSheet.Text);
        byte[] expectedBytes = [.. bytes[..(bom ? (label == "utf-16le" ? 2 : 3) : 0)], .. enc.GetBytes(expectedText)];
        Assert.Equal(expectedBytes, u.NewLinked!.Bytes.ToArray());
        // The old snapshot is untouched.
        Assert.Equal(css.Replace("\r\n", "\n", StringComparison.Ordinal), l.Sheet.Text);
    }

    [Fact]
    public void Linked_sheet_refuses_text_its_encoding_cannot_hold()
    {
        LinkedStylesheet l = LinkedStylesheet.Create("https://example.test/a.css", Encoding.Latin1.GetBytes("a { b: c }"), protocolEncodingLabel: "windows-1252");
        BatchResult r = new EditBatch(null, null, [SheetEdit.SetDeclaration(l.Sheet, Rule(l.Sheet, 0), "content", "\"\u4e2d\"")], [l]).Apply();
        Assert.False(r.Succeeded);
        Assert.Equal(EditStatus.Failed, r.Outcomes[0].Status);
    }
}
