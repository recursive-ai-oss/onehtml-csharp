using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RecursiveAI.OneHtml.Accessibility.Aria;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

/// <summary>
/// Base of the built-in ARIA rules. The descriptor is assembled from the catalogue: an axe rule gives the criteria,
/// best-practice flag, tags and impact; an ACT-only rule gives them from the ACT record. Descriptions and help texts are
/// written for this library.
/// </summary>
internal abstract class AriaRule : IAccessibilityRule
{
    private const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    protected AriaRule(string id, string description, string help, string? fix, string? noFixReason, bool axe = true, IReadOnlyList<string>? actIds = null, Impact? impact = null, IReadOnlyList<string>? extraTags = null, IReadOnlyList<string>? criteria = null, RuleNeeds needs = RuleNeeds.Static)
    {
        Description = description;
        Help = help;
        actIds ??= [];
        var sources = new List<RuleSource>();
        var tags = new List<string>(extraTags ?? []);
        bool bestPractice;
        var crit = new List<string>(criteria ?? []);
        if (axe)
        {
            AxeRule a = AxeRules.Find(id) ?? throw new InvalidOperationException("Not an axe rule: " + id);
            sources.Add(RuleSource.Axe(id));
            tags.AddRange(a.Tags);
            bestPractice = a.IsBestPractice;
            crit.AddRange(a.CriterionNumbers);
            Impact = impact ?? ParseImpact(a.Impact);
        }
        else
        {
            bestPractice = false;
            Impact = impact ?? Impact.Serious;
        }

        foreach (string act in actIds)
        {
            ActRule r = ActRules.Find(act) ?? throw new InvalidOperationException("Unknown ACT rule " + act);
            sources.Add(RuleSource.Act(act));
            if (!axe)
            {
                crit.AddRange(r.Criteria);
            }
        }

        if (!axe && actIds.Count == 0)
        {
            sources.Add(RuleSource.Own(id));
        }

        Descriptor = new RuleDescriptor(id, sources, crit.Distinct(StringComparer.Ordinal), bestPractice, needs, fix, noFixReason, tags.Distinct(StringComparer.Ordinal));
    }

    public RuleDescriptor Descriptor { get; }

    public string Description { get; }

    public string Help { get; }

    public Impact Impact { get; }

    public virtual RuleScope Scope => RuleScope.Unspecified;

    public virtual IReadOnlyCollection<string>? RelevantElements => null;

    public abstract void Evaluate(CheckContext context, RuleReporter reporter);

    private static Impact ParseImpact(string s) => s switch
    {
        "minor" => Impact.Minor,
        "moderate" => Impact.Moderate,
        "serious" => Impact.Serious,
        "critical" => Impact.Critical,
        _ => throw new InvalidOperationException("Unknown impact " + s),
    };

    // ---- helpers shared by the rules ----

    /// <summary>Every element of the document in document order (template contents excluded).</summary>
    protected static IEnumerable<Node> Elements(CheckContext c) => c.Elements();

    /// <summary>True for HTML and SVG elements, the two namespaces the ACT ARIA rules apply to.</summary>
    protected static bool IsHtmlOrSvg(Node e) => e.NamespaceURI is XhtmlNamespace or SvgNamespace;

    protected static bool IsBlank(ReadOnlySpan<char> v)
    {
        foreach (char ch in v)
        {
            if (!(ch is ' ' or '\t' or '\n' or '\f' or '\r'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Splits on the ASCII whitespace of the HTML Standard.</summary>
    protected static List<string> Tokens(string value)
    {
        var list = new List<string>();
        int i = 0;
        while (i < value.Length)
        {
            while (i < value.Length && value[i] is ' ' or '\t' or '\n' or '\f' or '\r')
            {
                i++;
            }

            int start = i;
            while (i < value.Length && !(value[i] is ' ' or '\t' or '\n' or '\f' or '\r'))
            {
                i++;
            }

            if (i > start)
            {
                list.Add(value[start..i]);
            }
        }

        return list;
    }

    protected static bool EqualsAscii(string? a, string b) => a is not null && Ascii.EqualsIgnoreCase(a, b);

    /// <summary>True for a defined ARIA state or property name.</summary>
    protected static bool IsAriaName(string qualifiedName) => qualifiedName.StartsWith("aria-", StringComparison.Ordinal);

    /// <summary>
    /// For every node index, whether the node or a descendant in the DOM satisfies <paramref name="test"/>
    /// (<paramref name="includeSelf"/> false: descendants only). One reverse pass over document order, linear in the document.
    /// </summary>
    protected static bool[] SubtreeHas(CheckContext c, Func<Node, bool> test, bool includeSelf)
    {
        Document doc = c.Document;
        var own = new bool[doc.NodeCount];
        var sub = new bool[doc.NodeCount];
        var all = new List<Node>(c.AllElements());
        for (int i = all.Count - 1; i >= 0; i--)
        {
            Node n = all[i];
            own[n.Index] = test(n);
            bool childHas = sub[n.Index];
            sub[n.Index] = childHas || (includeSelf && own[n.Index]);
            // sub[] of a node holds "a descendant (or the node itself, if includeSelf) satisfies": fold it into the parent.
            Node p = n.ParentElement;
            if (!p.IsNull)
            {
                sub[p.Index] |= childHas || own[n.Index];
            }
        }

        return sub;
    }

    protected static SuggestedEdit RemoveAttribute(RuleReporter r, Node element, string attribute) =>
        r.Suggest(Editing.Edit.RemoveAttribute(element, attribute), "Remove the " + attribute + " attribute.");
}
