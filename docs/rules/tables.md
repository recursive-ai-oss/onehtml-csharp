# Table rules

A rule area of the Validation package: the axe-core 4.14.0 table rules and the two ACT table rules. Ids, tags and impacts
come from the axe-core rule table as facts; every description and help text is our own. How to run them is in
[checker.md](checker.md), the result shape in [results.md](results.md).

All rules here are static. They read the accessibility tree: a table is any node whose computed role is `table`, `grid` or
`treegrid`, so a layout table that the tree gives no table role is not looked at, and `<div role="table">` is.

## The rules

| Rule | Sources | Criteria | Impact | Suggested edit |
|---|---|---|---|---|
| empty-table-header | axe | best practice | minor | none |
| scope-attr-valid | axe | best practice | moderate | remove an invalid `scope` value from a `th` |
| table-duplicate-name | axe | best practice | minor | none |
| table-fake-caption | axe | 1.3.1 | serious | none |
| td-has-header | axe | 1.3.1 | critical | none |
| td-headers-attr | axe, ACT a25f45 | 1.3.1 | serious | none |
| th-has-data-cells | axe, ACT d0f69e | 1.3.1 | serious | none |

`table-fake-caption` and `td-has-header` are in axe-core's experimental group. They are implemented from the published
rule descriptions only, so their details below are ours.

## The grid

The rules share one grid of cells per table (`TableGrid`):

- Rows are the `row` nodes under the table, not looking inside a nested table. Cells are the `cell`, `gridcell`,
  `columnheader` and `rowheader` nodes of a row.
- Cells are placed with `colspan` and `rowspan` (`aria-colspan` and `aria-rowspan` for ARIA tables), as the HTML Standard's
  table processing model does. `colspan` is clamped to 1..1000 and `rowspan` to 0..65534; `rowspan="0"` reaches the
  end of the table here (the Standard ends it at the row group). A table with more than 200,000 cells is skipped.
- A header cell is a cell whose computed role is `columnheader` or `rowheader`, so `scope`, position and explicit roles
  are already resolved by the role layer.
- **Headers assigned to a cell.** When a `td` or `th` has a `headers` attribute, the cells of the table whose ids it lists
  are its headers and nothing else is (ids that are not cells of this table, and the cell's own id, are ignored, as step 3
  of the Standard's header assignment says). Otherwise every column header above it in the columns it spans, and every row
  header to its left in the rows it spans, is assigned to it. At most 16 headers are listed per direction; this keeps the
  work linear on hostile tables. `headers` means nothing on elements other than `td` and `th`.

## What each rule checks

- **td-has-header.** In a table of three or more rows and three or more columns (our reading of axe's "larger than 3 by 3")
  with the role `table`, each `td` that holds text, an image, a control or another object must have at least one header
  assigned. Empty cells are skipped. *Fix:* none; which cells head the data is content.
- **td-headers-attr** (ACT a25f45). For each `td`/`th` with a `headers` attribute in a rendered table, every token must be
  the id of a cell of the same table and none may be the cell's own id. Any other token fails and is listed in the
  finding. *Fix:* none; the right header cells are a content decision.
- **th-has-data-cells** (ACT d0f69e). Each rendered row or column header must be assigned to at least one cell of its
  table, data or header. A header past the last data row, a header whose column has only cells with a `headers`
  attribute that name others, and a header with nothing under or beside it fail. *Fix:* none.
- **table-fake-caption.** In a `table` element with at least two rows and two columns and no `caption`, the first row has
  exactly one cell, a `td` with text, that spans every column: it is acting as the caption. The cell fails. A table
  without such a cell passes. *Fix:* none; moving the text into `<caption>` changes structure and styling, and whether
  the cell is a title is a judgement.
- **table-duplicate-name.** A `table` with both a non-empty `summary` attribute and a non-empty `caption` fails when the two
  texts are equal after collapsing white space and ignoring ASCII case. *Fix:* none; either could be the one to reword.
- **empty-table-header.** Each exposed `columnheader` or `rowheader` node with an empty accessible name is *needs review*
  (an empty corner cell can be deliberate); with a name it passes. *Fix:* none.
- **scope-attr-valid.** `scope` is allowed on `th` only and takes `row`, `col`, `rowgroup` or `colgroup`, ASCII
  case-insensitively (HTML Standard, the `th` element). An element with an invalid value fails; a valid value on an element
  that is not a `th` fails too. Applies to every HTML element, exposed or not.

## Fix decisions

Only `scope-attr-valid` suggests an edit, and only for an invalid *value* on a `th`: the Standard treats an invalid value
as the auto state, the same as no attribute, so removing the attribute changes no behaviour and is the one fix that needs
no judgement. A valid `scope` on a `td` gets no suggestion: it may be a leftover that an older assistive technology
reads, and the right change (make it a `th`, or drop the attribute) depends on the table.

## Differences from axe-core and from ACT

- **th-has-data-cells.** axe-core's table lists this rule as needs-review only; ACT d0f69e says *failed* for a header with
  no assigned cell, and ACT wins, so this rule reports `failed`. In the axe-compatible export a failed result is a
  violation where axe would report an incomplete one.
- **td-headers-attr.** ACT's expectation applies to `headers` on a "cell"; we apply it to `td` and `th` only, since the
  attribute is not defined on `div role=cell`. ACT's test case for that (headers on `div role="cell"`) is inapplicable.
- **td-has-header and table-fake-caption** apply only to a table that looks like a data table by its markup: a `th`, a
  `caption`, a `scope` or `headers` attribute, or a `summary` attribute (axe applies neither rule to a plain grid of `td`
  cells; measured on 14 probe tables). They still also require the table's role to be `table`. Axe reports the table where
  we report its cells; the comparison treats a container and its contents as one finding.
- **table-fake-caption** is reported only for a `td` first row cell. Axe also reports a `th` that spans the first row and a
  table that has a real `caption` and a spanning first row; we keep those as passes (a `th` title row and a captioned table
  are not a caption in disguise).
- **td-has-header** counts an empty `th` as a header; axe does not (a header row whose first cell is empty is a violation
  there).
- **td-headers-attr** accepts the id of any cell of the table (ACT a25f45); axe requires the ids to be `th` cells and
  reports a `headers` that points at a `td`. The HTML standard also asks for `th`; ACT wins here.
- **th-has-data-cells** fails a header with no cells (ACT d0f69e); axe reports a table with only a header as incomplete.
- **empty-table-header** is needs-review for an empty `th` (it may be a deliberate corner cell); axe reports a violation,
  also for a `th` with only an `aria-label`.
- **ACT cases not judged:** a25f45 inapplicable example 3 (a table moved off-screen with `position: absolute` and
  negative offsets) needs layout to see that it is not visible; it is listed in `testdata/act/tables-forms-language-expectations.tsv`.
