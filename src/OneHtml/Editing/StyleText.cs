using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>
/// Edits declarations in the text of a <c>style</c> attribute through the CSS Syntax ranges, so everything the edit
/// does not name (spacing, comments, other declarations, odd formatting) stays as written.
/// </summary>
internal static class StyleText
{
    /// <summary>Sets a declaration; returns the new attribute text, or null with <paramref name="error"/>.</summary>
    public static string? Set(string current, string property, string value, bool important, out string? error)
    {
        error = ValidatePropertyName(property);
        if (error is not null)
        {
            return null;
        }

        // The pair must parse as exactly one declaration, with the importance the caller asked for; this rejects
        // values that would end the declaration early or carry their own !important.
        string probe = property + ":" + value + (important ? " !important" : string.Empty);
        CssTree check = CssTree.ParseDeclarationList(probe);
        List<CssNode> nodes = Collect(check);
        if (nodes.Count != 1 || nodes[0].Kind != CssNodeKind.Declaration || nodes[0].IsImportant != important)
        {
            error = "the value is not a single CSS declaration value: " + value;
            return null;
        }

        CssTree tree = CssTree.ParseDeclarationList(current);
        List<CssNode> all = Collect(tree);
        CssNode? last = null;
        foreach (CssNode n in all)
        {
            if (n.Kind == CssNodeKind.Declaration && SameProperty(n, property))
            {
                last = n;
            }
        }

        if (last is { } d)
        {
            // Edit from the back so earlier offsets stay valid.
            string text = current;
            CssRange vr = d.ValueRange;
            if (important && !d.IsImportant)
            {
                text = text.Insert(vr.End, " !important");
            }
            else if (!important && d.IsImportant)
            {
                text = text.Remove(vr.End, d.ImportantRange.End - vr.End);
            }

            return text[..vr.Start] + value + text[vr.End..];
        }

        string declaration = property + DeclarationSeparator(all, current) + value + (important ? " !important" : string.Empty);
        CssNode? tail = null;
        foreach (CssNode n in all)
        {
            if (n.Kind == CssNodeKind.Declaration)
            {
                tail = n;
            }
        }

        if (tail is not { } t)
        {
            return current + declaration + ";";
        }

        int end = t.Range.End;
        bool terminated = end > 0 && current[end - 1] == ';';
        string gap = InterGap(all);
        return terminated
            ? current[..end] + gap + declaration + ";" + current[end..]
            : current[..end] + ";" + gap + declaration + current[end..];
    }

    /// <summary>Removes every declaration of a property; returns the new attribute text, or null with <paramref name="error"/>.</summary>
    public static string? Remove(string current, string property, out string? error)
    {
        error = ValidatePropertyName(property);
        if (error is not null)
        {
            return null;
        }

        CssTree tree = CssTree.ParseDeclarationList(current);
        List<CssNode> all = Collect(tree);
        string text = current;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            CssNode n = all[i];
            if (n.Kind != CssNodeKind.Declaration || !SameProperty(n, property))
            {
                continue;
            }

            if (i + 1 < all.Count)
            {
                text = text.Remove(n.Range.Start, all[i + 1].Range.Start - n.Range.Start);
            }
            else
            {
                int from = i > 0 ? all[i - 1].Range.End : 0;
                text = text.Remove(from, n.Range.End - from);
            }
        }

        return text;
    }

    private static List<CssNode> Collect(CssTree tree)
    {
        var list = new List<CssNode>();
        foreach (CssNode n in tree.Nodes)
        {
            list.Add(n);
        }

        return list;
    }

    private static bool SameProperty(CssNode d, string property) =>
        property.StartsWith("--", StringComparison.Ordinal)
            ? string.Equals(d.Name, property, StringComparison.Ordinal)
            : string.Equals(d.Name, property, StringComparison.OrdinalIgnoreCase);

    private static string? ValidatePropertyName(string property)
    {
        if (property.Length == 0)
        {
            return "the property name is empty";
        }

        for (int i = 0; i < property.Length; i++)
        {
            char c = property[i];
            bool ok = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '-' or '_' || (i > 0 && c is >= '0' and <= '9');
            if (!ok)
            {
                return "the property name is not a CSS identifier: " + property;
            }
        }

        return null;
    }

    // The text between a declaration's name and value, copied from the last declaration so the new one looks alike.
    private static string DeclarationSeparator(List<CssNode> all, string current)
    {
        for (int i = all.Count - 1; i >= 0; i--)
        {
            CssNode n = all[i];
            if (n.Kind != CssNodeKind.Declaration)
            {
                continue;
            }

            CssRange nr = n.NameRange;
            CssRange vr = n.ValueRange;
            if (vr.Start > nr.End && vr.Start <= current.Length)
            {
                string sep = current[nr.End..vr.Start];
                if (sep.IndexOf('/') < 0 && sep.Contains(':', StringComparison.Ordinal))
                {
                    return sep;
                }
            }

            break;
        }

        return ": ";
    }

    // The whitespace between two declarations, if there are two: otherwise one space.
    private static string InterGap(List<CssNode> all)
    {
        for (int i = 0; i + 1 < all.Count; i++)
        {
            if (all[i].Kind == CssNodeKind.Declaration && all[i + 1].Kind == CssNodeKind.Declaration)
            {
                CssTree t = all[i].Tree;
                string between = t.Text[all[i].Range.End..all[i + 1].Range.Start];
                if (between.Length <= 40 && ModelWhitespace(between))
                {
                    return between;
                }
            }
        }

        return " ";
    }

    private static bool ModelWhitespace(string s)
    {
        foreach (char c in s)
        {
            if (c is not (' ' or '\t' or '\n' or '\f' or '\r'))
            {
                return false;
            }
        }

        return true;
    }
}
