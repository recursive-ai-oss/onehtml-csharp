# The Chrome accessibility-tree differential

`OneHtml.Differential ax` compares our accessibility tree with the one Chrome returns from `Accessibility.getFullAXTree`, node by node, for pages captured by `tools/capture`. Chrome is a black box here: only its protocol output is read. The command is in `tools/OneHtml.Differential`; nothing in `src/` depends on it.

```
OneHtml.Differential ax <capture-dir> [--corpus <dir>] [--out <dir>] [--jobs n] [--limit n] [--timeout s] [--top n] [--facts on|off|both] [--dump]
```

| Argument | Meaning |
|---|---|
| `<capture-dir>` | The capture tool's output directory (or its `captures/` directory). Every `…/<sha256>/<WxH>/` directory with a `meta.json` (or an `errors.json`) is one capture. |
| `--corpus` | Where stylesheets come from: a corpus version directory (`manifest.jsonl`, stylesheet bodies by URL) or a directory of loose `.css` files, matched by the end of the URL path. Chrome was served the same bytes. Without it, linked and imported sheets are missing and the cascade runs on what the page itself holds. |
| `--out` | Output directory, default `./out`. |
| `--jobs`, `--limit`, `--timeout`, `--top` | As for the other commands: parallel captures (default 2), the first n captures in sha256 order, seconds per capture (default 30), classes shown in `summary.md` (default 20). |
| `--facts` | `on`, `off` or `both` (default). See "Rendered facts". |
| `--dump` | Also writes `dump/<sha12>-<viewport>.txt`: one line per Chrome node with its counterpart, then our unpaired nodes. For finding out why a class exists. A Chrome node with no counterpart also shows where it sits (`under` its nearest paired ancestor), and one of ours shows its parent's path. |

Exit status is 0 whatever the rates are; the numbers are the result.

## What is compared

For each capture the tool reads `dom.json`, `snapshot.json`, `ax.json` and `meta.json`:

1. Chrome's serialized DOM (doctype plus `outerHTML`) is parsed with OneHtml. The cascade gets the capture's viewport (`width`, `height`, `deviceScaleFactor`) as media features, the page URL as base URL, and the stylesheet resolver above. Scripts never ran; that is how the capture was taken.
2. The snapshot is mapped onto the document (`DomSnapshotAdapter.Map`). A snapshot of a different DOM is **refused**; the capture is counted as `refused` with the refusal reason and not compared, because nodes cannot be matched without the mapping.
3. Our tree is built (`AccessibilityTree.Build`, with `AccessibilityOptions.BaseUrl` the capture's final URL) and written in the Chrome-shaped export (`onehtml-ax/1`). Chrome's main-frame nodes and the export are read by the same code into a common shape, so both sides pass through the same normalization.
4. Nodes are matched, then compared.

Only the main frame's nodes are compared; frames of other documents, and the child ids that point into them, are ignored.

### Matching

1. The root web area pairs with our document node.
2. A Chrome node with a `backendDOMNodeId` pairs with our node for the same DOM node. The id is translated through the snapshot's `backendNodeId` and the mapping's node accounting (`NodeFacts.BackendNodeId`).
3. **Anonymous nodes.** A Chrome node that has no OneHtml node (no backend id, or one the mapping does not carry: generated text, layout boxes, user-agent shadow content) pairs with the first still unpaired child of the counterpart of its nearest paired ancestor that is of the same kind (text or not), has the same role and the same ignored flag, and has the same name. Anything else stays unpaired. The number paired this way is in the summary.
4. Chrome's `InlineTextBox` nodes are skipped on read. They are layout boxes of a `StaticText` and carry no information the static text does not.
5. Unpaired nodes are counted per side. An unpaired node that is ignored is counted separately (`chromeOnlyIgnored`, `oursOnlyIgnored`): an ignored node is not part of what assistive technology walks, so it does not change the exposed tree. Chrome omits `visibility: hidden` and `aria-hidden` elements and `noscript`; we keep them as ignored nodes.

### Fields

Each paired node is compared on the fields below. A field is compared when it applies; `compared` in the report is the number of times it was.

| Field | Compared | Notes |
|---|---|---|
| `ignored` | every pair | Chrome's `ignored` against ours. If the flags differ, no other content field of that pair is compared. |
| `hidden` | every pair | Whether the ignored reasons include `notRendered`, `ariaHiddenElement`, `ariaHiddenSubtree`, `inertElement` or `inertSubtree`. |
| `ignoredReasons` | both ignored | The remaining reasons (`uninteresting`, `presentationalRole`, `labelFor`, …) as a sorted set. |
| `role` | both not ignored | Chrome's role value (including its internal roles) against ours (`InternalRole` or the Core-AAM name). |
| `name` | both not ignored | After the normalization below. |
| `nameSource` | names equal and not empty | The step that produced the name, in these categories: `aria-labelledby`, `aria-label`, `native-element` (label, legend, caption, figcaption, SVG or document `title` element), `native-attribute` (`alt`, `value`, …), `title` (the attribute), `placeholder`, `contents`, `text` (static text). For Chrome it is the first entry of `name.sources` that is not superseded and has a non-empty value. |
| `description` | both not ignored | After the normalization below. |
| `value` | either has one | The widget value. |
| `prop.<name>` | either has it | `focusable`, `focused` (not the root: the page's own focus), `disabled`, `checked`, `expanded`, `selected`, `pressed`, `required`, `invalid`, `level`, `live`, `modal`, `hasPopup`, `multiselectable`, `readonly`, `valuemin`, `valuemax`, `valuetext`, `orientation`, `multiline`, `editable`, `url` (not the root: the document's URL is not in the DOM), `posinset`, `setsize`, `autocomplete`, `busy`, `atomic`, `relevant`, `roledescription`, `keyshortcuts`. |
| `rel.<name>` | either has it | `activedescendant`, `controls`, `describedby`, `details`, `errormessage`, `flowto`, `labelledby`, `owns`: the related nodes in order, as OneHtml node indexes. |
| `parent` | every pair | Parent in the unpruned tree (ignored nodes included): the pair of Chrome's parent against ours. |
| `exposedParent` | every pair | Nearest ancestor that is not ignored. |
| `childOrder` | not-ignored pairs with at least two common exposed children | The order of the exposed children that both sides have. A child present on one side only is reported once, as `chromeOnly` or `oursOnly`. |
| `chromeOnly`, `oursOnly` | every not-ignored node of that side | Present on one side only. |
| `chromeOnlyIgnored`, `oursOnlyIgnored` | every ignored node of that side | As above, for ignored nodes. |

### Normalization

Everything that makes the two sides spelled differently without a difference in meaning is in `AxNormalizer` (`tools/OneHtml.Differential/AxModel.cs`), nowhere else. It is:

- **White space.** Runs of ASCII white space (space, tab, line feed, form feed, carriage return) in names, descriptions and `valuetext` collapse to one space and the ends are trimmed. A no-break space stays.
- **Flags.** `false` and absent are the same for `focusable`, `focused`, `disabled`, `required`, `readonly`, `modal`, `multiselectable`, `multiline` and `busy`; `invalid=false` is absent; an empty `valuetext`, `roledescription`, `keyshortcuts`, `autocomplete` or `relevant` is absent. Chrome reports these flags as `false` where the role supports them and we leave them out. `expanded`, `selected`, `checked` and `pressed` are not touched: absent means the role does not have the state, `false` means it is off.
- **Numbers.** Property numbers are compared in their shortest round-trip spelling (`1` and `1.0`). The `value` of a `meter`, `progressbar`, `slider`, `spinbutton` or `scrollbar` is compared as a 32-bit float, because Chrome stores these as single-precision floats and reports them widened (`0.6` arrives as `0.6000000238418579`). Other values compare as written.
- **Value and flags added by the last round.** An empty `value` is absent; `atomic=false` is absent; `busy` as `1` is `true`.
- **Parents.** `parent` and `exposedParent` are compared through the nearest ancestor that has a counterpart on the other side, so an unpaired ignored node is not counted twice.
- **Role of ignored nodes.** Both sides call an ignored node `none`; its role is not compared.
- **Name source of static text** is `text` on both sides.
- **Names and descriptions of ignored nodes** are not compared (neither side has them).

Nothing else is normalized: no role aliases, no case folding, no Unicode normalization.

### Rendered facts

`--facts` selects whether our tree is built with the capture's rendered facts (`AccessibilityOptions.Facts`). The facts are always used for the mapping in step 2, in both modes.

- `off`: one run without facts.
- `on`: one run with facts; the report is that run.
- `both` (default): each page is compared twice, with and without facts, over the same Chrome nodes. The per-field table and the classes are the run with facts; the section "With and without rendered facts" prints both rates side by side (with the number of mismatching nodes in brackets).

A capture whose snapshot does not map onto its DOM is refused in every mode.

## Output

Written to `--out`; nothing in it should be copied into the repository except reduced, hand-written cases.

| File | Content |
|---|---|
| `summary.md` | Counts, refused and failed captures with reasons, the per-field table, the with/without facts table, the top classes with up to three examples each. |
| `summary.json` | Schema `onehtml-ax-differential/1`: `reference` (Chrome product, revision and protocol version from the capture's `environment.json`), `facts`, `captures`, `compared`, `pages`, `refused`, `failed`, `chromeNodes`, `oursNodes`, `matched`, `matchedAnonymous`, `refusals` (`sha256`, `viewport`, `url`, `reason`), `fields` (`field`, `compared`, `occurrences`, `nodes`, `pages`, `pagesCompared`, `rate`), `topClasses`. |
| `classes.json` | Schema `onehtml-ax-differential/1/classes`: every class (`field`, `pattern`, `occurrences`, `nodes`, `pages`, `examples`). An example has `page` (sha256), `viewport`, `url`, `node` (our DOM path such as `html[1] > body[2] > div[6]`, or `chrome#<backend id> <role>` for a Chrome-only node), `chrome` and `ours` (values, clipped to 80 characters). Ordered by occurrences, then field and pattern. |
| `captures.jsonl` | One line per capture: `sha256`, `viewport`, `url`, `status` (`ok`, `refused`, `skipped`, `error`), `detail`, node counts and mismatch count. |
| `dump/` | With `--dump`, see above. |

**Rates.** `occurrences` counts mismatching (capture, node) pairs; `nodes` counts distinct (page, node) pairs, so a page captured at two viewports counts its nodes once; `pages` is the number of pages with at least one. `rate` is occurrences over `compared`. For properties and relations, `compared` counts the pairs where either side has the property, so a rare property that always disagrees reads 100%.

**Classes.** A class is a field plus a pattern: for `role`, the two roles (`LayoutTable -> table`); for `name`, `description` and `value`, the role and the shape of the difference (`chrome-empty`, `ours-empty`, `ours-prefix`, `chrome-prefix`, `case-only`, `differs`); for properties, the role and both values; for `ignored`, which side is ignored and the roles and reasons; for the structure fields, the roles of both parents; for the presence fields, the role and, when ignored, the reasons. Output is deterministic: captures are processed in sha256 and viewport order whatever `--jobs` is.

## Worker run

The command takes the worker's shared-job lock like the other corpus commands and exits with status 3 at once if it is held:

```
bash tools/OneHtml.Differential/run-worker.sh ax ~/onehtml/capture-out/<version> ~/onehtml/out/ax --corpus ~/onehtml/corpus/<version> --facts both --jobs 8
```

`--limit N` takes the first N captures by hash and `--pages <sha256 prefix,...>` selects pages; `--dump` writes a per-node listing of the selected pages.

## Results with and without rendered facts

Chrome for Testing 156.0.8078.47 (protocol 1.3), headless, 1280x800, page script off; .NET 10 on the AMD Ryzen 9 7950X3D worker (32 threads), `--facts both --jobs 2 --limit 2100`, about 125 seconds. The sample is the first 2,100 captures by hash: 2,095 compared (1,840,086 Chrome nodes, 1,973,233 of ours, 1,779,203 matched, 284,249 of them by the anonymous-node rule) and 5 refused (a custom element whose shadow root the snapshot omits, and two whose text differs in line breaks). Rates are mismatching nodes over the times the field was compared, with the number of nodes in brackets. "Before" is the same pages with the tree as it stood at the start of this round (facts on). Fields that never differ are left out; the run's `summary.md` has all of them.

| field | before (facts) | without facts | with facts |
|---|---:|---:|---:|
| ignored | 0.291% (5114) | 13.279% (199263) | 0.173% (3086) |
| hidden | 0.06% (1053) | 0.088% (1323) | 0.06% (1072) |
| ignoredReasons | 0.124% (491) | 1.68% (1380) | 0.118% (475) |
| role | 0.089% (1216) | 0.485% (5914) | 0.089% (1217) |
| name | 0.591% (8027) | 0.561% (6845) | 0.498% (6845) |
| nameSource | 0.012% (102) | 0.014% (102) | 0.012% (102) |
| description | 0.135% (1836) | 0.148% (1810) | 0.132% (1810) |
| parent | 0.307% (5398) | 0.422% (6332) | 0.303% (5397) |
| exposedParent | 0.389% (6851) | 23.639% (354741) | 0.245% (4362) |
| chromeOnly | 1.979% (27510) | 11.866% (164933) | 0.913% (12687) |
| oursOnly | 0.347% (4733) | 8.231% (126706) | 0.378% (5222) |
| chromeOnlyIgnored | 11.737% (52827) | 38.774% (174511) | 10.708% (48196) |
| oursOnlyIgnored | 32.173% (189519) | 79.531% (341912) | 31.864% (188808) |
| prop.checked | 0.039% (2) | 0.039% (2) | 0.039% (2) |
| prop.disabled | 6.931% (21) | 6.623% (20) | 6.931% (21) |
| prop.editable | 0.028% (1) | 0.028% (1) | 0.877% (61) |
| prop.expanded | 0.097% (5) | 0.096% (5) | 0.097% (5) |
| prop.focusable | 0.02% (58) | 0.02% (58) | 0.02% (58) |
| prop.hasPopup | 0.635% (19) | 0.635% (19) | 0.635% (19) |
| prop.invalid | 32.836% (22) | 32.836% (22) | 32.836% (22) |
| prop.level | 0.011% (22) | 0.011% (21) | 0.011% (22) |
| prop.posinset | 100% (28) | 100% (28) | 100% (28) |
| prop.readonly | 17.949% (7) | 17.949% (7) | 17.949% (7) |
| prop.roledescription | 5.069% (11) | 5.069% (11) | 5.069% (11) |
| prop.selected | 0.377% (58) | 0.377% (58) | 0.377% (58) |
| prop.setsize | 100% (28) | 100% (28) | 100% (28) |
| prop.url | 1.061% (2832) | 1.061% (2832) | 1.061% (2832) |
| prop.valuemax | 2.572% (8) | 2.572% (8) | 2.572% (8) |
| prop.valuemin | 1.929% (6) | 1.929% (6) | 1.929% (6) |
| prop.valuetext | 9.565% (11) | 9.565% (11) | 9.565% (11) |

Without facts the tree cannot know what Chrome did not draw, so `ignored`, `exposedParent`, `chromeOnly` and `oursOnly` are high; with facts they are the figures above. No field is worse with facts than without by more than a few nodes, except `prop.editable` (1 node without facts, 61 with: the inner editors of text fields, see the classes). The probe pages (`testdata/rendered/pages/*probe*.html`, with Chrome's captures beside them) pin the rules that came out of this round: `ChromeProbeTests` compares the exposed structure of the generated-content probe, the inner nodes of text fields and the name of each of 170 links with Chrome's.

### Classes

A class is a field plus a pattern (see "Output"). Dispositions of every class above 0.1% of its field's compared count (for the small property fields, above one node). **Fixed** means the class shrank to the figures given, with a hand-written test and a profile entry; **open** means it was not traced to a rule and is not documented as a deliberate difference.

| Class | Count | Disposition |
|---|---:|---|
| `oursOnlyIgnored` (`none` with `notVisible`, `notRendered`, `ariaHiddenSubtree`, `ariaHiddenElement`, `uninteresting`, `emptyAlt`, `presentationalRole`) | about 189,000 | **Representation, deliberate.** Our tree keeps every DOM node with its hidden and ignored reasons so that edits and diffs map back to the markup. Chrome omits nodes it has no use for. An ignored node is never part of the exposed tree. Not fixed. |
| `chromeOnlyIgnored` | about 48,000 | **Representation.** Chrome creates ignored nodes (children of `display: none` and `aria-hidden` subtrees, `uninteresting` wrappers around layout objects) that have no DOM node of ours. Ignored, so not exposed. The wrappers of `::before` and `::after` boxes are now ours too (profile entry `generated-content-node`). |
| `ignored`: Chrome `generic`, ours ignored `uninteresting` | 2,026 -> 1,014 | **Fixed in part, open.** The residue was traced: a generic with drawn `::before` or `::after` text is kept (fixed, 1,000 of them), an inline-block `span` is kept (fixed; the earlier rule ignored every span), and what is left are spans and divs of the same shape that Chrome ignores (inline-block spans with and without generated content split about 60 to 40, `div` and `span` flex items with a text child), where nothing in the capture's styles separates the two. Probe pages show Chrome ignoring a block-level empty `::before` box in a block host and keeping it in an inline host. |
| `ignored`: Chrome ignored `uninteresting`, ours `generic` | 716 -> 290 | **Open, small.** Mostly an inline-block span whose Chrome node is ignored. |
| `ignored`: Chrome `LabelText`, ours ignored `labelFor` (and the text children) | 485 -> 395 on 15 pages | **Fixed in part.** A probe of 50 label forms settled the rule (profile entry `label-for`): a label with only text that labels a checkbox or a radio button is `labelFor` whether it wraps the control or uses `for`, before or after it; a label with an element child, or of any other control, is `LabelText`. The 395 left are on 15 pages and were not traced. |
| `ignored`: Chrome `image`, ours ignored `emptyAlt` | 362 | **Deviation.** An `img` with `alt=""` and a `title` is an `image` in Chrome 156 (47 of 47 in 400 pages); HTML-AAM and WPT `html-aam/roles-contextual.html` (`el-img-empty-alt-title`) give it no role. Kept at the specification's answer. |
| `hidden` (1,072 nodes, 0.06%) | 1,072 | **Below the threshold.** The `ariaHiddenSubtree` against `ariaHiddenElement` split is Chrome's choice for nested `aria-hidden` that the profile entry `ignored-reasons` handles for the element's own attribute; the rest are `notRendered` subtrees of `display: none` ancestors that Chrome lists. |
| `ignoredReasons` (475 nodes, 0.118%) | 475 | **Open, near the threshold.** `uninteresting` against no reason on SVG graphics elements (Chrome gives `notRendered` to `title`, `desc`, `stop` and some `path` elements depending on layout objects we do not model) and `uninteresting` against `presentationalRole` in `role=presentation` tables. |
| `parent`, `exposedParent` | 5,400 and 6,851 -> 4,362 | **Follows from the other classes.** `exposedParent` fell from 0.39% to 0.245% with the generated-content and label fixes; the rest is the generic differences above and `LayoutTableCell` against `LayoutTable`. |
| `chromeOnly`: `StaticText` (4,082), `generic` (2,572) | 27,510 -> 12,687 | **Fixed in part.** `::before`/`::after` text and boxes, the text of input buttons and the inner editors of text fields are now nodes (profile entries `generated-content-node` and `control-text-node`); the part that is left is the shadow content of date, time, file, range and select controls, the pseudo boxes of display table, flex, grid and list-item (Chrome nests them differently), image-input parts, a `StaticText` that Chrome gives to whitespace Chrome draws between inline elements that are not both `display: inline`, and the `ListMarker` and `listitem` of `::before` boxes with `display: list-item`. **Open.** |
| `oursOnly`: `StaticText` (3,184), `generic` (1,688) | 4,733 -> 5,222 | **Open.** Whitespace text nodes Chrome merges with a neighbour, and generics of ours that Chrome has no node for at all: an `a` without `href` is now left out like a span (fixed), but inline-block spans and block `a`/`div` wrappers remain. The `oursOnly` rate rose from 0.35% to 0.38% while `chromeOnly` fell from 1.98% to 0.91%: more nodes are created, some of them not Chrome's. |
| `name`: `link differs` | 7,800 -> 4,392 | **Fixed in part.** Flex and grid items and `display: contents` elements are blocks for the spaces in a name (profile entry `positioned-blockification`, 150 of 150 probe links agree), SVG `style` text is not name content (176 links), the rest is `title` and `aria-label` of hidden children Chrome includes, `alt` of images Chrome leaves out (a link with a lazy-load placeholder image), the spaces Chrome puts in a long text that wraps inside a word (a URL as link text) and spaces of icon-font spans. **Open.** |
| `name`: `term` (929), `time`, `figure`, `heading` | 929 + 114 + 392 + 186 | `dt` named from its contents and `time` unnamed (profile entry `name-from-contents-roles`, fixed). A `figure` whose name we take from a `title` or `figcaption` that Chrome leaves empty (392 on 78 pages) and a `heading` inside a button whose name we leave empty (186 on 34 pages) are **open**. |
| `name`: `option` `case-only` | 147 on 7 pages | **Deviation, open.** Chrome does not apply the select's `text-transform` to an option's name; we do. |
| `role` (1,217 nodes, 0.089%) | 1,217 | **Below the threshold.** `cell`, `row`, `table` against `LayoutTableCell`, `LayoutTableRow`, `LayoutTable` (profile entry `layout-table`), and `image` against `SvgRoot`. |
| `description`, `nameSource` | 1,810 and 102 | **Open, small.** `description` is 0.132%; the remainder is the `name` classes (a link's `title` as description when Chrome uses it as the name). |
| `prop.url`: `image` and `srcset` | 2,832 | **Deviation.** Chrome reports the image chosen from `srcset` and `picture` for its viewport and density; we state the `src`, because choosing needs the layout width. |
| `prop.editable` | see below | **Second pass** ("Property fields, second pass"): traced and fixed, or documented per class. |
| `prop.disabled`, `invalid`, `hasPopup`, `readonly`, `selected`, `posinset`, `setsize`, `valuemin`, `valuemax`, `valuetext`, `roledescription` | see below | **Second pass** ("Property fields, second pass"): traced and fixed, or documented per class. |
| `value` | see below | **Second pass** ("Property fields, second pass"): traced and fixed, or documented per class. |
| `rel.labelledby`, `rel.controls` | 298 and 14 | **Open.** Chrome lists the label for a radio, checkbox and textbox where our name came from another source; navigation landmarks whose label target is not in Chrome's tree. |

The open classes above are not traced to a rule. The largest are `name` `link differs` (0.3% of the field's compared nodes), the generic and `StaticText` differences in `chromeOnly` and `oursOnly`, and the three small `ignored` residues.

## Property fields, second pass

The first run of the full corpus (12,788 captures, facts both) left these property fields above 1%: `posinset` and `setsize` 100%, `invalid` 27.3%, `readonly` 21.5%, `disabled` 15.8%, `valuetext` 7.9%, `value` 4.9%, `roledescription` 2.3%, `selected` 1.7%, `valuemax` 1.2%, `autocomplete` 2.8%, `hasPopup` 1.0%, `editable` 1.1%, `keyshortcuts` 1.0%, and `chromeOnly` 1.126%. Each had one or two systematic causes. They were found by writing a probe page for each suspected behaviour (`testdata/rendered/pages/axprops.html`, `axprops2.html`, `axprops3.html`), capturing it in the pinned Chrome (captures beside it in `testdata/rendered/captures`), and fixing the tree or export to match; the rule is a profile entry of the name given. `ChromePropertyTests` has one case per cause; `ChromePropertyProbeTests` compares every element with an `id` on the first two probe pages property by property and lists the three differences left; `ChromeProbeTests` compares the inner-node structure of the third.

### Causes

| Field | Cause | Profile entry |
|---|---|---|
| `posinset`, `setsize` | Chrome never states either (214 of 214 mismatches had Chrome absent), whether the author gave them or not; we stated the given ones. | `export-set-position` |
| `invalid` | Chrome reports constraint failures of more control kinds than we did (radio groups, required file, date and time fields, list boxes, email and url syntax, pattern, step, date range) and none for a disabled or readonly control or after an explicit `aria-invalid=false`. | `constraint-invalid` |
| `readonly` | Chrome states it on text boxes and grids only, and not when disabled. | `readonly-exposure` |
| `disabled` | A disabled ancestor reaches only the focusable descendants, a disabled fieldset or optgroup is not itself disabled, an option of a disabled select is. | `disabled-inheritance` |
| `valuetext`, `valuemin`, `valuemax`, `value` of ranges | Chrome never states `aria-valuetext`, reports bounds as single-precision numbers, lets the author's `aria-valuemin`/`max`/`now` win over native attributes, and gives a spin button 0, 0 and 0 and a progress bar with a bad `aria-valuenow` the value 0. | `range-values` |
| `value` of text-like controls | "No file chosen", password bullets, the text of a custom combobox or text box, the first enabled option of a select (not the first), lower-cased colours, no value for an invalid date, no line breaks in a text value, the select's `text-transform`. | `input-password-file`, `option-selectedness`, `text-values` |
| `selected` | Chrome omits `selected=false` on an option, tab or tree item when another item of the list says anything about `aria-selected`; it states none on a disabled option; a tree item is `false` by default. | `selected-states` |
| `role` (a by-product) | An `option` role with no listbox around it (a `button`, `a` or `li` carrying `role=option` in a popup) is the element's own role in Chrome. This removed about 7 in 8 of the `role` mismatches on the 171-page subset. | `option-needs-listbox` |
| `roledescription` | Not stated on a generic element; non-ASCII text arrives as Latin-1 bytes of its UTF-8 form. | `roledescription-roles` |
| `autocomplete`, `hasPopup` | `hasPopup` on any element, listbox by default for every combobox, `aria-haspopup=1` and `false` are not popups; `autocomplete` on text fields only, and for `input list`. | `autocomplete-popup`, `autocomplete-text-fields` |
| `keyshortcuts` | The `accesskey` attribute as written, after `Alt+`. | `accesskey-shortcut` |
| `editable` | Editable text inherits down from a `contenteditable` host; the inner editor of a disabled or read-only field is not editable. | `editable-inherited` |
| `expanded`, `level`, `multiline`, `required` (found by the probe comparison) | An ARIA combobox has no `expanded` of its own, tree items have levels, a `contenteditable` text box is multiline, a list box select is required. | `combobox-expanded`, `treeitem-level`, `readonly-exposure`, `text-values` |
| `chromeOnly` `generic` (a search field with a placeholder) | The placeholder leaf comes before the ignored node that holds the inner editor of a search or number field, and a combobox input has that node too. | `placeholder-leaf` |

### Before and after

Same machine and run as above (Chrome for Testing 156.0.8078.47, `--facts both --jobs 2`, one process; AMD Ryzen 9 7950X3D, 32 threads, load average 4 to 5 from other work during the runs, so these are counts, not timings). Counts are mismatching nodes with facts. The subset is the 171 captures whose first mismatch examples were in the classes above (every class had at least one page); the sample is 399 captures from the middle of the corpus order (`--skip 3000 --limit 400`, one refused), as a check that no other field gets worse.

| field | subset before | subset after | sample before | sample after |
|---|---:|---:|---:|---:|
| ignored | 127 | 127 | 730 | 730 |
| hidden | 738 | 738 | 219 | 219 |
| ignoredReasons | 273 | 273 | 222 | 222 |
| role | 571 | 82 | 228 | 228 |
| name | 586 | 586 | 3,932 | 3,932 |
| nameSource | 23 | 23 | 59 | 59 |
| description | 452 | 452 | 498 | 498 |
| parent | 627 | 627 | 609 | 609 |
| exposedParent | 235 | 235 | 988 | 988 |
| chromeOnly | 874 | 799 | 2,380 | 2,267 |
| oursOnly | 737 | 693 | 925 | 820 |
| prop.posinset, prop.setsize | 151 each | 0 | 0 | 0 |
| prop.invalid | 45 | 0 | 1 | 0 |
| prop.readonly | 36 | 0 | 1 | 0 |
| prop.disabled | 79 | 13 | 1 | 0 |
| prop.valuetext | 83 | 0 | 3 | 0 |
| prop.valuemin, prop.valuemax | 9, 21 | 0, 0 | 0 | 0 |
| prop.roledescription | 19 | 0 | 11 | 0 |
| prop.selected | 571 | 18 | 18 | 10 |
| prop.autocomplete | 6 | 0 | 0 | 0 |
| prop.hasPopup | 61 | 0 | 6 | 0 |
| prop.editable | 73 | 4 | 3 | 0 |
| prop.expanded | 10 | 0 | 2 | 0 |
| prop.level | 29 | 1 | 4 | 4 |
| prop.keyshortcuts | 9 | 0 | 0 | 0 |
| prop.required, prop.multiline | 2, 1 | 0, 0 | 0 | 0 |
| prop.url, prop.focusable, rel.* | unchanged | unchanged | unchanged | unchanged |
| value | 86 | 40 | 2 | 2 |

The final code was run on both sets. Every field not named is equal; none rose. The final rates come from the next full-corpus run.

### What is left, per field

| Field | Left | Why |
|---|---|---|
| `disabled` | 13 on the subset | An `a` inside an `object` (the plug-in fallback content) is disabled in Chrome, with no ancestor that says so. Not traced further. |
| `selected` | 18 on the subset | A tab in a tab list of `aria-multiselectable` accordion panels states nothing in Chrome although no tab says anything; we state `false`. A row with `aria-selected=false`. The rule behind the first is not clear from the probes. |
| `value` | 40 on the subset | A button with the combobox role whose content has an `svg` with a title: Chrome's value includes the title ("1 downwardArrowQuantity"), ours the text only (30). A select with a trailing direction mark (`\u200e`) that Chrome trims (2). A custom combobox whose hidden part Chrome leaves out of the value (1). A text field with `contenteditable=false` and `role=textbox`, whose text Chrome joins with its line breaks (3). Other select cases (3). An input with `role=status` (1). |
| `autocomplete`, `hasPopup`, `keyshortcuts`, `invalid`, `readonly`, `roledescription`, `valuetext`, `valuemin`, `valuemax`, `posinset`, `setsize` | 0 on the subset | Fixed. |
| `editable` | 4 on the subset | Static text inside an editing host that our tree does not mark; not traced. |
| `prop.url` | about 0.9% | `srcset` choice, see above; not in this unit's list. |
| `chromeOnly` | 830 on the subset (0.6%) | Not reduced to one cause. The largest single class found by tracing is a harness artefact: pages that link their stylesheets with `crossorigin` from another host (a CDN, with a subresource-integrity hash) load in our cascade but are rejected by Chrome in the capture, because the stored response has no CORS headers. A Bootstrap `.modal { display: none }` hides the dialog for us and not for Chrome, so Chrome's whole dialog subtree is `chromeOnly` (traced on one page; whether the same cause is behind the 80-page `listitem` and `list` classes was not checked). Fixing it means serving the stored sheets with `Access-Control-Allow-Origin` in the capture tool, which needs a re-capture; it is not a library difference. The rest is the generic, `StaticText` and `ListMarker` classes above. |

### Behaviour not copied

- The Chrome value of an editing host with block content (`x\n\nbs` for a `div` holding a `p`, a button and a span) depends on layout line breaks; ours has none (`ChromePropertyProbeTests`).
- `aria-selected=false` on a `row`: Chrome states nothing, we state `false` (as for the grid cell and option, which state it).
- Step mismatch counts only from a `min` (the step base is the `min`, else the `value` itself).

## Findings on the fixture pages

The fixture pages (`testdata/rendered/pages`) were captured with Chrome 156 (headless, script off, 1280×800). After the fixes listed in the profile, what is left is below, each with a verdict. "Chrome" means Chrome's choice where the specifications leave room or are silent; "ours" means we do not yet do what Chrome does and no specification says otherwise. Evidence is Chrome's output on the page named.

| Class | Verdict |
|---|---|
| `chromeOnly`: `ListMarker`, generated `StaticText` (`::before`/`::after` content), `LineBreak`, `MenuListPopup`, the shadow content of date, number, textarea and select controls, the `StaticText` of a submit button's value | **Needs rendering; partly done.** Chrome creates these from layout objects and user-agent shadow trees. They have no DOM node in the document. With facts the list marker is a node of ours (profile entry `list-marker-node`) and the select popup too (`layout-select-popup`); generated `::before` and `::after` boxes and text (`generated-content-node`), the text of input buttons and the inner editor of text fields (`control-text-node`) are nodes too; the shadow content of date, file, range and select controls is not. HTML-AAM and CSS Content give generated content a place in names (done in the name computation) but not in the tree. |
| `role`: `LayoutTable`, `LayoutTableRow`, `LayoutTableCell` against `table`, `row`, `cell`, `generic` | **Chrome.** Chrome gives the internal layout-table roles to tables it judges to be for layout (a one-cell table, `display: table` boxes) and keeps `table`, `row` and `cell` for data tables. HTML-AAM maps `table` to `table`. Without facts the heuristic is not reproduced. With facts it is (profile entry `layout-table`), fitted on 171 tables of the corpus sample. |
| `ignored`/`ignoredReasons`: a `label` whose control is a checkbox or radio button is ignored with `labelFor`, and its text is `presentationalRole` | **Chrome**, observed on three labels, all of them checkbox or radio labels with the control first. `label` elements around text fields (control after the text) are `LabelText`. Which of "checkbox or radio" and "control first" decides is not determined by the fixtures. A capture of `<label>text <input type=checkbox></label>` and `<label><input type=text> text</label>` would settle it. |
| `ignored`, `ignoredReasons`, `role`: cells and rows of a table with `role=presentation` | **Chrome** differs from the inherited-presentational rule we follow (WAI-ARIA 1.2 section 5.2.7): its `tbody` and `tr` are ignored as `uninteresting` and its `td` are `generic` and exposed. We make them all `presentationalRole`. Observed on one table. |
| `ignored`: `template` | **Chrome**, unexplained: a `template` directly in the body is `generic`, a `template` in a `ul` is ignored. We ignore both (a template has no rendering). |
| `prop.focusable` on `iframe` | **Chrome** does not report an iframe as focusable. HTML section 6.6.3 lists navigable containers as focusable areas, so we do. |
| `value`: `No file chosen` on a file input | **Reproduced** since the second pass (profile entry `input-password-file`). |
| `name`: `mi` text `𝑥` against `x` | **Chrome** applies `text-transform: math-auto` from the MathML Core user-agent style to a single-character `mi`; our cascade does not implement that value. |
| `parent`: children of `b`, `i` and `visibility: hidden` elements | **Representation.** We keep these as ignored nodes (the unpruned `parent` therefore names them); Chrome has no node. `exposedParent` agrees. |
| `parent`, `exposedParent`: `option` and `optgroup` under `MenuListPopup` | **Chrome** puts the options of a drop-down select in a popup node that exists only while laid out; ours are children of the combobox. |
| `oursOnlyIgnored`: `noscript`, `visibility: hidden`, `aria-hidden` | **Representation**, see "Matching". |

Profile entries added or confirmed from this run are in `accessibility-chromium-profile.md` (evidence "observed in Chrome output"). Unobserved and worth a capture: an empty `div` and `span`; `u`, `s`, `small` and other inline formatting elements; a nested list; `<label>` with each control type and position; the other date and time input types; nested tables and `role=presentation` tables with `th`.
