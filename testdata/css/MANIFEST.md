# CSS test data

Redistributable third-party files, unmodified. Notices are in `THIRD-PARTY-NOTICES.md` at the repository root.

| Path | Source | Commit | Licence |
|---|---|---|---|
| `css-parsing-tests/*.json`, `LICENSE` | https://github.com/SimonSapin/css-parsing-tests (the generator is left out) | `203ce36bffd617db7f118c551e32794561fb273d` | CC0 |
| `wpt/css/css-syntax/` (`*.html`, `crashtests/`, `support/`, `META.yml`), `wpt/css/css-color/parsing/`, `wpt/css/css-values/`, `wpt/css/css-variables/`, `wpt/css/mediaqueries/` (only the files that call the parsing and evaluation helpers the replay runs), `wpt/LICENSE.md` | https://github.com/web-platform-tests/wpt (`charset/` left out) | `68d964a5f292bc4b2c2eba2f21e2b916190b6ad2` | BSD-3-Clause |

Known failures of the web-platform-tests value replay are listed with reasons in `wpt-expectations.txt` (written for this repository).

SHA-256 of the css-parsing-tests files: `css-parsing-tests/SHA256SUMS`; of the web-platform-tests files: `wpt/SHA256SUMS`.

Our own test stylesheets are in `own/` (written for this repository, no third-party content).
