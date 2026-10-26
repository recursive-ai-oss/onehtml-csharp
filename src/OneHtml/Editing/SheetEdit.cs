using System;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>The stylesheet operations; the names match the saved-edit format.</summary>
public enum SheetEditOperation
{
    /// <summary>Sets one property of a rule: replaces the value of its last declaration of that property, or appends a declaration.</summary>
    SetDeclaration = 0,

    /// <summary>Removes every declaration of a property from a rule; applied (changing nothing) when there is none.</summary>
    RemoveDeclaration = 1,

    /// <summary>Inserts one rule before or after a rule, or at the end of a stylesheet or of a rule's block.</summary>
    InsertRule = 2,

    /// <summary>Removes a rule.</summary>
    RemoveRule = 3,

    /// <summary>Replaces a rule by another rule.</summary>
    ReplaceRule = 4,
}

/// <summary>Where <see cref="SheetEditOperation.InsertRule"/> puts the new rule relative to <see cref="SheetEdit.Target"/>.</summary>
public enum RulePlacement
{
    /// <summary>Immediately before the target rule.</summary>
    Before = 0,

    /// <summary>Immediately after the target rule.</summary>
    After = 1,

    /// <summary>As the last item of the target's block, or of the stylesheet when there is no target.</summary>
    End = 2,
}

/// <summary>
/// One edit to a stylesheet. Targets are rules of a specific <see cref="Stylesheet"/> snapshot. Edits of one transaction
/// all refer to the snapshot they were made for; the engine places them by the original source positions, so the order
/// of the list only decides the order among edits that land at the same place.
/// </summary>
public sealed class SheetEdit
{
    private SheetEdit(SheetEditOperation operation, Stylesheet sheet, CssNode target, bool hasTarget)
    {
        Operation = operation;
        Sheet = sheet;
        Target = target;
        HasTarget = hasTarget;
    }

    /// <summary>The operation.</summary>
    public SheetEditOperation Operation { get; }

    /// <summary>The stylesheet snapshot the edit was made for.</summary>
    public Stylesheet Sheet { get; }

    /// <summary>
    /// The rule the edit targets: the rule to change or remove; the reference rule of a before/after insertion; or the
    /// block rule of an <see cref="RulePlacement.End"/> insertion. Meaningful only when <see cref="HasTarget"/> is true.
    /// </summary>
    public CssNode Target { get; }

    /// <summary>False only for an end-of-stylesheet insertion.</summary>
    public bool HasTarget { get; }

    /// <summary>The property of a declaration edit.</summary>
    public string? Property { get; private init; }

    /// <summary>The new value of <see cref="SheetEditOperation.SetDeclaration"/>, without <c>!important</c>.</summary>
    public string? Value { get; private init; }

    /// <summary>Whether the declaration carries <c>!important</c> afterwards; false removes an existing one.</summary>
    public bool Important { get; private init; }

    /// <summary>The CSS text of a rule for insertion or replacement.</summary>
    public string? Css { get; private init; }

    /// <summary>Where an insertion goes.</summary>
    public RulePlacement Placement { get; private init; }

    /// <summary>Opaque data the library stores and hands back but never reads.</summary>
    public object? Meta { get; init; }

    /// <summary>Sets <paramref name="property"/> to <paramref name="value"/> in <paramref name="rule"/>.</summary>
    /// <param name="sheet">The stylesheet snapshot.</param>
    /// <param name="rule">A style rule or at-rule with a block, of <paramref name="sheet"/>.</param>
    /// <param name="property">A CSS identifier; custom properties are case-sensitive, others ASCII case-insensitive.</param>
    /// <param name="value">One declaration value; it must parse as exactly one declaration. Surrounding white space is dropped.</param>
    /// <param name="important">Whether the declaration is <c>!important</c> afterwards.</param>
    public static SheetEdit SetDeclaration(Stylesheet sheet, CssNode rule, string property, string value, bool important = false)
    {
        Check(sheet, rule);
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
        return new SheetEdit(SheetEditOperation.SetDeclaration, sheet, rule, true) { Property = property, Value = value, Important = important };
    }

    /// <summary>Removes every declaration of <paramref name="property"/> directly in <paramref name="rule"/>.</summary>
    public static SheetEdit RemoveDeclaration(Stylesheet sheet, CssNode rule, string property)
    {
        Check(sheet, rule);
        ArgumentNullException.ThrowIfNull(property);
        return new SheetEdit(SheetEditOperation.RemoveDeclaration, sheet, rule, true) { Property = property };
    }

    /// <summary>Inserts the rule <paramref name="css"/> immediately before <paramref name="rule"/>.</summary>
    public static SheetEdit InsertRuleBefore(Stylesheet sheet, CssNode rule, string css)
    {
        Check(sheet, rule);
        ArgumentNullException.ThrowIfNull(css);
        return new SheetEdit(SheetEditOperation.InsertRule, sheet, rule, true) { Css = css, Placement = RulePlacement.Before };
    }

    /// <summary>Inserts the rule <paramref name="css"/> immediately after <paramref name="rule"/>.</summary>
    public static SheetEdit InsertRuleAfter(Stylesheet sheet, CssNode rule, string css)
    {
        Check(sheet, rule);
        ArgumentNullException.ThrowIfNull(css);
        return new SheetEdit(SheetEditOperation.InsertRule, sheet, rule, true) { Css = css, Placement = RulePlacement.After };
    }

    /// <summary>Inserts the rule <paramref name="css"/> as the last item of the stylesheet, or of <paramref name="container"/>'s block.</summary>
    public static SheetEdit AppendRule(Stylesheet sheet, string css, CssNode? container = null)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(css);
        if (container is { } c)
        {
            Check(sheet, c);
            return new SheetEdit(SheetEditOperation.InsertRule, sheet, c, true) { Css = css, Placement = RulePlacement.End };
        }

        return new SheetEdit(SheetEditOperation.InsertRule, sheet, default, false) { Css = css, Placement = RulePlacement.End };
    }

    /// <summary>Removes <paramref name="rule"/>.</summary>
    public static SheetEdit RemoveRule(Stylesheet sheet, CssNode rule)
    {
        Check(sheet, rule);
        return new SheetEdit(SheetEditOperation.RemoveRule, sheet, rule, true);
    }

    /// <summary>Replaces <paramref name="rule"/> by the rule <paramref name="css"/>.</summary>
    public static SheetEdit ReplaceRule(Stylesheet sheet, CssNode rule, string css)
    {
        Check(sheet, rule);
        ArgumentNullException.ThrowIfNull(css);
        return new SheetEdit(SheetEditOperation.ReplaceRule, sheet, rule, true) { Css = css };
    }

    internal SheetEdit WithMeta(object? meta) => new(Operation, Sheet, Target, HasTarget)
    {
        Property = Property,
        Value = Value,
        Important = Important,
        Css = Css,
        Placement = Placement,
        Meta = meta,
    };

    /// <inheritdoc/>
    public override string ToString() => Operation + " " + (HasTarget ? Target.ToString() : "(end)");

    private static void Check(Stylesheet sheet, CssNode rule)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (rule.Tree is null || !ReferenceEquals(rule.Tree, sheet.Tree))
        {
            throw new ArgumentException("The rule belongs to a different stylesheet snapshot.", nameof(rule));
        }
    }
}
