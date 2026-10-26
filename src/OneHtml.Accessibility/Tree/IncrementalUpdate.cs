using System;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Accessibility.Aria;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Accessibility.Tree;

/// <summary>
/// Who points at an element by ID, for one document: the elements whose <c>aria-labelledby</c>, <c>aria-describedby</c> or
/// <c>aria-owns</c> name an ID (WAI-ARIA 1.2 section 6.2). The accessible name and description of an element read the
/// elements those attributes reach (AccName 1.2 steps 2B and 2D), so a change to a target changes the referrer.
/// </summary>
internal sealed class DependencyIndex
{
    private static readonly string[] Attributes = ["aria-labelledby", "aria-describedby", "aria-owns"];
    private readonly Dictionary<string, List<int>> _referrers = new(StringComparer.Ordinal);

    public DependencyIndex(Document doc, int maxReferences, CancellationToken ct)
    {
        int visited = 0;
        foreach (Node e in doc.Root.DescendantElements)
        {
            if ((++visited & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            foreach (string attribute in Attributes)
            {
                string? value = e.GetAttribute(attribute);
                if (value is null)
                {
                    continue;
                }

                int pos = 0;
                int count = 0;
                while (OwnsMap.NextToken(value, ref pos, out string id) && count++ < maxReferences)
                {
                    if (!_referrers.TryGetValue(id, out List<int>? list))
                    {
                        _referrers[id] = list = [];
                    }

                    if (list.Count == 0 || list[^1] != e.Index)
                    {
                        list.Add(e.Index);
                    }
                }
            }
        }
    }

    /// <summary>The elements that name <paramref name="id"/> in one of the attributes, in document order.</summary>
    public IReadOnlyList<int> Referrers(string id) => _referrers.TryGetValue(id, out List<int>? l) ? l : [];

    /// <summary>
    /// The elements whose name, description or content reads element <paramref name="index"/>: its parent (name from content,
    /// AccName step 2F; also <c>legend</c>, <c>caption</c> and <c>figcaption</c> children), the elements that reference its ID,
    /// the other children of a <c>figure</c>, and for a <c>label</c> the control it labels (the <c>for</c> target) and the controls inside it (HTML section 4.10.4).
    /// <paramref name="work"/> counts the elements visited, so a caller can stop a walk that nested labels make quadratic.
    /// </summary>
    public void Dependents(Document doc, int index, List<int> into, ref long work)
    {
        Node n = doc.GetNode(index);
        Node parent = n.Parent;
        if (!parent.IsNull && parent.Index != 0)
        {
            into.Add(parent.Index);
            if (parent.Namespace == ElementNamespace.Html && parent.LocalName == "figure")
            {
                // An img that fills its figure is named by the figure's figcaption (HTML-AAM section 4.1.1), so the
                // figure's children read each other.
                for (Node c = parent.FirstChild; !c.IsNull; c = c.NextSibling)
                {
                    if (c.IsElement)
                    {
                        into.Add(c.Index);
                    }

                    work++;
                }
            }
        }

        if (!n.IsElement)
        {
            return;
        }

        if (n.Id is { Length: > 0 } id)
        {
            into.AddRange(Referrers(id));
        }

        if (n.Namespace == ElementNamespace.Html && n.LocalName == "label")
        {
            if (n.GetAttribute("for") is { } f)
            {
                Node target = doc.GetElementById(f);
                if (!target.IsNull)
                {
                    into.Add(target.Index);
                }
            }

            foreach (Node d in n.DescendantElements)
            {
                into.Add(d.Index);
                work++;
            }
        }
    }
}

/// <summary>
/// The plan for building a tree from a previous one: which elements keep their accessible name and description. The roles,
/// hidden status, <c>aria-owns</c> structure, states, relations, focus order and diagnostics are cheap single passes and are
/// always computed afresh; names and descriptions are the costly part (AccName walks the contents and the references of each
/// element), so those are carried over for every element whose inputs did not change.
/// </summary>
internal sealed class UpdatePlan
{
    private readonly AccessibilityTree _old;
    private readonly Document _newDoc;
    private readonly ChangeSet? _changes;
    private readonly IReadOnlyList<int> _seeds;
    private int[] _oldToNew = [];
    private int[] _newToOld = [];
    private bool[] _dirty = [];

    public UpdatePlan(AccessibilityTree old, Document newDoc, ChangeSet? changes, IReadOnlyList<int> seeds)
    {
        _old = old;
        _newDoc = newDoc;
        _changes = changes;
        _seeds = seeds;
    }

    /// <summary>True when the dependency walk reached its work bound and every name is computed again instead.</summary>
    public bool Exhausted { get; private set; }

    public int Recomputed { get; private set; }

    public int Reused { get; private set; }

    /// <summary>Works out which elements are dirty. Called once the new hidden, role and <c>aria-owns</c> maps exist.</summary>
    public void Prepare(HiddenMap hidden, RoleMap roles, OwnsMap owns, int maxReferences, CancellationToken ct)
    {
        Document oldDoc = _old.Document;
        int oldCount = oldDoc.NodeCount;
        int newCount = _newDoc.NodeCount;
        _oldToNew = new int[oldCount];
        _newToOld = new int[newCount];
        Array.Fill(_newToOld, -1);
        for (int o = 0; o < oldCount; o++)
        {
            int n = o;
            if (_changes is not null)
            {
                Node m = _changes.MapToNew(oldDoc.GetNode(o));
                n = m.IsNull ? -1 : m.Index;
            }

            _oldToNew[o] = n;
            if (n >= 0 && n < newCount && _newToOld[n] < 0)
            {
                _newToOld[n] = o;
            }
        }

        _dirty = new bool[newCount];
        DependencyIndex oldDeps = _old.Dependencies(maxReferences, ct);
        var newDeps = new DependencyIndex(_newDoc, maxReferences, ct);
        var queue = new Queue<int>();
        var dependents = new List<int>();

        long work = 0;
        long budget = 8L * newCount + 100_000;
        void Mark(int n)
        {
            if (n >= 0 && n < newCount && !_dirty[n])
            {
                _dirty[n] = true;
                queue.Enqueue(n);
            }
        }

        // Everything that reads an old node also reads what replaced it; map the old readers to the new document.
        void MarkOldDependents(int o)
        {
            if (work > budget)
            {
                return;
            }

            dependents.Clear();
            oldDeps.Dependents(oldDoc, o, dependents, ref work);
            foreach (int d in dependents)
            {
                Mark(_oldToNew[d]);
            }
        }

        // An ID names the first element that carries it (HTML section 3.2.6.2), so an element that gains, loses or changes an ID
        // takes a reference with it from the element that held the ID, or hands it to the next one. A label's control is such a
        // reader: it is found through the ID in the label's for attribute.
        void MarkIdHolders(string? id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            Node holder = _newDoc.GetElementById(id);
            if (!holder.IsNull)
            {
                Mark(holder.Index);
            }

            Node before = oldDoc.GetElementById(id);
            if (!before.IsNull)
            {
                Mark(_oldToNew[before.Index]);
            }
        }

        void MarkSeed(int n, int o)
        {
            if (n >= 0)
            {
                Mark(n);
                if (_newDoc.GetNode(n).IsElement)
                {
                    MarkIdHolders(_newDoc.GetNode(n).Id);
                }
            }

            if (o >= 0)
            {
                MarkOldDependents(o);
                if (oldDoc.GetNode(o).IsElement)
                {
                    MarkIdHolders(oldDoc.GetNode(o).Id);
                }
            }
        }

        // 1. Nodes with no counterpart, or whose place, hiding, role or owner differs.
        HiddenMap oldHidden = _old.HiddenMap;
        RoleMap oldRoles = _old.Roles;
        OwnsMap oldOwns = _old.OwnsMap;
        for (int n = 1; n < newCount; n++)
        {
            if ((n & 1023) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            int o = _newToOld[n];
            Node node = _newDoc.GetNode(n);
            if (node.Kind is not (NodeKind.Element or NodeKind.Text))
            {
                continue;
            }

            if (o < 0)
            {
                MarkSeed(n, -1);
                continue;
            }

            Node oldNode = oldDoc.GetNode(o);
            if (oldNode.Kind != node.Kind)
            {
                MarkSeed(n, o);
                continue;
            }

            int op = oldNode.Parent.IsNull ? -1 : _oldToNew[oldNode.Parent.Index];
            int np = node.Parent.IsNull ? -1 : node.Parent.Index;
            bool differs = op != np
                || hidden.Reasons(n) != oldHidden.Reasons(o) || hidden.Own(n) != oldHidden.Own(o);
            if (!differs && node.IsElement)
            {
                NodeRole a = roles[node];
                NodeRole b = oldRoles[oldNode];
                int oldOwner = oldOwns.OwnerOf(o);
                differs = !ReferenceEquals(a.Role, b.Role) || a.Origin != b.Origin || a.IsDisabled != b.IsDisabled
                    || (owns.OwnerOf(n) < 0) != (oldOwner < 0)
                    || (oldOwner >= 0 && _oldToNew[oldOwner] != owns.OwnerOf(n))
                    || OwnedDiffers(owns.OwnedBy(n), oldOwns.OwnedBy(o));
            }

            if (differs)
            {
                MarkSeed(n, o);
            }
        }

        // 2. What the edit and the restyle say changed, with what read it before.
        foreach (int s in _seeds)
        {
            if (s >= 0 && s < newCount)
            {
                MarkSeed(s, _newToOld[s]);
            }
        }

        if (_changes is not null)
        {
            foreach (NodeChange c in _changes.Changed)
            {
                MarkSeed(c.Node.Index, c.Old.Index);
            }
        }

        // 3. Removed nodes: whatever read them (a label's control, an ID's referrers, the parent they left).
        for (int o = 1; o < oldCount; o++)
        {
            if (_oldToNew[o] < 0 && oldDoc.GetNode(o).Kind is NodeKind.Element or NodeKind.Text)
            {
                MarkSeed(-1, o);
            }
        }

        // 4. Close over the readers.
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (work > budget)
            {
                // Nested labels make the walk quadratic; compute every name again, which is always correct.
                Exhausted = true;
                Array.Fill(_dirty, true);
                return;
            }

            int n = queue.Dequeue();
            dependents.Clear();
            newDeps.Dependents(_newDoc, n, dependents, ref work);
            foreach (int d in dependents)
            {
                Mark(d);
            }
        }
    }

    private bool OwnedDiffers(List<int>? now, List<int>? before)
    {
        int a = now?.Count ?? 0;
        int b = before?.Count ?? 0;
        if (a != b)
        {
            return true;
        }

        for (int i = 0; i < a; i++)
        {
            if (_oldToNew[before![i]] != now![i])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The previous name and description of an element, mapped to the new document, when its inputs did not change.</summary>
    public bool TryReuse(int newIndex, out AxText name, out AxText description)
    {
        name = description = AxText.Empty;
        int o = newIndex < _newToOld.Length ? _newToOld[newIndex] : -1;
        if (o < 0 || _dirty[newIndex] || _old.FindByIndex(o) is not { Kind: AxNodeKind.Element } prev)
        {
            Recomputed++;
            return false;
        }

        if (!Remap(prev.Name, out name) || !Remap(prev.Description, out description))
        {
            Recomputed++;
            return false;
        }

        Reused++;
        return true;
    }

    private bool Remap(AxText t, out AxText mapped)
    {
        mapped = t;
        if (t.RelatedElements.Count == 0)
        {
            return true;
        }

        var list = new List<Node>(t.RelatedElements.Count);
        foreach (Node r in t.RelatedElements)
        {
            int n = _oldToNew[r.Index];
            if (n < 0)
            {
                return false;
            }

            list.Add(_newDoc.GetNode(n));
        }

        mapped = t with { RelatedElements = list };
        return true;
    }
}
