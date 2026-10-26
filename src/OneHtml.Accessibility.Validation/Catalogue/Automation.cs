using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

/// <summary>How far a machine can decide whether a page meets a success criterion.</summary>
public enum AutomationLevel
{
    /// <summary>Every failure condition can be decided mechanically.</summary>
    Fully,

    /// <summary>Some failure conditions can be decided mechanically; the rest need a person.</summary>
    Partly,

    /// <summary>The criterion needs human judgement, or running behaviour a static check cannot see.</summary>
    Not,
}

/// <summary>The assessment of one criterion: its level and the reason, in a sentence.</summary>
/// <param name="Number">The criterion number, such as <c>1.4.3</c>.</param>
/// <param name="Level">How far it can be automated.</param>
/// <param name="Basis">Why, in one sentence.</param>
public sealed record AutomationAssessment(string Number, AutomationLevel Level, string Basis);

/// <summary>The automation assessment of every WCAG criterion, from embedded catalogue data.</summary>
public static class AutomationAssessments
{
    private static readonly Dictionary<string, AutomationAssessment> s_byNumber = Load();

    /// <summary>All assessments in criterion order.</summary>
    public static IReadOnlyCollection<AutomationAssessment> All => s_byNumber.Values;

    /// <summary>Gets the assessment of a criterion.</summary>
    /// <param name="number">The criterion number.</param>
    /// <exception cref="ArgumentException">There is no assessment for the number.</exception>
    public static AutomationAssessment Get(string number) =>
        s_byNumber.TryGetValue(number, out AutomationAssessment? a) ? a : throw new ArgumentException("No automation assessment for " + number, nameof(number));

    private static Dictionary<string, AutomationAssessment> Load()
    {
        using JsonDocument document = CatalogueData.Open("automation.json");
        var map = new Dictionary<string, AutomationAssessment>(StringComparer.Ordinal);
        foreach (JsonElement item in document.RootElement.GetProperty("criteria").EnumerateArray())
        {
            string number = item.GetProperty("num").GetString()!;
            AutomationLevel level = item.GetProperty("automation").GetString() switch
            {
                "fully" => AutomationLevel.Fully,
                "partly" => AutomationLevel.Partly,
                "not" => AutomationLevel.Not,
                _ => throw new InvalidOperationException("Unknown automation level for " + number),
            };
            map.Add(number, new AutomationAssessment(number, level, item.GetProperty("basis").GetString()!));
        }

        return map;
    }
}
