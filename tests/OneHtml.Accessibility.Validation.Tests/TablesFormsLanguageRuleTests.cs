using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>Behaviour of the table, form-autocomplete and language rules beyond the ACT cases, and their exports on a fixed page.</summary>
public sealed class TablesFormsLanguageRuleTests
{
    private static readonly string[] s_ruleIds =
    [
        "autocomplete-valid", "element-lang-matches-content", "empty-table-header", "html-has-lang", "html-lang-matches-content",
        "html-lang-valid", "html-xml-lang-mismatch", "scope-attr-valid", "table-duplicate-name", "table-fake-caption",
        "td-has-header", "td-headers-attr", "th-has-data-cells", "valid-lang",
    ];

    private static CheckReport Check(string html, params string[] rules) =>
        new Checker().Check(Document.Parse(html), new CheckOptions { RuleIds = rules });

    private static IEnumerable<RuleResult> Reported(CheckReport r) => r.Results.Where(x => x.Outcome != Outcome.Inapplicable);

    private static int Count(CheckReport r, string rule, Outcome outcome) => r.Results.Count(x => x.RuleId == rule && x.Outcome == outcome);

    [Theory]
    [InlineData("en", true)]
    [InlineData("EN-gb", true)]
    [InlineData("zh-Hans-CN", true)]
    [InlineData("qaa", true)]
    [InlineData("qtz-x-foo", true)]
    [InlineData("en-US-GB", true)]
    [InlineData(" fr ", true)]
    [InlineData("eng", false)]
    [InlineData("i-lux", false)]
    [InlineData("x-private", false)]
    [InlineData("dutch", false)]
    [InlineData("#1", false)]
    public void Known_primary_language(string tag, bool expected)
    {
        CheckReport r = Check("<!DOCTYPE html><html lang=\"" + tag + "\"><body>x", "html-lang-valid");
        Assert.Equal(expected, Count(r, "html-lang-valid", Outcome.Passed) == 1);
    }

    [Theory]
    [InlineData("zz", true)]
    [InlineData("zz-Latn-CN", true)]
    [InlineData("zz-1901", true)]
    [InlineData("zz-a-bbb-x-a-ccc", true)]
    [InlineData("x-whatever", true)]
    [InlineData("i-klingon", true)]
    [InlineData("zz-US-GB", false)]
    [InlineData("zz-", false)]
    [InlineData("zz--US", false)]
    [InlineData("zz-a", false)]
    [InlineData("zz_US", false)]
    [InlineData("abcdefghi", false)]
    [InlineData("e", false)]
    public void Language_tag_syntax_is_reported_on_unknown_languages(string tag, bool wellFormed)
    {
        // The message of a valid-lang failure says whether the tag at least follows the RFC 5646 grammar.
        CheckReport r = Check("<!DOCTYPE html><html lang=en><body><p lang=\"" + tag + "\">text", "valid-lang");
        RuleResult f = Assert.Single(r.WithOutcome(Outcome.Failed));
        Assert.Equal(wellFormed ? "unknown primary language" : "not a well-formed language tag", f.Findings[0].Message);
    }

    [Fact]
    public void Autocomplete_token_order()
    {
        string Page(string v, string tag = "input") => "<" + tag + " autocomplete=\"" + v + "\">";
        Assert.Equal(1, Count(Check(Page("section-a shipping work tel-national webauthn"), "autocomplete-valid"), "autocomplete-valid", Outcome.Passed));
        Assert.Equal(1, Count(Check(Page("SHIPPING Street-Address"), "autocomplete-valid"), "autocomplete-valid", Outcome.Passed));
        Assert.Equal(1, Count(Check(Page("billing shipping email"), "autocomplete-valid"), "autocomplete-valid", Outcome.Failed));
        Assert.Equal(1, Count(Check(Page("email home"), "autocomplete-valid"), "autocomplete-valid", Outcome.Failed));
        Assert.Equal(1, Count(Check(Page("on email"), "autocomplete-valid"), "autocomplete-valid", Outcome.Failed));
        Assert.Equal(1, Count(Check(Page("section-"), "autocomplete-valid"), "autocomplete-valid", Outcome.Failed));
        // webauthn is for input and textarea only.
        Assert.Equal(1, Count(Check(Page("username webauthn", "select"), "autocomplete-valid"), "autocomplete-valid", Outcome.Failed));
        Assert.Equal(1, Count(Check(Page("username webauthn", "textarea"), "autocomplete-valid"), "autocomplete-valid", Outcome.Passed));
        // A control inside a disabled fieldset is not checked, except inside its first legend.
        Assert.Empty(Reported(Check("<fieldset disabled><input autocomplete=nope></fieldset>", "autocomplete-valid")));
        Assert.Equal(1, Count(Check("<fieldset disabled><legend><input autocomplete=nope></legend></fieldset>", "autocomplete-valid"), "autocomplete-valid", Outcome.Failed));
    }

    [Fact]
    public void Autocomplete_has_no_suggested_edit()
    {
        CheckReport r = Check("<input autocomplete=\"badname\">", "autocomplete-valid");
        Assert.All(r.Results, x => Assert.Empty(x.SuggestedEdits));
    }

    [Fact]
    public void Td_has_header_applies_to_tables_of_three_by_three()
    {
        string small = "<table><tr><td>a<td>b<tr><td>c<td>d</table>";
        Assert.Empty(Reported(Check(small, "td-has-header")));
        // A plain grid of cells is not taken for a data table (axe-core gates on the same markup evidence); a summary attribute is evidence.
        string plain = "<table><tr><td>a<td>b<td>c<tr><td>d<td>e<td>f<tr><td>g<td>h<td>i</table>";
        Assert.Empty(Reported(Check(plain, "td-has-header")));
        string big = "<table summary=s><tr><td>a<td>b<td>c<tr><td>d<td>e<td>f<tr><td>g<td>h<td>i</table>";
        Assert.Equal(9, Count(Check(big, "td-has-header"), "td-has-header", Outcome.Failed));
        string withTh = "<table><tr><th>A<th>B<th>C<tr><td>d<td>e<td>f<tr><td>g<td>h<td>i</table>";
        CheckReport r = Check(withTh, "td-has-header");
        Assert.Equal(6, Count(r, "td-has-header", Outcome.Passed));
        Assert.Equal(0, Count(r, "td-has-header", Outcome.Failed));
        // An empty cell is not checked.
        Assert.Equal(8, Count(Check("<table summary=s><tr><td>a<td>b<td>c<tr><td>d<td>e<td>f<tr><td>g<td>h<td> </table>", "td-has-header"), "td-has-header", Outcome.Failed));
    }

    [Fact]
    public void Fake_caption_and_duplicate_name()
    {
        CheckReport fake = Check("<table><tr><td colspan=2>Sales by quarter<tr><th>Q1<th>Q2<tr><td>1<td>2</table>", "table-fake-caption");
        Assert.Equal(1, Count(fake, "table-fake-caption", Outcome.Failed));
        CheckReport real = Check("<table><caption>Sales</caption><tr><td colspan=2>Sales by quarter<tr><th>Q1<th>Q2<tr><td>1<td>2</table>", "table-fake-caption");
        Assert.Equal(0, Count(real, "table-fake-caption", Outcome.Failed));

        Assert.Equal(1, Count(Check("<table summary=' Opening  hours '><caption>opening hours</caption><tr><td>x</table>", "table-duplicate-name"), "table-duplicate-name", Outcome.Failed));
        Assert.Equal(1, Count(Check("<table summary='When we are open'><caption>Opening hours</caption><tr><td>x</table>", "table-duplicate-name"), "table-duplicate-name", Outcome.Passed));
    }

    [Fact]
    public void Empty_table_header_is_left_to_a_person()
    {
        CheckReport r = Check("<table><tr><th></th><th>Name</th><td role=columnheader> </td></tr></table>", "empty-table-header");
        Assert.Equal(2, Count(r, "empty-table-header", Outcome.NeedsReview));
        Assert.Equal(1, Count(r, "empty-table-header", Outcome.Passed));
    }

    [Fact]
    public void Header_without_cells_and_headers_attribute_override()
    {
        // The headers attribute replaces the positional association, so Value ends up with nobody.
        CheckReport r = Check("<table><tr><th id=a>A<th id=b>B<tr><td headers=a>1<td headers=a>2</table>", "th-has-data-cells");
        Assert.Equal(1, Count(r, "th-has-data-cells", Outcome.Failed));
        Assert.Equal(1, Count(r, "th-has-data-cells", Outcome.Passed));
        // Row spans: the row header at the left reaches the cell below through rowspan.
        CheckReport spans = Check("<table><tr><th rowspan=2 scope=row>R<td>1<tr><td>2</table>", "th-has-data-cells");
        Assert.Equal(0, Count(spans, "th-has-data-cells", Outcome.Failed));
    }

    [Fact]
    public void Scope_values_and_the_one_suggested_edit()
    {
        string html = "<table><tr><th scope=col>ok<th scope=column>bad<td scope=row>x</table>";
        CheckReport r = Check(html, "scope-attr-valid");
        Assert.Equal(1, Count(r, "scope-attr-valid", Outcome.Passed));
        RuleResult[] failed = r.WithOutcome(Outcome.Failed).ToArray();
        Assert.Equal(2, failed.Length);
        // Only the invalid value on a th gets a suggestion; a valid value on a td has no single fix.
        Assert.Single(failed, f => f.SuggestedEdits.Count > 0);

        Document doc = Document.Parse(html);
        CheckReport before = new Checker().Check(doc, new CheckOptions { RuleIds = ["scope-attr-valid"] });
        IReadOnlyList<Edit> edits = before.SuggestedEditList();
        EditResult applied = doc.Apply(edits);
        Assert.True(applied.Succeeded, applied.Failure);
        Assert.DoesNotContain("scope=column", applied.Document!.Source.Text.ToString(), StringComparison.Ordinal);
        Assert.Contains("scope=col>ok", applied.Document.Source.Text.ToString(), StringComparison.Ordinal);
        ReplayResult replayed = before.SuggestedEdits().Replay(doc);
        Assert.True(replayed.Succeeded, replayed.Failure);
        Assert.Equal(applied.Document.Source.Text.ToString(), replayed.Batch!.Document!.Source.Text.ToString());
        CheckReport after = new Checker().Check(applied.Document, new CheckOptions { RuleIds = ["scope-attr-valid"] });
        Assert.Equal(1, Count(after, "scope-attr-valid", Outcome.Failed));
        Assert.Equal(1, Count(after, "scope-attr-valid", Outcome.Passed));
    }

    [Fact]
    public void Language_rules_give_no_suggested_edit()
    {
        CheckReport r = Check("<html lang=eng xml:lang=fr><body><p lang=dutch>Hello there</p>", s_ruleIds);
        Assert.NotEmpty(r.WithOutcome(Outcome.Failed));
        Assert.All(r.Results.Where(x => x.RuleId != "scope-attr-valid"), x => Assert.Empty(x.SuggestedEdits));
    }

    [Fact]
    public void Language_rules_on_a_page_without_html_lang()
    {
        CheckReport r = Check("<!DOCTYPE html><p>hi", "html-has-lang", "html-lang-valid", "html-xml-lang-mismatch");
        Assert.Equal(1, Count(r, "html-has-lang", Outcome.Failed));
        Assert.DoesNotContain(Reported(r), x => x.RuleId != "html-has-lang");
    }

    [Fact]
    public void Hostile_tables_stay_bounded()
    {
        // Thousands of header cells in one column and a huge colspan must not blow up.
        var sb = new System.Text.StringBuilder("<table><tr><td colspan=999999 rowspan=999999>x</td></tr>");
        for (int i = 0; i < 4000; i++)
        {
            sb.Append("<tr><th>h").Append(i).Append("</th><td headers='a b c d e f g h'>v</td></tr>");
        }

        sb.Append("</table>");
        CheckReport r = Check(sb.ToString(), s_ruleIds);
        Assert.NotNull(r);
    }

    private static string GoldenDir { get; } = Path.Combine(AriaActTests.Repo, "tests", "OneHtml.Accessibility.Validation.Tests", "Golden");

    private static CheckReport GoldenReport()
    {
        Document doc = Document.Parse(File.ReadAllBytes(Path.Combine(GoldenDir, "tables-forms-language-page.html")));
        AccessibilityTree tree = AccessibilityTree.Build(doc, DocumentStyles.Create(doc));
        return new Checker().Check(tree, new CheckOptions { RuleIds = s_ruleIds });
    }

    private static void Compare(string name, string actual)
    {
        string path = Path.Combine(GoldenDir, name);
        if (Environment.GetEnvironmentVariable("ONEHTML_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual);
        }

        Assert.Equal(File.ReadAllText(path), actual);
    }

    [Fact]
    public void Native_json_matches_the_golden_file() =>
        Compare("tables-forms-language-page.native.json", GoldenReport().ToNativeJson(new ExportOptions { Indented = true, Url = "https://example.test/page" }));

    [Fact]
    public void Axe_compatible_json_matches_the_golden_file() =>
        Compare("tables-forms-language-page.axe.json", GoldenReport().ToAxeJson(new ExportOptions { Indented = true, Url = "https://example.test/page" }));
}
