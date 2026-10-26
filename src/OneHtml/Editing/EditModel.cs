using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Dom;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>Which part of a node an edit changed; a changed part is rewritten as one unit.</summary>
internal enum RegionKind : byte
{
    /// <summary>The element's attribute list.</summary>
    Attrs = 0,

    /// <summary>The list of an element's children.</summary>
    Children = 1,

    /// <summary>The characters of a text node.</summary>
    Text = 2,
}

internal readonly record struct RegionKey(RegionKind Kind, int Node);

/// <summary>An attribute of the working tree: the name as stored, its namespace, its decoded value, and the base row it came from (-1 when new).</summary>
internal struct ModelAttr
{
    public int Name;
    public AttributeNamespace Namespace;
    public string Value;
    public int Orig;
}

/// <summary>
/// The tree an edit transaction is applied to. It starts as a copy of the snapshot's columns (the node arrays are
/// copied whole; text and attribute values stay shared) and is changed in place by each edit. The original source
/// ranges stay on the copied nodes, so the splice builder can find the bytes of every node that was there before;
/// nodes inserted by edits live past the end of the original rows.
/// </summary>
internal sealed partial class EditModel
{
    public const ushort Dead = 0x1000;
    public const ushort Unwrapped = 0x2000;

    private readonly Dictionary<int, List<ModelAttr>> _attrs = [];
    private readonly HashSet<int> _attrsToFlush = [];
    private Dictionary<int, int>? _contentOwner;
    private List<RegionKey> _touched = [];

    public EditModel(Document baseDocument, int editCount)
    {
        Base = baseDocument;
        BA = baseDocument.Arena;
        BaseCount = BA.Count;
        BaseAttrTotal = BA.AttrTotal;
        Limits = baseDocument.Source.Limits;
        A = CloneArena(BA, Limits);
        Names = baseDocument.Names.Clone();
        View = new Document(A, Names, baseDocument.Source, baseDocument.Mode, false, baseDocument.DocumentElementIndex, baseDocument.DoctypeIndex, baseDocument.DoctypePublicId, baseDocument.DoctypeSystemId, [])
        {
            ScriptingEnabled = baseDocument.ScriptingEnabled,
        };
        TouchedBy = new List<RegionKey>[editCount];
    }

    public Document Base { get; }

    public Arena BA { get; }

    public Arena A { get; }

    public NameTable Names { get; }

    /// <summary>A document over the working columns, for the serializer and the comparison.</summary>
    public Document View { get; }

    public int BaseCount { get; }

    public int BaseAttrTotal { get; }

    public ParseLimits Limits { get; }

    /// <summary>The regions each edit touched, by edit index.</summary>
    public List<RegionKey>[] TouchedBy { get; }

    /// <summary>Every touched region and the edits that touched it.</summary>
    public Dictionary<RegionKey, List<int>> Regions { get; } = [];

    /// <summary>Text nodes edited by splicing parts of their text (stylesheet text), by node.</summary>
    public Dictionary<int, IReadOnlyList<TextSplice>> TextSplices { get; } = [];

    /// <summary>Elements whose <c>style</c> attribute was changed by a style edit.</summary>
    public HashSet<int> StyleEdited { get; } = [];

    /// <summary>Original nodes that were moved under a node an edit created.</summary>
    public List<int> Wrapped { get; } = [];

    /// <summary>Original nodes that were given a new parent.</summary>
    public HashSet<int> Moved { get; } = [];

    public int CurrentEdit { get; set; }

    public bool IsGone(int i) => (A.Flags[i] & (Dead | Unwrapped)) != 0;

    public bool IsText(int i) => A.Kind[i] == (byte)NodeKind.Text;

    public bool IsElement(int i) => A.Kind[i] == (byte)NodeKind.Element;

    public ReadOnlySpan<char> DataOf(int i) => new Node(View, i).Data;

    /// <summary>The node whose children list holds the children: a template's content for a template, the node itself otherwise.</summary>
    public int Holder(int i) =>
        A.Kind[i] == (byte)NodeKind.Element && A.Ns[i] == (byte)ElementNamespace.Html && A.Name[i] == N.Template && A.Aux[i] >= 0 ? A.Aux[i] : i;

    public bool IsVoid(int i) =>
        A.Kind[i] == (byte)NodeKind.Element && A.Ns[i] == (byte)ElementNamespace.Html && HtmlNames.IsVoid(A.Name[i]);

    /// <summary>The parent, or for a template's content root the template.</summary>
    public int ParentOrOwner(int i)
    {
        int p = A.Parent[i];
        if (p >= 0)
        {
            return p;
        }

        return A.Kind[i] == (byte)NodeKind.DocumentFragment ? ContentOwner(i) : -1;
    }

    public int ContentOwner(int content)
    {
        if (_contentOwner is null)
        {
            _contentOwner = [];
            for (int i = 0; i < A.Count; i++)
            {
                if (A.Kind[i] == (byte)NodeKind.Element && A.Aux[i] >= 0)
                {
                    _contentOwner[A.Aux[i]] = i;
                }
            }
        }

        return _contentOwner.TryGetValue(content, out int owner) ? owner : -1;
    }

    public static bool IsWhitespace(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
        {
            if (c is not (' ' or '\t' or '\n' or '\f' or '\r'))
            {
                return false;
            }
        }

        return true;
    }

    // ---- touch bookkeeping ----

    private void Touch(RegionKind kind, int node)
    {
        var key = new RegionKey(kind, node);
        if (!Regions.TryGetValue(key, out List<int>? edits))
        {
            edits = [];
            Regions[key] = edits;
        }

        if (edits.Count == 0 || edits[^1] != CurrentEdit)
        {
            edits.Add(CurrentEdit);
        }

        if (!_touched.Contains(key))
        {
            _touched.Add(key);
        }
    }

    /// <summary>Marks a text node changed; for a node an edit created, its parent's children are marked, since the node is written with them.</summary>
    private void TouchText(int t)
    {
        if (t < BaseCount)
        {
            Touch(RegionKind.Text, t);
        }
        else
        {
            TouchChildren(A.Parent[t]);
        }
    }

    /// <summary>Marks the children of <paramref name="parent"/> changed; for a node an edit created, the nearest original ancestor is marked.</summary>
    private void TouchChildren(int parent)
    {
        int p = parent;
        while (p >= BaseCount)
        {
            int q = ParentOrOwner(p);
            if (q < 0)
            {
                return;
            }

            p = q;
        }

        if (p >= 0)
        {
            Touch(RegionKind.Children, p);
        }
    }

    // ---- attributes ----

    public List<ModelAttr> AttrsOf(int e)
    {
        if (_attrs.TryGetValue(e, out List<ModelAttr>? list))
        {
            return list;
        }

        list = new List<ModelAttr>(A.AttrCount[e] + 1);
        int start = A.AttrStart[e];
        for (int i = 0; i < A.AttrCount[e]; i++)
        {
            int row = start + i;
            list.Add(new ModelAttr
            {
                Name = A.AName[row],
                Namespace = (AttributeNamespace)((A.AFlags[row] & AF.NsMask) >> AF.NsShift),
                Value = new Attr(View, row).Value.ToString(),
                Orig = row < BaseAttrTotal && e < BaseCount ? row : -1,
            });
        }

        _attrs[e] = list;
        return list;
    }

    /// <summary>Writes the changed attribute lists back into the columns so that readers of <see cref="View"/> see them.</summary>
    public void Flush()
    {
        foreach (int e in _attrsToFlush)
        {
            List<ModelAttr> list = _attrs[e];
            int start = A.AttrTotal;
            foreach (ModelAttr m in list)
            {
                int row = A.AddAttribute(m.Name, (byte)((int)m.Namespace << AF.NsShift), -1, -1);
                A.AValStart[row] = -1;
                A.AValEnd[row] = -1;
                A.AValOff[row] = A.AppendSide(m.Value);
                A.AValLen[row] = m.Value.Length;
                A.AFlags[row] |= AF.ValueSide;
            }

            A.AttrStart[e] = start;
            A.AttrCount[e] = list.Count;
        }

        _attrsToFlush.Clear();
    }

    private void AttrsChanged(int e) => _attrsToFlush.Add(e);

    public void SetData(int node, string text)
    {
        A.DataOff[node] = A.AppendSide(text);
        A.DataLen[node] = text.Length;
        A.Flags[node] |= F.DataSide;
    }

    public int NewText(string text)
    {
        int n = A.AddNode(NodeKind.Text, (byte)ElementNamespace.Html, -1);
        SetData(n, text);
        return n;
    }

    // ---- arena copy ----

    private static Arena CloneArena(Arena s, ParseLimits limits)
    {
        var d = new Arena(s.Count + 32, s.AttrTotal + 32, limits.MaxNodes, limits.MaxTotalAttributes);
        int n = s.Count;
        Array.Copy(s.Kind, d.Kind, n);
        Array.Copy(s.Ns, d.Ns, n);
        Array.Copy(s.Flags, d.Flags, n);
        Array.Copy(s.Name, d.Name, n);
        Array.Copy(s.Parent, d.Parent, n);
        Array.Copy(s.First, d.First, n);
        Array.Copy(s.Last, d.Last, n);
        Array.Copy(s.Next, d.Next, n);
        Array.Copy(s.Prev, d.Prev, n);
        Array.Copy(s.AttrStart, d.AttrStart, n);
        Array.Copy(s.AttrCount, d.AttrCount, n);
        Array.Copy(s.SrcStart, d.SrcStart, n);
        Array.Copy(s.SrcEnd, d.SrcEnd, n);
        Array.Copy(s.EndStart, d.EndStart, n);
        Array.Copy(s.EndEnd, d.EndEnd, n);
        Array.Copy(s.DataOff, d.DataOff, n);
        Array.Copy(s.DataLen, d.DataLen, n);
        Array.Copy(s.Aux, d.Aux, n);
        d.Count = n;
        int a = s.AttrTotal;
        Array.Copy(s.AName, d.AName, a);
        Array.Copy(s.AFlags, d.AFlags, a);
        Array.Copy(s.ANameStart, d.ANameStart, a);
        Array.Copy(s.ANameEnd, d.ANameEnd, a);
        Array.Copy(s.AValStart, d.AValStart, a);
        Array.Copy(s.AValEnd, d.AValEnd, a);
        Array.Copy(s.AValOff, d.AValOff, a);
        Array.Copy(s.AValLen, d.AValLen, a);
        d.AttrTotal = a;
        d.AppendSide(s.Side.AsSpan(0, s.SideLen));
        return d;
    }

    // ---- importing parsed fragments ----

    /// <summary>Copies the top-level nodes of a parsed fragment into the working columns, detached, and returns them in order.</summary>
    public List<int> Import(Document frag)
    {
        Arena fa = frag.Arena;
        var roots = new List<int>();
        var stack = new Stack<(int Src, int Parent)>();
        PushChildrenReversed(fa, 0, -1, stack);
        while (stack.Count > 0)
        {
            (int src, int parent) = stack.Pop();
            int copy = CopyNode(frag, src);
            if (parent < 0)
            {
                roots.Add(copy);
            }
            else
            {
                A.Insert(parent, -1, copy);
            }

            if (fa.Kind[src] == (byte)NodeKind.Element && fa.Aux[src] >= 0)
            {
                int content = A.AddNode(NodeKind.DocumentFragment, (byte)ElementNamespace.Html, -1);
                A.Aux[copy] = content;
                if (_contentOwner is not null)
                {
                    _contentOwner[content] = copy;
                }

                PushChildrenReversed(fa, fa.Aux[src], content, stack);
            }

            PushChildrenReversed(fa, src, copy, stack);
        }

        return roots;
    }

    private static void PushChildrenReversed(Arena fa, int node, int dstParent, Stack<(int, int)> stack)
    {
        for (int c = fa.Last[node]; c >= 0; c = fa.Prev[c])
        {
            stack.Push((c, dstParent));
        }
    }

    private int CopyNode(Document frag, int s)
    {
        Arena fa = frag.Arena;
        var kind = (NodeKind)fa.Kind[s];
        int name = fa.Name[s];
        if (name >= N.Count)
        {
            name = Names.Intern(frag.Names.NameOf(name));
        }

        int n = A.AddNode(kind, fa.Ns[s], name);
        A.Flags[n] = (ushort)(fa.Flags[s] & (F.IntegrationPoint | F.SelfClosing));
        switch (kind)
        {
            case NodeKind.Element:
                {
                    int count = fa.AttrCount[s];
                    A.AttrStart[n] = A.AttrTotal;
                    A.AttrCount[n] = count;
                    for (int i = 0; i < count; i++)
                    {
                        int from = fa.AttrStart[s] + i;
                        int an = fa.AName[from];
                        if (an >= N.Count)
                        {
                            an = Names.Intern(frag.Names.NameOf(an));
                        }

                        int row = A.AddAttribute(an, (byte)(fa.AFlags[from] & AF.NsMask), -1, -1);
                        string v = new Attr(frag, from).Value.ToString();
                        A.AValStart[row] = -1;
                        A.AValEnd[row] = -1;
                        A.AValOff[row] = A.AppendSide(v);
                        A.AValLen[row] = v.Length;
                        A.AFlags[row] |= AF.ValueSide;
                    }

                    break;
                }

            case NodeKind.Text or NodeKind.Comment or NodeKind.ProcessingInstruction:
                {
                    ReadOnlySpan<char> data = new Node(frag, s).Data;
                    A.DataOff[n] = A.AppendSide(data);
                    A.DataLen[n] = data.Length;
                    A.Flags[n] |= F.DataSide;
                    break;
                }

            default:
                break;
        }

        return n;
    }

    // ---- removal ----

    /// <summary>Detaches a node and marks it and everything below it dead (a template's content included).</summary>
    public void Kill(int node)
    {
        A.Detach(node);
        var stack = new Stack<int>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            A.Flags[i] |= Dead;
            for (int c = A.First[i]; c >= 0; c = A.Next[c])
            {
                stack.Push(c);
            }

            if (A.Kind[i] == (byte)NodeKind.Element && A.Aux[i] >= 0)
            {
                stack.Push(A.Aux[i]);
            }
        }
    }
}
