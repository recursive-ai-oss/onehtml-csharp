using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>What we concluded about one element for one rule, after collapsing duplicates.</summary>
/// <param name="Class">Coarse outcome: failed is <see cref="AxeClass.Violation"/>, needs-review and needs-render are <see cref="AxeClass.Incomplete"/>.</param>
/// <param name="Detail">The precise outcome: <c>fail</c>, <c>pass</c>, <c>review</c> or <c>render</c>.</param>
/// <param name="Message">Our message for the result (kept for examples).</param>
internal readonly record struct OurNode(AxeClass Class, string Detail, string Message);

/// <summary>One element axe reported for a rule, resolved to a node index of our parse (or -1).</summary>
internal readonly record struct TheirNode(int Index, AxeClass Class, string Where, string Html, string How, string Message);

/// <summary>One example of a mismatch class, kept small and without page content beyond a tag-sized excerpt.</summary>
internal sealed record AxeExample(string Page, string Where, string Html, string Ours, string Axe);

/// <summary>Per-rule counts over one or many pages. Merges by addition.</summary>
internal sealed class RuleTally
{
    public int PagesAxe;           // pages where axe reported at least one node (any group)
    public int PagesOurs;          // pages where we reported at least one node
    public int PagesBoth;
    public long NodesAxe;
    public long NodesOurs;
    public long Agree;             // same coarse outcome on the same element
    public long AgreeReview;       // of Agree: both incomplete, ours needs-review
    public long AgreeRender;       // of Agree: both incomplete, ours needs-render
    public long Contained;         // unmatched elements paired with an axe node on an ancestor or descendant (axe reports the table, we the cells)
    public long Collapsed;         // our failures axe reports once per group (GroupRules)
    public long Unresolved;        // axe nodes we could not find in our parse (nested targets, selector misses)
    public long Disagreeing;       // axe nodes where the XPath and the selector named different elements
    public readonly SortedDictionary<string, long> Classes = new(StringComparer.Ordinal);
    public readonly SortedDictionary<string, List<AxeExample>> Examples = new(StringComparer.Ordinal);

    public void Merge(RuleTally o, int exampleCap)
    {
        PagesAxe += o.PagesAxe;
        PagesOurs += o.PagesOurs;
        PagesBoth += o.PagesBoth;
        NodesAxe += o.NodesAxe;
        NodesOurs += o.NodesOurs;
        Agree += o.Agree;
        AgreeReview += o.AgreeReview;
        AgreeRender += o.AgreeRender;
        Contained += o.Contained;
        Collapsed += o.Collapsed;
        Unresolved += o.Unresolved;
        Disagreeing += o.Disagreeing;
        foreach ((string k, long v) in o.Classes)
        {
            Classes[k] = Classes.GetValueOrDefault(k) + v;
        }

        foreach ((string k, List<AxeExample> list) in o.Examples)
        {
            if (!Examples.TryGetValue(k, out List<AxeExample>? mine))
            {
                Examples[k] = mine = [];
            }

            foreach (AxeExample e in list)
            {
                if (mine.Count < exampleCap)
                {
                    mine.Add(e);
                }
            }
        }
    }

    public JsonObject ToJson()
    {
        var classes = new JsonObject();
        foreach ((string k, long v) in Classes)
        {
            classes[k] = v;
        }

        var examples = new JsonObject();
        foreach ((string k, List<AxeExample> list) in Examples)
        {
            var arr = new JsonArray();
            foreach (AxeExample e in list)
            {
                arr.Add(new JsonObject { ["page"] = e.Page, ["where"] = e.Where, ["html"] = e.Html, ["ours"] = e.Ours, ["axe"] = e.Axe });
            }

            examples[k] = arr;
        }

        return new JsonObject
        {
            ["pagesAxe"] = PagesAxe, ["pagesOurs"] = PagesOurs, ["pagesBoth"] = PagesBoth, ["nodesAxe"] = NodesAxe, ["nodesOurs"] = NodesOurs,
            ["agree"] = Agree, ["agreeReview"] = AgreeReview, ["agreeRender"] = AgreeRender, ["contained"] = Contained, ["collapsed"] = Collapsed, ["unresolved"] = Unresolved, ["disagreeing"] = Disagreeing,
            ["classes"] = classes, ["examples"] = examples,
        };
    }

    public static RuleTally FromJson(JsonNode n)
    {
        var t = new RuleTally
        {
            PagesAxe = (int)n["pagesAxe"]!, PagesOurs = (int)n["pagesOurs"]!, PagesBoth = (int)n["pagesBoth"]!, NodesAxe = (long)n["nodesAxe"]!,
            NodesOurs = (long)n["nodesOurs"]!, Agree = (long)n["agree"]!, AgreeReview = (long)n["agreeReview"]!, AgreeRender = (long)n["agreeRender"]!,
            Contained = (long)n["contained"]!, Collapsed = (long)n["collapsed"]!, Unresolved = (long)n["unresolved"]!, Disagreeing = (long)n["disagreeing"]!,
        };
        foreach ((string k, JsonNode? v) in n["classes"]!.AsObject())
        {
            t.Classes[k] = (long)v!;
        }

        foreach ((string k, JsonNode? v) in n["examples"]!.AsObject())
        {
            t.Examples[k] = [.. v!.AsArray().Select(e => new AxeExample((string)e!["page"]!, (string)e["where"]!, (string)e["html"]!, (string)e["ours"]!, (string)e["axe"]!))];
        }

        return t;
    }
}

/// <summary>
/// The per-node comparison of our results with axe's for one page, and the normalizations it applies. Normalizations (all documented in
/// <c>docs/axe-differential.md</c>): results are keyed by (rule, element); several results of one rule on one element collapse to the
/// most severe (fail, then render, then review, then pass); a text-node target counts for its parent element; our Inapplicable results
/// and axe's inapplicable group carry no nodes; needs-review and needs-render both correspond to axe's incomplete.
/// </summary>
internal static class AxeCompare
{
    public const int ExamplesPerPageClass = 2;

    private static int Severity(string detail) => detail switch { "fail" => 3, "render" => 2, "review" => 1, _ => 0 };

    private static int Severity(AxeClass c) => c switch { AxeClass.Violation => 3, AxeClass.Incomplete => 2, AxeClass.Pass => 1, _ => 0 };

    /// <summary>Adds a result of ours to a (rule, element) map, keeping the most severe.</summary>
    public static void Add(Dictionary<int, OurNode> map, int index, OurNode node)
    {
        if (!map.TryGetValue(index, out OurNode have) || Severity(node.Detail) > Severity(have.Detail))
        {
            map[index] = node;
        }
    }

    /// <summary>The class key of a mismatch or agreement: our precise outcome, a bar, then axe's group (<c>none</c> when absent).</summary>
    public static string Key(string? ours, AxeClass theirs) =>
        (ours ?? "none") + "|" + (theirs switch { AxeClass.Violation => "violation", AxeClass.Pass => "pass", AxeClass.Incomplete => "incomplete", _ => "none" });

    /// <summary>
    /// Rules for which axe reports a group of elements once (on its first member) where we report every member. For these, a
    /// failure (or review) of ours with no counterpart counts as collapsed when axe has a violation (or incomplete) of the same rule on the page.
    /// </summary>
    public static readonly HashSet<string> GroupRules = new(StringComparer.Ordinal)
    {
        "duplicate-id", "duplicate-id-active", "duplicate-id-aria", "accesskeys", "landmark-unique", "landmark-no-duplicate-banner",
        "landmark-no-duplicate-contentinfo", "landmark-no-duplicate-main", "identical-links-same-purpose",
    };

    /// <summary>
    /// Rules whose node granularity is not part of the finding: axe reports the wrappers or children it happens to reach, which
    /// differs from page to page with no stable rule. They are compared once per page: the strongest non-pass outcome each side reports.
    /// </summary>
    public static readonly HashSet<string> PageRules = new(StringComparer.Ordinal) { "region" };

    private static bool Related(int a, int b, Func<int, int> parent)
    {
        for (int x = a; x >= 0; x = parent(x))
        {
            if (x == b)
            {
                return true;
            }
        }

        for (int x = b; x >= 0; x = parent(x))
        {
            if (x == a)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Compares one rule on one page.</summary>
    /// <param name="ours">Our elements for the rule, or null when the rule is not implemented here.</param>
    /// <param name="theirs">Axe's nodes for the rule.</param>
    /// <param name="page">Short page id for examples.</param>
    /// <param name="describe">Describes one of our elements for an example (tag excerpt).</param>
    /// <param name="parent">Parent element index of an element index, or -1; enables the containment pairing.</param>
    /// <param name="rule">The rule id; decides whether group collapsing applies.</param>
    /// <param name="subsumed">Elements where a failure of ours is already covered by a stricter rule axe reports a violation for (axe leaves the enhanced contrast rule at pass there); a failure of ours on one counts as collapsed.</param>
    public static RuleTally CompareRule(IReadOnlyDictionary<int, OurNode>? ours, IReadOnlyList<TheirNode> theirs, string page, Func<int, string> describe, Func<int, int>? parent = null, string rule = "", ISet<int>? subsumed = null)
    {
        var t = new RuleTally();
        var theirMap = new Dictionary<int, TheirNode>();
        foreach (TheirNode n in theirs)
        {
            t.NodesAxe++;
            if (n.Index < 0)
            {
                t.Unresolved++;
                Example(t, "unresolved|" + (n.Class == AxeClass.Violation ? "violation" : n.Class == AxeClass.Pass ? "pass" : "incomplete"), page, n.Where, n.Html, string.Empty, n.How + " " + n.Message);
                continue;
            }

            if (n.How == "xpath(selector-error)")
            {
                // Our selector parser rejected a selector axe produced: a finding about the parser, not about the rule.
                t.Classes["unresolved|selector-error"] = t.Classes.GetValueOrDefault("unresolved|selector-error") + 1;
                Example(t, "unresolved|selector-error", page, n.Where, n.Html, string.Empty, n.How);
            }

            if (n.How == "xpath!selector")
            {
                t.Disagreeing++;
            }

            // Two axe nodes on one element (it happens when a selector matches a node that axe reports twice) collapse the same way.
            if (!theirMap.TryGetValue(n.Index, out TheirNode have) || Severity(n.Class) > Severity(have.Class))
            {
                theirMap[n.Index] = n;
            }
        }

        int oursCount = ours?.Count ?? 0;
        t.NodesOurs = oursCount;
        t.PagesAxe = theirs.Count > 0 ? 1 : 0;
        t.PagesOurs = oursCount > 0 ? 1 : 0;
        t.PagesBoth = t.PagesAxe & t.PagesOurs;
        if (ours is null)
        {
            return t;
        }

        if (PageRules.Contains(rule))
        {
            AxeClass axeTop = AxeClass.None;
            foreach (TheirNode n in theirs)
            {
                if (n.Class != AxeClass.Pass && Severity(n.Class) > Severity(axeTop))
                {
                    axeTop = n.Class;
                }
            }

            OurNode? ourTop = null;
            foreach (OurNode n in ours.Values)
            {
                if (n.Class != AxeClass.Pass && (ourTop is null || Severity(n.Class) > Severity(ourTop.Value.Class)))
                {
                    ourTop = n;
                }
            }

            if (axeTop == AxeClass.None && ourTop is null)
            {
                return t;
            }

            if (ourTop is { } top && top.Class == axeTop)
            {
                t.Agree++;
                if (top.Class == AxeClass.Incomplete)
                {
                    if (top.Detail == "review")
                    {
                        t.AgreeReview++;
                    }
                    else
                    {
                        t.AgreeRender++;
                    }
                }
            }
            else
            {
                Bump(t, Key(ourTop?.Detail, axeTop), page, string.Empty, string.Empty, ourTop?.Message ?? string.Empty, string.Empty);
            }

            return t;
        }

        var oursLeft = new List<(int Index, OurNode Node)>();
        foreach ((int index, OurNode our) in ours)
        {
            if (theirMap.TryGetValue(index, out TheirNode their))
            {
                if (subsumed is not null && subsumed.Contains(index) && our.Class == AxeClass.Violation && their.Class == AxeClass.Pass)
                {
                    t.Collapsed++;
                    continue;
                }

                if (our.Class == their.Class)
                {
                    t.Agree++;
                    if (our.Class == AxeClass.Incomplete)
                    {
                        if (our.Detail == "review")
                        {
                            t.AgreeReview++;
                        }
                        else
                        {
                            t.AgreeRender++;
                        }
                    }

                    continue;
                }

                // WCAG 2.5.8 exempts a control whose size the browser sets; axe has no such exception (docs/axe-differential.md). The class is
                // named apart so that the documented divergence is counted separately from open disagreements.
                string key = Key(our.Detail, their.Class);
                if (rule == "target-size" && key == "pass|violation" && our.Message.Contains("size the browser sets", StringComparison.Ordinal))
                {
                    key = "pass|violation (user agent control)";
                }

                Bump(t, key, page, their.Where, their.Html, our.Message, their.Message);
            }
            else
            {
                oursLeft.Add((index, our));
            }
        }

        var theirsLeft = theirMap.Where(kv => !ours.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
        var theirsAbsorbed = new HashSet<int>();

        // Containment: axe reports a table where we report its cells (or the reverse). Same group on a related element is one finding.
        if (parent is not null)
        {
            var oursStill = new List<(int Index, OurNode Node)>();
            foreach ((int index, OurNode our) in oursLeft)
            {
                TheirNode? hit = null;
                if (our.Class != AxeClass.Pass)
                {
                    foreach (TheirNode their in theirsLeft)
                    {
                        if (their.Class == our.Class && Related(index, their.Index, parent))
                        {
                            hit = their;
                            break;
                        }
                    }
                }

                if (hit is { } h)
                {
                    t.Contained++;
                    theirsAbsorbed.Add(h.Index);
                }
                else
                {
                    oursStill.Add((index, our));
                }
            }

            oursLeft = oursStill;
        }

        var axeClasses = new HashSet<AxeClass>(theirMap.Values.Select(n => n.Class));
        foreach ((int index, OurNode our) in oursLeft)
        {
            if (subsumed is not null && subsumed.Contains(index) && our.Class == AxeClass.Violation)
            {
                t.Collapsed++;
                continue;
            }

            if (GroupRules.Contains(rule) && our.Class != AxeClass.Pass && axeClasses.Any(c => c != AxeClass.Pass))
            {
                t.Collapsed++;
                continue;
            }

            Bump(t, Key(our.Detail, AxeClass.None), page, describe(index), string.Empty, our.Message, string.Empty);
        }

        foreach (TheirNode their in theirsLeft)
        {
            if (!theirsAbsorbed.Contains(their.Index))
            {
                Bump(t, Key(null, their.Class), page, their.Where, their.Html, string.Empty, their.Message);
            }
        }

        return t;
    }

    private static void Bump(RuleTally t, string key, string page, string where, string html, string oursMessage, string axeMessage)
    {
        t.Classes[key] = t.Classes.GetValueOrDefault(key) + 1;
        Example(t, key, page, where, html, oursMessage, axeMessage);
    }

    private static void Example(RuleTally t, string key, string page, string where, string html, string ours, string axe)
    {
        if (!t.Examples.TryGetValue(key, out List<AxeExample>? list))
        {
            t.Examples[key] = list = [];
        }

        if (list.Count < ExamplesPerPageClass)
        {
            list.Add(new AxeExample(page, Cut(where, 160), Cut(html, 160), Cut(ours, 160), Cut(axe, 200)));
        }
    }

    private static string Cut(string s, int n) => s.Length <= n ? s : s[..n];
}
