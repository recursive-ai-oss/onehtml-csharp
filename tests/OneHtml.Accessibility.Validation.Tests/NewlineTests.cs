using System;
using System.IO;
using System.Linq;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

/// <summary>
/// The JSON the libraries write is a language-neutral format: line breaks are always "\n", never the operating system's.
/// <c>Utf8JsonWriter</c> follows <c>Environment.NewLine</c> unless told otherwise, so a Windows run would differ from a
/// Linux one. These checks hold on every OS: the text has no carriage return, and every writer in the shipped sources
/// names its newline.
/// </summary>
public sealed class NewlineTests
{
    [Fact]
    public void Every_json_writer_in_the_sources_sets_its_newline()
    {
        string src = Path.Combine(AriaActTests.Repo, "src");
        var missing = new System.Collections.Generic.List<string>();
        foreach (string file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string text = File.ReadAllText(file);
            int at = 0;
            while ((at = text.IndexOf("new JsonWriterOptions", at, StringComparison.Ordinal)) >= 0)
            {
                int end = text.IndexOf('}', at);
                if (!text.AsSpan(at, end - at).Contains("NewLine = \"\\n\"", StringComparison.Ordinal))
                {
                    missing.Add(Path.GetRelativePath(src, file));
                }

                at = end;
            }

            // A writer made without options takes the OS newline when indented; the compact form has none.
            Assert.DoesNotContain("Environment.NewLine", text, StringComparison.Ordinal);
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Indented_exports_contain_no_carriage_return()
    {
        Document doc = Document.Parse("<!doctype html><title>t</title><img src=a.png><button></button>"u8.ToArray());
        CheckReport report = new Checker().Check(AccessibilityTree.Build(doc, Css.DocumentStyles.Create(doc)));
        var options = new ExportOptions { Indented = true, Url = "https://example.test/" };
        Assert.DoesNotContain('\r', report.ToNativeJson(options));
        Assert.DoesNotContain('\r', report.ToAxeJson(options));
        Assert.DoesNotContain('\r', report.ToSarifJson(options));
    }
}
