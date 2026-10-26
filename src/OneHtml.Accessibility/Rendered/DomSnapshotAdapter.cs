using System;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility.Rendered;

/// <summary>How strictly attributes must agree between the snapshot and the document for the two to count as the same DOM.</summary>
public enum AttributeCheck
{
    /// <summary>Every element has the same attributes with the same values (the default): a changed attribute can change layout, so the facts no longer describe the page.</summary>
    Values,

    /// <summary>Every element has the same attribute names; values may differ. Use when only values were edited and the layout is known not to depend on them.</summary>
    Names,

    /// <summary>Attributes are not compared.</summary>
    None,
}

/// <summary>Why a snapshot was not mapped onto a document.</summary>
public enum RefusalReason
{
    /// <summary>The requested snapshot document index does not exist.</summary>
    DocumentIndexOutOfRange,

    /// <summary>The OneHtml document is a fragment; only whole documents carry a snapshot.</summary>
    DocumentIsFragment,

    /// <summary>The snapshot document has no usable DOM (no nodes, or a node type this mapper does not know).</summary>
    MalformedSnapshot,

    /// <summary>The number of elements, text nodes or comments differs.</summary>
    NodeCountMismatch,

    /// <summary>The trees have the same counts but differ in shape or in an element name.</summary>
    StructureMismatch,

    /// <summary>A text node's characters differ.</summary>
    TextMismatch,

    /// <summary>An element's attributes differ (see <see cref="AttributeCheck"/>).</summary>
    AttributeMismatch,

    /// <summary>The structure fingerprints differ although the walk found no difference. Not expected; reported rather than ignored.</summary>
    FingerprintMismatch,
}

/// <summary>A precise statement of why a snapshot was refused.</summary>
/// <param name="Reason">The category.</param>
/// <param name="Message">A sentence saying what differs, with both sides' values.</param>
/// <param name="SnapshotNode">The snapshot node index where the difference was found, or -1.</param>
/// <param name="DocumentNode">The document node index where the difference was found, or -1.</param>
/// <param name="Path">The place in the document, such as <c>html[1] &gt; body[2] &gt; div[3]</c>, or an empty string.</param>
public sealed record SnapshotRefusal(RefusalReason Reason, string Message, int SnapshotNode, int DocumentNode, string Path)
{
    /// <summary>The message with its path.</summary>
    public override string ToString() => Path.Length == 0 ? $"{Reason}: {Message}" : $"{Reason}: {Message} (at {Path})";
}

/// <summary>
/// Where every node of the whole snapshot went. On success <see cref="IsComplete"/> is true: each node is mapped to a document node,
/// is a pseudo-element attached to its host, is inside a shadow tree, is a document-level node with no counterpart, or belongs to another document.
/// </summary>
/// <param name="Mapped">Snapshot nodes paired with a document node (the document, elements, text, comments, template content).</param>
/// <param name="Pseudo">Pseudo-element nodes and their descendants, attached to the host element as generated boxes.</param>
/// <param name="Shadow">Nodes inside shadow roots (user-agent shadow trees of form controls, and author shadow roots).</param>
/// <param name="Prolog">Document-level nodes other than the root element (doctype, comments before or after it) that the document did not pair.</param>
/// <param name="OtherDocuments">Nodes of the other snapshot documents (frames).</param>
/// <param name="LayoutItems">Layout items of the mapped snapshot document.</param>
/// <param name="LayoutMapped">Layout items that became a node's box.</param>
/// <param name="LayoutPseudo">Layout items that became a generated box.</param>
/// <param name="LayoutShadow">Layout items of nodes inside shadow trees (dropped).</param>
/// <param name="LayoutDuplicate">Layout items beyond the first for one node (dropped).</param>
/// <param name="TotalNodes">All nodes in the snapshot.</param>
public sealed record SnapshotAccounting(int Mapped, int Pseudo, int Shadow, int Prolog, int OtherDocuments, int LayoutItems, int LayoutMapped, int LayoutPseudo, int LayoutShadow, int LayoutDuplicate, int TotalNodes)
{
    /// <summary>Whether every node is in exactly one category and every layout item is placed or dropped on purpose.</summary>
    public bool IsComplete => Mapped + Pseudo + Shadow + Prolog + OtherDocuments == TotalNodes && LayoutMapped + LayoutPseudo + LayoutShadow + LayoutDuplicate == LayoutItems;
}

/// <summary>The outcome of <see cref="DomSnapshotAdapter.Map"/>.</summary>
public sealed class DomSnapshotMapResult
{
    internal DomSnapshotMapResult(RenderedFacts? facts, SnapshotRefusal? refusal, SnapshotAccounting? accounting)
    {
        Facts = facts;
        Refusal = refusal;
        Accounting = accounting;
    }

    /// <summary>Whether the snapshot was accepted.</summary>
    public bool Succeeded => Facts is not null;

    /// <summary>The facts when accepted.</summary>
    public RenderedFacts? Facts { get; }

    /// <summary>Why the snapshot was refused, when it was.</summary>
    public SnapshotRefusal? Refusal { get; }

    /// <summary>Where every snapshot node went, when accepted.</summary>
    public SnapshotAccounting? Accounting { get; }
}

/// <summary>Options for <see cref="DomSnapshotAdapter.Map"/>.</summary>
public sealed record DomSnapshotMapOptions
{
    /// <summary>Which snapshot document to map: 0 is the page; others are frames. Default 0.</summary>
    public int DocumentIndex { get; init; }

    /// <summary>How attributes are compared. Default <see cref="AttributeCheck.Values"/>.</summary>
    public AttributeCheck Attributes { get; init; } = AttributeCheck.Values;

    /// <summary>The viewport width the capture used, in CSS pixels; recorded in the facts. Default 0 (unknown).</summary>
    public double ViewportWidth { get; init; }

    /// <summary>The viewport height the capture used, in CSS pixels. Default 0 (unknown).</summary>
    public double ViewportHeight { get; init; }

    /// <summary>The style context the capture was taken under; recorded in the facts. Default <see langword="null"/>.</summary>
    public StyleContext? Context { get; init; }

    /// <summary>Background samples to attach to the facts. Entries that match no element are ignored here; use <see cref="RenderedFacts.WithBackgroundSamples"/> to see them.</summary>
    public BackgroundSamples? BackgroundSamples { get; init; }
}

/// <summary>
/// Maps a <see cref="DomSnapshot"/> onto the nodes of a OneHtml <see cref="Document"/> parsed from the same DOM, and refuses a
/// snapshot of a different DOM. The rules are in <c>docs/rendered-facts.md</c>.
/// </summary>
public static class DomSnapshotAdapter
{
    private const byte Main = 0, Prolog = 1, Pseudo = 2, Shadow = 3, TemplateRoot = 4;

    /// <summary>Maps one snapshot document onto a OneHtml document.</summary>
    /// <param name="snapshot">The parsed snapshot.</param>
    /// <param name="document">A document parsed from the DOM the snapshot was captured from: Chrome's serialized DOM, or the original bytes when no script ran.</param>
    /// <param name="options">Options; <see langword="null"/> means defaults.</param>
    /// <param name="cancellationToken">Checked every few thousand nodes.</param>
    /// <returns>The facts, or a precise refusal. A mismatched DOM is a refusal, not an exception.</returns>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static DomSnapshotMapResult Map(DomSnapshot snapshot, Document document, DomSnapshotMapOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(document);
        options ??= new DomSnapshotMapOptions();
        return new Mapper(snapshot, document, options, cancellationToken).Run();
    }

    /// <summary>The structure fingerprint of a document, as stored in <see cref="RenderedFacts.StructureFingerprint"/>.</summary>
    /// <param name="document">The document.</param>
    public static ulong StructureFingerprint(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return DocStats(document).Fingerprint;
    }

    /// <summary>The structure fingerprint of the page a snapshot document describes, comparable with <see cref="StructureFingerprint(Document)"/>.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="documentIndex">Which snapshot document; 0 is the page.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="documentIndex"/> does not exist.</exception>
    public static ulong StructureFingerprint(DomSnapshot snapshot, int documentIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if ((uint)documentIndex >= (uint)snapshot.Documents.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(documentIndex));
        }

        var m = new Mapper(snapshot, null, new DomSnapshotMapOptions { DocumentIndex = documentIndex }, default);
        m.Classify();
        return m.SnapshotStats().Fingerprint;
    }

    private readonly record struct Stats(int Elements, int Texts, int Comments, ulong Fingerprint);

    private const ulong FnvOffset = 14695981039346656037UL, FnvPrime = 1099511628211UL;

    private static ulong Feed(ulong h, ulong x) => (h ^ x) * FnvPrime;

    private static ulong FeedNode(ulong h, int depth, int kind, ReadOnlySpan<char> name)
    {
        h = Feed(h, (ulong)depth);
        h = Feed(h, (ulong)kind);
        foreach (char c in name)
        {
            h = Feed(h, c is >= 'A' and <= 'Z' ? (ulong)(c + 32) : c);
        }

        return Feed(h, 0xFFFF);
    }

    private static Stats DocStats(Document doc)
    {
        Node el = default;
        for (Node c = doc.Root.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == NodeKind.Element)
            {
                el = c;
                break;
            }
        }

        int elements = 0, texts = 0, comments = 0;
        ulong h = FnvOffset;
        if (el.IsNull)
        {
            return new Stats(0, 0, 0, h);
        }

        Node cur = el;
        int depth = 1;
        bool done = false;
        while (!done)
        {
            switch (cur.Kind)
            {
                case NodeKind.Element:
                    elements++;
                    h = FeedNode(h, depth, 1, cur.LocalName);
                    break;
                case NodeKind.Text:
                    texts++;
                    h = FeedNode(h, depth, 3, default);
                    break;
                case NodeKind.Comment:
                case NodeKind.ProcessingInstruction:
                    comments++;
                    break;
            }

            Node fc = cur.FirstChild;
            if (!fc.IsNull)
            {
                cur = fc;
                depth++;
                continue;
            }

            while (true)
            {
                if (cur.Index == el.Index)
                {
                    done = true;
                    break;
                }

                Node ns = cur.NextSibling;
                if (!ns.IsNull)
                {
                    cur = ns;
                    break;
                }

                cur = cur.Parent;
                depth--;
                if (cur.Index == el.Index)
                {
                    done = true;
                    break;
                }
            }
        }

        return new Stats(elements, texts, comments, h);
    }

    private static bool AsciiEquals(string a, ReadOnlySpan<char> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            char x = a[i], y = b[i];
            if (x == y)
            {
                continue;
            }

            if (x is >= 'A' and <= 'Z')
            {
                x = (char)(x + 32);
            }

            if (y is >= 'A' and <= 'Z')
            {
                y = (char)(y + 32);
            }

            if (x != y)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class Mapper(DomSnapshot snapshot, Document? document, DomSnapshotMapOptions options, CancellationToken ct)
    {
        private readonly DomSnapshotDocument _sd = snapshot.Documents.Count > options.DocumentIndex && options.DocumentIndex >= 0 ? snapshot.Documents[options.DocumentIndex] : null!;
        private DomSnapshotDocumentData D => _sd.Data;

        private int _n;
        private byte[] _cat = [];
        private int[] _pseudoRoot = [], _firstKid = [], _nextKid = [], _lastKid = [], _pseudoTypeStr = [], _pseudoIdentStr = [];
        private bool[] _inTemplate = [];
        private int _prolog;
        private int _tick;

        private string Name(int i) => snapshot.Str(D.NodeName[i]);

        private void Tick()
        {
            if ((++_tick & 8191) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
        }

        /// <summary>Assigns every snapshot node of the mapped document to a category and links the nodes that are compared against the document.</summary>
        public void Classify()
        {
            DomSnapshotDocumentData d = D;
            _n = d.NodeType.Length;
            int n = _n;
            _cat = new byte[n];
            _pseudoRoot = new int[n];
            _firstKid = new int[n];
            _nextKid = new int[n];
            _lastKid = new int[n];
            _inTemplate = new bool[n];
            Array.Fill(_firstKid, -1);
            Array.Fill(_nextKid, -1);
            Array.Fill(_lastKid, -1);
            Array.Fill(_pseudoRoot, -1);

            _pseudoTypeStr = Expand(d.PseudoType, n);
            _pseudoIdentStr = Expand(d.PseudoIdentifier, n);
            var shadowRoot = new bool[n];
            for (int k = 0; k < d.ShadowRootType.Index.Length; k++)
            {
                if (snapshot.Str(d.ShadowRootType.Value[k]).Length > 0)
                {
                    shadowRoot[d.ShadowRootType.Index[k]] = true;
                }
            }

            for (int i = 0; i < n; i++)
            {
                Tick();
                int p = d.ParentIndex[i];
                if (p < 0)
                {
                    _cat[i] = Main;
                    continue;
                }

                byte pc = _cat[p];
                bool isPseudo = _pseudoTypeStr[i] >= 0 || Name(i).StartsWith("::", StringComparison.Ordinal);
                if (pc == Shadow)
                {
                    _cat[i] = Shadow;
                }
                else if (pc == Pseudo)
                {
                    _cat[i] = Pseudo;
                    _pseudoRoot[i] = _pseudoRoot[p];
                }
                else if (shadowRoot[i])
                {
                    _cat[i] = Shadow;
                }
                else if (isPseudo)
                {
                    _cat[i] = Pseudo;
                    _pseudoRoot[i] = i;
                }
                else if (d.NodeType[i] == 11 && pc == Main && d.NodeType[p] == 1 && AsciiEquals(Name(p), "template"))
                {
                    _cat[i] = TemplateRoot;
                }
                else if (pc == Main && d.NodeType[p] == 9 && d.NodeType[i] != 1)
                {
                    _cat[i] = Prolog;
                    _prolog++;
                }
                else
                {
                    _cat[i] = Main;
                }

                _inTemplate[i] = _inTemplate[p] || _cat[i] == TemplateRoot;
                if (_cat[i] is Main or TemplateRoot)
                {
                    if (_lastKid[p] < 0)
                    {
                        _firstKid[p] = i;
                    }
                    else
                    {
                        _nextKid[_lastKid[p]] = i;
                    }

                    _lastKid[p] = i;
                }
            }
        }

        private static int[] Expand(Rare r, int n)
        {
            var a = new int[n];
            Array.Fill(a, -1);
            for (int k = 0; k < r.Index.Length; k++)
            {
                a[r.Index[k]] = r.Value.Length > 0 ? r.Value[k] : 0;
            }

            return a;
        }

        private int FirstMainKid(int p)
        {
            int k = _firstKid[p];
            while (k >= 0 && _cat[k] != Main)
            {
                k = _nextKid[k];
            }

            return k;
        }

        private int NextMainKid(int k)
        {
            k = _nextKid[k];
            while (k >= 0 && _cat[k] != Main)
            {
                k = _nextKid[k];
            }

            return k;
        }

        public Stats SnapshotStats()
        {
            int root = 0;
            if (_n == 0 || _cat.Length == 0)
            {
                return new Stats(0, 0, 0, FnvOffset);
            }

            int el = FirstMainKid(root);
            int elements = 0, texts = 0, comments = 0;
            ulong h = FnvOffset;
            if (el < 0)
            {
                return new Stats(0, 0, 0, h);
            }

            int cur = el;
            int depth = 1;
            bool done = false;
            while (!done)
            {
                Tick();
                switch (D.NodeType[cur])
                {
                    case 1:
                        elements++;
                        h = FeedNode(h, depth, 1, Name(cur));
                        break;
                    case 3 or 4:
                        texts++;
                        h = FeedNode(h, depth, 3, default);
                        break;
                    case 8 or 7:
                        comments++;
                        break;
                }

                int fk = FirstMainKid(cur);
                if (fk >= 0)
                {
                    cur = fk;
                    depth++;
                    continue;
                }

                while (true)
                {
                    if (cur == el)
                    {
                        done = true;
                        break;
                    }

                    int nk = NextMainKid(cur);
                    if (nk >= 0)
                    {
                        cur = nk;
                        break;
                    }

                    cur = D.ParentIndex[cur];
                    depth--;
                    if (cur == el)
                    {
                        done = true;
                        break;
                    }
                }
            }

            return new Stats(elements, texts, comments, h);
        }

        private static DomSnapshotMapResult Refuse(RefusalReason reason, string message, int sn = -1, int dn = -1, string path = "") =>
            new(null, new SnapshotRefusal(reason, message, sn, dn, path), null);

        public DomSnapshotMapResult Run()
        {
            Document doc = document!;
            if (_sd is null)
            {
                return Refuse(RefusalReason.DocumentIndexOutOfRange, $"The snapshot has {snapshot.Documents.Count} documents; index {options.DocumentIndex} does not exist.");
            }

            if (doc.IsFragment)
            {
                return Refuse(RefusalReason.DocumentIsFragment, "The document is a fragment; a snapshot describes a whole document.");
            }

            DomSnapshotDocumentData d = D;
            if (d.NodeType.Length == 0 || d.NodeType[0] != 9)
            {
                return Refuse(RefusalReason.MalformedSnapshot, "The snapshot document does not start with a document node.");
            }

            Classify();
            int n = _n;

            // Counts per kind first: the cheapest way to tell two different pages apart.
            Stats snap = SnapshotStats();
            Stats docStats = DocStats(doc);
            if (snap.Elements != docStats.Elements || snap.Texts != docStats.Texts || snap.Comments != docStats.Comments)
            {
                return Refuse(
                    RefusalReason.NodeCountMismatch,
                    $"The snapshot has {snap.Elements} elements, {snap.Texts} text nodes and {snap.Comments} comments; the document has {docStats.Elements}, {docStats.Texts} and {docStats.Comments}.");
            }

            // Then pair the trees node by node; this finds the first difference in document order and fills the node map.
            var snapToDoc = new int[n];
            Array.Fill(snapToDoc, -1);
            var docToSnap = new int[doc.NodeCount];
            Array.Fill(docToSnap, -1);
            DomSnapshotMapResult? refusal = Align(doc, snapToDoc, docToSnap);
            if (refusal is not null)
            {
                return refusal;
            }

            if (snap.Fingerprint != docStats.Fingerprint)
            {
                return Refuse(RefusalReason.FingerprintMismatch, $"The structure fingerprints differ ({snap.Fingerprint:x16} and {docStats.Fingerprint:x16}).");
            }

            return Build(doc, snapToDoc, docToSnap, docStats.Fingerprint);
        }

        private DomSnapshotMapResult? Align(Document doc, int[] snapToDoc, int[] docToSnap)
        {
            DomSnapshotDocumentData d = D;
            var stack = new List<(int S, int D)>();
            var level = new List<(int S, int D)>();

            snapToDoc[0] = doc.Root.Index;
            docToSnap[doc.Root.Index] = 0;
            stack.Add((0, doc.Root.Index));

            while (stack.Count > 0)
            {
                (int sp, int dpIndex) = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                Node dp = doc.GetNode(dpIndex);
                level.Clear();

                // Template content hangs off the template element, not among its children.
                for (int k = _firstKid[sp]; k >= 0; k = _nextKid[k])
                {
                    if (_cat[k] != TemplateRoot)
                    {
                        continue;
                    }

                    Node content = dp.TemplateContent;
                    if (content.IsNull)
                    {
                        return Refuse(RefusalReason.StructureMismatch, "The snapshot has template content under this element; the document element has none.", k, dp.Index, Path(dp));
                    }

                    snapToDoc[k] = content.Index;
                    docToSnap[content.Index] = k;
                    level.Add((k, content.Index));
                }

                // At the document level only the root element is paired; a doctype or comment before or after it has no counterpart.
                bool docLevel = dp.Kind == NodeKind.Document;
                int s = FirstMainKid(sp);
                Node dn = docLevel ? SkipToElement(dp.FirstChild) : dp.FirstChild;
                int position = 0;
                while (s >= 0 || !dn.IsNull)
                {
                    Tick();
                    position++;
                    if (s < 0 || dn.IsNull)
                    {
                        return Refuse(
                            RefusalReason.StructureMismatch,
                            $"The snapshot has {CountMainKids(sp)} children here and the document has {CountChildren(dp, docLevel)}.",
                            s < 0 ? sp : s,
                            dn.IsNull ? dp.Index : dn.Index,
                            Path(dp));
                    }

                    DomSnapshotMapResult? bad = ComparePair(s, dn, position, dp);
                    if (bad is not null)
                    {
                        return bad;
                    }

                    snapToDoc[s] = dn.Index;
                    docToSnap[dn.Index] = s;
                    if (d.NodeType[s] == 1)
                    {
                        level.Add((s, dn.Index));
                    }

                    s = NextMainKid(s);
                    dn = docLevel ? SkipToElement(dn.NextSibling) : dn.NextSibling;
                }

                for (int i = level.Count - 1; i >= 0; i--)
                {
                    stack.Add(level[i]);
                }
            }

            return null;
        }

        private DomSnapshotMapResult? ComparePair(int s, Node dn, int position, Node dParent)
        {
            DomSnapshotDocumentData d = D;
            int type = d.NodeType[s];
            NodeKind kind = dn.Kind;
            switch (type)
            {
                case 1:
                    if (kind != NodeKind.Element)
                    {
                        return Refuse(RefusalReason.StructureMismatch, $"Child {position}: the snapshot has the element <{Name(s).ToLowerInvariantAscii()}>; the document has {Describe(dn)}.", s, dn.Index, Path(dn));
                    }

                    if (!AsciiEquals(Name(s), dn.LocalName))
                    {
                        return Refuse(RefusalReason.StructureMismatch, $"Child {position}: the snapshot has <{Name(s).ToLowerInvariantAscii()}>; the document has <{dn.LocalName}>.", s, dn.Index, Path(dn));
                    }

                    string? attr = CompareAttributes(s, dn);
                    return attr is null ? null : Refuse(RefusalReason.AttributeMismatch, attr, s, dn.Index, Path(dn));

                case 3 or 4:
                    if (kind != NodeKind.Text)
                    {
                        return Refuse(RefusalReason.StructureMismatch, $"Child {position}: the snapshot has a text node; the document has {Describe(dn)}.", s, dn.Index, Path(dn));
                    }

                    string value = snapshot.Str(d.NodeValue.Length == 0 ? -1 : d.NodeValue[s]);
                    if (!value.AsSpan().SequenceEqual(dn.Data))
                    {
                        return Refuse(RefusalReason.TextMismatch, $"Text differs: the snapshot has \"{Clip(value)}\"; the document has \"{Clip(dn.Data.ToString())}\".", s, dn.Index, Path(dn));
                    }

                    return null;

                case 8 or 7:
                    return kind is NodeKind.Comment or NodeKind.ProcessingInstruction
                        ? null
                        : Refuse(RefusalReason.StructureMismatch, $"Child {position}: the snapshot has a comment; the document has {Describe(dn)}.", s, dn.Index, Path(dn));

                default:
                    return Refuse(RefusalReason.MalformedSnapshot, $"Snapshot node {s} has node type {type}, which a parsed HTML document does not contain here.", s, dn.Index, Path(dn));
            }
        }

        private static string Clip(string s)
        {
            string t = s.Length <= 40 ? s : s[..40] + "...";
            return t.Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);
        }

        private static string Describe(Node n) => n.Kind switch
        {
            NodeKind.Element => "<" + n.LocalName + ">",
            NodeKind.Text => "a text node",
            NodeKind.Comment => "a comment",
            _ => n.Kind.ToString(),
        };

        private int CountMainKids(int p)
        {
            int c = 0;
            for (int k = FirstMainKid(p); k >= 0; k = NextMainKid(k))
            {
                c++;
            }

            return c;
        }

        private static Node SkipToElement(Node n)
        {
            while (!n.IsNull && n.Kind != NodeKind.Element)
            {
                n = n.NextSibling;
            }

            return n;
        }

        private static int CountChildren(Node p, bool elementsOnly)
        {
            int c = 0;
            for (Node k = p.FirstChild; !k.IsNull; k = k.NextSibling)
            {
                if (!elementsOnly || k.Kind == NodeKind.Element)
                {
                    c++;
                }
            }

            return c;
        }

        private static string Path(Node n)
        {
            if (n.IsNull)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            for (Node c = n; !c.IsNull && c.Kind != NodeKind.Document && c.Kind != NodeKind.DocumentFragment; c = c.Parent)
            {
                if (parts.Count >= 32)
                {
                    parts.Add("...");
                    break;
                }

                int idx = 1;
                for (Node sib = c.PreviousSibling; !sib.IsNull; sib = sib.PreviousSibling)
                {
                    // Elements are numbered among their element siblings, like :nth-child; other nodes among all siblings.
                    if (c.Kind != NodeKind.Element || sib.Kind == NodeKind.Element)
                    {
                        idx++;
                    }
                }

                parts.Add((c.Kind == NodeKind.Element ? c.LocalName : "#" + c.Kind.ToString().ToLowerInvariantAscii()) + "[" + idx + "]");
            }

            parts.Reverse();
            return string.Join(" > ", parts);
        }

        private string? CompareAttributes(int s, Node dn)
        {
            DomSnapshotDocumentData d = D;
            if (options.Attributes == AttributeCheck.None || d.AttrOffsets.Length != _n + 1)
            {
                return null;
            }

            int a = d.AttrOffsets[s];
            int b = d.AttrOffsets[s + 1];
            int snapCount = (b - a) / 2;
            AttributeList attrs = dn.Attributes;
            if (snapCount != attrs.Count)
            {
                return $"The snapshot element has {snapCount} attributes; the document element has {attrs.Count}.";
            }

            Dictionary<string, int>? index = null;
            if (snapCount > 8)
            {
                index = new Dictionary<string, int>(snapCount, StringComparer.Ordinal);
                for (int k = 0; k < snapCount; k++)
                {
                    index[snapshot.Str(d.AttrData[a + (2 * k)])] = d.AttrData[a + (2 * k) + 1];
                }
            }

            for (int i = 0; i < attrs.Count; i++)
            {
                Attr attr = attrs[i];
                string name = attr.QualifiedName;
                int valueIdx = -2;
                if (index is not null)
                {
                    if (index.TryGetValue(name, out int v))
                    {
                        valueIdx = v;
                    }
                }
                else
                {
                    for (int k = 0; k < snapCount; k++)
                    {
                        if (string.Equals(snapshot.Str(d.AttrData[a + (2 * k)]), name, StringComparison.Ordinal))
                        {
                            valueIdx = d.AttrData[a + (2 * k) + 1];
                            break;
                        }
                    }
                }

                if (valueIdx == -2)
                {
                    return $"The document element has the attribute \"{Clip(name)}\"; the snapshot element does not.";
                }

                if (options.Attributes == AttributeCheck.Values)
                {
                    string snapValue = snapshot.Str(valueIdx);
                    if (!snapValue.AsSpan().SequenceEqual(attr.Value))
                    {
                        return $"The attribute \"{Clip(name)}\" is \"{Clip(snapValue)}\" in the snapshot and \"{Clip(attr.Value.ToString())}\" in the document.";
                    }
                }
            }

            return null;
        }

        // ---- building the facts ----

        private sealed class StringPool(string[] source)
        {
            private readonly Dictionary<int, int> _byOld = [];
            private readonly Dictionary<string, int> _byText = new(StringComparer.Ordinal);

            public List<string> List { get; } = [];

            public int Old(int index)
            {
                if (index < 0)
                {
                    return -1;
                }

                if (_byOld.TryGetValue(index, out int n))
                {
                    return n;
                }

                n = New(source[index]);
                _byOld[index] = n;
                return n;
            }

            public int New(string text)
            {
                if (_byText.TryGetValue(text, out int n))
                {
                    return n;
                }

                n = List.Count;
                List.Add(text);
                _byText[text] = n;
                return n;
            }
        }

        private sealed class Gen
        {
            public int Host, Root, Type, Ident, Nodes;
            public List<int> Boxes = [];
            public System.Text.StringBuilder? Text;
            public int RootBox = -1;
        }

        private DomSnapshotMapResult Build(Document doc, int[] snapToDoc, int[] docToSnap, ulong fingerprint)
        {
            DomSnapshotDocumentData d = D;
            int n = _n;
            int nodeCount = doc.NodeCount;
            var pool = new StringPool(snapshot.Strings);
            var f = new FactsData
            {
                DocumentId = doc.Id,
                NodeCount = nodeCount,
                Fingerprint = fingerprint,
                Product = snapshot.Product,
                ProtocolVersion = snapshot.ProtocolVersion,
                ViewportWidth = options.ViewportWidth,
                ViewportHeight = options.ViewportHeight,
                Context = options.Context,
                Page = new PageFacts(_sd.DocumentUrl, _sd.BaseUrl, _sd.Title, _sd.FrameId, _sd.ContentLanguage, _sd.EncodingName, _sd.ContentWidth, _sd.ContentHeight, _sd.ScrollOffsetX, _sd.ScrollOffsetY),
                NodeBox = new int[nodeCount],
                NodeSnapshot = docToSnap,
                NodeStackRoot = new int[nodeCount],
                NodeBackend = new long[nodeCount],
                Flags = new byte[nodeCount],
            };
            Array.Fill(f.NodeBox, -1);
            Array.Fill(f.NodeStackRoot, -1);
            Array.Fill(f.NodeBackend, -1L);
            if (d.BackendNodeId.Length == n)
            {
                for (int i = 0; i < nodeCount; i++)
                {
                    if (docToSnap[i] >= 0)
                    {
                        f.NodeBackend[i] = d.BackendNodeId[docToSnap[i]];
                    }
                }
            }

            string[] styleNames = [.. snapshot.ComputedStyleNames];
            f.StyleNames = styleNames;
            int stride = styleNames.Length;
            f.StyleStride = stride;
            for (int i = 0; i < styleNames.Length; i++)
            {
                f.StyleIndex[styleNames[i]] = i;
            }

            // Pseudo-elements: one entry per pseudo root whose host is a mapped node.
            var gens = new List<Gen>();
            var genOfRoot = new Dictionary<int, Gen>();
            int pseudoNodes = 0, shadowNodes = 0, prologNodes = 0, mapped = 0;
            for (int i = 0; i < n; i++)
            {
                Tick();
                switch (_cat[i])
                {
                    case Pseudo:
                        pseudoNodes++;
                        break;
                    case Shadow:
                        shadowNodes++;
                        break;
                    case Prolog:
                        prologNodes++;
                        break;
                    default:
                        if (snapToDoc[i] >= 0)
                        {
                            mapped++;
                        }

                        break;
                }

                if (_cat[i] == Pseudo && _pseudoRoot[i] == i)
                {
                    int host = snapToDoc[d.ParentIndex[i]];
                    if (host < 0)
                    {
                        continue;
                    }

                    string type = _pseudoTypeStr[i] >= 0 ? snapshot.Str(_pseudoTypeStr[i]) : Name(i)[2..];
                    var g = new Gen
                    {
                        Host = host,
                        Root = i,
                        Type = pool.New(type),
                        Ident = _pseudoIdentStr[i] >= 0 ? pool.Old(_pseudoIdentStr[i]) : -1,
                    };
                    gens.Add(g);
                    genOfRoot[i] = g;
                }
            }

            // Layout items become boxes: pass one decides which ones are kept.
            int l = d.LayoutNodeIndex.Length;
            var liToBox = new int[l];
            Array.Fill(liToBox, -1);
            int boxes = 0, layoutMapped = 0, layoutPseudo = 0, layoutShadow = 0, layoutDup = 0;
            for (int li = 0; li < l; li++)
            {
                Tick();
                int sn = d.LayoutNodeIndex[li];
                int dn = snapToDoc[sn];
                if (dn >= 0)
                {
                    if (f.NodeBox[dn] >= 0)
                    {
                        layoutDup++;
                        continue;
                    }

                    f.NodeBox[dn] = boxes;
                    liToBox[li] = boxes++;
                    layoutMapped++;
                }
                else if (_cat[sn] == Pseudo)
                {
                    if (!genOfRoot.TryGetValue(_pseudoRoot[sn], out Gen? g))
                    {
                        // The host is not a mapped node (it sits in a shadow tree): nothing to attach to.
                        layoutShadow++;
                        continue;
                    }

                    // Chrome reports a pseudo-element's own box and then, under the same node index, the anonymous text box that holds its
                    // generated text (layoutText set). The first item is the element's box; later ones are its generated children.
                    if (sn == g.Root && g.RootBox < 0)
                    {
                        g.RootBox = boxes;
                    }

                    g.Boxes.Add(boxes);
                    liToBox[li] = boxes++;
                    layoutPseudo++;
                }
                else
                {
                    layoutShadow++;
                }
            }

            foreach (Gen g in gens)
            {
                g.Nodes = 0;
            }

            for (int i = 0; i < n; i++)
            {
                if (_cat[i] == Pseudo && genOfRoot.TryGetValue(_pseudoRoot[i], out Gen? g))
                {
                    g.Nodes++;
                }
            }

            // Pass two fills the box columns.
            f.BoxCount = boxes;
            f.Bounds = new double[boxes * 4];
            f.Client = d.ClientRects.Length == 0 ? [] : new double[boxes * 4];
            f.Offset = d.OffsetRects.Length == 0 ? [] : new double[boxes * 4];
            f.Scroll = d.ScrollRects.Length == 0 ? [] : new double[boxes * 4];
            f.TextOpacity = new double[boxes];
            f.Paint = new int[boxes];
            f.Text = new int[boxes];
            f.Blended = new int[boxes];
            f.Stacking = new byte[boxes];
            f.StyleValues = stride == 0 ? [] : new int[boxes * stride];
            Array.Fill(f.StyleValues, -1);
            Array.Fill(f.Bounds, double.NaN);

            var isStacking = new bool[l];
            foreach (int li in d.StackingContexts)
            {
                isStacking[li] = true;
            }

            for (int li = 0; li < l; li++)
            {
                int b = liToBox[li];
                if (b < 0)
                {
                    continue;
                }

                if (d.Bounds.Length > 0)
                {
                    Array.Copy(d.Bounds, li * 4, f.Bounds, b * 4, 4);
                }

                if (f.Client.Length > 0)
                {
                    Array.Copy(d.ClientRects, li * 4, f.Client, b * 4, 4);
                }

                if (f.Offset.Length > 0)
                {
                    Array.Copy(d.OffsetRects, li * 4, f.Offset, b * 4, 4);
                }

                if (f.Scroll.Length > 0)
                {
                    Array.Copy(d.ScrollRects, li * 4, f.Scroll, b * 4, 4);
                }

                f.TextOpacity[b] = d.TextColorOpacities.Length == 0 ? double.NaN : d.TextColorOpacities[li];
                f.Paint[b] = d.PaintOrders.Length == 0 ? -1 : d.PaintOrders[li];
                f.Text[b] = d.LayoutText.Length == 0 ? -1 : pool.Old(d.LayoutText[li]);
                f.Blended[b] = d.BlendedBackground.Length == 0 ? -1 : pool.Old(d.BlendedBackground[li]);
                f.Stacking[b] = isStacking[li] ? (byte)1 : (byte)0;
                if (stride > 0 && d.StyleOffsets.Length == l + 1)
                {
                    int so = d.StyleOffsets[li];
                    if (d.StyleOffsets[li + 1] - so == stride)
                    {
                        for (int k = 0; k < stride; k++)
                        {
                            f.StyleValues[(b * stride) + k] = pool.Old(d.StyleData[so + k]);
                        }
                    }
                }

                // Text of generated content collects on the pseudo-element.
                int sn = d.LayoutNodeIndex[li];
                if (snapToDoc[sn] < 0 && _cat[sn] == Pseudo && genOfRoot.TryGetValue(_pseudoRoot[sn], out Gen? g) && f.Text[b] >= 0)
                {
                    (g.Text ??= new System.Text.StringBuilder()).Append(pool.List[f.Text[b]]);
                }
            }

            // Text boxes, grouped by owning box and kept in the order Chrome gave them.
            int tbTotal = d.TextBoxLayoutIndex.Length;
            f.TbStart = new int[boxes + 1];
            for (int t = 0; t < tbTotal; t++)
            {
                int b = liToBox[d.TextBoxLayoutIndex[t]];
                if (b >= 0)
                {
                    f.TbStart[b + 1]++;
                }
            }

            for (int b = 0; b < boxes; b++)
            {
                f.TbStart[b + 1] += f.TbStart[b];
            }

            f.TextBoxes = new TextBox[f.TbStart[boxes]];
            var fill = new int[boxes];
            for (int t = 0; t < tbTotal; t++)
            {
                int b = liToBox[d.TextBoxLayoutIndex[t]];
                if (b < 0)
                {
                    continue;
                }

                int o = t * 4;
                f.TextBoxes[f.TbStart[b] + fill[b]++] = new TextBox(d.TextBoxStart[t], d.TextBoxLength[t], new LayoutRect(d.TextBoxBounds[o], d.TextBoxBounds[o + 1], d.TextBoxBounds[o + 2], d.TextBoxBounds[o + 3]));
            }

            // Stacking context membership: the nearest ancestor-or-self whose box begins one.
            var rootSnap = new int[n];
            Array.Fill(rootSnap, -1);
            for (int i = 0; i < n; i++)
            {
                int dn = snapToDoc[i];
                if (dn < 0)
                {
                    continue;
                }

                int p = d.ParentIndex[i];
                int b = f.NodeBox[dn];
                rootSnap[i] = b >= 0 && f.Stacking[b] != 0 ? i : (p >= 0 ? rootSnap[p] : -1);
                f.NodeStackRoot[dn] = rootSnap[i] >= 0 ? snapToDoc[rootSnap[i]] : -1;
            }

            // Generated boxes, ordered by host (then snapshot order).
            gens.Sort(static (x, y) => x.Host != y.Host ? x.Host.CompareTo(y.Host) : x.Root.CompareTo(y.Root));
            int gc = gens.Count;
            f.GenHost = new int[gc];
            f.GenType = new int[gc];
            f.GenIdent = new int[gc];
            f.GenBox = new int[gc];
            f.GenText = new int[gc];
            f.GenNodes = new int[gc];
            var genBoxLists = new int[gc][];
            for (int i = 0; i < gc; i++)
            {
                Gen g = gens[i];
                f.GenHost[i] = g.Host;
                f.GenType[i] = g.Type;
                f.GenIdent[i] = g.Ident;
                f.GenBox[i] = g.RootBox;
                f.GenText[i] = g.Text is null ? -1 : pool.New(g.Text.ToString());
                f.GenNodes[i] = g.Nodes;
                genBoxLists[i] = [.. g.Boxes];
            }

            f.GenBoxes = genBoxLists;

            // Per-node form state, frames.
            for (int k = 0; k < d.InputValue.Index.Length; k++)
            {
                int dn = snapToDoc[d.InputValue.Index[k]];
                if (dn >= 0)
                {
                    f.InputValue[dn] = pool.Old(d.InputValue.Value[k]);
                }
            }

            for (int k = 0; k < d.TextValue.Index.Length; k++)
            {
                int dn = snapToDoc[d.TextValue.Index[k]];
                if (dn >= 0)
                {
                    f.TextValue[dn] = pool.Old(d.TextValue.Value[k]);
                }
            }

            SetFlag(d.InputChecked, snapToDoc, f.Flags, FactsData.FlagChecked);
            SetFlag(d.OptionSelected, snapToDoc, f.Flags, FactsData.FlagSelected);
            SetFlag(d.IsClickable, snapToDoc, f.Flags, FactsData.FlagClickable);

            var frames = new List<FrameLink>();
            for (int k = 0; k < d.ContentDocumentIndex.Index.Length; k++)
            {
                int dn = snapToDoc[d.ContentDocumentIndex.Index[k]];
                int target = d.ContentDocumentIndex.Value[k];
                if (dn >= 0 && target >= 0)
                {
                    frames.Add(new FrameLink(dn, target));
                }
            }

            frames.Sort(static (x, y) => x.NodeIndex.CompareTo(y.NodeIndex));
            f.Frames = [.. frames];

            f.Strings = [.. pool.List];

            int total = snapshot.TotalNodeCount;
            var accounting = new SnapshotAccounting(mapped, pseudoNodes, shadowNodes, prologNodes, total - n, l, layoutMapped, layoutPseudo, layoutShadow, layoutDup, total);
            if (!accounting.IsComplete)
            {
                return Refuse(RefusalReason.MalformedSnapshot, $"Internal accounting failed: {mapped} mapped + {pseudoNodes} pseudo + {shadowNodes} shadow + {prologNodes} prolog + {total - n} other of {total} nodes.");
            }

            var facts = new RenderedFacts(f);
            if (options.BackgroundSamples is { } samples)
            {
                facts = facts.WithBackgroundSamples(samples).Facts;
            }

            return new DomSnapshotMapResult(facts, null, accounting);
        }

        private static void SetFlag(Rare r, int[] snapToDoc, byte[] flags, byte flag)
        {
            foreach (int s in r.Index)
            {
                int dn = snapToDoc[s];
                if (dn >= 0)
                {
                    flags[dn] |= flag;
                }
            }
        }
    }
}

internal static class AsciiString
{
    public static string ToLowerInvariantAscii(this string s)
    {
        bool any = false;
        foreach (char c in s)
        {
            if (c is >= 'A' and <= 'Z')
            {
                any = true;
                break;
            }
        }

        if (!any)
        {
            return s;
        }

        return string.Create(s.Length, s, static (span, src) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                char c = src[i];
                span[i] = c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
            }
        });
    }
}
