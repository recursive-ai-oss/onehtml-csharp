# Rules that close the ACT gaps

These rules exist because the W3C ACT rule list asks for them and no axe-core rule does. Seven ask a person (needs-review);
the focus indicator is decided from the cascade. The full per-rule account of ACT coverage, with case counts, is in
[../act-coverage.md](../act-coverage.md). None of the rules has a suggested edit: every fix is a content or design choice.

| Rule | ACT | Criteria | Outcome |
|---|---|---|---|
| `image-of-text` | 0va7u6 (approved) | 1.4.5, 1.4.9 | needs-review for each drawn image |
| `document-title-descriptive` | c4a8a4 (approved) | 2.4.2 | needs-review for the first `title` with text |
| `focus-visible-indicator` | oj04fd (approved) | 2.4.7 | passed, failed, or needs-review |
| `link-descriptive` | aizyf1 | 2.4.9 | needs-review for each exposed link with a name |
| `heading-descriptive` | b49b2e | 2.4.6 | needs-review for each drawn heading with text |
| `label-descriptive` | cc0f0a | 2.4.6 | needs-review for each visible label of a field |
| `image-decorative` | e88epe | 1.1.1 | needs-review for each picture the tree ignores |
| `identical-links-same-context` | fd3a94 | 2.4.4, 2.4.9 | passed when the targets agree, else needs-review |

`summary-name` (names area) also takes ACT 2t702h as a source; the vendored case set has no cases for that rule.

## How each one decides

- **image-of-text.** An image is an `img` with `src` or `srcset`, an `input type=image` with `src`, an SVG `image`, an `object` with `data`,
  or any element whose computed `background-image` has a `url()` or `image-set()`. It must be drawn (not `display: none`, `hidden`,
  `visibility: hidden`). What the pixels show is not in the document, so every one is a review item, including `alt=""` images
  (ACT 0va7u6 judges those too, by whether they are decoration).
- **document-title-descriptive.** The first `title` element that is a descendant of `html` and has a child text node with more than
  white space. A page without one is `document-title`'s finding.
- **focus-visible-indicator.** Applies only when two or more elements are in sequential focus navigation (a page with one focusable
  element satisfies 2.4.7 as ACT oj04fd assumes). For each of them the page is restyled with keyboard focus on that element
  (`StyleContext.WithFocus`, so `:focus-visible` matches) and compared with the unfocused page. A change in any painted property
  (outline, border, box-shadow, background, colour, text decoration, opacity, filter, transform, size and position offsets, and a
  few more, listed in the source) on the element **or any other element** counts as a visible indicator: passed. Otherwise failed.
  An element with `onfocus`, `onfocusin`, `onblur` or `onfocusout` is needs-review instead of failed, because script can add
  an indicator the cascade cannot see. The browser's default focus ring comes from `:focus-visible { outline: auto }` in the user
  agent sheet, so an element whose outline the page does not remove passes. Whether the change is large or contrasting enough is
  2.4.11 and 2.4.12, not this rule. At most 2000 focusable elements are followed per page (the rest are needs-review) and the
  comparison across the whole page stops after 20 million element comparisons, falling back to the focused element alone.
- **link-descriptive.** An exposed element with the role `link` and a non-empty accessible name. The wording and its context are for a
  person; compare `link-in-context-descriptive`, which allows the sentence around the link.
- **heading-descriptive.** A drawn element with the role `heading` that has a name or text. A heading hidden with `aria-hidden` is still
  on screen, so it is reviewed (ACT b49b2e Passed Example 7).
- **label-descriptive.** Fields with the role checkbox, combobox, listbox, menuitemcheckbox, menuitemradio, radio, searchbox, slider,
  spinbutton, switch or textbox that are drawn. Their labels are the `label` elements that label them and the elements their
  `aria-labelledby` names; each label is reported once and must be drawn (an `aria-hidden` label counts, as on screen it is visible).
- **image-decorative.** A drawn `img`, `canvas` or `svg` that the tree leaves out (not exposed, or a presentational role), or an `svg` with
  the role `graphics-document` and no name, or a `canvas` with no name and no explicit role. A picture inside an element that is named
  by the author's `aria-label` or `aria-labelledby` is not applicable.
- **identical-links-same-context.** Exposed links with the same folded name and the same context. The context is the closest ancestor with a
  block-level display (`block`, `list-item`, `table-cell`, `flow-root`, `table*`, `flex*`, `grid*`), covering the sentence, paragraph,
  list item and table cell of the ACT definition; the header cells of a table cell are not followed. The links pass when they resolve
  to the same URL; different URLs are needs-review (equivalent resources, such as a redirect, need a person). Links with no `href`
  are never judged to agree.

## Cases that cannot be judged here

`testdata/act/gaps-expectations.tsv` lists each ACT case the static check gets "wrong", with the reason:

- an element is moved off-screen with CSS (needs layout, so rendered facts);
- a canvas nothing was drawn on, or an image file that does not exist (a browser knows; a document does not);
- a failing focus case whose stylesheet is not part of the vendored case (the page keeps the default focus outline).

## Not automatable

Proposed ACT rules with no rule here, and why, are in the table of [../act-coverage.md](../act-coverage.md): the cross-page rules
(047fe0, b40fd1, 3e12e1, ye5d6e), the script-driven ones (keyboard traps 80af7b, a1b64e, ebe86a; device motion 7677a9, c249d5;
keyboard shortcuts ffbc54; automatic text updates efbfc7), and three that need a person to find the content or a layout
at zoom (36b590, 9bd38c, 59br37).
