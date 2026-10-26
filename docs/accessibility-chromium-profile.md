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

Entries are added when a choice is made, never silently. The entries after `value-minimums` belong to the tree, the
name computation, states and focus.

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


## Tree, names, states, focus

The entries below belong to the accessibility tree (`docs/accessibility-tree.md`).
## `tree-keeps-ignored`

- **Topic:** Ignored nodes in the tree
- **Choice:** The tree keeps nodes that are ignored (hidden by visibility, aria-hidden or inert, presentational, uninteresting) with their reasons, and the children of an ignored node stay its children, as getFullAXTree returns it. Nodes that are not rendered at all have no tree node.
- **Matches:** Chrome returns ignored nodes with ignored=true and ignoredReasons; the exact set of nodes is checked by the differential.
- **Evidence:** **unverified until the Chrome differential**

## `ignored-reason-names`

- **Topic:** Spelling of ignored reasons
- **Choice:** notVisible (visibility), ariaHiddenElement and ariaHiddenSubtree, inertElement and inertSubtree, presentationalRole, uninteresting. An ignored node is exported with the role none.
- **Matches:** Names follow what the DevTools Protocol returns for ignoredReasons.
- **Evidence:** **unverified until the Chrome differential**

## `generic-pruning`

- **Topic:** Generic elements
- **Choice:** A generic element is ignored as uninteresting unless it has a name or description, is focusable, or carries a role, tabindex, contenteditable or title attribute or any aria-* attribute. An element with no role at all is ignored too.
- **Matches:** Chrome prunes generic containers that carry no information.
- **Evidence:** **unverified until the Chrome differential**

## `static-text-nodes`

- **Topic:** Text nodes
- **Choice:** Each text node is a StaticText node with its text, white space collapsed. A white-space-only text node is kept, as an ignored node, only when its parent is inline. Leading and trailing white space is trimmed at the start and end of a block. There are no InlineTextBox children.
- **Matches:** Chrome exposes StaticText with InlineTextBox children; the boxes need layout.
- **Evidence:** **unverified until the Chrome differential**

## `not-rendered-absent`

- **Topic:** Elements that are not rendered
- **Choice:** display: none (including the hidden attribute and template through the user-agent sheet), the contents of a closed details other than its first summary, and the contents of content-visibility: hidden have no tree node. An area inside a map is exposed although the rendering rules give it display: none.
- **Matches:** HTML section 15.3.1, HTML section 4.11.1, CSS Containment 2 section 4.2; the area exception follows browsers that expose image-map areas.
- **Evidence:** **unverified until the Chrome differential**

## `name-hidden-referenced`

- **Topic:** Hidden nodes in a name
- **Choice:** A hidden node contributes nothing, except a root of the computation or a node named by aria-labelledby or aria-describedby. Below such a root, a node is left out only when hidden for a reason the root does not have: a hidden root brings its whole subtree in. A node hidden only by visibility still lets its visible descendants through. Native label, caption and legend elements are not directly referenced: hidden ones are skipped.
- **Matches:** wpt accname/name/comp_labelledby_hidden_nodes.html, comp_hidden_not_referenced.html, html-aam/hidden-names.html.
- **Evidence:** test suite

## `labelledby-fallthrough`

- **Topic:** aria-labelledby that names no text
- **Choice:** When every referenced node gives no text, the computation continues with aria-label and the later steps.
- **Matches:** AccName 1.2 step 2B is silent; wpt accname/name/comp_label.html passes with this reading.
- **Evidence:** test suite

## `labelledby-used-once`

- **Topic:** A node named by aria-labelledby inside another computation
- **Choice:** A node used as an aria-labelledby or aria-describedby target in a computation is left out when the same computation meets it again as content.
- **Matches:** wpt accname/name/comp_name_from_content.html (heading with two links).
- **Evidence:** test suite

## `embedded-controls`

- **Topic:** Embedded controls
- **Choice:** A textbox, combobox, listbox, slider, spinbutton, progressbar, meter or scrollbar inside a label, a referenced node or contents being read contributes its value (input value, textarea text, selected options, aria-valuetext or aria-valuenow) and its own aria-label is ignored. The element being named is exempt, so aria-labelledby naming itself still reads its aria-label. A non-native combobox with no selected option is read as contents.
- **Matches:** wpt accname/name/comp_embedded_control.html and manual/name_*_label_embedded_*.
- **Evidence:** test suite

## `name-prohibited-roles`

- **Topic:** Naming of roles that prohibit it
- **Choice:** aria-labelledby, aria-label and title still name elements whose role prohibits naming (generic, paragraph and so on); such roles never take a name from contents.
- **Matches:** wpt html-aam/accname-computation-by-element/phrasing-elements.html and accname/name/comp_label.html.
- **Evidence:** test suite

## `native-name-sources`

- **Topic:** Native name sources
- **Choice:** Form controls: label elements, then (buttons) value, then title, then placeholder, then aria-placeholder; input type=image: labels, alt, title, then Submit; submit and reset buttons fall back to Submit and Reset. img and area: alt (an img with alt="" has no name from title or contents); an img with no alt that fills its figure takes the figcaption after title. fieldset: the first rendered legend; table: the first rendered caption; figure: its figcaption; optgroup, option and track: label; svg elements: the first title child. A select is never named from its options.
- **Matches:** wpt html-aam/accname-computation-by-element/*, accname/name/comp_tooltip.html, comp_host_language_label.html.
- **Evidence:** test suite

## `generated-content-names`

- **Topic:** CSS generated content in names
- **Choice:** ::before and ::after content is part of the name of the contents: strings, attr(), counter() and counters() (CSS Lists 3 scopes, including the counter properties of the pseudo-element itself) and quotes (open-quote and close-quote as the curly double quotes). Alternative text after / replaces the content and is set off by a space on each side; a block-level pseudo-element likewise. ::marker boxes are not generated.
- **Matches:** wpt accname/name/comp_name_from_content.html, comp_name_from_content_alt_counter_multi_instance.html; the ::marker pages are tentative and listed as known failures.
- **Evidence:** test suite

## `text-transform-in-names`

- **Topic:** text-transform in names
- **Choice:** The transform of the parent element (uppercase, lowercase, capitalize) is applied to the text of a text node before it joins a name.
- **Matches:** wpt accname/name/comp_name_from_content.html.
- **Evidence:** test suite

## `name-spacing`

- **Topic:** Joining in name from contents
- **Choice:** A child whose display is not inline (or contents) is set off by a space on each side; br adds a space; inline children run together; a descendant holding only white space keeps it. The result has white space collapsed and trimmed (ASCII white space only).
- **Matches:** wpt accname/name/comp_name_from_content.html, comp_text_node.html.
- **Evidence:** test suite

## `aria-owns-resolution`

- **Topic:** aria-owns
- **Choice:** Owners are visited in document order and the first owner of an element wins. An element never owns itself, the root element or an ancestor of the owner in the tree as built so far. aria-owns on an element that is not rendered, aria-hidden or inert, or naming an element that is not rendered or has visibility: hidden, is ignored. Owned elements follow the DOM children of the owner in the order of the attribute. Work is bounded.
- **Matches:** wpt accname/aria-owns.html; WAI-ARIA 1.2 section 6.6.6.
- **Evidence:** test suite

## `aria-disabled-inherited`

- **Topic:** aria-disabled on ancestors
- **Choice:** An element is disabled when an ancestor has aria-disabled="true", as well as by its own attribute or a disabled fieldset.
- **Matches:** Chrome exposes disabled on descendants of an aria-disabled element.
- **Evidence:** **unverified until the Chrome differential**

## `default-states`

- **Topic:** States exposed without an attribute
- **Choice:** A combobox is expanded=false; checkbox, radio, switch and menuitem checkbox and radio roles are checked=false; option and tab are selected=false. Range roles (slider, scrollbar, meter, progressbar) default to 0 and 100 and a slider with no value sits at the midpoint; an input type=range clamps its value into its range.
- **Matches:** HTML section 4.10.5.1.13; Chrome exposes these states.
- **Evidence:** **unverified until the Chrome differential**

## `set-position`

- **Topic:** posinset and setsize
- **Choice:** Items with the role listitem, menuitem (and its checkbox and radio forms), option, radio, tab, treeitem, or article directly in a feed, form a set with the siblings of that kind under the nearest ancestor that is not generic, none, ignored or (for treeitem and menuitem) a group. A native radio button belongs to the set of its name in its form. Ignored items are not counted; an explicit aria-posinset restarts the count and the first aria-setsize found is used for the set.
- **Matches:** Core-AAM section 5.4 and HTML-AAM; unverified in the differential.
- **Evidence:** **unverified until the Chrome differential**

## `focus-order`

- **Topic:** Sequential focus order
- **Choice:** Positive tabindex values ascending, then tabindex 0 and natively focusable elements in document order. A negative tabindex is focusable but not in the order. Disabled, inert, not rendered and visibility: hidden elements are not focusable. Radio buttons are not collapsed to one tab stop per group and scrollers are not made focusable.
- **Matches:** HTML section 6.6.3; Chrome differs for radio groups and scrollers.
- **Evidence:** **unverified until the Chrome differential**

## `export-role-names`

- **Topic:** Role names in the Chrome-shaped export
- **Choice:** Core-AAM computed role names are used, except br as LineBreak, label as LabelText, legend as Legend and iframe as Iframe (internal roles), StaticText for text and RootWebArea for the document.
- **Matches:** Chrome DevTools Protocol AXNode role values.
- **Evidence:** **unverified until the Chrome differential**
