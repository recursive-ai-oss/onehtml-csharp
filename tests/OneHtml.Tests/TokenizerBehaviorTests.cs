using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Encodings;
using RecursiveAI.OneHtml.Parsing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests;

public class TokenizerBehaviorTests
{
    private static List<string> Tokens(HtmlTokenizer t)
    {
        var list = new List<string>();
        while (t.Next() != HtmlTokenKind.EndOfFile)
        {
            list.Add(t.Kind switch
            {
                HtmlTokenKind.Character => "text:" + t.Data.ToString(),
                HtmlTokenKind.StartTag => "<" + t.Name.ToString() + ">",
                HtmlTokenKind.EndTag => "</" + t.Name.ToString() + ">",
                HtmlTokenKind.Comment => "comment:" + t.Data.ToString(),
                HtmlTokenKind.Doctype => "doctype:" + t.Name.ToString(),
                _ => t.Kind.ToString(),
            });
        }

        return list;
    }

    [Fact]
    public void Tokens_carry_their_source_ranges()
    {
        const string html = "ab<p id=\"x\" class='y&amp;z' data=q hidden>c &amp; d</p><!--k--><!doctype html>";
        var t = new HtmlTokenizer(html);

        Assert.Equal(HtmlTokenKind.Character, t.Next());
        Assert.Equal((0, 2), (t.Start, t.End));
        Assert.Equal("ab", t.Data.ToString());

        Assert.Equal(HtmlTokenKind.StartTag, t.Next());
        Assert.Equal("p", t.Name.ToString());
        Assert.Equal(html.IndexOf("<p", StringComparison.Ordinal), t.Start);
        Assert.Equal(html.IndexOf('>') + 1, t.End);
        Assert.Equal((3, 4), (t.NameStart, t.NameEnd));
        ParsedAttributes(t, html);

        Assert.Equal(HtmlTokenKind.Character, t.Next());
        Assert.Equal("c ", t.Data.ToString());
        Assert.Equal(HtmlTokenKind.Character, t.Next());
        Assert.Equal("&", t.Data.ToString());
        Assert.Equal("&amp;", html[t.Start..t.End]);
        Assert.Equal(HtmlTokenKind.Character, t.Next());
        Assert.Equal(" d", t.Data.ToString());
        Assert.Equal(HtmlTokenKind.EndTag, t.Next());
        Assert.Equal("</p>", html[t.Start..t.End]);
        Assert.Equal(HtmlTokenKind.Comment, t.Next());
        Assert.Equal("k", t.Data.ToString());
        Assert.Equal("<!--k-->", html[t.Start..t.End]);
        Assert.Equal(HtmlTokenKind.Doctype, t.Next());
        Assert.Equal("html", t.Name.ToString());
        Assert.Equal("<!doctype html>", html[t.Start..t.End]);
        Assert.Equal(HtmlTokenKind.EndOfFile, t.Next());
        Assert.Equal(HtmlTokenKind.EndOfFile, t.Next());
        Assert.Equal(html.Length, t.Start);
    }

    private static void ParsedAttributes(HtmlTokenizer t, string html)
    {
        AttributeViewList attrs = t.Attributes;
        Assert.Equal(4, attrs.Count);

        AttributeView id = attrs[0];
        Assert.Equal("id", id.Name.ToString());
        Assert.Equal("x", id.Value.ToString());
        Assert.Equal(AttributeQuote.DoubleQuoted, id.Quote);
        Assert.Equal("id=\"x\"", html[id.Start..id.End]);
        Assert.Equal("x", html[id.ValueStart..id.ValueEnd]);

        AttributeView cls = attrs[1];
        Assert.Equal("y&z", cls.Value.ToString());
        Assert.Equal(AttributeQuote.SingleQuoted, cls.Quote);
        Assert.Equal("y&amp;z", html[cls.ValueStart..cls.ValueEnd]);
        Assert.Equal("class='y&amp;z'", html[cls.Start..cls.End]);

        AttributeView data = attrs[2];
        Assert.Equal(AttributeQuote.Unquoted, data.Quote);
        Assert.Equal("data=q", html[data.Start..data.End]);

        AttributeView hidden = attrs[3];
        Assert.False(hidden.HasValue);
        Assert.Equal(AttributeQuote.NoValue, hidden.Quote);
        Assert.Equal("hidden", html[hidden.Start..hidden.End]);
        Assert.True(attrs.TryGet("data", out AttributeView found));
        Assert.Equal("q", found.Value.ToString());
        Assert.False(attrs.TryGet("nope", out _));
    }

    [Fact]
    public void Names_are_lowercased_and_values_decoded_without_changing_the_ranges()
    {
        var t = new HtmlTokenizer("<DIV CLASS=\"A&lt;B\" Id=x>");
        Assert.Equal(HtmlTokenKind.StartTag, t.Next());
        Assert.Equal("div", t.Name.ToString());
        Assert.Equal("class", t.Attributes[0].Name.ToString());
        Assert.Equal("A<B", t.Attributes[0].Value.ToString());
        Assert.Equal("id", t.Attributes[1].Name.ToString());
        Assert.Equal(5, t.Attributes[0].NameStart);
        Assert.Equal(10, t.Attributes[0].NameEnd);
    }

    [Fact]
    public void Duplicate_attributes_are_dropped_but_kept_for_the_source_map()
    {
        const string html = "<a x=1 y=2 X=3 x=4>";
        var t = new HtmlTokenizer(html, new HtmlTokenizerOptions { RecordErrors = true });
        t.Next();
        Assert.Equal(2, t.Attributes.Count);
        Assert.Equal("1", t.Attributes[0].Value.ToString());
        Assert.Equal(2, t.DuplicateAttributes.Count);
        Assert.Equal("X=3", html[t.DuplicateAttributes[0].Start..t.DuplicateAttributes[0].End]);
        Assert.Equal(2, t.Errors!.Count(e => e.Error == HtmlParseError.DuplicateAttribute));
    }

    [Fact]
    public void The_tree_builder_switches_states_after_start_tags()
    {
        var t = new HtmlTokenizer("<title>a<b>&amp;</title><p>x</p>");
        Assert.Equal(HtmlTokenKind.StartTag, t.Next());
        t.SetState(TokenizerState.Rcdata);
        Assert.Equal(HtmlTokenKind.Character, t.Next());
        Assert.Equal("a<b>", t.Data.ToString());
        Assert.Equal(HtmlTokenKind.Character, t.Next());
        Assert.Equal("&", t.Data.ToString());
        Assert.Equal(HtmlTokenKind.EndTag, t.Next());
        Assert.Equal("title", t.Name.ToString());
        Assert.Equal(["<p>", "text:x", "</p>"], Tokens(t));

        var s = new HtmlTokenizer("<script>if (a<b) x('</scr' + 'ipt>');</SCRIPT >z");
        s.Next();
        s.SetState(TokenizerState.ScriptData);
        Assert.Equal(["text:if (a<b) x('</scr' + 'ipt>');", "</script>", "text:z"], Tokens(s));

        var style = new HtmlTokenizer("<style>a</b>&amp;</style>");
        style.Next();
        style.SetState(TokenizerState.Rawtext);
        Assert.Equal(["text:a</b>&amp;", "</style>"], Tokens(style));

        var plain = new HtmlTokenizer("<plaintext></plaintext><p>");
        plain.Next();
        plain.SetState(TokenizerState.Plaintext);
        Assert.Equal(["text:</plaintext><p>"], Tokens(plain));
    }

    [Fact]
    public void An_end_tag_is_appropriate_only_for_the_last_start_tag()
    {
        var t = new HtmlTokenizer("</b></title>");
        t.LastStartTagName = "title";
        t.SetState(TokenizerState.Rcdata);
        Assert.Equal(["text:</b>", "</title>"], Tokens(t));

        var none = new HtmlTokenizer("</title>");
        none.SetState(TokenizerState.Rcdata);
        Assert.Equal(["text:</title>"], Tokens(none));
    }

    [Fact]
    public void Cdata_sections_need_foreign_content()
    {
        var foreign = new HtmlTokenizer("<![CDATA[a<b>]]>c") { InForeignContent = true };
        Assert.Equal(["text:a<b>", "text:c"], Tokens(foreign));

        var html = new HtmlTokenizer("<![CDATA[a]]>c");
        Assert.Equal(["comment:[CDATA[a]]", "text:c"], Tokens(html));
    }

    [Fact]
    public void Comments_doctypes_and_processing_instructions_have_the_right_pieces()
    {
        var t = new HtmlTokenizer("<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01//EN\" 'http://x/y.dtd'><?php echo 1 ?><!--a<!--b--!>c-->");
        Assert.Equal(HtmlTokenKind.Doctype, t.Next());
        Assert.True(t.HasDoctypeName);
        Assert.Equal("html", t.Name.ToString());
        Assert.Equal("-//W3C//DTD HTML 4.01//EN", t.PublicId);
        Assert.Equal("http://x/y.dtd", t.SystemId);
        Assert.False(t.ForceQuirks);
        Assert.Equal(HtmlTokenKind.ProcessingInstruction, t.Next());
        Assert.Equal("php", t.Name.ToString());
        Assert.Equal("echo 1 ", t.Data.ToString());
        Assert.Equal(HtmlTokenKind.Comment, t.Next());
        // "--!>" closes a comment (13.2.5.52 comment end bang state), so the rest is text.
        Assert.Equal("a<!--b", t.Data.ToString());
        Assert.Equal(HtmlTokenKind.Character, t.Next());
        Assert.Equal("c-->", t.Data.ToString());
    }

    [Fact]
    public void Tokens_map_to_byte_ranges_through_the_source_text()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("\u00E9<b>\u4E2D</b>");
        SourceText source = SourceText.Decode(bytes, WhatwgEncoding.Utf8, EncodingConfidence.Certain);
        var t = new HtmlTokenizer(source);
        var ranges = new List<string>();
        while (t.Next() != HtmlTokenKind.EndOfFile)
        {
            ranges.Add(Encoding.UTF8.GetString(bytes, source.ToByteOffset(t.Start), source.ToByteOffset(t.End) - source.ToByteOffset(t.Start)));
        }

        Assert.Equal(["\u00E9", "<b>", "\u4E2D", "</b>"], ranges);
    }

    [Fact]
    public void Input_stream_errors_are_reported_in_offset_order_with_the_tokenizer_errors()
    {
        var t = new HtmlTokenizer("a\u0001<\u0000b\uFFFE>", new HtmlTokenizerOptions { RecordErrors = true });
        Tokens(t);
        Assert.Equal(
            [HtmlParseError.ControlCharacterInInputStream, HtmlParseError.InvalidFirstCharacterOfTagName, HtmlParseError.UnexpectedNullCharacter, HtmlParseError.NoncharacterInInputStream],
            t.Errors!.Select(e => e.Error));
        Assert.True(t.Errors!.Select(e => e.Offset).SequenceEqual(t.Errors!.Select(e => e.Offset).Order()));
    }

    [Fact]
    public void Errors_are_not_collected_unless_asked_for()
    {
        var t = new HtmlTokenizer("<a b=1 b=2>\u0000");
        Tokens(t);
        Assert.Null(t.Errors);
    }

    // ---- Limits and hostile input ----

    [Fact]
    public void Too_many_attributes_fail_before_they_are_read()
    {
        string html = "<a " + string.Join(' ', Enumerable.Range(0, 1030).Select(i => "a" + i)) + ">";
        var t = new HtmlTokenizer(html);
        HtmlLimitExceededException ex = Assert.Throws<HtmlLimitExceededException>(() => t.Next());
        Assert.Equal(nameof(ParseLimits.MaxAttributesPerTag), ex.LimitName);
        Assert.Equal(1024, ex.Limit);

        // Exactly at the limit is fine.
        string ok = "<a " + string.Join(' ', Enumerable.Range(0, 1024).Select(i => "a" + i)) + ">";
        var okTokenizer = new HtmlTokenizer(ok);
        Assert.Equal(HtmlTokenKind.StartTag, okTokenizer.Next());
        Assert.Equal(1024, okTokenizer.Attributes.Count);

        // A custom limit.
        var small = new HtmlTokenizer("<a b c d>", new HtmlTokenizerOptions { Limits = new ParseLimits { MaxAttributesPerTag = 2 } });
        Assert.Throws<HtmlLimitExceededException>(() => small.Next());
    }

    [Fact]
    public void Duplicate_detection_stays_correct_with_many_attributes()
    {
        var sb = new StringBuilder("<a");
        for (int i = 0; i < 500; i++)
        {
            sb.Append(" n").Append(i);
        }

        sb.Append(" n0 n250 n499 n500>");
        var t = new HtmlTokenizer(sb.ToString(), new HtmlTokenizerOptions { RecordErrors = true });
        t.Next();
        Assert.Equal(501, t.Attributes.Count);
        Assert.Equal(3, t.DuplicateAttributes.Count);

        // The index is reset for the next tag.
        t.Next();
        var again = new HtmlTokenizer("<a x x><b x x x>");
        again.Next();
        Assert.Equal(1, again.Attributes.Count);
        again.Next();
        Assert.Equal(1, again.Attributes.Count);
        Assert.Equal(2, again.DuplicateAttributes.Count);
    }

    [Fact]
    public void The_input_size_limit_applies_to_the_tokenizer_too()
    {
        Assert.Throws<HtmlLimitExceededException>(() => new HtmlTokenizer(new string('a', 11), new HtmlTokenizerOptions { Limits = new ParseLimits { MaxInputLength = 10 } }));
    }

    [Fact]
    public void Cancellation_is_honoured_between_tokens()
    {
        using var cts = new System.Threading.CancellationTokenSource();
        var t = new HtmlTokenizer("<a><b>");
        t.Next(cts.Token);
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => t.Next(cts.Token));
    }

    public static IEnumerable<object[]> HostileInputs()
    {
        yield return ["lt", new string('<', 200_000)];
        yield return ["lt-slash", string.Concat(Enumerable.Repeat("</", 100_000))];
        yield return ["amp", string.Concat(Enumerable.Repeat("&", 200_000))];
        yield return ["amp-hash", string.Concat(Enumerable.Repeat("&#", 100_000))];
        yield return ["amp-x", string.Concat(Enumerable.Repeat("&#x", 100_000))];
        yield return ["amp-names", string.Concat(Enumerable.Repeat("&notin;&nbsp&amp=&zzz;", 20_000))];
        yield return ["amp-long-name", "&" + new string('a', 300_000) + ";"];
        yield return ["comments", string.Concat(Enumerable.Repeat("<!--", 100_000))];
        yield return ["comment-dashes", "<!--" + string.Concat(Enumerable.Repeat("--!", 100_000))];
        yield return ["comment-lt", "<!--" + string.Concat(Enumerable.Repeat("<!-", 100_000))];
        yield return ["tag-open", "<a " + new string('x', 300_000)];
        yield return ["attr-quote", "<a b=\"" + new string('&', 100_000)];
        yield return ["attr-many-eq", "<a " + string.Concat(Enumerable.Repeat("=", 100_000)) + ">"];
        yield return ["nul", new string('\0', 200_000)];
        yield return ["doctype", "<!DOCTYPE " + string.Concat(Enumerable.Repeat("a ", 100_000))];
        yield return ["pi", "<?a " + string.Concat(Enumerable.Repeat("?", 100_000))];
        yield return ["script-escape", string.Concat(Enumerable.Repeat("<!--<script ", 50_000))];
        yield return ["cdata", "<![CDATA[" + string.Concat(Enumerable.Repeat("]", 100_000))];
        yield return ["many-tags", string.Concat(Enumerable.Repeat("<a b c d=e>", 50_000))];
        yield return ["deep", string.Concat(Enumerable.Repeat("<div>", 100_000)) + string.Concat(Enumerable.Repeat("</div>", 100_000))];
    }

    [Theory]
    [MemberData(nameof(HostileInputs))]
    public void Hostile_input_is_tokenized_in_linear_time_in_every_state(string name, string html)
    {
        foreach (TokenizerState state in Enum.GetValues<TokenizerState>())
        {
            var sw = Stopwatch.StartNew();
            var t = new HtmlTokenizer(html, new HtmlTokenizerOptions { RecordErrors = true });
            t.LastStartTagName = "script";
            t.SetState(state);
            int tokens = 0;
            while (t.Next() != HtmlTokenKind.EndOfFile)
            {
                tokens++;
                Assert.True(t.End >= t.Start && t.End <= html.Length, name);
            }

            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"{name} in {state} took {sw.Elapsed}");
            Assert.True(tokens >= 0);
        }
    }
}
