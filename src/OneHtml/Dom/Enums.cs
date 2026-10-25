using System;

namespace RecursiveAI.OneHtml;

/// <summary>The kind of a node in a <see cref="Document"/>.</summary>
public enum NodeKind : byte
{
    /// <summary>The root of a parsed document. Its children are the DOCTYPE, comments, processing instructions and the document element.</summary>
    Document = 0,

    /// <summary>The root of a parsed fragment, or the content of a <c>template</c> element.</summary>
    DocumentFragment = 1,

    /// <summary>A DOCTYPE. <see cref="Node.LocalName"/> is its name; see <see cref="Node.PublicId"/> and <see cref="Node.SystemId"/>.</summary>
    DocumentType = 2,

    /// <summary>An element.</summary>
    Element = 3,

    /// <summary>A text node.</summary>
    Text = 4,

    /// <summary>A comment.</summary>
    Comment = 5,

    /// <summary>A processing instruction. <see cref="Node.LocalName"/> is its target, <see cref="Node.Data"/> its data.</summary>
    ProcessingInstruction = 6,
}

/// <summary>The namespace of an element.</summary>
public enum ElementNamespace : byte
{
    /// <summary>The HTML namespace, <c>http://www.w3.org/1999/xhtml</c>.</summary>
    Html = 0,

    /// <summary>The MathML namespace, <c>http://www.w3.org/1998/Math/MathML</c>.</summary>
    MathMl = 1,

    /// <summary>The SVG namespace, <c>http://www.w3.org/2000/svg</c>.</summary>
    Svg = 2,
}

/// <summary>The namespace of an attribute. Only attributes the parser adjusts in foreign content have one.</summary>
public enum AttributeNamespace : byte
{
    /// <summary>No namespace.</summary>
    None = 0,

    /// <summary>XLink, <c>http://www.w3.org/1999/xlink</c>: <c>xlink:href</c> and its siblings.</summary>
    XLink = 1,

    /// <summary>XML, <c>http://www.w3.org/XML/1998/namespace</c>: <c>xml:lang</c> and <c>xml:space</c>.</summary>
    Xml = 2,

    /// <summary>XMLNS, <c>http://www.w3.org/2000/xmlns/</c>: <c>xmlns</c> and <c>xmlns:xlink</c>.</summary>
    Xmlns = 3,
}

/// <summary>The rendering mode the DOCTYPE selects (HTML 13.2.6.4.1).</summary>
public enum QuirksMode : byte
{
    /// <summary>Standards mode.</summary>
    NoQuirks = 0,

    /// <summary>Limited-quirks (almost standards) mode.</summary>
    LimitedQuirks = 1,

    /// <summary>Quirks mode.</summary>
    Quirks = 2,
}

/// <summary>How a node relates to the bytes it came from, beyond its ranges. Bits can combine.</summary>
[Flags]
public enum NodeOrigin : ushort
{
    /// <summary>The node sits where its source bytes say it does.</summary>
    None = 0,

    /// <summary>
    /// The element has no start tag in the source: the parser made it (an implied <c>html</c>, <c>head</c>,
    /// <c>body</c>, <c>tbody</c>, <c>tr</c>, <c>colgroup</c>, or the <c>p</c> or <c>br</c> an end tag stands for).
    /// Its start range is empty, at the offset of the token that made it.
    /// </summary>
    ImpliedStartTag = 1,

    /// <summary>
    /// The element is a copy the parser made of another element's start tag (reopened formatting elements and
    /// elements the adoption agency algorithm recreates). Its start range is the original tag's range, so the same
    /// bytes belong to more than one node.
    /// </summary>
    Cloned = 2,

    /// <summary>
    /// The node, or some text it holds, was moved away from where the source puts it: foster parenting, or the
    /// adoption agency algorithm. An edit must not assume the node lies inside its parent's source bytes.
    /// </summary>
    Reparented = 4,

    /// <summary>
    /// Some attributes of this <c>html</c> or <c>body</c> element came from a later duplicate start tag. Their
    /// ranges lie outside this element's own start tag.
    /// </summary>
    AttributesMerged = 8,

    /// <summary>The start tag was written with a trailing solidus, as in <c>&lt;br/&gt;</c> or <c>&lt;svg/&gt;</c>.</summary>
    SelfClosingSyntax = 16,

    /// <summary>
    /// A text node made from several tokens that are not one run of the source (for example text split by a
    /// fostered element). Its range is the hull of the tokens.
    /// </summary>
    MergedText = 64,

    /// <summary>
    /// The node is a copy a DOM step made while the document was parsed: the children of a <c>select</c>'s selected
    /// <c>option</c> cloned into its <c>selectedcontent</c> element. It has no source bytes of its own (its ranges are
    /// <see cref="SourceRange.None"/>), so edits and the lossless writer never treat it as source.
    /// </summary>
    Synthesized = 1024,
}

/// <summary>A half-open range of UTF-16 offsets into <see cref="Document.Source"/>'s text. <see cref="Start"/> is -1 when there is none.</summary>
/// <param name="Start">The offset of the first character, or -1 for no range.</param>
/// <param name="End">The offset after the last character, or -1 for no range.</param>
public readonly record struct SourceRange(int Start, int End)
{
    /// <summary>The value meaning there is no range.</summary>
    public static SourceRange None => new(-1, -1);

    /// <summary>True when this is a range (possibly empty), not <see cref="None"/>.</summary>
    public bool IsPresent => Start >= 0;

    /// <summary>The number of characters, or 0 when there is no range.</summary>
    public int Length => Start >= 0 ? End - Start : 0;
}
