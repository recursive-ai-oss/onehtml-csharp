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
bash tools/OneHtml.Differential/run-worker.sh ax ~/onehtml/capture-out/<version> ~/onehtml/out/ax --corpus ~/onehtml/corpus/<version> --jobs 8
```

## Results with and without rendered facts

Chrome for Testing 156.0.8078.47 (protocol 1.3), headless, 1280x800, page script off; .NET 10 runtime; run with `--facts both --jobs 2` on an AMD Ryzen 9 7950X3D worker (32 threads), about 90 seconds per 2,100 pages. Sample C is 2,133 corpus captures (every 6th by hash, starting at the 1st) and sample D is 2,129 others (every 6th, starting at the 4th); the rules in the `layout-*` entries were fitted on about 640 other captures and, for the table rule, on the tables of 6,000 pages that overlap C and D, so the rates are not a clean held-out test of the table rule. The fixtures are the 16 hand-written pages of `testdata/rendered/pages` (the earlier ones plus `layout.html` and `svgprobe.html`). Rates are mismatching nodes over the times the field was compared; the node counts are in the report. The full capture set of 12,750 pages has not been run with this version.

| field | C without | C with | D without | D with | fixtures without | fixtures with |
|---|---:|---:|---:|---:|---:|---:|
| ignored | 14.0% | 0.66% | 15.7% | 0.76% | 2.9% | 2.8% |
| hidden | 0.17% | 0.13% | 0.19% | 0.16% | 0% | 0% |
| ignoredReasons | 16.7% | 4.0% | 19.1% | 3.9% | 4.8% | 4.3% |
| role | 0.63% | 0.20% | 0.64% | 0.17% | 3.6% | 0% |
| name | 4.9% | 4.4% | 4.7% | 4.2% | 0.6% | 0.5% |
| parent | 9.8% | 2.0% | 9.6% | 2.0% | 2.7% | 1.7% |
| exposedParent | 31.3% | 1.0% | 29.9% | 1.1% | 3.6% | 2.6% |
| chromeOnly | 10.3% | 1.9% | 10.6% | 2.0% | 8.8% | 7.0% |
| oursOnly | 7.4% | 0.48% | 7.4% | 0.52% | 0.3% | 0.3% |
| prop.focusable | 0.17% | 0.17% | 0.16% | 0.16% | 6.3% | 6.3% |
| prop.level | 0.28% | 0.24% | 0.55% | 0.53% | 0% | 0% |

No field is worse with facts. The facts-free role rate also falls (about 1.7% to 0.6% on the earlier sample) because `svg` and the non-landmark `header` and `footer` now take Chrome's roles without facts (profile entries `svg-root-role`, `section-header-footer`). What is left:

| Class | Verdict |
|---|---|
| `oursOnlyIgnored`: `visibility: hidden`, `aria-hidden` and plain inline or block containers | **Representation.** We keep ignored nodes that Chrome does not list at all (an exposed tree is the same without them). Plain inline containers Chrome ignores are no longer nodes of ours (profile entry `layout-generic-pruning`); the others stay. |
| `chromeOnlyIgnored`: `notRendered`, `aria-hidden` subtree | **Representation.** Chrome lists nodes that are not rendered or hidden; ours have no node (not rendered) or are listed elsewhere. |
| `chromeOnly`: `StaticText` | **Chrome**, unexplained for 614 drawn spaces between inline elements; the rest is generated content, `::before` and `::after` text, and text of user-agent shadow trees (form controls, `video`). Generated text boxes could supply the first; not done. |
| `chromeOnly`: `generic` | **Needs the user-agent shadow tree**: the inner editor of a text field and the controls of `video` and `audio`. |
| `role`: `cell`, `row`, `table` against `LayoutTableCell`, `LayoutTableRow`, `LayoutTable` | **Chrome**, partly open: the data-table versus layout-table rule (profile entry `layout-table`) now reads only a table's own cells (nested tables were being counted) and treats 20 or more rows as a data table. About 50 small tables of 4,500 with no evidence of any kind are still data tables in Chrome; a background colour on the first cell is the only feature that separates them, and weakly. |
| `role`: `image` against `SvgRoot` | **Fixed, not layout dependent** (profile entry `svg-root-role`): Chrome gives an `svg` the role `image`, and `SvgRoot` only when something inside it is exposed. The `svgprobe` fixture separates the cases. |
| `sectionheader`, `sectionfooter` | **Fixed, not layout dependent** (profile entry `section-header-footer`): a `header` or `footer` that is not a landmark is `sectionheader` or `sectionfooter`. |
| `prop.focusable` on `iframe` | **Chrome** does not report an iframe as focusable (51 nodes). Unchanged. |
| `parent`: options under `none` | **Chrome** puts an ignored node between the popup and the options of a select with option groups (3 nodes in `forms.html`). |

Chrome's `getFullAXTree` carries no bounding box, off-screen state or scrollable property (none of 121,682 nodes on 120 corpus pages), so the export has none; clipped, zero-size and off-screen elements stay in the tree (the `overflow` and `positioned` fixtures).

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
