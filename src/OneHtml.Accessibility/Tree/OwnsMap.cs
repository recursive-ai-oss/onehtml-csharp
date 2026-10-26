using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Accessibility.Tree;

/// <summary>
/// <c>aria-owns</c> re-parenting (WAI-ARIA 1.2 section 6.6.6 and Core-AAM section 5.2): the first owner in document order
/// wins, an element never owns itself or one of its own ancestors in the tree as it stands, and an owned element leaves its
/// document parent. References to elements that are not in the tree are ignored.
/// </summary>
internal sealed class OwnsMap
{
    private readonly int[] _ownerOf;
    private readonly Dictionary<int, List<int>> _owned = new();

    private OwnsMap(int count)
    {
        _ownerOf = new int[count];
        Array.Fill(_ownerOf, -1);
    }

    public List<AxDiagnostic> Diagnostics { get; } = [];

    /// <summary>The element that adopted <paramref name="index"/>, or -1.</summary>
    public int OwnerOf(int index) => _ownerOf[index];

    /// <summary>The elements adopted by <paramref name="owner"/> in attribute order, or null.</summary>
    public List<int>? OwnedBy(int owner) => _owned.TryGetValue(owner, out List<int>? l) ? l : null;

    public static OwnsMap Compute(Document doc, HiddenMap hidden, AccessibilityOptions options, System.Threading.CancellationToken ct)
    {
        var map = new OwnsMap(doc.NodeCount);

        // The tree's parent of each node as it stands while owners are processed.
        int[]? parentEff = null;
        long budget = options.MaxOwnsWork;
        bool limitReported = false;
        int visited = 0;

        foreach (Node e in doc.Root.DescendantElements)
        {
            if ((++visited & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            string? value = e.GetAttribute("aria-owns");
            // WAI-ARIA 1.2 section 6.6.6: aria-owns on an element excluded from the tree is not resolved.
            if (value is null || value.Length == 0 || hidden.NotRendered(e.Index) || (hidden.Reasons(e.Index) & (HiddenReasons.AriaHidden | HiddenReasons.Inert)) != 0)
            {
                continue;
            }

            int pos = 0;
            int count = 0;
            List<string>? seen = null;
            while (NextToken(value, ref pos, out string token) && count++ < options.MaxIdReferences)
            {
                if (seen is not null && seen.Contains(token))
                {
                    continue;
                }

                (seen ??= []).Add(token);
                Node target = doc.GetElementById(token);
                if (target.IsNull)
                {
                    continue; // reported as a broken reference with the other relations
                }

                string? why = null;
                if (hidden.NotRendered(target.Index) || (hidden.Own(target.Index) & HiddenReasons.VisibilityHidden) != 0)
                {
                    why = "the element is hidden from all users";
                }
                else if (target.Index == e.Index)
                {
                    why = "an element cannot own itself";
                }
                else if (target == doc.DocumentElement)
                {
                    why = "the root element cannot be owned";
                }
                else if (map._ownerOf[target.Index] >= 0)
                {
                    why = "an earlier element already owns it";
                }
                else
                {
                    parentEff ??= BuildParents(doc);
                    // A cycle: the target is an ancestor of the owner in the tree as it stands.
                    int a = e.Index;
                    while (a > 0)
                    {
                        if (a == target.Index)
                        {
                            why = "it is an ancestor of the owner";
                            break;
                        }

                        a = parentEff[a];
                        if (--budget < 0)
                        {
                            why = "the work limit was reached";
                            if (!limitReported)
                            {
                                limitReported = true;
                                map.Diagnostics.Add(new AxDiagnostic(AxDiagnosticKind.LimitExceeded, e, "aria-owns", null, "The aria-owns work limit was reached; further aria-owns references are ignored."));
                            }

                            break;
                        }
                    }
                }

                if (why is not null)
                {
                    map.Diagnostics.Add(new AxDiagnostic(AxDiagnosticKind.AriaOwnsIgnored, e, "aria-owns", token, "aria-owns reference to \"" + token + "\" ignored: " + why + "."));
                    continue;
                }

                map._ownerOf[target.Index] = e.Index;
                parentEff![target.Index] = e.Index;
                if (!map._owned.TryGetValue(e.Index, out List<int>? list))
                {
                    map._owned[e.Index] = list = [];
                }

                list.Add(target.Index);
            }
        }

        return map;
    }

    private static int[] BuildParents(Document doc)
    {
        var p = new int[doc.NodeCount];
        for (int i = 0; i < p.Length; i++)
        {
            Node par = doc.GetNode(i).Parent;
            p[i] = par.IsNull ? -1 : par.Index;
        }

        return p;
    }

    internal static bool NextToken(string s, ref int pos, out string token)
    {
        while (pos < s.Length && IsWs(s[pos]))
        {
            pos++;
        }

        int start = pos;
        while (pos < s.Length && !IsWs(s[pos]))
        {
            pos++;
        }

        token = s.Substring(start, pos - start);
        return pos > start;
    }

    internal static bool IsWs(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';
}
