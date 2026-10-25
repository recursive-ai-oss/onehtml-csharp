using System;
using System.Collections.Generic;
using System.Text;
using RecursiveAI.OneHtml.Dom;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>
/// Compares the working tree (what the edits should have produced) with the re-parsed output, node by node and
/// without recursion, and records which node of the output each working node became. Runs of adjacent text nodes
/// count as one (the parser merges them) and empty text counts as nothing.
/// </summary>
internal static class TreeComparer
{
    /// <summary>Returns null when the trees are equal, otherwise a description of the first difference.</summary>
    /// <param name="expected">The working tree's view.</param>
    /// <param name="got">The re-parsed output.</param>
    /// <param name="map">Filled with, for each working node, the index of its counterpart in <paramref name="got"/> or -1.</param>
    public static string? Compare(Document expected, Document got, int[] map)
    {
        Array.Fill(map, -1);
        Arena ea = expected.Arena;
        Arena ga = got.Arena;
        var stack = new Stack<(int E, int G)>();
        map[0] = 0;
        stack.Push((0, 0));
        var expItems = new List<int>();
        var gotItems = new List<int>();
        while (stack.Count > 0)
        {
            (int e, int g) = stack.Pop();
            if (ea.Kind[e] == (byte)NodeKind.Element && ea.Ns[e] == (byte)ElementNamespace.Html && expected.Names.NameOf(ea.Name[e]) == "selectedcontent")
            {
                // The parser fills selectedcontent with a copy of the selected option; it follows the option, not the edit.
                continue;
            }

            Collect(ea, e, expItems, expected);
            gotItems.Clear();
            for (int c = ga.First[g]; c >= 0; c = ga.Next[c])
            {
                gotItems.Add(c);
            }

            int gi = 0;
            for (int ei = 0; ei < expItems.Count; ei++)
            {
                int en = expItems[ei];
                if (gi >= gotItems.Count)
                {
                    return Describe(expected, en, "is missing from the output");
                }

                int gn = gotItems[gi++];
                if (ea.Kind[en] != ga.Kind[gn])
                {
                    return Describe(expected, en, "is " + (NodeKind)ga.Kind[gn] + " in the output");
                }

                if (ea.Kind[en] == (byte)NodeKind.Text)
                {
                    // Text: gather the run of adjacent text nodes (the next ones in the list) and compare as one.
                    int runEnd = ei;
                    while (runEnd + 1 < expItems.Count && ea.Kind[expItems[runEnd + 1]] == (byte)NodeKind.Text && ea.Parent[expItems[runEnd + 1]] == ea.Parent[en])
                    {
                        runEnd++;
                    }

                    if (!TextEqual(expected, expItems, ei, runEnd, new Node(got, gn).Data))
                    {
                        return Describe(expected, en, "has different text: expected '" + Preview(expected, expItems, ei, runEnd) + "' but the output has '" + Cut(new Node(got, gn).Data.ToString()) + "'");
                    }

                    for (int r = ei; r <= runEnd; r++)
                    {
                        map[expItems[r]] = gn;
                    }

                    ei = runEnd;
                    continue;
                }

                string? diff = NodeDifference(expected, en, got, gn);
                if (diff is not null)
                {
                    return Describe(expected, en, diff);
                }

                map[en] = gn;
                if (ea.Kind[en] == (byte)NodeKind.Element && ea.Aux[en] >= 0)
                {
                    if (ga.Aux[gn] < 0)
                    {
                        return Describe(expected, en, "has template content in the edited tree but not in the output");
                    }

                    map[ea.Aux[en]] = ga.Aux[gn];
                    stack.Push((ea.Aux[en], ga.Aux[gn]));
                }
                else if (ea.Kind[en] == (byte)NodeKind.Element && ga.Aux[gn] >= 0)
                {
                    return Describe(expected, en, "has template content in the output but not in the edited tree");
                }

                if (ea.Kind[en] == (byte)NodeKind.Element)
                {
                    stack.Push((en, gn));
                }
            }

            if (gi < gotItems.Count)
            {
                return "the output has an extra " + (NodeKind)ga.Kind[gotItems[gi]] + " after " + Path(expected, e);
            }
        }

        return null;
    }

    // The children of a node as the parser would produce them: no empty text nodes.
    private static void Collect(Arena a, int parent, List<int> into, Document doc)
    {
        into.Clear();
        for (int c = a.First[parent]; c >= 0; c = a.Next[c])
        {
            if (a.Kind[c] == (byte)NodeKind.Text && new Node(doc, c).Data.IsEmpty)
            {
                continue;
            }

            into.Add(c);
        }
    }

    private static bool TextEqual(Document doc, List<int> items, int from, int to, ReadOnlySpan<char> got)
    {
        int pos = 0;
        for (int i = from; i <= to; i++)
        {
            ReadOnlySpan<char> piece = new Node(doc, items[i]).Data;
            if (pos + piece.Length > got.Length || !got.Slice(pos, piece.Length).SequenceEqual(piece))
            {
                return false;
            }

            pos += piece.Length;
        }

        return pos == got.Length;
    }

    private static string Preview(Document doc, List<int> items, int from, int to)
    {
        var sb = new StringBuilder();
        for (int i = from; i <= to && sb.Length < 40; i++)
        {
            sb.Append(new Node(doc, items[i]).Data);
        }

        return Cut(sb.ToString());
    }

    private static string Cut(string s) => s.Length > 40 ? s[..40] + "…" : s;

    private static string? NodeDifference(Document x, int e, Document y, int g)
    {
        Arena ea = x.Arena;
        Arena ga = y.Arena;
        switch ((NodeKind)ea.Kind[e])
        {
            case NodeKind.Element:
                if (ea.Ns[e] != ga.Ns[g] || x.Names.NameOf(ea.Name[e]) != y.Names.NameOf(ga.Name[g]))
                {
                    return "is <" + x.Names.NameOf(ea.Name[e]) + "> in the edited tree but <" + y.Names.NameOf(ga.Name[g]) + "> in the output";
                }

                if (ea.AttrCount[e] != ga.AttrCount[g])
                {
                    return "has " + ea.AttrCount[e] + " attributes in the edited tree but " + ga.AttrCount[g] + " in the output";
                }

                for (int i = 0; i < ea.AttrCount[e]; i++)
                {
                    int er = ea.AttrStart[e] + i;
                    int gr = ga.AttrStart[g] + i;
                    var xa = new Attr(x, er);
                    var ya = new Attr(y, gr);
                    if (xa.QualifiedName != ya.QualifiedName || xa.Namespace != ya.Namespace || !xa.Value.SequenceEqual(ya.Value))
                    {
                        return "differs at attribute '" + xa.QualifiedName + "' (the output has '" + ya.QualifiedName + "')";
                    }
                }

                return null;
            case NodeKind.Comment or NodeKind.ProcessingInstruction:
                return new Node(x, e).Data.SequenceEqual(new Node(y, g).Data) && new Node(x, e).LocalName == new Node(y, g).LocalName ? null : "has different data";
            case NodeKind.DocumentType:
                return new Node(x, e).LocalName == new Node(y, g).LocalName && new Node(x, e).PublicId == new Node(y, g).PublicId && new Node(x, e).SystemId == new Node(y, g).SystemId
                    ? null
                    : "is a different doctype";
            default:
                return null;
        }
    }

    private static string Describe(Document doc, int node, string what) => Path(doc, node) + " " + what;

    private static string Path(Document doc, int node)
    {
        Arena a = doc.Arena;
        var parts = new List<string>();
        for (int x = node; x >= 0 && parts.Count < 8; x = a.Parent[x])
        {
            parts.Add(a.Kind[x] == (byte)NodeKind.Element ? doc.Names.NameOf(a.Name[x]) : ((NodeKind)a.Kind[x]).ToString());
        }

        parts.Reverse();
        return string.Join('/', parts);
    }
}
