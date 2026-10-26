# RecursiveAI.OneHtml

Parse HTML exactly as a browser does, query it, compute styles, and edit HTML and CSS without disturbing a byte the edit didn't touch. Pure C# on the .NET base class library, safe code only, NativeAOT- and trim-compatible, `net10.0`.

- **Encoding and parser.** The WHATWG Encoding Standard (BOM, transport `Content-Type`, `<meta>` prescan) and the HTML tokenizer and tree construction, including the adoption agency algorithm, foster parenting, `<template>`, foreign content and fragment parsing. Parse errors are recorded as the spec says, never thrown.
- **DOM as snapshots.** A `Document` is an immutable arena; `Node` is a `readonly struct` handle. Every node knows its byte range in the original, and parser-inserted nodes are marked.
- **Selectors Level 4** compiled for repeated use, and locators that are checked to match exactly their target.
- **CSS.** CSS Syntax parsing with source maps, a property table generated from the CSS specifications, and a cascade for every known property (layers, `!important`, `var()`, shorthands, media and supports conditions) under a `StyleContext` that holds the viewport, media features and one focus or hover state.
- **Lossless editing.** `doc.Apply(edits)` splices bytes only where an edit landed, re-parses the output and compares it with the expected tree, and returns a new snapshot plus a change set. Edits can be saved as versioned JSON and replayed on another copy of the page; a replay that cannot find exactly one matching target reports `stale` or `ambiguous` and never guesses.

```csharp
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Editing;
Document doc = Document.Parse("<p>Hello <a href=/x>world</a>");
Node link = doc.Root.QuerySelector("a[href]");
EditResult edit = doc.Apply(new[] { Edit.SetAttribute(link, "title", "A link") });
byte[] output = edit.Document!.ToLosslessBytes();
```

Styles under a context:

```csharp
using RecursiveAI.OneHtml.Css;
Document doc = Document.Parse("<style>a { color: red } a:focus { color: blue }</style><a href=/x>x</a>");
Node link = doc.Root.QuerySelector("a");
DocumentStyles styles = DocumentStyles.Create(doc);
string normal = styles.GetStyle(link).GetPropertyValue("color");
string focused = styles.WithContext(StyleContext.Default.WithFocus(link)).GetStyle(link).GetPropertyValue("color");
```

The library never reads files or the network, runs no script and does no layout. Linked stylesheets are supplied by the caller. Hostile-input limits (depth, attribute count, input size, stylesheet and selector complexity) are checked before the work they guard.

## More

- Family overview, conformance numbers and benchmarks: [README](https://github.com/recursive-ai-oss/onehtml-csharp/blob/main/README.md).
- Behaviour documents: [docs/](https://github.com/recursive-ai-oss/onehtml-csharp/blob/main/docs).
- Third-party material and licences: [THIRD-PARTY-NOTICES.md](https://github.com/recursive-ai-oss/onehtml-csharp/blob/main/THIRD-PARTY-NOTICES.md).

Licensed under either of MIT or Apache-2.0 at your option. Copyright Recursive AI, LLC.
