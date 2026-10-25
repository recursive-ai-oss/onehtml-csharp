using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Dom;

// HTML 4.10.17 "The selectedcontent element" and 4.10.10 "The option element". A browser runs these DOM steps while the
// parser inserts and pops elements, with no script involved, so the DOM it exposes already holds the copy of the
// selected option's content inside each selectedcontent element. The parser does the same, keeping only the state
// the steps need: which option of each select is selected and which selectedcontent elements it owns.
internal sealed partial class TreeBuilder
{
    /// <summary>What the parser knows about one <c>select</c> element while it is being built.</summary>
    private sealed class SelectState
    {
        public bool Multiple;
        public long DisplaySize = 1;
        public int Selected = -1;
        public int FirstEnabled = -1;
        public readonly List<int> Contents = [];
    }

    private Dictionary<int, SelectState>? _selectStates;
    private Dictionary<long, int>? _optionSelectMemo;
    private Dictionary<int, int>? _contentMemo;
    private List<long>? _memoChain;
    private long _selectedContentWork;

    private bool HasAttribute(int node, int atom)
    {
        int start = _a.AttrStart[node];
        for (int i = 0; i < _a.AttrCount[node]; i++)
        {
            if (_a.AName[start + i] == atom)
            {
                return true;
            }
        }

        return false;
    }

    private ReadOnlySpan<char> AttributeValue(int node, int atom)
    {
        int start = _a.AttrStart[node];
        for (int i = 0; i < _a.AttrCount[node]; i++)
        {
            int row = start + i;
            if (_a.AName[row] == atom)
            {
                return (_a.AFlags[row] & AF.ValueSide) != 0
                    ? _a.Side.AsSpan(_a.AValOff[row], _a.AValLen[row])
                    : _text.AsSpan(_a.AValOff[row], _a.AValLen[row]);
            }
        }

        return default;
    }

    private bool IsHtml(int node, int name) => _a.Kind[node] == (byte)NodeKind.Element && _a.Ns[node] == Html && _a.Name[node] == name;

    private SelectState StateOf(int select)
    {
        _selectStates ??= [];
        if (_selectStates.TryGetValue(select, out SelectState? st))
        {
            return st;
        }

        st = new SelectState { Multiple = HasAttribute(select, N.Multiple) };
        st.DisplaySize = st.Multiple ? 4 : 1;
        if (HasAttribute(select, N.Size) && TryParseNonNegative(AttributeValue(select, N.Size), out long size))
        {
            st.DisplaySize = size;
        }

        _selectStates[select] = st;
        return st;
    }

    /// <summary>HTML 2.3.4.2 "rules for parsing non-negative integers".</summary>
    private static bool TryParseNonNegative(ReadOnlySpan<char> s, out long value)
    {
        value = 0;
        int i = 0;
        while (i < s.Length && s[i] is ' ' or '\t' or '\n' or '\f' or '\r')
        {
            i++;
        }

        bool negative = false;
        if (i < s.Length && s[i] is '-' or '+')
        {
            negative = s[i] == '-';
            i++;
        }

        if (i >= s.Length || s[i] is < '0' or > '9')
        {
            return false;
        }

        while (i < s.Length && s[i] is >= '0' and <= '9')
        {
            value = Math.Min(value * 10 + (s[i] - '0'), int.MaxValue);
            i++;
        }

        return !negative || value == 0;
    }

    /// <summary>
    /// HTML "get the nearest ancestor select" for an option whose parent is <paramref name="parent"/>. Answers are
    /// remembered per node, so a long run of siblings costs one walk.
    /// </summary>
    private int NearestSelectOfOption(int parent)
    {
        _optionSelectMemo ??= [];
        List<long> chain = _memoChain ??= [];
        chain.Clear();
        int cur = parent;
        bool seenOptgroup = false;
        int result;
        while (true)
        {
            if (cur < 0)
            {
                result = -1;
                break;
            }

            long key = ((long)cur << 1) | (seenOptgroup ? 1L : 0L);
            if (_optionSelectMemo.TryGetValue(key, out result))
            {
                break;
            }

            chain.Add(key);
            if (_a.Kind[cur] == (byte)NodeKind.Element && _a.Ns[cur] == Html)
            {
                int name = _a.Name[cur];
                if (name == N.Datalist || name == N.Hr || name == N.Option)
                {
                    result = -1;
                    break;
                }

                if (name == N.Optgroup)
                {
                    if (seenOptgroup)
                    {
                        result = -1;
                        break;
                    }

                    seenOptgroup = true;
                }
                else if (name == N.Select)
                {
                    result = cur;
                    break;
                }
            }

            cur = _a.Parent[cur];
        }

        foreach (long key in chain)
        {
            _optionSelectMemo[key] = result;
        }

        return result;
    }

    /// <summary>
    /// HTML "recalculate a selectedcontent element's disabledness" for the chain starting at <paramref name="node"/>:
    /// returns the nearest select (-1 for none) and whether an option, a selectedcontent or a second select sits above.
    /// </summary>
    private int ContentContext(int node)
    {
        _contentMemo ??= [];
        List<long> chain = _memoChain ??= [];
        chain.Clear();
        int cur = node;
        int result;
        while (true)
        {
            if (cur < 0)
            {
                result = 0; // no select (-1 + 1), not disabled
                break;
            }

            if (_contentMemo.TryGetValue(cur, out result))
            {
                break;
            }

            chain.Add(cur);
            cur = _a.Parent[cur];
        }

        for (int i = chain.Count - 1; i >= 0; i--)
        {
            int n = (int)chain[i];
            int nearest = (result >> 1) - 1;
            bool disabled = (result & 1) != 0;
            if (_a.Kind[n] == (byte)NodeKind.Element && _a.Ns[n] == Html)
            {
                int name = _a.Name[n];
                if (name == N.Option || name == N.Selectedcontent)
                {
                    disabled = true;
                }
                else if (name == N.Select)
                {
                    disabled |= nearest >= 0;
                    nearest = n;
                }
            }

            result = ((nearest + 1) << 1) | (disabled ? 1 : 0);
            _contentMemo[n] = result;
        }

        return result;
    }

    private bool IsDisabledOption(int option)
    {
        if (HasAttribute(option, N.Disabled))
        {
            return true;
        }

        int parent = _a.Parent[option];
        return parent >= 0 && IsHtml(parent, N.Optgroup) && HasAttribute(parent, N.Disabled);
    }

    /// <summary>The option insertion steps: update its nearest ancestor select, then run the selectedness setting algorithm (4.10.7).</summary>
    private void OnOptionInserted(int option)
    {
        if (_cnt[N.Select] == 0)
        {
            return;
        }

        int select = NearestSelectOfOption(_a.Parent[option]);
        if (select < 0)
        {
            return;
        }

        SelectState st = StateOf(select);
        if (st.Multiple)
        {
            return;
        }

        // An option made with a selected attribute starts out selected; the algorithm keeps only the last one.
        if (HasAttribute(option, N.Selected))
        {
            if (st.Selected >= 0)
            {
                _a.Flags[st.Selected] &= unchecked((ushort)~F.Selected);
            }

            _a.Flags[option] |= F.Selected;
            st.Selected = option;
        }

        if (st.FirstEnabled < 0 && !IsDisabledOption(option))
        {
            st.FirstEnabled = option;
        }

        if (st.Selected < 0 && st.FirstEnabled >= 0 && st.DisplaySize == 1)
        {
            _a.Flags[st.FirstEnabled] |= F.Selected;
            st.Selected = st.FirstEnabled;
        }
    }

    /// <summary>
    /// "When an option element is popped off the stack of open elements of an HTML parser, update descendant
    /// selectedcontent elements for an option" (4.10.10).
    /// </summary>
    private void OnOptionPopped(int option)
    {
        if (_cnt[N.Select] == 0 || (_a.Flags[option] & F.Selected) == 0 || _selectStates is null)
        {
            return;
        }

        int select = NearestSelectOfOption(_a.Parent[option]);
        if (select < 0 || !_selectStates.TryGetValue(select, out SelectState? st) || st.Multiple)
        {
            return;
        }

        foreach (int content in st.Contents)
        {
            UpdateSelectedContent(st, content);
        }
    }

    /// <summary>The selectedcontent post-connection steps (4.10.17): remember the element and fill it.</summary>
    private void OnSelectedContentInserted(int content)
    {
        if (_cnt[N.Select] == 0)
        {
            return;
        }

        int ctx = ContentContext(_a.Parent[content]);
        int select = (ctx >> 1) - 1;
        if ((ctx & 1) != 0 || select < 0)
        {
            return;
        }

        SelectState st = StateOf(select);
        if (st.Multiple)
        {
            return;
        }

        st.Contents.Add(content);
        UpdateSelectedContent(st, content);
    }

    /// <summary>
    /// HTML "update a selectedcontent": clone the children of the select's selected option into the element, replacing
    /// what it held, or empty it when no option is selected. The copies are synthesized nodes with no source bytes.
    /// </summary>
    private void UpdateSelectedContent(SelectState st, int content)
    {
        if (++_selectedContentWork > _limits.MaxNodes)
        {
            throw new HtmlLimitExceededException(nameof(ParseLimits.MaxNodes), _limits.MaxNodes, _selectedContentWork);
        }

        int option = st.Selected;
        if ((option < 0 || _a.First[option] < 0) && _a.First[content] < 0)
        {
            return;
        }

        // "Replace all" with the new fragment (or with null): the old children leave the tree.
        while (_a.First[content] >= 0)
        {
            _a.Detach(_a.First[content]);
        }

        if (option >= 0)
        {
            CloneChildren(option, content);
        }
    }

    /// <summary>DOM "clone a node" with the subtree flag, applied to each child of <paramref name="from"/>; iterative.</summary>
    private void CloneChildren(int from, int to)
    {
        var work = new Stack<(int Source, int Parent)>();
        for (int c = _a.Last[from]; c >= 0; c = _a.Prev[c])
        {
            work.Push((c, to));
        }

        while (work.Count > 0)
        {
            (int src, int parent) = work.Pop();
            int copy = CloneSynthesized(src);
            _a.Insert(parent, -1, copy);
            if (_a.Kind[src] != (byte)NodeKind.Element)
            {
                continue;
            }

            // A template's cloning steps copy its contents too.
            int srcContent = _a.Aux[src];
            if (_a.Ns[src] == Html && _a.Name[src] == N.Template && srcContent >= 0)
            {
                int copyContent = _a.Aux[copy];
                for (int c = _a.Last[srcContent]; c >= 0; c = _a.Prev[c])
                {
                    work.Push((c, copyContent));
                }
            }

            for (int c = _a.Last[src]; c >= 0; c = _a.Prev[c])
            {
                work.Push((c, copy));
            }
        }
    }

    private int CloneSynthesized(int src)
    {
        var kind = (NodeKind)_a.Kind[src];
        int e = _a.AddNode(kind, _a.Ns[src], _a.Name[src]);
        _a.Flags[e] = (ushort)(F.Synthesized | (_a.Flags[src] & (F.DataSide | F.IntegrationPoint)));
        switch (kind)
        {
            case NodeKind.Element:
                int n = _a.AttrCount[src];
                _a.AttrStart[e] = _a.AttrTotal;
                _a.AttrCount[e] = n;
                for (int i = 0; i < n; i++)
                {
                    int from = _a.AttrStart[src] + i;
                    int row = _a.AddAttribute(_a.AName[from], _a.AFlags[from], -1, -1);
                    _a.AValStart[row] = -1;
                    _a.AValEnd[row] = -1;
                    _a.AValOff[row] = _a.AValOff[from];
                    _a.AValLen[row] = _a.AValLen[from];
                }

                if (_a.Ns[src] == Html && _a.Name[src] == N.Template)
                {
                    _a.Aux[e] = _a.AddNode(NodeKind.DocumentFragment, Html, -1);
                }

                break;
            case NodeKind.Text or NodeKind.Comment or NodeKind.ProcessingInstruction:
                _a.DataOff[e] = _a.DataOff[src];
                _a.DataLen[e] = _a.DataLen[src];
                break;
            default:
                // A DOCTYPE cannot be inside an option.
                break;
        }

        return e;
    }
}
