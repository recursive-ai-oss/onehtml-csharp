using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RecursiveAI.OneHtml.Css;
using RecursiveAI.OneHtml.Dom;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>A <c>&lt;style&gt;</c> element of a document with the stylesheet its text holds.</summary>
public sealed class InlineStylesheet
{
    internal InlineStylesheet(int index, Node element, Stylesheet sheet, int textNode)
    {
        Index = index;
        Element = element;
        Sheet = sheet;
        TextNode = textNode;
    }

    /// <summary>The position among the document's HTML <c>style</c> elements in tree order, from 0.</summary>
    public int Index { get; }

    /// <summary>The saved-edit name of the sheet: <c>inline:</c> followed by <see cref="Index"/>.</summary>
    public string Name => "inline:" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The <c>style</c> element.</summary>
    public Node Element { get; }

    /// <summary>
    /// The parsed sheet. Its text is the element's source text byte for byte (carriage returns kept), so its offsets
    /// are offsets into the element's content.
    /// </summary>
    public Stylesheet Sheet { get; }

    /// <summary>The index of the element's only text child when it has exactly one that is a stretch of the source, otherwise -1.</summary>
    internal int TextNode { get; }
}

/// <summary>Finds the stylesheets embedded in a document.</summary>
public static class InlineStylesheets
{
    private static readonly ConditionalWeakTable<Document, IReadOnlyList<InlineStylesheet>> s_cache = [];

    /// <summary>
    /// The document's <c>style</c> elements of the HTML namespace in tree order (template contents excluded), each
    /// with its parsed sheet. The list is computed once per snapshot.
    /// </summary>
    /// <param name="document">The snapshot.</param>
    /// <exception cref="CssLimitExceededException">A sheet exceeds a CSS limit.</exception>
    public static IReadOnlyList<InlineStylesheet> Of(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (s_cache.TryGetValue(document, out IReadOnlyList<InlineStylesheet>? cached))
        {
            return cached;
        }

        IReadOnlyList<InlineStylesheet> list = Collect(document);
        return s_cache.GetValue(document, _ => list);
    }

    private static List<InlineStylesheet> Collect(Document doc)
    {
        var result = new List<InlineStylesheet>();
        Arena a = doc.Arena;
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            int n = stack.Pop();
            if (n != 0 && a.Kind[n] == (byte)NodeKind.Element && a.Ns[n] == (byte)ElementNamespace.Html && doc.Names.NameOf(a.Name[n]) == "style")
            {
                result.Add(Make(doc, result.Count, n));
            }

            // Children in reverse so they pop in tree order.
            var kids = new List<int>();
            for (int c = a.First[n]; c >= 0; c = a.Next[c])
            {
                if (a.Kind[c] == (byte)NodeKind.Element)
                {
                    kids.Add(c);
                }
            }

            for (int i = kids.Count - 1; i >= 0; i--)
            {
                stack.Push(kids[i]);
            }
        }

        return result;
    }

    private static InlineStylesheet Make(Document doc, int index, int element)
    {
        Arena a = doc.Arena;
        int textCount = 0;
        int only = -1;
        for (int c = a.First[element]; c >= 0; c = a.Next[c])
        {
            if (a.Kind[c] == (byte)NodeKind.Text)
            {
                textCount++;
                only = c;
            }
        }

        string text = string.Empty;
        int textNode = -1;
        if (textCount == 1 && SourceInfo.TryExtent(a, only, out int s, out int e) && e <= doc.Source.Text.Length)
        {
            text = doc.Source.Text.Substring(s, e - s);
            textNode = only;
        }
        else if (textCount > 0)
        {
            var sb = new System.Text.StringBuilder();
            for (int c = a.First[element]; c >= 0; c = a.Next[c])
            {
                if (a.Kind[c] == (byte)NodeKind.Text)
                {
                    sb.Append(new Node(doc, c).Data);
                }
            }

            text = sb.ToString();
        }

        string name = "inline:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new InlineStylesheet(index, new Node(doc, element), Stylesheet.Parse(text, name), textNode);
    }
}
