using System;
using System.Collections.Generic;
using System.Text;

namespace RecursiveAI.OneHtml.Dom;

/// <summary>The HTML fragment serialization algorithm (HTML 13.3), written without recursion.</summary>
internal static class HtmlSerializer
{
    public static void WriteChildren(Document doc, int node, bool scripting, StringBuilder sb)
    {
        Arena a = doc.Arena;
        if (IsVoid(doc, node))
        {
            return;
        }

        Walk(doc, a, ChildrenHolder(a, node), scripting, sb);
    }

    public static void WriteNode(Document doc, int node, bool scripting, StringBuilder sb)
    {
        Arena a = doc.Arena;
        switch ((NodeKind)a.Kind[node])
        {
            case NodeKind.Document:
            case NodeKind.DocumentFragment:
                Walk(doc, a, node, scripting, sb);
                break;
            case NodeKind.Element:
                WriteOpen(doc, a, node, sb);
                if (!IsVoid(doc, node))
                {
                    Walk(doc, a, ChildrenHolder(a, node), scripting, sb);
                    WriteClose(doc, a, node, sb);
                }

                break;
            default:
                WriteLeaf(doc, a, node, scripting, sb);
                break;
        }
    }

    // A template serializes its content; everything else its own children.
    private static int ChildrenHolder(Arena a, int node) =>
        a.Kind[node] == (byte)NodeKind.Element && a.Ns[node] == (byte)ElementNamespace.Html && a.Name[node] == N.Template && a.Aux[node] >= 0
            ? a.Aux[node]
            : node;

    private static void Walk(Document doc, Arena a, int holder, bool scripting, StringBuilder sb)
    {
        var stack = new List<int>();
        int cur = a.First[holder];
        while (true)
        {
            if (cur >= 0)
            {
                if (a.Kind[cur] == (byte)NodeKind.Element)
                {
                    WriteOpen(doc, a, cur, sb);
                    if (!IsVoid(doc, cur))
                    {
                        stack.Add(cur);
                        cur = a.First[ChildrenHolder(a, cur)];
                        continue;
                    }
                }
                else
                {
                    WriteLeaf(doc, a, cur, scripting, sb);
                }

                cur = a.Next[cur];
            }
            else
            {
                if (stack.Count == 0)
                {
                    return;
                }

                int parent = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                WriteClose(doc, a, parent, sb);
                cur = a.Next[parent];
            }
        }
    }

    private static void WriteOpen(Document doc, Arena a, int e, StringBuilder sb)
    {
        sb.Append('<').Append(doc.Names.NameOf(a.Name[e]));
        int start = a.AttrStart[e];
        for (int i = 0; i < a.AttrCount[e]; i++)
        {
            int at = start + i;
            sb.Append(' ');
            int nsBits = (a.AFlags[at] & AF.NsMask) >> AF.NsShift;
            string local = doc.Names.NameOf(a.AName[at]);
            switch ((AttributeNamespace)nsBits)
            {
                case AttributeNamespace.XLink:
                    sb.Append("xlink:");
                    break;
                case AttributeNamespace.Xml:
                    sb.Append("xml:");
                    break;
                case AttributeNamespace.Xmlns:
                    if (local != "xmlns")
                    {
                        sb.Append("xmlns:");
                    }

                    break;
                default:
                    break;
            }

            sb.Append(local).Append("=\"");
            ReadOnlySpan<char> value = (a.AFlags[at] & AF.ValueSide) != 0
                ? a.Side.AsSpan(a.AValOff[at], a.AValLen[at])
                : doc.Source.Text.AsSpan(a.AValOff[at], a.AValLen[at]);
            Escape(value, attributeMode: true, sb);
            sb.Append('"');
        }

        sb.Append('>');
    }

    private static void WriteClose(Document doc, Arena a, int e, StringBuilder sb) =>
        sb.Append("</").Append(doc.Names.NameOf(a.Name[e])).Append('>');

    private static void WriteLeaf(Document doc, Arena a, int n, bool scripting, StringBuilder sb)
    {
        ReadOnlySpan<char> data = (a.Flags[n] & F.DataSide) != 0
            ? a.Side.AsSpan(a.DataOff[n], a.DataLen[n])
            : doc.Source.Text.AsSpan(a.DataOff[n], a.DataLen[n]);
        switch ((NodeKind)a.Kind[n])
        {
            case NodeKind.Text:
                if (ParentIsRawText(a, n, scripting))
                {
                    sb.Append(data);
                }
                else
                {
                    Escape(data, attributeMode: false, sb);
                }

                break;
            case NodeKind.Comment:
                sb.Append("<!--").Append(data).Append("-->");
                break;
            case NodeKind.ProcessingInstruction:
                sb.Append("<?").Append(doc.Names.NameOf(a.Name[n])).Append(' ').Append(data).Append("?>");
                break;
            case NodeKind.DocumentType:
                sb.Append("<!DOCTYPE ").Append(doc.Names.NameOf(a.Name[n])).Append('>');
                break;
            default:
                break;
        }
    }

    private static bool ParentIsRawText(Arena a, int text, bool scripting)
    {
        int p = a.Parent[text];
        if (p < 0 || a.Kind[p] != (byte)NodeKind.Element || a.Ns[p] != (byte)ElementNamespace.Html)
        {
            return false;
        }

        int name = a.Name[p];
        return name is N.Style or N.Script or N.Xmp or N.Iframe or N.Noembed or N.Noframes or N.Plaintext
            || (name == N.Noscript && scripting);
    }

    private static bool IsVoid(Document doc, int node)
    {
        Arena a = doc.Arena;
        if (a.Kind[node] != (byte)NodeKind.Element || a.Ns[node] != (byte)ElementNamespace.Html)
        {
            return false;
        }

        return a.Name[node] is N.Area or N.Base or N.Basefont or N.Bgsound or N.Br or N.Col or N.Embed or N.Frame or N.Hr
            or N.Img or N.Input or N.Keygen or N.Link or N.Meta or N.Param or N.Source or N.Track or N.Wbr;
    }

    private static void Escape(ReadOnlySpan<char> s, bool attributeMode, StringBuilder sb)
    {
        int from = 0;
        for (int i = 0; i < s.Length; i++)
        {
            string? rep = s[i] switch
            {
                '&' => "&amp;",
                '\u00A0' => "&nbsp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' when attributeMode => "&quot;",
                _ => null,
            };
            if (rep is not null)
            {
                sb.Append(s[from..i]).Append(rep);
                from = i + 1;
            }
        }

        sb.Append(s[from..]);
    }
}
