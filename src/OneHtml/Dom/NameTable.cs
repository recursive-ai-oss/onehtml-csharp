using System;

namespace RecursiveAI.OneHtml.Dom;

/// <summary>
/// Interns element and attribute names as small integers. The names in <see cref="N"/> have fixed ids shared by every
/// document; any other name gets the next id the first time a document uses it. A table is written only while its
/// document is being parsed and is read-only afterwards, so a finished document can be read from any thread.
/// </summary>
internal sealed class NameTable
{
    private static readonly int[] s_knownSlots = BuildKnown();

    private string[] _names = [];
    private int[] _slots = [];
    private int _count;

    /// <summary>The number of names the document added to the fixed ones.</summary>
    public int DynamicCount => _count;

    /// <summary>The id for a name, adding it if it is new.</summary>
    public int Intern(ReadOnlySpan<char> name)
    {
        uint hash = Hash(name);
        int[] known = s_knownSlots;
        int mask = known.Length - 1;
        for (int i = (int)(hash & (uint)mask); ; i = (i + 1) & mask)
        {
            int slot = known[i];
            if (slot == 0)
            {
                break;
            }

            if (N.Names[slot - 1].AsSpan().SequenceEqual(name))
            {
                return slot - 1;
            }
        }

        if (_slots.Length == 0)
        {
            _slots = new int[32];
            _names = new string[16];
        }

        mask = _slots.Length - 1;
        int j = (int)(hash & (uint)mask);
        for (; ; j = (j + 1) & mask)
        {
            int slot = _slots[j];
            if (slot == 0)
            {
                break;
            }

            if (_names[slot - 1].AsSpan().SequenceEqual(name))
            {
                return N.Count + slot - 1;
            }
        }

        if (_count == _names.Length)
        {
            Array.Resize(ref _names, _count * 2);
        }

        _names[_count] = name.ToString();
        _count++;
        _slots[j] = _count;
        if (_count * 2 > _slots.Length)
        {
            Rehash();
        }

        return N.Count + _count - 1;
    }

    /// <summary>Looks a name up without adding it; -1 when it has no id.</summary>
    public int Find(ReadOnlySpan<char> name)
    {
        uint hash = Hash(name);
        int[] known = s_knownSlots;
        int mask = known.Length - 1;
        for (int i = (int)(hash & (uint)mask); ; i = (i + 1) & mask)
        {
            int slot = known[i];
            if (slot == 0)
            {
                break;
            }

            if (N.Names[slot - 1].AsSpan().SequenceEqual(name))
            {
                return slot - 1;
            }
        }

        if (_slots.Length == 0)
        {
            return -1;
        }

        mask = _slots.Length - 1;
        for (int j = (int)(hash & (uint)mask); ; j = (j + 1) & mask)
        {
            int slot = _slots[j];
            if (slot == 0)
            {
                return -1;
            }

            if (_names[slot - 1].AsSpan().SequenceEqual(name))
            {
                return N.Count + slot - 1;
            }
        }
    }

    /// <summary>The text of an id.</summary>
    public string NameOf(int id) => id < N.Count ? N.Names[id] : _names[id - N.Count];

    private void Rehash()
    {
        int[] slots = new int[_slots.Length * 2];
        int mask = slots.Length - 1;
        for (int n = 0; n < _count; n++)
        {
            int j = (int)(Hash(_names[n]) & (uint)mask);
            while (slots[j] != 0)
            {
                j = (j + 1) & mask;
            }

            slots[j] = n + 1;
        }

        _slots = slots;
    }

    private static uint Hash(ReadOnlySpan<char> s)
    {
        uint h = 2166136261;
        foreach (char c in s)
        {
            h = (h ^ c) * 16777619;
        }

        return h ^ (h >> 15);
    }

    private static int[] BuildKnown()
    {
        int size = 1;
        while (size < N.Count * 3)
        {
            size <<= 1;
        }

        int[] slots = new int[size];
        int mask = size - 1;
        for (int n = 0; n < N.Count; n++)
        {
            int i = (int)(Hash(N.Names[n]) & (uint)mask);
            while (slots[i] != 0)
            {
                i = (i + 1) & mask;
            }

            slots[i] = n + 1;
        }

        return slots;
    }
}
