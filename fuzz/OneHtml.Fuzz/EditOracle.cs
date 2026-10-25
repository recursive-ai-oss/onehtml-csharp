using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Fuzz;

/// <summary>
/// A second, deliberately plain implementation of the edit operations on a simple mutable tree, used to say what
/// the editor's output should parse to. It shares nothing with the editor except the fragment parser.
/// </summary>
internal static class Oracle
{
    [ThreadStatic]
    private static bool s_scripting;

    private sealed class SNode
    {
        public NodeKind Kind;
        public ElementNamespace Ns;
        public string Name = string.Empty;
        public List<(string Name, string Value)> Attrs = [];
        public string Text = string.Empty;
        public List<SNode> Kids = [];
        public SNode? Parent;
        public Node Source;
        public bool Dead;
        public string PublicId = string.Empty;
        public string SystemId = string.Empty;
    }

    /// <summary>Returns true when the output equals the reference; false with a difference, or false with null when the reference could not follow the edits.</summary>
    public static bool Check(Document before, Document after, List<Edit> edits, bool scripting, out string? difference)
    {
        difference = null;
        s_scripting = scripting;
        var map = new Dictionary<int, SNode>();
        SNode root = From(before.Root, map);
        foreach (Edit e in edits)
        {
            if (!map.TryGetValue(e.Target.Index, out SNode? target) || target.Dead || !Apply(e, target))
            {
                return false;
            }
        }

        // Style edits are checked by their own tests; here the style attribute's text is masked on both sides.
        bool mask = edits.Exists(e => e.Operation is EditOperation.SetStyle or EditOperation.RemoveStyle);
        string want = Dump(root, mask);
        string got = Dump(From(after.Root, null), mask);
        if (want == got)
        {
            return true;
        }

        string[] a = want.Split('\n');
        string[] b = got.Split('\n');
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i])
        {
            i++;
        }

        difference = "line " + i.ToString(CultureInfo.InvariantCulture) + ": reference '" + (i < a.Length ? a[i] : "<end>") + "' output '" + (i < b.Length ? b[i] : "<end>") + "'";
        return false;
    }

    // ---- conversion ----

    private static SNode From(Node root, Dictionary<int, SNode>? map)
    {
        var top = Make(root);
        var stack = new Stack<(Node, SNode)>();
        stack.Push((root, top));
        map?.Add(root.Index, top);
        while (stack.Count > 0)
        {
            (Node n, SNode s) = stack.Pop();
            Node holder = n.IsElement && !n.TemplateContent.IsNull ? n.TemplateContent : n;
            for (Node c = holder.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                SNode k = Make(c);
                k.Parent = s;
                s.Kids.Add(k);
                map?.Add(c.Index, k);
                stack.Push((c, k));
            }
        }

        return top;
    }

    private static SNode Make(Node n)
    {
        var s = new SNode { Kind = n.Kind, Ns = n.Namespace, Source = n };
        switch (n.Kind)
        {
            case NodeKind.Element:
                s.Name = n.LocalName;
                foreach (Attr a in n.Attributes)
                {
                    s.Attrs.Add((a.QualifiedName, a.Value.ToString()));
                }

                break;
            case NodeKind.Text or NodeKind.Comment:
                s.Text = n.Data.ToString();
                break;
            case NodeKind.DocumentType:
                s.Name = n.LocalName;
                s.PublicId = n.PublicId;
                s.SystemId = n.SystemId;
                break;
            default:
                break;
        }

        return s;
    }

    private static List<SNode> FromFragment(Document frag)
    {
        var map = new Dictionary<int, SNode>();
        SNode root = From(frag.Root, map);
        foreach (SNode k in root.Kids)
        {
            k.Parent = null;
        }

        return root.Kids;
    }

    // ---- the operations ----

    private static bool Apply(Edit e, SNode t)
    {
        switch (e.Operation)
        {
            case EditOperation.SetAttribute:
                {
                    // Foreign elements repair attribute names; the reference does not model that.
                    if (t.Kind != NodeKind.Element || t.Ns != ElementNamespace.Html)
                    {
                        return false;
                    }

                    string name = Lower(e.Name!);
                    int at = t.Attrs.FindIndex(a => a.Name == name);
                    if (at >= 0)
                    {
                        t.Attrs[at] = (name, e.Value!);
                    }
                    else
                    {
                        t.Attrs.Add((name, e.Value!));
                    }

                    return true;
                }

            case EditOperation.RemoveAttribute:
                if (t.Kind != NodeKind.Element || t.Ns != ElementNamespace.Html)
                {
                    return false;
                }

                t.Attrs.RemoveAll(a => a.Name == Lower(e.Name!));
                return true;
            case EditOperation.SetStyle:
                if (t.Kind != NodeKind.Element)
                {
                    return false;
                }

                if (!t.Attrs.Exists(a => a.Name == "style"))
                {
                    t.Attrs.Add(("style", string.Empty));
                }

                return true;
            case EditOperation.RemoveStyle:
                return t.Kind == NodeKind.Element;
            case EditOperation.ReplaceText:
                return ReplaceText(t, e.Value!);
            case EditOperation.InsertBefore or EditOperation.InsertAfter or EditOperation.Replace:
                {
                    SNode? p = t.Parent;
                    if (p is null)
                    {
                        return false;
                    }

                    List<SNode>? nodes = Parse(p, e.Value!);
                    if (nodes is null)
                    {
                        return false;
                    }

                    int i = p.Kids.IndexOf(t);
                    if (e.Operation == EditOperation.InsertAfter)
                    {
                        i++;
                    }

                    Link(p, i, nodes);
                    if (e.Operation == EditOperation.Replace)
                    {
                        Remove(t);
                    }

                    return true;
                }

            case EditOperation.Prepend or EditOperation.Append:
                {
                    if (t.Kind != NodeKind.Element || IsVoid(t))
                    {
                        return false;
                    }

                    List<SNode>? nodes = Parse(t, e.Value!);
                    if (nodes is null)
                    {
                        return false;
                    }

                    Link(t, e.Operation == EditOperation.Prepend ? 0 : t.Kids.Count, nodes);
                    return true;
                }

            case EditOperation.Remove:
                if (t.Parent is null)
                {
                    return false;
                }

                Remove(t);
                return true;
            case EditOperation.Unwrap:
                {
                    SNode? p = t.Parent;
                    if (p is null || p.Kind == NodeKind.Document || t.Kind != NodeKind.Element || (t.Ns == ElementNamespace.Html && t.Name == "template"))
                    {
                        return false;
                    }

                    int i = p.Kids.IndexOf(t);
                    List<SNode> kids = t.Kids;
                    t.Kids = [];
                    p.Kids.RemoveAt(i);
                    t.Parent = null;
                    t.Dead = true;
                    Link(p, i, kids);
                    return true;
                }

            case EditOperation.Wrap:
                {
                    SNode? p = t.Parent;
                    if (p is null || p.Kind == NodeKind.Document)
                    {
                        return false;
                    }

                    int i = p.Kids.IndexOf(t);
                    if (i + e.Count > p.Kids.Count)
                    {
                        return false;
                    }

                    List<SNode>? roots = Parse(p, e.Value!);
                    if (roots is null)
                    {
                        return false;
                    }

                    SNode? wrapper = null;
                    foreach (SNode r in roots)
                    {
                        if (r.Kind == NodeKind.Element && wrapper is null)
                        {
                            wrapper = r;
                        }
                        else if (!(r.Kind == NodeKind.Text && IsWhitespace(r.Text)))
                        {
                            return false;
                        }
                    }

                    if (wrapper is null || IsVoid(wrapper))
                    {
                        return false;
                    }

                    List<SNode> run = p.Kids.GetRange(i, e.Count);
                    p.Kids.RemoveRange(i, e.Count);
                    foreach (SNode r in run)
                    {
                        r.Parent = wrapper;
                        wrapper.Kids.Add(r);
                    }

                    wrapper.Parent = p;
                    p.Kids.Insert(i, wrapper);
                    return true;
                }

            default:
                return false;
        }
    }

    private static bool ReplaceText(SNode t, string text)
    {
        if (t.Kind == NodeKind.Text)
        {
            if (t.Parent is null)
            {
                return false;
            }

            if (text.Length == 0)
            {
                Remove(t);
            }
            else
            {
                t.Text = text;
            }

            return true;
        }

        if (t.Kind != NodeKind.Element)
        {
            return false;
        }

        int first = t.Kids.FindIndex(k => k.Kind == NodeKind.Text);
        if (first < 0)
        {
            if (text.Length > 0)
            {
                Link(t, 0, [new SNode { Kind = NodeKind.Text, Text = text }]);
            }

            return true;
        }

        for (int i = t.Kids.Count - 1; i > first; i--)
        {
            if (t.Kids[i].Kind == NodeKind.Text)
            {
                Remove(t.Kids[i]);
            }
        }

        if (text.Length == 0)
        {
            Remove(t.Kids[first]);
        }
        else
        {
            t.Kids[first].Text = text;
        }

        return true;
    }

    private static List<SNode>? Parse(SNode parent, string html)
    {
        if (parent.Kind == NodeKind.Document)
        {
            Document f0 = Document.ParseFragment(html, new FragmentContext(ElementNamespace.Html, "body"), new ParseOptions { ScriptingEnabled = s_scripting });
            List<SNode> nodes0 = FromFragment(f0);
            foreach (SNode n in nodes0)
            {
                if (n.Kind != NodeKind.Comment)
                {
                    return null;
                }
            }

            return nodes0;
        }

        FragmentContext ctx;
        if (!parent.Source.IsNull && parent.Kind == NodeKind.Element)
        {
            ctx = FragmentContext.FromElement(parent.Source);
        }
        else
        {
            bool inForm = false;
            for (SNode? p = parent; p is not null; p = p.Parent)
            {
                if (p.Kind == NodeKind.Element && p.Ns == ElementNamespace.Html && p.Name == "form")
                {
                    inForm = true;
                }
            }

            ctx = new FragmentContext(parent.Ns, parent.Name, inForm);
        }

        return FromFragment(Document.ParseFragment(html, ctx, new ParseOptions { ScriptingEnabled = s_scripting }));
    }

    private static void Link(SNode parent, int index, List<SNode> nodes)
    {
        foreach (SNode n in nodes)
        {
            n.Parent = parent;
        }

        parent.Kids.InsertRange(index, nodes);
    }

    private static void Remove(SNode n)
    {
        n.Parent?.Kids.Remove(n);
        n.Parent = null;
        var stack = new Stack<SNode>();
        stack.Push(n);
        while (stack.Count > 0)
        {
            SNode x = stack.Pop();
            x.Dead = true;
            foreach (SNode k in x.Kids)
            {
                stack.Push(k);
            }
        }
    }

    private static bool IsVoid(SNode e) =>
        e.Ns == ElementNamespace.Html && e.Name is "area" or "base" or "basefont" or "bgsound" or "br" or "col" or "embed" or "frame" or "hr" or "img" or "input" or "keygen" or "link" or "meta" or "param" or "source" or "track" or "wbr";

    private static bool IsWhitespace(string s)
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

    private static string Lower(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            sb.Append(c is >= 'A' and <= 'Z' ? (char)(c + 0x20) : c);
        }

        return sb.ToString();
    }

    // ---- comparison form ----

    private static string Dump(SNode root, bool maskStyle)
    {
        var sb = new StringBuilder();
        var stack = new Stack<(SNode, int)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            (SNode n, int depth) = stack.Pop();
            sb.Append(' ', depth);
            int lineStart = sb.Length;
            switch (n.Kind)
            {
                case NodeKind.Element:
                    sb.Append('<').Append((int)n.Ns).Append(n.Name);
                    foreach ((string an, string av) in n.Attrs)
                    {
                        sb.Append(' ').Append(an).Append("=\"").Append(maskStyle && an == "style" ? "~" : av).Append('"');
                    }

                    sb.Append('>');
                    break;
                case NodeKind.Text:
                    sb.Append('"').Append(n.Text).Append('"');
                    break;
                case NodeKind.Comment:
                    sb.Append("<!--").Append(n.Text).Append("-->");
                    break;
                case NodeKind.DocumentType:
                    sb.Append("<!doctype ").Append(n.Name).Append(' ').Append(n.PublicId).Append(' ').Append(n.SystemId).Append('>');
                    break;
                default:
                    sb.Append(n.Kind);
                    break;
            }

            sb.Replace('\n', '\u2424', lineStart, sb.Length - lineStart).Append('\n');
            if (n.Kind == NodeKind.Element && n.Ns == ElementNamespace.Html && n.Name == "selectedcontent")
            {
                continue;
            }

            // The parser merges adjacent text; the editor may add whitespace-only text to keep indentation.
            var kids = new List<SNode>();
            var run = new StringBuilder();
            foreach (SNode k in n.Kids)
            {
                if (k.Kind == NodeKind.Text)
                {
                    run.Append(k.Text);
                    continue;
                }

                Flush(kids, run);
                kids.Add(k);
            }

            Flush(kids, run);
            for (int i = kids.Count - 1; i >= 0; i--)
            {
                stack.Push((kids[i], depth + 1));
            }
        }

        return sb.ToString();
    }

    private static void Flush(List<SNode> kids, StringBuilder run)
    {
        if (run.Length > 0)
        {
            string s = run.ToString();
            run.Clear();
            if (!IsWhitespace(s))
            {
                kids.Add(new SNode { Kind = NodeKind.Text, Text = s });
            }
        }
    }
}
