using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Accessibility.Validation;

/// <summary>What a rule concluded about one target.</summary>
public enum Outcome : byte
{
    /// <summary>The target breaks the rule.</summary>
    Failed = 0,

    /// <summary>The target meets the rule.</summary>
    Passed = 1,

    /// <summary>The static facts cannot decide; a person has to look.</summary>
    NeedsReview = 2,

    /// <summary>The verdict needs rendered facts (layout, painting) that were not supplied.</summary>
    NeedsRender = 3,

    /// <summary>The rule found nothing to apply to. One result with no target per rule that applied to nothing.</summary>
    Inapplicable = 4,
}

/// <summary>How serious a failure is, on the four-step scale accessibility checkers commonly use.</summary>
public enum Impact : byte
{
    /// <summary>Unlikely to stop anyone from doing anything.</summary>
    Minor = 0,

    /// <summary>Makes a task harder.</summary>
    Moderate = 1,

    /// <summary>Makes a task very hard for some people.</summary>
    Serious = 2,

    /// <summary>Can make a task impossible for some people.</summary>
    Critical = 3,
}

/// <summary>Whether a problem can stand between a person and a task, computed from the accessibility tree.</summary>
public enum TaskImpact : byte
{
    /// <summary>The target is neither interactive nor part of a form or dialog.</summary>
    Cosmetic = 0,

    /// <summary>The target is interactive (focusable or a widget role) or sits inside a form or dialog.</summary>
    BlocksTask = 1,
}

/// <summary>One thing a rule found on a target: an attribute and value, with a sentence about it.</summary>
/// <param name="Attribute">The attribute concerned (<c>role</c>, <c>aria-label</c>, …), or <see langword="null"/>.</param>
/// <param name="Value">The offending value or token, or <see langword="null"/>.</param>
/// <param name="Message">A short English description.</param>
public readonly record struct Finding(string? Attribute, string? Value, string Message);

/// <summary>An edit a rule proposes. The library never applies it; <see cref="Edit"/> targets the snapshot that was checked.</summary>
/// <param name="Edit">The edit, carrying the rule id and criteria in its <c>Meta</c>.</param>
/// <param name="Description">What the edit does, in a sentence.</param>
public sealed record SuggestedEdit(Edit Edit, string Description);

/// <summary>The result of one rule on one target (or, for <see cref="Outcome.Inapplicable"/>, on nothing).</summary>
public sealed class RuleResult
{
    private readonly Lazy<string?> _locator;

    internal RuleResult(
        IAccessibilityRule rule,
        Outcome outcome,
        Node target,
        string message,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<SuggestedEdit> suggested,
        IReadOnlyList<string> factsMissing,
        IReadOnlyList<string> factsUsed,
        TaskImpact taskImpact,
        string? taskImpactReason,
        bool cssOnly,
        Func<Node, string?> locator,
        SourceRange source)
    {
        Rule = rule;
        Outcome = outcome;
        Target = target;
        Message = message;
        Findings = findings;
        SuggestedEdits = suggested;
        FactsMissing = factsMissing;
        FactsUsed = factsUsed;
        TaskImpact = taskImpact;
        TaskImpactReason = taskImpactReason;
        CssOnly = cssOnly;
        SourceBytes = source;
        _locator = new Lazy<string?>(() => target.IsNull ? null : locator(target));
    }

    /// <summary>The rule that produced the result.</summary>
    public IAccessibilityRule Rule { get; }

    /// <summary>The rule's id.</summary>
    public string RuleId => Rule.Descriptor.Id;

    /// <summary>The verdict.</summary>
    public Outcome Outcome { get; }

    /// <summary>The element (or text node) the verdict is about; the null node for <see cref="Outcome.Inapplicable"/>.</summary>
    public Node Target { get; }

    /// <summary>One sentence about the verdict.</summary>
    public string Message { get; }

    /// <summary>What was found, in document attribute order. Empty for passes.</summary>
    public IReadOnlyList<Finding> Findings { get; }

    /// <summary>Edits that fix the finding. Present only where the rule names exactly one judgement-free fix.</summary>
    public IReadOnlyList<SuggestedEdit> SuggestedEdits { get; }

    /// <summary>
    /// Rendered facts the result used, such as <c>text boxes</c>. Empty when the verdict came from the document, the
    /// cascade and the tree alone; a result that needed facts it did not have lists them in <see cref="FactsMissing"/> instead.
    /// </summary>
    public IReadOnlyList<string> FactsUsed { get; }

    /// <summary>For <see cref="Outcome.NeedsRender"/>, the facts a caller would have to supply.</summary>
    public IReadOnlyList<string> FactsMissing { get; }

    /// <summary>Whether the target can stand between a person and a task.</summary>
    public TaskImpact TaskImpact { get; }

    /// <summary>Why the task impact is what it is: <c>interactive</c>, <c>form</c>, <c>dialog</c>, or <see langword="null"/> for cosmetic.</summary>
    public string? TaskImpactReason { get; }

    /// <summary>
    /// True when the result was computed under a style context with interaction state (focus, hover, active, target):
    /// script-driven states are invisible, so such a result reflects the CSS alone.
    /// </summary>
    public bool CssOnly { get; }

    /// <summary>The target's locator, generated on first use; <see langword="null"/> when there is no target or it has none.</summary>
    public string? Locator => _locator.Value;

    /// <summary>The target's byte range in the source, or <see cref="SourceRange.None"/>.</summary>
    public SourceRange SourceBytes { get; }
}
