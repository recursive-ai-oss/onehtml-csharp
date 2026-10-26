using System;
using System.Collections.Generic;
using System.Text;
using RecursiveAI.OneHtml.Accessibility.Tree;

namespace RecursiveAI.OneHtml.Accessibility.Validation.Rules;

/// <summary>Shared table helpers.</summary>
internal static class TableSupport
{
    /// <summary>The most cells of one table the rules look at; a table past it is skipped.</summary>
    public const int MaxCells = 200_000;

    public static string Collapse(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                space = sb.Length > 0;
            }
            else
            {
                if (space)
                {
                    sb.Append(' ');
                    space = false;
                }

                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    /// <summary>True when the cell has text, or contents that stand for something (an image, a control, a graphic).</summary>
    public static bool HasContent(Node cell)
    {
        foreach (Node d in cell.Descendants)
        {
            if (d.Kind == NodeKind.Text)
            {
                foreach (char ch in d.Data)
                {
                    if (!char.IsWhiteSpace(ch))
                    {
                        return true;
                    }
                }
            }
            else if (d.IsElement && d.LocalName is "img" or "svg" or "input" or "button" or "select" or "textarea" or "canvas" or "video" or "audio" or "object" or "iframe" or "meter" or "progress" or "math")
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>axe <c>td-headers-attr</c>, ACT a25f45.</summary>
internal sealed class TdHeadersAttrRule : AriaRule
{
    public TdHeadersAttrRule()
        : base("td-headers-attr",
            "Every id listed in a cell's headers attribute must belong to another cell of the same table.",
            "Make each id in the headers attribute the id of a header or data cell in the same table, and do not list the cell's own id.",
            null,
            "The right header cells depend on what the table says; the markup does not show them.",
            actIds: ["a25f45"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        // ACT a25f45: applies to a headers attribute on a cell of a rendered table; every token must be the id of a cell of the same table,
        // and none may be the cell's own id (HTML 4.9.12.1 step 3 ignores both).
        foreach (TableGrid grid in TableGrid.All(context, TableSupport.MaxCells, reporter.CancellationToken))
        {
            if (!context.IsExposed(grid.Table.DomNode))
            {
                continue;
            }

            foreach (TableGrid.Cell cell in grid.Cells)
            {
                if (TableGrid.HeadersAttribute(cell) is not { } tokens)
                {
                    continue;
                }

                string? own = cell.Node.DomNode.GetAttribute("id");
                var findings = new List<Finding>();
                foreach (string t in tokens)
                {
                    if (own is not null && t == own)
                    {
                        findings.Add(new Finding("headers", t, "refers to the cell itself"));
                    }
                    else if (grid.FindById(t) is null)
                    {
                        findings.Add(new Finding("headers", t, "is not the id of a cell in this table"));
                    }
                }

                if (findings.Count == 0)
                {
                    reporter.Passed(cell.Node.DomNode);
                }
                else
                {
                    reporter.Failed(cell.Node.DomNode, "The headers attribute lists ids that are not other cells of the same table: " + string.Join(", ", findings.ConvertAll(f => f.Value)) + ".", findings);
                }
            }
        }
    }
}

/// <summary>axe <c>th-has-data-cells</c>, ACT d0f69e.</summary>
internal sealed class ThHasDataCellsRule : AriaRule
{
    public ThHasDataCellsRule()
        : base("th-has-data-cells",
            "Every header cell of a table must have at least one cell that it is the header of.",
            "Remove the header cell or the markup that cuts it off from its cells, or add the cells it describes.",
            null,
            "Whether a header is stray or its cells are missing is a content decision.",
            actIds: ["d0f69e"])
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        // ACT d0f69e: each rendered row or column header must be assigned to at least one cell (data or header) of its table.
        foreach (TableGrid grid in TableGrid.All(context, TableSupport.MaxCells, reporter.CancellationToken))
        {
            if (grid.Truncated || !context.IsExposed(grid.Table.DomNode))
            {
                continue;
            }

            var assigned = new HashSet<TableGrid.Cell>();
            int n = 0;
            foreach (TableGrid.Cell cell in grid.Cells)
            {
                if ((++n & 1023) == 0)
                {
                    reporter.CancellationToken.ThrowIfCancellationRequested();
                }

                foreach (TableGrid.Cell h in grid.HeadersOf(cell))
                {
                    assigned.Add(h);
                }
            }

            foreach (TableGrid.Cell cell in grid.Cells)
            {
                if (!TableGrid.IsHeaderRole(cell.Node.Role) || !context.IsExposed(cell.Node.DomNode))
                {
                    continue;
                }

                if (assigned.Contains(cell))
                {
                    reporter.Passed(cell.Node.DomNode);
                }
                else
                {
                    reporter.Failed(cell.Node.DomNode, "This header cell is not the header of any cell in its table.");
                }
            }
        }
    }
}

/// <summary>axe <c>td-has-header</c>: data cells of a large table need a header.</summary>
internal sealed class TdHasHeaderRule : AriaRule
{
    public TdHasHeaderRule()
        : base("td-has-header",
            "Each cell with content in a data table of at least three rows and three columns must have a header cell.",
            "Mark the heading cells with th (and scope where needed), or give the cell a headers attribute that lists its header cells.",
            null,
            "Which cells are the headers is a content decision.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        // Tables of 3x3 cells or more are taken to be data tables whose cells must be readable without seeing their position.
        // The cells checked are td elements that hold content; a header is any cell assigned by headers or by position.
        foreach (TableGrid grid in TableGrid.All(context, TableSupport.MaxCells, reporter.CancellationToken))
        {
            if (grid.Truncated || grid.Table.Role != "table" || grid.Rows < 3 || grid.Columns < 3 || !context.IsExposed(grid.Table.DomNode))
            {
                continue;
            }

            int n = 0;
            foreach (TableGrid.Cell cell in grid.Cells)
            {
                if ((++n & 1023) == 0)
                {
                    reporter.CancellationToken.ThrowIfCancellationRequested();
                }

                Node dom = cell.Node.DomNode;
                if (cell.Node.Role != "cell" || dom.Namespace != ElementNamespace.Html || dom.LocalName != "td"
                    || !context.IsExposed(dom) || !TableSupport.HasContent(dom))
                {
                    continue;
                }

                if (grid.HeadersOf(cell).Count > 0)
                {
                    reporter.Passed(dom);
                }
                else
                {
                    reporter.Failed(dom, "This cell has no header cell in a table of " + grid.Rows + " rows and " + grid.Columns + " columns.");
                }
            }
        }
    }
}

/// <summary>axe <c>table-fake-caption</c>: a first row that is a caption in disguise.</summary>
internal sealed class TableFakeCaptionRule : AriaRule
{
    public TableFakeCaptionRule()
        : base("table-fake-caption",
            "A data cell that spans the whole first row to title the table should be a caption element.",
            "Move the text into a caption element as the first child of the table and remove the spanning cell.",
            null,
            "Moving the text changes the table's structure and styling; whether the cell is a title is a judgement.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        // A table of at least two rows and two columns whose first row holds one td with text, spanning every column, and which has
        // no caption element: the cell is acting as a caption.
        foreach (TableGrid grid in TableGrid.All(context, TableSupport.MaxCells, reporter.CancellationToken))
        {
            if (grid.Truncated || grid.Table.Role != "table" || grid.Rows < 2 || grid.Columns < 2 || !context.IsExposed(grid.Table.DomNode))
            {
                continue;
            }

            Node table = grid.Table.DomNode;
            if (table.Namespace != ElementNamespace.Html || table.LocalName != "table")
            {
                continue;
            }

            TableGrid.Cell? first = null;
            int inFirstRow = 0;
            foreach (TableGrid.Cell cell in grid.Cells)
            {
                if (cell.Row != 0)
                {
                    break;
                }

                inFirstRow++;
                first = cell;
            }

            bool hasCaption = false;
            for (Node ch = table.FirstChild; !ch.IsNull; ch = ch.NextSibling)
            {
                if (ch.IsElement && ch.LocalName == "caption")
                {
                    hasCaption = true;
                    break;
                }
            }

            bool fake = !hasCaption && inFirstRow == 1 && first is { } f && f.ColumnSpan >= grid.Columns && f.Node.DomNode.LocalName == "td"
                && f.Node.Role == "cell" && TableSupport.Collapse(f.Node.DomNode.TextContent ?? string.Empty).Length > 0;
            if (fake)
            {
                reporter.Failed(first!.Node.DomNode, "This cell spans the whole first row and holds text, as a caption would.");
            }
            else
            {
                reporter.Passed(table);
            }
        }
    }
}

/// <summary>axe <c>table-duplicate-name</c>: the caption must not repeat the summary.</summary>
internal sealed class TableDuplicateNameRule : AriaRule
{
    public TableDuplicateNameRule()
        : base("table-duplicate-name",
            "A table's caption should not repeat the text of its summary attribute.",
            "Make the caption name the table and use summary only for extra detail, or remove the obsolete summary attribute.",
            null,
            "Either the caption or the summary can be the one to drop or reword, which is a content decision.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        foreach (Node el in Elements(context))
        {
            if (el.Namespace != ElementNamespace.Html || el.LocalName != "table" || el.GetAttribute("summary") is not { } summary)
            {
                continue;
            }

            string s = TableSupport.Collapse(summary);
            Node caption = default;
            for (Node ch = el.FirstChild; !ch.IsNull; ch = ch.NextSibling)
            {
                if (ch.IsElement && ch.LocalName == "caption")
                {
                    caption = ch;
                    break;
                }
            }

            if (s.Length == 0 || caption.IsNull)
            {
                continue;
            }

            string c = TableSupport.Collapse(caption.TextContent ?? string.Empty);
            if (c.Length == 0)
            {
                continue;
            }

            if (string.Equals(s, c, StringComparison.OrdinalIgnoreCase))
            {
                reporter.Failed(el, "The caption and the summary attribute say the same thing.", [new Finding("summary", summary, "same text as the caption")]);
            }
            else
            {
                reporter.Passed(el);
            }
        }
    }
}

/// <summary>axe <c>empty-table-header</c>: a header cell with nothing to read.</summary>
internal sealed class EmptyTableHeaderRule : AriaRule
{
    public EmptyTableHeaderRule()
        : base("empty-table-header",
            "A table header cell should have text a screen reader can read.",
            "Put text in the header cell, or make it a data cell if it does not head anything.",
            null,
            "What the header should say is a content decision.")
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        // Row and column headers (th or an explicit role) whose accessible name is empty. An empty corner cell can be intentional,
        // so a finding is left to a person rather than failed.
        int counter = 0;
        foreach (AxNode n in context.Tree.Nodes)
        {
            if ((++counter & 1023) == 0)
            {
                reporter.CancellationToken.ThrowIfCancellationRequested();
            }

            if (n.Kind != AxNodeKind.Element || !TableGrid.IsHeaderRole(n.Role) || !context.IsExposed(n.DomNode))
            {
                continue;
            }

            if (TableSupport.Collapse(n.Name.Value).Length == 0)
            {
                reporter.NeedsReview(n.DomNode, "This header cell has no text, so it gives its column or row no name.");
            }
            else
            {
                reporter.Passed(n.DomNode);
            }
        }
    }
}

/// <summary>axe <c>scope-attr-valid</c>.</summary>
internal sealed class ScopeAttrValidRule : AriaRule
{
    public ScopeAttrValidRule()
        : base("scope-attr-valid",
            "The scope attribute belongs on th elements and must be col, row, colgroup or rowgroup.",
            "Use scope only on th elements, with the value col, row, colgroup or rowgroup.",
            "Remove an invalid scope value from a th: a browser already treats it as no scope, so removing it changes nothing but the markup.",
            null)
    {
    }

    public override void Evaluate(CheckContext context, RuleReporter reporter)
    {
        // HTML 4.9.10 "The th element": scope is an enumerated attribute (row, col, rowgroup, colgroup) allowed on th only; a missing or
        // invalid value is the auto state. The suggestion is only for an invalid value on a th, since there removal gives the same state.
        foreach (Node el in Elements(context))
        {
            if (el.Namespace != ElementNamespace.Html || el.GetAttribute("scope") is not { } scope)
            {
                continue;
            }

            bool valueOk = scope.Equals("row", StringComparison.OrdinalIgnoreCase) || scope.Equals("col", StringComparison.OrdinalIgnoreCase)
                || scope.Equals("rowgroup", StringComparison.OrdinalIgnoreCase) || scope.Equals("colgroup", StringComparison.OrdinalIgnoreCase);
            bool isTh = el.LocalName == "th";
            if (valueOk && isTh)
            {
                reporter.Passed(el);
                continue;
            }

            if (!valueOk)
            {
                IReadOnlyList<SuggestedEdit>? fix = isTh ? [RemoveAttribute(reporter, el, "scope")] : null;
                reporter.Failed(el, "\"" + scope + "\" is not a valid scope value.", [new Finding("scope", scope, "not row, col, rowgroup or colgroup")], fix);
            }
            else
            {
                reporter.Failed(el, "The scope attribute is only meaningful on a th element.", [new Finding("scope", scope, "on " + el.LocalName)]);
            }
        }
    }
}
