using System;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml;

/// <summary>Applying edits to a <see cref="Document"/>.</summary>
public static class DocumentEditing
{
    /// <summary>
    /// Applies a transaction and returns a new snapshot plus the change set; <paramref name="document"/> and every
    /// handle into it stay valid and unchanged. All edits apply or none does: on failure the result says which edit
    /// failed and why, and has no new snapshot.
    /// </summary>
    /// <param name="document">The snapshot the transaction was made for.</param>
    /// <param name="transaction">The edits.</param>
    /// <param name="options">Bounds; <see langword="null"/> means the defaults.</param>
    /// <param name="cancellationToken">Checked between edits and between re-parses.</param>
    /// <exception cref="ArgumentException"><paramref name="transaction"/> was made for another snapshot.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static EditResult Apply(this Document document, EditTransaction transaction, EditOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(document, transaction.Document))
        {
            throw new ArgumentException("The transaction was made for a different snapshot.", nameof(transaction));
        }

        return transaction.Apply(options, cancellationToken);
    }

    /// <summary>Applies a list of edits as one transaction; see <see cref="Apply(Document, EditTransaction, EditOptions?, CancellationToken)"/>.</summary>
    /// <param name="document">The snapshot the edits target.</param>
    /// <param name="edits">The edits in order.</param>
    /// <param name="options">Bounds; <see langword="null"/> means the defaults.</param>
    /// <param name="cancellationToken">Checked between edits and between re-parses.</param>
    /// <exception cref="ArgumentException">An edit targets a node of another snapshot.</exception>
    public static EditResult Apply(this Document document, IEnumerable<Edit> edits, EditOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(edits);
        return new EditTransaction(document, edits).Apply(options, cancellationToken);
    }
}
