using System.Collections.Generic;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Css;

public class ValueTests
{
    private static ComponentValueList V(string css) => CssTree.ParseComponentValueList(css).Values;

    [Theory]
    [InlineData("calc(1px + 2px)", "calc(3px)")]
    [InlineData("calc(1in + 1px)", "calc(97px)")]
    [InlineData("calc(20px + 2em)", "calc(2em + 20px)")]
    [InlineData("calc(20px + 0%)", "calc(0% + 20px)")]
    [InlineData("calc(100% - 10px)", "calc(100% - 10px)")]
    [InlineData("calc(2 * (3px + 4%))", "calc(8% + 6px)")]
    [InlineData("calc(10px / 4)", "calc(2.5px)")]
    [InlineData("min(1px, 2px)", "calc(1px)")]
    [InlineData("max(10px, 5%)", "max(10px, 5%)")]
    [InlineData("clamp(1px, 5px, 3px)", "calc(3px)")]
    [InlineData("calc(infinity * 1px)", "calc(infinity * 1px)")]
    [InlineData("calc(0 / 0 * 1px)", "calc(NaN * 1px)")]
    [InlineData("round(up, 5px, 2px)", "calc(6px)")]
    [InlineData("mod(-7px, 3px)", "calc(2px)")]
    [InlineData("rem(-7px, 3px)", "calc(-1px)")]
    [InlineData("calc(sin(90deg) * 1px)", "calc(1px)")]
    [InlineData("calc(pi * 1px)", "calc(3.141592653589793px)")]
    public void Calc_length_percentage(string input, string expected)
    {
        Assert.True(CssQuantity.TryParse(V(input), QuantityKinds.LengthPercentage, out CssQuantity q), input);
        Assert.Equal(expected, q.ToCss());
    }

    [Theory]
    [InlineData("calc(1px + 1)")]
    [InlineData("calc(1px+2px)")]
    [InlineData("calc(1px + )")]
    [InlineData("calc()")]
    [InlineData("calc(1px * 2px)")]
    [InlineData("calc(1px,2px)")]
    [InlineData("min()")]
    [InlineData("calc(var(--x))")]
    [InlineData("sin(1px)")]
    public void Calc_invalid(string input)
    {
        bool ok = CssQuantity.TryParse(V(input), QuantityKinds.LengthPercentage | QuantityKinds.Number, out CssQuantity q);
        Assert.False(ok, input + " => " + q.ToCss());
    }

    [Fact]
    public void Calc_resolves_relative_units()
    {
        Assert.True(CssQuantity.TryParse(V("calc(50% + 2em)"), QuantityKinds.LengthPercentage, out CssQuantity q));
        LengthContext ctx = LengthContext.Default with { FontSize = 10 };
        Assert.Equal(120, q.Resolve(ctx, 200));
        Assert.True(CssQuantity.TryParse(V("10vw"), QuantityKinds.Length, out q));
        Assert.Equal(128, q.Resolve(LengthContext.Default with { ViewportWidth = 1280 }));
    }

    [Fact]
    public void Calc_deep_nesting_does_not_recurse()
    {
        string s = new string('(', 5000) + "1px" + new string(')', 5000);
        CssTree tree = CssTree.ParseComponentValueList("calc" + s, new CssParseOptions { MaxNestingDepth = 100000 });
        _ = CssQuantity.TryParse(tree.Values, QuantityKinds.Length, out _);
    }

    [Theory]
    [InlineData("inline", "inline")]
    [InlineData("block flow", "block")]
    [InlineData("inline flow-root", "inline-block")]
    [InlineData("block flex", "flex")]
    [InlineData("flex block", "flex")]
    [InlineData("inline flex", "inline-flex")]
    [InlineData("list-item", "list-item")]
    [InlineData("inline list-item", "inline list-item")]
    [InlineData("flow-root list-item", "flow-root list-item")]
    [InlineData("none", "none")]
    [InlineData("contents", "contents")]
    [InlineData("table-cell", "table-cell")]
    [InlineData("ruby", "ruby")]
    [InlineData("block ruby", "block ruby")]
    public void Display_valid(string input, string expected)
    {
        Assert.True(DisplayValue.TryParse(V(input), out DisplayValue d), input);
        Assert.Equal(expected, d.ToCss());
    }

    [Theory]
    [InlineData("block block")]
    [InlineData("flex grid")]
    [InlineData("list-item flex")]
    [InlineData("none block")]
    [InlineData("foo")]
    [InlineData("")]
    public void Display_invalid(string input) => Assert.False(DisplayValue.TryParse(V(input), out _));

    [Fact]
    public void Content_parses_alt_text_and_attr()
    {
        Assert.True(ContentValue.TryParse(V("\"\\201C\" attr(title) counter(item, decimal) open-quote / \"quote\""), out ContentValue? c));
        Assert.Equal(4, c!.Items.Count);
        Assert.Equal(ContentItemKind.Attr, c.Items[1].Kind);
        Assert.Equal("title", c.Items[1].Text);
        Assert.Equal(ContentItemKind.Counter, c.Items[2].Kind);
        Assert.Equal("quote", c.AlternativeText[0].Text);
        Assert.True(ContentValue.TryParse(V("none"), out c));
        Assert.True(c!.IsNone);
        Assert.False(ContentValue.TryParse(V("\"a\" /"), out _));
        Assert.False(ContentValue.TryParse(V("foo"), out _));
        Assert.True(ContentValue.TryParse(V("url(x.png) / \"alt\""), out c));
        Assert.Equal(ContentItemKind.Image, c!.Items[0].Kind);
    }

    [Fact]
    public void Typed_properties()
    {
        Assert.True(TypedProperties.TryParseOpacity(V("50%"), out double o));
        Assert.Equal(0.5, o);
        Assert.True(TypedProperties.TryParseBackgroundImage(V("none, url(a.png), linear-gradient(red, blue)"), out ImageKind[] layers));
        Assert.Equal([ImageKind.None, ImageKind.Url, ImageKind.Gradient], layers);
        Assert.True(TypedProperties.TryParseClipPath(V("circle(50%) border-box"), out ClipPathKind cp));
        Assert.Equal(ClipPathKind.Shape, cp);
        Assert.True(TypedProperties.TryParseClip(V("rect(0, 5px, auto, 0)"), out ClipValue clip));
        Assert.Equal("rect(0px, 5px, auto, 0px)", clip.ToCss());
        Assert.True(TypedProperties.TryParseFontFamily(V("\"Helvetica Neue\", Arial, sans-serif"), out FontFamilyValue? ff));
        Assert.Equal(["Helvetica Neue", "Arial", "sans-serif"], ff!.Names);
        Assert.True(TypedProperties.TryParseFontWeight(V("bolder"), out FontWeightValue? fw));
        Assert.Equal(700, fw!.Resolve(400));
        Assert.Equal(900, fw.Resolve(600));
        Assert.True(TypedProperties.TryParseFontSize(V("1.5em"), out FontSizeValue? fs));
        Assert.Equal(24, fs!.ResolvePx(LengthContext.Default));
        Assert.True(TypedProperties.TryParseSpeakAs(V("spell-out digits no-punctuation"), out SpeakAsValue sa));
        Assert.True(sa.SpellOut && sa.Digits);
        Assert.False(TypedProperties.TryParseVisibility(V("none"), out _));
        Assert.True(TypedProperties.TryParsePointerEvents(V("VisiblePainted"), out PointerEventsValue pe));
        Assert.Equal(PointerEventsValue.VisiblePainted, pe);
    }

    [Fact]
    public void Wide_keywords()
    {
        Assert.Equal(CssWideKeyword.RevertLayer, CssWideKeywords.Detect(V(" Revert-Layer ")));
        Assert.Equal(CssWideKeyword.None, CssWideKeywords.Detect(V("initial initial")));
        Assert.Equal(CssWideKeyword.None, CssWideKeywords.Detect(V("red")));
    }

    [Fact]
    public void Colors_convert_and_resolve()
    {
        Assert.True(CssColor.TryParse("oklch(0.5 0.1 200)", out CssColor c));
        (byte r, byte g, byte b, double a) = c.ToRgba8();
        Assert.True(g > r && b > r && a == 1);
        Assert.True(CssColor.TryParse("color-mix(in srgb, red, blue)", out c));
        Assert.Equal("color(srgb 0.5 0 0.5)", c.ToCss());
        Assert.True(CssColor.TryParse("color-mix(in srgb, red 25%, blue)", out c));
        Assert.Equal("color(srgb 0.25 0 0.75)", c.ToCss());
        Assert.True(CssColor.TryParse("rgb(from red r g b / 50%)", out c));
        Assert.Equal("color(srgb 1 0 0 / 0.5)", c.ToCss());
        Assert.True(CssColor.TryParse("rgb(from rgb(10 20 30) calc(r * 2) g b)", out c));
        Assert.Equal("color(srgb 0.078431 0.078431 0.117647)", c.ToCss());
        Assert.True(CssColor.TryParse("light-dark(white, black)", out c));
        Assert.Equal("rgb(255, 255, 255)", c.ToCss());
        Assert.True(CssColor.TryParse("light-dark(white, black)", out c, new ColorParseOptions { DarkScheme = true }));
        Assert.Equal("rgb(0, 0, 0)", c.ToCss());
        Assert.True(CssColor.TryParse("color(display-p3 1 0 0)", out c));
        (double sr, _, _, _) = c.ToSrgb();
        Assert.Equal(1, sr);
        Assert.True(CssColor.TryParse("CurrentColor", out c));
        Assert.True(c.IsCurrentColor);
        Assert.True(CssColor.TryParse("Canvas", out c));
        Assert.Equal("canvas", c.ToCss());
        Assert.False(CssColor.TryParse("rgb(1 2)", out _));
        Assert.False(CssColor.TryParse("#12", out _));
    }

    [Fact]
    public void Colors_nesting_is_bounded()
    {
        string s = "red";
        for (int i = 0; i < 40; i++)
        {
            s = "color-mix(in srgb, " + s + ", blue)";
        }

        Assert.False(CssColor.TryParse(s, out _));
    }
}

public class ShorthandTests
{
    private static ComponentValueList V(string css) => CssTree.ParseComponentValueList(css).Values;

    private static Dictionary<string, string> Expand(string prop, string value, ShorthandStatus expected = ShorthandStatus.Expanded)
    {
        ShorthandExpansion e = Shorthands.Expand(prop, V(value));
        Assert.Equal(expected, e.Status);
        var d = new Dictionary<string, string>();
        foreach (ExpandedLonghand l in e.Longhands)
        {
            d[l.Name] = l.Keyword != CssWideKeyword.None ? CssWideKeywords.ToCss(l.Keyword) : l.Value.Raw.ToString();
        }

        return d;
    }

    [Fact]
    public void Margin_and_padding()
    {
        var d = Expand("margin", "1px 2px 3px");
        Assert.Equal("1px", d["margin-top"]);
        Assert.Equal("2px", d["margin-right"]);
        Assert.Equal("3px", d["margin-bottom"]);
        Assert.Equal("2px", d["margin-left"]);
        Expand("margin", "1px 2px 3px 4px 5px", ShorthandStatus.Invalid);
        Expand("padding", "-1px", ShorthandStatus.Invalid);
        Assert.Equal("auto", Expand("margin-inline", "auto")["margin-inline-end"]);
        Assert.Equal("5%", Expand("inset", "5%")["left"]);
    }

    [Fact]
    public void Border_expands_to_leaves()
    {
        var d = Expand("border", "2px solid red");
        Assert.Equal("2px", d["border-top-width"]);
        Assert.Equal("solid", d["border-left-style"]);
        Assert.Equal("red", d["border-bottom-color"]);
        Assert.Equal("initial", d["border-image-source"]);
        Assert.Equal("blue", Expand("border-top", "blue")["border-top-color"]);
        Assert.Equal("initial", Expand("border-top", "blue")["border-top-width"]);
        Expand("border", "2px 3px", ShorthandStatus.Invalid);
        Assert.Equal("solid", Expand("border-style", "dashed solid")["border-right-style"]);
    }

    [Fact]
    public void Outline_overflow_decoration_list_style()
    {
        var d = Expand("outline", "auto 2px");
        Assert.Equal("auto", d["outline-style"]);
        Assert.Equal("2px", d["outline-width"]);
        var o = Expand("overflow", "hidden auto");
        Assert.Equal("hidden", o["overflow-x"]);
        Assert.Equal("auto", o["overflow-y"]);
        var t = Expand("text-decoration", "underline overline wavy red");
        Assert.Equal("underline overline", t["text-decoration-line"]);
        Assert.Equal("wavy", t["text-decoration-style"]);
        var l = Expand("list-style", "none inside");
        Assert.Equal("none", l["list-style-type"]);
        Assert.Equal("none", l["list-style-image"]);
        Assert.Equal("inside", l["list-style-position"]);
        Assert.Equal("square", Expand("list-style", "square")["list-style-type"]);
    }

    [Fact]
    public void Font()
    {
        var d = Expand("font", "italic small-caps bold 12px/1.5 \"Helvetica Neue\", Arial, sans-serif");
        Assert.Equal("italic", d["font-style"]);
        Assert.Equal("bold", d["font-weight"]);
        Assert.Equal("12px", d["font-size"]);
        Assert.Equal("1.5", d["line-height"]);
        Assert.Equal("\"Helvetica Neue\", Arial, sans-serif", d["font-family"]);
        Assert.Equal("small-caps", d["font-variant-caps"]);
        Assert.Equal("initial", d["font-kerning"]);
        Expand("font", "12px", ShorthandStatus.Invalid);
        Expand("font", "bold", ShorthandStatus.Invalid);
        Assert.Equal("large", Expand("font", "large serif")["font-size"]);
    }

    [Fact]
    public void Background()
    {
        var d = Expand("background", "url(a.png) no-repeat center / cover, #fff");
        Assert.Equal("url(a.png), none", d["background-image"]);
        Assert.Equal("no-repeat, repeat", d["background-repeat"]);
        Assert.Equal("center, 0%", d["background-position-x"]);
        Assert.Equal("cover, auto", d["background-size"]);
        Assert.Equal("#fff", d["background-color"]);
        var e = Expand("background", "red");
        Assert.Equal("red", e["background-color"]);
        Assert.Equal("initial", e["background-image"]);
        Assert.Equal("left", Expand("background", "left 10px top 20px")["background-position-x"].Split(' ')[0]);
        Expand("background", "red blue", ShorthandStatus.Invalid);
        var p = Expand("background-position", "center top 10px");
        Assert.Equal("center", p["background-position-x"]);
        Assert.Equal("top 10px", p["background-position-y"]);
        Expand("background-position", "top 10px", ShorthandStatus.Invalid);
    }

    [Fact]
    public void Transition_and_animation()
    {
        var d = Expand("transition", "opacity 1s ease-in 2s, color .5s");
        Assert.Equal("opacity, color", d["transition-property"]);
        Assert.Equal("1s, .5s", d["transition-duration"]);
        Assert.Equal("2s, 0s", d["transition-delay"]);
        var a = Expand("animation", "spin 2s infinite alternate");
        Assert.Equal("spin", a["animation-name"]);
        Assert.Equal("infinite", a["animation-iteration-count"]);
        Assert.Equal("alternate", a["animation-direction"]);
        Assert.Equal("none", Expand("animation", "none 1s")["animation-fill-mode"]);
    }

    [Fact]
    public void Wide_keyword_var_and_unknown()
    {
        var d = Expand("margin", "inherit");
        Assert.Equal("inherit", d["margin-left"]);
        Expand("margin", "var(--x)", ShorthandStatus.PendingSubstitution);
        Expand("grid", "auto / auto", ShorthandStatus.NotExpanded);
        Expand("color", "red", ShorthandStatus.NotShorthand);
        Assert.True(Shorthands.HasGrammar("background"));
        Assert.False(Shorthands.HasGrammar("grid"));
    }
}

public class VarTests
{
    private static ComponentValueList V(string css) => CssTree.ParseComponentValueList(css).Values;

    private static Dictionary<string, string> Resolve(params (string Name, string Value)[] props)
    {
        var d = new Dictionary<string, ComponentValueList>();
        foreach ((string n, string v) in props)
        {
            d[n] = V(v);
        }

        return VarSubstitution.ResolveCustomProperties(d);
    }

    [Fact]
    public void Substitutes_with_fallbacks()
    {
        Dictionary<string, string> m = Resolve(("--a", "10px"), ("--b", "var(--a) solid"), ("--c", "var(--missing, 3em)"));
        Assert.Equal("10px", m["--a"]);
        Assert.Equal("10px solid", m["--b"]);
        Assert.Equal("3em", m["--c"].Trim());
        SubstitutionResult r = VarSubstitution.Substitute(V("calc(var(--a) + var(--zz, 1px))"), m);
        Assert.True(r.IsValid);
        Assert.Equal("calc(10px + 1px)", r.Text.Replace("/**/", "").Replace("  ", " "));
        Assert.Equal(SubstitutionStatus.InvalidAtComputedValueTime, VarSubstitution.Substitute(V("var(--zz)"), m).Status);
        Assert.Equal(SubstitutionStatus.Syntax, VarSubstitution.Substitute(V("var(x)"), m).Status);
        Assert.True(VarSubstitution.Substitute(V("var(--zz, var(--a))"), m).IsValid);
    }

    [Fact]
    public void Cycles_are_invalid()
    {
        Dictionary<string, string> m = Resolve(("--a", "var(--b)"), ("--b", "var(--a)"), ("--c", "var(--a, red)"), ("--s", "var(--s)"), ("--ok", "1"));
        Assert.DoesNotContain("--a", m.Keys);
        Assert.DoesNotContain("--b", m.Keys);
        Assert.DoesNotContain("--s", m.Keys);
        Assert.Equal("red", m["--c"].Trim());
        Assert.Equal("1", m["--ok"]);
    }

    [Fact]
    public void Adjacent_tokens_do_not_fuse()
    {
        Dictionary<string, string> m = Resolve(("--n", "5"));
        SubstitutionResult r = VarSubstitution.Substitute(V("var(--n)px"), m);
        Assert.True(r.IsValid);
        Assert.Contains("/**/", r.Text, System.StringComparison.Ordinal);
        Assert.False(CssQuantity.TryParse(r.Value, QuantityKinds.Length, out _));
    }

    [Fact]
    public void Billion_laughs_fails_fast()
    {
        var props = new List<(string, string)> { ("--l0", "lol lol lol lol lol lol lol lol lol lol") };
        for (int i = 1; i <= 40; i++)
        {
            string p = "var(--l" + (i - 1) + ")";
            props.Add(("--l" + i, string.Join(' ', p, p, p, p, p, p, p, p, p, p)));
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Dictionary<string, string> m = Resolve([.. props]);
        Assert.True(m.ContainsKey("--l2"));
        Assert.DoesNotContain("--l40", m.Keys);
        SubstitutionResult r = VarSubstitution.Substitute(V("var(--l40)"), m);
        Assert.Equal(SubstitutionStatus.InvalidAtComputedValueTime, r.Status);
        Assert.True(sw.ElapsedMilliseconds < 5000, sw.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void Empty_expansion_bomb_is_bounded_by_steps()
    {
        // No output at all, yet exponential work without a step budget: --e(i) = var(--e(i-1)) var(--e(i-1)) ... with an empty base.
        var d = new Dictionary<string, ComponentValueList> { ["--e0"] = V("") };
        for (int i = 1; i <= 60; i++)
        {
            string p = "var(--e" + (i - 1) + ", ) var(--e" + (i - 1) + ", )";
            d["--e" + i] = V(p);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Dictionary<string, string> m = VarSubstitution.ResolveCustomProperties(d);
        Assert.True(m.ContainsKey("--e60"));
        Assert.True(sw.ElapsedMilliseconds < 5000);
    }

    [Fact]
    public void Fallback_bomb_hits_the_limit()
    {
        // Nested fallbacks multiply: var(--x, var(--x, ...)) is linear, but repeating it gives a long chain of steps.
        string inner = "1";
        for (int i = 0; i < 200; i++)
        {
            inner = "var(--nope" + i + ", " + inner + ")";
        }

        SubstitutionResult r = VarSubstitution.Substitute(V(inner), new Dictionary<string, string>());
        Assert.True(r.IsValid);
        Assert.Equal("1", r.Text.Trim());
        var tiny = new VarLimits { MaxSteps = 50 };
        Assert.Equal(SubstitutionStatus.LimitExceeded, VarSubstitution.Substitute(V(inner), new Dictionary<string, string>(), tiny).Status);
    }
}

public class MediaAndSupportsTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("screen", true)]
    [InlineData("print", false)]
    [InlineData("not print", true)]
    [InlineData("only screen and (min-width: 800px)", true)]
    [InlineData("(max-width: 800px)", false)]
    [InlineData("(width >= 1000px)", true)]
    [InlineData("(500px < width < 1500px)", true)]
    [InlineData("(1500px > width)", true)]
    [InlineData("(prefers-reduced-motion: reduce)", false)]
    [InlineData("(prefers-reduced-motion: no-preference)", true)]
    [InlineData("(prefers-color-scheme: dark), (hover)", true)]
    [InlineData("(scripting: none)", true)]
    [InlineData("(scripting)", false)]
    [InlineData("(unknown-feature: 1)", false)]
    [InlineData("not (unknown-feature: 1)", false)]
    [InlineData("not ((width: 5px) or (height: 5px))", true)]
    [InlineData("(orientation: landscape) and (resolution >= 1dppx)", true)]
    [InlineData("(aspect-ratio: 16/9)", true)]
    [InlineData("(min-aspect-ratio: 2/1)", false)]
    [InlineData("tv", false)]
    [InlineData("color)", false)]
    [InlineData("(forced-colors: active)", false)]
    [InlineData("(color)", true)]
    [InlineData("(min-width: calc(1px + 2px))", true)]
    [InlineData("(width: 1280px) and (height: 720px)", true)]
    [InlineData("(min-width: 40em)", true)]
    public void Evaluates_against_the_default_context(string query, bool expected) =>
        Assert.Equal(expected, MediaQueryList.Parse(query).Matches(StyleContext.Default));

    [Fact]
    public void Evaluates_against_other_contexts()
    {
        var dark = StyleContext.Default.WithMedia(MediaFeatures.Default with { ColorScheme = PrefersColorScheme.Dark, ReducedMotion = PrefersReduced.Reduce, Width = 400, Height = 800, ForcedColors = true });
        Assert.True(MediaQueryList.Parse("(prefers-color-scheme: dark) and (prefers-reduced-motion) and (forced-colors: active)").Matches(dark));
        Assert.True(MediaQueryList.Parse("(orientation: portrait) and (max-width: 400px)").Matches(dark));
        var print = MediaFeatures.Default with { Type = MediaType.Print };
        Assert.True(MediaQueryList.Parse("print and (overflow-block: paged)").Matches(print));
    }

    [Theory]
    [InlineData("(color", "(color)")]
    [InlineData(" ( color  )  ", "(color)")]
    [InlineData("color)", "not all")]
    [InlineData("all,all", "all, all")]
    [InlineData(",", "not all, not all")]
    [InlineData(" foo,", "foo, not all")]
    [InlineData("(min-resolution: calc(1x))", "(min-resolution: calc(1dppx))")]
    [InlineData("(width >= 400px)", "(width >= 400px)")]
    [InlineData("(400px <= width <= 800px)", "(400px <= width <= 800px)")]
    [InlineData("NOT PRINT AND (COLOR)", "not print and (color)")]
    public void Serializes(string input, string expected) => Assert.Equal(expected, MediaQueryList.Parse(input).ToCss());

    [Theory]
    [InlineData("(display: grid)", true)]
    [InlineData("(display: foobar)", false)]
    [InlineData("(foo: bar)", false)]
    [InlineData("(--custom: anything)", true)]
    [InlineData("not (display: foobar)", true)]
    [InlineData("(display: grid) and (color: red)", true)]
    [InlineData("(display: grid) and (display: nope)", false)]
    [InlineData("(display: nope) or (color: red)", true)]
    [InlineData("(display: grid !important)", true)]
    [InlineData("(margin: 1px 2px)", true)]
    [InlineData("(margin: 1px 2px 3px 4px 5px)", false)]
    [InlineData("(color: notacolor)", false)]
    [InlineData("(width: auto)", true)]
    [InlineData("(width: -5px)", true)]
    [InlineData("(padding-top: -5px)", false)]
    [InlineData("(width: var(--w))", true)]
    [InlineData("selector(a > b)", true)]
    [InlineData("selector(a, b)", false)]
    [InlineData("font-format(woff2)", true)]
    [InlineData("font-tech(variations)", true)]
    [InlineData("at-rule(@media)", true)]
    [InlineData("at-rule(@bogus)", false)]
    [InlineData("display: flex", true)]
    [InlineData("unknown-thing(x)", false)]
    [InlineData("", false)]
    [InlineData("(display: grid) and", false)]
    public void Supports(string condition, bool expected) => Assert.Equal(expected, SupportsCondition.Parse(condition).Evaluate());

    [Fact]
    public void Supports_selector_hook()
    {
        var opts = new SupportsOptions { SelectorSupported = s => s.StartsWith('a') };
        Assert.True(SupportsCondition.Parse("selector(a:hover)", opts).Evaluate());
        Assert.False(SupportsCondition.Parse("selector(b)", opts).Evaluate());
    }
}

public class PropertyTableTests
{
    [Fact]
    public void Table_has_the_expected_shape()
    {
        Assert.True(PropertyTable.Count > 800);
        Assert.True(PropertyTable.TryGet("Background-Color", out PropertyInfo? bg));
        Assert.Equal(PropertyInheritance.No, bg.Inherited);
        Assert.Equal("transparent", bg.InitialText);
        Assert.Equal("transparent", bg.InitialValue.Raw.ToString());
        Assert.True(PropertyTable.Find("color")!.Inherited == PropertyInheritance.Yes);
        PropertyInfo margin = PropertyTable.Find("margin")!;
        Assert.True(margin.IsShorthand);
        Assert.Equal(["margin-top", "margin-right", "margin-bottom", "margin-left"], margin.Longhands);
        Assert.Contains("margin", PropertyTable.GetShorthandsOf("margin-top"));
        Assert.Equal(PropertyInitialKind.SeeLonghands, PropertyTable.Find("font")!.InitialKind);
        Assert.Equal("-webkit-box-shadow", PropertyTable.Find("-webkit-box-shadow")!.Name);
        Assert.Equal("box-shadow", PropertyTable.ResolveAlias(PropertyTable.Find("-webkit-box-shadow")!).Name);
        Assert.Contains(PropertyTable.GetLogicalGroup("margin"), p => p.Name == "margin-inline-start" && p.IsLogical);
        Assert.Contains(PropertyTable.GetLogicalGroup("margin"), p => p.Name == "margin-top" && !p.IsLogical);
        Assert.False(PropertyTable.TryGet("--x", out _));
    }

    [Fact]
    public void Initial_values_parse_for_every_value_property()
    {
        foreach (PropertyInfo p in PropertyTable.All)
        {
            if (p.InitialKind == PropertyInitialKind.Value)
            {
                Assert.False(p.InitialValue.IsEmpty, p.Name);
            }
        }
    }

    [Fact]
    public void Accessibility_properties_are_typed_with_valid_initial_values()
    {
        string[] names = ["display", "visibility", "color", "background-color", "opacity", "font-size", "font-weight", "font-style", "position", "top", "overflow-x", "pointer-events", "clip", "speak-as"];
        foreach (string n in names)
        {
            PropertyInfo p = PropertyTable.Find(n)!;
            if (p.InitialKind == PropertyInitialKind.Value)
            {
                Assert.True(DeclarationSupport.IsSupported(n, p.InitialValue), $"{n}: {p.InitialText}");
            }
        }
    }
}
