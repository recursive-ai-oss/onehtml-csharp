using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Style;

/// <summary>
/// The declaration validator hook. The validator below is written like an outside library's grammar would be: it uses only
/// the public <see cref="TokenRange"/> and <see cref="StyleSourceOptions"/>.
/// </summary>
public class DeclarationValidatorTests
{
    /// <summary>width, min-width, max-width: <c>auto</c>, a non-negative length or percentage, or a calc(). Everything else is accepted.</summary>
    private static bool Grammar(string property, TokenRange v, StyleOrigin origin)
    {
        switch (property)
        {
            case "width":
            case "min-width":
            case "max-width":
                TokenRange r = v.Trim();
                int i = r.From;
                if (r.IsFunction(i, "calc"))
                {
                    return r.Next(i) == r.To;
                }

                if (r.IsIdent(i, "auto") || (property != "width" && r.IsIdent(i, "none")))
                {
                    return r.Next(i) == r.To;
                }

                CssTokenKind k = r.Kind(i);
                bool numeric = k == CssTokenKind.Percentage || (k == CssTokenKind.Dimension && r.TryUnit(i, out _)) || (k == CssTokenKind.Number && r.Number(i) == 0);
                return numeric && r.Number(i) >= 0 && r.Next(i) == r.To;
            case "margin-left":
                return !(v.Kind(v.From) == CssTokenKind.Dimension && v.Number(v.From) == 3); // stands in for a grammar that refuses 3px here
            default:
                return true;
        }
    }

    private static StyleSourceOptions Opts(DeclarationValidator? v = null, List<string>? log = null) => new()
    {
        DeclarationValidator = log is null ? v : (p, t, o) =>
        {
            log.Add(p + "=" + t.RawText().Trim());
            return v is null || v(p, t, o);
        },
    };

    private static string Get(string html, string id, string prop, StyleSourceOptions? o)
    {
        DocumentStyles s = CascadeTests.Styles(html, null, o);
        return s.GetStyle(s.Document.GetElementById(id)).GetPropertyValue(prop);
    }

    [Fact]
    public void Without_a_validator_the_later_declaration_wins()
    {
        const string h = "<style>p{width:100%; width:-webkit-fill-available}</style><p id=a>";
        Assert.Equal("-webkit-fill-available", Get(h, "a", "width", null));
    }

    [Fact]
    public void A_rejected_declaration_falls_back_to_the_earlier_one()
    {
        const string h = "<style>p{width:100%; width:-webkit-fill-available}</style><p id=a>";
        Assert.Equal("100%", Get(h, "a", "width", Opts(Grammar)));
    }

    [Fact]
    public void A_rejected_declaration_with_nothing_earlier_gives_the_initial_value()
    {
        Assert.Equal("auto", Get("<style>p{width:-webkit-fill-available}</style><p id=a>", "a", "width", Opts(Grammar)));
    }

    [Fact]
    public void A_rejected_important_declaration_does_not_beat_a_normal_one()
    {
        const string h = "<style>p{width:-webkit-fill-available !important} p{width:50%}</style><p id=a>";
        Assert.Equal("-webkit-fill-available", Get(h, "a", "width", null));
        Assert.Equal("50%", Get(h, "a", "width", Opts(Grammar)));
    }

    [Fact]
    public void Inline_style_and_nested_rules_are_validated()
    {
        const string h = "<style>p{width:10px; & b{width:20px; width:bogus}}</style><p id=a style='width:30%; width:-webkit-fill-available'><b id=b>";
        Assert.Equal("30%", Get(h, "a", "width", Opts(Grammar)));
        Assert.Equal("20px", Get(h, "b", "width", Opts(Grammar)));
    }

    [Fact]
    public void An_expanded_shorthand_is_validated_by_its_longhands_and_dropped_whole()
    {
        var log = new List<string>();
        const string h = "<style>p{margin:1px} p{margin:2px 3px}</style><p id=a>";
        Assert.Equal("1px", Get(h, "a", "margin-left", Opts(Grammar)));
        Assert.Equal("1px", Get(h, "a", "margin-top", Opts(Grammar)));
        Get(h, "a", "margin-left", Opts(Grammar, log));
        Assert.Contains("margin-left=3px", log);
        Assert.DoesNotContain(log, e => e.StartsWith("margin=", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Values_with_var_and_custom_properties_and_keywords_are_not_asked()
    {
        var log = new List<string>();
        const string h = "<style>p{--w:-webkit-fill-available; width:var(--w); min-width:inherit; --x:bogus}</style><p id=a>";
        Assert.Equal("-webkit-fill-available", Get(h, "a", "width", Opts(Grammar, log)));
        Assert.Empty(log);
    }

    [Fact]
    public void Typed_properties_and_the_user_agent_sheet_are_not_asked()
    {
        var log = new List<string>();
        Get("<p id=a style='color:red; display:block; width:5px'>", "a", "color", Opts(Grammar, log));
        Assert.Equal(["width=5px"], log);
    }

    [Fact]
    public void The_origin_is_passed()
    {
        var seen = new List<StyleOrigin>();
        var user = Stylesheet.Parse("p{width:1px}");
        var o = new StyleSourceOptions
        {
            UserSheets = [user],
            DeclarationValidator = (p, t, origin) =>
            {
                seen.Add(origin);
                return true;
            },
        };
        Get("<style>p{width:2px}</style><p id=a>", "a", "width", o);
        Assert.Equal([StyleOrigin.User, StyleOrigin.Author], seen.OrderBy(x => x).ToList());
    }

    [Fact]
    public void A_token_range_can_be_parsed_from_text()
    {
        TokenRange r = TokenRange.Parse(" 10px ");
        Assert.True(Grammar("width", r, StyleOrigin.Author));
        Assert.False(Grammar("width", TokenRange.Parse("-webkit-fill-available"), StyleOrigin.Author));
        Assert.False(Grammar("width", TokenRange.Parse("-1px"), StyleOrigin.Author));
    }

    [Fact]
    public void Overlays_and_restyle_equal_a_full_recompute()
    {
        const string h = "<style>p{width:10px; width:-webkit-fill-available} a:hover{width:5px; width:bogus} a:focus{margin:1px; margin:2px 3px}</style><p id=p><a id=a href=#>x</a></p>";
        StyleSourceOptions o = Opts(Grammar);
        DocumentStyles baseStyles = CascadeTests.Styles(h, null, o);
        Document doc = baseStyles.Document;
        StyleContext ctx = StyleContext.Default.WithHover(doc.GetElementById("a")).WithFocus(doc.GetElementById("a"));
        DocumentStyles overlay = baseStyles.WithContext(ctx);
        DocumentStyles full = DocumentStyles.Create(doc, ctx, o);
        foreach (Node e in doc.Root.DescendantElements)
        {
            Assert.Null(overlay.GetStyle(e).FirstDifference(full.GetStyle(e)));
        }

        Assert.Equal("5px", overlay.GetStyle(doc.GetElementById("a")).GetPropertyValue("width"));
        Assert.Equal("1px", overlay.GetStyle(doc.GetElementById("a")).GetPropertyValue("margin-left"));

        EditResult er = doc.Apply([Edit.SetAttribute(doc.GetElementById("p"), "style", "width:7%; width:-webkit-fill-available")]);
        RestyleResult res = baseStyles.Restyle(er);
        DocumentStyles fresh = DocumentStyles.Create(er.Document!, null, o);
        foreach (Node e in er.Document!.Root.DescendantElements)
        {
            Assert.Null(res.Styles.GetStyle(e).FirstDifference(fresh.GetStyle(e)));
        }

        Assert.Equal("7%", res.Styles.GetStyle(er.Document!.GetElementById("p")).GetPropertyValue("width"));
    }
}
