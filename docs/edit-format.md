# Edit format, version 1

The saved form of edits: a JSON envelope that names what to change durably enough to replay on another copy of the page or stylesheet, plus the replay rules. It is language-neutral; a port must read and write the same JSON and give the same replay outcomes from this text. The in-memory operations and how HTML and CSS bytes are written are in `editing.md`. Locators and fingerprints are in `locators.md`.

## Model

- An **edit** is one operation on one target. A **transaction** is an ordered list of edits applied together: all apply or none does, and the caller gets a reason per edit.
- In memory, targets are handles into a specific snapshot. A saved edit instead carries a **durable target** (below) that replay resolves against whatever document it is given.
- Markup an edit inserts is an HTML fragment parsed in the target's context, and inserted CSS is parsed as one rule; neither is built by string concatenation.
- Each edit carries an opaque `meta` value. The library stores and returns it and never reads it.
- Replay never guesses. An edit whose target cannot be identified with certainty is reported, and then nothing is applied.

## Envelope

```json
{
  "format": "onehtml-edits",
  "version": 1,
  "document": { "url": "https://example.test/", "sha256": "…" },
  "edits": [
    {
      "op": "setAttribute",
      "target": { "locator": "…", "fingerprint": { }, "anchor": { } },
      "name": "title",
      "value": "Campus map",
      "meta": { "criterion": "4.1.2" }
    }
  ]
}
```

- `format` must be `"onehtml-edits"` and `version` the number `1`; anything else is rejected. A new version is introduced only for changes that alter meaning.
- `document` is informative (what the edits were made against); `sha256` is the hex SHA-256 of the document's bytes when it had any. Replay does not use it.
- **Reading:** unknown members are ignored at every level, duplicate members keep the last value, a UTF-8 byte order mark is skipped, and text after the top-level value is an error. Members of `edits` that are not objects are errors. An edit with no `op` is accepted by the reader and reported `invalid` at replay.
- **Writing:** members come in the fixed order shown in this document, compact by default, strings escaped only as RFC 8259 requires plus the C0 controls (non-ASCII text, `<`, `>`, `&` and `'` are written as they are, as UTF-8); numbers are integers except inside `meta`. The same envelope always writes the same bytes.
- **`meta`** is any JSON value. It is stored as compact JSON text (re-written token by token, so whitespace is not preserved) and written back at the same place. `null` means absent.

## Operations

| `op` | Target | Other members |
|---|---|---|
| `setAttribute` | element | `name`, `value` |
| `removeAttribute` | element | `name` |
| `replaceText` | text node, or element (all its children become one text node) | `text` |
| `insertBefore`, `insertAfter` | node | `html` |
| `prepend`, `append` | element | `html` |
| `wrap` | node, or `count` contiguous siblings starting at it (default 1) | `html` (one element), `count` |
| `unwrap` | element | |
| `replace` | node | `html` |
| `remove` | node | |
| `setStyle`, `removeStyle` | element (`style=""`) | `property`, `value`, `important` |
| `setDeclaration` | rule | `property`, `value`, `important` |
| `removeDeclaration` | rule | `property` |
| `insertRule` | see below | `css` |
| `removeRule` | rule | |
| `replaceRule` | rule | `css` |

`setDeclaration` replaces the value of the rule's last declaration of that property, or appends one; `removeDeclaration` removes every declaration of the property and is applied (changing nothing) when there is none. The placement of `replaceText` on an element, of `wrap`, and of insertions inside `template` elements is as in `editing.md`.

**`insertRule`** places a rule with exactly one of these members, whose value is a rule target:
- `"before": <rule target>` and `"after": <rule target>`: next to that rule.
- `"end": <rule target>`: as the last item of that rule's block.
- `"end": true`: at the end of the stylesheet named by the member `sheet`.

Stylesheet edits name their sheet in the target's `sheet` member (and in `sheet` for `"end": true`): `inline:<n>` for the n-th `<style>` element, counting HTML-namespace `style` elements from 0 in tree order and leaving out `template` contents, or the URL the caller supplied for a linked sheet.

## Targets

An element target:

```json
{
  "locator": "nav#main-nav > ul > li:nth-child(3) > a",
  "fingerprint": {
    "tag": "a",
    "attrs": { "class": "nav-link", "href": "/calendar" },
    "text": "sha256:9f2c…",
    "path": "sha256:41ab…"
  },
  "anchor": { "start": 18234, "end": 18301, "bytes": "sha256:c07e…" }
}
```

- **`locator`:** a selector unique in the document it was generated from. It prefers `id`, `data-*`, `name`, landmark and ARIA attributes and falls back to the shortest unique structural path. A locator is *standalone* when it is a single compound selector (no combinator): it names the element by itself rather than by position.
- **`fingerprint`:** `tag`; `attrs`, the key attributes present (`class` without hash-like names, `href`, `id` unless hash-like, `name`, `role`, `src`, `type`), sorted by name; `text`, `sha256:` and the hex SHA-256 of the whitespace-normalized text content; `path`, `sha256:` and the hex SHA-256 of the position path (`tag[k]/…` from the root, k the 1-based position among element siblings).
- **`anchor`:** the source range of the element's start tag through its end (`start`, `end`, in bytes of the original input, or in text offsets when the document was parsed from a string) and `bytes`, `sha256:` and the hash of those bytes.

**Text and comment targets** have `"kind": "text"` or `"kind": "comment"`, a `parent` (an element target), an `index` (the position among the parent's child nodes) and `fingerprint.text` (the hash of the node's data after whitespace normalization).

**Rule targets** have `"kind": "rule"` and:
- `sheet`: the sheet name (above).
- `selector`: the rule's prelude with runs of white space collapsed to one space. For an at-rule it is `@name prelude` with the name lower-cased.
- `atRules`: the `selector` values of the enclosing rules, outermost first. Nested style rules count as enclosing rules.
- `index`: the 0-based position of the rule among all rules of the sheet with the same `selector` and the same `atRules`, in document order.
- `fingerprint.declarations`: `sha256:` and the hash of the rule's own declarations (names lower-cased except custom properties, values whitespace-normalized, importance), in order.

## Replay

Every edit's target is resolved against the document as it is now. Resolution:

1. **Anchor.** If the target has an anchor and exactly one node of the right kind sits at exactly that range with the same tag and bytes hashing to `bytes`, that node is the candidate. If the locator also matches exactly one *different* node, the edit is `stale` (the page changed in a way the anchor cannot see). If the locator matches nothing or several, the anchor wins.
2. **Locator.** Otherwise the locator must match exactly one node: none gives `stale`, several give `ambiguous`.
3. **Fingerprint.** `tag` and `attrs` must be equal and `text` equal after normalization, or the edit is `stale`. `path` is advisory when the locator is standalone; for any other locator a path mismatch makes the edit `stale`, because a positional locator selects whatever now sits at that position. A fingerprint is required unless the anchor resolved the target; a target with neither is `invalid`.
4. **Text and comment targets** resolve their `parent` as above, then take the child at `index`, which must be of the right kind and have the fingerprinted text.
5. **Rule targets** find the sheet by name (a missing sheet is `stale`), then the rules with equal `selector` and `atRules`. With an `index`, the rule at that position must exist and its declarations hash must match, else `stale`; without one, one candidate resolves and several are `ambiguous`.

Outcomes per edit: `applied`; `stale` (target gone or no longer the same); `ambiguous` (several candidates); `invalid` (the operation does not fit the target, the operation is unknown, the member it needs is missing); `failed` (the result cannot be written losslessly); `notAttempted` (this edit resolved, but another edit did not, so nothing was applied). A transaction with any outcome other than `applied` applies nothing.

Two byte-identical copies of an element cannot be told apart by anything this format records. If the anchor's bytes hold at the saved position, replay applies there even when an identical copy was inserted nearby; that is the same element as far as the format can know. A target whose anchor no longer holds and whose locator matches the copies is `ambiguous`.

## Limits

The reader checks these before building anything and rejects with an error that names the limit; the caller may pass others. Defaults: input 32 MiB; nesting depth 32 (the reader keeps an explicit counter and never recurses on depth); 100,000 edits; any string 16 MiB; `meta` 64 KiB per edit and 256 times that in total; 64 attributes per fingerprint; 64 `atRules` per target.

## Versioning and determinism

Version 1 is described here. A reader of version 1 ignores members it does not know; a change that alters the meaning of an existing member is a new version. Writing is deterministic: the same edits give the same bytes on every platform.
