using System;
using System.Collections.Generic;
using System.Text;
using RecursiveAI.OneHtml.Dom;
using RecursiveAI.OneHtml.Parsing;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>A replacement of one stretch of the original text.</summary>
internal sealed class Splice
{
    public int Start;
    public int End;
    public string Text = string.Empty;
    public int Seq;
    public RegionKey Region;

    /// <summary>0 for a local splice; the number of enclosing levels re-serialized otherwise.</summary>
    public int Level;
}

internal sealed class BuildResult
{
    public List<Splice> Items { get; } = [];

    /// <summary>Set when no splice set can be built: a region ran out of enclosing elements, or something cannot be written.</summary>
    public string? Error { get; set; }

    public RegionKey? ErrorRegion { get; set; }

    /// <summary>The level each region ended up at, including bumps the builder made to resolve overlaps.</summary>
    public Dictionary<RegionKey, int> Levels { get; } = [];

    public Dictionary<RegionKey, string> Reasons { get; } = [];
}

/// <summary>
/// Turns the changed regions of the working tree into replacements of the original text. A region is rewritten at
/// level 0 by editing only the bytes of the construct that changed (an attribute, a text node, the gaps between
/// children). At level n it is rewritten by re-serializing the n-th enclosing element that has bytes of its own.
/// </summary>
internal sealed class SpliceBuilder
{
    private readonly EditModel _m;
    private readonly Arena _ba;
    private readonly Arena _a;
    private readonly Document _base;
    private readonly string _text;
    private readonly MarkupWriter _writer;
    private readonly LocalStyle _style;
    private int _seq;
    private HashSet<int> _hasBase = [];

    public SpliceBuilder(EditModel model, LocalStyle style, DocumentEncoder? encoder)
    {
        _m = model;
        _ba = model.BA;
        _a = model.A;
        _base = model.Base;
        _text = model.Base.Source.Text;
        _style = style;
        _writer = new MarkupWriter(model.View, style, encoder, model.Base.ScriptingEnabled);
    }

    public BuildResult Build(Dictionary<RegionKey, int> requested)
    {
        var result = new BuildResult();
        _m.Flush();
        ComputeHasBase();
        var regions = new List<RegionKey>();
        foreach (RegionKey k in _m.Regions.Keys)
        {
            if (!IsRegionLive(k))
            {
                continue;
            }

            regions.Add(k);
        }

        var depth = new Dictionary<RegionKey, int>();
        foreach (RegionKey k in regions)
        {
            depth[k] = Depth(k.Node);
        }

        regions.Sort((x, y) =>
        {
            int c = depth[y].CompareTo(depth[x]);
            if (c != 0)
            {
                return c;
            }

            c = x.Node.CompareTo(y.Node);
            return c != 0 ? c : x.Kind.CompareTo(y.Kind);
        });

        var levels = new Dictionary<RegionKey, int>();
        foreach (RegionKey k in regions)
        {
            levels[k] = requested.GetValueOrDefault(k);
        }

        for (int attempt = 0; attempt < 64; attempt++)
        {
            var items = new List<Splice>();
            _seq = 0;
            result.Reasons.Clear();
            foreach (RegionKey k in regions)
            {
                int level = levels[k];
                if (level == 0)
                {
                    int before = items.Count;
                    if (TryPrecise(k, items, out string? why))
                    {
                        continue;
                    }

                    items.RemoveRange(before, items.Count - before);
                    level = 1;
                    levels[k] = 1;
                    result.Reasons[k] = why!;
                }

                int whole = AncestorAt(k, level);
                if (whole < 0)
                {
                    result.Error = "no enclosing element of the change can be re-serialized"
                        + (result.Reasons.TryGetValue(k, out string? r) ? " (" + r + ")" : string.Empty);
                    result.ErrorRegion = k;
                    return result;
                }

                SourceInfo.TryExtent(_ba, whole, out int s, out int e);
                var sb = new StringBuilder();
                _writer.WriteNode(whole, sb);
                if (_writer.Error is not null)
                {
                    result.Error = _writer.Error;
                    result.ErrorRegion = k;
                    return result;
                }

                items.Add(new Splice { Start = s, End = e, Text = sb.ToString(), Seq = _seq++, Region = k, Level = level });
            }

            RegionKey? conflict = Resolve(items);
            if (conflict is null)
            {
                result.Items.AddRange(items);
                foreach (KeyValuePair<RegionKey, int> kv in levels)
                {
                    result.Levels[kv.Key] = kv.Value;
                }

                if (_writer.Error is not null)
                {
                    result.Error = _writer.Error;
                }

                return result;
            }

            levels[conflict.Value] = levels[conflict.Value] + 1;
            result.Reasons[conflict.Value] = "its replacement overlaps another replacement";
        }

        result.Error = "the replacements could not be made to agree";
        return result;
    }

    private bool IsRegionLive(RegionKey k)
    {
        ushort f = _a.Flags[k.Node];
        if ((f & EditModel.Dead) != 0)
        {
            return false;
        }

        // An unwrapped element has no children list of its own any more, but its removed children still need deleting.
        return k.Kind == RegionKind.Children || (f & EditModel.Unwrapped) == 0;
    }

    private int Depth(int node)
    {
        int d = 0;
        for (int x = node; x >= 0 && d < 100_000; d++)
        {
            int p = _ba.Parent[x];
            if (p < 0 && _ba.Kind[x] == (byte)NodeKind.DocumentFragment)
            {
                p = _m.ContentOwner(x);
            }

            x = p;
        }

        return d;
    }

    /// <summary>The n-th enclosing element (counting the owner as the first) that has bytes of its own, or -1.</summary>
    private int AncestorAt(RegionKey k, int level)
    {
        int owner = k.Kind switch
        {
            RegionKind.Text => _m.ParentOrOwner(k.Node),
            _ => _a.Kind[k.Node] == (byte)NodeKind.DocumentFragment ? _m.ContentOwner(k.Node) : k.Node,
        };
        int count = 0;
        for (int x = owner; x >= 0; x = _m.ParentOrOwner(x))
        {
            if (x < _m.BaseCount && _a.Kind[x] == (byte)NodeKind.Element && !_m.IsGone(x) && SourceInfo.TryExtent(_ba, x, out _, out _))
            {
                count++;
                if (count == level)
                {
                    return x;
                }
            }
        }

        return -1;
    }

    // ---- overlaps ----

    /// <summary>
    /// Drops replacements that lie inside a larger one (the larger text already shows the final state of that
    /// stretch), sorts the rest by position and returns the region of a replacement that overlaps another only in part.
    /// </summary>
    private static RegionKey? Resolve(List<Splice> items)
    {
        items.Sort(static (x, y) =>
        {
            int c = x.Start.CompareTo(y.Start);
            if (c != 0)
            {
                return c;
            }

            c = y.End.CompareTo(x.End);
            return c != 0 ? c : x.Seq.CompareTo(y.Seq);
        });
        var kept = new List<Splice>(items.Count);
        Splice? cover = null;
        foreach (Splice x in items)
        {
            if (cover is not null && x.Start < cover.End)
            {
                bool inside = x.Start >= cover.Start && x.End <= cover.End;
                if (!inside)
                {
                    return x.Region;
                }

                bool empty = x.End == x.Start;
                if (!empty || (x.Start > cover.Start && x.Start < cover.End))
                {
                    continue;
                }
            }

            kept.Add(x);
            if (x.End > x.Start)
            {
                cover = x;
            }
        }

        // Insertions at one offset keep the order they were produced in; a replacement at the same offset follows them.
        kept.Sort(static (x, y) =>
        {
            int c = x.Start.CompareTo(y.Start);
            if (c != 0)
            {
                return c;
            }

            c = x.End.CompareTo(y.End);
            return c != 0 ? c : x.Seq.CompareTo(y.Seq);
        });
        items.Clear();
        items.AddRange(kept);
        return null;
    }

    // ---- level 0 ----

    private bool TryPrecise(RegionKey k, List<Splice> into, out string? why)
    {
        why = null;
        var local = new List<Splice>();
        bool ok = k.Kind switch
        {
            RegionKind.Attrs => TryAttrs(k.Node, local, out why),
            RegionKind.Text => TryText(k.Node, local, out why),
            _ => TryChildren(k.Node, local, out why),
        };
        if (!ok)
        {
            return false;
        }

        foreach (Splice s in local)
        {
            s.Region = k;
            s.Seq = _seq++;
            s.Level = 0;
            into.Add(s);
        }

        return true;
    }

    private static Splice Make(int start, int end, string text) => new() { Start = start, End = end, Text = text };

    // ---- attributes ----

    private bool TryAttrs(int e, List<Splice> into, out string? why)
    {
        why = null;
        if (!SourceInfo.HasStartTag(_ba, e))
        {
            why = "the start tag is not in the source";
            return false;
        }

        int tagStart = _ba.SrcStart[e];
        int tagEnd = _ba.SrcEnd[e];
        int baseStart = _ba.AttrStart[e];
        int baseCount = _ba.AttrCount[e];
        List<ModelAttr> now = _m.AttrsOf(e);
        var current = new Dictionary<int, int>();
        for (int i = 0; i < now.Count; i++)
        {
            if (now[i].Orig >= 0)
            {
                current[now[i].Orig] = i;
            }
        }

        int lastEnd = -1;
        bool html = _a.Ns[e] == (byte)ElementNamespace.Html;
        for (int i = 0; i < baseCount; i++)
        {
            int row = baseStart + i;
            var attr = new Attr(_base, row);
            SourceRange range = attr.Range;
            if (!range.IsPresent || attr.NameRange.Start < 0)
            {
                why = "an attribute has no bytes of its own";
                return false;
            }

            if (!current.TryGetValue(row, out int at))
            {
                int del = attr.NameRange.Start;
                while (del > 0 && IsSpace(_text[del - 1]))
                {
                    del--;
                }

                into.Add(Make(del, range.End, string.Empty));
                continue;
            }

            if (range.Start >= tagStart && range.End <= tagEnd)
            {
                lastEnd = Math.Max(lastEnd, range.End);
            }

            string value = now[at].Value;
            if (!attr.Value.SequenceEqual(value))
            {
                ValueSplice(e, row, attr, value, into);
            }
        }

        string added = string.Empty;
        foreach (ModelAttr m in now)
        {
            if (m.Orig >= 0)
            {
                continue;
            }

            string local = _m.Names.NameOf(m.Name);
            string q = m.Namespace switch
            {
                AttributeNamespace.XLink => "xlink:" + local,
                AttributeNamespace.Xml => "xml:" + local,
                AttributeNamespace.Xmlns => local == "xmlns" ? "xmlns" : "xmlns:" + local,
                _ => local,
            };
            var sb = new StringBuilder();
            _writer.WriteAttribute(q, html, m.Value, sb);
            added += sb.ToString();
        }

        if (added.Length > 0)
        {
            int pos = lastEnd;
            if (pos < 0)
            {
                pos = tagStart + 1;
                while (pos < tagEnd && _text[pos] is not (' ' or '\t' or '\n' or '\f' or '\r' or '/' or '>'))
                {
                    pos++;
                }
            }

            into.Add(Make(pos, pos, added));
        }

        return true;
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';

    private void ValueSplice(int e, int row, Attr attr, string value, List<Splice> into)
    {
        AttributeQuote quote = attr.Quote;
        SourceRange vr = attr.ValueRange;
        char styleQuote = _style.Quote;
        switch (quote)
        {
            case AttributeQuote.NoValue:
                into.Add(Make(attr.NameRange.End, attr.NameRange.End, "=" + styleQuote + _writer.EscapeAttributeValue(value, styleQuote) + styleQuote));
                break;
            case AttributeQuote.Unquoted:
                into.Add(SafeUnquoted(value)
                    ? Make(vr.Start, vr.End, value)
                    : Make(vr.Start, vr.End, styleQuote + _writer.EscapeAttributeValue(value, styleQuote) + styleQuote));
                break;
            default:
                {
                    char q = quote == AttributeQuote.SingleQuoted ? '\'' : '"';
                    string old = attr.Value.ToString();
                    bool verbatim = (_ba.AFlags[row] & AF.ValueSide) == 0 && old.Length == vr.End - vr.Start;
                    if (verbatim && _m.StyleEdited.Contains(e) && attr.LocalName == "style")
                    {
                        // A style edit names one declaration: change only the bytes between the common prefix and suffix.
                        int p = 0;
                        int max = Math.Min(old.Length, value.Length);
                        while (p < max && old[p] == value[p])
                        {
                            p++;
                        }

                        if (p > 0 && char.IsHighSurrogate(old[p - 1]))
                        {
                            p--;
                        }

                        int s = 0;
                        while (s < max - p && old[old.Length - 1 - s] == value[value.Length - 1 - s])
                        {
                            s++;
                        }

                        if (s > 0 && char.IsLowSurrogate(old[old.Length - s]))
                        {
                            s--;
                        }

                        string middle = value.Substring(p, value.Length - p - s);
                        into.Add(Make(vr.Start + p, vr.End - s, _writer.EscapeAttributeValue(middle, q)));
                    }
                    else
                    {
                        into.Add(Make(vr.Start, vr.End, _writer.EscapeAttributeValue(value, q)));
                    }

                    break;
                }
        }
    }

    private static bool SafeUnquoted(string v)
    {
        if (v.Length == 0 || v[^1] == '/')
        {
            return false;
        }

        foreach (char c in v)
        {
            bool ok = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_' or '.' or ':' or '/' or '#' or '%' or '+' or ',' or '@' or '~';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    // ---- text ----

    private bool TryText(int t, List<Splice> into, out string? why)
    {
        why = null;
        if (!SourceInfo.TryExtent(_ba, t, out int s, out int e))
        {
            why = "the text is not a stretch of the source";
            return false;
        }

        var sb = new StringBuilder();
        _writer.WriteLeaf(t, sb);
        if (_writer.Error is not null)
        {
            why = _writer.Error;
            return false;
        }

        into.Add(Make(s, e, sb.ToString()));
        return true;
    }

    // ---- children ----

    private void ComputeHasBase()
    {
        _hasBase = [];
        foreach (int w in _m.Wrapped)
        {
            if (_m.IsGone(w))
            {
                continue;
            }

            for (int x = _a.Parent[w]; x >= _m.BaseCount; x = _a.Parent[x])
            {
                if (!_hasBase.Add(x))
                {
                    break;
                }
            }
        }
    }

    private bool TryChildren(int p, List<Splice> into, out string? why)
    {
        why = null;
        var baseChildren = new List<int>();
        var position = new Dictionary<int, int>();
        for (int c = _ba.First[p]; c >= 0; c = _ba.Next[c])
        {
            position[c] = baseChildren.Count;
            baseChildren.Add(c);
        }

        // 1. Children that were removed: delete their bytes. Unwrapped elements lose only their tags.
        foreach (int b in baseChildren)
        {
            ushort f = _a.Flags[b];
            if ((f & EditModel.Dead) != 0)
            {
                if (!SourceInfo.TryExtent(_ba, b, out int s, out int e))
                {
                    why = "a removed node has no bytes of its own";
                    return false;
                }

                into.Add(Make(s, e, string.Empty));
            }
            else if ((f & EditModel.Unwrapped) != 0)
            {
                if (!SourceInfo.HasStartTag(_ba, b))
                {
                    why = "an unwrapped element's start tag is not in the source";
                    return false;
                }

                into.Add(Make(_ba.SrcStart[b], _ba.SrcEnd[b], string.Empty));
                if (_ba.EndStart[b] >= 0)
                {
                    into.Add(Make(_ba.EndStart[b], _ba.EndEnd[b], string.Empty));
                }
            }
        }

        if ((_a.Flags[p] & EditModel.Unwrapped) != 0)
        {
            return true;
        }

        // 2. New nodes: insert their markup between the original nodes around them.
        var members = new List<int>();
        for (int c = _a.First[p]; c >= 0; c = _a.Next[c])
        {
            members.Add(c);
        }

        int contentStart = -1;
        bool contentKnown = false;
        int cursor = -1;
        int pastBase = 0;
        int prevBase = -1;
        int i = 0;
        while (i < members.Count)
        {
            int node = members[i];
            if (node < _m.BaseCount)
            {
                int anchor = AnchorOf(node, p, position);
                if (anchor < 0)
                {
                    why = "a node came from outside this parent";
                    return false;
                }

                prevBase = node;
                cursor = -1;
                pastBase = Math.Max(pastBase, anchor + 1);
                i++;
                continue;
            }

            int k = i;
            while (k < members.Count && members[k] >= _m.BaseCount)
            {
                k++;
            }

            int upTo = baseChildren.Count;
            if (k < members.Count)
            {
                upTo = AnchorOf(members[k], p, position);
                if (upTo < 0)
                {
                    why = "a node came from outside this parent";
                    return false;
                }
            }

            int at = -1;
            bool replacing = false;
            for (int j = pastBase; j < upTo; j++)
            {
                if ((_a.Flags[baseChildren[j]] & EditModel.Dead) != 0)
                {
                    if (!SourceInfo.TryExtent(_ba, baseChildren[j], out at, out _))
                    {
                        why = "a removed node has no bytes of its own";
                        return false;
                    }

                    replacing = true;
                    break;
                }
            }

            if (at < 0)
            {
                if (cursor < 0 && prevBase >= 0)
                {
                    if (!SourceInfo.TryExtent(_ba, prevBase, out _, out int prevEnd))
                    {
                        why = "a kept node has no bytes of its own";
                        return false;
                    }

                    cursor = prevEnd;
                }

                if (cursor < 0)
                {
                    if (!contentKnown)
                    {
                        contentKnown = true;
                        contentStart = ContentStart(p);
                    }

                    if (contentStart < 0)
                    {
                        why = "the parent's start tag is not in the source";
                        return false;
                    }

                    cursor = contentStart;
                }

                at = cursor;
            }

            int where = at;
            for (int j = i; j < k; j++)
            {
                if (!EmitNew(members[j], ref where, into, ref why))
                {
                    return false;
                }
            }

            if (!replacing)
            {
                cursor = where;
            }

            i = k;
        }

        return true;
    }

    private int ContentStart(int p)
    {
        switch ((NodeKind)_a.Kind[p])
        {
            case NodeKind.Document:
                return 0;
            case NodeKind.DocumentFragment:
                {
                    int owner = _m.ContentOwner(p);
                    return owner >= 0 && SourceInfo.TryContentStart(_ba, owner, out int s) ? s : -1;
                }

            default:
                return SourceInfo.TryContentStart(_ba, p, out int start) ? start : -1;
        }
    }

    // The position in the original list of children of the child of p that holds an original node (its own position, or
    // that of the element it was unwrapped from), or -1.
    private int AnchorOf(int node, int p, Dictionary<int, int> position)
    {
        for (int x = node; x >= 0; x = _ba.Parent[x])
        {
            if (_ba.Parent[x] == p)
            {
                return position.GetValueOrDefault(x, -1);
            }
        }

        return -1;
    }

    private bool EmitNew(int n, ref int cursor, List<Splice> into, ref string? why)
    {
        if (!_hasBase.Contains(n))
        {
            var sb = new StringBuilder();
            _writer.WriteNode(n, sb);
            if (_writer.Error is not null)
            {
                why = _writer.Error;
                return false;
            }

            into.Add(Make(cursor, cursor, sb.ToString()));
            return true;
        }

        // A new element that holds original nodes (a wrapper): write its tags around the nodes' own bytes.
        var frames = new Stack<(int Node, int Next)>();
        if (!Open(n, ref cursor, into, frames, ref why))
        {
            return false;
        }

        while (frames.Count > 0)
        {
            (int node, int next) = frames.Pop();
            if (next < 0)
            {
                var sb = new StringBuilder();
                _writer.WriteCloseTag(node, sb);
                into.Add(Make(cursor, cursor, sb.ToString()));
                continue;
            }

            frames.Push((node, _a.Next[next]));
            if (next < _m.BaseCount)
            {
                if (!SourceInfo.TryExtent(_ba, next, out _, out int end))
                {
                    why = "a wrapped node has no bytes of its own";
                    return false;
                }

                cursor = end;
            }
            else if (_hasBase.Contains(next))
            {
                if (!Open(next, ref cursor, into, frames, ref why))
                {
                    return false;
                }
            }
            else
            {
                var sb = new StringBuilder();
                _writer.WriteNode(next, sb);
                into.Add(Make(cursor, cursor, sb.ToString()));
            }
        }

        if (_writer.Error is not null)
        {
            why = _writer.Error;
            return false;
        }

        return true;
    }

    private bool Open(int n, ref int cursor, List<Splice> into, Stack<(int Node, int Next)> frames, ref string? why)
    {
        int first = FirstBase(n);
        if (first < 0 || !SourceInfo.TryExtent(_ba, first, out int start, out _))
        {
            why = "a wrapped node has no bytes of its own";
            return false;
        }

        var sb = new StringBuilder();
        _writer.WriteOpenTag(n, sb);
        cursor = start;
        into.Add(Make(cursor, cursor, sb.ToString()));
        int holder = _a.Ns[n] == (byte)ElementNamespace.Html && _a.Name[n] == N.Template && _a.Aux[n] >= 0 ? _a.Aux[n] : n;
        frames.Push((n, _a.First[holder]));
        return true;
    }

    // The first original node (in document order) under a new element.
    private int FirstBase(int n)
    {
        var stack = new Stack<int>();
        stack.Push(n);
        while (stack.Count > 0)
        {
            int x = stack.Pop();
            if (x < _m.BaseCount && x != n)
            {
                return x;
            }

            for (int c = _a.Last[x]; c >= 0; c = _a.Prev[c])
            {
                if (c < _m.BaseCount || _hasBase.Contains(c))
                {
                    stack.Push(c);
                }
            }
        }

        return -1;
    }
}
