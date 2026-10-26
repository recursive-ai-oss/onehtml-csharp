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

Chrome for Testing 156.0.8078.47 (protocol 1.3), headless, 1280x800, page script off; .NET 10 on the AMD Ryzen 9 7950X3D worker (32 threads), `--facts both --jobs 2 --limit 2100`, about 125 seconds. The sample is the first 2,100 captures by hash: 2,095 compared (1,840,086 Chrome nodes, 1,954,001 of ours, 1,759,749 matched, 264,876 of them by the anonymous-node rule) and 5 refused (a custom element whose shadow root the snapshot omits, and two whose text differs in line breaks). Rates are mismatching nodes over the times the field was compared, with the number of nodes in brackets. "Before" is the same pages with the tree as it stood before this round of fixes (facts on). Fields that never differ are left out; the run's `summary.md` has all of them.

| field | before (facts) | without facts | with facts |
|---|---:|---:|---:|
| ignored | 0.665% | 13.282% (199313) | 0.291% (5114) |
| hidden | 0.375% | 0.088% (1323) | 0.06% (1053) |
| ignoredReasons | 5.37% | 1.681% (1380) | 0.124% (491) |
| role | 0.218% | 0.485% (5914) | 0.089% (1216) |
| name | 1.53% | 0.658% (8027) | 0.591% (8027) |
| nameSource | 0.0063% | 0.014% (102) | 0.012% (102) |
| description | 1.05% | 0.151% (1836) | 0.135% (1836) |
| parent | 1.75% | 0.422% (6332) | 0.307% (5398) |
| exposedParent | 0.81% | 23.642% (354777) | 0.389% (6851) |
| chromeOnly | 1.86% | 11.866% (164933) | 1.979% (27510) |
| oursOnly | 0.372% | 8.231% (126706) | 0.347% (4733) |
| chromeOnlyIgnored | 11.6% | 38.774% (174511) | 11.737% (52827) |
| oursOnlyIgnored | 32.3% | 79.548% (341912) | 32.173% (189519) |
| prop.checked | 0.388% | 0.039% (2) | 0.039% (2) |
| prop.disabled | 5.17% | 6.623% (20) | 6.931% (21) |
| prop.editable | 1.13% | 0.028% (1) | 0.028% (1) |
| prop.expanded | 0.637% | 0.096% (5) | 0.097% (5) |
| prop.focusable | 0.136% | 0.02% (58) | 0.02% (58) |
| prop.hasPopup | 64.7% | 0.635% (19) | 0.635% (19) |
| prop.invalid | 94% | 32.836% (22) | 32.836% (22) |
| prop.level | 0.538% | 0.011% (21) | 0.011% (22) |
| prop.posinset | 100% | 100% (28) | 100% (28) |
| prop.readonly | 17.9% | 17.949% (7) | 17.949% (7) |
| prop.roledescription | 5.07% | 5.069% (11) | 5.069% (11) |
| prop.selected | 1.85% | 0.377% (58) | 0.377% (58) |
| prop.setsize | 100% | 100% (28) | 100% (28) |
| prop.url | 3.04% | 1.061% (2832) | 1.061% (2832) |
| prop.valuemax | 2.57% | 2.572% (8) | 2.572% (8) |
| prop.valuemin | 1.93% | 1.929% (6) | 1.929% (6) |
| prop.valuetext | 9.57% | 9.565% (11) | 9.565% (11) |

Without facts the tree cannot know what Chrome did not draw, so `ignored`, `exposedParent`, `chromeOnly` and `oursOnly` are high; with facts they are the figures above. No field is worse with facts than without by more than a few nodes (`prop.disabled` 20 to 21 nodes).

### Classes

A class is a field plus a pattern (see "Output"). The run has 62 classes above 0.1% of their field's compared count (for the small property fields, above one node). Dispositions:

| Class | Count | Disposition |
|---|---:|---|
| `oursOnlyIgnored`: `none` with `notVisible`, `notRendered`, `ariaHiddenSubtree`, `ariaHiddenElement`, `uninteresting`, `emptyAlt`, `presentationalRole` | about 190,000 nodes | **Representation, deliberate.** Our tree keeps every DOM node with its hidden and ignored reasons so that edits and diffs map back to the markup. Chrome omits nodes it has no use for. An ignored node is never part of the exposed tree, and the exposed trees agree (`exposedParent` 0.53%). Not fixed. |
| `chromeOnlyIgnored`: `notRendered`, `ariaHiddenSubtree`, `uninteresting`, `presentationalRole`, `notVisible` and no reason | about 52,000 nodes | **Representation.** Chrome creates ignored nodes (the children of `display: none` and `aria-hidden` subtrees it needs for relations, the `uninteresting` wrappers that carry layout objects) that have no DOM node of ours or are folded into a parent. Ignored, so not exposed. |
| `ignoredReasons` (491 nodes, 0.12%) | 491 | **Closed to near the threshold.** The rest is `uninteresting` against no reason on SVG graphics elements (profile entry `layout-generic-pruning` covers HTML only); Chrome gives `notRendered` to `title`, `desc` and some `path` elements and `stop` under `defs` and `uninteresting` to others, which depends on layout objects we do not model. |
| `ignored`: Chrome `generic`, ours ignored `uninteresting` (2,026 on 329 pages) | 2,026 | **Open, layout heuristic.** An attribute-free `span` or `div` with text inside an `a`, `button` or flex item. Chrome keeps about 40 of each 1,000 such spans where we drop them; among attribute-free spans Chrome exposes mostly those whose computed display is `inline-block` (which our rule already keeps) and ignores block-level ones, so the residue depends on something the capture's styles do not show. Not traced further. |
| `ignored`: Chrome `image`, ours ignored `emptyAlt` (362 on 81 pages) | 362 | **Deviation.** An `img` with `alt=""` and a `title` is an `image` in Chrome 156 (47 of 47 in 400 pages); HTML-AAM and WPT `html-aam/roles-contextual.html` (`el-img-empty-alt-title`) give it no role. Kept at the specification's answer. |
| `hidden` (1,053 nodes, 0.06%) | 1,053 | **Below the threshold.** The `ariaHiddenElement` against `ariaHiddenSubtree` choice is fixed (an element with its own `aria-hidden=true` is the element even inside another, profile entry `ignored-reasons`); a `wbr` is `notRendered` (profile entry `wbr-not-rendered`). What is left is `notRendered` subtrees of `display: none` ancestors that Chrome lists. |
| `parent`, `exposedParent` against `none` or `generic` parents | 5,400 and 9,300 | **Follows from the two ignored classes above**: the nearest paired ancestor differs when one side has an extra generic. |
| `chromeOnly`: `generic` (11,206) and `StaticText` (10,271) | 21,500 | **Needs layout text boxes and user-agent shadow trees.** Chrome adds a `generic` between a link and its text or image, text for `::before` and `::after` generated content (a "×" in a button), separators, and the inner editor and controls of form controls and media. Generated text boxes could supply part of it; not done. |
| `oursOnly`: `StaticText` (2,888), `generic` (1,495) | 4,400 | **Open.** Text nodes Chrome merges with a neighbour or leaves out under `li`; generics of ours it prunes. |
| `name`: `link differs` (4,592 on 262 pages) | 7,800 | **Mostly spacing.** Chrome puts a space around text of boxes that are blocks in layout; the blockification of positioned and floated elements (profile entry `positioned-blockification`) removed the largest group. What is left is spacing around flex and grid items and inline-block content, which needs layout; Chrome also includes the text of some hidden elements. Documented, not fixed. |
| `role` (1,216 nodes, 0.089%) | 1,216 | **Below the threshold.** What is left is `cell`, `row`, `table` against `LayoutTableCell`, `LayoutTableRow`, `LayoutTable` (the data-table rule of profile entry `layout-table`: small tables with no evidence of any kind that Chrome still treats as data tables) and a few `image` against `SvgRoot`. |
| `description`, `nameSource` | 1,783 and 54 | Below 0.15% of the field; the remainder is the `name` classes. |
| `prop.url`: `image` (400, one page) and `srcset` w-descriptor cases | 2,927 | **Deviation.** Chrome reports the image chosen from `srcset` and `picture` for its viewport and density; we state the `src`, because choosing needs the layout width. |
| `prop.disabled`, `invalid`, `hasPopup`, `readonly`, `selected`, `valuemin`, `valuemax`, `valuetext`, `roledescription`, `posinset`, `setsize` | 7 to 28 nodes each | **Open, small.** Disabled options inside an `optgroup`; invalid combobox and radio states from constraint validation; `hasPopup` on custom comboboxes and disclosure triangles; readonly on comboboxes; `posinset` and `setsize` that we state for menu items and Chrome does not; `valuemin` and `valuemax` for a number input whose bounds it reports as 0; `valuetext` of a progress bar; `roledescription` text that Chrome decodes twice (`KunngjÃ¸ring`: a Chrome defect we do not reproduce). The rates look large because the denominators are tiny (28 nodes). |
| `value` | 66 | **Chrome user-interface text and form state.** "No file chosen", the selected option text of a select with `text-transform`, password bullets, the text of a textarea. Not reproduced. |
| `rel.labelledby`, `rel.controls` | 298 and 14 | **Open.** Chrome lists the label for a radio, checkbox and textbox where our name came from another source; navigation landmarks whose label target is not in Chrome's tree. |

The classes marked open were not traced to a specification or a Chrome rule. Every one is below 1% of its field except the small property fields, and none is in `role` above 0.09%.

## Findings on the fixture pages

The fixture pages (`testdata/rendered/pages`) were captured with Chrome 156 (headless, script off, 1280×800). After the fixes listed in the profile, what is left is below, each with a verdict. "Chrome" means Chrome's choice where the specifications leave room or are silent; "ours" means we do not yet do what Chrome does and no specification says otherwise. Evidence is Chrome's output on the page named.

| Class | Verdict |
|---|---|
| `chromeOnly`: `ListMarker`, generated `StaticText` (`::before`/`::after` content), `LineBreak`, `MenuListPopup`, the shadow content of date, number, textarea and select controls, the `StaticText` of a submit button's value | **Needs rendering.** Chrome creates these from layout objects and user-agent shadow trees. They have no DOM node in the document. With facts the list marker is a node of ours (profile entry `list-marker-node`) and the select popup too (`layout-select-popup`); generated `::before` and `::after` text and shadow content are still not nodes. HTML-AAM and CSS Content give generated content a place in names (done in the name computation) but not in the tree. |
| `role`: `LayoutTable`, `LayoutTableRow`, `LayoutTableCell` against `table`, `row`, `cell`, `generic` | **Chrome.** Chrome gives the internal layout-table roles to tables it judges to be for layout (a one-cell table, `display: table` boxes) and keeps `table`, `row` and `cell` for data tables. HTML-AAM maps `table` to `table`. Without facts the heuristic is not reproduced. With facts it is (profile entry `layout-table`), fitted on 171 tables of the corpus sample. |
| `ignored`/`ignoredReasons`: a `label` whose control is a checkbox or radio button is ignored with `labelFor`, and its text is `presentationalRole` | **Chrome**, observed on three labels, all of them checkbox or radio labels with the control first. `label` elements around text fields (control after the text) are `LabelText`. Which of "checkbox or radio" and "control first" decides is not determined by the fixtures. A capture of `<label>text <input type=checkbox></label>` and `<label><input type=text> text</label>` would settle it. |
| `ignored`, `ignoredReasons`, `role`: cells and rows of a table with `role=presentation` | **Chrome** differs from the inherited-presentational rule we follow (WAI-ARIA 1.2 section 5.2.7): its `tbody` and `tr` are ignored as `uninteresting` and its `td` are `generic` and exposed. We make them all `presentationalRole`. Observed on one table. |
| `ignored`: `template` | **Chrome**, unexplained: a `template` directly in the body is `generic`, a `template` in a `ul` is ignored. We ignore both (a template has no rendering). |
| `prop.focusable` on `iframe` | **Chrome** does not report an iframe as focusable. HTML section 6.6.3 lists navigable containers as focusable areas, so we do. |
| `value`: `No file chosen` on a file input | **Chrome** user-interface text (locale dependent). Not reproduced. |
| `name`: `mi` text `𝑥` against `x` | **Chrome** applies `text-transform: math-auto` from the MathML Core user-agent style to a single-character `mi`; our cascade does not implement that value. |
| `parent`: children of `b`, `i` and `visibility: hidden` elements | **Representation.** We keep these as ignored nodes (the unpruned `parent` therefore names them); Chrome has no node. `exposedParent` agrees. |
| `parent`, `exposedParent`: `option` and `optgroup` under `MenuListPopup` | **Chrome** puts the options of a drop-down select in a popup node that exists only while laid out; ours are children of the combobox. |
| `oursOnlyIgnored`: `noscript`, `visibility: hidden`, `aria-hidden` | **Representation**, see "Matching". |

Profile entries added or confirmed from this run are in `accessibility-chromium-profile.md` (evidence "observed in Chrome output"). Unobserved and worth a capture: an empty `div` and `span`; `u`, `s`, `small` and other inline formatting elements; a nested list; `<label>` with each control type and position; the other date and time input types; nested tables and `role=presentation` tables with `th`.
