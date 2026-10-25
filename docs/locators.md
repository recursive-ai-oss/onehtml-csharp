# Locators and fingerprints

Normative behaviour of `RecursiveAI.OneHtml.Locators`, written so another language can reproduce it byte for byte.

## Hash-like names

Build tools mint class names and ids (`Button_primary__3xK9z`, `css-1x2y3z`, `sc-bdfBwQ`) that change with every build.
Locators never use them and fingerprints drop them. There is one definition, `GeneratedNames.IsHashLike(name)`:

1. Cut the name into segments at every character that is not an ASCII letter or digit.
2. The name is hash-like when some segment of **five or more** characters is one of:
   - **all digits** (`12345`);
   - **letters and digits** with at least two changes between a digit and a non-digit (`3xK9z`, `550e8400`;
     `year2024` has one change and is not hash-like);
   - **letters only, with both an upper-case and a lower-case letter**, that is not a run of camel-case words. A
     camel-case run is: an optional capital, one or more lower-case letters, then any number of words that are one
     capital followed by one or more lower-case letters.

Vectors: hash-like — `12345`, `3xK9z`, `550e8400`, `bdfBwQ`, `css-1x2y3z`, `Button_primary__3xK9z`.
Not hash-like — `year2024`, `navLink`, `Button`, `iPhone`, `main`, `nav-link`, `x1`, `a1b2` (shorter than five), `HEADER`
(one case only).

Letters are ASCII only; every other character is a separator.

## Locator generation

A locator is a selector (Selectors 4 syntax, serialized per CSSOM) that is unique in the document it was generated from.
Only elements in the document tree get one (not the contents of a `<template>`, not nodes of another document).

Per element, the **candidates**, best first:

1. `#id` when the id is non-empty and not hash-like (ids are compared case-folded in quirks mode).
2. A test hook: `[data-testid="v"]`, then `data-test`, `data-test-id`, `data-cy`, `data-qa`, `data-automation-id`,
   `data-id`, each with a non-empty value, in that order of attribute name.
3. `tag[name="v"]` when `name` is non-empty.
4. `[role="r"][aria-label="l"]` when both are non-empty, then `[aria-label="l"]`, then `[role="r"]`.
5. The bare tag name.

A candidate is **unique** when exactly one element of the document offers the same candidate (counted across every
element's full candidate list, not only its best one). Classes are never used.

Algorithm: walk from the element up through its element ancestors; at each element (starting with the element itself)
take the first unique candidate as the **anchor** and stop. Each element passed over contributes a step. A step is
`tag:nth-of-type(k)` (k is the 1-based position among siblings with the same type), or just `tag` when the element is the
only one of its type among its siblings. When the element or its parent is not in the HTML namespace the step is
`tag:nth-child(k)` instead. The locator is the anchor followed by the steps from the outermost down, each joined by
` > `. The tag in a step is the local name serialized as a CSS identifier.

Limits: at most 120 steps; an element further than that below any anchor has no locator (`TryGenerate` returns false).
If no ancestor offers an anchor, there is no locator (the document element is normally anchored by its tag).

Verification: before returning, the text is parsed and the element must match it. Anchors are unique and each step
selects one child, so the match is unique by construction; `Verify` additionally queries the whole document and demands
exactly the target (linear time, meant for tests).

Values are serialized as CSS strings and identifiers per CSSOM, so ids with special characters round-trip.

## Fingerprint

`ElementFingerprint.Create(element)` gives four fields.

- **tag**: the element's local name.
- **attrs**: for each of `class`, `href`, `id`, `name`, `role`, `src`, `type` (in that, i.e. sorted, order) that is present
  without a namespace:
  - `class`: split on ASCII whitespace; drop hash-like tokens; join the rest with a single space; omit the key if none
    remain;
  - `id`: omitted when hash-like;
  - others: the value as written.
- **text**: `"sha256:"` + lower-case hex SHA-256 of the UTF-8 bytes of the normalized text content. Text content is the
  concatenation of the element's descendant text nodes in tree order (comments, processing instructions excluded).
  Normalization: every run of ASCII whitespace (U+0020, U+0009, U+000A, U+000C, U+000D) becomes one space; leading and
  trailing whitespace is removed; nothing else changes (no-break spaces stay).
- **path**: `"sha256:"` + lower-case hex SHA-256 of the UTF-8 bytes of the path text: for each element from the document
  element down to this one, `localName[k]` with k the 1-based position among its **element** siblings, joined by `/`
  (for example `html[1]/body[2]/div[3]`).

SHA-256 is implemented in the library (no operating-system dependency).

Comparison: tag and attrs must be equal exactly; text hashes equal; the path hash is advisory and reported separately.
