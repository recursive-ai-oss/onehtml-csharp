using System.Collections.Generic;
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

    /// <summary>
    /// Answers <see cref="Of"/> for many nodes of one document. Each element's answer is kept, so a page of nested elements
    /// costs one step per element instead of a walk up to the root for each.
    /// </summary>
    public sealed class Cache
    {
        private readonly Dictionary<int, string?> _byElement = [];
        private readonly List<int> _path = [];

        /// <summary>The same value as <see cref="Lang.Of"/>.</summary>
        public string? Of(Node dom)
        {
            Node cur = dom.Kind == NodeKind.Element ? dom : dom.Parent;
            _path.Clear();
            string? result = null;
            while (!cur.IsNull && cur.IsElement)
            {
                if (_byElement.TryGetValue(cur.Index, out string? known))
                {
                    result = known;
                    break;
                }

                _path.Add(cur.Index);
                if (cur.GetAttribute("lang") is { } l)
                {
                    string t = TreeText.Trim(l);
                    result = t.Length == 0 ? null : t;
                    break;
                }

                cur = cur.Parent;
            }

            foreach (int i in _path)
            {
                _byElement[i] = result;
            }

            return result;
        }
    }
}
