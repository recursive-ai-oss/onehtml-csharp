# Test data manifest

Files here are copied unchanged from their sources. `SHA256SUMS` lists the hash of every file.

| Path | Source | Licence | Version |
|---|---|---|---|
| `html5lib-tests/tokenizer/`, `html5lib-tests/encoding/`, `html5lib-tests/LICENSE` | <https://github.com/html5lib/html5lib-tests> | MIT | commit `c777c408b61078ea2eb4acefc2535f54dbc8b28a` (2026-09-29) |
| `whatwg/encodings.json`, `whatwg/indexes.json` | <https://encoding.spec.whatwg.org/encodings.json>, <https://encoding.spec.whatwg.org/indexes.json> | CC BY 4.0 | retrieved 2026-10-25 |
| `wpt/html-syntax-parsing/`, `wpt/LICENSE.md` | <https://github.com/web-platform-tests/wpt> (`html/syntax/parsing/resources/`) | BSD-3-Clause | commit `68d964a5f292bc4b2c2eba2f21e2b916190b6ad2` |
| `wpt/selectors/` (`dom/nodes/`, `css/selectors/parsing/`, `html/semantics/selectors/pseudo-classes/`, WPT-relative paths) | <https://github.com/web-platform-tests/wpt> | BSD-3-Clause | commit `68d964a5f292bc4b2c2eba2f21e2b916190b6ad2` |
| `act/` (`testcases.json` and `testcases/<rule>/<case>.*`, 1134 case files) | <https://act-rules.github.io/testcases.json> and the files it lists; site built from <https://github.com/act-rules/act-rules.github.io> | W3C Software and Document Notice and License | index sha256 `a805d865d61ae2418e56a6a9d303fe60c85089c792b897eb9472ea5513156293` (retrieved 2026-10-25); repository commit `622e612066a7b17727c3755d041930f8e108f35b`; fetched by `tools/catalogue/sources.py --act-testcases`; files unchanged |

The tokenizer and encoding suites are run by `tests/OneHtml.Tests`. Cases that are deliberately not run are listed,
with the reason, in `tests/OneHtml.Tests/Html5lib/KnownDifferences.cs`. The tree construction suite is run by
`tests/OneHtml.Tests/Html5lib/TreeConstructionTests.cs`.

The ACT test cases refer to media and other assets under `test-assets/` in the repository named above; those assets are not copied.
