# Rendered facts and the DOMSnapshot adapter

`RecursiveAI.OneHtml.Accessibility.Rendered` holds what a browser's layout engine said about a page, keyed by the nodes of a OneHtml document. The library never drives a browser: the caller captures a `DOMSnapshot.captureSnapshot` result (and the page's serialized DOM) and passes the JSON in.

## Pinned format

| | |
|---|---|
| Source | Chrome DevTools Protocol, domain `DOMSnapshot`, method `captureSnapshot` (protocol version `1.3`; documented at chromedevtools.github.io/devtools-protocol/tot/DOMSnapshot/) |
| Facts model version | `RenderedFacts.FormatVersion` = 1; raised only when the meaning of a fact changes |
| Accepted inputs | the protocol result `{"documents", "strings"}`; a protocol response `{"id", "result": {...}}`; the repository's capture file `{"schema", "format", "snapshot"}` |
| Checks on the capture file's `format` | `domain` must be `DOMSnapshot`, `method` `captureSnapshot`, `protocolVersion` major `1`; anything else is refused with `DomSnapshotFormatException`. `product` and `protocolVersion` are kept on the facts |
| Request parameters assumed | `includePaintOrder`, `includeDOMRects`, `includeBlendedBackgroundColors`, `includeTextColorOpacities` (each optional: a fact whose table is absent reads as absent) and a `computedStyles` list |
| Computed-style list | the result does not say which properties were requested; the capture file's `format.parameters.computedStyles` does, and a caller with a bare result passes the list to `DomSnapshot.Parse`. `layout.styles[i]` holds one string-table index per requested property, in that order |

The snapshot's format is not a stable API of Chrome. Re-run the fixture captures on each Chrome upgrade; a change shows up as a refusal or a failed key-fact test.

## Reading (`DomSnapshot.Parse`)

`Utf8JsonReader` over the whole input, no reflection, nested values skipped by the reader's own explicit stack, so nesting never recurses. Unknown members are ignored. Tables are validated once after reading so later stages index without checks:

- `parentIndex`, `nodeType`, `nodeName` have one entry per node (`nodeValue`, `backendNodeId`, `attributes` are optional but, when present, equal in length); a parent index is `-1` (exactly one root, node 0) or the index of an earlier node;
- every string index is in `[-1, strings.Length)`; `attributes` entries have an even length;
- rare data (`pseudoType`, `shadowRootType`, `contentDocumentIndex`, `inputValue`, `textValue`, `inputChecked`, `optionSelected`, `isClickable`, …) refers to existing nodes; frame links to existing documents;
- layout arrays equal the layout item count when present; rectangles are `[x, y, width, height]`, or `[]`/`null` for none (read as absent); text boxes refer to existing layout items; every `stackingContexts` entry is a layout item;
- with a style list, every layout item has none or exactly that many style values.

Limits (`DomSnapshotLimits`), each checked before the array that would exceed it grows. The first column is the property name; a violation throws `DomSnapshotFormatException` whose `LimitName`, `Limit` and `Actual` say which:

| Limit | Default |
|---|---|
| `MaxInputBytes` | 256 MiB |
| `MaxStrings` / `MaxStringChars` | 16 Mi entries / 256 Mi UTF-16 units |
| `MaxDocuments` | 4096 |
| `MaxNodes` / `MaxLayoutNodes` / `MaxTextBoxes` (over all documents) | 8 Mi / 8 Mi / 16 Mi |
| `MaxArrayElements` (one flattened table) | 128 Mi |
| `MaxNodeDepth` (DOM nesting, from `parentIndex`, computed without recursion) | 10 000 |
| `MaxComputedStyleNames` | 1024 |
| `MaxJsonDepth` | 64 |

Anything else wrong with the input (malformed JSON, a wrong type, an inconsistent table, another protocol version) is also a `DomSnapshotFormatException`. Mismatch between a snapshot and a document is never an exception: it is a refusal (below).

## The facts

`RenderedFacts` is immutable and thread-safe, holds no reference to the document (it records `Document.Id` and the node count), and is queried with a `Node`: `facts[node]` (or `TryGet`) gives a `NodeFacts` view over shared tables.

Per node (`NodeFacts`): `HasLayout`; `Layout` (a `BoxFacts`); `IsStackingContext`; `StackingContextNodeIndex` (nearest ancestor-or-self whose box begins a stacking context); `BackendNodeId`; `SnapshotNodeIndex`; `InputValue`, `TextValue`, `InputChecked`, `OptionSelected`, `IsClickable`; `GeneratedBoxes`; `Background`.

Per box (`BoxFacts`): `BorderBox` (the layout item's `bounds`, document coordinates, CSS pixels); `OffsetRect`, `ClientRect`, `ScrollRect` (`offsetRects`, `clientRects`, `scrollRects` as reported, null when absent); `PaintOrder` (global; boxes painted together share a value; larger paints later); `IsStackingContext`; `LayoutText`; `TextBoxes` (start and length in UTF-16 units of the layout text, and bounds); `BlendedBackgroundColor` and `TextColorOpacity` (as reported); `GetStyle(name)` (the raw string Chrome serialized, for a property in `ComputedStyleNames`).

A node **was laid out** iff it has a box: `display: none` elements and their descendants, and `display: contents` elements, have none; `visibility: hidden` and zero-opacity elements do. Facts say nothing about states the capture did not have (hover, focus); `RenderedFacts.Context` and `ViewportWidth`/`ViewportHeight` record what the caller said the capture was taken under.

Boolean rare data (`inputChecked`, `optionSelected`, `isClickable`) lists only the nodes where it is true, so these facts are `false` for every other node, not "unknown".

## Choosing the document

The intended input is Chrome's serialized DOM (`document.documentElement.outerHTML`, optionally preceded by the serialized doctype) parsed by OneHtml, captured in the same session as the snapshot with page script disabled. Parsing the original bytes also works when no script ran or changed the DOM. A DOM that script changed, parsed from the original bytes, is refused (below).

## Mapping rules (`DomSnapshotAdapter.Map`)

Every snapshot node of the chosen snapshot document lands in exactly one category, and `SnapshotAccounting.IsComplete` states that none is lost:

| Category | Which nodes | Facts |
|---|---|---|
| mapped | the document node, elements, text nodes, comments, and template content (the fragment under a `template` pairs with `TemplateContent`) | on the OneHtml node |
| pseudo | a node with a `pseudoType` (or a name starting with `::`) and all its descendants | `GeneratedBox` on the host element: type, identifier, the pseudo-element's box, the boxes of its generated children, and the generated text (the layout text of those boxes in layout order) |
| shadow | a shadow root (any `shadowRootType`: user-agent, open, closed) and everything below it, including pseudo-elements whose host is inside it | none; user-agent shadow trees of form controls have no OneHtml nodes. The host element keeps its own facts |
| prolog | document-level nodes other than the root element (doctype, comments before or after `<html>`) | none; the document is not required to have them, and when it does they are not compared |
| other documents | the nodes of every other snapshot document | map each with `Map(..., new DomSnapshotMapOptions { DocumentIndex = n })` against a document parsed from that frame; `RenderedFacts.Frames` lists the frame owner elements and their document indexes |

Layout items follow their node: a mapped node's first layout item becomes its box (a second item for the same node is counted as `LayoutDuplicate` and dropped); items of pseudo-element nodes become generated boxes; items inside shadow trees are dropped (`LayoutShadow`).

Pairing walks both trees in document order, iteratively. Under each pair of parents, the snapshot's children (leaving out pseudo-elements, shadow roots and prolog nodes) and the document's children must line up one to one:

- elements by local name, compared ASCII case-insensitively (Chrome reports `DIV`, SVG keeps `foreignObject`);
- text nodes (and CDATA nodes) by their exact characters, including whitespace-only text nodes;
- comments and processing instructions by position only (they never affect rendering);
- attributes per `AttributeCheck` (below).

### The refusal rule

A snapshot is refused (`DomSnapshotMapResult.Succeeded` is false, `Refusal` says why, nothing throws) unless the DOM it describes is the document's DOM:

1. **Counts.** The number of elements, text nodes and comments (outside template content and the document prolog) must match. A removed or added node fails here: `NodeCountMismatch`, with both sides' counts.
2. **Walk.** The first difference in document order, with the snapshot node, the document node and a path such as `html[1] > body[2] > div[1] > span[1]` (elements numbered among element siblings): `StructureMismatch` (a different element name or kind, or different child counts), `TextMismatch` (shows both strings, clipped), `AttributeMismatch`.
3. **Fingerprint.** A 64-bit FNV-1a hash over the pre-order stream of (depth, kind, ASCII-lower-cased element name) for elements and text nodes. Both sides compute it (`DomSnapshotAdapter.StructureFingerprint`); the document's is stored in the facts. After a clean walk the fingerprints must agree (`FingerprintMismatch`; not expected, but never ignored).

`AttributeCheck` decides how attributes weigh in:

| Mode | Rule |
|---|---|
| `Values` (default) | every element has the same attributes (same names, same values, any order). A changed value can change layout, so the facts no longer describe the page |
| `Names` | the same names; values may differ. For a document whose attribute values were edited where layout is known not to depend on them (for example `aria-*`, `title`) |
| `None` | attributes are not compared |

Also refused: a snapshot document index that does not exist (`DocumentIndexOutOfRange`), a fragment document (`DocumentIsFragment`), and a snapshot document that does not start with a document node (`MalformedSnapshot`).

Declarative shadow DOM written as `<template shadowrootmode>` in the source is consumed by Chrome, so a document parsed from the original bytes keeps the `template` element and is refused; parse the serialized DOM instead.

## Background samples

Chrome's snapshot carries no pixels. A caller who has colour summaries of the pixels behind elements supplies them as JSON (`BackgroundSamples.Parse`), and `RenderedFacts.WithBackgroundSamples` returns new facts with them attached (`NodeFacts.Background`); the original facts are unchanged.

```json
{
  "format": "onehtml-background-samples",
  "version": 1,
  "viewport": { "width": 1280, "height": 800 },
  "samples": [
    { "backendNodeId": 12, "median": "#003366", "p5": "rgb(0, 30, 80)", "p95": "#114477", "pixels": 4800 },
    { "snapshotNode": 31, "median": "rgba(10, 20, 30, 0.5)" }
  ]
}
```

- `format` and `version` are required; the reader refuses another version. Unknown members are ignored.
- `viewport` is optional; when both it and the facts' viewport are known and differ, `WithBackgroundSamples` throws `ArgumentException`.
- Each sample names its element by `backendNodeId` (preferred when both are given) or by `snapshotNode`, the node's index in the snapshot's main document. Entries that name no mapped element are reported in `BackgroundSampleApplication.Unmatched`. A later entry for the same element replaces an earlier one.
- `median`, `p5`, `p95` are per-channel median and 5th and 95th percentiles over the pixels behind the element's text, as `#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa`, `rgb(r, g, b)` or `rgba(r, g, b, a)`. `p5` and `p95` default to `median`. `pixels` (optional, ≥ 0) is the sample size.
- Limits: 64 MiB of input, 1 000 000 samples, JSON depth 16; violations throw `BackgroundSamplesFormatException`.

## What Chrome 156 does (observed on the fixture captures)

- The snapshot has **no shadow roots and no template content fragment**: form controls show no user-agent shadow tree and a `template` element has no children in the node table. The mapper still handles both if a capture has them (tested on synthetic snapshots).
- A **pseudo-element** (`::before`, `::after`, `::marker`) is an element node (`nodeType` 1, name `::before`) child of its host with `pseudoType` set. It has **two layout items under the same node index**: the element's box (no layout text), then an anonymous box carrying the generated text (`layoutText`) with the text box. The first becomes the generated box's own layout; the rest are its further boxes, and their text is the generated text.
- Every **text node** that is laid out has a layout item with `layoutText`; text boxes refer to it (`start` is relative to that text). Collapsed whitespace text can have a layout item with an empty rectangle.
- `offsetRects` are relative to the offset parent, `clientRects` are `[clientLeft, clientTop, clientWidth, clientHeight]`, `scrollRects` are `[scrollLeft, scrollTop, scrollWidth, scrollHeight]`; text items have `[]`. `bounds` are absolute document coordinates.
- `blendedBackgroundColors` is one string index per layout item, `-1` when the element has no background of its own; `textColorOpacities` is a number per item (0.9 inside an element with `opacity: .9`).
- `nodeName` is upper case for HTML elements (`DIV`), `#text`, `#document`; the doctype node (type 10) is named in lower case (`html`) and is absent for pages without a doctype.
- `position: sticky`, `fixed`, `opacity < 1` and `z-index` on positioned elements begin stacking contexts (`stackingContexts`).
- Frames: each `iframe` has a `contentDocumentIndex` entry, including empty and `display: none` frames; every frame document is its own `documents` entry.

## Testing

- `FakeSnapshot` (tests) writes snapshot-shaped JSON for a document with pseudo-elements, shadow trees, frames and template content, for cases a given capture does not have.
- `testdata/rendered/pages/` holds the fixture pages; `testdata/rendered/captures/` holds Chrome's output on them (snapshot, serialized DOM, browser version), and `CaptureTests` maps each and asserts key facts.
- Fuzz target `domsnapshot` (`fuzz/`): reader, adapter and background-sample reader; a successful mapping must account for every node.
