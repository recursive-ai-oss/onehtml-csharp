# Fuzzing

`OneHtml.Fuzz` holds the targets (`tokenize`, `treebuilder`, `css`, `noop`); a target must never throw. Three ways to run one:

- `OneHtml.Fuzz <target> <file>` runs it once on a file (replay a crash).
- `OneHtml.Fuzz <target> --mutate <seconds> <seed-dir>` is a coverage-blind mutation loop for machines with no driver; it stops at the first exception and saves `crash-*.bin` in the working directory.
- `OneHtml.Fuzz <target>` is a libFuzzer target under SharpFuzz, driven by `libfuzzer-dotnet`. Use `run.sh` for this.

## Coverage-guided runs

The driver is the prebuilt `libfuzzer-dotnet` release (a fuzzing tool, pinned by tag and SHA-256 in `setup-worker.sh`); the instrumenter is the `SharpFuzz.CommandLine` dotnet tool. Setup is user-local under `~/onehtml/u7/` and needs no sudo:

```
bash fuzz/setup-worker.sh
```

`run.sh` publishes the harness, instruments `RecursiveAI.OneHtml.dll` only, copies the seed directory into `<out-dir>/corpus` (the seeds are never modified) and runs one target for the given time:

```
bash fuzz/run.sh <target> <seconds> <seed-dir> <out-dir>
bash fuzz/run.sh treebuilder 1800 ~/onehtml/u7/seeds ~/onehtml/u7/fuzz/treebuilder
```

Crashes, timeouts and out-of-memory inputs go to `<out-dir>/crashes/`; `fuzz.log` is libFuzzer's log and `result.txt` has the executions, executions per second, coverage features and corpus size, and the crash count. Reuse the same `<out-dir>` to continue a campaign (the corpus carries over); `FUZZ_SEED`, `FUZZ_MAX_LEN` and `SKIP_BUILD=1` are optional. Calls are capped at 7200 s. One process only; calls over 300 s take `~/WORKER-BENCH.lock` (exit status 3 at once if it is held; never waits) and run under it. Seeds: any files, for example test-suite inputs and a few corpus pages copied by hand into a seed directory on the worker.

Replay a crash: `dotnet <build>/OneHtml.Fuzz.dll <target> <out-dir>/crashes/crash-<hash>`.
