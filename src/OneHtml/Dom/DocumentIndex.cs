using System.Collections.Generic;
using System.Threading;

namespace RecursiveAI.OneHtml.Dom;

/// <summary>
/// Side tables of one snapshot, each built on first use from the immutable arena. A table is complete before it is
/// published (compare-exchange of a reference), so any number of threads can use them; two threads that race build the
/// table twice and one result is kept.
/// </summary>
internal sealed class DocumentIndex
{
    private readonly Document _doc;
    private IdTable? _ids;
    private SiblingTable? _siblings;
    private SiblingTable? _types;
    private FormTables? _forms;

    public DocumentIndex(Document doc) => _doc = doc;

    public int FirstWithId(string id)
    {
        if (id.Length == 0)
        {
            return -1;
        }

        return Ids.Head.TryGetValue(id, out int i) ? i : -1;
    }

    /// <summary>Like <see cref="FirstWithId(string)"/> for an id held as a slice of the document text; allocation-free.</summary>
    public int FirstWithId(System.ReadOnlySpan<char> id)
    {
        if (id.Length == 0)
        {
            return -1;
        }

        return Ids.Head.GetAlternateLookup<System.ReadOnlySpan<char>>().TryGetValue(id, out int i) ? i : -1;
    }

    /// <summary>The next element in tree order with the same id as <paramref name="element"/>, or -1.</summary>
    public int NextWithSameId(int element) => Ids.DuplicateNext.TryGetValue(element, out int n) ? n : -1;

    public bool HasDuplicateIds => Ids.DuplicateNext.Count > 0;

    private IdTable Ids => _ids ?? Publish(ref _ids, BuildIds());

    /// <summary>1-based position among the element siblings of each element (0 for non-elements).</summary>
    public SiblingTable Siblings => _siblings ?? Publish(ref _siblings, BuildSiblings(false));

    /// <summary>1-based position among the element siblings with the same namespace and name.</summary>
    public SiblingTable Types => _types ?? Publish(ref _types, BuildSiblings(true));

    public FormTables Forms => _forms ?? Publish(ref _forms, FormTables.Build(_doc));

    private static T Publish<T>(ref T? field, T built)
        where T : class
    {
        T? existing = Interlocked.CompareExchange(ref field, built, null);
        return existing ?? built;
    }

    private string? _pragma;
    private volatile bool _pragmaDone;

    /// <summary>The pragma-set default language (HTML 4.2.5.3, <c>http-equiv=content-language</c>), or null.</summary>
    public string? PragmaLanguage
    {
        get
        {
            if (!_pragmaDone)
            {
                _pragma = ComputePragma();
                _pragmaDone = true;
            }

            return _pragma;
        }
    }

    private string? ComputePragma()
    {
        Arena a = _doc.Arena;
        for (int i = Node.NextPreorder(a, 0, 0); i >= 0; i = Node.NextPreorder(a, i, 0))
        {
            var n = new Node(_doc, i);
            if (!Selectors.ElementState.IsHtml(n, "meta") || !Selectors.ElementState.LowerEquals(Selectors.ElementState.AttrSpan(n, "http-equiv"), "content-language") || !Selectors.ElementState.Has(n, "content"))
            {
                continue;
            }

            string v = Selectors.ElementState.AttrString(n, "content");
            if (v.Contains(',', System.StringComparison.Ordinal))
            {
                return null;
            }

            v = v.Trim(' ', '\t', '\n', '\f', '\r');
            return v.Length == 0 ? null : v;
        }

        return null;
    }

    private IdTable BuildIds()
    {
        Arena a = _doc.Arena;
        var head = new Dictionary<string, int>(System.StringComparer.Ordinal);
        var tail = new Dictionary<string, int>(System.StringComparer.Ordinal);
        var dup = new Dictionary<int, int>();
        var root = new Node(_doc, 0);
        for (int i = Node.NextPreorder(a, 0, 0); i >= 0; i = Node.NextPreorder(a, i, 0))
        {
            if (a.Kind[i] != (byte)NodeKind.Element)
            {
                continue;
            }

            var n = new Node(_doc, i);
            int at = n.FindAttr(N.Id);
            if (at < 0)
            {
                continue;
            }

            System.ReadOnlySpan<char> v = n.AttrValue(at);
            if (v.Length == 0)
            {
                continue;
            }

            string key = v.ToString();
            if (head.TryAdd(key, i))
            {
                tail[key] = i;
            }
            else
            {
                dup[tail[key]] = i;
                tail[key] = i;
            }
        }

        _ = root;
        return new IdTable(head, dup);
    }

    private SiblingTable BuildSiblings(bool byType)
    {
        Arena a = _doc.Arena;
        int count = a.Count;
        var pos = new int[count];
        var total = new int[count];
        Dictionary<long, int>? scratch = byType ? [] : null;
        for (int p = 0; p < count; p++)
        {
            int first = a.First[p];
            if (first < 0)
            {
                continue;
            }

            scratch?.Clear();
            int k = 0;
            for (int x = first; x >= 0; x = a.Next[x])
            {
                if (a.Kind[x] != (byte)NodeKind.Element)
                {
                    continue;
                }

                if (scratch is null)
                {
                    pos[x] = ++k;
                }
                else
                {
                    long key = ((long)a.Ns[x] << 32) | (uint)a.Name[x];
                    scratch.TryGetValue(key, out int c);
                    scratch[key] = c + 1;
                    pos[x] = c + 1;
                }
            }

            for (int x = first; x >= 0; x = a.Next[x])
            {
                if (a.Kind[x] != (byte)NodeKind.Element)
                {
                    continue;
                }

                if (scratch is null)
                {
                    total[x] = k;
                }
                else
                {
                    total[x] = scratch[((long)a.Ns[x] << 32) | (uint)a.Name[x]];
                }
            }
        }

        return new SiblingTable(pos, total);
    }

    internal sealed class IdTable
    {
        public IdTable(Dictionary<string, int> head, Dictionary<int, int> dup)
        {
            Head = head;
            DuplicateNext = dup;
        }

        public Dictionary<string, int> Head { get; }

        public Dictionary<int, int> DuplicateNext { get; }
    }

    internal sealed class SiblingTable
    {
        public SiblingTable(int[] position, int[] total)
        {
            Position = position;
            Total = total;
        }

        /// <summary>1-based position from the start.</summary>
        public int[] Position { get; }

        /// <summary>How many siblings are counted (of the element, or of its type).</summary>
        public int[] Total { get; }
    }
}
