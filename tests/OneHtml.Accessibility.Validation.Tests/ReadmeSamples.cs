// The code blocks in README.md and in the package READMEs are copied from this file. ReadmeSampleTests compiles them
// here (this file is part of the test build) and checks that every line of every csharp block in the READMEs is
// a line of this file, so a README sample cannot drift from code that builds.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Accessibility;
using RecursiveAI.OneHtml.Accessibility.Validation;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Tests;

public class ReadmeSampleTests
{
    private static string Dir => AppContext.BaseDirectory;

    // OneHtml: parse, query, edit losslessly.
    [Fact]
    public void OneHtml_sample()
    {
        Document doc = Document.Parse("<p>Hello <a href=/x>world</a>");
        Node link = doc.Root.QuerySelector("a[href]");
        EditResult edit = doc.Apply(new[] { Edit.SetAttribute(link, "title", "A link") });
        byte[] output = edit.Document!.ToLosslessBytes();
        Assert.True(edit.Succeeded);
        Assert.Contains("title=\"A link\"", Encoding.UTF8.GetString(output), StringComparison.Ordinal);
        Assert.StartsWith("<p>Hello <a href=/x", Encoding.UTF8.GetString(output), StringComparison.Ordinal);
    }

    // OneHtml: styles under a style context.
    [Fact]
    public void Styles_sample()
    {
        Document doc = Document.Parse("<style>a { color: red } a:focus { color: blue }</style><a href=/x>x</a>");
        Node link = doc.Root.QuerySelector("a");
        DocumentStyles styles = DocumentStyles.Create(doc);
        string normal = styles.GetStyle(link).GetPropertyValue("color");
        string focused = styles.WithContext(StyleContext.Default.WithFocus(link)).GetStyle(link).GetPropertyValue("color");
        Assert.NotEqual(normal, focused);
    }

    // Accessibility: the tree, the reading view and the navigation lists.
    [Fact]
    public void Accessibility_sample()
    {
        Document doc = Document.Parse("<main><h1>Title</h1><img src=a.png alt='Logo'><button>Save</button></main>");
        DocumentStyles styles = DocumentStyles.Create(doc);
        AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
        foreach (AxNode node in tree.Nodes.Where(n => !n.IsIgnored))
        {
            Console.WriteLine($"{node.Role} \"{node.Name.Value}\"");
        }

        string reading = tree.CreateReadingView().ToText();
        int headings = tree.CreateNavigationLists().Headings.Count;
        Assert.Contains(tree.Nodes, n => n.Role == "button" && n.Name.Value == "Save");
        Assert.Contains("Save", reading, StringComparison.Ordinal);
        Assert.Equal(1, headings);
    }

    // Validation: check a page and export the results.
    [Fact]
    public void Validation_sample()
    {
        Document doc = Document.Parse("<html lang=en><title>t</title><img src=a.png><button></button>");
        CheckReport report = new Checker().Check(doc);
        foreach (RuleResult result in report.WithOutcome(Outcome.Failed))
        {
            Console.WriteLine($"{result.RuleId}: {result.Message}");
        }

        string axeJson = report.ToAxeJson();
        string sarif = report.ToSarifJson();
        Assert.Contains(report.WithOutcome(Outcome.Failed), r => r.RuleId == "image-alt");
        Assert.Contains("image-alt", axeJson, StringComparison.Ordinal);
        Assert.Contains("2.1.0", sarif, StringComparison.Ordinal);
    }

    // Every csharp line in a README is a line of this file.
    [Theory]
    [InlineData("README.md")]
    [InlineData("README.OneHtml.md")]
    [InlineData("README.Accessibility.md")]
    [InlineData("README.Validation.md")]
    public void Readme_code_lines_come_from_the_samples(string readme)
    {
        string[] sample = System.IO.File.ReadAllLines(System.IO.Path.Combine(Dir, "ReadmeSamples.cs.txt")).Select(l => l.Trim()).ToArray();
        var known = new HashSet<string>(sample, StringComparer.Ordinal);
        bool inBlock = false;
        int checkedLines = 0;
        foreach (string raw in System.IO.File.ReadAllLines(System.IO.Path.Combine(Dir, readme)))
        {
            string line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inBlock = line == "```csharp";
                continue;
            }

            if (inBlock && line.Length > 0)
            {
                Assert.True(known.Contains(line), $"{readme}: not in ReadmeSamples.cs: {line}");
                checkedLines++;
            }
        }

        Assert.True(checkedLines > 0, readme + " has no csharp sample");
    }
}
