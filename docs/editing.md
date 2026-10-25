# Editing HTML

Normative behaviour of `Document.Apply` and `DocumentEditor` in `RecursiveAI.OneHtml`. A port must reproduce it from this text. Stylesheet edits, the saved-edit JSON and replay are separate.

## Contract

- `doc.Apply(edits)` returns an `EditResult`: either a new `Document` and a `ChangeSet`, or a failure with a reason per edit. **All edits apply or none do.** The old snapshot and every handle into it are untouched.
- Edit targets are `Node` handles into the snapshot being edited. Edits in one transaction all refer to that snapshot, and are applied in order; an edit may target a node created by an earlier edit of the same transaction.
- An empty transaction returns bytes identical to the input.
- Each `EditOutcome` is `Applied`, `Invalid` (the operation does not fit the target or its input is unusable: `unwrap` on a text node, an edit to a synthesized `selectedcontent` clone, NUL in text), `Failed` (the result cannot be expressed in HTML: no enclosing region re-parses to the expected tree), or `NotAttempted` (an earlier edit stopped the transaction). A no-op such as removing an absent attribute is `Applied`.
- Limits, checked before the work they guard: `MaxEdits` (100,000), `MaxInsertedLength` (16 MiB of inserted markup and text), `MaxVerifications` (1,024 re-parses in one-at-a-time fallback). Exceeding one fails the transaction cleanly.

## Operations

| Operation | Notes |
|---|---|
| `SetAttribute(element, name, value)` | Replaces the value, or appends the attribute. Name matching is ASCII case-insensitive for HTML elements, exact for foreign-element adjusted names. |
| `RemoveAttribute(element, name)` | No-op (applied, no range) when absent. |
| `ReplaceText(target, text)` | On a text node: its data. On an element: all its children become one text node (none for empty text). Escaped for the context: raw text and RCDATA elements get their content verbatim or character-reference escaped as the element's parsing mode requires; foreign content escapes as normal data. |
| `InsertBefore`, `InsertAfter`, `Prepend`, `Append` | `html` is parsed with the fragment algorithm in the target's context (the parent's, for before/after). |
| `Wrap(node, html, count)` | `html` must parse to one element. The `count` contiguous siblings from `node` move inside it, **after the wrapper's own children**. |
| `Unwrap(element)` | Children take its place. |
| `Replace(node, html)`, `Remove(node)` | |
| `SetStyle(element, property, value, important)`, `RemoveStyle(element, property)` | Edit the `style=""` attribute using CSS Syntax declaration ranges. `!important` is kept when the declaration has it and `important` is not given. |

A `template` target means its content fragment: children are inserted into, and read from, the template contents.

## How the output is built

1. The edits run against a working copy of the arena columns, in order. Each edit marks a **region** dirty: an element's attribute list, an element's child list, or a single text node.
2. Each dirty region becomes a **splice** of the original text. Bytes outside splices are copied as they are, so they are identical to the input by construction. A region that would be rewritten gets the smallest range that covers the edit: one attribute value, one attribute, the attribute list, the child range.
3. The spliced text is encoded in the **document's encoding**. Editing a `<meta charset>` does not change the encoding used. A character the encoding cannot represent is written as a hexadecimal character reference (`&#xHHHH;`) in attribute values and data; in raw text, where references are not decoded, such a character makes the edit `Invalid`. `replacement` and other encodings with no encoder are rejected. ISO-2022-JP is edited only where the splice sits in ASCII state; otherwise the edit falls back or fails.
4. The output is parsed again. The result must equal the working tree (`TreeComparer`: kinds, names, namespaces, attributes, text, in order). `selectedcontent` children are not compared, since they are clones of the selected option. **The returned snapshot is that re-parse**, so its source map is exact for the output bytes. A mismatch is never returned as output; it triggers fallback.

## Writing rules

- **Escaping:** `&` and `<`, `>` (and NBSP as `&nbsp;`, always a reference) in data; `&` and the quote character, and NBSP, in attribute values. NUL and lone surrogates are rejected (`Invalid`). CR is written as `&#13;` so it survives newline normalisation. Raw text and script content is written unescaped (HTML 13.3); content that would not re-parse to the same text, such as one containing its own end tag, fails the re-parse check (`Failed`). A leading line feed in `pre`, `listing` and `textarea` content is protected with an extra line feed.
- **Local style** (`LocalStyle`), inferred from a bounded sample (the first 5,000 elements) and copied into new markup: attribute quote character (single only when more frequent than double), tag-name case and attribute-name case (each upper or lower), void-element form (`<br>`, `<br/>`, `<br />`), the newline sequence, newline (CRLF or LF) and indentation. Inserted markup is parsed first, then serialized by `MarkupWriter` in this style; attributes keep the author's order and name case.
- **Indentation (line mode):** when the target sits on its own line (only whitespace between the previous line break and its start tag, and a line break after it), inserted siblings are placed on their own lines with the target's indentation, and prepend/append use the child indentation inferred from the existing children. This adds whitespace text nodes to the tree; they are listed in `ChangeSet.Added`. For `prepend`, the new element goes after the element's leading whitespace text. Otherwise (inline context) nothing is added.
- Void elements and elements with no end tag in the source are not given end tags unless the markup requires them.
- `style=""` edits splice only the declaration when the attribute is quoted and its value is verbatim (no references); otherwise the whole value is replaced.

## Fallback

A splice cannot be used when the edited node, or something it affects, is not where the source says: parser-inserted (`implied` tbody/head/body), displaced (foster parenting, adoption agency), cloned, or when the new bytes would change how surrounding markup parses (a `<p>` closed by an inserted block, an inserted `<tr>` in a table).

The engine first tries the whole transaction with fine splices. If the re-parse differs, the edits are re-applied one at a time and each failing region is **widened to its 1st, 2nd, … enclosing element that has bytes of its own**: the region's range becomes that element's children range, re-serialized from the working tree. The first level whose output re-parses to the expected tree wins. If none does before the document root, the edit is `Failed` (the document root is never rewritten). Every widened range is reported: `EditResult.Fallbacks` has the edit index, range, level and reason, and `EditedRange.FallbackLevel` is greater than zero. Fallback output is canonical serialization of the subtree, so bytes inside a fallback range are not preserved; bytes outside it are.

Synthesized `selectedcontent` clones are never editable. Edit the selected `option` instead.

## Change set

- `Added`: nodes in the new snapshot that did not exist (including whitespace nodes added by line mode). `Removed`: nodes of the old snapshot that are gone. `Changed`: `NodeChange(Node, Old, Changes)` where `Changes` flags `Attributes`, `Data` (text characters), `Children` (child list changed) and `Parent` (moved to another parent); `Node` refers to the new snapshot and `Old` to the old one.
- `ChangeSet.MapToNew(oldNode)` maps an old handle to its counterpart in the new snapshot (the null node if removed; `ArgumentException` for a node of another snapshot).
- `Ranges`: one `EditedRange` per rewritten region, with the old and new ranges in text units and bytes, the new bytes, the indexes of the edits that produced it, and its fallback level. Ranges are sorted and do not overlap. This is the audit diff: replace `OldByteRange` of the input with `NewBytes`, and the output follows.
- Because the snapshot is a re-parse, node identity across snapshots is established by the `MapToNew` correspondence computed from the working tree, not by index.

## Cost

One edit costs about one parse of the document plus about 1 ms (the verification re-parse dominates; the column copy does not). On a 35,011-node page: parse 4.6 ms, one attribute edit 5.8 ms (12.4 MB allocated), 100 attribute edits in one transaction 7.2 ms. Reads on an old snapshot, on a fresh parse and on an edited snapshot cost the same.
