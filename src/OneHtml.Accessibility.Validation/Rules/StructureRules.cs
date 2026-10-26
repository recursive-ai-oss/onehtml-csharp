using System;
using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneHtml.Accessibility.Aria;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

// ACT bc4a75 "ARIA required owned elements": an element with an explicit role that requires owned elements must have
// them in the accessibility tree, aria-owns re-parenting included (WAI-ARIA 1.2 section 5.2.4). Combobox and busy
// widgets are exempt, as the ACT rule says.
internal sealed class AriaRequiredChildrenRule : AriaRule
{
    public AriaRequiredChildrenRule()
        : base(
            "aria-required-children",
            "An element whose role needs owned elements (a list needs items, a table needs rows) must contain them.",
            "Add the required child elements, or use a role that needs none.",
            null,
            "Adding rows, items or options is content, and wrapping existing children can change the page's structure, so the right edit is the author's.",
            actIds: ["bc4a75"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter) =>
        StructureDiagnosticRules.Run(context, reporter, owned: true);
}

// ACT ff89c9 "ARIA required context role": an element with an explicit role that needs a parent role (a listitem needs
// a list) must sit inside one in the accessibility tree (WAI-ARIA 1.2 section 5.2.3).
internal sealed class AriaRequiredParentRule : AriaRule
{
    public AriaRequiredParentRule()
        : base(
            "aria-required-parent",
            "An element whose role needs a parent role (a list item needs a list, a tab needs a tab list) must be inside one.",
            "Put the element inside an element with the required role, or use a role that needs no parent.",
            null,
            "Wrapping orphans in a container changes layout and styling, and which container role is right depends on the content.",
            actIds: ["ff89c9"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter) =>
        StructureDiagnosticRules.Run(context, reporter, owned: false);
}

internal static class StructureDiagnosticRules
{
    internal static void Run(CheckContext context, RuleReporter reporter, bool owned)
    {
        int counter = 0;
        foreach (AxNode node in context.Tree.Nodes)
        {
            if ((++counter & 1023) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (node.Kind != AxNodeKind.Element || !context.IsExposed(node.DomNode))
            {
                continue;
            }

            NodeRole nr = context.Tree.Roles[node.DomNode];
            if (nr.ExplicitRole is not { } explicitRole || nr.ImplicitRole is { } implicitRole && implicitRole.Name == explicitRole.Name)
            {
                continue;
            }

            if (node.AriaRole is not { } role || role.Name != explicitRole.Name)
            {
                continue;
            }

            if (owned ? role.RequiredOwned.Count == 0 || role.Name == "combobox" || IsBusy(node) : role.RequiredContext.Count == 0)
            {
                continue;
            }

            string? problem = owned ? CheckOwned(node, role) : CheckContext(node, role, context);
            if (problem is null)
            {
                reporter.Passed(node.DomNode);
            }
            else
            {
                reporter.Failed(node.DomNode, problem, [new Finding("role", role.Name, problem)]);
            }
        }
    }

    // ACT bc4a75 exempts an element with aria-busy=true or a tree ancestor that has it.
    private static bool IsBusy(AxNode node)
    {
        for (AxNode? a = node; a is not null; a = a.Parent)
        {
            if (a.Kind == AxNodeKind.Element && a.States.Busy)
            {
                return true;
            }
        }

        return false;
    }

    // A node the tree ignores (a plain div, a presentational element) stands for its children: they belong to its parent.
    private static void Owned(AxNode parent, List<AxNode> into)
    {
        var stack = new Stack<(AxNode Node, int Next)>();
        stack.Push((parent, 0));
        while (stack.Count > 0)
        {
            (AxNode n, int next) = stack.Pop();
            if (next >= n.Children.Count)
            {
                continue;
            }

            stack.Push((n, next + 1));
            AxNode child = n.Children[next];
            if (child.Kind != AxNodeKind.Element)
            {
                continue;
            }

            if (child.Hidden.Reasons.HasFlag(HiddenReasons.AriaHidden) || child.Hidden.Reasons.HasFlag(HiddenReasons.Inert))
            {
                continue;
            }

            if (child.IsIgnored)
            {
                stack.Push((child, 0));
            }
            else
            {
                into.Add(child);
            }
        }
    }

    // ACT bc4a75: the target owns at least one element with a required owned role, and owns only such elements. A role
    // that sits between (the group of group > listitem) may nest and is checked in turn. WAI-ARIA 1.2 section 5.2.4.
    private static string? CheckOwned(AxNode node, AriaRole role)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        var between = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<IReadOnlyList<string>> chains = role.RequiredOwned;

        foreach (IReadOnlyList<string> chain in chains)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                allowed.Add(chain[i]);
                if (i < chain.Count - 1)
                {
                    between.Add(chain[i]);
                }
            }
        }

        var leaves = new HashSet<string>(StringComparer.Ordinal);
        foreach (IReadOnlyList<string> chain in chains)
        {
            if (chain.Count > 0)
            {
                leaves.Add(chain[^1]);
            }
        }

        bool foundLeaf = false;
        var work = new Stack<AxNode>();
        work.Push(node);
        var kids = new List<AxNode>();
        while (work.Count > 0)
        {
            AxNode owner = work.Pop();
            kids.Clear();
            Owned(owner, kids);
            foreach (AxNode k in kids)
            {
                string? kr = k.AriaRole?.Name;
                if (kr is null || !allowed.Contains(kr))
                {
                    return "The role " + role.Name + " owns an element with the role " + (kr ?? "generic") + ", which is not one of its required owned roles (" + string.Join(", ", allowed.Order(StringComparer.Ordinal)) + ").";
                }

                foundLeaf |= leaves.Contains(kr);
                if (between.Contains(kr))
                {
                    work.Push(k);
                }
            }
        }

        return foundLeaf ? null : "The role " + role.Name + " requires owned elements with the role " + string.Join(" or ", leaves.Order(StringComparer.Ordinal)) + ".";
    }

    // ACT ff89c9: the target's parent in the accessibility tree (ignored nodes skipped) has a required context role, possibly
    // through the roles that sit between them in the context role's owned chain. WAI-ARIA 1.2 section 5.2.3.
    private static string? CheckContext(AxNode node, AriaRole role, CheckContext context)
    {
        _ = context;
        AxNode? p = Parent(node);
        var between = new HashSet<string>(StringComparer.Ordinal);
        foreach (string ctx in role.RequiredContext)
        {
            if (AriaTaxonomy.FindRole(ctx) is not { } c)
            {
                continue;
            }

            foreach (IReadOnlyList<string> chain in c.RequiredOwned)
            {
                if (chain.Count > 1 && chain[^1] == role.Name)
                {
                    for (int i = 0; i < chain.Count - 1; i++)
                    {
                        between.Add(chain[i]);
                    }
                }
            }
        }

        while (p is not null)
        {
            string? pr = p.AriaRole?.Name;
            if (pr is not null && System.Linq.Enumerable.Contains(role.RequiredContext, pr))
            {
                return null;
            }

            if (pr is null || !between.Contains(pr))
            {
                break;
            }

            p = Parent(p);
        }

        return "The role " + role.Name + " requires a parent with the role " + string.Join(" or ", role.RequiredContext) + ".";
    }

    private static AxNode? Parent(AxNode n)
    {
        AxNode? p = n.Parent;
        while (p is not null && p.IsIgnored && p.Kind == AxNodeKind.Element)
        {
            p = p.Parent;
        }

        return p;
    }
}

// ACT 6cfa84: an element with aria-hidden="true" must not contain anything a keyboard user can tab to.
internal sealed class AriaHiddenFocusRule : AriaRule
{
    public AriaHiddenFocusRule()
        : base(
            "aria-hidden-focus",
            "An element with aria-hidden=\"true\" must not contain anything that can receive focus through the Tab key.",
            "Make the hidden content non-focusable (tabindex=\"-1\" or disabled), or stop hiding it.",
            null,
            "Hiding it from assistive technology, making it unfocusable and showing it are three different outcomes, and the author must choose.",
            actIds: ["6cfa84"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        bool[]? tabbable = null;
        foreach (Node el in Elements(context))
        {
            if (!EqualsAscii(el.GetAttribute("aria-hidden"), "true"))
            {
                continue;
            }

            tabbable ??= SubtreeHas(context, n => context.Find(n) is { } ax && ax.Focus.IsTabbable, includeSelf: true);
            if (tabbable[el.Index])
            {
                reporter.Failed(el, "The element is hidden with aria-hidden but contains something reachable with the Tab key.", [new Finding("aria-hidden", "true", "A focusable element is inside.")]);
            }
            else
            {
                reporter.Passed(el);
            }
        }
    }
}

// ACT 307n5z: a role whose children are presentational (button, checkbox, img, tab, …) hides its descendants from
// assistive technology, so none of them may be tabbable (WAI-ARIA 1.2 section 5.2.7, "Children Presentational").
internal sealed class PresentationalChildrenFocusableRule : AriaRule
{
    public PresentationalChildrenFocusableRule()
        : base(
            "presentational-children-focusable",
            "An element whose role makes its children presentational (a button, a tab, an image) must not contain anything reachable with the Tab key.",
            "Move the focusable element out, or change the outer role to one that exposes its children.",
            null,
            "Removing the focusable child, moving it or changing the outer role each change the page; the author must choose.",
            axe: false,
            actIds: ["307n5z"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        bool[]? tabbable = null;
        foreach (Node el in Elements(context))
        {
            if (!IsHtmlOrSvg(el) || context.Tree.Roles[el].Role is not { ChildrenPresentational: true } role)
            {
                continue;
            }

            tabbable ??= SubtreeHas(context, n => context.Find(n) is { } ax && ax.Focus.IsTabbable, includeSelf: false);
            if (tabbable[el.Index])
            {
                reporter.Failed(el, "The role " + role.Name + " makes its children presentational, yet it contains something reachable with the Tab key.");
            }
            else
            {
                reporter.Passed(el);
            }
        }
    }
}

// Accessible-name rules for widget roles that only exist by explicit role (axe aria-*-name rules); ACT m6b1q3 is the
// menuitem case. WCAG 4.1.2 Name, Role, Value (1.1.1 for meter and progressbar).
internal sealed class RoleNameRule : AriaRule
{
    private readonly HashSet<string> _roles;

    public RoleNameRule(string id, string description, string help, IReadOnlyList<string> roles, IReadOnlyList<string>? actIds = null)
        : base(id, description, help, null, "The name is content: only the author knows what the control is called.", actIds: actIds)
    {
        _roles = new HashSet<string>(roles, StringComparer.Ordinal);
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        int counter = 0;
        foreach (AxNode node in context.Tree.Nodes)
        {
            if ((++counter & 1023) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (node.Kind != AxNodeKind.Element || node.RoleOrigin != RoleOrigin.Explicit || node.AriaRole is not { } role || !_roles.Contains(role.Name) || !context.IsExposed(node.DomNode))
            {
                continue;
            }

            if (IsBlank(node.Name.Value))
            {
                reporter.Failed(node.DomNode, "The " + role.Name + " has no accessible name.", [new Finding("role", role.Name, "No accessible name could be computed.")]);
            }
            else
            {
                reporter.Passed(node.DomNode);
            }
        }
    }
}
