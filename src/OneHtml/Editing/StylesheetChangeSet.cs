using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>A stretch of a stylesheet's text that an edit replaced, in the old and the new text.</summary>
/// <param name="OldRange">The replaced range of the old text.</param>
/// <param name="NewRange">The range the replacement occupies in the new text.</param>
/// <param name="FallbackLevel">0 for a local splice; otherwise the number of enclosing rules rewritten.</param>
/// <param name="EditIndexes">The edits (positions in the sheet's edit list) behind the range.</param>
public sealed record StylesheetEditedRange(CssRange OldRange, CssRange NewRange, int FallbackLevel, IReadOnlyList<int> EditIndexes)
{
    /// <summary>True when an enclosing rule was re-serialized instead of a local splice.</summary>
    public bool IsFallback => FallbackLevel > 0;
}

/// <summary>A rule or declaration that exists in both snapshots with different contents.</summary>
/// <param name="Old">The node in the old sheet.</param>
/// <param name="New">The node in the new sheet.</param>
public readonly record struct StylesheetNodeChange(CssNode Old, CssNode New);

/// <summary>
/// What an edit transaction changed in one stylesheet: rules and declarations added, removed and changed, and the text
/// ranges involved. Added nodes belong to <see cref="NewSheet"/>, removed ones to <see cref="OldSheet"/>. A rule counts
/// as changed when an edit landed inside it, so a rule containing an added declaration is listed under
/// <see cref="Changed"/> along with the declaration under <see cref="Added"/>.
/// </summary>
public sealed class StylesheetChangeSet
{
    internal StylesheetChangeSet(Stylesheet oldSheet, Stylesheet newSheet, IReadOnlyList<CssNode> added, IReadOnlyList<CssNode> removed, IReadOnlyList<StylesheetNodeChange> changed, IReadOnlyList<StylesheetEditedRange> ranges)
    {
        OldSheet = oldSheet;
        NewSheet = newSheet;
        Added = added;
        Removed = removed;
        Changed = changed;
        Ranges = ranges;
    }

    /// <summary>The snapshot the edits were made for.</summary>
    public Stylesheet OldSheet { get; }

    /// <summary>The result.</summary>
    public Stylesheet NewSheet { get; }

    /// <summary>Rules and declarations of the new sheet that did not exist before, in source order.</summary>
    public IReadOnlyList<CssNode> Added { get; }

    /// <summary>Rules and declarations of the old sheet that are gone, in source order.</summary>
    public IReadOnlyList<CssNode> Removed { get; }

    /// <summary>Rules and declarations that continue to exist but had an edit land inside them, in source order.</summary>
    public IReadOnlyList<StylesheetNodeChange> Changed { get; }

    /// <summary>The replaced ranges in source order. Text outside them is identical in both sheets.</summary>
    public IReadOnlyList<StylesheetEditedRange> Ranges { get; }

    /// <summary>True when nothing changed.</summary>
    public bool IsEmpty => Ranges.Count == 0;

    internal static StylesheetChangeSet Build(Stylesheet oldSheet, Stylesheet newSheet, List<SheetPiece> pieces)
    {
        int n = pieces.Count;
        int[] delta = new int[n + 1];
        var ranges = new List<StylesheetEditedRange>(n);
        for (int i = 0; i < n; i++)
        {
            SheetPiece p = pieces[i];
            int ns = p.Start + delta[i];
            delta[i + 1] = delta[i] + p.Text.Length - (p.End - p.Start);
            ranges.Add(new StylesheetEditedRange(new CssRange(p.Start, p.End), new CssRange(ns, ns + p.Text.Length), p.Level, p.Edits.ToArray()));
        }

        CssTree ot = oldSheet.Tree;
        CssTree nt = newSheet.Tree;
        var removed = new List<CssNode>();
        var changed = new List<StylesheetNodeChange>();
        var oldMapped = new List<(int Item, int NS, int NE, bool Changed)>();
        for (int i = 1; i < ot.ItemCount; i++)
        {
            CssNodeKind kind = ot.Items[i].Kind;
            if (kind is not (CssNodeKind.QualifiedRule or CssNodeKind.AtRule or CssNodeKind.Declaration))
            {
                continue;
            }

            int s = ot.Items[i].Start;
            int e = ot.Items[i].End;
            // The pieces are in order: find the first one that could touch this node.
            int lo = FirstEndingAfter(pieces, s);
            bool inside = false;
            bool touched = false;
            for (int k = lo; k < n && pieces[k].Start <= e; k++)
            {
                SheetPiece p = pieces[k];
                bool nonEmpty = p.End > p.Start;
                if (nonEmpty && p.Start <= s && e <= p.End)
                {
                    inside = true;
                    break;
                }

                if ((nonEmpty && p.Start < e && p.End > s) || (!nonEmpty && p.Start > s && p.Start < e))
                {
                    touched = true;
                }
            }

            if (inside)
            {
                removed.Add(new CssNode(ot, i));
                continue;
            }

            int ns = MapStart(pieces, delta, s);
            int ne = MapEnd(pieces, delta, e);
            oldMapped.Add((i, ns, ne, touched));
        }

        var added = new List<CssNode>();
        var byRange = new Dictionary<(int, int, CssNodeKind), int>();
        var byStart = new Dictionary<(int, CssNodeKind), int>();
        for (int i = 1; i < nt.ItemCount; i++)
        {
            CssNodeKind kind = nt.Items[i].Kind;
            if (kind is CssNodeKind.QualifiedRule or CssNodeKind.AtRule or CssNodeKind.Declaration)
            {
                byRange.TryAdd((nt.Items[i].Start, nt.Items[i].End, kind), i);
                byStart.TryAdd((nt.Items[i].Start, kind), i);
            }
        }

        // An old node continues as the new node at its mapped start; it is changed when the end differs from the mapped
        // end (a semicolon added after it, say) or an edit landed inside it.
        var used = new HashSet<int>();
        foreach ((int item, int ns, int ne, bool touched) in oldMapped)
        {
            CssNodeKind kind = ot.Items[item].Kind;
            bool exact = byRange.TryGetValue((ns, ne, kind), out int ni);
            if (!exact && !byStart.TryGetValue((ns, kind), out ni))
            {
                removed.Add(new CssNode(ot, item));
                continue;
            }

            used.Add(ni);
            if (touched || !exact)
            {
                changed.Add(new StylesheetNodeChange(new CssNode(ot, item), new CssNode(nt, ni)));
            }
        }

        for (int i = 1; i < nt.ItemCount; i++)
        {
            if (nt.Items[i].Kind is CssNodeKind.QualifiedRule or CssNodeKind.AtRule or CssNodeKind.Declaration && !used.Contains(i))
            {
                added.Add(new CssNode(nt, i));
            }
        }

        removed.Sort(static (x, y) => x.Index.CompareTo(y.Index));

        return new StylesheetChangeSet(oldSheet, newSheet, added, removed, changed, ranges);
    }

    private static int FirstEndingAfter(List<SheetPiece> pieces, int pos)
    {
        int lo = 0;
        int hi = pieces.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (pieces[mid].End < pos)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    // The new position of an old start offset: every piece that ends at or before it counts (an insertion exactly at
    // the start goes in front of the node).
    private static int MapStart(List<SheetPiece> pieces, int[] delta, int pos)
    {
        int lo = 0;
        int hi = pieces.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (pieces[mid].End <= pos)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return pos + delta[lo];
    }

    // The new position of an old end offset: an insertion exactly at the end goes after the node.
    private static int MapEnd(List<SheetPiece> pieces, int[] delta, int pos)
    {
        int lo = 0;
        int hi = pieces.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            SheetPiece p = pieces[mid];
            if (p.End < pos || (p.End == pos && p.End > p.Start))
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return pos + delta[lo];
    }
}
