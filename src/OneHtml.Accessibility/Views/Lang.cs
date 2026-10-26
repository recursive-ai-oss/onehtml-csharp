using RecursiveAI.OneHtml.Accessibility.Tree;

namespace RecursiveAI.OneHtml.Accessibility.Views;

/// <summary>The language in effect at a node.</summary>
internal static class Lang
{
    /// <summary>
    /// The value of the nearest <c>lang</c> attribute on the node or an ancestor element (HTML section 3.2.6.2), trimmed;
    /// <see langword="null"/> when there is none, or when the nearest is empty, which declares the language unknown.
    /// </summary>
    public static string? Of(Node dom)
    {
        Node cur = dom.Kind == NodeKind.Element ? dom : dom.Parent;
        while (!cur.IsNull && cur.IsElement)
        {
            if (cur.GetAttribute("lang") is { } l)
            {
                string t = TreeText.Trim(l);
                return t.Length == 0 ? null : t;
            }

            cur = cur.Parent;
        }

        return null;
    }
}
