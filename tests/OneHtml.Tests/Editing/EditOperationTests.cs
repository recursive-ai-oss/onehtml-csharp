using Xunit;
using RecursiveAI.OneHtml.Editing;
using static RecursiveAI.OneHtml.Tests.Editing.EditTestHelpers;

namespace RecursiveAI.OneHtml.Tests.Editing;

public class EditOperationTests
{
    [Fact]
    public void SetAttribute_replaces_only_the_value()
    {
        Assert.Equal("<a  href='y' class=\"z\">t</a>", Apply("<a  href='x' class=\"z\">t</a>", d => [Edit.SetAttribute(Find(d, "a"), "href", "y")]));
        Assert.Equal("<a href=\"it&quot;s\">", Apply("<a href=\"x\">", d => [Edit.SetAttribute(Find(d, "a"), "HREF", "it\"s")]));
        Assert.Equal("<a href=y>", Apply("<a href=x>", d => [Edit.SetAttribute(Find(d, "a"), "href", "y")]));
        Assert.Equal("<a href=\"y z\">", Apply("<a href=x>", d => [Edit.SetAttribute(Find(d, "a"), "href", "y z")]));
        Assert.Equal("<input disabled=\"x\">", Apply("<input disabled>", d => [Edit.SetAttribute(Find(d, "input"), "disabled", "x")]));
    }

    [Fact]
    public void SetAttribute_adds_after_the_last_attribute()
    {
        Assert.Equal("<a href=\"x\" title=\"t\" >hi</a>", Apply("<a href=\"x\" >hi</a>", d => [Edit.SetAttribute(Find(d, "a"), "title", "t")]));
        Assert.Equal("<p class=\"c\">", Apply("<p>", d => [Edit.SetAttribute(Find(d, "p"), "class", "c")]));
        Assert.Equal("<br class=\"c\"/>", Apply("<br/>", d => [Edit.SetAttribute(Find(d, "br"), "class", "c")]));
        Assert.Equal("<br id=\"a\" class=\"c\" />", Apply("<br id=\"a\" />", d => [Edit.SetAttribute(Find(d, "br"), "class", "c")]));
    }

    [Fact]
    public void SetAttribute_follows_the_documents_quote_and_case()
    {
        Assert.Equal("<P ID='a' CLASS='c'>", Apply("<P ID='a'>", d => [Edit.SetAttribute(Find(d, "p"), "class", "c")]));
    }

    [Fact]
    public void SetAttribute_twice_uses_the_last_value()
    {
        Assert.Equal("<a href=\"3\" t=\"b\">", Apply("<a href=\"1\">", d => [Edit.SetAttribute(Find(d, "a"), "href", "2"), Edit.SetAttribute(Find(d, "a"), "t", "a"), Edit.SetAttribute(Find(d, "a"), "href", "3"), Edit.SetAttribute(Find(d, "a"), "t", "b")]));
    }

    [Fact]
    public void RemoveAttribute_takes_the_whitespace_before_it()
    {
        Assert.Equal("<a b=\"1\" d=\"3\">", Apply("<a b=\"1\" c=\"2\" d=\"3\">", d => [Edit.RemoveAttribute(Find(d, "a"), "c")]));
        Assert.Equal("<a c=\"2\">", Apply("<a b=\"1\" c=\"2\">", d => [Edit.RemoveAttribute(Find(d, "a"), "b")]));
        Assert.Equal("<a b=1>", Apply("<a b=1 c>", d => [Edit.RemoveAttribute(Find(d, "a"), "c")]));
        Assert.Equal("<a>", Apply("<a b>", d => [Edit.RemoveAttribute(Find(d, "a"), "b")]));
        Assert.Equal("<a b>", Apply("<a b>", d => [Edit.RemoveAttribute(Find(d, "a"), "zzz")]));
    }

    [Fact]
    public void RemoveAttribute_then_set_puts_it_back_at_the_end()
    {
        Assert.Equal("<a c=\"2\" b=\"9\">", Apply("<a b=\"1\" c=\"2\">", d => [Edit.RemoveAttribute(Find(d, "a"), "b"), Edit.SetAttribute(Find(d, "a"), "b", "9")]));
    }

    [Fact]
    public void ReplaceText_on_a_text_node_escapes()
    {
        Assert.Equal("<p>a &amp; &lt;b&gt;</p>", Apply("<p>old</p>", d => [Edit.ReplaceText(FindText(d, "old"), "a & <b>")]));
        Assert.Equal("<p>one<b>two</b>NEW</p>", Apply("<p>one<b>two</b>old</p>", d => [Edit.ReplaceText(FindText(d, "old"), "NEW")]));
    }

    [Fact]
    public void ReplaceText_in_raw_text_elements_is_not_escaped()
    {
        Assert.Equal("<script>if (a < b && c) {}</script>", Apply("<script>x</script>", d => [Edit.ReplaceText(Find(d, "script"), "if (a < b && c) {}")]));
        Assert.Equal("<style>a > b { }</style>", Apply("<style>x</style>", d => [Edit.ReplaceText(Find(d, "style"), "a > b { }")]));
        Assert.Equal("<title>a &amp; b</title>", Apply("<title>x</title>", d => [Edit.ReplaceText(Find(d, "title"), "a & b")]));
        Assert.Equal("<textarea>\n\nx</textarea>", Apply("<textarea>y</textarea>", d => [Edit.ReplaceText(Find(d, "textarea"), "\nx")]));
    }

    [Fact]
    public void ReplaceText_on_an_element_with_no_text_adds_a_first_child()
    {
        Assert.Equal("<p>hi<b></b></p>", Apply("<p><b></b></p>", d => [Edit.ReplaceText(Find(d, "p"), "hi")]));
        Assert.Equal("<p>X<b>b</b></p>", Apply("<p>a<b>b</b>c</p>", d => [Edit.ReplaceText(Find(d, "p"), "X")]));
    }

    [Fact]
    public void Insert_next_to_a_node_in_inline_content()
    {
        Assert.Equal("<p><b>x</b>hello</p>", Apply("<p>hello</p>", d => [Edit.Prepend(Find(d, "p"), "<b>x</b>")]));
        Assert.Equal("<p>hello<b>x</b></p>", Apply("<p>hello</p>", d => [Edit.Append(Find(d, "p"), "<b>x</b>")]));
        Assert.Equal("<p><i>1</i><b>x</b>hello</p>", Apply("<p><i>1</i>hello</p>", d => [Edit.InsertAfter(Find(d, "i"), "<b>x</b>")]));
        Assert.Equal("<p><b>x</b><i>1</i>hello</p>", Apply("<p><i>1</i>hello</p>", d => [Edit.InsertBefore(Find(d, "i"), "<b>x</b>")]));
    }

    [Fact]
    public void Insert_keeps_the_indentation_of_siblings_on_their_own_lines()
    {
        const string Html = "<ul>\n  <li>a</li>\n  <li>b</li>\n</ul>";
        Assert.Equal("<ul>\n  <li>a</li>\n  <li>b</li>\n  <li>c</li>\n</ul>", Apply(Html, d => [Edit.Append(Find(d, "ul"), "<li>c</li>")]));
        Assert.Equal("<ul>\n  <li>c</li>\n  <li>a</li>\n  <li>b</li>\n</ul>", Apply(Html, d => [Edit.Prepend(Find(d, "ul"), "<li>c</li>")]));
        Assert.Equal("<ul>\n  <li>a</li>\n  <li>c</li>\n  <li>b</li>\n</ul>", Apply(Html, d => [Edit.InsertAfter(Find(d, "li"), "<li>c</li>")]));
        Assert.Equal("<ul>\n  <li>a</li>\n  <li>c</li>\n  <li>b</li>\n</ul>", Apply(Html, d => [Edit.InsertBefore(Find(d, "li", 1), "<li>c</li>")]));
        Assert.Equal("<ul>\n\t<li>a</li>\n\t<li>c</li>\n\t<li>d</li>\n</ul>", Apply("<ul>\n\t<li>a</li>\n</ul>", d => [Edit.Append(Find(d, "ul"), "<li>c</li><li>d</li>")]));
    }

    [Fact]
    public void Insert_into_an_empty_element()
    {
        Assert.Equal("<div><b>x</b></div>", Apply("<div></div>", d => [Edit.Append(Find(d, "div"), "<b>x</b>")]));
        Assert.Equal("<div><b>x</b></div>", Apply("<div></div>", d => [Edit.Prepend(Find(d, "div"), "<b>x</b>")]));
        Assert.Equal("<div>text</div>", Apply("<div></div>", d => [Edit.Append(Find(d, "div"), "text")]));
    }

    [Fact]
    public void Insert_normalizes_new_markup_to_the_local_style()
    {
        Assert.Equal("<DIV CLASS='a'><IMG SRC='y' /><BR /></DIV>", Apply("<DIV CLASS='a'><IMG SRC='y' /></DIV>", d => [Edit.Append(Find(d, "div"), "<br>")]));
        Assert.Equal("<P ID='a'><A HREF='u'>l</A></P>", Apply("<P ID='a'></P>", d => [Edit.Append(Find(d, "p"), "<a href=\"u\">l</a>")]));
        Assert.Equal("<p id=\"a\"><br/></p>", Apply("<p id=\"a\"><img/></p>", d => [Edit.Remove(Find(d, "img")), Edit.Append(Find(d, "p"), "<br>")]));
    }

    [Fact]
    public void Replace_and_Remove()
    {
        Assert.Equal("<p>a<i>n</i>c</p>", Apply("<p>a<b>old</b>c</p>", d => [Edit.Replace(Find(d, "b"), "<i>n</i>")]));
        Assert.Equal("<p>ac</p>", Apply("<p>a<b>old</b>c</p>", d => [Edit.Remove(Find(d, "b"))]));
        Assert.Equal("<p>ac</p>", Apply("<p>a<b>old</b>c</p>", d => [Edit.Replace(Find(d, "b"), "")]));
        Assert.Equal("<p>a</p>", Apply("<p>a<!--c--></p>", d => [Edit.Remove(Find(d, "p").LastChild)]));
        Assert.Equal("<ul>\n  <li>a</li>\n  \n</ul>", Apply("<ul>\n  <li>a</li>\n  <li>b</li>\n</ul>", d => [Edit.Remove(Find(d, "li", 1))]));
    }

    [Fact]
    public void Wrap_a_node_and_a_run_of_siblings()
    {
        Assert.Equal("<p>a<span class=\"w\"><b>x</b></span>c</p>", Apply("<p>a<b>x</b>c</p>", d => [Edit.Wrap(Find(d, "b"), "<span class=\"w\"></span>")]));
        Assert.Equal("<div><section><p>1</p><p>2</p></section><p>3</p></div>", Apply("<div><p>1</p><p>2</p><p>3</p></div>", d => [Edit.Wrap(Find(d, "p"), "<section></section>", 2)]));
        Assert.Equal("<div><section><h1>t</h1><p>1</p></section></div>", Apply("<div><p>1</p></div>", d => [Edit.Wrap(Find(d, "p"), "<section><h1>t</h1></section>")]));
    }

    [Fact]
    public void Unwrap_removes_only_the_tags()
    {
        Assert.Equal("<p>a<i>x</i>y c</p>", Apply("<p>a<b><i>x</i>y</b> c</p>", d => [Edit.Unwrap(Find(d, "b"))]));
        Assert.Equal("<p>a c</p>", Apply("<p>a<b></b> c</p>", d => [Edit.Unwrap(Find(d, "b"))]));
        Assert.Equal("<ul><li>a<li>b</li></ul>", Apply("<ul><li>a<li>b</ul>", d => [Edit.Unwrap(Find(d, "li", 1)), Edit.Wrap(FindText(d, "b"), "<li></li>")]));
    }

    [Fact]
    public void SetStyle_edits_one_declaration_in_place()
    {
        Assert.Equal("<p style=\"color: blue; margin:0\">", Apply("<p style=\"color: red; margin:0\">", d => [Edit.SetStyle(Find(d, "p"), "color", "blue")]));
        Assert.Equal("<p style=\"color: red; margin:0; top:1px;\">", Apply("<p style=\"color: red; margin:0;\">", d => [Edit.SetStyle(Find(d, "p"), "top", "1px")]));
        Assert.Equal("<p style=\"color:red !important\">", Apply("<p style=\"color:red\">", d => [Edit.SetStyle(Find(d, "p"), "color", "red", important: true)]));
        Assert.Equal("<p style=\"color:blue\">", Apply("<p style=\"color:red !important\">", d => [Edit.SetStyle(Find(d, "p"), "color", "blue")]));
        Assert.Equal("<p style=\"color: red;\">", Apply("<p>", d => [Edit.SetStyle(Find(d, "p"), "color", "red")]));
        Assert.Equal("<p style=\"a: 1; --x: y\">", Apply("<p style=\"a: 1\">", d => [Edit.SetStyle(Find(d, "p"), "--x", "y")]));
    }

    [Fact]
    public void RemoveStyle_deletes_declarations_with_their_separators()
    {
        Assert.Equal("<p style=\"a:1; c:3\">", Apply("<p style=\"a:1; b:2; c:3\">", d => [Edit.RemoveStyle(Find(d, "p"), "b")]));
        Assert.Equal("<p style=\"a:1;\">", Apply("<p style=\"a:1; b:2;\">", d => [Edit.RemoveStyle(Find(d, "p"), "b")]));
        Assert.Equal("<p style=\"\">", Apply("<p style=\"b:2;\">", d => [Edit.RemoveStyle(Find(d, "p"), "B")]));
        Assert.Equal("<p style=\"a:1\">", Apply("<p style=\"a:1\">", d => [Edit.RemoveStyle(Find(d, "p"), "zz")]));
    }

    [Fact]
    public void SetStyle_inside_an_attribute_with_character_references_rewrites_the_value()
    {
        Assert.Equal("<p style=\"font-family: &quot;A&quot;; color: blue\">", Apply("<p style=\"font-family: &quot;A&quot;; color: red\">", d => [Edit.SetStyle(Find(d, "p"), "color", "blue")]));
    }

    [Fact]
    public void Template_content_is_the_target_of_append()
    {
        Assert.Equal("<template><b>x</b><i>y</i></template>", Apply("<template><b>x</b></template>", d => [Edit.Append(Find(d, "template"), "<i>y</i>")]));
        Assert.Equal("<template><i>y</i></template>", Apply("<template></template>", d => [Edit.Append(Find(d, "template"), "<i>y</i>")]));
    }

    [Fact]
    public void Foreign_attributes_get_the_parsers_names()
    {
        Assert.Equal("<svg viewBox=\"0 0 1 1\"></svg>", Apply("<svg></svg>", d => [Edit.SetAttribute(Find(d, "svg"), "viewbox", "0 0 1 1")]));
        Assert.Equal("<svg><use xlink:href=\"#a\"></use></svg>", Apply("<svg><use></use></svg>", d => [Edit.SetAttribute(Find(d, "use"), "xlink:href", "#a")]));
    }

    [Fact]
    public void Comments_can_be_inserted_at_the_document_level()
    {
        Assert.Equal("<!DOCTYPE html><!--x--><p>a</p>", Apply("<!DOCTYPE html><p>a</p>", d => [Edit.InsertAfter(d.Doctype, "<!--x-->")]));
    }

    [Fact]
    public void Empty_transaction_returns_the_same_snapshot()
    {
        Document d = Parse("<p>a</p>");
        EditResult r = d.Apply(System.Array.Empty<Edit>());
        Assert.True(r.Succeeded);
        Assert.Same(d, r.Document);
        Assert.Empty(r.Changes!.Ranges);
    }

    [Fact]
    public void Invalid_edits_are_reported_and_nothing_applies()
    {
        Document d = Parse("<p>a<br></p>");
        EditResult r = d.Apply([Edit.SetAttribute(Find(d, "p"), "x", "1"), Edit.Append(Find(d, "br"), "<b></b>")]);
        Assert.False(r.Succeeded);
        Assert.Null(r.Document);
        Assert.Equal(EditStatus.Invalid, r.Outcomes[1].Status);
        Assert.Equal(EditStatus.NotAttempted, r.Outcomes[0].Status);
        r = d.Apply([Edit.Remove(Find(d, "p")), Edit.SetAttribute(Find(d, "p"), "x", "1")]);
        Assert.Equal(EditStatus.Invalid, r.Outcomes[1].Status);
        r = d.Apply([Edit.SetAttribute(Find(d, "p"), "a b", "1")]);
        Assert.Equal(EditStatus.Invalid, r.Outcomes[0].Status);
        r = d.Apply([Edit.Unwrap(d.Root.FirstChild)]);
        Assert.False(r.Succeeded);
    }

    [Fact]
    public void Unwrap_of_a_text_or_comment_node_is_invalid()
    {
        Document d = Parse("<p>a<!--c--><b>x</b></p>");
        EditResult r = d.Apply([Edit.Unwrap(FindText(d, "a"))]);
        Assert.False(r.Succeeded);
        Assert.Equal(EditStatus.Invalid, r.Outcomes[0].Status);
        r = d.Apply([Edit.Unwrap(Find(d, "p").FirstChild.NextSibling)]);
        Assert.Equal(EditStatus.Invalid, r.Outcomes[0].Status);
    }

    [Fact]
    public void The_old_snapshot_is_untouched()
    {
        Document d = Parse("<p class=a>one</p>");
        Node p = Find(d, "p");
        byte[] before = d.ToLosslessBytes();
        EditResult r = d.Apply([Edit.SetAttribute(p, "class", "b"), Edit.ReplaceText(FindText(d, "one"), "two")]);
        Assert.True(r.Succeeded);
        Assert.Equal("a", p.GetAttribute("class"));
        Assert.Equal(before, d.ToLosslessBytes());
        Assert.Equal("<p class=b>two</p>", r.Document!.Source.Text);
        Assert.Equal("b", r.Changes!.MapToNew(p).GetAttribute("class"));
    }
}
