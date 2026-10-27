using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Dom;
using RecursiveAI.OneHtml.Selectors;

namespace RecursiveAI.OneHtml.Css;

/// <summary>
/// A change of the interaction state for <see cref="LiveDocumentStyles.ApplyState"/>: which of hover, active, focus and
/// <c>:target</c> move, and to which element. A value type, so building and passing one allocates nothing. Parts that are not
/// named keep their current value; naming a part with the null node clears it.
/// </summary>
public readonly struct ElementStateChange
{
    private const byte HoverPart = 1;
    private const byte ActivePart = 2;
    private const byte FocusPart = 4;
    private const byte TargetPart = 8;

    private readonly byte _parts;
    private readonly Node _hover;
    private readonly Node _active;
    private readonly Node _focus;
    private readonly Node _target;
    private readonly FocusSource _source;

    private ElementStateChange(byte parts, Node hover, Node active, Node focus, FocusSource source, Node target)
    {
        _parts = parts;
        _hover = hover;
        _active = active;
        _focus = focus;
        _source = source;
        _target = target;
    }

    /// <summary>True when no part is named, so applying the change does nothing.</summary>
    public bool IsEmpty => _parts == 0;

    internal bool SetsHover => (_parts & HoverPart) != 0;

    internal bool SetsActive => (_parts & ActivePart) != 0;

    internal bool SetsFocus => (_parts & FocusPart) != 0;

    internal bool SetsTarget => (_parts & TargetPart) != 0;

    internal Node HoverNode => _hover;

    internal Node ActiveNode => _active;

    internal Node FocusNode => _focus;

    internal Node TargetNode => _target;

    internal FocusSource Source => _source;

    /// <summary>A change that moves the hover to this element (it and its ancestors match <c>:hover</c>).</summary>
    /// <param name="element">The hovered element, or the null node for none.</param>
    public static ElementStateChange Hover(Node element) => default(ElementStateChange).WithHover(element);

    /// <summary>A change that moves the activation to this element (it and its ancestors match <c>:active</c>).</summary>
    /// <param name="element">The active element, or the null node for none.</param>
    public static ElementStateChange Active(Node element) => default(ElementStateChange).WithActive(element);

    /// <summary>A change that moves focus to this element.</summary>
    /// <param name="element">The focused element, or the null node to clear focus.</param>
    /// <param name="source">How it got focus; it decides <c>:focus-visible</c>. Ignored when clearing.</param>
    public static ElementStateChange Focus(Node element, FocusSource source = FocusSource.Keyboard) => default(ElementStateChange).WithFocus(element, source);

    /// <summary>A change that moves <c>:target</c> to this element.</summary>
    /// <param name="element">The target element, or the null node for none.</param>
    public static ElementStateChange Target(Node element) => default(ElementStateChange).WithTarget(element);

    /// <summary>This change, also moving the hover.</summary>
    /// <param name="element">The hovered element, or the null node for none.</param>
    public ElementStateChange WithHover(Node element) => new((byte)(_parts | HoverPart), element, _active, _focus, _source, _target);

    /// <summary>This change, also moving the activation.</summary>
    /// <param name="element">The active element, or the null node for none.</param>
    public ElementStateChange WithActive(Node element) => new((byte)(_parts | ActivePart), _hover, element, _focus, _source, _target);

    /// <summary>This change, also moving focus.</summary>
    /// <param name="element">The focused element, or the null node to clear focus.</param>
    /// <param name="source">How it got focus. Ignored when clearing.</param>
    public ElementStateChange WithFocus(Node element, FocusSource source = FocusSource.Keyboard) => new((byte)(_parts | FocusPart), _hover, _active, element, source, _target);

    /// <summary>This change, also moving <c>:target</c>.</summary>
    /// <param name="element">The target element, or the null node for none.</param>
    public ElementStateChange WithTarget(Node element) => new((byte)(_parts | TargetPart), _hover, _active, _focus, _source, element);
}

/// <summary>
/// The computed styles of a document under an interaction state that moves in place, for a caller that changes hover, active,
/// focus or <c>:target</c> many times a second. It is made from a <see cref="DocumentStyles"/> with <see cref="Create"/>, owns its
/// buffers, and never touches that object or any view made from it: those stay immutable snapshots. <see cref="ApplyState"/>
/// restyles only what the change can reach and allocates nothing in steady state; <see cref="ChangedElementIndices"/> lists the
/// elements whose style object changed; <see cref="GetStyle(int)"/> and <see cref="GetValue"/> read styles by element index
/// without building anything.
/// </summary>
/// <remarks>
/// <para>
/// Single writer: call <see cref="ApplyState"/> from one thread at a time, and do not read while it runs. Between calls any number
/// of threads may read. <see cref="ComputedStyle"/> objects it returns are shared, immutable and stay valid after later changes.
/// </para>
/// <para>
/// "Steady state" means after a warm-up in which each element and state was reached once: the first time an element meets a
/// state combination its style may be built (and cached in the shared style table), and a rule that uses <c>:has()</c> with a state
/// pseudo-class rebuilds its page-wide marks into the same array on the first match after each change. Reads of typed values
/// allocate once per style when first asked, then never. A cascade that rolls back (<c>revert</c>, <c>revert-layer</c>) allocates.
/// </para>
/// <para>
/// Media features and the viewport are fixed at creation, because they select which rules exist; to change them, make a new
/// <see cref="DocumentStyles"/> with <see cref="DocumentStyles.WithContext"/> and call <see cref="Create"/> again.
/// </para>
/// </remarks>
public sealed class LiveDocumentStyles
{
    private readonly DocumentStyles _snapshot;
    private readonly Document _doc;
    private readonly Arena _a;
    private readonly StyleEngine _engine;
    private readonly ComputedStyle?[] _styles;
    private readonly int[] _depth;
    private readonly RuleSel[]?[] _staticMatches;
    private readonly InteractionState _state = new();
    private readonly InteractionState _prevState = new();
    private readonly FormStateOverrides _formCur;
    private readonly FormStateOverrides _formPrev;
    private readonly List<int> _formTouchedIndex = [];
    private readonly List<FormStateKind> _formTouchedKind = [];
    private readonly List<int> _formRoots = [];
    private readonly SelectorMatchOptions _cur;
    private readonly SelectorMatchOptions _prev;
    private readonly SelectorBulkCache? _bulk;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, ComputedStyle?> _pseudo = new();

    // Per-change scratch, reused so a change allocates nothing.
    private readonly int[] _fullStamp;
    private readonly int[] _withinStamp;
    private readonly int[] _affectedStamp;
    private readonly int[] _queuedStamp;
    private int _epoch;
    private int[] _affected = new int[64];
    private int _affectedCount;
    private int[] _changed = new int[64];
    private int _changedCount;
    private readonly PriorityQueue<int, int> _queue = new();
    private readonly List<FocusAnchor> _anchorsChanged = [];
    private readonly List<FocusAnchor> _anchorsPicked = [];
    private readonly List<RuleSel> _candidates = [];
    private readonly List<RuleSel> _oldMatches = [];
    private readonly List<RuleSel> _newMatches = [];
    private readonly Stack<(int Parent, int Level)> _levels = new();

    private Node _hover;
    private Node _active;
    private Node _focus;
    private Node _target;
    private FocusSource _focusSource;

    private LiveDocumentStyles(DocumentStyles snapshot)
    {
        _snapshot = snapshot;
        _doc = snapshot.Document;
        _a = _doc.Arena;
        _engine = snapshot.Engine;
        StyleContext c = snapshot.Context;
        _hover = c.Hover;
        _active = c.Active;
        _focus = c.Focus;
        _target = c.Target;
        _focusSource = c.Focus.IsNull ? FocusSource.Keyboard : c.FocusSource;
        SetState(_state, _focus, _focusSource, _hover, _active, _target);
        _prevState.CopyFrom(_state);
        _formCur = FormStateOverrides.CreateLive(_doc);
        _formPrev = FormStateOverrides.CreateLive(_doc);
        _formCur.CopyFrom(c.FormState);
        _formPrev.CopyFrom(c.FormState);
        _state.SetForm(_formCur);
        _prevState.SetForm(_formPrev);
        _bulk = _engine.UsesBulkCaches ? new SelectorBulkCache() : null;
        var scratch = new MatchScratch();
        _cur = new SelectorMatchOptions { Scope = _doc.DocumentElement, State = _state, BulkCache = _bulk, Scratch = scratch };
        _prev = new SelectorMatchOptions { Scope = _doc.DocumentElement, State = _prevState, Scratch = scratch };

        int n = _doc.NodeCount;
        _styles = new ComputedStyle?[n];
        _depth = new int[n];
        _staticMatches = new RuleSel[]?[n];
        _fullStamp = new int[n];
        _withinStamp = new int[n];
        _affectedStamp = new int[n];
        _queuedStamp = new int[n];
        for (int i = 0; i < n; i++)
        {
            if (_a.Kind[i] != (byte)NodeKind.Element)
            {
                continue;
            }

            int p = _a.Parent[i];
            _depth[i] = p >= 0 && _a.Kind[p] == (byte)NodeKind.Element ? _depth[p] + 1 : 0;
            _styles[i] = snapshot.StyleOf(i);
        }
    }

    /// <summary>
    /// Makes live styles from a <see cref="DocumentStyles"/>, starting in its context (its media features are fixed from here on, its
    /// hover, active, focus and target are the starting state). Computes the style of every element, so it costs about
    /// <see cref="DocumentStyles.ComputeAll"/>; the argument is not changed and keeps working as before.
    /// </summary>
    /// <param name="styles">The styles to start from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="styles"/> is null.</exception>
    public static LiveDocumentStyles Create(DocumentStyles styles)
    {
        ArgumentNullException.ThrowIfNull(styles);
        return new LiveDocumentStyles(styles);
    }

    /// <summary>The document.</summary>
    public Document Document => _doc;

    /// <summary>The hovered element (it and its ancestors match <c>:hover</c>), or the null node.</summary>
    public Node Hover => _hover;

    /// <summary>The element being activated, or the null node.</summary>
    public Node Active => _active;

    /// <summary>The focused element, or the null node.</summary>
    public Node Focus => _focus;

    /// <summary>How <see cref="Focus"/> was given; <see cref="FocusSource.Keyboard"/> when nothing is focused.</summary>
    public FocusSource FocusSource => _focusSource;

    /// <summary>The element <c>:target</c> names, or the null node.</summary>
    public Node Target => _target;

    /// <summary>True when a rule that styles <c>::before</c>, <c>::after</c> or <c>::marker</c> reads an interaction state, so <see cref="ChangedElementIndices"/> does not list every generated box that changed; read those through <see cref="GetPseudoStyle"/>.</summary>
    public bool HasInteractivePseudoElementRules => _engine.Db.PseudoElementDynamic;

    /// <summary>The current context: the starting media features with the current interaction state. Builds a new object, so it is not for the per-change path.</summary>
    public StyleContext GetContext() =>
        _snapshot.Context with { Focus = _focus, FocusSource = _focusSource, Hover = _hover, Active = _active, Target = _target, FormState = _formCur.ToImmutable() };

    /// <summary>
    /// An immutable <see cref="DocumentStyles"/> equal to the current state, made as <see cref="DocumentStyles.WithContext"/> makes one
    /// from the starting styles. Allocates; later changes do not reach it.
    /// </summary>
    public DocumentStyles ToSnapshot() => _snapshot.WithContext(GetContext());

    /// <summary>The number of elements whose style object changed in the last <see cref="ApplyState"/>; 0 before the first.</summary>
    public int ChangedElementCount => _changedCount;

    /// <summary>
    /// The indices (<see cref="Node.Index"/>) of the elements whose style object the last <see cref="ApplyState"/> replaced, ascending.
    /// A view of an internal buffer: valid until the next <see cref="ApplyState"/>. Elements whose generated boxes changed are not
    /// included unless their own style changed (see <see cref="HasInteractivePseudoElementRules"/>).
    /// </summary>
    public ReadOnlySpan<int> ChangedElementIndices => _changed.AsSpan(0, _changedCount);

    /// <summary>
    /// Copies <see cref="ChangedElementIndices"/> into a caller-owned buffer. Allocation-free.
    /// </summary>
    /// <param name="destination">Where to write, ascending.</param>
    /// <returns>
    /// The number of indices written, which is <see cref="ChangedElementCount"/> or <c>destination.Length</c> if that is smaller: a buffer
    /// that is too small receives the lowest indices and the rest are not written (compare the result with <see cref="ChangedElementCount"/>).
    /// </returns>
    public int ChangedElementsInto(Span<int> destination)
    {
        int n = Math.Min(destination.Length, _changedCount);
        _changed.AsSpan(0, n).CopyTo(destination);
        return n;
    }

    /// <summary>The computed style of an element under the current state. Allocation-free.</summary>
    /// <param name="elementIndex">The <see cref="Node.Index"/> of an element of <see cref="Document"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The index is not an element of the document.</exception>
    public ComputedStyle GetStyle(int elementIndex)
    {
        if ((uint)elementIndex >= (uint)_styles.Length || _styles[elementIndex] is not { } s)
        {
            throw new ArgumentOutOfRangeException(nameof(elementIndex), "The index is not an element of the document.");
        }

        return s;
    }

    /// <summary>The computed style of an element under the current state. Allocation-free.</summary>
    /// <param name="element">An element of <see cref="Document"/>.</param>
    /// <exception cref="ArgumentException">The node is not an element of this document.</exception>
    public ComputedStyle GetStyle(Node element)
    {
        if (element.IsNull || !ReferenceEquals(element.Document, _doc) || !element.IsElement)
        {
            throw new ArgumentException("The node is not an element of this document.", nameof(element));
        }

        return _styles[element.Index]!;
    }

    /// <summary>
    /// The value of a property for an element, by ids: the shared object of the style table, as <see cref="ComputedStyle.GetValue(int)"/> reads it.
    /// Allocation-free.
    /// </summary>
    /// <param name="elementIndex">The <see cref="Node.Index"/> of an element of <see cref="Document"/>.</param>
    /// <param name="propertyId">An id from <see cref="PropertyTable.IdOf"/>.</param>
    /// <returns>The value, or null for an id that is not a property.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is not an element of the document.</exception>
    public StyleValue? GetValue(int elementIndex, int propertyId) => GetStyle(elementIndex).GetValue(propertyId);

    /// <summary>
    /// The style of a generated box under the current state, as <see cref="DocumentStyles.GetPseudoStyle"/> reads it. Computed on first
    /// use and kept until the next state change when a rule styling generated boxes reads interaction state. Safe to call from several threads between state changes.
    /// </summary>
    /// <param name="element">An element of <see cref="Document"/>.</param>
    /// <param name="kind">Which pseudo-element.</param>
    /// <exception cref="ArgumentException">The node is not an element of this document.</exception>
    public ComputedStyle? GetPseudoStyle(Node element, PseudoElementKind kind)
    {
        ComputedStyle style = GetStyle(element);
        string name = PseudoNames.Of(kind);
        if (!_engine.Db.PseudoIndex.ContainsKey(name) || (kind == PseudoElementKind.Placeholder && !ElementState.HasPlaceholderBox(element)))
        {
            return null;
        }

        long key = PseudoNames.Key(element.Index, kind);
        if (_pseudo.TryGetValue(key, out ComputedStyle? cached))
        {
            return cached;
        }

        return _pseudo.GetOrAdd(key, kind == PseudoElementKind.Selection
            ? _engine.ComputeHighlight(element, i => _styles[i]!, _pseudo, _cur)
            : _engine.ComputePseudo(element, name, style, _cur));
    }

    /// <summary>
    /// Moves hover, active, focus and/or <c>:target</c>, and restyles what the move reaches: the elements whose rules with a state
    /// pseudo-class match differently (through combinators and <c>:has()</c> too) and the descendants whose inherited values change.
    /// The result equals <see cref="DocumentStyles.WithContext"/> with the equivalent <see cref="StyleContext"/>. Read what changed
    /// from <see cref="ChangedElementIndices"/>. Allocation-free in steady state (see the type remarks).
    /// </summary>
    /// <param name="change">The parts to move.</param>
    /// <exception cref="ArgumentException">A named node is not an element of the document.</exception>
    public void ApplyState(in ElementStateChange change)
    {
        _changedCount = 0;
        Node hover = change.SetsHover ? change.HoverNode : _hover;
        Node active = change.SetsActive ? change.ActiveNode : _active;
        Node focus = change.SetsFocus ? change.FocusNode : _focus;
        Node target = change.SetsTarget ? change.TargetNode : _target;
        FocusSource source = change.SetsFocus ? change.Source : _focusSource;
        Check(hover);
        Check(active);
        Check(focus);
        Check(target);
        if (focus.IsNull)
        {
            source = FocusSource.Keyboard;
        }

        if (hover.Equals(_hover) && active.Equals(_active) && focus.Equals(_focus) && target.Equals(_target) && source == _focusSource)
        {
            return;
        }

        _prevState.CopyFrom(_state);
        _hover = hover;
        _active = active;
        _focus = focus;
        _target = target;
        _focusSource = source;
        SetState(_state, focus, source, hover, active, target);
        _bulk?.InvalidateInteraction();
        if (_engine.Db.PseudoElementDynamic)
        {
            _pseudo.Clear();
        }

        RuleDatabase db = _engine.Db;
        if (db.DynamicIndex.Count == 0)
        {
            return;
        }

        BeginEpoch();
        _affectedCount = 0;
        if (db.FocusAnchors is null)
        {
            ScanAll(db.DynamicIndex);
        }
        else
        {
            Anchored();
            if (db.StateNestedReach)
            {
                ScanAll(db.NestedReachIndex);
            }
        }

        Restyle();
    }

    /// <summary>
    /// Changes form state (checkedness, disabledness, indeterminate state) of any number of elements at once and restyles what the change
    /// reaches: the elements whose rules with <c>:checked</c>, <c>:disabled</c>, <c>:enabled</c>, <c>:indeterminate</c>, <c>:read-only</c> or
    /// <c>:read-write</c> match differently (through combinators, <c>:has()</c> and <c>:not()</c> too), the controls a disabled <c>fieldset</c> or
    /// <c>optgroup</c> and the rest of a radio group depend on, and the descendants whose inherited values change. The result equals
    /// <see cref="DocumentStyles.WithContext"/> with the equivalent <see cref="StyleContext.FormState"/>. Read what changed from
    /// <see cref="ChangedElementIndices"/>. Allocation-free in steady state (see the type remarks). Overrides are literal, as
    /// <see cref="FormStateOverrides"/> says; hover, focus, active and target are untouched.
    /// </summary>
    /// <param name="changes">The changes, applied in order; a later change to the same element and state wins. Empty does nothing.</param>
    /// <exception cref="ArgumentException">A change names a node that is not an element of the document. Nothing is applied.</exception>
    public void ApplyFormState(ReadOnlySpan<FormStateChange> changes)
    {
        _changedCount = 0;
        foreach (FormStateChange c in changes)
        {
            if (c.Element.IsNull || !ReferenceEquals(c.Element.Document, _doc) || !c.Element.IsElement)
            {
                throw new ArgumentException("A change names a node that is not an element of the document.", nameof(changes));
            }
        }

        _formTouchedIndex.Clear();
        _formTouchedKind.Clear();
        _formRoots.Clear();
        foreach (FormStateChange c in changes)
        {
            if (_formCur.Set(c.Element.Index, c.Kind, c.Value))
            {
                _formTouchedIndex.Add(c.Element.Index);
                _formTouchedKind.Add(c.Kind);
            }
        }

        if (_formTouchedIndex.Count == 0)
        {
            return;
        }

        try
        {
            _bulk?.InvalidateInteraction();
            RuleDatabase db = _engine.Db;
            if (db.PseudoElementDynamic)
            {
                _pseudo.Clear();
            }

            if (db.DynamicIndex.Count == 0)
            {
                return;
            }

            BeginEpoch();
            _affectedCount = 0;
            if (db.FocusAnchors is null)
            {
                ScanAll(db.DynamicIndex);
            }
            else
            {
                for (int i = 0; i < _formTouchedIndex.Count; i++)
                {
                    var x = new Node(_doc, _formTouchedIndex[i]);
                    _formRoots.Add(x.Index);
                    FormDependents.Add(x, _formTouchedKind[i], _formRoots);
                }

                for (int i = 0; i < _formRoots.Count; i++)
                {
                    Root(new Node(_doc, _formRoots[i]));
                }

                if (db.StateNestedReach)
                {
                    ScanAll(db.NestedReachIndex);
                }
            }

            Restyle();
        }
        finally
        {
            // The state the next change is compared against is the one just applied.
            for (int i = 0; i < _formTouchedIndex.Count; i++)
            {
                _formPrev.Set(_formTouchedIndex[i], _formTouchedKind[i], _formCur.ValueOf(_formTouchedIndex[i], _formTouchedKind[i]));
            }
        }
    }

    private void Check(Node n)
    {
        if (!n.IsNull && (!ReferenceEquals(n.Document, _doc) || !n.IsElement))
        {
            throw new ArgumentException("The state names a node that is not an element of the document.", nameof(n));
        }
    }

    // The same interaction state a StyleContext gives the matcher: :focus-visible after keyboard focus, and after pointer focus only on
    // text-entry controls and editing hosts; the control of a hovered or active label matches with it.
    private static void SetState(InteractionState s, Node focus, FocusSource source, Node hover, Node active, Node target) =>
        s.Set(focus, !focus.IsNull && (source == FocusSource.Keyboard || ElementState.IsTextEntryOrEditingHost(focus)), hover, LabelControl.Of(hover), active, LabelControl.Of(active), target);

    private void BeginEpoch()
    {
        if (++_epoch == int.MaxValue)
        {
            Array.Clear(_fullStamp);
            Array.Clear(_withinStamp);
            Array.Clear(_affectedStamp);
            Array.Clear(_queuedStamp);
            _epoch = 1;
        }
    }

    private void AddAffected(int index)
    {
        if (_affectedStamp[index] == _epoch)
        {
            return;
        }

        _affectedStamp[index] = _epoch;
        if (_affectedCount == _affected.Length)
        {
            Array.Resize(ref _affected, _affected.Length * 2);
        }

        _affected[_affectedCount++] = index;
    }

    // ---- finding what a state change reaches ----
    //
    // Selectors 4 section 9: a user-action pseudo-class matches an element and, for :hover, :active and :focus-within, its ancestors. So a
    // compound that reads state can change its match only at the elements the state moved from or to, and at their ancestors. From such
    // an anchor a selector reaches only along its own combinators: below, and to later siblings with their descendants (a :has() argument
    // reads from the ancestors, and with a sibling combinator from their earlier siblings). Only the selectors whose anchor changed are
    // tested, and only over that reach. This is the walk the focus overlay uses, widened to every state and made allocation-free.

    private void Anchored()
    {
        InteractionState p = _prevState;
        InteractionState c = _state;
        if (!p.Hover.Equals(c.Hover))
        {
            Root(p.Hover);
            Root(c.Hover);
        }

        if (!p.HoverControl.Equals(c.HoverControl))
        {
            Root(p.HoverControl);
            Root(c.HoverControl);
        }

        if (!p.Active.Equals(c.Active))
        {
            Root(p.Active);
            Root(c.Active);
        }

        if (!p.ActiveControl.Equals(c.ActiveControl))
        {
            Root(p.ActiveControl);
            Root(c.ActiveControl);
        }

        if (!p.Focus.Equals(c.Focus) || p.FocusVisible != c.FocusVisible)
        {
            Root(p.Focus);
            Root(c.Focus);
        }

        if (!p.Target.Equals(c.Target))
        {
            Root(p.Target);
            Root(c.Target);
        }
    }

    // Visits a node a state moved to or from, in full, then its ancestors for the compounds that match along the chain. An ancestor
    // visited before has had all of its own ancestors visited, so the walk stops there.
    private void Root(Node x)
    {
        if (x.IsNull || !x.IsElement || _fullStamp[x.Index] == _epoch)
        {
            return;
        }

        Visit(x.Index, true);
        for (int i = _a.Parent[x.Index]; i >= 0 && _a.Kind[i] == (byte)NodeKind.Element && _withinStamp[i] != _epoch; i = _a.Parent[i])
        {
            Visit(i, false);
        }
    }

    private void Visit(int index, bool full)
    {
        if (full)
        {
            _fullStamp[index] = _epoch;
        }

        _withinStamp[index] = _epoch;
        RuleDatabase db = _engine.Db;
        List<FocusAnchor> anchors = db.FocusAnchors!;
        var p = new Node(_doc, index);
        _anchorsChanged.Clear();
        SelectorMatcher.ChangedFocusAnchors(p, Candidates(p, anchors), _prev, _cur, !full, _anchorsChanged);
        for (int i = 0; i < _anchorsChanged.Count; i++)
        {
            TestReach(p, _anchorsChanged[i]);
        }

        // An earlier sibling of a changed element or of one of its ancestors can match a :has() with a sibling combinator.
        if (db.HasSidewaysFocus)
        {
            for (Node s = p.PreviousSibling; !s.IsNull; s = s.PreviousSibling)
            {
                if (!s.IsElement)
                {
                    continue;
                }

                _anchorsChanged.Clear();
                SelectorMatcher.ChangedFocusAnchors(s, Candidates(s, anchors), _prev, _cur, true, _anchorsChanged, true);
                for (int i = 0; i < _anchorsChanged.Count; i++)
                {
                    TestReach(s, _anchorsChanged[i]);
                }
            }
        }
    }

    // The anchors whose compound the element could satisfy (by its id, class or type), in a reused list.
    private List<FocusAnchor> Candidates(Node e, List<FocusAnchor> anchors)
    {
        _anchorsPicked.Clear();
        _candidates.Clear();
        _engine.Db.FocusAnchorIndex!.Collect(e, _candidates);
        for (int i = 0; i < _candidates.Count; i++)
        {
            _anchorsPicked.Add(anchors[_candidates[i].Sel]);
        }

        return _anchorsPicked;
    }

    private void TestReach(Node p, FocusAnchor a)
    {
        // The element itself first: the anchor compound may be the selector's subject.
        if (SelectorMatcher.MatchesComplex(p, a.Selector, _prev) != SelectorMatcher.MatchesComplex(p, a.Selector, _cur))
        {
            AddAffected(p.Index);
        }

        if (a.ReachesBelow)
        {
            TestBelow(p, a);
        }

        if (a.ReachesBeside)
        {
            for (Node sib = p.NextSibling; !sib.IsNull; sib = sib.NextSibling)
            {
                if (!sib.IsElement)
                {
                    continue;
                }

                TestOne(sib, a);
                if (a.ReachesBelow)
                {
                    TestBelow(sib, a);
                }
            }
        }
    }

    // The descendant elements an anchor can reach: all of them, or only as many levels as its child combinators allow.
    private void TestBelow(Node root, FocusAnchor a)
    {
        if (a.MaxDepth == int.MaxValue)
        {
            foreach (Node d in root.DescendantElements)
            {
                TestOne(d, a);
            }

            return;
        }

        _levels.Clear();
        _levels.Push((root.Index, 1));
        while (_levels.Count > 0)
        {
            (int parent, int level) = _levels.Pop();
            for (int c = _a.First[parent]; c >= 0; c = _a.Next[c])
            {
                if (_a.Kind[c] != (byte)NodeKind.Element)
                {
                    continue;
                }

                TestOne(new Node(_doc, c), a);
                if (level < a.MaxDepth)
                {
                    _levels.Push((c, level + 1));
                }
            }
        }
    }

    private void TestOne(Node e, FocusAnchor a)
    {
        if (_affectedStamp[e.Index] != _epoch && SelectorMatcher.MatchesComplex(e, a.Selector, _prev) != SelectorMatcher.MatchesComplex(e, a.Selector, _cur))
        {
            AddAffected(e.Index);
        }
    }

    // The slow case: a state pseudo-class inside a logical argument with a combinator ('a:not(.x:hover b)'): the element that matches is
    // not on the chain the state moved along, so the anchors cannot find it. Every element is matched against just those selectors
    // (the rest are found by the anchors) under both states.
    private void ScanAll(RuleIndex dynamic)
    {
        foreach (Node e in _doc.Root.DescendantElements)
        {
            _candidates.Clear();
            dynamic.Collect(e, _candidates);
            if (_candidates.Count == 0)
            {
                continue;
            }

            _oldMatches.Clear();
            _newMatches.Clear();
            SelectorMatcher.FilterMatching(e, _candidates, _prev, _oldMatches);
            SelectorMatcher.FilterMatching(e, _candidates, _cur, _newMatches);
            if (!SameMatches(_oldMatches, _newMatches))
            {
                AddAffected(e.Index);
            }
        }
    }

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

    // ---- recomputing the affected elements, parents first, and what inheritance carries below ----

    private void Restyle()
    {
        if (_affectedCount == 0)
        {
            return;
        }

        _queue.Clear();
        for (int i = 0; i < _affectedCount; i++)
        {
            int e = _affected[i];
            _queuedStamp[e] = _epoch;
            _queue.Enqueue(e, _depth[e]);
        }

        while (_queue.TryDequeue(out int idx, out int depth))
        {
            int p = _a.Parent[idx];
            ComputedStyle? parent = p >= 0 && _a.Kind[p] == (byte)NodeKind.Element ? _styles[p] : null;
            // An element no rule match moved for only inherits something new: its winning declarations are the ones it has, so the
            // cascade is not run again, the shared style for them under the new parent is looked up.
            ComputedStyle? fresh = _affectedStamp[idx] != _epoch && parent is not null && _styles[idx] is { } before ? _engine.Reparent(before, parent) : null;
            if (fresh is null)
            {
                fresh = _engine.Compute(new Node(_doc, idx), parent, _cur, _staticMatches);
            }

            if (ReferenceEquals(fresh, _styles[idx]))
            {
                continue;
            }

            _styles[idx] = fresh;
            if (_changedCount == _changed.Length)
            {
                Array.Resize(ref _changed, _changed.Length * 2);
            }

            _changed[_changedCount++] = idx;
            for (int c = _a.First[idx]; c >= 0; c = _a.Next[c])
            {
                if (_a.Kind[c] == (byte)NodeKind.Element && _queuedStamp[c] != _epoch)
                {
                    _queuedStamp[c] = _epoch;
                    _queue.Enqueue(c, depth + 1);
                }
            }
        }

        _changed.AsSpan(0, _changedCount).Sort();
    }
}
