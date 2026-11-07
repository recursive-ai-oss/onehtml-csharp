using System;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Accessibility.Tree;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Accessibility;

/// <summary>A heading in <see cref="PageOutline.Headings"/>.</summary>
/// <param name="Node">The heading's element in the document.</param>
/// <param name="Level">Its level, as <see cref="HeadingEntry.Level"/> gives it.</param>
/// <param name="Name">Its accessible name; empty when it has none, and also when the outline was made without heading names (see <see cref="PageOutline.HasHeadingNames"/>).</param>
/// <param name="Lang">The language in effect, or null.</param>
public sealed record OutlineHeading(Node Node, int Level, string Name, string? Lang);

/// <summary>A landmark in <see cref="PageOutline.Landmarks"/>.</summary>
/// <param name="Node">The landmark's element in the document.</param>
/// <param name="Role">Its role, as <see cref="LandmarkEntry.Role"/> gives it.</param>
/// <param name="Name">Its accessible name; empty when it has none.</param>
/// <param name="Depth">How many landmarks contain it.</param>
public sealed record OutlineLandmark(Node Node, string Role, string Name, int Depth);

/// <summary>
/// The landmarks and headings of a page, found without building the whole accessibility tree. They are the lists
/// <see cref="NavigationLists.Landmarks"/> and <see cref="NavigationLists.Headings"/> would hold for the tree
/// <see cref="AccessibilityTree.Build"/> makes from the same document and styles: the same elements in the same order, with the same
/// roles, levels, depths, languages and names. The entries point at document elements instead of tree nodes, so a caller that only
/// needs to know which elements are landmarks or headings (to break a text into sections, say) does not pay for names, states,
/// relations, focus and diagnostics of every other node. Immutable and safe to read from any number of threads.
/// </summary>
public sealed class PageOutline
{
    internal PageOutline(IReadOnlyList<OutlineHeading> headings, IReadOnlyList<OutlineLandmark> landmarks, bool hasHeadingNames)
    {
        Headings = headings;
        Landmarks = landmarks;
        HasHeadingNames = hasHeadingNames;
    }

    /// <summary>Headings, <c>h1</c> to <c>h6</c> and <c>role=heading</c>, in tree order. Ignored and hidden ones are not listed.</summary>
    public IReadOnlyList<OutlineHeading> Headings { get; }

    /// <summary>Landmark regions in tree order; <c>region</c> and <c>form</c> appear only when they have a name, as in <see cref="NavigationLists.Landmarks"/>.</summary>
    public IReadOnlyList<OutlineLandmark> Landmarks { get; }

    /// <summary>True when the outline was made with heading names, so <see cref="OutlineHeading.Name"/> is the accessible name; false when names were left out and are empty.</summary>
    public bool HasHeadingNames { get; }

    /// <summary>
    /// Finds the landmarks and headings of a document under its styles. The work is the hidden-status, <c>aria-owns</c> and role passes
    /// of <see cref="AccessibilityTree.Build"/> plus the names of the landmarks (and of the headings, when asked); no tree is built. Time
    /// is linear in the document and no step recurses on its depth. The tree is made without rendered facts, so an outline equals the
    /// lists of a tree built without them. Names are computed within the same <see cref="AccessibilityOptions.MaxNameWork"/> limit as in
    /// a tree, which counts all the names a tree computes, so on a page that reaches the limit a name can differ.
    /// </summary>
    /// <param name="document">A parsed document.</param>
    /// <param name="styles">The cascade of the same document.</param>
    /// <param name="includeHeadingNames">True (the default) to compute each heading's accessible name; false to leave <see cref="OutlineHeading.Name"/> empty, which saves the name computation when only the headings' positions matter.</param>
    /// <param name="options">Limits; defaults when <see langword="null"/>. Its <see cref="AccessibilityOptions.Facts"/> must be null.</param>
    /// <param name="cancellationToken">Observed periodically.</param>
    /// <returns>The outline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> or <paramref name="styles"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="styles"/> belongs to another document, or <paramref name="options"/> carries rendered facts.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static PageOutline Create(Document document, DocumentStyles styles, bool includeHeadingNames = true, AccessibilityOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(styles);
        if (!ReferenceEquals(styles.Document, document))
        {
            throw new ArgumentException("The styles belong to another document.", nameof(styles));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (options?.Facts is not null)
        {
            throw new ArgumentException("An outline is made without rendered facts.", nameof(options));
        }

        return new TreeBuilder(document, styles, options ?? new AccessibilityOptions(), null, cancellationToken).RunOutline(includeHeadingNames);
    }
}
