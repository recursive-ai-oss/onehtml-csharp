using System.Linq;
using System.Threading;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Css;

/// <summary>The stylesheet object model: ranges, kinds and the typed at-rule views.</summary>
public class CssModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Slice(string text, CssRange r) => text.Substring(r.Start, r.Length);

    [Fact]
    public void Declaration_parts_have_exact_ranges()
    {
        const string css = "a {  color /*c*/ : red   blue ! important ;  --x:/*k*/ 1 /*m*/ 2;last:v}";
        Stylesheet s = Stylesheet.Parse(css, cancellationToken: Ct);
        CssNode rule = s.Rules.Single();
        Assert.Equal(css, Slice(css, rule.Range));
        Assert.Equal("a", Slice(css, rule.PreludeRange));
        Assert.Equal("{  color /*c*/ : red   blue ! important ;  --x:/*k*/ 1 /*m*/ 2;last:v}", Slice(css, rule.BlockRange));
        CssNode[] d = [.. rule.Declarations];
        Assert.Equal(3, d.Length);

        Assert.Equal("color", Slice(css, d[0].NameRange));
        Assert.Equal(":", Slice(css, d[0].ColonRange));
        Assert.Equal("red   blue", Slice(css, d[0].ValueRange));
        Assert.Equal("! important", Slice(css, d[0].ImportantRange));
        Assert.Equal(";", Slice(css, d[0].SemicolonRange));
        Assert.Equal("color /*c*/ : red   blue ! important ;", Slice(css, d[0].Range));
        Assert.True(d[0].IsImportant);

        Assert.True(d[1].IsCustomProperty);
        Assert.Equal("1 /*m*/ 2", Slice(css, d[1].ValueRange));
        Assert.False(d[1].IsImportant);
        Assert.True(d[1].ImportantRange.IsEmpty);

        Assert.Equal("last:v", Slice(css, d[2].Range));
        Assert.True(d[2].SemicolonRange.IsEmpty);
    }

    [Fact]
    public void Unclosed_blocks_run_to_the_end_of_the_text()
    {
        const string css = "a { b: c; @media x { d { e: f";
        Stylesheet s = Stylesheet.Parse(css, cancellationToken: Ct);
        CssNode a = s.Rules.Single();
        Assert.False(a.IsBlockClosed);
        Assert.Equal(css.Length, a.Range.End);
        CssNode media = a.Rules.Single();
        Assert.Equal(AtRuleKind.Media, media.AtRuleKind);
        Assert.Equal(css.Length, media.Range.End);
        Assert.Equal("e: f", Assert.Single(Assert.Single(media.Rules).Declarations).Raw.ToString());
    }

    [Fact]
    public void Discarded_source_is_kept_as_invalid_nodes()
    {
        const string css = "@a; junk ; b{} c"; // semicolons do not end a qualified rule
        CssNode[] n = [.. Stylesheet.Parse(css, cancellationToken: Ct).Rules];
        Assert.Equal([CssNodeKind.AtRule, CssNodeKind.QualifiedRule, CssNodeKind.Invalid], n.Select(x => x.Kind));
        Assert.Equal("junk ; b", n[1].PreludeText.ToString());
        Assert.Equal("c", n[2].Raw.ToString());
    }

    [Fact]
    public void Declarations_and_rules_stay_interleaved_in_source_order()
    {
        CssNode a = Stylesheet.Parse("a { x: 1; & b { } y: 2; @media z { } }", cancellationToken: Ct).Rules.Single();
        Assert.Equal([CssNodeKind.Declaration, CssNodeKind.QualifiedRule, CssNodeKind.Declaration, CssNodeKind.AtRule], a.Children.Select(c => c.Kind));
    }

    [Fact]
    public void At_rule_views()
    {
        const string css = "@media screen and (min-width: 1px) { a {} } @supports (display: grid) { b {} } @layer a, b.c ; @layer { c {} }"
            + "@import url(\"x.css\") layer(base) supports(display: grid) print and (min-width: 2px); @import 'y.css' layer;"
            + "@container card (min-width: 400px) { d {} } @namespace svg url(http://x/y); @namespace \"http://z\"; @charset \"utf-8\"; @font-face { } @unknown;";
        CssNode[] n = [.. Stylesheet.Parse(css, cancellationToken: Ct).Rules];

        Assert.Equal("screen and (min-width: 1px)", n[0].AsMedia()!.Value.Queries.Raw.ToString());
        Assert.Single(n[0].AsMedia()!.Value.Rules);
        Assert.Equal("(display: grid)", n[1].AsSupports()!.Value.Condition.Raw.ToString());

        LayerRule statement = n[2].AsLayer()!.Value;
        Assert.True(statement.IsStatement);
        Assert.Equal(["a", "b.c"], statement.Names);
        LayerRule block = n[3].AsLayer()!.Value;
        Assert.False(block.IsStatement);
        Assert.Empty(block.Names);

        ImportRule import = n[4].AsImport()!.Value;
        Assert.Equal("x.css", import.Url);
        Assert.True(import.HasLayer);
        Assert.Equal("base", import.LayerName);
        Assert.Equal("display: grid", css.Substring(import.SupportsRange.Start, import.SupportsRange.Length));
        Assert.Equal("print and (min-width: 2px)", import.MediaText);
        ImportRule plain = n[5].AsImport()!.Value;
        Assert.Equal("y.css", plain.Url);
        Assert.True(plain.HasLayer);
        Assert.Null(plain.LayerName);

        ContainerRule container = n[6].AsContainer()!.Value;
        Assert.Equal("card", container.ContainerName);
        Assert.Equal("(min-width: 400px)", css.Substring(container.Condition.Start, container.Condition.Length));

        Assert.Equal(("svg", "http://x/y"), (n[7].AsNamespace()!.Value.Prefix, n[7].AsNamespace()!.Value.Uri));
        Assert.Equal((null, "http://z"), (n[8].AsNamespace()!.Value.Prefix, n[8].AsNamespace()!.Value.Uri));
        Assert.Equal("utf-8", n[9].AsCharset()!.Value.Encoding);
        Assert.Equal(AtRuleKind.FontFace, n[10].AtRuleKind);
        Assert.Equal(AtRuleKind.Unknown, n[11].AtRuleKind);
        Assert.Null(n[0].AsSupports());
    }

    [Fact]
    public void Names_are_decoded_and_compared_without_case()
    {
        CssNode rule = Stylesheet.Parse("@MEDIA x { } a { COLOR: red; --Mixed: 1; \\63 olor: blue }", cancellationToken: Ct).Rules.Last();
        CssNode[] d = [.. rule.Declarations];
        Assert.True(d[0].NameEqualsIgnoreCase("color"));
        Assert.Equal("--Mixed", d[1].Name);
        Assert.Equal("color", d[2].Name);
        Assert.Equal(AtRuleKind.Media, Stylesheet.Parse("@MEDIA x { }", cancellationToken: Ct).Rules.Single().AtRuleKind);
    }

    [Fact]
    public void Comma_separated_lists_split_at_top_level_commas()
    {
        CssTree t = CssTree.ParseCommaSeparatedList("a, f(b, c) , [d, e],", cancellationToken: Ct);
        Assert.Equal(3, t.GroupCount);
        Assert.Equal("a", t.GetGroup(0).Raw.ToString());
        Assert.Equal("f(b, c) ", t.GetGroup(1).Trimmed().Raw.ToString() + " ");
        Assert.Equal("[d, e]", t.GetGroup(2).Trimmed().Raw.ToString());
        Assert.Equal(0, CssTree.ParseCommaSeparatedList("", cancellationToken: Ct).GroupCount);
    }

    [Fact]
    public void Canonical_serialization_follows_cssom()
    {
        Stylesheet s = Stylesheet.Parse("A  ,B{COLOR : Red  !IMPORTANT;--X:  1 ;margin:0 0 0 0}@media print{p{}} @import 'a\"b.css';", cancellationToken: Ct);
        Assert.Equal("A ,B { color: Red !important; --X: 1; margin: 0 0 0 0; }\n@media print {\n  p { }\n}\n@import \"a\\\"b.css\";", s.Serialize());
        Assert.Equal("\\31 23", CssSerializer.SerializeIdentifier("123"));
        Assert.Equal("-\\31 ", CssSerializer.SerializeIdentifier("-1"));
        Assert.Equal("\\-", CssSerializer.SerializeIdentifier("-"));
        Assert.Equal("a\\ b\\.c", CssSerializer.SerializeIdentifier("a b.c"));
        Assert.Equal("\"a\\\"b\\\\c\\a \"", CssSerializer.SerializeString("a\"b\\c\n"));
    }
}
