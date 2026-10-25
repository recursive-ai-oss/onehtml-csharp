using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Selectors;

internal enum SimpleKind : byte
{
    Type,
    Id,
    Class,
    Attribute,
    PseudoClass,
    Nth,
    Logical,
    Lang,
    Dir,
    Heading,
    PseudoElement,
}

internal enum LogicalKind : byte
{
    Is,
    Where,
    Not,
    Has,
}

internal enum NsKind : byte
{
    /// <summary>No prefix written: any namespace for types, no namespace for attributes (and the default namespace when one is declared).</summary>
    Unspecified,
    Any,
    None,
    Specific,
}

internal enum AttrOp : byte
{
    Exists,
    Equals,
    Includes,
    DashMatch,
    Prefix,
    Suffix,
    Substring,
}

internal enum NthKind : byte
{
    Child,
    LastChild,
    OfType,
    LastOfType,
}

internal enum PseudoKind : byte
{
    Never,
    Root,
    Empty,
    FirstChild,
    LastChild,
    OnlyChild,
    FirstOfType,
    LastOfType,
    OnlyOfType,
    Scope,
    Defined,
    Link,
    AnyLink,
    Hover,
    Active,
    Focus,
    FocusVisible,
    FocusWithin,
    Target,
    TargetWithin,
    Checked,
    Disabled,
    Enabled,
    Required,
    Optional,
    ReadOnly,
    ReadWrite,
    PlaceholderShown,
    Default,
    Indeterminate,
    Valid,
    Invalid,
    InRange,
    OutOfRange,
    Open,
    HeadingAny,
}

/// <summary>One simple selector. A single class with a kind keeps the matcher a flat switch.</summary>
internal sealed class Simple
{
    public SimpleKind Kind;
    public PseudoKind Pseudo;
    public NthKind Nth;
    public LogicalKind Logical;
    public AttrOp Op;

    /// <summary>0 none, 1 = i, 2 = s.</summary>
    public byte Flag;

    public NsKind Ns;
    public string? NsUri;
    public string? NsPrefix;

    /// <summary>Type name, id, class name, attribute name, or the canonical lower-case pseudo name.</summary>
    public string Name = string.Empty;

    /// <summary>ASCII lower-case of <see cref="Name"/>, for types and attributes.</summary>
    public string LowerName = string.Empty;

    public string Value = string.Empty;
    public int A;
    public int B;

    /// <summary>Arguments of :is/:where/:not/:has, the "of S" of nth-child, :host(), ::slotted().</summary>
    public SelectorList? Sub;

    /// <summary>:lang() ranges; heading level pairs in <see cref="Pairs"/>.</summary>
    public string[]? Strings;

    public int[]? Pairs;

    /// <summary>True for a type selector written as <c>*</c>.</summary>
    public bool IsUniversal;

    /// <summary>For ::part(), the written arguments; for other functional pseudo-elements, the serialized argument.</summary>
    public string? ArgText;

    public Specificity Specificity;
    public SelectorDependencies Deps;
}

internal sealed class Compound
{
    public Simple[] Parts = [];

    /// <summary>True when the compound holds a pseudo-element: nothing in an element tree matches it.</summary>
    public bool HasPseudoElement;
}

internal sealed class ComplexSelector
{
    public Compound[] Compounds = [];

    /// <summary>Combinators between compounds: <c>Combs[i]</c> sits between <c>Compounds[i]</c> and <c>Compounds[i + 1]</c>.</summary>
    public Combinator[] Combs = [];

    /// <summary>For a relative selector (inside <c>:has()</c>), the leading combinator; otherwise <see cref="Combinator.Descendant"/>.</summary>
    public Combinator Leading;

    public bool IsRelative;
    public bool HasPseudoElement;

    /// <summary>An item of a forgiving list (<c>:is()</c>, <c>:where()</c>) that did not parse: kept as text, never matches.</summary>
    public bool Invalid;

    public string? Raw;
    public Specificity Specificity;
    public SelectorDependencies Deps;

    /// <summary>A number unique within the owning parse, for memo keys.</summary>
    public int Id;

    public SelectorKey Key;
}

/// <summary>
/// A parsed, compiled selector list (Selectors 4). Immutable and safe to share between threads; compile once and reuse
/// for repeated queries.
/// </summary>
public sealed class SelectorList
{
    internal SelectorList(ComplexSelector[] items, bool relative)
    {
        Items = items;
        IsRelative = relative;
        Specificity max = default;
        SelectorDependencies deps = SelectorDependencies.None;
        foreach (ComplexSelector s in items)
        {
            if (s.Specificity > max)
            {
                max = s.Specificity;
            }

            deps |= s.Deps;
        }

        MaxSpecificity = max;
        Dependencies = deps;
    }

    internal ComplexSelector[] Items { get; }

    internal bool IsRelative { get; }

    /// <summary>The number of complex selectors in the list.</summary>
    public int Count => Items.Length;

    /// <summary>The highest specificity of the list's selectors (what <c>:is()</c> takes).</summary>
    public Specificity MaxSpecificity { get; }

    /// <summary>What a match result depends on beyond the element's own attributes and its ancestors; combined over the list.</summary>
    public SelectorDependencies Dependencies { get; }

    /// <summary>The specificity of one complex selector of the list.</summary>
    /// <param name="index">From 0 to <see cref="Count"/> - 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public Specificity GetSpecificity(int index)
    {
        if ((uint)index >= (uint)Items.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return Items[index].Specificity;
    }

    /// <summary>
    /// The lookup key of one complex selector's rightmost compound: an id, else a class, else a type. A rule engine buckets
    /// rules by it and verifies candidates with <see cref="Node.Matches(SelectorList, SelectorMatchOptions?)"/>.
    /// </summary>
    /// <param name="index">From 0 to <see cref="Count"/> - 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public SelectorKey GetKey(int index)
    {
        if ((uint)index >= (uint)Items.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return Items[index].Key;
    }

    /// <summary>True when the complex selector ends in a pseudo-element, which never matches an element (the cascade styles the pseudo-element instead).</summary>
    /// <param name="index">From 0 to <see cref="Count"/> - 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public bool HasPseudoElement(int index)
    {
        if ((uint)index >= (uint)Items.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return Items[index].HasPseudoElement;
    }

    /// <summary>The selectors' attribute names, class names and ids, for invalidation: a change to one of them can change a match.</summary>
    /// <param name="attributeNames">Receives lower-cased attribute names.</param>
    /// <param name="classNames">Receives class names.</param>
    /// <param name="ids">Receives ids.</param>
    public void CollectDependencies(ISet<string> attributeNames, ISet<string> classNames, ISet<string> ids)
    {
        ArgumentNullException.ThrowIfNull(attributeNames);
        ArgumentNullException.ThrowIfNull(classNames);
        ArgumentNullException.ThrowIfNull(ids);
        Collect(this, attributeNames, classNames, ids);
    }

    private static void Collect(SelectorList list, ISet<string> attrs, ISet<string> classes, ISet<string> ids)
    {
        var stack = new Stack<SelectorList>();
        stack.Push(list);
        while (stack.Count > 0)
        {
            SelectorList l = stack.Pop();
            foreach (ComplexSelector c in l.Items)
            {
                foreach (Compound k in c.Compounds)
                {
                    foreach (Simple s in k.Parts)
                    {
                        switch (s.Kind)
                        {
                            case SimpleKind.Attribute:
                                attrs.Add(s.LowerName);
                                break;
                            case SimpleKind.Class:
                                classes.Add(s.Name);
                                break;
                            case SimpleKind.Id:
                                ids.Add(s.Name);
                                break;
                            case SimpleKind.PseudoClass or SimpleKind.Nth or SimpleKind.Heading:
                                break;
                        }

                        if (s.Kind == SimpleKind.Lang)
                        {
                            attrs.Add("lang");
                            attrs.Add("xml:lang");
                        }
                        else if (s.Kind == SimpleKind.Dir)
                        {
                            attrs.Add("dir");
                        }

                        if (s.Sub is not null)
                        {
                            stack.Push(s.Sub);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Parses a selector list (Selectors 4 §3): a comma-separated list of complex selectors. Any invalid part makes the whole
    /// list invalid (the "unforgiving" parse a style rule's prelude and <c>querySelector</c> use).
    /// </summary>
    /// <param name="text">The selector text.</param>
    /// <param name="options">Namespaces and limits; null for none and the defaults.</param>
    /// <exception cref="SelectorSyntaxException">The text is not a valid selector list, or exceeds a limit (<see cref="SelectorLimitExceededException"/>).</exception>
    public static SelectorList Parse(string text, SelectorParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return SelectorParser.ParseText(text, options, SelectorParser.Mode.List);
    }

    /// <summary>Parses a selector list given as the component values of a rule prelude or a function argument.</summary>
    /// <param name="values">The component values, whitespace included.</param>
    /// <param name="options">Namespaces and limits; null for none and the defaults.</param>
    /// <exception cref="SelectorSyntaxException">The values are not a valid selector list, or exceed a limit.</exception>
    public static SelectorList Parse(ComponentValueList values, SelectorParseOptions? options = null) =>
        SelectorParser.ParseValues(values, options, SelectorParser.Mode.List);

    /// <summary>Parses a selector list without throwing.</summary>
    /// <param name="text">The selector text.</param>
    /// <param name="list">The result, or null.</param>
    /// <param name="options">Namespaces and limits; null for none and the defaults.</param>
    /// <returns>True when the text is a valid selector list within the limits.</returns>
    public static bool TryParse(string text, out SelectorList? list, SelectorParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return SelectorParser.TryParseText(text, options, SelectorParser.Mode.List, out list);
    }

    /// <summary>Parses a selector list given as component values, without throwing.</summary>
    /// <param name="values">The component values.</param>
    /// <param name="list">The result, or null.</param>
    /// <param name="options">Namespaces and limits; null for none and the defaults.</param>
    /// <returns>True when valid.</returns>
    public static bool TryParse(ComponentValueList values, out SelectorList? list, SelectorParseOptions? options = null) =>
        SelectorParser.TryParseValues(values, options, SelectorParser.Mode.List, out list);

    /// <summary>
    /// Whether the text is a valid selector list: the check <c>@supports selector()</c> needs is
    /// <see cref="IsValidComplexSelector(string, SelectorParseOptions?)"/>.
    /// </summary>
    /// <param name="text">The selector text.</param>
    /// <param name="options">Namespaces and limits.</param>
    public static bool IsValid(string text, SelectorParseOptions? options = null) => TryParse(text, out _, options);

    /// <summary>
    /// Whether the text is one valid complex selector (no comma list, no leading combinator), which is what
    /// <c>@supports selector(…)</c> takes (CSS Conditional 4 §2.3). Pseudo-elements and unknown pseudo-classes are not valid.
    /// </summary>
    /// <param name="text">The selector text.</param>
    /// <param name="options">Namespaces and limits.</param>
    public static bool IsValidComplexSelector(string text, SelectorParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return SelectorParser.TryParseText(text, options, SelectorParser.Mode.Single, out _);
    }

    /// <summary><see cref="IsValidComplexSelector(string, SelectorParseOptions?)"/> for component values.</summary>
    /// <param name="values">The component values (the function's arguments).</param>
    /// <param name="options">Namespaces and limits.</param>
    public static bool IsValidComplexSelector(ComponentValueList values, SelectorParseOptions? options = null) =>
        SelectorParser.TryParseValues(values, options, SelectorParser.Mode.Single, out _);

    /// <summary>The CSSOM serialization of the list (Selectors 4 / CSSOM "serialize a group of selectors").</summary>
    public override string ToString() => SelectorSerializer.Serialize(this);
}
