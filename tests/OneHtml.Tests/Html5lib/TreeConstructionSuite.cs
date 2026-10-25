using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RecursiveAI.OneHtml.Tests.Html5lib;

/// <summary>One case of a tree construction <c>.dat</c> file.</summary>
internal sealed record TreeCase(string File, int Index, string Data, string? FragmentContext, bool? Scripting, string Expected);

/// <summary>Reads the tree construction test files and writes trees in their dump format.</summary>
internal static class TreeConstructionSuite
{
    public static string Directory { get; } = Path.Combine(TestPaths.TestData, "wpt", "html-syntax-parsing");

    public static IReadOnlyList<string> Files()
    {
        var list = new List<string>();
        foreach (string f in System.IO.Directory.GetFiles(Directory, "*.dat"))
        {
            list.Add(Path.GetFileName(f));
        }

        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static readonly Dictionary<string, List<TreeCase>> s_cache = new(StringComparer.Ordinal);

    public static List<TreeCase> Load(string file)
    {
        lock (s_cache)
        {
            if (!s_cache.TryGetValue(file, out List<TreeCase>? list))
            {
                list = Parse(file);
                s_cache[file] = list;
            }

            return list;
        }
    }

    private static List<TreeCase> Parse(string file)
    {
        string text = File.ReadAllText(Path.Combine(Directory, file), new UTF8Encoding(false));
        string[] lines = text.Split('\n');
        var cases = new List<TreeCase>();
        int i = 0;
        while (i < lines.Length)
        {
            if (lines[i].Length == 0)
            {
                i++;
                continue;
            }

            if (lines[i] == "#description")
            {
                i++;
                while (i < lines.Length && lines[i] != "#data")
                {
                    i++;
                }
            }

            if (lines[i] != "#data")
            {
                throw new InvalidDataException($"{file}: expected #data at line {i + 1}, found '{lines[i]}'");
            }

            i++;
            var data = new List<string>();
            while (lines[i] != "#errors")
            {
                data.Add(lines[i++]);
            }

            string? fragment = null;
            bool? scripting = null;
            while (lines[i] != "#document")
            {
                if (lines[i] == "#document-fragment")
                {
                    fragment = lines[i + 1];
                    i++;
                }
                else if (lines[i] == "#script-on")
                {
                    scripting = true;
                }
                else if (lines[i] == "#script-off")
                {
                    scripting = false;
                }

                i++;
            }

            i++;
            var doc = new List<string>();
            while (i < lines.Length)
            {
                if (lines[i].Length == 0 && (i + 1 >= lines.Length || lines[i + 1] == "#data" || lines[i + 1] == "#description" || (lines[i + 1].Length == 0 && i + 2 >= lines.Length)))
                {
                    break;
                }

                doc.Add(lines[i++]);
            }

            cases.Add(new TreeCase(file, cases.Count, string.Join('\n', data), fragment, scripting, string.Join('\n', doc)));
        }

        return cases;
    }

    /// <summary>The dump of a document or fragment: one line per node, as the suite's README describes. No recursion.</summary>
    public static string Dump(Node root)
    {
        var sb = new StringBuilder();
        var stack = new List<(Node Node, int Depth)>();
        Push(stack, root, 0, sb);
        return sb.ToString();
    }

    private static void Push(List<(Node, int)> stack, Node root, int depth, StringBuilder sb)
    {
        // Children are pushed in reverse so that they pop in document order.
        PushChildren(stack, root, depth);
        bool first = true;
        while (stack.Count > 0)
        {
            (Node node, int d) = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            if (!first)
            {
                sb.Append('\n');
            }

            first = false;
            sb.Append("| ").Append(' ', d * 2);
            switch (node.Kind)
            {
                case NodeKind.Element:
                    sb.Append('<').Append(node.Namespace switch { ElementNamespace.Svg => "svg ", ElementNamespace.MathMl => "math ", _ => string.Empty }).Append(node.LocalName).Append('>');
                    var attrs = new List<(string Name, string Value)>();
                    foreach (Attr a in node.Attributes)
                    {
                        string prefix = a.Namespace switch { AttributeNamespace.XLink => "xlink ", AttributeNamespace.Xml => "xml ", AttributeNamespace.Xmlns => "xmlns ", _ => string.Empty };
                        attrs.Add((prefix + a.LocalName, a.Value.ToString()));
                    }

                    attrs.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
                    foreach ((string name, string value) in attrs)
                    {
                        sb.Append("\n| ").Append(' ', (d + 1) * 2).Append(name).Append("=\"").Append(value).Append('"');
                    }

                    Node content = node.TemplateContent;
                    if (!content.IsNull)
                    {
                        sb.Append("\n| ").Append(' ', (d + 1) * 2).Append("content");
                        PushChildren(stack, content, d + 2);
                    }
                    else
                    {
                        PushChildren(stack, node, d + 1);
                    }

                    break;
                case NodeKind.Text:
                    sb.Append('"').Append(node.Data).Append('"');
                    break;
                case NodeKind.Comment:
                    sb.Append("<!-- ").Append(node.Data).Append(" -->");
                    break;
                case NodeKind.ProcessingInstruction:
                    sb.Append("<?").Append(node.LocalName).Append(' ').Append(node.Data).Append("?>");
                    break;
                case NodeKind.DocumentType:
                    sb.Append("<!DOCTYPE ").Append(node.LocalName);
                    if (node.PublicId.Length > 0 || node.SystemId.Length > 0)
                    {
                        sb.Append(" \"").Append(node.PublicId).Append("\" \"").Append(node.SystemId).Append('"');
                    }

                    sb.Append('>');
                    break;
                default:
                    break;
            }
        }
    }

    private static void PushChildren(List<(Node, int)> stack, Node parent, int depth)
    {
        for (Node c = parent.LastChild; !c.IsNull; c = c.PreviousSibling)
        {
            stack.Add((c, depth));
        }
    }
}
