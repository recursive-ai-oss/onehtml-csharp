using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

internal static class IdRuleSet
{
    internal static IEnumerable<IAccessibilityRule> Create()
    {
        yield return new DuplicateIdRule("duplicate-id", IdKind.Plain, "An id value should be used by one element only.", "Give each element its own id.");
        yield return new DuplicateIdRule("duplicate-id-active", IdKind.Active, "A focusable element must not share its id with another element.", "Give each focusable element its own id.");
        yield return new DuplicateIdRule("duplicate-id-aria", IdKind.Referenced, "An id that a label or an ARIA attribute refers to must be used by one element only.", "Give each element its own id and keep the reference pointing at the right one.");
    }
}

internal enum IdKind { Plain, Active, Referenced }

// ACT 3ea0c8 (deprecated with WCAG 2.2's removal of 4.1.1), axe duplicate-id, duplicate-id-active and duplicate-id-aria:
// every id attribute with a non-empty value on an HTML or SVG element must be unique in its tree (HTML 3.2.6.1,
// the id attribute). The three rules split the elements as the axe rules do and never overlap: ids that something refers to
// (an ARIA attribute or a label's for) go to duplicate-id-aria, other focusable elements to duplicate-id-active, the rest
// to duplicate-id. Hidden elements count, as the ACT rule says; template contents are another tree.
//
// No suggested edit: browsers resolve every reference to the first element with the id, so the later ones look
// harmless to rename, but the author may have meant any of them, stylesheets and scripts we cannot see can select every
// copy, and the new id would be an invented name.
internal sealed class DuplicateIdRule : StructureRule
{
    private static readonly string[] s_idrefAttributes =
    [
        "aria-labelledby", "aria-describedby", "aria-owns", "aria-controls", "aria-flowto", "aria-details", "aria-errormessage", "aria-activedescendant",
    ];

    private readonly IdKind _kind;

    public DuplicateIdRule(string id, IdKind kind, string description, string help)
        : base(id, description, help, null,
            "Which element should keep the id, and what the others are called, is the author's decision; stylesheets and scripts the library cannot see may depend on either.",
            actIds: ["3ea0c8"])
    {
        _kind = kind;
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        var byId = new Dictionary<string, List<Node>>(StringComparer.Ordinal);
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (Node e in Elements(context))
        {
            reporter.CancellationToken.ThrowIfCancellationRequested();
            if (!IsHtmlOrSvg(e))
            {
                continue;
            }

            if (e.GetAttribute("id") is { Length: > 0 } id)
            {
                if (!byId.TryGetValue(id, out List<Node>? list))
                {
                    byId[id] = list = [];
                }

                list.Add(e);
            }

            foreach (string attr in s_idrefAttributes)
            {
                if (e.GetAttribute(attr) is { } v)
                {
                    referenced.UnionWith(Tokens(v));
                }
            }

            if (IsHtml(e, "label") && e.GetAttribute("for") is { } f && f.Length > 0)
            {
                referenced.Add(f);
            }
        }

        foreach (Node e in Elements(context))
        {
            if (!IsHtmlOrSvg(e) || e.GetAttribute("id") is not { Length: > 0 } id || !Applies(context, e, id, referenced))
            {
                continue;
            }

            int n = byId[id].Count;
            if (n == 1)
            {
                reporter.Passed(e);
            }
            else
            {
                reporter.Failed(e, "The id " + id + " is used by " + n + " elements.", [new Finding("id", id, n + " elements share this id.")]);
            }
        }
    }

    private bool Applies(CheckContext c, Node e, string id, HashSet<string> referenced)
    {
        bool isReferenced = referenced.Contains(id);
        bool focusable = c.Tree.Roles[e].IsFocusable;
        return _kind switch
        {
            IdKind.Referenced => isReferenced,
            IdKind.Active => !isReferenced && focusable,
            _ => !isReferenced && !focusable,
        };
    }
}
