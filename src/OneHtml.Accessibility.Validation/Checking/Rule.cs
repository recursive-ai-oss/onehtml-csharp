using System;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Locators;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

namespace RecursiveAI.OneHtml.Accessibility.Validation;

/// <summary>A check. Implement it to add a private rule; the built-in rules are implementations too.</summary>
/// <remarks>
/// A rule reports a result per target through the <see cref="RuleReporter"/> it is given. A rule that reports nothing
/// is recorded as inapplicable. A rule must be deterministic and must not keep state between calls, since one instance
/// may be used by several checkers at once.
/// </remarks>
public interface IAccessibilityRule
{
    /// <summary>The catalogue record: id, sources, criteria, needs, fix or the reason there is none, tags.</summary>
    RuleDescriptor Descriptor { get; }

    /// <summary>One sentence saying what the rule checks, in plain words.</summary>
    string Description { get; }

    /// <summary>One sentence saying how to fix a failure.</summary>
    string Help { get; }

    /// <summary>The impact of a failure.</summary>
    Impact Impact { get; }

    /// <summary>Evaluates the rule over the context, reporting through <paramref name="reporter"/>.</summary>
    /// <param name="context">The document, styles and tree.</param>
    /// <param name="reporter">Where results go. It also carries the cancellation token.</param>
    void Evaluate(CheckContext context, RuleReporter reporter);

    /// <summary>
    /// What the rule's result for a target depends on, which lets <see cref="Checker.Recheck(CheckReport, Editing.EditResult, RestyleResult, AccessibilityTree, Rendered.RenderedFacts?, CancellationToken)"/>
    /// re-run it only where an edit reached. The default, <see cref="RuleScope.Unspecified"/>, means the rule may read
    /// anything, so it runs in full after every edit; a rule that does not override this keeps working unchanged.
    /// </summary>
    RuleScope Scope => RuleScope.Unspecified;

    /// <summary>
    /// For a <see cref="RuleScope.Page"/> rule: the local names of the elements the rule reads, so an edit that touches none of
    /// them leaves the rule's results alone. <see langword="null"/> (the default) means any change re-runs the rule.
    /// </summary>
    IReadOnlyCollection<string>? RelevantElements => null;
}

/// <summary>
/// The inputs a rule's result for one target reads, declared by <see cref="IAccessibilityRule.Scope"/>. Combine the flags
/// <see cref="Node"/>, <see cref="Descendants"/>, <see cref="Ancestors"/> and <see cref="IdReferences"/> to say that a result
/// depends on all of them; <see cref="Page"/> stands alone.
/// </summary>
/// <remarks>
/// A rule that declares a node-level scope promises three things: it reports each result on the node whose inputs it
/// read (never on another node), it finds its targets by iterating the elements the checker offers it (see
/// <see cref="CheckContext.Elements"/>) or by examining nodes independently of one another, and its outcome and message do
/// not depend on anything but the declared inputs. The checker enforces the first by dropping a result reported on a node it
/// did not ask about; a rule whose result also depends on other nodes must declare more, or nothing.
/// </remarks>
[Flags]
public enum RuleScope
{
    /// <summary>Nothing is declared: the rule runs in full on every re-check. The safe default.</summary>
    Unspecified = 0,

    /// <summary>
    /// The target's own attributes, text, cascaded style and accessibility-tree node (role, name, description, states,
    /// relations, hidden status, focusability, diagnostics and its parent in the tree).
    /// </summary>
    Node = 1,

    /// <summary>The target's DOM descendants too: their attributes, text and tree nodes.</summary>
    Descendants = 2,

    /// <summary>The target's DOM ancestors too: their attributes and style.</summary>
    Ancestors = 4,

    /// <summary>
    /// The elements the target names by ID (<c>aria-labelledby</c>, <c>for</c>, <c>headers</c>, …) and the elements that name
    /// the target, as the declared inputs of <see cref="Node"/> for those elements.
    /// </summary>
    IdReferences = 8,

    /// <summary>
    /// The rule compares or counts across the whole page (duplicate ids, one main landmark). It re-runs in full when the edit
    /// touched anything, or, with <see cref="IAccessibilityRule.RelevantElements"/>, when it touched one of those elements.
    /// </summary>
    Page = 16,

    /// <summary>The own attributes, text and tree node of the target's sibling elements and text (a list item looks at its neighbours).</summary>
    Siblings = 32,
}

/// <summary>What a rule can see: the document, its cascade, the accessibility tree and the style context they were computed under.</summary>
public sealed class CheckContext
{
    private readonly Lazy<LocatorGenerator> _locators;

    /// <summary>Creates a context over a tree. The document, styles and style context are the tree's own.</summary>
    /// <param name="tree">The accessibility tree to check.</param>
    /// <param name="facts">Rendered facts captured for the tree's document, or <see langword="null"/> (the default) for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="facts"/> were not captured for the tree's document (the same <c>Document</c> object).</exception>
    public CheckContext(AccessibilityTree tree, RenderedFacts? facts = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (facts is not null && (facts.DocumentId != tree.Document.Id || facts.NodeCount != tree.Document.NodeCount))
        {
            throw new ArgumentException("The rendered facts describe a different document.", nameof(facts));
        }

        Tree = tree;
        Facts = facts;
        _locators = new Lazy<LocatorGenerator>(() => new LocatorGenerator(tree.Document), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The accessibility tree.</summary>
    public AccessibilityTree Tree { get; }

    /// <summary>The rendered facts the caller supplied, or <see langword="null"/>. Rules that need layout decide only when they are present.</summary>
    public RenderedFacts? Facts { get; }

    /// <summary>The document the tree was built from.</summary>
    public Document Document => Tree.Document;

    /// <summary>The cascade the tree was built with.</summary>
    public DocumentStyles Styles => Tree.Styles;

    /// <summary>The style context (viewport, media features, interaction state) the tree was computed under.</summary>
    public StyleContext StyleContext => Tree.Context;

    /// <summary>The tree's node for an element, or <see langword="null"/> when it is not in the tree (not rendered).</summary>
    /// <param name="element">An element of <see cref="Document"/>.</param>
    public AxNode? Find(Node element) => Tree.Find(element);

    /// <summary>
    /// True when the element is exposed to assistive technology: it has a tree node and nothing hides it
    /// (<c>aria-hidden</c>, <c>inert</c> and <c>visibility</c> hiding count as hiding). This is the "included in the
    /// accessibility tree" of the ACT rules.
    /// </summary>
    /// <param name="element">An element of <see cref="Document"/>.</param>
    public bool IsExposed(Node element) =>
        Tree.Find(element) is { } n && !n.Hidden.IsNotRendered
        && (n.Hidden.Reasons & (HiddenReasons.AriaHidden | HiddenReasons.Inert | HiddenReasons.VisibilityHidden)) == 0;

    // Set by the checker for a re-check: the elements (ascending index) a node-scoped rule has to look at again, and a mask over
    // every node index of the results it may report. Null means the whole document.
    internal int[]? CandidateElements { get; set; }

    internal bool[]? CandidateMask { get; set; }

    internal int[]? CandidateNodes { get; set; }

    /// <summary>
    /// The elements a rule should examine, in document order (template contents excluded). During a re-check this is only the
    /// elements a node-scoped rule has to look at again, so a rule that iterates this and reports on the element it examined
    /// does the least work with no change to its code.
    /// </summary>
    public IEnumerable<Node> Elements()
    {
        if (CandidateElements is not { } list)
        {
            return Document.Root.DescendantElements;
        }

        return CandidatesOnly(list);
    }

    /// <summary>Every element of the document in document order (template contents excluded), whatever a re-check restricts <see cref="Elements"/> to. For a rule that must look at the whole page to decide about one element.</summary>
    public IEnumerable<Node> AllElements() => Document.Root.DescendantElements;

    /// <summary>
    /// The tree nodes a rule should examine: the whole tree normally, and during a re-check only the nodes whose DOM node the
    /// edit reached (see <see cref="Elements"/>). Nodes a tree does not hold for a DOM node (not rendered) are not returned.
    /// </summary>
    public IEnumerable<AxNode> TreeNodes()
    {
        if (CandidateNodes is not { } list)
        {
            return Tree.Nodes;
        }

        return TreeNodesOf(list);
    }

    private IEnumerable<AxNode> TreeNodesOf(int[] list)
    {
        foreach (int i in list)
        {
            // Find can return a node that is not in Tree.Nodes (an element owned through aria-owns by something outside the tree);
            // a rule that walks the whole tree never sees it, so neither does a narrowed walk.
            if (Tree.Find(Document.GetNode(i)) is { } n && n.Id >= 0 && n.Id < Tree.Nodes.Count && ReferenceEquals(Tree.Nodes[n.Id], n))
            {
                yield return n;
            }
        }
    }

    private IEnumerable<Node> CandidatesOnly(int[] list)
    {
        foreach (int i in list)
        {
            yield return Document.GetNode(i);
        }
    }

    private readonly Dictionary<string, object> _memo = [];

    // A value several rules share for this tree, computed by the first rule that asks. Rules run one at a time.
    internal T Memo<T>(string key, Func<T> create) where T : class
    {
        if (!_memo.TryGetValue(key, out object? v))
        {
            _memo[key] = v = create();
        }

        return (T)v;
    }

    internal string? LocatorOf(Node node) => _locators.Value.TryGenerate(node, out string? l) ? l : null;
}

/// <summary>Collects a rule's results. Created by the <see cref="Checker"/>.</summary>
public sealed class RuleReporter
{
    private readonly IAccessibilityRule _rule;
    private readonly CheckContext _context;
    private readonly List<RuleResult> _results;
    private readonly List<string> _criteria = [];
    private readonly int _maxResults;

    internal RuleReporter(IAccessibilityRule rule, CheckContext context, List<RuleResult> results, int maxResults, CancellationToken cancellationToken)
    {
        _rule = rule;
        _context = context;
        _results = results;
        _maxResults = maxResults;
        CancellationToken = cancellationToken;
        foreach (SuccessCriterion c in rule.Descriptor.Criteria)
        {
            _criteria.Add(c.Number);
        }
    }

    /// <summary>The token to observe in long loops.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>True once the checker's result limit for this rule was reached; further reports are dropped.</summary>
    public bool LimitReached { get; private set; }

    /// <summary>Reports that the target meets the rule.</summary>
    /// <param name="target">The element or text node.</param>
    /// <param name="message">A sentence, or <see langword="null"/> for the rule's default.</param>
    /// <param name="factsUsed">Rendered facts the verdict used; empty for none.</param>
    public void Passed(Node target, string? message = null, IReadOnlyList<string>? factsUsed = null) =>
        Add(Outcome.Passed, target, message ?? "Meets the rule.", [], [], [], factsUsed);

    /// <summary>Reports that the target breaks the rule.</summary>
    /// <param name="target">The element or text node.</param>
    /// <param name="message">A sentence.</param>
    /// <param name="findings">What was found; may be empty.</param>
    /// <param name="suggested">Edits that fix it, only where there is exactly one judgement-free fix.</param>
    /// <param name="factsUsed">Rendered facts the verdict used; empty for none.</param>
    public void Failed(Node target, string message, IReadOnlyList<Finding>? findings = null, IReadOnlyList<SuggestedEdit>? suggested = null, IReadOnlyList<string>? factsUsed = null) =>
        Add(Outcome.Failed, target, message, findings ?? [], suggested ?? [], [], factsUsed);

    /// <summary>Reports that a person has to decide.</summary>
    /// <param name="target">The element or text node.</param>
    /// <param name="message">A sentence.</param>
    /// <param name="findings">What was found; may be empty.</param>
    /// <param name="factsUsed">Rendered facts the verdict used; empty for none.</param>
    public void NeedsReview(Node target, string message, IReadOnlyList<Finding>? findings = null, IReadOnlyList<string>? factsUsed = null) =>
        Add(Outcome.NeedsReview, target, message, findings ?? [], [], [], factsUsed);

    /// <summary>Reports that rendered facts are needed to decide.</summary>
    /// <param name="target">The element or text node.</param>
    /// <param name="message">A sentence.</param>
    /// <param name="missingFacts">Names of the facts a caller would have to supply.</param>
    public void NeedsRender(Node target, string message, IReadOnlyList<string> missingFacts) =>
        Add(Outcome.NeedsRender, target, message, [], [], missingFacts);

    /// <summary>
    /// Creates a suggested edit whose metadata names the rule and its criteria, as the edit format's <c>meta</c>.
    /// </summary>
    /// <param name="edit">The edit.</param>
    /// <param name="description">What it does, in a sentence.</param>
    public SuggestedEdit Suggest(Editing.Edit edit, string description)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var meta = new Dictionary<string, object?>
        {
            ["rule"] = _rule.Descriptor.Id,
            ["criteria"] = _criteria.ToArray(),
            ["suggested"] = true,
        };
        return new SuggestedEdit(edit.WithMeta(meta), description);
    }

    internal bool HasResults => _results.Count > 0;

    private void Add(Outcome outcome, Node target, string message, IReadOnlyList<Finding> findings, IReadOnlyList<SuggestedEdit> suggested, IReadOnlyList<string> missing, IReadOnlyList<string>? factsUsed = null)
    {
        if (_context.CandidateMask is { } mask && (target.IsNull || target.Index >= mask.Length || !mask[target.Index]))
        {
            return;
        }

        if (_results.Count >= _maxResults)
        {
            LimitReached = true;
            return;
        }

        (TaskImpact impact, string? reason) = TaskImpactAnalyzer.Analyze(_context, target);
        SourceRange range = SourceRange.None;
        if (!target.IsNull && target.Source.Range is { IsPresent: true } r)
        {
            range = target.Document!.ToByteRange(r);
        }

        _results.Add(new RuleResult(_rule, outcome, target, message, findings, suggested, missing, factsUsed ?? [], impact, reason,
            _context.StyleContext.HasInteractionState, _context.LocatorOf, range));
    }
}
