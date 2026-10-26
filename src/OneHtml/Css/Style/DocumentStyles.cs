using System;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Dom;
using RecursiveAI.OneHtml.Selectors;

namespace RecursiveAI.OneHtml.Css;

/// <summary>A generated box that CSS styles through a pseudo-element.</summary>
public enum PseudoElementKind : byte
{
    /// <summary><c>::before</c>.</summary>
    Before = 0,

    /// <summary><c>::after</c>.</summary>
    After = 1,

    /// <summary><c>::marker</c>.</summary>
    Marker = 2,
}

/// <summary>The outcome of <see cref="DocumentStyles.Restyle"/>.</summary>
/// <param name="Styles">The styles of the new document.</param>
/// <param name="RestyledElements">The indices (<see cref="Node.Index"/>) of the elements whose style object was replaced, ascending. Empty after a full recompute, which replaces everything.</param>
/// <param name="FullRecompute">True when the change could not be applied incrementally (the tree shape differs) and every style is computed afresh.</param>
/// <param name="PseudoElementsMayDiffer">The indices of the elements whose <c>::before</c>, <c>::after</c> or <c>::marker</c> style may differ from the old document's, ascending: a superset of <paramref name="RestyledElements"/> that adds the elements whose pseudo-element rules match differently (an attribute, class or id change reaching a selector before the pseudo-element, a shape change, an added, removed or edited rule). Without any pseudo-element rule it is just the changed elements; after a full recompute it is empty.</param>
public sealed record RestyleResult(DocumentStyles Styles, IReadOnlyList<int> RestyledElements, bool FullRecompute, IReadOnlyList<int>? PseudoElementsMayDiffer = null);

/// <summary>
/// The computed styles of a document under a <see cref="StyleContext"/>: the cascade (CSS Cascade 5) run over the
/// user-agent sheet, user sheets, <c>style</c> and <c>link</c> elements and <c>style=""</c> attributes. Styles are computed
/// per element on first request (parents first) and shared between elements that cascade to the same values.
/// Thread-safe for reading.
/// </summary>
/// <remarks>
/// <see cref="WithContext"/> and <see cref="Restyle"/> return a view that shares this object's styles and recomputes only
/// the elements the change can reach. See <c>docs/cascade.md</c>.
/// </remarks>
public sealed class DocumentStyles
{
    private readonly Document _doc;
    private readonly StyleContext _context;
    private readonly StyleSourceOptions _options;
    private readonly StyleEngine _engine;
    private readonly SelectorMatchOptions _match;
    private readonly ComputedStyle?[]? _styles;
    private readonly DocumentStyles? _fallback;
    private readonly Dictionary<int, ComputedStyle>? _overlay;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, ComputedStyle?> _pseudo = new();

    private DocumentStyles(Document doc, StyleContext context, StyleSourceOptions options, StyleEngine engine, DocumentStyles? fallback)
    {
        _doc = doc;
        _context = context;
        _options = options;
        _engine = engine;
        _fallback = fallback;
        _match = new SelectorMatchOptions
        {
            Scope = doc.DocumentElement,
            State = context.ToInteractionState(),
            BulkCache = engine.UsesBulkCaches ? new SelectorBulkCache() : null,
        };
        if (fallback is null)
        {
            _styles = new ComputedStyle?[doc.NodeCount];
        }
        else
        {
            _overlay = [];
        }
    }

    /// <summary>The document.</summary>
    public Document Document => _doc;

    internal StyleSourceOptions SourceOptions => _options;

    /// <summary>The context the styles were computed under.</summary>
    public StyleContext Context => _context;

    /// <summary>The sheets taken into the cascade, in cascade order: user agent, user, then author sheets in document order (imports follow their importer).</summary>
    public IReadOnlyList<StyleSheetInfo> Sheets => _engine.Db.Sheets;

    /// <summary>What could not be gathered: missing sheets, import cycles, skipped rules, dropped selectors (at most 256 are kept).</summary>
    public IReadOnlyList<StyleDiagnostic> Diagnostics => _engine.Db.Diagnostics;

    /// <summary>True when a rule that styles <c>::before</c>, <c>::after</c> or <c>::marker</c> reads an interaction state (<c>:hover</c>, <c>:focus</c>, …), so a change of <see cref="StyleContext"/> can change generated content that <see cref="ChangedElements"/> does not list.</summary>
    public bool HasInteractivePseudoElementRules => _engine.Db.PseudoElementDynamic;

    /// <summary>The number of style rules (nested rules and declaration runs counted separately) in the cascade.</summary>
    public int RuleCount => _engine.Db.Rules.Count;

    /// <summary>The number of distinct style objects built so far, across this view and the ones it shares an engine with.</summary>
    public int DistinctStyleCount => _engine.StyleCount;

    /// <summary>For a view made by <see cref="WithContext"/> or <see cref="Restyle"/>: the indices of the elements whose style differs from the view it was made from, ascending. Empty for a fresh view.</summary>
    public IReadOnlyList<int> ChangedElements
    {
        get
        {
            if (_overlay is null)
            {
                return [];
            }

            var l = new List<int>(_overlay.Keys);
            l.Sort();
            return l;
        }
    }

    /// <summary>Gathers the style sources of a document and prepares the cascade. No element style is computed yet.</summary>
    /// <param name="document">The document.</param>
    /// <param name="context">The media features and interaction state; <see cref="StyleContext.Default"/> when null. Node handles in it must belong to <paramref name="document"/>.</param>
    /// <param name="options">Where linked sheets come from and the bounds; defaults when null (no resolver, so linked sheets are reported missing).</param>
    /// <param name="cancellationToken">Cancels the gathering.</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentException">The context names a node of another document.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public static DocumentStyles Create(Document document, StyleContext? context = null, StyleSourceOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        context ??= StyleContext.Default;
        options ??= new StyleSourceOptions();
        CheckContext(document, context);
        RuleDatabase db = new RuleGatherer(document, context, options, cancellationToken).Build();
        return new DocumentStyles(document, context, options, new StyleEngine(document, context, db, options), null);
    }

    private static void CheckContext(Document document, StyleContext context)
    {
        foreach (Node n in new[] { context.Focus, context.Hover, context.Active, context.Target })
        {
            if (!n.IsNull && (!ReferenceEquals(n.Document, document) || !n.IsElement))
            {
                throw new ArgumentException("The style context names a node that is not an element of the document.", nameof(context));
            }
        }
    }

    /// <summary>The computed style of an element, computing it (and its ancestors') on first use.</summary>
    /// <param name="element">An element of <see cref="Document"/>.</param>
    /// <exception cref="ArgumentException">The node is not an element of this document.</exception>
    public ComputedStyle GetStyle(Node element)
    {
        if (element.IsNull || !ReferenceEquals(element.Document, _doc) || !element.IsElement)
        {
            throw new ArgumentException("The node is not an element of this document.", nameof(element));
        }

        return StyleOf(element.Index);
    }

    /// <summary>
    /// The computed style of a generated box of an element (CSS Pseudo-Elements 4): <c>::before</c>, <c>::after</c> or
    /// <c>::marker</c>. It inherits from the element's style. The result is <see langword="null"/> when no rule styles that
    /// pseudo-element, which also means no box is generated for <c>::before</c> and <c>::after</c>; when a style is
    /// returned the box exists only if its <see cref="ComputedStyle.Content"/> is neither <c>normal</c> nor <c>none</c>
    /// (<c>::marker</c> is generated by list items regardless). Thread-safe; computed on first use and cached.
    /// </summary>
    /// <param name="element">An element of <see cref="Document"/>.</param>
    /// <param name="kind">Which pseudo-element.</param>
    /// <exception cref="ArgumentException">The node is not an element of this document.</exception>
    public ComputedStyle? GetPseudoStyle(Node element, PseudoElementKind kind)
    {
        if (element.IsNull || !ReferenceEquals(element.Document, _doc) || !element.IsElement)
        {
            throw new ArgumentException("The node is not an element of this document.", nameof(element));
        }

        string name = kind switch { PseudoElementKind.Before => "before", PseudoElementKind.After => "after", PseudoElementKind.Marker => "marker", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        if (!_engine.Db.PseudoIndex.ContainsKey(name))
        {
            return null;
        }

        long key = ((long)element.Index << 2) | (long)kind;
        if (_pseudo.TryGetValue(key, out ComputedStyle? cached))
        {
            return cached;
        }

        ComputedStyle? built = _engine.ComputePseudo(element, name, StyleOf(element.Index), _match);
        return _pseudo.GetOrAdd(key, built);
    }

    /// <summary>Computes the style of every element now, in document order.</summary>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>The number of elements.</returns>
    public int ComputeAll(CancellationToken cancellationToken = default)
    {
        int n = 0;
        foreach (Node e in _doc.Root.DescendantElements)
        {
            if ((++n & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            StyleOf(e.Index);
        }

        return n;
    }

    internal ComputedStyle StyleOf(int index)
    {
        if (_overlay is not null)
        {
            return _overlay.TryGetValue(index, out ComputedStyle? s) ? s : _fallback!.StyleOf(index);
        }

        ComputedStyle? cur = Volatile.Read(ref _styles![index]);
        if (cur is not null)
        {
            return cur;
        }

        Arena a = _doc.Arena;
        var pending = new List<int>();
        int k = index;
        ComputedStyle? parent = null;
        while (true)
        {
            pending.Add(k);
            int p = a.Parent[k];
            if (p < 0 || a.Kind[p] != (byte)NodeKind.Element)
            {
                break;
            }

            ComputedStyle? ps = Volatile.Read(ref _styles[p]);
            if (ps is not null)
            {
                parent = ps;
                break;
            }

            k = p;
        }

        for (int i = pending.Count - 1; i >= 0; i--)
        {
            int idx = pending[i];
            ComputedStyle built = _engine.Compute(new Node(_doc, idx), parent, _match);
            ComputedStyle? prior = Interlocked.CompareExchange(ref _styles[idx], built, null);
            parent = prior ?? built;
        }

        return parent!;
    }

    // ---- interaction overlay ----

    /// <summary>
    /// The styles under another context. When only the interaction state differs, the result is an overlay: it recomputes
    /// the elements that a rule with a dynamic pseudo-class (<c>:hover</c>, <c>:focus</c>, <c>:focus-visible</c>,
    /// <c>:focus-within</c>, <c>:active</c>, <c>:target</c>, also inside <c>:has()</c>) matches differently, plus their
    /// descendants wherever inheritance changes a value, and shares every other style with this view. When the media features
    /// differ the rule set itself differs, so the sheets are gathered again.
    /// </summary>
    /// <param name="context">The new context; its nodes must belong to this document.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="ArgumentException">The context names a node of another document.</exception>
    public DocumentStyles WithContext(StyleContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        CheckContext(_doc, context);
        if (!context.Media.Equals(_context.Media))
        {
            return Create(_doc, context, _options, cancellationToken);
        }

        var view = new DocumentStyles(_doc, context, _options, _engine, this);
        var affected = new HashSet<int>();
        if (_engine.Db.DynamicIndex.Count > 0 && !SameInteraction(_context, context))
        {
            if (!TryFocusAffected(_context, context, view._match, affected, cancellationToken))
            {
                var cand = new List<RuleSel>();
                var oldM = new List<RuleSel>();
                var newM = new List<RuleSel>();
                int n = 0;
                foreach (Node e in _doc.Root.DescendantElements)
                {
                    if ((++n & 255) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    StyleEngine.MatchSet(e, _engine.Db.DynamicIndex, _match, cand, oldM);
                    StyleEngine.MatchSet(e, _engine.Db.DynamicIndex, view._match, cand, newM);
                    if (!SameMatches(oldM, newM))
                    {
                        affected.Add(e.Index);
                    }
                }
            }
        }

        view.RunWorklist(affected, false, cancellationToken);
        return view;
    }

    // When only the focused element differs and no rule that reads focus uses :focus inside :has(), a selector's match can change only
    // at an "anchor": an element, among the two focused ones and their ancestors (for :focus-within), whose compound with the focus
    // pseudo-class matches differently. From an anchor a match reaches only along the rest of that selector: its descendants, and its
    // later siblings with their descendants. So only the selectors whose anchor changed are tested, and only over that reach: the
    // cost of one overlay follows the part of the page the focus can reach, not the page. False when the case does not apply.
    private bool TryFocusAffected(StyleContext from, StyleContext to, SelectorMatchOptions toOptions, HashSet<int> affected, CancellationToken cancellationToken)
    {
        List<FocusAnchor>? anchors = _engine.Db.FocusAnchors;
        if (anchors is null || !from.Hover.Equals(to.Hover) || !from.Active.Equals(to.Active) || !from.Target.Equals(to.Target))
        {
            return false;
        }

        var changed = new List<FocusAnchor>();
        foreach (Node x in new[] { from.Focus, to.Focus })
        {
            for (Node p = x; !p.IsNull && p.IsElement; p = p.ParentElement)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Only :focus-within compounds can change at an ancestor of the focused element.
                changed.Clear();
                SelectorMatcher.ChangedFocusAnchors(p, anchors, _match, toOptions, p.Index != x.Index, changed);
                foreach (FocusAnchor a in changed)
                {
                    TestReach(p, a, toOptions, affected, cancellationToken);
                }
            }
        }

        return true;
    }

    private void TestReach(Node p, FocusAnchor a, SelectorMatchOptions toOptions, HashSet<int> affected, CancellationToken cancellationToken)
    {
        // The element itself first: the anchor compound may be the selector's subject.
        if (SelectorMatcher.MatchesComplex(p, a.Selector, _match) != SelectorMatcher.MatchesComplex(p, a.Selector, toOptions))
        {
            affected.Add(p.Index);
        }

        int n = 0;
        if (a.ReachesBelow)
        {
            foreach (Node d in p.DescendantElements)
            {
                TestOne(d, a, toOptions, affected, ref n, cancellationToken);
            }
        }

        if (a.ReachesBeside)
        {
            for (Node sib = p.NextSibling; !sib.IsNull; sib = sib.NextSibling)
            {
                if (!sib.IsElement)
                {
                    continue;
                }

                TestOne(sib, a, toOptions, affected, ref n, cancellationToken);
                foreach (Node d in sib.DescendantElements)
                {
                    TestOne(d, a, toOptions, affected, ref n, cancellationToken);
                }
            }
        }
    }

    private void TestOne(Node e, FocusAnchor a, SelectorMatchOptions toOptions, HashSet<int> affected, ref int n, CancellationToken cancellationToken)
    {
        if ((++n & 1023) == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (!affected.Contains(e.Index) && SelectorMatcher.MatchesComplex(e, a.Selector, _match) != SelectorMatcher.MatchesComplex(e, a.Selector, toOptions))
        {
            affected.Add(e.Index);
        }
    }

    private static bool SameInteraction(StyleContext a, StyleContext b) =>
        a.Focus.Equals(b.Focus) && a.Hover.Equals(b.Hover) && a.Active.Equals(b.Active) && a.Target.Equals(b.Target)
        && (a.Focus.IsNull || a.FocusSource == b.FocusSource);

    private static bool SameMatches(List<RuleSel> a, List<RuleSel> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!ReferenceEquals(a[i].Rule, b[i].Rule) || a[i].Sel != b[i].Sel)
            {
                return false;
            }
        }

        return true;
    }

    // ---- restyle after a change ----

    /// <summary>
    /// The styles of a changed document. Only elements the change can reach are recomputed: each changed element; elements
    /// that a selector reading a changed attribute, class or id matches differently (descendant, sibling, <c>:has()</c> and
    /// <c>:nth-*</c> reach included, found by matching the affected selectors in the old and new document); elements the
    /// added, removed or edited rules match (<see cref="StyleChange.SheetsChanged"/>); and descendants whose inherited
    /// values change. Everything else keeps its style object.
    /// </summary>
    /// <param name="newDocument">The document after the change. It must have the same tree shape as <see cref="Document"/>: the same node count, and the same parent and kind for every node index.</param>
    /// <param name="change">What changed.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public RestyleResult Restyle(Document newDocument, StyleChange change, CancellationToken cancellationToken = default) =>
        RestyleWith(newDocument, change, _options, cancellationToken);

    // The options of the new view: the same as this view's, except that an edit of a linked sheet swaps that sheet's text in
    // the resolver (see DocumentStylesEditing).
    internal RestyleResult RestyleWith(Document newDocument, StyleChange change, StyleSourceOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(newDocument);
        ArgumentNullException.ThrowIfNull(change);
        int[]? oldToNew = null;
        if (change.OldToNew is { } map)
        {
            if (map.Count != _doc.NodeCount)
            {
                throw new ArgumentException("OldToNew must have one entry per node of the old document.", nameof(change));
            }

            oldToNew = new int[map.Count];
            for (int i = 0; i < oldToNew.Length; i++)
            {
                oldToNew[i] = map[i] >= 0 && map[i] < newDocument.NodeCount ? map[i] : -1;
            }
        }

        StyleContext newContext = Rebind(_context, newDocument, oldToNew);
        bool sameShape = SameShape(_doc, newDocument);
        if (!sameShape && oldToNew is null)
        {
            return new RestyleResult(Create(newDocument, newContext, options, cancellationToken), [], true);
        }

        if (oldToNew is not null && IsIdentity(oldToNew, newDocument.NodeCount))
        {
            oldToNew = null;
        }

        StyleEngine engine = _engine;
        RuleDatabase? newDb = null;
        if (change.SheetsChanged)
        {
            newDb = new RuleGatherer(newDocument, newContext, options, cancellationToken).Build();
            if (newDb.Quirks != _engine.Db.Quirks || newDb.LayerSignature != _engine.Db.LayerSignature)
            {
                return new RestyleResult(new DocumentStyles(newDocument, newContext, options, new StyleEngine(newDocument, newContext, newDb, options), null), [], true);
            }

            engine = new StyleEngine(newDocument, newContext, newDb, options);
        }

        int[]? newToOld = null;
        DocumentStyles fallback = this;
        if (oldToNew is not null)
        {
            if (engine.UsesHas)
            {
                // :has() reaches from any descendant to any ancestor and the ancestor's earlier siblings; recompute.
                return new RestyleResult(Create(newDocument, newContext, options, cancellationToken), [], true);
            }

            newToOld = new int[newDocument.NodeCount];
            Array.Fill(newToOld, -1);
            for (int o = 0; o < oldToNew.Length; o++)
            {
                if (oldToNew[o] >= 0)
                {
                    newToOld[oldToNew[o]] = o;
                }
            }

            fallback = Remapped(newDocument, newContext, engine, newToOld);
        }

        var view = new DocumentStyles(newDocument, newContext, options, engine, fallback);
        var affected = new HashSet<int>();
        if (newToOld is not null)
        {
            AddShapeChanges(newDocument, oldToNew!, newToOld, engine.UsesStructure, affected);
        }

        // Pseudo-element rules are not part of an element's own style, so the reach below skips them. The elements they
        // can restyle are found the same way, in a second pass over the pseudo-element selectors only, and reported apart.
        bool anyPseudo = _engine.Db.PseudoIndex.Count > 0 || (engine.Db.PseudoIndex.Count > 0);
        HashSet<int>? pseudoAffected = anyPseudo ? new HashSet<int>(affected) : null;

        foreach (ElementChange ch in change.Elements)
        {
            if (ch.NodeIndex >= 0 && ch.NodeIndex < newDocument.NodeCount && newDocument.Arena.Kind[ch.NodeIndex] == (byte)NodeKind.Element)
            {
                affected.Add(ch.NodeIndex);
            }
        }

        if (pseudoAffected is not null)
        {
            foreach (ElementChange ch in change.Elements)
            {
                if (ch.NodeIndex >= 0 && ch.NodeIndex < newDocument.NodeCount && newDocument.Arena.Kind[ch.NodeIndex] == (byte)NodeKind.Element)
                {
                    pseudoAffected.Add(ch.NodeIndex);
                }
            }

            AddSelectorReach(view, change, newToOld, pseudoAffected, cancellationToken, pseudo: true);
            if (newDb is not null)
            {
                AddRuleReach(view, newDb, newToOld, pseudoAffected, cancellationToken, pseudo: true);
            }
        }

        AddSelectorReach(view, change, newToOld, affected, cancellationToken);
        if (newDb is not null)
        {
            AddRuleReach(view, newDb, newToOld, affected, cancellationToken);
        }

        view.RunWorklist(affected, !ReferenceEquals(engine, _engine), cancellationToken);
        var changed = new List<int>(view._overlay!.Keys);
        changed.Sort();
        List<int> pseudoList = [];
        if (pseudoAffected is not null)
        {
            pseudoAffected.UnionWith(changed);
            pseudoList.AddRange(pseudoAffected);
            pseudoList.Sort();
        }

        return new RestyleResult(view, changed, false, pseudoList);
    }

    private static bool IsIdentity(int[] map, int newCount)
    {
        if (map.Length != newCount)
        {
            return false;
        }

        for (int i = 0; i < map.Length; i++)
        {
            if (map[i] != i)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The old styles carried over to the new document's node indices; elements without an old counterpart compute when read.</summary>
    private DocumentStyles Remapped(Document newDocument, StyleContext context, StyleEngine engine, int[] newToOld)
    {
        var mid = new DocumentStyles(newDocument, context, _options, engine, null);
        for (int n = 0; n < newToOld.Length; n++)
        {
            if (newToOld[n] >= 0 && newDocument.Arena.Kind[n] == (byte)NodeKind.Element)
            {
                mid._styles![n] = StyleOf(newToOld[n]);
            }
        }

        return mid;
    }

    // Elements the tree edit itself touches: new, moved, or next to something that was added, removed or moved (CSS Selectors 4
    // sibling combinators and position pseudo-classes), and the parents whose child list changed (:empty).
    private void AddShapeChanges(Document newDoc, int[] oldToNew, int[] newToOld, bool structural, HashSet<int> affected)
    {
        Arena na = newDoc.Arena;
        Arena oa = _doc.Arena;
        var parentsTouched = new HashSet<int>();
        for (int n = 0; n < newToOld.Length; n++)
        {
            int o = newToOld[n];
            int np = na.Parent[n];
            bool isElement = na.Kind[n] == (byte)NodeKind.Element;
            bool touched = o < 0;
            if (!touched && isElement)
            {
                int op = oa.Parent[o];
                touched = op < 0 ? np >= 0 : np < 0 || oldToNew[op] != np || PrevElementMapped(oa, oldToNew, o) != PrevElement(na, n);
            }

            if (touched)
            {
                if (isElement)
                {
                    affected.Add(n);
                }

                if (np >= 0)
                {
                    parentsTouched.Add(np);
                }
            }
        }

        // Removed nodes touch the parent they left.
        for (int o = 0; o < oldToNew.Length; o++)
        {
            if (oldToNew[o] < 0 && oa.Parent[o] >= 0 && oldToNew[oa.Parent[o]] is var p and >= 0)
            {
                parentsTouched.Add(p);
            }
        }

        foreach (int p in parentsTouched)
        {
            if (na.Kind[p] == (byte)NodeKind.Element)
            {
                affected.Add(p);
            }

            if (structural)
            {
                for (int c = na.First[p]; c >= 0; c = na.Next[c])
                {
                    if (na.Kind[c] == (byte)NodeKind.Element)
                    {
                        affected.Add(c);
                    }
                }
            }
        }
    }

    private static int PrevElement(Arena a, int i)
    {
        for (int p = a.Prev[i]; p >= 0; p = a.Prev[p])
        {
            if (a.Kind[p] == (byte)NodeKind.Element)
            {
                return p;
            }
        }

        return -1;
    }

    private static int PrevElementMapped(Arena a, int[] oldToNew, int o)
    {
        int p = PrevElement(a, o);
        return p < 0 ? -1 : oldToNew[p];
    }

    private static StyleContext Rebind(StyleContext c, Document doc, int[]? oldToNew)
    {
        Node Map(Node n)
        {
            int i = n.IsNull ? -1 : oldToNew is null ? n.Index : n.Index < oldToNew.Length ? oldToNew[n.Index] : -1;
            return i < 0 || i >= doc.NodeCount ? default : doc.GetNode(i);
        }

        return c with { Focus = Map(c.Focus), Hover = Map(c.Hover), Active = Map(c.Active), Target = Map(c.Target) };
    }

    private static bool SameShape(Document a, Document b)
    {
        if (a.NodeCount != b.NodeCount)
        {
            return false;
        }

        Arena x = a.Arena;
        Arena y = b.Arena;
        int n = a.NodeCount;
        return x.Kind.AsSpan(0, n).SequenceEqual(y.Kind.AsSpan(0, n)) && x.Parent.AsSpan(0, n).SequenceEqual(y.Parent.AsSpan(0, n))
            && x.Name.AsSpan(0, n).SequenceEqual(y.Name.AsSpan(0, n)) && x.Ns.AsSpan(0, n).SequenceEqual(y.Ns.AsSpan(0, n))
            && x.Prev.AsSpan(0, n).SequenceEqual(y.Prev.AsSpan(0, n));
    }

    private void AddSelectorReach(DocumentStyles view, StyleChange change, int[]? newToOld, HashSet<int> affected, CancellationToken ct, bool pseudo = false)
    {
        if (change.Elements.Count == 0)
        {
            return;
        }

        var attrs = new HashSet<string>(StringComparer.Ordinal);
        var classes = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        bool anyAttr = false;
        foreach (ElementChange ch in change.Elements)
        {
            int oldIdx = OldIndex(newToOld, ch.NodeIndex);
            if (oldIdx < 0 || oldIdx >= _doc.NodeCount || ch.NodeIndex >= view._doc.NodeCount || _doc.Arena.Kind[oldIdx] != (byte)NodeKind.Element)
            {
                continue;
            }

            Node oldE = _doc.GetNode(oldIdx);
            Node newE = view._doc.GetNode(ch.NodeIndex);
            IEnumerable<string> names = ch.AttributeNames ?? AllAttributeNames(oldE, newE);
            foreach (string raw in names)
            {
                string name = raw.AsciiLower();
                anyAttr = true;
                if (name == "class")
                {
                    SymmetricDifference(oldE.GetAttribute("class"), newE.GetAttribute("class"), classes);
                }
                else if (name == "id")
                {
                    if (oldE.GetAttribute("id") is { Length: > 0 } a)
                    {
                        ids.Add(a.AsciiLower());
                    }

                    if (newE.GetAttribute("id") is { Length: > 0 } b)
                    {
                        ids.Add(b.AsciiLower());
                    }
                }
                else
                {
                    attrs.Add(name);
                }
            }
        }

        if (!anyAttr)
        {
            return;
        }

        List<StyleRule> rules = _engine.Db.RulesReading(attrs, classes, ids, attrs.Count > 0 || classes.Count > 0 || ids.Count > 0);
        if (rules.Count == 0)
        {
            return;
        }

        var index = new RuleIndex(_engine.Db.Quirks);
        foreach (StyleRule r in rules)
        {
            for (int i = 0; i < r.Selectors!.Count; i++)
            {
                if (r.Selectors.HasPseudoElement(i) == pseudo)
                {
                    index.Add(new RuleSel(r, i, r.Selectors.GetSpecificity(i)), r.Selectors.GetKey(i));
                }
            }
        }

        if (pseudo && index.Count == 0)
        {
            return;
        }

        // The old match context belongs to the old document; the new one to the new document.
        var cand = new List<RuleSel>();
        var oldM = new List<RuleSel>();
        var newM = new List<RuleSel>();
        int count = 0;
        foreach (Node e in view._doc.Root.DescendantElements)
        {
            if ((++count & 255) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            int oi = OldIndex(newToOld, e.Index);
            if (oi < 0)
            {
                continue;
            }

            Node oldNode = _doc.GetNode(oi);
            StyleEngine.MatchSet(oldNode, index, _match, cand, oldM, pseudo);
            StyleEngine.MatchSet(e, index, view._match, cand, newM, pseudo);
            if (!SameMatches(oldM, newM))
            {
                affected.Add(e.Index);
            }
        }
    }

    private static IEnumerable<string> AllAttributeNames(Node a, Node b)
    {
        foreach (Attr at in a.Attributes)
        {
            yield return at.QualifiedName;
        }

        foreach (Attr at in b.Attributes)
        {
            yield return at.QualifiedName;
        }
    }

    private static void SymmetricDifference(string? a, string? b, HashSet<string> into)
    {
        var x = new HashSet<string>(Tokens(a), StringComparer.Ordinal);
        var y = new HashSet<string>(Tokens(b), StringComparer.Ordinal);
        foreach (string t in x)
        {
            if (!y.Contains(t))
            {
                into.Add(t.AsciiLower());
            }
        }

        foreach (string t in y)
        {
            if (!x.Contains(t))
            {
                into.Add(t.AsciiLower());
            }
        }
    }

    private static IEnumerable<string> Tokens(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            yield break;
        }

        foreach (string t in s.Split([' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries))
        {
            yield return t;
        }
    }

    /// <summary>Elements matched by a rule that exists in only one of the old and new rule sets (the rules between the common prefix and suffix, which also covers moved rules).</summary>
    private static int OldIndex(int[]? newToOld, int newIndex) => newToOld is null ? newIndex : newIndex >= 0 && newIndex < newToOld.Length ? newToOld[newIndex] : -1;

    private void AddRuleReach(DocumentStyles view, RuleDatabase newDb, int[]? newToOld, HashSet<int> affected, CancellationToken ct, bool pseudo = false)
    {
        string[] a = _engine.Db.Signatures;
        string[] b = newDb.Signatures;
        int pre = 0;
        while (pre < a.Length && pre < b.Length && a[pre] == b[pre])
        {
            pre++;
        }

        int suf = 0;
        while (suf < a.Length - pre && suf < b.Length - pre && a[a.Length - 1 - suf] == b[b.Length - 1 - suf])
        {
            suf++;
        }

        var oldIndex = new RuleIndex(_engine.Db.Quirks);
        var newIndex = new RuleIndex(newDb.Quirks);
        AddRange(oldIndex, _engine.Db.Rules, pre, a.Length - suf, pseudo);
        AddRange(newIndex, newDb.Rules, pre, b.Length - suf, pseudo);
        if (oldIndex.Count == 0 && newIndex.Count == 0)
        {
            return;
        }

        var cand = new List<RuleSel>();
        var m = new List<RuleSel>();
        int count = 0;
        foreach (Node e in view._doc.Root.DescendantElements)
        {
            if ((++count & 255) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            int oi = OldIndex(newToOld, e.Index);
            if (oi < 0)
            {
                continue;
            }

            StyleEngine.MatchSet(_doc.GetNode(oi), oldIndex, _match, cand, m, pseudo);
            if (m.Count > 0)
            {
                affected.Add(e.Index);
                continue;
            }

            StyleEngine.MatchSet(e, newIndex, view._match, cand, m, pseudo);
            if (m.Count > 0)
            {
                affected.Add(e.Index);
            }
        }

        static void AddRange(RuleIndex index, List<StyleRule> rules, int from, int to, bool pseudo)
        {
            for (int i = from; i < to; i++)
            {
                StyleRule r = rules[i];
                for (int s = 0; s < r.Selectors!.Count; s++)
                {
                    if (r.Selectors.HasPseudoElement(s) == pseudo)
                    {
                        index.Add(new RuleSel(r, s, r.Selectors.GetSpecificity(s)), r.Selectors.GetKey(s));
                    }
                }
            }
        }
    }

    // ---- recompute the affected set and what inheritance carries below it ----

    private void RunWorklist(HashSet<int> affected, bool compareByValue, CancellationToken ct)
    {
        if (affected.Count == 0)
        {
            return;
        }

        Arena a = _doc.Arena;
        var queue = new PriorityQueue<int, int>();
        var queued = new HashSet<int>(affected);
        foreach (int e in affected)
        {
            queue.Enqueue(e, Depth(a, e));
        }

        int n = 0;
        while (queue.TryDequeue(out int idx, out int depth))
        {
            if ((++n & 255) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            int p = a.Parent[idx];
            ComputedStyle? parent = p >= 0 && a.Kind[p] == (byte)NodeKind.Element ? StyleOf(p) : null;
            ComputedStyle fresh = _engine.Compute(new Node(_doc, idx), parent, _match);
            ComputedStyle old = _fallback!.StyleOf(idx);
            // Typed values (em, %, currentcolor, bolder) resolve through the parent chain, so equal value text is only
            // enough when the old style still hangs below the same parent object.
            bool same = ReferenceEquals(fresh, old) || (compareByValue && ReferenceEquals(old.Parent, parent) && fresh.FastValueEquals(old));
            if (same)
            {
                continue;
            }

            _overlay![idx] = fresh;
            for (int c = a.First[idx]; c >= 0; c = a.Next[c])
            {
                if (a.Kind[c] == (byte)NodeKind.Element && queued.Add(c))
                {
                    queue.Enqueue(c, depth + 1);
                }
            }
        }
    }

    private static int Depth(Arena a, int index)
    {
        int d = 0;
        for (int p = a.Parent[index]; p >= 0; p = a.Parent[p])
        {
            d++;
        }

        return d;
    }
}
