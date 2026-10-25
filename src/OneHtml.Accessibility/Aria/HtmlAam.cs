using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Accessibility.Aria;

/// <summary>One row of the HTML-AAM element role mapping tables (section 3.5) or attribute tables (section 3.6), as published.</summary>
public sealed class HtmlAamEntry
{
    internal HtmlAamEntry(string id, string title, string elements, string ariaMapping, string comments)
    {
        Id = id;
        Title = title;
        Elements = elements;
        AriaMapping = ariaMapping;
        Comments = comments;
    }

    /// <summary>The identifier after the <c>el-</c> or <c>att-</c> prefix of the specification's fragment id.</summary>
    public string Id { get; }

    /// <summary>The heading of the entry, such as <c>header (scoped to the body element)</c>.</summary>
    public string Title { get; }

    /// <summary>For attribute entries, the elements the mapping applies to; empty for element entries.</summary>
    public string Elements { get; }

    /// <summary>The WAI-ARIA mapping column, such as <c>banner role</c>.</summary>
    public string AriaMapping { get; }

    /// <summary>The comments column.</summary>
    public string Comments { get; }
}

/// <summary>
/// The text of the HTML-AAM element and attribute mapping tables, extracted from the pinned specification. The role
/// mapper in this package implements them; the extract is kept so a test can confirm every entry is covered.
/// </summary>
public static class HtmlAam
{
    private static readonly List<HtmlAamEntry> s_elements = Load("html-aam-elements.json", "elements");
    private static readonly List<HtmlAamEntry> s_attributes = Load("html-aam-attributes.json", "attributes");

    /// <summary>The element entries, in specification order.</summary>
    public static IReadOnlyList<HtmlAamEntry> Elements => s_elements;

    /// <summary>The attribute entries, in specification order.</summary>
    public static IReadOnlyList<HtmlAamEntry> Attributes => s_attributes;

    private static List<HtmlAamEntry> Load(string file, string array)
    {
        Assembly assembly = typeof(HtmlAam).Assembly;
        using Stream stream = assembly.GetManifestResourceStream("Aria." + file)
            ?? throw new InvalidOperationException("Missing embedded data: " + file);
        using JsonDocument doc = JsonDocument.Parse(stream);
        var list = new List<HtmlAamEntry>();
        foreach (JsonElement e in doc.RootElement.GetProperty(array).EnumerateArray())
        {
            list.Add(new HtmlAamEntry(
                e.GetProperty("id").GetString()!, e.GetProperty("title").GetString()!,
                e.GetProperty("elements").GetString()!, e.GetProperty("aria").GetString()!,
                e.GetProperty("comments").GetString()!));
        }

        return list;
    }
}
