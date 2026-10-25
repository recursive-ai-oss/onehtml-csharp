using System;
using System.Collections.Generic;
using System.Threading;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>Bounds and switches for <c>Apply</c>.</summary>
public sealed class EditOptions
{
    /// <summary>The most edits one transaction may hold. Default 100,000.</summary>
    public int MaxEdits { get; init; } = 100_000;

    /// <summary>
    /// The most characters of markup and text, all edits together, a transaction may insert. Checked before anything
    /// is parsed. Default 16,777,216.
    /// </summary>
    public long MaxInsertedLength { get; init; } = 1 << 24;

    /// <summary>
    /// The most times the edited document may be re-parsed while looking for the smallest region that has to be
    /// re-serialized (see <see cref="EditResult.Fallbacks"/>). One re-parse always happens; more only happen when
    /// the first check fails. Default 1,024.
    /// </summary>
    public int MaxVerifications { get; init; } = 1024;
}

/// <summary>
/// An ordered list of edits against one snapshot, applied together: all of them or none. Edits apply in order, so a
/// later edit sees the effect of an earlier one on the tree. Every target is a handle into
/// <see cref="Document"/>.
/// </summary>
public sealed class EditTransaction
{
    /// <summary>Creates a transaction.</summary>
    /// <param name="document">The snapshot the edits target.</param>
    /// <param name="edits">The edits, in the order they apply. Copied.</param>
    /// <exception cref="ArgumentException">An edit targets a node of another snapshot or the null node.</exception>
    public EditTransaction(Document document, IEnumerable<Edit> edits)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(edits);
        Document = document;
        var list = new List<Edit>(edits);
        foreach (Edit e in list)
        {
            ArgumentNullException.ThrowIfNull(e);
            if (e.Target.IsNull || !ReferenceEquals(e.Target.Document, document))
            {
                throw new ArgumentException("An edit targets a node of a different snapshot: " + e, nameof(edits));
            }
        }

        Edits = list;
    }

    /// <summary>The snapshot the edits target.</summary>
    public Document Document { get; }

    /// <summary>The edits in order.</summary>
    public IReadOnlyList<Edit> Edits { get; }

    /// <summary>Applies the transaction; the same as <c>Document.Apply(this)</c>.</summary>
    /// <param name="options">Bounds; <see langword="null"/> means the defaults.</param>
    /// <param name="cancellationToken">Checked between edits and between re-parses.</param>
    public EditResult Apply(EditOptions? options = null, CancellationToken cancellationToken = default) =>
        EditEngine.Run(this, options ?? new EditOptions(), cancellationToken);
}

/// <summary>
/// Gathers edits against a snapshot in a mutating style and turns them into a transaction. Each method adds one edit
/// and returns it; nothing changes until <see cref="Apply"/>.
/// </summary>
public sealed class DocumentEditor
{
    private readonly List<Edit> _edits = [];

    /// <summary>Starts an editor for a snapshot.</summary>
    /// <param name="document">The snapshot whose nodes the edits target.</param>
    public DocumentEditor(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
    }

    /// <summary>The snapshot being edited.</summary>
    public Document Document { get; }

    /// <summary>The edits gathered so far, in order.</summary>
    public IReadOnlyList<Edit> Edits => _edits;

    /// <summary>Adds an edit built elsewhere.</summary>
    /// <param name="edit">The edit; its target must be a node of <see cref="Document"/>.</param>
    public Edit Add(Edit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.Target.IsNull || !ReferenceEquals(edit.Target.Document, Document))
        {
            throw new ArgumentException("The edit targets a node of a different snapshot.", nameof(edit));
        }

        _edits.Add(edit);
        return edit;
    }

    /// <inheritdoc cref="Edit.SetAttribute"/>
    public Edit SetAttribute(Node element, string name, string value) => Add(Edit.SetAttribute(element, name, value));

    /// <inheritdoc cref="Edit.RemoveAttribute"/>
    public Edit RemoveAttribute(Node element, string name) => Add(Edit.RemoveAttribute(element, name));

    /// <inheritdoc cref="Edit.ReplaceText"/>
    public Edit ReplaceText(Node target, string text) => Add(Edit.ReplaceText(target, text));

    /// <inheritdoc cref="Edit.InsertBefore"/>
    public Edit InsertBefore(Node node, string html) => Add(Edit.InsertBefore(node, html));

    /// <inheritdoc cref="Edit.InsertAfter"/>
    public Edit InsertAfter(Node node, string html) => Add(Edit.InsertAfter(node, html));

    /// <inheritdoc cref="Edit.Prepend"/>
    public Edit Prepend(Node element, string html) => Add(Edit.Prepend(element, html));

    /// <inheritdoc cref="Edit.Append"/>
    public Edit Append(Node element, string html) => Add(Edit.Append(element, html));

    /// <inheritdoc cref="Edit.Wrap"/>
    public Edit Wrap(Node node, string html, int count = 1) => Add(Edit.Wrap(node, html, count));

    /// <inheritdoc cref="Edit.Unwrap"/>
    public Edit Unwrap(Node element) => Add(Edit.Unwrap(element));

    /// <inheritdoc cref="Edit.Replace"/>
    public Edit Replace(Node node, string html) => Add(Edit.Replace(node, html));

    /// <inheritdoc cref="Edit.Remove"/>
    public Edit Remove(Node node) => Add(Edit.Remove(node));

    /// <inheritdoc cref="Edit.SetStyle"/>
    public Edit SetStyle(Node element, string property, string value, bool important = false) =>
        Add(Edit.SetStyle(element, property, value, important));

    /// <inheritdoc cref="Edit.RemoveStyle"/>
    public Edit RemoveStyle(Node element, string property) => Add(Edit.RemoveStyle(element, property));

    /// <summary>Forgets the gathered edits.</summary>
    public void Clear() => _edits.Clear();

    /// <summary>Snapshots the gathered edits as a transaction. Later changes to the editor do not affect it.</summary>
    public EditTransaction ToTransaction() => new(Document, _edits);

    /// <summary>Applies the gathered edits to <see cref="Document"/>.</summary>
    /// <param name="options">Bounds; <see langword="null"/> means the defaults.</param>
    /// <param name="cancellationToken">Checked between edits and between re-parses.</param>
    public EditResult Apply(EditOptions? options = null, CancellationToken cancellationToken = default) =>
        ToTransaction().Apply(options, cancellationToken);
}
