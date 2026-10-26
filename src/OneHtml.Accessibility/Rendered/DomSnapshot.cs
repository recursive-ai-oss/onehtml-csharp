using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Accessibility.Rendered;

/// <summary>
/// A parsed <c>DOMSnapshot.captureSnapshot</c> result: the shared string table and one <see cref="DomSnapshotDocument"/>
/// per document (the page first, then its frames). Immutable once read. Use <see cref="DomSnapshotAdapter"/> to map it
/// onto a OneHtml document.
/// </summary>
public sealed class DomSnapshot
{
    /// <summary>The protocol version this reader is pinned to. A capture that names another major version is refused.</summary>
    public const string PinnedProtocolVersion = "1.3";

    private readonly DomSnapshotDocument[] _documents;

    internal DomSnapshot(string[] strings, DomSnapshotDocumentData[] documents, string[] computedStyleNames, string? product, string? protocolVersion)
    {
        Strings = strings;
        _documents = new DomSnapshotDocument[documents.Length];
        for (int i = 0; i < documents.Length; i++)
        {
            _documents[i] = new DomSnapshotDocument(this, documents[i]);
        }

        ComputedStyleNames = computedStyleNames;
        Product = product;
        ProtocolVersion = protocolVersion;
    }

    internal string[] Strings { get; }

    /// <summary>The documents; index 0 is the page, the others are frames in the order Chrome reported them.</summary>
    public IReadOnlyList<DomSnapshotDocument> Documents => _documents;

    /// <summary>
    /// The computed-style property names, in the order they were requested; each layout item's style values are in this
    /// order. Empty when the capture did not say which were requested (a bare protocol result) and the caller did not supply them.
    /// </summary>
    public IReadOnlyList<string> ComputedStyleNames { get; }

    /// <summary>The browser product string of the capture (for example <c>HeadlessChrome/156.0.8078.47</c>), when the capture recorded it.</summary>
    public string? Product { get; }

    /// <summary>The DevTools protocol version of the capture, when the capture recorded it.</summary>
    public string? ProtocolVersion { get; }

    /// <summary>Total number of DOM nodes over all documents.</summary>
    public int TotalNodeCount
    {
        get
        {
            long n = 0;
            foreach (DomSnapshotDocument d in _documents)
            {
                n += d.NodeCount;
            }

            return (int)n;
        }
    }

    internal string Str(int index) => index < 0 ? string.Empty : Strings[index];

    internal string? StrOrNull(int index) => index < 0 ? null : Strings[index];

    /// <summary>
    /// Reads a snapshot. Accepts the protocol result (<c>{"documents", "strings"}</c>), a full protocol response
    /// (<c>{"result": ...}</c>) and the repository's capture file (<c>{"format", "snapshot"}</c>), which also carries the
    /// computed-style list and the pinned format.
    /// </summary>
    /// <param name="utf8Json">The JSON text, UTF-8.</param>
    /// <param name="computedStyleNames">
    /// The <c>computedStyles</c> list the capture requested, for a bare result. Ignored when the input carries its own list.
    /// </param>
    /// <param name="limits">Bounds; <see langword="null"/> means <see cref="DomSnapshotLimits.Default"/>.</param>
    /// <exception cref="DomSnapshotFormatException">The JSON is malformed, inconsistent, of another format version, or over a limit.</exception>
    public static DomSnapshot Parse(ReadOnlySpan<byte> utf8Json, IReadOnlyList<string>? computedStyleNames = null, DomSnapshotLimits? limits = null) =>
        DomSnapshotReader.Read(utf8Json, computedStyleNames, limits ?? DomSnapshotLimits.Default);
}

/// <summary>One document of a <see cref="DomSnapshot"/>: a flat, pre-order table of DOM nodes plus layout and text boxes.</summary>
public sealed class DomSnapshotDocument
{
    private readonly DomSnapshot _owner;

    internal DomSnapshotDocument(DomSnapshot owner, DomSnapshotDocumentData data)
    {
        _owner = owner;
        Data = data;
    }

    internal DomSnapshotDocumentData Data { get; }

    /// <summary>Number of DOM nodes (the table is in document order: parents before children).</summary>
    public int NodeCount => Data.NodeType.Length;

    /// <summary>Number of layout items.</summary>
    public int LayoutCount => Data.LayoutNodeIndex.Length;

    /// <summary>Number of text boxes.</summary>
    public int TextBoxCount => Data.TextBoxLayoutIndex.Length;

    /// <summary>The document URL, if reported.</summary>
    public string? DocumentUrl => _owner.StrOrNull(Data.DocumentUrl);

    /// <summary>The base URL, if reported.</summary>
    public string? BaseUrl => _owner.StrOrNull(Data.BaseUrl);

    /// <summary>The title, if reported.</summary>
    public string? Title => _owner.StrOrNull(Data.Title);

    /// <summary>The frame id of the document, if reported.</summary>
    public string? FrameId => _owner.StrOrNull(Data.FrameId);

    /// <summary>The content language, if reported.</summary>
    public string? ContentLanguage => _owner.StrOrNull(Data.ContentLanguage);

    /// <summary>The encoding name, if reported.</summary>
    public string? EncodingName => _owner.StrOrNull(Data.EncodingName);

    /// <summary>Document content width in CSS pixels; <see cref="double.NaN"/> when not reported.</summary>
    public double ContentWidth => Data.ContentWidth;

    /// <summary>Document content height in CSS pixels; <see cref="double.NaN"/> when not reported.</summary>
    public double ContentHeight => Data.ContentHeight;

    /// <summary>Horizontal scroll offset; <see cref="double.NaN"/> when not reported.</summary>
    public double ScrollOffsetX => Data.ScrollOffsetX;

    /// <summary>Vertical scroll offset; <see cref="double.NaN"/> when not reported.</summary>
    public double ScrollOffsetY => Data.ScrollOffsetY;

    /// <summary>The DOM <c>nodeType</c> of a node (1 element, 3 text, 8 comment, 9 document, 10 doctype, 11 fragment).</summary>
    /// <param name="node">The node index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="node"/> is out of range.</exception>
    public int NodeType(int node) => Data.NodeType[Check(node)];

    /// <summary>The <c>nodeName</c> of a node.</summary>
    /// <param name="node">The node index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="node"/> is out of range.</exception>
    public string NodeName(int node) => _owner.Str(Data.NodeName[Check(node)]);

    /// <summary>The index of a node's parent, or -1 for the root.</summary>
    /// <param name="node">The node index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="node"/> is out of range.</exception>
    public int ParentIndex(int node) => Data.ParentIndex[Check(node)];

    /// <summary>The backend node id of a node, or -1 when not reported.</summary>
    /// <param name="node">The node index.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="node"/> is out of range.</exception>
    public long BackendNodeId(int node) => Data.BackendNodeId.Length == 0 ? -1 : Data.BackendNodeId[Check(node)];

    private int Check(int node)
    {
        if ((uint)node >= (uint)NodeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(node));
        }

        return node;
    }
}

/// <summary>Sparse per-node data (<c>RareStringData</c>, <c>RareIntegerData</c>, <c>RareBooleanData</c>) as parallel arrays.</summary>
internal readonly struct Rare(int[] index, int[] value)
{
    public readonly int[] Index = index;

    public readonly int[] Value = value;

    public static Rare Empty => new([], []);
}

/// <summary>The raw arrays of one document, as read from the JSON and validated.</summary>
internal sealed class DomSnapshotDocumentData
{
    public int DocumentUrl = -1, BaseUrl = -1, Title = -1, FrameId = -1, ContentLanguage = -1, EncodingName = -1, PublicId = -1, SystemId = -1;

    public double ContentWidth = double.NaN, ContentHeight = double.NaN, ScrollOffsetX = double.NaN, ScrollOffsetY = double.NaN;

    // nodes
    public int[] NodeType = [], ParentIndex = [], NodeName = [], NodeValue = [];

    public long[] BackendNodeId = [];

    public int[] AttrData = [], AttrOffsets = [0];

    public Rare ContentDocumentIndex = Rare.Empty, ShadowRootType = Rare.Empty, PseudoType = Rare.Empty, PseudoIdentifier = Rare.Empty,
        InputValue = Rare.Empty, TextValue = Rare.Empty, InputChecked = Rare.Empty, OptionSelected = Rare.Empty, IsClickable = Rare.Empty,
        CurrentSourceUrl = Rare.Empty, OriginUrl = Rare.Empty;

    // layout (rectangles are 4 doubles per item; NaN when the rectangle was empty or absent)
    public int[] LayoutNodeIndex = [];

    public double[] Bounds = [], ClientRects = [], OffsetRects = [], ScrollRects = [], TextColorOpacities = [];

    public int[] StyleData = [], StyleOffsets = [0], LayoutText = [], PaintOrders = [], BlendedBackground = [], StackingContexts = [];

    // text boxes
    public double[] TextBoxBounds = [];

    public int[] TextBoxStart = [], TextBoxLength = [], TextBoxLayoutIndex = [];
}
