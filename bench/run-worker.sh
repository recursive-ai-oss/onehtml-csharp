#!/bin/bash
# Runs the parse benchmark on the shared worker. Usage:
#   bench/run-worker.sh <corpus-version-dir> [out-dir] [extra BenchmarkDotNet arguments...]
# Environment: ONEHTML_BENCH_SAMPLE_SIZE (default 100), ONEHTML_BENCH_BAND_CAP (default none),
#   ONEHTML_BENCH_MAX_SECONDS (default 7200, the cut-off for the whole benchmark).
# Takes ~/WORKER-BENCH.lock and exits with status 3 at once when it is held. Writes environment.txt, load.log and the
# BenchmarkDotNet results to the out dir (default ~/onehtml/u7/bench-out/<timestamp>).

set -o pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
CORPUS=${1:?usage: run-worker.sh <corpus-version-dir> [out-dir] [BenchmarkDotNet args]}
OUT=${2:-$HOME/onehtml/u7/bench-out/$(date +%Y%m%d-%H%M%S)}
[ $# -ge 2 ] && shift 2 || shift
MAX=${ONEHTML_BENCH_MAX_SECONDS:-7200}

. "$ROOT/tools/worker-lock.sh" parse-bench "$MAX"

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

echo "before:" >> "$OUT/load.log"; sample_load
( while :; do sleep 30; sample_load; done ) &
BG_PIDS="$BG_PIDS $!"

dotnet build "$ROOT/bench/OneHtml.Benchmarks" -c Release -v quiet -clp:NoSummary || exit 1
BIN=$(ls -d "$ONEHTML_ARTIFACTS"/bin/OneHtml.Benchmarks/release*)

# BenchmarkDotNet runs in process: its out-of-process builder expects the project next to the binaries.
timeout "$MAX" dotnet "$BIN/OneHtml.Benchmarks.dll" --inProcess --filter '*' --artifacts "$OUT/bdn" "$@" 2>&1 | tee "$OUT/bench.log"
STATUS=$?

echo "after:" >> "$OUT/load.log"; sample_load
echo "results in $OUT"
exit $STATUS
