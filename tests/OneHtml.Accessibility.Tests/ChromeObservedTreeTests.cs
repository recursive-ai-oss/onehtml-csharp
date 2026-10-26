using System;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>
/// Tree behaviours that follow what Chrome's accessibility tree showed for the fixture pages (see the profile entries of the same
/// names in <c>docs/accessibility-chromium-profile.md</c>). The inputs are written here, not copied from a capture.
/// </summary>
public sealed class ChromeObservedTreeTests
{
    private static (Document Doc, AccessibilityTree Tree) Build(string html, AccessibilityOptions? options = null)
    {
        Document doc = Document.Parse(html);
        return (doc, AccessibilityTree.Build(doc, DocumentStyles.Create(doc), null, options));
    }

    private static AxNode Find(Document doc, AccessibilityTree tree, string selector) =>
        tree.Find(doc.Root.QuerySelector(selector)) ?? throw new InvalidOperationException("not in the tree: " + selector);

    private static JsonElement ExportNode(JsonDocument export, AxNode node) => export.RootElement.GetProperty("nodes")[node.Id];

    private static string? Prop(JsonElement exported, string name)
    {
        foreach (JsonElement p in exported.GetProperty("properties").EnumerateArray())
        {
            if (p.GetProperty("name").GetString() == name)
            {
                JsonElement v = p.GetProperty("value").GetProperty("value");
                return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
            }
        }

        return null;
    }

    [Fact]
    public void Generic_containers_are_exposed_but_the_document_wrappers_and_bold_italic_are_not()
    {
        (Document doc, AccessibilityTree tree) = Build("<div id=d><span id=s>x</span><b id=b>y</b><i id=i>z</i></div>");
        Assert.False(Find(doc, tree, "#d").IsIgnored);
        Assert.Equal("generic", Find(doc, tree, "#d").Role);
        Assert.False(Find(doc, tree, "#s").IsIgnored);
        Assert.True(Find(doc, tree, "#b").IsIgnored);
        Assert.True(Find(doc, tree, "#i").IsIgnored);
        Assert.True(Find(doc, tree, "body").IsIgnored);
        Assert.Contains("uninteresting", Find(doc, tree, "body").IgnoredReasons);
    }

    [Fact]
    public void Elements_with_a_Chrome_internal_role_are_not_ignored_and_export_it()
    {
        (Document doc, AccessibilityTree tree) = Build("<label id=l>a <input id=t></label><iframe id=f></iframe><details><summary id=s>S</summary>x</details><input id=d type=date value=2026-01-02><input id=c type=color>");
        using JsonDocument export = JsonDocument.Parse(tree.ToChromeJson());
        foreach ((string selector, string role) in new[] { ("#l", "LabelText"), ("#f", "Iframe"), ("#s", "DisclosureTriangle"), ("#d", "Date"), ("#c", "ColorWell") })
        {
            AxNode n = Find(doc, tree, selector);
            Assert.False(n.IsIgnored);
            Assert.Equal(role, n.InternalRole);
            Assert.Equal(string.Empty, n.Role);
            Assert.Equal(role, ExportNode(export, n).GetProperty("role").GetProperty("value").GetString());
        }

        Assert.Equal("2026-01-02", Find(doc, tree, "#d").States.Value);
        Assert.Equal("#000000", Find(doc, tree, "#c").States.Value);
    }

    [Fact]
    public void Svg_and_mathml_roots_keep_their_role_and_export_chromes_name()
    {
        (Document doc, AccessibilityTree tree) = Build("<svg id=s><title>T</title></svg><math id=m><mi id=i>x</mi></math>");
        using JsonDocument export = JsonDocument.Parse(tree.ToChromeJson());
        Assert.Equal("graphics-document", Find(doc, tree, "svg").Role);
        Assert.Equal("SvgRoot", Find(doc, tree, "svg").InternalRole);
        Assert.Equal("MathMLMath", ExportNode(export, Find(doc, tree, "math")).GetProperty("role").GetProperty("value").GetString());
        Assert.Equal("MathMLIdentifier", Find(doc, tree, "mi").InternalRole);
        // The text of an SVG title is not rendered: it has no node, but it still names the svg.
        Assert.Null(tree.Find(doc.Root.QuerySelector("title").FirstChild));
        Assert.Equal("T", Find(doc, tree, "svg").Name.Value);
    }

    [Fact]
    public void An_unnamed_form_is_a_form_and_a_password_is_a_textbox_without_a_value()
    {
        (Document doc, AccessibilityTree tree) = Build("<form id=f><input id=p type=password value=secret><input id=u type=file></form>");
        Assert.Equal("form", Find(doc, tree, "#f").Role);
        Assert.Equal("textbox", Find(doc, tree, "#p").Role);
        Assert.Null(Find(doc, tree, "#p").States.Value);
        Assert.Equal("button", Find(doc, tree, "#u").Role);
    }

    [Fact]
    public void A_row_has_no_name_from_its_cells_but_a_cell_has_and_an_implicit_tbody_is_ignored()
    {
        (Document doc, AccessibilityTree tree) = Build("<table><caption>C</caption><tbody><tr id=r><td id=c>cell</td></tr></tbody><thead><tr><th>h</th></tr></thead></table>");
        Assert.Equal("row", Find(doc, tree, "#r").Role);
        Assert.True(Find(doc, tree, "#r").Name.IsEmpty);
        Assert.Equal("cell", Find(doc, tree, "#c").Name.Value);
        Assert.True(Find(doc, tree, "tbody").IsIgnored);
        Assert.False(Find(doc, tree, "thead").IsIgnored);
        Assert.Equal("rowgroup", Find(doc, tree, "thead").Role);
    }

    [Fact]
    public void A_row_is_still_named_by_aria_label()
    {
        (Document doc, AccessibilityTree tree) = Build("<table><tr id=r aria-label=Total><td>1</td></tr></table>");
        Assert.Equal("Total", Find(doc, tree, "#r").Name.Value);
    }

    [Fact]
    public void Textarea_and_option_have_no_text_children_but_keep_their_name_and_value()
    {
        (Document doc, AccessibilityTree tree) = Build("<textarea id=t>some text</textarea><select id=s><option id=o>Alpha</option></select>");
        Assert.Empty(Find(doc, tree, "#t").Children);
        Assert.Empty(Find(doc, tree, "#o").Children);
        Assert.Equal("some text", Find(doc, tree, "#t").States.Value);
        Assert.Equal("Alpha", Find(doc, tree, "#o").Name.Value);
    }

    [Fact]
    public void Options_are_focusable_without_joining_the_tab_order_and_a_select_has_a_menu_popup()
    {
        (Document doc, AccessibilityTree tree) = Build("<select id=s><option id=o>A</option><option id=d disabled>B</option></select>");
        AxNode option = Find(doc, tree, "#o");
        Assert.True(option.Focus.IsFocusable);
        Assert.False(option.Focus.IsTabbable);
        Assert.DoesNotContain(option, tree.FocusOrder);
        Assert.False(Find(doc, tree, "#d").Focus.IsFocusable);
        Assert.Equal("menu", Find(doc, tree, "#s").States.HasPopup);
    }

    [Fact]
    public void List_items_get_the_number_of_list_ancestors_as_their_level()
    {
        (Document doc, AccessibilityTree tree) = Build("<ul><li id=a>a<ul><li id=b>b</li></ul></li><li id=c aria-level=5>c</li></ul>");
        Assert.Equal(1, Find(doc, tree, "#a").States.Level);
        Assert.Equal(2, Find(doc, tree, "#b").States.Level);
        Assert.Equal(5, Find(doc, tree, "#c").States.Level);
    }

    [Fact]
    public void Number_and_range_inputs_state_bounds_and_value_text()
    {
        (Document doc, AccessibilityTree tree) = Build("<input id=n type=number value=3><input id=r type=range value=4 max=10><input id=m type=number min=2 max=9>");
        AxStates n = Find(doc, tree, "#n").States;
        Assert.Equal((0, 0, "3"), (n.ValueMin, n.ValueMax, n.ValueText));
        Assert.Equal("plaintext", n.Editable);
        AxStates r = Find(doc, tree, "#r").States;
        Assert.Equal((0, 10, "4"), (r.ValueMin, r.ValueMax, r.ValueText));
        AxStates m = Find(doc, tree, "#m").States;
        Assert.Equal((2, 9), (m.ValueMin, m.ValueMax));
        Assert.Null(m.ValueText);
    }

    [Fact]
    public void The_export_leaves_out_computed_set_positions_but_not_given_ones()
    {
        (Document doc, AccessibilityTree tree) = Build("<ul><li id=a>a</li><li id=b aria-posinset=7 aria-setsize=9>b</li></ul>");
        using JsonDocument export = JsonDocument.Parse(tree.ToChromeJson());
        AxNode a = Find(doc, tree, "#a");
        Assert.Equal(1, a.States.PosInSet);
        Assert.Null(Prop(ExportNode(export, a), "posinset"));
        Assert.Null(Prop(ExportNode(export, a), "setsize"));
        AxNode b = Find(doc, tree, "#b");
        Assert.Equal("7", Prop(ExportNode(export, b), "posinset"));
        Assert.Equal("9", Prop(ExportNode(export, b), "setsize"));
    }

    [Fact]
    public void The_export_reports_a_native_label_as_labelledby_but_the_tree_relation_stays_aria_only()
    {
        (Document doc, AccessibilityTree tree) = Build("<label id=l>Name <input id=i></label>");
        using JsonDocument export = JsonDocument.Parse(tree.ToChromeJson());
        AxNode input = Find(doc, tree, "#i");
        Assert.Null(input.GetRelation(AxRelationKind.LabelledBy));
        JsonElement p = ExportNode(export, input).GetProperty("properties").EnumerateArray().First(x => x.GetProperty("name").GetString() == "labelledby");
        Assert.Equal(doc.Root.QuerySelector("#l").Index, p.GetProperty("value").GetProperty("relatedNodes")[0].GetProperty("backendDOMNodeId").GetInt32());
    }

    [Fact]
    public void Link_and_image_urls_are_resolved_against_the_base_url()
    {
        string html = "<a id=a href='/x?y=1#z'>a</a><a id=b href='https://other.example/p'>b</a><img id=i src=pic.png alt=pic>";
        (Document doc, AccessibilityTree tree) = Build(html, new AccessibilityOptions { BaseUrl = "https://site.example/dir/page.html" });
        Assert.Equal("https://site.example/x?y=1#z", Find(doc, tree, "#a").States.Url);
        Assert.Equal("https://other.example/p", Find(doc, tree, "#b").States.Url);
        Assert.Equal("https://site.example/dir/pic.png", Find(doc, tree, "#i").States.Url);
        (doc, tree) = Build("<base href='/other/'><a id=a href='q'>a</a>", new AccessibilityOptions { BaseUrl = "https://site.example/dir/page.html" });
        Assert.Equal("https://site.example/other/q", Find(doc, tree, "#a").States.Url);
        (doc, tree) = Build(html);
        Assert.Equal("/x?y=1#z", Find(doc, tree, "#a").States.Url);
    }
}
