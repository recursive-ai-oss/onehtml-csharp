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
