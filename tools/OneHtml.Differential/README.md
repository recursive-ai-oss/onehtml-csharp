# OneHtml.Differential

Corpus commands that compare our parser with itself and with AngleSharp. AngleSharp is used in-process as a black box, and only here (never in `src/`). Both commands take a corpus version directory (see `tools/corpus/README.md`: `manifest.jsonl` plus `bodies/ab/<sha256>`) or any directory of loose files (`.css` is a stylesheet, everything else a page). Items are processed in SHA-256 order, so `--limit n` always picks the same items. Output never contains page content except where stated below; copy nothing from an output directory into the repo.

```
OneHtml.Differential <roundtrip|angle> <corpus-dir> [--out dir] [--jobs n] [--limit n] [--timeout s] [--top n] [--max-reduce n]
```

Defaults: `--out ./out`, `--jobs 2`, no limit, `--timeout 30` seconds per item, `--top 20`, `--max-reduce 100`.

## `roundtrip`: lossless output and robustness

For every 2xx page, parses the stored bytes with the record's `Content-Type` and requires `ToLosslessBytes()` to equal the input. For every 2xx stylesheet, parses the bytes (encoding by CSS Syntax's fallback rules, using the library's WHATWG encodings) and requires the source writer to reproduce the decoded text. Every item runs under a wall-clock timeout (a parse that ignores cancellation for 5 more seconds is abandoned and reported as `hang`), every exception is caught and classified.

Failure classes: `mismatch`, `timeout`, `hang`, `limit` (a configured parse limit was hit), `exception:<Type>`, `missing-body`.

Writes to `--out`: `summary.json`, `summary.md` (counts, failures by class, top failures with their sha256, slowest files with parse time and node count) and `failures/<class>.sha256.txt`, one sha256 per line. Exit status 1 if anything failed.

## `angle`: AngleSharp tree differential

For every page, parses with our parser and with AngleSharp's `HtmlParser`, giving AngleSharp the decoded text our sniffer produced, and compares the trees in document order: node kind, namespace, local name, attributes (qualified name, namespace, value, order), text and comment data, doctype name, public and system identifiers, template contents, child counts, and quirks mode. AngleSharp reports only quirks and not-quirks, so limited-quirks counts as standards mode. The two parsers' sniffed encodings are compared separately as the `encoding` field (AngleSharp gets the same bytes and `Content-Type` through its own loader; the default for a page with no declaration differs between the two, so expect many `windows-1252 -> utf-8` rows). After a difference in a node's kind, namespace or name its subtree is not compared.

Writes to `--out`:

- `summary.json` and `summary.md`: pages compared and identical, mismatch rate per field (pages with at least one, and total occurrences), encoding disagreements, and the classes below.
- A **class** is the first difference in a page's tree, keyed by field, the node types on both sides and the element path without indexes (`attr.name|xlink:href|(absent) @ body>math>mi`); encoding and quirks differences are classes of their own. Each class lists its page count and an example page by sha256.
- `classes/<id>.sha256.txt`: the pages of each class.
- `repro/<id>/min.html`: a minimized reproducer for the top `--max-reduce` classes: a window around the divergence, then delta debugging over lines and characters (up to 6000 parses or 20 s per class) while the same field and node types still differ. **These are derived from crawled pages: keep them out of the repo.** Hand-written tests are written from them instead.
- `pages.jsonl`: one line per page with sha256, node count, both encodings and mismatch counts per field.

## Worker commands

Both commands take the worker's shared-job lock inside the job (`~/WORKER-BENCH.lock`) and exit with status 3 at once if it is held; they never wait. Run from the repo root on the worker, after the repo is synced:

```
bash tools/OneHtml.Differential/run-worker.sh roundtrip ~/onehtml/corpus/<version> ~/onehtml/u7/out/roundtrip --jobs 2
bash tools/OneHtml.Differential/run-worker.sh angle     ~/onehtml/corpus/<version> ~/onehtml/u7/out/angle     --jobs 2
```

The script builds in Release into `~/onehtml/u7/artifacts` with the worker's .NET 10, records CPU, core count and load before, during (every 30 s) and after in `load.log`, and cuts the run off after `ONEHTML_RUN_MAX_SECONDS` (default 7200). Outputs land in the out dir with `environment.txt`, `load.log` and `run.log`. Runs on more than 2 jobs or for over 5 minutes must keep the lock; for a first look add `--limit 500`.
