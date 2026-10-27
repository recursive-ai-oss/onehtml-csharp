using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Rendered;

/// <summary>
/// Bounds on what a <see cref="RenderedFactsBuilder"/> accepts. Each is checked before the data it guards is stored, so a runaway
/// caller fails with a <see cref="RenderedFactsLimitException"/> instead of exhausting memory. There is no byte limit: nothing is parsed.
/// </summary>
public sealed record RenderedFactsLimits
{
    /// <summary>The defaults.</summary>
    public static RenderedFactsLimits Default { get; } = new();

    /// <summary>Most boxes, generated ones included. Default 8 Mi, the adapter's layout-item limit.</summary>
    public int MaxBoxes { get; init; } = 8 * 1024 * 1024;

    /// <summary>Most text boxes over all boxes. Default 16 Mi.</summary>
    public int MaxTextBoxes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Most distinct strings (layout text, blended colors, style values, form values, pseudo-element text). Default 16 Mi.</summary>
    public int MaxStrings { get; init; } = 16 * 1024 * 1024;

    /// <summary>Largest total length of all distinct strings, in UTF-16 code units. Default 256 Mi.</summary>
    public long MaxStringChars { get; init; } = 256L * 1024 * 1024;

    /// <summary>Most computed-style names. Default 1024.</summary>
    public int MaxComputedStyleNames { get; init; } = 1024;
}

/// <summary>A <see cref="RenderedFactsBuilder"/> was given more than its <see cref="RenderedFactsLimits"/> allow.</summary>
public sealed class RenderedFactsLimitException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="limitName">The <see cref="RenderedFactsLimits"/> property that was exceeded.</param>
    /// <param name="limit">The limit.</param>
    /// <param name="actual">The count the call would have reached.</param>
    public RenderedFactsLimitException(string limitName, long limit, long actual)
        : base($"The facts exceed the {limitName} limit of {limit} (would reach {actual}).")
    {
        LimitName = limitName;
        Limit = limit;
        Actual = actual;
    }

    /// <summary>The <see cref="RenderedFactsLimits"/> property that was exceeded.</summary>
    public string LimitName { get; }

    /// <summary>The limit.</summary>
    public long Limit { get; }

    /// <summary>The count the call would have reached.</summary>
    public long Actual { get; }
}

/// <summary>
/// The layout facts of one box, as a caller that already has them in memory supplies them. Every member matches the same-named member of
/// <see cref="BoxFacts"/>; an unset member reads as absent.
/// </summary>
public sealed record BoxInput
{
    /// <summary>The border box in document coordinates, CSS pixels. Null reads as a rectangle of NaN. Components must not be infinite.</summary>
    public LayoutRect? BorderBox { get; init; }

    /// <summary>The offset rectangle, or null.</summary>
    public LayoutRect? OffsetRect { get; init; }

    /// <summary>The client rectangle, or null.</summary>
    public LayoutRect? ClientRect { get; init; }

    /// <summary>The scroll rectangle, or null.</summary>
    public LayoutRect? ScrollRect { get; init; }

    /// <summary>The global paint order index (0 or more), or null. Larger paints later.</summary>
    public int? PaintOrder { get; init; }

    /// <summary>Whether the box begins a stacking context. Default false.</summary>
    public bool IsStackingContext { get; init; }

    /// <summary>The rendered text of a text box's layout object, or null.</summary>
    public string? LayoutText { get; init; }

    /// <summary>The rendered text runs, in paint order; null or empty for none. Starts and lengths are 0 or more.</summary>
    public IReadOnlyList<TextBox>? TextBoxes { get; init; }

    /// <summary>The blended background color as Chrome writes it (<c>rgb(0, 0, 0)</c>), or null.</summary>
    public string? BlendedBackgroundColor { get; init; }

    /// <summary>The computed text opacity (finite), or null.</summary>
    public double? TextColorOpacity { get; init; }

    /// <summary>
    /// The computed style values, one per name given to <see cref="RenderedFactsBuilder.SetComputedStyleNames"/>, in that order (a null entry
    /// is a property with no value); null for a box with none. A different length is an <see cref="ArgumentException"/>.
    /// </summary>
    public IReadOnlyList<string?>? Styles { get; init; }
}

/// <summary>The boxes of one generated pseudo-element (<c>::before</c>, <c>::marker</c>, ...) on a host node.</summary>
public sealed record GeneratedBoxInput
{
    /// <summary>Creates the input.</summary>
    /// <param name="pseudoType">The pseudo-element type as <see cref="GeneratedBox.PseudoType"/> reports it (<c>before</c>, <c>marker</c>, ...).</param>
    public GeneratedBoxInput(string pseudoType)
    {
        ArgumentNullException.ThrowIfNull(pseudoType);
        PseudoType = pseudoType;
    }

    /// <summary>The pseudo-element type, for example <c>before</c>.</summary>
    public string PseudoType { get; }

    /// <summary>The pseudo-element's identifier (for <c>::view-transition-group</c> and similar), or null.</summary>
    public string? Identifier { get; init; }

    /// <summary>The text its layout produced, or null when none.</summary>
    public string? Text { get; init; }

    /// <summary>How many nodes the pseudo-element's subtree has (0 or more). Default 0.</summary>
    public int NodeCount { get; init; }

    /// <summary>Every box of the subtree, the root's included, in the order to report them.</summary>
    public IReadOnlyList<BoxInput>? Boxes { get; init; }

    /// <summary>The index in <see cref="Boxes"/> of the pseudo-element's own box, or -1 for none (the default).</summary>
    public int LayoutIndex { get; init; } = -1;
}

/// <summary>
/// Builds <see cref="RenderedFacts"/> directly from per-node data, for a caller that already holds layout results in memory (a renderer, or a
/// parsed capture) and has no use for a <c>DOMSnapshot.captureSnapshot</c> document. The result is the same kind of object the
/// <see cref="DomSnapshotAdapter"/> returns and every consumer treats it alike.
/// </summary>
/// <remarks>
/// <para>The checks match the adapter's: a node must belong to the builder's document (a different document, or a null handle, is an
/// <see cref="ArgumentException"/>), at most one box per node, style values as long as the style-name list, finite numbers, and every count and
/// string size bounded by <see cref="RenderedFactsLimits"/> before it is stored. Nothing is copied from the caller's collections after the call
/// returns, so they may be reused. The viewport and <see cref="StyleContext"/> are recorded as given, as in <see cref="DomSnapshotMapOptions"/>.</para>
/// <para>The structure fingerprint is computed from the document, so the facts carry the same one the adapter would record. The stacking context
/// of every node is derived from the boxes and the document's tree. A builder is not thread-safe; the facts it builds are.</para>
/// </remarks>
public sealed class RenderedFactsBuilder
{
    private readonly Document _document;
    private readonly RenderedFactsLimits _limits;
    private readonly int _nodeCount;

    private readonly Dictionary<string, int> _stringIndex = new(StringComparer.Ordinal);
    private readonly List<string> _strings = [];
    private long _stringChars;

    private string[] _styleNames = [];
    private bool _boxesAdded;

    // per node
    private readonly int[] _nodeBox;
    private readonly int[] _nodeSnapshot;
    private readonly long[] _nodeBackend;
    private readonly byte[] _flags;
    private readonly Dictionary<int, int> _inputValue = [];
    private readonly Dictionary<int, int> _textValue = [];
    private readonly Dictionary<int, int> _frames = [];
    private Dictionary<int, BackgroundSample>? _samples;

    // per box
    private readonly List<double> _bounds = [], _client = [], _offset = [], _scroll = [], _textOpacity = [];
    private bool _hasClient, _hasOffset, _hasScroll;
    private readonly List<int> _paint = [], _text = [], _blended = [], _styleValues = [], _tbStart = [0];
    private readonly List<byte> _stacking = [];
    private readonly List<TextBox> _textBoxes = [];

    private readonly List<(int Host, int Type, int Ident, int Box, int Text, int Nodes, int[] Boxes)> _generated = [];

    private string? _product, _protocolVersion;
    private double _viewportWidth, _viewportHeight;
    private StyleContext? _context;
    private PageFacts _page = new(null, null, null, null, null, null, double.NaN, double.NaN, double.NaN, double.NaN);

    /// <summary>Creates a builder for facts about <paramref name="document"/>.</summary>
    /// <param name="document">The document the facts describe. Nodes are matched by identity: handles from another parse are refused.</param>
    /// <param name="limits">The bounds; null for <see cref="RenderedFactsLimits.Default"/>.</param>
    public RenderedFactsBuilder(Document document, RenderedFactsLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        _limits = limits ?? RenderedFactsLimits.Default;
        _nodeCount = document.NodeCount;
        _nodeBox = new int[_nodeCount];
        _nodeSnapshot = new int[_nodeCount];
        _nodeBackend = new long[_nodeCount];
        _flags = new byte[_nodeCount];
        Array.Fill(_nodeBox, -1);
        Array.Fill(_nodeSnapshot, -1);
        Array.Fill(_nodeBackend, -1L);
    }

    /// <summary>The document the facts describe.</summary>
    public Document Document => _document;

    /// <summary>The number of boxes added so far, generated ones included.</summary>
    public int BoxCount => _paint.Count;

    /// <summary>Records the viewport the layout was computed for, in CSS pixels, as <see cref="DomSnapshotMapOptions.ViewportWidth"/> does. Default 0 (unknown).</summary>
    /// <param name="width">Width, 0 or more and finite.</param>
    /// <param name="height">Height, 0 or more and finite.</param>
    public void SetViewport(double width, double height)
    {
        if (!double.IsFinite(width) || width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The viewport width must be finite and not negative.");
        }

        if (!double.IsFinite(height) || height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "The viewport height must be finite and not negative.");
        }

        _viewportWidth = width;
        _viewportHeight = height;
    }

    /// <summary>Records the style context the layout was computed under (<see cref="RenderedFacts.Context"/>). Default none.</summary>
    /// <param name="context">The context, or null.</param>
    public void SetContext(StyleContext? context) => _context = context;

    /// <summary>Records page-level facts (<see cref="RenderedFacts.Page"/>). Default: nothing known, sizes NaN.</summary>
    /// <param name="page">The page facts.</param>
    public void SetPage(PageFacts page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _page = page;
    }

    /// <summary>Records where the facts came from (<see cref="RenderedFacts.Product"/>, <see cref="RenderedFacts.ProtocolVersion"/>). Default null.</summary>
    /// <param name="product">A free-text product name, or null.</param>
    /// <param name="protocolVersion">A free-text version, or null.</param>
    public void SetSource(string? product, string? protocolVersion)
    {
        _product = product;
        _protocolVersion = protocolVersion;
    }

    /// <summary>
    /// Sets the names of the computed styles that <see cref="BoxInput.Styles"/> carries, once, before the first box. Default none.
    /// </summary>
    /// <param name="names">The names, at most <see cref="RenderedFactsLimits.MaxComputedStyleNames"/>; a later duplicate shadows an earlier one, as in the adapter.</param>
    /// <exception cref="InvalidOperationException">A box was already added.</exception>
    public void SetComputedStyleNames(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (_boxesAdded)
        {
            throw new InvalidOperationException("The computed style names must be set before the first box is added.");
        }

        if (names.Count > _limits.MaxComputedStyleNames)
        {
            throw new RenderedFactsLimitException(nameof(RenderedFactsLimits.MaxComputedStyleNames), _limits.MaxComputedStyleNames, names.Count);
        }

        var copy = new string[names.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = names[i] ?? throw new ArgumentException("A style name is null.", nameof(names));
        }

        _styleNames = copy;
    }

    /// <summary>Adds the box of a node (<see cref="NodeFacts.Layout"/>).</summary>
    /// <param name="node">A node of the builder's document.</param>
    /// <param name="box">Its layout facts.</param>
    /// <exception cref="ArgumentException">The node is null or from another document, it already has a box, or <paramref name="box"/> is malformed.</exception>
    /// <exception cref="RenderedFactsLimitException">A limit would be exceeded.</exception>
    public void AddBox(Node node, BoxInput box)
    {
        ArgumentNullException.ThrowIfNull(box);
        int index = Check(node, nameof(node));
        if (_nodeBox[index] >= 0)
        {
            throw new ArgumentException("The node already has a box.", nameof(node));
        }

        _nodeBox[index] = AppendBox(box);
    }

    /// <summary>Adds a generated pseudo-element of a host node (<see cref="NodeFacts.GeneratedBoxes"/>). Several may share a host; they are reported in the order added.</summary>
    /// <param name="host">The node the pseudo-element belongs to.</param>
    /// <param name="generated">Its facts.</param>
    /// <exception cref="ArgumentException">The host is null or from another document, or an input is malformed.</exception>
    /// <exception cref="RenderedFactsLimitException">A limit would be exceeded.</exception>
    public void AddGeneratedContent(Node host, GeneratedBoxInput generated)
    {
        ArgumentNullException.ThrowIfNull(generated);
        int hostIndex = Check(host, nameof(host));
        IReadOnlyList<BoxInput> boxes = generated.Boxes ?? [];
        if (generated.NodeCount < 0)
        {
            throw new ArgumentException("The node count is negative.", nameof(generated));
        }

        if (generated.LayoutIndex < -1 || generated.LayoutIndex >= boxes.Count)
        {
            throw new ArgumentException("LayoutIndex is outside Boxes.", nameof(generated));
        }

        if (_paint.Count + (long)boxes.Count > _limits.MaxBoxes)
        {
            throw new RenderedFactsLimitException(nameof(RenderedFactsLimits.MaxBoxes), _limits.MaxBoxes, _paint.Count + (long)boxes.Count);
        }

        int type = Intern(generated.PseudoType);
        int ident = generated.Identifier is null ? -1 : Intern(generated.Identifier);
        int text = generated.Text is null ? -1 : Intern(generated.Text);
        var ids = new int[boxes.Count];
        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = AppendBox(boxes[i] ?? throw new ArgumentException("A box is null.", nameof(generated)));
        }

        _generated.Add((hostIndex, type, ident, generated.LayoutIndex < 0 ? -1 : ids[generated.LayoutIndex], text, generated.NodeCount, ids));
    }

    /// <summary>Records the browser's backend node id of a node (<see cref="NodeFacts.BackendNodeId"/>). Default -1.</summary>
    /// <param name="node">A node of the builder's document.</param>
    /// <param name="backendNodeId">The id, or -1 for none.</param>
    public void SetBackendNodeId(Node node, long backendNodeId) => _nodeBackend[Check(node, nameof(node))] = backendNodeId < -1 ? -1 : backendNodeId;

    /// <summary>Records the node's index in a source snapshot (<see cref="NodeFacts.SnapshotNodeIndex"/>), used to match background samples. Default -1.</summary>
    /// <param name="node">A node of the builder's document.</param>
    /// <param name="snapshotNodeIndex">The index, or -1 for none.</param>
    public void SetSnapshotNodeIndex(Node node, int snapshotNodeIndex) => _nodeSnapshot[Check(node, nameof(node))] = snapshotNodeIndex < -1 ? -1 : snapshotNodeIndex;

    /// <summary>Records the current value of a form control (<see cref="NodeFacts.InputValue"/>).</summary>
    /// <param name="node">A node of the builder's document.</param>
    /// <param name="value">The value.</param>
    public void SetInputValue(Node node, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int i = Check(node, nameof(node));
        _inputValue[i] = Intern(value);
    }

    /// <summary>Records the current text of a text area or similar (<see cref="NodeFacts.TextValue"/>).</summary>
    /// <param name="node">A node of the builder's document.</param>
    /// <param name="value">The value.</param>
    public void SetTextValue(Node node, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int i = Check(node, nameof(node));
        _textValue[i] = Intern(value);
    }

    /// <summary>Records that a control is checked (<see cref="NodeFacts.InputChecked"/>). Every node starts false.</summary>
    /// <param name="node">A node of the builder's document.</param>
    /// <param name="value">The state.</param>
    public void SetChecked(Node node, bool value) => SetFlag(Check(node, nameof(node)), FactsData.FlagChecked, value);

    /// <summary>Records that an option is selected (<see cref="NodeFacts.OptionSelected"/>). Every node starts false.</summary>
    /// <param name="node">A node of the builder's document.</param>
    /// <param name="value">The state.</param>
    public void SetOptionSelected(Node node, bool value) => SetFlag(Check(node, nameof(node)), FactsData.FlagSelected, value);

    /// <summary>Records that a node has a click listener or is clickable (<see cref="NodeFacts.IsClickable"/>). Every node starts false.</summary>
    /// <param name="node">A node of the builder's document.</param>
    /// <param name="value">The state.</param>
    public void SetClickable(Node node, bool value) => SetFlag(Check(node, nameof(node)), FactsData.FlagClickable, value);

    /// <summary>Links a frame element to the index of its content document (<see cref="RenderedFacts.Frames"/>).</summary>
    /// <param name="node">The frame element, a node of the builder's document.</param>
    /// <param name="contentDocumentIndex">The index of the content document in the caller's own list (0 or more).</param>
    public void SetFrame(Node node, int contentDocumentIndex)
    {
        if (contentDocumentIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contentDocumentIndex), "The content document index is negative.");
        }

        _frames[Check(node, nameof(node))] = contentDocumentIndex;
    }

    /// <summary>Records a background sample for a node (<see cref="NodeFacts.Background"/>), as <see cref="RenderedFacts.WithBackgroundSamples"/> does for a sample file.</summary>
    /// <param name="node">A node of the builder's document.</param>
    /// <param name="sample">The sample.</param>
    public void SetBackgroundSample(Node node, BackgroundSample sample) => (_samples ??= [])[Check(node, nameof(node))] = sample;

    /// <summary>
    /// Builds the facts. The builder stays usable: it can take more data and build again, and each result is independent.
    /// </summary>
    /// <returns>The facts.</returns>
    public RenderedFacts Build()
    {
        int boxes = _paint.Count;
        int stride = _styleNames.Length;
        var f = new FactsData
        {
            DocumentId = _document.Id,
            NodeCount = _nodeCount,
            Fingerprint = DomSnapshotAdapter.StructureFingerprint(_document),
            Product = _product,
            ProtocolVersion = _protocolVersion,
            ViewportWidth = _viewportWidth,
            ViewportHeight = _viewportHeight,
            Context = _context,
            Page = _page,
            NodeBox = (int[])_nodeBox.Clone(),
            NodeSnapshot = (int[])_nodeSnapshot.Clone(),
            NodeBackend = (long[])_nodeBackend.Clone(),
            Flags = (byte[])_flags.Clone(),
            StyleNames = (string[])_styleNames.Clone(),
            StyleStride = stride,
            BoxCount = boxes,
            Bounds = [.. _bounds],
            Client = _hasClient ? [.. _client] : [],
            Offset = _hasOffset ? [.. _offset] : [],
            Scroll = _hasScroll ? [.. _scroll] : [],
            TextOpacity = [.. _textOpacity],
            Paint = [.. _paint],
            Text = [.. _text],
            Blended = [.. _blended],
            StyleValues = stride == 0 ? [] : [.. _styleValues],
            TbStart = [.. _tbStart],
            Stacking = [.. _stacking],
            TextBoxes = [.. _textBoxes],
            Strings = [.. _strings],
            InputValue = new Dictionary<int, int>(_inputValue),
            TextValue = new Dictionary<int, int>(_textValue),
            Samples = _samples is null ? null : new Dictionary<int, BackgroundSample>(_samples),
        };
        for (int i = 0; i < stride; i++)
        {
            f.StyleIndex[_styleNames[i]] = i;
        }

        // Generated boxes ordered by host, then by the order added (a stable sort on the host).
        int gc = _generated.Count;
        var order = new int[gc];
        for (int i = 0; i < gc; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => _generated[a].Host != _generated[b].Host ? _generated[a].Host.CompareTo(_generated[b].Host) : a.CompareTo(b));
        f.GenHost = new int[gc];
        f.GenType = new int[gc];
        f.GenIdent = new int[gc];
        f.GenBox = new int[gc];
        f.GenText = new int[gc];
        f.GenNodes = new int[gc];
        f.GenBoxes = new int[gc][];
        for (int k = 0; k < gc; k++)
        {
            var g = _generated[order[k]];
            f.GenHost[k] = g.Host;
            f.GenType[k] = g.Type;
            f.GenIdent[k] = g.Ident;
            f.GenBox[k] = g.Box;
            f.GenText[k] = g.Text;
            f.GenNodes[k] = g.Nodes;
            f.GenBoxes[k] = g.Boxes;
        }

        var frames = new List<FrameLink>(_frames.Count);
        foreach (KeyValuePair<int, int> kv in _frames)
        {
            frames.Add(new FrameLink(kv.Key, kv.Value));
        }

        frames.Sort(static (x, y) => x.NodeIndex.CompareTo(y.NodeIndex));
        f.Frames = [.. frames];

        f.NodeStackRoot = StackingRoots(f);
        return new RenderedFacts(f);
    }

    // The nearest ancestor-or-self whose box begins a stacking context, for every node. Iterative: a deep document must not recurse.
    private int[] StackingRoots(FactsData f)
    {
        var root = new int[_nodeCount];
        var known = new bool[_nodeCount];
        var chain = new List<int>();
        for (int i = 0; i < _nodeCount; i++)
        {
            if (known[i])
            {
                continue;
            }

            chain.Clear();
            int cur = i;
            int above = -1;
            while (true)
            {
                if (known[cur])
                {
                    above = root[cur];
                    break;
                }

                chain.Add(cur);
                Node parent = _document.GetNode(cur).Parent;
                if (parent.IsNull)
                {
                    break;
                }

                cur = parent.Index;
            }

            for (int k = chain.Count - 1; k >= 0; k--)
            {
                int n = chain[k];
                int b = f.NodeBox[n];
                above = b >= 0 && f.Stacking[b] != 0 ? n : above;
                root[n] = above;
                known[n] = true;
            }
        }

        return root;
    }

    private int Check(Node node, string parameter)
    {
        if (node.Document is not { } doc || doc.Id != _document.Id || node.Index < 0 || node.Index >= _nodeCount)
        {
            throw new ArgumentException("The node is null or belongs to a different document than these facts.", parameter);
        }

        return node.Index;
    }

    private void SetFlag(int index, byte flag, bool value) => _flags[index] = value ? (byte)(_flags[index] | flag) : (byte)(_flags[index] & ~flag);

    private int Intern(string text)
    {
        if (_stringIndex.TryGetValue(text, out int i))
        {
            return i;
        }

        if (_strings.Count >= _limits.MaxStrings)
        {
            throw new RenderedFactsLimitException(nameof(RenderedFactsLimits.MaxStrings), _limits.MaxStrings, _strings.Count + 1L);
        }

        if (_stringChars + text.Length > _limits.MaxStringChars)
        {
            throw new RenderedFactsLimitException(nameof(RenderedFactsLimits.MaxStringChars), _limits.MaxStringChars, _stringChars + text.Length);
        }

        _stringChars += text.Length;
        _strings.Add(text);
        _stringIndex[text] = _strings.Count - 1;
        return _strings.Count - 1;
    }

    private static void CheckRect(LayoutRect r, string what)
    {
        if (double.IsInfinity(r.X) || double.IsInfinity(r.Y) || double.IsInfinity(r.Width) || double.IsInfinity(r.Height))
        {
            throw new ArgumentException($"The {what} has an infinite component.");
        }
    }

    private static void Put(List<double> list, LayoutRect? r)
    {
        LayoutRect v = r ?? new LayoutRect(double.NaN, double.NaN, double.NaN, double.NaN);
        list.Add(v.X);
        list.Add(v.Y);
        list.Add(v.Width);
        list.Add(v.Height);
    }

    // Validates one box completely before the first column grows, so a refused box leaves no trace. (The boxes of one generated
    // pseudo-element are added one by one; if a later one is refused, the earlier ones stay as boxes no node points to.)
    private int AppendBox(BoxInput box)
    {
        if (_paint.Count >= _limits.MaxBoxes)
        {
            throw new RenderedFactsLimitException(nameof(RenderedFactsLimits.MaxBoxes), _limits.MaxBoxes, _paint.Count + 1L);
        }

        int stride = _styleNames.Length;
        if (box.Styles is { } styles && styles.Count != stride)
        {
            throw new ArgumentException($"The box has {styles.Count} style values; {stride} style names were set.", nameof(box));
        }

        foreach (LayoutRect? r in new[] { box.BorderBox, box.ClientRect, box.OffsetRect, box.ScrollRect })
        {
            if (r is { } rect)
            {
                CheckRect(rect, "rectangle");
            }
        }

        if (box.PaintOrder is < 0)
        {
            throw new ArgumentException("The paint order is negative.", nameof(box));
        }

        if (box.TextColorOpacity is { } op && !double.IsFinite(op))
        {
            throw new ArgumentException("The text opacity is not finite.", nameof(box));
        }

        int tbCount = box.TextBoxes?.Count ?? 0;
        if (_textBoxes.Count + (long)tbCount > _limits.MaxTextBoxes)
        {
            throw new RenderedFactsLimitException(nameof(RenderedFactsLimits.MaxTextBoxes), _limits.MaxTextBoxes, _textBoxes.Count + (long)tbCount);
        }

        if (box.TextBoxes is { } tbs)
        {
            foreach (TextBox t in tbs)
            {
                if (t.Start < 0 || t.Length < 0 || (long)t.Start + t.Length > int.MaxValue)
                {
                    throw new ArgumentException("A text box has a negative or overflowing range.", nameof(box));
                }

                CheckRect(t.Bounds, "text box");
            }
        }

        // Interning can throw a limit exception half way; strings it already added are harmless extras.
        int text = box.LayoutText is null ? -1 : Intern(box.LayoutText);
        int blended = box.BlendedBackgroundColor is null ? -1 : Intern(box.BlendedBackgroundColor);
        int[] style = [];
        if (box.Styles is { } sv)
        {
            style = new int[sv.Count];
            for (int i = 0; i < style.Length; i++)
            {
                style[i] = sv[i] is null ? -1 : Intern(sv[i]!);
            }
        }

        int id = _paint.Count;
        _boxesAdded = true;
        Put(_bounds, box.BorderBox);
        Put(_client, box.ClientRect);
        Put(_offset, box.OffsetRect);
        Put(_scroll, box.ScrollRect);
        _hasClient |= box.ClientRect is not null;
        _hasOffset |= box.OffsetRect is not null;
        _hasScroll |= box.ScrollRect is not null;
        _textOpacity.Add(box.TextColorOpacity ?? double.NaN);
        _paint.Add(box.PaintOrder ?? -1);
        _text.Add(text);
        _blended.Add(blended);
        _stacking.Add(box.IsStackingContext ? (byte)1 : (byte)0);
        if (stride > 0)
        {
            if (style.Length == 0)
            {
                for (int i = 0; i < stride; i++)
                {
                    _styleValues.Add(-1);
                }
            }
            else
            {
                _styleValues.AddRange(style);
            }
        }

        if (box.TextBoxes is { } list)
        {
            foreach (TextBox t in list)
            {
                _textBoxes.Add(t);
            }
        }

        _tbStart.Add(_textBoxes.Count);
        return id;
    }
}
