using System;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneHtml.Editing;

/// <summary>What happened to one edit of a batch: an HTML edit or a stylesheet edit.</summary>
public sealed class BatchOutcome
{
    internal BatchOutcome(int index, Edit? edit, SheetEdit? sheetEdit)
    {
        Index = index;
        Edit = edit;
        SheetEdit = sheetEdit;
    }

    /// <summary>The position in the batch: the document edits first, then the stylesheet edits.</summary>
    public int Index { get; }

    /// <summary>The document edit, or <see langword="null"/> for a stylesheet edit.</summary>
    public Edit? Edit { get; }

    /// <summary>The stylesheet edit, or <see langword="null"/> for a document edit.</summary>
    public SheetEdit? SheetEdit { get; }

    /// <summary>What happened.</summary>
    public EditStatus Status { get; internal set; } = EditStatus.NotAttempted;

    /// <summary>Why the edit was not applied; <see langword="null"/> when it was.</summary>
    public string? Reason { get; internal set; }

    /// <summary>0 when the edit is a local splice; otherwise how many enclosing constructs were re-serialized.</summary>
    public int FallbackLevel { get; internal set; }
}

/// <summary>The result of a batch for one stylesheet.</summary>
public sealed class SheetUpdate
{
    internal SheetUpdate(string name, Stylesheet oldSheet, Stylesheet newSheet, InlineStylesheet? inline, LinkedStylesheet? oldLinked, LinkedStylesheet? newLinked, StylesheetChangeSet changes)
    {
        Name = name;
        OldSheet = oldSheet;
        NewSheet = newSheet;
        OldInline = inline;
        OldLinked = oldLinked;
        NewLinked = newLinked;
        Changes = changes;
    }

    /// <summary><c>inline:n</c> for a style element, the URL for a linked sheet, otherwise the sheet's location or empty.</summary>
    public string Name { get; }

    /// <summary>The snapshot the edits were made for.</summary>
    public Stylesheet OldSheet { get; }

    /// <summary>The edited snapshot.</summary>
    public Stylesheet NewSheet { get; }

    /// <summary>The style element's sheet in the old document, for an embedded sheet.</summary>
    public InlineStylesheet? OldInline { get; }

    /// <summary>The linked sheet that was edited, if it was one.</summary>
    public LinkedStylesheet? OldLinked { get; }

    /// <summary>The edited linked sheet with its new bytes, if it was one.</summary>
    public LinkedStylesheet? NewLinked { get; }

    /// <summary>What changed in the sheet.</summary>
    public StylesheetChangeSet Changes { get; }
}

/// <summary>The result of <see cref="EditBatch.Apply"/>.</summary>
public sealed class BatchResult
{
    internal BatchResult(bool succeeded, string? failure, Document? document, ChangeSet? changes, IReadOnlyList<SheetUpdate> sheets, IReadOnlyList<BatchOutcome> outcomes, IReadOnlyList<FallbackInfo> fallbacks, int verifications)
    {
        Succeeded = succeeded;
        Failure = failure;
        Document = document;
        Changes = changes;
        Sheets = sheets;
        Outcomes = outcomes;
        Fallbacks = fallbacks;
        Verifications = verifications;
    }

    /// <summary>True when every edit applied.</summary>
    public bool Succeeded { get; }

    /// <summary>A sentence saying why the batch failed; <see langword="null"/> on success.</summary>
    public string? Failure { get; }

    /// <summary>The new document snapshot; <see langword="null"/> on failure or when the batch had no document.</summary>
    public Document? Document { get; }

    /// <summary>The document's change set; <see langword="null"/> on failure or when the batch had no document.</summary>
    public ChangeSet? Changes { get; }

    /// <summary>One entry per edited stylesheet, in the order the stylesheets first appear in the edit list.</summary>
    public IReadOnlyList<SheetUpdate> Sheets { get; }

    /// <summary>One entry per edit, document edits first.</summary>
    public IReadOnlyList<BatchOutcome> Outcomes { get; }

    /// <summary>The document's fallbacks; see <see cref="EditResult.Fallbacks"/>. Stylesheet fallbacks are in <see cref="StylesheetChangeSet.Ranges"/>.</summary>
    public IReadOnlyList<FallbackInfo> Fallbacks { get; }

    /// <summary>The number of re-parses made to verify the output.</summary>
    public int Verifications { get; }
}

/// <summary>
/// Document edits and stylesheet edits applied together: all of them apply, or none does. Stylesheet edits reach three
/// kinds of sheet. A sheet that is a <c>style</c> element of the document (from <see cref="InlineStylesheets.Of"/>) is
/// written into the document's source as a splice of the element's content. A sheet that belongs to a supplied
/// <see cref="LinkedStylesheet"/> gets new bytes in its own encoding. Any other <see cref="Stylesheet"/> is edited as
/// text only.
/// </summary>
public sealed class EditBatch
{
    /// <summary>Gathers the edits of one batch.</summary>
    /// <param name="document">The snapshot document edits and inline sheets belong to; <see langword="null"/> when there are none.</param>
    /// <param name="edits">The document edits, all targeting <paramref name="document"/>.</param>
    /// <param name="sheetEdits">The stylesheet edits.</param>
    /// <param name="linkedSheets">The linked sheets that stylesheet edits may target.</param>
    /// <exception cref="ArgumentException">There are document edits but no document, or an edit targets another snapshot.</exception>
    public EditBatch(Document? document, IEnumerable<Edit>? edits, IEnumerable<SheetEdit>? sheetEdits, IEnumerable<LinkedStylesheet>? linkedSheets = null)
    {
        Document = document;
        var e = new List<Edit>(edits ?? []);
        if (e.Count > 0 && document is null)
        {
            throw new ArgumentException("Document edits need a document.", nameof(document));
        }

        foreach (Edit x in e)
        {
            ArgumentNullException.ThrowIfNull(x);
            if (x.Target.IsNull || !ReferenceEquals(x.Target.Document, document))
            {
                throw new ArgumentException("An edit targets a node of a different snapshot: " + x, nameof(edits));
            }
        }

        var s = new List<SheetEdit>(sheetEdits ?? []);
        foreach (SheetEdit x in s)
        {
            ArgumentNullException.ThrowIfNull(x);
        }

        var l = new List<LinkedStylesheet>(linkedSheets ?? []);
        foreach (LinkedStylesheet x in l)
        {
            ArgumentNullException.ThrowIfNull(x);
        }

        Edits = e;
        SheetEdits = s;
        LinkedSheets = l;
    }

    /// <summary>The document, if any.</summary>
    public Document? Document { get; }

    /// <summary>The document edits.</summary>
    public IReadOnlyList<Edit> Edits { get; }

    /// <summary>The stylesheet edits.</summary>
    public IReadOnlyList<SheetEdit> SheetEdits { get; }

    /// <summary>The linked sheets stylesheet edits may target.</summary>
    public IReadOnlyList<LinkedStylesheet> LinkedSheets { get; }

    private sealed class Group
    {
        public Stylesheet Sheet = null!;
        public List<int> Edits = [];
        public InlineStylesheet? Inline;
        public LinkedStylesheet? Linked;
        public SheetPlan Plan = null!;
        public int RawEditIndex = -1;
    }

    /// <summary>Applies everything or nothing.</summary>
    /// <param name="options">Bounds; <see langword="null"/> means the defaults.</param>
    /// <param name="cancellationToken">Checked between edits and between re-parses.</param>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public BatchResult Apply(EditOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new EditOptions();
        int h = Edits.Count;
        var outcomes = new List<BatchOutcome>(h + SheetEdits.Count);
        foreach (Edit e in Edits)
        {
            outcomes.Add(new BatchOutcome(outcomes.Count, e, null));
        }

        foreach (SheetEdit e in SheetEdits)
        {
            outcomes.Add(new BatchOutcome(outcomes.Count, null, e));
        }

        if ((long)h + SheetEdits.Count > options.MaxEdits)
        {
            return Fail("the batch has " + (h + SheetEdits.Count) + " edits; the limit is " + options.MaxEdits, outcomes);
        }

        long inserted = 0;
        foreach (SheetEdit e in SheetEdits)
        {
            inserted += (e.Css?.Length ?? 0) + (e.Value?.Length ?? 0);
        }

        if (inserted > options.MaxInsertedLength)
        {
            return Fail("the batch inserts " + inserted + " characters; the limit is " + options.MaxInsertedLength, outcomes);
        }

        // Group the stylesheet edits by sheet, in order of first appearance.
        var groups = new List<Group>();
        for (int i = 0; i < SheetEdits.Count; i++)
        {
            Stylesheet sheet = SheetEdits[i].Sheet;
            Group? g = groups.Find(x => ReferenceEquals(x.Sheet, sheet));
            if (g is null)
            {
                g = new Group { Sheet = sheet };
                if (Document is not null)
                {
                    foreach (InlineStylesheet inl in InlineStylesheets.Of(Document))
                    {
                        if (ReferenceEquals(inl.Sheet, sheet))
                        {
                            g.Inline = inl;
                            break;
                        }
                    }
                }

                g.Linked = LinkedSheets.Count == 0 ? null : FindLinked(sheet);
                groups.Add(g);
            }

            g.Edits.Add(i);
        }

        foreach (Group g in groups)
        {
            var list = new List<SheetEdit>(g.Edits.Count);
            foreach (int i in g.Edits)
            {
                list.Add(SheetEdits[i]);
            }

            g.Plan = SheetEngine.Run(g.Sheet, list, options.MaxVerifications, cancellationToken);
            if (!g.Plan.Ok)
            {
                EditStatus status = g.Plan.Invalid ? EditStatus.Invalid : EditStatus.Failed;
                if (g.Plan.ErrorEdit >= 0)
                {
                    BatchOutcome o = outcomes[h + g.Edits[g.Plan.ErrorEdit]];
                    o.Status = status;
                    o.Reason = g.Plan.Error;
                }
                else
                {
                    foreach (int i in g.Edits)
                    {
                        outcomes[h + i].Status = status;
                        outcomes[h + i].Reason = g.Plan.Error;
                    }
                }

                return Fail("stylesheet edit failed: " + g.Plan.Error, outcomes);
            }
        }

        // The embedded sheets become edits of the style elements' text, in the same transaction as the document edits.
        var all = new List<Edit>(Edits);
        foreach (Group g in groups)
        {
            if (g.Inline is { } inl && g.Plan.Pieces.Count > 0)
            {
                g.RawEditIndex = all.Count;
                if (inl.TextNode >= 0)
                {
                    var parts = new List<TextSplice>(g.Plan.Pieces.Count);
                    foreach (SheetPiece p in g.Plan.Pieces)
                    {
                        parts.Add(new TextSplice(p.Start, p.End, p.Text));
                    }

                    all.Add(Edit.StyleTextSplices(new Node(Document!, inl.TextNode), g.Plan.NewText, parts));
                }
                else
                {
                    all.Add(Edit.ReplaceText(inl.Element, g.Plan.NewText));
                }
            }
        }

        EditResult? er = null;
        if (Document is not null)
        {
            er = EditEngine.Run(new EditTransaction(Document, all), options, cancellationToken);
            for (int i = 0; i < h; i++)
            {
                EditOutcome src = er.Outcomes[i];
                outcomes[i].Status = src.Status;
                outcomes[i].Reason = src.Reason;
                outcomes[i].FallbackLevel = src.FallbackLevel;
            }

            if (!er.Succeeded)
            {
                foreach (Group g in groups)
                {
                    if (g.RawEditIndex >= 0 && er.Outcomes[g.RawEditIndex].Status != EditStatus.NotAttempted && er.Outcomes[g.RawEditIndex].Status != EditStatus.Applied)
                    {
                        foreach (int i in g.Edits)
                        {
                            outcomes[h + i].Status = er.Outcomes[g.RawEditIndex].Status;
                            outcomes[h + i].Reason = "writing the style element failed: " + er.Outcomes[g.RawEditIndex].Reason;
                        }
                    }
                }

                return Fail(er.Failure ?? "the document edits failed", outcomes);
            }
        }

        var updates = new List<SheetUpdate>(groups.Count);
        foreach (Group g in groups)
        {
            SheetUpdate? u = Finish(g, er, out string? error);
            if (u is null)
            {
                foreach (int i in g.Edits)
                {
                    outcomes[h + i].Status = EditStatus.Failed;
                    outcomes[h + i].Reason = error;
                }

                return Fail("stylesheet edit failed: " + error, outcomes);
            }

            updates.Add(u);
            foreach (SheetPiece p in g.Plan.Pieces)
            {
                foreach (int ei in p.Edits)
                {
                    BatchOutcome o = outcomes[h + g.Edits[ei]];
                    o.FallbackLevel = Math.Max(o.FallbackLevel, p.Level);
                }
            }
        }

        foreach (BatchOutcome o in outcomes)
        {
            o.Status = EditStatus.Applied;
        }

        return new BatchResult(true, null, er?.Document, er?.Changes, updates, outcomes, er?.Fallbacks ?? [], er?.Verifications ?? 0);
    }

    private LinkedStylesheet? FindLinked(Stylesheet sheet)
    {
        foreach (LinkedStylesheet l in LinkedSheets)
        {
            if (ReferenceEquals(l.Sheet, sheet))
            {
                return l;
            }
        }

        return null;
    }

    private static SheetUpdate? Finish(Group g, EditResult? er, out string? error)
    {
        error = null;
        Stylesheet newSheet;
        LinkedStylesheet? newLinked = null;
        string name = g.Inline?.Name ?? g.Linked?.Url ?? g.Sheet.Location ?? string.Empty;
        if (g.Inline is { } inl)
        {
            Document nd = er!.Document!;
            Node ne = er.Changes!.MapToNew(inl.Element);
            if (ne.IsNull)
            {
                error = "the style element is no longer in the document";
                return null;
            }

            InlineStylesheet? found = null;
            foreach (InlineStylesheet x in InlineStylesheets.Of(nd))
            {
                if (x.Element == ne)
                {
                    found = x;
                    break;
                }
            }

            if (found is null || (g.Plan.Pieces.Count > 0 && !string.Equals(found.Sheet.Text, g.Plan.NewText, StringComparison.Ordinal)))
            {
                error = "the style element in the output does not hold the edited stylesheet";
                return null;
            }

            newSheet = found.Sheet;
        }
        else if (g.Linked is { } linked)
        {
            if (g.Plan.Pieces.Count == 0)
            {
                newLinked = linked;
                newSheet = linked.Sheet;
            }
            else
            {
                newLinked = linked.WithPieces(g.Plan.Pieces, g.Plan.NewText, out error);
                if (newLinked is null)
                {
                    return null;
                }

                newSheet = newLinked.Sheet;
            }
        }
        else
        {
            newSheet = g.Plan.Pieces.Count == 0 ? g.Sheet : Stylesheet.Parse(g.Plan.NewText, g.Sheet.Location);
        }

        StylesheetChangeSet changes = StylesheetChangeSet.Build(g.Sheet, newSheet, g.Plan.Pieces);
        return new SheetUpdate(name, g.Sheet, newSheet, g.Inline, g.Linked, newLinked, changes);
    }

    private static BatchResult Fail(string reason, List<BatchOutcome> outcomes) =>
        new(false, reason, null, null, [], outcomes, [], 0);
}

/// <summary>Editing a stylesheet on its own.</summary>
public static class StylesheetEditing
{
    /// <summary>
    /// Applies stylesheet edits to a sheet as text; the result's single <see cref="BatchResult.Sheets"/> entry holds the new snapshot.
    /// </summary>
    /// <param name="sheet">The snapshot the edits were made for.</param>
    /// <param name="edits">The edits; each must target <paramref name="sheet"/>.</param>
    /// <param name="options">Bounds; <see langword="null"/> means the defaults.</param>
    /// <param name="cancellationToken">Checked between re-parses.</param>
    public static BatchResult Apply(this Stylesheet sheet, IEnumerable<SheetEdit> edits, EditOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(edits);
        return new EditBatch(null, null, edits).Apply(options, cancellationToken);
    }
}
