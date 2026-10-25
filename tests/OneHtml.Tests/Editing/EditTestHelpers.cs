using Xunit;
using System.Collections.Generic;
using System.Text;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Tests.Editing;

internal static class EditTestHelpers
{
    public static Document Parse(string html) => Document.Parse(html);

    public static Document ParseBytes(byte[] bytes, string? contentType = null) =>
        Document.Parse(bytes, new ParseOptions { ContentType = contentType });

    /// <summary>The nth element (document order) with this local name.</summary>
    public static Node Find(Document d, string name, int nth = 0)
    {
        var stack = new Stack<Node>();
        stack.Push(d.Root);
        int seen = 0;
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            if (n.IsElement && n.LocalName == name && seen++ == nth)
            {
                return n;
            }

            var kids = new List<Node>();
            for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                kids.Add(c);
            }

            if (n.IsElement && !n.TemplateContent.IsNull)
            {
                kids.Add(n.TemplateContent);
            }

            for (int i = kids.Count - 1; i >= 0; i--)
            {
                stack.Push(kids[i]);
            }
        }

        throw new KeyNotFoundException(name + "#" + nth);
    }

    public static Node FindText(Document d, string contains)
    {
        var stack = new Stack<Node>();
        stack.Push(d.Root);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            if (n.Kind == NodeKind.Text && n.Data.ToString().Contains(contains, System.StringComparison.Ordinal))
            {
                return n;
            }

            var kids = new List<Node>();
            for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                kids.Add(c);
            }

            for (int i = kids.Count - 1; i >= 0; i--)
            {
                stack.Push(kids[i]);
            }
        }

        throw new KeyNotFoundException(contains);
    }

    public static string Text(EditResult r)
    {
        Assert.True(r.Succeeded, r.Failure);
        return r.Document!.Source.Text;
    }

    public static string Apply(string html, System.Func<Document, Edit[]> edits)
    {
        Document d = Parse(html);
        EditResult r = d.Apply(edits(d));
        return Text(r);
    }

    public static string Utf8(byte[] b) => Encoding.UTF8.GetString(b);
}
