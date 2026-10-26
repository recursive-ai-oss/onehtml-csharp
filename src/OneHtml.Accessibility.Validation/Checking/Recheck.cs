using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RecursiveAI.OneHtml.Accessibility.Aria;
using RecursiveAI.OneHtml.Accessibility.Rendered;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Accessibility.Validation;

public sealed partial class Checker
{
    // A rule whose affected share of the page is above this runs over the whole document: the bookkeeping would cost more than it saves.
    private const double PartialRunLimit = 0.4;

    /// <summary>
    /// Checks the document an edit produced, equal to <see cref="Check(AccessibilityTree, CheckOptions?, CancellationToken)"/> on
    /// <paramref name="updatedTree"/> with the options of <paramref name="previous"/> and <paramref name="facts"/>, but re-running
    /// each rule only where the edit reached. Rules that declare a <see cref="IAccessibilityRule.Scope"/> run over the nodes whose
    /// inputs changed (the edit's change set, the restyled elements and the nodes whose tree node differs, widened by the rule's
    /// scope); the previous results of every other target are carried over, remapped to the new snapshot, and the results of
    /// removed nodes are dropped. A rule without a scope, and a rule whose scope reaches most of the page, runs in full.
    /// </summary>
    /// <remarks>
    /// The whole check runs again, and <see cref="CheckReport.Recheck"/> says why, when rendered facts take part (the previous
    /// report's, because an edit makes them stale, or <paramref name="facts"/>), when the previous report dropped results at
    /// <see cref="CheckOptions.MaxResultsPerRule"/>, when the restyle was a full recompute, or when the style context differs.
    /// The report's suggested edits target the new snapshot.
    /// </remarks>
    /// <param name="previous">The report of the document before the edit, made by this checker.</param>
    /// <param name="edit">A successful result of <c>Document.Apply</c> on the previous report's document.</param>
    /// <param name="restyle">The result of restyling the previous tree's styles with the same edit.</param>
    /// <param name="updatedTree">The tree of the edited document, from <c>previous.Tree.Update(edit, restyle)</c> or built afresh.</param>
    /// <param name="facts">Facts captured from the edited document, or <see langword="null"/> (the default) for none. The previous report's facts are never reused.</param>
    /// <param name="cancellationToken">Observed between rules and inside long loops.</param>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="facts"/> is null.</exception>
    /// <exception cref="ArgumentException">The edit failed or was not applied to the previous report's document, the tree is not of the edited document, a rule of <paramref name="previous"/> is not one of this checker's, or the facts are not of the edited document.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public CheckReport Recheck(CheckReport previous, EditResult edit, RestyleResult restyle, AccessibilityTree updatedTree, RenderedFacts? facts = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(edit);
        if (!edit.Succeeded || edit.Changes is null || edit.Document is null)
        {
            throw new ArgumentException("The edit did not succeed, so there is no new document.", nameof(edit));
        }

        return RecheckCore(previous, edit.Changes, edit.Document, restyle, updatedTree, facts, cancellationToken);
    }

    /// <summary>
    /// <see cref="Recheck(CheckReport, EditResult, RestyleResult, AccessibilityTree, RenderedFacts?, CancellationToken)"/> after a batch
    /// of document and stylesheet edits. A batch that changed only stylesheets keeps the document; the restyled elements are what
    /// the re-check reaches.
    /// </summary>
    /// <param name="previous">The report of the document before the batch, made by this checker.</param>
    /// <param name="batch">A successful result of <c>EditBatch.Apply</c> on the previous report's document.</param>
    /// <param name="restyle">The result of restyling the previous tree's styles with the same batch.</param>
    /// <param name="updatedTree">The tree after the batch.</param>
    /// <param name="facts">Facts captured after the batch, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">Observed between rules and inside long loops.</param>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="facts"/> is null.</exception>
    /// <exception cref="ArgumentException">As for the overload taking an <see cref="EditResult"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public CheckReport Recheck(CheckReport previous, BatchResult batch, RestyleResult restyle, AccessibilityTree updatedTree, RenderedFacts? facts = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(batch);
        if (!batch.Succeeded)
        {
            throw new ArgumentException("The batch did not succeed, so there is no new document.", nameof(batch));
        }

        return RecheckCore(previous, batch.Changes, batch.Document ?? previous.Document, restyle, updatedTree, facts, cancellationToken);
    }

    private CheckReport RecheckCore(CheckReport previous, ChangeSet? changes, Document newDoc, RestyleResult restyle, AccessibilityTree updatedTree, RenderedFacts? facts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(restyle);
        ArgumentNullException.ThrowIfNull(updatedTree);
        if (!ReferenceEquals(updatedTree.Document, newDoc))
        {
            throw new ArgumentException("The tree is not of the edited document.", nameof(updatedTree));
        }

        if (changes is null ? !ReferenceEquals(newDoc, previous.Document) : !ReferenceEquals(changes.OldDocument, previous.Document))
        {
            throw new ArgumentException("The edit was not applied to the previous report's document.", nameof(previous));
        }

        var known = new HashSet<IAccessibilityRule>(_rules, ReferenceEqualityComparer.Instance);
        foreach (IAccessibilityRule r in previous.RulesRun)
        {
            if (!known.Contains(r))
            {
                throw new ArgumentException("The previous report was made by a checker with other rules.", nameof(previous));
            }
        }

        CheckOptions o = previous.Options;
        CheckOptions options = o.WithFacts(facts);

        string? why =
            o.Facts is not null ? "the previous report used rendered facts, which an edit makes stale"
            : facts is not null ? "rendered facts were supplied for the edited document"
            : previous.LimitReached ? "the previous report dropped results at the per-rule limit"
            : restyle.FullRecompute ? "the restyle was a full recompute"
            : !updatedTree.Context.Equals(previous.Tree.Context) ? "the style context changed"
            : null;
        if (why is not null)
        {
            CheckReport full = Check(updatedTree, options, ct);
            return new CheckReport(full.Tree, full.Profile, full.RulesRun, full.Results, full.LimitReached, full.Options, full.Slices, new RecheckInfo(false, why, full.RulesRun.Count, 0, 0, newDoc.NodeCount));
        }

        return new Rechecker(this, previous, changes, newDoc, restyle, updatedTree, options, ct).Run();
    }

    /// <summary>One incremental re-check: the affected nodes, each rule's widened view of them, and the merge.</summary>
    private sealed class Rechecker
    {
        private static readonly string[] s_referenceAttributes =
        [
            "aria-labelledby", "aria-describedby", "aria-owns", "aria-controls", "aria-flowto", "aria-details", "aria-errormessage",
            "aria-activedescendant", "for", "headers", "form", "list", "href",
        ];

        private readonly Checker _checker;
        private readonly CheckReport _previous;
        private readonly ChangeSet? _changes;
        private readonly Document _oldDoc;
        private readonly Document _doc;
        private readonly RestyleResult _restyle;
        private readonly AccessibilityTree _tree;
        private readonly CheckOptions _options;
        private readonly CancellationToken _ct;
        private readonly int _n;
        private readonly bool[] _dirty;
        private readonly Dictionary<int, Scoped> _scoped = [];
        private bool[]? _inIteration;
        private int _elementCount;
        private HashSet<string>? _touchedNames;
        private bool _anyChange;
        private bool[]? _refDirty;
        private readonly List<int> _removedParents = [];
        private readonly Dictionary<int, (TaskImpact Impact, string? Reason, SourceRange Range)> _targetFacts = [];

        public Rechecker(Checker checker, CheckReport previous, ChangeSet? changes, Document doc, RestyleResult restyle, AccessibilityTree tree, CheckOptions options, CancellationToken ct)
        {
            _checker = checker;
            _previous = previous;
            _changes = changes;
            _oldDoc = previous.Document;
            _doc = doc;
            _restyle = restyle;
            _tree = tree;
            _options = options;
            _ct = ct;
            _n = doc.NodeCount;
            _dirty = new bool[_n];
        }

        private sealed record Scoped(bool[] Mask, int[] Elements, int[] All, bool AnyNonElement);

        public CheckReport Run()
        {
            MarkDirty();
            var context = new CheckContext(_tree, null);
            var all = new List<RuleResult>();
            var slices = new List<RuleSlice>();
            bool limit = false;
            int full = 0, partly = 0, carried = 0;
            int max = Math.Max(1, _options.MaxResultsPerRule);
            int dirtyCount = 0;
            foreach (bool b in _dirty)
            {
                if (b)
                {
                    dirtyCount++;
                }
            }

            var rules = _previous.RulesRun;
            for (int ri = 0; ri < rules.Count; ri++)
            {
                _ct.ThrowIfCancellationRequested();
                IAccessibilityRule rule = rules[ri];
                RuleSlice old = _previous.Slices[ri];
                RuleScope scope = rule.Scope;
                bool runFull;
                Scoped? s = null;
                if (scope == RuleScope.Unspecified)
                {
                    runFull = true;
                }
                else if (scope.HasFlag(RuleScope.Page))
                {
                    runFull = PageTouched(rule);
                    if (!runFull)
                    {
                        // Nothing the rule reads changed: its results carry over as they are.
                        if (TryCarry(old, context, null, [], all, slices, max, out _))
                        {
                            carried++;
                            continue;
                        }

                        runFull = true;
                    }
                }
                else
                {
                    s = ScopeFor(scope);
                    runFull = s.Elements.Length > PartialRunLimit * _elementCount;
                }

                if (runFull)
                {
                    full++;
                    limit |= RunRule(rule, context, _options, max, all, slices, _ct);
                    continue;
                }

                if (s is null)
                {
                    continue;
                }

                if (s.Elements.Length == 0 && !s.AnyNonElement)
                {
                    if (TryCarry(old, context, null, [], all, slices, max, out _))
                    {
                        carried++;
                        continue;
                    }

                    full++;
                    limit |= RunRule(rule, context, _options, max, all, slices, _ct);
                    continue;
                }

                // Partial run: the rule looks only at the affected elements and may report only on affected nodes.
                context.CandidateElements = s.Elements;
                context.CandidateNodes = s.All;
                context.CandidateMask = s.Mask;
                List<RuleResult> raw;
                bool partialLimit;
                try
                {
                    raw = Evaluate(rule, context, int.MaxValue, _ct, out partialLimit);
                }
                finally
                {
                    context.CandidateElements = null;
                    context.CandidateNodes = null;
                    context.CandidateMask = null;
                }

                if (!TryCarry(old, context, s.Mask, raw, all, slices, max, out _) || partialLimit)
                {
                    full++;
                    limit |= RunRule(rule, context, _options, max, all, slices, _ct);
                    continue;
                }

                partly++;
            }

            var info = new RecheckInfo(true, null, full, partly, carried, dirtyCount);
            return new CheckReport(_tree, _options.Profile, rules, all, limit, _options, slices, info);
        }

        private static List<RuleResult> Evaluate(IAccessibilityRule rule, CheckContext context, int maxResults, CancellationToken ct, out bool limit)
        {
            var results = new List<RuleResult>();
            var reporter = new RuleReporter(rule, context, results, maxResults, ct);
            rule.Evaluate(context, reporter);
            limit = reporter.LimitReached;
            return results;
        }

        /// <summary>
        /// Appends the rule's report entries: the previous ones that survive (target kept and outside <paramref name="mask"/>),
        /// remapped to the new snapshot, merged with the freshly evaluated <paramref name="fresh"/> results. False when a previous
        /// result cannot be remapped or the merged rule would exceed the per-rule limit; the caller then runs the rule in full.
        /// </summary>
        private bool TryCarry(RuleSlice old, CheckContext context, bool[]? mask, List<RuleResult> fresh, List<RuleResult> all, List<RuleSlice> slices, int max, out List<RuleResult>? merged)
        {
            merged = null;
            var kept = new List<RuleResult>();
            var keptDropped = new List<int>();
            if (!old.Inapplicable)
            {
                for (int i = old.Start; i < old.Start + old.Count; i++)
                {
                    RuleResult r = _previous.Results[i];
                    if (r.Target.IsNull)
                    {
                        return false;
                    }

                    Node t = MapNode(r.Target);

                    if (t.IsNull || (mask is not null && mask[t.Index]))
                    {
                        continue;
                    }

                    RuleResult? rebased = Rebase(r, t, context);
                    if (rebased is null)
                    {
                        return false;
                    }

                    kept.Add(rebased);
                }

                foreach (int oldIndex in old.DroppedPasses)
                {
                    Node t = MapNode(_oldDoc.GetNode(oldIndex));
                    if (!t.IsNull && (mask is null || !mask[t.Index]))
                    {
                        keptDropped.Add(t.Index);
                    }
                }
            }

            var freshVisible = new List<RuleResult>();
            foreach (RuleResult r in SortResults(fresh))
            {
                if (r.Outcome != Outcome.Passed || _options.IncludePasses)
                {
                    freshVisible.Add(r);
                }
                else
                {
                    keptDropped.Add(r.Target.Index);
                }
            }

            int raw = kept.Count + freshVisible.Count + keptDropped.Count;
            if (raw > max)
            {
                return false;
            }

            int start = all.Count;
            if (raw == 0)
            {
                all.Add(Inapplicable(old.Rule, _tree));
                slices.Add(new RuleSlice(old.Rule, start, 1, true, []));
                return true;
            }

            kept.AddRange(freshVisible);
            merged = freshVisible.Count == 0 && IsInReportOrder(kept) ? kept : SortResults(kept);
            all.AddRange(merged);
            slices.Add(new RuleSlice(old.Rule, start, merged.Count, false, [.. keptDropped]));
            return true;
        }

        // Carried results keep their relative order; the report order is by target index, which edits may renumber.
        private static bool IsInReportOrder(List<RuleResult> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                RuleResult a = list[i - 1], b = list[i];
                int c = a.Target.Index.CompareTo(b.Target.Index);
                if (c > 0 || (c == 0 && (int)a.Outcome > (int)b.Outcome))
                {
                    return false;
                }
            }

            return true;
        }

        private Node MapNode(Node old) => _changes is null ? old : _changes.MapToNew(old);

        private RuleResult? Rebase(RuleResult r, Node target, CheckContext context)
        {
            IReadOnlyList<SuggestedEdit> suggested = r.SuggestedEdits;
            if (suggested.Count > 0)
            {
                var list = new List<SuggestedEdit>(suggested.Count);
                foreach (SuggestedEdit s in suggested)
                {
                    Node et = MapNode(s.Edit.Target);
                    if (et.IsNull)
                    {
                        return null;
                    }

                    list.Add(new SuggestedEdit(s.Edit.WithTarget(et), s.Description));
                }

                suggested = list;
            }

            // Several results often share a target; its task impact and byte range are worked out once.
            if (!_targetFacts.TryGetValue(target.Index, out (TaskImpact Impact, string? Reason, SourceRange Range) f))
            {
                (TaskImpact impact, string? reason) = TaskImpactAnalyzer.Analyze(context, target);
                SourceRange range = SourceRange.None;
                if (target.Source.Range is { IsPresent: true } sr)
                {
                    range = _doc.ToByteRange(sr);
                }

                _targetFacts[target.Index] = f = (impact, reason, range);
            }

            (TaskImpact Impact, string? Reason, SourceRange Range) = f;
            return new RuleResult(r.Rule, r.Outcome, target, r.Message, r.Findings, suggested, r.FactsMissing, r.FactsUsed, Impact, Reason,
                _tree.Context.HasInteractionState, context.LocatorOf, Range);
        }

        // ---- what the edit reached ----

        private void MarkDirty()
        {
            if (_changes is not null)
            {
                foreach (Node added in _changes.Added)
                {
                    MarkSubtree(added);
                    _anyChange = true;
                }

                foreach (NodeChange c in _changes.Changed)
                {
                    _dirty[c.Node.Index] = true;
                    _anyChange = true;
                }

                foreach (Node removed in _changes.Removed)
                {
                    _anyChange = true;
                    Node parent = removed.Parent;
                    if (!parent.IsNull && MapNode(parent) is { IsNull: false } p)
                    {
                        _dirty[p.Index] = true;
                        _removedParents.Add(p.Index);
                    }
                }
            }

            foreach (int i in _restyle.RestyledElements)
            {
                _dirty[i] = true;
            }

            if (_restyle.PseudoElementsMayDiffer is { } pseudo)
            {
                foreach (int i in pseudo)
                {
                    _dirty[i] = true;
                }
            }

            if (_restyle.RestyledElements.Count > 0)
            {
                _anyChange = true;
            }

            MarkTreeChanges();
        }

        private void MarkSubtree(Node root)
        {
            var stack = new Stack<Node>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Node n = stack.Pop();
                _dirty[n.Index] = true;
                for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
                {
                    stack.Push(c);
                }

                if (n.IsElement && n.TemplateContent is { IsNull: false } content)
                {
                    stack.Push(content);
                }
            }
        }

        /// <summary>
        /// Marks the DOM nodes whose tree node was added, removed or differs in anything a rule can read. Tree nodes are paired
        /// through the change set (an old DOM node and the new node it became); the comparison covers everything a rule can ask
        /// of an <see cref="AxNode"/> except the position in focus order, which shifts for every later node when one is inserted.
        /// </summary>
        private void MarkTreeChanges()
        {
            AccessibilityTree before = _previous.Tree;
            // For each new node, the old node it came from (the first, when several text nodes merged).
            var newToOld = new int[_n];
            Array.Fill(newToOld, -1);
            for (int i = _oldDoc.NodeCount - 1; i >= 0; i--)
            {
                Node m = MapNode(_oldDoc.GetNode(i));
                if (!m.IsNull)
                {
                    newToOld[m.Index] = i;
                }
            }

            // Both trees are read through their DOM nodes: a node can have a tree node in one and none in the other.
            for (int i = 0; i < _n; i++)
            {
                Node now = _doc.GetNode(i);
                AxNode? a = _tree.Find(now);
                int old = newToOld[i];
                AxNode? b = old < 0 ? null : before.Find(_oldDoc.GetNode(old));
                if ((a is not null || b is not null) && (a is null || b is null || !SameForRules(b, a, newToOld)))
                {
                    Mark(now);
                }
                else if (now.IsElement && old >= 0 && !SameRole(before.Roles[_oldDoc.GetNode(old)], _tree.Roles[now]))
                {
                    // The role layer is read on its own by rules (an element with no tree node still has one), and a role can follow from an ancestor.
                    Mark(now);
                }
            }
        }

        private static bool SameRole(NodeRole x, NodeRole y)
        {
            if (x.IsComputed != y.IsComputed || x.Role?.Name != y.Role?.Name || x.ExplicitRole?.Name != y.ExplicitRole?.Name || x.ImplicitRole?.Name != y.ImplicitRole?.Name
                || x.RoleWhenNamed?.Name != y.RoleWhenNamed?.Name || x.Origin != y.Origin || x.IsFocusable != y.IsFocusable || x.IsDisabled != y.IsDisabled || x.IsInert != y.IsInert)
            {
                return false;
            }

            ReadOnlySpan<RoleDiagnostic> dx = x.Diagnostics, dy = y.Diagnostics;
            if (dx.Length != dy.Length)
            {
                return false;
            }

            for (int i = 0; i < dx.Length; i++)
            {
                if (dx[i] != dy[i])
                {
                    return false;
                }
            }

            return true;
        }

        private void Mark(Node n)
        {
            if (!n.IsNull)
            {
                _dirty[n.Index] = true;
                _anyChange = true;
            }
        }

        private bool SameForRules(AxNode b, AxNode a, int[] newToOld)
        {
            if (b.Kind != a.Kind || b.Role != a.Role || b.InternalRole != a.InternalRole || b.RoleOrigin != a.RoleOrigin
                || !string.Equals(b.AriaRole?.Name, a.AriaRole?.Name, StringComparison.Ordinal) || b.IsIgnored != a.IsIgnored)
            {
                return false;
            }

            if (!SameText(b.Name, a.Name, newToOld) || !SameText(b.Description, a.Description, newToOld) || b.Hidden.Reasons != a.Hidden.Reasons || b.Hidden.IsInherited != a.Hidden.IsInherited
                || !SameNode(b.Hidden.Origin, a.Hidden.Origin, newToOld))
            {
                return false;
            }

            if (b.Focus.IsFocusable != a.Focus.IsFocusable || b.Focus.TabIndex != a.Focus.TabIndex || b.Focus.IsTabbable != a.Focus.IsTabbable)
            {
                return false;
            }

            if ((b.Parent is null) != (a.Parent is null) || (b.Parent is not null && !SameNode(b.Parent.DomNode, a.Parent!.DomNode, newToOld)))
            {
                return false;
            }

            if (!b.IgnoredReasons.SequenceEqual(a.IgnoredReasons, StringComparer.Ordinal) || !SameStates(b.States, a.States) || !SameRelations(b, a, newToOld))
            {
                return false;
            }

            if (b.Diagnostics.Count != a.Diagnostics.Count)
            {
                return false;
            }

            for (int i = 0; i < b.Diagnostics.Count; i++)
            {
                AxDiagnostic x = b.Diagnostics[i], y = a.Diagnostics[i];
                if (x.Kind != y.Kind || x.Attribute != y.Attribute || x.Value != y.Value || x.Message != y.Message || x.RoleKind != y.RoleKind || !SameNode(x.Node, y.Node, newToOld))
                {
                    return false;
                }
            }

            return true;
        }

        // An old node and a new one are the same node when the change set maps one to the other.
        private bool SameNode(Node old, Node now, int[] newToOld)
        {
            if (old.IsNull || now.IsNull)
            {
                return old.IsNull && now.IsNull;
            }

            return newToOld[now.Index] == old.Index || (_changes is null && old.Index == now.Index);
        }

        private bool SameNodes(IReadOnlyList<Node> old, IReadOnlyList<Node> now, int[] newToOld)
        {
            if (old.Count != now.Count)
            {
                return false;
            }

            for (int i = 0; i < old.Count; i++)
            {
                if (!SameNode(old[i], now[i], newToOld))
                {
                    return false;
                }
            }

            return true;
        }

        private bool SameText(AxText b, AxText a, int[] newToOld) =>
            b.Value == a.Value && b.Source == a.Source && b.Attribute == a.Attribute && SameNodes(b.RelatedElements, a.RelatedElements, newToOld);

        private bool SameRelations(AxNode b, AxNode a, int[] newToOld)
        {
            if (b.Relations.Count != a.Relations.Count)
            {
                return false;
            }

            for (int i = 0; i < b.Relations.Count; i++)
            {
                AxRelation x = b.Relations[i], y = a.Relations[i];
                if (x.Kind != y.Kind || x.Attribute != y.Attribute || !SameNodes(x.Elements, y.Elements, newToOld) || !x.BrokenIds.SequenceEqual(y.BrokenIds, StringComparer.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameStates(AxStates x, AxStates y) =>
            x.Expanded == y.Expanded && x.Checked == y.Checked && x.Selected == y.Selected && x.Pressed == y.Pressed && x.Disabled == y.Disabled && x.Required == y.Required
            && x.Invalid == y.Invalid && x.ReadOnly == y.ReadOnly && x.Level == y.Level && x.PosInSet == y.PosInSet && x.SetSize == y.SetSize && x.ValueNow == y.ValueNow
            && x.ValueMin == y.ValueMin && x.ValueMax == y.ValueMax && x.ValueText == y.ValueText && x.Value == y.Value && x.Live == y.Live && x.Atomic == y.Atomic
            && x.Relevant == y.Relevant && x.Busy == y.Busy && x.Modal == y.Modal && x.Current == y.Current && x.HasPopup == y.HasPopup && x.Orientation == y.Orientation
            && x.MultiSelectable == y.MultiSelectable && x.Multiline == y.Multiline && x.AutoComplete == y.AutoComplete && x.KeyShortcuts == y.KeyShortcuts
            && x.RoleDescription == y.RoleDescription && x.Url == y.Url && x.Editable == y.Editable;

        // ---- widening by scope ----

        private Scoped ScopeFor(RuleScope scope)
        {
            int key = (int)scope & (int)(RuleScope.Node | RuleScope.Descendants | RuleScope.Ancestors | RuleScope.IdReferences | RuleScope.Siblings);
            if (_scoped.TryGetValue(key, out Scoped? cached))
            {
                return cached;
            }

            EnsureIteration();
            bool[] seed = _dirty;
            if (scope.HasFlag(RuleScope.IdReferences))
            {
                seed = Union(seed, RefDirty());
            }

            bool[] mask = seed;
            if (scope.HasFlag(RuleScope.Siblings))
            {
                mask = Union(mask, Siblings(seed));
            }

            if (scope.HasFlag(RuleScope.Descendants))
            {
                mask = Union(mask, Ancestors(seed));
            }

            if (scope.HasFlag(RuleScope.Ancestors))
            {
                mask = Union(mask, Descendants(seed));
            }

            var elements = new List<int>();
            var every = new List<int>();
            bool nonElement = false;
            for (int i = 0; i < _n; i++)
            {
                if (!mask[i])
                {
                    continue;
                }

                every.Add(i);

                if (_inIteration![i])
                {
                    elements.Add(i);
                }
                else
                {
                    nonElement = true;
                }
            }

            var s = new Scoped(mask, [.. elements], [.. every], nonElement);
            _scoped[key] = s;
            return s;
        }

        private void EnsureIteration()
        {
            if (_inIteration is not null)
            {
                return;
            }

            _inIteration = new bool[_n];
            foreach (Node e in _doc.Root.DescendantElements)
            {
                _inIteration[e.Index] = true;
                _elementCount++;
            }

            if (_elementCount == 0)
            {
                _elementCount = 1;
            }
        }

        private static bool[] Union(bool[] a, bool[] b)
        {
            var r = new bool[a.Length];
            for (int i = 0; i < r.Length; i++)
            {
                r[i] = a[i] || b[i];
            }

            return r;
        }

        /// <summary>Marks the siblings of every seed node, and every child of a parent that lost a child.</summary>
        private bool[] Siblings(bool[] seed)
        {
            var marked = new bool[_n];
            var parents = new bool[_n];

            void All(Node parent)
            {
                if (parent.IsNull || parents[parent.Index])
                {
                    return;
                }

                parents[parent.Index] = true;
                for (Node c = parent.FirstChild; !c.IsNull; c = c.NextSibling)
                {
                    marked[c.Index] = true;
                }
            }

            for (int i = 0; i < _n; i++)
            {
                if (seed[i])
                {
                    All(_doc.GetNode(i).Parent);
                }
            }

            foreach (int p in _removedParents)
            {
                All(_doc.GetNode(p));
            }

            return marked;
        }

        /// <summary>Marks the DOM ancestors of every seed node.</summary>
        private bool[] Ancestors(bool[] seed)
        {
            var up = new bool[_n];
            for (int i = 0; i < _n; i++)
            {
                if (!seed[i])
                {
                    continue;
                }

                for (Node p = _doc.GetNode(i).Parent; !p.IsNull && !up[p.Index]; p = p.Parent)
                {
                    up[p.Index] = true;
                }
            }

            return up;
        }

        /// <summary>Marks every DOM descendant of every seed node.</summary>
        private bool[] Descendants(bool[] seed)
        {
            var down = new bool[_n];
            var stack = new Stack<Node>();
            for (int i = 0; i < _n; i++)
            {
                if (!seed[i] || down[i])
                {
                    continue;
                }

                stack.Push(_doc.GetNode(i));
                while (stack.Count > 0)
                {
                    Node n = stack.Pop();
                    for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
                    {
                        if (!down[c.Index])
                        {
                            down[c.Index] = true;
                            stack.Push(c);
                        }
                    }

                    if (n.IsElement && n.TemplateContent is { IsNull: false } content && !down[content.Index])
                    {
                        down[content.Index] = true;
                        stack.Push(content);
                    }
                }
            }

            return down;
        }

        private sealed class ReferenceIndex
        {
            public readonly Dictionary<string, List<int>> ById = new(StringComparer.Ordinal);
            public readonly Dictionary<string, List<int>> Referrers = new(StringComparer.Ordinal);
        }

        private static void ReferenceTokens(Node element, List<string> into)
        {
            foreach (string name in s_referenceAttributes)
            {
                if (element.GetAttribute(name) is not { Length: > 0 } v)
                {
                    continue;
                }

                if (name == "href")
                {
                    if (v[0] == '#' && v.Length > 1)
                    {
                        into.Add(v[1..]);
                        if (v.Contains('%', StringComparison.Ordinal))
                        {
                            into.Add(Uri.UnescapeDataString(v[1..]));
                        }
                    }

                    continue;
                }

                if (name is "for" or "form" or "list")
                {
                    // One ID, whatever characters it holds; the tokens below cover a reader that splits it anyway.
                    into.Add(v);
                }

                int i = 0;
                while (i < v.Length)
                {
                    while (i < v.Length && v[i] is ' ' or '\t' or '\n' or '\f' or '\r')
                    {
                        i++;
                    }

                    int start = i;
                    while (i < v.Length && !(v[i] is ' ' or '\t' or '\n' or '\f' or '\r'))
                    {
                        i++;
                    }

                    if (i > start)
                    {
                        into.Add(v[start..i]);
                    }
                }
            }
        }

        private static void Add(Dictionary<string, List<int>> map, string key, int index)
        {
            if (!map.TryGetValue(key, out List<int>? l))
            {
                map[key] = l = [];
            }

            l.Add(index);
        }

        /// <summary>Elements tied to a changed node by an ID reference, in either direction, whether the reference exists now or existed before.</summary>
        private bool[] RefDirty()
        {
            if (_refDirty is not null)
            {
                return _refDirty;
            }

            var index = new ReferenceIndex();
            var tokens = new List<string>();
            foreach (Node e in _doc.Root.DescendantElements)
            {
                if (e.GetAttribute("id") is { Length: > 0 } id)
                {
                    Add(index.ById, id, e.Index);
                }

                tokens.Clear();
                ReferenceTokens(e, tokens);
                foreach (string t in tokens)
                {
                    Add(index.Referrers, t, e.Index);
                }
            }

            var result = new bool[_n];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var scratch = new List<string>();

            void Examine(Node element)
            {
                if (element.GetAttribute("id") is { Length: > 0 } id)
                {
                    ids.Add(id);
                }

                scratch.Clear();
                ReferenceTokens(element, scratch);
                foreach (string t in scratch)
                {
                    // The element names this ID: whatever carries the ID is tied to it.
                    if (index.ById.TryGetValue(t, out List<int>? targets))
                    {
                        foreach (int ti in targets)
                        {
                            result[ti] = true;
                        }
                    }
                }
            }

            for (int i = 0; i < _n; i++)
            {
                if (_dirty[i] && _doc.GetNode(i) is { IsElement: true } e)
                {
                    Examine(e);
                    LabelAssociation(e, result);
                }
            }

            if (_changes is not null)
            {
                foreach (NodeChange c in _changes.Changed)
                {
                    if (c.Old.IsElement)
                    {
                        Examine(c.Old);
                    }
                }

                foreach (Node removed in _changes.Removed)
                {
                    var stack = new Stack<Node>();
                    stack.Push(removed);
                    while (stack.Count > 0)
                    {
                        Node n = stack.Pop();
                        if (n.IsElement)
                        {
                            Examine(n);
                        }

                        for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
                        {
                            stack.Push(c);
                        }
                    }
                }
            }

            foreach (string id in ids)
            {
                if (index.ById.TryGetValue(id, out List<int>? withId))
                {
                    foreach (int i in withId)
                    {
                        result[i] = true;
                    }
                }

                if (index.Referrers.TryGetValue(id, out List<int>? refs))
                {
                    foreach (int i in refs)
                    {
                        result[i] = true;
                    }
                }
            }

            return _refDirty = result;
        }

        /// <summary>
        /// A label and the control it labels are tied to each other even without an ID: a control inside a label, and the
        /// labelable elements inside a label that has no <c>for</c>.
        /// </summary>
        private static void LabelAssociation(Node e, bool[] result)
        {
            for (Node p = e.ParentElement; !p.IsNull; p = p.ParentElement)
            {
                if (p.LocalName == "label" && p.Namespace == ElementNamespace.Html)
                {
                    result[p.Index] = true;
                    break;
                }
            }

            if (e.LocalName == "label" && e.Namespace == ElementNamespace.Html && !e.HasAttribute("for"))
            {
                foreach (Node d in e.DescendantElements)
                {
                    if (d.LocalName is "button" or "meter" or "output" or "progress" or "select" or "textarea" or "input")
                    {
                        result[d.Index] = true;
                    }
                }
            }
        }

        // ---- page rules ----

        private bool PageTouched(IAccessibilityRule rule)
        {
            if (!_anyChange)
            {
                return false;
            }

            if (rule.RelevantElements is not { } relevant)
            {
                return true;
            }

            _touchedNames ??= TouchedNames();
            foreach (string name in relevant)
            {
                if (_touchedNames.Contains(name))
                {
                    return true;
                }
            }

            return false;
        }

        private HashSet<string> TouchedNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            void Note(Node n)
            {
                Node e = n.IsElement ? n : n.Parent;
                if (!e.IsNull && e.IsElement)
                {
                    names.Add(e.LocalName);
                }
            }

            for (int i = 0; i < _n; i++)
            {
                if (_dirty[i])
                {
                    Note(_doc.GetNode(i));
                }
            }

            if (_changes is not null)
            {
                foreach (Node removed in _changes.Removed)
                {
                    var stack = new Stack<Node>();
                    stack.Push(removed);
                    Note(removed.Parent.IsNull ? removed : removed.Parent);
                    while (stack.Count > 0)
                    {
                        Node n = stack.Pop();
                        Note(n);
                        for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
                        {
                            stack.Push(c);
                        }
                    }
                }
            }

            return names;
        }
    }
}
