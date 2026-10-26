using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Accessibility.Aria;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

// ACT 5f99a7 "ARIA attribute is defined in WAI-ARIA": every attribute starting with aria- must name a state or property
// that WAI-ARIA defines. Elements are checked wherever they are, hidden or not, as the ACT rule's applicability says.
internal sealed class AriaValidAttrRule : AriaRule
{
    public AriaValidAttrRule()
        : base(
            "aria-valid-attr",
            "Every attribute that starts with aria- must be a state or property defined by WAI-ARIA.",
            "Fix the spelling of the attribute, or remove it if WAI-ARIA has no such state or property.",
            "Remove the aria- attribute that WAI-ARIA does not define. A browser ignores it, so removing it changes nothing a user perceives.",
            null,
            actIds: ["5f99a7"])
    {
    }

    public override RuleScope Scope => RuleScope.Node;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        int counter = 0;
        foreach (Node el in Elements(context))
        {
            if ((++counter & 1023) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (!IsHtmlOrSvg(el))
            {
                continue;
            }

            bool any = false;
            List<Finding>? bad = null;
            List<SuggestedEdit>? fixes = null;
            foreach (Attr a in el.Attributes)
            {
                string name = a.QualifiedName;
                if (!IsAriaName(name))
                {
                    continue;
                }

                any = true;
                if (AriaTaxonomy.FindProperty(name) is null)
                {
                    (bad ??= []).Add(new Finding(name, a.Value.ToString(), name + " is not a state or property defined by WAI-ARIA."));
                    (fixes ??= []).Add(RemoveAttribute(reporter, el, name));
                }
            }

            if (bad is not null)
            {
                reporter.Failed(el, "The element has an aria- attribute that WAI-ARIA does not define.", bad, fixes);
            }
            else if (any)
            {
                reporter.Passed(el, "Every aria- attribute is defined by WAI-ARIA.");
            }
        }
    }
}

// ACT 6a7281 "ARIA state or property has valid value": each non-empty state or property value fits its value type;
// an ID reference that a role requires must name an element that exists (ARIA 1.2 section 6.3, "Values for States and Properties").
internal sealed class AriaValidAttrValueRule : AriaRule
{
    public AriaValidAttrValueRule()
        : base(
            "aria-valid-attr-value",
            "Every non-empty WAI-ARIA state or property must have a value of its type, and a required ID reference must name an element in the page.",
            "Use one of the values WAI-ARIA allows for the attribute, or point the required reference at an element that exists.",
            null,
            "The right value depends on what the author meant: a mistyped value, an id that should exist, or an attribute that should go. No single edit is correct for all of them.",
            actIds: ["6a7281"])
    {
    }

    public override RuleScope Scope => RuleScope.Node | RuleScope.IdReferences;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        int counter = 0;
        foreach (Node el in Elements(context))
        {
            if ((++counter & 1023) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (!IsHtmlOrSvg(el))
            {
                continue;
            }

            NodeRole nr = context.Tree.Roles[el];
            bool any = false;
            foreach (Attr a in el.Attributes)
            {
                if (IsAriaName(a.QualifiedName) && AriaTaxonomy.FindProperty(a.QualifiedName) is not null && !IsBlank(a.Value))
                {
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                continue;
            }

            List<Finding>? bad = null;
            AriaRole? role = nr.Role;
            foreach (RoleDiagnostic d in nr.Diagnostics)
            {
                if (d.Kind != RoleDiagnosticKind.InvalidAttributeValue || d.Attribute is null)
                {
                    continue;
                }

                (bad ??= []).Add(new Finding(d.Attribute, d.Value, d.Message));
            }

            // A required ID reference (or list) needs at least one id that exists in the document.
            if (role is not null)
            {
                foreach (string req in role.RequiredAttributes)
                {
                    AriaProperty? p = AriaTaxonomy.FindProperty(req);
                    if (p is null || p.ValueType is not (AriaValueType.IdReference or AriaValueType.IdReferenceList))
                    {
                        continue;
                    }

                    string? v = el.GetAttribute(req);
                    if (v is null || IsBlank(v))
                    {
                        continue;
                    }

                    bool found = false;
                    foreach (string id in Tokens(v))
                    {
                        if (!context.Document.GetElementById(id).IsNull)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        (bad ??= []).Add(new Finding(req, v, req + " is required by the role " + role.Name + " but names no element in the page."));
                    }
                }
            }

            // Beyond what a role requires: an ID reference that names no element anywhere in the page. axe-core reports these, and the
            // split is the one it makes: a label or description may be added after load (needs review); a control, owned, flow-to,
            // active-descendant or details reference that points nowhere is wrong. A closed control (aria-expanded=false) may
            // refer to content that is not in the page yet, so aria-controls is not checked there. aria-errormessage is only
            // used while the element is invalid.
            List<Finding>? dangling = null;
            List<Finding>? review = null;
            foreach (Attr a in el.Attributes)
            {
                string an = a.QualifiedName;
                if (an is not ("aria-labelledby" or "aria-describedby" or "aria-controls" or "aria-owns" or "aria-flowto" or "aria-activedescendant" or "aria-details" or "aria-errormessage")
                    || IsBlank(a.Value))
                {
                    continue;
                }

                string v = a.Value.ToString();
                bool alreadyReported = bad is not null && bad.Exists(f => f.Attribute == an);
                bool found = false;
                foreach (string id in Tokens(v))
                {
                    if (!context.Document.GetElementById(id).IsNull)
                    {
                        found = true;
                        break;
                    }
                }

                if (found || alreadyReported)
                {
                    continue;
                }

                if (an == "aria-controls" && EqualsAscii(el.GetAttribute("aria-expanded"), "false"))
                {
                    continue;
                }

                if (an == "aria-errormessage" && !EqualsAscii(el.GetAttribute("aria-invalid"), "true"))
                {
                    continue;
                }

                // A popup trigger's controlled element is often created when it opens.
                if (an is "aria-labelledby" or "aria-describedby" or "aria-errormessage" || (an == "aria-controls" && el.HasAttribute("aria-haspopup") && !EqualsAscii(el.GetAttribute("aria-haspopup"), "false")))
                {
                    (review ??= []).Add(new Finding(an, v, an + " names no element in the page; it may be added later."));
                }
                else
                {
                    (dangling ??= []).Add(new Finding(an, v, an + " names no element in the page."));
                }
            }

            if (dangling is not null)
            {
                (bad ??= []).AddRange(dangling);
            }

            if (bad is not null)
            {
                reporter.Failed(el, "A WAI-ARIA state or property has a value it cannot have.", bad);
            }
            else if (review is not null)
            {
                reporter.NeedsReview(el, "An ID reference names no element in the page.", review);
            }
            else
            {
                reporter.Passed(el, "Every non-empty state and property has a valid value.");
            }
        }
    }
}

// axe "aria-allowed-attr" and ACT 5c01ea: an aria-* attribute must be supported by the element's role or be global (ARIA 1.2 section 6.6).
internal sealed class AriaAllowedAttrRule : AriaRule
{
    public AriaAllowedAttrRule()
        : base(
            "aria-allowed-attr",
            "Each WAI-ARIA state or property on an element must be one that the element's role supports.",
            "Remove the attribute, or give the element the role that supports it.",
            null,
            "The attribute may be right and the role wrong (or the reverse); choosing between changing the role and dropping the attribute is a judgement.",
            actIds: ["5c01ea"])
    {
    }

    public override RuleScope Scope => RuleScope.Node;

    public override void Evaluate(CheckContext context, RuleReporter reporter) =>
        AttributeDiagnosticRules.Run(context, reporter, RoleDiagnosticKind.UnsupportedAttribute, "The role does not support this state or property.");
}

// axe "aria-prohibited-attr" and ACT 5c01ea / kb1m8s: roles that prohibit naming or other attributes (ARIA 1.2 section 5.3.2).
internal sealed class AriaProhibitedAttrRule : AriaRule
{
    public AriaProhibitedAttrRule()
        : base(
            "aria-prohibited-attr",
            "A WAI-ARIA attribute must not be used on a role that prohibits it, such as aria-label on an element with no role.",
            "Remove the attribute, or give the element a role that can be named.",
            null,
            "The attribute may be a name or state meant for another element or role, so whether to remove it, move it or change the role depends on what the author meant.",
            actIds: ["5c01ea", "kb1m8s"])
    {
    }

    public override RuleScope Scope => RuleScope.Node;

    public override void Evaluate(CheckContext context, RuleReporter reporter) =>
        AttributeDiagnosticRules.Run(context, reporter, RoleDiagnosticKind.ProhibitedAttribute, "The role prohibits this state or property.");
}

internal static class AttributeDiagnosticRules
{
    internal static void Run(CheckContext context, RuleReporter reporter, RoleDiagnosticKind kind, string reason)
    {
        int counter = 0;
        foreach (AxNode node in context.TreeNodes())
        {
            if ((++counter & 1023) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            Node el = node.DomNode;
            if (node.Kind != AxNodeKind.Element || !context.IsExposed(el) || el.NamespaceURI is not ("http://www.w3.org/1999/xhtml" or "http://www.w3.org/2000/svg"))
            {
                continue;
            }

            bool any = false;
            foreach (Attr a in el.Attributes)
            {
                if (a.QualifiedName.StartsWith("aria-", StringComparison.Ordinal) && AriaTaxonomy.FindProperty(a.QualifiedName) is not null)
                {
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                continue;
            }

            NodeRole nr = context.Tree.Roles[el];
            List<Finding>? bad = null;
            foreach (RoleDiagnostic d in nr.Diagnostics)
            {
                if (d.Kind != kind || d.Attribute is null)
                {
                    continue;
                }

                string where = nr.Role is { } r ? "the role " + r.Name : "an element with no role";
                (bad ??= []).Add(new Finding(d.Attribute, d.Value ?? el.GetAttribute(d.Attribute), d.Attribute + " is not allowed on " + where + ". " + reason));
            }

            // An element with no role supports only the global attributes, plus the few ARIA in HTML grants to native
            // controls that have no role of their own (an input of type color, file, date…, and the summary of a details).
            if (kind == RoleDiagnosticKind.UnsupportedAttribute && nr.Role is null)
            {
                foreach (Attr a in el.Attributes)
                {
                    if (a.QualifiedName.StartsWith("aria-", StringComparison.Ordinal) && AriaTaxonomy.FindProperty(a.QualifiedName) is { IsGlobal: false } p && !NativeGrant(el, p.Name))
                    {
                        (bad ??= []).Add(new Finding(p.Name, a.Value.ToString(), p.Name + " is not allowed on an element with no role. " + reason));
                    }
                }
            }

            if (bad is not null)
            {
                reporter.Failed(el, "The element has a WAI-ARIA attribute its role does not allow.", bad);
            }
            else
            {
                reporter.Passed(el, "Every WAI-ARIA attribute is allowed on the element's role.");
            }
        }
    }
    private static bool NativeGrant(Node el, string attribute) => el.LocalName switch
    {
        "input" => attribute is "aria-disabled" or "aria-invalid" or "aria-required",
        "summary" => attribute is "aria-disabled" or "aria-haspopup",
        _ => false,
    };
}

// axe "aria-conditional-attr": the native state wins over aria-checked on a native checkbox or radio button (ARIA in HTML, input type=checkbox and radio).
internal sealed class AriaConditionalAttrRule : AriaRule
{
    public AriaConditionalAttrRule()
        : base(
            "aria-conditional-attr",
            "A native checkbox must not carry an aria-checked value that disagrees with its checked state.",
            "Drop aria-checked and let the checked attribute carry the state.",
            "Remove aria-checked from a native checkbox whose value disagrees with the checked attribute. The native state is the one assistive technology reports.",
            null,
            actIds: ["5c01ea"])
    {
    }

    public override RuleScope Scope => RuleScope.Node;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node el in Elements(context))
        {
            if (el.LocalName != "input" || !IsHtmlOrSvg(el) || el.NamespaceURI != "http://www.w3.org/1999/xhtml" || !(EqualsAscii(el.GetAttribute("type"), "checkbox") || EqualsAscii(el.GetAttribute("type"), "radio"))
                || el.GetAttribute("aria-checked") is not { } value || IsBlank(value) || !context.IsExposed(el))
            {
                continue;
            }

            bool native = el.HasAttribute("checked");
            if (EqualsAscii(value, "mixed") && EqualsAscii(el.GetAttribute("type"), "checkbox"))
            {
                // Indeterminate is a script-set state, invisible to a static check.
                reporter.NeedsReview(el, "aria-checked is mixed; whether the checkbox is indeterminate cannot be read from the markup.", [new Finding("aria-checked", value, "mixed")]);
            }
            else if (EqualsAscii(value, native ? "true" : "false"))
            {
                reporter.Passed(el, "aria-checked agrees with the checked attribute.");
            }
            else
            {
                reporter.Failed(el, "aria-checked disagrees with the checked attribute.", [new Finding("aria-checked", value, "The control is natively " + (native ? "checked" : "unchecked") + ".")],
                    [RemoveAttribute(reporter, el, "aria-checked")]);
            }
        }
    }
}

// axe "aria-braille-equivalent": aria-braillelabel and aria-brailleroledescription refine a non-braille label or role
// description and mean nothing without one (WAI-ARIA 1.3 draft sections on braille properties).
internal sealed class AriaBrailleEquivalentRule : AriaRule
{
    public AriaBrailleEquivalentRule()
        : base(
            "aria-braille-equivalent",
            "aria-braillelabel needs an accessible name and aria-brailleroledescription needs an aria-roledescription to refine.",
            "Add the ordinary label or role description that the braille text abbreviates.",
            null,
            "The missing text is content, which only the author can supply.")
    {
    }

    public override RuleScope Scope => RuleScope.Node | RuleScope.Descendants;

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (AxNode node in context.TreeNodes())
        {
            if (node.Kind != AxNodeKind.Element || !context.IsExposed(node.DomNode))
            {
                continue;
            }

            Node el = node.DomNode;
            string? label = el.GetAttribute("aria-braillelabel");
            string? roleDesc = el.GetAttribute("aria-brailleroledescription");
            bool hasLabel = label is not null && !IsBlank(label);
            bool hasRoleDesc = roleDesc is not null && !IsBlank(roleDesc);
            if (!hasLabel && !hasRoleDesc)
            {
                continue;
            }

            List<Finding>? bad = null;
            // The ordinary text a braille label abbreviates may be the name or, on an element that takes no name, its text content.
            if (hasLabel && node.Name.IsEmpty && IsBlank(el.TextContent ?? string.Empty))
            {
                (bad ??= []).Add(new Finding("aria-braillelabel", label, "The element has no accessible name or text for the braille label to refine."));
            }

            if (hasRoleDesc && (el.GetAttribute("aria-roledescription") is not { } rd || IsBlank(rd)))
            {
                (bad ??= []).Add(new Finding("aria-brailleroledescription", roleDesc, "The element has no aria-roledescription for the braille role description to refine."));
            }

            if (bad is null)
            {
                reporter.Passed(el);
            }
            else
            {
                // A braille property without its ordinary counterpart is something an author has to look at: the counterpart may be
                // supplied by script or by a name the static tree cannot see, so the verdict is a review, not a failure.
                reporter.NeedsReview(el, "A braille property has no ordinary equivalent.", bad);
            }
        }
    }
}
