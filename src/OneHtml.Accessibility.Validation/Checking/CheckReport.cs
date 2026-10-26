using System;
using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Accessibility.Validation;

/// <summary>The results of one <see cref="Checker"/> run, in the fixed order described on <see cref="Checker"/>.</summary>
public sealed class CheckReport
{
    internal CheckReport(AccessibilityTree tree, Catalogue.ValidationProfile? profile, IReadOnlyList<IAccessibilityRule> rulesRun, IReadOnlyList<RuleResult> results, bool limitReached,
        CheckOptions options, IReadOnlyList<RuleSlice> slices, RecheckInfo? recheck = null)
    {
        Options = options;
        Slices = slices;
        Recheck = recheck;
        Tree = tree;
        Profile = profile;
        RulesRun = rulesRun;
        Results = results;
        LimitReached = limitReached;
    }

    internal CheckOptions Options { get; }

    // Where each rule's results sit in Results, so a re-check can take them over rule by rule.
    internal IReadOnlyList<RuleSlice> Slices { get; }

    /// <summary>
    /// How <see cref="Checker.Recheck(CheckReport, Editing.EditResult, RestyleResult, AccessibilityTree, Rendered.RenderedFacts?, System.Threading.CancellationToken)"/>
    /// made this report; <see langword="null"/> for a report made by <see cref="Checker.Check(Document, CheckOptions?, System.Threading.CancellationToken)"/>.
    /// </summary>
    public RecheckInfo? Recheck { get; }

    /// <summary>The tree that was checked.</summary>
    public AccessibilityTree Tree { get; }

    /// <summary>The document that was checked.</summary>
    public Document Document => Tree.Document;

    /// <summary>The style context the tree was computed under.</summary>
    public StyleContext StyleContext => Tree.Context;

    /// <summary>The profile that selected the rules, or <see langword="null"/> when none was given.</summary>
    public Catalogue.ValidationProfile? Profile { get; }

    /// <summary>The rules that ran, in id order.</summary>
    public IReadOnlyList<IAccessibilityRule> RulesRun { get; }

    /// <summary>Every result, ordered by rule id, then target in document order, then outcome.</summary>
    public IReadOnlyList<RuleResult> Results { get; }

    /// <summary>True when some rule reached <see cref="CheckOptions.MaxResultsPerRule"/> and dropped results.</summary>
    public bool LimitReached { get; }

    /// <summary>The results with the given outcome, in report order.</summary>
    /// <param name="outcome">The outcome.</param>
    public IEnumerable<RuleResult> WithOutcome(Outcome outcome) => Results.Where(r => r.Outcome == outcome);

    /// <summary>The number of results per outcome.</summary>
    /// <param name="outcome">The outcome.</param>
    public int Count(Outcome outcome) => Results.Count(r => r.Outcome == outcome);

    /// <summary>
    /// The suggested edits of all failed results as an edit-format envelope (version 1), in report order and without
    /// duplicates (same operation, target, name and value). Replaying or applying it changes nothing else.
    /// </summary>
    /// <exception cref="ArgumentException">A target cannot be described (it has no source range or locator).</exception>
    public EditEnvelope SuggestedEdits() => EditJson.Create(Document, SuggestedEditList());

    /// <summary>The distinct edits that <see cref="SuggestedEdits"/> would contain, for applying with <c>Document.Apply</c>.</summary>
    public IReadOnlyList<Edit> SuggestedEditList()
    {
        var edits = new List<Edit>();
        var seen = new HashSet<(EditOperation, int, string?, string?)>();
        foreach (RuleResult r in Results)
        {
            foreach (SuggestedEdit s in r.SuggestedEdits)
            {
                if (seen.Add((s.Edit.Operation, s.Edit.Target.Index, s.Edit.Name, s.Edit.Value)))
                {
                    edits.Add(s.Edit);
                }
            }
        }

        return edits;
    }
}

/// <summary>Where one rule's results sit in a report, and the passes that were left out of it.</summary>
/// <param name="Rule">The rule.</param>
/// <param name="Start">Index of its first entry in <see cref="CheckReport.Results"/>.</param>
/// <param name="Count">How many entries it has there (an inapplicable rule has one).</param>
/// <param name="Inapplicable">True when the entry is the rule's single inapplicable result.</param>
/// <param name="DroppedPasses">Node indexes of the passed results left out because <see cref="CheckOptions.IncludePasses"/> was false.</param>
internal readonly record struct RuleSlice(IAccessibilityRule Rule, int Start, int Count, bool Inapplicable, int[] DroppedPasses);

/// <summary>What a re-check did, for callers that want to know how much work it saved.</summary>
public sealed class RecheckInfo
{
    internal RecheckInfo(bool incremental, string? fullRunReason, int rulesRerunInFull, int rulesRerunPartly, int rulesCarried, int nodesReexamined)
    {
        Incremental = incremental;
        FullRunReason = fullRunReason;
        RulesRerunInFull = rulesRerunInFull;
        RulesRerunPartly = rulesRerunPartly;
        RulesCarried = rulesCarried;
        NodesReexamined = nodesReexamined;
    }

    /// <summary>False when the whole check ran again (see <see cref="FullRunReason"/>); true when results were carried over.</summary>
    public bool Incremental { get; }

    /// <summary>Why every rule ran in full, or <see langword="null"/> for an incremental re-check.</summary>
    public string? FullRunReason { get; }

    /// <summary>Rules that ran over the whole document (no declared scope, or too much of the page was affected).</summary>
    public int RulesRerunInFull { get; }

    /// <summary>Rules that ran only over the nodes the edit reached.</summary>
    public int RulesRerunPartly { get; }

    /// <summary>Rules whose results were all taken over unchanged (remapped to the new snapshot).</summary>
    public int RulesCarried { get; }

    /// <summary>The number of nodes the edit, the restyle and the tree changes reached before each rule's scope widened them.</summary>
    public int NodesReexamined { get; }
}
