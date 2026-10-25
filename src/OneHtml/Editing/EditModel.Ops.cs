using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Dom;

namespace RecursiveAI.OneHtml.Editing;

internal sealed partial class EditModel
{
    /// <summary>
    /// Applies one edit to the working tree. Returns null when it applied, or the reason it does not fit.
    /// </summary>
    /// <exception cref="HtmlLimitExceededException">The edit would grow the tree past a configured limit.</exception>
    public string? Apply(Edit edit, int index)
    {
        CurrentEdit = index;
        _touched = [];
        TouchedBy[index] = _touched;
        int t = edit.Target.Index;
        if (IsGone(t))
        {
            return "the target was removed by an earlier edit";
        }

        if ((BA.Flags[t] & F.Synthesized) != 0)
        {
            return "the node is a copy the parser made (of the selected option's content); edit the option instead";
        }

        if (edit.Operation is EditOperation.Prepend or EditOperation.Append or EditOperation.ReplaceText && IsElement(t) && A.Ns[t] == (byte)ElementNamespace.Html && Names.NameOf(A.Name[t]) == "selectedcontent")
        {
            return "the content of selectedcontent is a copy of the selected option's content; edit the option instead";
        }

        switch (edit.Operation)
        {
            case EditOperation.SetAttribute:
                return IsElement(t) ? SetAttribute(t, edit.Name!, edit.Value!) : "the target is not an element";
            case EditOperation.RemoveAttribute:
                return IsElement(t) ? RemoveAttribute(t, edit.Name!) : "the target is not an element";
            case EditOperation.SetStyle:
                return IsElement(t) ? SetStyle(t, edit.Name!, edit.Value!, edit.Important) : "the target is not an element";
            case EditOperation.RemoveStyle:
                return IsElement(t) ? RemoveStyle(t, edit.Name!) : "the target is not an element";
            case EditOperation.ReplaceText:
                return ReplaceText(t, edit.Value!);
            case EditOperation.InsertBefore:
            case EditOperation.InsertAfter:
            case EditOperation.Replace:
                return InsertAround(edit.Operation, t, edit.Value!);
            case EditOperation.Prepend:
            case EditOperation.Append:
                return InsertInside(edit.Operation == EditOperation.Prepend, t, edit.Value!);
            case EditOperation.Wrap:
                return Wrap(t, edit.Value!, edit.Count);
            case EditOperation.Unwrap:
                return Unwrap(t);
            case EditOperation.Remove:
                {
                    int p = A.Parent[t];
                    if (p < 0)
                    {
                        return "the target has no parent";
                    }

                    Detach(t);
                    return null;
                }

            default:
                return "unknown operation";
        }
    }

    // ---- attributes ----

    private static string? ValidateText(string s, string what)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\0')
            {
                return what + " contains U+0000, which HTML does not keep";
            }

            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                return what + " contains an unpaired surrogate";
            }
        }

        return null;
    }

    private static string? ValidateAttributeName(string name)
    {
        if (name.Length == 0)
        {
            return "the attribute name is empty";
        }

        foreach (char c in name)
        {
            if (c is ' ' or '\t' or '\n' or '\f' or '\r' or '/' or '>' or '=' or '"' or '\'' or '<' or '\0' || c > 0x7E || c < 0x20)
            {
                return "the attribute name has a character that cannot be written in a tag: " + name;
            }
        }

        return null;
    }

    private static string LowerAscii(string s)
    {
        int i = 0;
        while (i < s.Length && s[i] is not (>= 'A' and <= 'Z'))
        {
            i++;
        }

        if (i == s.Length)
        {
            return s;
        }

        return string.Create(s.Length, s, static (dest, src) =>
        {
            for (int k = 0; k < src.Length; k++)
            {
                char c = src[k];
                dest[k] = c is >= 'A' and <= 'Z' ? (char)(c + 0x20) : c;
            }
        });
    }

    private string QualifiedName(in ModelAttr m)
    {
        string local = Names.NameOf(m.Name);
        return m.Namespace switch
        {
            AttributeNamespace.XLink => "xlink:" + local,
            AttributeNamespace.Xml => "xml:" + local,
            AttributeNamespace.Xmlns => local == "xmlns" ? "xmlns" : "xmlns:" + local,
            _ => local,
        };
    }

    private int FindAttribute(int e, List<ModelAttr> list, string name)
    {
        // DOM "get an attribute by name": HTML elements compare the lowercased name, others compare exactly.
        string wanted = A.Ns[e] == (byte)ElementNamespace.Html ? LowerAscii(name) : name;
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(QualifiedName(list[i]), wanted, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private string? SetAttribute(int e, string name, string value)
    {
        string? bad = ValidateAttributeName(name) ?? ValidateText(value, "the attribute value");
        if (bad is not null)
        {
            return bad;
        }

        SetAttributeValue(e, name, value);
        return null;
    }

    private void SetAttributeValue(int e, string name, string value)
    {
        List<ModelAttr> list = AttrsOf(e);
        int at = FindAttribute(e, list, name);
        if (at >= 0)
        {
            if (list[at].Value == value)
            {
                return;
            }

            ModelAttr m = list[at];
            m.Value = value;
            list[at] = m;
        }
        else
        {
            // HTML 13.2.5.33 lowercases attribute names; foreign content then repairs the case of SVG names and
            // splits the prefixed names (13.2.6.5), so a new attribute is named the way the parser would name it.
            string lower = LowerAscii(name);
            int atom = Names.Intern(lower);
            var ns = AttributeNamespace.None;
            byte elementNs = A.Ns[e];
            if (elementNs != (byte)ElementNamespace.Html)
            {
                (atom, ns) = AdjustForeign(atom, elementNs == (byte)ElementNamespace.MathMl);
            }

            list.Add(new ModelAttr { Name = atom, Namespace = ns, Value = value, Orig = -1 });
        }

        AttrsChanged(e);
        Touch(RegionKind.Attrs, e);
    }

    private static (int Atom, AttributeNamespace Ns) AdjustForeign(int atom, bool mathMl)
    {
        if (mathMl)
        {
            if (atom == N.Definitionurl)
            {
                atom = N.CcDefinitionURL;
            }
        }
        else
        {
            atom = ElementInfo.FixSvgAttribute(atom);
        }

        switch (atom)
        {
            case N.XlinkActuate: return (N.Actuate, AttributeNamespace.XLink);
            case N.XlinkArcrole: return (N.Arcrole, AttributeNamespace.XLink);
            case N.XlinkHref: return (N.Href, AttributeNamespace.XLink);
            case N.XlinkRole: return (N.Role, AttributeNamespace.XLink);
            case N.XlinkShow: return (N.Show, AttributeNamespace.XLink);
            case N.XlinkTitle: return (N.Title, AttributeNamespace.XLink);
            case N.XlinkType: return (N.Type, AttributeNamespace.XLink);
            case N.XmlLang: return (N.Lang, AttributeNamespace.Xml);
            case N.XmlSpace: return (N.Space, AttributeNamespace.Xml);
            case N.Xmlns: return (atom, AttributeNamespace.Xmlns);
            case N.XmlnsXlink: return (N.Xlink, AttributeNamespace.Xmlns);
            default: return (atom, AttributeNamespace.None);
        }
    }

    private string? RemoveAttribute(int e, string name)
    {
        List<ModelAttr> list = AttrsOf(e);
        int at = FindAttribute(e, list, name);
        if (at < 0)
        {
            return null;
        }

        list.RemoveAt(at);
        AttrsChanged(e);
        Touch(RegionKind.Attrs, e);
        return null;
    }

    private string? SetStyle(int e, string property, string value, bool important)
    {
        string? bad = ValidateText(value, "the value");
        if (bad is not null)
        {
            return bad;
        }

        List<ModelAttr> list = AttrsOf(e);
        int at = FindAttribute(e, list, "style");
        string current = at >= 0 ? list[at].Value : string.Empty;
        string? result = StyleText.Set(current, property, value, important, out string? error);
        if (result is null)
        {
            return error;
        }

        if (at >= 0)
        {
            StyleEdited.Add(e);
        }

        SetAttributeValue(e, "style", result);
        return null;
    }

    private string? RemoveStyle(int e, string property)
    {
        List<ModelAttr> list = AttrsOf(e);
        int at = FindAttribute(e, list, "style");
        if (at < 0)
        {
            return null;
        }

        string? result = StyleText.Remove(list[at].Value, property, out string? error);
        if (result is null)
        {
            return error;
        }

        StyleEdited.Add(e);
        SetAttributeValue(e, "style", result);
        return null;
    }

    // ---- text ----

    private string? ReplaceText(int t, string text)
    {
        string? bad = ValidateText(text, "the text");
        if (bad is not null)
        {
            return bad;
        }

        if (IsText(t))
        {
            if (A.Parent[t] < 0)
            {
                return "the text node is not in the tree";
            }

            if (text.Length == 0)
            {
                Detach(t);
                return null;
            }

            if (DataOf(t).SequenceEqual(text))
            {
                return null;
            }

            SetData(t, text);
            TouchText(t);
            return null;
        }

        if (!IsElement(t))
        {
            return "the target is neither a text node nor an element";
        }

        int holder = Holder(t);
        var texts = new List<int>();
        for (int c = A.First[holder]; c >= 0; c = A.Next[c])
        {
            if (IsText(c))
            {
                texts.Add(c);
            }
        }

        if (texts.Count == 0)
        {
            if (text.Length > 0)
            {
                int n = NewText(text);
                A.Insert(holder, A.First[holder], n);
                TouchChildren(holder);
            }

            return null;
        }

        if (texts.Count == 1 && DataOf(texts[0]).SequenceEqual(text))
        {
            return null;
        }

        if (text.Length == 0)
        {
            foreach (int c in texts)
            {
                Detach(c);
            }

            return null;
        }

        SetData(texts[0], text);
        TouchText(texts[0]);
        for (int i = 1; i < texts.Count; i++)
        {
            Detach(texts[i]);
        }

        return null;
    }

    // ---- structure ----

    /// <summary>Removes a node (with its subtree) from the tree and records where its bytes were.</summary>
    private void Detach(int node)
    {
        int parent = A.Parent[node];
        if (node < BaseCount)
        {
            TouchChildren(BA.Parent[node] >= 0 ? BA.Parent[node] : parent);
        }

        TouchChildren(parent);
        Kill(node);
    }

    private string? ParseInto(int parent, string html, out Document? frag)
    {
        frag = null;
        string? bad = ValidateText(html, "the markup");
        if (bad is not null)
        {
            return bad;
        }

        FragmentContext? ctx = ContextFor(parent, out string? error);
        if (ctx is null)
        {
            return error;
        }

        frag = Document.ParseFragment(html, ctx, new ParseOptions { ScriptingEnabled = Base.ScriptingEnabled, Limits = Limits });
        return null;
    }

    private FragmentContext? ContextFor(int parent, out string? error)
    {
        error = null;
        int el = parent;
        switch ((NodeKind)A.Kind[parent])
        {
            case NodeKind.Document:
                return new FragmentContext(ElementNamespace.Html, "body", false, false, Base.Mode);
            case NodeKind.DocumentFragment:
                el = ContentOwner(parent);
                if (el < 0)
                {
                    error = "the fragment is not a template's content";
                    return null;
                }

                break;
            case NodeKind.Element:
                break;
            default:
                error = "the parent cannot hold children";
                return null;
        }

        bool inForm = false;
        for (int n = el; n >= 0; n = ParentOrOwner(n))
        {
            if (A.Kind[n] == (byte)NodeKind.Element && A.Ns[n] == (byte)ElementNamespace.Html && A.Name[n] == N.Form)
            {
                inForm = true;
                break;
            }
        }

        var ns = (ElementNamespace)A.Ns[el];
        bool ip = ns == ElementNamespace.MathMl && (A.Flags[el] & F.IntegrationPoint) != 0;
        return new FragmentContext(ns, Names.NameOf(A.Name[el]), inForm, ip, Base.Mode);
    }

    private string? CheckRoots(int parent, List<int> roots)
    {
        if (A.Kind[parent] == (byte)NodeKind.Document)
        {
            foreach (int r in roots)
            {
                if (A.Kind[r] != (byte)NodeKind.Comment)
                {
                    return "only comments can be inserted directly under the document";
                }
            }
        }

        return null;
    }

    private string? InsertAround(EditOperation op, int t, string html)
    {
        int parent = A.Parent[t];
        if (parent < 0)
        {
            return "the target has no parent";
        }

        string? error = ParseInto(parent, html, out Document? frag);
        if (error is not null)
        {
            return error;
        }

        List<int> roots = Import(frag!);
        error = CheckRoots(parent, roots);
        if (error is not null)
        {
            return error;
        }

        if (roots.Count == 0 && op != EditOperation.Replace)
        {
            return null;
        }

        switch (op)
        {
            case EditOperation.InsertBefore:
                InsertRun(parent, t, t, false, roots);
                break;
            case EditOperation.InsertAfter:
                InsertRun(parent, A.Next[t], t, true, roots);
                break;
            default:
                foreach (int r in roots)
                {
                    A.Insert(parent, t, r);
                }

                TouchChildren(parent);
                Detach(t);
                break;
        }

        return null;
    }

    private string? InsertInside(bool prepend, int t, string html)
    {
        if (!IsElement(t))
        {
            return "the target is not an element";
        }

        if (IsVoid(t))
        {
            return "a void element cannot have children";
        }

        int holder = Holder(t);
        string? error = ParseInto(holder, html, out Document? frag);
        if (error is not null)
        {
            return error;
        }

        List<int> roots = Import(frag!);
        if (roots.Count == 0)
        {
            return null;
        }

        if (prepend)
        {
            int x = A.First[holder];
            while (x >= 0 && IsText(x) && IsWhitespace(DataOf(x)))
            {
                x = A.Next[x];
            }

            InsertRun(holder, A.First[holder], x, false, roots);
        }
        else
        {
            int x = A.Last[holder];
            while (x >= 0 && IsText(x) && IsWhitespace(DataOf(x)))
            {
                x = A.Prev[x];
            }

            InsertRun(holder, -1, x, true, roots);
        }

        return null;
    }

    /// <summary>
    /// Links <paramref name="roots"/> under <paramref name="parent"/> before <paramref name="plainBefore"/>. When the
    /// neighbouring element sits on its own indented line, a whitespace node with the same newline and indentation is
    /// put between the new nodes and their neighbour, so the markup keeps its layout. The whitespace is part of the
    /// tree like any text, which is how the comparison with the re-parsed output stays exact.
    /// </summary>
    private void InsertRun(int parent, int plainBefore, int lineAnchor, bool after, List<int> roots)
    {
        string? indent = null;
        if (lineAnchor >= 0 && A.Kind[lineAnchor] is (byte)NodeKind.Element or (byte)NodeKind.Comment && AllElementsOrComments(roots))
        {
            indent = IndentBefore(lineAnchor);
        }

        if (indent is null)
        {
            foreach (int r in roots)
            {
                A.Insert(parent, plainBefore, r);
            }
        }
        else if (after)
        {
            int before = A.Next[lineAnchor];
            foreach (int r in roots)
            {
                A.Insert(parent, before, NewText("\n" + indent));
                A.Insert(parent, before, r);
            }
        }
        else
        {
            foreach (int r in roots)
            {
                A.Insert(parent, lineAnchor, r);
                A.Insert(parent, lineAnchor, NewText("\n" + indent));
            }
        }

        TouchChildren(parent);
    }

    private bool AllElementsOrComments(List<int> roots)
    {
        foreach (int r in roots)
        {
            if (A.Kind[r] is not ((byte)NodeKind.Element or (byte)NodeKind.Comment))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The indentation of the line a node starts, when it is preceded by whitespace holding a newline.</summary>
    private string? IndentBefore(int node)
    {
        int w = A.Prev[node];
        if (w < 0 || !IsText(w))
        {
            return null;
        }

        ReadOnlySpan<char> s = DataOf(w);
        int nl = s.LastIndexOf('\n');
        if (nl < 0 || !IsWhitespace(s))
        {
            return null;
        }

        ReadOnlySpan<char> indent = s[(nl + 1)..];
        foreach (char c in indent)
        {
            if (c is not (' ' or '\t'))
            {
                return null;
            }
        }

        return indent.ToString();
    }

    private string? Wrap(int t, string html, int count)
    {
        int parent = A.Parent[t];
        if (parent < 0)
        {
            return "the target has no parent";
        }

        if (A.Kind[parent] == (byte)NodeKind.Document)
        {
            return "nodes directly under the document cannot be wrapped";
        }

        var run = new List<int>(count);
        int n = t;
        for (int i = 0; i < count; i++)
        {
            if (n < 0)
            {
                return "fewer siblings than the wrap count follow the target";
            }

            run.Add(n);
            n = A.Next[n];
        }

        string? error = ParseInto(parent, html, out Document? frag);
        if (error is not null)
        {
            return error;
        }

        List<int> roots = Import(frag!);
        int wrapper = -1;
        foreach (int r in roots)
        {
            if (A.Kind[r] == (byte)NodeKind.Element && wrapper < 0)
            {
                wrapper = r;
            }
            else if (!(A.Kind[r] == (byte)NodeKind.Text && IsWhitespace(DataOf(r))))
            {
                return "the wrapper markup must be exactly one element";
            }
        }

        if (wrapper < 0)
        {
            return "the wrapper markup must be exactly one element";
        }

        if (IsVoid(wrapper))
        {
            return "a void element cannot wrap anything";
        }

        int into = Holder(wrapper);
        A.Insert(parent, t, wrapper);
        foreach (int r in run)
        {
            A.Detach(r);
            A.Insert(into, -1, r);
            if (r < BaseCount)
            {
                Wrapped.Add(r);
                Moved.Add(r);
            }
        }

        TouchChildren(parent);
        foreach (int r in run)
        {
            if (r < BaseCount)
            {
                TouchChildren(BA.Parent[r]);
            }
        }

        return null;
    }

    private string? Unwrap(int t)
    {
        int parent = A.Parent[t];
        if (parent < 0)
        {
            return "the target has no parent";
        }

        if (A.Kind[parent] == (byte)NodeKind.Document)
        {
            return "children cannot be moved up to the document";
        }

        if (A.Ns[t] == (byte)ElementNamespace.Html && A.Name[t] == N.Template)
        {
            return "a template's content cannot be unwrapped";
        }

        TouchChildren(parent);
        if (t < BaseCount)
        {
            TouchChildren(BA.Parent[t] >= 0 ? BA.Parent[t] : parent);
        }

        while (A.First[t] >= 0)
        {
            int c = A.First[t];
            A.Detach(c);
            A.Insert(parent, t, c);
            if (c < BaseCount)
            {
                Moved.Add(c);
            }
        }

        A.Detach(t);
        A.Flags[t] |= Unwrapped;
        return null;
    }
}
