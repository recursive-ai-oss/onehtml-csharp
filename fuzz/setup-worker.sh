#!/bin/bash
# One-time, user-local setup for coverage-guided fuzzing on the worker. Installs under ~/onehtml/u7 (override with
# ONEHTML_U7_HOME): the SharpFuzz instrumenter as a dotnet tool, and the prebuilt libFuzzer driver for .NET
# (libfuzzer-dotnet, a fuzzing tool) pinned to one release and checked by SHA-256. No sudo, nothing system-wide.

set -euo pipefail
U7=${ONEHTML_U7_HOME:-$HOME/onehtml/u7}
export PATH=$HOME/dotnet10:$PATH DOTNET_ROOT=$HOME/dotnet10 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export NUGET_PACKAGES=$U7/nuget
mkdir -p "$U7/tools"

DRIVER_TAG=v2025.05.02.0904
DRIVER_SHA=c2c2a90d94c409a4af339a0d4f244e0442c5a5d249be0f1252ba07871f285958
if [ ! -x "$U7/tools/libfuzzer-dotnet" ]; then
    curl -fsSL -o "$U7/tools/libfuzzer-dotnet.part" \
        "https://github.com/Metalnem/libfuzzer-dotnet/releases/download/$DRIVER_TAG/libfuzzer-dotnet-ubuntu"
    echo "$DRIVER_SHA  $U7/tools/libfuzzer-dotnet.part" | sha256sum -c -
    chmod +x "$U7/tools/libfuzzer-dotnet.part"
    mv "$U7/tools/libfuzzer-dotnet.part" "$U7/tools/libfuzzer-dotnet"
fi

if [ ! -x "$U7/tools/sharpfuzz" ]; then
    dotnet tool install SharpFuzz.CommandLine --tool-path "$U7/tools"
fi

echo "ready: $U7/tools"
