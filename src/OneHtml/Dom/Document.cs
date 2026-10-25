using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using RecursiveAI.OneHtml.Dom;
using RecursiveAI.OneHtml.Encodings;
using RecursiveAI.OneHtml.Parsing;

namespace RecursiveAI.OneHtml;

/// <summary>Options for parsing a document or a fragment.</summary>
public sealed class ParseOptions
{
    /// <summary>
    /// Whether scripting counts as enabled for the parser (the "scripting flag"). It decides how <c>noscript</c> is
    /// parsed: as raw text when enabled, as markup when disabled. The library never runs script; the default is
    /// <see langword="false"/>, which keeps the content of <c>noscript</c> in the tree.
    /// </summary>
    public bool ScriptingEnabled { get; init; }

    /// <summary>
    /// Whether to keep the tokenizer's parse errors in <see cref="Document.ParseErrors"/>. Default <see langword="false"/>.
    /// Only tokenizer errors are recorded; tree construction errors are not.
    /// </summary>
    public bool RecordErrors { get; init; }

    /// <summary>The input and tree bounds. Default <see cref="ParseLimits.Default"/>.</summary>
    public ParseLimits? Limits { get; init; }

    /// <summary>The transport <c>Content-Type</c> header value, for parsing bytes. Default <see langword="null"/>.</summary>
    public string? ContentType { get; init; }

    /// <summary>The encoding used for bytes when nothing else decides. Default windows-1252.</summary>
    public WhatwgEncoding? DefaultEncoding { get; init; }

    /// <summary>An encoding the caller insists on for bytes; a byte order mark still wins. Default <see langword="null"/>.</summary>
    public WhatwgEncoding? OverrideEncoding { get; init; }

    /// <summary>How many bytes the <c>&lt;meta&gt;</c> prescan reads. Default 1024.</summary>
    public int PrescanLimit { get; init; } = EncodingSniffer.DefaultPrescanLimit;
}

/// <summary>
/// The element a fragment is parsed in (HTML 13.4): it decides the tokenizer state and the insertion mode the
/// fragment starts in.
/// </summary>
public sealed class FragmentContext
{
    /// <summary>Describes a context element.</summary>
    /// <param name="ns">The element's namespace.</param>
    /// <param name="localName">Its local name, as the parser would have left it (lowercase for HTML).</param>
    /// <param name="insideForm">True when the element is, or has an ancestor that is, a <c>form</c> element.</param>
    /// <param name="isHtmlIntegrationPoint">
    /// True when the element is a MathML <c>annotation-xml</c> whose <c>encoding</c> attribute is <c>text/html</c> or
    /// <c>application/xhtml+xml</c> (ignoring case). SVG <c>foreignObject</c>, <c>desc</c> and <c>title</c> are
    /// recognised by name.
    /// </param>
    /// <param name="quirksMode">The mode of the document the element is in.</param>
    public FragmentContext(
        ElementNamespace ns,
        string localName,
        bool insideForm = false,
        bool isHtmlIntegrationPoint = false,
        QuirksMode quirksMode = QuirksMode.NoQuirks)
    {
        ArgumentNullException.ThrowIfNull(localName);
        Namespace = ns;
        LocalName = localName;
        InsideForm = insideForm;
        IsHtmlIntegrationPoint = isHtmlIntegrationPoint;
        QuirksMode = quirksMode;
    }

    /// <summary>The element's namespace.</summary>
    public ElementNamespace Namespace { get; }

    /// <summary>The element's local name.</summary>
    public string LocalName { get; }

    /// <summary>Whether a <c>form</c> element is the element or one of its ancestors.</summary>
    public bool InsideForm { get; }

    /// <summary>Whether the element is a MathML <c>annotation-xml</c> that is an HTML integration point.</summary>
    public bool IsHtmlIntegrationPoint { get; }

    /// <summary>The mode of the document the element is in.</summary>
    public QuirksMode QuirksMode { get; }

    /// <summary>Describes an element of a parsed document as a fragment context.</summary>
    /// <param name="element">An element.</param>
    /// <exception cref="ArgumentException"><paramref name="element"/> is not an element.</exception>
    public static FragmentContext FromElement(Node element)
    {
        if (!element.IsElement)
        {
            throw new ArgumentException("The context must be an element.", nameof(element));
        }

        bool inForm = false;
        for (Node n = element; !n.IsNull; n = n.Parent)
        {
            if (n.IsElement && n.Namespace == ElementNamespace.Html && n.LocalName == "form")
            {
                inForm = true;
                break;
            }
        }

        bool ip = element.Namespace == ElementNamespace.MathMl && element.LocalName == "annotation-xml"
            && element.Document!.IsIntegrationPoint(element.Index);
        return new FragmentContext(element.Namespace, element.LocalName, inForm, ip, element.Document!.Mode);
    }
}

/// <summary>
/// An immutable snapshot of a parsed document or fragment: its nodes, the source they came from and the map between
/// them. Any number of threads can read a snapshot at once.
/// </summary>
/// <remarks>
/// The nodes are columns of integers in one arena, so a document costs a handful of arrays rather than an object per
/// node, and a <see cref="Node"/> handle is a document reference and an index. Text and attribute values are slices
/// of <see cref="Source"/> where the source holds them verbatim.
/// </remarks>
public sealed class Document
{
    private static long s_nextId;

    private readonly IReadOnlyList<ParseError> _parseErrors;

    internal Document(
        Arena arena,
        NameTable names,
        SourceText source,
        QuirksMode mode,
        bool isFragment,
        int documentElement,
        int doctype,
        string publicId,
        string systemId,
        IReadOnlyList<ParseError> parseErrors)
    {
        Arena = arena;
        Names = names;
        Source = source;
        Mode = mode;
        IsFragment = isFragment;
        DocumentElementIndex = documentElement;
        DoctypeIndex = doctype;
        DoctypePublicId = publicId;
        DoctypeSystemId = systemId;
        _parseErrors = parseErrors;
        Id = Interlocked.Increment(ref s_nextId);
    }

    internal Arena Arena { get; }

    internal NameTable Names { get; }

    internal int DocumentElementIndex { get; }

    internal int DoctypeIndex { get; }

    internal string DoctypePublicId { get; }

    internal string DoctypeSystemId { get; }

    /// <summary>
    /// A number unique to this snapshot within the process. A <see cref="Node"/> remembers its document, so handles
    /// into different snapshots never compare equal.
    /// </summary>
    public long Id { get; }

    /// <summary>The decoded text and original bytes this document was parsed from.</summary>
    public SourceText Source { get; }

    /// <summary>The rendering mode the DOCTYPE selected (or quirks mode when there is none).</summary>
    public QuirksMode Mode { get; }

    /// <summary>True when the document came from <see cref="ParseFragment"/>; its root is then a document fragment.</summary>
    public bool IsFragment { get; }

    /// <summary>The number of nodes in the arena, including template content roots and any detached parser scaffolding.</summary>
    public int NodeCount => Arena.Count;

    /// <summary>The root: a <see cref="NodeKind.Document"/>, or a <see cref="NodeKind.DocumentFragment"/> for a fragment.</summary>
    public Node Root => new(this, 0);

    /// <summary>The root element (<c>html</c>), or the null node (always for a fragment).</summary>
    public Node DocumentElement => DocumentElementIndex < 0 ? default : new Node(this, DocumentElementIndex);

    /// <summary>The DOCTYPE node, or the null node.</summary>
    public Node Doctype => DoctypeIndex < 0 ? default : new Node(this, DoctypeIndex);

    /// <summary>The tokenizer's parse errors in source order; empty unless <see cref="ParseOptions.RecordErrors"/> was set.</summary>
    public IReadOnlyList<ParseError> ParseErrors => _parseErrors;

    /// <summary>Gets a node by its index.</summary>
    /// <param name="index">From 0 to <see cref="NodeCount"/> - 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public Node GetNode(int index)
    {
        if ((uint)index >= (uint)Arena.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new Node(this, index);
    }

    /// <summary>
    /// Decodes and parses bytes as a browser would: encoding sniffing, then the HTML parsing algorithm. A late
    /// <c>&lt;meta&gt;</c> that disagrees with a tentative encoding restarts the parse once with the new encoding.
    /// </summary>
    /// <param name="bytes">The document. Kept by reference, not copied.</param>
    /// <param name="options">Options; <see langword="null"/> means all defaults.</param>
    /// <param name="cancellationToken">Checked while decoding and between tokens.</param>
    /// <exception cref="HtmlLimitExceededException">A configured limit was exceeded.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static Document Parse(ReadOnlyMemory<byte> bytes, ParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ParseOptions();
        SourceText source = SourceText.Decode(
            bytes,
            new DecodeOptions
            {
                ContentType = options.ContentType,
                DefaultEncoding = options.DefaultEncoding,
                OverrideEncoding = options.OverrideEncoding,
                PrescanLimit = options.PrescanLimit,
                Limits = options.Limits,
            },
            cancellationToken);
        return Parse(source, options, cancellationToken);
    }

    /// <summary>Parses text (newlines are normalized first). The document has no original bytes.</summary>
    /// <param name="html">The markup.</param>
    /// <param name="options">Options; <see langword="null"/> means all defaults.</param>
    /// <param name="cancellationToken">Checked between tokens.</param>
    /// <exception cref="HtmlLimitExceededException">A configured limit was exceeded.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static Document Parse(string html, ParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        return Parse(SourceText.FromString(html, options?.Limits), options, cancellationToken);
    }

    /// <summary>Parses already decoded text.</summary>
    /// <param name="source">The decoded input.</param>
    /// <param name="options">Options; <see langword="null"/> means all defaults.</param>
    /// <param name="cancellationToken">Checked between tokens.</param>
    /// <exception cref="HtmlLimitExceededException">A configured limit was exceeded.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static Document Parse(SourceText source, ParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Run(source, options ?? new ParseOptions(), null, cancellationToken);
    }

    /// <summary>
    /// Parses a fragment in the context of an element (the HTML fragment parsing algorithm, HTML 13.4). The result's
    /// root is a document fragment holding the parsed nodes; ranges are offsets into the fragment's own text.
    /// </summary>
    /// <param name="html">The markup.</param>
    /// <param name="context">The element the markup is parsed in.</param>
    /// <param name="options">Options; <see langword="null"/> means all defaults. Scripting follows <see cref="ParseOptions.ScriptingEnabled"/>.</param>
    /// <param name="cancellationToken">Checked between tokens.</param>
    /// <exception cref="HtmlLimitExceededException">A configured limit was exceeded.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static Document ParseFragment(string html, FragmentContext context, ParseOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(context);
        return Run(SourceText.FromString(html, options?.Limits), options ?? new ParseOptions(), context, cancellationToken);
    }

    /// <summary>
    /// The HTML fragment serialization algorithm (HTML 13.3): the markup of the children of <paramref name="node"/>
    /// (of its template content, for a <c>template</c>). Void elements give the empty string.
    /// </summary>
    /// <param name="node">A node of this document.</param>
    /// <param name="scriptingEnabled">Whether <c>noscript</c> content is written as raw text. Default <see langword="false"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="node"/> belongs to another document.</exception>
    public string SerializeChildren(Node node, bool scriptingEnabled = false)
    {
        if (!ReferenceEquals(node.Document, this))
        {
            throw new ArgumentException("The node belongs to another document.", nameof(node));
        }

        var sb = new StringBuilder();
        HtmlSerializer.WriteChildren(this, node.Index, scriptingEnabled, sb);
        return sb.ToString();
    }

    /// <summary>The markup of a node and its descendants: the node's own tag, then <see cref="SerializeChildren"/>, then its end tag.</summary>
    /// <param name="node">A node of this document.</param>
    /// <param name="scriptingEnabled">Whether <c>noscript</c> content is written as raw text. Default <see langword="false"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="node"/> belongs to another document.</exception>
    public string SerializeNode(Node node, bool scriptingEnabled = false)
    {
        if (!ReferenceEquals(node.Document, this))
        {
            throw new ArgumentException("The node belongs to another document.", nameof(node));
        }

        var sb = new StringBuilder();
        HtmlSerializer.WriteNode(this, node.Index, scriptingEnabled, sb);
        return sb.ToString();
    }

    /// <summary>
    /// Writes the document exactly as it was given: the original bytes. For a document parsed from a string there are
    /// no bytes, and the normalized text is written as UTF-8.
    /// </summary>
    /// <param name="writer">Where to write.</param>
    public void WriteLossless(IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ReadOnlySpan<byte> bytes = Source.Bytes.Span;
        if (bytes.Length > 0 || Source.Text.Length == 0)
        {
            writer.Write(bytes);
            return;
        }

        int max = Encoding.UTF8.GetMaxByteCount(Source.Text.Length);
        Span<byte> span = writer.GetSpan(max);
        int n = Encoding.UTF8.GetBytes(Source.Text, span);
        writer.Advance(n);
    }

    /// <summary>The bytes <see cref="WriteLossless"/> writes.</summary>
    public byte[] ToLosslessBytes()
    {
        var writer = new ArrayBufferWriter<byte>();
        WriteLossless(writer);
        return writer.WrittenSpan.ToArray();
    }

    /// <summary>Converts a range of text offsets to a range of byte offsets in <see cref="SourceText.Bytes"/>.</summary>
    /// <param name="range">A range from this document, or <see cref="SourceRange.None"/>.</param>
    public SourceRange ToByteRange(SourceRange range) =>
        range.IsPresent ? new SourceRange(Source.ToByteOffset(range.Start), Source.ToByteOffset(range.End)) : range;

    internal bool IsIntegrationPoint(int index) => (Arena.Flags[index] & F.IntegrationPoint) != 0;

    private static Document Run(SourceText source, ParseOptions options, FragmentContext? context, CancellationToken cancellationToken)
    {
        while (true)
        {
            var builder = new TreeBuilder(source, options, context);
            Document? document = builder.Run(cancellationToken, out SourceText? restart);
            if (document is not null)
            {
                return document;
            }

            // A <meta> named another encoding while ours was tentative: start over on the text decoded with it.
            source = restart!;
        }
    }
}
