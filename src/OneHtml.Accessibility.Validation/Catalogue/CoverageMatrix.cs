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
    /// the rules this library implements. Automation says how far a machine can decide the criterion (fully, partly, not),
    /// with the reason. Before the table come the counts per WCAG version and a summary for each profile (criteria in
    /// the profile, how many have an implemented rule, how they split by automation level, and the gaps).
    /// </summary>
    /// <param name="implementedRules">The descriptors of the implemented rules; may be <see langword="null"/>.</param>
    public static string ToMarkdown(IEnumerable<RuleDescriptor>? implementedRules = null)
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
        var noCriterion = new List<string>();
        if (implementedRules is not null)
        {
            foreach (RuleDescriptor rule in implementedRules)
            {
                if (rule.Criteria.Count == 0)
                {
                    noCriterion.Add(rule.Id);
                }

                foreach (SuccessCriterion criterion in rule.Criteria)
                {
                    Add(other, criterion.Number, rule.Id);
                }
            }
        }

        var sb = new StringBuilder();
        sb.Append("| Criterion | Name | Level | In WCAG | ACT rules | axe-core rules | Implemented | Automation | Basis |\n");
        sb.Append("|---|---|---|---|---|---|---|---|---|\n");
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
              .Append(" | ").Append(AutomationName(AutomationAssessments.Get(c.Number).Level))
              .Append(" | ").Append(AutomationAssessments.Get(c.Number).Basis)
              .Append(" |\n");
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
        head.Append(ProfileSummaries(other, act, axe));
        sb.Append('\n');
        if (noCriterion.Count > 0)
        {
            sb.Append("Implemented best-practice rules, which check no criterion: ").Append(string.Join(", ", noCriterion.Order(StringComparer.Ordinal))).Append(".\n");
        }

        return head.Append(sb).ToString();
    }

    private static string AutomationName(AutomationLevel level) => level switch
    {
        AutomationLevel.Fully => "fully",
        AutomationLevel.Partly => "partly",
        _ => "not",
    };

    // Per profile: how many of its criteria have an implemented rule, any rule at all (ours, ACT or axe-core), and
    // how the criteria split by automation level. A criterion that can be automated but has no implemented rule is a gap.
    private static string ProfileSummaries(
        Dictionary<string, List<string>> implemented,
        Dictionary<string, List<string>> act,
        Dictionary<string, List<string>> axe)
    {
        var sb = new StringBuilder();
        sb.Append("Profile summaries (criteria in the profile; the automation columns split them by assessment; *Implemented* counts criteria that have at least one rule in this package; *Gaps* counts criteria assessed fully or partly automatable that have none):\n\n");
        sb.Append("| Profile | Criteria | Implemented | Implemented, fully | Implemented, partly | Implemented, not | Not automatable | Gaps (fully or partly, no rule) | Some ACT or axe-core rule, none ours |\n");
        sb.Append("|---|---|---|---|---|---|---|---|---|\n");
        foreach (ValidationProfile profile in new[] { ValidationProfile.Wcag20AA, ValidationProfile.Wcag21AA, ValidationProfile.Wcag22AA, ValidationProfile.All })
        {
            int total = 0, impl = 0, gaps = 0, others = 0, notAuto = 0;
            var byLevel = new int[3];
            foreach (SuccessCriterion c in profile.Criteria)
            {
                total++;
                AutomationLevel level = AutomationAssessments.Get(c.Number).Level;
                bool has = implemented.ContainsKey(c.Number);
                notAuto += level == AutomationLevel.Not ? 1 : 0;
                if (has)
                {
                    impl++;
                    byLevel[(int)level]++;
                }
                else
                {
                    gaps += level != AutomationLevel.Not ? 1 : 0;
                    others += act.ContainsKey(c.Number) || axe.ContainsKey(c.Number) ? 1 : 0;
                }
            }

            sb.Append("| ").Append(profile.Name)
              .Append(" | ").Append(total.ToString(CultureInfo.InvariantCulture))
              .Append(" | ").Append(impl.ToString(CultureInfo.InvariantCulture))
              .Append(" | ").Append(byLevel[0].ToString(CultureInfo.InvariantCulture))
              .Append(" | ").Append(byLevel[1].ToString(CultureInfo.InvariantCulture))
              .Append(" | ").Append(byLevel[2].ToString(CultureInfo.InvariantCulture))
              .Append(" | ").Append(notAuto.ToString(CultureInfo.InvariantCulture))
              .Append(" | ").Append(gaps.ToString(CultureInfo.InvariantCulture))
              .Append(" | ").Append(others.ToString(CultureInfo.InvariantCulture))
              .Append(" |\n");
        }

        return sb.Append('\n').ToString();
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
