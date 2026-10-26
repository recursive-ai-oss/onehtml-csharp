using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Rendered;

/// <summary>Document-level facts of the captured page.</summary>
/// <param name="Url">The document URL, or <see langword="null"/>.</param>
/// <param name="BaseUrl">The base URL, or <see langword="null"/>.</param>
/// <param name="Title">The title at capture time, or <see langword="null"/>.</param>
/// <param name="FrameId">The frame id, or <see langword="null"/>.</param>
/// <param name="Language">The content language, or <see langword="null"/>.</param>
/// <param name="Encoding">The encoding name, or <see langword="null"/>.</param>
/// <param name="ContentWidth">Scrollable content width, or <see cref="double.NaN"/>.</param>
/// <param name="ContentHeight">Scrollable content height, or <see cref="double.NaN"/>.</param>
/// <param name="ScrollX">Horizontal scroll offset at capture, or <see cref="double.NaN"/>.</param>
/// <param name="ScrollY">Vertical scroll offset at capture, or <see cref="double.NaN"/>.</param>
public sealed record PageFacts(string? Url, string? BaseUrl, string? Title, string? FrameId, string? Language, string? Encoding, double ContentWidth, double ContentHeight, double ScrollX, double ScrollY);

/// <summary>A frame owner element and the snapshot document holding its content.</summary>
/// <param name="NodeIndex">The index of the frame owner element (<c>iframe</c>, <c>frame</c>, <c>object</c>, <c>embed</c>) in the OneHtml document.</param>
/// <param name="SnapshotDocumentIndex">The index into <see cref="DomSnapshot.Documents"/> of the frame's content document.</param>
public readonly record struct FrameLink(int NodeIndex, int SnapshotDocumentIndex);

/// <summary>
/// What Chrome's layout engine reported about a page: boxes, paint order, stacking contexts, text boxes and the computed
/// styles of a requested property list, keyed by the nodes of one OneHtml <see cref="Document"/>. Built by
/// <see cref="DomSnapshotAdapter"/>. Immutable, safe to read from any number of threads, and holds no reference to the
/// document (so it does not keep it alive); queries take a <see cref="Node"/> of the document it was built for.
/// </summary>
public sealed class RenderedFacts
{
    /// <summary>The version of this model. Raised only when the meaning of a fact changes.</summary>
    public const int FormatVersion = 1;

    internal RenderedFacts(FactsData data)
    {
        D = data;
    }

    internal FactsData D { get; }

    /// <summary>The <see cref="Document.Id"/> of the document the facts were mapped onto.</summary>
    public long DocumentId => D.DocumentId;

    /// <summary>The number of nodes of that document.</summary>
    public int NodeCount => D.NodeCount;

    /// <summary>A 64-bit hash of the structure the facts were mapped over (element names and text nodes with their depths, in document order).</summary>
    public ulong StructureFingerprint => D.Fingerprint;

    /// <summary>The browser product of the capture, when recorded.</summary>
    public string? Product => D.Product;

    /// <summary>The DevTools protocol version of the capture, when recorded.</summary>
    public string? ProtocolVersion => D.ProtocolVersion;

    /// <summary>The viewport width in CSS pixels the page was laid out in; 0 when the caller did not say.</summary>
    public double ViewportWidth => D.ViewportWidth;

    /// <summary>The viewport height in CSS pixels; 0 when the caller did not say.</summary>
    public double ViewportHeight => D.ViewportHeight;

    /// <summary>
    /// The style context the capture was taken under, as the caller described it, or <see langword="null"/>. Facts say nothing about
    /// states the capture did not have (hover, focus), so consumers should treat a different context as a different capture.
    /// </summary>
    public StyleContext? Context => D.Context;

    /// <summary>Document-level facts.</summary>
    public PageFacts Page => D.Page;

    /// <summary>The computed-style property names that <see cref="BoxFacts.GetStyle(string)"/> can answer, in capture order.</summary>
    public IReadOnlyList<string> ComputedStyleNames => D.StyleNames;

    /// <summary>The number of boxes (layout items) attached to nodes of the document.</summary>
    public int BoxCount => D.BoxCount;

    /// <summary>The number of pseudo-element boxes attached to their hosts.</summary>
    public int GeneratedBoxCount => D.GenHost.Length;

    /// <summary>The frame owner elements of the document and where their content documents are in the snapshot.</summary>
    public IReadOnlyList<FrameLink> Frames => D.Frames;

    /// <summary>The number of nodes that have background samples.</summary>
    public int BackgroundSampleCount => D.Samples?.Count ?? 0;

    /// <summary>Whether any node has a background sample.</summary>
    public bool HasBackgroundSamples => BackgroundSampleCount > 0;

    /// <summary>The facts of a node.</summary>
    /// <param name="node">A node of the document the facts were built for.</param>
    /// <exception cref="ArgumentException"><paramref name="node"/> is null or belongs to another document.</exception>
    public NodeFacts this[Node node]
    {
        get
        {
            if (!TryGet(node, out NodeFacts f))
            {
                throw new ArgumentException("The node is null or belongs to a different document than these facts.", nameof(node));
            }

            return f;
        }
    }

    /// <summary>Gets the facts of a node without throwing.</summary>
    /// <param name="node">The node.</param>
    /// <param name="facts">The facts when the node belongs to the document.</param>
    /// <returns><see langword="false"/> for a null node or a node of another document.</returns>
    public bool TryGet(Node node, out NodeFacts facts)
    {
        if (node.Document is { } doc && doc.Id == D.DocumentId && node.Index < D.NodeCount)
        {
            facts = new NodeFacts(this, node.Index);
            return true;
        }

        facts = default;
        return false;
    }

    /// <summary>The nearest ancestor-or-self that begins a stacking context.</summary>
    /// <param name="node">A node of the document the facts were built for.</param>
    /// <returns>That node, or a null node when none does (or the node is not covered).</returns>
    /// <exception cref="ArgumentException"><paramref name="node"/> is null or belongs to another document.</exception>
    public Node GetStackingContext(Node node)
    {
        NodeFacts f = this[node];
        int s = f.StackingContextNodeIndex;
        return s < 0 ? default : node.Document!.GetNode(s);
    }

    /// <summary>
    /// Attaches background samples. Entries name their element by snapshot node index or backend node id; entries that name no
    /// mapped element are listed in the result. A later entry for the same element replaces an earlier one.
    /// </summary>
    /// <param name="samples">The samples.</param>
    /// <returns>New facts sharing everything else with these, and what matched.</returns>
    /// <exception cref="ArgumentException">The samples were taken for a different viewport.</exception>
    public BackgroundSampleApplication WithBackgroundSamples(BackgroundSamples samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.ViewportWidth > 0 && D.ViewportWidth > 0 && (samples.ViewportWidth != D.ViewportWidth || samples.ViewportHeight != D.ViewportHeight))
        {
            throw new ArgumentException($"The samples were taken at {samples.ViewportWidth}x{samples.ViewportHeight}; the facts are for {D.ViewportWidth}x{D.ViewportHeight}.", nameof(samples));
        }

        var byBackend = new Dictionary<long, int>();
        var bySnapshot = new Dictionary<int, int>();
        for (int i = 0; i < D.NodeCount; i++)
        {
            if (D.NodeSnapshot[i] >= 0)
            {
                bySnapshot[D.NodeSnapshot[i]] = i;
            }

            if (D.NodeBackend[i] >= 0)
            {
                byBackend[D.NodeBackend[i]] = i;
            }
        }

        var map = new Dictionary<int, BackgroundSample>(D.Samples ?? []);
        var unmatched = new List<int>();
        for (int i = 0; i < samples.Entries.Count; i++)
        {
            BackgroundSampleEntry e = samples.Entries[i];
            int node = -1;
            if (e.BackendNodeId is { } b)
            {
                if (!byBackend.TryGetValue(b, out node))
                {
                    node = -1;
                }
            }
            else if (e.SnapshotNode is { } s && !bySnapshot.TryGetValue(s, out node))
            {
                node = -1;
            }

            if (node < 0)
            {
                unmatched.Add(i);
            }
            else
            {
                map[node] = e.Sample;
            }
        }

        FactsData copy = D.Clone();
        copy.Samples = map;
        return new BackgroundSampleApplication(new RenderedFacts(copy), samples.Entries.Count - unmatched.Count, unmatched);
    }

    // ---- generated-box accessors (GeneratedBox is a view over these) ----

    internal string GenType(int i) => D.Str(D.GenType[i]);

    internal string GenIdentifier(int i) => D.Str(D.GenIdent[i]);

    internal BoxFacts GenBox(int i) => new(this, D.GenBox[i]);

    internal string GenText(int i) => D.Str(D.GenText[i]);

    internal int GenNodeCount(int i) => D.GenNodes[i];

    internal BoxFacts[] GenAllBoxes(int i)
    {
        int[] ids = D.GenBoxes[i];
        var a = new BoxFacts[ids.Length];
        for (int k = 0; k < a.Length; k++)
        {
            a[k] = new BoxFacts(this, ids[k]);
        }

        return a;
    }

    internal GeneratedBox[] GeneratedFor(int nodeIndex)
    {
        int[] hosts = D.GenHost;
        int lo = 0, hi = hosts.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (hosts[mid] < nodeIndex)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        int end = lo;
        while (end < hosts.Length && hosts[end] == nodeIndex)
        {
            end++;
        }

        if (end == lo)
        {
            return [];
        }

        var result = new GeneratedBox[end - lo];
        for (int i = lo; i < end; i++)
        {
            result[i - lo] = new GeneratedBox(this, i);
        }

        return result;
    }
}

/// <summary>The result of <see cref="RenderedFacts.WithBackgroundSamples(BackgroundSamples)"/>.</summary>
/// <param name="Facts">Facts with the samples attached.</param>
/// <param name="Applied">How many entries named a mapped element.</param>
/// <param name="Unmatched">Indexes into <see cref="BackgroundSamples.Entries"/> of entries that named no mapped element.</param>
public sealed record BackgroundSampleApplication(RenderedFacts Facts, int Applied, IReadOnlyList<int> Unmatched);

/// <summary>The facts of one node of the document: a cheap view over the shared tables.</summary>
public readonly struct NodeFacts
{
    private readonly RenderedFacts _facts;

    internal NodeFacts(RenderedFacts facts, int nodeIndex)
    {
        _facts = facts;
        NodeIndex = nodeIndex;
    }

    /// <summary>The node's index in the document (<see cref="Node.Index"/>).</summary>
    public int NodeIndex { get; }

    /// <summary>Whether the node was laid out at all: it has a box. Elements that are <c>display: none</c>, descendants of such, and <c>display: contents</c> elements have none.</summary>
    public bool HasLayout => _facts.D.NodeBox[NodeIndex] >= 0;

    /// <summary>The node's own box; <see cref="BoxFacts.IsValid"/> is false when <see cref="HasLayout"/> is.</summary>
    public BoxFacts Layout => new(_facts, _facts.D.NodeBox[NodeIndex]);

    /// <summary>Whether the node begins a stacking context.</summary>
    public bool IsStackingContext => HasLayout && Layout.IsStackingContext;

    /// <summary>The index of the nearest ancestor-or-self node that begins a stacking context, or -1.</summary>
    public int StackingContextNodeIndex => _facts.D.NodeStackRoot[NodeIndex];

    /// <summary>The node's backend node id in the capture, or -1 when the node was not in the snapshot or the ids were not reported.</summary>
    public long BackendNodeId => _facts.D.NodeBackend[NodeIndex];

    /// <summary>The node's index in the snapshot document, or -1 when the snapshot has no node for it (for example template content a capture left out).</summary>
    public int SnapshotNodeIndex => _facts.D.NodeSnapshot[NodeIndex];

    /// <summary>The value of an <c>input</c> as the page held it, or <see langword="null"/>.</summary>
    public string? InputValue => _facts.D.InputValue.TryGetValue(NodeIndex, out int s) ? _facts.D.Str(s) : null;

    /// <summary>The value of a <c>textarea</c> as the page held it, or <see langword="null"/>.</summary>
    public string? TextValue => _facts.D.TextValue.TryGetValue(NodeIndex, out int s) ? _facts.D.Str(s) : null;

    /// <summary>Whether a checkbox or radio was checked. The snapshot lists only the checked ones, so this is false for every other node.</summary>
    public bool InputChecked => (_facts.D.Flags[NodeIndex] & FactsData.FlagChecked) != 0;

    /// <summary>Whether an <c>option</c> was selected. The snapshot lists only the selected ones, so this is false for every other node.</summary>
    public bool OptionSelected => (_facts.D.Flags[NodeIndex] & FactsData.FlagSelected) != 0;

    /// <summary>Whether the page treats the node as responding to clicks (a link, or a node with a click listener).</summary>
    public bool IsClickable => (_facts.D.Flags[NodeIndex] & FactsData.FlagClickable) != 0;

    /// <summary>The pseudo-element boxes (<c>::before</c>, <c>::after</c>, <c>::marker</c> and so on) of this element, in snapshot order.</summary>
    public IReadOnlyList<GeneratedBox> GeneratedBoxes => _facts.GeneratedFor(NodeIndex);

    /// <summary>The background sample supplied by the caller for this element, if any.</summary>
    public BackgroundSample? Background => _facts.D.Samples is { } s && s.TryGetValue(NodeIndex, out BackgroundSample v) ? v : null;

    /// <summary>A computed style value of the node's box as Chrome reported it; see <see cref="BoxFacts.GetStyle(string)"/>.</summary>
    /// <param name="property">The property name, as in <see cref="RenderedFacts.ComputedStyleNames"/>.</param>
    public string? GetStyle(string property) => Layout.GetStyle(property);
}

/// <summary>The layout facts of one box: a cheap view over the shared tables.</summary>
public readonly struct BoxFacts
{
    private readonly RenderedFacts? _facts;
    private readonly int _box;

    internal BoxFacts(RenderedFacts facts, int box)
    {
        _facts = box < 0 ? null : facts;
        _box = box;
    }

    /// <summary>Whether this is a real box. A default value, or the layout of a node that was not laid out, is not.</summary>
    public bool IsValid => _facts is not null;

    private FactsData D => _facts?.D ?? throw new InvalidOperationException("There is no box: the node was not laid out.");

    /// <summary>The border box in document coordinates (the layout item's <c>bounds</c>); <see cref="double.NaN"/> components if the capture had none.</summary>
    /// <exception cref="InvalidOperationException">There is no box.</exception>
    public LayoutRect BorderBox => Rect(D.Bounds);

    /// <summary>
    /// The element's offset rectangle (<c>offsetRects</c>: offsetLeft, offsetTop, offsetWidth, offsetHeight), or <see langword="null"/>
    /// when the capture did not include DOM rectangles or the item has none.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no box.</exception>
    public LayoutRect? OffsetRect => OptRect(D.Offset);

    /// <summary>The client rectangle (<c>clientRects</c>: clientLeft, clientTop, clientWidth, clientHeight), or <see langword="null"/>.</summary>
    /// <exception cref="InvalidOperationException">There is no box.</exception>
    public LayoutRect? ClientRect => OptRect(D.Client);

    /// <summary>The scroll rectangle (<c>scrollRects</c>: scrollLeft, scrollTop, scrollWidth, scrollHeight), or <see langword="null"/>.</summary>
    /// <exception cref="InvalidOperationException">There is no box.</exception>
    public LayoutRect? ScrollRect => OptRect(D.Scroll);

    /// <summary>
    /// The global paint order index: boxes painted together share an index, and a larger index paints later (on top). <see langword="null"/>
    /// when the capture did not include paint order.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no box.</exception>
    public int? PaintOrder => D.Paint[_box] < 0 ? null : D.Paint[_box];

    /// <summary>Whether the box begins a stacking context.</summary>
    /// <exception cref="InvalidOperationException">There is no box.</exception>
    public bool IsStackingContext => D.Stacking[_box] != 0;

    /// <summary>The text of a text box's layout object (whitespace collapsed as rendered), or <see langword="null"/>.</summary>
    /// <exception cref="InvalidOperationException">There is no box.</exception>
    public string? LayoutText => D.Text[_box] < 0 ? null : D.Str(D.Text[_box]);

    /// <summary>The rendered text runs of a text node, one per line fragment, in the order Chrome reported them; empty for boxes with none.</summary>
    /// <exception cref="InvalidOperationException">There is no box.</exception>
    public ReadOnlySpan<TextBox> TextBoxes => D.TextBoxes.AsSpan(D.TbStart[_box], D.TbStart[_box + 1] - D.TbStart[_box]);

    /// <summary>
    /// The background colours blended over everything the element overlaps, as Chrome's string (for example <c>rgb(0, 0, 0)</c>), or
    /// <see langword="null"/> when the capture did not include them. See <see cref="Rgba.TryParse(ReadOnlySpan{char}, out Rgba)"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no box.</exception>
    public string? BlendedBackgroundColor => D.Blended[_box] < 0 ? null : D.Str(D.Blended[_box]);

    /// <summary>The computed text opacity (the product of the opacities of overlapping ancestors), or <see langword="null"/>.</summary>
    /// <exception cref="InvalidOperationException">There is no box.</exception>
    public double? TextColorOpacity => double.IsNaN(D.TextOpacity[_box]) ? null : D.TextOpacity[_box];

    /// <summary>A computed style value as Chrome serialized it.</summary>
    /// <param name="property">A name from <see cref="RenderedFacts.ComputedStyleNames"/>, compared ordinally.</param>
    /// <returns>The value, or <see langword="null"/> when the property was not captured or the box has no styles.</returns>
    /// <exception cref="InvalidOperationException">There is no box.</exception>
    public string? GetStyle(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        FactsData d = D;
        if (!d.StyleIndex.TryGetValue(property, out int col) || d.StyleStride == 0)
        {
            return null;
        }

        int s = d.StyleValues[(_box * d.StyleStride) + col];
        return s < 0 ? null : d.Str(s);
    }

    private LayoutRect Rect(double[] a)
    {
        int o = _box * 4;
        return a.Length == 0 ? new LayoutRect(double.NaN, double.NaN, double.NaN, double.NaN) : new LayoutRect(a[o], a[o + 1], a[o + 2], a[o + 3]);
    }

    private LayoutRect? OptRect(double[] a)
    {
        if (a.Length == 0 || double.IsNaN(a[_box * 4]))
        {
            return null;
        }

        return Rect(a);
    }
}

/// <summary>The tables behind a <see cref="RenderedFacts"/>. Built once by the adapter, never changed afterwards.</summary>
internal sealed class FactsData
{
    public const byte FlagChecked = 1, FlagSelected = 4, FlagClickable = 16;

    public long DocumentId;
    public int NodeCount;
    public ulong Fingerprint;
    public string? Product, ProtocolVersion;
    public double ViewportWidth, ViewportHeight;
    public StyleContext? Context;
    public PageFacts Page = new(null, null, null, null, null, null, double.NaN, double.NaN, double.NaN, double.NaN);

    public string[] Strings = [];
    public string[] StyleNames = [];
    public Dictionary<string, int> StyleIndex = new(StringComparer.Ordinal);
    public int StyleStride;

    // per node
    public int[] NodeBox = [], NodeSnapshot = [], NodeStackRoot = [];
    public long[] NodeBackend = [];
    public byte[] Flags = [];
    public Dictionary<int, int> InputValue = [], TextValue = [];
    public FrameLink[] Frames = [];
    public Dictionary<int, BackgroundSample>? Samples;

    // per box (4 doubles per rectangle; an empty array means the capture had none)
    public int BoxCount;
    public double[] Bounds = [], Client = [], Offset = [], Scroll = [], TextOpacity = [];
    public int[] Paint = [], Text = [], Blended = [], StyleValues = [], TbStart = [0];
    public byte[] Stacking = [];
    public TextBox[] TextBoxes = [];

    // generated boxes, sorted by host node index (stable)
    public int[] GenHost = [], GenType = [], GenIdent = [], GenBox = [], GenText = [], GenNodes = [];
    public int[][] GenBoxes = [];

    public string Str(int i) => i < 0 ? string.Empty : Strings[i];

    public FactsData Clone() => (FactsData)MemberwiseClone();
}
