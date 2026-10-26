# Benchmarks

BenchmarkDotNet. AngleSharp and HtmlAgilityPack are used here as black boxes for comparison only.

## Parse benchmark

`ParseBenchmarks` parses a fixed sample of pages **from bytes, end to end** with four rows:

| Row | What it measures |
|---|---|
| `OneHtml` (baseline) | `Document.Parse(bytes)` |
| `OneHtmlParseAndWalk` | the same, then a walk of the whole tree reading every node's name, attributes and text length: the read-path check, which should cost little over parse |
| `AngleSharp` | `HtmlParser.ParseDocument(stream)` on a stream over the bytes |
| `HtmlAgilityPack` | `HtmlDocument.Load(stream)` on a stream over the bytes |

One invocation parses the whole sample once. Extra columns: `MB/s` (sample bytes over the mean sweep time) and `Alloc/doc` (allocated bytes per sweep divided by the page count); `Allocated` is per sweep.

**The sample** comes from the directory in `ONEHTML_BENCH_CORPUS` (a corpus version directory, or a directory of HTML files). Pages are taken in SHA-256 order: `ONEHTML_BENCH_SAMPLE_SIZE` pages (default 100), at most `ONEHTML_BENCH_BAND_CAP` from each size band (default no cap; bands are under 16 KiB, 16 to 128 KiB, 128 to 512 KiB, 512 KiB and up). The same corpus and settings always give the same sample; the run prints its size, byte total and first and last sha256. Nothing from the corpus is stored in the repo. Benchmarks run in process (`--inProcess`) because BenchmarkDotNet's separate-process builder needs the project beside the binaries, which the redirected build output does not have.

Locally: `ONEHTML_BENCH_CORPUS=<dir> dotnet run -c Release --project bench/OneHtml.Benchmarks -- --inProcess --job dry --filter '*'` (a dry run proves the setup; its numbers mean nothing).

## On the worker

```
bash bench/run-worker.sh ~/onehtml/corpus/<version> ~/onehtml/worker/bench-out/<name>
```

Optional: `ONEHTML_BENCH_SAMPLE_SIZE`, `ONEHTML_BENCH_BAND_CAP`, `ONEHTML_BENCH_MAX_SECONDS` (default 7200), and BenchmarkDotNet arguments after the out dir (for example `--job short`). The script claims `~/WORKER-BENCH.lock` atomically and exits with status 3 at once if it is held, builds in Release, records CPU model, cores, SDK, package versions and the load average before, during (every 30 s) and after, and runs the benchmark under `timeout`. Outputs in the out dir: `environment.txt`, `load.log`, `bench.log`, `bdn/results/*`. A run that overlapped another heavy job is not publishable; check `load.log`.

## Checker against axe-core in Chrome

The same pages, three measurements, ours in process and axe-core 4.14.0 in the pinned Chrome (a black box: its bundle is injected as an opaque string and run, never read; pages are served offline from the corpus with page script off, as in `tools/capture`).

| Measurement | Ours (`CheckBenchmarks`, `CheckPerPage`) | axe-core (`axe-bench/axe_bench.py`) |
|---|---|---|
| Full check | `Checker.Check(document)`: cascade, tree and every rule, from a parsed document | `axe.run` over the document, every rule enabled, all result types, no xpath |
| Re-check after one edit | `Apply` of one `SetAttribute` on the first element in `<body>`, `Restyle`, `AccessibilityTree.Update`, then every rule over the new tree | `setAttribute('data-bench', '1')` on the same element, then a full `axe.run` (axe has no incremental mode) |
| Focus overlays | for each of the first 20 focusable nodes: the tree with that node focused (`StyleContext.WithFocus`) and the `color-contrast` rule re-run | for each of the first 20 focusable elements: `focus()`, then `axe.run` with only `color-contrast` |

Differences to keep in mind when reading the numbers: our side reads the page's own bytes and does not get the linked stylesheets, while Chrome loads them; Chrome's time is measured inside the page around the awaited `axe.run` (`performance.now()`), so it excludes protocol and load time; the focusable sets are each side's own notion of focusable (the counts are printed); and the first edit and focus runs of each side are warm-ups that are not reported.

- `BenchmarkDotNet` rows (time and allocation per sweep of the sample): `--filter '*CheckBenchmarks*'`.
- Per-page timings: `dotnet OneHtml.Benchmarks.dll --check-per-page ours.jsonl` (median of `ONEHTML_BENCH_REPEATS`, default 3, after one warm-up).
- Chrome side and comparison: `python3 bench/axe-bench/axe_bench.py run --corpus <corpus> --ours ours.jsonl --out axe.jsonl`, then `axe_bench.py compare ours.jsonl axe.jsonl`.
- On the worker, all of it under the lock: `bash bench/run-worker.sh checker ~/onehtml/corpus/<version> ~/onehtml/bench-out/<name>` (extra variables: `ONEHTML_BENCH_FOCUS_CAP`, `ONEHTML_BENCH_AXE_LIMIT`, `ONEHTML_BENCH_SKIP_BDN`).

## Incremental re-check

`RecheckBenchmarks` times a full check against `Checker.Recheck` after one attribute edit and after one text edit on a synthetic page of about 35,000 DOM nodes, with the edit, restyle and updated tree prepared in setup so the "Re-check" rows are the checker alone, and "Edit and re-check" rows that include `Apply`, `Restyle` and `AccessibilityTree.Update`. Those rows run every rule but `focus-visible-indicator`, which takes tens of seconds on this page and cannot be narrowed; `RecheckAllRulesBenchmarks` has the whole catalogue in a short job. Filter with `--filter '*Recheck*'` (and set `ONEHTML_BENCH_CORPUS`, which the entry point requires, to any directory of HTML files).

