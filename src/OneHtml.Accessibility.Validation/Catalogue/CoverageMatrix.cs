using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

/// <summary>Builds the coverage matrix: every WCAG criterion with the rules that check it.</summary>
public static class CoverageMatrix
{
    /// <summary>
    /// Renders the matrix as Markdown with Unix line endings. Rows are criteria in number order. The columns list the
    /// versions and level, then ACT rules (from the W3C list), axe-core rules (from their <c>wcagNNN</c> tags) and
    /// any extra catalogue rules passed in. Automation is "unassessed" until a criterion has been assessed.
    /// </summary>
    /// <param name="extraRules">Catalogue rules beyond the ACT and axe data; may be <see langword="null"/>.</param>
    public static string ToMarkdown(IEnumerable<RuleDescriptor>? extraRules = null)
    {
        var act = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (ActRule rule in ActRules.All)
        {
            foreach (string number in rule.Criteria)
            {
                Add(act, number, rule.Status == ActRuleStatus.Deprecated ? rule.Id + " (deprecated)" : rule.Id);
            }
        }

        var axe = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (AxeRule rule in AxeRules.All)
        {
            foreach (string number in rule.CriterionNumbers)
            {
                Add(axe, number, rule.Group == "deprecated" ? rule.Id + " (deprecated)" : rule.Id);
            }
        }

        var other = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (extraRules is not null)
        {
            foreach (RuleDescriptor rule in extraRules)
            {
                foreach (SuccessCriterion criterion in rule.Criteria)
                {
                    Add(other, criterion.Number, rule.Id);
                }
            }
        }

        var sb = new StringBuilder();
        sb.Append("| Criterion | Name | Level | In WCAG | ACT rules | axe-core rules | Other rules | Automation |\n");
        sb.Append("|---|---|---|---|---|---|---|---|\n");
        int withAny = 0;
        foreach (SuccessCriterion c in WcagCriteria.All)
        {
            act.TryGetValue(c.Number, out List<string>? a);
            axe.TryGetValue(c.Number, out List<string>? x);
            other.TryGetValue(c.Number, out List<string>? o);
            withAny += a is not null || x is not null || o is not null ? 1 : 0;
            sb.Append("| ").Append(c.Number)
              .Append(" | ").Append(c.Handle)
              .Append(" | ").Append(c.Level.ToString())
              .Append(" | ").Append(Versions(c))
              .Append(" | ").Append(Join(a))
              .Append(" | ").Append(Join(x))
              .Append(" | ").Append(Join(o))
              .Append(" | unassessed |\n");
        }

        var head = new StringBuilder();
        head.Append("Criteria with at least one rule: ")
            .Append(withAny.ToString(CultureInfo.InvariantCulture)).Append(" of ")
            .Append(WcagCriteria.All.Count.ToString(CultureInfo.InvariantCulture)).Append(".\n\n");
        head.Append("| WCAG version | A | AA | AAA | Total |\n|---|---|---|---|---|\n");
        foreach (WcagVersion v in new[] { WcagVersion.Wcag20, WcagVersion.Wcag21, WcagVersion.Wcag22 })
        {
            int a = 0, aa = 0, aaa = 0;
            foreach (SuccessCriterion c in WcagCriteria.ForVersion(v))
            {
                switch (c.Level)
                {
                    case WcagLevel.A: a++; break;
                    case WcagLevel.AA: aa++; break;
                    default: aaa++; break;
                }
            }

            head.Append("| ").Append(VersionName(v))
                .Append(" | ").Append(a.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(aa.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(aaa.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append((a + aa + aaa).ToString(CultureInfo.InvariantCulture)).Append(" |\n");
        }

        head.Append('\n');
        return head.Append(sb).ToString();
    }

    private static void Add(Dictionary<string, List<string>> map, string number, string id)
    {
        if (!map.TryGetValue(number, out List<string>? list))
        {
            map[number] = list = [];
        }

        if (!list.Contains(id))
        {
            list.Add(id);
        }
    }

    private static string Join(List<string>? ids) =>
        ids is null ? "" : string.Join(", ", ids.Order(StringComparer.Ordinal));

    private static string VersionName(WcagVersion v) => v switch
    {
        WcagVersion.Wcag20 => "2.0",
        WcagVersion.Wcag21 => "2.1",
        _ => "2.2",
    };

    private static string Versions(SuccessCriterion c)
    {
        string text = VersionName(c.IntroducedIn) + (c.RemovedIn is null ? "+" : "");
        return c.RemovedIn is { } removed ? text + " (removed in " + VersionName(removed) + ")" : text;
    }
}
