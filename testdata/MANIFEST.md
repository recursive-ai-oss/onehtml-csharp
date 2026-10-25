# Test data manifest

Files here are copied unchanged from their sources. `SHA256SUMS` lists the hash of every file.

| Path | Source | Licence | Version |
|---|---|---|---|
| `html5lib-tests/tokenizer/`, `html5lib-tests/encoding/`, `html5lib-tests/LICENSE` | <https://github.com/html5lib/html5lib-tests> | MIT | commit `c777c408b61078ea2eb4acefc2535f54dbc8b28a` (2026-09-29) |
| `whatwg/encodings.json`, `whatwg/indexes.json` | <https://encoding.spec.whatwg.org/encodings.json>, <https://encoding.spec.whatwg.org/indexes.json> | CC BY 4.0 | retrieved 2026-10-25 |

The tokenizer and encoding suites are run by `tests/OneHtml.Tests`. Cases that are deliberately not run are listed,
with the reason, in `tests/OneHtml.Tests/Html5lib/KnownDifferences.cs`.
