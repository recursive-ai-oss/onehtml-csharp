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

A 1,000-page sample takes about 3 minutes of checking at two jobs, both modes.

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

Sample: the first 1,000 captures of corpus 2026-10a in sha256 order (13,857 pages exist; the full-corpus run is below under "Not done"),
one viewport, stylesheets served from the corpus, compared with `--jobs 2 --timeout 120 --corpus <the corpus>`. Checker and tree as in this repository
at the commit that holds this file; Chrome for Testing 156.0.8078.47, axe-core 4.14.0, SDK 10.0.402, run on a 7950X3D worker (16 cores, 32 threads,
load 2 to 17 from other work while it ran). Ratios are over nodes, as defined above. Pages that carry a corpus stylesheet served with a
non-CSS content type do not get that stylesheet: Chrome ignored it in the capture (the crawl had stored an HTML error page under a `.css` URL).

| | Without facts | With facts |
|---|---:|---:|
| Pages compared | 1,000 | 996 (4 snapshots refused) |
| Agreeing nodes (agree + contained + collapsed) | 830,558 | 964,964 |
| Mismatching nodes | 25,334 | 34,831 |
| Agreement | 97.04 % | 96.52 % |

Before this work the same pages agreed on 95.48 % (without facts) and 95.17 % (with: 962,485 agreeing, 48,816 mismatching nodes). With facts there
are more mismatches than without because the rules that need layout (`target-size`, `link-in-text-block`, contrast) produce verdicts only there; the
rate is over compared nodes, and needs-render nodes are not compared. With facts 5,656 `color-contrast` nodes are needs-render and not compared; without facts nearly all contrast and target-size nodes are.

### Per rule

All rules with nodes on the sample, largest number of mismatches with facts first. Agree is (agree + contained + collapsed) over that plus the
mismatches; pass-only differences and needs-render nodes are outside it. The largest classes are `ours|axe` as defined above, with facts.

| Rule | Nodes axe / ours (with facts) | Agree without facts | Agree with facts | Mismatch without → with | Largest classes with facts |
|---|---|---|---|---|---|
| color-contrast | 177896 / 178062 | 93.1 % | 92.4 % | 9811 → 12932 | pass\|incomplete 9849, none\|incomplete 1871, fail\|incomplete 850 |
| color-contrast-enhanced | 177896 / 178062 | 93.3 % | 92.5 % | 9567 → 12672 | pass\|incomplete 9328, fail\|incomplete 1362, none\|incomplete 1197 |
| target-size | 103294 / 116466 | 91.1 % | 96.2 % | 138 → 3541 | pass\|violation (user agent control) 1739, pass\|violation 738, review\|none 376 |
| hidden-content | 932209 / 13705 | 93.5 % | 93.6 % | 947 → 933 | none\|incomplete 773, review\|pass 156, unresolved\|selector-error 4 |
| css-orientation-lock | 996 / 0 | 0.0 % | 0.0 % | 646 → 644 | none\|incomplete 644 |
| duplicate-id-aria | 7895 / 8388 | 95.2 % | 95.2 % | 407 → 405 | fail\|incomplete 405 |
| skip-link | 275 / 1622 | 43.7 % | 44.0 % | 354 → 348 | fail\|none 346, fail\|pass 1, fail\|incomplete 1 |
| identical-links-same-purpose | 76974 / 49339 | 98.9 % | 98.9 % | 334 → 330 | review\|pass 128, none\|incomplete 122, review\|none 63 |
| aria-hidden-focus | 13619 / 14730 | 97.7 % | 97.6 % | 328 → 328 | fail\|incomplete 178, fail\|none 150 |
| aria-prohibited-attr | 16332 / 16257 | 98.0 % | 98.0 % | 326 → 319 | fail\|incomplete 224, fail\|pass 72, pass\|violation 11 |
| label-content-name-mismatch | 3106 / 3206 | 92.2 % | 91.1 % | 237 → 278 | none\|incomplete 89, fail\|none 60, pass\|violation 58 |
| aria-valid-attr-value | 16332 / 39523 | 98.3 % | 98.4 % | 275 → 265 | pass\|incomplete 168, review\|none 51, fail\|none 27 |
| link-in-text-block | 2766 / 3027 | 89.9 % | 90.6 % | 271 → 250 | fail\|incomplete 79, fail\|pass 66, fail\|none 60 |
| aria-required-parent | 963 / 850 | 84.0 % | 83.9 % | 137 → 137 | pass\|violation 137 |
| duplicate-id | 62620 / 64350 | 99.7 % | 99.8 % | 166 → 137 | fail\|none 71, none\|violation 66 |
| aria-allowed-role | 13912 / 6478 | 98.0 % | 98.0 % | 134 → 132 | none\|incomplete 67, fail\|pass 28, pass\|violation 24 |
| focus-order-semantics | 560 / 743 | 81.4 % | 81.4 % | 127 → 126 | fail\|none 118, fail\|pass 6, pass\|violation 1 |
| p-as-heading | 4149 / 4726 | 96.1 % | 96.1 % | 119 → 119 | pass\|violation 38, review\|violation 28, review\|pass 16 |
| link-name | 115339 / 113760 | 99.9 % | 99.9 % | 106 → 105 | pass\|violation 64, none\|violation 34, fail\|pass 7 |
| landmark-complementary-is-top-level | 690 / 574 | 85.1 % | 85.1 % | 100 → 100 | none\|violation 99, fail\|pass 1 |
| presentation-role-conflict | 3750 / 7004 | 98.3 % | 98.3 % | 64 → 64 | fail\|none 62, unresolved\|selector-error 1, pass\|violation 1 |
| aria-allowed-attr | 16332 / 16257 | 99.7 % | 99.7 % | 47 → 47 | fail\|pass 40, pass\|violation 7 |
| form-field-multiple-labels | 5235 / 4057 | 98.9 % | 98.9 % | 47 → 47 | none\|incomplete 44, review\|pass 3 |
| bypass | 969 / 996 | 95.4 % | 95.4 % | 46 → 46 | fail\|none 24, fail\|incomplete 15, fail\|pass 7 |
| landmark-one-main | 996 / 996 | 95.7 % | 95.7 % | 43 → 43 | fail\|pass 43 |
| landmark-unique | 4665 / 5850 | 99.3 % | 99.3 % | 42 → 42 | fail\|pass 18, fail\|none 18, none\|violation 6 |
| aria-command-name | 593 / 1750 | 93.8 % | 93.7 % | 40 → 40 | fail\|none 38, pass\|violation 2 |
| aria-required-children | 315 / 278 | 86.0 % | 86.6 % | 39 → 37 | pass\|violation 27, fail\|incomplete 8, fail\|pass 2 |
| td-has-header | 71 / 1340 | 41.4 % | 42.1 % | 34 → 33 | none\|violation 28, fail\|none 5 |
| region | 579724 / 2893 | 95.2 % | 95.2 % | 30 → 30 | fail\|none 16, none\|violation 12, unresolved\|selector-error 2 |
| label | 4996 / 4996 | 99.5 % | 99.5 % | 25 → 25 | fail\|pass 21, pass\|violation 3, pass\|incomplete 1 |
| scope-attr-valid | 96 / 210 | 80.7 % | 80.7 % | 23 → 23 | fail\|none 23 |
| aria-dialog-name | 118 / 86 | 81.1 % | 81.1 % | 20 → 20 | none\|violation 20 |
| th-has-data-cells | 162 / 439 | 0.0 % | 0.0 % | 20 → 20 | fail\|none 15, none\|incomplete 5 |
| duplicate-id-active | 4506 / 6843 | 98.6 % | 99.6 % | 77 → 19 | fail\|none 18, none\|violation 1 |
| page-has-heading-one | 996 / 996 | 98.1 % | 98.1 % | 19 → 19 | fail\|pass 19 |
| aria-required-attr | 6564 / 5477 | 99.6 % | 99.7 % | 23 → 17 | fail\|pass 15, pass\|violation 2 |
| empty-table-header | 446 / 439 | 96.1 % | 96.1 % | 17 → 17 | review\|violation 17 |
| image-alt | 14389 / 14384 | 99.9 % | 99.9 % | 17 → 16 | fail\|pass 12, unresolved\|selector-error 2, pass\|violation 2 |
| table-fake-caption | 162 / 105 | 85.5 % | 85.5 % | 16 → 16 | pass\|violation 11, none\|violation 5 |
| button-name | 5450 / 5279 | 99.7 % | 99.7 % | 15 → 15 | pass\|violation 13, fail\|none 2 |
| input-button-name | 342 / 344 | 96.2 % | 96.2 % | 13 → 13 | pass\|violation 13 |
| label-title-only | 5235 / 4745 | 99.7 % | 99.7 % | 12 → 12 | fail\|pass 7, pass\|violation 5 |
| nested-interactive | 29891 / 42945 | 100.0 % | 100.0 % | 12 → 12 | none\|violation 6, fail\|none 4, unresolved\|selector-error 1 |
| landmark-no-duplicate-banner | 792 / 843 | 98.8 % | 98.8 % | 10 → 10 | fail\|pass 5, fail\|none 5 |
| list | 16760 / 17138 | 100.0 % | 100.0 % | 7 → 7 | fail\|pass 6, fail\|none 1 |
| select-name | 239 / 239 | 97.2 % | 97.1 % | 7 → 7 | fail\|pass 7 |
| landmark-no-duplicate-contentinfo | 796 / 830 | 99.4 % | 99.4 % | 5 → 5 | none\|violation 5 |
| frame-title-unique | 70 / 12 | 66.7 % | 66.7 % | 4 → 4 | pass\|incomplete 4 |
| video-caption | 26 / 28 | 68.6 % | 85.7 % | 11 → 4 | review\|pass 2, review\|none 2 |
| aria-valid-attr | 16332 / 39602 | 100.0 % | 100.0 % | 3 → 3 | fail\|none 3 |
| definition-list | 326 / 326 | 99.1 % | 99.1 % | 3 → 3 | fail\|pass 3 |
| image-redundant-alt | 14389 / 7219 | 100.0 % | 100.0 % | 3 → 3 | unresolved\|selector-error 2, none\|violation 1 |
| aria-input-field-name | 11 / 41 | 89.5 % | 84.6 % | 2 → 2 | fail\|none 2 |
| audio-caption | 3 / 1 | 66.7 % | 33.3 % | 1 → 2 | none\|incomplete 2 |
| heading-order | 12489 / 12451 | 100.0 % | 100.0 % | 2 → 2 | pass\|violation 1, fail\|none 1 |
| landmark-no-duplicate-main | 641 / 657 | 99.7 % | 99.7 % | 2 → 2 | fail\|pass 1, fail\|none 1 |
| empty-heading | 12489 / 12451 | 100.0 % | 100.0 % | 1 → 1 | pass\|violation 1 |
| meta-refresh-no-exceptions | 1 / 1 | 0.0 % | 0.0 % | 1 → 1 | fail\|pass 1 |
| object-alt | 1 / 2 | 50.0 % | 50.0 % | 1 → 1 | fail\|none 1 |
| accesskeys | 73 / 82 | 100.0 % | 100.0 % | 0 → 0 | - |
| area-alt | 26 / 26 | 100.0 % | 100.0 % | 0 → 0 | - |
| aria-conditional-attr | 16332 / 2 | 100.0 % | 100.0 % | 0 → 0 | - |
| aria-deprecated-role | 6564 / 6515 | 100.0 % | 100.0 % | 0 → 0 | - |
| aria-hidden-body | 996 / 996 | 100.0 % | 100.0 % | 0 → 0 | - |
| aria-progressbar-name | 12 / 12 | 100.0 % | 100.0 % | 0 → 0 | - |
| aria-roles | 6564 / 6532 | 100.0 % | 100.0 % | 0 → 0 | - |
| aria-tab-name | 38 / 213 | 100.0 % | 100.0 % | 0 → 0 | - |
| aria-toggle-field-name | 26 / 45 | 100.0 % | 100.0 % | 0 → 0 | - |
| aria-tooltip-name | 10 / 10 | 100.0 % | 100.0 % | 0 → 0 | - |
| autocomplete-valid | 399 / 151 | 100.0 % | 100.0 % | 0 → 0 | - |
| avoid-inline-spacing | 10350 / 698 | 100.0 % | 100.0 % | 0 → 0 | - |
| dlitem | 1138 / 1138 | 100.0 % | 100.0 % | 0 → 0 | - |
| document-title | 996 / 996 | 100.0 % | 100.0 % | 0 → 0 | - |
| frame-title | 137 / 137 | 100.0 % | 100.0 % | 0 → 0 | - |
| html-has-lang | 996 / 996 | 100.0 % | 100.0 % | 0 → 0 | - |
| html-lang-valid | 938 / 937 | 100.0 % | 100.0 % | 0 → 0 | - |
| html-xml-lang-mismatch | 24 / 24 | 100.0 % | 100.0 % | 0 → 0 | - |
| input-image-alt | 11 / 11 | 100.0 % | 100.0 % | 0 → 0 | - |
| landmark-banner-is-top-level | 842 / 843 | 100.0 % | 100.0 % | 0 → 0 | - |
| landmark-contentinfo-is-top-level | 830 / 830 | 100.0 % | 100.0 % | 0 → 0 | - |
| landmark-main-is-top-level | 656 / 657 | 100.0 % | 100.0 % | 0 → 0 | - |
| listitem | 75186 / 74771 | 100.0 % | 100.0 % | 0 → 0 | - |
| marquee | 5 / 5 | 100.0 % | 100.0 % | 0 → 0 | - |
| meta-refresh | 1 / 1 | 100.0 % | 100.0 % | 0 → 0 | - |
| meta-viewport | 988 / 159 | 100.0 % | 100.0 % | 0 → 0 | - |
| meta-viewport-large | 988 / 159 | 100.0 % | 100.0 % | 0 → 0 | - |
| role-img-alt | 158 / 158 | 100.0 % | 100.0 % | 0 → 0 | - |
| scrollable-region-focusable | 80 / 45 | 100.0 % | 100.0 % | 0 → 0 | - |
| summary-name | 132 / 132 | 100.0 % | 100.0 % | 0 → 0 | - |
| svg-img-alt | 360 / 360 | 100.0 % | 100.0 % | 0 → 0 | - |
| tabindex | 2743 / 93 | 100.0 % | 100.0 % | 0 → 0 | - |
| table-duplicate-name | 491 / 1 | 100.0 % | 100.0 % | 0 → 0 | - |
| valid-lang | 501 / 327 | 100.0 % | 100.0 % | 0 → 0 | - |

Before this work, with facts (mismatching nodes): `target-size` 7,751 (92.2 % agree), `link-in-text-block` 10,663 (18.4 %), `listitem` 581,
`color-contrast` 13,812 (91.9 %), `color-contrast-enhanced` 10,431 (93.6 %); the last grew to 12,672 because symbol-only text is now counted
(the kept `pass|incomplete` class below), not because anything got worse. `aria-required-parent` and `empty-heading` had grown after the tree
changes (29 to 166 and 1 to 68); they are back to 137 (one page, kept) and 1.

### Checker time

Per page, the whole rule set (`checkMs` of each page), 1,000 pages; "before" is the checker at the start of this work with the same harness.

| | Median | p95 | Max | Sum |
|---|---:|---:|---:|---:|
| Without facts, before | 38 ms | 1,768 ms | 24,489 ms | 415.6 s |
| Without facts, after | 5 ms | 275 ms | 8,825 ms | 57.4 s |
| With facts, before | 38 ms | 1,754 ms | 24,166 ms | 412.1 s |
| With facts, after | 9 ms | 259 ms | 7,973 ms | 58.0 s |

The saving is the focus overlays: `focus-visible-indicator` restyles only what a focus reaches instead of the whole page per focusable. The slowest
page (06e2ed1a8276, 8 s) was not analysed further. Numbers are from one run each and the worker was shared.

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

Each mismatch class of the 1,000-page sample and of the written pages is fixed, kept on purpose, or open. Dispositions are also in the rule pages
(`docs/rules/*.md`, "Measured against axe-core 4.14.0").

**Fixed because of the differential** (earlier work, then this round):

- Earlier: dangling ID references (`aria-valid-attr-value`); `aria-braille-equivalent`; native radio in `aria-conditional-attr`; the data-table gate of
  `td-has-header` and `table-fake-caption`; complementary inside `main`; `meta-viewport-large` range; `region` content definition;
  `hidden-content` (own computed display or visibility, embedded objects, `&nbsp;`); `image-redundant-alt` (equality of the container's other text);
  `p-as-heading` observed thresholds; `identical-links-same-purpose` (relative links resolve against the page address); the text colour of form controls.
- `link-in-text-block`: a block of text is the nearest block whose text outside links is longer than the text of its links; a link alone in
  its block is not applicable; a background image on the link is a cue. 10,663 mismatching nodes to 250.
- `target-size`: the target is the element's own border box; focusable widgets and `summary` are targets; the order of tests follows axe's (size,
  in-sentence, user-agent control, fully covered, spacing, then content overflow); `aria-hidden` controls are neighbours without verdict;
  completely covered targets (later in tree order, or later painted) are not targets. 7,751 to 3,541.
- `listitem`: an `li` hidden from assistive technology is not examined (581 `fail|none` to 0).
- `color-contrast`: push-button `input` labels are judged; native button and field fills apply unless the author sets a background; text over an
  image or positioned box that is not its ancestor is never judged against the page colour (needs-render, or background samples with facts); the
  `body link` attribute colours the document's links in the cascade.
- Tree: a heading (or any role) inside a button is named from its contents again (`empty-heading` 68 to 1; the name computer had stopped seeing
  the role); a plain container between a context role and its child is looked through by `aria-required-parent` and `aria-required-children`
  (`aria-required-parent` fail|pass 29 to 0). Chrome AX differential on the first 2,095 captures with facts, before and after: ignored 0.173 %
  and 0.173 %, role 0.089 % and 0.089 %, name 0.498 % and 0.484 %, description 0.132 % and 0.132 %, exposedParent 0.245 % and 0.245 %.
- The harness did not supply a stylesheet whose recorded content type is not `text/css` (Chrome ignores such a body; the crawl had stored an
  HTML error page under a `.css` URL, and its rules produced a `fail|pass` class that looked like a cascade bug). This affects `ax`, `axe` and `styles`.

**Kept on purpose** (the reason is the spec or ACT, so it is not a bug on our side; the larger classes first):

- `color-contrast` and `-enhanced` pass|incomplete (9,849 and 9,328): text of symbols only (icon fonts). axe is incomplete ("only non-text characters"); ACT
  afw4f7 does not apply to text without letters or digits.
- `color-contrast` none|incomplete (1,871), fail|incomplete (850): empty or hidden text, and elements axe thinks are overlapped, where our facts know the stacking.
- `color-contrast` pass|violation (34): axe checks text made of punctuation only; ACT exempts it.
- `target-size` pass|violation "user agent control" (1,739): WCAG 2.5.8 exempts a control whose size the browser sets; axe measures it.
- `target-size` review|none (376), fail|incomplete (15): negative `tabindex` and content overflow, which need a person.
- `link-in-text-block` fail|pass (66): a link whose colour equals the text colour is a failure of technique F73; axe passes it. fail|incomplete (79):
  axe stops at "element overlap".
- `landmark-one-main` fail|pass (43): axe passes a page with no `main` when a rendered `dialog` exists; a dialog is not the page's main content (the
  rule keeps the spec reading, and says so in `docs/rules/structure.md`).
- `p-as-heading` (119 nodes, 96.1 %): the thresholds are ours and the rule is a best practice with no ACT rule to settle them; the rule keeps them
  and says so in `docs/rules/structure.md`.
- `aria-required-parent` pass|violation (137), `aria-required-children` pass|violation (27): an `li` between `ul[role=menu]` and its `menuitem`; Chrome's
  own tree has `menu > menuitem` (page 122a2205c3f5 supplies 112 and 21 of them).
- `aria-prohibited-attr` fail|incomplete (224), fail|pass (72); `skip-link` fail|none (346); `aria-hidden-focus` fail|incomplete (178) and fail|none (150);
  `duplicate-id-aria` fail|incomplete (405); `bypass`: as in the earlier sample (role data prohibits naming where axe warns; a fragment link to a
  missing target fails; axe is incomplete while a modal is open or needs a link).
- `css-orientation-lock` none|incomplete (644): axe is incomplete on every page with an orientation media query it cannot evaluate.

**Open** (not explained; each has examples in `classes.json`):

- `target-size` pass|violation (738), fail|pass (287), none|violation (169), fail|none (69). Mostly axe's *partially obscured* measurement (it uses the
  part of the box that other elements leave open) and neighbours it counts differently; a subtraction over later-painted boxes made 123 more nodes
  wrong and was dropped. See `docs/rules/presentation.md`.
- `link-in-text-block` fail|none (60 nodes on 32 pages): links with large boxes (images, cards) that axe does not select.
- `color-contrast` fail|pass (20, enhanced 168 in the earlier numbers) and fail|none (294).
- `hidden-content` none|incomplete (773) and review|pass (156): hidden elements axe flags that hold only elements without text or media.
- `identical-links-same-purpose` review|pass (128), none|incomplete (122), review|none (63).
- `label-content-name-mismatch` (278), `aria-valid-attr-value` pass|incomplete (168), `aria-allowed-role` none|incomplete (67), `focus-order-semantics`
  fail|none (118), `landmark-complementary-is-top-level` none|violation (99), `presentation-role-conflict` fail|none (62), `duplicate-id` (137),
  `aria-command-name` fail|none (38), `td-has-header` none|violation (28).


## Contrast, spacing and target size

The fixture pages (ACT `78fd32`, the four image-background pages of ACT `afw4f7` and `09o5cg`, and `testdata/rendered/pages/target-size.html` with its
capture) agree as before: `avoid-inline-spacing` agrees on all seven cases with facts; the image pages are needs-render on our side and incomplete on
axe's (both decline to guess); `target-size` on the fixture agrees on all 14 targets axe reports with facts. The class analysis on the sample is in the
rule pages: `docs/rules/contrast.md` (contrast, link-in-text-block) and `docs/rules/presentation.md` (target-size).

Measured axe behaviour, learned from probe pages and the sample (all black-box output):

- **target-size**: the target is the element's own border box; tests run in the order size, in-sentence, user-agent control, fully covered, spacing,
  content overflow (incomplete only after both size and spacing fail); `summary` is a target; `aria-hidden` controls count as neighbours and get no verdict;
  links with the same `href` are one target except for `#` and `javascript:`; only `overflow: hidden|clip` clips, never on `html`/`body`.
- **link-in-text-block**: identical link and text colours pass; `<script>` text counts as text; a background image on the link counts as a cue.
- **listitem**: items hidden from assistive technology are not examined.
- **color-contrast**: `input` push-button labels are judged from their value; the native fill of buttons and fields applies unless the author sets a background.

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

- Frames and shadow roots: nested targets are counted, not compared.
- Partial obscuring in `target-size`, and the open classes above.
- The full-corpus run (13,857 pages; needs a gate window and claims the worker lock; `--timeout 300` because the largest pages exceed 30 s with all rules):
  `bash tools/OneHtml.Differential/run-worker.sh axe ~/onehtml/capture-out/2026-10a ~/onehtml/out/axe --corpus ~/onehtml/corpus/2026-10a --jobs 8 --timeout 300`.
  It is resumable (run it again, or use `--max-seconds`); add `--facts on` or `off` to run one mode.
