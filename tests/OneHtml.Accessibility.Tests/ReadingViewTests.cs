using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>Golden tests: the reading view and navigation lists of hand-written pages, reviewed by eye when they were recorded.</summary>
public sealed class ReadingViewTests
{
    public static TheoryData<string> Pages()
    {
        var d = new TheoryData<string>();
        foreach ((string name, _) in RealisticPages.All)
        {
            d.Add(name);
        }

        return d;
    }

    private static string Render(string html)
    {
        Document doc = Document.Parse(html);
        AccessibilityTree tree = AccessibilityTree.Build(doc, Css.DocumentStyles.Create(doc));
        var sb = new StringBuilder();
        foreach (ReadingItem i in tree.CreateReadingView().Items)
        {
            sb.Append(' ', i.Depth * 2).Append(i.Kind switch { ReadingItemKind.Text => "T ", ReadingItemKind.Element => "E ", ReadingItemKind.Start => "> ", _ => "< " }).Append(i.Text);
            if (i.Lang is not null)
            {
                sb.Append("  [").Append(i.Lang).Append(']');
            }

            sb.Append('\n');
        }

        NavigationLists nav = tree.CreateNavigationLists();
        sb.Append("--- headings\n");
        foreach (HeadingEntry h in nav.Headings)
        {
            sb.Append(h.Level).Append(' ').Append(h.Name).Append('\n');
        }

        sb.Append("--- landmarks\n");
        foreach (LandmarkEntry l in nav.Landmarks)
        {
            sb.Append(' ', l.Depth * 2).Append(l.Role).Append(l.Name.Length > 0 ? ": " + l.Name : "").Append('\n');
        }

        sb.Append("--- links\n");
        foreach (LinkEntry l in nav.Links)
        {
            sb.Append(l.Name).Append(" -> ").Append(l.Target ?? "(none)").Append(l.IsSamePage ? " (same page)" : "").Append('\n');
        }

        sb.Append("--- form fields\n");
        foreach (FormFieldEntry f in nav.FormFields)
        {
            sb.Append(f.Role).Append(": ").Append(f.Label).Append(f.Required ? " required" : "").Append(f.Invalid ? " invalid" : "").Append(f.Disabled ? " disabled" : "")
                .Append(f.Value is null ? "" : " = " + f.Value).Append('\n');
        }

        sb.Append("--- tables\n");
        foreach (TableEntry t in nav.Tables)
        {
            sb.Append(t.Caption.Length == 0 ? "(no caption)" : t.Caption).Append(' ').Append(t.Rows).Append('x').Append(t.Columns)
                .Append(" cols=[").AppendJoin("|", t.ColumnHeaders).Append("] rows=[").AppendJoin("|", t.RowHeaders).Append("]\n");
        }

        return sb.ToString();
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Reading_view_and_navigation_lists_match_the_golden_file(string name)
    {
        string html = RealisticPages.All.First(p => p.Name == name).Html;
        string actual = Render(html);
        string path = Path.Combine(TestPaths.TestData, "reading-view", name + ".txt");
        if (Environment.GetEnvironmentVariable("ONEHTML_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }

        Assert.Equal(File.ReadAllText(path), actual);
    }
}
