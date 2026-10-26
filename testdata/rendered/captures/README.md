# Chrome captures of the fixture pages

One directory per page in `../pages/`. Each holds, gzip-compressed JSON:

- `snapshot.json.gz`: the `DOMSnapshot.captureSnapshot` result with the request that produced it (`format`: domain, method, protocol version, browser product, and the parameters including the list of computed styles).
- `dom.json.gz`: the page's serialized DOM after load (`outerHTML` of the document element, and the doctype).

Captured with Chrome for Testing **156.0.8078.47** (protocol 1.3), headless, 1280x800 viewport, device scale factor 1, page script disabled, images blocked, every page served as `https://fixtures.invalid/<page>.html`. Every parameter of `captureSnapshot` that adds data (`includePaintOrder`, `includeDOMRects`, `includeBlendedBackgroundColors`, `includeTextColorOpacities`) was on.

Text widths and line breaks depend on the fonts installed on the machine that captured them, so tests must not assert exact text metrics. They assert structure: which nodes have boxes, ordering of paint order, number of text boxes, generated content.

These files are the output of a browser run on pages written for this repository. Refresh them on each Chrome upgrade and re-run the tests.

The `layout` and `svgprobe` directories also hold `ax.json.gz`: the `getFullAXTree` result of the same page, one entry per frame, for tests that compare the tree with Chrome's. `svgprobe` is the page behind the svg and header/footer role entries of the Chromium profile.

`axprops`, `axprops2` and `axprops3` are probe pages for the properties Chrome states on controls and widgets (disabled, selected, invalid, readonly, popups, ranges, values, editable, the inner nodes of text fields), one element per observation. They were served as `https://pages.test/<page>.html`, and the directories hold `ax.json.gz`, `dom.json.gz` and `snapshot.json.gz`. `ChromePropertyProbeTests` and `ChromeProbeTests` compare our tree with them.
