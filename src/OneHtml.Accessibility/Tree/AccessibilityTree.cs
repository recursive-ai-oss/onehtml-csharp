using System;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Accessibility.Aria;
using RecursiveAI.OneHtml.Accessibility.Tree;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;
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
    private DependencyIndex? _deps;

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

        return new TreeBuilder(document, styles, options ?? new AccessibilityOptions(), null, cancellationToken).Run();
    }


    /// <summary>What <see cref="Update(EditResult, RestyleResult, CancellationToken)"/> or <see cref="WithContext"/> did to make this tree; null for a tree made by <see cref="Build"/>.</summary>
    public TreeUpdateInfo? UpdateInfo { get; internal set; }

    internal bool UsedCounters { get; set; }

    internal HiddenMap HiddenMap => _hidden;

    internal OwnsMap OwnsMap => _owns;

    internal AxNode? FindByIndex(int domIndex) => domIndex >= 0 && domIndex < _byDom.Length ? _byDom[domIndex] : null;

    internal DependencyIndex Dependencies(int maxReferences, CancellationToken ct)
    {
        lock (_locators)
        {
            return _deps ??= new DependencyIndex(Document, maxReferences, ct);
        }
    }

    /// <summary>
    /// The tree of the document an edit produced, equal to <see cref="Build"/> on the new document with the same styles and
    /// options, computed by carrying over the accessible name and description of every element whose inputs the edit did not
    /// touch. The inputs are the element's own attributes, its contents, the elements its ID references and label association
    /// reach, the elements that reference it, its hidden status and role, its place under its parent or <c>aria-owns</c> owner,
    /// its style and its pseudo-element styles (<see cref="RestyleResult.PseudoElementsMayDiffer"/>). Roles, hidden status,
    /// <c>aria-owns</c> structure, states, relations, focus order and diagnostics are linear passes and are computed afresh.
    /// The tree is built afresh, as <see cref="Build"/> does, when the restyle was a full recompute, when this tree reached a
    /// limit, or when CSS counters took part in a name; <see cref="UpdateInfo"/> says which.
    /// </summary>
    /// <param name="edit">A successful result of <c>Document.Apply</c> on <see cref="Document"/>.</param>
    /// <param name="restyle">The result of restyling <see cref="Styles"/> with the same edit (<c>styles.Restyle(edit)</c>).</param>
    /// <param name="cancellationToken">Observed periodically.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The edit failed, was not applied to this tree's document, or the restyle is not of the edit's document.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public AccessibilityTree Update(EditResult edit, RestyleResult restyle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!edit.Succeeded || edit.Changes is null || edit.Document is null)
        {
            throw new ArgumentException("The edit did not succeed, so there is no new document.", nameof(edit));
        }

        return UpdateCore(edit.Document, edit.Changes, restyle, cancellationToken);
    }

    /// <summary>
    /// The tree after a batch of document and stylesheet edits; see <see cref="Update(EditResult, RestyleResult, CancellationToken)"/>.
    /// A batch with no document change (only stylesheets) keeps the document and updates the elements the restyle reached.
    /// </summary>
    /// <param name="batch">A successful result of <c>EditBatch.Apply</c> on <see cref="Document"/>.</param>
    /// <param name="restyle">The result of restyling <see cref="Styles"/> with the same batch.</param>
    /// <param name="cancellationToken">Observed periodically.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The batch failed, or does not belong to this tree's document or the restyle.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public AccessibilityTree Update(BatchResult batch, RestyleResult restyle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!batch.Succeeded)
        {
            throw new ArgumentException("The batch did not succeed, so there is no new document.", nameof(batch));
        }

        return UpdateCore(batch.Document ?? Document, batch.Changes, restyle, cancellationToken);
    }

    /// <summary>
    /// The tree of the same document under another style context (focus, hover, target, active), recomputing the names of the
    /// elements whose style the context changes; equal to <see cref="Build"/> with that context. A context with other media
    /// features, or a pseudo-element rule that reads an interaction state, builds the tree afresh.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">Observed periodically.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    /// <exception cref="ArgumentException">The context names a node of another document.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public AccessibilityTree WithContext(StyleContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        DocumentStyles styles = Styles.WithContext(context, cancellationToken);
        string? reason = null;
        if (!context.Media.Equals(Context.Media))
        {
            reason = "the media features changed";
        }
        else if (Styles.HasInteractivePseudoElementRules)
        {
            reason = "a pseudo-element rule reads an interaction state";
        }

        return Next(Document, null, styles, styles.ChangedElements, reason, cancellationToken);
    }

    private AccessibilityTree UpdateCore(Document newDocument, ChangeSet? changes, RestyleResult restyle, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(restyle);
        if (!ReferenceEquals(restyle.Styles.Document, newDocument))
        {
            throw new ArgumentException("The restyle is not of the edited document.", nameof(restyle));
        }

        if (changes is not null ? !ReferenceEquals(changes.OldDocument, Document) : !ReferenceEquals(newDocument, Document))
        {
            throw new ArgumentException("The edit was not applied to this tree's document.");
        }

        string? reason = restyle.FullRecompute ? "the restyle recomputed every style" : null;
        var seeds = new List<int>(restyle.RestyledElements);
        if (restyle.PseudoElementsMayDiffer is { } pseudo)
        {
            seeds.AddRange(pseudo);
        }

        return Next(newDocument, changes, restyle.Styles, seeds, reason, ct);
    }

    private AccessibilityTree Next(Document newDocument, ChangeSet? changes, DocumentStyles styles, IReadOnlyList<int> seeds, string? fullReason, CancellationToken ct)
    {
        fullReason ??= LimitReached ? "the previous tree reached a limit" : UsedCounters ? "CSS counters took part in a name" : null;
        if (fullReason is not null)
        {
            AccessibilityTree full = new TreeBuilder(newDocument, styles, Options, null, ct).Run();
            full.UpdateInfo = new TreeUpdateInfo(false, fullReason, CountElements(full), CountElements(full), 0);
            return full;
        }

        var plan = new UpdatePlan(this, newDocument, changes, seeds);
        AccessibilityTree tree = new TreeBuilder(newDocument, styles, Options, plan, ct).Run();
        tree.UpdateInfo = plan.Exhausted
            ? new TreeUpdateInfo(false, "the dependency walk reached its work bound", CountElements(tree), plan.Recomputed, plan.Reused)
            : new TreeUpdateInfo(true, null, CountElements(tree), plan.Recomputed, plan.Reused);
        return tree;
    }

    private static int CountElements(AccessibilityTree t)
    {
        int c = 0;
        foreach (AxNode n in t.Nodes)
        {
            if (n.Kind == AxNodeKind.Element)
            {
                c++;
            }
        }

        return c;
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

    /// <summary>What differs between this tree and a later one; see <see cref="TreeDiff"/>.</summary>
    /// <param name="after">The later tree.</param>
    /// <param name="changes">The change set of the edit that produced the later snapshot; <see langword="null"/> when both trees are of the same document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="after"/> is null.</exception>
    /// <exception cref="ArgumentException">The trees are of different documents and <paramref name="changes"/> does not relate them.</exception>
    public TreeDiff Diff(AccessibilityTree after, ChangeSet? changes = null) => TreeDiff.Compute(this, after, changes);

    /// <summary>The reading view of this tree; see <see cref="ReadingView"/>.</summary>
    /// <param name="cancellationToken">Observed periodically.</param>
    public ReadingView CreateReadingView(CancellationToken cancellationToken = default) => ReadingView.Create(this, cancellationToken);

    /// <summary>The navigation lists of this tree; see <see cref="NavigationLists"/>.</summary>
    /// <param name="cancellationToken">Observed periodically.</param>
    public NavigationLists CreateNavigationLists(CancellationToken cancellationToken = default) => NavigationLists.Create(this, cancellationToken);

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
