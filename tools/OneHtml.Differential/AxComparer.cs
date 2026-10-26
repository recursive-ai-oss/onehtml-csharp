using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RecursiveAI.OneHtml.Differential;

/// <summary>One disagreement between Chrome and our tree.</summary>
/// <param name="Field">The compared field: <c>role</c>, <c>name</c>, <c>prop.checked</c>, <c>parent</c>, …</param>
/// <param name="Pattern">What kind of disagreement, with the roles involved; mismatches with the same field and pattern form a class.</param>
/// <param name="Chrome">Chrome's value (clipped).</param>
/// <param name="Ours">Our value (clipped).</param>
/// <param name="NodeKey">Identifies the node within the page: the OneHtml node index, or <c>c</c> and the Chrome node id for a Chrome-only node.</param>
/// <param name="Path">The node's DOM path, or Chrome's backend node id and role.</param>
internal sealed record AxMismatch(string Field, string Pattern, string Chrome, string Ours, string NodeKey, string Path)
{
    public string ClassKey => Field + " | " + Pattern;
}

/// <summary>The result of comparing one capture.</summary>
internal sealed class AxPageDiff
{
    public int ChromeNodes { get; set; }

    public int OursNodes { get; set; }

    public int Matched { get; set; }

    public int MatchedByAnonymousRule { get; set; }

    /// <summary>Per field: how many times it was compared on this page.</summary>
    public Dictionary<string, long> Compared { get; } = new(StringComparer.Ordinal);

    public List<AxMismatch> Mismatches { get; } = [];

    /// <summary>One line per Chrome node and per unpaired node of ours, when a dump was asked for.</summary>
    public List<string> Dump { get; } = [];
}

/// <summary>
/// Pairs the nodes of Chrome's tree with ours and compares them. The rules are in <c>docs/chrome-ax-differential.md</c>.
/// </summary>
internal static class AxComparer
{
    private const int Clip = 80;

    /// <summary>Pairs Chrome entries with ours: root with root; entries with the same OneHtml node; then anonymous text and presentational entries.</summary>
    /// <returns>For each Chrome entry the index of ours, or -1.</returns>
    public static (int[] ChromeToOurs, int[] OursToChrome, int Anonymous) Match(AxSide chrome, AxSide ours)
    {
        int[] c2o = new int[chrome.Entries.Count];
        int[] o2c = new int[ours.Entries.Count];
        Array.Fill(c2o, -1);
        Array.Fill(o2c, -1);
        var byDom = new Dictionary<int, int>();
        int oursRoot = -1;
        foreach (AxEntry o in ours.Entries)
        {
            if (o.IsRoot)
            {
                oursRoot = o.Index;
            }
            else if (o.Dom >= 0)
            {
                byDom[o.Dom] = o.Index;
            }
        }

        foreach (AxEntry c in chrome.Entries)
        {
            if (c.IsRoot && c.Parent < 0 && oursRoot >= 0 && o2c[oursRoot] < 0)
            {
                c2o[c.Index] = oursRoot;
                o2c[oursRoot] = c.Index;
            }
            else if (c.Dom >= 0 && byDom.TryGetValue(c.Dom, out int oi) && o2c[oi] < 0)
            {
                c2o[c.Index] = oi;
                o2c[oi] = c.Index;
            }
        }

        // Anonymous rule: a Chrome entry with no OneHtml node (generated text, anonymous presentational boxes) pairs with the first
        // unpaired entry of ours under the counterpart of its nearest paired ancestor that is of the same kind (text or element), has
        // the same role and the same name. Anything else stays unpaired and is reported as present on one side only.
        int anonymous = 0;
        foreach (AxEntry c in chrome.Entries)
        {
            if (c2o[c.Index] >= 0 || c.Dom >= 0)
            {
                continue;
            }

            int anc = c.Parent;
            while (anc >= 0 && c2o[anc] < 0)
            {
                anc = chrome.Entries[anc].Parent;
            }

            if (anc < 0)
            {
                continue;
            }

            foreach (int oi in AllChildren(ours, c2o[anc]))
            {
                AxEntry o = ours.Entries[oi];
                if (o2c[oi] < 0 && o.IsText == c.IsText && o.Role == c.Role && o.Ignored == c.Ignored && o.Name == c.Name)
                {
                    c2o[c.Index] = oi;
                    o2c[oi] = c.Index;
                    anonymous++;
                    break;
                }
            }
        }

        return (c2o, o2c, anonymous);
    }

    /// <summary>All descendants of an entry in document order that are reached through unpaired entries only are not considered: the direct children, plus those of unpaired children.</summary>
    private static IEnumerable<int> AllChildren(AxSide side, int parent)
    {
        foreach (int c in side.Entries[parent].Children)
        {
            yield return c;
        }
    }

    public static AxPageDiff Compare(AxSide chrome, AxSide ours, bool dump = false)
    {
        (int[] c2o, int[] o2c, int anonymous) = Match(chrome, ours);
        var diff = new AxPageDiff { ChromeNodes = chrome.Entries.Count, OursNodes = ours.Entries.Count, MatchedByAnonymousRule = anonymous };
        void Count(string field, long n = 1) => diff.Compared[field] = diff.Compared.GetValueOrDefault(field) + n;
        void Add(string field, string pattern, string cv, string ov, AxEntry key, AxEntry? oursEntry) =>
            diff.Mismatches.Add(new AxMismatch(field, pattern, Clipped(cv), Clipped(ov), oursEntry is not null ? oursEntry.Index.ToString(CultureInfo.InvariantCulture) : "c" + key.NodeId, oursEntry is not null && oursEntry.Path.Length > 0 ? oursEntry.Path : key.Path));

        // A node on one side only. An ignored node is not part of what assistive technology walks, so it is counted in a field of its
        // own (chromeOnlyIgnored, oursOnlyIgnored): the exposed tree is the same without it.
        foreach (AxEntry c in chrome.Entries)
        {
            string field = c.Ignored ? "chromeOnlyIgnored" : "chromeOnly";
            Count(field);
            if (c2o[c.Index] < 0)
            {
                Add(field, "role:" + c.Role + (c.Ignored ? " ignored[" + string.Join(',', c.IgnoredReasons) + "]" : string.Empty), c.Role, string.Empty, c, null);
            }
        }

        foreach (AxEntry o in ours.Entries)
        {
            string field = o.Ignored ? "oursOnlyIgnored" : "oursOnly";
            Count(field);
            if (o2c[o.Index] < 0)
            {
                Add(field, "role:" + o.Role + (o.Ignored ? " ignored[" + string.Join(',', o.IgnoredReasons) + "]" : string.Empty), string.Empty, o.Role, o, o);
            }
        }

        string RoleOf(AxSide side, int i) => i < 0 ? "(none)" : side.Entries[i].Role;
        int[] cExposedParent = ExposedParents(chrome, c2o);
        int[] oExposedParent = ExposedParents(ours, o2c);

        foreach (AxEntry c in chrome.Entries)
        {
            int oi = c2o[c.Index];
            if (oi < 0)
            {
                continue;
            }

            AxEntry o = ours.Entries[oi];
            diff.Matched++;

            Count("ignored");
            if (c.Ignored != o.Ignored)
            {
                Add("ignored", c.Ignored ? "chrome ignored[" + string.Join(',', c.IgnoredReasons) + "] ours " + o.Role : "chrome " + c.Role + " ours ignored[" + string.Join(',', o.IgnoredReasons) + "]", c.Ignored.ToString(), o.Ignored.ToString(), c, o);
            }

            Count("hidden");
            string ch = string.Join(',', c.IgnoredReasons.Where(AxNormalizer.IsHiddenReason));
            string oh = string.Join(',', o.IgnoredReasons.Where(AxNormalizer.IsHiddenReason));
            if (ch != oh)
            {
                Add("hidden", "chrome[" + ch + "] ours[" + oh + "]", ch, oh, c, o);
            }

            if (c.Ignored && o.Ignored)
            {
                Count("ignoredReasons");
                string cr = string.Join(',', c.IgnoredReasons.Where(r => !AxNormalizer.IsHiddenReason(r)));
                string or = string.Join(',', o.IgnoredReasons.Where(r => !AxNormalizer.IsHiddenReason(r)));
                if (cr != or)
                {
                    Add("ignoredReasons", "chrome[" + cr + "] ours[" + or + "]", cr, or, c, o);
                }
            }

            CompareStructure(c, o, c2o, o2c, chrome, ours, cExposedParent, oExposedParent, Count, Add, RoleOf);

            if (c.Ignored || o.Ignored)
            {
                continue;
            }

            Count("role");
            if (c.Role != o.Role)
            {
                Add("role", c.Role + " -> " + o.Role, c.Role, o.Role, c, o);
            }

            Count("name");
            if (c.Name != o.Name)
            {
                Add("name", "role:" + c.Role + " " + TextKind(c.Name, o.Name), c.Name, o.Name, c, o);
            }
            else if (c.Name.Length > 0)
            {
                Count("nameSource");
                if (c.NameSource != o.NameSource)
                {
                    Add("nameSource", "role:" + c.Role + " " + c.NameSource + " -> " + o.NameSource, c.NameSource, o.NameSource, c, o);
                }
            }

            Count("description");
            if (c.Description != o.Description)
            {
                Add("description", "role:" + c.Role + " " + TextKind(c.Description, o.Description), c.Description, o.Description, c, o);
            }

            if (c.Value is not null || o.Value is not null)
            {
                Count("value");
                if (!string.Equals(c.Value, o.Value, StringComparison.Ordinal))
                {
                    Add("value", "role:" + c.Role + " " + TextKind(c.Value ?? string.Empty, o.Value ?? string.Empty), c.Value ?? "(absent)", o.Value ?? "(absent)", c, o);
                }
            }

            foreach (string p in AxNormalizer.ComparedProps)
            {
                if ((p == "focused" || p == "url") && c.IsRoot)
                {
                    continue;
                }

                bool hc = c.Props.TryGetValue(p, out string? cv);
                bool ho = o.Props.TryGetValue(p, out string? ov);
                if (!hc && !ho)
                {
                    continue;
                }

                Count("prop." + p);
                if (cv != ov)
                {
                    Add("prop." + p, "role:" + c.Role + " chrome=" + Short(cv) + " ours=" + Short(ov), cv ?? "(absent)", ov ?? "(absent)", c, o);
                }
            }

            foreach (string p in AxNormalizer.RelationProps)
            {
                bool hc = c.Relations.TryGetValue(p, out string? cv);
                bool ho = o.Relations.TryGetValue(p, out string? ov);
                if (!hc && !ho)
                {
                    continue;
                }

                Count("rel." + p);
                if (cv != ov)
                {
                    Add("rel." + p, "role:" + c.Role + (cv is null ? " chrome absent" : ov is null ? " ours absent" : " targets differ"), cv ?? "(absent)", ov ?? "(absent)", c, o);
                }
            }
        }

        if (dump)
        {
            foreach (AxEntry c in chrome.Entries)
            {
                int oi = c2o[c.Index];
                string where = string.Empty;
                if (oi >= 0)
                {
                    where = "  @ " + ours.Entries[oi].Path + ours.Entries[oi].Extra;
                }
                else
                {
                    where = c.Extra;
                    // For a Chrome node with no counterpart: where it sits, as the path of its nearest paired ancestor.
                    int guard = 0;
                    for (int a = c.Parent; a >= 0 && guard++ < 1024; a = chrome.Entries[a].Parent)
                    {
                        if (c2o[a] >= 0)
                        {
                            where = c.Extra + "  under " + Describe("O", ours.Entries[c2o[a]]) + " @ " + ours.Entries[c2o[a]].Path + ours.Entries[c2o[a]].Extra;
                            break;
                        }
                    }
                }

                diff.Dump.Add(Describe("C", c) + "  <=>  " + (oi < 0 ? "(none)" : Describe("O", ours.Entries[oi])) + where);
            }

            foreach (AxEntry o in ours.Entries.Where(e => o2c[e.Index] < 0))
            {
                string under = string.Empty;
                int guard2 = 0;
                for (int a = o.Parent; a >= 0 && guard2++ < 1024; a = ours.Entries[a].Parent)
                {
                    if (ours.Entries[a].Dom >= 0)
                    {
                        under = "  under " + ours.Entries[a].Path;
                        break;
                    }
                }

                diff.Dump.Add("(none)  <=>  " + Describe("O", o) + "  @ " + o.Path + o.Extra + under);
            }
        }

        return diff;
    }

    private static string Describe(string side, AxEntry e) =>
        side + ":" + e.Role + (e.Ignored ? " IGN[" + string.Join(',', e.IgnoredReasons) + "]" : string.Empty) + (e.Name.Length > 0 ? " \"" + Clipped(e.Name) + "\"" : string.Empty) + (e.Description.Length > 0 ? " desc=\"" + Clipped(e.Description) + "\"" : string.Empty) + (e.Value is { Length: > 0 } ? " value=\"" + Clipped(e.Value) + "\"" : string.Empty)
        + (e.Props.Count > 0 ? " {" + string.Join(' ', e.Props.Select(p => p.Key + "=" + p.Value)) + "}" : string.Empty);

    private delegate void AddFn(string field, string pattern, string cv, string ov, AxEntry key, AxEntry? oursEntry);

    private static void CompareStructure(AxEntry c, AxEntry o, int[] c2o, int[] o2c, AxSide chrome, AxSide ours, int[] cExp, int[] oExp, Action<string, long> count, AddFn add, Func<AxSide, int, string> roleOf)
    {
        // The parent is the nearest ancestor that has a counterpart on the other side: a node present on one side only (a hidden node that
        // one tree lists and the other omits) is reported once as chromeOnly or oursOnly and does not also change every child's parent.
        count("parent", 1);
        int cpa = PairedAncestor(chrome, c.Parent, c2o);
        int cp = cpa < 0 ? -1 : c2o[cpa];
        int op = PairedAncestor(ours, o.Parent, o2c);
        if (cp != op)
        {
            add("parent", "chrome-parent:" + roleOf(chrome, c.Parent) + " ours-parent:" + roleOf(ours, o.Parent), roleOf(chrome, c.Parent), roleOf(ours, o.Parent), c, o);
        }

        count("exposedParent", 1);
        int cep = cExp[c.Index];
        int mapped = cep < 0 ? -1 : c2o[cep];
        if (mapped != oExp[o.Index])
        {
            add("exposedParent", "chrome-parent:" + roleOf(chrome, cep) + " ours-parent:" + roleOf(ours, oExp[o.Index]), roleOf(chrome, cep), roleOf(ours, oExp[o.Index]), c, o);
        }

        if (!c.Ignored && !o.Ignored || c.IsRoot)
        {
            // Order of the exposed children that both sides have, so a missing child is reported once, as present on one side only.
            List<int> cKids = ExposedChildren(chrome, c.Index).Select(i => c2o[i]).Where(i => i >= 0).ToList();
            List<int> oKids = ExposedChildren(ours, o.Index).Where(i => o2c[i] >= 0).ToList();
            var common = new HashSet<int>(cKids);
            common.IntersectWith(oKids);
            List<int> a = cKids.Where(common.Contains).ToList();
            List<int> b = oKids.Where(common.Contains).ToList();
            if (common.Count > 1)
            {
                count("childOrder", 1);
                if (!a.SequenceEqual(b))
                {
                    add("childOrder", "role:" + c.Role, string.Join(',', a), string.Join(',', b), c, o);
                }
            }
        }
    }

    /// <summary>The nearest ancestor from <paramref name="start"/> upwards (itself included) that has a counterpart, as an index on its own side, or -1.</summary>
    private static int PairedAncestor(AxSide side, int start, int[] toOther)
    {
        int p = start;
        int guard = 0;
        while (p >= 0 && toOther[p] < 0 && guard++ < side.Entries.Count)
        {
            p = side.Entries[p].Parent;
        }

        return p;
    }

    /// <summary>For each entry the index of its nearest ancestor that is not ignored and has a counterpart on the other side, or -1.</summary>
    private static int[] ExposedParents(AxSide side, int[] toOther)
    {
        int[] r = new int[side.Entries.Count];
        for (int i = 0; i < r.Length; i++)
        {
            int p = side.Entries[i].Parent;
            int guard = 0;
            while (p >= 0 && (side.Entries[p].Ignored || toOther[p] < 0) && guard++ < r.Length)
            {
                p = side.Entries[p].Parent;
            }

            r[i] = p;
        }

        return r;
    }

    /// <summary>Unignored descendants reached through ignored nodes only, in order.</summary>
    private static List<int> ExposedChildren(AxSide side, int parent)
    {
        var result = new List<int>();
        var stack = new Stack<int>();
        for (int k = side.Entries[parent].Children.Count - 1; k >= 0; k--)
        {
            stack.Push(side.Entries[parent].Children[k]);
        }

        int guard = 0;
        while (stack.Count > 0 && guard++ <= side.Entries.Count)
        {
            int i = stack.Pop();
            if (!side.Entries[i].Ignored)
            {
                result.Add(i);
                continue;
            }

            List<int> kids = side.Entries[i].Children;
            for (int k = kids.Count - 1; k >= 0; k--)
            {
                stack.Push(kids[k]);
            }
        }

        return result;
    }

    /// <summary>Names the shape of a text disagreement.</summary>
    public static string TextKind(string chrome, string ours)
    {
        if (chrome.Length == 0)
        {
            return "chrome-empty";
        }

        if (ours.Length == 0)
        {
            return "ours-empty";
        }

        if (chrome.StartsWith(ours, StringComparison.Ordinal))
        {
            return "ours-prefix";
        }

        if (ours.StartsWith(chrome, StringComparison.Ordinal))
        {
            return "chrome-prefix";
        }

        return string.Equals(chrome, ours, StringComparison.OrdinalIgnoreCase) ? "case-only" : "differs";
    }

    private static string Short(string? v) => v is null ? "(absent)" : v.Length > 24 ? v[..24] + "…" : v;

    private static string Clipped(string v) => v.Length > Clip ? v[..Clip] + "…" : v;
}
