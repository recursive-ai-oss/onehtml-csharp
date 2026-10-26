using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Accessibility.Aria;
using RecursiveAI.OneHtml.Accessibility.Validation.Catalogue;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

/// <summary>
/// Base of the page-structure rules (landmarks, headings, lists, ids, focus, obsolete elements, meta elements): the
/// helpers they share. Descriptors come from the catalogue as for the ARIA rules.
/// </summary>
internal abstract class StructureRule : AriaRule
{
    private static readonly HashSet<string> s_landmarks = new(StringComparer.Ordinal)
    {
        "banner", "complementary", "contentinfo", "form", "main", "navigation", "region", "search",
    };

    protected StructureRule(string id, string description, string help, string? fix, string? noFixReason, bool axe = true, IReadOnlyList<string>? actIds = null, Impact? impact = null, RuleNeeds needs = RuleNeeds.Static)
        : base(id, description, help, fix, noFixReason, axe, actIds, impact, needs: needs)
    {
    }

    protected static bool IsHtml(Node e, string localName) => e.IsElement && e.Namespace == ElementNamespace.Html && string.Equals(e.LocalName, localName, StringComparison.Ordinal);

    /// <summary>True when the element is laid out and not <c>visibility</c>-hidden: the "visible" of the rules that talk about hidden content.</summary>
    protected static bool IsRendered(CheckContext c, Node e)
    {
        HiddenInfo h = c.Tree.GetHidden(e);
        return !h.IsNotRendered && (h.Reasons & HiddenReasons.VisibilityHidden) == 0;
    }

    /// <summary>
    /// True when the node is a landmark. WAI-ARIA 1.2 lists the landmark roles; HTML-AAM and WAI-ARIA 1.2 (5.3.10, 5.3.12)
    /// make a <c>form</c> or <c>region</c> a landmark only when it has an accessible name, which is what Chrome exposes too.
    /// </summary>
    protected static bool IsLandmark(AxNode n)
    {
        if (n.Kind != AxNodeKind.Element || n.AriaRole is not { } r || !s_landmarks.Contains(r.Name))
        {
            return false;
        }

        return r.Name is not ("form" or "region") || !IsBlank(n.Name.Value);
    }

    protected static bool IsRole(AxNode n, string role) => n.Kind == AxNodeKind.Element && n.AriaRole is { } r && r.Name == role;

    /// <summary>Exposed landmark nodes of the tree, in tree order.</summary>
    protected static List<AxNode> Landmarks(CheckContext c)
    {
        var list = new List<AxNode>();
        foreach (AxNode n in c.Tree.Nodes)
        {
            if (IsLandmark(n) && c.IsExposed(n.DomNode))
            {
                list.Add(n);
            }
        }

        return list;
    }

    /// <summary>The element the page-level rules report on: the <c>html</c> element.</summary>
    protected static Node PageNode(CheckContext c) => c.Document.DocumentElement;

    /// <summary>Exposed headings in tree order with their levels (WAI-ARIA 1.2 heading: <c>aria-level</c> defaults to 2).</summary>
    protected static List<(AxNode Node, int Level)> Headings(CheckContext c)
    {
        var list = new List<(AxNode, int)>();
        foreach (AxNode n in c.Tree.Nodes)
        {
            if (IsRole(n, "heading") && c.IsExposed(n.DomNode))
            {
                list.Add((n, n.States.Level ?? 2));
            }
        }

        return list;
    }

    /// <summary>ASCII-only lower-casing, normalized whitespace: the comparison key for names.</summary>
    protected static string Normalize(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        bool space = false;
        foreach (char ch in s)
        {
            if (ch is ' ' or '\t' or '\n' or '\f' or '\r')
            {
                space = sb.Length > 0;
                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            sb.Append(ch is >= 'A' and <= 'Z' ? (char)(ch + 32) : ch);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The skip links of the page: <c>a</c> elements whose href is a fragment naming an element of the document
    /// (HTML 7.4.6.3 "find a potential indicated element"; the fragment is tried as written and percent-decoded).
    /// </summary>
    protected static Node SkipTarget(CheckContext c, Node a)
    {
        if (!IsHtml(a, "a") || a.GetAttribute("href") is not { Length: > 1 } href || href[0] != '#')
        {
            return default;
        }

        string frag = href[1..];
        Node t = c.Document.GetElementById(frag);
        if (t.IsNull && frag.Contains('%', StringComparison.Ordinal))
        {
            t = c.Document.GetElementById(Uri.UnescapeDataString(frag));
        }

        return t;
    }

    /// <summary>The roles of ARIA's widget class, including those that inherit from it (WAI-ARIA 1.2 5.3.2).</summary>
    protected static bool IsWidgetRole(AriaRole role)
    {
        // Superclasses lists the direct parents; follow them with a work list (the taxonomy is small and acyclic).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var work = new Stack<AriaRole>();
        work.Push(role);
        while (work.Count > 0)
        {
            AriaRole r = work.Pop();
            if (r.Name == "widget")
            {
                return true;
            }

            foreach (string s in r.Superclasses)
            {
                if (seen.Add(s) && AriaTaxonomy.FindRole(s) is { } parent)
                {
                    work.Push(parent);
                }
            }
        }

        return false;
    }
}
