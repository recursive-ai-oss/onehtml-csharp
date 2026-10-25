using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Accessibility.Aria;

/// <summary>How the computed role of an element was derived.</summary>
public enum RoleOrigin : byte
{
    /// <summary>The element has no role: HTML-AAM maps it to none ("No corresponding role") and no valid <c>role</c> token applies.</summary>
    None = 0,

    /// <summary>The first valid, non-abstract token of the <c>role</c> attribute (WAI-ARIA 1.2 section 8.1, "Role Attribute").</summary>
    Explicit,

    /// <summary>The element's native role from HTML-AAM.</summary>
    Implicit,

    /// <summary>
    /// An explicit <c>none</c> or <c>presentation</c> was ignored because the element is focusable or has a global
    /// WAI-ARIA attribute (WAI-ARIA 1.2 "Presentational Roles Conflict Resolution"); the role is the implicit one.
    /// </summary>
    ConflictResolved,

    /// <summary>The element is a required owned element of a presentational parent (a <c>li</c> in a <c>ul role=none</c>) and is presentational too.</summary>
    InheritedPresentational,

    /// <summary>The element is a descendant of a role whose children are presentational (a span in a button).</summary>
    ChildrenPresentational,
}

/// <summary>What a role diagnostic reports.</summary>
public enum RoleDiagnosticKind : byte
{
    /// <summary>A <c>role</c> token that is not a WAI-ARIA role.</summary>
    InvalidRoleToken,

    /// <summary>A <c>role</c> token naming an abstract role, which authors must not use (WAI-ARIA 1.2 section 5.3.1).</summary>
    AbstractRole,

    /// <summary>An <c>aria-*</c> attribute the role prohibits (<c>aria-label</c> on <c>generic</c>).</summary>
    ProhibitedAttribute,

    /// <summary>An <c>aria-*</c> attribute the role does not support.</summary>
    UnsupportedAttribute,

    /// <summary>An <c>aria-*</c> attribute deprecated on the role or entirely.</summary>
    DeprecatedAttribute,

    /// <summary>An <c>aria-*</c> name that is not a WAI-ARIA state or property.</summary>
    UnknownAttribute,

    /// <summary>A value that does not match the attribute's type or allowed tokens.</summary>
    InvalidAttributeValue,

    /// <summary>An ID reference that names no element in the document.</summary>
    BrokenIdReference,

    /// <summary>A state or property the explicit role requires and the element does not supply.</summary>
    MissingRequiredAttribute,

    /// <summary>Reserved for the tree: the required context role (a <c>listitem</c> outside a list) is missing.</summary>
    MissingRequiredContext,

    /// <summary>Reserved for the tree: the required owned elements (a <c>list</c> without items) are missing.</summary>
    MissingRequiredOwnedElements,

    /// <summary>The element hit a limit (role tokens, ID references, or diagnostics) and the rest was not examined.</summary>
    LimitExceeded,
}

/// <summary>One finding about an element's role or its WAI-ARIA attributes.</summary>
/// <param name="Kind">What was found.</param>
/// <param name="Attribute">The attribute (or <c>role</c>) concerned, or <see langword="null"/>.</param>
/// <param name="Value">The offending token or value, or <see langword="null"/>.</param>
/// <param name="Message">A short English description.</param>
public readonly record struct RoleDiagnostic(RoleDiagnosticKind Kind, string? Attribute, string? Value, string Message);

/// <summary>Settings and limits for <see cref="RoleComputer"/>.</summary>
public sealed class RoleOptions
{
    /// <summary>
    /// Answers whether an element has an accessible name, for the HTML-AAM mappings that depend on one (<c>section</c>,
    /// <c>form</c> and a scoped <c>aside</c>). When <see langword="null"/>, those
    /// elements get the role they have without a name and report <see cref="NodeRole.RoleWhenNamed"/>; name
    /// computation is a separate step.
    /// </summary>
    public Func<OneHtml.Node, bool>? HasAccessibleName { get; init; }

    /// <summary>The most diagnostics kept for one element; the rest are dropped and a <see cref="RoleDiagnosticKind.LimitExceeded"/> is added. Default 64.</summary>
    public int MaxDiagnosticsPerElement { get; init; } = 64;

    /// <summary>The most <c>role</c> tokens examined on one element. Default 1024.</summary>
    public int MaxRoleTokens { get; init; } = 1024;

    /// <summary>The most ID references examined in one attribute value. Default 4096.</summary>
    public int MaxIdReferencesPerAttribute { get; init; } = 4096;
}

/// <summary>The roles of every element of a document, computed from markup alone (no style, no layout).</summary>
public sealed class RoleMap
{
    private static readonly RoleDiagnostic[] s_noDiagnostics = [];

    private readonly AriaRole?[] _role;
    private readonly AriaRole?[] _explicit;
    private readonly AriaRole?[] _implicit;
    private readonly AriaRole?[] _whenNamed;
    private readonly byte[] _origin;
    private readonly byte[] _flags;
    private readonly int[] _diagStart;
    private readonly int[] _diagCount;
    private readonly RoleDiagnostic[] _diagnostics;

    internal const byte FlagFocusable = 1;
    internal const byte FlagComputed = 2;
    internal const byte FlagDisabled = 4;
    internal const byte FlagInert = 8;

    internal RoleMap(
        OneHtml.Document document, AriaRole?[] role, AriaRole?[] explicitRole, AriaRole?[] implicitRole, AriaRole?[] whenNamed,
        byte[] origin, byte[] flags, int[] diagStart, int[] diagCount, RoleDiagnostic[] diagnostics)
    {
        Document = document;
        _role = role;
        _explicit = explicitRole;
        _implicit = implicitRole;
        _whenNamed = whenNamed;
        _origin = origin;
        _flags = flags;
        _diagStart = diagStart;
        _diagCount = diagCount;
        _diagnostics = diagnostics;
    }

    /// <summary>The document the map describes.</summary>
    public OneHtml.Document Document { get; }

    /// <summary>The total number of diagnostics over all elements.</summary>
    public int DiagnosticCount => _diagnostics.Length;

    /// <summary>
    /// The result for an element of this document. For anything else (text, comments, the document, elements inside
    /// <c>template</c> content, which is inert) the result has <see cref="NodeRole.IsComputed"/> false.
    /// </summary>
    /// <param name="node">A node of <see cref="Document"/>.</param>
    /// <exception cref="ArgumentException">The node belongs to another document.</exception>
    public NodeRole this[OneHtml.Node node]
    {
        get
        {
            if (node.IsNull || !ReferenceEquals(node.Document, Document))
            {
                throw new ArgumentException("The node is not from this role map's document.", nameof(node));
            }

            return new NodeRole(this, node.Index);
        }
    }

    /// <summary>Every element that has a result, in document order.</summary>
    public IEnumerable<NodeRole> Elements
    {
        get
        {
            for (int i = 0; i < _flags.Length; i++)
            {
                if ((_flags[i] & FlagComputed) != 0)
                {
                    yield return new NodeRole(this, i);
                }
            }
        }
    }

    internal AriaRole? RoleAt(int i) => _role[i];

    internal AriaRole? ExplicitAt(int i) => _explicit[i];

    internal AriaRole? ImplicitAt(int i) => _implicit[i];

    internal AriaRole? WhenNamedAt(int i) => _whenNamed[i];

    internal RoleOrigin OriginAt(int i) => (RoleOrigin)_origin[i];

    internal byte FlagsAt(int i) => _flags[i];

    internal ReadOnlySpan<RoleDiagnostic> DiagnosticsAt(int i) =>
        _diagCount[i] == 0 ? s_noDiagnostics : _diagnostics.AsSpan(_diagStart[i], _diagCount[i]);
}

/// <summary>The role result for one element: a handle into a <see cref="RoleMap"/>.</summary>
public readonly struct NodeRole
{
    private readonly RoleMap? _map;
    private readonly int _index;

    internal NodeRole(RoleMap map, int index)
    {
        _map = map;
        _index = index;
    }

    /// <summary>True when the element was processed; false for non-elements and inert template content.</summary>
    public bool IsComputed => _map is not null && (_map.FlagsAt(_index) & RoleMap.FlagComputed) != 0;

    /// <summary>The element.</summary>
    public OneHtml.Node Node => _map is null ? default : _map.Document.GetNode(_index);

    /// <summary>The computed ARIA role after conflict resolution and presentational inheritance, or <see langword="null"/> when the element has none.</summary>
    public AriaRole? Role => _map?.RoleAt(_index);

    /// <summary>
    /// The role name as Core-AAM's computed role gives it: <c>none</c> for <c>presentation</c>, <c>image</c> for
    /// <c>img</c>, <c>list</c> for the deprecated <c>directory</c>; the empty string when there is no role.
    /// </summary>
    public string ComputedRole => Role is { } r ? RoleComputer.ComputedRoleName(r) : string.Empty;

    /// <summary>The role the <c>role</c> attribute selected (first valid, non-abstract token), or <see langword="null"/>. Includes explicit <c>none</c>.</summary>
    public AriaRole? ExplicitRole => _map?.ExplicitAt(_index);

    /// <summary>The role HTML-AAM gives the element natively, or <see langword="null"/>.</summary>
    public AriaRole? ImplicitRole => _map?.ImplicitAt(_index);

    /// <summary>
    /// For an element whose native role depends on its accessible name (<c>section</c> → <c>region</c>, <c>form</c>,
    /// or a scoped <c>aside</c>), the role it takes when named and
    /// <see cref="RoleOptions.HasAccessibleName"/> was not supplied; otherwise <see langword="null"/>. The name
    /// computation resolves it.
    /// </summary>
    public AriaRole? RoleWhenNamed => _map?.WhenNamedAt(_index);

    /// <summary>How <see cref="Role"/> was derived.</summary>
    public RoleOrigin Origin => _map?.OriginAt(_index) ?? RoleOrigin.None;

    /// <summary>True when the role is <c>none</c> or <c>presentation</c>.</summary>
    public bool IsPresentational => Role is { } r && (r.Name is "none" or "presentation");

    /// <summary>
    /// True when the element is focusable by HTML's rules alone (links, enabled form controls, a valid
    /// <c>tabindex</c>, editing hosts, the first <c>summary</c>, controls on media, <c>iframe</c>) and not inert.
    /// Style (<c>display</c>, <c>visibility</c>) is not consulted.
    /// </summary>
    public bool IsFocusable => _map is not null && (_map.FlagsAt(_index) & RoleMap.FlagFocusable) != 0;

    /// <summary>True when a form control is disabled by its own attribute or by a disabled fieldset ancestor.</summary>
    public bool IsDisabled => _map is not null && (_map.FlagsAt(_index) & RoleMap.FlagDisabled) != 0;

    /// <summary>True when the element or an ancestor has the <c>inert</c> attribute.</summary>
    public bool IsInert => _map is not null && (_map.FlagsAt(_index) & RoleMap.FlagInert) != 0;

    /// <summary>The findings for the element's role and WAI-ARIA attributes, in attribute order. Valid for the life of the map.</summary>
    public ReadOnlySpan<RoleDiagnostic> Diagnostics => _map is null ? default : _map.DiagnosticsAt(_index);
}
