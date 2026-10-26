using System;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>
/// One minimal page per choice the tree makes to match Chrome's accessibility tree (profile entries of the same names in
/// <c>docs/accessibility-chromium-profile.md</c>). The pages are written here.
/// </summary>
public sealed class ChromeFixTests
{
    private static (Document Doc, AccessibilityTree Tree, JsonDocument Export) Build(string html, AccessibilityOptions? options = null)
    {
        Document doc = Document.Parse(html);
        AccessibilityTree tree = AccessibilityTree.Build(doc, DocumentStyles.Create(doc), null, options);
        return (doc, tree, JsonDocument.Parse(tree.ToChromeJson()));
    }

    private static AxNode Find(Document doc, AccessibilityTree tree, string selector) =>
        tree.Find(doc.Root.QuerySelector(selector)) ?? throw new InvalidOperationException("not in the tree: " + selector);

    private static string? Prop(JsonDocument export, AxNode node, string name)
    {
        foreach (JsonElement p in export.RootElement.GetProperty("nodes")[node.Id].GetProperty("properties").EnumerateArray())
        {
            if (p.GetProperty("name").GetString() == name)
            {
                JsonElement v = p.GetProperty("value").GetProperty("value");
                return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
            }
        }

        return null;
    }

    private static string ExportedRole(JsonDocument export, AxNode node) =>
        export.RootElement.GetProperty("nodes")[node.Id].GetProperty("role").GetProperty("value").GetString()!;

    [Fact]
    public void HasPopup_true_is_menu_and_a_list_autocomplete_opens_a_listbox()
    {
        (Document doc, AccessibilityTree tree, _) = Build("<div id=a role=button aria-haspopup=true>a</div><div id=b role=button aria-haspopup=dialog>b</div><input id=c role=combobox aria-autocomplete=list><input id=d role=combobox aria-autocomplete=both>");
        Assert.Equal("menu", Find(doc, tree, "#a").States.HasPopup);
        Assert.Equal("dialog", Find(doc, tree, "#b").States.HasPopup);
        Assert.Equal("listbox", Find(doc, tree, "#c").States.HasPopup);
        Assert.Equal("listbox", Find(doc, tree, "#d").States.HasPopup);
    }

    [Fact]
    public void Live_atomic_and_relevant_are_only_on_live_regions()
    {
        (Document doc, AccessibilityTree tree, _) = Build("<div id=a role=alert>x</div><div id=b aria-live=polite>x</div><div id=c aria-atomic=true aria-relevant=all>x</div>");
        AxNode alert = Find(doc, tree, "#a");
        Assert.Equal("assertive", alert.States.Live);
        Assert.Equal(true, alert.States.Atomic);
        Assert.Equal("additions text", alert.States.Relevant);
        AxNode polite = Find(doc, tree, "#b");
        Assert.Equal("polite", polite.States.Live);
        Assert.NotEqual(true, polite.States.Atomic);
        Assert.Equal("additions text", polite.States.Relevant);
        AxNode plain = Find(doc, tree, "#c");
        Assert.Null(plain.States.Live);
        Assert.Null(plain.States.Atomic);
        Assert.Null(plain.States.Relevant);
    }

    [Fact]
    public void An_accesskey_is_exposed_as_an_Alt_shortcut()
    {
        (Document doc, AccessibilityTree tree, JsonDocument export) = Build("<button id=a accesskey=k>a</button><button id=b>b</button>");
        Assert.Equal("Alt+k", Prop(export, Find(doc, tree, "#a"), "keyshortcuts"));
        Assert.Null(Prop(export, Find(doc, tree, "#b"), "keyshortcuts"));
    }

    [Fact]
    public void Every_text_entry_input_is_editable_and_a_checkbox_is_not()
    {
        (Document doc, AccessibilityTree tree, JsonDocument export) = Build("<input id=a type=email><input id=b type=search><input id=c type=tel><input id=d type=url><input id=e type=password><input id=f type=checkbox>");
        foreach (string id in new[] { "#a", "#b", "#c", "#d", "#e" })
        {
            Assert.NotNull(Prop(export, Find(doc, tree, id), "editable"));
        }

        Assert.Null(Prop(export, Find(doc, tree, "#f"), "editable"));
    }

    [Fact]
    public void An_option_is_selected_by_its_selectedness_and_the_first_option_of_a_single_select_by_default()
    {
        (Document doc, AccessibilityTree tree, _) = Build("<select id=s1><option id=a>A<option id=b>B</select><select id=s2><option id=c>C<option id=d selected>D</select>");
        Assert.Equal(true, Find(doc, tree, "#a").States.Selected);
        Assert.NotEqual(true, Find(doc, tree, "#b").States.Selected);
        Assert.NotEqual(true, Find(doc, tree, "#c").States.Selected);
        Assert.Equal(true, Find(doc, tree, "#d").States.Selected);
    }

    [Fact]
    public void Constraint_invalid_covers_checkbox_placeholder_select_and_number_range_but_not_empty_text_fields()
    {
        (Document doc, AccessibilityTree tree, JsonDocument export) = Build(
            "<input id=a type=checkbox required><input id=b type=checkbox required checked>" +
            "<select id=c required><option value=''>Pick<option value=1>One</select><select id=d required><option value=1 selected>One</select>" +
            "<input id=e type=number min=1 max=3 value=5><input id=f type=number min=1 max=3 value=2>" +
            "<input id=g type=text required>");
        Assert.Equal("true", Prop(export, Find(doc, tree, "#a"), "invalid"));
        Assert.Null(Prop(export, Find(doc, tree, "#b"), "invalid"));
        Assert.Equal("true", Prop(export, Find(doc, tree, "#c"), "invalid"));
        Assert.Null(Prop(export, Find(doc, tree, "#d"), "invalid"));
        Assert.Equal("true", Prop(export, Find(doc, tree, "#e"), "invalid"));
        Assert.Null(Prop(export, Find(doc, tree, "#f"), "invalid"));
        Assert.Null(Prop(export, Find(doc, tree, "#g"), "invalid"));
        Assert.Null(Find(doc, tree, "#a").States.Invalid);
    }

    [Fact]
    public void A_url_is_stated_only_for_links_and_images()
    {
        (Document doc, AccessibilityTree tree, _) = Build("<a id=a href=/x>a</a><a id=b href=/x role=button>b</a><img id=c src=/p.png alt=p><iframe id=d src=/f></iframe>");
        Assert.Equal("/x", Find(doc, tree, "#a").States.Url);
        Assert.Null(Find(doc, tree, "#b").States.Url);
        Assert.Equal("/p.png", Find(doc, tree, "#c").States.Url);
        Assert.Null(Find(doc, tree, "#d").States.Url);
    }

    [Theory]
    [InlineData("http://EXAMPLE.com:80/a/./b/../c", "http://example.com/a/c")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("https://example.com:8443/p", "https://example.com:8443/p")]
    [InlineData("https://example.com/a%2fB?q=%7e#%7E", "https://example.com/a%2fB?q=%7e#%7E")]
    [InlineData("../up", "https://example.com/up")]
    [InlineData("?q=1", "https://example.com/dir/page?q=1")]
    [InlineData("//other.example/x", "https://other.example/x")]
    [InlineData("#f", "https://example.com/dir/page#f")]
    public void Urls_are_resolved_the_way_the_URL_Standard_serializes_them(string reference, string expected)
    {
        (Document doc, AccessibilityTree tree, _) = Build("<a id=a href='" + reference + "'>a</a>", new AccessibilityOptions { BaseUrl = "https://example.com/dir/page" });
        Assert.Equal(expected, Find(doc, tree, "#a").States.Url);
    }

    [Fact]
    public void A_dl_is_a_DescriptionList_and_a_figcaption_is_a_Figcaption()
    {
        (Document doc, AccessibilityTree tree, JsonDocument export) = Build("<dl id=d><dt>a<dd>b</dl><figure><figcaption id=f>c</figcaption></figure>");
        Assert.Equal("DescriptionList", ExportedRole(export, Find(doc, tree, "#d")));
        Assert.Equal("Figcaption", ExportedRole(export, Find(doc, tree, "#f")));
    }

    [Fact]
    public void An_li_outside_a_list_is_still_a_listitem()
    {
        (Document doc, AccessibilityTree tree, JsonDocument export) = Build("<div><li id=l>x</li></div>");
        Assert.Equal("listitem", ExportedRole(export, Find(doc, tree, "#l")));
    }

    [Fact]
    public void An_unnamed_aside_inside_sectioning_content_is_generic_but_a_named_or_top_level_one_is_a_landmark()
    {
        (Document doc, AccessibilityTree tree, JsonDocument export) = Build("<aside id=top>t</aside><article><aside id=in>i</aside><aside id=named aria-label=N>n</aside></article>");
        Assert.Equal("complementary", ExportedRole(export, Find(doc, tree, "#top")));
        Assert.Equal("generic", ExportedRole(export, Find(doc, tree, "#in")));
        Assert.Equal("complementary", ExportedRole(export, Find(doc, tree, "#named")));
    }

    [Fact]
    public void Descendants_of_a_button_keep_their_roles()
    {
        (Document doc, AccessibilityTree tree, _) = Build("<button><h2 id=h>Title</h2></button>");
        AxNode h = Find(doc, tree, "#h");
        Assert.Equal("heading", h.Role);
        Assert.False(h.IsIgnored);
    }

    [Fact]
    public void In_a_table_with_role_none_cells_are_generic_and_rows_are_uninteresting()
    {
        (Document doc, AccessibilityTree tree, _) = Build("<table role=none><tbody><tr id=r><td id=c>x</td></tr></tbody></table>");
        AxNode cell = Find(doc, tree, "#c");
        Assert.Equal("generic", cell.Role);
        Assert.False(cell.IsIgnored);
        AxNode row = Find(doc, tree, "#r");
        Assert.True(row.IsIgnored);
        Assert.Contains("uninteresting", row.IgnoredReasons);
    }

    [Fact]
    public void Noscript_is_not_rendered()
    {
        (Document doc, AccessibilityTree tree, _) = Build("<noscript id=n>x</noscript>");
        // Like display: none, a not-rendered element has no node in the tree.
        Assert.Null(tree.Find(doc.Root.QuerySelector("#n")));
    }

    [Fact]
    public void An_autofocus_element_is_exported_as_focused()
    {
        (Document doc, AccessibilityTree tree, JsonDocument export) = Build("<input id=a autofocus><input id=b>");
        Assert.Equal("true", Prop(export, Find(doc, tree, "#a"), "focused"));
        Assert.Null(Prop(export, Find(doc, tree, "#b"), "focused"));
    }

    [Fact]
    public void Static_text_has_the_text_transform_applied()
    {
        (Document doc, AccessibilityTree tree, _) = Build("<p id=p style='text-transform:uppercase'>hello world</p>");
        AxNode p = Find(doc, tree, "#p");
        Assert.Equal("HELLO WORLD", p.Children.Single(c => c.Kind == AxNodeKind.Text).Name.Value);
    }

    [Fact]
    public void A_title_is_the_description_of_a_generic_element_but_still_names_other_roles()
    {
        (Document doc, AccessibilityTree tree, _) = Build("<div id=g title=tip>x</div><div id=a role=application title=tip>x</div><button id=b title=tip></button><a id=l href=/x title='Go'>Go</a>");
        AxNode g = Find(doc, tree, "#g");
        Assert.True(g.Name.IsEmpty);
        Assert.Equal("tip", g.Description.Value);
        Assert.Equal("tip", Find(doc, tree, "#a").Name.Value);
        Assert.Equal("tip", Find(doc, tree, "#b").Name.Value);
        AxNode l = Find(doc, tree, "#l");
        Assert.Equal("Go", l.Name.Value);
        Assert.True(l.Description.IsEmpty);
    }

    [Fact]
    public void Positioned_and_floated_elements_are_blocks_for_the_spaces_in_a_name_but_flex_items_are_not()
    {
        (Document doc, AccessibilityTree tree, _) = Build(
            "<a id=a href=/x>one<span style='position:absolute'>two</span>three</a>" +
            "<a id=b href=/x>one<span style='float:left'>two</span>three</a>" +
            "<a id=c href=/x>one<span>two</span>three</a>" +
            "<a id=d href=/x style='display:flex'>one<span>two</span>three</a>");
        Assert.Equal("one two three", Find(doc, tree, "#a").Name.Value);
        Assert.Equal("one two three", Find(doc, tree, "#b").Name.Value);
        Assert.Equal("onetwothree", Find(doc, tree, "#c").Name.Value);
        Assert.Equal("onetwothree", Find(doc, tree, "#d").Name.Value);
    }

    [Fact]
    public void The_export_states_required_only_for_roles_that_take_it_and_not_for_checkbox_radio_select_or_button()
    {
        (Document doc, AccessibilityTree tree, JsonDocument export) = Build("<input id=a type=text required><input id=b type=checkbox required><input id=c type=radio required><select id=d required><option>x</select><div id=e role=textbox aria-required=true></div>");
        Assert.Equal("true", Prop(export, Find(doc, tree, "#a"), "required"));
        Assert.Null(Prop(export, Find(doc, tree, "#b"), "required"));
        Assert.Null(Prop(export, Find(doc, tree, "#c"), "required"));
        Assert.Null(Prop(export, Find(doc, tree, "#d"), "required"));
        Assert.Equal("true", Prop(export, Find(doc, tree, "#e"), "required"));
    }

    [Fact]
    public void The_export_lists_only_exposed_targets_of_controls_and_every_target_of_labelledby_and_describedby()
    {
        (Document doc, AccessibilityTree tree, JsonDocument export) = Build(
            "<button id=a aria-controls='shown gone'>a</button><div id=shown>s</div><div id=gone hidden>g</div>" +
            "<div id=b role=group aria-labelledby=gone>b</div><div id=c aria-describedby=gone>c</div>");
        JsonElement controls = export.RootElement.GetProperty("nodes")[Find(doc, tree, "#a").Id].GetProperty("properties").EnumerateArray()
            .First(p => p.GetProperty("name").GetString() == "controls").GetProperty("value").GetProperty("relatedNodes");
        Assert.Equal(1, controls.GetArrayLength());
        Assert.Equal(doc.Root.QuerySelector("#shown").Index, controls[0].GetProperty("backendDOMNodeId").GetInt32());
        JsonElement labelled = export.RootElement.GetProperty("nodes")[Find(doc, tree, "#b").Id].GetProperty("properties").EnumerateArray()
            .First(p => p.GetProperty("name").GetString() == "labelledby").GetProperty("value").GetProperty("relatedNodes");
        Assert.Equal(doc.Root.QuerySelector("#gone").Index, labelled[0].GetProperty("backendDOMNodeId").GetInt32());
    }

    [Fact]
    public void An_iframe_is_not_exported_as_focusable()
    {
        (Document doc, AccessibilityTree tree, JsonDocument export) = Build("<iframe id=f></iframe><button id=b>b</button>");
        Assert.Null(Prop(export, Find(doc, tree, "#f"), "focusable"));
        Assert.Equal("true", Prop(export, Find(doc, tree, "#b"), "focusable"));
    }

    [Fact]
    public void An_li_in_a_list_element_with_another_role_is_presentational_but_stays_a_listitem_in_a_plain_or_list_role_list()
    {
        (Document doc, AccessibilityTree tree, JsonDocument export) = Build("<ul role=menu><li id=a>a</li></ul><ul role=tablist><li id=b>b</li></ul><ul><li id=c>c</li></ul><ul role=list><li id=d>d</li></ul>");
        Assert.True(Find(doc, tree, "#a").IsIgnored);
        Assert.Contains("presentationalRole", Find(doc, tree, "#a").IgnoredReasons);
        Assert.True(Find(doc, tree, "#b").IsIgnored);
        Assert.Equal("listitem", ExportedRole(export, Find(doc, tree, "#c")));
        Assert.Equal("listitem", ExportedRole(export, Find(doc, tree, "#d")));
    }

    [Fact]
    public void A_text_only_label_of_a_checkbox_or_radio_is_ignored_as_label_for_but_other_labels_are_not()
    {
        (Document doc, AccessibilityTree tree, JsonDocument export) = Build(
            "<input id=c type=checkbox><label id=l1 for=c>Text</label>" +
            "<input id=r type=radio><label id=l2 for=r><b>Bold</b> text</label>" +
            "<input id=t type=text><label id=l3 for=t>Name</label>");
        AxNode l1 = Find(doc, tree, "#l1");
        Assert.True(l1.IsIgnored);
        Assert.Equal(["labelFor"], l1.IgnoredReasons);
        Assert.All(l1.Children, c => Assert.Contains("presentationalRole", c.IgnoredReasons));
        Assert.Equal("LabelText", ExportedRole(export, Find(doc, tree, "#l2")));
        Assert.Equal("LabelText", ExportedRole(export, Find(doc, tree, "#l3")));
        Assert.Equal("Text", Find(doc, tree, "#c").Name.Value);
    }

    [Fact]
    public void An_aria_hidden_element_inside_an_aria_hidden_one_is_still_the_element()
    {
        (Document doc, AccessibilityTree tree, _) = Build("<a id=a href=/x aria-hidden=true><svg id=s aria-hidden=true><title>t</title></svg><span id=p>x</span></a>");
        Assert.Equal(["ariaHiddenElement"], Find(doc, tree, "#a").IgnoredReasons);
        Assert.Equal(["ariaHiddenElement"], Find(doc, tree, "#s").IgnoredReasons);
        Assert.Equal(["ariaHiddenSubtree"], Find(doc, tree, "#p").IgnoredReasons);
    }

    [Fact]
    public void A_wbr_is_ignored_as_not_rendered()
    {
        (Document doc, AccessibilityTree tree, _) = Build("<p>long<wbr>word</p>");
        AxNode wbr = Find(doc, tree, "wbr");
        Assert.True(wbr.IsIgnored);
        Assert.Equal(["notRendered"], wbr.IgnoredReasons);
    }
}
