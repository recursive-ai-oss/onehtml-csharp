#!/bin/bash
# Source this from a worker job script: `. "$(dirname "$0")/../tools/worker-lock.sh" <label> <max-seconds>`.
#
# Claims ~/WORKER-BENCH.lock atomically and exits with status 3 straight away if somebody else holds it; it never
# waits. The lock is released when the sourcing script exits. Background process ids the script wants killed at exit
# can be appended to BG_PIDS.
#
# Anything that runs longer than a few minutes or uses many cores on the shared worker takes this lock, runs under
# `timeout`, and is cut into chunks of at most two hours.

WORKER_LOCK="$HOME/WORKER-BENCH.lock"
BG_PIDS=""

release_lock() {
    for pid in $BG_PIDS; do kill "$pid" 2>/dev/null; done
    rm -f "$WORKER_LOCK"
}

_start=$(date +%FT%T%z)
_end=$(date -d "+${2:-7200} seconds" +%FT%T%z)
set -o noclobber
echo "onehtml ${1:-job} $_start $_end" > "$WORKER_LOCK" || { echo "LOCK HELD: $(cat "$WORKER_LOCK" 2>/dev/null)"; exit 3; }
set +o noclobber
trap release_lock EXIT
trap 'exit 130' INT TERM
