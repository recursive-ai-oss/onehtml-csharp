# Reference capture

Loads each HTML page of a corpus version in the pinned headless Chrome (see `../pins`) and records what Chrome says about it: the accessibility tree, a DOM snapshot with computed styles and layout, axe-core results, and the serialized DOM. These are the black-box references the accessibility-tree, rendered-facts and checker comparisons run against. Python 3.13, standard library only. Nothing here is part of the libraries.

## Usage

```
python3 capture.py probe                                  # start Chrome, pick the script-disabled mode, print it
python3 capture.py run --corpus ~/onehtml/corpus/<version> --out <out-dir> [options]
python3 capture.py summary <out-dir>
bash run-worker.sh <corpus-dir> <out-dir> [run options]   # worker wrapper: lock, timeout, load log
python3 -m unittest test_capture                          # self-test, no Chrome needed
```

`run` options:

| Option | Meaning |
|---|---|
| `--viewport WxH` | Repeatable. Default `1280x800` (device scale factor 1, not mobile). Each page is loaded afresh per viewport. |
| `--jobs N` | Chrome processes in parallel (default 2). |
| `--limit N` | Only the first N pages of the corpus, in manifest order. |
| `--max-seconds S` | Stop starting pages after S seconds; pages in flight finish. The last line is `REMAINING=<n>`. |
| `--page-timeout S` | Per page and viewport, 120 by default. A page that does not finish is recorded as `timeout`. |
| `--axe-timeout S` | Seconds allowed for `axe.run` on one page (default 60). |
| `--settle S` | Wait after the page reports complete, 0.3 by default. |
| `--retry-failed` | Also redo pages recorded as failed (otherwise only pages with no record are attempted). |
| `--recycle N` | Restart a Chrome after N pages (default 100). |
| `--no-sandbox auto\|always\|never` | `auto` (default) starts Chrome sandboxed and only drops the sandbox if it cannot start with it; the choice is recorded in `environment.json`. |
| `--script-mode` | `auto` or one of the modes below. |
| `--chrome`, `--axe` | Override the pinned binaries. |

Resumable: run the same command again. A page counts as done when the run manifest has a record for its body hash. A Chrome that dies is replaced and the page retried once. `environment.json` from the first run must match (Chrome product, axe checksum, viewports, computed-style list) or the run refuses, so one output directory never mixes setups.

## Which pages

Manifest records with `kind: page`, status 200, a `text/html` content type and a stored body, one per distinct body hash (the first URL in manifest order). The page is loaded from its recorded URL, so relative links resolve as they did when it was fetched.

## Offline serving

Chrome is started with `--proxy-server=http://127.0.0.1:9` and `--host-resolver-rules="MAP * ~NOTFOUND"`, so nothing can reach a network even if interception missed it. Every request goes through the `Fetch` domain and is answered by the tool:

- **Documents and stylesheets** (including `@import`s and subframes) whose URL is in the corpus manifest are fulfilled with the stored body, the recorded status code and the recorded response headers. URLs are compared after normalization (scheme and host lower-cased, default port and fragment dropped). Stored redirects (301, 302, …) are replayed as redirects. A stored `gzip` or `deflate` body is decoded and `Content-Encoding`/`Content-Length`/`Transfer-Encoding`/`Set-Cookie`/`Refresh` and a few other headers are dropped. Other headers, including `Content-Type` and `Content-Security-Policy`, are served as recorded, so a page's own policy applies as it did in the wild.
- **A stylesheet or subframe document not in the corpus** gets an empty 200 (`text/css` or `text/html`), so loading finishes and nothing shows as an error page.
- **Everything else** (scripts, images, fonts, media, XHR/fetch, WebSockets, a main document not in the corpus) fails with `BlockedByClient`. Scripts and images are blocked even when the corpus holds them. Images being absent means layout that depends on image size is not reproduced; the layout boxes are for the page without them.

Per capture, `meta.json` counts requests as `served`, `empty` and `blocked` and lists up to 100 URLs that were not served.

Chrome flags: `--headless --remote-debugging-pipe` (protocol over pipes, no port) `--no-first-run --no-default-browser-check --disable-gpu --disable-background-networking --disable-component-update --disable-sync --disable-extensions --disable-default-apps --disable-breakpad --disable-client-side-phishing-detection --metrics-recording-only --mute-audio --force-color-profile=srgb --force-device-scale-factor=1 --lang=en-US --hide-scrollbars --disable-features=IsolateOrigins,site-per-process,… --disable-site-isolation-trials` plus the proxy and resolver flags. Site isolation is off so that every frame lives in the one session whose requests we intercept. `--no-sandbox` is added only when Chrome cannot start without it, which is acceptable here because every request is answered from the local store. The exact list is in `environment.json`. Each page runs in its own browser context (no shared cache or cookies).

## Script-disabled mode

The library sees the static document and never runs script, so page script must not run. Axe, however, is script, and has to run. The modes, tried in this order unless `--script-mode` names one:

1. `emulation-main`: `Emulation.setScriptExecutionDisabled(true)` on the tab, then our own `Runtime.evaluate` calls in the page's main world. Page scripts (inline, external, event handlers) do not run, and the parser's scripting flag is off, so `<noscript>` content is parsed as markup, as in a static parse.
2. `emulation-isolated`: the same switch, with our evaluations in an isolated world created with `Page.createIsolatedWorld`.
3. `csp-main`: no switch; every document is served with an added `Content-Security-Policy: script-src 'none'`, which stops page scripts, and our evaluations run in the main world. `<noscript>` stays raw text in this mode.

At start the tool loads a built-in probe page (an inline script that renames the title, a `<noscript>` block) in each mode and takes the first in which the script did not run and an evaluation works. The mode and the probe's findings (including `noscript_parsed_as_markup`) are written to `environment.json` and `meta.json`; `probe` prints them. Modes that were tried and rejected are listed too. If none works, the run stops.

axe-core is `axe.min.js` from the pinned install, read as an opaque string and sent to `Runtime.evaluate` after the other outputs have been taken (so it cannot change them). In the two `emulation` modes the switch is turned off again just before axe is injected: axe waits on timers and promises, which never fire while scripting is disabled, so `axe.run` would never return. By then the page has finished loading with scripts off and nothing in it is pending, so none of its script runs afterwards. `--axe-timeout` (default 60 s) bounds `axe.run`; a timed-out step is recorded as an error for that step and the other outputs are kept. It is never inspected, copied or stored. Axe is injected into the top frame only; frames are reported by axe as it sees fit.

## Output

```
<out>/
  environment.json     setup of the first run (schema onehtml-capture/1/environment)
  manifest.jsonl       one line per page attempt, plus run / run-end lines
  logs/chrome-*.stderr
  debug/<body sha256>.log   per page: every protocol command with its time and result summary, every request and
                            how it was answered, each step, and on failure the exception and the last command
  captures/<sha[0:2]>/<body sha256>/<WxH>/
    meta.json          written last; its presence means the capture is complete
    ax.json.gz  snapshot.json.gz  axe.json.gz  dom.json.gz
    errors.json        instead of meta.json when a step failed (same shape, with `errors`)
```

Files are gzip (mtime 0) of compact UTF-8 JSON. Every file has a `schema` string; the version is the number after `onehtml-capture/`, raised only when meaning changes. Unknown fields are ignorable.

**`ax.json`** (`onehtml-capture/1/ax`): `{"schema", "frames": [{"frameId", "parentFrameId", "url", "nodes": [...], "error"?}]}`. Frames are in document order, the main frame first, at most 50. `nodes` is the `nodes` array of `Accessibility.getFullAXTree` for that frame, unchanged.

**`snapshot.json`** (`onehtml-capture/1/snapshot`): `{"schema", "format": {"domain": "DOMSnapshot", "method": "captureSnapshot", "protocolVersion", "product", "parameters": {...}}, "snapshot": {"documents": [...], "strings": [...]}}`. `parameters` is exactly what was sent: `computedStyles` (the list in `capture.py`, also in `environment.json`), `includePaintOrder`, `includeDOMRects`, `includeBlendedBackgroundColors`, `includeTextColorOpacities`, all true. The `snapshot` value is the protocol's result unchanged. The format is pinned by Chrome's version plus `protocolVersion`; a reader checks both.

**`dom.json`** (`onehtml-capture/1/dom`): `{"schema", "doctype": "<!DOCTYPE html>" | null, "outerHTML"}`: `document.documentElement.outerHTML` after load, with the doctype serialized separately.

**`axe.json`** (`onehtml-capture/1/axe`): `{"schema", "axe_version", "options", "results"}`. `results` is axe's result object as returned by `axe.run`, unchanged. The run enables every rule from `axe.getRules()` (best-practice and experimental included) and sets `resultTypes` to violations, passes, incomplete and inapplicable, so all nodes with their `target` selectors are kept for passes and incomplete too, and `xpath: true`.

**`meta.json`** (`onehtml-capture/1/meta`): `url`, `body_sha256`, `viewport {width, height, deviceScaleFactor}`, `final_url`, `captured_at`, `script_mode`, `ok`, `warnings` (for example the page navigated away by redirect or meta refresh), `errors` (empty when ok; keys `ax`, `snapshot`, `dom`, `axe`), `files {ax|snapshot|dom|axe: {bytes, json_bytes, sha256}}`, `ax {frames, nodes, nodes_main}`, `snapshot {documents, strings, nodes, layout_nodes}`, `dom {chars}`, `axe {version, violations, passes, incomplete, inapplicable, rules}` where the four totals count nodes and `rules` is `{kind: {ruleId: nodes}}`, `requests {served, empty, blocked, probe}`, `unserved [...]`, `seconds {total, ax, snapshot, dom, axe}`.

**`manifest.jsonl`**: lines with `"type": "page"` have `url`, `body_sha256`, `body_bytes`, `run`, `started`, `status` (`ok`, `failed`, `timeout`, `crashed`), `error`, `seconds` and per viewport `{viewport, ok, errors, ax_nodes, snapshot_bytes, axe_violations, bytes, seconds}`. `run` lines record the chunk start, `run-end` lines its counts and `remaining`.

**`environment.json`** (`onehtml-capture/1/environment`): `created`, `chrome {product, protocolVersion, revision, userAgent, pinned}`, `axe {pinned, sha256, options}`, `viewports`, `computed_styles`, `flags`, `script_mode`, `script_probe`, `script_modes_rejected`, `serving`, `settle_seconds`, `fonts {faces, sha256}` (installed fonts, from `fc-list`; text boxes depend on them), `python`, `corpus`.

## Processes

Each Chrome is started through a small launcher that asks the kernel to kill it when this process dies (`PR_SET_PDEATHSIG`, so even `kill -9` of the tool takes the Chromes with it) and runs in its own process group. On SIGTERM, SIGINT, SIGHUP and normal exit the tool kills every Chrome it started. A Chrome whose command timed out is discarded and replaced.

Known limits: transitions and animations in flight at capture time are not frozen; captures taken on hosts with different fonts differ in layout; the capture is of one load, not of repeated runs.

## Worker smoke

```
cd <repo>
PY=$(~/.local/bin/uv python find 3.13)
$PY tools/capture/capture.py probe
bash tools/capture/run-worker.sh ~/onehtml/corpus/smoke ~/onehtml/capture-out/smoke --limit 20 --jobs 2
$PY tools/capture/capture.py summary ~/onehtml/capture-out/smoke
```

`summary` prints pages by status, AX node counts, snapshot sizes, serialized DOM sizes and axe violation nodes by rule.
