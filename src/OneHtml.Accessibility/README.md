# RecursiveAI.OneHtml.Accessibility

The accessibility tree a browser exposes to screen readers, computed from a `RecursiveAI.OneHtml` document and its cascade with no browser. Chromium is the default where the specifications leave room, and each such choice is in one documented list.

- **Tree.** For every node: role (and how it was derived), accessible name and description with the AccName step that produced them, states and properties, relations, focus order, hidden status with its reason, diagnostics (invalid roles, broken ID references, missing required parents) and the source node. Specifications: WAI-ARIA 1.2, HTML-AAM, AccName 1.2, Core-AAM, SVG-AAM.
- **Incremental.** `tree.Update(edit, restyle)` recomputes only what an edit's change set reached; the result equals a full rebuild.
- **Views.** A reading view (the tree linearized with role announcements, names, key states and boundaries) and navigation lists (headings, landmarks, links, form fields, tables), each item mapped back to its node. Announcements are generic English text and an approximation, not any screen reader's output. Tree and reading-view diffs between snapshots or contexts.
- **`RenderedFacts`.** An optional side table of layout boxes and paint order, filled from Chrome's `DOMSnapshot.captureSnapshot` JSON that the caller supplies, or directly from in-memory layout data with `RenderedFactsBuilder`. A snapshot of a different DOM is refused. Without facts the tree is computed from markup and the cascade alone.
- **Export** in the shape of Chrome's accessibility tree, for comparison.

```csharp
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Accessibility;
using RecursiveAI.OneHtml.Css;
Document doc = Document.Parse("<main><h1>Title</h1><img src=a.png alt='Logo'><button>Save</button></main>");
DocumentStyles styles = DocumentStyles.Create(doc);
AccessibilityTree tree = AccessibilityTree.Build(doc, styles);
foreach (AxNode node in tree.Nodes.Where(n => !n.IsIgnored))
{
    Console.WriteLine($"{node.Role} \"{node.Name.Value}\"");
}
string reading = tree.CreateReadingView().ToText();
int headings = tree.CreateNavigationLists().Headings.Count;
```

No layout without `RenderedFacts`, no script, no screen-reader emulation. Name computation, `aria-owns` re-parenting and ID lists are bounded so hostile pages cannot hang the build.

## More

- Family overview, conformance numbers and benchmarks: [README](https://github.com/recursive-ai-oss/onehtml-csharp/blob/main/README.md).
- Behaviour documents: [docs/](https://github.com/recursive-ai-oss/onehtml-csharp/blob/main/docs).
- Third-party material and licences: [THIRD-PARTY-NOTICES.md](https://github.com/recursive-ai-oss/onehtml-csharp/blob/main/THIRD-PARTY-NOTICES.md).

Licensed under either of MIT or Apache-2.0 at your option. Copyright Recursive AI, LLC.
