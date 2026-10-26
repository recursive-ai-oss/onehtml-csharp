using System;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Accessibility.Aria;
using RecursiveAI.OneHtml.Accessibility.Tree;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Locators;

namespace RecursiveAI.OneHtml.Accessibility;

/// <summary>
/// The accessibility tree of a document under a style context: the objects a browser exposes to assistive technology, with
/// roles, accessible names and descriptions, states, relations, focus and hidden status. Built eagerly; immutable and safe to
/// read from any number of threads afterwards.
/// </summary>
public sealed class AccessibilityTree
{
    private readonly AxNode?[] _byDom;
    private readonly HiddenMap _hidden;
    private readonly RoleMap _roles;
    private readonly OwnsMap _owns;
    private readonly Lazy<LocatorGenerator> _locators;

    internal AccessibilityTree(Document document, DocumentStyles styles, AccessibilityOptions options, HiddenMap hidden, RoleMap roles, OwnsMap owns)
    {
        _owns = owns;
        Document = document;
        Styles = styles;
        Options = options;
        _hidden = hidden;
        _roles = roles;
        _byDom = new AxNode?[document.NodeCount];
        _locators = new Lazy<LocatorGenerator>(() => new LocatorGenerator(document), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The document the tree describes.</summary>
    public Document Document { get; }

    /// <summary>The styles it was computed with, under <see cref="Context"/>.</summary>
    public DocumentStyles Styles { get; }

    /// <summary>The style context: viewport, media features and interaction state.</summary>
    public StyleContext Context => Styles.Context;

    /// <summary>The limits the tree was built with.</summary>
    public AccessibilityOptions Options { get; }

    /// <summary>The document node's tree node.</summary>
    public AxNode Root { get; internal set; } = null!;

    /// <summary>Every node in tree order (depth first, children in order), the root first. <see cref="AxNode.Id"/> is the index.</summary>
    public IReadOnlyList<AxNode> Nodes { get; internal set; } = [];

    /// <summary>The nodes sequential keyboard navigation visits, in order (positive <c>tabindex</c> first, then document order).</summary>
    public IReadOnlyList<AxNode> FocusOrder { get; internal set; } = [];

    /// <summary>Every finding, in tree order, those of the tree as a whole last.</summary>
    public IReadOnlyList<AxDiagnostic> Diagnostics { get; internal set; } = [];

    /// <summary>True when a limit stopped some computation, so some names are incomplete; see the <see cref="AxDiagnosticKind.LimitExceeded"/> diagnostics.</summary>
    public bool LimitReached { get; internal set; }

    /// <summary>
    /// Builds the tree. Roles, hidden status, <c>aria-owns</c> re-parenting, names, states, relations and focus are all
    /// computed here, in time linear in the document (name computations are bounded by <see cref="AccessibilityOptions.MaxNameWork"/>).
    /// No step recurses on the depth of the input.
    /// </summary>
    /// <param name="document">A parsed document.</param>
    /// <param name="styles">The cascade of the same document.</param>
    /// <param name="context">A style context to compute under; <see langword="null"/> uses the context of <paramref name="styles"/>. A different context is applied as an overlay that restyles only what it reaches.</param>
    /// <param name="options">Limits; defaults when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Observed periodically.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="styles"/> belongs to another document.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static AccessibilityTree Build(Document document, DocumentStyles styles, StyleContext? context = null, AccessibilityOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(styles);
        if (!ReferenceEquals(styles.Document, document))
        {
            throw new ArgumentException("The styles belong to another document.", nameof(styles));
        }

        if (context is not null && !context.Equals(styles.Context))
        {
            styles = styles.WithContext(context, cancellationToken);
        }

        return new TreeBuilder(document, styles, options ?? new AccessibilityOptions(), cancellationToken).Run();
    }

    /// <summary>The tree node of a DOM node, or <see langword="null"/> when it has none (not rendered, a comment, a whitespace-only text between blocks).</summary>
    /// <param name="node">A node of <see cref="Document"/>.</param>
    /// <exception cref="ArgumentException">The node is not from this document.</exception>
    public AxNode? Find(Node node)
    {
        if (node.IsNull || !ReferenceEquals(node.Document, Document))
        {
            throw new ArgumentException("The node is not from this tree's document.", nameof(node));
        }

        return _byDom[node.Index];
    }

    /// <summary>Hidden status of any element or text node of the document, including nodes that have no tree node because they are not rendered.</summary>
    /// <param name="node">A node of <see cref="Document"/>.</param>
    /// <exception cref="ArgumentException">The node is not from this document.</exception>
    public HiddenInfo GetHidden(Node node)
    {
        if (node.IsNull || !ReferenceEquals(node.Document, Document))
        {
            throw new ArgumentException("The node is not from this tree's document.", nameof(node));
        }

        return _hidden.Get(node);
    }

    /// <summary>The role-layer result for an element (explicit and implicit role, origin, role diagnostics), with names resolved.</summary>
    public RoleMap Roles => _roles;

    /// <summary>The Chrome-shaped export (<c>Accessibility.getFullAXTree</c> nodes) as JSON; see <c>docs/accessibility-tree.md</c>.</summary>
    public string ToChromeJson() => ChromeAxExport.ToJson(this);

    /// <summary>
    /// The accessible name of any element of the document, including one that is not in the tree (not rendered, so it has no
    /// <see cref="AxNode"/>), computed the same way as a tree node's name.
    /// </summary>
    /// <param name="element">An element of <see cref="Document"/>.</param>
    /// <exception cref="ArgumentException">The node is not an element of this document.</exception>
    public AxText ComputeName(Node element)
    {
        if (element.IsNull || !ReferenceEquals(element.Document, Document) || !element.IsElement)
        {
            throw new ArgumentException("The node is not an element of this tree's document.", nameof(element));
        }

        return NewNames().ComputeName(element);
    }

    /// <summary>The accessible description of any element of the document; see <see cref="ComputeName"/>.</summary>
    /// <param name="element">An element of <see cref="Document"/>.</param>
    /// <exception cref="ArgumentException">The node is not an element of this document.</exception>
    public AxText ComputeDescription(Node element)
    {
        if (element.IsNull || !ReferenceEquals(element.Document, Document) || !element.IsElement)
        {
            throw new ArgumentException("The node is not an element of this tree's document.", nameof(element));
        }

        NameComputer names = NewNames();
        return names.ComputeDescription(element, names.ComputeName(element).Source);
    }

    private NameComputer NewNames() =>
        new(Document, Styles, _hidden, _roles, _owns, Options, new CounterResolver(Document, Styles, _hidden), default);

    internal void Register(Node dom, AxNode node) => _byDom[dom.Index] = node;

    internal string? LocatorOf(Node element)
    {
        lock (_locators)
        {
            return _locators.Value.TryGenerate(element, out string? l) ? l : null;
        }
    }
}
