using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

public sealed class AccessibilityTreeTests
{
    private static (Document Doc, AccessibilityTree Tree) Build(string html, StyleContext? context = null)
    {
        Document doc = Document.Parse(html);
        return (doc, AccessibilityTree.Build(doc, DocumentStyles.Create(doc), context));
    }

    private static AxNode Find(Document doc, AccessibilityTree tree, string selector) =>
        tree.Find(doc.Root.QuerySelector(selector)) ?? throw new InvalidOperationException("not in the tree: " + selector);

    // ---- hidden status ----

    [Theory]
    [InlineData("<div id=t style='display:none'>x</div>", HiddenReasons.DisplayNone)]
    [InlineData("<div id=t hidden>x</div>", HiddenReasons.HiddenAttribute)]
    [InlineData("<div></div><template id=t><p>x</p></template>", HiddenReasons.Template)]
    [InlineData("<div id=t style='content-visibility:hidden'><p id=u>x</p></div>", HiddenReasons.None)]
    public void Hidden_reason_of_the_element(string html, HiddenReasons expected)
    {
        (Document doc, AccessibilityTree tree) = Build(html);
        HiddenInfo h = tree.GetHidden(doc.Root.QuerySelector("#t"));
        Assert.Equal(expected, h.Reasons);
        if (expected != HiddenReasons.None)
        {
            Assert.True(h.IsNotRendered);
            Assert.Null(tree.Find(doc.Root.QuerySelector("#t")));
        }
    }

    [Fact]
    public void Content_visibility_hidden_hides_the_contents_not_the_element()
    {
        (Document doc, AccessibilityTree tree) = Build("<div id=t style='content-visibility:hidden'><p id=u>x</p></div>");
        Assert.NotNull(tree.Find(doc.Root.QuerySelector("#t")));
        HiddenInfo u = tree.GetHidden(doc.Root.QuerySelector("#u"));
        Assert.Equal(HiddenReasons.ContentVisibilityHidden, u.Reasons);
        Assert.True(u.IsInherited);
        Assert.Equal("t", u.Origin.Id);
        Assert.Null(tree.Find(doc.Root.QuerySelector("#u")));
    }

    [Fact]
    public void Display_none_is_inherited_by_descendants_with_the_origin()
    {
        (Document doc, AccessibilityTree tree) = Build("<div id=t style='display:none'><p><span id=u>x</span></p></div>");
        HiddenInfo h = tree.GetHidden(doc.Root.QuerySelector("#u"));
        Assert.Equal(HiddenReasons.DisplayNone, h.Reasons);
        Assert.True(h.IsInherited);
        Assert.Equal("t", h.Origin.Id);
    }

    [Fact]
    public void Visibility_hidden_is_ignored_but_present_and_a_visible_descendant_shows_again()
    {
        (Document doc, AccessibilityTree tree) = Build("<div id=t style='visibility:hidden'><button id=a>A</button><button id=b style='visibility:visible'>B</button></div>");
        AxNode a = Find(doc, tree, "#a");
        AxNode b = Find(doc, tree, "#b");
        Assert.True(a.IsIgnored);
        Assert.Contains("notVisible", a.IgnoredReasons);
        Assert.Equal(HiddenReasons.VisibilityHidden, a.Hidden.Primary);
        Assert.False(b.IsIgnored);
        Assert.False(b.Focus.IsFocusable && a.Focus.IsFocusable);
        Assert.False(a.Focus.IsFocusable);
        Assert.True(b.Focus.IsFocusable);
    }

    [Fact]
    public void Aria_hidden_covers_the_subtree_and_a_descendant_cannot_undo_it()
    {
        (Document doc, AccessibilityTree tree) = Build("<div id=t aria-hidden=true><p id=u aria-hidden=false>x</p></div>");
        AxNode t = Find(doc, tree, "#t");
        AxNode u = Find(doc, tree, "#u");
        Assert.True(t.IsIgnored);
        Assert.Contains("ariaHiddenElement", t.IgnoredReasons);
        Assert.True(u.IsIgnored);
        Assert.Contains("ariaHiddenSubtree", u.IgnoredReasons);
        Assert.True(u.Hidden.IsInherited);
        Assert.Equal(t.DomNode, u.Hidden.Origin);
    }

    [Fact]
    public void Inert_subtree_is_ignored_and_not_focusable()
    {
        (Document doc, AccessibilityTree tree) = Build("<div id=t inert><button id=b>B</button></div>");
        AxNode b = Find(doc, tree, "#b");
        Assert.True(b.IsIgnored);
        Assert.Contains("inertSubtree", b.IgnoredReasons);
        Assert.False(b.Focus.IsFocusable);
    }

    [Fact]
    public void Closed_details_hides_everything_but_the_first_summary()
    {
        (Document doc, AccessibilityTree tree) = Build("<details id=d><summary id=s>S</summary><p id=p>P</p><summary id=s2>S2</summary></details><details open><summary>S</summary><p id=q>Q</p></details>");
        Assert.NotNull(tree.Find(doc.Root.QuerySelector("#s")));
        Assert.Equal(HiddenReasons.ClosedDetails, tree.GetHidden(doc.Root.QuerySelector("#p")).Reasons);
        Assert.Equal(HiddenReasons.ClosedDetails, tree.GetHidden(doc.Root.QuerySelector("#s2")).Reasons);
        Assert.NotNull(tree.Find(doc.Root.QuerySelector("#q")));
    }

    [Fact]
    public void Layout_dependent_hiding_is_unknown()
    {
        Assert.Equal(LayoutHiding.Unknown, HiddenInfo.LayoutDependent);
    }

    // ---- aria-owns ----

    private static string ChildIds(AxNode n) => string.Join(",", n.Children.Where(c => c.Kind == AxNodeKind.Element).Select(c => c.DomNode.Id));

    [Fact]
    public void Owned_element_moves_to_the_owner_after_its_own_children()
    {
        (Document doc, AccessibilityTree tree) = Build("<ul id=o aria-owns='z y'><li id=a>a</li></ul><ul id=p><li id=y>y</li><li id=z>z</li></ul>");
        AxNode o = Find(doc, tree, "#o");
        Assert.Equal("a,z,y", ChildIds(o));
        Assert.Equal("", ChildIds(Find(doc, tree, "#p")));
        Assert.Same(o, Find(doc, tree, "#z").Parent);
        AxRelation owns = o.GetRelation(AxRelationKind.Owns)!.Value;
        Assert.Equal(2, owns.Targets.Count);
    }

    [Fact]
    public void First_owner_in_document_order_wins()
    {
        (Document doc, AccessibilityTree tree) = Build("<div id=a aria-owns=x></div><div id=b aria-owns=x></div><p id=x>x</p>");
        Assert.Equal("x", ChildIds(Find(doc, tree, "#a")));
        Assert.Equal("", ChildIds(Find(doc, tree, "#b")));
        Assert.Contains(tree.Diagnostics, d => d.Kind == AxDiagnosticKind.AriaOwnsIgnored && d.Node.Id == "b");
    }

    [Fact]
    public void Owning_an_ancestor_or_oneself_is_ignored()
    {
        (Document doc, AccessibilityTree tree) = Build("<div id=a><div id=b aria-owns='a b'><p id=c>c</p></div></div>");
        Assert.Equal("b", ChildIds(Find(doc, tree, "#a")));
        Assert.Equal("c", ChildIds(Find(doc, tree, "#b")));
        Assert.Equal(2, tree.Diagnostics.Count(d => d.Kind == AxDiagnosticKind.AriaOwnsIgnored));
    }

    [Fact]
    public void Owner_cycles_are_cut()
    {
        (Document doc, AccessibilityTree tree) = Build("<div id=a aria-owns=b></div><div id=b aria-owns=a></div>");
        // a adopts b; b then cannot adopt its new owner.
        Assert.Equal("b", ChildIds(Find(doc, tree, "#a")));
        Assert.Equal("", ChildIds(Find(doc, tree, "#b")));
        Assert.Equal(tree.Nodes.Count, tree.Nodes.Select(n => n.Id).Distinct().Count());
    }

    [Fact]
    public void Owning_a_descendant_of_an_owned_element_is_allowed_and_the_tree_stays_a_tree()
    {
        (Document doc, AccessibilityTree tree) = Build("<div id=a aria-owns=c></div><div id=b><p id=c>c</p></div>");
        Assert.Equal("c", ChildIds(Find(doc, tree, "#a")));
        Assert.Equal("", ChildIds(Find(doc, tree, "#b")));
    }

    [Fact]
    public void Aria_owns_on_a_hidden_owner_or_of_a_hidden_target_is_not_resolved()
    {
        (Document doc, AccessibilityTree tree) = Build("<div id=a aria-hidden=true aria-owns=x></div><div id=b aria-owns=y></div><p id=x>x</p><p id=y hidden>y</p>");
        Assert.Equal("", ChildIds(Find(doc, tree, "#a")));
        Assert.Equal("", ChildIds(Find(doc, tree, "#b")));
        Assert.NotNull(Find(doc, tree, "#x").Parent);
    }

    // ---- names ----

    private static string Name(string html, string selector = "#t")
    {
        (Document doc, AccessibilityTree tree) = Build(html);
        return Find(doc, tree, selector).Name.Value;
    }

    [Fact]
    public void Generated_content_is_part_of_the_name_with_attr_counters_and_quotes()
    {
        Assert.Equal("[x] hello (2)", Name("<style>#t::before{content:'[' attr(data-k) '] '} #t::after{content:' (' counter(n) ')'} #t{counter-reset:n 2}</style><button id=t data-k=x>hello</button>"));
        Assert.Equal("“quoted”", Name("<a href=# id=t><q>quoted</q></a>"));
        Assert.Equal("Alt text label", Name("<style>#t::before{content:'ignored' / 'Alt text'}</style><button id=t>label</button>"));
    }

    [Fact]
    public void Counters_follow_scope_and_list_items()
    {
        // Each sibling bumps the same counter; a nested reset starts a new scope; list items count themselves.
        string css = "<style>.n::before{counter-increment:c; content:counter(c) '.'} .r{counter-reset:c 10}</style>";
        Assert.Equal("2.b", Name(css + "<div style='counter-reset:c'><p class=n>a</p><a href=# class=n id=t>b</a></div>", "#t"));
        Assert.Equal("11.b", Name(css + "<div><p class=n>a</p><div class=r><a href=# class=n id=t>b</a></div></div>", "#t"));
        Assert.Equal("2) x", Name("<style>li::before{content:counter(list-item) ') '}</style><ol><li>a</li><li id=t role=button>x</li></ol>", "#t"));
    }

    [Fact]
    public void Block_children_are_separated_and_inline_children_are_not()
    {
        Assert.Equal("one two", Name("<button id=t><div>one</div><div>two</div></button>"));
        Assert.Equal("onetwo", Name("<button id=t><span>one</span><span>two</span></button>"));
    }

    [Fact]
    public void Labelledby_uses_hidden_referenced_nodes_but_not_hidden_descendants_of_visible_ones()
    {
        Assert.Equal("secret more", Name("<button id=t aria-labelledby=h>x</button><div id=h style='display:none'>secret <b>more</b></div>"));
        Assert.Equal("seen", Name("<button id=t aria-labelledby=v>x</button><div id=v>seen<b style='display:none'> hidden</b></div>"));
        Assert.Equal("a b", Name("<button id=t aria-labelledby='a b'>x</button><p id=a>a</p><p id=b hidden>b</p>"));
    }

    [Fact]
    public void Labelledby_cycles_and_self_references_end()
    {
        Assert.Equal("own", Name("<div id=t role=group aria-label=own aria-labelledby=t></div>"));
        (Document doc, AccessibilityTree tree) = Build("<div id=a role=group aria-labelledby=b></div><div id=b role=group aria-labelledby=a>bee</div>");
        Assert.Equal("bee", Find(doc, tree, "#a").Name.Value);
        Assert.Equal("", Find(doc, tree, "#b").Name.Value);
    }

    [Fact]
    public void Native_labels_alt_caption_legend_and_fallbacks()
    {
        Assert.Equal("Label text", Name("<label for=t>Label text</label><input id=t>"));
        Assert.Equal("Wrapped", Name("<label>Wrapped <input id=t></label>").Trim());
        Assert.Equal("pic", Name("<img id=t alt=pic>"));
        Assert.Equal("cap", Name("<table id=t><caption>cap</caption><tr><td>x</td></tr></table>"));
        Assert.Equal("leg", Name("<fieldset id=t><legend>leg</legend></fieldset>"));
        Assert.Equal("tip", Name("<input id=t title=tip>"));
        Assert.Equal("hint", Name("<input id=t placeholder=hint>"));
        Assert.Equal("", Name("<img id=t alt='' title=tip>"));
        Assert.Equal("Go", Name("<input id=t type=submit value=Go>"));
        Assert.Equal("Submit", Name("<input id=t type=submit>"));
    }

    [Fact]
    public void Embedded_controls_contribute_their_value()
    {
        Assert.Equal("Flash 3 times", Name("<label for=c>Flash <input type=text value=3> times</label><input id=t type=checkbox>".Replace("for=c", "for=t", StringComparison.Ordinal)).Trim());
        Assert.Equal("Volume 7", Name("<label for=t>Volume <span role=slider aria-valuenow=7 aria-valuemin=0 aria-valuemax=10></span></label><input id=t>"));
    }

    [Fact]
    public void Source_of_the_name_is_reported()
    {
        (Document doc, AccessibilityTree tree) = Build("<button id=a aria-label=x>y</button><button id=b>y</button><button id=c title=z></button><input id=d placeholder=p><button id=e aria-labelledby=a>q</button>");
        Assert.Equal(AxTextSource.AriaLabel, Find(doc, tree, "#a").Name.Source);
        Assert.Equal(AxTextSource.Contents, Find(doc, tree, "#b").Name.Source);
        Assert.Equal(AxTextSource.Title, Find(doc, tree, "#c").Name.Source);
        Assert.Equal(AxTextSource.Placeholder, Find(doc, tree, "#d").Name.Source);
        AxText e = Find(doc, tree, "#e").Name;
        Assert.Equal(AxTextSource.LabelledBy, e.Source);
        Assert.Equal("a", e.RelatedElements[0].Id);
    }

    [Fact]
    public void Description_from_describedby_aria_description_and_title()
    {
        (Document doc, AccessibilityTree tree) = Build("<button id=a aria-describedby=d>A</button><p id=d>desc</p><button id=b aria-description=ad>B</button><button id=c title=tip>C</button><button id=e title=tip aria-label=named>E</button>");
        Assert.Equal("desc", Find(doc, tree, "#a").Description.Value);
        Assert.Equal(AxTextSource.AriaDescription, Find(doc, tree, "#b").Description.Source);
        Assert.Equal("tip", Find(doc, tree, "#c").Description.Value);
        Assert.Equal("tip", Find(doc, tree, "#e").Description.Value);
    }

    [Fact]
    public void Role_when_named_is_resolved_with_the_name()
    {
        (Document doc, AccessibilityTree tree) = Build("<section id=a aria-label=News>x</section><section id=b>y</section><form id=c title=T></form>");
        Assert.Equal("region", Find(doc, tree, "#a").Role);
        Assert.Equal("generic", Find(doc, tree, "#b").Role);
        Assert.Equal("form", Find(doc, tree, "#c").Role);
    }

    // ---- states ----

    [Fact]
    public void Position_in_set_and_set_size_are_computed_and_explicit_values_are_kept()
    {
        (Document doc, AccessibilityTree tree) = Build("<ul id=l><li id=a>a</li><li id=b>b</li><div><li id=c>c</li></div></ul><div role=listbox><div role=option id=o1>1</div><div role=option id=o2 aria-posinset=5 aria-setsize=9>2</div><div role=option id=o3>3</div></div>");
        Assert.Equal((1, 3), (Find(doc, tree, "#a").States.PosInSet!.Value, Find(doc, tree, "#a").States.SetSize!.Value));
        Assert.Equal(2, Find(doc, tree, "#b").States.PosInSet);
        Assert.Equal((1, 9), (Find(doc, tree, "#o1").States.PosInSet!.Value, Find(doc, tree, "#o1").States.SetSize!.Value));
        Assert.Equal(5, Find(doc, tree, "#o2").States.PosInSet);
        Assert.Equal(6, Find(doc, tree, "#o3").States.PosInSet);
    }

    [Fact]
    public void Radios_of_one_name_form_a_set()
    {
        (Document doc, AccessibilityTree tree) = Build("<form><input type=radio name=g id=a><input type=radio name=g id=b checked><input type=radio name=h id=c></form>");
        Assert.Equal((2, 2), (Find(doc, tree, "#b").States.PosInSet!.Value, Find(doc, tree, "#b").States.SetSize!.Value));
        Assert.Equal((1, 1), (Find(doc, tree, "#c").States.PosInSet!.Value, Find(doc, tree, "#c").States.SetSize!.Value));
        Assert.Equal(AxTristate.True, Find(doc, tree, "#b").States.Checked);
        Assert.Equal(AxTristate.False, Find(doc, tree, "#a").States.Checked);
    }

    [Fact]
    public void Native_and_aria_states_and_properties()
    {
        (Document doc, AccessibilityTree tree) = Build(
            "<button id=a aria-pressed=mixed aria-expanded=false aria-haspopup=menu>A</button>"
            + "<input id=b required readonly aria-invalid=true><fieldset disabled><input id=c></fieldset>"
            + "<div id=d role=slider aria-valuenow=3 aria-valuemin=1 aria-valuemax=9 aria-valuetext=three aria-orientation=vertical></div>"
            + "<h3 id=e>h</h3><div id=f role=heading>x</div><div role=group aria-disabled=true><button id=g>g</button></div>"
            + "<div id=h role=alert>a</div><input id=i type=range min=0 max=10 value=4><a id=j href=/x aria-current=page>j</a>"
            + "<select id=k multiple><option selected>o</option></select><textarea id=l>t</textarea>");
        Assert.Equal(AxTristate.Mixed, Find(doc, tree, "#a").States.Pressed);
        Assert.Equal(false, Find(doc, tree, "#a").States.Expanded);
        Assert.Equal("menu", Find(doc, tree, "#a").States.HasPopup);
        AxStates b = Find(doc, tree, "#b").States;
        Assert.True(b.Required);
        Assert.True(b.ReadOnly);
        Assert.Equal("true", b.Invalid);
        Assert.True(Find(doc, tree, "#c").States.Disabled);
        AxStates d = Find(doc, tree, "#d").States;
        Assert.Equal((3.0, 1.0, 9.0, "three", "vertical"), (d.ValueNow!.Value, d.ValueMin!.Value, d.ValueMax!.Value, d.ValueText, d.Orientation));
        Assert.Equal(3, Find(doc, tree, "#e").States.Level);
        Assert.Equal(2, Find(doc, tree, "#f").States.Level);
        Assert.True(Find(doc, tree, "#g").States.Disabled);
        Assert.Equal("assertive", Find(doc, tree, "#h").States.Live);
        Assert.Equal((4.0, 0.0, 10.0), (Find(doc, tree, "#i").States.ValueNow!.Value, Find(doc, tree, "#i").States.ValueMin!.Value, Find(doc, tree, "#i").States.ValueMax!.Value));
        Assert.Equal("page", Find(doc, tree, "#j").States.Current);
        Assert.Equal("/x", Find(doc, tree, "#j").States.Url);
        Assert.True(Find(doc, tree, "#k").States.MultiSelectable);
        Assert.Equal("t", Find(doc, tree, "#l").States.Value);
        Assert.True(Find(doc, tree, "#l").States.Multiline);
    }

    [Fact]
    public void Relations_resolve_and_report_broken_references()
    {
        (Document doc, AccessibilityTree tree) = Build("<button id=a aria-controls='p missing' aria-describedby=p aria-details=gone>A</button><div id=p>p</div><div id=gone hidden></div><input id=i list=dl><datalist id=dl></datalist>");
        AxNode a = Find(doc, tree, "#a");
        AxRelation controls = a.GetRelation(AxRelationKind.Controls)!.Value;
        Assert.Equal("missing", Assert.Single(controls.BrokenIds));
        Assert.Single(controls.Targets);
        AxRelation details = a.GetRelation(AxRelationKind.Details)!.Value;
        Assert.Empty(details.Targets);
        Assert.Contains(tree.Diagnostics, d => d.Kind == AxDiagnosticKind.BrokenIdReference && d.Attribute == "aria-details");
        Assert.Contains(tree.Diagnostics, d => d.Kind == AxDiagnosticKind.Role && d.RoleKind == Aria.RoleDiagnosticKind.BrokenIdReference && d.Value == "missing");
        Assert.NotNull(Find(doc, tree, "#i").GetRelation(AxRelationKind.Controls));
    }

    // ---- focus ----

    [Fact]
    public void Sequential_focus_order_puts_positive_tabindex_first()
    {
        (Document doc, AccessibilityTree tree) = Build("<a id=a href=#>a</a><button id=b tabindex=2>b</button><div id=c tabindex=0>c</div><div id=d tabindex=-1>d</div><button id=e tabindex=1>e</button><button id=f disabled>f</button><input id=g type=hidden><div id=h tabindex=2>h</div>");
        Assert.Equal("e,b,h,a,c", string.Join(",", tree.FocusOrder.Select(n => n.DomNode.Id)));
        Assert.True(Find(doc, tree, "#d").Focus.IsFocusable);
        Assert.False(Find(doc, tree, "#d").Focus.IsTabbable);
        Assert.False(Find(doc, tree, "#f").Focus.IsFocusable);
        Assert.Equal(0, Find(doc, tree, "#e").Focus.SequentialIndex);
        Assert.Equal(2, Find(doc, tree, "#h").Focus.SequentialIndex);
    }

    [Fact]
    public void Style_context_changes_focus_visibility()
    {
        string html = "<style>#a{display:none} #a:focus{display:block}</style><button id=b>b</button>";
        (Document doc, AccessibilityTree plain) = Build(html);
        Assert.NotNull(plain.Find(doc.Root.QuerySelector("#b")));
        Assert.Same(doc, plain.Document);
    }

    // ---- diagnostics ----

    [Fact]
    public void Diagnostics_report_context_owned_names_and_focusable_hidden()
    {
        (Document doc, AccessibilityTree tree) = Build(
            "<div role=listitem id=li>x</div><div role=list id=empty></div><button id=nameless></button><div role=img id=noname></div>"
            + "<button id=fh aria-hidden=true>x</button><ul id=ok><li>fine</li></ul>");
        bool Has(AxDiagnosticKind k, string id) => tree.Diagnostics.Any(d => d.Kind == k && d.Node.Id == id);
        Assert.True(Has(AxDiagnosticKind.MissingRequiredContext, "li"));
        Assert.True(Has(AxDiagnosticKind.MissingRequiredOwnedElements, "empty"));
        Assert.True(Has(AxDiagnosticKind.NamelessInteractive, "nameless"));
        Assert.True(Has(AxDiagnosticKind.EmptyNameRequired, "noname"));
        Assert.True(Has(AxDiagnosticKind.FocusableUnderAriaHidden, "fh"));
        Assert.DoesNotContain(tree.Diagnostics, d => d.Node.Id == "ok");
        Assert.Contains(Find(doc, tree, "#li").Diagnostics, d => d.Kind == AxDiagnosticKind.MissingRequiredContext);
    }

    // ---- shape ----

    [Fact]
    public void Tree_nodes_know_their_parts()
    {
        (Document doc, AccessibilityTree tree) = Build("<!doctype html><title>Page</title><main id=m><h1 id=h>Head <em>er</em></h1><p>Text</p></main>");
        Assert.Equal("RootWebArea", tree.Root.Role);
        Assert.Equal("Page", tree.Root.Name.Value);
        AxNode h = Find(doc, tree, "#h");
        Assert.Equal("heading", h.Role);
        Assert.Equal(Aria.RoleOrigin.Implicit, h.RoleOrigin);
        Assert.Equal("Head er", h.Name.Value);
        Assert.Equal("#h", h.Locator);
        Assert.True(h.SourceBytes.IsPresent);
        Assert.Equal("main", Find(doc, tree, "#m").Role);
        Assert.Contains(tree.Nodes, n => n.Kind == AxNodeKind.Text && n.Name.Value == "Text");
        for (int i = 0; i < tree.Nodes.Count; i++)
        {
            Assert.Equal(i, tree.Nodes[i].Id);
        }
    }

    [Fact]
    public void Export_has_the_shape_of_the_devtools_nodes()
    {
        (Document doc, AccessibilityTree tree) = Build("<main><button id=b aria-pressed=true>Go</button><div>plain</div></main>");
        using JsonDocument json = JsonDocument.Parse(tree.ToChromeJson());
        Assert.Equal("onehtml-ax/1", json.RootElement.GetProperty("schema").GetString());
        JsonElement nodes = json.RootElement.GetProperty("nodes");
        Assert.Equal(tree.Nodes.Count, nodes.GetArrayLength());
        JsonElement root = nodes[0];
        Assert.Equal("0", root.GetProperty("nodeId").GetString());
        Assert.Equal("RootWebArea", root.GetProperty("role").GetProperty("value").GetString());
        JsonElement button = nodes.EnumerateArray().First(n => n.TryGetProperty("backendDOMNodeId", out JsonElement id) && id.GetInt32() == doc.Root.QuerySelector("#b").Index);
        Assert.False(button.GetProperty("ignored").GetBoolean());
        Assert.Equal("button", button.GetProperty("role").GetProperty("value").GetString());
        JsonElement name = button.GetProperty("name");
        Assert.Equal("Go", name.GetProperty("value").GetString());
        Assert.Equal("contents", name.GetProperty("sources")[0].GetProperty("type").GetString());
        Assert.Contains(button.GetProperty("properties").EnumerateArray(), p => p.GetProperty("name").GetString() == "pressed" && p.GetProperty("value").GetProperty("value").GetString() == "true");
        Assert.Contains(button.GetProperty("properties").EnumerateArray(), p => p.GetProperty("name").GetString() == "focusable");
        JsonElement ignored = nodes.EnumerateArray().First(n => n.GetProperty("ignored").GetBoolean());
        Assert.Equal("none", ignored.GetProperty("role").GetProperty("value").GetString());
        Assert.True(ignored.GetProperty("ignoredReasons").GetArrayLength() > 0);
        // Child and parent ids agree.
        foreach (JsonElement n in nodes.EnumerateArray())
        {
            foreach (JsonElement c in n.GetProperty("childIds").EnumerateArray())
            {
                JsonElement child = nodes[int.Parse(c.GetString()!, System.Globalization.CultureInfo.InvariantCulture)];
                Assert.Equal(n.GetProperty("nodeId").GetString(), child.GetProperty("parentId").GetString());
            }
        }
    }

    [Fact]
    public void Export_is_deterministic()
    {
        string html = "<main aria-owns=z><h1>t</h1></main><p id=z>z</p><a href=#>l</a>";
        Assert.Equal(Build(html).Tree.ToChromeJson(), Build(html).Tree.ToChromeJson());
    }

    [Fact]
    public void Wrong_arguments_throw_argument_exceptions()
    {
        Document a = Document.Parse("<p>a</p>");
        Document b = Document.Parse("<p>b</p>");
        Assert.Throws<ArgumentException>(() => AccessibilityTree.Build(a, DocumentStyles.Create(b)));
        AccessibilityTree tree = AccessibilityTree.Build(a, DocumentStyles.Create(a));
        Assert.Throws<ArgumentException>(() => tree.Find(b.Root));
        Assert.Throws<ArgumentNullException>(() => AccessibilityTree.Build(null!, DocumentStyles.Create(a)));
    }

    [Fact]
    public void Cancellation_is_honoured()
    {
        Document doc = Document.Parse("<p>a</p>");
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();
        DocumentStyles styles = DocumentStyles.Create(doc);
        // A tiny document may finish before the first periodic check; a large one must not.
        string big = string.Concat(Enumerable.Repeat("<p>x</p>", 5000));
        Document large = Document.Parse(big);
        DocumentStyles ls = DocumentStyles.Create(large);
        Assert.Throws<OperationCanceledException>(() => AccessibilityTree.Build(large, ls, null, null, cts.Token));
        Assert.NotNull(AccessibilityTree.Build(doc, styles));
    }
}
