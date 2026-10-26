using System;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneHtml.Differential;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>The matcher, comparer and normalizer of the Chrome accessibility-tree differential, on small trees written here.</summary>
public sealed class AxDifferentialTests
{
    private static AxSide Read(string nodesJson, Func<long, int>? map = null)
    {
        using JsonDocument d = JsonDocument.Parse(nodesJson);
        return AxReader.Read(d.RootElement, map ?? (b => (int)b), null);
    }

    private const string ChromeTree = """
        [
          {"nodeId":"1","ignored":false,"role":{"type":"internalRole","value":"RootWebArea"},"name":{"type":"computedString","value":"Page"},"properties":[{"name":"focusable","value":{"type":"booleanOrUndefined","value":true}},{"name":"focused","value":{"type":"booleanOrUndefined","value":true}}],"childIds":["2"],"backendDOMNodeId":1},
          {"nodeId":"2","ignored":true,"ignoredReasons":[{"name":"uninteresting","value":{"type":"boolean","value":true}}],"role":{"type":"role","value":"none"},"parentId":"1","childIds":["3","5"],"backendDOMNodeId":2},
          {"nodeId":"3","ignored":false,"role":{"type":"role","value":"button"},"name":{"type":"computedString","value":"  Go\n  now ","sources":[{"type":"relatedElement","attribute":"aria-labelledby"},{"type":"attribute","attribute":"aria-label","superseded":true},{"type":"contents","value":{"type":"computedString","value":"Go now"}}]},"properties":[{"name":"focusable","value":{"type":"booleanOrUndefined","value":true}},{"name":"required","value":{"type":"boolean","value":false}}],"parentId":"2","childIds":["4","-1000000001"],"backendDOMNodeId":3},
          {"nodeId":"4","ignored":false,"role":{"type":"internalRole","value":"StaticText"},"name":{"type":"computedString","value":"Go now"},"parentId":"3","childIds":[],"backendDOMNodeId":4},
          {"nodeId":"-1000000001","ignored":false,"role":{"type":"internalRole","value":"InlineTextBox"},"name":{"type":"computedString","value":"Go now"},"parentId":"4","childIds":[]},
          {"nodeId":"5","ignored":false,"role":{"type":"internalRole","value":"StaticText"},"name":{"type":"computedString","value":"1"},"parentId":"2","childIds":[]}
        ]
        """;

    [Fact]
    public void The_reader_skips_text_boxes_and_normalizes_names_and_flags()
    {
        AxSide chrome = Read(ChromeTree);
        Assert.Equal(5, chrome.Entries.Count);
        AxEntry button = chrome.Entries[2];
        Assert.Equal("Go now", button.Name);
        Assert.Equal("contents", button.NameSource);
        Assert.False(button.Props.ContainsKey("required"));
        Assert.Equal("true", button.Props["focusable"]);
        Assert.Equal([2, 4], chrome.Entries[1].Children);
        Assert.Equal([3], chrome.Entries[2].Children);
        Assert.Empty(chrome.Entries[3].Children);
    }

    [Theory]
    [InlineData("  a \t b\n", "a b")]
    [InlineData("a\u00A0b", "a\u00A0b")]
    [InlineData("", "")]
    [InlineData(" ", "")]
    public void Text_collapses_only_ascii_white_space(string input, string expected) => Assert.Equal(expected, AxNormalizer.Text(input));

    [Fact]
    public void Range_values_compare_as_single_precision_but_other_text_does_not()
    {
        Assert.Equal(AxNormalizer.RangeValue("0.6"), AxNormalizer.RangeValue("0.6000000238418579"));
        Assert.NotEqual(AxNormalizer.RangeValue("0.6"), AxNormalizer.RangeValue("0.7"));
        Assert.Equal("abc", AxNormalizer.RangeValue("abc"));
        AxSide textbox = Read("""[{"nodeId":"1","ignored":false,"role":{"type":"role","value":"textbox"},"value":{"type":"string","value":"16777217"},"childIds":[],"backendDOMNodeId":1}]""");
        Assert.Equal("16777217", textbox.Entries[0].Value);
        AxSide meter = Read("""[{"nodeId":"1","ignored":false,"role":{"type":"role","value":"meter"},"value":{"type":"string","value":"0.6000000238418579"},"childIds":[],"backendDOMNodeId":1}]""");
        Assert.Equal("0.6", meter.Entries[0].Value);
    }

    [Fact]
    public void False_flags_equal_absent_ones_but_false_expanded_does_not()
    {
        AxSide s = Read("""
            [{"nodeId":"1","ignored":false,"role":{"type":"role","value":"combobox"},"properties":[{"name":"invalid","value":{"type":"token","value":"false"}},{"name":"readonly","value":{"type":"boolean","value":false}},{"name":"expanded","value":{"type":"booleanOrUndefined","value":false}},{"name":"valuetext","value":{"type":"string","value":""}}],"childIds":[],"backendDOMNodeId":1}]
            """);
        Assert.Equal(["expanded"], s.Entries[0].Props.Keys.ToArray());
        Assert.Equal("false", s.Entries[0].Props["expanded"]);
    }

    [Fact]
    public void Matching_pairs_the_root_nodes_with_the_same_dom_node_and_anonymous_text_by_content()
    {
        // Chrome: root(1) > ignored(2) > [button(3) > text(4), text(5, no DOM node: generated)]. Ours: root > ignored > button > text, plus a text with the same name as Chrome's anonymous one.
        AxSide chrome = Read(ChromeTree);
        AxSide ours = Read("""
            [
              {"nodeId":"0","ignored":false,"role":{"type":"internalRole","value":"RootWebArea"},"name":{"type":"computedString","value":"Page"},"childIds":["1"]},
              {"nodeId":"1","ignored":true,"ignoredReasons":[{"name":"uninteresting","value":{"type":"boolean","value":true}}],"role":{"type":"role","value":"none"},"parentId":"0","childIds":["2","4"],"backendDOMNodeId":2},
              {"nodeId":"2","ignored":false,"role":{"type":"role","value":"button"},"name":{"type":"computedString","value":"Go now"},"parentId":"1","childIds":["3"],"backendDOMNodeId":3},
              {"nodeId":"3","ignored":false,"role":{"type":"internalRole","value":"StaticText"},"name":{"type":"computedString","value":"Go now"},"parentId":"2","childIds":[],"backendDOMNodeId":4},
              {"nodeId":"4","ignored":false,"role":{"type":"internalRole","value":"StaticText"},"name":{"type":"computedString","value":"1"},"parentId":"1","childIds":[],"backendDOMNodeId":9}
            ]
            """);
        // Chrome's last text has no DOM node (generated content); ours has a text node the snapshot knows nothing of. They pair only by
        // the anonymous rule: same kind, role and name under paired parents.
        (int[] c2o, int[] o2c, int anonymous) = AxComparer.Match(chrome, ours);
        Assert.Equal(0, c2o[0]);
        Assert.Equal(1, c2o[1]);
        Assert.Equal(2, c2o[2]);
        Assert.Equal(3, c2o[3]);
        Assert.Equal(4, c2o[4]);
        Assert.Equal(1, anonymous);
        Assert.All(o2c, i => Assert.True(i >= 0));
    }

    [Fact]
    public void Comparing_identical_trees_finds_nothing_and_a_changed_role_name_and_parent_are_each_reported_once()
    {
        AxSide chrome = Read(ChromeTree);
        AxPageDiff diff = AxComparer.Compare(chrome, Read(ChromeTree));
        Assert.Empty(diff.Mismatches);
        Assert.Equal(5, diff.Matched);

        string changed = ChromeTree.Replace("\"value\":\"button\"", "\"value\":\"link\"").Replace("\"value\":\"  Go\\n  now \"", "\"value\":\"Go\"");
        AxPageDiff d2 = AxComparer.Compare(chrome, Read(changed));
        Assert.Single(d2.Mismatches, m => m.Field == "role" && m.Pattern == "button -> link");
        Assert.Single(d2.Mismatches, m => m.Field == "name" && m.Pattern == "role:button ours-prefix");
        Assert.Equal(2, d2.Mismatches.Count);
    }

    [Fact]
    public void A_node_on_one_side_only_is_counted_by_whether_it_is_exposed()
    {
        AxSide chrome = Read(ChromeTree);
        AxSide ours = Read("""[{"nodeId":"0","ignored":false,"role":{"type":"internalRole","value":"RootWebArea"},"name":{"type":"computedString","value":"Page"},"properties":[{"name":"focusable","value":{"type":"booleanOrUndefined","value":true}}],"childIds":[]}]""");
        AxPageDiff diff = AxComparer.Compare(chrome, ours);
        Assert.Equal(1, diff.Matched);
        Assert.Equal(3, diff.Mismatches.Count(m => m.Field == "chromeOnly"));
        Assert.Equal(1, diff.Mismatches.Count(m => m.Field == "chromeOnlyIgnored"));
        Assert.DoesNotContain(diff.Mismatches, m => m.Field == "prop.focused");
        Assert.Equal(4, diff.Compared["chromeOnly"]);
    }

    [Fact]
    public void Ignored_nodes_are_compared_by_their_flag_and_reasons_only()
    {
        AxSide chrome = Read(ChromeTree);
        AxSide ours = Read(ChromeTree.Replace("\"ignored\":true,\"ignoredReasons\":[{\"name\":\"uninteresting\"", "\"ignored\":true,\"ignoredReasons\":[{\"name\":\"presentationalRole\""));
        AxPageDiff diff = AxComparer.Compare(chrome, ours);
        Assert.Single(diff.Mismatches);
        Assert.Equal("ignoredReasons", diff.Mismatches[0].Field);
    }

    [Fact]
    public void Text_kinds_are_named()
    {
        Assert.Equal("chrome-empty", AxComparer.TextKind(string.Empty, "x"));
        Assert.Equal("ours-empty", AxComparer.TextKind("x", string.Empty));
        Assert.Equal("ours-prefix", AxComparer.TextKind("abc", "ab"));
        Assert.Equal("chrome-prefix", AxComparer.TextKind("ab", "abc"));
        Assert.Equal("case-only", AxComparer.TextKind("AB", "ab"));
        Assert.Equal("differs", AxComparer.TextKind("a", "b"));
    }
}
