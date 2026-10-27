using System.Collections.Generic;
using RecursiveAI.OneHtml.Dom;

namespace RecursiveAI.OneHtml.Selectors;

/// <summary>
/// The elements, besides the changed one, whose own form-state pseudo-classes can change with it (HTML 4.16.3): a disabled <c>fieldset</c> disables
/// the controls inside it and a disabled <c>optgroup</c> its options; a radio button's checkedness decides whether the rest of its named group
/// is <c>:indeterminate</c>. Selectors reach the others (siblings, ancestors through <c>:has()</c>) along their own combinators, so only
/// these dependencies need adding to the elements a restyle starts from.
/// </summary>
internal static class FormDependents
{
    /// <summary>Appends the arena indices of the elements that depend on the state <paramref name="kind"/> of <paramref name="element"/>. Allocates only to grow <paramref name="into"/>.</summary>
    public static void Add(Node element, FormStateKind kind, List<int> into)
    {
        if (kind == FormStateKind.Disabled)
        {
            if (ElementState.IsHtml(element, "fieldset") || ElementState.IsHtml(element, "optgroup"))
            {
                foreach (Node d in element.DescendantElements)
                {
                    if (ElementState.CanBeDisabled(d))
                    {
                        into.Add(d.Index);
                    }
                }
            }
        }
        else if (kind == FormStateKind.Checked && element.Document!.Index.Forms.RadioGroupOf.TryGetValue(element.Index, out int group))
        {
            foreach (int member in element.Document.Index.Forms.RadioGroupMembers[group])
            {
                if (member != element.Index)
                {
                    into.Add(member);
                }
            }
        }
    }
}
