using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

/// <summary>The review state of an ACT rule on the W3C's published list.</summary>
public enum ActRuleStatus
{
    /// <summary>Reviewed and approved by the ACT task force.</summary>
    Approved,

    /// <summary>Published for review but not yet approved.</summary>
    Proposed,

    /// <summary>No longer maintained and not to be used.</summary>
    Deprecated,
}

/// <summary>One ACT rule from the W3C list.</summary>
/// <param name="Id">The six-character rule id.</param>
/// <param name="Name">The rule's published name.</param>
/// <param name="Status">Approved, proposed or deprecated.</param>
/// <param name="IsComposite">True for a composite rule, which combines the results of its <paramref name="InputRules"/>.</param>
/// <param name="Criteria">Numbers of the WCAG success criteria the rule maps to, in number order.</param>
/// <param name="Techniques">WCAG technique ids named in the rule's requirements (such as <c>G18</c>).</param>
/// <param name="OtherRequirements">Requirements that are not WCAG criteria or techniques, as published (such as <c>aria12:namecalculation</c>).</param>
/// <param name="InputAspects">What the rule inspects: DOM Tree, CSS Styling, Accessibility Tree, Language, Audio output, Visual output, Source code. Empty for composite rules.</param>
/// <param name="InputRules">Ids of the atomic rules a composite rule combines.</param>
public sealed record ActRule(
    string Id,
    string Name,
    ActRuleStatus Status,
    bool IsComposite,
    IReadOnlyList<string> Criteria,
    IReadOnlyList<string> Techniques,
    IReadOnlyList<string> OtherRequirements,
    IReadOnlyList<string> InputAspects,
    IReadOnlyList<string> InputRules);

/// <summary>The W3C ACT rules list, as of the pinned date in the data file.</summary>
public static class ActRules
{
    private static readonly ActRule[] s_all = Load();
    private static readonly Dictionary<string, ActRule> s_byId = Index(s_all);

    /// <summary>All rules in id order.</summary>
    public static IReadOnlyList<ActRule> All => s_all;

    /// <summary>Finds a rule by id, or returns <see langword="null"/>.</summary>
    public static ActRule? Find(string id) => s_byId.GetValueOrDefault(id);

    private static Dictionary<string, ActRule> Index(ActRule[] all)
    {
        var map = new Dictionary<string, ActRule>(all.Length, StringComparer.Ordinal);
        foreach (ActRule rule in all)
        {
            map.Add(rule.Id, rule);
        }

        return map;
    }

    private static ActRule[] Load()
    {
        using JsonDocument document = CatalogueData.Open("act-rules.json");
        JsonElement list = document.RootElement.GetProperty("rules");
        var result = new ActRule[list.GetArrayLength()];
        int i = 0;
        foreach (JsonElement item in list.EnumerateArray())
        {
            result[i++] = new ActRule(
                item.GetProperty("id").GetString()!,
                item.GetProperty("name").GetString()!,
                item.GetProperty("status").GetString() switch
                {
                    "approved" => ActRuleStatus.Approved,
                    "proposed" => ActRuleStatus.Proposed,
                    "deprecated" => ActRuleStatus.Deprecated,
                    _ => throw new InvalidOperationException("Unknown ACT rule status"),
                },
                item.GetProperty("type").GetString() == "composite",
                CatalogueData.Strings(item.GetProperty("criteria")),
                CatalogueData.Strings(item.GetProperty("techniques")),
                CatalogueData.Strings(item.GetProperty("otherRequirements")),
                CatalogueData.Strings(item.GetProperty("inputAspects")),
                CatalogueData.Strings(item.GetProperty("inputRules")));
        }

        return result;
    }
}

/// <summary>One case from the ACT test case index.</summary>
/// <param name="Id">The test case id (a hash of its content).</param>
/// <param name="Title">The case's title, such as "Passed Example 1".</param>
/// <param name="RuleId">The ACT rule the case tests.</param>
/// <param name="Expected">The expected outcome: <c>passed</c>, <c>failed</c> or <c>inapplicable</c>.</param>
/// <param name="RelativePath">The case file's path under the test case directory, such as <c>testcases/5f99a7/9a41….html</c>.</param>
/// <param name="Url">Where the W3C publishes the case.</param>
public sealed record ActTestCase(string Id, string Title, string RuleId, string Expected, string RelativePath, string Url);

/// <summary>Reads the ACT test case index (<c>testcases.json</c>). The library never reads files; the caller supplies the text.</summary>
public static class ActTestCases
{
    /// <summary>Parses the index.</summary>
    /// <exception cref="JsonException">The text is not the expected index.</exception>
    public static IReadOnlyList<ActTestCase> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement list = document.RootElement.GetProperty("testcases");
        var result = new List<ActTestCase>(list.GetArrayLength());
        foreach (JsonElement item in list.EnumerateArray())
        {
            result.Add(new ActTestCase(
                item.GetProperty("testcaseId").GetString()!,
                item.GetProperty("testcaseTitle").GetString()!,
                item.GetProperty("ruleId").GetString()!,
                item.GetProperty("expected").GetString()!,
                item.GetProperty("relativePath").GetString()!,
                item.GetProperty("url").GetString()!));
        }

        return result;
    }
}
