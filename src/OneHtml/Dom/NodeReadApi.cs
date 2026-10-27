using System;
using System.Collections.Generic;
using System.Text;
using RecursiveAI.OneHtml.Dom;
using RecursiveAI.OneHtml.Selectors;

namespace RecursiveAI.OneHtml;

/// <summary>The result of <see cref="Node.CompareDocumentPosition"/>, the bit set of DOM 4.4 "compareDocumentPosition".</summary>
[Flags]
public enum DocumentPosition
{
    /// <summary>The nodes are the same node.</summary>
    None = 0,

    /// <summary>The nodes are in different trees (or different snapshots).</summary>
    Disconnected = 1,

    /// <summary>The other node precedes this one.</summary>
    Preceding = 2,

    /// <summary>The other node follows this one.</summary>
    Following = 4,

    /// <summary>The other node is an ancestor of this one.</summary>
    Contains = 8,

    /// <summary>The other node is a descendant of this one.</summary>
    ContainedBy = 16,

    /// <summary>The order of disconnected nodes is implementation specific; here it is the order of the snapshot's indexes.</summary>
    ImplementationSpecific = 32,
}

public readonly partial struct Node
{
    private Arena Ar => Document!.Arena;

    /// <summary>The snapshot's document node (<see cref="NodeKind.Document"/>, or the fragment root of a fragment snapshot).</summary>
    public Node OwnerDocument => IsNull ? default : Document!.Root;

    /// <summary>The parent when it is an element, else the null node.</summary>
    public Node ParentElement
    {
        get
        {
            int p = Ar.Parent[Index];
            return p >= 0 && Ar.Kind[p] == (byte)NodeKind.Element ? new Node(Document!, p) : default;
        }
    }

    /// <summary>The element children in document order. Allocation-free to enumerate. (<c>ParentNode.children</c>.)</summary>
    public ElementChildren Children => new(this);

    /// <summary>All child nodes in document order (<c>Node.childNodes</c>). Allocation-free to enumerate.</summary>
    public NodeChildren ChildNodes => new(this);

    /// <summary>The number of element children.</summary>
    public int ChildElementCount
    {
        get
        {
            int n = 0;
            Arena a = Ar;
            for (int c = a.First[Index]; c >= 0; c = a.Next[c])
            {
                if (a.Kind[c] == (byte)NodeKind.Element)
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>The first child that is an element, or the null node.</summary>
    public Node FirstElementChild
    {
        get
        {
            Arena a = Ar;
            for (int c = a.First[Index]; c >= 0; c = a.Next[c])
            {
                if (a.Kind[c] == (byte)NodeKind.Element)
                {
                    return new Node(Document!, c);
                }
            }

            return default;
        }
    }

    /// <summary>The last child that is an element, or the null node.</summary>
    public Node LastElementChild
    {
        get
        {
            Arena a = Ar;
            for (int c = a.Last[Index]; c >= 0; c = a.Prev[c])
            {
                if (a.Kind[c] == (byte)NodeKind.Element)
                {
                    return new Node(Document!, c);
                }
            }

            return default;
        }
    }

    /// <summary>The next sibling that is an element, or the null node.</summary>
    public Node NextElementSibling
    {
        get
        {
            Arena a = Ar;
            for (int c = a.Next[Index]; c >= 0; c = a.Next[c])
            {
                if (a.Kind[c] == (byte)NodeKind.Element)
                {
                    return new Node(Document!, c);
                }
            }

            return default;
        }
    }

    /// <summary>The previous sibling that is an element, or the null node.</summary>
    public Node PreviousElementSibling
    {
        get
        {
            Arena a = Ar;
            for (int c = a.Prev[Index]; c >= 0; c = a.Prev[c])
            {
                if (a.Kind[c] == (byte)NodeKind.Element)
                {
                    return new Node(Document!, c);
                }
            }

            return default;
        }
    }

    /// <summary>The element's name for <c>Element.tagName</c>: the qualified name, upper-cased (ASCII) for an HTML element. Allocates a string. Empty for non-elements.</summary>
    public string TagName
    {
        get
        {
            if (!IsElement)
            {
                return string.Empty;
            }

            string local = LocalName;
            return Namespace == ElementNamespace.Html ? AsciiUpper(local) : local;
        }
    }

    /// <summary>The element's namespace URI, or <see langword="null"/> for other nodes.</summary>
    public string? NamespaceURI => IsElement
        ? Namespace switch
        {
            ElementNamespace.Html => "http://www.w3.org/1999/xhtml",
            ElementNamespace.MathMl => "http://www.w3.org/1998/Math/MathML",
            _ => "http://www.w3.org/2000/svg",
        }
        : null;

    /// <summary>The value of the <c>id</c> attribute, or the empty string (<c>Element.id</c>). Allocates a string.</summary>
    public string Id => IsElement ? FindAttr(N.Id) is int i && i >= 0 ? AttrValue(i).ToString() : string.Empty : string.Empty;

    /// <summary>The value of the <c>class</c> attribute, or the empty string (<c>Element.className</c>). Allocates a string.</summary>
    public string ClassName => IsElement ? FindAttr(N.Class) is int i && i >= 0 ? AttrValue(i).ToString() : string.Empty : string.Empty;

    /// <summary>The class tokens (<c>Element.classList</c>, read-only). Allocation-free.</summary>
    public ClassList ClassList => new(IsElement && FindAttr(N.Class) is int i && i >= 0 ? AttrValue(i) : default);

    /// <summary>True when the element has at least one attribute.</summary>
    public bool HasAttributes => IsElement && Ar.AttrCount[Index] > 0;

    /// <summary>The value of the attribute with this namespace and local name, or <see langword="null"/>. Exact comparison.</summary>
    /// <param name="ns">The attribute namespace; <see cref="AttributeNamespace.None"/> for none.</param>
    /// <param name="localName">The local name.</param>
    public string? GetAttribute(AttributeNamespace ns, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        if (!IsElement)
        {
            return null;
        }

        AttributeList list = Attributes;
        for (int i = 0; i < list.Count; i++)
        {
            Attr a = list[i];
            if (a.Namespace == ns && a.LocalName == localName)
            {
                return a.Value.ToString();
            }
        }

        return null;
    }

    /// <summary>True when <see cref="GetAttribute(AttributeNamespace, string)"/> would find an attribute.</summary>
    /// <param name="ns">The attribute namespace.</param>
    /// <param name="localName">The local name.</param>
    public bool HasAttribute(AttributeNamespace ns, string localName) => GetAttribute(ns, localName) is not null;

    /// <summary>
    /// <c>Node.textContent</c>: for an element or fragment, the data of all descendant text nodes in document order;
    /// for text, comments and processing instructions their data; <see langword="null"/> for documents and DOCTYPEs.
    /// </summary>
    public string? TextContent
    {
        get
        {
            if (IsNull)
            {
                return null;
            }

            switch (Kind)
            {
                case NodeKind.Document:
                case NodeKind.DocumentType:
                    return null;
                case NodeKind.Text:
                case NodeKind.Comment:
                case NodeKind.ProcessingInstruction:
                    return Data.ToString();
                default:
                    var sb = new StringBuilder();
                    Arena a = Ar;
                    for (int i = NextPreorder(a, Index, Index); i >= 0; i = NextPreorder(a, i, Index))
                    {
                        if (a.Kind[i] == (byte)NodeKind.Text)
                        {
                            sb.Append(new Node(Document!, i).Data);
                        }
                    }

                    return sb.ToString();
            }
        }
    }

    /// <summary>The root of the tree the node is in: the document or fragment root, or a detached ancestor (<c>getRootNode()</c>).</summary>
    public Node GetRootNode()
    {
        Arena a = Ar;
        int i = Index;
        while (a.Parent[i] >= 0)
        {
            i = a.Parent[i];
        }

        return new Node(Document!, i);
    }

    /// <summary>True when the node's root is the document (not a template's content, which is a separate tree).</summary>
    public bool IsConnected => !IsNull && Ar.Kind[RootIndex(Index)] == (byte)NodeKind.Document;

    /// <summary>True when <paramref name="other"/> is this node or a descendant of it (<c>Node.contains</c>); false for the null node and for nodes of another snapshot.</summary>
    /// <param name="other">A node.</param>
    public bool Contains(Node other)
    {
        if (IsNull || other.IsNull || !ReferenceEquals(Document, other.Document))
        {
            return false;
        }

        Arena a = Ar;
        for (int i = other.Index; i >= 0; i = a.Parent[i])
        {
            if (i == Index)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>DOM 4.4 "compareDocumentPosition" of <paramref name="other"/> relative to this node.</summary>
    /// <param name="other">A node.</param>
    /// <exception cref="ArgumentException">Either node is the null node.</exception>
    public DocumentPosition CompareDocumentPosition(Node other)
    {
        if (IsNull || other.IsNull)
        {
            throw new ArgumentException("Both nodes must be non-null.");
        }

        if (Equals(other))
        {
            return DocumentPosition.None;
        }

        Arena a = Ar;
        if (!ReferenceEquals(Document, other.Document) || RootIndex(Index) != RootIndex(other.Index))
        {
            // The spec asks for a consistent arbitrary order; the index order is one.
            DocumentPosition order = ReferenceEquals(Document, other.Document)
                ? (other.Index < Index ? DocumentPosition.Preceding : DocumentPosition.Following)
                : (other.Document!.Id < Document!.Id ? DocumentPosition.Preceding : DocumentPosition.Following);
            return DocumentPosition.Disconnected | DocumentPosition.ImplementationSpecific | order;
        }

        if (other.Contains(this))
        {
            return DocumentPosition.Contains | DocumentPosition.Preceding;
        }

        if (Contains(other))
        {
            return DocumentPosition.ContainedBy | DocumentPosition.Following;
        }

        // Find the children of the lowest common ancestor that lead to each node.
        int depthA = Depth(a, Index);
        int depthB = Depth(a, other.Index);
        int x = Index;
        int y = other.Index;
        while (depthA > depthB)
        {
            x = a.Parent[x];
            depthA--;
        }

        while (depthB > depthA)
        {
            y = a.Parent[y];
            depthB--;
        }

        while (a.Parent[x] != a.Parent[y])
        {
            x = a.Parent[x];
            y = a.Parent[y];
        }

        // x and y are distinct siblings; whichever is reached first walking forward from x comes first.
        for (int s = a.Next[x]; s >= 0; s = a.Next[s])
        {
            if (s == y)
            {
                return DocumentPosition.Following;
            }
        }

        return DocumentPosition.Preceding;
    }

    /// <summary>
    /// The descendants in document order (preorder), excluding this node and the content of <c>template</c> elements.
    /// Iterative and allocation-free.
    /// </summary>
    public NodeDescendants Descendants => new(this, false);

    /// <summary>The descendant elements in document order. Iterative and allocation-free.</summary>
    public NodeDescendants DescendantElements => new(this, true);

    /// <summary>The nodes that follow this node in document order within the tree, including its descendants first.</summary>
    /// <param name="scope">Stop after this node's subtree; the null node for no limit (the whole tree).</param>
    /// <returns>The next node in preorder, or the null node.</returns>
    public Node NextInDocumentOrder(Node scope = default)
    {
        int r = NextPreorder(Ar, Index, scope.IsNull ? -1 : scope.Index);
        return r < 0 ? default : new Node(Document!, r);
    }

    /// <summary>A typed view when this is an element; <see langword="null"/> otherwise.</summary>
    public Element? AsElement() => IsElement ? new Element(this) : null;

    /// <summary><c>ParentNode.querySelector</c>: the first descendant element that matches, in document order, or the null node.</summary>
    /// <param name="selectors">A selector list.</param>
    /// <exception cref="SelectorSyntaxException">The selector list is invalid (or over a limit).</exception>
    public Node QuerySelector(string selectors) => QuerySelector(SelectorList.Parse(selectors));

    /// <summary><c>ParentNode.querySelector</c> with a compiled selector list.</summary>
    /// <param name="selectors">A compiled selector list.</param>
    /// <param name="options">Scope and interaction state; by default <c>:scope</c> is this node.</param>
    public Node QuerySelector(SelectorList selectors, SelectorMatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        return SelectorMatcher.QueryFirst(this, selectors, options);
    }

    /// <summary><c>ParentNode.querySelectorAll</c>: every descendant element that matches, in document order.</summary>
    /// <param name="selectors">A selector list.</param>
    /// <exception cref="SelectorSyntaxException">The selector list is invalid (or over a limit).</exception>
    public List<Node> QuerySelectorAll(string selectors)
    {
        var result = new List<Node>();
        QuerySelectorAll(SelectorList.Parse(selectors), result);
        return result;
    }

    /// <summary><c>ParentNode.querySelectorAll</c> with a compiled selector list: a new list of every matching descendant element, in document order.</summary>
    /// <param name="selectors">A compiled selector list.</param>
    /// <param name="options">Scope and interaction state; by default <c>:scope</c> is this node.</param>
    public List<Node> QuerySelectorAll(SelectorList selectors, SelectorMatchOptions? options = null)
    {
        var result = new List<Node>();
        QuerySelectorAll(selectors, result, options);
        return result;
    }

    /// <summary>Appends every matching descendant element, in document order, to <paramref name="results"/>. Allocates nothing itself when the selector needs no side tables.</summary>
    /// <param name="selectors">A compiled selector list.</param>
    /// <param name="results">The list to append to.</param>
    /// <param name="options">Scope and interaction state; by default <c>:scope</c> is this node.</param>
    public void QuerySelectorAll(SelectorList selectors, List<Node> results, SelectorMatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        ArgumentNullException.ThrowIfNull(results);
        SelectorMatcher.QueryAll(this, selectors, results, options);
    }

    /// <summary><c>Element.matches</c>. False for anything but an element.</summary>
    /// <param name="selectors">A selector list.</param>
    /// <exception cref="SelectorSyntaxException">The selector list is invalid (or over a limit).</exception>
    public bool Matches(string selectors) => Matches(SelectorList.Parse(selectors));

    /// <summary><c>Element.matches</c> with a compiled selector list.</summary>
    /// <param name="selectors">A compiled selector list.</param>
    /// <param name="options">Scope and interaction state; by default <c>:scope</c> is this element.</param>
    public bool Matches(SelectorList selectors, SelectorMatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        return IsElement && SelectorMatcher.Matches(this, selectors, options);
    }

    /// <summary><c>Element.closest</c>: this element or its nearest ancestor element that matches, or the null node.</summary>
    /// <param name="selectors">A selector list.</param>
    /// <exception cref="SelectorSyntaxException">The selector list is invalid (or over a limit).</exception>
    public Node Closest(string selectors) => Closest(SelectorList.Parse(selectors));

    /// <summary><c>Element.closest</c> with a compiled selector list.</summary>
    /// <param name="selectors">A compiled selector list.</param>
    /// <param name="options">Scope and interaction state; by default <c>:scope</c> is this element.</param>
    public Node Closest(SelectorList selectors, SelectorMatchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        if (!IsElement)
        {
            return default;
        }

        // :scope is this element for every candidate, whatever the candidate is.
        SelectorMatchOptions scoped = options is not null && !options.Scope.IsNull
            ? options
            : new SelectorMatchOptions { Scope = this, State = options?.State };
        Arena a = Ar;
        for (int i = Index; i >= 0 && a.Kind[i] == (byte)NodeKind.Element; i = a.Parent[i])
        {
            var n = new Node(Document!, i);
            if (SelectorMatcher.Matches(n, selectors, scoped))
            {
                return n;
            }
        }

        return default;
    }

    /// <summary>
    /// <c>getElementsByTagName</c> (DOM 4.9): descendant elements whose qualified name is <paramref name="qualifiedName"/>.
    /// <c>*</c> matches all. For HTML elements the name is compared lower-cased. A snapshot never changes, so the list is
    /// a plain list rather than a live collection.
    /// </summary>
    /// <param name="qualifiedName">A tag name or <c>*</c>.</param>
    public List<Node> GetElementsByTagName(string qualifiedName)
    {
        ArgumentNullException.ThrowIfNull(qualifiedName);
        var result = new List<Node>();
        string lower = AsciiLower(qualifiedName);
        bool all = qualifiedName == "*";
        foreach (Node e in DescendantElements)
        {
            if (all || (e.Namespace == ElementNamespace.Html ? e.LocalName == lower : e.LocalName == qualifiedName))
            {
                result.Add(e);
            }
        }

        return result;
    }

    /// <summary><c>getElementsByTagNameNS</c> (DOM 4.9): descendant elements with this namespace URI and local name; <c>*</c> matches any.</summary>
    /// <param name="namespaceUri">A namespace URI, <c>*</c>, or null/empty for no namespace (which no parsed element has).</param>
    /// <param name="localName">A local name or <c>*</c>.</param>
    public List<Node> GetElementsByTagNameNS(string? namespaceUri, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        var result = new List<Node>();
        foreach (Node e in DescendantElements)
        {
            if ((namespaceUri == "*" || e.NamespaceURI == (string.IsNullOrEmpty(namespaceUri) ? null : namespaceUri))
                && (localName == "*" || e.LocalName == localName))
            {
                result.Add(e);
            }
        }

        return result;
    }

    /// <summary>
    /// <c>getElementsByClassName</c> (DOM 4.9): descendant elements that have every class in
    /// <paramref name="classNames"/> (split on ASCII whitespace). Classes compare ASCII case-insensitively in quirks mode.
    /// An empty list of classes matches nothing.
    /// </summary>
    /// <param name="classNames">Whitespace-separated class names.</param>
    public List<Node> GetElementsByClassName(string classNames)
    {
        ArgumentNullException.ThrowIfNull(classNames);
        var result = new List<Node>();
        var wanted = new List<string>();
        ClassList.AddTokens(classNames, wanted);
        if (wanted.Count == 0)
        {
            return result;
        }

        bool quirks = Document!.Mode == QuirksMode.Quirks;
        foreach (Node e in DescendantElements)
        {
            ClassList have = e.ClassList;
            bool ok = true;
            foreach (string w in wanted)
            {
                if (!have.Contains(w, quirks))
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                result.Add(e);
            }
        }

        return result;
    }

    // ---- helpers ----

    /// <summary>Index of the first attribute (no namespace) with this name id, or -1.</summary>
    internal int FindAttr(int nameId)
    {
        Arena a = Ar;
        int start = a.AttrStart[Index];
        int end = start + a.AttrCount[Index];
        for (int i = start; i < end; i++)
        {
            if (a.AName[i] == nameId && (a.AFlags[i] & AF.NsMask) == 0)
            {
                return i;
            }
        }

        return -1;
    }

    internal ReadOnlySpan<char> AttrValue(int attrIndex)
    {
        Arena a = Ar;
        return (a.AFlags[attrIndex] & AF.ValueSide) != 0
            ? a.Side.AsSpan(a.AValOff[attrIndex], a.AValLen[attrIndex])
            : Document!.Source.Text.AsSpan(a.AValOff[attrIndex], a.AValLen[attrIndex]);
    }

    internal static int NextPreorder(Arena a, int i, int scope)
    {
        int c = a.First[i];
        if (c >= 0)
        {
            return c;
        }

        while (i >= 0 && i != scope)
        {
            int n = a.Next[i];
            if (n >= 0)
            {
                return n;
            }

            i = a.Parent[i];
        }

        return -1;
    }

    internal static int RootIndex(Arena a, int i)
    {
        while (a.Parent[i] >= 0)
        {
            i = a.Parent[i];
        }

        return i;
    }

    private int RootIndex(int i) => RootIndex(Ar, i);

    private static int Depth(Arena a, int i)
    {
        int d = 0;
        while (a.Parent[i] >= 0)
        {
            i = a.Parent[i];
            d++;
        }

        return d;
    }

    internal static string AsciiUpper(string s)
    {
        int i = 0;
        while (i < s.Length && s[i] is not (>= 'a' and <= 'z'))
        {
            i++;
        }

        if (i == s.Length)
        {
            return s;
        }

        return string.Create(s.Length, s, static (span, src) =>
        {
            for (int k = 0; k < span.Length; k++)
            {
                char c = src[k];
                span[k] = c is >= 'a' and <= 'z' ? (char)(c - 0x20) : c;
            }
        });
    }

    internal static string AsciiLower(string s)
    {
        int i = 0;
        while (i < s.Length && s[i] is not (>= 'A' and <= 'Z'))
        {
            i++;
        }

        if (i == s.Length)
        {
            return s;
        }

        return string.Create(s.Length, s, static (span, src) =>
        {
            for (int k = 0; k < span.Length; k++)
            {
                char c = src[k];
                span[k] = c is >= 'A' and <= 'Z' ? (char)(c + 0x20) : c;
            }
        });
    }
}

/// <summary>The element children of a node, for <c>foreach</c>. Allocation-free.</summary>
public readonly struct ElementChildren : IEnumerable<Node>
{
    private readonly Node _parent;

    internal ElementChildren(Node parent) => _parent = parent;

    /// <summary>Gets the enumerator.</summary>
    public Enumerator GetEnumerator() => new(_parent);

    IEnumerator<Node> IEnumerable<Node>.GetEnumerator()
    {
        Enumerator e = GetEnumerator();
        while (e.MoveNext())
        {
            yield return e.Current;
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => ((IEnumerable<Node>)this).GetEnumerator();

    /// <summary>Enumerates element children.</summary>
    public struct Enumerator
    {
        private readonly Document? _document;
        private int _next;
        private int _current;

        internal Enumerator(Node parent)
        {
            _document = parent.Document;
            _next = parent.IsNull ? -1 : parent.Document!.Arena.First[parent.Index];
            _current = -1;
        }

        /// <summary>The current element.</summary>
        public readonly Node Current => new(_document!, _current);

        /// <summary>Moves to the next element child.</summary>
        public bool MoveNext()
        {
            if (_document is null)
            {
                return false;
            }

            Arena a = _document.Arena;
            while (_next >= 0)
            {
                int c = _next;
                _next = a.Next[c];
                if (a.Kind[c] == (byte)NodeKind.Element)
                {
                    _current = c;
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>The descendants of a node in document order, for <c>foreach</c>. Uses the parent links, so it needs no stack.</summary>
public readonly struct NodeDescendants : IEnumerable<Node>
{
    private readonly Node _root;
    private readonly bool _elementsOnly;

    internal NodeDescendants(Node root, bool elementsOnly)
    {
        _root = root;
        _elementsOnly = elementsOnly;
    }

    /// <summary>Gets the enumerator.</summary>
    public Enumerator GetEnumerator() => new(_root, _elementsOnly);

    IEnumerator<Node> IEnumerable<Node>.GetEnumerator()
    {
        Enumerator e = GetEnumerator();
        while (e.MoveNext())
        {
            yield return e.Current;
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => ((IEnumerable<Node>)this).GetEnumerator();

    /// <summary>Enumerates descendants.</summary>
    public struct Enumerator
    {
        private readonly Document? _document;
        private readonly int _root;
        private readonly bool _elementsOnly;
        private int _current;

        internal Enumerator(Node root, bool elementsOnly)
        {
            _document = root.Document;
            _root = root.IsNull ? -1 : root.Index;
            _elementsOnly = elementsOnly;
            _current = _root;
        }

        /// <summary>The current descendant.</summary>
        public readonly Node Current => new(_document!, _current);

        /// <summary>Moves to the next descendant in document order.</summary>
        public bool MoveNext()
        {
            if (_document is null || _current < 0)
            {
                return false;
            }

            Arena a = _document.Arena;
            while (true)
            {
                _current = Node.NextPreorder(a, _current, _root);
                if (_current < 0)
                {
                    return false;
                }

                if (!_elementsOnly || a.Kind[_current] == (byte)NodeKind.Element)
                {
                    return true;
                }
            }
        }
    }
}

/// <summary>The whitespace-separated tokens of a <c>class</c> attribute (<c>DOMTokenList</c>, read-only). A view over the attribute's text.</summary>
public readonly ref struct ClassList
{
    private readonly ReadOnlySpan<char> _value;

    internal ClassList(ReadOnlySpan<char> value) => _value = value;

    /// <summary>The number of tokens. Counted by scanning.</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (ReadOnlySpan<char> unused in this)
            {
                n++;
            }

            return n;
        }
    }

    /// <summary>The token at a position, as a string (allocates).</summary>
    /// <param name="index">From 0.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is out of range.</exception>
    public string this[int index]
    {
        get
        {
            int n = 0;
            foreach (ReadOnlySpan<char> t in this)
            {
                if (n++ == index)
                {
                    return t.ToString();
                }
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    /// <summary>True when a token equals <paramref name="token"/> exactly.</summary>
    /// <param name="token">A class name.</param>
    public bool Contains(string token) => Contains(token, false);

    /// <summary>True when a token equals <paramref name="token"/>, ASCII case-insensitively when <paramref name="ignoreCase"/> (quirks mode).</summary>
    /// <param name="token">A class name.</param>
    /// <param name="ignoreCase">Compare ASCII case-insensitively.</param>
    public bool Contains(string token, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(token);
        foreach (ReadOnlySpan<char> t in this)
        {
            if (ignoreCase ? IsAsciiEqual(t, token) : t.SequenceEqual(token))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the enumerator.</summary>
    public Enumerator GetEnumerator() => new(_value);

    internal static bool IsAsciiEqual(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            char x = a[i];
            char y = b[i];
            if (x != y)
            {
                if (x is >= 'A' and <= 'Z')
                {
                    x = (char)(x + 0x20);
                }

                if (y is >= 'A' and <= 'Z')
                {
                    y = (char)(y + 0x20);
                }

                if (x != y)
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal static void AddTokens(string text, List<string> into)
    {
        foreach (ReadOnlySpan<char> t in new ClassList(text))
        {
            into.Add(t.ToString());
        }
    }

    /// <summary>Enumerates tokens as spans of the attribute text.</summary>
    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<char> _text;
        private int _pos;
        private ReadOnlySpan<char> _current;

        internal Enumerator(ReadOnlySpan<char> text)
        {
            _text = text;
            _pos = 0;
            _current = default;
        }

        /// <summary>The current token.</summary>
        public readonly ReadOnlySpan<char> Current => _current;

        /// <summary>Moves to the next token.</summary>
        public bool MoveNext()
        {
            while (_pos < _text.Length && IsSpace(_text[_pos]))
            {
                _pos++;
            }

            if (_pos >= _text.Length)
            {
                return false;
            }

            int start = _pos;
            while (_pos < _text.Length && !IsSpace(_text[_pos]))
            {
                _pos++;
            }

            _current = _text.Slice(start, _pos - start);
            return true;
        }

        private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';
    }
}

/// <summary>A typed view of an element node: the element-only members of the W3C <c>Element</c> interface.</summary>
public readonly struct Element : IEquatable<Element>
{
    internal Element(Node node) => Node = node;

    /// <summary>The underlying node handle.</summary>
    public Node Node { get; }

    /// <summary>The uppercase (HTML) tag name; see <see cref="Node.TagName"/>.</summary>
    public string TagName => Node.TagName;

    /// <summary>The local name.</summary>
    public string LocalName => Node.LocalName;

    /// <summary>The namespace URI.</summary>
    public string NamespaceURI => Node.NamespaceURI!;

    /// <summary>The <c>id</c> attribute, or empty.</summary>
    public string Id => Node.Id;

    /// <summary>The <c>class</c> attribute, or empty.</summary>
    public string ClassName => Node.ClassName;

    /// <summary>The class tokens.</summary>
    public ClassList ClassList => Node.ClassList;

    /// <summary>The attributes in source order.</summary>
    public AttributeList Attributes => Node.Attributes;

    /// <summary>The value of the attribute with this name, or null; see <see cref="Node.GetAttribute(string)"/>.</summary>
    /// <param name="name">An attribute name.</param>
    public string? GetAttribute(string name) => Node.GetAttribute(name);

    /// <summary>True when the attribute exists.</summary>
    /// <param name="name">An attribute name.</param>
    public bool HasAttribute(string name) => Node.HasAttribute(name);

    /// <summary>The element children.</summary>
    public ElementChildren Children => Node.Children;

    /// <summary>The parent element, or the null node.</summary>
    public Node ParentElement => Node.ParentElement;

    /// <summary>The first element child.</summary>
    public Node FirstElementChild => Node.FirstElementChild;

    /// <summary>The last element child.</summary>
    public Node LastElementChild => Node.LastElementChild;

    /// <summary>The next element sibling.</summary>
    public Node NextElementSibling => Node.NextElementSibling;

    /// <summary>The previous element sibling.</summary>
    public Node PreviousElementSibling => Node.PreviousElementSibling;

    /// <summary>The number of element children.</summary>
    public int ChildElementCount => Node.ChildElementCount;

    /// <summary>The text of the element's descendants.</summary>
    public string TextContent => Node.TextContent ?? string.Empty;

    /// <summary>See <see cref="Node.Matches(string)"/>.</summary>
    /// <param name="selectors">A selector list.</param>
    public bool Matches(string selectors) => Node.Matches(selectors);

    /// <summary>See <see cref="Node.Closest(string)"/>.</summary>
    /// <param name="selectors">A selector list.</param>
    public Node Closest(string selectors) => Node.Closest(selectors);

    /// <summary>See <see cref="Node.QuerySelector(string)"/>.</summary>
    /// <param name="selectors">A selector list.</param>
    public Node QuerySelector(string selectors) => Node.QuerySelector(selectors);

    /// <summary>See <see cref="Node.QuerySelectorAll(string)"/>.</summary>
    /// <param name="selectors">A selector list.</param>
    public List<Node> QuerySelectorAll(string selectors) => Node.QuerySelectorAll(selectors);

    /// <inheritdoc />
    public bool Equals(Element other) => Node.Equals(other.Node);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Element e && Equals(e);

    /// <inheritdoc />
    public override int GetHashCode() => Node.GetHashCode();

    /// <summary>Handle equality.</summary>
    /// <param name="left">A view.</param>
    /// <param name="right">Another view.</param>
    public static bool operator ==(Element left, Element right) => left.Equals(right);

    /// <summary>Handle inequality.</summary>
    /// <param name="left">A view.</param>
    /// <param name="right">Another view.</param>
    public static bool operator !=(Element left, Element right) => !left.Equals(right);
}

public sealed partial class Document
{
    private DocumentIndex? _index;

    /// <summary>
    /// <c>Document.getElementById</c>: the first element in document order whose <c>id</c> is exactly
    /// <paramref name="id"/> (case-sensitive in every mode), or the null node. Builds the id table on first use.
    /// </summary>
    /// <param name="id">An id; the empty string never matches.</param>
    public Node GetElementById(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        int i = Index.FirstWithId(id);
        return i < 0 ? default : new Node(this, i);
    }

    /// <summary>The lazily built, thread-safe side tables of this snapshot.</summary>
    internal DocumentIndex Index
    {
        get
        {
            DocumentIndex? x = System.Threading.Volatile.Read(ref _index);
            if (x is null)
            {
                x = new DocumentIndex(this);
                x = System.Threading.Interlocked.CompareExchange(ref _index, x, null) ?? x;
            }

            return x;
        }
    }
}
