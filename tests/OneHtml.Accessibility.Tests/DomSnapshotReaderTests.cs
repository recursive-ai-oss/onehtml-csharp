using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

public sealed class DomSnapshotReaderTests
{
    private static byte[] Valid()
    {
        Document doc = Document.Parse("<!DOCTYPE html><div id=a><p>hello</p><input value=x></div>");
        FakeNode root = FakeSnapshot.FromDocument(doc, n => n.Kind == NodeKind.Element ? new FakeLayout { W = 5, H = 5, Styles = ["a", "b", "c"] } : null);
        return FakeSnapshot.ToJson(root);
    }

    private static byte[] Wrap(string inner) => Encoding.UTF8.GetBytes(inner);

    private static DomSnapshotFormatException Fails(string json, DomSnapshotLimits? limits = null) =>
        Assert.Throws<DomSnapshotFormatException>(() => DomSnapshot.Parse(Wrap(json), null, limits));

    private static string Doc(string nodes, string layout = "{}") => "{\"documents\":[{\"nodes\":" + nodes + ",\"layout\":" + layout + "}],\"strings\":[\"a\",\"b\"]}";

    [Fact]
    public void The_capture_file_wrapper_carries_style_names_and_the_format()
    {
        Document doc = Document.Parse("<p>x</p>");
        byte[] json = FakeSnapshot.ToJson(withFormat: true, FakeSnapshot.FromDocument(doc));
        DomSnapshot s = DomSnapshot.Parse(json);
        Assert.Equal(FakeSnapshot.StyleNames, s.ComputedStyleNames);
        Assert.Equal("HeadlessChrome/0.0.0.0", s.Product);
        Assert.Equal("1.3", s.ProtocolVersion);
        Assert.Single(s.Documents);
        Assert.Equal("#document", s.Documents[0].NodeName(0));
        Assert.Equal(9, s.Documents[0].NodeType(0));
        Assert.Equal(-1, s.Documents[0].ParentIndex(0));
        Assert.Equal("http://test.invalid/", s.Documents[0].DocumentUrl);
    }

    [Fact]
    public void A_protocol_response_wrapping_the_result_is_read()
    {
        string inner = Encoding.UTF8.GetString(Valid());
        DomSnapshot s = DomSnapshot.Parse(Wrap("{\"id\":7,\"result\":" + inner + "}"), FakeSnapshot.StyleNames);
        Assert.True(s.Documents[0].NodeCount > 5);
        Assert.Equal(3, s.ComputedStyleNames.Count);
    }

    [Fact]
    public void Unknown_members_are_ignored()
    {
        string inner = Encoding.UTF8.GetString(Valid());
        string json = "{\"extra\":{\"deep\":[1,[2,[3]]]}," + inner.TrimStart('{').Insert(0, "") ;
        _ = DomSnapshot.Parse(Wrap(json));
    }

    [Fact]
    public void A_different_protocol_domain_method_or_major_version_is_refused()
    {
        string bare = Encoding.UTF8.GetString(Valid());
        string body = bare.TrimStart('{');
        Fails("{\"format\":{\"domain\":\"Accessibility\"}," + body);
        Fails("{\"format\":{\"method\":\"getSnapshot\"}," + body);
        Fails("{\"format\":{\"protocolVersion\":\"2.0\"}," + body);
        _ = DomSnapshot.Parse(Wrap("{\"format\":{\"protocolVersion\":\"1.4\"}," + body));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"documents\":[]}")]
    [InlineData("{\"strings\":[]}")]
    [InlineData("{\"documents\":{},\"strings\":[]}")]
    [InlineData("{\"documents\":[1],\"strings\":[]}")]
    [InlineData("{\"documents\":[{}],\"strings\":[1]}")]
    [InlineData("{\"documents\":[{\"nodes\":{\"nodeType\":[\"x\"]}}],\"strings\":[]}")]
    [InlineData("{\"documents\":[{\"nodes\":{\"nodeType\":[1.5]}}],\"strings\":[]}")]
    [InlineData("{\"documents\":[{\"nodes\":{\"nodeType\":[1]}}],\"strings\":[]}")]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"documents\":[{\"nodes\":{\"nodeType\":[1,")]
    [InlineData("nul")]
    [InlineData("{} {}")]
    public void Malformed_inputs_fail_with_the_typed_exception(string json)
    {
        Fails(json);
    }

    [Fact]
    public void Inconsistent_tables_are_refused()
    {
        // parent after child
        Fails(Doc("{\"nodeType\":[9,1],\"parentIndex\":[-1,1],\"nodeName\":[0,0]}"));
        // two roots
        Fails(Doc("{\"nodeType\":[9,1],\"parentIndex\":[-1,-1],\"nodeName\":[0,0]}"));
        // lengths differ
        Fails(Doc("{\"nodeType\":[9,1],\"parentIndex\":[-1],\"nodeName\":[0,0]}"));
        // string index out of range
        Fails(Doc("{\"nodeType\":[9],\"parentIndex\":[-1],\"nodeName\":[5]}"));
        // layout item names a node that is not there
        Fails(Doc("{\"nodeType\":[9],\"parentIndex\":[-1],\"nodeName\":[0]}", "{\"nodeIndex\":[3]}"));
        // odd attribute list
        Fails(Doc("{\"nodeType\":[9],\"parentIndex\":[-1],\"nodeName\":[0],\"attributes\":[[0]]}"));
        // rare data index out of range
        Fails(Doc("{\"nodeType\":[9],\"parentIndex\":[-1],\"nodeName\":[0],\"pseudoType\":{\"index\":[4],\"value\":[0]}}"));
        // frame points to a document that is not there
        Fails(Doc("{\"nodeType\":[9],\"parentIndex\":[-1],\"nodeName\":[0],\"contentDocumentIndex\":{\"index\":[0],\"value\":[5]}}"));
        // rectangle with three numbers
        Fails(Doc("{\"nodeType\":[9],\"parentIndex\":[-1],\"nodeName\":[0]}", "{\"nodeIndex\":[0],\"bounds\":[[1,2,3]]}"));
        // text box naming a layout item that is not there
        Fails("{\"documents\":[{\"nodes\":{\"nodeType\":[9],\"parentIndex\":[-1],\"nodeName\":[0]},\"textBoxes\":{\"layoutIndex\":[4],\"bounds\":[[0,0,1,1]],\"start\":[0],\"length\":[1]}}],\"strings\":[\"a\"]}");
        // style count does not match the requested list
        Assert.Throws<DomSnapshotFormatException>(() => DomSnapshot.Parse(
            Wrap(Doc("{\"nodeType\":[9],\"parentIndex\":[-1],\"nodeName\":[0]}", "{\"nodeIndex\":[0],\"styles\":[[0,1]]}")),
            ["a", "b", "c"]));
    }

    [Fact]
    public void Empty_rectangles_and_nulls_are_read_as_absent()
    {
        DomSnapshot s = DomSnapshot.Parse(Wrap(Doc(
            "{\"nodeType\":[9,1],\"parentIndex\":[-1,0],\"nodeName\":[0,1]}",
            "{\"nodeIndex\":[0,1],\"bounds\":[[0,0,10,10],[1,2,3,4]],\"offsetRects\":[[],[1,2,3,4]],\"blendedBackgroundColors\":[null,[1]]}")));
        Assert.Equal(2, s.Documents[0].LayoutCount);
    }

    // ---- limits ----

    [Fact]
    public void Limits_fail_before_allocating()
    {
        byte[] valid = Valid();
        DomSnapshotFormatException e = Assert.Throws<DomSnapshotFormatException>(() => DomSnapshot.Parse(valid, null, new DomSnapshotLimits { MaxInputBytes = 100 }));
        Assert.Equal("MaxInputBytes", e.LimitName);
        Assert.Equal("MaxNodes", Assert.Throws<DomSnapshotFormatException>(() => DomSnapshot.Parse(valid, null, new DomSnapshotLimits { MaxNodes = 3 })).LimitName);
        Assert.Equal("MaxStrings", Assert.Throws<DomSnapshotFormatException>(() => DomSnapshot.Parse(valid, null, new DomSnapshotLimits { MaxStrings = 3 })).LimitName);
        Assert.Equal("MaxLayoutNodes", Assert.Throws<DomSnapshotFormatException>(() => DomSnapshot.Parse(valid, null, new DomSnapshotLimits { MaxLayoutNodes = 1 })).LimitName);
        Assert.Equal("MaxDocuments", Assert.Throws<DomSnapshotFormatException>(() => DomSnapshot.Parse(Wrap("{\"documents\":[{},{},{}],\"strings\":[]}"), null, new DomSnapshotLimits { MaxDocuments = 2 })).LimitName);
        Assert.Equal("MaxStringChars", Assert.Throws<DomSnapshotFormatException>(() => DomSnapshot.Parse(valid, null, new DomSnapshotLimits { MaxStringChars = 5 })).LimitName);
        Assert.Equal("MaxComputedStyleNames", Assert.Throws<DomSnapshotFormatException>(() => DomSnapshot.Parse(valid, ["a", "b", "c"], new DomSnapshotLimits { MaxComputedStyleNames = 2 })).LimitName);
    }

    [Fact]
    public void A_deep_dom_is_bounded_without_recursion()
    {
        int n = 200_000;
        var sb = new StringBuilder("{\"documents\":[{\"nodes\":{\"nodeType\":[9");
        for (int i = 1; i < n; i++)
        {
            sb.Append(",1");
        }

        sb.Append("],\"parentIndex\":[-1");
        for (int i = 1; i < n; i++)
        {
            sb.Append(',').Append(i - 1);
        }

        sb.Append("],\"nodeName\":[0");
        for (int i = 1; i < n; i++)
        {
            sb.Append(",0");
        }

        sb.Append("]}}],\"strings\":[\"a\"]}");
        var sw = Stopwatch.StartNew();
        DomSnapshotFormatException e = Fails(sb.ToString());
        Assert.Equal("MaxNodeDepth", e.LimitName);

        // And a deep DOM within the limit maps without recursion: the document is shallower than the snapshot allows.
        Assert.True(sw.ElapsedMilliseconds < 5000);
    }

    [Fact]
    public void Deeply_nested_json_fails_fast()
    {
        var sw = Stopwatch.StartNew();
        string deepArrays = "{\"x\":" + new string('[', 500_000) + new string(']', 500_000) + "}";
        Fails(deepArrays);
        Fails("{\"documents\":[{\"nodes\":{\"nodeType\":" + new string('[', 200_000));
        Fails("{\"documents\":[{\"unknown\":" + new string('{', 100_000).Replace("{", "{\"a\":", StringComparison.Ordinal));
        Assert.True(sw.ElapsedMilliseconds < 5000, sw.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void A_huge_array_is_stopped_at_the_limit_not_after_it()
    {
        var sb = new StringBuilder("{\"documents\":[{\"nodes\":{\"nodeType\":[");
        for (int i = 0; i < 300_000; i++)
        {
            sb.Append("1,");
        }

        sb.Append("1]}}],\"strings\":[]}");
        var sw = Stopwatch.StartNew();
        DomSnapshotFormatException e = Fails(sb.ToString(), new DomSnapshotLimits { MaxNodes = 1000 });
        Assert.Equal("MaxNodes", e.LimitName);
        Assert.Equal(1000, e.Limit);
        Assert.True(sw.ElapsedMilliseconds < 2000);
    }

    // ---- hostile mutation ----

    [Fact]
    public void Random_byte_mutations_never_escape_the_typed_exception_or_hang()
    {
        byte[] valid = Valid();
        Document doc = Document.Parse("<!DOCTYPE html><div id=a><p>hello</p><input value=x></div>");
        var rnd = new Random(20261025);
        var sw = Stopwatch.StartNew();
        int parsed = 0, refused = 0, mapped = 0;
        var limits = new DomSnapshotLimits { MaxNodes = 10_000, MaxStrings = 10_000, MaxLayoutNodes = 10_000, MaxTextBoxes = 10_000, MaxInputBytes = 1 << 20 };
        for (int round = 0; round < 4000; round++)
        {
            byte[] m = (byte[])valid.Clone();
            int edits = 1 + rnd.Next(4);
            for (int k = 0; k < edits; k++)
            {
                switch (rnd.Next(4))
                {
                    case 0:
                        m[rnd.Next(m.Length)] = (byte)rnd.Next(256);
                        break;
                    case 1:
                        m[rnd.Next(m.Length)] = (byte)"0123456789-[]{},\""[rnd.Next(17)];
                        break;
                    case 2:
                        {
                            int a = rnd.Next(m.Length), b = rnd.Next(m.Length);
                            (m[a], m[b]) = (m[b], m[a]);
                            break;
                        }

                    default:
                        m = m.Take(rnd.Next(m.Length)).ToArray();
                        if (m.Length == 0)
                        {
                            m = [0];
                        }

                        break;
                }
            }

            try
            {
                DomSnapshot s = DomSnapshot.Parse(m, FakeSnapshot.StyleNames, limits);
                parsed++;
                DomSnapshotMapResult r = DomSnapshotAdapter.Map(s, doc);
                if (r.Succeeded)
                {
                    mapped++;
                    Assert.True(r.Accounting!.IsComplete);
                }
                else
                {
                    refused++;
                }
            }
            catch (DomSnapshotFormatException)
            {
            }
        }

        Assert.True(parsed > 0 && refused >= 0 && mapped >= 0);
        Assert.True(sw.ElapsedMilliseconds < 60_000, sw.ElapsedMilliseconds + " ms");
    }
}
