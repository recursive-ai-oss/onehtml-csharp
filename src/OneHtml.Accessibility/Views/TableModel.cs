using System;
using System.Collections.Generic;

namespace RecursiveAI.OneHtml.Accessibility.Views;

/// <summary>
/// The grid of a table in the accessibility tree: where each cell sits after row and column spans, and which header cells
/// apply to it. Header cells are found by position: a column header covers the cells below it in the columns it spans, a row
/// header the cells to its right in the rows it spans (HTML section 4.9.12 "Processing model", without the <c>headers</c>
/// attribute and <c>scope</c> refinements, which the roles already carry: a <c>th</c> becomes a column or row header by
/// them in the role layer).
/// </summary>
internal sealed class TableModel
{
    public sealed record Cell(AxNode Node, int Row, int Column, int RowSpan, int ColumnSpan);

    private readonly Dictionary<AxNode, Cell> _byNode = [];

    private TableModel(AxNode table)
    {
        Table = table;
    }

    public AxNode Table { get; }

    public int Rows { get; private set; }

    public int Columns { get; private set; }

    public List<Cell> Cells { get; } = [];

    public Cell? Find(AxNode node) => _byNode.GetValueOrDefault(node);

    private static bool IsTable(string role) => role is "table" or "grid" or "treegrid";

    private static bool IsCell(string role) => role is "cell" or "gridcell" or "columnheader" or "rowheader";

    public static TableModel Build(AxNode table, int maxCells)
    {
        var model = new TableModel(table);
        var rows = new List<AxNode>();
        // Rows belong to the nearest table: do not look inside a nested one.
        var stack = new Stack<AxNode>();
        for (int i = table.Children.Count - 1; i >= 0; i--)
        {
            stack.Push(table.Children[i]);
        }

        while (stack.Count > 0)
        {
            AxNode n = stack.Pop();
            if (n.Kind == AxNodeKind.Element && n.Role == "row")
            {
                rows.Add(n);
                continue;
            }

            if (n.Kind == AxNodeKind.Element && IsTable(n.Role))
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
                    model.Rows = rows.Count;
                    return model;
                }

                while (col < carry.Count && carry[col] > 0)
                {
                    col++;
                }

                int cs = Span(cellNode, "colspan", "aria-colspan", 1, 1000);
                int rs = Span(cellNode, "rowspan", "aria-rowspan", 1, 65534);
                var cell = new Cell(cellNode, r, col, rs, cs);
                model.Cells.Add(cell);
                model._byNode[cellNode] = cell;
                while (carry.Count < col + cs)
                {
                    carry.Add(0);
                }

                for (int k = 0; k < cs; k++)
                {
                    carry[col + k] = Math.Max(carry[col + k], rs);
                }

                col += cs;
                model.Columns = Math.Max(model.Columns, col);
            }

            for (int i = 0; i < carry.Count; i++)
            {
                if (carry[i] > 0)
                {
                    carry[i]--;
                }
            }
        }

        model.Rows = rows.Count;
        return model;
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
            if (n.Kind == AxNodeKind.Element && IsCell(n.Role))
            {
                yield return n;
                continue;
            }

            if (n.Kind == AxNodeKind.Element && (n.Role == "row" || IsTable(n.Role)))
            {
                continue;
            }

            for (int i = n.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(n.Children[i]);
            }
        }
    }

    private static int Span(AxNode n, string html, string aria, int min, int max)
    {
        string? v = n.DomNode.GetAttribute(html) ?? n.DomNode.GetAttribute(aria);
        if (v is not null && int.TryParse(v.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int x))
        {
            return Math.Clamp(x, min, max);
        }

        return min;
    }

    /// <summary>The most header cells listed for one cell; a table with more has no useful answer and the bound keeps the work linear.</summary>
    public const int MaxHeaders = 16;

    private Dictionary<int, List<Cell>>? _columnHeaders;
    private Dictionary<int, List<Cell>>? _rowHeaders;

    /// <summary>The header cells that apply to a data cell, in grid order: column headers above it, then row headers to its left.</summary>
    public (List<AxNode> Columns, List<AxNode> Rows) HeadersOf(Cell cell)
    {
        var cols = new List<AxNode>();
        var rows = new List<AxNode>();
        if (cell.Node.Role is "columnheader" or "rowheader")
        {
            return (cols, rows);
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

        var seen = new HashSet<AxNode>();
        for (int k = 0; k < cell.ColumnSpan && cols.Count < MaxHeaders; k++)
        {
            if (_columnHeaders.TryGetValue(cell.Column + k, out List<Cell>? l))
            {
                foreach (Cell h in l)
                {
                    if (h.Row < cell.Row && seen.Add(h.Node) && cols.Count < MaxHeaders)
                    {
                        cols.Add(h.Node);
                    }
                }
            }
        }

        for (int k = 0; k < Math.Min(cell.RowSpan, Rows) && rows.Count < MaxHeaders; k++)
        {
            if (_rowHeaders!.TryGetValue(cell.Row + k, out List<Cell>? l))
            {
                foreach (Cell h in l)
                {
                    if (h.Column < cell.Column && seen.Add(h.Node) && rows.Count < MaxHeaders)
                    {
                        rows.Add(h.Node);
                    }
                }
            }
        }

        return (cols, rows);
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
