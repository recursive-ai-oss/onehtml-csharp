using System;
using System.Collections.Generic;
using System.Linq;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Accessibility.Validation;

/// <summary>The results of one <see cref="Checker"/> run, in the fixed order described on <see cref="Checker"/>.</summary>
public sealed class CheckReport
{
    internal CheckReport(AccessibilityTree tree, Catalogue.ValidationProfile? profile, IReadOnlyList<IAccessibilityRule> rulesRun, IReadOnlyList<RuleResult> results, bool limitReached)
    {
        Tree = tree;
        Profile = profile;
        RulesRun = rulesRun;
        Results = results;
        LimitReached = limitReached;
    }

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
