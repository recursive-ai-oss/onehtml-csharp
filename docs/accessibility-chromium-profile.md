# Chromium profile for roles

Where WAI-ARIA 1.2, HTML-AAM and Core-AAM leave a choice, the accessibility tree follows what Chromium exposes. This
file lists every such choice made in the role computation (`RecursiveAI.OneHtml.Accessibility.Aria`). It is normative for
the library: a port must make the same choices. The same list is available in code as `ChromiumProfile.Entries`.

Each entry has an id, the choice, the browser behaviour it matches with where that is documented, and its evidence:

- **test suite**: a web-platform-tests page expects the behaviour and the library passes it. The pages test browsers
  through `get_computed_role`, so this is evidence of what Chromium and the other engines agree on;
- **specification**: a normative statement in the specification fixes the choice;
- **unverified until the Chrome differential**: inferred from public documentation or spec notes. The differential that
  compares the computed tree with Chrome's `Accessibility.getFullAXTree` output either confirms or corrects it.

Entries are added when a choice is made, never silently. Names and descriptions are not part of this list: they belong to
the name computation.

## `generic-fallback`

- **Topic:** HTML elements HTML-AAM does not list
- **Choice:** Exposed with the generic role (font, center, marquee, unknown elements, autonomous custom elements).
- **Matches:** Chrome exposes unlisted elements as generic containers.
- **Evidence:** **unverified until the Chrome differential**

## `empty-alt-image`

- **Topic:** img with an empty alt
- **Choice:** The role is none unless aria-label has text or aria-labelledby names an element. A title does not name it.
- **Matches:** wpt html-aam/roles-contextual.html el-img-empty-alt-title and the aria-label and aria-labelledby cases.
- **Evidence:** test suite

## `unnamed-landmark-token`

- **Topic:** role=region or role=form without a name
- **Choice:** The token is skipped like an unknown token, so the next token or the native role applies.
- **Matches:** wpt wai-aria/role/region-roles.html, form-roles.html, fallback-roles.html; WAI-ARIA 1.2 section 9.1.
- **Evidence:** test suite

## `named-native-landmarks`

- **Topic:** section, form and sectioned aside
- **Choice:** region, form and complementary only with an accessible name; otherwise generic. A title attribute counts as a name.
- **Matches:** wpt html-aam/roles-contextual.html el-section-title, el-aside-in-section-title.
- **Evidence:** test suite

## `section-header-footer`

- **Topic:** header and footer outside the body scope
- **Choice:** generic. The HTML-AAM draft's sectionheader and sectionfooter roles are not produced.
- **Matches:** The matching tentative wpt pages expect the draft roles and are listed as known failures.
- **Evidence:** **unverified until the Chrome differential**

## `landmark-scope`

- **Topic:** What scopes header, footer and aside
- **Choice:** The nearest ancestor among article, aside, nav, section (sectioning), main, body. header and footer are landmarks only with no such ancestor except body; aside is complementary when the nearest is main or body.
- **Matches:** wpt html-aam/roles-contextual.html and aside-in-prefixed-article.html.
- **Evidence:** test suite

## `li-accessibility-parent`

- **Topic:** li
- **Choice:** listitem when the nearest ancestor with a role other than generic or presentational has the list role (ul, ol, menu, dir, role=list); otherwise generic.
- **Matches:** wpt html-aam/roles-generic.tentative.html el-li-within-generic-within-list-parent, el-li-within-explicit-role-list-parent.
- **Evidence:** test suite

## `th-header-kind`

- **Topic:** th in the auto scope state
- **Choice:** columnheader inside thead or in a row with no td; rowheader in a row that has a td. scope=col|colgroup gives columnheader, scope=row|rowgroup gives rowheader.
- **Matches:** wpt html-aam/table-roles.html. The auto rule is a simplification of the HTML header-cell algorithm.
- **Evidence:** **unverified until the Chrome differential**

## `computed-role-names`

- **Topic:** Names of computed roles
- **Choice:** presentation is reported as none, img as image, directory as list.
- **Matches:** wpt wai-aria/role/synonym-roles.html; Core-AAM computed role.
- **Evidence:** test suite

## `role-token-matching`

- **Topic:** Role tokens
- **Choice:** Split on the HTML ASCII whitespace; matched ASCII case-insensitively; the first valid, non-abstract token wins; image is accepted for img.
- **Matches:** wpt wai-aria/role/fallback-roles.html, invalid-roles.html, roles.html.
- **Evidence:** test suite

## `presentational-conflict`

- **Topic:** none or presentation on a focusable element or one with a global aria-* attribute
- **Choice:** Ignored: the implicit role applies. Any state or property marked global in WAI-ARIA 1.2 counts, deprecated ones and prohibited ones included. Non-global attributes do not.
- **Matches:** wpt wai-aria/role/role_none_conflict_resolution.html and .tentative.html.
- **Evidence:** test suite

## `inherited-presentational`

- **Topic:** Required owned elements of a presentational element
- **Choice:** They are presentational too, even with global aria-* attributes; a focusable one is not. An owned element with its own non-presentational role keeps it.
- **Matches:** wpt wai-aria/role/role_none_conflict_resolution_spec_ambiguities.tentative.html.
- **Evidence:** test suite

## `children-presentational`

- **Topic:** Descendants of roles with presentational children
- **Choice:** Reported as none with the origin ChildrenPresentational, unless focusable.
- **Matches:** WAI-ARIA 1.2 section 5.2.9; wpt marks the nested-button cases ambiguous.
- **Evidence:** **unverified until the Chrome differential**

## `static-focusable`

- **Topic:** Focusable by markup
- **Choice:** Valid tabindex, links with href, enabled form controls except hidden inputs, iframe, the first summary of a details, audio and video with controls, editing hosts, none of it inside an inert subtree. Style is not consulted.
- **Matches:** HTML 6.6.3 focusable area; iframe, media controls and summary behaviour are unverified.
- **Evidence:** **unverified until the Chrome differential**

## `empty-aria-value`

- **Topic:** An empty aria-* value
- **Choice:** Treated as not specified and not reported.
- **Matches:** WAI-ARIA 1.2 section 9.2 for booleans; extended to all types.
- **Evidence:** **unverified until the Chrome differential**

## `heading-default-level`

- **Topic:** role=heading without aria-level
- **Choice:** Level 2 (DefaultHeadingLevel).
- **Matches:** Browsers expose level 2; WAI-ARIA 1.2 leaves it undefined.
- **Evidence:** **unverified until the Chrome differential**

## `option-context`

- **Topic:** option outside select and datalist
- **Choice:** No role.
- **Matches:** HTML-AAM lists option only in a list of options.
- **Evidence:** **unverified until the Chrome differential**

## `summary-role`

- **Topic:** summary
- **Choice:** The first summary child of a details has no role of its own; any other summary is generic.
- **Matches:** wpt html-aam/summary.html.
- **Evidence:** test suite

## `foreign-elements`

- **Topic:** SVG and MathML elements
- **Choice:** Only the roots are mapped (svg: graphics-document, math: math); other foreign elements have no implicit role until SVG-AAM is applied.
- **Matches:** HTML-AAM defers both to their own mapping documents.
- **Evidence:** specification

## `switch-input`

- **Topic:** input type=checkbox with the switch attribute
- **Choice:** switch.
- **Matches:** wpt html-aam/roles.tentative.html el-input-checkbox-switch.
- **Evidence:** test suite

## `select-kind`

- **Topic:** select
- **Choice:** listbox when multiple is present or size is above 1; otherwise combobox.
- **Matches:** wpt html-aam/roles.html el-select-listbox; HTML-AAM select entries.
- **Evidence:** test suite

## `input-suggestions`

- **Topic:** input with a list attribute
- **Choice:** combobox when list names a datalist in the same document, for the text, search, tel, url and email types.
- **Matches:** HTML-AAM el-input-textetc-autocomplete.
- **Evidence:** specification

## `required-attribute-native`

- **Topic:** Required states and properties of an explicit role on a native element
- **Choice:** Supplied by the native element: heading level for h1-h6, checked for checkbox and radio inputs, selected for option, valuenow for range and number inputs, meter and progress, expanded and controls for select and text inputs. Required attributes are checked only for explicit roles that differ from the element's native role.
- **Matches:** ARIA in HTML conformance model and HTML-AAM section 3.6.
- **Evidence:** **unverified until the Chrome differential**

## `value-minimums`

- **Topic:** Lower bounds of integer attributes
- **Choice:** aria-level, aria-posinset, aria-colindex, aria-rowindex, aria-colspan at least 1; aria-rowspan at least 0; aria-setsize, aria-colcount, aria-rowcount at least -1.
- **Matches:** WAI-ARIA 1.2 prose of those properties.
- **Evidence:** specification
