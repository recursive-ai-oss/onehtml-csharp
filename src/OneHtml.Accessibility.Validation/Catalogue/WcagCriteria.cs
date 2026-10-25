using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

/// <summary>Every success criterion of WCAG 2.0, 2.1 and 2.2, from the W3C's published machine-readable WCAG data.</summary>
public static class WcagCriteria
{
    private static readonly SuccessCriterion[] s_all = Load();
    private static readonly Dictionary<string, SuccessCriterion> s_byNumber = Index(s_all);

    /// <summary>All criteria in number order, including ones a later version removed (4.1.1 Parsing).</summary>
    public static IReadOnlyList<SuccessCriterion> All => s_all;

    /// <summary>Finds a criterion by number such as <c>1.4.3</c>, or returns <see langword="null"/>.</summary>
    public static SuccessCriterion? Find(string number) => s_byNumber.GetValueOrDefault(number);

    /// <summary>Gets a criterion by number.</summary>
    /// <exception cref="ArgumentException">There is no such criterion.</exception>
    public static SuccessCriterion Get(string number) =>
        Find(number) ?? throw new ArgumentException("Unknown success criterion: " + number, nameof(number));

    /// <summary>The criteria that belong to a version of WCAG, at every level.</summary>
    public static IEnumerable<SuccessCriterion> ForVersion(WcagVersion version)
    {
        foreach (SuccessCriterion criterion in s_all)
        {
            if (criterion.IsInVersion(version))
            {
                yield return criterion;
            }
        }
    }

    private static Dictionary<string, SuccessCriterion> Index(SuccessCriterion[] all)
    {
        var map = new Dictionary<string, SuccessCriterion>(all.Length, StringComparer.Ordinal);
        foreach (SuccessCriterion criterion in all)
        {
            map.Add(criterion.Number, criterion);
        }

        return map;
    }

    private static SuccessCriterion[] Load()
    {
        using JsonDocument document = CatalogueData.Open("wcag-criteria.json");
        JsonElement list = document.RootElement.GetProperty("criteria");
        var result = new SuccessCriterion[list.GetArrayLength()];
        int i = 0;
        foreach (JsonElement item in list.EnumerateArray())
        {
            JsonElement removed = item.GetProperty("removedIn");
            result[i++] = new SuccessCriterion(
                item.GetProperty("num").GetString()!,
                item.GetProperty("id").GetString()!,
                item.GetProperty("handle").GetString()!,
                item.GetProperty("level").GetString() switch
                {
                    "A" => WcagLevel.A,
                    "AA" => WcagLevel.AA,
                    "AAA" => WcagLevel.AAA,
                    _ => throw new InvalidOperationException("Unknown WCAG level"),
                },
                CatalogueData.ParseVersion(item.GetProperty("introduced").GetString()!),
                removed.ValueKind == JsonValueKind.Null ? null : CatalogueData.ParseVersion(removed.GetString()!));
        }

        return result;
    }
}
