#!/bin/bash
# Runs a corpus command of OneHtml.Differential on the shared worker. Usage:
#   tools/OneHtml.Differential/run-worker.sh <roundtrip|angle> <corpus-version-dir> <out-dir> [options...]
# Options are passed through (--jobs, --limit, --timeout, --top, --max-reduce). The tool defaults to --jobs 2.
# Takes ~/WORKER-BENCH.lock and exits with status 3 at once when it is held; the run is cut off after
# ONEHTML_RUN_MAX_SECONDS (default 7200). Writes the command's summary files and load.log to the out dir.

set -o pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
CMD=${1:?usage: run-worker.sh <roundtrip|angle> <corpus-version-dir> <out-dir> [options]}
CORPUS=${2:?corpus dir}
OUT=${3:?out dir}
shift 3
MAX=${ONEHTML_RUN_MAX_SECONDS:-7200}

. "$ROOT/tools/worker-lock.sh" "differential-$CMD" "$MAX"

export PATH=$HOME/dotnet10:$PATH DOTNET_ROOT=$HOME/dotnet10 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export MSBUILDDISABLENODEREUSE=1 ONEHTML_ARTIFACTS=$HOME/onehtml/u7/artifacts NUGET_PACKAGES=$HOME/onehtml/u7/nuget
mkdir -p "$OUT"

sample_load() { echo "$(date +%T) $(cut -d' ' -f1-3 /proc/loadavg)" >> "$OUT/load.log"; }
{
    echo "date: $(date -u +%FT%TZ)"
    echo "cpu: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2- | sed 's/^ //')"
    echo "cores: $(nproc)"
    echo "sdk: $(dotnet --version)"
    echo "corpus: $CORPUS"
    grep -E 'PackageVersion Include="AngleSharp"' "$ROOT/Directory.Packages.props" | sed 's/^ *//'
} > "$OUT/environment.txt"
echo "before:" >> "$OUT/load.log"; sample_load
( while :; do sleep 30; sample_load; done ) &
BG_PIDS="$BG_PIDS $!"

dotnet build "$ROOT/tools/OneHtml.Differential" -c Release -v quiet -clp:NoSummary || exit 1
BIN=$(ls -d "$ONEHTML_ARTIFACTS"/bin/OneHtml.Differential/release*)

timeout "$MAX" dotnet "$BIN/OneHtml.Differential.dll" "$CMD" "$CORPUS" --out "$OUT" "$@" 2>&1 | tee "$OUT/run.log"
STATUS=$?
echo "after:" >> "$OUT/load.log"; sample_load
exit $STATUS
