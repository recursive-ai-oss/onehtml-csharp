using System.Collections.Generic;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Differential;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>The matcher and the normalizations of the axe-core differential, on small pages and hand-made results.</summary>
public sealed class AxeDifferentialTests
{
    private const string Page = "<!DOCTYPE html><html><body><main id=\"m\"><p>a</p><div id=\"d\"><p>b</p><p id=\"x\">c</p></div><svg><circle/></svg></main><div><p>z</p></div></body></html>";

    private static Document Doc() => Document.Parse(Page);

    private static AxeNodeRef Ref(string? selector, string? xpath, bool nested = false) => new("r", AxeClass.Violation, selector, xpath, string.Empty, nested, string.Empty);

    [Theory]
    [InlineData("/html/body/main/div/p[2]", "x")]
    [InlineData("//main[@id='m']/div/p", "")]
    [InlineData("//div[@id='d']/p[2]", "x")]
    [InlineData("/html/body/div/p", "")]
    public void The_xpath_subset_resolves_to_the_element(string xpath, string expectedId)
    {
        Document doc = Doc();
        Node n = AxeTargets.ByXPath(doc, xpath);
        Assert.False(n.IsNull);
        Assert.Equal(expectedId, n.GetAttribute("id") ?? string.Empty);
    }

    [Theory]
    [InlineData("/html/body/main/div/p[3]")]
    [InlineData("//main[@id='nope']/p")]
    [InlineData("/html/body//p")]
    [InlineData("p")]
    public void A_path_outside_the_subset_or_missing_resolves_to_nothing(string xpath) => Assert.True(AxeTargets.ByXPath(Doc(), xpath).IsNull);

    [Fact]
    public void The_xpath_wins_over_the_selector_and_a_bad_selector_falls_back_on_the_xpath()
    {
        Document doc = Doc();
        Node agree = AxeTargets.Resolve(doc, Ref("#x", "//div[@id='d']/p[2]"), out string how);
        Assert.Equal("xpath+selector", how);
        Assert.Equal("x", agree.GetAttribute("id"));

        AxeTargets.Resolve(doc, Ref("#m", "//div[@id='d']/p[2]"), out how);
        Assert.Equal("xpath!selector", how);

        Node bad = AxeTargets.Resolve(doc, Ref("p[¿", "//div[@id='d']/p[2]"), out how);
        Assert.Equal("xpath(selector-error)", how);
        Assert.False(bad.IsNull);

        Node sel = AxeTargets.Resolve(doc, Ref("#x", null), out how);
        Assert.Equal("selector", how);
        Assert.False(sel.IsNull);

        Assert.True(AxeTargets.Resolve(doc, Ref("#x", "/html/body/main/div/p[2]", nested: true), out how).IsNull);
        Assert.Equal("nested", how);
        Assert.True(AxeTargets.Resolve(doc, Ref("#nothing", null), out how).IsNull);
        Assert.Equal("unresolved", how);
    }

    [Fact]
    public void The_reader_takes_nodes_groups_nesting_and_the_rules_that_ran()
    {
        const string json = """
            {"schema":"s","axe_version":"4.14.0","options":{},"results":{
             "violations":[{"id":"a","nodes":[{"target":["#x"],"xpath":["//p[@id='x']"],"html":"<p id=x>","all":[{"id":"c","message":"bad"}],"any":[],"none":[]}]}],
             "passes":[{"id":"b","nodes":[{"target":[["iframe","#y"]],"html":"","any":[],"all":[],"none":[]}]}],
             "incomplete":[],"inapplicable":[{"id":"z","nodes":[]}]}}
            """;
        AxeResultSet set = AxeReader.Read(Encoding.UTF8.GetBytes(json));
        Assert.Equal("4.14.0", set.Version);
        Assert.Equal(["a", "b", "z"], set.RulesRan.Order().ToArray());
        Assert.Equal(2, set.Nodes.Count);
        Assert.Equal(AxeClass.Violation, set.Nodes[0].Class);
        Assert.Equal("all: c: bad", set.Nodes[0].Message);
        Assert.False(set.Nodes[0].Nested);
        Assert.True(set.Nodes[1].Nested);
        Assert.Equal(AxeClass.Pass, set.Nodes[1].Class);
    }

    private static OurNode Our(string detail) => new(detail switch { "fail" => AxeClass.Violation, "pass" => AxeClass.Pass, _ => AxeClass.Incomplete }, detail, "m");

    private static TheirNode Their(int index, AxeClass c, string how = "xpath") => new(index, c, "sel" + index, string.Empty, how, string.Empty);

    private static RuleTally Compare(Dictionary<int, OurNode>? ours, TheirNode[] theirs, string rule = "r", System.Func<int, int>? parent = null) =>
        AxeCompare.CompareRule(ours, theirs, "page", i => "n" + i, parent, rule);

    [Fact]
    public void Agreement_is_by_group_and_review_and_render_both_match_incomplete()
    {
        RuleTally t = Compare(
            new() { [1] = Our("fail"), [2] = Our("pass"), [3] = Our("review"), [4] = Our("render"), [5] = Our("fail") },
            [Their(1, AxeClass.Violation), Their(2, AxeClass.Pass), Their(3, AxeClass.Incomplete), Their(4, AxeClass.Incomplete), Their(5, AxeClass.Pass)]);
        Assert.Equal(4, t.Agree);
        Assert.Equal(1, t.AgreeReview);
        Assert.Equal(1, t.AgreeRender);
        Assert.Equal(1, t.Classes["fail|pass"]);
        Assert.Equal(5, t.NodesAxe);
        Assert.Equal(1, t.PagesBoth);
    }

    [Fact]
    public void Unmatched_nodes_become_ours_only_or_axe_only_and_an_unimplemented_rule_counts_axe_alone()
    {
        RuleTally t = Compare(new() { [1] = Our("fail") }, [Their(2, AxeClass.Violation), Their(-1, AxeClass.Violation, "unresolved")]);
        Assert.Equal(1, t.Classes["fail|none"]);
        Assert.Equal(1, t.Classes["none|violation"]);
        Assert.Equal(1, t.Unresolved);

        RuleTally none = Compare(null, [Their(2, AxeClass.Violation)]);
        Assert.Equal(1, none.NodesAxe);
        Assert.Equal(0, none.PagesOurs);
        Assert.Empty(none.Classes);
    }

    [Fact]
    public void Several_results_on_one_element_keep_the_most_severe()
    {
        var map = new Dictionary<int, OurNode>();
        AxeCompare.Add(map, 1, Our("pass"));
        AxeCompare.Add(map, 1, Our("review"));
        AxeCompare.Add(map, 1, Our("fail"));
        AxeCompare.Add(map, 1, Our("pass"));
        Assert.Equal("fail", map[1].Detail);
        Assert.Single(map);
    }

    [Fact]
    public void A_group_axe_reports_on_a_container_matches_our_findings_on_its_contents()
    {
        // Elements 10 and 11 are cells in table 5; axe reports the table.
        System.Func<int, int> parent = i => i switch { 10 or 11 => 5, 5 => 1, _ => -1 };
        RuleTally t = Compare(new() { [10] = Our("fail"), [11] = Our("fail") }, [Their(5, AxeClass.Violation)], parent: parent);
        Assert.Equal(2, t.Contained);
        Assert.Empty(t.Classes);

        // A different group on the container is not the same finding.
        RuleTally other = Compare(new() { [10] = Our("fail") }, [Their(5, AxeClass.Incomplete)], parent: parent);
        Assert.Equal(0, other.Contained);
        Assert.Equal(2, other.Classes.Count);
    }

    [Fact]
    public void A_group_rule_reported_once_by_axe_collapses_our_extra_members_but_only_when_axe_reports_something()
    {
        RuleTally t = Compare(new() { [1] = Our("fail"), [2] = Our("fail"), [3] = Our("fail") }, [Their(1, AxeClass.Violation)], "duplicate-id");
        Assert.Equal(1, t.Agree);
        Assert.Equal(2, t.Collapsed);
        Assert.Empty(t.Classes);

        RuleTally lone = Compare(new() { [2] = Our("fail") }, [], "duplicate-id");
        Assert.Equal(1, lone.Classes["fail|none"]);

        RuleTally ordinary = Compare(new() { [1] = Our("fail"), [2] = Our("fail") }, [Their(1, AxeClass.Violation)], "link-name");
        Assert.Equal(1, ordinary.Classes["fail|none"]);
    }

    [Fact]
    public void Tallies_merge_by_addition_and_survive_json()
    {
        RuleTally a = Compare(new() { [1] = Our("fail") }, [Their(2, AxeClass.Violation)]);
        RuleTally b = Compare(new() { [1] = Our("fail") }, [Their(1, AxeClass.Violation)]);
        var sum = new RuleTally();
        sum.Merge(RuleTally.FromJson(a.ToJson()), 5);
        sum.Merge(RuleTally.FromJson(b.ToJson()), 5);
        Assert.Equal(1, sum.Agree);
        Assert.Equal(1, sum.Classes["fail|none"]);
        Assert.Equal(2, sum.PagesBoth);
        Assert.Equal("sel2", sum.Examples["none|violation"][0].Where);
    }

    [Fact]
    public void Region_is_compared_once_per_page_by_its_strongest_outcome()
    {
        // axe reports the wrappers it reaches; we report the topmost element. Different nodes, same page verdict.
        RuleTally same = Compare(new() { [1] = Our("fail") }, [Their(7, AxeClass.Violation), Their(8, AxeClass.Violation), Their(9, AxeClass.Pass)], "region");
        Assert.Equal(1, same.Agree);
        Assert.Empty(same.Classes);
        RuleTally differ = Compare(new() { [1] = Our("fail") }, [Their(7, AxeClass.Pass)], "region");
        Assert.Equal(0, differ.Agree);
        Assert.Equal(1, differ.Classes["fail|none"]);
        RuleTally clean = Compare(new() { [1] = Our("pass") }, [Their(7, AxeClass.Pass)], "region");
        Assert.Equal(0, clean.Agree);
        Assert.Empty(clean.Classes);
    }

    [Fact]
    public void An_enhanced_contrast_failure_where_axe_reports_the_stricter_failure_is_collapsed()
    {
        var subsumed = new HashSet<int> { 1, 2 };
        RuleTally t = AxeCompare.CompareRule(
            new Dictionary<int, OurNode> { [1] = Our("fail"), [2] = Our("fail"), [3] = Our("fail") },
            [Their(1, AxeClass.Pass), Their(3, AxeClass.Pass)], "page", i => "n" + i, null, "color-contrast-enhanced", subsumed);
        Assert.Equal(2, t.Collapsed);
        Assert.Equal(1, t.Classes["fail|pass"]);
    }
}
