using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using RecursiveAI.OneHtml.Editing;
using Xunit;
using static RecursiveAI.OneHtml.Tests.Editing.EditTestHelpers;

namespace RecursiveAI.OneHtml.Tests.Editing;

public class EditFallbackTests
{
    [Fact]
    public void An_element_with_an_implied_start_tag_is_edited_through_the_enclosing_element()
    {
        Document d = Parse("<html><p>x</html>");
        EditResult r = d.Apply([Edit.SetAttribute(Find(d, "body"), "class", "c")]);
        Assert.True(r.Succeeded, r.Failure);
        Assert.Equal("<html><head></head><body class=\"c\"><p>x</p></body></html>", r.Document!.Source.Text);
        FallbackInfo fb = Assert.Single(r.Fallbacks);
        Assert.Equal(1, fb.Level);
        Assert.Equal(1, r.Outcomes[0].FallbackLevel);
        Assert.Contains("start tag", fb.Reason, StringComparison.Ordinal);
        Assert.True(Assert.Single(r.Changes!.Ranges).IsFallback);
    }

    [Fact]
    public void A_local_splice_that_changes_how_the_markup_parses_is_widened()
    {
        // The leading line feed after <pre> is dropped by the parser, so once the pre is gone the text needs the
        // enclosing element rewritten without it.
        Document d = Parse("<div><pre>\nx</pre> y</div>");
        EditResult r = d.Apply([Edit.Unwrap(Find(d, "pre"))]);
        Assert.True(r.Succeeded, r.Failure);
        Assert.Equal("<div>x y</div>", r.Document!.Source.Text);
        Assert.Single(r.Fallbacks);
    }

    [Fact]
    public void Markup_that_no_html_can_express_fails_cleanly()
    {
        Document d = Parse("<table><tbody><tr><td>x</td></tr></tbody></table>");
        EditResult r = d.Apply([Edit.Append(Find(d, "table"), "hello")]);
        Assert.False(r.Succeeded);
        Assert.Null(r.Document);
        Assert.Equal(EditStatus.Failed, r.Outcomes[0].Status);
        Assert.NotNull(r.Outcomes[0].Reason);

        d = Parse("<p>a<p>b");
        r = d.Apply([Edit.Append(Find(d, "p"), "<div></div>")]);
        Assert.False(r.Succeeded);
        Assert.Equal(EditStatus.Failed, r.Outcomes[0].Status);
    }

    [Fact]
    public void Parser_copies_are_never_edited_directly()
    {
        Document d = Parse("<select><button><selectedcontent></selectedcontent></button><option selected><b>a</b></option></select>");
        Node clone = Find(d, "selectedcontent").FirstChild;
        Assert.False(clone.IsNull);
        Assert.Equal(NodeOrigin.Synthesized, clone.Origin);
        EditResult r = d.Apply([Edit.SetAttribute(clone, "class", "x")]);
        Assert.Equal(EditStatus.Invalid, r.Outcomes[0].Status);
        r = d.Apply([Edit.Append(Find(d, "selectedcontent"), "x")]);
        Assert.Equal(EditStatus.Invalid, r.Outcomes[0].Status);
        r = d.Apply([Edit.SetAttribute(Find(d, "b", 1), "class", "x")]);
        Assert.True(r.Succeeded, r.Failure);
    }

    [Fact]
    public void Text_added_in_the_same_transaction_can_be_replaced()
    {
        Assert.Equal("<p>y</p>", Apply("<p></p>", d => [Edit.Prepend(Find(d, "p"), "x"), Edit.ReplaceText(Find(d, "p"), "y")]));
        Assert.Equal("<p>y</p>", Apply("<p>a</p>", d => [Edit.Prepend(Find(d, "p"), "x"), Edit.ReplaceText(Find(d, "p"), "y")]));
    }

    [Fact]
    public void Later_edits_see_earlier_ones()
    {
        Assert.Equal("<div><section><p>1</p></section></div>", Apply("<div><i><p>1</p></i></div>", d => [Edit.Unwrap(Find(d, "i")), Edit.Wrap(Find(d, "p"), "<section></section>")]));
        Assert.Equal("<p>b</p>", Apply("<p>a<b></b></p>", d => [Edit.Remove(Find(d, "b")), Edit.ReplaceText(Find(d, "p"), "b")]));
    }
}

public class EditEncodingTests
{
    static EditEncodingTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void A_character_the_encoding_lacks_becomes_a_character_reference()
    {
        byte[] bytes = Encoding.Latin1.GetBytes("<meta charset=\"iso-8859-1\"><p title=\"caf\u00E9\">caf\u00E9</p>");
        Document d = ParseBytes(bytes);
        EditResult r = d.Apply([Edit.ReplaceText(FindText(d, "caf"), "caf\u00E9 \u2603 \U0001F600"), Edit.SetAttribute(Find(d, "p"), "title", "\u00E9\u2603")]);
        Assert.True(r.Succeeded, r.Failure);
        Document nd = r.Document!;
        Assert.Equal("caf\u00E9 \u2603 \U0001F600", FindText(nd, "caf").Data.ToString());
        Assert.Equal("\u00E9\u2603", Find(nd, "p").GetAttribute("title"));
        string raw = Encoding.Latin1.GetString(nd.Source.Bytes.ToArray());
        Assert.Contains("caf\u00E9 &#x2603; &#x1F600;", raw, StringComparison.Ordinal);
        Assert.Contains("title=\"\u00E9&#x2603;\"", raw, StringComparison.Ordinal);
        Assert.True(nd.Source.Bytes.Span.Contains((byte)0xE9));
    }

    [Fact]
    public void An_unrepresentable_character_where_no_reference_works_fails()
    {
        byte[] bytes = Encoding.Latin1.GetBytes("<meta charset=\"iso-8859-1\"><script>x</script><!--c-->");
        Document d = ParseBytes(bytes);
        EditResult r = d.Apply([Edit.ReplaceText(Find(d, "script"), "var s = '\u2603';")]);
        Assert.False(r.Succeeded);
        Assert.Contains("encoding", r.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Characters_the_encoding_has_are_written_as_bytes()
    {
        Document d = ParseBytes(Encoding.GetEncoding("shift_jis").GetBytes("<meta charset=\"shift_jis\"><p>\u65E5\u672C</p>"));
        EditResult r = d.Apply([Edit.Append(Find(d, "p"), "<b>\u8A9E</b>\u2603")]);
        Assert.True(r.Succeeded, r.Failure);
        byte[] expected = Encoding.GetEncoding("shift_jis").GetBytes("<meta charset=\"shift_jis\"><p>\u65E5\u672C<b>\u8A9E</b>&#x2603;</p>");
        Assert.Equal(expected, r.Document!.Source.Bytes.ToArray());
    }

    [Fact]
    public void Utf16_documents_keep_their_byte_order_mark()
    {
        string html = "<p class=a>hi</p>";
        byte[] bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(html)];
        Document d = ParseBytes(bytes);
        EditResult r = d.Apply([Edit.SetAttribute(Find(d, "p"), "id", "\u2603")]);
        Assert.True(r.Succeeded, r.Failure);
        Assert.Equal([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("<p class=a id=\"\u2603\">hi</p>")], r.Document!.Source.Bytes.ToArray());
    }

    [Fact]
    public void Bytes_outside_the_edit_are_untouched_and_a_bom_stays()
    {
        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("<!--a--><p>\u00E9</p>")];
        Document d = ParseBytes(bytes);
        EditResult r = d.Apply([Edit.InsertBefore(d.Root.FirstChild, "<!--n-->")]);
        Assert.True(r.Succeeded, r.Failure);
        Assert.Equal([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("<!--n--><!--a--><p>\u00E9</p>")], r.Document!.Source.Bytes.ToArray());
        EditedRange range = Assert.Single(r.Changes!.Ranges);
        Assert.Equal(new SourceRange(3, 3), range.OldByteRange);
        Assert.Equal(new SourceRange(3, 11), range.NewByteRange);
    }

    [Fact]
    public void New_lines_follow_the_documents_line_ending()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("<ul>\r\n  <li>a</li>\r\n</ul>\r\n");
        Document d = ParseBytes(bytes);
        EditResult r = d.Apply([Edit.Append(Find(d, "ul"), "<li>b</li>"), Edit.InsertAfter(Find(d, "ul"), "<p>x\ny</p>")]);
        Assert.True(r.Succeeded, r.Failure);
        Assert.Equal("<ul>\r\n  <li>a</li>\r\n  <li>b</li>\r\n</ul><p>x\r\ny</p>\r\n", Encoding.UTF8.GetString(r.Document!.Source.Bytes.Span));
    }
}

public class EditLimitTests
{
    [Fact]
    public void Limits_are_checked_before_any_work()
    {
        Document d = Parse("<p>a</p>");
        Node p = Find(d, "p");
        EditResult r = d.Apply([Edit.Remove(p), Edit.Remove(p)], new EditOptions { MaxEdits = 1 });
        Assert.False(r.Succeeded);
        Assert.Equal(EditStatus.NotAttempted, r.Outcomes[0].Status);
        r = d.Apply([Edit.Append(p, new string('x', 100))], new EditOptions { MaxInsertedLength = 50 });
        Assert.False(r.Succeeded);
        Assert.Contains("limit", r.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Growing_past_the_node_limit_is_reported_not_thrown()
    {
        var limits = new ParseLimits { MaxNodes = 40 };
        Document d = Document.Parse("<p>a</p>", new ParseOptions { Limits = limits });
        EditResult r = d.Apply([Edit.Append(Find(d, "p"), string.Concat(System.Linq.Enumerable.Repeat("<b>x</b>", 50)))]);
        Assert.False(r.Succeeded);
        Assert.Equal(EditStatus.Invalid, r.Outcomes[0].Status);
    }

    [Fact]
    public void Foreign_snapshots_and_fragments_are_rejected()
    {
        Document a = Parse("<p>a</p>");
        Document b = Parse("<p>a</p>");
        Assert.Throws<ArgumentException>(() => new EditTransaction(a, [Edit.Remove(Find(b, "p"))]));
        Assert.Throws<ArgumentException>(() => b.Apply(new EditTransaction(a, [])));
        Document frag = Document.ParseFragment("<p>a</p>", new FragmentContext(ElementNamespace.Html, "div"));
        EditResult r = frag.Apply([Edit.Remove(frag.Root.FirstChild)]);
        Assert.False(r.Succeeded);
    }

    [Fact]
    public void Cancellation_is_honoured()
    {
        Document d = Parse("<p>a</p>");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => d.Apply(new EditTransaction(d, [Edit.Remove(Find(d, "p"))]), null, cts.Token));
    }

    [Fact]
    public void Many_edits_stay_linear()
    {
        var sb = new StringBuilder("<ul>");
        for (int i = 0; i < 20000; i++)
        {
            sb.Append("<li class=a>").Append(i).Append("</li>");
        }

        sb.Append("</ul>");
        Document d = Parse(sb.ToString());
        var edits = new List<Edit>();
        for (Node li = Find(d, "li"); !li.IsNull; li = li.NextSibling)
        {
            edits.Add(Edit.SetAttribute(li, "class", "b"));
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        EditResult r = d.Apply(edits);
        Assert.True(r.Succeeded, r.Failure);
        Assert.Equal(1, r.Verifications);
        Assert.True(clock.Elapsed.TotalSeconds < 10, "took " + clock.Elapsed);
    }
}

public class EditChangeSetTests
{
    [Fact]
    public void The_change_set_names_added_removed_and_changed_nodes()
    {
        Document d = Parse("<div><p id=a>one</p><span>two</span><b>three</b></div>");
        Node p = Find(d, "p");
        Node span = Find(d, "span");
        EditResult r = d.Apply([Edit.SetAttribute(p, "id", "z"), Edit.Remove(span), Edit.InsertAfter(Find(d, "b"), "<i>n</i>")]);
        Assert.True(r.Succeeded, r.Failure);
        ChangeSet cs = r.Changes!;
        Assert.Same(d, cs.OldDocument);
        Assert.Same(r.Document, cs.NewDocument);
        Node added = Assert.Single(cs.Added);
        Assert.Equal("i", added.LocalName);
        Assert.Same(r.Document, added.Document);
        Node removed = Assert.Single(cs.Removed);
        Assert.Equal(span, removed);
        Assert.True(cs.MapToNew(span).IsNull);
        Assert.Equal("z", cs.MapToNew(p).GetAttribute("id"));
        Assert.Contains(cs.Changed, c => c.Old == p && c.Changes == NodeChanges.Attributes && c.Node.LocalName == "p");
        Assert.Contains(cs.Changed, c => c.Old == Find(d, "div") && (c.Changes & NodeChanges.Children) != 0);
        Assert.Equal(3, cs.Ranges.Count);
        Assert.All(cs.Ranges, rg => Assert.False(rg.IsFallback));
        Assert.Equal([0], cs.Ranges[0].EditIndexes);
        Assert.Equal(3, r.Outcomes.Count);
        Assert.All(r.Outcomes, o => Assert.Equal(EditStatus.Applied, o.Status));
    }

    [Fact]
    public void Wrapping_and_unwrapping_report_moved_nodes()
    {
        Document d = Parse("<div><p>1</p><p>2</p></div>");
        Node p1 = Find(d, "p");
        EditResult r = d.Apply([Edit.Wrap(p1, "<section></section>")]);
        Assert.True(r.Succeeded, r.Failure);
        Assert.Equal("section", Assert.Single(r.Changes!.Added).LocalName);
        Assert.Contains(r.Changes.Changed, c => c.Old == p1 && c.Changes == NodeChanges.Parent);
        Node moved = r.Changes.MapToNew(p1);
        Assert.Equal("section", moved.Parent.LocalName);
        Assert.Equal("div", moved.Parent.Parent.LocalName);
    }

    [Fact]
    public void Handles_of_the_old_snapshot_keep_working_after_many_edits()
    {
        Document d = Parse("<p>a</p><p>b</p>");
        Node second = Find(d, "p", 1);
        Document current = d;
        for (int i = 0; i < 20; i++)
        {
            EditResult r = current.Apply([Edit.SetAttribute(Find(current, "p"), "n", i.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
            Assert.True(r.Succeeded);
            current = r.Document!;
        }

        Assert.Equal("b", second.FirstChild.Data.ToString());
        Assert.Equal("<p n=\"19\">a</p><p>b</p>", current.Source.Text);
    }
}

public class EditRoundTripTests
{
    [Fact]
    public void An_edit_that_changes_nothing_changes_no_bytes()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("<!DOCTYPE html>\r\n<P CLASS=a>x</P>\r\n");
        Document d = ParseBytes(bytes);
        EditResult r = d.Apply([Edit.SetAttribute(Find(d, "p"), "class", "a"), Edit.RemoveAttribute(Find(d, "p"), "none"), Edit.ReplaceText(FindText(d, "x"), "x")]);
        Assert.True(r.Succeeded);
        Assert.Same(d, r.Document);
        Assert.Equal(bytes, r.Document!.ToLosslessBytes());
    }

    [Fact]
    public void An_insert_then_remove_of_the_same_place_restores_the_bytes()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("<ul>\n  <li>a</li>\n</ul>");
        Document d = ParseBytes(bytes);
        EditResult added = d.Apply([Edit.Append(Find(d, "ul"), "<li>b</li>")]);
        Assert.True(added.Succeeded);
        var removals = new List<Edit>();
        foreach (Node n in added.Changes!.Added)
        {
            removals.Add(Edit.Remove(n));
        }

        EditResult back = added.Document!.Apply(removals);
        Assert.True(back.Succeeded);
        Assert.Equal(bytes, back.Document!.Source.Bytes.ToArray());
    }
}
