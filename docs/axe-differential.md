# The axe-core differential

`OneHtml.Differential axe` compares what the checker (`RecursiveAI.OneHtml.Accessibility.Validation`) concludes about a page with
what axe-core 4.14.0 concluded about the same page in Chrome for Testing 156.0.8078.47. axe-core is a black box here: it is
injected into the page, run, and only its results are read (`tools/capture`). Nothing in this comparison reads its code.

The comparison is per rule and per element. For each rule it publishes the pages where the rule applies, how many elements
both sides judge the same way, and every kind of disagreement with examples. A disagreement is either fixed in the rule or
described under "Differences" below and in the rule's page in `docs/rules/`. Where ACT and axe disagree, ACT wins.

## Running it

```
OneHtml.Differential axe <capture-dir> [--corpus <corpus-or-stylesheet-dir>] [--out <dir>] [--facts on|off|both] [--jobs n]
                         [--limit n] [--timeout s] [--max-seconds n] [--fresh]
```

- `<capture-dir>` is the output of `tools/capture` (`captures/<sha>/<viewport>/{meta,dom,axe,snapshot}.json.gz`).
- `--corpus` supplies the stylesheets the pages link (a corpus version or a directory of `.css` files), exactly as for `ax`.
- `--facts both` (default) checks every page twice, once without rendered facts and once with the facts from the same capture's
  `DOMSnapshot`. A snapshot the adapter refuses is counted and that page is compared without facts only.
- **Resumable and chunkable.** Each finished capture leaves `<out>/parts/<sha>-<viewport>.json`; running the same command again
  skips them. `--max-seconds n` stops starting captures after n seconds and prints `REMAINING=<count>`. `--fresh` throws the
  parts away (do this after changing a rule). The summary is rebuilt from all parts each time.
- A capture whose comparison throws, hangs or times out is recorded as an error part and does not stop the run.
- `ONEHTML_AXE_DUMP_RULE=<rule> ONEHTML_AXE_DUMP_DIR=<dir>` writes, for that rule, a node listing per page (ours and axe's, side by
  side) to investigate a class.

Output in `<out>`: `summary.md` (per-rule table and the biggest classes), `summary.json` (the same, per mode, with the rules axe
ran that we do not implement and the rules of ours with no axe counterpart), `classes.json` (every class with up to five examples)
and `parts/`. Format `onehtml-axe-differential/1`.

Worker run, as for `ax` (the corpus and its captures are on the worker):

```
bash tools/OneHtml.Differential/run-worker.sh axe ~/onehtml/capture-out/<version> ~/onehtml/out/axe --corpus ~/onehtml/corpus/<version> --jobs 8
```

A sample of 279 pages with 4,471 stylesheets takes about 25 s at three jobs, so a full corpus run is a short job.

## What is compared

Our side is the checker run on **Chrome's serialized DOM** (`dom.json` of the capture), with the page's stylesheets in the cascade
and, in `on` mode, the rendered facts of the same capture. axe ran on the live DOM of that same page, so the two see the same
tree. The results are taken from the `CheckReport` (`ToAxeJson` maps them into the groups below, and the comparison uses the same
mapping), not parsed back from JSON:

| Ours | axe group |
|---|---|
| failed | `violations` |
| passed | `passes` |
| needs-review, needs-render | `incomplete` |
| inapplicable | the rule appears in `inapplicable` (no nodes) |

### Matching

1. **Rule.** Our rule id is axe's id (the catalogue uses axe's ids); a rule naming another axe source is mapped to it.
2. **Element.** axe's node is found in our parse of the same serialized DOM by its **XPath** (axe is asked for `xpath: true`;
   the subset it emits is absolute `name[n]` paths, an optional leading `//name[@id='x']`, and `*[name()='x']`). The unique
   **selector** is the second opinion: if the two name different elements the XPath wins and the node is counted as
   *disagreeing*; if only the selector resolves it is used; if neither does, or the target crosses a frame or shadow root, the
   node is *unresolved* and counted apart. A selector our selector parser rejects is counted too (`unresolved|selector-error`,
   2 nodes in the sample: an attribute name with a non-ASCII character).
3. **Outcome.** A result is compared as a group (violation, pass, incomplete). Needs-review and needs-render both correspond to
   `incomplete`; the report shows how many of the agreeing incompletes were each.

### Normalizations

Each of these is applied before counting, and each is stated in `summary.md`.

- **One verdict per (rule, element).** Several results of one rule on one element, from either side, collapse to the most severe
  (fail, then incomplete, then pass).
- **Text-node targets** count for their parent element; axe only reports elements.
- **Pass-only differences are not disagreements.** axe reports a pass for every candidate it examined; some of our rules report
  passes only where they have a per-element verdict. A pass reported by one side with no result at all on the other
  (`pass|none`, `none|pass`) is counted in its own column and left out of the agreement rate.
- **Container and contents.** axe reports a `table` where we report its cells (`td-has-header`, `td-headers-attr`,
  `table-fake-caption`, `th-has-data-cells`) and the like. An unmatched finding of ours and an unmatched axe finding of the same
  group on an ancestor or descendant element count as one agreement (*contained*). Passes are not paired this way.
- **Groups reported once.** For `duplicate-id`, `duplicate-id-active`, `duplicate-id-aria`, `accesskeys`, `landmark-unique`,
  `landmark-no-duplicate-banner`, `-contentinfo`, `-main` and `identical-links-same-purpose`, axe puts the finding on the first
  member of the group (measured: two banners give one violation) and we put it on every member (ACT 3ea0c8 for ids). An
  unmatched failure or review of ours counts as *collapsed* when axe reported any violation or incomplete of that rule on the
  page. With no axe finding it stays a mismatch.
- **Needs-render is not compared.** Where we cannot decide without rendered facts (`render|…` classes), the node is counted in its own
  column and left out of the agreement rate, whatever axe says. With facts the column is small; without them it is most of the contrast and
  target-size nodes.
- **`region` is compared once per page.** axe reports the wrappers it happens to reach (on Divi-style pages a wrapper passes while its
  children violate, with no rule we could find); we report the topmost element. The page verdict is the strongest non-pass outcome each
  side reports for the rule, and agrees or not as one finding.
- **Enhanced contrast is subsumed by the AA failure.** Where axe reports a `color-contrast` violation it reports `color-contrast-enhanced`
  for the same element as a pass (with the same ratio); our enhanced failure there is the same finding and counts as collapsed. ACT 09o5cg
  fails any text below 7:1, so we keep reporting it.
- **Rules only one side has** are listed, not scored: axe rules we do not implement, and our rules with no axe counterpart.
- **Agreement rate** = (agree + contained + collapsed) / (that + mismatches); unresolved nodes and pass-only differences are outside it.

Mismatch classes are named `ours|axe`, with our precise outcome (`fail`, `pass`, `review`, `render`) and axe's group
(`violation`, `pass`, `incomplete`) or `none` when that side had no result for the element.

## What axe does, from its output

Learned by writing pages and reading the results (`testdata/axe/probes/`, captured in Chrome 156 with axe 4.14.0). The expected
outcomes of the element-id'd cases are in `testdata/axe/expectations.tsv` and are checked by `AxeExpectationsTests` on every build.

- **meta-refresh** (`content="600"`): violation for delays up to 72000 s; **meta-refresh-no-exceptions** violates only delays above
  72000 s, and passes 600 s. Each `meta` is judged separately.
- **meta-viewport**: violation for `user-scalable=no`/`0` and `maximum-scale` below 2 (1.5, 1); pass for 2 and 5. **meta-viewport-large**:
  violation only for `maximum-scale` from 2 up to 5 (2 fails, 5 passes); `user-scalable=no` passes it.
- **bypass**: no node on a page with no links; incomplete ("No valid skip link found") on pages with a link and no heading, no
  landmark and no skip link; pass when any in-page link exists, also one to a missing or hidden target. **skip-link**: inapplicable
  for `href="#missing"`.
- **aria-valid-attr-value**: `aria-labelledby`, `aria-describedby` and `aria-errormessage` (with `aria-invalid="true"`) naming no id:
  incomplete; `aria-controls`, `aria-owns`, `aria-flowto`, `aria-activedescendant`, `aria-details`: violation; `aria-controls` with
  `aria-expanded="false"` passes, and with `aria-haspopup` it is incomplete; a list with one existing id passes; an empty value passes.
- **region**: violation on the topmost element holding content with no landmark inside it; the elements inside it pass. Not content:
  `button` (and its text), `iframe`, inline `svg`, empty elements. Content: text, `img`, `input`, `select`, links. An unnamed `form` and an
  unnamed `section` are not landmarks; `role="region"` is, named or not. Dialogs, alerts, `aria-live` and `header` are. A skip-link
  target is not exempt; the skip link is.
- **table-fake-caption** and **td-has-header** apply to tables with a `th`, a `caption`, or a `scope`/`headers` attribute; not to a grid of
  `td` cells (24 probe tables). `table-fake-caption` fires on a first row of one spanning `td` or `th` even when the table has a caption;
  `td-has-header` counts an empty `th` as no header (the 3 by 3 minimum is ours: every smaller probe table was a plain grid, so the size is not measured), and `td-headers-attr` fires when `headers` names a `td`.
- **aria-braille-equivalent**: incomplete (not a violation) for a braille label without accessible text and for
  `aria-brailleroledescription` without `aria-roledescription`; text content counts as accessible text.
- **aria-prohibited-attr**: `aria-braillelabel` on a generic `div` and `aria-label` on `role="text"` pass; `aria-label` on a `presentation`
  table, a `generic` or `paragraph` role and a `span` with a conflicting role list is incomplete.
- **definition-list**: a `dl` with at least one `dt` and one `dd` passes even with an unpaired `dt`.
- **p-as-heading** (`p-heading.html`): a paragraph styled bold alone, italic alone, or with `strong`/`b` passes. Bold together with a size
  well above the next paragraph's (24 px against 16 px) is a violation, also when the next paragraph is bold; a size 1.5 times the next
  paragraph's alone is incomplete; 1.33 times with bold passes. A long paragraph passes. Our rule fails two signs together (bold, italic, size
  at least 1.4 times the next paragraph's), reviews a size alone, and passes the rest.
- **identical-links-same-purpose**: one incomplete per group, on its first link. Relative and absolute targets are compared after resolving
  against the page address.
- **hidden-content** (`hidden-content.html`): every element is a pass except an element whose own computed `display` is none (the `hidden`
  attribute included), or whose `visibility` is hidden, and that holds text or an image, frame, media or object below it: incomplete. A child
  of such an element whose own display is not none passes (a `li` inside a hidden `ul`), but a child that sets `display: none` itself is
  incomplete again. An empty hidden element, one with only a comment or white space (`&nbsp;` counts), `aria-hidden`, `<details>` content, `content-visibility`,
  `template`, `script` and an image inside a hidden parent are passes. Frames, images and media that are hidden themselves are incomplete without text.
- **image-redundant-alt** (`img-redundant.html`): the image's `alt` equals, ignoring case and runs of white space, all the text of its link,
  button, `p` or `li` besides the image: violation (text before or after, text in nested `p`/`div` inside the link). Text that merely contains
  the alt (`alt="Send Flowers"` beside "Send Flowers Hand delivered") passes, as do a `div` or `span` parent, an `aria-label` that
  duplicates an image-only link, and two images in one link.
- **landmark-one-main** (`no-main.html` and probes in `misc-probes`): a rendered element with `role="dialog"` (or a `dialog` element) makes the
  rule pass on a page with no main, also with `aria-hidden="true"` and when empty; `role="alertdialog"`, `display: none` and the `hidden`
  attribute do not. No other cause was found for the 6 sample pages without a dialog.
- **color-contrast-enhanced**: reported as a pass wherever the same element violates `color-contrast` (see Normalizations).
- **color-contrast** text colour of form controls: black (`ButtonText`/`FieldText`) unless the author sets `color` on the control; not the
  parent's colour.

## Results

Sample: 279 corpus pages (a seeded random sample of corpus 2026-10a, one viewport, stylesheets served from the corpus), captured
with `--jobs 1`, compared with `--timeout 300 --corpus <the sample's store>` (the default 30 s timeout is too short for the largest
pages with all rules and facts). Checker as in this repository after main of 2026-10-26 (contrast, presentation, media, ACT-gap
rules, the tree reading facts). The table covers 103 rules. Ratios are over nodes, as defined above.

| | Without facts | With facts |
|---|---:|---:|
| Pages compared | 279 | 278 (1 snapshot refused) |
| Agreeing nodes (agree + contained + collapsed) | 192,003 | 209,526 |
| Mismatching nodes | 9,072 | 11,096 |
| Agreement | 95.49 % | 94.97 % |
| Agreement without `hidden-content` and `region` | 95.59 % | 95.05 % |
| Needs-render nodes (not compared) | 33,731 | 4,521 |
| Unresolved axe nodes | 257 | 248 |

The earlier sample (before the contrast, target-size and presentation rules existed) agreed on 90.85 % because `hidden-content` and `region`
were then counted by node; the rules fixed since, and the comparisons now made per page (`region`) or by group, put them in line. The new
mismatch mass is mostly the contrast rules (below) and `link-in-text-block`. Excluding the contrast, target-size, `link-in-text-block` and
`hidden-content` rules, the rest agree on about 99 % of nodes. Facts matter for the rules that need them (`scrollable-region-focusable`,
`label-content-name-mismatch`, `target-size`, and the contrast rules, below).

The first 40 rules by volume (without facts, as of the final run; `summary.md` of a run has all of them):

| Rule | Pages axe / ours | Nodes axe / ours | Agree (incl. contained, collapsed) | Mismatch | Agree % |
|---|---|---|---:|---:|---:|
| hidden-content | 279 / 205 | 219068 / 4252 | 4178 | 406 | 91.1 |
| region | 276 / 270 | 130091 / 722 | 174 | 3 | 98.3 |
| color-contrast | 272 / 269 | 38237 / 41955 | 33920 | 3249 | 91.3 |
| color-contrast-enhanced | 272 / 269 | 38237 / 41955 | 31132 | 2595 | 92.3 |
| target-size | 263 / 264 | 22324 / 28325 | 506 | 74 | 87.2 |
| link-name | 262 / 262 | 24733 / 24196 | 24118 | 24 | 99.9 |
| duplicate-id | 273 / 273 | 16829 / 17113 | 17092 | 23 | 99.9 |
| listitem | 243 / 243 | 16518 / 16574 | 16277 | 2 | 100.0 |
| identical-links-same-purpose | 262 / 243 | 16941 / 9752 | 5178 | 91 | 98.3 |
| nested-interactive | 257 / 264 | 6609 / 10088 | 6573 | 0 | 100.0 |
| aria-valid-attr | 198 / 211 | 3203 / 6681 | 3203 | 0 | 100.0 |
| aria-valid-attr-value | 198 / 211 | 3203 / 6665 | 3152 | 57 | 98.2 |
| image-alt | 245 / 249 | 4351 / 4594 | 4350 | 3 | 99.9 |
| list | 245 / 245 | 3710 / 3704 | 3650 | 1 | 100.0 |
| aria-allowed-attr | 198 / 198 | 3203 / 3176 | 3158 | 3 | 99.9 |
| aria-prohibited-attr | 198 / 198 | 3203 / 3176 | 3039 | 124 | 96.1 |
| empty-heading | 259 / 259 | 3200 / 3171 | 3169 | 1 | 100.0 |
| heading-order | 259 / 259 | 3200 / 3171 | 3170 | 0 | 100.0 |
| image-redundant-alt | 245 / 213 | 4351 / 1822 | 1765 | 14 | 99.2 |
| link-in-text-block | 167 / 213 | 812 / 4021 | 661 | 1794 | 26.9 |
| aria-allowed-role | 192 / 173 | 3031 / 1607 | 1601 | 28 | 98.3 |
| aria-hidden-focus | 164 / 164 | 1834 / 2004 | 1797 | 44 | 97.6 |
| avoid-inline-spacing | 168 / 26 | 3352 / 231 | 218 | 0 | 100.0 |
| presentation-role-conflict | 130 / 169 | 1344 / 2221 | 1341 | 3 | 99.8 |
| aria-roles | 174 / 174 | 1635 / 1634 | 1617 | 5 | 99.7 |
| aria-deprecated-role | 174 / 174 | 1635 / 1627 | 1615 | 0 | 100.0 |
| aria-conditional-attr | 198 / 1 | 3203 / 1 | 1 | 0 | 100.0 |
| duplicate-id-aria | 172 / 172 | 1559 / 1600 | 1575 | 25 | 98.4 |
| landmark-unique | 242 / 242 | 1339 / 1698 | 1682 | 16 | 99.1 |
| aria-required-attr | 174 / 146 | 1655 / 1381 | 1369 | 0 | 100.0 |
| p-as-heading | 145 / 145 | 1130 / 1132 | 883 | 18 | 98.0 |
| button-name | 189 / 189 | 962 / 919 | 911 | 0 | 100.0 |
| duplicate-id-active | 192 / 206 | 773 / 1026 | 800 | 7 | 99.1 |
| label-title-only | 156 / 143 | 913 / 816 | 815 | 1 | 99.9 |
| label | 151 / 151 | 822 / 822 | 819 | 3 | 99.6 |
| form-field-multiple-labels | 156 / 88 | 913 / 574 | 574 | 3 | 99.5 |
| label-content-name-mismatch | 93 / 111 | 652 / 729 | 587 | 38 | 93.9 |
| dlitem | 25 / 26 | 428 / 448 | 428 | 0 | 100.0 |
| skip-link | 67 / 169 | 83 / 562 | 83 | 108 | 43.5 |
| tabindex | 87 / 7 | 574 / 32 | 32 | 0 | 100.0 |

Rules axe ran that this checker does not report: `aria-roledescription`, `frame-tested` (axe's own frame check, not applicable).
Rules of ours with no axe counterpart: `element-lang-matches-content`, `html-lang-matches-content`, `image-name-descriptive`, `link-in-context-descriptive` (ACT rules,
needs-review).

### Pages written for the differential

`testdata/axe/pages` and `testdata/axe/probes` are pages written for this repository, one question each; `expectations.tsv` records
axe's outcome for the elements with an id. A fifth column marks the six rows where our verdict deliberately differs (listed below).

| Page | Settles |
|---|---|
| `fallback-roles`, `role-text`, `braille`, `checkbox-aria-checked`, `broken-refs`, `duplicate-ids` | the open questions of the ARIA and id rules (below) |
| `table-headers`, `table-probes` | `td-has-header`, `table-fake-caption` and the other table rules (the two rules were written from one-line descriptions) |
| `idrefs`, `dup-aria` | every ID-reference attribute with a dangling id; duplicated ids referenced by ARIA |
| `meta-refresh`, `viewport`, `bypass-*`, `region-*`, `links-same`, `misc-probes` | the axe behaviours in "What axe does" |
| `axe-misc`, `labels`, `date-inputs`, `empty-and-inline`, `nested-lists`, `nested-tables`, `template-list`, `no-lang` | broad coverage, and the Chrome accessibility-tree pages (below) |

The golden page `tests/OneHtml.Accessibility.Validation.Tests/Golden/aria-page.html` was captured as well.

## Differences

Each mismatch class of the sample and of the written pages is fixed, kept on purpose, or open. Dispositions are also in the rule pages
(`docs/rules/aria.md`, `structure.md`, `tables.md`, `names.md`, "Measured against axe-core 4.14.0").

**Fixed because of the differential:** dangling ID references (`aria-valid-attr-value`, needs-review or fail by attribute);
`aria-braille-equivalent` (needs-review, text content counts); native radio in `aria-conditional-attr`; the data-table gate of
`td-has-header` and `table-fake-caption`; complementary inside `main`; `meta-viewport-large` range; `region` content definition and
unnamed `role="region"`; `hidden-content` (own computed display or visibility, embedded objects, `&nbsp;`; 8,492 review|pass → 74, 887
none|incomplete → 312); `image-redundant-alt` (equality of the container's other text, not containment: fail|pass 124 → 0);
`p-as-heading` (two signs, observed thresholds: fail|pass 67 → 0); `identical-links-same-purpose` (relative links resolve against
the page address: review|none 811 → 13, review|pass 251 → 42); the text colour of form controls in the contrast rules (fail|pass 313 → 275).

**Kept on purpose** (the reason is the spec or ACT, so it is not a bug on our side; the larger classes first):

- `aria-prohibited-attr` fail|incomplete (103), fail|pass (11), pass|incomplete (8): the role data prohibits naming where axe warns or passes.
- `skip-link` fail|none (108): a fragment link to a missing target fails; axe is inapplicable.
- `aria-hidden-focus` fail|incomplete (35), fail|none (7): axe is incomplete while a modal dialog is open.
- `duplicate-id-aria` fail|incomplete (25): axe reports incomplete without saying why, for ids a label or `aria-labelledby` points at; we fail (the reference is ambiguous).
- `bypass` fail|none (13), fail|incomplete (3): axe needs a link and calls it incomplete.
- `aria-allowed-role`, `aria-roles`, `aria-required-attr`, `aria-required-children`, `aria-required-parent`, `focus-order-semantics`,
  `meta-refresh*`, `definition-list`, `empty-table-header`, `td-headers-attr`, `th-has-data-cells`, `image-alt` (focusable empty alt),
  `link-name` (names from `title`): see the tables in the rule pages.
- `label-content-name-mismatch` render|none (85), render|pass (31), render|violation (3): without facts we ask for the layout; with facts these
  classes are gone. What remains with facts: `none|incomplete` (25, axe's single-character rule), `fail|none` (7), `none|violation` (5), `pass|violation` (6).

**Open** (not explained; each has examples in `classes.json`):

- `landmark-one-main` fail|pass (17): axe passes a page with no main when a rendered `dialog` (not `alertdialog`) exists (probes
  `no-main`, `misc-probes`; 11 of the 17 sample pages have one). We keep failing: a dialog is not the page's main content. 6 pages have no
  explanation.
- `region` (compared per page, 98.3 %): the two remaining disagreements are pages where axe has no violation and we do; the granularity of
  axe's wrappers is not reproduced and not scored.
- `hidden-content` none|incomplete (312) and review|pass (74): hidden elements axe flags that hold only elements without text or media
  (the rule is an advisory; whether a `button > span` or an empty-looking container is "content" to axe was not pinned down), and hidden
  elements with a tracking pixel we flag.
- `identical-links-same-purpose` review|pass (42), none|incomplete (24), review|none (13), pass|incomplete (10): which member of a group axe
  names, percent-encoding of non-ASCII targets, and `tel:` targets.
- `image-redundant-alt` fail|none (14): images inside `noscript`, which axe does not see as elements.
- `p-as-heading` pass|violation (6), pass|incomplete (4), fail|incomplete/none (3).
- `aria-allowed-role` none|violation / none|incomplete (22), `aria-dialog-name` none|violation (7), `landmark-unique`, `duplicate-id` fail|none (21).


## Contrast, spacing and target size

Run with the contrast, text spacing, orientation, target size and media rules, which were added separately from the rules this page otherwise covers (the
comparison used a checkout with those rules and this command; it reproduces with `axe` wherever both are present). Pages: the ACT `78fd32` failed examples and inapplicable example 2,
the four image-background pages of ACT `afw4f7` (`b84e9e`, `11ec27`) and `09o5cg` (`d30f6d`, `051137`), and `testdata/rendered/pages/target-size.html`
with its capture. Images are blocked in captures, so the four image pages render without their image.

- **avoid-inline-spacing, ACT 78fd32.** All six failed examples are violations in axe (`important-line-height`). Without facts we report
  needs-render for the six and for the inapplicable example; **with facts all seven agree** (six fail, one pass). The text boxes turn the
  cases that were not applicable into real verdicts.
- **color-contrast and color-contrast-enhanced on the four image pages.** axe: incomplete for contrast ("background color could not be determined
  due to a background image"). Ours: needs-render for both rules, with or without facts, because the image is absent from the snapshot and no
  background sample exists; both sides decline to guess. The sample path (`background samples`) cannot be exercised by these pages: the captured
  Chrome has no image to sample.
- **target-size** on the fixture page (17 targets: a crowded and a spaced link row, an inline link pair, a large link, a plain checkbox and a
  16 px author-sized checkbox and buttons): without facts all are needs-render (axe: 6 violations, 8 passes); **with facts all 14 targets axe reports
  agree**, and the three inline links in the sentence pass here with no axe result (axe does not report inline links).
- On the solid-background text of these pages all 11 `color-contrast` nodes of the ACT pages and all 16 of the fixture page agree.

### The contrast, target-size and link rules on the sample (with facts)

Classes of the 279-page sample, in nodes, and what they are:

- `color-contrast` pass|incomplete (2,046), and the same for the enhanced rule (1,133): text made of symbols only (icon-font glyphs). axe says
  "only non-text characters" and leaves it incomplete; we pass it, since ACT afw4f7 does not apply to text without letters or digits. Kept.
- `color-contrast` none|incomplete (580): axe is incomplete for inputs without a value, hidden elements, 1:1 colour pairs and elements it
  thinks are overlapped; we report nothing for them. Kept (empty fields and hidden text have nothing to read).
- `color-contrast` fail|incomplete (374): axe cannot determine the background because another element overlaps; with facts we know the
  stacking and read a solid colour, and fail. Kept (this is what the facts are for).
- `color-contrast` fail|pass (275; enhanced 726) and none|violation (17): **open.** The one cause found was the text colour of form controls
  (fixed above). The rest are links and spans whose colour our cascade takes from a low-specificity `a` rule where Chrome has black
  (page `1cfe36814e3e`, `#menu-item-986 > a`): a cascade difference to find in the style engine, not a contrast-rule fault.
- `link-in-text-block` fail|none (1,652), fail|incomplete (82), fail|pass (25): **open.** We fail links in navigation blocks that axe does
  not consider text blocks at all; the definition of "block that holds other visible text" is broader than axe's. The rule's 27 % agreement
  is this one class.
- `target-size` fail|pass (751), pass|violation (436), fail|incomplete (492): **open.** Not analysed beyond the sample: axe's exceptions
  (inline targets, spacing circles, targets in the same block) and ours differ in ways the written fixture does not exercise.
- `css-orientation-lock` none|incomplete (181): axe is incomplete on every page with a `@media (orientation)` rule it cannot evaluate; we
  report nothing when no locking transform is present. Kept.
- `skip-link` fail|none: see above.

## Chrome accessibility-tree pages

The wanted pages were captured and run through `ax` (`empty-and-inline`, `nested-lists`, `labels`, `date-inputs`, `nested-tables`, `template-list`
and the rest of `testdata/axe/pages`; 19 pages, 960 matched nodes). New classes, none fixed here (they belong to the tree computation):

- **Layout tables:** Chrome's `LayoutTable`/`LayoutTableRow`/`LayoutTableCell` against our `table`/`row`/`cell` for nested and presentational tables with
  `th` (44 nodes, 3 pages). Our data/layout decision differs from Chrome's for these shapes.
- **Presentational tables:** Chrome keeps a `generic` where we ignore the table as `presentationalRole` (7).
- **Labels:** a `label` whose control precedes it: Chrome ignores it (`labelFor`), we keep a `LabelText` (14 `exposedParent`, 7 `ignored`).
- **Date and time inputs:** Chrome's `DateTime` and inner `spinbutton`s (16 nodes) are not in our tree.
- **`dt` in a `template`-containing `dl`:** Chrome names the `term` from its text (4 nodes); ours is empty.
- **`<ol><span>`**: a `generic` we expose and Chrome does not (13).
- **`u`, `s`, `small`:** text inside is attached to a `paragraph` by Chrome and a `generic` by us (`exposedParent`, 3). `role=text` children of a link or button
  reparent differently (4).
- Smaller: `select` options' `selected` (4), `aria-haspopup` on `combobox` (2), `accesskey` as `keyshortcuts` (4), `iframe` focusable (7), listitem `level` (2).

## Not done

- A full-corpus run (a gate window).
- Frames and shadow roots: nested targets are counted, not compared.
- The class analysis of `target-size`, `link-in-text-block` and the remaining `color-contrast` fail|pass nodes (above).
- The full-corpus command (needs a window; claims the worker lock; `--timeout 300` because the largest pages exceed 30 s with all rules):
  `bash tools/OneHtml.Differential/run-worker.sh axe ~/onehtml/capture-out/2026-10a ~/onehtml/out/axe --corpus ~/onehtml/corpus/2026-10a --jobs 8 --timeout 300`.
  It is resumable (run it again, or use `--max-seconds`); add `--facts on` or `off` to run one mode.
