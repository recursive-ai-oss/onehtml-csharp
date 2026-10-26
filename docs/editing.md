# Editing HTML

Normative behaviour of `Document.Apply` and `DocumentEditor` in `RecursiveAI.OneHtml`. A port must reproduce it from this text. The saved-edit JSON and replay are in `edit-format.md`; stylesheet edits are at the end of this file.

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
| `Unwrap(element)` | Children take its place. A text, comment or doctype target is `Invalid`, as is an element whose parent is the document, and a `template` element. |
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

## Cost of a stylesheet edit

One edit on a 1,000-rule, 73 KB sheet (parse 0.52 ms, 1.6 MB): `setDeclaration` 2.85 ms / 7.5 MB before the narrow check (5.4 parses of time, 4.7 of allocation), 0.88 ms / 2.7 MB after (1.7 and 1.7). What remains is the parse that produces the new sheet snapshot and the change set's pass over the sheet's rules. An edit set spread over the whole sheet (50 declarations across 1,000 rules) has a window as wide as the sheet and costs what it did.

## Restyle after an edit

To get the styles of the edited document, call `styles.Restyle(result)` with the `EditResult` or `BatchResult`; see `cascade.md`.

## Statuses

`EditStatus` also has `Stale` (a saved target is gone or no longer looks like it did) and `Ambiguous` (a saved target matches several places). Only replay produces them; see `edit-format.md`.

## Stylesheet edits

`SheetEdit` operations: `SetDeclaration`, `RemoveDeclaration`, `InsertRule` (before or after a rule, or at the end of a stylesheet or of a rule's block), `RemoveRule`, `ReplaceRule`. Targets are rules of a specific `Stylesheet` snapshot. `Stylesheet.Apply(edits)` edits a stylesheet as text; `EditBatch` applies document edits and stylesheet edits together, all or nothing, to `<style>` elements (`InlineStylesheets.Of(doc)`, named `inline:<n>`), to supplied `LinkedStylesheet`s (named by URL, written back in the sheet's own encoding) and to loose snapshots.

- **Splice only what changed.** Every edit becomes a local replacement of source text. For a `<style>` element only the stretches that change are spliced into the document, so the document's change set shows those text stretches. For a linked sheet the new bytes are the old bytes with the same stretches replaced. All edits of a transaction refer to the original snapshot and are placed by original position; list order decides only the order among edits landing at the same offset: appended declarations, then insertions after a rule, insertions before a rule, then insertions at the end of a block.
- **Conflicts.** Two edits that change the same bytes, or an insertion strictly inside a replaced stretch, make the transaction `Invalid`. Pure deletions that touch or overlap merge. A later edit that contradicts an earlier one on the same property is resolved by the expected rule model and written through the fallback below.
- **Local format.** New text copies the sheet: the newline sequence (documents and sheets are decoded with newlines normalized to line feed, and the original convention is restored on writing), the indentation unit and the indentation of the line the neighbour starts on, the text before and after a declaration's colon and before `!important`, whether the last declaration of a block ends in a semicolon, and the white space between adjacent rules (taken from the pair around the reference rule, then any pair in the container, then any pair at the top level). With no evidence the defaults are: one line feed between rules, two spaces of indentation, `: ` after a property, a semicolon after the last declaration. Removing an item removes the white space that separated it from its neighbour (after it, or before it for the last item of a block); comments between rules stay.
- **Inserted CSS** must parse as exactly one rule (CSS Syntax 3 §5.3 "consume a rule"); leading and trailing white space is dropped, a block-less at-rule gets its semicolon, and later lines are re-indented. An insertion at the end of a stylesheet whose last item is an incomplete prelude cannot be written and is `Failed`.
- **Verification.** The edited text is parsed again and its rule events (rule kind, name, normalized prelude, declarations with normalized values and importance, in order) must equal the events derived from the original tree and the edits. The expected events never come from the new text.
  - **Narrow check first.** The check covers a stretch of whole top-level rules: the rules the edits touch, plus one on each side, to the end of the sheet when that is nearer. Only that stretch of the new text is parsed, and only its expected events are built. This is sound because a rule list is parsed rule by rule from any rule boundary: the text before the stretch is unchanged and its rules ended at their own delimiters in the original parse; the stretch ends at the end of the sheet or after an untouched rule that ended at its delimiter. When the stretch alone gives the expected rules, the whole sheet does.
  - **Whole-sheet check as the fallback.** A narrow mismatch proves nothing, so the whole new text is parsed and compared to the whole expected list; that result decides. The same happens when the new text is longer than the token or input limit, or when the pieces do not lie inside the stretch. `EditOptions.VerifyWholeStylesheet` skips the narrow check; the two give the same output.
- **Fallback.** If they differ, the rule containing the edit is rewritten from the expected events; if that still differs, the next enclosing rule is rewritten, up to the stylesheet's top level. Each rewrite is reported in the `StylesheetChangeSet.Ranges` with its level (0 is a local splice).
- **Limits.** The `EditOptions` limits apply to the whole batch: the count of edits, the inserted length, and the number of verification parses.
- **Change set.** `StylesheetChangeSet` lists rules `Added`, `Removed` and `Changed` (new-snapshot nodes for added and changed, old-snapshot nodes for removed) and the rewritten ranges, each with old and new range, the edits that produced it and its fallback level. A cascade consumer restyles the elements matched by the rules in the change set. An edit of an embedded sheet is also in the document's change set as a text change.
- **Linked sheets.** Unencodable characters in the target encoding make the edit `Failed`; the byte order mark is kept.
