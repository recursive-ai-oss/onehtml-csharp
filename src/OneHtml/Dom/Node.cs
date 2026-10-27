using System;
using RecursiveAI.OneHtml.Parsing;

namespace RecursiveAI.OneHtml.Dom
{
    /// <summary>Per-node flag bits. The low bits that <see cref="NodeOrigin"/> exposes keep the same values.</summary>
    internal static class F
    {
        public const ushort ImpliedStart = 1;
        public const ushort Cloned = 2;
        public const ushort Reparented = 4;
        public const ushort AttrsMerged = 8;
        public const ushort SelfClosing = 16;

        /// <summary>The text or comment data lives in the side store, not in the source text.</summary>
        public const ushort DataSide = 32;
        public const ushort TextMerged = 64;

        /// <summary>A MathML <c>annotation-xml</c> whose <c>encoding</c> makes it an HTML integration point.</summary>
        public const ushort IntegrationPoint = 128;

        /// <summary>Parser bookkeeping, cleared before the document is published.</summary>
        public const ushort InStack = 256;
        public const ushort InAfe = 512;

        /// <summary>The node was made by a DOM step (a selectedcontent copy), not read from the source.</summary>
        public const ushort Synthesized = 1024;

        /// <summary>Parser bookkeeping: an option whose selectedness is true.</summary>
        public const ushort Selected = 2048;

        public const ushort PublicMask = ImpliedStart | Cloned | Reparented | AttrsMerged | SelfClosing | TextMerged | Synthesized;
    }

    /// <summary>Attribute flag bits: quote style in bits 0-1, namespace in bits 2-3, value-in-side-store in bit 4.</summary>
    internal static class AF
    {
        public const byte QuoteMask = 3;
        public const int NsShift = 2;
        public const byte NsMask = 3 << NsShift;
        public const byte ValueSide = 16;
    }
}

namespace RecursiveAI.OneHtml
{
    /// <summary>
    /// A handle to a node of a <see cref="Document"/>: the document and an index into its columns. Copying it is
    /// cheap, and it keeps referring to the snapshot it came from. The default value is the null node.
    /// </summary>
    public readonly partial struct Node : IEquatable<Node>
    {
        internal Node(Document document, int index)
        {
            Document = document;
            Index = index;
        }

        /// <summary>The snapshot this node belongs to; <see langword="null"/> for the null node.</summary>
        public Document? Document { get; }

        /// <summary>The position of the node in the snapshot's columns. Stable for the life of the snapshot.</summary>
        public int Index { get; }

        /// <summary>True for the default value, which stands for "no node": the parent of the root, the child of a leaf.</summary>
        public bool IsNull => Document is null;

        /// <summary>What kind of node this is.</summary>
        /// <exception cref="InvalidOperationException">This is the null node.</exception>
        public NodeKind Kind => (NodeKind)A.Kind[Index];

        /// <summary>True for an element.</summary>
        public bool IsElement => Document is not null && A.Kind[Index] == (byte)NodeKind.Element;

        /// <summary>
        /// The element's namespace. <see cref="ElementNamespace.Html"/> for anything that is not an element.
        /// </summary>
        public ElementNamespace Namespace => (ElementNamespace)A.Ns[Index];

        /// <summary>
        /// The element's local name as the parser leaves it (lowercase for HTML, with the case repairs of SVG and MathML
        /// names such as <c>foreignObject</c>), a DOCTYPE's name, or a processing instruction's target. Empty for other kinds.
        /// </summary>
        public string LocalName => A.Kind[Index] is (byte)NodeKind.Element or (byte)NodeKind.DocumentType or (byte)NodeKind.ProcessingInstruction
            ? Document!.Names.NameOf(A.Name[Index])
            : string.Empty;

        /// <summary>The parent, or the null node for a root. A template's content root has no parent; see <see cref="TemplateContent"/>.</summary>
        public Node Parent => Make(A.Parent[Index]);

        /// <summary>The first child, or the null node.</summary>
        public Node FirstChild => Make(A.First[Index]);

        /// <summary>The last child, or the null node.</summary>
        public Node LastChild => Make(A.Last[Index]);

        /// <summary>The next sibling, or the null node.</summary>
        public Node NextSibling => Make(A.Next[Index]);

        /// <summary>The previous sibling, or the null node.</summary>
        public Node PreviousSibling => Make(A.Prev[Index]);

        /// <summary>True when the node has at least one child.</summary>
        public bool HasChildNodes => A.First[Index] >= 0;

        /// <summary>
        /// For an HTML <c>template</c> element, the root of its content (kind <see cref="NodeKind.DocumentFragment"/>);
        /// the null node for every other node.
        /// </summary>
        public Node TemplateContent => A.Kind[Index] == (byte)NodeKind.Element ? Make(A.Aux[Index]) : default;

        /// <summary>The attributes in source order. Empty for anything but an element.</summary>
        public AttributeList Attributes => new(Document!, A.AttrStart[Index], A.AttrCount[Index]);

        /// <summary>
        /// The characters of a text node, a comment or a processing instruction. Empty for other kinds. Valid as long
        /// as the document is; it is a slice of the source text where the source holds the characters verbatim.
        /// </summary>
        public ReadOnlySpan<char> Data
        {
            get
            {
                if (A.Kind[Index] is not ((byte)NodeKind.Text or (byte)NodeKind.Comment or (byte)NodeKind.ProcessingInstruction))
                {
                    return default;
                }

                return (A.Flags[Index] & Dom.F.DataSide) != 0
                    ? A.Side.AsSpan(A.DataOff[Index], A.DataLen[Index])
                    : Document!.Source.Text.AsSpan(A.DataOff[Index], A.DataLen[Index]);
            }
        }

        /// <summary>The public identifier of a DOCTYPE; empty for other nodes.</summary>
        public string PublicId => A.Kind[Index] == (byte)NodeKind.DocumentType ? Document!.DoctypePublicId : string.Empty;

        /// <summary>The system identifier of a DOCTYPE; empty for other nodes.</summary>
        public string SystemId => A.Kind[Index] == (byte)NodeKind.DocumentType ? Document!.DoctypeSystemId : string.Empty;

        /// <summary>How the node relates to the bytes it came from; see <see cref="NodeOrigin"/>.</summary>
        public NodeOrigin Origin => (NodeOrigin)(A.Flags[Index] & Dom.F.PublicMask);

        /// <summary>The source map entry of the node.</summary>
        public NodeSource Source => new(this);

        /// <summary>
        /// The value of the attribute with this name, or <see langword="null"/>. For an element in the HTML namespace
        /// the name is matched ASCII case-insensitively, as <c>getAttribute</c> does; otherwise exactly. Only
        /// attributes with no namespace are considered.
        /// </summary>
        /// <param name="name">The attribute name.</param>
        public string? GetAttribute(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            int i = FindAttributeIndex(name);
            return i < 0 ? null : new Attr(Document!, i).Value.ToString();
        }

        /// <summary>True when <see cref="GetAttribute(string)"/> would find an attribute. Allocation-free.</summary>
        /// <param name="name">The attribute name.</param>
        public bool HasAttribute(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return FindAttributeIndex(name) >= 0;
        }

        // The arena index of the first attribute with this name (no namespace), or -1; the name rules of GetAttribute.
        private int FindAttributeIndex(string name)
        {
            if (A.Kind[Index] != (byte)NodeKind.Element)
            {
                return -1;
            }

            bool ignoreCase = A.Ns[Index] == (byte)ElementNamespace.Html;
            int start = A.AttrStart[Index];
            int end = start + A.AttrCount[Index];
            for (int i = start; i < end; i++)
            {
                var a = new Attr(Document!, i);
                if (a.Namespace == AttributeNamespace.None && NameEquals(a.LocalName, name, ignoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The value of the attribute as a slice of the document text, or false when there is none. Allocation-free.</summary>
        internal bool TryGetAttributeValue(string name, out ReadOnlySpan<char> value)
        {
            int i = FindAttributeIndex(name);
            if (i < 0)
            {
                value = default;
                return false;
            }

            value = new Attr(Document!, i).Value;
            return true;
        }

        /// <inheritdoc />
        public bool Equals(Node other) => ReferenceEquals(Document, other.Document) && Index == other.Index;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is Node n && Equals(n);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Document is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Document), Index);

        /// <summary>Two handles are equal when they name the same node of the same snapshot.</summary>
        public static bool operator ==(Node left, Node right) => left.Equals(right);

        /// <summary>Two handles are different when they name different nodes or snapshots.</summary>
        public static bool operator !=(Node left, Node right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() => Document is null ? "(null)" : $"{Kind}#{Index} {LocalName}";

        private Dom.Arena A => Document!.Arena;

        private Node Make(int index) => index < 0 ? default : new Node(Document!, index);

        private static bool NameEquals(string actual, string wanted, bool ignoreCase)
        {
            if (actual.Length != wanted.Length)
            {
                return false;
            }

            for (int i = 0; i < actual.Length; i++)
            {
                char a = actual[i];
                char w = wanted[i];
                if (a != w)
                {
                    if (!ignoreCase)
                    {
                        return false;
                    }

                    if (w is >= 'A' and <= 'Z')
                    {
                        w = (char)(w + 0x20);
                    }

                    if (a != w)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    /// <summary>The children of a node, for <c>foreach</c>.</summary>
    public readonly struct NodeChildren : System.Collections.Generic.IEnumerable<Node>
    {
        private readonly Node _parent;

        internal NodeChildren(Node parent) => _parent = parent;

        /// <summary>Gets the enumerator.</summary>
        public Enumerator GetEnumerator() => new(_parent);

        System.Collections.Generic.IEnumerator<Node> System.Collections.Generic.IEnumerable<Node>.GetEnumerator()
        {
            Enumerator e = GetEnumerator();
            while (e.MoveNext())
            {
                yield return e.Current;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => ((System.Collections.Generic.IEnumerable<Node>)this).GetEnumerator();

        /// <summary>Enumerates children by following sibling links.</summary>
        public struct Enumerator
        {
            private readonly Document? _document;
            private readonly int _first;
            private int _current;
            private bool _started;

            internal Enumerator(Node parent)
            {
                _document = parent.Document;
                _first = parent.IsNull ? -1 : parent.Document!.Arena.First[parent.Index];
                _current = -1;
                _started = false;
            }

            /// <summary>The current child.</summary>
            public readonly Node Current => new(_document!, _current);

            /// <summary>Moves to the next child.</summary>
            public bool MoveNext()
            {
                if (!_started)
                {
                    _started = true;
                    _current = _first;
                }
                else if (_current >= 0)
                {
                    _current = _document!.Arena.Next[_current];
                }

                return _current >= 0;
            }
        }
    }

    /// <summary>One attribute of an element.</summary>
    public readonly struct Attr
    {
        private readonly Document _document;
        private readonly int _index;

        internal Attr(Document document, int index)
        {
            _document = document;
            _index = index;
        }

        /// <summary>The local name: lowercase for HTML, with the case repairs of SVG and MathML attributes, without any prefix.</summary>
        public string LocalName => _document.Names.NameOf(_document.Arena.AName[_index]);

        /// <summary>The attribute's namespace.</summary>
        public AttributeNamespace Namespace => (AttributeNamespace)((_document.Arena.AFlags[_index] & Dom.AF.NsMask) >> Dom.AF.NsShift);

        /// <summary>The name as serialized: <c>xlink:href</c>, <c>xml:lang</c>, <c>xmlns</c>, <c>xmlns:xlink</c>, or the local name.</summary>
        public string QualifiedName => Namespace switch
        {
            AttributeNamespace.XLink => "xlink:" + LocalName,
            AttributeNamespace.Xml => "xml:" + LocalName,
            AttributeNamespace.Xmlns => LocalName == "xmlns" ? "xmlns" : "xmlns:" + LocalName,
            _ => LocalName,
        };

        /// <summary>The decoded value; empty when the attribute has none. A slice of the source text where the source holds the value verbatim.</summary>
        public ReadOnlySpan<char> Value
        {
            get
            {
                Dom.Arena a = _document.Arena;
                return (a.AFlags[_index] & Dom.AF.ValueSide) != 0
                    ? a.Side.AsSpan(a.AValOff[_index], a.AValLen[_index])
                    : _document.Source.Text.AsSpan(a.AValOff[_index], a.AValLen[_index]);
            }
        }

        /// <summary>How the value was written.</summary>
        public AttributeQuote Quote => (AttributeQuote)(_document.Arena.AFlags[_index] & Dom.AF.QuoteMask);

        /// <summary>True when the attribute had a value (even an empty one) in the source.</summary>
        public bool HasValue => Quote != AttributeQuote.NoValue;

        /// <summary>The name as written (before case repair).</summary>
        public SourceRange NameRange => new(_document.Arena.ANameStart[_index], _document.Arena.ANameEnd[_index]);

        /// <summary>The value as written, inside any quotes; an empty range at the end of the name when there is no value.</summary>
        public SourceRange ValueRange => new(_document.Arena.AValStart[_index], _document.Arena.AValEnd[_index]);

        /// <summary>The whole attribute: from the start of the name to after the closing quote or the unquoted value.</summary>
        public SourceRange Range
        {
            get
            {
                Dom.Arena a = _document.Arena;
                if (a.ANameStart[_index] < 0)
                {
                    return SourceRange.None;
                }

                int end = Quote switch
                {
                    AttributeQuote.NoValue => a.ANameEnd[_index],
                    AttributeQuote.Unquoted => a.AValEnd[_index],
                    _ => a.AValEnd[_index] + 1,
                };
                return new SourceRange(a.ANameStart[_index], end);
            }
        }
    }

    /// <summary>The attributes of an element in source order.</summary>
    public readonly struct AttributeList
    {
        private readonly Document? _document;
        private readonly int _start;

        internal AttributeList(Document document, int start, int count)
        {
            _document = document;
            _start = start;
            Count = count;
        }

        /// <summary>The number of attributes.</summary>
        public int Count { get; }

        /// <summary>The attribute at a position.</summary>
        /// <param name="index">From 0 to <see cref="Count"/> - 1.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
        public Attr this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return new Attr(_document!, _start + index);
            }
        }

        /// <summary>Gets the enumerator.</summary>
        public Enumerator GetEnumerator() => new(this);

        /// <summary>Enumerates the attributes.</summary>
        public struct Enumerator
        {
            private readonly AttributeList _list;
            private int _i;

            internal Enumerator(AttributeList list)
            {
                _list = list;
                _i = -1;
            }

            /// <summary>The current attribute.</summary>
            public readonly Attr Current => _list[_i];

            /// <summary>Moves to the next attribute.</summary>
            public bool MoveNext() => ++_i < _list.Count;
        }
    }

    /// <summary>
    /// The source map entry of one node: where its bytes are, and whether the parser put the node where the bytes say.
    /// Ranges are UTF-16 offsets into <see cref="Document.Source"/>'s text; <see cref="Document.ToByteRange"/> gives bytes.
    /// </summary>
    public readonly struct NodeSource
    {
        private readonly Node _node;

        internal NodeSource(Node node) => _node = node;

        private Dom.Arena A => _node.Document!.Arena;

        /// <summary>
        /// For an element, its start tag (empty and at the causing token for an implied one, see
        /// <see cref="NodeOrigin.ImpliedStartTag"/>). For a comment, DOCTYPE or processing instruction, the whole
        /// construct. For text, the characters as written: character references included, and the hull of the tokens
        /// when <see cref="NodeOrigin.MergedText"/> is set. <see cref="SourceRange.None"/> for roots.
        /// </summary>
        public SourceRange Range => new(A.SrcStart[_node.Index], A.SrcEnd[_node.Index]);

        /// <summary>The end tag that closed the element, or <see cref="SourceRange.None"/> when the source has none (implied, void, or unclosed).</summary>
        public SourceRange EndTag => new(A.EndStart[_node.Index], A.EndEnd[_node.Index]);

        /// <summary>True when the element's start tag is in the source.</summary>
        public bool HasStartTag => _node.IsElement && (A.Flags[_node.Index] & (Dom.F.ImpliedStart | Dom.F.Synthesized)) == 0;

        /// <summary>True when the element has an end tag in the source.</summary>
        public bool HasEndTag => A.EndStart[_node.Index] >= 0;

        /// <summary>
        /// For text, a comment or a processing instruction whose characters are in the source verbatim, the range of
        /// those characters (a comment's data without <c>&lt;!--</c> and <c>--&gt;</c>); otherwise <see cref="SourceRange.None"/>.
        /// </summary>
        public SourceRange DataRange
        {
            get
            {
                int i = _node.Index;
                if (A.Kind[i] is (byte)NodeKind.Text or (byte)NodeKind.Comment or (byte)NodeKind.ProcessingInstruction
                    && (A.Flags[i] & Dom.F.DataSide) == 0)
                {
                    return new SourceRange(A.DataOff[i], A.DataOff[i] + A.DataLen[i]);
                }

                return SourceRange.None;
            }
        }

        /// <summary>
        /// True when the parser made the node or put it somewhere other than its source position, so an editor must
        /// not assume the node sits inside its parent's source bytes. Equals <c>Origin != None</c> apart from
        /// self-closing syntax.
        /// </summary>
        public bool IsDisplaced => (A.Flags[_node.Index] & (Dom.F.ImpliedStart | Dom.F.Cloned | Dom.F.Reparented | Dom.F.TextMerged | Dom.F.Synthesized)) != 0;
    }
}
