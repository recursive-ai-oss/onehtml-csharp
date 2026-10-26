# Page-structure rules

The second rule area of the Validation package: landmarks and regions, bypass, headings, lists, ids, focus and
interaction, obsolete elements, and the `meta` elements that change how a page refreshes or zooms. They are the axe-core
4.14.0 rules of those subjects plus the ACT rules that match them. Ids, tags and impacts come from the axe-core rule
table as facts; every description and help text is our own. How to run rules is in [checker.md](checker.md), the result
shape in [results.md](results.md); the ARIA area is in [aria.md](aria.md).

Deprecated and experimental axe rules are implemented and keep their axe tags (`deprecated`, `experimental`), so a
selection by tag can leave them out: `duplicate-id`, `duplicate-id-active` and `landmark-complementary-is-top-level` are
deprecated; `p-as-heading`, `focus-order-semantics` and `hidden-content` are experimental.

## Conventions

- "Rendered" means laid out and not `visibility`-hidden (`display: none`, `hidden`, closed `details` and `<template>`
  are not rendered). "Exposed" is `CheckContext.IsExposed`: rendered and not hidden by `aria-hidden` or `inert`. Rules
  about what a person perceives or reaches use rendered or exposed elements; the id rules look at every element, as ACT
  3ea0c8 says.
- A landmark is an exposed element whose role is `banner`, `complementary`, `contentinfo`, `main`, `navigation`,
  `search`, or a `form` or `region` that has an accessible name (WAI-ARIA 1.2 and HTML-AAM; an unnamed `section` or
  `form` is not a landmark, as in Chrome).
- Page-level rules (`bypass`, `landmark-one-main`, `page-has-heading-one`, `region` when everything is covered) report on
  the `html` element.
- `Elements` means every element in document order, template contents excluded.
- Results come in document order of the target, then outcome, as always. Every rule that applies to nothing is
  inapplicable.

## The rules

Columns: sources, criteria (or best practice), axe impact, needs, and the suggested edit. A suggested edit exists only
where the rule names exactly one correct, judgement-free fix; the library never applies it. The fix decisions follow
the table.

| Rule | Sources | Criteria | Impact | Needs | Suggested edit |
|---|---|---|---|---|---|
| landmark-banner-is-top-level | axe | best practice | moderate | static | none |
| landmark-complementary-is-top-level (deprecated) | axe | best practice | moderate | static | none |
| landmark-contentinfo-is-top-level | axe | best practice | moderate | static | none |
| landmark-main-is-top-level | axe | best practice | moderate | static | none |
| landmark-no-duplicate-banner | axe | best practice | moderate | static | none |
| landmark-no-duplicate-contentinfo | axe | best practice | moderate | static | none |
| landmark-no-duplicate-main | axe | best practice | moderate | static | none |
| landmark-one-main | axe | best practice | moderate | static | none |
| landmark-unique | axe | best practice | moderate | static | none |
| region | axe | best practice | moderate | static | none |
| bypass | axe, ACT cf77f2 | 2.4.1 | serious | static | none |
| skip-link | axe | best practice | moderate | static | none |
| heading-order | axe | best practice | moderate | static | none |
| page-has-heading-one | axe | best practice | moderate | static | none |
| p-as-heading (experimental) | axe | 1.3.1 | serious | static (cascade) | none |
| list | axe | 1.3.1 | serious | static | none |
| listitem | axe | 1.3.1 | serious | static | wrap a run of orphan `li` in `ul` |
| definition-list | axe | 1.3.1 | serious | static | none |
| dlitem | axe | 1.3.1 | serious | static | none |
| duplicate-id (deprecated) | axe, ACT 3ea0c8 | 4.1.1 (obsolete) | minor | static | none |
| duplicate-id-active (deprecated) | axe, ACT 3ea0c8 | 4.1.1 (obsolete) | serious | static | none |
| duplicate-id-aria | axe, ACT 3ea0c8 | 4.1.2 | critical | static | none |
| nested-interactive | axe, ACT 307n5z | 4.1.2 | serious | static | none |
| tabindex | axe | best practice | serious | static | `tabindex` → `0`, only when the focus order stays |
| focus-order-semantics (experimental) | axe | best practice | minor | static | none |
| scrollable-region-focusable | axe, ACT 0ssw9k | 2.1.1 | serious | rendered facts | none |
| frame-focusable-content | axe, ACT akn7bn | 2.1.1 | serious | static (facts optional) | none |
| accesskeys | axe | best practice | serious | static | none |
| hidden-content (experimental) | axe | best practice | minor | static | none (review item) |
| blink | axe | 2.2.2 | serious | static | none |
| marquee | axe | 2.2.2 | serious | static | none |
| server-side-image-map | axe | 2.1.1 | minor | static | none (review item) |
| meta-refresh | axe, ACT bc659a | 2.2.1 | critical | static | none |
| meta-refresh-no-exceptions | axe, ACT bisz58 | 2.2.4, 3.2.5 | minor | static | none |
| meta-viewport | axe, ACT b4f0c3 | 1.4.4 | moderate | static | none |
| meta-viewport-large | axe | best practice | minor | static | none |

The criteria are those the catalogue takes from the axe tags; `docs/coverage.md` is regenerated from them.

## Fix decisions

Three rules were candidates and two offer an edit. Everything else has none, for the reason each rule's descriptor gives
(`RuleDescriptor.NoFixReason`): moving landmarks, changing heading levels, choosing which element keeps an id, naming
landmarks or replacing a marquee all need the author.

- **tabindex.** Setting a positive `tabindex` to `0` is judgement-free *only if* it leaves the sequential focus order
  exactly as it is. Positive values sort before every other tabbable element, so changing them moves the element unless
  the page's focus order already follows the document order. The rule offers `tabindex="0"` on every failing element
  when the tree's focus order is strictly increasing in document order, and nothing otherwise (then the author chose the
  order). The edits are one set and must be applied together: applying one of several positive values alone can reorder
  the rest. After applying them all the focus order is the same and the findings are gone.
- **listitem.** An `li` outside a list renders with a bullet already, so `ul` is the list it looks like, and the edit is
  `wrap` of the run of consecutive sibling `li` (whitespace and comments may sit between) in `<ul></ul>`, offered on the
  first `li` of the run; one edit clears every `li` of the run. It is offered only when no item has a `value` attribute,
  none has a role, and the parent has no role. It is the one decision here that deserves a second look by a person: the
  wrapper adds the default list indentation and margins of `ul`, so the page may move a little. The edit goes through the
  fragment parser, splices bytes, and re-checking shows no `listitem` failure.
- **Duplicate ids: no edit.** Renaming the later copies looks safe because browsers resolve every reference to the first
  element with the id, but the author may have meant any of them, a stylesheet or script the library cannot see may
  select every copy, and the new id would be an invented name. A rename is judgement-free only if no reference could
  change meaning, and that cannot be known from one document.
- **blink and marquee: no edit.** Unwrapping drops the author's intended effect, and CSS may style the element.
- **meta-viewport: no edit.** Removing the restricting properties and raising the limit are both correct edits, so there
  is no single fix.
- **Skip links, headings and landmarks: no edit.** Targets, levels, names and roles are content.

## What each rule checks

- **Landmark rules** (axe, best practice). The four `-is-top-level` rules fail a banner, complementary, contentinfo or
  main landmark that has another landmark among its ancestors in the accessibility tree. `header` and `footer` are
  banner and contentinfo only outside sectioning content (HTML-AAM), so a `header` inside `main` is not subject to the
  rule. The `no-duplicate` rules fail every landmark of the role when the page has more than one. `landmark-one-main`
  passes when the page has at least one main (a second is `landmark-no-duplicate-main`'s). `landmark-unique` compares role
  and accessible name (ASCII case-insensitive, whitespace collapsed); two landmarks with the same pair both fail, and an
  unnamed landmark fails only if another unnamed one has the same role.
- **region** (axe, best practice). Content outside every landmark is skipped by landmark navigation. Content is non-blank
  text or an image, media, control or embedded content that is rendered and exposed. The rule reports the topmost element
  that holds uncovered content and no landmark, and an element that has stray text of its own beside landmarks. Skip
  links, dialogs (`dialog`, `alertdialog`) and live regions count as covered. If nothing fails and there is content, the
  `html` element passes.
- **bypass** (ACT cf77f2, axe, WCAG 2.4.1). Passes when the page has a heading, a main landmark, or a skip link (an `a`
  whose href is a fragment that names an element of the page, HTML 7.4.6.3) that is rendered. Fails otherwise.
- **skip-link** (axe, best practice). An `a` with an in-page fragment href must name an element that exists and is
  rendered; `#top` is accepted (HTML 7.4.6.3 scrolls to the top).
- **heading-order** (axe). A heading level may be at most one more than the previous heading's; the first heading is never
  flagged. Headings are exposed `heading` nodes: `h1`–`h6` and `role=heading` with `aria-level` (default 2).
- **page-has-heading-one** (axe). At least one exposed level-one heading.
- **p-as-heading** (axe, experimental; WCAG 1.3.1, technique F2). A `p` of at most 140 characters whose next sibling
  element is a `p` shows signs of a heading: all its text bold (weight 700 or more), all italic against a next paragraph that is
  not, or a smallest font size at least 1.4 times the next paragraph's largest. Two signs together fail; a size sign alone is
  needs-review; one bold or italic paragraph alone passes (as in axe, from probes). Weight, style and size are *computed style*, so the cascade decides and no layout is needed: this rule
  does not need rendered facts.
- **list** (axe). A `ul` or `ol` without a role, or with `role=list`, has only `li`, `script`, `template` and elements with
  `role=listitem` as rendered children, and no loose text. An `li` with another role fails.
- **listitem** (axe). An `li` without a role (or `role=listitem`) must have a parent `ul`, `ol` or `menu` that has no role,
  `none` or `presentation`, or any parent with `role=list`.
- **definition-list** (axe, HTML 4.4.9). A `dl` without a role holds groups of one or more `dt` then one or more `dd`,
  optionally each group in a `div`, plus `script` and `template`. An empty `dl` passes; loose text, other elements, a `dd`
  before any `dt` and a `dt` with no `dd` fail.
- **dlitem** (axe). `dt` and `dd` are inside a `dl`, directly or through a `div`.
- **duplicate-id, duplicate-id-active, duplicate-id-aria** (ACT 3ea0c8, axe). Every non-empty `id` on an HTML or SVG
  element must be unique in the document. The three rules split the elements and never overlap: ids that an ARIA
  attribute (`aria-labelledby`, `aria-describedby`, `aria-owns`, `aria-controls`, `aria-flowto`, `aria-details`,
  `aria-errormessage`, `aria-activedescendant`) or a `label for` refers to belong to `duplicate-id-aria`, other focusable
  elements to `duplicate-id-active`, the rest to `duplicate-id`. Every element that shares the id fails.
- **nested-interactive** (ACT 307n5z, axe). An element whose role has presentational children (button, tab, img, …)
  contains nothing in sequential focus navigation (tabbable, rendered, exposed).
- **tabindex** (axe). A rendered HTML element with an integer `tabindex` above 0 fails. The integer is read by the HTML
  rules for parsing integers (`"+2"` is 2).
- **focus-order-semantics** (axe, experimental). An element with a `tabindex` that is tabbable, exposed and not natively
  focusable passes if its role is a widget role (WAI-ARIA widget class, followed transitively), a landmark, `dialog`,
  `alertdialog`, `application` or `tabpanel`, or if its computed `overflow` is `auto` or `scroll` (the usual way to make a
  scrolling container reachable); otherwise it fails.
- **scrollable-region-focusable** (ACT 0ssw9k, axe). A rendered element (not `html` or `body`) with computed `overflow-x` or
  `overflow-y` of `auto` or `scroll` and with content must be tabbable or contain something tabbable if it scrolls.
  Whether it scrolls is layout. *Without facts:* an element that is already reachable passes ("whether or not it
  scrolls"), and one that is not is **needs-render**, naming the facts `clientRects` and `scrollRects`. *With facts:* an
  element that scrolls (scroll size more than one pixel beyond the client size in an overflowing axis) passes or fails,
  with `FactsUsed` set; one that does not scroll is not applicable. Facts that lack the rectangles give needs-render for
  an unreachable element.
- **frame-focusable-content** (ACT akn7bn, axe). An `iframe` (or `frame`) with a negative `tabindex` must not hold
  anything tabbable. Only inline content (`srcdoc`) can be checked: it is parsed, a tree is built and its focus order
  must be empty. A frame loaded from a URL is needs-review. A frame that is not rendered passes, and so does one the facts
  show as one pixel or less in both directions (`FactsUsed`: `bounds`).
- **accesskeys** (axe). Every access key is one element's: each space-separated token of `accesskey`, compared ASCII
  case-insensitively, among rendered elements; elements sharing a key fail.
- **hidden-content** (axe, experimental). A review item: the topmost element hidden by `display: none` or
  `visibility: hidden` that has text, outside `head`, `script`, `style`, `template`, `noscript`, form-list elements and
  the like. The `hidden` attribute alone does not count (it is intended hiding). Needs the cascade only.
- **blink, marquee** (axe, WCAG 2.2.2). The obsolete elements fail wherever they are.
- **server-side-image-map** (axe). An `img` with `ismap` inside an `a` with `href` is needs-review; without the link it
  does nothing (HTML 4.8.3) and is not applicable.
- **meta-refresh, meta-refresh-no-exceptions** (ACT bc659a, bisz58). Applies to the first `meta` with `http-equiv`
  `refresh` (ASCII case-insensitive) whose `content` is valid by the HTML shared declarative refresh steps: optional
  white space, ASCII digits (or a leading `.`), then the end, `;`, `,` or white space. `meta-refresh` passes with a delay
  of 0 or over 72000 seconds; the no-exceptions rule passes only at 0. The URL part is not parsed (the checker has no base
  URL), so a URL that would not parse still counts as valid.
- **meta-viewport, meta-viewport-large** (ACT b4f0c3, axe). A viewport `meta` (name ASCII case-insensitive) fails if its
  `content` turns zoom off or caps it too low. The content is `name=value` pairs separated by comma, semicolon or white
  space; the later of two same-named properties wins; `yes` is 1, `no` is 0, a number's leading numeric part counts, and a
  negative or unparseable value is ignored. `user-scalable` is off for `no` or a magnitude under 1; `maximum-scale` fails
  when under 2 (`meta-viewport`) or under 5 (`meta-viewport-large`; `user-scalable=no` fails it too).

## Where ACT and axe differ

ACT wins. The first list comes from the ACT texts and test cases; "Measured against axe-core 4.14.0" below it comes from
the axe differential (`docs/axe-differential.md`).

- **Duplicate ids:** every element that shares an id fails (ACT 3ea0c8 applies to every id attribute). Whether axe reports
  one result per id value is for the axe differential to record.
- **meta-refresh:** the first *valid* refresh decides; an invalid one is skipped (bc659a Failed Example 3, Inapplicable
  Examples 3–8). A delay above 72000 passes.
- **meta-viewport:** `maximum-scale=yes` fails (yes is 1) and a negative value passes (ACT Failed Example 4, Passed
  Example 3).
- **bypass:** one page cannot tell what repeats across a site, which is what ACT's four sub-rules (047fe0, b40fd1, 3e12e1,
  ye5d6e) depend on. `bypass` answers the page-level question with the mechanisms a single page can show (heading, main
  landmark, skip link). It agrees with cf77f2 on 9 of its 14 cases; the others need script or knowledge of repeated
  content.
- **scrollable-region-focusable:** ACT also takes padding into account (a section whose content only overflows its
  padding is not applicable); we use the scroll and client sizes the facts report.
- **p-as-heading and hidden-content** use the cascade, not layout facts.
- **aria-hidden or hidden content** is not counted by the landmark and heading rules; only exposed elements are.

### Measured against axe-core 4.14.0

Fixed after the differential:

- **region:** a `button`, an `iframe`, an inline `svg` and the text inside a button are not page content (axe does not
  report an element that holds only these); an element with `role="region"` counts as a region even without a name
  (an unnamed `section` still does not). Skip-link targets are *not* exempt in either checker; the skip link itself is.
- **landmark-complementary-is-top-level:** a complementary landmark directly inside `main` passes, as the ARIA Authoring
  Practices allow.
- **meta-viewport-large:** reports only `maximum-scale` from 2 up to (not including) 5. Switching zoom off or capping it
  below 2 is `meta-viewport`'s finding; axe reports it once.
- **hidden-content:** content hidden with the `hidden` attribute counts, as it does with `display: none`.

Reported once per group by axe (the comparison folds them: `duplicate-id`, `duplicate-id-active`, `duplicate-id-aria`,
`accesskeys`, `landmark-unique`, `landmark-no-duplicate-banner`, `-contentinfo`, `-main`, `identical-links-same-purpose`).
Axe puts the finding on the first element of the group; we put it on every member, as ACT 3ea0c8 requires for ids.

Differences kept on purpose:

| Rule | Ours | axe-core |
|---|---|---|
| `meta-refresh` and `-no-exceptions` | Follow ACT: the first valid refresh decides; `meta-refresh` fails a delay of 1 to 72000 s, `-no-exceptions` any delay above 0. | Judges every `meta` separately, and its `-no-exceptions` rule fails only delays above 72000 s (a delay of 600 s passes it). |
| `bypass` | Fails a page with no heading, no `main` and no skip link (ACT cf77f2). | Applies only to pages with at least one link; reports incomplete, not a violation; accepts any in-page link as a mechanism, even to a missing or hidden target. |
| `skip-link` | A fragment link whose target is missing or hidden fails. | Inapplicable for a link whose target does not exist. |
| `definition-list` | Each `dt` group must be followed by a `dd`. | Passes a `dl` that holds at least one `dt` and one `dd`. |
| `p-as-heading` | Two signs (bold, italic, size at least 1.4 times the next paragraph's) fail; a size alone is needs-review. | Same on the probes; the rest of the mismatches on real pages (bold with a size alone, a few italic cases) are not reproduced. |
| `hidden-content` | An element whose own computed display is none (the `hidden` attribute included), or whose visibility is hidden under a visible parent, and that holds text (white space and `&nbsp;` do not count) or an image, frame, media or object, is needs-review. | Same on the probes; on real pages it flags about 8 % more hidden roots (elements without text or media) than we do. |
| `region` (granularity) | Reports the topmost element with content that holds no landmark. | Same on the probes. On real pages it reports some wrappers' children and passes the wrapper (the Divi `#main-content` case); not reproduced, so the differential compares `region` once per page. |
| `landmark-one-main` | No main landmark in the DOM fails. | Also passes when a rendered `role="dialog"` or `dialog` element exists (probes; 11 of 17 sample pages); a dialog is not main content, so we keep failing. 6 pages unexplained. |

## ACT test cases

`StructureActTests` runs every vendored case of the ACT rules below, judged as the ARIA cases are (one verdict per page:
failed when some target fails, passed when targets exist and all pass, inapplicable when none applies). Cases that need
layout or script are N/A with the reason in `testdata/act/structure-expectations.tsv`.

| ACT rule | Our rule(s) | Cases | Pass | N/A | Fail |
|---|---|---|---|---|---|
| cf77f2 Bypass Blocks of Repeated Content | bypass | 14 | 9 | 5 | 0 |
| 3ea0c8 Id attribute value is unique | duplicate-id, duplicate-id-active, duplicate-id-aria | 10 | 10 | 0 | 0 |
| akn7bn Iframe with interactive elements is not excluded from tab-order | frame-focusable-content | 6 | 5 | 1 | 0 |
| 0ssw9k Scrollable content can be reached with sequential focus navigation | scrollable-region-focusable | 10 | 6 | 4 | 0 |
| bc659a Meta element has no refresh delay | meta-refresh | 15 | 15 | 0 | 0 |
| bisz58 Meta element has no refresh delay (no exception) | meta-refresh-no-exceptions | 14 | 14 | 0 | 0 |
| b4f0c3 Meta viewport allows for zoom | meta-viewport | 11 | 11 | 0 | 0 |
| 307n5z Element with presentational children has no focusable content | nested-interactive (in `AriaActTests`) | 7 | 7 | 0 | 0 |
| total | | 87 | 77 | 10 | 0 |

The ten N/A cases: cf77f2 Passed Examples 1, 8 and 11 use a script as the bypass instrument; Passed Example 7 passes
because the repeated content is at the end; Inapplicable Example 1 is an SVG document. akn7bn Passed Example 3 passes
because a one-pixel iframe is invisible (the rule does pass it when facts show that). 0ssw9k Failed Examples 1 and 2 and
Inapplicable Example 5 need the scroll size (the rule answers needs-render); Inapplicable Example 2 has nothing to
scroll, which only layout shows. `StructureRuleTests` covers the scroll rule with facts built from a fake snapshot.

ACT rules of these subjects that are not rules here, with the reason and their case counts counted as N/A:
047fe0 (14 cases), b40fd1 (8), 3e12e1 (8) and ye5d6e (12) depend on which content repeats across pages; b49b2e (17)
asks whether a heading is descriptive, which is a person's judgement; e6952f (10, deprecated) is about attributes
repeated within a tag, which the parser drops, so only a parse that records errors could see them. ffd0e9 (heading has a
non-empty name) belongs to the names-and-labels area.

## Suggested edits are checked

`StructureRuleTests` applies the suggested edits of the area through `Document.Apply` (no re-serialization fallback) and
through replay of their saved form, requires identical bytes, re-checks, and requires that no rule fails more than it did
before. The wrap edit's output is compared byte for byte.

## Timing

A 29,000-node page (the checker performance test's page), Release build, one thread, local machine: the 36 rules of this
area take 36 ms and allocate 10.7 MB; the whole checker (61 rules) takes 72 ms and 30 MB. The accessibility tree
build on the same page is 114 ms.
