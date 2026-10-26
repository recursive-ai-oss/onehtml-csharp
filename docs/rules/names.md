# Names and labels rules

The second rule area of the Validation package: rules about whether things have an accessible name, a text alternative
or a label. Ids, tags and impacts come from the axe-core 4.14.0 rule table as facts; every description and help text is
our own. How to run rules is in [checker.md](../checker.md), the result shape in [results.md](../results.md), the ARIA area in
[aria.md](aria.md).

Unless a rule says otherwise it looks at elements that are exposed to assistive technology (`CheckContext.IsExposed`: in
the tree, not hidden by `display`, `hidden`, `aria-hidden`, `inert` or `visibility`), which is the "included in the
accessibility tree" of the ACT rules. The name is the one the tree computes (AccName 1.2 with the HTML-AAM and SVG-AAM
rules, see [accessibility-tree.md](../accessibility-tree.md)); a name is empty when it has no character other than white
space (any Unicode white space, as ACT defines it).

Every rule here is static except `label-content-name-mismatch`, which reads laid-out text when rendered facts are
supplied and asks for them when it cannot decide (see below).

## Fix decisions

A suggested edit exists only where a rule names exactly one correct, judgement-free fix. In this area there is none, and
that is deliberate. A name, a label, a text alternative, a title or a heading is content: what to write (or whether an
image is informative or decorative) is the author's call. Adding `alt=""` to an unnamed image would be a guess that the
image is decorative, adding a `title` invents wording, and removing an empty heading or link changes the page. Each rule
below states its reason. `NameRuleTests.Names_rules_suggest_no_edits` keeps it that way.

## The rules

Columns: sources, criteria (or best practice), axe impact, needs.

| Rule | Sources | Criteria | Impact | Needs |
|---|---|---|---|---|
| area-alt | axe, ACT c487ae | 2.4.4, 4.1.2 | critical | static |
| button-name | axe, ACT 97a4e1 | 4.1.2 | critical | static |
| document-title | axe, ACT 2779a5 | 2.4.2 | serious | static |
| empty-heading | axe, ACT ffd0e9 | best practice | minor | static |
| form-field-multiple-labels | axe | 3.3.2 | moderate | static |
| frame-title | axe, ACT cae760 | 4.1.2 | serious | static |
| frame-title-unique | axe, ACT 4b1c6c | 4.1.2 | serious | static |
| identical-links-same-purpose | axe, ACT b20e66 | 2.4.9 | minor | static |
| image-alt | axe, ACT 23a2a8 | 1.1.1 | critical | static |
| image-name-descriptive | ACT qt1vmo | 1.1.1 | moderate | static (review only) |
| image-redundant-alt | axe | best practice | minor | static |
| input-button-name | axe, ACT 97a4e1 | 4.1.2 | critical | static |
| input-image-alt | axe, ACT 59796f | 1.1.1, 4.1.2 | critical | static |
| label | axe, ACT e086e5 | 4.1.2 | critical | static |
| label-content-name-mismatch | axe (experimental), ACT 2ee8b8 | 2.5.3 | serious | rendered facts |
| label-title-only | axe | best practice | serious | static |
| link-in-context-descriptive | ACT 5effbb | 2.4.4, 2.4.9 | moderate | static (review only) |
| link-name | axe, ACT c487ae | 2.4.4, 4.1.2 | serious | static |
| object-alt | axe, ACT 8fc3b6 | 1.1.1 | serious | static |
| role-img-alt | axe, ACT 23a2a8 | 1.1.1 | serious | static |
| select-name | axe, ACT e086e5 | 4.1.2 | critical | static |
| summary-name | axe | 4.1.2 | serious | static |
| svg-img-alt | axe, ACT 7d6734 | 1.1.1 | serious | static |

The criteria are the axe tags' (the catalogue's convention for rules with both sources). ACT maps some of these rules to
more: c487ae adds 1.1.1 and 2.4.9, e086e5 adds 1.3.1 and 2.5.3.

Three ARIA-area rules also carry ACT ids from this area, because ACT's subject is wider than axe's rule: `aria-command-name`
(ACT 97a4e1 for role button, c487ae for role link), `aria-input-field-name` and `aria-toggle-field-name` (ACT e086e5).
The ACT subjects "image", "button", "link" and "form field" are each split across several axe rules (an `img`, a
`role=img` element and an SVG image; a `button`, an input button and `role=button`), so one ACT rule is answered by
several rules; the test harness lists which.

### Text alternatives

- **image-alt** (ACT 23a2a8). An exposed `img` is either named or marked decorative. With the role `img` it needs a
  non-empty name (from `alt`, `aria-label`, `aria-labelledby`, or `title` when there is no `alt`). With the role `none` or
  `presentation` (an empty `alt`, or the role attribute) it passes as decorative. *Difference from the role mapping:*
  HTML-AAM maps an `alt` of white space alone to `none`; ACT 23a2a8 treats only an empty `alt` or a presentational role as
  the decorative mark, so `alt=" "` with no other name fails. ACT wins. *No fix:* decorative or informative, and the words,
  are the author's.
- **input-image-alt** (ACT 59796f). `input type=image` needs a name the author gave: `alt`, `aria-label`,
  `aria-labelledby`, or `title`. The label a browser supplies when there is none ("Submit") does not count (ACT lists it
  as failed). *No fix:* the alternative describes the action.
- **area-alt** (ACT c487ae). An `area` with an `href` has the role link and needs a name. *No fix.*
- **object-alt** (ACT 8fc3b6). An exposed `object` that renders non-text content needs a name. Whether the resource is text
  is known from the `type` (a `text/*` type, XHTML, XML or JSON) or the extension of `data` (`.html`, `.htm`, `.txt`, and
  similar); those objects are not applicable, and anything else counts as non-text, because a missed failure is worse than
  a missed exemption. Objects with a presentational role are not applicable. *No fix.*
- **svg-img-alt** (ACT 7d6734). An SVG element with the explicit role `img`, `graphics-symbol` or `graphics-document` needs
  a name (a `title` child, `aria-label` or `aria-labelledby`). Elements with other roles, or with `graphics-object`, are
  not applicable. *No fix.*
- **role-img-alt** (ACT 23a2a8). An element other than `img` and the SVG elements with the explicit role `img` needs a
  name. *No fix.*
- **image-redundant-alt**. An `img` with an `alt` inside a link, button, paragraph, list item or table cell is reported when
  the container's other text equals the alt (compared after lower-casing ASCII letters and collapsing white
  space; a container whose text only contains the alt is not reported, as in axe). The nearest such container is used; the text is read once per container. *No fix:* the alt can be emptied or
  reworded, and which depends on what the image shows.
- **image-name-descriptive** (ACT qt1vmo). Review only: every exposed image with a name (an `img`, an element with the role
  `img`, an SVG graphic, a `canvas`) is listed as needs-review with the name found. A graphic inside a link or button is
  taken as part of that control's name (ACT lists such a case as not applicable). *No fix:* whether a text is descriptive is
  a judgement.

### Controls

- **button-name** (ACT 97a4e1). A `button` element with the role button needs a name. *No fix.*
- **input-button-name** (ACT 97a4e1). An `input` of type `button`, `submit` or `reset` needs a name. The labels a browser
  gives `submit` and `reset` count, since people see them. *No fix.*
- **link-name** (ACT c487ae). An `a` with an `href` and the role link (or a link subclass, `doc-biblioref`, `doc-glossref`,
  `doc-noteref`) needs a name. *No fix.*
- **select-name**. A `select` needs a name (ACT e086e5 covers it as a form field). *No fix.*
- **summary-name**. The `summary` of a `details` element needs a name. *No fix.*
- **empty-heading** (ACT ffd0e9). An exposed heading (`h1`–`h6` or the role heading) needs a name. ACT lists no criterion
  for the rule and axe treats it as a best practice, so it is one here. *No fix:* deleting the empty heading, or giving
  it text, are both content changes.
- **aria-command-name**, **aria-input-field-name**, **aria-toggle-field-name** are described in [aria.md](aria.md).

### Forms

- **label** (ACT e086e5). A native form field (`input` other than hidden, image, button, submit and reset, and `textarea`)
  needs a name; `select` is `select-name`. A `placeholder` counts, as the name computation says. Disabled fields count
  too: ACT passes and fails them like any other. *No fix:* the label's wording and place are content.
- **label-title-only**. A field named only by its `title` is reported, as is a field with no name but an
  `aria-describedby` (a description is not a name). A field with a name from a label, `aria-label` or `aria-labelledby`
  passes. Best practice. *No fix.*
- **form-field-multiple-labels**. A field with more than one `label` element (by `for` or by wrapping, HTML 4.10.4) is
  needs-review: assistive technologies join the labels in an order they choose. A field with exactly one passes. *No fix:*
  which text to keep is content.
- **label-content-name-mismatch** (ACT 2ee8b8, axe experimental). For a control whose role takes its name from content
  (button, checkbox, gridcell, link, menuitem, menuitemcheckbox, menuitemradio, option, radio, switch, tab, treeitem) and
  whose name comes from `aria-label` or `aria-labelledby`, the name must contain the visible label. Both are normalised
  by lower-casing letters and digits and dropping punctuation and symbols. A one-character label (an X for close) counts
  as a symbol and passes, as ACT passes it. The visible label is a layout question:
  - *With rendered facts:* the text of each descendant text node that has a laid-out box with an area, in the layout's own
    form (`FactsUsed`: `layout text`, `text boxes`). Text without a box is not visible.
  - *Without:* the rule decides from the tree when no style on the control or below it can hide, move, clip or redraw the
    text. The styles that stop it are a computed `opacity` of 0, a `font-size` of 0, a `clip-path`, a `clip`, a
    `text-indent`, a `transform`, an overflow other than visible, a negative `left` or `top` on an absolutely positioned
    element, and a `font-family` with "icon" or "symbol" in it (icon fonts draw words as glyphs). Then the result is
    `NeedsRender` with the missing facts `layout text of the visible label` and `text boxes of the visible label`.
    Ancestors of the control are not examined.
  - *Difference:* axe's rule is experimental and applies only to names from `aria-label`/`aria-labelledby`; ACT also passes
    controls whose name is their content, and so do we, but we report only the first kind. *No fix:* the name is the
    author's wording.

### Frames and the page

- **frame-title** (ACT cae760). An exposed `iframe` or `frame` needs a name. An `iframe` with a negative `tabindex` is not
  applicable (ACT cae760: it is not in sequential focus navigation). *No fix.*
- **frame-title-unique** (ACT 4b1c6c). Frames with the same name (compared with lower-cased ASCII and collapsed white
  space) must serve the same purpose. Frames that show the same resource (the same `src` once resolved against the
  document's `base`, or the same `srcdoc`) pass. Frames that show different resources are needs-review: whether they are
  equivalent needs a person who sees the pages. Frames with a name nobody else has are not applicable. *Difference:* axe's
  description says frames must have unique titles, which would fail these; ACT lets equivalent frames share a name, and
  wins. *No fix.*
- **document-title** (ACT 2779a5). The first `title` element in the document (HTML namespace; an SVG `title` does not
  count) must have text. The result targets the `html` element. *No fix:* the title is content.

### Links

- **identical-links-same-purpose** (ACT b20e66). Links (the role link) with the same name must serve the same purpose.
  Links whose targets resolve to the same address pass; links to different addresses, or with no `href` (a script-driven
  link), are needs-review. A name nobody else has is not applicable. Targets resolve against the document's `base` or, when
  it has none, a fixed placeholder address (the library does not know the page's own address; two targets that are the
  same resource in the real page are the same here). SVG links with an `href` or `xlink:href` are links. *No fix.*
- **link-in-context-descriptive** (ACT 5effbb). Review only: every exposed link with a name is listed as needs-review with
  the name found. *No fix.*

## Measured against axe-core 4.14.0

Differences from the differential that we keep (axe-core 4.14.0 on the right):

- **image-alt:** `<img alt="" tabindex="0">` fails (a focusable image is not decorative; ACT 23a2a8). Axe passes any image with an `alt` attribute.
- **link-name:** a link whose only content is an element with a `title` (a `picture`, say) has a name here, by AccName 2F and 2I; axe reports a violation because it wants visible text, an `aria-label` or an image `alt` (21 nodes in the sample; one more link there, whose content is an empty icon `span`, passes here and fails in axe).
- **label, form-field-multiple-labels:** with a duplicated id a `label for` points at the first element only; the second
  field has no name here, while axe reports it as labelled or as incomplete.
- **label-content-name-mismatch:** visible text of a single character, such as the "X" of a close button named "Close", is non-text content for axe (incomplete); we pass it.
- **identical-links-same-purpose:** reported for the later links of a group (axe reports the first link of the group).
- **aria-dialog-name, form-field-multiple-labels:** the sample shows differences that have not been explained; they are listed in the differential's classes (`docs/axe-differential.md`, "Open"). `image-redundant-alt` is explained there (fail|none: images in `noscript`).

## ACT results

The vendored ACT test cases are run by `NamesActTests` (see the ARIA harness for the page-level judgement). A rule with
a person-judged outcome (`frame-title-unique`, `identical-links-same-purpose`, `link-in-context-descriptive`,
`image-name-descriptive`) answers needs-review where ACT expects passed or failed; those cases are counted as *judgement*.
Cases the static checks cannot decide are *n/a*, each with its reason in `testdata/act/names-expectations.tsv`.

| ACT rule | Rules answering | Cases | Pass | Judgement | N/A | Fail |
|---|---|---|---|---|---|---|
| 97a4e1 button | button-name, input-button-name, aria-command-name (role button) | 17 | 17 | 0 | 0 | 0 |
| c487ae link | link-name, area-alt, aria-command-name (role link) | 28 | 28 | 0 | 0 | 0 |
| e086e5 form field | label, select-name, aria-input-field-name, aria-toggle-field-name | 17 | 17 | 0 | 0 | 0 |
| 23a2a8 image | image-alt, role-img-alt, svg-img-alt | 18 | 18 | 0 | 0 | 0 |
| 59796f image button | input-image-alt | 12 | 12 | 0 | 0 | 0 |
| cae760 iframe | frame-title | 10 | 10 | 0 | 0 | 0 |
| 4b1c6c identical iframe names | frame-title-unique | 23 | 12 | 9 | 2 | 0 |
| 2779a5 page title | document-title | 11 | 10 | 0 | 1 | 0 |
| 2ee8b8 label in name | label-content-name-mismatch | 13 | 12 | 0 | 1 | 0 |
| 5effbb link in context | link-in-context-descriptive | 18 | 3 | 15 | 0 | 0 |
| b20e66 identical link names | identical-links-same-purpose | 21 | 6 | 14 | 1 | 0 |
| ffd0e9 heading | empty-heading | 15 | 14 | 0 | 1 | 0 |
| m6b1q3 menuitem | aria-command-name | 8 | 8 | 0 | 0 | 0 |
| qt1vmo image name descriptive | image-name-descriptive | 16 | 10 | 6 | 0 | 0 |
| 7d6734 SVG image | svg-img-alt | 10 | 10 | 0 | 0 | 0 |
| 8fc3b6 object | object-alt | 14 | 14 | 0 | 0 | 0 |

The n/a cases: two iframe cases whose frames come from a script (shadow root) or a nested `srcdoc` (the library does not
parse frame contents); one link case with a link inside an `srcdoc`; a standalone SVG document served as HTML (2779a5); an
icon-font label (2ee8b8, the rule answers needs-render); and an `img` with role presentation inside a heading whose alt
text our name computation still gives to the heading (ffd0e9 expects an empty name; the name computation is the tree's,
and the Chrome differential will say which is right).

ACT rules in the vendored set whose subject is names but which this area does not implement: aizyf1 (link is descriptive),
cc0f0a (form field label is descriptive), b49b2e (heading is descriptive), c4a8a4 (page title is descriptive), fd3a94
(identical links in the same context) and e88epe (image not in the tree is decorative). The first four are review-only
and would list every link, label, heading or title; fd3a94 needs a context definition. They are left for the next pass,
and 9eb3f6 (image file name as name) is deprecated.
