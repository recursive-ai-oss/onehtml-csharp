using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

internal static class ListRuleSet
{
    internal static IEnumerable<IAccessibilityRule> Create()
    {
        yield return new ListRule();
        yield return new ListItemRule();
        yield return new DefinitionListRule();
        yield return new DefinitionItemRule();
    }

    /// <summary>The first token of the role attribute, ASCII lower case, or null when there is none.</summary>
    internal static string? RoleToken(Node e)
    {
        if (e.GetAttribute("role") is not { } v)
        {
            return null;
        }

        int i = 0;
        while (i < v.Length && v[i] is ' ' or '\t' or '\n' or '\f' or '\r')
        {
            i++;
        }

        int s = i;
        while (i < v.Length && !(v[i] is ' ' or '\t' or '\n' or '\f' or '\r'))
        {
            i++;
        }

        return i > s ? v[s..i].ToLowerInvariantAscii() : null;
    }
}

// axe list, WCAG 1.3.1 (HTML 4.4.5 and 4.4.6 content models): a ul or ol holds li elements, plus script-supporting
// elements. A list item given another role is no longer a list item, and loose text is not part of any item.
internal sealed class ListRule : StructureRule
{
    public ListRule()
        : base("list", "A ul or ol may only directly contain li, script and template elements.", "Move other elements and text into li elements, or out of the list.", null,
            "Where stray content belongs, and whether it should be a list item at all, is the author's decision.")
    {
    }

    public override RuleScope Scope => RuleScope.Node | RuleScope.Descendants;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node list in Elements(context))
        {
            reporter.CancellationToken.ThrowIfCancellationRequested();
            if (!(IsHtml(list, "ul") || IsHtml(list, "ol")) || ListRuleSet.RoleToken(list) is not (null or "list") || !IsRendered(context, list))
            {
                continue;
            }

            var findings = new List<Finding>();
            for (Node ch = list.FirstChild; !ch.IsNull; ch = ch.NextSibling)
            {
                if (ch.Kind == NodeKind.Text)
                {
                    if (!IsBlank(ch.Data))
                    {
                        findings.Add(new Finding(null, null, "Text directly inside the list."));
                    }

                    continue;
                }

                if (!ch.IsElement || !IsRendered(context, ch) || IsHtml(ch, "script") || IsHtml(ch, "template"))
                {
                    continue;
                }

                string? role = ListRuleSet.RoleToken(ch);
                if (role == "listitem" || (IsHtml(ch, "li") && role is null))
                {
                    continue;
                }

                findings.Add(role is not null
                    ? new Finding("role", role, "A child with the role " + role + " is not a list item.")
                    : new Finding(null, ch.LocalName, "A " + ch.LocalName + " element directly inside the list."));
            }

            if (findings.Count == 0)
            {
                reporter.Passed(list);
            }
            else
            {
                reporter.Failed(list, "The list has children that are not list items.", findings);
            }
        }
    }
}

// axe listitem, WCAG 1.3.1. An li belongs in a ul, ol or menu (HTML 4.4.8) or in an element with the role list. Only the
// orphan li has no suggested edit: wrapping it in a ul would change the layout (default list indentation), and whether the
// item belongs in a ul, an ol or a role=list container is the author's decision (see docs/rules/structure.md).
internal sealed class ListItemRule : StructureRule
{
    public ListItemRule()
        : base("listitem", "An li element must be inside a ul, an ol or a list.", "Put the li in a ul or ol, or give its parent the role list.",
            null, "Whether the item belongs in a ul, an ol or another list, and what the wrapper does to the layout, depends on what the author meant.")
    {
    }

    public override RuleScope Scope => RuleScope.Node | RuleScope.Ancestors | RuleScope.Siblings;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node li in Elements(context))
        {
            reporter.CancellationToken.ThrowIfCancellationRequested();
            if (!IsHtml(li, "li") || ListRuleSet.RoleToken(li) is not (null or "listitem") || !IsRendered(context, li))
            {
                continue;
            }

            // Inside aria-hidden content (a closed modal's panel) no assistive technology reads the item, so its place in a list is not
            // perceivable; axe-core reports nothing for such items either.
            if (!context.IsExposed(li))
            {
                continue;
            }

            Node parent = li.ParentElement;
            if (!parent.IsNull && IsListContainer(parent))
            {
                reporter.Passed(li);
                continue;
            }

            reporter.Failed(li, "The li is not inside a ul, an ol or a list.", [new Finding(null, parent.IsNull ? null : parent.LocalName, "Its parent is not a list.")]);
        }
    }

    private static bool IsListContainer(Node parent)
    {
        string? role = ListRuleSet.RoleToken(parent);
        if (role is "list")
        {
            return true;
        }

        // A ul or ol with no role, or presentational (then its items are presentational too, ARIA 1.2 presentational roles inheritance).
        return (IsHtml(parent, "ul") || IsHtml(parent, "ol") || IsHtml(parent, "menu")) && role is null or "none" or "presentation";
    }
}

// axe definition-list, WCAG 1.3.1 (HTML 4.4.9): a dl is a sequence of groups, each one or more dt then one or more dd,
// optionally each group wrapped in a div. An empty dl is fine. Loose text and other elements are not.
internal sealed class DefinitionListRule : StructureRule
{
    public DefinitionListRule()
        : base("definition-list", "A dl may only directly contain well-ordered dt and dd groups, div, script and template elements.", "Order the terms and descriptions as dt then dd, and move other content out of the dl.", null,
            "Which terms and descriptions belong together is the author's decision.")
    {
    }

    public override RuleScope Scope => RuleScope.Node | RuleScope.Descendants;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node dl in Elements(context))
        {
            reporter.CancellationToken.ThrowIfCancellationRequested();
            if (!IsHtml(dl, "dl") || ListRuleSet.RoleToken(dl) is not null || !IsRendered(context, dl))
            {
                continue;
            }

            string? problem = Validate(context, dl);
            if (problem is null)
            {
                reporter.Passed(dl);
            }
            else
            {
                reporter.Failed(dl, problem, [new Finding(null, null, problem)]);
            }
        }
    }

    private enum State { Start, Terms, Descriptions }

    private static string? Validate(CheckContext c, Node dl)
    {
        State state = State.Start;
        for (Node ch = dl.FirstChild; !ch.IsNull; ch = ch.NextSibling)
        {
            if (ch.Kind == NodeKind.Text)
            {
                if (!IsBlank(ch.Data))
                {
                    return "Text directly inside the dl.";
                }

                continue;
            }

            if (!ch.IsElement || !IsRendered(c, ch) || IsHtml(ch, "script") || IsHtml(ch, "template"))
            {
                continue;
            }

            if (IsHtml(ch, "div"))
            {
                // A div holds one whole group: terms, then descriptions.
                State inner = State.Start;
                for (Node g = ch.FirstChild; !g.IsNull; g = g.NextSibling)
                {
                    if (g.Kind == NodeKind.Text && !IsBlank(g.Data))
                    {
                        return "Text inside a div of the dl.";
                    }

                    if (!g.IsElement || !IsRendered(c, g) || IsHtml(g, "script") || IsHtml(g, "template"))
                    {
                        continue;
                    }

                    if (!Step(ref inner, g, out string? p))
                    {
                        return p;
                    }
                }

                if (inner != State.Descriptions)
                {
                    return "A div of the dl does not hold a term followed by a description.";
                }

                if (state == State.Terms)
                {
                    return "A term has no description before the next group.";
                }

                state = State.Descriptions;
                continue;
            }

            if (!Step(ref state, ch, out string? problem))
            {
                return problem;
            }
        }

        return state == State.Terms ? "The last term has no description." : null;
    }

    private static bool Step(ref State state, Node el, out string? problem)
    {
        problem = null;
        if (IsHtml(el, "dt"))
        {
            state = State.Terms;
            return true;
        }

        if (IsHtml(el, "dd"))
        {
            if (state == State.Start)
            {
                problem = "A description comes before any term.";
                return false;
            }

            state = State.Descriptions;
            return true;
        }

        problem = "A " + el.LocalName + " element directly inside the dl.";
        return false;
    }
}

// axe dlitem, WCAG 1.3.1: dt and dd belong in a dl, directly or through a div (HTML 4.4.10 and 4.4.11).
internal sealed class DefinitionItemRule : StructureRule
{
    public DefinitionItemRule()
        : base("dlitem", "A dt or dd element must be inside a dl.", "Put the dt and dd elements in a dl.", null,
            "Whether the content is a definition list at all is the author's decision.")
    {
    }

    public override RuleScope Scope => RuleScope.Node | RuleScope.Ancestors;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node e in Elements(context))
        {
            if (!(IsHtml(e, "dt") || IsHtml(e, "dd")) || !IsRendered(context, e))
            {
                continue;
            }

            Node p = e.ParentElement;
            bool ok = IsHtml(p, "dl") || (IsHtml(p, "div") && IsHtml(p.ParentElement, "dl"));
            if (ok)
            {
                reporter.Passed(e);
            }
            else
            {
                reporter.Failed(e, "The " + e.LocalName + " is not inside a dl.", [new Finding(null, p.IsNull ? null : p.LocalName, "Its parent is not a dl.")]);
            }
        }
    }
}
