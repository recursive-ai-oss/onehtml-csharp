using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using RecursiveAI.OneHtml.Accessibility.Views;

namespace RecursiveAI.OneHtml.Accessibility;

/// <summary>A heading in <see cref="NavigationLists.Headings"/>.</summary>
/// <param name="Node">The heading's tree node.</param>
/// <param name="Level">Its level, 1 to 6 for <c>h1</c> to <c>h6</c>; the <c>aria-level</c> for other headings (2 when none is given, as WAI-ARIA 1.2 section 6.6.3 says).</param>
/// <param name="Name">Its accessible name.</param>
/// <param name="Lang">The language in effect, or null.</param>
public sealed record HeadingEntry(AxNode Node, int Level, string Name, string? Lang);

/// <summary>A landmark in <see cref="NavigationLists.Landmarks"/>.</summary>
/// <param name="Node">The landmark's tree node.</param>
/// <param name="Role">Its role: <c>banner</c>, <c>complementary</c>, <c>contentinfo</c>, <c>form</c>, <c>main</c>, <c>navigation</c>, <c>region</c> or <c>search</c>.</param>
/// <param name="Name">Its accessible name; empty when it has none.</param>
/// <param name="Depth">How many landmarks contain it.</param>
public sealed record LandmarkEntry(AxNode Node, string Role, string Name, int Depth);

/// <summary>A link in <see cref="NavigationLists.Links"/>.</summary>
/// <param name="Node">The link's tree node.</param>
/// <param name="Name">Its accessible name; empty when it has none.</param>
/// <param name="Target">The <c>href</c> as written, or null when the link has none (a placeholder link).</param>
/// <param name="IsSamePage">True when the target is a fragment of the same page (starts with <c>#</c>).</param>
public sealed record LinkEntry(AxNode Node, string Name, string? Target, bool IsSamePage);

/// <summary>A form control in <see cref="NavigationLists.FormFields"/>.</summary>
/// <param name="Node">The control's tree node.</param>
/// <param name="Role">Its role.</param>
/// <param name="Label">Its accessible name; empty when it has none.</param>
/// <param name="Required">True when the control is required.</param>
/// <param name="Invalid">True when the control is marked invalid.</param>
/// <param name="Disabled">True when the control is disabled.</param>
/// <param name="Value">Its current value as text, when it has one.</param>
public sealed record FormFieldEntry(AxNode Node, string Role, string Label, bool Required, bool Invalid, bool Disabled, string? Value);

/// <summary>A data table in <see cref="NavigationLists.Tables"/>.</summary>
/// <param name="Node">The table's tree node.</param>
/// <param name="Caption">Its accessible name (from <c>caption</c>, <c>aria-label</c> or <c>aria-labelledby</c>); empty when it has none.</param>
/// <param name="Rows">The number of rows.</param>
/// <param name="Columns">The number of columns after column spans.</param>
/// <param name="ColumnHeaders">The names of the column header cells, in grid order.</param>
/// <param name="RowHeaders">The names of the row header cells, in grid order.</param>
public sealed record TableEntry(AxNode Node, string Caption, int Rows, int Columns, IReadOnlyList<string> ColumnHeaders, IReadOnlyList<string> RowHeaders);

/// <summary>
/// The lists a screen reader offers for jumping around a page, taken from the tree: headings, landmarks, links, form fields
/// and tables, each in tree order and each mapping back to its tree node and DOM node. Nodes the tree ignores or hides are not
/// listed. Approximates a reader's element lists; see <c>docs/reading-view.md</c>.
/// </summary>
public sealed class NavigationLists
{
    private static readonly HashSet<string> FieldRoles = new(StringComparer.Ordinal)
    {
        "textbox", "searchbox", "combobox", "listbox", "checkbox", "radio", "switch", "slider", "spinbutton",
    };

    private static readonly HashSet<string> LandmarkRoles = new(StringComparer.Ordinal)
    {
        "banner", "complementary", "contentinfo", "form", "main", "navigation", "region", "search",
    };

    private NavigationLists(IReadOnlyList<HeadingEntry> headings, IReadOnlyList<LandmarkEntry> landmarks, IReadOnlyList<LinkEntry> links, IReadOnlyList<FormFieldEntry> fields, IReadOnlyList<TableEntry> tables)
    {
        Headings = headings;
        Landmarks = landmarks;
        Links = links;
        FormFields = fields;
        Tables = tables;
    }

    /// <summary>Headings, <c>h1</c> to <c>h6</c> and <c>role=heading</c>.</summary>
    public IReadOnlyList<HeadingEntry> Headings { get; }

    /// <summary>Landmark regions. <c>region</c> and <c>form</c> appear only when they have a name, because the tree gives them those roles only then.</summary>
    public IReadOnlyList<LandmarkEntry> Landmarks { get; }

    /// <summary>Links: nodes with the role <c>link</c>.</summary>
    public IReadOnlyList<LinkEntry> Links { get; }

    /// <summary>Text fields, search fields, combo boxes, list boxes, checkboxes, radio buttons, switches, sliders and spin buttons. Buttons are not form fields here.</summary>
    public IReadOnlyList<FormFieldEntry> FormFields { get; }

    /// <summary>Tables that present data (role <c>table</c>, <c>grid</c> or <c>treegrid</c>); layout tables, whose role is <c>none</c>, are not listed.</summary>
    public IReadOnlyList<TableEntry> Tables { get; }

    /// <summary>Makes the lists of a tree.</summary>
    /// <param name="tree">The tree.</param>
    /// <param name="cancellationToken">Observed periodically.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static NavigationLists Create(AccessibilityTree tree, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var headings = new List<HeadingEntry>();
        var landmarks = new List<LandmarkEntry>();
        var links = new List<LinkEntry>();
        var fields = new List<FormFieldEntry>();
        var tables = new List<TableEntry>();
        var stack = new Stack<(AxNode Node, int Depth)>();
        stack.Push((tree.Root, 0));
        int steps = 0;
        while (stack.Count > 0)
        {
            if ((++steps & 255) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            (AxNode n, int depth) = stack.Pop();
            int childDepth = depth;
            if (n.Kind == AxNodeKind.Element && !n.IsIgnored)
            {
                string role = n.Role;
                if (role == "heading")
                {
                    headings.Add(new HeadingEntry(n, n.States.Level ?? 2, n.Name.Value, Lang.Of(n.DomNode)));
                }
                else if (role == "link")
                {
                    string? href = n.States.Url;
                    links.Add(new LinkEntry(n, n.Name.Value, href, href is not null && href.StartsWith('#')));
                }
                else if (FieldRoles.Contains(role))
                {
                    AxStates s = n.States;
                    fields.Add(new FormFieldEntry(n, role, n.Name.Value, s.Required, s.Invalid is { Length: > 0 } iv && iv != "false", s.Disabled,
                        NonEmpty(s.ValueText ?? s.Value ?? (s.ValueNow is { } d ? d.ToString("R", CultureInfo.InvariantCulture) : null))));
                }
                else if (role is "table" or "grid" or "treegrid")
                {
                    tables.Add(TableOf(n));
                }

                if (LandmarkRoles.Contains(role))
                {
                    landmarks.Add(new LandmarkEntry(n, role, n.Name.Value, depth));
                    childDepth = depth + 1;
                }
            }

            for (int i = n.Children.Count - 1; i >= 0; i--)
            {
                stack.Push((n.Children[i], childDepth));
            }
        }

        return new NavigationLists(headings, landmarks, links, fields, tables);
    }

    private static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static TableEntry TableOf(AxNode table)
    {
        TableModel m = TableModel.Build(table, 100_000);
        var cols = new List<string>();
        var rows = new List<string>();
        foreach (TableModel.Cell c in m.Cells)
        {
            if (c.Node.Role == "columnheader")
            {
                cols.Add(c.Node.Name.Value);
            }
            else if (c.Node.Role == "rowheader")
            {
                rows.Add(c.Node.Name.Value);
            }
        }

        return new TableEntry(table, table.Name.Value, m.Rows, m.Columns, cols, rows);
    }
}
