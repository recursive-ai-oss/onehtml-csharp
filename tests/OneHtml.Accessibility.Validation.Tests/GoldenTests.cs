using System;
using System.IO;
using RecursiveAI.OneHtml.Css;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>
/// Byte-for-byte checks of both exports on a fixed page. Set <c>ONEHTML_UPDATE_GOLDEN=1</c> to rewrite the expected
/// files after a deliberate change, and read the diff.
/// </summary>
public sealed class GoldenTests
{
    private static string Dir { get; } = Path.Combine(AriaActTests.Repo, "tests", "OneHtml.Accessibility.Validation.Tests", "Golden");

    private static CheckReport Report(string page = "aria-page", StyleContext? context = null)
    {
        Document doc = Document.Parse(File.ReadAllBytes(Path.Combine(Dir, page + ".html")));
        AccessibilityTree tree = AccessibilityTree.Build(doc, DocumentStyles.Create(doc), context);
        return new Checker().Check(tree);
    }

    private static void Compare(string name, string actual)
    {
        string path = Path.Combine(Dir, name);
        if (Environment.GetEnvironmentVariable("ONEHTML_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual);
        }

        Assert.Equal(File.ReadAllText(path), actual);
    }

    [Fact]
    public void Native_json_matches_the_golden_file() =>
        Compare("aria-page.native.json", Report().ToNativeJson(new ExportOptions { Indented = true, Url = "https://example.test/page" }));

    [Fact]
    public void Axe_compatible_json_matches_the_golden_file() =>
        Compare("aria-page.axe.json", Report().ToAxeJson(new ExportOptions { Indented = true, Url = "https://example.test/page" }));

    [Fact]
    public void Names_page_native_json_matches_the_golden_file() =>
        Compare("names-page.native.json", Report("names-page").ToNativeJson(new ExportOptions { Indented = true, Url = "https://example.test/names" }));

    [Fact]
    public void Names_page_axe_compatible_json_matches_the_golden_file() =>
        Compare("names-page.axe.json", Report("names-page").ToAxeJson(new ExportOptions { Indented = true, Url = "https://example.test/names" }));

    [Fact]
    public void Structure_page_native_json_matches_the_golden_file() =>
        Compare("structure-page.native.json", Report(page: "structure-page").ToNativeJson(new ExportOptions { Indented = true, Url = "https://example.test/structure" }));

    [Fact]
    public void Structure_page_axe_compatible_json_matches_the_golden_file() =>
        Compare("structure-page.axe.json", Report(page: "structure-page").ToAxeJson(new ExportOptions { Indented = true, Url = "https://example.test/structure" }));

    [Fact]
    public void Structure_page_reports_each_structure_rule_it_was_written_for()
    {
        CheckReport r = Report(page: "structure-page");
        var missing = new System.Collections.Generic.List<string>();
        foreach (string rule in new[]
        {
            "skip-link", "landmark-unique", "landmark-no-duplicate-contentinfo", "heading-order", "page-has-heading-one", "p-as-heading", "list", "listitem",
            "definition-list", "duplicate-id", "duplicate-id-active", "tabindex", "focus-order-semantics", "accesskeys", "nested-interactive", "frame-focusable-content",
            "blink", "server-side-image-map", "hidden-content", "meta-refresh", "meta-viewport", "region",
        })
        {
            if (!System.Linq.Enumerable.Any(r.Results, x => x.RuleId == rule && x.Outcome is Outcome.Failed or Outcome.NeedsReview))
            {
                missing.Add(rule);
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Exports_do_not_depend_on_the_run()
    {
        Assert.Equal(Report().ToNativeJson(), Report().ToNativeJson());
        Assert.Equal(Report().ToAxeJson(), Report().ToAxeJson());
    }
}
