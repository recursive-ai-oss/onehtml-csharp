using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Accessibility.Aria;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

// ACT 674b10 "Role attribute has valid value": a non-empty role attribute on an element that is not hidden from assistive
// technology has at least one token naming a non-abstract role (WAI-ARIA 1.2 section 8.1, fallback roles).
internal sealed class AriaRolesRule : AriaRule
{
    public AriaRolesRule()
        : base(
            "aria-roles",
            "A role attribute must contain at least one token that names a WAI-ARIA role authors may use.",
            "Correct the role name, or remove the attribute and use an element that already has the meaning.",
            null,
            "Passing a list of tokens when one of them is valid is allowed (fallback roles), so a failure means no token is usable and the intended role is unknown.",
            actIds: ["674b10"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node el in Elements(context))
        {
            if (!IsHtmlOrSvg(el) || el.GetAttribute("role") is not { } value || IsBlank(value) || !context.IsExposed(el))
            {
                continue;
            }

            List<Finding>? bad = null;
            bool valid = false;
            foreach (string token in Tokens(value))
            {
                AriaRole? role = AriaTaxonomy.FindRole(token) ?? (EqualsAscii(token, "image") ? AriaTaxonomy.FindRole("img") : null);
                if (role is { IsAbstract: false })
                {
                    valid = true;
                }
                else
                {
                    (bad ??= []).Add(new Finding("role", token, role is null ? token + " is not a WAI-ARIA role." : token + " is an abstract role, which authors may not use."));
                }
            }

            if (valid)
            {
                reporter.Passed(el, "The role attribute has a usable token.");
            }
            else
            {
                reporter.Failed(el, "No token of the role attribute names a usable role.", bad);
            }
        }
    }
}

// axe "aria-deprecated-role": the role that applies must not be one the specifications deprecate.
// directory: WAI-ARIA 1.2 section 5.3.x, "Deprecated in ARIA 1.2", use list. doc-biblioentry and doc-endnote:
// DPUB-ARIA 1.1, "Deprecated in DPUB-ARIA 1.1".
internal sealed class AriaDeprecatedRoleRule : AriaRule
{
    private static readonly HashSet<string> s_deprecated = new(StringComparer.Ordinal) { "directory", "doc-biblioentry", "doc-endnote" };

    public AriaDeprecatedRoleRule()
        : base(
            "aria-deprecated-role",
            "The role that applies to an element must not be one that WAI-ARIA or its digital publishing module has deprecated.",
            "Replace the deprecated role with the role the specification recommends in its place.",
            "Replace the role directory with list, the replacement WAI-ARIA 1.2 names.",
            null)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node el in Elements(context))
        {
            if (!IsHtmlOrSvg(el) || el.GetAttribute("role") is not { } value || IsBlank(value) || !context.IsExposed(el))
            {
                continue;
            }

            string? chosen = null;
            foreach (string token in Tokens(value))
            {
                AriaRole? role = AriaTaxonomy.FindRole(token);
                if (role is { IsAbstract: false })
                {
                    chosen = token;
                    break;
                }
            }

            if (chosen is null)
            {
                continue;
            }

            string? lower = null;
            foreach (string d in s_deprecated)
            {
                if (EqualsAscii(chosen, d))
                {
                    lower = d;
                }
            }

            if (lower is null)
            {
                reporter.Passed(el);
                continue;
            }

            List<SuggestedEdit>? fix = null;
            if (lower == "directory")
            {
                var tokens = Tokens(value);
                int at = tokens.FindIndex(t => EqualsAscii(t, "directory"));
                tokens[at] = "list";
                fix = [reporter.Suggest(Editing.Edit.SetAttribute(el, "role", string.Join(' ', tokens)), "Use the role list instead of directory.")];
            }

            reporter.Failed(el, "The role " + lower + " is deprecated.", [new Finding("role", chosen, lower + " is deprecated.")], fix);
        }
    }
}

// ACT 4e8ab6 "Element with role attribute has required states and properties".
internal sealed class AriaRequiredAttrRule : AriaRule
{
    public AriaRequiredAttrRule()
        : base(
            "aria-required-attr",
            "An element with an explicit role must set the states and properties that role requires.",
            "Add the required attribute with a value that matches the element's state.",
            null,
            "The value of a required state (checked, the current value of a slider) is the page's own data, so the library cannot choose it.",
            actIds: ["4e8ab6"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (AxNode node in context.Tree.Nodes)
        {
            if (node.Kind != AxNodeKind.Element || !context.IsExposed(node.DomNode))
            {
                continue;
            }

            NodeRole nr = context.Tree.Roles[node.DomNode];
            if (nr.ExplicitRole is not { } explicitRole || nr.ImplicitRole is { } implicitRole && implicitRole.Name == explicitRole.Name)
            {
                continue;
            }

            List<Finding>? bad = null;
            foreach (RoleDiagnostic d in nr.Diagnostics)
            {
                if (d.Kind == RoleDiagnosticKind.MissingRequiredAttribute)
                {
                    (bad ??= []).Add(new Finding(d.Attribute, d.Value, d.Message));
                }
            }

            // ACT 4e8ab6 wants the required attribute set and not empty; the role layer treats an empty value as absent.
            if (nr.Role is { } r)
            {
                foreach (string req in r.RequiredAttributes)
                {
                    if (node.DomNode.GetAttribute(req) is { } v && IsBlank(v) && (bad is null || !bad.Exists(f => f.Attribute == req)))
                    {
                        (bad ??= []).Add(new Finding(req, v, req + " is required by the role " + r.Name + " but is empty."));
                    }
                }
            }

            if (bad is null)
            {
                reporter.Passed(node.DomNode);
            }
            else
            {
                reporter.Failed(node.DomNode, "The role " + explicitRole.Name + " is missing required states or properties.", bad);
            }
        }
    }
}

// axe "aria-hidden-body": aria-hidden="true" on body hides the whole page from assistive technology (WAI-ARIA 1.2 section 6.6.1).
internal sealed class AriaHiddenBodyRule : AriaRule
{
    public AriaHiddenBodyRule()
        : base(
            "aria-hidden-body",
            "The body element must not have aria-hidden=\"true\", which hides the whole page from assistive technology.",
            "Remove aria-hidden from body; hide only the parts of the page that need hiding.",
            "Remove aria-hidden=\"true\" from body. No reading of the page is served by hiding all of it.",
            null)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        Node body = context.Document.Root.QuerySelector("body");
        if (body.IsNull)
        {
            return;
        }

        string? v = body.GetAttribute("aria-hidden");
        if (EqualsAscii(v, "true"))
        {
            reporter.Failed(body, "aria-hidden=\"true\" on body hides the whole page.", [new Finding("aria-hidden", v, "The page is hidden from assistive technology.")],
                [RemoveAttribute(reporter, body, "aria-hidden")]);
        }
        else
        {
            reporter.Passed(body);
        }
    }
}

// ACT 46ca7f "Element marked as decorative is not exposed": img with alt="" or role none/presentation must end up
// with the role none or presentation, which conflict resolution denies when the element is focusable or has a global
// WAI-ARIA attribute (WAI-ARIA 1.2 section 8.4, Presentational Roles Conflict Resolution).
internal sealed class PresentationRoleConflictRule : AriaRule
{
    public PresentationRoleConflictRule()
        : base(
            "presentation-role-conflict",
            "An element marked as decorative must really be presentational: it may not be focusable or carry a global WAI-ARIA attribute.",
            "Either make the element non-interactive without WAI-ARIA attributes, or drop the presentational role and give it a real one.",
            null,
            "Two fixes exist (remove the role or remove what conflicts with it) and which one is right depends on the author's intent.",
            actIds: ["46ca7f"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node el in Elements(context))
        {
            if (!IsHtmlOrSvg(el))
            {
                continue;
            }

            bool decorative = false;
            if (el.GetAttribute("role") is { } roleValue && !IsBlank(roleValue))
            {
                decorative = RoleComputer.ResolveExplicitRole(roleValue) is { Name: "none" or "presentation" };
            }

            if (!decorative && el.LocalName == "img" && el.NamespaceURI == "http://www.w3.org/1999/xhtml" && el.GetAttribute("alt") is { Length: 0 })
            {
                decorative = true;
            }

            if (!decorative)
            {
                continue;
            }

            if (!context.IsExposed(el))
            {
                reporter.Passed(el, "The element is not in the accessibility tree.");
                continue;
            }

            NodeRole nr = context.Tree.Roles[el];

            if (nr.Role is { Name: "none" or "presentation" } || nr.Role is null && nr.Origin == RoleOrigin.None)
            {
                reporter.Passed(el);
            }
            else
            {
                string why = nr.IsFocusable ? "it is focusable" : "it has a global WAI-ARIA attribute";
                reporter.Failed(el, "The element is marked as decorative but has the role " + (nr.Role?.Name ?? "none") + " because " + why + ".",
                    [new Finding("role", el.GetAttribute("role"), "The presentational role is ignored because " + why + ".")]);
            }
        }
    }
}

// axe "aria-text": the role text (a WAI-ARIA 1.3 draft idea some authors use) marks inline text that must not contain
// anything focusable, since the role flattens its descendants.
internal sealed class AriaTextRule : AriaRule
{
    public AriaTextRule()
        : base(
            "aria-text",
            "An element with role=\"text\" must not contain focusable elements.",
            "Move the focusable element out of the role=\"text\" element, or remove the role.",
            null,
            "Moving a control or dropping the role changes the page's structure, which is an authoring decision.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        bool[]? below = null;
        foreach (Node el in Elements(context))
        {
            if (el.GetAttribute("role") is not { } value || !EqualsAscii(Tokens(value) is { Count: > 0 } t ? t[0] : null, "text") || !context.IsExposed(el))
            {
                continue;
            }

            below ??= SubtreeHas(context, n => context.Find(n) is { } ax && ax.Focus.IsFocusable, includeSelf: false);
            if (below[el.Index])
            {
                reporter.Failed(el, "The element has role=\"text\" and contains a focusable element.");
            }
            else
            {
                reporter.Passed(el);
            }
        }
    }
}
