using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RecursiveAI.OneHtml.Accessibility.Aria;
using Xunit;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

public sealed partial class TaxonomyTests
{
    [Fact]
    public void Counts_by_module_match_the_generated_data()
    {
        IEnumerable<IGrouping<AriaModule, AriaRole>> byModule = AriaTaxonomy.Roles.GroupBy(r => r.Module);
        Assert.Equal(94, AriaTaxonomy.Roles.Count(r => r.Module == AriaModule.Aria12));
        Assert.Equal(41, AriaTaxonomy.Roles.Count(r => r.Module == AriaModule.DigitalPublishing));
        Assert.Equal(3, AriaTaxonomy.Roles.Count(r => r.Module == AriaModule.Graphics));
        Assert.Equal(3, AriaTaxonomy.Roles.Count(r => r.Module == AriaModule.Aria13Draft));
        Assert.Equal(12, AriaTaxonomy.Roles.Count(r => r.IsAbstract));
        Assert.Equal(53, AriaTaxonomy.Properties.Count);
        Assert.Equal(10, AriaTaxonomy.Properties.Count(p => p.IsState));
        Assert.Equal(4, byModule.Count());
    }

    [Fact]
    public void Roles_carry_their_characteristics()
    {
        AriaRole generic = AriaTaxonomy.FindRole("GENERIC")!;
        Assert.Contains("aria-label", generic.ProhibitedAttributes);
        Assert.Contains("aria-roledescription", generic.ProhibitedAttributes);
        Assert.True(generic.NameProhibited);
        foreach (string name in new[] { "none", "presentation", "code", "paragraph", "strong", "emphasis", "subscript", "superscript", "insertion", "deletion", "caption" })
        {
            string probe = name == "none" ? "presentation" : name;
            Assert.Contains("aria-label", AriaTaxonomy.FindRole(probe)!.ProhibitedAttributes);
        }

        AriaRole heading = AriaTaxonomy.FindRole("heading")!;
        Assert.Equal(["aria-level"], heading.RequiredAttributes);
        Assert.True(heading.NameFromContents && heading.NameFromAuthor && heading.NameRequired);
        Assert.Equal(["sectionhead"], heading.Superclasses);
        Assert.True(AriaTaxonomy.FindRole("widget")!.IsAbstract);
        Assert.Equal(["list", "directory"], AriaTaxonomy.FindRole("listitem")!.RequiredContext.OrderByDescending(x => x == "list"));
        Assert.Equal("vertical", AriaTaxonomy.FindRole("scrollbar")!.ImplicitValues["aria-orientation"]);
        Assert.Equal("polite", AriaTaxonomy.FindRole("log")!.ImplicitValues["aria-live"]);
        Assert.True(AriaTaxonomy.FindRole("button")!.ChildrenPresentational);
        Assert.Contains(AriaTaxonomy.FindRole("listbox")!.RequiredOwned, c => c.SequenceEqual(["group", "option"]));
        Assert.Contains("aria-valuenow", AriaTaxonomy.FindRole("separator")!.RequiredWhenFocusable);
        Assert.Contains("aria-braillelabel", AriaTaxonomy.FindRole("button")!.SupportedAttributes);
        Assert.Equal(AriaModule.DigitalPublishing, AriaTaxonomy.FindRole("doc-chapter")!.Module);
        Assert.Equal(AriaModule.Graphics, AriaTaxonomy.FindRole("graphics-symbol")!.Module);
        Assert.Equal(["row", "rowgroup", "cell", "columnheader", "gridcell", "rowheader"], AriaTaxonomy.GetRequiredOwnedClosure("table"));
        Assert.Empty(AriaTaxonomy.GetRequiredOwnedClosure("button"));
        Assert.Null(AriaTaxonomy.FindRole("image"));
    }

    [Fact]
    public void Every_listed_attribute_is_a_known_property()
    {
        foreach (AriaRole role in AriaTaxonomy.Roles)
        {
            foreach (string a in role.SupportedAttributes.Concat(role.RequiredAttributes).Concat(role.ProhibitedAttributes))
            {
                Assert.NotNull(AriaTaxonomy.FindProperty(a));
            }
        }
    }

    [Fact]
    public void Properties_carry_types_tokens_and_defaults()
    {
        Assert.Equal(AriaValueType.Tristate, AriaTaxonomy.FindProperty("aria-checked")!.ValueType);
        Assert.Equal(AriaValueType.IdReferenceList, AriaTaxonomy.FindProperty("aria-labelledby")!.ValueType);
        Assert.Equal(AriaValueType.IdReference, AriaTaxonomy.FindProperty("aria-activedescendant")!.ValueType);
        Assert.Equal(AriaValueType.IntegerValue, AriaTaxonomy.FindProperty("aria-level")!.ValueType);
        Assert.Equal(AriaValueType.Number, AriaTaxonomy.FindProperty("aria-valuenow")!.ValueType);
        Assert.Equal(AriaValueType.TextValue, AriaTaxonomy.FindProperty("aria-label")!.ValueType);
        Assert.Equal(AriaValueType.TokenList, AriaTaxonomy.FindProperty("aria-relevant")!.ValueType);
        AriaProperty live = AriaTaxonomy.FindProperty("ARIA-LIVE")!;
        Assert.Equal(["assertive", "off", "polite"], live.Tokens.OrderBy(x => x, System.StringComparer.Ordinal));
        Assert.Equal("off", live.DefaultValue);
        Assert.True(live.IsGlobal);
        Assert.False(AriaTaxonomy.FindProperty("aria-checked")!.IsGlobal);
        Assert.True(AriaTaxonomy.FindProperty("aria-grabbed")!.IsDeprecated);
        Assert.True(AriaTaxonomy.FindProperty("aria-pressed")!.IsState);
        Assert.Contains("generic", AriaTaxonomy.FindProperty("aria-labelledby")!.GlobalExceptRoles);
        Assert.Equal(20, AriaTaxonomy.Properties.Count(p => p.IsGlobal));
    }

    [Fact]
    public void Role_lookup_is_ascii_case_insensitive_only()
    {
        Assert.NotNull(AriaTaxonomy.FindRole("BuTtOn"));
        Assert.Null(AriaTaxonomy.FindRole("\u017Fection")); // U+017F is not an ASCII case variant of s
        Assert.NotNull(AriaTaxonomy.FindRole("Button".AsSpan()));
    }

    [Fact]
    public void Html_aam_extract_agrees_with_the_element_mapper()
    {
        // Entries whose heading is just a tag name and whose mapping is "<role> role" or "No corresponding role"
        // must map as the table says. Entries with conditions are covered by the targeted tests.
        var failures = new List<string>();
        int checkedCount = 0;
        foreach (HtmlAamEntry e in HtmlAam.Elements)
        {
            if (e.Title != e.Id || e.Id.Contains('-') || e.Id is "svg" or "math" or "summary" or "td" or "th" or "li" or "option" or "td-gridcell" or "form")
            {
                continue;
            }

            Match m = SimpleMapping().Match(e.AriaMapping);
            string? expected = m.Success ? m.Groups[1].Value : e.AriaMapping.StartsWith("No corresponding role", System.StringComparison.Ordinal) ? "" : null;
            if (expected is null)
            {
                continue;
            }

            string tag = e.Id;
            string html = tag switch
            {
                "caption" or "col" or "colgroup" or "tbody" or "tfoot" or "thead" or "tr" => "<table><" + tag + " id=t>",
                "dd" or "dt" => "<dl><" + tag + " id=t>",
                "figcaption" => "<figure><figcaption id=t>",
                "legend" => "<fieldset><legend id=t>",
                "rp" or "rt" => "<ruby><" + tag + " id=t>",
                "source" or "track" => "<video><" + tag + " id=t>",
                "area" => "<map><area id=t href=x>",
                "a" => "<a id=t href=x>",
                "base" or "link" or "meta" or "script" or "style" or "title" or "noscript" => "<" + tag + " id=t>",
                _ => "<" + tag + " id=t>",
            };
            OneHtml.Document doc = OneHtml.Document.Parse(html);
            OneHtml.Node n = doc.Root.QuerySelector("#t");
            if (n.IsNull)
            {
                continue; // the parser moved or dropped it (head content, bare table parts)
            }

            checkedCount++;
            string got = RoleComputer.Compute(doc)[n].ComputedRole;
            if (got != expected)
            {
                failures.Add(tag + ": spec says '" + expected + "', mapper gives '" + got + "'");
            }
        }

        Assert.True(checkedCount > 60, "too few entries compared: " + checkedCount);
        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    [GeneratedRegex(@"^(\w+) role\s*$", RegexOptions.None, 1000)]
    private static partial Regex SimpleMapping();

    [Fact]
    public void Attribute_mapping_ids_used_by_implicit_properties_exist()
    {
        var ids = HtmlAam.Attributes.Select(a => a.Id).ToHashSet();
        foreach (string id in new[] { "checked", "checked-absent", "disabled", "disabled-fieldset", "required", "readonly", "placeholder", "multiple-select", "selected", "list", "min", "max", "value-meter", "min-input", "max-input", "value-input", "colspan", "rowspan", "open-details" })
        {
            Assert.Contains(id, ids);
        }
    }

    [Fact]
    public void Profile_entries_are_documented()
    {
        string doc = File.ReadAllText(Path.Combine(TestPaths.TestData, "..", "docs", "accessibility-chromium-profile.md"));
        Assert.Equal(ChromiumProfile.Entries.Count, ChromiumProfile.Entries.Select(e => e.Id).Distinct().Count());
        foreach (ProfileEntry e in ChromiumProfile.Entries)
        {
            Assert.Contains("`" + e.Id + "`", doc);
        }
    }
}
