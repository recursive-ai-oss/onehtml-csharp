# Rendered-facts fixtures

`pages/` holds small HTML pages written for this repository, one feature each (hidden content, stacking, clipping,
generated content, form controls, tables, frames, templates, whitespace, wrapped text, backgrounds, a page whose
script would change the DOM, implied structure, landmarks, pointer target sizes). They are ours and carry the repository licence.

`captures/` holds the output of a pinned headless Chrome on those pages: `DOMSnapshot.captureSnapshot` results and the
serialized DOM, with the Chrome version recorded beside them. See `docs/rendered-facts.md`.
