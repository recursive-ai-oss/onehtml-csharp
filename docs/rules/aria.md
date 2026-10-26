# ARIA rules

The first rule area of the Validation package: the axe-core 4.14.0 rules whose id starts with `aria-` (the deprecated
`aria-roledescription` left out), `presentation-role-conflict`, and the ACT rules that match them. Ids, tags and impacts
come from the axe-core rule table as facts; every description and help text is our own. How to run them is in
[checker.md](checker.md), the result shape in [results.md](results.md).

All rules here are static: they read the document, the cascade and the accessibility tree, and need no rendered facts.
Unless a rule says otherwise it applies to elements that are exposed to assistive technology (`CheckContext.IsExposed`:
in the tree, not hidden by `display`, `hidden`, `aria-hidden`, `inert` or `visibility`), which is the "included in the
accessibility tree" of the ACT rules. The ACT rules that apply "to any element" or to attributes wherever they are
(5f99a7, 6a7281, 6cfa84, 307n5z, 46ca7f) use every element in the document.

## The rules

Columns: sources, criteria (or best practice), axe impact, and the fix decision. A suggested edit exists only where the
rule names exactly one correct, judgement-free fix; the library never applies it.

| Rule | Sources | Criteria | Impact | Suggested edit |
|---|---|---|---|---|
| aria-allowed-attr | axe, ACT 5c01ea | 4.1.2 | critical | none |
| aria-allowed-role | axe | best practice | minor | none |
| aria-braille-equivalent | axe | 4.1.2 | serious | none |
| aria-command-name | axe, ACT m6b1q3 | 4.1.2 | serious | none |
| aria-conditional-attr | axe, ACT 5c01ea | 4.1.2 | serious | remove `aria-checked` |
| aria-deprecated-role | axe | 4.1.2 | minor | `directory` → `list` |
| aria-dialog-name | axe | best practice | serious | none |
| aria-hidden-body | axe | 1.3.1, 4.1.2 | critical | remove `aria-hidden` |
| aria-hidden-focus | axe, ACT 6cfa84 | 4.1.2 | serious | none |
| aria-input-field-name | axe | 4.1.2 | serious | none |
| aria-meter-name | axe | 1.1.1 | serious | none |
| aria-progressbar-name | axe | 1.1.1 | serious | none |
| aria-prohibited-attr | axe, ACT 5c01ea, kb1m8s | 4.1.2 | serious | remove the attribute |
| aria-required-attr | axe, ACT 4e8ab6 | 4.1.2 | critical | none |
| aria-required-children | axe, ACT bc4a75 | 1.3.1 | critical | none |
| aria-required-parent | axe, ACT ff89c9 | 1.3.1 | critical | none |
| aria-roles | axe, ACT 674b10 | 4.1.2 | critical | none |
| aria-tab-name | axe | 4.1.2 | serious | none |
| aria-text | axe | best practice | serious | none |
| aria-toggle-field-name | axe | 4.1.2 | serious | none |
| aria-tooltip-name | axe | 4.1.2 | serious | none |
| aria-treeitem-name | axe | best practice | serious | none |
| aria-valid-attr | axe, ACT 5f99a7 | 4.1.2 | critical | remove the attribute |
| aria-valid-attr-value | axe, ACT 6a7281 | 4.1.2 | critical | remove the attribute (not a required one) |
| presentation-role-conflict | axe, ACT 46ca7f | best practice | minor | none |

`nested-interactive` (axe, ACT 307n5z) is the same check as the ACT rule about presentational children; it is listed in
[structure.md](structure.md) with the other focus rules.

### What each rule checks and why it has the fix it has

- **aria-valid-attr** (ACT 5f99a7). Every attribute whose name starts with `aria-` on an HTML or SVG element must be a
  state or property that WAI-ARIA 1.2 (with the 1.3 draft properties the role data includes) defines. *Fix:* remove an
  undefined attribute. No browser acts on it, so removing it changes nothing a user perceives.
- **aria-valid-attr-value** (ACT 6a7281). Each non-empty defined state or property has a value of its type (true/false,
  tristate, token from the allowed list, token list, integer, number, single ID reference). An ID reference or list that
  the element's role *requires* must name at least one element that exists. A broken reference to a property the role does
  not require is not a failure here (ACT passes it; the tree reports it as a diagnostic). *Fix:* remove an attribute with
  an invalid value, because the browser already falls back to the attribute's default. *No fix* when the role requires
  that attribute (removing it would trade this failure for a missing required attribute), and none for a missing required
  reference.
- **aria-allowed-attr**. A defined attribute must be supported by the element's role (own, required or inherited), be
  global, or, on an element with no role, be one that ARIA in HTML grants to the element (`aria-disabled`,
  `aria-invalid` and `aria-required` on `input`; `aria-disabled` and `aria-haspopup` on `summary`). *No fix:* an
  unsupported attribute may mean the role is wrong rather than the attribute, and choosing which is a judgement.
- **aria-prohibited-attr** (ACT 5c01ea, kb1m8s). The role prohibits the attribute (`aria-label` and `aria-labelledby` on
  `generic`, `presentation`, `paragraph`, …; the exception list of each global attribute). *Fix:* remove the attribute,
  since authors may not set it on that role.
- **aria-conditional-attr**. `aria-checked` on a native `input type=checkbox` must agree with the `checked` attribute;
  `mixed` cannot be judged from markup (indeterminate is set by script) and is needs-review. *Fix:* remove `aria-checked`
  when it disagrees, because the native state is the one assistive technology reports.
- **aria-roles** (ACT 674b10). A non-empty `role` attribute on an exposed HTML or SVG element has at least one token that
  names a non-abstract WAI-ARIA, DPUB or Graphics role (`image` counts as `img`). A list of fallback roles passes if any
  token is valid. *No fix:* the intended role of a misspelt token is unknown; removing a bad token before a valid one is
  never needed because such a list passes.
- **aria-deprecated-role**. The role that applies (the first valid token) is not `directory`, `doc-biblioentry` or
  `doc-endnote`. *Fix:* only `directory` → `list`, which WAI-ARIA 1.2 names as its replacement; the two DPUB roles name
  no single replacement.
- **aria-allowed-role** (best practice). The explicit role is one that ARIA in HTML allows on the element, or the
  element's own role. The table is `Rules/Data/allowed-roles.json`, generated by `tools/validation/generate-allowed-roles.py`
  from the ARIA in HTML Recommendation; context-dependent rows (`div` in `dl`, `li` in a list, table cells and rows by
  the table's role, `header`, `footer`, `figure`, `summary`, `select`, `img` without a name, `input` by type) are
  resolved in code. Elements with no row, and custom elements, are not checked. *No fix:* a different role changes what
  the element means.
- **aria-required-attr** (ACT 4e8ab6). An element with an explicit role, other than the role it has natively, sets every
  state or property the role requires (`aria-checked` for `checkbox`, `aria-valuenow` for `slider`, `aria-controls` and
  `aria-expanded` for `combobox`, …), non-empty, unless the native element supplies it. *No fix:* the value is the page's
  own data.
- **aria-required-children** (ACT bc4a75). An element with an explicit role, other than its native one, that requires
  owned elements owns at least one with a required role *and owns only such elements*. "Owns" means the children in the
  accessibility tree (after `aria-owns`), looking through nodes the tree ignores (plain `div`, `presentation`); a node the
  tree keeps (a `div` with `aria-live`, say) counts as an owned element with its own role. A group between them is
  allowed and its children are checked in turn (`menu > group > menuitem`). Comboboxes and elements with
  `aria-busy="true"` (or inside one) are exempt. *No fix:* adding rows or items is content; wrapping changes structure.
- **aria-required-parent** (ACT ff89c9). An element with an explicit role, other than its native one, that requires a
  context role has, as its parent in the accessibility tree (ignored nodes skipped), an element with that role, directly
  or through a group that the context role's owned chain allows. *No fix:* the right container is a layout decision.
- **aria-hidden-focus** (ACT 6cfa84). An element with `aria-hidden="true"` and every element inside it are not part of
  sequential focus navigation (not focusable, or `tabindex` negative, disabled, inert or not rendered). *No fix:* hiding
  from assistive technology, making unfocusable, and showing are three different outcomes.
- **presentation-role-conflict** (ACT 46ca7f). An element marked decorative (`role=none` or `presentation`, or an `img`
  with `alt=""`) that is in the accessibility tree must end up with the role `none`/`presentation`. Conflict resolution
  (WAI-ARIA 1.2 section 8.4) takes the presentational role away from a focusable element or one with a global attribute;
  the role layer applies it to all of them, including an `img` with an empty `alt`, so the rule reads the computed role.
  Not in the tree counts as passed.
  *No fix:* there are two (drop the role or drop what conflicts), and the author's intent decides.
- **aria-hidden-body**. `body` must not carry `aria-hidden="true"`. *Fix:* remove it; no reading of the page is served by
  hiding all of it.
- **aria-text** (best practice). An element whose first `role` token is `text` contains no focusable descendant. *No fix.*
- **aria-braille-equivalent**. `aria-braillelabel` needs an accessible name and `aria-brailleroledescription` needs a
  non-empty `aria-roledescription`. *No fix:* the text is content.
- **The name rules** (`aria-command-name`, `aria-input-field-name`, `aria-toggle-field-name`, `aria-dialog-name`,
  `aria-meter-name`, `aria-progressbar-name`, `aria-tab-name`, `aria-tooltip-name`, `aria-treeitem-name`). An exposed
  element whose role comes from the `role` attribute and is one of the rule's roles has a non-empty computed accessible
  name (AccName 1.2, through the tree). Roles: command: button, link, menuitem; input field: combobox, listbox, searchbox,
  slider, spinbutton, textbox; toggle: checkbox, menuitemcheckbox, menuitemradio, option, radio, switch; dialog: dialog,
  alertdialog; the rest one role each. Elements named by their native element (a `button` with text) are the business of
  the names-and-labels area. *No fix:* a name is content.

## Where the rules follow ACT instead of axe

The ACT rule wins where the two differ, and each difference is a place to expect a mismatch against axe until the axe
captures are compared. These are from reading the rule texts and the test cases, not yet from axe output.

- `aria-roles`: a list of fallback roles passes if any token is valid.
- `aria-valid-attr-value`: a broken ID reference to a property the role does not require passes.
- `aria-required-children`: extra children that are not required owned roles fail, not only a missing required one;
  `list` also accepts `group > listitem`; the role data lists that chain for `list` (see `accessibility-roles.md`).
- `aria-required-attr`: an empty value counts as missing.
- `aria-allowed-attr`: on an element with no role only global attributes (and the ARIA in HTML grants above) pass.
- Results here never use needs-review except `aria-conditional-attr` with `mixed`. Axe lists "needs review" outcomes for
  several of these rules (for example broken references and `aria-braille-equivalent`); where ours decides statically,
  expect a `violations` or `passes` entry for axe's `incomplete` one.
- Nothing is evaluated for `aria-roledescription` (deprecated in axe 4.14.0) or ACT in6db8 (deprecated in ACT).

## ACT test cases

`AriaActTests` runs every vendored case of the ACT rules above. A case's expectation is for the whole page: failed when
some target fails, passed when targets exist and all pass, inapplicable when none applies. Cases whose expectation needs
script are N/A, with the reason in `testdata/act/aria-expectations.tsv`.

| ACT rule | Our rule(s) | Cases | Pass | N/A | Fail |
|---|---|---|---|---|---|
| 307n5z Element with presentational children has no focusable content | nested-interactive | 7 | 7 | 0 | 0 |
| 46ca7f Element marked as decorative is not exposed | presentation-role-conflict | 10 | 10 | 0 | 0 |
| 4e8ab6 Element with role attribute has required states and properties | aria-required-attr | 8 | 8 | 0 | 0 |
| 5c01ea ARIA state or property is permitted | aria-allowed-attr, aria-prohibited-attr, aria-conditional-attr | 15 | 15 | 0 | 0 |
| 5f99a7 ARIA attribute is defined in WAI-ARIA | aria-valid-attr | 7 | 7 | 0 | 0 |
| 674b10 Role attribute has valid value | aria-roles | 10 | 10 | 0 | 0 |
| 6a7281 ARIA state or property has valid value | aria-valid-attr-value | 24 | 24 | 0 | 0 |
| 6cfa84 Element with aria-hidden has no content in sequential focus navigation | aria-hidden-focus | 14 | 13 | 1 | 0 |
| bc4a75 ARIA required owned elements | aria-required-children | 19 | 19 | 0 | 0 |
| ff89c9 ARIA required context role | aria-required-parent | 15 | 13 | 2 | 0 |
| m6b1q3 Menuitem has non-empty accessible name | aria-command-name | 8 | 8 | 0 | 0 |
| total | | 137 | 134 | 3 | 0 |

The three N/A cases: 6cfa84 Passed Example 4 passes only because a focus listener moves focus away at once;
ff89c9 Passed Example 6 and Failed Example 4 build their shadow root with a script.

ACT rules not run here: 97a4e1 (button), c487ae (link) and e086e5 (form field) cover native elements as well as
role-given ones, and belong to the names-and-labels area; in6db8 is deprecated; kb1m8s has no test cases (it is covered
by `aria-prohibited-attr`).

## Suggested edits are checked

`SuggestedEditTests` applies the suggested edits of every vendored ACT page of these rules and of hand-written pages
through `Document.Apply` and through replay of their saved form, requires identical output, requires that each finding
with a suggestion is gone afterwards, and that no failure appears that was not there before.
