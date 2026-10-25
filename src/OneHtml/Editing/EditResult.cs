using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>What happened to one edit of a transaction.</summary>
public enum EditStatus
{
    /// <summary>The edit is part of the new snapshot (a no-op edit, such as removing an absent attribute, counts).</summary>
    Applied = 0,

    /// <summary>The edit does not fit its target, or its input is not usable; <see cref="EditOutcome.Reason"/> says why. The transaction applied nothing.</summary>
    Invalid = 1,

    /// <summary>
    /// The edit makes sense on the tree, but no markup can express the result: re-parsing any rewrite of the
    /// surrounding source gives a different tree. The transaction applied nothing.
    /// </summary>
    Failed = 2,

    /// <summary>An earlier edit stopped the transaction before this one was looked at.</summary>
    NotAttempted = 3,
}

/// <summary>The result of one edit.</summary>
public sealed class EditOutcome
{
    internal EditOutcome(int index, Edit edit)
    {
        Index = index;
        Edit = edit;
    }

    /// <summary>The edit's position in the transaction.</summary>
    public int Index { get; }

    /// <summary>The edit.</summary>
    public Edit Edit { get; }

    /// <summary>What happened.</summary>
    public EditStatus Status { get; internal set; } = EditStatus.NotAttempted;

    /// <summary>Why the edit was <see cref="EditStatus.Invalid"/> or <see cref="EditStatus.Failed"/>; <see langword="null"/> otherwise.</summary>
    public string? Reason { get; internal set; }

    /// <summary>
    /// The deepest fallback level any of this edit's regions needed: 0 when every byte change is local to the
    /// edited construct, 1 or more when an enclosing element had to be re-serialized (see <see cref="EditResult.Fallbacks"/>).
    /// </summary>
    public int FallbackLevel { get; internal set; }

    /// <summary>The indexes into <see cref="ChangeSet.Ranges"/> of the byte ranges this edit changed.</summary>
    public IReadOnlyList<int> RangeIndexes { get; internal set; } = [];
}

/// <summary>What changed about a node that exists in both snapshots.</summary>
[Flags]
public enum NodeChanges
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The element's attributes changed.</summary>
    Attributes = 1,

    /// <summary>The characters of a text node changed.</summary>
    Data = 2,

    /// <summary>The list of children changed.</summary>
    Children = 4,

    /// <summary>The node moved to another parent (it was wrapped or its parent was unwrapped).</summary>
    Parent = 8,
}

/// <summary>A node that is in both snapshots and changed.</summary>
public readonly record struct NodeChange
{
    internal NodeChange(Node node, Node old, NodeChanges changes)
    {
        Node = node;
        Old = old;
        Changes = changes;
    }

    /// <summary>The node in the new snapshot.</summary>
    public Node Node { get; }

    /// <summary>The same node in the old snapshot.</summary>
    public Node Old { get; }

    /// <summary>What changed.</summary>
    public NodeChanges Changes { get; }
}

/// <summary>
/// One contiguous stretch of the source that was replaced: the old range, the new range and the new bytes. The list
/// of these is the audit diff of a transaction. Ranges are in document order and do not overlap.
/// </summary>
public sealed class EditedRange
{
    internal EditedRange(SourceRange oldText, SourceRange oldBytes, SourceRange newText, SourceRange newBytes, ReadOnlyMemory<byte> newData, string newString, IReadOnlyList<int> edits, int fallbackLevel)
    {
        OldTextRange = oldText;
        OldByteRange = oldBytes;
        NewTextRange = newText;
        NewByteRange = newBytes;
        NewBytes = newData;
        NewText = newString;
        EditIndexes = edits;
        FallbackLevel = fallbackLevel;
    }

    /// <summary>The replaced range in the old snapshot's text (UTF-16 offsets); empty for a pure insertion.</summary>
    public SourceRange OldTextRange { get; }

    /// <summary>The replaced range in the old snapshot's bytes. For a document with no original bytes, the same as the text range.</summary>
    public SourceRange OldByteRange { get; }

    /// <summary>The range the new characters occupy in the new snapshot's text; empty for a pure deletion.</summary>
    public SourceRange NewTextRange { get; }

    /// <summary>The range the new bytes occupy in the new snapshot's bytes. For a document with no original bytes, the same as the text range.</summary>
    public SourceRange NewByteRange { get; }

    /// <summary>The bytes that replaced the old range, in the document's encoding. Empty for a document with no original bytes.</summary>
    public ReadOnlyMemory<byte> NewBytes { get; }

    /// <summary>The characters that replaced the old range.</summary>
    public string NewText { get; }

    /// <summary>The edits (by index in the transaction) that caused this change.</summary>
    public IReadOnlyList<int> EditIndexes { get; }

    /// <summary>0 when the change is local to the edited construct; 1 or more when it re-serialized an enclosing element, the number of levels above the smallest.</summary>
    public int FallbackLevel { get; }

    /// <summary>True when an enclosing element was re-serialized, which rewrites bytes the edit did not name.</summary>
    public bool IsFallback => FallbackLevel > 0;
}

/// <summary>A place where an edit could not be applied locally and an enclosing element was re-serialized.</summary>
/// <param name="EditIndex">The edit that needed it.</param>
/// <param name="Range">The old text range that was rewritten.</param>
/// <param name="Level">How many enclosing levels up the rewrite had to go.</param>
/// <param name="Reason">Why the local splice was not enough.</param>
public readonly record struct FallbackInfo(int EditIndex, SourceRange Range, int Level, string Reason);

/// <summary>
/// What a transaction changed. Added and changed nodes are handles into the new snapshot, removed nodes handles into
/// the old one. Only the roots of added and removed subtrees are listed; their descendants are added or removed
/// with them.
/// </summary>
public sealed class ChangeSet
{
    internal ChangeSet(Document oldDocument, Document newDocument, IReadOnlyList<Node> added, IReadOnlyList<Node> removed, IReadOnlyList<NodeChange> changed, IReadOnlyList<EditedRange> ranges, int[] oldToNew)
    {
        OldDocument = oldDocument;
        NewDocument = newDocument;
        Added = added;
        Removed = removed;
        Changed = changed;
        Ranges = ranges;
        OldToNew = oldToNew;
    }

    /// <summary>The snapshot the transaction was applied to.</summary>
    public Document OldDocument { get; }

    /// <summary>The snapshot the transaction produced.</summary>
    public Document NewDocument { get; }

    /// <summary>
    /// The roots of the subtrees the edits created, in the new snapshot. Whitespace the editor wrote to keep the
    /// markup's indentation is listed like any other added node.
    /// </summary>
    public IReadOnlyList<Node> Added { get; }

    /// <summary>
    /// The roots of the subtrees the edits removed, in the old snapshot, in document order. An unwrapped element is
    /// listed alone; its children are not removed.
    /// </summary>
    public IReadOnlyList<Node> Removed { get; }

    /// <summary>Nodes present in both snapshots that changed, in the new snapshot's node order.</summary>
    public IReadOnlyList<NodeChange> Changed { get; }

    /// <summary>The audit diff: every replaced byte range, in document order.</summary>
    public IReadOnlyList<EditedRange> Ranges { get; }

    internal int[] OldToNew { get; }

    /// <summary>
    /// Finds the node of the new snapshot that an old node became. When several text nodes merged into one, they all
    /// map to it.
    /// </summary>
    /// <param name="old">A node of <see cref="OldDocument"/>.</param>
    /// <returns>The node, or the null node when the old node was removed (or is not in the tree).</returns>
    /// <exception cref="ArgumentException"><paramref name="old"/> belongs to another snapshot.</exception>
    public Node MapToNew(Node old)
    {
        if (!ReferenceEquals(old.Document, OldDocument))
        {
            throw new ArgumentException("The node is not from the old snapshot.", nameof(old));
        }

        int i = OldToNew[old.Index];
        return i < 0 ? default : new Node(NewDocument, i);
    }
}

/// <summary>The result of <c>Apply</c>.</summary>
public sealed class EditResult
{
    internal EditResult(bool succeeded, string? failure, Document? document, ChangeSet? changes, IReadOnlyList<EditOutcome> outcomes, IReadOnlyList<FallbackInfo> fallbacks, int verifications)
    {
        Succeeded = succeeded;
        Failure = failure;
        Document = document;
        Changes = changes;
        Outcomes = outcomes;
        Fallbacks = fallbacks;
        Verifications = verifications;
    }

    /// <summary>True when every edit applied.</summary>
    public bool Succeeded { get; }

    /// <summary>A sentence saying why the transaction failed; <see langword="null"/> on success.</summary>
    public string? Failure { get; }

    /// <summary>The new snapshot; <see langword="null"/> when the transaction failed.</summary>
    public Document? Document { get; }

    /// <summary>What changed; <see langword="null"/> when the transaction failed. An empty transaction gives an empty change set and the same bytes.</summary>
    public ChangeSet? Changes { get; }

    /// <summary>One entry per edit, in order.</summary>
    public IReadOnlyList<EditOutcome> Outcomes { get; }

    /// <summary>
    /// The edits that could not be made as local splices and re-serialized an enclosing element instead (the
    /// smallest one whose markup re-parses to the expected tree). Empty when every change was local.
    /// </summary>
    public IReadOnlyList<FallbackInfo> Fallbacks { get; }

    /// <summary>How many times the edited output was re-parsed and compared; 1 when nothing needed a fallback, 0 for an empty transaction.</summary>
    public int Verifications { get; }
}
