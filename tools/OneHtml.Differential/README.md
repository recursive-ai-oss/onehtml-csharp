# OneHtml.Differential

Corpus commands that compare our parser with itself and with AngleSharp, and our accessibility tree with Chrome's. AngleSharp is used in-process as a black box, and only here (never in `src/`). The corpus commands take a corpus version directory (see `tools/corpus/README.md`: `manifest.jsonl` plus `bodies/ab/<sha256>`) or any directory of loose files (`.css` is a stylesheet, everything else a page). Items are processed in SHA-256 order, so `--limit n` always picks the same items. Output never contains page content except where stated below; copy nothing from an output directory into the repo.

```
OneHtml.Differential <roundtrip|angle|replay> <corpus-dir> [--out dir] [--jobs n] [--limit n] [--timeout s] [--top n] [--max-reduce n] [--only pages|stylesheets] [--seed n] [--per-page n]
OneHtml.Differential show <file>
```

Defaults: `--out ./out`, `--jobs 2`, no limit, `--timeout 30` seconds per item, `--top 20`, `--max-reduce 100` (for `angle`, how many mismatches are minimized, see below). `--only` restricts `roundtrip`. `show <file>` prints our tree and AngleSharp's for one input, and where the first differing text nodes differ.

## `roundtrip`: lossless output and robustness

For every 2xx page, parses the stored bytes with the record's `Content-Type` and requires `ToLosslessBytes()` to equal the input. For every 2xx stylesheet, parses the bytes (encoding by CSS Syntax's fallback rules, using the library's WHATWG encodings) and requires the source writer to reproduce the decoded text. Every item runs under a wall-clock timeout (a parse that ignores cancellation for 5 more seconds is abandoned and reported as `hang`), every exception is caught and classified (nothing an item does can abort the run).

Failure classes: `mismatch`, `timeout`, `hang`, `limit` (a configured parse limit was hit), `exception:<Type>`, `missing-body`.

Writes to `--out`: `summary.json`, `summary.md` (counts, failures by class, top failures with their sha256, slowest files with parse time and node count) and `failures/<class>.sha256.txt`, one sha256 per line. Exit status 1 if anything failed.

## `angle`: AngleSharp tree differential

For every page, parses with our parser and with AngleSharp's `HtmlParser`, giving AngleSharp the decoded text our sniffer produced, and compares the trees in document order: node kind, namespace, local name, attributes, text and comment data, doctype name, public and system identifiers, template contents, child counts, and quirks mode. Attributes are paired by namespace and local name (what DOM identifies an attribute by), then qualified name (`attr.prefix`), value and order are compared. AngleSharp reports only quirks and not-quirks, so limited-quirks counts as standards mode. The two parsers' sniffed encodings are compared separately as the `encoding` field (AngleSharp gets the same bytes and `Content-Type` through its own loader). When only the last-resort default separates the two (ours windows-1252, AngleSharp UTF-8; HTML 13.2.3.2 leaves it to the locale) the page is not a mismatch and is listed as such.

Below a structural difference (child counts, node kinds, names) the two trees no longer line up by position, so that subtree is not compared further and what it would report is not counted.

Writes to `--out`:

- `summary.json` and `summary.md`: pages compared and identical, mismatch rate per field (pages with at least one, and total occurrences), encoding disagreements, causes and classes.
- A **class** is one difference in a page, keyed by field, the node types on both sides, the innermost element and the **cause** it was traced to (`childCount @ div [search-element]`). Per page a class counts once: the document-level differences, and every difference that is not inside the subtree of an earlier structural one.
- A **cause** is a section of `docs/anglesharp-differential.md`, where our behaviour is tied to the standard's text. It is recognised from the field and node types, or, for structural differences, from the minimized reproducer. A class with no known cause is `unexplained`; the goal is none. `--max-reduce` bounds how many mismatches are minimized (default 100; the full corpus needs about 200); a mismatch beyond it is `not-analysed`.
- `classes/<id>.sha256.txt`: the pages of each class.
- `repro/<id>/min.html`: the minimized reproducer of the first page of each class: a window around the divergence, then delta debugging over lines, over pieces of long lines and over characters (up to 6000 parses or 20 s) while the same class key and the same path shape still differ. **These are derived from crawled pages: keep them out of the repo.** Hand-written tests are written from them instead (`AngleSharpDivergenceTests`).
- `pages.jsonl`: one line per page with sha256, node count, both encodings and mismatch counts per field.

## `replay`: durable edits on mutated copies

For every page, saves a `setAttribute` edit (`EditJson.Create`, written and read back as JSON) on up to `--per-page` sampled elements (default 5), then replays it on copies of the page changed in one known way: blanks inside the start tag, swapped attribute order, a sibling inserted before the target, the target removed, duplicated before or after, its text changed, its `id` or `class` changed, an unrelated text node changed (longer, then same length). Seven more mutations do the same for the page's `<style>` rules (whitespace, rule removed or duplicated, declaration changed, rule inserted, a style element inserted before). The expected element comes from the mutation, never from the library: a replay that applies to a node other than the target counts as `wrong` unless the node is a byte-identical copy (`twin`) or has the same tag, key attributes and text as the target (`lookalike`), which the saved format cannot tell apart. Exit status 1 if any `wrong`. Targets whose edit does not apply even to the unmutated page are skipped and counted.

Writes `replay.md` (applied, stale, ambiguous, invalid, wrong, twin, lookalike and unexpected counts per mutation) and `replay-findings.txt` to `--out`; `--seed` fixes the sampling.

## `ax`: Chrome accessibility-tree differential

Takes a `tools/capture` output directory instead of a corpus: compares our tree with Chrome's `Accessibility.getFullAXTree` node by node and writes mismatch rates per field and mismatch classes with examples (`summary.md`, `summary.json`, `classes.json`, `captures.jsonl`). `--corpus` supplies the stylesheets the pages link; `--facts on|off|both` and `--dump` as described in `docs/chrome-ax-differential.md`, which is the normative description of the matching, the normalization and the output.

## Worker commands

Every command takes the worker's shared-job lock inside the job (`~/WORKER-BENCH.lock`) and exit with status 3 at once if it is held; they never wait. Run from the repo root on the worker, after the repo is synced:

```
bash tools/OneHtml.Differential/run-worker.sh roundtrip ~/onehtml/corpus/<version> ~/onehtml/u7/out/roundtrip --jobs 2
bash tools/OneHtml.Differential/run-worker.sh angle     ~/onehtml/corpus/<version> ~/onehtml/u7/out/angle     --jobs 2
bash tools/OneHtml.Differential/run-worker.sh ax ~/onehtml/capture-out/<version> ~/onehtml/out/ax --corpus ~/onehtml/corpus/<version> --jobs 8
```

The script builds in Release into `~/onehtml/u7/artifacts` with the worker's .NET 10, records CPU, core count and load before, during (every 30 s) and after in `load.log`, and cuts the run off after `ONEHTML_RUN_MAX_SECONDS` (default 7200). Outputs land in the out dir with `environment.txt`, `load.log` and `run.log`. Runs on more than 2 jobs or for over 5 minutes must keep the lock; for a first look add `--limit 500`.
