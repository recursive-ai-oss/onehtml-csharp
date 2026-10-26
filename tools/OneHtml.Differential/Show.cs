using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>
/// <c>show &lt;file&gt;</c>: prints our tree and AngleSharp's for one input, side by side in sequence, for looking at a
/// divergence by hand. The file is decoded as the parser sniffs it.
/// </summary>
internal static class Show
{
    public static int Run(Options o)
    {
        // Both parsers get the text our sniffer decoded, as the angle command does.
        Document ours = Document.Parse(File.ReadAllBytes(o.Corpus));
        string text = ours.Source.Text;
        IDocument theirs = new HtmlParser().ParseDocument(text);
        var sb = new StringBuilder();
        sb.AppendLine("== ours");
        Ours(sb, ours.Root, 0);
        sb.AppendLine("== AngleSharp");
        Theirs(sb, theirs, 0);
        Console.Write(sb.ToString());
        FirstTextDifference(ours.Root, theirs);
        return 0;
    }

    private static void Texts(Node n, List<string> list)
    {
        foreach (Node c in n.ChildNodes)
        {
            if (c.Kind == NodeKind.Text)
            {
                list.Add(c.Data.ToString());
            }
            else if (c.Kind == NodeKind.Element)
            {
                Texts(c, list);
            }
        }
    }

    private static void Texts(INode n, List<string> list)
    {
        foreach (INode c in n.ChildNodes)
        {
            if (c.NodeType == NodeType.Text)
            {
                list.Add(c.TextContent ?? string.Empty);
            }
            else if (c.NodeType == NodeType.Element)
            {
                Texts(c, list);
            }
        }
    }

    /// <summary>Prints where the first pair of text nodes that differ first differs, as code points.</summary>
    private static void FirstTextDifference(Node o, INode t)
    {
        var a = new List<string>();
        var b = new List<string>();
        Texts(o, a);
        Texts(t, b);
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
        {
            if (a[i] == b[i])
            {
                continue;
            }

            int k = 0;
            while (k < a[i].Length && k < b[i].Length && a[i][k] == b[i][k])
            {
                k++;
            }

            string Cp(string s) => string.Join(' ', s.Skip(Math.Max(0, k - 3)).Take(8).Select(c => ((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture)));
            Console.WriteLine($"== first text difference: node {i}, char {k}\n ours   {Cp(a[i])}\n theirs {Cp(b[i])}");
            return;
        }

        Console.WriteLine($"== text nodes: ours {a.Count}, theirs {b.Count}");
    }

    // Control characters are written as \u escapes so that differences in them can be seen.
    private static string Clip(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.Length > 60 ? s[..60] : s)
        {
            if (c < ' ' || (c >= '\u007f' && c <= '\u009f') || c == '\uFFFD')
            {
                sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.Append(s.Length > 60 ? "..." : string.Empty).Append(" [").Append(s.Length).Append(']').ToString();
    }

    private static void Ours(StringBuilder sb, Node n, int depth)
    {
        sb.Append(' ', depth * 2);
        switch (n.Kind)
        {
            case NodeKind.Element:
                sb.Append('<').Append(n.LocalName);
                foreach (Attr a in n.Attributes)
                {
                    sb.Append(' ').Append(a.QualifiedName).Append("=\"").Append(Clip(a.Value.ToString())).Append('"');
                }

                sb.Append('>');
                break;
            default:
                sb.Append('#').Append(n.Kind).Append(' ').Append(Clip(n.Data.ToString()));
                break;
        }

        sb.AppendLine();
        foreach (Node c in n.ChildNodes)
        {
            Ours(sb, c, depth + 1);
        }

        if (n.Kind == NodeKind.Element && n.LocalName == "template" && !n.TemplateContent.IsNull)
        {
            sb.Append(' ', (depth + 1) * 2).AppendLine("#content");
            foreach (Node c in n.TemplateContent.ChildNodes)
            {
                Ours(sb, c, depth + 2);
            }
        }
    }

    private static void Theirs(StringBuilder sb, INode n, int depth)
    {
        sb.Append(' ', depth * 2);
        if (n is IElement e)
        {
            sb.Append('<').Append(e.LocalName);
            foreach (IAttr a in e.Attributes)
            {
                sb.Append(' ').Append(a.Name).Append("=\"").Append(Clip(a.Value)).Append('"');
            }

            sb.Append('>');
        }
        else
        {
            sb.Append('#').Append(n.NodeType).Append(' ').Append(Clip(n.TextContent ?? string.Empty));
        }

        sb.AppendLine();
        foreach (INode c in n.ChildNodes)
        {
            Theirs(sb, c, depth + 1);
        }

        if (n is AngleSharp.Html.Dom.IHtmlTemplateElement t)
        {
            sb.Append(' ', (depth + 1) * 2).AppendLine("#content");
            foreach (INode c in t.Content.ChildNodes)
            {
                Theirs(sb, c, depth + 2);
            }
        }

        if (n is IElement { ShadowRoot: { } sr })
        {
            sb.Append(' ', (depth + 1) * 2).AppendLine("#shadow-root");
            foreach (INode c in sr.ChildNodes)
            {
                Theirs(sb, c, depth + 2);
            }
        }
    }
}
