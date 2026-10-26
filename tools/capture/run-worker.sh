#!/bin/bash
# Runs the reference capture on the shared worker. Usage:
#   tools/capture/run-worker.sh <corpus-version-dir> <out-dir> [capture.py run options...]
# Options are passed through (--jobs, --limit, --viewport, --max-seconds, --retry-failed ...).
# Takes ~/WORKER-BENCH.lock and exits with status 3 at once when it is held. The run is cut off after
# ONEHTML_RUN_MAX_SECONDS (default 7200); capture.py is also told to stop starting pages a minute before that,
# so a chunk ends with its REMAINING= line. Writes load.log and environment.txt to the out dir.

set -o pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
CORPUS=${1:?usage: run-worker.sh <corpus-version-dir> <out-dir> [options]}
OUT=${2:?out dir}
shift 2
MAX=${ONEHTML_RUN_MAX_SECONDS:-7200}

. "$ROOT/tools/worker-lock.sh" "capture" "$MAX"

PY=$("$HOME/.local/bin/uv" python find 3.13) || exit 1
mkdir -p "$OUT"
sample_load() { echo "$(date +%T) $(cut -d' ' -f1-3 /proc/loadavg)" >> "$OUT/load.log"; }
{
    echo "date: $(date -u +%FT%TZ)"
    echo "cpu: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2- | sed 's/^ //')"
    echo "cores: $(nproc)"
    echo "python: $("$PY" --version)"
    echo "corpus: $CORPUS"
} >> "$OUT/environment.txt"
echo "before:" >> "$OUT/load.log"; sample_load
( while :; do sleep 30; sample_load; done ) &
BG_PIDS="$BG_PIDS $!"

timeout "$MAX" "$PY" "$ROOT/tools/capture/capture.py" run --corpus "$CORPUS" --out "$OUT" \
    --max-seconds $((MAX - 60)) "$@" 2>&1 | tee -a "$OUT/run.log"
STATUS=$?
echo "after:" >> "$OUT/load.log"; sample_load
exit $STATUS
