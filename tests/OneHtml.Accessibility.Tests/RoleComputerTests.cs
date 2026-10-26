using System.Linq;
using RecursiveAI.OneHtml.Accessibility.Aria;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

public sealed class RoleComputerTests
{
    private static (OneHtml.Document Doc, RoleMap Map) Compute(string html, bool nameByAriaLabel = true)
    {
        OneHtml.Document doc = OneHtml.Document.Parse(html);
        RoleMap map = RoleComputer.Compute(doc, new RoleOptions
        {
            HasAccessibleName = nameByAriaLabel ? n => !string.IsNullOrWhiteSpace(n.GetAttribute("aria-label")) : null,
        });
        return (doc, map);
    }

    private static NodeRole Of(string html, string selector, bool named = true)
    {
        (OneHtml.Document doc, RoleMap map) = Compute(html, named);
        return map[doc.Root.QuerySelector(selector)];
    }

    private static string Role(string html, string selector) => Of(html, selector).ComputedRole;

    [Theory]
    [InlineData("<div id=t role='foo button'>", "button")]
    [InlineData("<div id=t role='  button  '>", "button")]
    [InlineData("<div id=t role='BUTTON'>", "button")]
    [InlineData("<div id=t role='widget button'>", "button")]
    [InlineData("<div id=t role='foo'>", "generic")]
    [InlineData("<div id=t role='image'>", "image")]
    [InlineData("<div id=t role='img'>", "image")]
    [InlineData("<div id=t role='directory'>", "list")]
    [InlineData("<div id=t role='presentation'>", "none")]
    [InlineData("<div id=t role='none'>", "none")]
    [InlineData("<div id=t role='\u017Fection'>", "generic")]
    [InlineData("<h1 id=t role='structure'>", "heading")]
    public void Explicit_role_uses_the_first_valid_non_abstract_token(string html, string expected) =>
        Assert.Equal(expected, Role(html, "#t"));

    [Fact]
    public void Origins_tell_how_the_role_was_found()
    {
        Assert.Equal(RoleOrigin.Explicit, Of("<div id=t role=button>", "#t").Origin);
        Assert.Equal(RoleOrigin.Implicit, Of("<button id=t>", "#t").Origin);
        Assert.Equal(RoleOrigin.None, Of("<abbr id=t>", "#t").Origin);
        Assert.Equal("", Of("<abbr id=t>", "#t").ComputedRole);
    }

    [Theory]
    [InlineData("<h1 id=t role=none tabindex=0>", "heading")]
    [InlineData("<h1 id=t role=none tabindex=-1>", "heading")]
    [InlineData("<h1 id=t role=none aria-label=x>", "heading")]
    [InlineData("<h1 id=t role=presentation aria-describedby=a>", "heading")]
    [InlineData("<h1 id=t role=none aria-level=2>", "none")]
    [InlineData("<h1 id=t role=none>", "none")]
    [InlineData("<input id=t role=none>", "textbox")]
    [InlineData("<input id=t role=none readonly>", "textbox")]
    [InlineData("<input id=t role=none disabled>", "none")]
    [InlineData("<input id=t role=none disabled readonly>", "none")]
    [InlineData("<a id=t role=none href=x>", "link")]
    [InlineData("<a id=t role=none>", "none")]
    [InlineData("<p id=t role=none aria-label=x>", "paragraph")]
    [InlineData("<div id=t role=none contenteditable>", "generic")]
    public void Presentational_role_conflicts_resolve_to_the_implicit_role(string html, string expected) =>
        Assert.Equal(expected, Role(html, "#t"));

    [Fact]
    public void Conflict_resolved_origin_and_inert_subtree()
    {
        Assert.Equal(RoleOrigin.ConflictResolved, Of("<h1 id=t role=none tabindex=0>", "#t").Origin);
        // An inert subtree is not focusable, so the presentational role stands.
        Assert.Equal("none", Role("<div inert><h1 id=t role=none tabindex=0></div>", "#t"));
    }

    [Fact]
    public void Required_owned_elements_of_a_presentational_element_are_presentational()
    {
        (OneHtml.Document doc, RoleMap map) = Compute("<ul role=none><li id=a aria-describedby=z>a<li id=b tabindex=0>b<li id=c role=button>c</ul>" +
            "<table role=presentation><tr><th id=h>h<td id=d aria-label=x>d</table><div id=out><ul role=none><li><p id=p>x</ul></div>");
        Assert.Equal(RoleOrigin.InheritedPresentational, map[doc.Root.QuerySelector("#a")].Origin);
        Assert.Equal("none", map[doc.Root.QuerySelector("#a")].ComputedRole);
        Assert.Equal("listitem", map[doc.Root.QuerySelector("#b")].ComputedRole); // focusable
        Assert.Equal("button", map[doc.Root.QuerySelector("#c")].ComputedRole); // its own role
        Assert.Equal(RoleOrigin.InheritedPresentational, map[doc.Root.QuerySelector("#h")].Origin);
        Assert.Equal("none", map[doc.Root.QuerySelector("#d")].ComputedRole);
        Assert.Equal("none", map[doc.Root.QuerySelector("table")].ComputedRole);
        Assert.Equal("none", map[doc.Root.QuerySelector("tbody")].ComputedRole);
        Assert.Equal("paragraph", map[doc.Root.QuerySelector("#p")].ComputedRole); // not an owned element
    }

    [Fact]
    public void Children_of_roles_with_presentational_children_are_presentational()
    {
        (OneHtml.Document doc, RoleMap map) = Compute("<button><span id=a><b id=b>x</b></span><a id=c href=x>l</a></button><div role=img><p id=d>x</p></div>");
        Assert.Equal(RoleOrigin.ChildrenPresentational, map[doc.Root.QuerySelector("#a")].Origin);
        Assert.Equal("none", map[doc.Root.QuerySelector("#b")].ComputedRole);
        Assert.Equal("link", map[doc.Root.QuerySelector("#c")].ComputedRole); // focusable keeps its role
        Assert.Equal("none", map[doc.Root.QuerySelector("#d")].ComputedRole);
    }

    [Theory]
    [InlineData("<select id=t>", "combobox")]
    [InlineData("<select id=t multiple>", "listbox")]
    [InlineData("<select id=t size=4>", "listbox")]
    [InlineData("<select id=t size=1>", "combobox")]
    [InlineData("<select id=t size=bogus>", "combobox")]
    [InlineData("<input id=t>", "textbox")]
    [InlineData("<input id=t type=TEXT>", "textbox")]
    [InlineData("<input id=t type=bogus>", "textbox")]
    [InlineData("<input id=t type=search>", "searchbox")]
    [InlineData("<input id=t type=number>", "spinbutton")]
    [InlineData("<input id=t type=range>", "slider")]
    [InlineData("<input id=t type=checkbox>", "checkbox")]
    [InlineData("<input id=t type=checkbox switch>", "switch")]
    [InlineData("<input id=t type=radio>", "radio")]
    [InlineData("<input id=t type=image>", "button")]
    [InlineData("<input id=t type=submit>", "button")]
    [InlineData("<input id=t type=password>", "textbox")]
    [InlineData("<input id=t type=hidden>", "")]
    [InlineData("<input id=t type=date>", "")]
    [InlineData("<input id=t list=dl><datalist id=dl></datalist>", "combobox")]
    [InlineData("<input id=t type=email list=dl><datalist id=dl></datalist>", "combobox")]
    [InlineData("<input id=t type=search list=dl><datalist id=dl></datalist>", "combobox")]
    [InlineData("<input id=t list=missing>", "textbox")]
    [InlineData("<input id=t list=dl><div id=dl></div>", "textbox")]
    [InlineData("<input id=t type=range list=dl><datalist id=dl></datalist>", "slider")]
    [InlineData("<datalist id=t>", "listbox")]
    [InlineData("<textarea id=t>", "textbox")]
    public void Form_controls(string html, string expected) => Assert.Equal(expected, Role(html, "#t"));

    [Fact]
    public void Header_footer_and_aside_depend_on_their_scope()
    {
        Assert.Equal("banner", Role("<header id=t>", "#t"));
        Assert.Equal("banner", Role("<div><header id=t></div>", "#t"));
        Assert.Equal("contentinfo", Role("<footer id=t>", "#t"));
        Assert.Equal("generic", Role("<article><header id=t></article>", "#t"));
        Assert.Equal("generic", Role("<main><footer id=t></main>", "#t"));
        Assert.Equal("generic", Role("<section aria-label=s><footer id=t></section>", "#t"));
        Assert.Equal("banner", Role("<article></article><header id=t>", "#t"));
        Assert.Equal("complementary", Role("<aside id=t>", "#t"));
        Assert.Equal("complementary", Role("<main><aside id=t></main>", "#t"));
        Assert.Equal("generic", Role("<main><article><aside id=t></article></main>", "#t"));
        Assert.Equal("complementary", Role("<article><aside id=t aria-label=x></article>", "#t"));
        // Without a name probe the unnamed role is reported, to be resolved by the name computation.
        NodeRole deferred = Of("<article><aside id=t aria-label=x></article>", "#t", named: false);
        Assert.Equal("generic", deferred.ComputedRole);
        Assert.Equal("complementary", deferred.RoleWhenNamed!.Name);
    }

    [Theory]
    [InlineData("<section id=t>", "generic")]
    [InlineData("<section id=t aria-label=x>", "region")]
    [InlineData("<form id=t>", "form")]
    [InlineData("<form id=t aria-label=x>", "form")]
    [InlineData("<div id=t role=region>", "generic")]
    [InlineData("<div id=t role='region group'>", "group")]
    [InlineData("<div id=t role=region aria-label=x>", "region")]
    [InlineData("<div id=t role=form>", "generic")]
    [InlineData("<img id=t alt=''>", "none")]
    [InlineData("<img id=t alt=' '>", "none")]
    [InlineData("<img id=t alt>", "none")]
    [InlineData("<img id=t alt='' aria-label=x>", "image")]
    [InlineData("<img id=t alt='' title=x>", "none")]
    [InlineData("<img id=t alt=x>", "image")]
    [InlineData("<img id=t>", "image")]
    public void Roles_that_depend_on_a_name(string html, string expected) => Assert.Equal(expected, Role(html, "#t"));

    [Theory]
    [InlineData("<table><tr><th id=t>", "columnheader")]
    [InlineData("<table><thead><tr><th id=t></table>", "columnheader")]
    [InlineData("<table><tr><th id=t><td>", "rowheader")]
    [InlineData("<table><tr><th id=t scope=col><td>", "columnheader")]
    [InlineData("<table><tr><th id=t scope=ROW>", "rowheader")]
    [InlineData("<table><tr><th id=t scope=rowgroup>", "rowheader")]
    [InlineData("<table><tr><td id=t>", "cell")]
    [InlineData("<table role=grid><tr><td id=t>", "gridcell")]
    [InlineData("<table role=treegrid><tr><td id=t>", "gridcell")]
    [InlineData("<table role=grid><tr><th id=t>", "columnheader")]
    [InlineData("<table role=grid><tr><td><table><tr><td id=t></table>", "cell")]
    [InlineData("<table><tr id=t>", "row")]
    [InlineData("<table><tbody id=t>", "rowgroup")]
    [InlineData("<table><caption id=t>", "caption")]
    public void Table_cells(string html, string expected) => Assert.Equal(expected, Role(html, "#t"));

    [Theory]
    [InlineData("<ul><li id=t>", "listitem")]
    [InlineData("<ol><li id=t>", "listitem")]
    [InlineData("<menu><li id=t>", "listitem")]
    [InlineData("<ul><div><li id=t></div></ul>", "listitem")]
    [InlineData("<div role=list><li id=t></div>", "listitem")]
    [InlineData("<ul role=menu><li id=t></ul>", "generic")]
    [InlineData("<div><li id=t></div>", "generic")]
    [InlineData("<select><option id=t>", "option")]
    [InlineData("<select><optgroup><option id=t>", "option")]
    [InlineData("<datalist><option id=t>", "option")]
    [InlineData("<div><option id=t></div>", "")]
    [InlineData("<a id=t href=x>", "link")]
    [InlineData("<a id=t>", "generic")]
    [InlineData("<map><area id=t href=x></map>", "link")]
    [InlineData("<details><summary id=t>", "")]
    [InlineData("<details><summary><summary id=t>", "generic")]
    [InlineData("<div><summary id=t></div>", "generic")]
    [InlineData("<svg id=t>", "graphics-document")]
    [InlineData("<svg><g id=t></g></svg>", "")]
    [InlineData("<math id=t>", "math")]
    [InlineData("<x-widget id=t>", "generic")]
    [InlineData("<h3 id=t>", "heading")]
    public void Other_contextual_mappings(string html, string expected) => Assert.Equal(expected, Role(html, "#t"));

    [Fact]
    public void Static_focusability()
    {
        Assert.True(Of("<a id=t href=x>", "#t").IsFocusable);
        Assert.False(Of("<a id=t>", "#t").IsFocusable);
        Assert.True(Of("<div id=t tabindex=-1>", "#t").IsFocusable);
        Assert.True(Of("<div id=t tabindex=' +3x'>", "#t").IsFocusable);
        Assert.False(Of("<div id=t tabindex=x>", "#t").IsFocusable);
        Assert.True(Of("<button id=t>", "#t").IsFocusable);
        Assert.False(Of("<button id=t disabled>", "#t").IsFocusable);
        Assert.False(Of("<fieldset disabled><button id=t></fieldset>", "#t").IsFocusable);
        Assert.True(Of("<fieldset disabled><legend><button id=t></legend></fieldset>", "#t").IsFocusable);
        Assert.False(Of("<fieldset disabled><legend></legend><legend><button id=t></legend></fieldset>", "#t").IsFocusable);
        Assert.False(Of("<input id=t type=hidden>", "#t").IsFocusable);
        Assert.True(Of("<div id=t contenteditable>", "#t").IsFocusable);
        Assert.True(Of("<details><summary id=t>", "#t").IsFocusable);
        Assert.False(Of("<details><summary><summary id=t>", "#t").IsFocusable);
        Assert.True(Of("<video id=t controls>", "#t").IsFocusable);
        Assert.False(Of("<div inert><button id=t></div>", "#t").IsFocusable);
        Assert.True(Of("<fieldset disabled><button id=t disabled></fieldset>", "#t").IsDisabled);
    }

    [Fact]
    public void Diagnostics_report_each_kind()
    {
        static RoleDiagnosticKind[] Kinds(string html) => [.. Of(html, "#t").Diagnostics.ToArray().Select(d => d.Kind)];

        Assert.Contains(RoleDiagnosticKind.InvalidRoleToken, Kinds("<div id=t role='foo button'>"));
        Assert.DoesNotContain(RoleDiagnosticKind.InvalidRoleToken, Kinds("<div id=t role='button foo'>")); // a fallback after the match
        Assert.Contains(RoleDiagnosticKind.AbstractRole, Kinds("<div id=t role='widget'>"));
        Assert.Contains(RoleDiagnosticKind.ProhibitedAttribute, Kinds("<div id=t aria-label=x>"));
        Assert.Contains(RoleDiagnosticKind.ProhibitedAttribute, Kinds("<p id=t aria-labelledby=t>"));
        Assert.Contains(RoleDiagnosticKind.ProhibitedAttribute, Kinds("<span id=t role=code aria-label=x>"));
        Assert.DoesNotContain(RoleDiagnosticKind.ProhibitedAttribute, Kinds("<button id=t aria-label=x>"));
        Assert.Contains(RoleDiagnosticKind.UnsupportedAttribute, Kinds("<div id=t role=button aria-checked=true>"));
        Assert.DoesNotContain(RoleDiagnosticKind.UnsupportedAttribute, Kinds("<div id=t role=checkbox aria-checked=true>"));
        Assert.DoesNotContain(RoleDiagnosticKind.UnsupportedAttribute, Kinds("<div id=t aria-live=polite>")); // global
        Assert.Contains(RoleDiagnosticKind.DeprecatedAttribute, Kinds("<div id=t aria-dropeffect=copy>"));
        Assert.Contains(RoleDiagnosticKind.DeprecatedAttribute, Kinds("<div id=t role=group aria-invalid=true>"));
        Assert.Contains(RoleDiagnosticKind.UnknownAttribute, Kinds("<div id=t aria-foo=x>"));
        Assert.Contains(RoleDiagnosticKind.InvalidAttributeValue, Kinds("<div id=t aria-live=loud>"));
        Assert.Contains(RoleDiagnosticKind.InvalidAttributeValue, Kinds("<div id=t role=checkbox aria-checked=yes>"));
        Assert.Contains(RoleDiagnosticKind.InvalidAttributeValue, Kinds("<div id=t role=heading aria-level=0>"));
        Assert.Contains(RoleDiagnosticKind.InvalidAttributeValue, Kinds("<div id=t role=heading aria-level=1.5>"));
        Assert.Contains(RoleDiagnosticKind.InvalidAttributeValue, Kinds("<div id=t role=slider aria-valuenow=abc>"));
        Assert.Contains(RoleDiagnosticKind.InvalidAttributeValue, Kinds("<div id=t role=button aria-haspopup='menu tree'>"));
        Assert.Contains(RoleDiagnosticKind.InvalidAttributeValue, Kinds("<div id=t aria-relevant='text bogus'>"));
        Assert.Contains(RoleDiagnosticKind.InvalidAttributeValue, Kinds("<div id=t aria-activedescendant='a b'>"));
        Assert.DoesNotContain(RoleDiagnosticKind.InvalidAttributeValue, Kinds("<div id=t aria-relevant='text additions' aria-busy=''>"));
        Assert.DoesNotContain(RoleDiagnosticKind.InvalidAttributeValue, Kinds("<div id=t role=slider aria-valuenow=-1.5e3>"));
        Assert.Contains(RoleDiagnosticKind.BrokenIdReference, Kinds("<div id=t aria-describedby='t missing'>"));
        Assert.DoesNotContain(RoleDiagnosticKind.BrokenIdReference, Kinds("<div id=t aria-describedby='t'>"));
        Assert.Contains(RoleDiagnosticKind.BrokenIdReference, Kinds("<div id=t role=listbox aria-activedescendant=nope>"));
        Assert.Contains(RoleDiagnosticKind.MissingRequiredAttribute, Kinds("<div id=t role=checkbox>"));
        Assert.Contains(RoleDiagnosticKind.MissingRequiredAttribute, Kinds("<div id=t role=heading>"));
        Assert.Contains(RoleDiagnosticKind.MissingRequiredAttribute, Kinds("<div id=t role=separator tabindex=0>"));
        Assert.DoesNotContain(RoleDiagnosticKind.MissingRequiredAttribute, Kinds("<div id=t role=separator>"));
        Assert.DoesNotContain(RoleDiagnosticKind.MissingRequiredAttribute, Kinds("<h2 id=t role=heading>")); // native level
        Assert.DoesNotContain(RoleDiagnosticKind.MissingRequiredAttribute, Kinds("<input id=t type=checkbox role=checkbox>"));
        Assert.DoesNotContain(RoleDiagnosticKind.MissingRequiredAttribute, Kinds("<input id=t type=checkbox role=switch>"));
        Assert.DoesNotContain(RoleDiagnosticKind.MissingRequiredAttribute, Kinds("<div id=t role=checkbox aria-checked=false>"));
    }

    [Fact]
    public void Diagnostic_details()
    {
        RoleDiagnostic d = Of("<div id=t role=checkbox aria-checked=maybe aria-foo>", "#t").Diagnostics.ToArray().First(x => x.Kind == RoleDiagnosticKind.InvalidAttributeValue);
        Assert.Equal("aria-checked", d.Attribute);
        Assert.Equal("maybe", d.Value);
        Assert.Empty(Of("<button id=t aria-pressed=true aria-expanded=false aria-controls=t>", "#t").Diagnostics.ToArray());
    }

    [Fact]
    public void Mapped_elements_without_a_node_have_no_result()
    {
        (OneHtml.Document doc, RoleMap map) = Compute("<p id=t>x<template><button id=inner></button></template>");
        Assert.False(map[doc.Root.QuerySelector("#t").FirstChild].IsComputed);
        Assert.True(map[doc.Root.QuerySelector("#t")].IsComputed);
        Assert.False(map[doc.Root.QuerySelector("template").TemplateContent.FirstChild].IsComputed); // inert template content
        Assert.Throws<System.ArgumentException>(() => map[OneHtml.Document.Parse("<p>").Root]);
    }

    [Fact]
    public void Elements_enumerate_in_document_order()
    {
        (OneHtml.Document _, RoleMap map) = Compute("<ul><li>a<li>b</ul>");
        Assert.Equal(["html", "head", "body", "ul", "li", "li"], map.Elements.Select(e => e.Node.LocalName));
    }

    [Fact]
    public void Implicit_properties_follow_the_attribute_tables()
    {
        (OneHtml.Document doc, _) = Compute("<h4 id=h></h4><input id=c type=checkbox checked required><input id=u type=checkbox disabled readonly>" +
            "<select id=s multiple required></select><textarea id=a placeholder=p></textarea><meter id=m min=1 max=9 value=3></meter>" +
            "<table><tr><td id=td colspan=3 rowspan=2></table><details open><summary id=sm></details><option id=o selected></option><input id=r type=range min=0 max=10 value=4>");
        string Props(string id) => string.Join(';', HtmlImplicitProperties.Get(doc.Root.QuerySelector("#" + id)).Select(p => p.Name + "=" + p.Value));
        Assert.Equal("aria-level=4", Props("h"));
        Assert.Equal("aria-checked=true;aria-required=true", Props("c"));
        Assert.Equal("aria-checked=false;aria-disabled=true;aria-readonly=true", Props("u"));
        Assert.Equal("aria-required=true;aria-multiselectable=true", Props("s"));
        Assert.Equal("aria-multiline=true;aria-placeholder=p", Props("a"));
        Assert.Equal("aria-valuemin=1;aria-valuemax=9;aria-valuenow=3", Props("m"));
        Assert.Equal("aria-colspan=3;aria-rowspan=2", Props("td"));
        Assert.Equal("aria-expanded=true", Props("sm"));
        Assert.Equal("aria-selected=true", Props("o"));
        Assert.Equal("aria-valuemin=0;aria-valuemax=10;aria-valuenow=4", Props("r"));
    }
}
