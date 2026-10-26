using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RecursiveAI.OneHtml.Accessibility.Tests;

/// <summary>Layout facts a test attaches to a fake snapshot node.</summary>
internal sealed class FakeLayout
{
    public double X, Y, W = 10, H = 10;
    public int Paint = 1;
    public bool Stacking;
    public string? Text;
    public List<(int Start, int Length, double X, double Y, double W, double H)> Boxes = [];
    public string?[] Styles = [];
}

/// <summary>A node of a fake snapshot: the shape of a DOMSnapshot node table row, as a tree so tests can edit it.</summary>
internal sealed class FakeNode
{
    public int Type;
    public string Name = "";
    public string? Value;
    public List<(string Name, string Value)> Attrs = [];
    public List<FakeNode> Kids = [];
    public string? Pseudo;
    public string? ShadowRoot;
    public int? ContentDocument;
    public FakeLayout? Layout;
    public string? InputValue;
    public bool InputChecked;
    public long Backend;
}

/// <summary>
/// Builds <c>DOMSnapshot.captureSnapshot</c>-shaped JSON from a OneHtml document, for tests that need cases a given capture does not
/// have. The shape follows the DevTools protocol reference; real Chrome captures are tested separately.
/// </summary>
internal static class FakeSnapshot
{
    public static readonly string[] StyleNames = ["display", "color", "visibility"];

    public static FakeNode FromDocument(Document doc, Func<Node, FakeLayout?>? layout = null)
    {
        long backend = 100;
        var root = new FakeNode { Type = 9, Name = "#document", Backend = backend++ };
        Add(doc.Root, root);
        return root;

        // Tests are small, so this walks recursively.
        void Add(Node parentSrc, FakeNode parent)
        {
            for (Node src = parentSrc.FirstChild; !src.IsNull; src = src.NextSibling)
            {
                var n = new FakeNode { Backend = backend++ };
                switch (src.Kind)
                {
                    case NodeKind.DocumentType:
                        n.Type = 10;
                        n.Name = src.LocalName;
                        break;
                    case NodeKind.Element:
                        n.Type = 1;
                        n.Name = src.Namespace == ElementNamespace.Html ? src.LocalName.ToUpperInvariant() : src.LocalName;
                        foreach (Attr a in src.Attributes)
                        {
                            n.Attrs.Add((a.QualifiedName, a.Value.ToString()));
                        }

                        break;
                    case NodeKind.Text:
                        n.Type = 3;
                        n.Name = "#text";
                        n.Value = src.Data.ToString();
                        break;
                    case NodeKind.Comment:
                        n.Type = 8;
                        n.Name = "#comment";
                        n.Value = src.Data.ToString();
                        break;
                    default:
                        continue;
                }

                n.Layout = layout?.Invoke(src);
                parent.Kids.Add(n);
                if (src.Kind == NodeKind.Element)
                {
                    Node tc = src.TemplateContent;
                    if (!tc.IsNull)
                    {
                        var frag = new FakeNode { Type = 11, Name = "#document-fragment", Backend = backend++ };
                        n.Kids.Add(frag);
                        Add(tc, frag);
                    }

                    Add(src, n);
                }
            }
        }
    }

    public static FakeNode Find(FakeNode root, string name, int skip = 0)
    {
        var st = new Stack<FakeNode>();
        st.Push(root);
        while (st.Count > 0)
        {
            FakeNode n = st.Pop();
            if (string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase) && skip-- == 0)
            {
                return n;
            }

            for (int i = n.Kids.Count - 1; i >= 0; i--)
            {
                st.Push(n.Kids[i]);
            }
        }

        throw new InvalidOperationException("no node " + name);
    }

    public static byte[] ToJson(params FakeNode[] documents) => ToJson(withFormat: false, documents);

    public static byte[] ToJson(bool withFormat, params FakeNode[] documents)
    {
        var strings = new List<string>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        int S(string? s)
        {
            if (s is null)
            {
                return -1;
            }

            if (!index.TryGetValue(s, out int i))
            {
                i = strings.Count;
                strings.Add(s);
                index[s] = i;
            }

            return i;
        }

        // The string table is complete only after the documents are written, so write them once to fill it, then again for real.
        byte[] Write(bool real)
        {
            var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                if (withFormat)
                {
                    w.WriteString("schema", "onehtml-capture/1/snapshot");
                    w.WriteStartObject("format");
                    w.WriteString("domain", "DOMSnapshot");
                    w.WriteString("method", "captureSnapshot");
                    w.WriteString("protocolVersion", "1.3");
                    w.WriteString("product", "HeadlessChrome/0.0.0.0");
                    w.WriteStartObject("parameters");
                    w.WriteStartArray("computedStyles");
                    foreach (string n in StyleNames)
                    {
                        w.WriteStringValue(n);
                    }

                    w.WriteEndArray();
                    w.WriteEndObject();
                    w.WriteEndObject();
                    w.WriteStartObject("snapshot");
                }

                w.WriteStartArray("documents");
                foreach (FakeNode docRoot in documents)
                {
                    WriteDocument(w, docRoot, S);
                }

                w.WriteEndArray();
                w.WriteStartArray("strings");
                if (real)
                {
                    foreach (string s in strings)
                    {
                        w.WriteStringValue(s);
                    }
                }

                w.WriteEndArray();
                if (withFormat)
                {
                    w.WriteEndObject();
                }

                w.WriteEndObject();
            }

            return ms.ToArray();
        }

        Write(false);
        return Write(true);
    }

    private static void WriteDocument(Utf8JsonWriter w, FakeNode root, Func<string?, int> s)
    {
        var flat = new List<(FakeNode Node, int Parent)>();
        var st = new Stack<(FakeNode Node, int Parent)>();
        st.Push((root, -1));
        while (st.Count > 0)
        {
            (FakeNode n, int parent) = st.Pop();
            int me = flat.Count;
            flat.Add((n, parent));
            for (int i = n.Kids.Count - 1; i >= 0; i--)
            {
                st.Push((n.Kids[i], me));
            }

        }

        w.WriteStartObject();
        w.WriteNumber("documentURL", s("http://test.invalid/"));
        w.WriteNumber("title", s("t"));
        w.WriteNumber("baseURL", s("http://test.invalid/"));
        w.WriteNumber("frameId", s("F" + root.Backend));
        w.WriteNumber("contentWidth", 800);
        w.WriteNumber("contentHeight", 600);
        w.WriteNumber("scrollOffsetX", 0);
        w.WriteNumber("scrollOffsetY", 0);

        w.WriteStartObject("nodes");
        WriteInts(w, "parentIndex", flat, e => e.Parent);
        WriteInts(w, "nodeType", flat, e => e.Node.Type);
        WriteInts(w, "nodeName", flat, e => s(e.Node.Name));
        WriteInts(w, "nodeValue", flat, e => s(e.Node.Value));
        w.WriteStartArray("backendNodeId");
        foreach (var e in flat)
        {
            w.WriteNumberValue(e.Node.Backend);
        }

        w.WriteEndArray();
        w.WriteStartArray("attributes");
        foreach (var e in flat)
        {
            w.WriteStartArray();
            foreach ((string name, string value) in e.Node.Attrs)
            {
                w.WriteNumberValue(s(name));
                w.WriteNumberValue(s(value));
            }

            w.WriteEndArray();
        }

        w.WriteEndArray();
        WriteRareStrings(w, "pseudoType", flat, e => e.Node.Pseudo, s);
        WriteRareStrings(w, "shadowRootType", flat, e => e.Node.ShadowRoot, s);
        WriteRareStrings(w, "inputValue", flat, e => e.Node.InputValue, s);
        w.WriteStartObject("contentDocumentIndex");
        WriteIndexAndValues(w, flat, e => e.Node.ContentDocument);
        w.WriteEndObject();
        w.WriteStartObject("inputChecked");
        w.WriteStartArray("index");
        for (int i = 0; i < flat.Count; i++)
        {
            if (flat[i].Node.InputChecked)
            {
                w.WriteNumberValue(i);
            }
        }

        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteEndObject();

        var withLayout = new List<int>();
        for (int i = 0; i < flat.Count; i++)
        {
            if (flat[i].Node.Layout is not null)
            {
                withLayout.Add(i);
            }
        }

        w.WriteStartObject("layout");
        w.WriteStartArray("nodeIndex");
        foreach (int i in withLayout)
        {
            w.WriteNumberValue(i);
        }

        w.WriteEndArray();
        w.WriteStartArray("bounds");
        foreach (int i in withLayout)
        {
            FakeLayout l = flat[i].Node.Layout!;
            WriteRect(w, l.X, l.Y, l.W, l.H);
        }

        w.WriteEndArray();
        foreach (string rect in new[] { "offsetRects", "clientRects", "scrollRects" })
        {
            w.WriteStartArray(rect);
            foreach (int i in withLayout)
            {
                if (flat[i].Node.Type != 1)
                {
                    w.WriteStartArray();
                    w.WriteEndArray();
                }
                else
                {
                    FakeLayout l = flat[i].Node.Layout!;
                    WriteRect(w, l.X, l.Y, l.W, l.H);
                }
            }

            w.WriteEndArray();
        }

        w.WriteStartArray("styles");
        foreach (int i in withLayout)
        {
            w.WriteStartArray();
            string?[] st2 = flat[i].Node.Layout!.Styles;
            for (int k = 0; k < StyleNames.Length; k++)
            {
                w.WriteNumberValue(s(k < st2.Length ? st2[k] : null));
            }

            w.WriteEndArray();
        }

        w.WriteEndArray();
        w.WriteStartArray("text");
        foreach (int i in withLayout)
        {
            w.WriteNumberValue(s(flat[i].Node.Layout!.Text));
        }

        w.WriteEndArray();
        w.WriteStartArray("paintOrders");
        foreach (int i in withLayout)
        {
            w.WriteNumberValue(flat[i].Node.Layout!.Paint);
        }

        w.WriteEndArray();
        w.WriteStartArray("blendedBackgroundColors");
        foreach (int i in withLayout)
        {
            w.WriteNumberValue(s("rgb(255, 255, 255)"));
        }

        w.WriteEndArray();
        w.WriteStartArray("textColorOpacities");
        foreach (int i in withLayout)
        {
            w.WriteNumberValue(1);
        }

        w.WriteEndArray();
        w.WriteStartObject("stackingContexts");
        w.WriteStartArray("index");
        for (int li = 0; li < withLayout.Count; li++)
        {
            if (flat[withLayout[li]].Node.Layout!.Stacking)
            {
                w.WriteNumberValue(li);
            }
        }

        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteEndObject();

        w.WriteStartObject("textBoxes");
        var tbs = new List<(int Li, (int Start, int Length, double X, double Y, double W, double H) B)>();
        for (int li = 0; li < withLayout.Count; li++)
        {
            foreach (var b in flat[withLayout[li]].Node.Layout!.Boxes)
            {
                tbs.Add((li, b));
            }
        }

        w.WriteStartArray("layoutIndex");
        foreach (var t in tbs)
        {
            w.WriteNumberValue(t.Li);
        }

        w.WriteEndArray();
        w.WriteStartArray("bounds");
        foreach (var t in tbs)
        {
            WriteRect(w, t.B.X, t.B.Y, t.B.W, t.B.H);
        }

        w.WriteEndArray();
        w.WriteStartArray("start");
        foreach (var t in tbs)
        {
            w.WriteNumberValue(t.B.Start);
        }

        w.WriteEndArray();
        w.WriteStartArray("length");
        foreach (var t in tbs)
        {
            w.WriteNumberValue(t.B.Length);
        }

        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteEndObject();
    }

    private static void WriteRect(Utf8JsonWriter w, double x, double y, double wd, double h)
    {
        w.WriteStartArray();
        w.WriteNumberValue(x);
        w.WriteNumberValue(y);
        w.WriteNumberValue(wd);
        w.WriteNumberValue(h);
        w.WriteEndArray();
    }

    private static void WriteInts(Utf8JsonWriter w, string name, List<(FakeNode Node, int Parent)> flat, Func<(FakeNode Node, int Parent), int> f)
    {
        w.WriteStartArray(name);
        foreach (var e in flat)
        {
            w.WriteNumberValue(f(e));
        }

        w.WriteEndArray();
    }

    private static void WriteIndexAndValues(Utf8JsonWriter w, List<(FakeNode Node, int Parent)> flat, Func<(FakeNode Node, int Parent), int?> f)
    {
        w.WriteStartArray("index");
        for (int i = 0; i < flat.Count; i++)
        {
            if (f(flat[i]) is not null)
            {
                w.WriteNumberValue(i);
            }
        }

        w.WriteEndArray();
        w.WriteStartArray("value");
        foreach (var e in flat)
        {
            if (f(e) is { } v)
            {
                w.WriteNumberValue(v);
            }
        }

        w.WriteEndArray();
    }

    private static void WriteRareStrings(Utf8JsonWriter w, string name, List<(FakeNode Node, int Parent)> flat, Func<(FakeNode Node, int Parent), string?> f, Func<string?, int> s)
    {
        w.WriteStartObject(name);
        WriteIndexAndValues(w, flat, e => f(e) is { } v ? s(v) : null);
        w.WriteEndObject();
    }

    public static Document Parse(string html) => Document.Parse(html);

    public static string Utf8(byte[] b) => Encoding.UTF8.GetString(b);
}
