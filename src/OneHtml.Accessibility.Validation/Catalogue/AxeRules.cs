using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

/// <summary>An axe-core rule's published facts: identifier, tags and impact. No description text is kept.</summary>
/// <param name="Id">The rule identifier.</param>
/// <param name="Group">Which section of the published table lists it: <c>wcag20</c>, <c>wcag21</c>, <c>wcag22</c>, <c>wcag-aaa</c>, <c>best-practice</c>, <c>experimental</c> or <c>deprecated</c>.</param>
/// <param name="Impact">The impact: minor, moderate, serious or critical.</param>
/// <param name="Tags">All tags as published, in published order.</param>
/// <param name="IssueTypes">The result kinds the table lists, such as <c>failure</c> and <c>needs review</c>.</param>
/// <param name="ActRules">ACT rule ids the table links to the rule.</param>
public sealed record AxeRule(
    string Id,
    string Group,
    string Impact,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> IssueTypes,
    IReadOnlyList<string> ActRules)
{
    /// <summary>True when the rule carries the <c>best-practice</c> tag.</summary>
    public bool IsBestPractice => Tags.Contains("best-practice");

    /// <summary>
    /// The success criteria named by the rule's <c>wcagNNN</c> tags (<c>wcag143</c> is 1.4.3). The principle and the
    /// guideline are one digit each, so the split is unambiguous. Level tags such as <c>wcag21aa</c> are not criteria.
    /// </summary>
    public IReadOnlyList<string> CriterionNumbers
    {
        get
        {
            var numbers = new List<string>();
            foreach (string tag in Tags)
            {
                if (tag.Length >= 7 && tag.StartsWith("wcag", StringComparison.Ordinal) && IsAllDigits(tag.AsSpan(4)))
                {
                    numbers.Add($"{tag[4]}.{tag[5]}.{tag[6..]}");
                }
            }

            return numbers;
        }
    }

    /// <summary>
    /// The WCAG versions and levels the rule's level tags name (<c>wcag2a</c>, <c>wcag21aa</c>, <c>wcag2aaa</c>, …) as
    /// published. <c>wcag2a-obsolete</c> is not a level tag.
    /// </summary>
    public IReadOnlyList<(WcagVersion Version, WcagLevel Level)> LevelTags
    {
        get
        {
            var result = new List<(WcagVersion, WcagLevel)>();
            foreach (string tag in Tags)
            {
                (WcagVersion, WcagLevel)? parsed = tag switch
                {
                    "wcag2a" => (WcagVersion.Wcag20, WcagLevel.A),
                    "wcag2aa" => (WcagVersion.Wcag20, WcagLevel.AA),
                    "wcag2aaa" => (WcagVersion.Wcag20, WcagLevel.AAA),
                    "wcag21a" => (WcagVersion.Wcag21, WcagLevel.A),
                    "wcag21aa" => (WcagVersion.Wcag21, WcagLevel.AA),
                    "wcag21aaa" => (WcagVersion.Wcag21, WcagLevel.AAA),
                    "wcag22a" => (WcagVersion.Wcag22, WcagLevel.A),
                    "wcag22aa" => (WcagVersion.Wcag22, WcagLevel.AA),
                    "wcag22aaa" => (WcagVersion.Wcag22, WcagLevel.AAA),
                    _ => null,
                };
                if (parsed is { } value)
                {
                    result.Add(value);
                }
            }

            return result;
        }
    }

    private static bool IsAllDigits(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>The axe-core rule identifiers of the pinned release.</summary>
public static class AxeRules
{
    private static readonly (AxeRule[] Rules, string Version) s_data = Load();
    private static readonly AxeRule[] s_all = s_data.Rules;
    private static readonly Dictionary<string, AxeRule> s_byId = Index(s_all);

    /// <summary>The axe-core release the identifiers come from.</summary>
    public static string AxeVersion => s_data.Version;

    /// <summary>All rules in identifier order.</summary>
    public static IReadOnlyList<AxeRule> All => s_all;

    /// <summary>Finds a rule by identifier, or returns <see langword="null"/>.</summary>
    public static AxeRule? Find(string id) => s_byId.GetValueOrDefault(id);

    private static Dictionary<string, AxeRule> Index(AxeRule[] all)
    {
        var map = new Dictionary<string, AxeRule>(all.Length, StringComparer.Ordinal);
        foreach (AxeRule rule in all)
        {
            map.Add(rule.Id, rule);
        }

        return map;
    }

    private static (AxeRule[], string) Load()
    {
        using JsonDocument document = CatalogueData.Open("axe-rules.json");
        string version = document.RootElement.GetProperty("axeVersion").GetString()!;
        JsonElement list = document.RootElement.GetProperty("rules");
        var result = new AxeRule[list.GetArrayLength()];
        int i = 0;
        foreach (JsonElement item in list.EnumerateArray())
        {
            result[i++] = new AxeRule(
                item.GetProperty("id").GetString()!,
                item.GetProperty("group").GetString()!,
                item.GetProperty("impact").GetString()!,
                CatalogueData.Strings(item.GetProperty("tags")),
                CatalogueData.Strings(item.GetProperty("issueTypes")),
                CatalogueData.Strings(item.GetProperty("actRules")));
        }

        return (result, version);
    }
}
