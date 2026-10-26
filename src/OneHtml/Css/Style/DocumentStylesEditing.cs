using System;
using System.Collections.Generic;
using System.Threading;
using RecursiveAI.OneHtml.Editing;

namespace RecursiveAI.OneHtml.Css;

/// <summary>From the result of an edit to the styles of the edited document.</summary>
public static class DocumentStylesEditing
{
    /// <summary>
    /// The styles of the document an edit produced, recomputing only what the change reaches (<see cref="DocumentStyles.Restyle(Document, StyleChange, CancellationToken)"/>).
    /// The result equals <see cref="DocumentStyles.Create"/> on the new document with the same context and options;
    /// <see cref="RestyleResult.FullRecompute"/> says when the change could not be narrowed.
    /// </summary>
    /// <param name="styles">The styles of the document the edit was applied to (<see cref="ChangeSet.OldDocument"/>).</param>
    /// <param name="edit">A successful result of <c>Document.Apply</c>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The edit failed, or was applied to another snapshot than <paramref name="styles"/> was made for.</exception>
    public static RestyleResult Restyle(this DocumentStyles styles, EditResult edit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(edit);
        if (!edit.Succeeded || edit.Changes is null || edit.Document is null)
        {
            throw new ArgumentException("The edit did not succeed, so there is no new document.", nameof(edit));
        }

        var edits = new List<Edit>(edit.Outcomes.Count);
        foreach (EditOutcome o in edit.Outcomes)
        {
            edits.Add(o.Edit);
        }

        return Restyle(styles, edit.Changes, edits, [], cancellationToken);
    }

    /// <summary>
    /// The styles after a batch of document and stylesheet edits. A linked sheet the batch edited is served to the cascade
    /// from its new text, in place of what the resolver the styles were made with returns for the same URL. That URL must be
    /// the absolute URL the cascade asks for (<see cref="LinkedStylesheet.Url"/>); other sheets still come from the old resolver.
    /// A batch with no document (only linked or loose sheets) leaves the document as it is.
    /// </summary>
    /// <param name="styles">The styles of the document the batch was made for.</param>
    /// <param name="batch">A successful result of <see cref="EditBatch.Apply"/>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The batch failed, or was made for another document than <paramref name="styles"/>.</exception>
    public static RestyleResult Restyle(this DocumentStyles styles, BatchResult batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(batch);
        if (!batch.Succeeded)
        {
            throw new ArgumentException("The batch did not succeed, so there is no new document.", nameof(batch));
        }

        var edits = new List<Edit>();
        foreach (BatchOutcome o in batch.Outcomes)
        {
            if (o.Edit is not null)
            {
                edits.Add(o.Edit);
            }
        }

        return Restyle(styles, batch.Changes, edits, batch.Sheets, cancellationToken);
    }

    private static RestyleResult Restyle(DocumentStyles styles, ChangeSet? changes, List<Edit> edits, IReadOnlyList<SheetUpdate> sheets, CancellationToken ct)
    {
        if (changes is not null && !ReferenceEquals(changes.OldDocument, styles.Document))
        {
            throw new ArgumentException("The edit was applied to a different snapshot than the styles were made for.", nameof(styles));
        }

        // An edited linked sheet replaces the old bytes in the resolver, keyed by the URL the cascade asks for.
        Dictionary<string, string>? replaced = null;
        bool sheetsChanged = false;
        foreach (SheetUpdate u in sheets)
        {
            if (u.Changes.IsEmpty)
            {
                continue;
            }

            sheetsChanged = true;
            if (u.NewLinked is { } linked)
            {
                (replaced ??= new Dictionary<string, string>(StringComparer.Ordinal))[linked.Url] = linked.Source.Text;
            }
        }

        StyleSourceOptions options = styles.SourceOptions;
        if (replaced is not null)
        {
            StylesheetResolver? inner = options.Resolver;
            Dictionary<string, string> map = replaced;
            options = options.WithResolver(url => map.TryGetValue(url, out string? text) ? new StylesheetResource(text) : inner?.Invoke(url));
        }

        Document newDoc = styles.Document;
        StyleChange change;
        if (changes is null)
        {
            change = new StyleChange { SheetsChanged = sheetsChanged };
        }
        else
        {
            newDoc = changes.NewDocument;
            change = BuildChange(changes, edits, sheetsChanged);
        }

        return styles.RestyleWith(newDoc, change, options, ct);
    }

    // The mapping the cascade asks for: the old-to-new node map, the elements whose attributes (with the names, when the
    // edits say which) or children or text changed, and whether any style sheet's input changed. Added, removed and moved
    // nodes are found by the restyle itself.
    private static StyleChange BuildChange(ChangeSet changes, List<Edit> edits, bool sheetsChanged)
    {
        Document oldDoc = changes.OldDocument;
        Document newDoc = changes.NewDocument;

        // Attribute names per changed element, from the edits that target it.
        Dictionary<int, List<string>>? names = null;
        foreach (Edit e in edits)
        {
            string? name = e.Operation switch
            {
                EditOperation.SetAttribute or EditOperation.RemoveAttribute => e.Name,
                EditOperation.SetStyle or EditOperation.RemoveStyle => "style",
                _ => null,
            };
            if (name is null || !ReferenceEquals(e.Target.Document, oldDoc))
            {
                continue;
            }

            int n = changes.OldToNew[e.Target.Index];
            if (n >= 0 && newDoc.Arena.Ns[n] == (byte)ElementNamespace.Html)
            {
                names ??= [];
                if (!names.TryGetValue(n, out List<string>? list))
                {
                    names[n] = list = [];
                }

                list.Add(AsciiLower(name));
            }
        }

        var elements = new List<ElementChange>(changes.Changed.Count);
        foreach (NodeChange c in changes.Changed)
        {
            Node node = c.Node;
            if ((c.Changes & NodeChanges.Attributes) != 0 && node.IsElement)
            {
                IReadOnlyList<string>? list = names is not null && names.TryGetValue(node.Index, out List<string>? l) ? l : null;
                elements.Add(new ElementChange(node.Index, list));
            }

            if ((c.Changes & (NodeChanges.Data | NodeChanges.Children)) != 0)
            {
                Node target = node.IsElement ? node : node.Parent;
                if (target.IsElement)
                {
                    elements.Add(new ElementChange(target.Index, []));
                }
            }

            if (!sheetsChanged && AffectsSheets(node, c.Changes))
            {
                sheetsChanged = true;
            }
        }

        if (!sheetsChanged)
        {
            foreach (Node r in changes.Removed)
            {
                if (SubtreeAffectsSheets(r))
                {
                    sheetsChanged = true;
                    break;
                }
            }
        }

        if (!sheetsChanged)
        {
            foreach (Node a in changes.Added)
            {
                if (SubtreeAffectsSheets(a) || a.Parent.IsElement && IsStyle(a.Parent))
                {
                    sheetsChanged = true;
                    break;
                }
            }
        }

        return new StyleChange { Elements = elements, OldToNew = changes.OldToNew, SheetsChanged = sheetsChanged };
    }

    private static bool IsStyle(Node e) => e.LocalName is "style";

    // style, link and base decide which sheets exist and where they resolve from; the doctype decides quirks mode.
    private static bool IsSheetSource(Node n) =>
        n.Kind == NodeKind.DocumentType || n.IsElement && n.LocalName is "style" or "link" or "base";

    private static bool AffectsSheets(Node n, NodeChanges changes)
    {
        if (n.Kind == NodeKind.Text)
        {
            return (changes & NodeChanges.Data) != 0 && n.Parent.IsElement && IsStyle(n.Parent);
        }

        return IsSheetSource(n);
    }

    private static bool SubtreeAffectsSheets(Node root)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            if (IsSheetSource(n))
            {
                return true;
            }

            if (n.Kind == NodeKind.Text && n.Parent.IsElement && IsStyle(n.Parent))
            {
                return true;
            }

            if (n.IsElement && !n.TemplateContent.IsNull)
            {
                stack.Push(n.TemplateContent);
            }

            for (Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                stack.Push(c);
            }
        }

        return false;
    }

    private static string AsciiLower(string s)
    {
        int i = 0;
        while (i < s.Length && !(s[i] >= 'A' && s[i] <= 'Z'))
        {
            i++;
        }

        if (i == s.Length)
        {
            return s;
        }

        return string.Create(s.Length, s, static (span, src) =>
        {
            for (int k = 0; k < span.Length; k++)
            {
                char c = src[k];
                span[k] = c >= 'A' && c <= 'Z' ? (char)(c + 32) : c;
            }
        });
    }
}
