using System;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RecursiveAI.OneHtml.Accessibility.Aria;

namespace RecursiveAI.OneHtml.Accessibility.Validation;

/// <summary>
/// Decides whether a problem on a node can stand between a person and a task: the node is interactive (focusable, or a
/// widget role) or sits inside a form or dialog. It is computed from the accessibility tree and nothing else.
/// </summary>
internal static class TaskImpactAnalyzer
{
    private static readonly HashSet<string> s_widgetRoles = new(StringComparer.Ordinal)
    {
        "button", "link", "checkbox", "radio", "switch", "textbox", "searchbox", "combobox", "listbox", "slider", "spinbutton",
        "menuitem", "menuitemcheckbox", "menuitemradio", "tab", "treeitem", "option", "gridcell", "scrollbar", "menu", "menubar",
        "tablist", "tree", "treegrid", "grid", "radiogroup", "toolbar",
    };

    private static readonly HashSet<string> s_containerRoles = new(StringComparer.Ordinal) { "form", "dialog", "alertdialog" };

    internal static (TaskImpact Impact, string? Reason) Analyze(CheckContext context, Node target)
    {
        if (target.IsNull || context.Find(target) is not { } node)
        {
            return (TaskImpact.Cosmetic, null);
        }

        if (node.Focus.IsFocusable || (node.AriaRole is { } r && s_widgetRoles.Contains(r.Name)))
        {
            return (TaskImpact.BlocksTask, "interactive");
        }

        for (AxNode? a = node; a is not null; a = a.Parent)
        {
            if (a.Kind == AxNodeKind.Element && ((a.AriaRole is { } ar && s_containerRoles.Contains(ar.Name)) || a.DomNode.LocalName == "form" && a.DomNode.NamespaceURI == "http://www.w3.org/1999/xhtml"))
            {
                return (TaskImpact.BlocksTask, a.AriaRole?.Name is "dialog" or "alertdialog" ? "dialog" : "form");
            }
        }

        return (TaskImpact.Cosmetic, null);
    }
}

/// <summary>What to run. Every non-empty selection must hold for a rule to run (they intersect).</summary>
public sealed class CheckOptions
{
    /// <summary>Run only rules in the profile (<see cref="Catalogue.ValidationProfile.Includes(Catalogue.RuleDescriptor)"/>). Default: every rule.</summary>
    public Catalogue.ValidationProfile? Profile { get; init; }

    /// <summary>Run only rules with at least one of these tags (case-sensitive, ordinal). Default: no restriction.</summary>
    public IReadOnlyCollection<string> Tags { get; init; } = [];

    /// <summary>Run only rules that check at least one of these success criteria, by number such as <c>4.1.2</c>. Default: no restriction.</summary>
    public IReadOnlyCollection<string> Criteria { get; init; } = [];

    /// <summary>Run only these rules, by id. Default: no restriction. An unknown id is an <see cref="ArgumentException"/>.</summary>
    public IReadOnlyCollection<string> RuleIds { get; init; } = [];

    /// <summary>
    /// Rendered facts captured for the document (layout boxes, text boxes). Rules that need layout use them when present and
    /// report needs-render, naming the missing facts, when absent. Default: none. They must describe the same document.
    /// </summary>
    public RenderedFacts? Facts { get; init; }

    /// <summary>Keep passed results. Default <see langword="true"/>; false keeps the report small on big pages.</summary>
    public bool IncludePasses { get; init; } = true;

    /// <summary>The most results one rule may report; further ones are dropped and <see cref="CheckReport.LimitReached"/> is set. Default 100,000.</summary>
    public int MaxResultsPerRule { get; init; } = 100_000;
}

/// <summary>Runs rules over an accessibility tree and collects their results in a fixed order.</summary>
/// <remarks>
/// Results are ordered by rule id (ordinal), then by the document order of the target, then by outcome
/// (failed, passed, needs-review, needs-render); a rule that applied to nothing contributes one inapplicable result.
/// The same input gives the same report whatever order the rules were registered in.
/// </remarks>
public sealed class Checker
{
    private readonly IAccessibilityRule[] _rules;

    /// <summary>Creates a checker with the built-in rules plus <paramref name="privateRules"/>.</summary>
    /// <param name="privateRules">Caller-supplied rules; their ids must differ from each other and from the built-in ones.</param>
    /// <exception cref="ArgumentException">Two rules share an id.</exception>
    public Checker(IEnumerable<IAccessibilityRule>? privateRules = null)
        : this(privateRules is null ? [.. BuiltInRules.All] : [.. BuiltInRules.All, .. privateRules])
    {
    }

    // The list overload is the one that validates and sorts; the public constructor adds the built-in rules first.
    private Checker(List<IAccessibilityRule> list)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (IAccessibilityRule r in list)
        {
            ArgumentNullException.ThrowIfNull(r);
            if (!seen.Add(r.Descriptor.Id))
            {
                throw new ArgumentException("Two rules share the id " + r.Descriptor.Id + ".", nameof(list));
            }
        }

        list.Sort((a, b) => string.CompareOrdinal(a.Descriptor.Id, b.Descriptor.Id));
        _rules = [.. list];
    }

    /// <summary>A checker with only the given rules (no built-in ones).</summary>
    /// <param name="rules">The rules.</param>
    public static Checker WithRules(IEnumerable<IAccessibilityRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return new Checker([.. rules]);
    }

    /// <summary>Every rule this checker knows, in id order.</summary>
    public IReadOnlyList<IAccessibilityRule> Rules => _rules;

    /// <summary>Builds the tree for a document with its default cascade and checks it.</summary>
    /// <param name="document">The document.</param>
    /// <param name="options">What to run; <see langword="null"/> runs every rule.</param>
    /// <param name="cancellationToken">Observed between rules and inside long loops.</param>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public CheckReport Check(Document document, CheckOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        AccessibilityTree tree = AccessibilityTree.Build(document, Css.DocumentStyles.Create(document, null, null, cancellationToken), null, null, cancellationToken);
        return Check(tree, options, cancellationToken);
    }

    /// <summary>Checks an accessibility tree already built (under any style context).</summary>
    /// <param name="tree">The tree.</param>
    /// <param name="options">What to run; <see langword="null"/> runs every rule.</param>
    /// <param name="cancellationToken">Observed between rules and inside long loops.</param>
    /// <exception cref="ArgumentException">A rule id in the options is unknown.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public CheckReport Check(AccessibilityTree tree, CheckOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        options ??= new CheckOptions();
        foreach (string id in options.RuleIds)
        {
            if (!_rules.Any(r => string.Equals(r.Descriptor.Id, id, StringComparison.Ordinal)))
            {
                throw new ArgumentException("Unknown rule id " + id + ".", nameof(options));
            }
        }

        var context = new CheckContext(tree, options.Facts);
        var all = new List<RuleResult>();
        var ran = new List<IAccessibilityRule>();
        bool limit = false;
        foreach (IAccessibilityRule rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Selected(rule, options))
            {
                continue;
            }

            ran.Add(rule);
            var results = new List<RuleResult>();
            var reporter = new RuleReporter(rule, context, results, Math.Max(1, options.MaxResultsPerRule), cancellationToken);
            rule.Evaluate(context, reporter);
            limit |= reporter.LimitReached;
            if (results.Count == 0)
            {
                all.Add(new RuleResult(rule, Outcome.Inapplicable, default, "The rule found nothing to check.", [], [], [], [], TaskImpact.Cosmetic, null,
                    tree.Context.HasInteractionState, _ => null, SourceRange.None));
                continue;
            }

            var indexed = results.Select((r, i) => (r, i)).ToList();
            indexed.Sort((a, b) =>
            {
                int c = a.r.Target.IsNull.CompareTo(b.r.Target.IsNull);
                if (c == 0)
                {
                    c = (a.r.Target.IsNull ? 0 : a.r.Target.Index).CompareTo(b.r.Target.IsNull ? 0 : b.r.Target.Index);
                }

                if (c == 0)
                {
                    c = ((int)a.r.Outcome).CompareTo((int)b.r.Outcome);
                }

                return c != 0 ? c : a.i.CompareTo(b.i);
            });
            foreach ((RuleResult r, _) in indexed)
            {
                if (r.Outcome != Outcome.Passed || options.IncludePasses)
                {
                    all.Add(r);
                }
            }
        }

        return new CheckReport(tree, options.Profile, ran, all, limit);
    }

    private static bool Selected(IAccessibilityRule rule, CheckOptions o)
    {
        RuleDescriptorView d = new(rule.Descriptor);
        if (o.Profile is not null && !o.Profile.Includes(rule.Descriptor))
        {
            return false;
        }

        if (o.RuleIds.Count > 0 && !o.RuleIds.Contains(d.Id))
        {
            return false;
        }

        if (o.Tags.Count > 0 && !o.Tags.Any(t => d.Tags.Contains(t)))
        {
            return false;
        }

        if (o.Criteria.Count > 0 && !o.Criteria.Any(c => d.Criteria.Contains(c)))
        {
            return false;
        }

        return true;
    }

    private readonly struct RuleDescriptorView
    {
        public RuleDescriptorView(Catalogue.RuleDescriptor d)
        {
            Id = d.Id;
            Tags = d.Tags;
            Criteria = d.Criteria.Select(c => c.Number).ToList();
        }

        public string Id { get; }

        public IReadOnlyList<string> Tags { get; }

        public IReadOnlyList<string> Criteria { get; }
    }
}
