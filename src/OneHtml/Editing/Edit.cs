using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>The kinds of edit a transaction can hold.</summary>
public enum EditOperation
{
    /// <summary>Sets an attribute, adding it when absent.</summary>
    SetAttribute = 0,

    /// <summary>Removes an attribute; a no-op when it is absent.</summary>
    RemoveAttribute = 1,

    /// <summary>Replaces the text of a text node, or all the text children of an element.</summary>
    ReplaceText = 2,

    /// <summary>Inserts parsed markup before a node.</summary>
    InsertBefore = 3,

    /// <summary>Inserts parsed markup after a node.</summary>
    InsertAfter = 4,

    /// <summary>Inserts parsed markup as the first children of an element.</summary>
    Prepend = 5,

    /// <summary>Inserts parsed markup as the last children of an element.</summary>
    Append = 6,

    /// <summary>Wraps a node, or a run of sibling nodes, in a new element.</summary>
    Wrap = 7,

    /// <summary>Replaces an element by its children.</summary>
    Unwrap = 8,

    /// <summary>Replaces a node by parsed markup.</summary>
    Replace = 9,

    /// <summary>Removes a node and its descendants.</summary>
    Remove = 10,

    /// <summary>Sets one declaration of an element's <c>style</c> attribute.</summary>
    SetStyle = 11,

    /// <summary>Removes every declaration of one property from an element's <c>style</c> attribute.</summary>
    RemoveStyle = 12,
}

/// <summary>
/// One operation on one node of a snapshot. Edits are plain values: build them with the static factories, collect
/// them in an <see cref="EditTransaction"/> (or a <see cref="DocumentEditor"/>) and apply them with
/// <c>document.Apply(...)</c>. Targets are handles into the snapshot being edited.
/// </summary>
/// <remarks>
/// Markup given to an edit is parsed with the HTML fragment parsing algorithm in the context of the element it lands
/// in, never spliced in as a string. <see cref="Meta"/> is carried along and never read.
/// </remarks>
public sealed class Edit
{
    private Edit(EditOperation operation, Node target)
    {
        Operation = operation;
        Target = target;
    }

    /// <summary>What the edit does.</summary>
    public EditOperation Operation { get; }

    /// <summary>The node the edit acts on, a handle into the snapshot the edit is applied to.</summary>
    public Node Target { get; }

    /// <summary>The attribute name for attribute edits, or the property name for style edits.</summary>
    public string? Name { get; private init; }

    /// <summary>The attribute value, the CSS value, the replacement text, or the markup, depending on the operation.</summary>
    public string? Value { get; private init; }

    /// <summary>For <see cref="EditOperation.SetStyle"/>, whether the declaration is <c>!important</c>.</summary>
    public bool Important { get; private init; }

    /// <summary>For <see cref="EditOperation.Wrap"/>, how many consecutive siblings (starting at <see cref="Target"/>) are wrapped.</summary>
    public int Count { get; private init; } = 1;

    /// <summary>An opaque value the library stores and returns in results but never interprets. Default <see langword="null"/>.</summary>
    public object? Meta { get; init; }

    /// <summary>
    /// For a stylesheet edit inside a <c>&lt;style&gt;</c> element: the stretches of the element's text that change, so
    /// that only those are spliced and the rest of the text keeps its bytes. <see cref="Value"/> is the whole new text.
    /// Set only by the batch applier.
    /// </summary>
    internal IReadOnlyList<TextSplice>? TextSplices { get; private init; }

    internal static Edit StyleTextSplices(Node textNodeOrElement, string newText, IReadOnlyList<TextSplice> splices) =>
        new(EditOperation.ReplaceText, textNodeOrElement) { Value = newText, TextSplices = splices };

    /// <summary>Sets an attribute. Names of HTML elements are matched and written in lowercase; the value is escaped for its quotes.</summary>
    /// <param name="element">An element.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The value; may be empty.</param>
    public static Edit SetAttribute(Node element, string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        return new Edit(EditOperation.SetAttribute, element) { Name = name, Value = value };
    }

    /// <summary>Removes an attribute together with the whitespace before it. Removing an absent attribute changes nothing.</summary>
    /// <param name="element">An element.</param>
    /// <param name="name">The attribute name.</param>
    public static Edit RemoveAttribute(Node element, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new Edit(EditOperation.RemoveAttribute, element) { Name = name };
    }

    /// <summary>
    /// Replaces text. On a text node it replaces the node's characters. On an element it replaces all of its text
    /// children with one text node placed where the first of them was (as the first child when there were none);
    /// child elements stay.
    /// </summary>
    /// <param name="target">A text node or an element.</param>
    /// <param name="text">The new text, not markup; an empty string removes the text.</param>
    public static Edit ReplaceText(Node target, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Edit(EditOperation.ReplaceText, target) { Value = text };
    }

    /// <summary>Inserts the nodes <paramref name="html"/> parses to as the previous siblings of <paramref name="node"/>.</summary>
    /// <param name="node">A node with a parent.</param>
    /// <param name="html">Markup, parsed in the context of the parent.</param>
    public static Edit InsertBefore(Node node, string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return new Edit(EditOperation.InsertBefore, node) { Value = html };
    }

    /// <summary>Inserts the nodes <paramref name="html"/> parses to as the next siblings of <paramref name="node"/>.</summary>
    /// <param name="node">A node with a parent.</param>
    /// <param name="html">Markup, parsed in the context of the parent.</param>
    public static Edit InsertAfter(Node node, string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return new Edit(EditOperation.InsertAfter, node) { Value = html };
    }

    /// <summary>Inserts the nodes <paramref name="html"/> parses to as the first children of <paramref name="element"/>. For a <c>template</c> the content is the target.</summary>
    /// <param name="element">An element that is not void.</param>
    /// <param name="html">Markup, parsed in the context of the element.</param>
    public static Edit Prepend(Node element, string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return new Edit(EditOperation.Prepend, element) { Value = html };
    }

    /// <summary>Inserts the nodes <paramref name="html"/> parses to as the last children of <paramref name="element"/>. For a <c>template</c> the content is the target.</summary>
    /// <param name="element">An element that is not void.</param>
    /// <param name="html">Markup, parsed in the context of the element.</param>
    public static Edit Append(Node element, string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return new Edit(EditOperation.Append, element) { Value = html };
    }

    /// <summary>
    /// Wraps <paramref name="count"/> consecutive siblings, starting at <paramref name="node"/>, in the element
    /// <paramref name="html"/> parses to. The wrapped nodes become the last children of the wrapper, after any
    /// children the markup already has.
    /// </summary>
    /// <param name="node">The first node to wrap; it needs a parent.</param>
    /// <param name="html">Markup for exactly one element (whitespace around it is ignored).</param>
    /// <param name="count">How many siblings to wrap, at least 1. Default 1.</param>
    public static Edit Wrap(Node node, string html, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        return new Edit(EditOperation.Wrap, node) { Value = html, Count = count };
    }

    /// <summary>Replaces an element by its children.</summary>
    /// <param name="element">An element whose parent is an element (not the document itself) and that is not a <c>template</c>.</param>
    public static Edit Unwrap(Node element) => new(EditOperation.Unwrap, element);

    /// <summary>Replaces a node by the nodes <paramref name="html"/> parses to; markup that parses to nothing removes the node.</summary>
    /// <param name="node">A node with a parent.</param>
    /// <param name="html">Markup, parsed in the context of the parent.</param>
    public static Edit Replace(Node node, string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return new Edit(EditOperation.Replace, node) { Value = html };
    }

    /// <summary>Removes a node and everything inside it.</summary>
    /// <param name="node">A node with a parent.</param>
    public static Edit Remove(Node node) => new(EditOperation.Remove, node);

    /// <summary>
    /// Sets a declaration in an element's <c>style</c> attribute: the last declaration of that property is changed
    /// in place (its name, spacing and any comments are kept), or one is appended. The attribute is created when absent.
    /// </summary>
    /// <param name="element">An element.</param>
    /// <param name="property">The property name, such as <c>color</c> or <c>--accent</c>.</param>
    /// <param name="value">The value, without <c>!important</c> and without a trailing semicolon.</param>
    /// <param name="important">Whether to mark the declaration <c>!important</c>.</param>
    public static Edit SetStyle(Node element, string property, string value, bool important = false)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
        return new Edit(EditOperation.SetStyle, element) { Name = property, Value = value, Important = important };
    }

    /// <summary>Removes every declaration of a property from an element's <c>style</c> attribute; the attribute stays, possibly empty. A no-op when none is present.</summary>
    /// <param name="element">An element.</param>
    /// <param name="property">The property name.</param>
    public static Edit RemoveStyle(Node element, string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return new Edit(EditOperation.RemoveStyle, element) { Name = property };
    }

    /// <summary>Returns a copy of this edit that carries <paramref name="meta"/> as its <see cref="Meta"/>.</summary>
    /// <param name="meta">The metadata, of a type <see cref="Meta"/> accepts.</param>
    public Edit WithMeta(object? meta) => new(Operation, Target)
    {
        Name = Name,
        Value = Value,
        Important = Important,
        Count = Count,
        TextSplices = TextSplices,
        Meta = meta,
    };

    /// <summary>
    /// Returns a copy of this edit aimed at <paramref name="target"/>, with the same operation, arguments and metadata. Used to
    /// carry an edit proposed for one snapshot over to the node that replaced its target in the next one.
    /// </summary>
    /// <param name="target">A node of the snapshot the copy will be applied to.</param>
    public Edit WithTarget(Node target) => new(Operation, target)
    {
        Name = Name,
        Value = Value,
        Important = Important,
        Count = Count,
        TextSplices = TextSplices,
        Meta = Meta,
    };

    /// <inheritdoc />
    public override string ToString() => Operation + " " + Target;
}

/// <summary>A replacement of the text range [Start, End) by Text.</summary>
internal readonly record struct TextSplice(int Start, int End, string Text);
