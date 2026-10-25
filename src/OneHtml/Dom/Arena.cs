using System;

namespace RecursiveAI.OneHtml.Dom;

/// <summary>
/// The flat columns of a document: one slot per node in each node column, one per attribute in each attribute column.
/// Index 0 is the root. Indices are never reused, and a finished arena is never written again.
/// </summary>
internal sealed class Arena
{
    // Per-node columns.
    public int Count;
    public byte[] Kind;
    public byte[] Ns;
    public ushort[] Flags;
    public int[] Name;
    public int[] Parent;
    public int[] First;
    public int[] Last;
    public int[] Next;
    public int[] Prev;
    public int[] AttrStart;
    public int[] AttrCount;
    public int[] SrcStart;
    public int[] SrcEnd;
    public int[] EndStart;
    public int[] EndEnd;
    public int[] DataOff;
    public int[] DataLen;

    // Elements: the content fragment of a template, or -1. Text: the end of the side-store space reserved for it.
    public int[] Aux;

    // Per-attribute columns.
    public int AttrTotal;
    public int[] AName;
    public byte[] AFlags;
    public int[] ANameStart;
    public int[] ANameEnd;
    public int[] AValStart;
    public int[] AValEnd;
    public int[] AValOff;
    public int[] AValLen;

    // Text the source does not hold verbatim: decoded character references, replaced characters, merged runs.
    public char[] Side = [];
    public int SideLen;

    private readonly int _maxNodes;
    private readonly int _maxAttributes;

    public Arena(int nodeCapacity, int attributeCapacity, int maxNodes, int maxAttributes)
    {
        _maxNodes = maxNodes;
        _maxAttributes = maxAttributes;
        nodeCapacity = Math.Max(nodeCapacity, 8);
        attributeCapacity = Math.Max(attributeCapacity, 8);
        Kind = new byte[nodeCapacity];
        Ns = new byte[nodeCapacity];
        Flags = new ushort[nodeCapacity];
        Name = new int[nodeCapacity];
        Parent = new int[nodeCapacity];
        First = new int[nodeCapacity];
        Last = new int[nodeCapacity];
        Next = new int[nodeCapacity];
        Prev = new int[nodeCapacity];
        AttrStart = new int[nodeCapacity];
        AttrCount = new int[nodeCapacity];
        SrcStart = new int[nodeCapacity];
        SrcEnd = new int[nodeCapacity];
        EndStart = new int[nodeCapacity];
        EndEnd = new int[nodeCapacity];
        DataOff = new int[nodeCapacity];
        DataLen = new int[nodeCapacity];
        Aux = new int[nodeCapacity];
        AName = new int[attributeCapacity];
        AFlags = new byte[attributeCapacity];
        ANameStart = new int[attributeCapacity];
        ANameEnd = new int[attributeCapacity];
        AValStart = new int[attributeCapacity];
        AValEnd = new int[attributeCapacity];
        AValOff = new int[attributeCapacity];
        AValLen = new int[attributeCapacity];
    }

    /// <summary>Adds a detached node and returns its index.</summary>
    public int AddNode(NodeKind kind, byte ns, int name)
    {
        int n = Count;
        if (n >= _maxNodes)
        {
            throw new HtmlLimitExceededException(nameof(ParseLimits.MaxNodes), _maxNodes, (long)n + 1);
        }

        if (n == Kind.Length)
        {
            GrowNodes();
        }

        Kind[n] = (byte)kind;
        Ns[n] = ns;
        Flags[n] = 0;
        Name[n] = name;
        Parent[n] = -1;
        First[n] = -1;
        Last[n] = -1;
        Next[n] = -1;
        Prev[n] = -1;
        AttrStart[n] = 0;
        AttrCount[n] = 0;
        SrcStart[n] = -1;
        SrcEnd[n] = -1;
        EndStart[n] = -1;
        EndEnd[n] = -1;
        DataOff[n] = 0;
        DataLen[n] = 0;
        Aux[n] = -1;
        Count = n + 1;
        return n;
    }

    /// <summary>Adds an attribute row and returns its index. The caller fills in the value.</summary>
    public int AddAttribute(int name, byte flags, int nameStart, int nameEnd)
    {
        int a = AttrTotal;
        if (a >= _maxAttributes)
        {
            throw new HtmlLimitExceededException(nameof(ParseLimits.MaxTotalAttributes), _maxAttributes, (long)a + 1);
        }

        if (a == AName.Length)
        {
            GrowAttributes();
        }

        AName[a] = name;
        AFlags[a] = flags;
        ANameStart[a] = nameStart;
        ANameEnd[a] = nameEnd;
        AValStart[a] = nameEnd;
        AValEnd[a] = nameEnd;
        AValOff[a] = 0;
        AValLen[a] = 0;
        AttrTotal = a + 1;
        return a;
    }

    /// <summary>Appends text to the side store and returns its offset.</summary>
    public int AppendSide(ReadOnlySpan<char> text)
    {
        int off = SideLen;
        EnsureSide(text.Length);
        text.CopyTo(Side.AsSpan(off));
        SideLen = off + text.Length;
        return off;
    }

    /// <summary>Reserves <paramref name="length"/> chars at the end of the side store and returns their offset.</summary>
    public int ReserveSide(int length)
    {
        int off = SideLen;
        EnsureSide(length);
        SideLen = off + length;
        return off;
    }

    private void EnsureSide(int extra)
    {
        long need = (long)SideLen + extra;
        if (need > Array.MaxLength)
        {
            throw new HtmlLimitExceededException(nameof(ParseLimits.MaxInputLength), Array.MaxLength, need);
        }

        if (need > Side.Length)
        {
            long cap = Math.Max(Math.Max(need, (long)Side.Length * 2), 256);
            Array.Resize(ref Side, (int)Math.Min(cap, Array.MaxLength));
        }
    }

    private void GrowNodes()
    {
        int cap = (int)Math.Min((long)Kind.Length * 2, Array.MaxLength);
        Array.Resize(ref Kind, cap);
        Array.Resize(ref Ns, cap);
        Array.Resize(ref Flags, cap);
        Array.Resize(ref Name, cap);
        Array.Resize(ref Parent, cap);
        Array.Resize(ref First, cap);
        Array.Resize(ref Last, cap);
        Array.Resize(ref Next, cap);
        Array.Resize(ref Prev, cap);
        Array.Resize(ref AttrStart, cap);
        Array.Resize(ref AttrCount, cap);
        Array.Resize(ref SrcStart, cap);
        Array.Resize(ref SrcEnd, cap);
        Array.Resize(ref EndStart, cap);
        Array.Resize(ref EndEnd, cap);
        Array.Resize(ref DataOff, cap);
        Array.Resize(ref DataLen, cap);
        Array.Resize(ref Aux, cap);
    }

    private void GrowAttributes()
    {
        int cap = (int)Math.Min((long)AName.Length * 2, Array.MaxLength);
        Array.Resize(ref AName, cap);
        Array.Resize(ref AFlags, cap);
        Array.Resize(ref ANameStart, cap);
        Array.Resize(ref ANameEnd, cap);
        Array.Resize(ref AValStart, cap);
        Array.Resize(ref AValEnd, cap);
        Array.Resize(ref AValOff, cap);
        Array.Resize(ref AValLen, cap);
    }

    // ---- Tree linking ----

    /// <summary>Inserts a detached <paramref name="node"/> under <paramref name="parent"/> before <paramref name="before"/> (-1 appends).</summary>
    public void Insert(int parent, int before, int node)
    {
        Parent[node] = parent;
        if (before < 0)
        {
            int last = Last[parent];
            Prev[node] = last;
            Next[node] = -1;
            if (last >= 0)
            {
                Next[last] = node;
            }
            else
            {
                First[parent] = node;
            }

            Last[parent] = node;
        }
        else
        {
            int prev = Prev[before];
            Prev[node] = prev;
            Next[node] = before;
            Prev[before] = node;
            if (prev >= 0)
            {
                Next[prev] = node;
            }
            else
            {
                First[parent] = node;
            }
        }
    }

    /// <summary>Unlinks <paramref name="node"/> from its parent. It stays in the arena, detached.</summary>
    public void Detach(int node)
    {
        int parent = Parent[node];
        if (parent < 0)
        {
            return;
        }

        int prev = Prev[node];
        int next = Next[node];
        if (prev >= 0)
        {
            Next[prev] = next;
        }
        else
        {
            First[parent] = next;
        }

        if (next >= 0)
        {
            Prev[next] = prev;
        }
        else
        {
            Last[parent] = prev;
        }

        Parent[node] = -1;
        Prev[node] = -1;
        Next[node] = -1;
    }
}
