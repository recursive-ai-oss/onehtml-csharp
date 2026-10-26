using System;
using System.Collections.Generic;
using System.Globalization;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Accessibility;

/// <summary>What kind of property of a tree node changed.</summary>
public enum AxChangeKind : byte
{
    /// <summary>The computed role.</summary>
    Role,

    /// <summary>The accessible name.</summary>
    Name,

    /// <summary>The accessible description.</summary>
    Description,

    /// <summary>A state or property of <see cref="AxStates"/>; <see cref="AxPropertyChange.Property"/> names it.</summary>
    State,

    /// <summary>The hidden status (<see cref="HiddenInfo.Reasons"/>).</summary>
    Hidden,

    /// <summary>Whether the node is ignored (not exposed to assistive technology).</summary>
    Ignored,

    /// <summary>Whether the node can take focus, or is reached by sequential navigation.</summary>
    Focus,

    /// <summary>A relation (<c>owns</c>, <c>controls</c>, <c>labelledby</c>, …): the IDs of the elements it names, space separated, with each unresolved ID after them and marked with a leading <c>?</c>.</summary>
    Relation,
}

/// <summary>One property of a node that differs between two trees. A missing value is <see langword="null"/>.</summary>
/// <param name="Kind">The kind of property.</param>
/// <param name="Property">Its name: <c>role</c>, <c>name</c>, <c>description</c>, <c>hidden</c>, <c>ignored</c>, <c>focusable</c>, <c>tabbable</c>, or the state's name (<c>checked</c>, <c>expanded</c>, <c>posinset</c>, …).</param>
/// <param name="OldValue">The value before, as text.</param>
/// <param name="NewValue">The value after, as text.</param>
public readonly record struct AxPropertyChange(AxChangeKind Kind, string Property, string? OldValue, string? NewValue);

/// <summary>A node present in both trees whose properties differ.</summary>
/// <param name="Before">The node in the earlier tree.</param>
/// <param name="After">The node in the later tree.</param>
/// <param name="Properties">The properties that differ, in a fixed order: role, name, description, ignored, hidden, focus, relations, then states by name.</param>
public sealed record AxNodeChange(AxNode Before, AxNode After, IReadOnlyList<AxPropertyChange> Properties);

/// <summary>
/// What differs between two accessibility trees. Nodes are matched by identity: within one document by DOM node; across two
/// snapshots through the edit's <see cref="ChangeSet"/> (an old DOM node and the new node it became). The document node
/// always matches the document node. Nodes with no counterpart are added or removed; those with one are compared on role,
/// name, description, ignored status, hidden status, focusability and every state.
/// </summary>
public sealed class TreeDiff
{
    internal TreeDiff(AccessibilityTree before, AccessibilityTree after, IReadOnlyList<AxNode> added, IReadOnlyList<AxNode> removed, IReadOnlyList<AxNodeChange> changed)
    {
        Before = before;
        After = after;
        Added = added;
        Removed = removed;
        Changed = changed;
    }

    /// <summary>The earlier tree.</summary>
    public AccessibilityTree Before { get; }

    /// <summary>The later tree.</summary>
    public AccessibilityTree After { get; }

    /// <summary>Nodes of <see cref="After"/> with no counterpart in <see cref="Before"/>, in tree order. Every node of an added subtree is listed.</summary>
    public IReadOnlyList<AxNode> Added { get; }

    /// <summary>Nodes of <see cref="Before"/> with no counterpart in <see cref="After"/>, in tree order.</summary>
    public IReadOnlyList<AxNode> Removed { get; }

    /// <summary>Matched nodes whose properties differ, in the later tree's order.</summary>
    public IReadOnlyList<AxNodeChange> Changed { get; }

    /// <summary>True when nothing was added, removed or changed.</summary>
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

    /// <summary>The node of the later tree that an earlier node became, or <see langword="null"/> when it was removed.</summary>
    public AxNode? Counterpart(AxNode before) => Pairing.Forward.GetValueOrDefault(before);

    internal NodePairing Pairing { get; init; } = null!;

    /// <summary>Compares two trees.</summary>
    /// <param name="before">The earlier tree.</param>
    /// <param name="after">The later tree.</param>
    /// <param name="changes">The change set of the edit that produced the later snapshot from the earlier one; <see langword="null"/> when both trees are of the same document (for instance under two style contexts).</param>
    /// <exception cref="ArgumentNullException">A tree is null.</exception>
    /// <exception cref="ArgumentException">The trees are of different documents and <paramref name="changes"/> does not relate them, or the same document and <paramref name="changes"/> is given.</exception>
    public static TreeDiff Compute(AccessibilityTree before, AccessibilityTree after, ChangeSet? changes = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        NodePairing pairing = NodePairing.Create(before, after, changes);
        var changed = new List<AxNodeChange>();
        foreach (AxNode a in after.Nodes)
        {
            if (pairing.Backward.TryGetValue(a, out AxNode? b))
            {
                List<AxPropertyChange> props = Compare(b, a);
                if (props.Count > 0)
                {
                    changed.Add(new AxNodeChange(b, a, props));
                }
            }
        }

        var added = new List<AxNode>();
        foreach (AxNode a in after.Nodes)
        {
            if (!pairing.Backward.ContainsKey(a))
            {
                added.Add(a);
            }
        }

        var removed = new List<AxNode>();
        foreach (AxNode b in before.Nodes)
        {
            if (!pairing.Forward.ContainsKey(b))
            {
                removed.Add(b);
            }
        }

        return new TreeDiff(before, after, added, removed, changed) { Pairing = pairing };
    }

    private static List<AxPropertyChange> Compare(AxNode b, AxNode a)
    {
        var l = new List<AxPropertyChange>();
        // The internal role (SvgRoot, sectionheader, LayoutTable...) is what the export shows, so it counts as the role.
        Add(l, AxChangeKind.Role, "role", b.InternalRole ?? b.Role, a.InternalRole ?? a.Role);
        Add(l, AxChangeKind.Name, "name", b.Name.Value, a.Name.Value);
        Add(l, AxChangeKind.Description, "description", b.Description.Value, a.Description.Value);
        Add(l, AxChangeKind.Ignored, "ignored", Bool(b.IsIgnored), Bool(a.IsIgnored));
        Add(l, AxChangeKind.Hidden, "hidden", b.Hidden.Reasons.ToString(), a.Hidden.Reasons.ToString());
        Add(l, AxChangeKind.Focus, "focusable", Bool(b.Focus.IsFocusable), Bool(a.Focus.IsFocusable));
        Add(l, AxChangeKind.Focus, "tabbable", Bool(b.Focus.IsTabbable), Bool(a.Focus.IsTabbable));
        foreach (AxRelationKind k in Enum.GetValues<AxRelationKind>())
        {
            Add(l, AxChangeKind.Relation, Tree.TreeText.AsciiLower(k.ToString()), RelationText(RelationOf(b, k)), RelationText(RelationOf(a, k)));
        }

        var x = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string k, string v) in StateEntries(b.States))
        {
            x[k] = v;
        }

        var y = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string k, string v) in StateEntries(a.States))
        {
            y[k] = v;
        }

        foreach ((string k, string _) in StateNames)
        {
            x.TryGetValue(k, out string? ov);
            y.TryGetValue(k, out string? nv);
            Add(l, AxChangeKind.State, k, ov, nv);
        }

        return l;
    }

    // The export reports a native label, caption or legend that names a node as a labelledby relation when the node has no
    // aria-labelledby of its own; the diff sees the same relation.
    private static AxRelation? RelationOf(AxNode n, AxRelationKind kind)
    {
        AxRelation? r = n.GetRelation(kind);
        if (r is null && kind == AxRelationKind.LabelledBy && n.Name.Source == AxTextSource.NativeLabelElement && n.Name.RelatedElements.Count > 0
            && n.Name.RelatedElements[0].Namespace == ElementNamespace.Html)
        {
            return new AxRelation(kind, "label", n.Name.RelatedElements, [], []);
        }

        return r;
    }

    private static string? RelationText(AxRelation? r)
    {
        if (r is not { } rel)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (Node e in rel.Elements)
        {
            parts.Add(e.Id is { Length: > 0 } id ? id : "(no id)");
        }

        foreach (string broken in rel.BrokenIds)
        {
            parts.Add("?" + broken);
        }

        return string.Join(' ', parts);
    }

    private static void Add(List<AxPropertyChange> l, AxChangeKind kind, string property, string? o, string? n)
    {
        if (!string.Equals(o, n, StringComparison.Ordinal))
        {
            l.Add(new AxPropertyChange(kind, property, o, n));
        }
    }

    private static string Bool(bool b) => b ? "true" : "false";

    private static readonly (string, string)[] StateNames = Names();

    private static (string, string)[] Names()
    {
        var states = new AxStates();
        var all = new List<(string, string)>();
        foreach ((string k, string v) in AllEntries(states, true))
        {
            all.Add((k, v));
        }

        return [.. all];
    }

    /// <summary>The states that are set, as (name, value) pairs; unset ones are left out.</summary>
    internal static IEnumerable<(string Name, string Value)> StateEntries(AxStates s) => AllEntries(s, false);

    private static IEnumerable<(string, string)> AllEntries(AxStates s, bool names)
    {
        static string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);
        static string I(int i) => i.ToString(CultureInfo.InvariantCulture);
        if (names || s.Expanded is not null)
        {
            yield return ("expanded", s.Expanded is { } e ? Bool(e) : "");
        }

        if (names || s.Checked is not null)
        {
            yield return ("checked", s.Checked is { } ck ? Tree.TreeText.AsciiLower(ck.ToString()) : "");
        }

        if (names || s.Selected is not null)
        {
            yield return ("selected", s.Selected is { } se ? Bool(se) : "");
        }

        if (names || s.Pressed is not null)
        {
            yield return ("pressed", s.Pressed is { } pr ? Tree.TreeText.AsciiLower(pr.ToString()) : "");
        }

        if (names || s.Disabled)
        {
            yield return ("disabled", Bool(s.Disabled));
        }

        if (names || s.Required)
        {
            yield return ("required", Bool(s.Required));
        }

        if (names || s.Invalid is not null)
        {
            yield return ("invalid", s.Invalid ?? "");
        }

        if (names || s.ReadOnly)
        {
            yield return ("readonly", Bool(s.ReadOnly));
        }

        if (names || s.Level is not null)
        {
            yield return ("level", s.Level is { } lv ? I(lv) : "");
        }

        if (names || s.PosInSet is not null)
        {
            yield return ("posinset", s.PosInSet is { } p ? I(p) : "");
        }

        if (names || s.SetSize is not null)
        {
            yield return ("setsize", s.SetSize is { } z ? I(z) : "");
        }

        if (names || s.ValueNow is not null)
        {
            yield return ("valuenow", s.ValueNow is { } vn ? N(vn) : "");
        }

        if (names || s.ValueMin is not null)
        {
            yield return ("valuemin", s.ValueMin is { } vm ? N(vm) : "");
        }

        if (names || s.ValueMax is not null)
        {
            yield return ("valuemax", s.ValueMax is { } vx ? N(vx) : "");
        }

        if (names || s.ValueText is not null)
        {
            yield return ("valuetext", s.ValueText ?? "");
        }

        if (names || s.Value is not null)
        {
            yield return ("value", s.Value ?? "");
        }

        if (names || s.Live is not null)
        {
            yield return ("live", s.Live ?? "");
        }

        if (names || s.Atomic is not null)
        {
            yield return ("atomic", s.Atomic is { } at ? Bool(at) : "");
        }

        if (names || s.Relevant is not null)
        {
            yield return ("relevant", s.Relevant ?? "");
        }

        if (names || s.Busy)
        {
            yield return ("busy", Bool(s.Busy));
        }

        if (names || s.Modal)
        {
            yield return ("modal", Bool(s.Modal));
        }

        if (names || s.Current is not null)
        {
            yield return ("current", s.Current ?? "");
        }

        if (names || s.HasPopup is not null)
        {
            yield return ("haspopup", s.HasPopup ?? "");
        }

        if (names || s.Orientation is not null)
        {
            yield return ("orientation", s.Orientation ?? "");
        }

        if (names || s.MultiSelectable)
        {
            yield return ("multiselectable", Bool(s.MultiSelectable));
        }

        if (names || s.Multiline)
        {
            yield return ("multiline", Bool(s.Multiline));
        }

        if (names || s.AutoComplete is not null)
        {
            yield return ("autocomplete", s.AutoComplete ?? "");
        }

        if (names || s.KeyShortcuts is not null)
        {
            yield return ("keyshortcuts", s.KeyShortcuts ?? "");
        }

        if (names || s.RoleDescription is not null)
        {
            yield return ("roledescription", s.RoleDescription ?? "");
        }

        if (names || s.Url is not null)
        {
            yield return ("url", s.Url ?? "");
        }

        if (names || s.Editable is not null)
        {
            yield return ("editable", s.Editable ?? "");
        }
    }

}

/// <summary>Which node of one tree is the same node as which of another; see <see cref="TreeDiff"/>.</summary>
internal sealed class NodePairing
{
    public Dictionary<AxNode, AxNode> Forward { get; } = [];

    public Dictionary<AxNode, AxNode> Backward { get; } = [];

    public static NodePairing Create(AccessibilityTree before, AccessibilityTree after, ChangeSet? changes)
    {
        var p = new NodePairing();
        if (changes is null)
        {
            if (!ReferenceEquals(before.Document, after.Document))
            {
                throw new ArgumentException("The trees are of different documents; pass the change set that relates them.", nameof(changes));
            }

            foreach (AxNode b in before.Nodes)
            {
                if (after.Find(after.Document.GetNode(b.DomNode.Index)) is { } a && a.Kind == b.Kind)
                {
                    p.Forward[b] = a;
                    p.Backward[a] = b;
                }
            }

            PairGenerated(before, after, p, b => after.Document.GetNode(b.DomNode.Index));
            return p;
        }

        if (!ReferenceEquals(changes.OldDocument, before.Document) || !ReferenceEquals(changes.NewDocument, after.Document))
        {
            throw new ArgumentException("The change set does not relate these trees' documents.", nameof(changes));
        }

        foreach (AxNode b in before.Nodes)
        {
            Node n = b.Kind == AxNodeKind.Document ? after.Document.Root : changes.MapToNew(b.DomNode);
            if (n.IsNull)
            {
                continue;
            }

            if (after.Find(n) is { } a && a.Kind == b.Kind && !p.Backward.ContainsKey(a))
            {
                p.Forward[b] = a;
                p.Backward[a] = b;
            }
        }

        PairGenerated(before, after, p, b => changes.MapToNew(b.DomNode));
        return p;
    }

    /// <summary>
    /// Generated nodes (list markers, select popups) have no node of their own; they sit on a host element. They are paired in order
    /// among the generated nodes of the paired host when role and kind agree, so an unchanged marker is not reported as removed
    /// and added.
    /// </summary>
    private static void PairGenerated(AccessibilityTree before, AccessibilityTree after, NodePairing p, Func<AxNode, Node> mapHost)
    {
        var afterByHost = new Dictionary<int, List<AxNode>>();
        foreach (AxNode a in after.Nodes)
        {
            if (a.Kind == AxNodeKind.Generated)
            {
                if (!afterByHost.TryGetValue(a.DomNode.Index, out List<AxNode>? l))
                {
                    afterByHost[a.DomNode.Index] = l = [];
                }

                l.Add(a);
            }
        }

        var used = new Dictionary<int, int>();
        foreach (AxNode b in before.Nodes)
        {
            if (b.Kind != AxNodeKind.Generated)
            {
                continue;
            }

            Node host = mapHost(b);
            if (host.IsNull || !afterByHost.TryGetValue(host.Index, out List<AxNode>? candidates))
            {
                continue;
            }

            used.TryGetValue(host.Index, out int at);
            if (at < candidates.Count && candidates[at].InternalRole == b.InternalRole && candidates[at].IsIgnored == b.IsIgnored)
            {
                p.Forward[b] = candidates[at];
                p.Backward[candidates[at]] = b;
                used[host.Index] = at + 1;
            }
        }
    }
}
