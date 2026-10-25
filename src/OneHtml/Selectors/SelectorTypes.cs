using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Selectors;

/// <summary>A selector combinator (Selectors 4 §16).</summary>
public enum Combinator : byte
{
    /// <summary>Whitespace: a descendant.</summary>
    Descendant = 0,

    /// <summary><c>&gt;</c>: a child.</summary>
    Child = 1,

    /// <summary><c>+</c>: the next sibling.</summary>
    NextSibling = 2,

    /// <summary><c>~</c>: a later sibling.</summary>
    SubsequentSibling = 3,

    /// <summary><c>||</c>: the column combinator. It parses, but nothing in an HTML document is in a grid column, so it never matches.</summary>
    Column = 4,
}

/// <summary>The specificity of a selector (Selectors 4 §17): ID, class-like and type-like counts.</summary>
/// <param name="A">The number of ID selectors.</param>
/// <param name="B">The number of class, attribute and pseudo-class selectors.</param>
/// <param name="C">The number of type selectors and pseudo-elements.</param>
public readonly record struct Specificity(int A, int B, int C) : IComparable<Specificity>
{
    /// <summary>Compares lexicographically by A, then B, then C.</summary>
    /// <param name="other">The other specificity.</param>
    public int CompareTo(Specificity other)
    {
        int c = A.CompareTo(other.A);
        if (c != 0)
        {
            return c;
        }

        c = B.CompareTo(other.B);
        return c != 0 ? c : C.CompareTo(other.C);
    }

    /// <summary>The component-wise sum.</summary>
    /// <param name="x">A specificity.</param>
    /// <param name="y">Another.</param>
    public static Specificity operator +(Specificity x, Specificity y) => new(x.A + y.A, x.B + y.B, x.C + y.C);

    /// <summary>True when <paramref name="x"/> is more specific.</summary>
    /// <param name="x">A specificity.</param>
    /// <param name="y">Another.</param>
    public static bool operator >(Specificity x, Specificity y) => x.CompareTo(y) > 0;

    /// <summary>True when <paramref name="x"/> is less specific.</summary>
    /// <param name="x">A specificity.</param>
    /// <param name="y">Another.</param>
    public static bool operator <(Specificity x, Specificity y) => x.CompareTo(y) < 0;

    /// <summary>True when <paramref name="x"/> is at least as specific.</summary>
    /// <param name="x">A specificity.</param>
    /// <param name="y">Another.</param>
    public static bool operator >=(Specificity x, Specificity y) => x.CompareTo(y) >= 0;

    /// <summary>True when <paramref name="x"/> is at most as specific.</summary>
    /// <param name="x">A specificity.</param>
    /// <param name="y">Another.</param>
    public static bool operator <=(Specificity x, Specificity y) => x.CompareTo(y) <= 0;
}

/// <summary>What a rightmost compound selector can be looked up by, for bucketing rules.</summary>
public enum SelectorKeyKind : byte
{
    /// <summary>No usable key: the compound needs a full scan.</summary>
    None = 0,

    /// <summary>An ID selector; <see cref="SelectorKey.Name"/> is the id.</summary>
    Id = 1,

    /// <summary>A class selector; <see cref="SelectorKey.Name"/> is the class.</summary>
    Class = 2,

    /// <summary>A type selector; <see cref="SelectorKey.Name"/> is the name, ASCII lower-cased (look elements up by their lower-cased local name).</summary>
    Tag = 3,
}

/// <summary>The bucket key of one complex selector's rightmost compound.</summary>
/// <param name="Kind">What the key is.</param>
/// <param name="Name">The id, class or lower-cased tag name; empty for <see cref="SelectorKeyKind.None"/>.</param>
public readonly record struct SelectorKey(SelectorKeyKind Kind, string Name);

/// <summary>
/// What a selector's result depends on besides the element's own name, id, classes and attributes. A restyle or
/// re-check engine uses these to decide what a DOM or state change can affect.
/// </summary>
[Flags]
public enum SelectorDependencies
{
    /// <summary>Nothing beyond the element itself and its ancestors.</summary>
    None = 0,

    /// <summary>A <c>:has()</c>: a change below or beside an element can change whether it matches.</summary>
    Has = 1,

    /// <summary>A <c>+</c> or <c>~</c> combinator: a change to an earlier sibling can change the result.</summary>
    SiblingCombinator = 2,

    /// <summary>A positional pseudo-class (<c>:nth-*</c>, <c>:first-child</c>, <c>:only-of-type</c>, …): inserting or removing a sibling changes positions.</summary>
    Structural = 4,

    /// <summary><c>:empty</c>: a change to the element's children changes the result.</summary>
    Empty = 8,

    /// <summary><c>:hover</c>.</summary>
    Hover = 16,

    /// <summary><c>:active</c>.</summary>
    Active = 32,

    /// <summary><c>:focus</c>, <c>:focus-visible</c>.</summary>
    Focus = 64,

    /// <summary><c>:focus-within</c>.</summary>
    FocusWithin = 128,

    /// <summary><c>:target</c> or <c>:target-within</c>.</summary>
    Target = 256,

    /// <summary><c>:lang()</c> or <c>:dir()</c>: the result is inherited from ancestors' attributes.</summary>
    LangOrDir = 512,

    /// <summary>A form or element-state pseudo-class (<c>:checked</c>, <c>:disabled</c>, <c>:valid</c>, <c>:default</c>, …) that reads attributes of the element and of related elements.</summary>
    FormState = 1024,

    /// <summary><c>:scope</c>: the result depends on the scoping element.</summary>
    Scope = 2048,

    /// <summary>A selector that can never match an element in this library (pseudo-elements, <c>:visited</c>, <c>:autofill</c>, …).</summary>
    NeverMatches = 4096,
}

/// <summary>Limits on selector parsing, checked while parsing and before work they guard.</summary>
public sealed class SelectorLimits
{
    /// <summary>The defaults: 65,536 characters, nesting 16, 128 compounds per complex selector, 4,096 complex selectors and 16,384 simple selectors in total.</summary>
    public static SelectorLimits Default { get; } = new();

    /// <summary>The longest selector text. Default 65,536.</summary>
    public int MaxLength { get; init; } = 1 << 16;

    /// <summary>How deeply <c>:is()</c>, <c>:not()</c>, <c>:has()</c> and the like nest. Default 16.</summary>
    public int MaxNesting { get; init; } = 16;

    /// <summary>Compound selectors in one complex selector. Default 128.</summary>
    public int MaxCompounds { get; init; } = 128;

    /// <summary>Complex selectors in the whole parse, nested ones included. Default 4,096.</summary>
    public int MaxComplexSelectors { get; init; } = 4096;

    /// <summary>Simple selectors in the whole parse. Default 16,384.</summary>
    public int MaxSimpleSelectors { get; init; } = 1 << 14;
}

/// <summary>Options for parsing selectors.</summary>
public sealed class SelectorParseOptions
{
    /// <summary>
    /// The namespace prefixes in scope (<c>@namespace</c> rules): prefix to namespace URI. The empty prefix names the default
    /// namespace. <see langword="null"/> (the default) declares none: <c>ns|a</c> is then a syntax error and type selectors
    /// match elements of any namespace.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Namespaces { get; init; }

    /// <summary>The limits; <see cref="SelectorLimits.Default"/> when null.</summary>
    public SelectorLimits? Limits { get; init; }
}

/// <summary>The selector text is not a valid selector list (DOM <c>SyntaxError</c>), or is beyond a limit.</summary>
public class SelectorSyntaxException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong.</param>
    public SelectorSyntaxException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public SelectorSyntaxException()
        : base("The selector is not valid.")
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong.</param>
    /// <param name="innerException">The cause.</param>
    public SelectorSyntaxException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A selector limit from <see cref="SelectorLimits"/> was exceeded.</summary>
public sealed class SelectorLimitExceededException : SelectorSyntaxException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="limit">The name of the limit.</param>
    public SelectorLimitExceededException(string limit)
        : base("The selector exceeds the limit " + limit + ".")
    {
        Limit = limit;
    }

    /// <summary>Creates the exception.</summary>
    public SelectorLimitExceededException()
        : this("selector")
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="limit">The name of the limit.</param>
    /// <param name="innerException">The cause.</param>
    public SelectorLimitExceededException(string limit, Exception innerException)
        : base("The selector exceeds the limit " + limit + ".", innerException)
    {
        Limit = limit;
    }

    /// <summary>The name of the limit that was hit.</summary>
    public string Limit { get; } = "selector";
}

/// <summary>
/// The interaction state a dynamic pseudo-class is matched against. Script-driven states are invisible to the library,
/// so the default is "nothing hovered, focused, active or targeted". All handles must belong to the document queried.
/// </summary>
public sealed class InteractionState
{
    /// <summary>The focused element (<c>:focus</c>), or the null node.</summary>
    public Node Focus { get; init; }

    /// <summary>Whether focus came from the keyboard or the page's own script, which makes <c>:focus-visible</c> match the focused element.</summary>
    public bool FocusVisible { get; init; }

    /// <summary>The hovered element; it and its ancestors match <c>:hover</c>.</summary>
    public Node Hover { get; init; }

    /// <summary>The element being activated; it and its ancestors match <c>:active</c>.</summary>
    public Node Active { get; init; }

    /// <summary>The element the URL fragment names (<c>:target</c>); it and its ancestors match <c>:target-within</c>.</summary>
    public Node Target { get; init; }
}

/// <summary>Per-call options for matching and querying.</summary>
public sealed class SelectorMatchOptions
{
    /// <summary>
    /// The element <c>:scope</c> matches. When null, queries and <c>Matches</c> use the node they were called on. A
    /// document root as scope makes <c>:scope</c> match the document element; a fragment root makes it match nothing.
    /// </summary>
    public Node Scope { get; init; }

    /// <summary>The interaction state, or null for none.</summary>
    public InteractionState? State { get; init; }

    internal static SelectorMatchOptions ForScope(Node scope) => new() { Scope = scope };
}
