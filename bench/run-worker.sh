#!/bin/bash
# Runs the parse benchmark on the shared worker. Usage:
#   bench/run-worker.sh <corpus-version-dir> [out-dir] [extra BenchmarkDotNet arguments...]
# Environment: ONEHTML_BENCH_SAMPLE_SIZE (default 100), ONEHTML_BENCH_BAND_CAP (default none), ONEHTML_BENCH_FILTER
#   (default "*ParseBenchmarks* *EditBenchmarks*"),
#   ONEHTML_BENCH_MAX_SECONDS (default 7200, the cut-off for the whole benchmark).
#
#   bench/run-worker.sh checker <corpus-version-dir> [out-dir]
# runs the checker against axe-core in Chrome instead: the BenchmarkDotNet rows (full check, re-check after one edit,
# focus overlays) with allocations, the per-page timings of the same three operations, the same pages timed under axe-core
# in the pinned Chrome (bench/axe-bench), and the comparison table. Same environment variables, plus
# ONEHTML_BENCH_REPEATS (default 3), ONEHTML_BENCH_FOCUS_CAP (default 20) and ONEHTML_BENCH_AXE_LIMIT (pages given to
# Chrome, default all of the sample); set ONEHTML_BENCH_SKIP_BDN=1 to skip the BenchmarkDotNet rows.
#
# Takes ~/WORKER-BENCH.lock and exits with status 3 at once when it is held. Writes environment.txt, load.log and the
# BenchmarkDotNet results to the out dir (default ~/onehtml/u7/bench-out/<timestamp>).

set -o pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
MODE=parse
if [ "${1:-}" = checker ]; then MODE=checker; shift; fi
CORPUS=${1:?usage: run-worker.sh <corpus-version-dir> [out-dir] [BenchmarkDotNet args]}
OUT=${2:-$HOME/onehtml/u7/bench-out/$(date +%Y%m%d-%H%M%S)}
[ $# -ge 2 ] && shift 2 || shift
MAX=${ONEHTML_BENCH_MAX_SECONDS:-7200}

. "$ROOT/tools/worker-lock.sh" "${MODE}-bench" "$MAX"

export PATH=$HOME/dotnet10:$PATH DOTNET_ROOT=$HOME/dotnet10 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export MSBUILDDISABLENODEREUSE=1 ONEHTML_ARTIFACTS=$HOME/onehtml/u7/artifacts
export NUGET_PACKAGES=$HOME/onehtml/u7/nuget ONEHTML_BENCH_CORPUS=$CORPUS
mkdir -p "$OUT"

sample_load() { echo "$(date +%T) $(cut -d' ' -f1-3 /proc/loadavg) running=$(cut -d' ' -f4 /proc/loadavg)" >> "$OUT/load.log"; }

{
    echo "date: $(date -u +%FT%TZ)"
    echo "cpu: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2- | sed 's/^ //')"
    echo "cores: $(nproc)"
    echo "sdk: $(dotnet --version)"
    echo "corpus: $CORPUS"
    echo "sample size: ${ONEHTML_BENCH_SAMPLE_SIZE:-100}, band cap: ${ONEHTML_BENCH_BAND_CAP:-none}"
    grep -E 'PackageVersion Include="(BenchmarkDotNet|AngleSharp|HtmlAgilityPack)"' "$ROOT/Directory.Packages.props" | sed 's/^ *//'
} > "$OUT/environment.txt"

if [ "$MODE" = checker ]; then
    export ONEHTML_BENCH_REPEATS=${ONEHTML_BENCH_REPEATS:-3}
    PY=$("$HOME/.local/bin/uv" python find 3.13) || exit 1
    {
        echo "python: $("$PY" --version)"
        echo "chrome and axe-core pins: $(grep -E '"version"' "$ROOT/tools/pins/pins.json" | tr -d ' \n')"
        echo "repeats: $ONEHTML_BENCH_REPEATS, focus cap: ${ONEHTML_BENCH_FOCUS_CAP:-20}"
    } >> "$OUT/environment.txt"
    echo "before:" >> "$OUT/load.log"; sample_load
    ( while :; do sleep 30; sample_load; done ) &
    BG_PIDS="$BG_PIDS $!"
    dotnet build "$ROOT/bench/OneHtml.Benchmarks" -c Release -v quiet -clp:NoSummary || exit 1
    BIN=$(ls -d "$ONEHTML_ARTIFACTS"/bin/OneHtml.Benchmarks/release*)
    dotnet "$BIN/OneHtml.Benchmarks.dll" --check-per-page "$OUT/ours.jsonl" 2>&1 | tee "$OUT/ours.log" || exit 1
    if [ -z "$ONEHTML_BENCH_SKIP_BDN" ]; then
        timeout "$MAX" dotnet "$BIN/OneHtml.Benchmarks.dll" --inProcess --filter '*CheckBenchmarks*' --artifacts "$OUT/bdn" 2>&1 | tee "$OUT/bench.log"
    fi
    LIMIT=${ONEHTML_BENCH_AXE_LIMIT:-0}
    "$PY" "$ROOT/bench/axe-bench/axe_bench.py" run --corpus "$CORPUS" --ours "$OUT/ours.jsonl" --out "$OUT/axe.jsonl" \
        --repeats "$ONEHTML_BENCH_REPEATS" --focus-cap "${ONEHTML_BENCH_FOCUS_CAP:-20}" --limit "$LIMIT" 2>&1 | tee "$OUT/axe.log"
    STATUS=${PIPESTATUS[0]}
    "$PY" "$ROOT/bench/axe-bench/axe_bench.py" compare "$OUT/ours.jsonl" "$OUT/axe.jsonl" | tee "$OUT/compare.txt"
    echo "after:" >> "$OUT/load.log"; sample_load
    echo "results in $OUT"
    exit $STATUS
fi

echo "before:" >> "$OUT/load.log"; sample_load
( while :; do sleep 30; sample_load; done ) &
BG_PIDS="$BG_PIDS $!"

dotnet build "$ROOT/bench/OneHtml.Benchmarks" -c Release -v quiet -clp:NoSummary || exit 1
BIN=$(ls -d "$ONEHTML_ARTIFACTS"/bin/OneHtml.Benchmarks/release*)

# BenchmarkDotNet runs in process: its out-of-process builder expects the project next to the binaries.
# The checker rows take seconds per invocation, which the in-process toolchain refuses, so they have their own mode.
# ONEHTML_BENCH_FILTER (space-separated BenchmarkDotNet filters) overrides the default parse and edit rows.
read -r -a FILTERS <<< "${ONEHTML_BENCH_FILTER:-*ParseBenchmarks* *EditBenchmarks*}"
timeout "$MAX" dotnet "$BIN/OneHtml.Benchmarks.dll" --inProcess --filter "${FILTERS[@]}" --artifacts "$OUT/bdn" "$@" 2>&1 | tee "$OUT/bench.log"
STATUS=$?

echo "after:" >> "$OUT/load.log"; sample_load
echo "results in $OUT"
exit $STATUS
