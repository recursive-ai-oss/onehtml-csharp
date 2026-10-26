using System.Collections.Generic;
using RecursiveAI.OneHtml.Accessibility.Validation.Rules;

namespace RecursiveAI.OneHtml.Accessibility.Validation;

/// <summary>The rules that ship with the package.</summary>
public static class BuiltInRules
{
    private static readonly IAccessibilityRule[] s_all = Create();

    /// <summary>Every built-in rule, in id order. The ARIA area (<c>docs/rules/aria.md</c>) and the names-and-labels area (<c>docs/rules/names.md</c>) list each with its sources and fix decision.</summary>
    public static IReadOnlyList<IAccessibilityRule> All => s_all;

    private static IAccessibilityRule[] Create()
    {
        var list = new List<IAccessibilityRule>
        {
            new AutocompleteValidRule(),
            new AriaAllowedAttrRule(),
            new AriaAllowedRoleRule(),
            new AriaBrailleEquivalentRule(),
            new AriaConditionalAttrRule(),
            new AriaDeprecatedRoleRule(),
            new AriaHiddenBodyRule(),
            new AriaHiddenFocusRule(),
            new AriaProhibitedAttrRule(),
            new AriaRequiredAttrRule(),
            new AriaRequiredChildrenRule(),
            new AriaRequiredParentRule(),
            new AriaRolesRule(),
            new AriaTextRule(),
            new AriaValidAttrRule(),
            new AriaValidAttrValueRule(),
            new ElementLangMatchesContentRule(),
            new EmptyTableHeaderRule(),
            new HtmlHasLangRule(),
            new HtmlLangMatchesContentRule(),
            new HtmlLangValidRule(),
            new HtmlXmlLangMismatchRule(),
            new ScopeAttrValidRule(),
            new TableDuplicateNameRule(),
            new TableFakeCaptionRule(),
            new TdHasHeaderRule(),
            new TdHeadersAttrRule(),
            new ThHasDataCellsRule(),
            new ValidLangRule(),
            new PresentationRoleConflictRule(),
            new RoleNameRule("aria-command-name", "A button, link or menu item given its role by the role attribute must have an accessible name.", "Give the control a visible text label, an aria-label or an aria-labelledby.", ["button", "link", "menuitem"], ["m6b1q3", "97a4e1", "c487ae"]),
            new RoleNameRule("aria-dialog-name", "A dialog or alert dialog given its role by the role attribute should have an accessible name.", "Name the dialog with aria-labelledby pointing at its title, or with aria-label.", ["dialog", "alertdialog"]),
            new RoleNameRule("aria-input-field-name", "A combo box, list box, search box, slider, spin button or text box given its role by the role attribute must have an accessible name.", "Label the field with a visible label, aria-label or aria-labelledby.", ["combobox", "listbox", "searchbox", "slider", "spinbutton", "textbox"], ["e086e5"]),
            new RoleNameRule("aria-meter-name", "A meter given its role by the role attribute must have an accessible name.", "Say what the meter measures with aria-label or aria-labelledby.", ["meter"]),
            new RoleNameRule("aria-progressbar-name", "A progress bar given its role by the role attribute must have an accessible name.", "Say what is progressing with aria-label or aria-labelledby.", ["progressbar"]),
            new RoleNameRule("aria-tab-name", "A tab given its role by the role attribute must have an accessible name.", "Give the tab text, an aria-label or an aria-labelledby.", ["tab"]),
            new RoleNameRule("aria-toggle-field-name", "A checkbox, radio button, switch, option or toggling menu item given its role by the role attribute must have an accessible name.", "Label the control with text, aria-label or aria-labelledby.", ["checkbox", "menuitemcheckbox", "menuitemradio", "option", "radio", "switch"], ["e086e5"]),
            new RoleNameRule("aria-tooltip-name", "A tooltip given its role by the role attribute must have an accessible name.", "Give the tooltip text content, an aria-label or an aria-labelledby.", ["tooltip"]),
            new RoleNameRule("aria-treeitem-name", "A tree item given its role by the role attribute should have an accessible name.", "Give the tree item text, an aria-label or an aria-labelledby.", ["treeitem"]),
        };
        list.AddRange(NameRuleSet.Create());
        list.AddRange(StructureRuleSet.Create());
        list.AddRange(VisualRuleSet.Create());
        list.AddRange(MediaRuleSet.Create());
        list.Sort((a, b) => string.CompareOrdinal(a.Descriptor.Id, b.Descriptor.Id));
        return [.. list];
    }
}
