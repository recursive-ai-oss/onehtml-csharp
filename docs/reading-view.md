# Reading view and navigation lists

`ReadingView.Create(tree)` and `NavigationLists.Create(tree)` (also `tree.CreateReadingView()` and
`tree.CreateNavigationLists()`) in `RecursiveAI.OneHtml.Accessibility` derive two things from an accessibility tree: the page
as one linear sequence of spoken-style steps, and the lists a reader offers for jumping around it.

**This is an approximation.** The wording is generic English, chosen to be clear and stable. It does not reproduce what
any particular screen reader says, and the tree it reads is Chromium's choice of tree (`accessibility-chromium-profile.md`).
Nothing in it is a statement about NVDA, JAWS, VoiceOver, Orca or ChromeVox. The golden files in
`testdata/reading-view/` show the output for the pages in the tests.

## The reading view

The tree is walked depth first in tree order: document order, with `aria-owns` children where the tree puts them. Each step
(`ReadingItem`) has:

- `Kind`: `Text`, `Element`, `Start` or `End`;
- `Text`: the wording, never empty;
- `Node` (the tree node), `DomNode`, and `Nodes` (every tree node a text run covers);
- `Lang`: the value of the nearest `lang` attribute on the node or an ancestor, trimmed; null when none is declared or the
  nearest is empty (the language is unknown);
- `Depth`: the number of containers open around it.

`ToText()` prints one step per line, two spaces of indent per depth.

### What produces a step

A node that the tree ignores produces nothing, but its children are still visited (they carry their own ignored and hidden
status). A hidden or `aria-hidden` subtree is ignored all the way down, so it is not read.

**Text runs.** The text of adjacent text nodes is merged into one `Text` step, as a reader reads a line. A run continues
across inline elements that are not announced (`b`, `span`, `em`, `time`, ...) and across the white space between inline
neighbours, and it ends at: an announced element or container (before and after it), the start or end of any element whose
`display` is not inline (a paragraph, `div`, list item, table part), and a change of `Lang`. Runs are whitespace-collapsed
and trimmed (ASCII whitespace only). Text that is the content of a native label which names a control or group (`label`
for a form control, `legend`, `caption`, `figcaption`) is not read as a run, because it is the name the control's or
container's step already says.

**Announced elements** produce an `Element` step:

```
<role phrase>[, <state>...][: <name>][, value <value>]
```

- The phrase is the table below. `aria-roledescription` replaces it.
- Names come from the tree. For a role that takes its name from contents, the name is included only when the element is a
  leaf: its contents are only text (nothing in it is itself announced) or its role's children are presentational (WAI-ARIA 1.2
  "Children Presentational"; those children are then not read again). When something inside is announced (a heading holding
  a link) the step says the role and the contents follow as their own steps, so no text is said twice. A name from
  `aria-label`, `aria-labelledby` or a native label is always included.
- `combobox` (including a `select`) is a leaf: its options are not read unless it is a visible `listbox`.
- Value (`, value X`) is added for text fields, search fields, combo boxes, sliders, spin buttons, progress bars and meters:
  `aria-valuetext`, else the value, else `aria-valuenow`.

**Containers** produce a `Start` step and, after their contents, an `End` step (`end of <phrase>`), at the same depth:

| Role | Start | End |
|---|---|---|
| `list` | `list with N items` (`list` when no `listitem` is counted); N counts the `listitem` descendants whose nearest list is this one | `end of list` |
| `table`, `grid`, `treegrid` | `table with R rows and C columns` (or `grid`, `tree grid`); rows are the `row` descendants, columns the widest row after column spans | `end of table` |
| `banner`, `complementary`, `contentinfo`, `main`, `navigation`, `search` | `<name> landmark` (`banner`, `complementary`, `content info`, `main`, `navigation`, `search`) | `end of <name> landmark` |
| `region`, `form` | `region`, `form` | `end of region`, `end of form` |
| `dialog`, `alertdialog` | `dialog` or `alert dialog`, then `, modal` when `aria-modal` is true | `end of dialog`, `end of alert dialog` |
| `group`, `radiogroup`, `toolbar`, `menu`, `menubar`, `listbox`, `tablist`, `tree`, `figure`, `article` | the phrase | `end of <phrase>` |

Landmark phrases do not say "landmark" for `region` and `form`, which exist as roles only when named. Container steps then
add their states (below) and `: <name>` when the container has a name. `region` and `form` have a name by construction;
`section` and `form` elements without one are `generic` in the tree and produce no container.

**Role phrases** (announced roles without a table row above): `link`, `button`, `heading level N` (`heading` without a level),
`checkbox`, `radio button`, `switch`, `text field`, `text area` (multiline), `search field`, `combo box`, `option`, `slider`,
`spin button`, `progress bar`, `meter`, `scroll bar`, `image` (role `img` and `image`), `menu item`, `checkbox menu item`,
`radio menu item`, `tab`, `tab panel`, `tree item`, `separator`, `math`, `tooltip`, `alert`, `status`, `log`, `marquee`,
`timer`, `note`, `block quote`, `term`, `definition`, `code`, `application`, `document`, `inserted text`, `deleted text`.
Other roles (`generic`, `paragraph`, `StaticText`, `strong`, `emphasis`, `row`, `rowgroup`, `caption`, ...) are silent.

**List items** are announced as `item P of N` (position in set and set size), then their contents follow.

**Table cells.**

- `columnheader` and `rowheader`: `column header` / `row header`, with `: <name>` when the cell holds only text; otherwise
  the contents follow.
- `cell` and `gridcell`: `cell`, then `: column header A / B, row header C` when headers apply, and the contents follow as
  steps of their own.
- The grid is laid out with `colspan` and `rowspan` (or `aria-colspan` / `aria-rowspan`), spans clamped to 1000 columns and
  65,534 rows. A column header applies to cells below it in the columns it spans, a row header to cells to its right in the
  rows it spans. Headers are the cells whose tree role is `columnheader` or `rowheader`, so `scope`, `th` position and
  explicit roles act through the role layer. At most 16 headers are listed per cell, and a table with more than 100,000 cells
  is read without cell headers beyond that point.

**States**, in this order, each added as `, <state>` when it applies to the role: `checked`, `not checked`,
`partially checked` (checkbox, radio, switch, menu item checkbox and radio); `pressed`, `not pressed`, `partially pressed`;
`expanded`, `collapsed`; `selected`; `current page`, `current step`, `current location`, `current date`, `current time`,
`current` (other tokens); `required`; `invalid entry` (any `aria-invalid` other than false); `unavailable` (disabled);
`read only`; `has popup`; and `P of N` for options, tabs, radio buttons, menu items and tree items.

## Navigation lists

`NavigationLists` has five lists, each in tree order, each entry holding its `AxNode` (and so its DOM node, locator and
source range). Ignored nodes are not listed.

| List | Contains | Entry |
|---|---|---|
| `Headings` | role `heading` | level (the heading level, 2 when a `role=heading` has none), name, `Lang` |
| `Landmarks` | roles `banner`, `complementary`, `contentinfo`, `form`, `main`, `navigation`, `region`, `search` | role, name, number of landmarks containing it |
| `Links` | role `link` | name, `href` as written (null when there is none), whether it starts with `#` |
| `FormFields` | roles `textbox`, `searchbox`, `combobox`, `listbox`, `checkbox`, `radio`, `switch`, `slider`, `spinbutton` | role, label (the accessible name), required, invalid, disabled, value text (null when empty) |
| `Tables` | roles `table`, `grid`, `treegrid` | caption (the accessible name), rows, columns, column header names, row header names |

Buttons are not form fields in these lists. A table whose role is `none` (a presentational table) is not listed; whether a
layout table gets that role is decided by the role layer.

## Differences

`ReadingView.Diff(other, changeSet?)` returns a `ReadingViewDiff`. Tree nodes are matched as for `TreeDiff`. A step is
identified by its tree node and `Kind` (a text run by its first text node); matched steps are `Changed` when `Text` or `Lang`
differs, steps with no match are `Added` or `Removed`, and a step that only moved is not a change. Removing the second of
three list items removes `item 2 of 3` and its text and changes `list with 3 items`, `item 1 of 3` and `item 3 of 3`.

## Not done

Announcements are English only; `Lang` is kept so a consumer can choose. Live regions, `aria-description` and tooltips,
form `title` hints, link visited state, images' long descriptions and the position of the virtual cursor are not part of
the view.
