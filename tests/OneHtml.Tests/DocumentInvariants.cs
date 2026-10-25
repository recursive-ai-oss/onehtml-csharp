using System;
using System.Collections.Generic;
using System.Globalization;

namespace RecursiveAI.OneHtml.Tests;

/// <summary>
/// Structural checks every document must satisfy, written against the public API only so the fuzz target can share
/// the file. Nothing recurses on tree depth.
/// </summary>
public static class DocumentInvariants
{
    /// <summary>Returns a description of the first broken invariant, or null.</summary>
    public static string? Check(Document doc)
    {
        int textLength = doc.Source.Text.Length;
        var seen = new HashSet<int>();
        var pending = new Stack<Node>();
        pending.Push(doc.Root);
        int reachable = 0;
        while (pending.Count > 0)
        {
            Node node = pending.Pop();
            if (!seen.Add(node.Index))
            {
                return $"node {node.Index} reached twice";
            }

            reachable++;
            string? problem = CheckNode(node, textLength);
            if (problem is not null)
            {
                return problem;
            }

            Node prev = default;
            foreach (Node child in node.ChildNodes)
            {
                if (child.Parent != node)
                {
                    return $"child {child.Index} of {node.Index} has parent {child.Parent.Index}";
                }

                if (child.PreviousSibling != prev)
                {
                    return $"child {child.Index} has a wrong previous sibling";
                }

                if (!prev.IsNull && prev.NextSibling != child)
                {
                    return $"node {prev.Index} has a wrong next sibling";
                }

                prev = child;
                pending.Push(child);
            }

            if (node.LastChild != prev)
            {
                return $"node {node.Index} has a wrong last child";
            }

            if (node.IsElement && !node.TemplateContent.IsNull)
            {
                Node content = node.TemplateContent;
                if (content.Kind != NodeKind.DocumentFragment || !content.Parent.IsNull)
                {
                    return $"template {node.Index} has a bad content root";
                }

                pending.Push(content);
            }
        }

        if (reachable > doc.NodeCount)
        {
            return "more reachable nodes than nodes";
        }

        return null;
    }

    private static string? CheckNode(Node node, int textLength)
    {
        NodeSource src = node.Source;
        if (!RangeOk(src.Range, textLength) || !RangeOk(src.EndTag, textLength) || !RangeOk(src.DataRange, textLength))
        {
            return $"node {node.Index} ({node.Kind}) has a range outside the text: {src.Range} {src.EndTag}";
        }

        switch (node.Kind)
        {
            case NodeKind.Text:
                if (node.Data.Length == 0)
                {
                    return $"empty text node {node.Index}";
                }

                if (node.HasChildNodes)
                {
                    return "text with children";
                }

                break;
            case NodeKind.Element:
                foreach (Attr a in node.Attributes)
                {
                    if (!RangeOk(a.NameRange, textLength) || !RangeOk(a.ValueRange, textLength) || !RangeOk(a.Range, textLength))
                    {
                        return $"attribute {a.LocalName} of {node.Index} has a range outside the text";
                    }

                    if (a.ValueRange.Start < a.NameRange.End)
                    {
                        return $"attribute {a.LocalName} of {node.Index}: value before name";
                    }
                }

                break;
            case NodeKind.Comment or NodeKind.ProcessingInstruction or NodeKind.DocumentType:
                if (node.HasChildNodes)
                {
                    return "leaf with children";
                }

                break;
            default:
                break;
        }

        return null;
    }

    private static bool RangeOk(SourceRange r, int length) => !r.IsPresent || (r.Start >= 0 && r.Start <= r.End && r.End <= length);

    /// <summary>A short description of a tree for failure messages.</summary>
    public static string Describe(Document doc) => string.Create(CultureInfo.InvariantCulture, $"{doc.NodeCount} nodes, {doc.Mode}");

    /// <summary>
    /// True when serializing the document and parsing the result is expected to give the same markup: the tree has
    /// none of the shapes the standard says do not survive the round trip (forms, tables, misnested formatting, raw
    /// text elements and the other elements whose parsing depends on the surrounding stack).
    /// </summary>
    public static bool ExpectsStableRoundTrip(Document doc)
    {
        var pending = new Stack<Node>();
        pending.Push(doc.Root);
        while (pending.Count > 0)
        {
            Node node = pending.Pop();
            if ((node.Origin & (NodeOrigin.Cloned | NodeOrigin.Reparented | NodeOrigin.MergedText | NodeOrigin.AttributesMerged)) != 0)
            {
                return false;
            }

            if (node.IsElement)
            {
                if (node.Namespace != ElementNamespace.Html || !StableTags.Contains(node.LocalName))
                {
                    return false;
                }

                if (node.Attributes.Count > 0)
                {
                    foreach (Attr a in node.Attributes)
                    {
                        if (a.Value.IndexOf('\0') >= 0)
                        {
                            return false;
                        }
                    }
                }
            }
            else if (node.Kind is NodeKind.Comment or NodeKind.ProcessingInstruction or NodeKind.DocumentType)
            {
                return false;
            }
            else if (node.Kind == NodeKind.Text)
            {
                foreach (char c in node.Data)
                {
                    if (c is '\0' or '\r')
                    {
                        return false;
                    }
                }
            }

            foreach (Node child in node.ChildNodes)
            {
                pending.Push(child);
            }
        }

        return true;
    }

    private static readonly HashSet<string> StableTags = new(StringComparer.Ordinal)
    {
        "html", "head", "body", "div", "span", "b", "i", "em", "strong", "u", "s", "small", "big", "code", "tt", "section", "article",
        "nav", "aside", "header", "footer", "main", "figure", "blockquote", "center", "sub", "sup", "abbr", "cite", "title", "label",
    };
}
