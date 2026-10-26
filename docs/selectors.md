# Selectors

Normative behaviour of `RecursiveAI.OneHtml.Selectors` and the `Node` query members. The grammar and matching rules are
those of Selectors Level 4 (with the HTML Standard section 4.16 for the HTML pseudo-classes and DOM for the query
methods); this file records what those texts leave open or what this library adds.

## Parsing

- Input is a string or a CSS Syntax component-value list (so `@supports selector()` can reuse it). Parsing never
  throws from `TryParse`/`IsValid`; `Parse` throws `SelectorSyntaxException`.
- `:is()` and `:where()` take **forgiving** lists: an invalid item is dropped and never matches. The dropped text is kept
  (serialized) so the list serializes stably. `IsValidComplexSelector` (the `selector()` test) is false when anything was
  dropped. `:not()`, `:has()` and top-level lists are not forgiving.
- A `[` or `(` still open at the end of input is closed there, as CSS Syntax says.
- Serialization follows CSSOM. Identifiers also escape non-ASCII code points that are not name code points.
- Namespaces: `SelectorParseOptions.Namespaces` maps prefixes to namespace URIs; an undeclared prefix is a syntax error;
  without a map a named prefix is an error; `*|x` and `|x` are accepted. A map entry for the empty prefix is the default namespace for unprefixed type selectors.
- Pseudo-elements are parsed and reported but never match an element. After `::part()` only state and user-action
  pseudo-classes are accepted; nothing after `::slotted()`; a second pseudo-element only after `::part()`/`::slotted()`.
  `:heading(<integer>#)` is parsed.
- The column combinator `||` parses and never matches.
- Limits, checked while parsing and before allocating: 65,536 characters; nesting of `:is/:where/:not/:has/:nth-… of`
  16; 128 compounds per complex selector; 4,096 complex selectors per list; 16,384 simple selectors. Exceeding one
  throws `SelectorLimitExceededException` (`TryParse` returns false). Parsing uses an explicit stack, not recursion.

## Specificity

Per Selectors 4 §17: `:is/:not/:has` take the most specific argument, `:nth-child(… of S)` adds the most specific `S`
to a pseudo-class, `:where` is zero, pseudo-elements count in C.

## Matching

- Right to left against a compiled list. Each list reports its rightmost-compound keys (id, class, tag) and its
  dependencies (`SelectorDependencies`: ancestor, sibling, `:has`, state) for the cascade to bucket and invalidate.
- No work is quadratic in the document: a failure memo starts after 64 backtracking steps, `:has()` marks are computed
  in bulk, `of S` positions are cached per snapshot. Deep trees (thousands of levels) use no recursion.
- Names: HTML elements in HTML documents match type selectors and attribute names ASCII-case-insensitively; the HTML
  list of case-insensitive attribute values (`type`, `lang`, …) is honoured. Class and id compare
  case-insensitively in quirks mode.
- `:scope` is the `SelectorMatchOptions.Scope` node, else the document element for queries on the document and the
  element itself for queries on an element.
- `:defined` matches every element that is not a custom element (no script runs, so custom elements are never defined).
- `:link`/`:any-link`: `a` and `area` with an `href`. `:visited`, `:local-link`, `:autofill`, `:popover-open`, `:modal`
  never match.
- `:lang()` uses the nearest `lang` (or `xml:lang` in foreign content) and extended filtering; `:lang("")` matches an
  element whose language is the empty string. `:dir()` follows `dir`, with `auto` read from the first strong character
  and otherwise inheriting; it is an approximation of the full bidi algorithm.
- Dynamic pseudo-classes (`:hover`, `:focus`, `:focus-visible`, `:focus-within`, `:active`, `:target`,
  `:target-within`) read `SelectorMatchOptions.State` (`InteractionState`); with none, they match nothing. `:target`
  works only for the connected document.

### Form and state pseudo-classes from attributes

`:checked`, `:disabled`/`:enabled` (including the fieldset and `legend` rule), `:required`/`:optional`,
`:read-only`/`:read-write` (HTML-namespace elements only), `:placeholder-shown`, `:default`, `:indeterminate`, `:open`,
`:in-range`/`:out-of-range` follow the HTML Standard from attributes alone.

`:valid`/`:invalid` are decided where static: `required`, `email`, `url` (absolute URL parse), `pattern` (the BCL
ECMAScript-mode regex with a 50 ms timeout; a pattern that does not compile, is over 4,096 characters or times out is ignored, as an invalid pattern is), number/range/date-time `min`/`max`, and
number `step`. There is no dirty flag and no custom validity, so script-set state is invisible.

## Query members

`QuerySelector`, `QuerySelectorAll`, `Matches`, `Closest`, `GetElementsByTagName`, `GetElementsByClassName` are on `Node`.
Navigation and matching against a compiled list allocate nothing; `QuerySelectorAll` into a caller list allocates only
that list. `Children` returns element children (W3C); `ChildNodes` returns all children. Indexes (ids, sibling
positions, form owners) are built on first use per snapshot and are safe to share between threads.

## Conformance (web-platform-tests, pinned commit in the notices file)

| Directory | Pass | Known failure | N/A |
|---|---|---|---|
| `dom/nodes` `selectors.js` (document, element, detached element, fragment contexts; query, queryAll, matches, invalid) | 2140 | 0 | 20 |
| `dom/nodes/Element-closest.html` | 29 | 0 | 0 |
| `css/selectors/parsing` (24 files) | 346 | 43 | 1 |
| `html/semantics/selectors/pseudo-classes` (39 files) | 22 | 0 | 75 |

N/A reasons: 20 expectations are built by script (`createElementNS`, `setAttributeNS`); 75 pseudo-class entries need
a script mutation of element state, user interaction, or reference rendering; 1 parsing case needs a browser feature.
Known failures: 43 assertions in three `*.tentative.html` files for proposals that are not in Selectors 4
(class-prefix `.foo-*` selectors and `:has-slotted`). The specificity files in `css/selectors` (`is/not/has`) compare
cascade winners; they run with the cascade (see `cascade.md`).
