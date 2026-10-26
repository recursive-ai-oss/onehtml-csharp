using System;
using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

public class CascadeTests
{
    internal static DocumentStyles Styles(string html, StyleContext? ctx = null, StyleSourceOptions? opts = null) =>
        DocumentStyles.Create(Document.Parse(html), ctx, opts);

    internal static string Color(string html, string id, string property = "color")
    {
        DocumentStyles s = Styles(html);
        return s.GetStyle(s.Document.GetElementById(id)).GetPropertyValue(property);
    }

    [Fact]
    public void Specificity_and_order()
    {
        const string h = "<style>p{color:red} #a{color:blue} p.c{color:green} p{color:yellow}</style><p id=a class=c>x</p><p id=b>y</p><p id=d class=c>z</p>";
        Assert.Equal("blue", Color(h, "a"));
        Assert.Equal("yellow", Color(h, "b"));
        Assert.Equal("green", Color(h, "d"));
    }

    [Fact]
    public void Important_and_inline_style()
    {
        const string h = "<style>#a{color:red !important} #b{color:red}</style><p id=a style='color:blue'>x</p><p id=b style='color:blue'>y</p><p id=c style='color:blue !important'>z</p>";
        Assert.Equal("red", Color(h, "a"));
        Assert.Equal("blue", Color(h, "b"));
        Assert.Equal("blue", Color(h, "c"));
    }

    [Fact]
    public void Inheritance_and_keywords()
    {
        const string h = "<style>div{color:red; border-top-color:green} span{border-top-color:inherit} i{color:initial} b{color:unset} u{border-top-color:unset}</style><div id=d><span id=s>a</span><i id=i>b</i><b id=b>c</b><u id=u>d</u></div>";
        Assert.Equal("red", Color(h, "s"));
        Assert.Equal("green", Color(h, "s", "border-top-color"));
        Assert.Equal("CanvasText", Color(h, "i"));
        Assert.Equal("red", Color(h, "b"));
        Assert.Equal("currentcolor", Color(h, "u", "border-top-color"));
    }

    [Fact]
    public void Layers_order_and_importance()
    {
        const string h = "<style>@layer a, b; @layer b { #x { color: green } } @layer a { #x { color: red } } #y { color: green } @layer a { #y { color: red } } #z{color:red !important} @layer a{ #z{color:green !important}}</style><p id=x></p><p id=y></p><p id=z></p>";
        Assert.Equal("green", Color(h, "x"));
        Assert.Equal("green", Color(h, "y"));
        Assert.Equal("green", Color(h, "z"));
    }

    [Fact]
    public void Revert_layer_and_revert()
    {
        const string h = "<style>@layer { #t { color: green; } } @layer { #t { color: red; color: revert-layer; } } #u{color:red} #v { display: revert }</style><p id=t>t</p><p id=u>u</p><p id=v>v</p>";
        Assert.Equal("green", Color(h, "t"));
        DocumentStyles s = Styles(h);
        Assert.Equal("block", s.GetStyle(s.Document.GetElementById("v")).GetPropertyValue("display"));
    }

    [Fact]
    public void Custom_properties_and_var()
    {
        const string h = "<style>:root{--c: green; --a: var(--b); --b: var(--a)} div{color: var(--c); background-color: var(--a, blue); margin: var(--m, 3px 4px)}</style><div id=d><p id=p style='--c: red; color: var(--c)'>x</p></div>";
        DocumentStyles s = Styles(h);
        Assert.Equal("green", s.GetStyle(s.Document.GetElementById("d")).GetPropertyValue("color"));
        Assert.Equal("blue", s.GetStyle(s.Document.GetElementById("d")).GetPropertyValue("background-color"));
        Assert.Equal("3px", s.GetStyle(s.Document.GetElementById("d")).GetPropertyValue("margin-top"));
        Assert.Equal("4px", s.GetStyle(s.Document.GetElementById("d")).GetPropertyValue("margin-left"));
        Assert.Equal("red", s.GetStyle(s.Document.GetElementById("p")).GetPropertyValue("color"));
    }

    [Fact]
    public void Nesting_and_media_and_supports()
    {
        const string h = "<style>.a { color: red; & .b { color: green } .c & { color: blue } @media (min-width: 100px) { color: orange } } @media (max-width: 10px) { .a { color: pink } } @supports (display: grid) { .d { color: teal } } @supports not (display: grid) { .d { color: gray } }</style><div class=a id=a><span class=b id=b></span></div><div class=c><div class=a id=a2></div></div><i class=d id=d></i>";
        Assert.Equal("orange", Color(h, "a"));
        Assert.Equal("green", Color(h, "b"));
        Assert.Equal("blue", Color(h, "a2"));
        Assert.Equal("teal", Color(h, "d"));
    }

    [Fact]
    public void Logical_properties_compete_with_physical()
    {
        const string h = "<style>#a{margin-left: 1px; margin-inline-start: 2px} #b{margin-inline-start: 2px; margin-left: 1px} #c{direction: rtl; margin-inline-start: 5px}</style><p id=a></p><p id=b></p><p id=c></p>";
        Assert.Equal("2px", Color(h, "a", "margin-left"));
        Assert.Equal("1px", Color(h, "b", "margin-left"));
        Assert.Equal("5px", Color(h, "c", "margin-right"));
    }

    [Fact]
    public void Ua_sheet_hides_and_presentational_hint()
    {
        const string h = "<body><p id=a hidden>x</p><font id=f color=red size=5>y</font><table bgcolor=00ff00 id=t></table>";
        DocumentStyles s = Styles(h);
        Assert.Equal("none", s.GetStyle(s.Document.GetElementById("a")).GetPropertyValue("display"));
        Assert.Equal("block", s.GetStyle(s.Document.GetElementById("a").ParentElement).GetPropertyValue("display"));
        Assert.Equal("red", s.GetStyle(s.Document.GetElementById("f")).GetPropertyValue("color"));
        Assert.Equal(24, s.GetStyle(s.Document.GetElementById("f")).FontSizePx);
        Assert.Equal("#00ff00", s.GetStyle(s.Document.GetElementById("t")).GetPropertyValue("background-color"));
    }

    [Fact]
    public void Typed_values_and_sharing()
    {
        const string h = "<style>html{font-size: 20px} div{font-size: 1.5em; font-weight: bolder; color: #336699; opacity:.5}</style><div id=a><div id=b></div></div><p>1</p><p>2</p><p>3</p>";
        DocumentStyles s = Styles(h);
        ComputedStyle a = s.GetStyle(s.Document.GetElementById("a"));
        ComputedStyle b = s.GetStyle(s.Document.GetElementById("b"));
        Assert.Equal(30, a.FontSizePx);
        Assert.Equal(45, b.FontSizePx);
        Assert.Equal(700, a.FontWeight);
        Assert.Equal(900, b.FontWeight);
        Assert.Equal(0.5, a.Opacity);
        Assert.Equal((0x33, 0x66, 0x99), (a.Color.ToRgba8().R, a.Color.ToRgba8().G, a.Color.ToRgba8().B));
        var ps = s.Document.Root.QuerySelectorAll("p").Select(p => s.GetStyle(p)).ToList();
        Assert.Same(ps[0], ps[1]);
        Assert.Same(ps[1], ps[2]);
    }

    [Fact]
    public void Link_import_and_missing()
    {
        var opts = new StyleSourceOptions
        {
            BaseUrl = "https://example.test/dir/page.html",
            Resolver = url => url switch
            {
                "https://example.test/dir/a.css" => new StylesheetResource("@import 'b.css' layer(L); p{color:red}"),
                "https://example.test/dir/b.css" => new StylesheetResource("p{color:green} @import 'a.css';"),
                _ => null,
            },
        };
        DocumentStyles s = Styles("<link rel=stylesheet href=a.css><link rel=stylesheet href=missing.css><p id=p>", null, opts);
        // The layered import loses to the unlayered rule of a.css.
        Assert.Equal("red", s.GetStyle(s.Document.GetElementById("p")).GetPropertyValue("color"));
        Assert.Contains(s.Diagnostics, d => d.Kind == StyleDiagnosticKind.MissingStylesheet);
    }

    [Fact]
    public void Focus_overlay_matches_full_recompute()
    {
        const string h = "<style>a{color:blue} a:focus-visible{color:red} form:focus-within{border-top-color:green} div:has(a:focus){background-color:yellow} a:hover{color:orange} input:hover{color:pink}</style><div id=d><form id=f><a id=a href=#>x</a><input id=i><label id=l for=i>L</label></form></div>";
        DocumentStyles baseStyles = Styles(h);
        Document doc = baseStyles.Document;
        StyleContext ctx = StyleContext.Default.WithFocus(doc.GetElementById("a"));
        DocumentStyles overlay = baseStyles.WithContext(ctx);
        DocumentStyles full = DocumentStyles.Create(doc, ctx);
        foreach (Node e in doc.Root.DescendantElements)
        {
            Assert.Null(overlay.GetStyle(e).FirstDifference(full.GetStyle(e)));
        }

        Assert.Equal("red", overlay.GetStyle(doc.GetElementById("a")).GetPropertyValue("color"));
        Assert.Equal("blue", baseStyles.GetStyle(doc.GetElementById("a")).GetPropertyValue("color"));
        Assert.Equal("yellow", overlay.GetStyle(doc.GetElementById("d")).GetPropertyValue("background-color"));
        Assert.Contains(doc.GetElementById("f").Index, overlay.ChangedElements);

        // Label hover reaches the control.
        DocumentStyles hover = baseStyles.WithContext(StyleContext.Default.WithHover(doc.GetElementById("l")));
        Assert.Equal("pink", hover.GetStyle(doc.GetElementById("i")).GetPropertyValue("color"));
    }
}
