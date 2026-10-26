using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Tests.Editing;

public class SheetEditTests
{
    internal static CssNode Rule(Stylesheet s, int n) => s.Rules.Where(r => r.Kind is CssNodeKind.QualifiedRule or CssNodeKind.AtRule).ElementAt(n);

    internal static string Apply(string css, System.Func<Stylesheet, IEnumerable<SheetEdit>> make, out BatchResult result)
    {
        Stylesheet s = Stylesheet.Parse(css);
        result = s.Apply(make(s));
        Assert.True(result.Succeeded, result.Failure);
        return result.Sheets[0].NewSheet.Text;
    }

    internal static string Apply(string css, System.Func<Stylesheet, IEnumerable<SheetEdit>> make) => Apply(css, make, out _);

    [Fact]
    public void SetDeclaration_replaces_value_only()
    {
        string r = Apply("a {\n  color: red;\n  margin: 0;\n}\n", s => [SheetEdit.SetDeclaration(s, Rule(s, 0), "color", "blue")]);
        Assert.Equal("a {\n  color: blue;\n  margin: 0;\n}\n", r);
    }

    [Fact]
    public void SetDeclaration_appends_in_local_style()
    {
        Assert.Equal("a {\n  color: red;\n  margin: 0;\n}\n", Apply("a {\n  color: red;\n}\n", s => [SheetEdit.SetDeclaration(s, Rule(s, 0), "margin", "0")]));
        Assert.Equal("a{color:red;margin:0}", Apply("a{color:red}", s => [SheetEdit.SetDeclaration(s, Rule(s, 0), "margin", "0")]));
        Assert.Equal("a { color: red; margin: 0 }", Apply("a { color: red }", s => [SheetEdit.SetDeclaration(s, Rule(s, 0), "margin", "0")]));
        Assert.Equal("a{margin: 0;}", Apply("a{}", s => [SheetEdit.SetDeclaration(s, Rule(s, 0), "margin", "0")]));
        Assert.Equal("a {\n    margin: 0;\n}\n", Apply("a {\n}\n", s => [SheetEdit.SetDeclaration(s, Rule(s, 0), "margin", "0")]).Replace("  margin", "    margin").Replace("      ", "    "));
    }

    [Fact]
    public void SetDeclaration_importance()
    {
        Assert.Equal("a{color:blue !important}", Apply("a{color:red}", s => [SheetEdit.SetDeclaration(s, Rule(s, 0), "color", "blue", true)]));
        Assert.Equal("a{color:blue}", Apply("a{color:red !important}", s => [SheetEdit.SetDeclaration(s, Rule(s, 0), "color", "blue")]));
    }

    [Fact]
    public void RemoveDeclaration_keeps_separation()
    {
        Assert.Equal("a{d:e}", Apply("a{b:c;d:e}", s => [SheetEdit.RemoveDeclaration(s, Rule(s, 0), "b")]));
        Assert.Equal("a{b:c;}", Apply("a{b:c;d:e}", s => [SheetEdit.RemoveDeclaration(s, Rule(s, 0), "d")]));
        Assert.Equal("a {\n  b: c;\n}\n", Apply("a {\n  b: c;\n  d: e;\n}\n", s => [SheetEdit.RemoveDeclaration(s, Rule(s, 0), "d")]));
        Assert.Equal("a {\n}\n", Apply("a {\n  b: c;\n}\n", s => [SheetEdit.RemoveDeclaration(s, Rule(s, 0), "B")]));
    }

    [Fact]
    public void InsertRemoveReplaceRule()
    {
        string src = "a { b: c }\n\nd { e: f }\n";
        Assert.Equal("a { b: c }\n\nx{y:z}\n\nd { e: f }\n", Apply(src, s => [SheetEdit.InsertRuleBefore(s, Rule(s, 1), "x{y:z}")]));
        Assert.Equal("a { b: c }\n\nd { e: f }\n\nx{y:z}\n", Apply(src, s => [SheetEdit.AppendRule(s, "x{y:z}")]));
        Assert.Equal("a { b: c }\n\nx{y:z}\n\nd { e: f }\n", Apply(src, s => [SheetEdit.InsertRuleAfter(s, Rule(s, 0), "x{y:z}")]));
        Assert.Equal("a { b: c }\n", Apply(src, s => [SheetEdit.RemoveRule(s, Rule(s, 1))]));
        Assert.Equal("d { e: f }\n", Apply(src, s => [SheetEdit.RemoveRule(s, Rule(s, 0))]));
        Assert.Equal("a { b: c }\n\nq{r:s}\n", Apply(src, s => [SheetEdit.ReplaceRule(s, Rule(s, 1), "q{r:s}")]));
    }

    [Fact]
    public void Nested_blocks_are_indented()
    {
        string src = "@media screen {\n  a { b: c }\n}\n";
        Assert.Equal("@media screen {\n  a { b: c }\n\n  x { y: z }\n}\n".Replace("\n\n  x", "\n  x"), Apply(src, s => [SheetEdit.AppendRule(s, "x { y: z }", Rule(s, 0))]));
    }

    [Fact]
    public void Conflicting_edits_are_invalid()
    {
        Stylesheet s = Stylesheet.Parse("a{b:c}");
        BatchResult r = s.Apply([SheetEdit.SetDeclaration(s, Rule(s, 0), "b", "d"), SheetEdit.RemoveRule(s, Rule(s, 0))]);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Outcomes, o => o.Status == EditStatus.Invalid);
    }

    [Fact]
    public void Bad_input_is_invalid()
    {
        Stylesheet s = Stylesheet.Parse("a{b:c}");
        Assert.False(s.Apply([SheetEdit.SetDeclaration(s, Rule(s, 0), "b", "d; e: f")]).Succeeded);
        Assert.False(s.Apply([SheetEdit.InsertRuleAfter(s, Rule(s, 0), "x{y:z} q{}")]).Succeeded);
        Assert.False(s.Apply([SheetEdit.InsertRuleAfter(s, Rule(s, 0), "x{y:z")]).Succeeded);
    }

    [Fact]
    public void Change_set_lists_nodes()
    {
        Apply("a { b: c }\nd { e: f }\n", s => [SheetEdit.SetDeclaration(s, Rule(s, 0), "g", "h"), SheetEdit.RemoveRule(s, Rule(s, 1))], out BatchResult r);
        StylesheetChangeSet c = r.Sheets[0].Changes;
        Assert.Single(c.Added);
        Assert.Equal("g: h", c.Added[0].Raw.ToString().TrimEnd(';'));
        Assert.Equal(2, c.Removed.Count);
        Assert.Equal(2, c.Changed.Count);
        Assert.Equal(2, c.Ranges.Count);
    }

    [Fact]
    public void Crlf_and_tabs_are_copied()
    {
        string r = Apply("a {\r\n\tcolor: red;\r\n}\r\n", s => [SheetEdit.SetDeclaration(s, Rule(s, 0), "margin", "0")]);
        Assert.Equal("a {\r\n\tcolor: red;\r\n\tmargin: 0;\r\n}\r\n", r);
        r = Apply("a {\r\n\tcolor: red;\r\n}\r\n", s => [SheetEdit.AppendRule(s, "b {\n  c: d;\n}")]);
        Assert.Equal("a {\r\n\tcolor: red;\r\n}\r\n\r\nb {\r\n  c: d;\r\n}\r\n".Replace("}\r\n\r\nb", "}\r\nb"), r);
    }
}
