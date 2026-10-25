using RecursiveAI.OneHtml.Dom;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>Where the bytes of a node of the original snapshot are.</summary>
internal static class SourceInfo
{
    /// <summary>Start tag missing from the source, or a parser copy of another tag: the node has no bytes of its own.</summary>
    private const ushort NoStartTag = F.ImpliedStart | F.Cloned | F.Synthesized;

    /// <summary>
    /// The whole construct: for an element its start tag through its end tag (or, with no end tag, through its last
    /// descendant); for text, a comment or a DOCTYPE the bytes it was read from. False when the parser made the
    /// node or merged it from several places, so its bytes are not a stretch of the source.
    /// </summary>
    public static bool TryExtent(Arena a, int i, out int start, out int end)
    {
        start = -1;
        end = -1;
        ushort f = a.Flags[i];
        if (a.Kind[i] == (byte)NodeKind.Element)
        {
            if ((f & NoStartTag) != 0 || a.SrcStart[i] < 0)
            {
                return false;
            }

            start = a.SrcStart[i];
            int cur = i;
            while (true)
            {
                if (a.EndStart[cur] >= 0)
                {
                    end = a.EndEnd[cur];
                    break;
                }

                int holder = a.Ns[cur] == (byte)ElementNamespace.Html && a.Name[cur] == N.Template && a.Aux[cur] >= 0 ? a.Aux[cur] : cur;
                int last = a.Last[holder];
                if (last < 0)
                {
                    end = a.SrcEnd[cur];
                    break;
                }

                if (a.Kind[last] != (byte)NodeKind.Element)
                {
                    end = a.SrcEnd[last];
                    break;
                }

                cur = last;
            }

            return end >= start;
        }

        if ((f & (F.TextMerged | F.Synthesized)) != 0 || a.Kind[i] is (byte)NodeKind.Document or (byte)NodeKind.DocumentFragment)
        {
            return false;
        }

        start = a.SrcStart[i];
        end = a.SrcEnd[i];
        return start >= 0 && end >= start;
    }

    /// <summary>The offset just after an element's start tag, where its content begins; false when the tag is not in the source.</summary>
    public static bool TryContentStart(Arena a, int element, out int start)
    {
        start = -1;
        if (a.Kind[element] != (byte)NodeKind.Element || (a.Flags[element] & NoStartTag) != 0 || a.SrcEnd[element] < 0)
        {
            return false;
        }

        start = a.SrcEnd[element];
        return true;
    }

    public static bool HasStartTag(Arena a, int element) =>
        a.Kind[element] == (byte)NodeKind.Element && (a.Flags[element] & NoStartTag) == 0 && a.SrcStart[element] >= 0 && a.SrcEnd[element] >= 0;
}
