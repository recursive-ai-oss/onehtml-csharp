# The accessibility tree

`AccessibilityTree.Build(document, styles, context?, options?, cancellationToken)` in `RecursiveAI.OneHtml.Accessibility`
computes the objects a browser exposes to assistive technology, from a parsed document and its cascade. This file is
normative for what the tree contains and how each part is computed; the choices the specifications leave open are in
`accessibility-chromium-profile.md`, and the role layer it builds on is in `accessibility-roles.md`.

## Pipeline

All walks are loops over explicit stacks or cursors; nothing recurses on the depth of the input.

1. **Hidden status** of every element and text node (below).
2. **`aria-owns`** re-parenting (below).
3. **Roles**, twice. The first pass leaves name-dependent roles unresolved (`RoleWhenNamed`) so that a name computation
   can run; the second pass supplies that computation as `RoleOptions.HasAccessibleName`, which decides `section`, `form`,
   `aside` and the `region` and `form` role tokens. The tree's roles are the second pass's.
4. **Nodes** for the document, every element that is rendered and every text node that is kept, in document order, then
   linked into children (own DOM children that were not adopted, then owned elements) and numbered depth first.
5. **Per node:** name and description, states, relations, focus, ignored status.
6. **Whole tree:** sequential focus order, position in set, required context and owned elements, diagnostics.

Cost: linear in the document apart from name computation, which is bounded by `AccessibilityOptions.MaxNameWork` (node
visits across all names, default 32 million) and `MaxNameLength` (65,536). `aria-owns` ancestor checks are bounded by
`MaxOwnsWork` (16 million steps) and ID lists by `MaxIdReferences` (4,096 per attribute). When a limit is reached the
result is partial for the nodes affected, `AccessibilityTree.LimitReached` is true and a `LimitExceeded` diagnostic is
recorded. The cancellation token is observed every few hundred nodes.

A style context other than the one `styles` was created with is applied with `DocumentStyles.WithContext`, so the tree for
"this menu item focused" is `Build(doc, styles, StyleContext.Default.WithFocus(item))`.

## Hidden status

`AccessibilityTree.GetHidden(node)` gives a `HiddenInfo` for any element or text node, whether or not it has a tree node:
the set of `HiddenReasons`, the element that causes the strongest one (`Origin`) and whether that is an ancestor
(`IsInherited`). The reasons:

| Reason | Source | Effect |
|---|---|---|
| `DisplayNone` | computed `display: none` on the element | no tree node, for it and its subtree |
| `HiddenAttribute` | the `hidden` attribute through the user-agent sheet (HTML 15.3.1); `until-found` is `content-visibility` instead | as above |
| `Template` | a `template` element | as above |
| `ClosedDetails` | any child of a `details` without `open` except its first `summary` | as above |
| `ContentVisibilityHidden` | the parent (or an ancestor) has `content-visibility: hidden` | the element has a node; its contents have none |
| `VisibilityHidden` | computed `visibility: hidden` or `collapse` on the element; a descendant with `visibility: visible` shows again; a text node follows its parent | node present and ignored |
| `AriaHidden` | `aria-hidden="true"` on the element or an ancestor; a descendant cannot undo it | node present and ignored |
| `Inert` | the `inert` attribute on the element or an ancestor | node present, ignored, not focusable |

`Primary` orders them: not-rendered kinds, then `AriaHidden`, `Inert`, `VisibilityHidden`. Hiding that depends on layout
(zero size, clipping, off-screen) is not known without layout: `HiddenInfo.LayoutDependent` is `LayoutHiding.Unknown`.
An `area` inside a `map` is not hidden although the rendering rules give it `display: none`.

## The tree

`Nodes` lists every node in tree order (`AxNode.Id` is the index; `Root` is the document). Each node is one of
`Document`, `Element` (rendered) or `Text`.

- **`aria-owns`** (WAI-ARIA 1.2 6.6.6, Core-AAM 5.2): owners are processed in document order; the first owner of an element
  wins; an element never owns itself, the root element, or an ancestor of the owner in the tree as built so far (a cycle);
  an owner that is not rendered, `aria-hidden` or `inert` does not resolve `aria-owns`; a target that is not rendered or has
  `visibility: hidden` is skipped. An owned element leaves its DOM parent and is appended, in the order of the attribute,
  after the owner's own children. Every skipped reference is an `AriaOwnsIgnored` diagnostic with the reason.
- **Text** nodes are `StaticText` with their text, white space collapsed. White-space-only text is kept (and ignored) only in
  an inline parent. At the start and end of a block, leading and trailing white space is trimmed.
- **Role** is the Core-AAM computed role name of the role layer (`image`, `none`, `generic`, …), with `Role` empty for an
  element with no role, `RoleOrigin` the derivation and `AriaRole` the record. `RootWebArea` is the document.
- **Ignored** nodes stay in the tree with `IsIgnored` and `IgnoredReasons`: `notVisible`, `ariaHiddenElement`,
  `ariaHiddenSubtree`, `inertElement`, `inertSubtree`, `presentationalRole` (role `none`/`presentation`, including
  imposed ones), `uninteresting` (a generic element with no name, description, focus, role, `tabindex`,
  `contenteditable`, `title` or `aria-*` attribute; an element with no role; white-space-only text). The children of an
  ignored node are still its children; a consumer that wants the pruned tree skips ignored nodes and lifts their children.
- **Locator** is generated on demand (`AxNode.Locator`), **source range** is `AxNode.SourceBytes`.

## Accessible name and description

`AxNode.Name` / `Description` are `AxText`: the text (white space collapsed and trimmed), the step that produced it
(`AxTextSource`), the attribute involved and the elements read. `AccessibilityTree.ComputeName(element)` gives the same
for any element, including one with no node.

The name follows AccName 1.2 section 4.3.2 as a loop over frames. For a node, in order:

1. **2A hidden:** see "Hidden nodes in a name" in the profile. The root and directly referenced nodes are exempt.
2. **2B `aria-labelledby`** (skipped inside another labelledby): referenced elements in order, de-duplicated, joined with
   one space; each is read with 2C–2I as a root. When nothing results, continue.
3. **2C `aria-label`**, unless the node is an embedded control (2E).
4. **2E embedded control** inside a label, a referenced node or contents: textbox value, selected options, range value.
5. **2D native sources** (HTML-AAM section 4, SVG-AAM): label elements, `alt`, `value`, legend, caption, figcaption, `label`
   attribute, SVG `title` child, per element as in the profile entry `native-name-sources`.
6. **2F contents,** when the role allows name from contents, or the node is read as part of another's name: `::before`
   content, the children (DOM children not adopted elsewhere, then owned elements), `::after` content. Text nodes
   contribute their text with `text-transform` applied. A child that is not inline is set off by a space on each side.
7. **2I `title`**, then `placeholder` and `aria-placeholder` for text fields, then the default `Submit`/`Reset`.

A node is read at most once per mode at a time (a cycle contributes nothing), a node used as a labelledby target is not read
again as content, and the element being named is not part of its own label. A role that prohibits naming still gets a name
from `aria-labelledby`, `aria-label` and `title`.

Generated content (profile `generated-content-names`): `content` strings, `attr()` (with fallback), `counter()` and
`counters()` (CSS Lists 3 scoping; the pseudo-element's own `counter-*` properties count; a list item increments
`list-item`), `open-quote`/`close-quote`, and the alternative text after `/`, which replaces the content.

**Description:** `aria-describedby` (as labelledby, with the node as the root), else `aria-description`, else `title` when
the name did not come from `title`.

## States, properties, relations

`AxNode.States` (`AxStates`) holds, when present: expanded, checked, selected, pressed (tristate where ARIA has one),
disabled (own, fieldset, `aria-disabled` of an ancestor), required, invalid, readonly, level, position in set and set size,
value, range (now, min, max, text), live, atomic, relevant, busy, modal, current, haspopup, orientation, multiselectable,
multiline, autocomplete, keyshortcuts, roledescription, url, editable. ARIA attributes count only for roles that support
them (global attributes always), prohibited ones never; the native mapping of HTML-AAM section 3.6 (`HtmlImplicitProperties`)
comes first. Default values are in profile entry `default-states`; heading level defaults to 2.

**Position in set** is the given `aria-posinset`/`aria-setsize` or computed per profile entry `set-position`.

**Relations** (`AxNode.Relations`): `Owns` (adopted elements only), `Controls` (also the `list` attribute of an input),
`LabelledBy`, `DescribedBy`, `Details`, `ErrorMessage`, `FlowTo`, `ActiveDescendant`. Each lists the elements the valid IDs
name, the tree nodes among them, and the IDs that name nothing.

## Focus

`AxNode.Focus`: `IsFocusable` (focusable by markup per the role layer, rendered, not `visibility: hidden`, not inert, not
disabled), the parsed `TabIndex`, and `SequentialIndex` (position in `AccessibilityTree.FocusOrder`, -1 when not tabbable).
`FocusOrder` is positive `tabindex` ascending (document order breaks ties), then the rest in document order.

## Diagnostics

`AccessibilityTree.Diagnostics` (and `AxNode.Diagnostics`) in tree order:

| Kind | When |
|---|---|
| `Role` | every diagnostic of the role layer (invalid role tokens, prohibited, unsupported or deprecated attributes, invalid values, broken ID references, missing required attributes) |
| `BrokenIdReference` | `aria-controls`, `aria-details`, `aria-errormessage`, `aria-flowto`, `aria-activedescendant` or `list` names an element that is not in the tree (the role layer reports IDs that name nothing) |
| `MissingRequiredContext` | the role needs an ancestor role (WAI-ARIA 1.2 5.2.3) that the final tree lacks, `aria-owns` included |
| `MissingRequiredOwnedElements` | the role needs owned descendants (5.2.4) that are not there; `combobox` and `aria-busy="true"` are exempt |
| `FocusableUnderAriaHidden` | an element that would take focus is hidden by `aria-hidden` |
| `NamelessInteractive` | an included button, link, checkbox, radio, switch, textbox, searchbox, combobox, listbox, slider, spinbutton, menu item, tab, treeitem or option with no name |
| `EmptyNameRequired` | an included node whose role requires a name has none |
| `AriaOwnsIgnored` | an `aria-owns` reference was not honoured (reason in the message) |
| `LimitExceeded` | a limit was reached |

## Chrome-shaped export

`AccessibilityTree.ToChromeJson()` writes `{"schema":"onehtml-ax/1","nodes":[…]}`. `nodes` is the array
`Accessibility.getFullAXTree` returns (Chrome DevTools Protocol `AXNode`), one entry per tree node in tree order:

| Field | Value |
|---|---|
| `nodeId` | the tree node's `Id` as a string |
| `ignored`, `ignoredReasons` | `ignored`; reasons as `[{"name":…,"value":{"type":"boolean","value":true}}]` when ignored |
| `role` | `{"type":"role","value":<computed role>}`; ignored nodes are `none`; `{"type":"internalRole",…}` for `RootWebArea`, `StaticText`, `LineBreak`, `LabelText`, `Legend`, `Iframe` |
| `name` | not ignored only: `{"type":"computedString","value":…,"sources":[{"type":"attribute"|"relatedElement"|"contents"|"placeholder","value":{…},"attribute"?,"attributeValue"?,"nativeSource"?}]}`; one source, the one that produced the name |
| `description` | present when not empty |
| `value` | `{"type":"string","value":…}` for text fields, selects and ranges |
| `properties` | `[{"name":…,"value":{"type":…,"value":…}}]` for a not ignored node, in this order: busy, disabled, editable, focusable, invalid, keyshortcuts, roledescription, live, atomic, relevant, autocomplete, hasPopup, level, multiselectable, orientation, multiline, readonly, required, valuemin, valuemax, valuetext, checked, expanded, modal, pressed, selected, posinset, setsize, then the relations (activedescendant, controls, describedby, details, errormessage, flowto, labelledby, owns), url. Value types: `boolean`, `booleanOrUndefined`, `tristate` (`"true"`, `"false"`, `"mixed"`), `token`, `tokenList`, `integer`, `number`, `string`, `idref`, `idrefList` (with `relatedNodes`). An ignored node lists only `hidden` when hidden |
| `parentId`, `childIds` | tree ids; an ignored node keeps its children |
| `backendDOMNodeId` | the arena index of the node in the OneHtml document (not Chrome's id); absent for the document |

Not exported: `InlineTextBox` nodes, `frameId`, `chromeRole`, `focused`, the `hiddenRoot` and `root` properties, name sources
that lost. The export is deterministic. Every spelling here is a profile entry marked unverified until the Chrome
differential compares it with captures; a change that alters meaning raises the schema version.

## Conformance

`tests/OneHtml.Accessibility.Tests/WptAccnameTests.cs` runs the pages in `testdata/wpt/accname/` statically
(web-platform-tests commit `68d964a5f292bc4b2c2eba2f21e2b916190b6ad2`): the expectations in the markup
(`data-expectedlabel`, `data-expecteddescription`, `data-expectedproperties`), an expected subtree in a script call, the JSON
of the manual pages, and, for the html-aam name pages, the markup their script builds (reproduced step by step by the test).
Known failures, each with its reason, are in `testdata/wpt/accname/expectations.tsv`.

| Directory | Pages | Pass | Fail (known) | N/A |
|---|---|---|---|---|
| `accname/` | 2 | 11 | 0 | 0 |
| `accname/name/` | 16 | 463 | 13 | 3 |
| `accname/name/shadowdom/` | 2 | 0 | 0 | 6 |
| `accname/manual/` | 159 | 152 | 7 | 0 |
| `html-aam/accname-computation-by-element/` | 17 | 516 | 8 | 0 |
| `html-aam/` (`names`, `hidden-names`, `figure-name…`) | 3 | 139 | 3 | 0 |
| `html-aam/heading/` | 1 | 14 | 0 | 0 |
| `wai-aria/` `checked`, `pressed`, `subtree` | 3 | 9 | 0 | 0 |
| `wai-aria/heading/` | 2 | 8 | 0 | 6 |
| total | 205 | 1,312 | 31 | 15 |

Not applicable: shadow DOM is built by script (`shadowdom/`, 6), `comp_name_from_content_alt_counter_invalidation.html`
changes style by script (3), and `wai-aria/heading/invalid-nesting.tentative.html` marks its expectations ambiguous (6).
Known failures (31): the tentative `::marker` text in names (8), the tentative naming of dialog and article from a heading
(5), the tentative cell and row pages that name them only from author attributes (6), the tentative page that does not
name a figure from its figcaption (3), a menu inside a label on five manual pages (5), two legacy manual pages that expect
the label's title between its generated content (2), and the image-map area page, whose setup leaves an `alt` in place for
its title and empty cases (2). The reasons are in the expectations file.
