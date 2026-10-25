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
bash bench/run-worker.sh ~/onehtml/corpus/<version> ~/onehtml/u7/bench-out/<name>
```

Optional: `ONEHTML_BENCH_SAMPLE_SIZE`, `ONEHTML_BENCH_BAND_CAP`, `ONEHTML_BENCH_MAX_SECONDS` (default 7200), and BenchmarkDotNet arguments after the out dir (for example `--job short`). The script claims `~/WORKER-BENCH.lock` atomically and exits with status 3 at once if it is held, builds in Release, records CPU model, cores, SDK, package versions and the load average before, during (every 30 s) and after, and runs the benchmark under `timeout`. Outputs in the out dir: `environment.txt`, `load.log`, `bench.log`, `bdn/results/*`. A run that overlapped another heavy job is not publishable; check `load.log`.
