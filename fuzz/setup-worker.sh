#!/bin/bash
# One-time, user-local setup for coverage-guided fuzzing on the worker. Installs under ~/onehtml/worker (override with
# ONEHTML_WORKER_HOME): the SharpFuzz instrumenter as a dotnet tool, and the prebuilt libFuzzer driver for .NET
# (libfuzzer-dotnet, a fuzzing tool) pinned to one release and checked by SHA-256. No sudo, nothing system-wide.

set -euo pipefail
WH=${ONEHTML_WORKER_HOME:-${ONEHTML_U7_HOME:-$HOME/onehtml/worker}}
export PATH=$HOME/dotnet10:$PATH DOTNET_ROOT=$HOME/dotnet10 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export NUGET_PACKAGES=$WH/nuget
mkdir -p "$WH/tools"

DRIVER_TAG=v2025.05.02.0904
DRIVER_SHA=c2c2a90d94c409a4af339a0d4f244e0442c5a5d249be0f1252ba07871f285958
if [ ! -x "$WH/tools/libfuzzer-dotnet" ]; then
    curl -fsSL -o "$WH/tools/libfuzzer-dotnet.part" \
        "https://github.com/Metalnem/libfuzzer-dotnet/releases/download/$DRIVER_TAG/libfuzzer-dotnet-ubuntu"
    echo "$DRIVER_SHA  $WH/tools/libfuzzer-dotnet.part" | sha256sum -c -
    chmod +x "$WH/tools/libfuzzer-dotnet.part"
    mv "$WH/tools/libfuzzer-dotnet.part" "$WH/tools/libfuzzer-dotnet"
fi

if [ ! -x "$WH/tools/sharpfuzz" ]; then
    dotnet tool install SharpFuzz.CommandLine --tool-path "$WH/tools"
fi

echo "ready: $WH/tools"
