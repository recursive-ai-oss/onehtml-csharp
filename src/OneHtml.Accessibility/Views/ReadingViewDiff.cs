using System;
using System.Collections.Generic;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Accessibility;

/// <summary>A reading step present in both views whose wording or language differs.</summary>
/// <param name="Before">The step in the earlier view.</param>
/// <param name="After">The step in the later view.</param>
public sealed record ReadingItemChange(ReadingItem Before, ReadingItem After);

/// <summary>
/// What differs between two reading views. A step is identified by the tree node it comes from (matched across trees as in
/// <see cref="TreeDiff"/>) and its <see cref="ReadingItem.Kind"/>; a text run is identified by its first text node. Steps that
/// match are compared on <see cref="ReadingItem.Text"/> and <see cref="ReadingItem.Lang"/>. A step that only moved is not a
/// change.
/// </summary>
public sealed class ReadingViewDiff
{
    private ReadingViewDiff(IReadOnlyList<ReadingItem> added, IReadOnlyList<ReadingItem> removed, IReadOnlyList<ReadingItemChange> changed)
    {
        Added = added;
        Removed = removed;
        Changed = changed;
    }

    /// <summary>Steps of the later view with no counterpart, in reading order.</summary>
    public IReadOnlyList<ReadingItem> Added { get; }

    /// <summary>Steps of the earlier view with no counterpart, in reading order.</summary>
    public IReadOnlyList<ReadingItem> Removed { get; }

    /// <summary>Matched steps whose text or language differs, in the later view's order.</summary>
    public IReadOnlyList<ReadingItemChange> Changed { get; }

    /// <summary>True when the two views say the same thing.</summary>
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

    internal static ReadingViewDiff Compute(ReadingView before, ReadingView after, ChangeSet? changes)
    {
        NodePairing pairing = NodePairing.Create(before.Tree, after.Tree, changes);
        var later = new Dictionary<(AxNode, ReadingItemKind), ReadingItem>();
        foreach (ReadingItem i in after.Items)
        {
            later.TryAdd((i.Node, i.Kind), i);
        }

        var matched = new HashSet<ReadingItem>();
        var removed = new List<ReadingItem>();
        var pairs = new Dictionary<ReadingItem, ReadingItem>();
        foreach (ReadingItem b in before.Items)
        {
            if (pairing.Forward.TryGetValue(b.Node, out AxNode? n) && later.TryGetValue((n, b.Kind), out ReadingItem? a) && matched.Add(a))
            {
                pairs[a] = b;
            }
            else
            {
                removed.Add(b);
            }
        }

        var added = new List<ReadingItem>();
        var changed = new List<ReadingItemChange>();
        foreach (ReadingItem a in after.Items)
        {
            if (!pairs.TryGetValue(a, out ReadingItem? b))
            {
                added.Add(a);
            }
            else if (!string.Equals(a.Text, b.Text, StringComparison.Ordinal) || !string.Equals(a.Lang, b.Lang, StringComparison.Ordinal))
            {
                changed.Add(new ReadingItemChange(b, a));
            }
        }

        return new ReadingViewDiff(added, removed, changed);
    }
}
