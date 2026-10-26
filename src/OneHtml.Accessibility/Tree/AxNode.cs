using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Accessibility.Aria;

namespace RecursiveAI.OneHtml.Accessibility;

/// <summary>What a tree node stands for.</summary>
public enum AxNodeKind : byte
{
    /// <summary>The document: the root of the tree.</summary>
    Document,

    /// <summary>An element.</summary>
    Element,

    /// <summary>A text node, exposed as static text.</summary>
    Text,
}

/// <summary>
/// One node of the accessibility tree: a document, an element that is rendered, or a text node. The tree holds nodes that
/// are ignored (hidden, presentational, uninteresting) as well as included ones, with the reason, so a consumer can
/// choose either view. Immutable once <see cref="AccessibilityTree.Build"/> returns, and safe to read from any thread.
/// </summary>
public sealed class AxNode
{
    private readonly AccessibilityTree _tree;
    private IReadOnlyList<AxNode> _children = [];
    private IReadOnlyList<AxRelation> _relations = [];
    private IReadOnlyList<AxDiagnostic> _diagnostics = [];
    private IReadOnlyList<string> _ignoredReasons = [];

    internal AxNode(AccessibilityTree tree, int id, AxNodeKind kind, Node dom)
    {
        _tree = tree;
        Id = id;
        Kind = kind;
        DomNode = dom;
    }

    /// <summary>The node's position in <see cref="AccessibilityTree.Nodes"/>; ids are consecutive from 0 for the document, in tree order.</summary>
    public int Id { get; internal set; }

    /// <summary>What the node stands for.</summary>
    public AxNodeKind Kind { get; }

    /// <summary>The document node, element or text node this node comes from.</summary>
    public Node DomNode { get; }

    /// <summary>The parent in the tree after <c>aria-owns</c> re-parenting; null for the document.</summary>
    public AxNode? Parent { get; internal set; }

    /// <summary>The children in tree order: the DOM children that were not adopted by another element, then the elements this one owns, in the order of <c>aria-owns</c>.</summary>
    public IReadOnlyList<AxNode> Children { get => _children; internal set => _children = value; }

    /// <summary>
    /// The role as Core-AAM's computed role names it (<c>image</c>, <c>none</c>, <c>generic</c>, <c>button</c>, …); static text
    /// is <c>StaticText</c> and the document <c>RootWebArea</c>. Empty for an element with no role.
    /// </summary>
    public string Role { get; internal set; } = string.Empty;

    /// <summary>The ARIA role record, or null for text, the document and elements with no role.</summary>
    public AriaRole? AriaRole { get; internal set; }

    /// <summary>How the role was derived: explicit, implicit, presentational-conflict and so on.</summary>
    public RoleOrigin RoleOrigin { get; internal set; }

    /// <summary>
    /// The name of the role Chromium gives the node when it is not a WAI-ARIA role (<c>LabelText</c>, <c>Legend</c>, <c>LineBreak</c>,
    /// <c>Iframe</c>, <c>DisclosureTriangle</c>, <c>Date</c>, <c>ColorWell</c>, <c>SvgRoot</c>, <c>MathMLMath</c> and so on), or null. The
    /// node keeps its Core-AAM <see cref="Role"/> (empty for elements with no role); an element with an internal role is not
    /// ignored as uninteresting. See profile entry <c>internal-roles</c>.
    /// </summary>
    public string? InternalRole { get; internal set; }

    /// <summary>The accessible name and the step that produced it.</summary>
    public AxText Name { get; internal set; } = AxText.Empty;

    /// <summary>The accessible description and its source.</summary>
    public AxText Description { get; internal set; } = AxText.Empty;

    /// <summary>True when the node is left out of what assistive technology walks: hidden, presentational or uninteresting.</summary>
    public bool IsIgnored { get; internal set; }

    /// <summary>Why the node is ignored; empty when it is not. The values follow the spelling of Chrome's <c>ignoredReasons</c> names, such as <c>ariaHiddenElement</c>, <c>notVisible</c>, <c>presentationalRole</c>, <c>uninteresting</c>.</summary>
    public IReadOnlyList<string> IgnoredReasons { get => _ignoredReasons; internal set => _ignoredReasons = value; }

    /// <summary>Hidden status with its reasons and origin. Layout-dependent hiding is not known (see <see cref="HiddenInfo.LayoutDependent"/>).</summary>
    public HiddenInfo Hidden { get; internal set; }

    /// <summary>States and properties (null members are absent).</summary>
    public AxStates States { get; internal set; } = new();

    /// <summary>The ID-reference relations of the node that it has, resolved.</summary>
    public IReadOnlyList<AxRelation> Relations { get => _relations; internal set => _relations = value; }

    /// <summary>Keyboard focus facts.</summary>
    public AxFocus Focus { get; internal set; }

    /// <summary>Findings about this node, in the order they were found.</summary>
    public IReadOnlyList<AxDiagnostic> Diagnostics { get => _diagnostics; internal set => _diagnostics = value; }

    /// <summary>The byte range of the node in the original input, or none for the document and parser-made nodes without a source.</summary>
    public SourceRange SourceBytes
    {
        get
        {
            SourceRange r = DomNode.Source.Range;
            return r.IsPresent ? DomNode.Document!.ToByteRange(r) : SourceRange.None;
        }
    }

    /// <summary>A selector that matches exactly this element in the document, generated on demand; null for text and the document, or when none can be built.</summary>
    public string? Locator => Kind == AxNodeKind.Element ? _tree.LocatorOf(DomNode) : null;

    /// <summary>The relation of a kind, or null when the node has none.</summary>
    /// <param name="kind">Which relation.</param>
    public AxRelation? GetRelation(AxRelationKind kind)
    {
        foreach (AxRelation r in _relations)
        {
            if (r.Kind == kind)
            {
                return r;
            }
        }

        return null;
    }

    internal bool AriaDisabledChain { get; set; }

    /// <inheritdoc />
    public override string ToString() => Kind == AxNodeKind.Text ? "StaticText \"" + Name.Value + "\"" : Role + (Name.IsEmpty ? string.Empty : " \"" + Name.Value + "\"");
}
