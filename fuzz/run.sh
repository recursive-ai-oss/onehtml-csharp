#!/bin/bash
# Runs one fuzz target under SharpFuzz with the libFuzzer driver. Usage:
#   fuzz/run.sh <target> <seconds> <seed-dir> <out-dir>
# Targets are the names OneHtml.Fuzz lists (tokenize, treebuilder, css). Run fuzz/setup-worker.sh once first.
# The seed directory is only read; libFuzzer works on a copy in <out-dir>/corpus. Crashes, timeouts and out-of-memory
# inputs land in <out-dir>/crashes; the log and a few headline numbers (executions, coverage features, crash count)
# in <out-dir>/fuzz.log and <out-dir>/result.txt.
# One process. Runs over 300 s take ~/WORKER-BENCH.lock and exit with status 3 at once when it is held; the
# duration is capped at 7200 s per call (split longer campaigns, reusing <out-dir>/corpus).
# Environment: FUZZ_MAX_LEN (default 65536), FUZZ_SEED (default 1), SKIP_BUILD=1 to reuse the last build.

set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
TARGET=${1:?usage: run.sh <target> <seconds> <seed-dir> <out-dir>}
SECONDS_TO_RUN=${2:?seconds}
SEEDS=${3:?seed dir}
OUT=${4:?out dir}
U7=${ONEHTML_U7_HOME:-$HOME/onehtml/u7}
[ "$SECONDS_TO_RUN" -le 7200 ] || { echo "at most 7200 s per call"; exit 2; }

if [ "$SECONDS_TO_RUN" -gt 300 ]; then
    . "$ROOT/tools/worker-lock.sh" "fuzz-$TARGET" $((SECONDS_TO_RUN + 600))
fi

export PATH=$HOME/dotnet10:$PATH DOTNET_ROOT=$HOME/dotnet10 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export MSBUILDDISABLENODEREUSE=1 NUGET_PACKAGES=$U7/nuget ONEHTML_ARTIFACTS=$U7/artifacts

BUILD=$U7/build/fuzz
if [ "${SKIP_BUILD:-0}" != 1 ]; then
    rm -rf "$BUILD"
    dotnet publish "$ROOT/fuzz/OneHtml.Fuzz" -c Release -o "$BUILD" -v quiet -clp:NoSummary
    # Instrument the library only; the harness and its dependencies stay plain.
    "$U7/tools/sharpfuzz" "$BUILD/RecursiveAI.OneHtml.dll"
    "$U7/tools/sharpfuzz" "$BUILD/RecursiveAI.OneHtml.Accessibility.dll"
fi

mkdir -p "$OUT/corpus" "$OUT/crashes"
cp -n "$SEEDS"/* "$OUT/corpus/" 2>/dev/null || true

# libfuzzer-dotnet starts one process per run of the target; this wrapper names the target for it.
cat > "$OUT/target.sh" <<EOF
#!/bin/sh
exec "$DOTNET_ROOT/dotnet" "$BUILD/OneHtml.Fuzz.dll" "$TARGET"
EOF
chmod +x "$OUT/target.sh"

set +e
"$U7/tools/libfuzzer-dotnet" --target_path="$OUT/target.sh" \
    -max_total_time="$SECONDS_TO_RUN" -max_len="${FUZZ_MAX_LEN:-65536}" -seed="${FUZZ_SEED:-1}" \
    -timeout=30 -rss_limit_mb=2048 -artifact_prefix="$OUT/crashes/" -print_final_stats=1 \
    "$OUT/corpus" > "$OUT/fuzz.log" 2>&1
STATUS=$?
set -e

{
    echo "target: $TARGET, $SECONDS_TO_RUN s, seed ${FUZZ_SEED:-1}, exit status $STATUS"
    grep -E '^stat::number_of_executed_units' "$OUT/fuzz.log" | tail -1 | sed 's/^stat:://'
    grep -E '^stat::average_exec_per_sec' "$OUT/fuzz.log" | tail -1 | sed 's/^stat:://'
    grep -oE 'cov: [0-9]+ ft: [0-9]+ corp: [0-9]+/[0-9]+[a-zA-Z]*' "$OUT/fuzz.log" | tail -1
    echo "crash artifacts: $(find "$OUT/crashes" -type f | wc -l)"
} 2>/dev/null | tee "$OUT/result.txt"
exit $STATUS
