#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
dotnet_bin="${MOVESETFORGE_DOTNET:-dotnet}"
"$dotnet_bin" build tests/MovesetForge.Tests -c Release -m:1 /p:UseSharedCompilation=false
"$dotnet_bin" tests/MovesetForge.Tests/bin/Release/net8.0/MovesetForge.Tests.dll "$@"
