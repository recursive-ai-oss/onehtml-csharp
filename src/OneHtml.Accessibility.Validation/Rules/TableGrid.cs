using System;
using System.Collections.Generic;
using System.Globalization;
using RecursiveAI.OneHtml.Accessibility.Tree;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

/// <summary>
/// The grid of one table in the accessibility tree, built for the table rules: where each cell sits after row and column
/// spans (HTML 4.9.12 "Processing model"), and which header cells are assigned to it. A header is assigned to a cell by the
/// cell's <c>headers</c> attribute when it has one; otherwise a column header applies to the cells below it in the columns it
/// spans, and a row header to the cells to its right in the rows it spans. Which cells are headers comes from the computed roles.
/// </summary>
internal sealed class TableGrid
{
    public sealed record Cell(AxNode Node, int Row, int Column, int RowSpan, int ColumnSpan);

    /// <summary>The most header cells listed for one cell and direction; keeps the work linear on absurd tables.</summary>
    private const int MaxHeaders = 16;

    private readonly Dictionary<string, Cell> _byId = new(StringComparer.Ordinal);
    private Dictionary<int, List<Cell>>? _columnHeaders;
    private Dictionary<int, List<Cell>>? _rowHeaders;

    private TableGrid(AxNode table) => Table = table;

    public AxNode Table { get; }

    public int Rows { get; private set; }

    public int Columns { get; private set; }

    /// <summary>True when the table had more cells than the limit and the grid stops early.</summary>
    public bool Truncated { get; private set; }

    public List<Cell> Cells { get; } = [];

    public static bool IsTableRole(string role) => role is "table" or "grid" or "treegrid";

    public static bool IsCellRole(string role) => role is "cell" or "gridcell" or "columnheader" or "rowheader";

    public static bool IsHeaderRole(string role) => role is "columnheader" or "rowheader";

    /// <summary>The grids of every table in the tree whose role makes it a table, in document order.</summary>
    public static List<TableGrid> All(CheckContext context, int maxCells, System.Threading.CancellationToken ct)
    {
        var list = new List<TableGrid>();
        foreach (AxNode n in context.Tree.Nodes)
        {
            ct.ThrowIfCancellationRequested();
            if (n.Kind == AxNodeKind.Element && IsTableRole(n.Role))
            {
                list.Add(Build(n, maxCells));
            }
        }

        return list;
    }

    public static TableGrid Build(AxNode table, int maxCells)
    {
        var grid = new TableGrid(table);
        var rows = new List<AxNode>();
        var stack = new Stack<AxNode>();
        for (int i = table.Children.Count - 1; i >= 0; i--)
        {
            stack.Push(table.Children[i]);
        }

        // Rows belong to the nearest table: a nested table is not entered.
        while (stack.Count > 0)
        {
            AxNode n = stack.Pop();
            if (n.Kind == AxNodeKind.Element && n.Role == "row")
            {
                rows.Add(n);
                continue;
            }

            if (n.Kind == AxNodeKind.Element && IsTableRole(n.Role))
            {
                continue;
            }

            for (int i = n.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(n.Children[i]);
            }
        }

        var carry = new List<int>();
        int total = 0;
        for (int r = 0; r < rows.Count; r++)
        {
            int col = 0;
            foreach (AxNode cellNode in CellsOf(rows[r]))
            {
                if (++total > maxCells)
                {
                    grid.Truncated = true;
                    grid.Rows = rows.Count;
                    return grid;
                }

                while (col < carry.Count && carry[col] > 0)
                {
                    col++;
                }

                int cs = Span(cellNode, "colspan", "aria-colspan", 1, 1, 1000);
                // rowspan="0" extends the cell to the end of the table here (HTML ends it at the row group; a close approximation).
                int rs = Span(cellNode, "rowspan", "aria-rowspan", 1, 0, 65534);
                if (rs == 0)
                {
                    rs = rows.Count - r;
                }

                var cell = new Cell(cellNode, r, col, rs, cs);
                grid.Cells.Add(cell);
                if (cellNode.DomNode.GetAttribute("id") is { Length: > 0 } id)
                {
                    grid._byId.TryAdd(id, cell);
                }

                while (carry.Count < col + cs)
                {
                    carry.Add(0);
                }

                for (int k = 0; k < cs; k++)
                {
                    carry[col + k] = Math.Max(carry[col + k], rs);
                }

                col += cs;
                grid.Columns = Math.Max(grid.Columns, col);
            }

            for (int i = 0; i < carry.Count; i++)
            {
                if (carry[i] > 0)
                {
                    carry[i]--;
                }
            }
        }

        grid.Rows = rows.Count;
        return grid;
    }

    private static IEnumerable<AxNode> CellsOf(AxNode row)
    {
        var stack = new Stack<AxNode>();
        for (int i = row.Children.Count - 1; i >= 0; i--)
        {
            stack.Push(row.Children[i]);
        }

        while (stack.Count > 0)
        {
            AxNode n = stack.Pop();
            if (n.Kind == AxNodeKind.Element && IsCellRole(n.Role))
            {
                yield return n;
                continue;
            }

            if (n.Kind == AxNodeKind.Element && (n.Role == "row" || IsTableRole(n.Role)))
            {
                continue;
            }

            for (int i = n.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(n.Children[i]);
            }
        }
    }

    private static int Span(AxNode n, string html, string aria, int fallback, int min, int max)
    {
        string? v = n.DomNode.GetAttribute(html) ?? n.DomNode.GetAttribute(aria);
        if (v is not null && int.TryParse(v.AsSpan().Trim(" \t\n\f\r"), NumberStyles.None, CultureInfo.InvariantCulture, out int x))
        {
            return Math.Clamp(x, min, max);
        }

        return fallback;
    }

    /// <summary>The cell for an element's <c>id</c> in this table, or <see langword="null"/> when no cell of the table has it.</summary>
    public Cell? FindById(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The tokens of the <c>headers</c> attribute of a td or th, or <see langword="null"/> when it has none (the attribute means nothing on other elements).</summary>
    public static List<string>? HeadersAttribute(Cell cell) =>
        cell.Node.DomNode is { Namespace: ElementNamespace.Html, LocalName: "td" or "th" } dom && dom.GetAttribute("headers") is { } v ? AutofillTokens.Split(v) : null;

    /// <summary>The header cells assigned to a cell, in grid order.</summary>
    public List<Cell> HeadersOf(Cell cell)
    {
        var result = new List<Cell>();
        if (HeadersAttribute(cell) is { } tokens)
        {
            foreach (string t in tokens)
            {
                if (_byId.TryGetValue(t, out Cell? h) && h != cell && !result.Contains(h))
                {
                    result.Add(h);
                }
            }

            return result;
        }

        if (_columnHeaders is null)
        {
            _columnHeaders = [];
            _rowHeaders = [];
            foreach (Cell c in Cells)
            {
                if (c.Node.Role == "columnheader")
                {
                    for (int k = 0; k < c.ColumnSpan; k++)
                    {
                        Add(_columnHeaders, c.Column + k, c);
                    }
                }
                else if (c.Node.Role == "rowheader")
                {
                    for (int k = 0; k < Math.Min(c.RowSpan, Rows); k++)
                    {
                        Add(_rowHeaders, c.Row + k, c);
                    }
                }
            }
        }

        var seen = new HashSet<Cell>();
        int count = 0;
        for (int k = 0; k < cell.ColumnSpan && count < MaxHeaders; k++)
        {
            if (_columnHeaders.TryGetValue(cell.Column + k, out List<Cell>? l))
            {
                foreach (Cell h in l)
                {
                    if (h.Row >= cell.Row || count >= MaxHeaders)
                    {
                        break;
                    }

                    if (seen.Add(h))
                    {
                        result.Add(h);
                        count++;
                    }
                }
            }
        }

        count = 0;
        for (int k = 0; k < Math.Min(cell.RowSpan, Rows) && count < MaxHeaders; k++)
        {
            if (_rowHeaders!.TryGetValue(cell.Row + k, out List<Cell>? l))
            {
                foreach (Cell h in l)
                {
                    if (h.Column >= cell.Column || count >= MaxHeaders)
                    {
                        break;
                    }

                    if (seen.Add(h))
                    {
                        result.Add(h);
                        count++;
                    }
                }
            }
        }

        return result;
    }

    private static void Add(Dictionary<int, List<Cell>> map, int key, Cell c)
    {
        if (!map.TryGetValue(key, out List<Cell>? l))
        {
            map[key] = l = [];
        }

        l.Add(c);
    }
}
