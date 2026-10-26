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

    private static CheckReport Report(StyleContext? context = null)
    {
        Document doc = Document.Parse(File.ReadAllBytes(Path.Combine(Dir, "aria-page.html")));
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
    public void Exports_do_not_depend_on_the_run()
    {
        Assert.Equal(Report().ToNativeJson(), Report().ToNativeJson());
        Assert.Equal(Report().ToAxeJson(), Report().ToAxeJson());
    }
}
