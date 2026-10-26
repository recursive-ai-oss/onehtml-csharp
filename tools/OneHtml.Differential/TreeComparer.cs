using System;
using System.Collections.Generic;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>One difference between our tree and AngleSharp's, found by <see cref="TreeComparer"/>.</summary>
/// <param name="Field">What differs: <c>kind</c>, <c>namespace</c>, <c>localName</c>, <c>attr.name</c>, <c>attr.value</c>, and so on.</param>
/// <param name="Path">Where, as a slash path of element names with child indexes.</param>
/// <param name="Ours">A short description of our side (node kind or name, attribute name, or a value).</param>
/// <param name="Theirs">A short description of AngleSharp's side.</param>
/// <param name="NodeDesc">The node type involved, for grouping.</param>
/// <param name="Hint">A text offset near the difference in our source, or -1.</param>
internal sealed record Mismatch(string Field, string Path, string Ours, string Theirs, string NodeDesc, int Hint)
{
    /// <summary>The key reproducers must keep: same field, same node types on both sides.</summary>
    public string Signature => Field is "text" or "comment" or "attr.value" or "childCount" or "attr.order" or "attr.namespace" or "attr.prefix" or "quirks" || Field.StartsWith("doctype.", StringComparison.Ordinal)
        ? Field
        : Field + "|" + Ours + "|" + Theirs;
}

/// <summary>
/// Compares a OneHtml document with an AngleSharp document node by node, in document order. After a difference in a
/// node's kind, namespace or name the node's subtree is not compared, since everything below would only repeat it.
/// </summary>
internal static class TreeComparer
{
    private const string HtmlNs = "http://www.w3.org/1999/xhtml";
    private const int MaxMismatches = 500;

    private sealed class Step
    {
        public Step(Step? parent, string segment)
        {
            Parent = parent;
            Segment = segment;
        }

        public Step? Parent { get; }

        public string Segment { get; }

        public override string ToString() => (Parent is null ? string.Empty : Parent + "/") + Segment;

        /// <summary>The element names along the path without indexes, last <paramref name="count"/> of them.</summary>
        public string Shape(int count)
        {
            var parts = new List<string>();
            for (Step? s = this; s is not null && parts.Count < count; s = s.Parent)
            {
                int bracket = s.Segment.IndexOf('[', StringComparison.Ordinal);
                parts.Add(bracket < 0 ? s.Segment : s.Segment[..bracket]);
            }

            parts.Reverse();
            return string.Join('>', parts);
        }
    }

    public static string NamespaceUri(ElementNamespace ns) => ns switch
    {
        ElementNamespace.MathMl => "http://www.w3.org/1998/Math/MathML",
        ElementNamespace.Svg => "http://www.w3.org/2000/svg",
        _ => HtmlNs,
    };

    private static string? AttributeNamespaceUri(AttributeNamespace ns) => ns switch
    {
        AttributeNamespace.XLink => "http://www.w3.org/1999/xlink",
        AttributeNamespace.Xml => "http://www.w3.org/XML/1998/namespace",
        AttributeNamespace.Xmlns => "http://www.w3.org/2000/xmlns/",
        _ => null,
    };

    /// <summary>Compares the two documents. Document-level fields (quirks mode) come first.</summary>
    public static List<Mismatch> Compare(Document ours, IDocument theirs)
    {
        var found = new List<Mismatch>();
        // AngleSharp reports only BackCompat (quirks) and CSS1Compat (everything else), so limited quirks maps to standards.
        bool ourQuirks = ours.Mode == QuirksMode.Quirks;
        bool theirQuirks = string.Equals(theirs.CompatMode, "BackCompat", StringComparison.Ordinal);
        if (ourQuirks != theirQuirks)
        {
            found.Add(new Mismatch("quirks", "#document", ours.Mode.ToString(), theirs.CompatMode, "#document", -1));
        }

        var stack = new Stack<(Node O, INode T, Step P)>();
        stack.Push((ours.Root, theirs, new Step(null, "#document")));
        while (stack.Count > 0 && found.Count < MaxMismatches)
        {
            (Node o, INode t, Step path) = stack.Pop();
            CompareNode(o, t, path, found, stack);
        }

        return found;
    }

    private static NodeType Expected(NodeKind k) => k switch
    {
        NodeKind.Document => NodeType.Document,
        NodeKind.DocumentFragment => NodeType.DocumentFragment,
        NodeKind.DocumentType => NodeType.DocumentType,
        NodeKind.Element => NodeType.Element,
        NodeKind.Text => NodeType.Text,
        NodeKind.Comment => NodeType.Comment,
        _ => NodeType.ProcessingInstruction,
    };

    private static string Desc(Node o) => o.Kind == NodeKind.Element ? o.LocalName : "#" + o.Kind;

    private static string Desc(INode t) => t is IElement e ? e.LocalName : "#" + t.NodeType;

    private static int HintOf(Node o)
    {
        for (Node n = o; !n.IsNull; n = n.Parent)
        {
            SourceRange r = n.Source.Range;
            if (r.IsPresent)
            {
                return r.Start;
            }
        }

        return -1;
    }

    private static void CompareNode(Node o, INode t, Step path, List<Mismatch> found, Stack<(Node, INode, Step)> stack)
    {
        string desc = Desc(o);
        string shape = path.Shape(3);
        if (Expected(o.Kind) != t.NodeType)
        {
            found.Add(new Mismatch("kind", path.ToString(), Desc(o), Desc(t), shape, HintOf(o)));
            return;
        }

        switch (o.Kind)
        {
            case NodeKind.Text or NodeKind.Comment or NodeKind.ProcessingInstruction:
                {
                    string ours = o.Data.ToString();
                    string theirs = t is ICharacterData cd ? cd.Data : t.TextContent ?? string.Empty;
                    if (!string.Equals(ours, theirs, StringComparison.Ordinal))
                    {
                        found.Add(new Mismatch(o.Kind == NodeKind.Comment ? "comment" : "text", path.ToString(), Clip(ours), Clip(theirs), shape, HintOf(o)));
                    }

                    return;
                }

            case NodeKind.DocumentType:
                {
                    var dt = (IDocumentType)t;
                    CompareValue("doctype.name", o.LocalName, dt.Name, path, found, o);
                    CompareValue("doctype.public", o.PublicId, dt.PublicIdentifier, path, found, o);
                    CompareValue("doctype.system", o.SystemId, dt.SystemIdentifier, path, found, o);
                    return;
                }

            case NodeKind.Element:
                {
                    var e = (IElement)t;
                    string ourNs = NamespaceUri(o.Namespace);
                    bool nameBad = false;
                    if (!string.Equals(ourNs, e.NamespaceUri ?? HtmlNs, StringComparison.Ordinal))
                    {
                        found.Add(new Mismatch("namespace", path.ToString(), desc + "@" + ourNs, Desc(t) + "@" + e.NamespaceUri, shape, HintOf(o)));
                        nameBad = true;
                    }

                    if (!string.Equals(o.LocalName, e.LocalName, StringComparison.Ordinal))
                    {
                        found.Add(new Mismatch("localName", path.ToString(), o.LocalName, e.LocalName, shape, HintOf(o)));
                        nameBad = true;
                    }

                    if (nameBad)
                    {
                        return;
                    }

                    CompareAttributes(o, e, path, found);
                    break;
                }
        }

        PushChildren(o, t, path, found, stack);
    }

    private static string Clip(string s) => s.Length <= 60 ? s : s[..60] + "...";

    private static void CompareValue(string field, string ours, string? theirs, Step path, List<Mismatch> found, Node o)
    {
        if (!string.Equals(ours, theirs ?? string.Empty, StringComparison.Ordinal))
        {
            found.Add(new Mismatch(field, path.ToString(), Clip(ours), Clip(theirs ?? string.Empty), path.Shape(3), HintOf(o)));
        }
    }

    /// <summary>
    /// Attributes are paired by namespace and local name, which is what DOM identifies an attribute by: the same pair can
    /// carry different prefixes, and two attributes may share a local name in different namespaces (<c>href</c> and
    /// <c>xlink:href</c> on SVG <c>use</c>). Qualified name, namespace and value are then compared on each pair.
    /// </summary>
    private static void CompareAttributes(Node o, IElement e, Step path, List<Mismatch> found)
    {
        string shape = path.Shape(3);
        var theirs = new Dictionary<string, (string Qualified, string Value)>(StringComparer.Ordinal);
        var theirOrder = new List<string>();
        foreach (IAttr a in e.Attributes)
        {
            string key = Key(string.IsNullOrEmpty(a.NamespaceUri) ? null : a.NamespaceUri, a.LocalName);
            string qualified = string.IsNullOrEmpty(a.Prefix) ? a.LocalName : a.Prefix + ":" + a.LocalName;
            if (theirs.TryAdd(key, (qualified, a.Value)))
            {
                theirOrder.Add(key);
            }
        }

        var ourOrder = new List<string>();
        var ourSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (Attr a in o.Attributes)
        {
            string name = a.QualifiedName;
            string key = Key(AttributeNamespaceUri(a.Namespace), a.LocalName);
            ourOrder.Add(key);
            ourSet.Add(key);
            if (!theirs.TryGetValue(key, out (string Qualified, string Value) tv))
            {
                found.Add(new Mismatch("attr.name", path.ToString(), name, "(absent)", shape, HintOf(o)));
                continue;
            }

            if (!string.Equals(name, tv.Qualified, StringComparison.Ordinal))
            {
                found.Add(new Mismatch("attr.prefix", path.ToString(), name, tv.Qualified, shape + "@" + a.LocalName, HintOf(o)));
            }

            string ov = a.Value.ToString();
            if (!string.Equals(ov, tv.Value, StringComparison.Ordinal))
            {
                found.Add(new Mismatch("attr.value", path.ToString() + "@" + name, Clip(ov), Clip(tv.Value), shape + "@" + name, HintOf(o)));
            }
        }

        foreach (string key in theirOrder)
        {
            if (!ourSet.Contains(key))
            {
                found.Add(new Mismatch("attr.name", path.ToString(), "(absent)", theirs[key].Qualified, shape, HintOf(o)));
            }
        }

        if (ourSet.Count == theirOrder.Count && ourSet.IsSupersetOf(theirOrder))
        {
            for (int i = 0; i < ourOrder.Count; i++)
            {
                if (!string.Equals(ourOrder[i], theirOrder[i], StringComparison.Ordinal))
                {
                    found.Add(new Mismatch("attr.order", path.ToString(), ourOrder[i], theirOrder[i], shape, HintOf(o)));
                    break;
                }
            }
        }
    }

    private static string Key(string? ns, string local) => (ns ?? string.Empty) + "|" + local;

    private static void PushChildren(Node o, INode t, Step path, List<Mismatch> found, Stack<(Node, INode, Step)> stack)
    {
        var ours = new List<Node>();
        foreach (Node c in o.ChildNodes)
        {
            ours.Add(c);
        }

        INodeList theirs = t.ChildNodes;
        int common = Math.Min(ours.Count, theirs.Length);
        if (ours.Count != theirs.Length)
        {
            found.Add(new Mismatch(
                "childCount",
                path.ToString(),
                ours.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                theirs.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                path.Shape(3),
                HintOf(o)));
        }

        // Template contents are compared like a child, after the real children so they pop first only if pushed last.
        if (o.Kind == NodeKind.Element && o.Namespace == ElementNamespace.Html && o.LocalName == "template")
        {
            Node content = o.TemplateContent;
            IDocumentFragment? theirContent = (t as IHtmlTemplateElement)?.Content;
            if (content.IsNull != (theirContent is null))
            {
                found.Add(new Mismatch("template", path.ToString(), content.IsNull ? "(none)" : "content", theirContent is null ? "(none)" : "content", path.Shape(3), HintOf(o)));
            }
            else if (!content.IsNull)
            {
                stack.Push((content, theirContent!, new Step(path, "#content")));
            }
        }

        for (int i = common - 1; i >= 0; i--)
        {
            Node c = ours[i];
            string seg = c.Kind == NodeKind.Element ? c.LocalName + "[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]" : "#" + c.Kind.ToString().ToLowerInvariant() + "[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
            stack.Push((c, theirs[i], new Step(path, seg)));
        }
    }
}
